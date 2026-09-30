// AccUploadModelCommand.cs — the REAL ACC upload, as a dispatchable command.
//
// Why this exists: ACCPublish builds a LOCAL ACC-ready ZIP and tells the operator to
// upload it by hand. That is honest, but it left the actual uploader — AccModelUpload,
// which does the full APS Data Management dance (storage object -> signed-S3 PUT ->
// item/version) — reachable from exactly one button inside the BIM Coordination Center
// and from no command tag at all.
//
// Two tags, one implementation:
//
//   ACC_UploadModel       picks a file. The manual case, unchanged.
//   ACC_UploadLastBundle  uploads the bundle ACCPublish last produced, with no picker.
//
// The second is only possible because ACCPublish now writes _BIM_COORD/acc/last_bundle.json
// naming the ZIP it built. That is a file choice, not a guess: PR #927 refused to wire an
// automatic upload precisely because "which file?" had no answer, and inventing one would
// put an unintended file into an issued CDE container.
//
// NEITHER TAG IS IN ANY KUT WORKFLOW, and that is deliberate. Wiring the capability is
// offline work; deciding that a fortnightly cycle should push into an issued container is
// the Information Manager's call, and it should not be made before one live round-trip has
// been proved (docs/KUT_LIVE_VERIFICATION_RUNBOOK.md, B1).
//
// Read-only with respect to the Revit model (no Transaction). Network I/O only.
// Credentials come from %APPDATA%\Planscape\acc_credentials.json (AccIssueSync).

