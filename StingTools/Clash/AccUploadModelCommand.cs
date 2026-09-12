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

            var creds = AccIssueSync.LoadCredentials();
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                TaskDialog.Show(DialogTitle,
                    "ACC credentials are not configured.\n\n" +
                    "Create %APPDATA%\\Planscape\\acc_credentials.json with at least:\n" +
                    "  ClientId, ClientSecret, RefreshToken, ProjectId\n\n" +
                    "The APS app also needs data:read, data:write and data:create scopes, " +
                    "or the upload is rejected after the file has already been staged.");
                return Result.Cancelled;
            }

            string file = ResolveFile(doc, out string cancelReason);
            if (string.IsNullOrEmpty(file))
            {
                if (!string.IsNullOrEmpty(cancelReason)) TaskDialog.Show(DialogTitle, cancelReason);
                return Result.Cancelled;
            }

            AccModelUpload.UploadResult result;
            try
            {
                result = AccModelUpload.UploadAsync(creds, file).GetAwaiter().GetResult();
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
                    $"Folder URN: {(string.IsNullOrWhiteSpace(creds.FolderUrn) ? "(auto-resolved 'Project Files')" : creds.FolderUrn)}\n\n" +
                    AccCommandOutcome.Remedy(status));
                StingLog.Warn($"ACC upload FAILED ({status}, HTTP {result?.HttpStatus ?? 0}) for '{file}': {why}");
                return Result.Failed;
            }

            TaskDialog.Show(DialogTitle,
                result.Message + (string.IsNullOrWhiteSpace(result.ItemUrn) ? "" : "\n\nItem: " + result.ItemUrn));
            StingLog.Info($"ACC upload: uploaded '{file}' -> {result.ItemUrn}");
            return Result.Succeeded;
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
                              $"Destination container: (from acc_credentials.json)\n\n" +
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