using System;
using System.IO;
using System.Linq;
using StingTools.Core.Drawing;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    /// <summary>Shared shell for both upload tags. The only difference is where the file
    /// comes from, so the credential check, the failure reporting and the logging are
    /// written once and cannot drift between the two.</summary>
    public abstract class AccUploadCommandBase : IExternalCommand
    {
        /// <summary>The file to upload, or null to cancel. Implementations report their own
        /// reason for cancelling.</summary>
        protected abstract string ResolveFile(Document doc, out string cancelReason);

        protected abstract string DialogTitle { get; }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            Document doc = ctx?.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC upload");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC upload");   // IM-18: project container ids first
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                TaskDialog.Show(DialogTitle,
                    "ACC is not set up for this project on this machine.\n\n" +
                    "BIM Coordination Center > ACC: enter the APS Client ID, 'Sign in with Autodesk', " +
                    "then 'Discover' to choose the ACC project. The APS app needs the data:read, " +
                    "data:write and data:create scopes.\n\n" + AccProjectScope.Describe(creds) + ".");
                return Result.Cancelled;
            }

            string file = ResolveFile(doc, out string cancelReason);
            if (string.IsNullOrEmpty(file))
            {
                if (!string.IsNullOrEmpty(cancelReason)) TaskDialog.Show(DialogTitle, cancelReason);
                return Result.Cancelled;
            }

            AccModelUpload.UploadResult result;
            AccUploadOptions options;
            try
            {
                options = BuildOptions(doc, file, policy, out string optionsRefusal);
                if (options == null)
                {
                    if (!string.IsNullOrEmpty(optionsRefusal)) TaskDialog.Show(DialogTitle, optionsRefusal);
                    return Result.Cancelled;
                }
                result = AccModelUpload.UploadAsync(creds, file, options).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StingLog.Error("ACC upload", ex);
                TaskDialog.Show(DialogTitle, "Upload failed: " + ex.Message);
                return Result.Failed;
            }

            if (result == null || !result.Ok)
            {
                // A failed upload must not read as a completed one — the operator would
                // believe the deliverable reached the CDE container. The failure KIND comes
                // from the same AccFetchStatus vocabulary every other ACC path reports, so
                // an auth problem reads as an auth problem rather than as "it didn't work".
                var status = result?.Status ?? AccFetchStatus.TransportFailed;
                string why = result?.Message ?? "the upload returned no result";
                TaskDialog.Show(DialogTitle,
                    $"The file was NOT uploaded to ACC.\n\n" +
                    $"File:    {Path.GetFileName(file)}\n" +
                    $"Failure: {status}\n" +
                    $"Reason:  {why}\n" +
                    $"Project (container) used: {creds.ProjectId}\n" +
                    $"Folder: {(policy.CdeFolders.Count > 0 ? "by CDE state (project cdeFolders)" : string.IsNullOrWhiteSpace(creds.FolderUrn) ? "the project's 'Project Files' folder" : creds.FolderUrn)}\n\n" +
                    AccCommandOutcome.Remedy(status));
                StingLog.Warn($"ACC upload FAILED ({status}, HTTP {result?.HttpStatus ?? 0}) for '{file}': {why}");
                return Result.Failed;
            }

            var cover = UploadTransmittalCover(doc, creds, file, options);
            string txNote = MarkBundleTransmittalSent(doc, file, result, cover.versionUrn);
            if (!string.IsNullOrEmpty(cover.note)) txNote = (txNote == null ? "" : txNote + "\n") + cover.note;
            TaskDialog.Show(DialogTitle,
                result.Message +
                (string.IsNullOrWhiteSpace(result.FolderReason) ? "" : "\n\nFolder: " + result.FolderReason) +
                (string.IsNullOrWhiteSpace(result.ItemUrn) ? "" : "\nItem: " + result.ItemUrn) +
                (string.IsNullOrWhiteSpace(result.MetadataNote) ? "" : "\n\n" + result.MetadataNote) +
                (txNote == null ? "" : "\n\n" + txNote));
            if (!result.MetadataComplete) StingLog.Warn("ACC upload: " + result.MetadataNote);
            StingLog.Info($"ACC upload: uploaded '{file}' -> {result.ItemUrn}");
            return Result.Succeeded;
        }

        /// <summary>
        /// The upload's folder and metadata decisions. The suitability comes from the ACC
        /// Publish bundle record when this is that bundle; otherwise, when the project maps
        /// CDE states to folders, the person is asked which state the file is in — the
        /// suitability decides where a deliverable may go, so it is never inferred from a
        /// file name. Returns null (with a reason, or none if the person cancelled) to stop.
        /// </summary>
        private static AccUploadOptions BuildOptions(Document doc, string file, AccOperatingPolicy policy, out string refusal)
        {
            refusal = null;
            var rec = AccBundleRecord.ReadExisting(BundleRecordPath(doc));
            bool isBundle = rec != null && string.Equals(Path.GetFullPath(rec.Path), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
            string suitability = isBundle ? rec.Suitability : string.Empty;

            if (policy.CdeFolders.Count > 0 && string.IsNullOrWhiteSpace(suitability))
            {
                if (!policy.MayPrompt)
                {
                    refusal = "This project files uploads by CDE state, and this file carries no suitability code. " +
                              "Nothing was uploaded (unattended runs do not guess where a deliverable belongs).";
                    return null;
                }
                var choices = AccOperatingPolicy.CdeStates
                    .Where(st => policy.CdeFolders.ContainsKey(st))
                    .Select(st => $"{st} — suitability {Iso19650Suitability.DefaultFor(st)}")
                    .ToList();
                string pick = StingTools.Select.StingListPicker.Show("ACC — which CDE state is this file in?",
                    $"{Path.GetFileName(file)}\n\nThe project maps each ISO 19650 CDE state to an ACC folder. " +
                    "Choose the state this file is issued in.", choices);
                if (string.IsNullOrEmpty(pick)) return null;
                suitability = Iso19650Suitability.DefaultFor(pick.Split(' ')[0]);
            }

            var options = new AccUploadOptions
            {
                Suitability = suitability ?? string.Empty,
                CdeFolders = policy.CdeFolders,
                CreateMissingAttributes = policy.DocsAttributesCreateMissing,
            };
            if (policy.DocsAttributes)
            {
                string originator = string.Empty;
                try { originator = ParameterHelpers.GetString(doc?.ProjectInformation, ParamRegistry.ORG_ORIGINATOR_CODE); }
                catch (Exception ex) { StingLog.Warn("ACC upload: originator code: " + ex.Message); }
                options.Metadata = new AccDocMetadataInput
                {
                    DocumentNumber = Path.GetFileNameWithoutExtension(file),
                    Suitability = suitability,
                    Revision = string.Empty,
                    TransmittalId = isBundle ? rec.TransmittalId : string.Empty,
                    Originator = originator,
                };
            }
            return options;
        }

        /// <summary>IM-17: ACCPublish records its bundle's transmittal as PREPARED. When
        /// exactly that file has now reached ACC, the row becomes SENT with today's issue
        /// date. Any other file changes nothing. Returns a line for the dialog, or null.</summary>
        /// <summary>
        /// Autodesk's ACC Transmittals API is READ-ONLY, so STING cannot create an ACC
        /// transmittal. The nearest auditable equivalent: the bundle's transmittal cover sheet
        /// (which otherwise sits unseen inside the ZIP) is uploaded beside it as its own
        /// document, named by the STING transmittal id and carrying the same ISO 19650
        /// attributes; the ACC version ids of both are recorded on the STING transmittal.
        /// Only for the ACC Publish bundle that carries a transmittal id. A cover that fails
        /// to upload does not undo the bundle upload - it is reported.
        /// </summary>
        private static (string versionUrn, string note) UploadTransmittalCover(Document doc, AccCredentials creds, string file, AccUploadOptions options)
        {
            try
            {
                var rec = AccBundleRecord.Read(BundleRecordPath(doc));
                if (rec == null || string.IsNullOrWhiteSpace(rec.TransmittalId)) return (null, null);
                if (!string.Equals(Path.GetFullPath(rec.Path), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                    return (null, null);

                string packageDir = Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file));
                string source = Path.Combine(packageDir, "TRANSMITTAL_COVER.txt");
                if (!File.Exists(source))
                    return (null, "The transmittal cover sheet was not found beside the bundle, so it was not uploaded separately.");

                string staged = Path.Combine(StingPaths.Staging(doc, "acc"), SafeName(rec.TransmittalId) + "_TRANSMITTAL.txt");
                File.Copy(source, staged, true);

                var coverOptions = new AccUploadOptions
                {
                    Suitability = options?.Suitability ?? rec.Suitability,
                    CdeFolders = options?.CdeFolders,
                    CreateMissingAttributes = options?.CreateMissingAttributes ?? false,
                };
                if (options?.Metadata != null)
                    coverOptions.Metadata = new AccDocMetadataInput
                    {
                        DocumentNumber = Path.GetFileNameWithoutExtension(staged),
                        Suitability = options.Metadata.Suitability,
                        Revision = options.Metadata.Revision,
                        TransmittalId = rec.TransmittalId,
                        Originator = options.Metadata.Originator,
                    };
                var r = AccModelUpload.UploadAsync(creds, staged, coverOptions).GetAwaiter().GetResult();
                if (!r.Ok)
                {
                    StingLog.Warn("ACC upload: transmittal cover not uploaded: " + r.Message);
                    return (null, "The bundle is in ACC, but its transmittal cover sheet could not be uploaded separately: " + r.Message);
                }
                return (r.VersionUrn, $"Transmittal cover uploaded to ACC as {Path.GetFileName(staged)} " +
                                      "(Autodesk's API cannot create ACC transmittals; this document is the record there).");
            }
            catch (Exception ex)
            {
                StingLog.Warn("ACC upload: transmittal cover: " + ex.Message);
                return (null, "The transmittal cover sheet could not be uploaded separately: " + ex.Message);
            }
        }

        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static string MarkBundleTransmittalSent(Document doc, string file, AccModelUpload.UploadResult result, string coverVersionUrn)
        {
            string itemUrn = result?.ItemUrn;
            try
            {
                var rec = AccBundleRecord.Read(BundleRecordPath(doc));
                if (rec == null || string.IsNullOrWhiteSpace(rec.TransmittalId)) return null;
                if (!string.Equals(Path.GetFullPath(rec.Path), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                    return null;

                string txPath = BIMManager.BIMManagerEngine.GetBIMManagerFilePath(doc, "transmittals.json");
                var rows = BIMManager.BIMManagerEngine.LoadJsonArray(txPath);
                var row = BIMManager.TransmittalRecord.MarkSent(rows, rec.TransmittalId, DateTime.Now,
                    Environment.UserName, "uploaded to ACC" + (string.IsNullOrWhiteSpace(itemUrn) ? "" : " as " + itemUrn));
                if (row == null) return null;
                // Traceability to ACC: the uploaded bundle and its cover sheet, by version.
                if (!string.IsNullOrWhiteSpace(result?.ItemUrn)) row["acc_item_urn"] = result.ItemUrn;
                if (!string.IsNullOrWhiteSpace(result?.VersionUrn)) row["acc_version_urn"] = result.VersionUrn;
                if (!string.IsNullOrWhiteSpace(result?.FolderUrn)) row["acc_folder_urn"] = result.FolderUrn;
                if (!string.IsNullOrWhiteSpace(coverVersionUrn)) row["acc_cover_version_urn"] = coverVersionUrn;
                BIMManager.BIMManagerEngine.SaveJsonFile(txPath, rows);
                StingLog.Info($"ACC upload: transmittal {rec.TransmittalId} marked SENT");
                return $"Transmittal {rec.TransmittalId} is now recorded as SENT.";
            }
            catch (Exception ex)
            {
                StingLog.Warn("ACC upload: could not mark the bundle's transmittal SENT: " + ex.Message);
                return "The upload succeeded, but its transmittal could not be marked SENT — see the log.";
            }
        }

        /// <summary>Where ACCPublish records the bundle it built. One place, so the writer
        /// and the reader cannot disagree.</summary>
        internal static string BundleRecordPath(Document doc)
        {
            try
            {
                string dir = StingPaths.MetaFile(doc, "_BIM_COORD", "acc");
                return Path.Combine(dir, AccBundleRecord.FileName);
            }
            catch (Exception ex) { StingLog.Warn("ACC bundle record path: " + ex.Message); return null; }
        }
    }

    /// <summary>ACC_UploadModel — pick any file and upload it. The manual case.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccUploadModelCommand : AccUploadCommandBase
    {
        protected override string DialogTitle => "ACC — Upload Model";

        protected override string ResolveFile(Document doc, out string cancelReason)
        {
            cancelReason = null;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick a model / deliverable to upload to ACC",
                Filter = "Model & doc files (*.zip;*.glb;*.ifc;*.nwc;*.nwd;*.rvt;*.pdf;*.dwg)|*.zip;*.glb;*.ifc;*.nwc;*.nwd;*.rvt;*.pdf;*.dwg|All files (*.*)|*.*",
            };
            // If ACCPublish has built a bundle, start the picker in its folder — a nudge,
            // not a default, so nothing is uploaded that the operator did not choose.
            var rec = AccBundleRecord.ReadExisting(BundleRecordPath(doc));
            if (rec != null)
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(rec.Path); } catch { }
                try { dlg.FileName = Path.GetFileName(rec.Path); } catch { }
            }
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }

    /// <summary>ACC_UploadLastBundle — upload the bundle ACCPublish last produced, with no
    /// file picker. Non-interactive, so it can run from a project-authored workflow.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccUploadLastBundleCommand : AccUploadCommandBase
    {
        protected override string DialogTitle => "ACC — Upload Last Bundle";

        protected override string ResolveFile(Document doc, out string cancelReason)
        {
            string recordPath = BundleRecordPath(doc);
            // ReadExisting, not Read: a record naming a ZIP that has since been deleted or
            // cleaned out is not a file to upload. Reporting "no bundle" is right; uploading
            // whatever now sits at that path would be exactly the guess this avoids.
            var rec = AccBundleRecord.ReadExisting(recordPath);
            if (rec == null)
            {
                var stale = AccBundleRecord.Read(recordPath);
                cancelReason = stale == null
                    ? "No ACC bundle has been built for this project yet.\n\n" +
                      "Run ACC Publish first — it packages the deliverables into a local " +
                      "ACC-ready ZIP and records which one it built. This command then uploads " +
                      "that bundle without asking you to pick a file."
                    : "The recorded ACC bundle is no longer on disk:\n\n" +
                      $"  {stale.Path}\n\n" +
                      "Nothing was uploaded. Re-run ACC Publish to build a fresh bundle — " +
                      "uploading whatever now sits at that path would be a guess.";
                return null;
            }

            cancelReason = null;
            var confirm = new TaskDialog(DialogTitle)
            {
                MainInstruction = "Upload the last ACC bundle?",
                MainContent = $"This uploads the bundle ACC Publish built:\n\n  {rec.Describe()}\n\n" +
                              $"Destination container: this project's ACC container (see the log line for where it came from)\n\n" +
                              "It goes into the real CDE. Nothing else is uploaded.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
                AllowCancellation = true,
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                cancelReason = null;
                return null;
            }
            return rec.Path;
        }
    }
}
