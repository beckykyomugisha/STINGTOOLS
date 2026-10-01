// StingTools — take a superseded / replaced deliverable out of circulation in ACC.
//
// DeliverableLifecycle.Supersede / Replace used to touch deliverables.json only. This is
// the Revit-side half of the fix: it finds the deliverable's own ACC document, asks (or
// follows the project setting), and hands the retirement to the Revit-free
// AccDocsLifecycle client, recording what happened on the register row.
//
// Where the ACC document is found: the upload ledger (acc_upload_ledger.json) — every
// per-document upload (Export Centre auto-upload, ACC_UploadModel) records its item,
// version and folder there, one entry per rendition (PDF, DWG …). A register row carrying
// acc_version_urn (+ acc_folder_urn) is still honoured when present. Nothing wrote those
// register fields, so looking only there never found anything (R11). A transmittal BUNDLE
// is deliberately NOT retired: one ZIP holds many deliverables, and archiving it to retire
// one of them would retire the rest. That case is reported, not guessed at.
//
// Setting (acc_settings.json, optional): "retireSupersededInAcc": "ask" (default) |
// "always" | "never". Unattended runs never prompt: "ask" becomes "not retired, reported".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    internal static class AccRetireDeliverable
    {
        internal const string SettingKey = "retireSupersededInAcc";

        /// <summary>
        /// Retire <paramref name="docNumber"/>'s ACC document with <paramref name="retireSuitability"/>
        /// (AB for superseded / replaced). Returns a line for the caller's message, or null
        /// when the deliverable has no ACC document on record. Never throws.
        /// </summary>
        internal static string AfterRetire(Document doc, string docNumber, string retireSuitability)
        {
            try
            {
                if (doc == null || string.IsNullOrWhiteSpace(docNumber)) return null;

                string regPath = CoordStores.Register(doc);
                var register = BIMManager.BIMManagerEngine.LoadJsonArray(regPath);
                var row = register.OfType<JObject>().FirstOrDefault(r =>
                    string.Equals(r["doc_number"]?.ToString(), docNumber, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(r["acc_version_urn"]?.ToString()));

                // The upload ledger is where per-document uploads are recorded (R11). An
                // unreadable ledger is not "never uploaded": say so rather than report nothing.
                string ledgerPath = AccUploadCommandBase.LedgerPath(doc);
                var ledger = AccUploadLedger.Load(ledgerPath, out string ledgerErr);
                var renditions = new List<(string version, string folder, AccLedgerEntry entry)>();
                if (ledger != null)
                    foreach (var e in ledger.LiveRenditions(docNumber))
                        renditions.Add((e.VersionUrn, e.FolderUrn, e));
                if (renditions.Count == 0 && row != null)
                    renditions.Add((row["acc_version_urn"].ToString(), row["acc_folder_urn"]?.ToString(), null));

                if (renditions.Count == 0)
                {
                    if (ledger == null)
                        return $"ACC: {docNumber} NOT retired — the upload ledger could not be read ({ledgerErr}), " +
                               "so STING cannot tell which ACC document it is. Retire it in ACC by hand.";
                    StingLog.Info($"ACC retire: {docNumber} has no per-document ACC record — nothing to retire in ACC.");
                    return $"ACC: no per-document ACC record for {docNumber}; if it reached ACC inside a transmittal " +
                           "bundle, retire it there by hand (a bundle is never archived for one of its documents).";
                }

                var policy = AccProjectSettingsFile.LoadFor(doc, "ACC retire");
                // D10: a settings file that does not load is named, never read as "ask".
                if (policy.Source == AccPolicySource.Malformed)
                    return $"ACC: {docNumber} NOT retired — the project's ACC settings file could not be read " +
                           $"({policy.LoadError}). Fix it and supersede again, or retire it in ACC by hand.";
                string archive = policy.CdeFolders.TryGetValue("ARCHIVE", out var a) ? a : null;
                if (!AccCdeRouting.LooksLikeFolderUrn(archive))
                    return $"ACC: {docNumber} NOT retired — acc_settings.json has no \"cdeFolders\".\"ARCHIVE\" folder.";

                string mode = policy.RetireSupersededInAcc;
                if (mode == "never")
                    return $"ACC: {docNumber} left in place ({SettingKey} = never).";
                if (mode != "always")
                {
                    if (!policy.MayPrompt)
                        return $"ACC: {docNumber} NOT retired — unattended run and {SettingKey} is 'ask'.";
                    var td = new TaskDialog("STING — retire in ACC")
                    {
                        MainInstruction = $"Move {docNumber} to the ACC ARCHIVE folder?",
                        MainContent = $"The deliverable is now {retireSuitability}. STING will copy its ACC document " +
                                      "into the project's ARCHIVE folder and set its suitability to " +
                                      $"{retireSuitability} there and on the original. The original is not deleted.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.Yes,
                    };
                    if (td.Show() != TaskDialogResult.Yes)
                        return $"ACC: {docNumber} left in place (declined).";
                }

                var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC retire");
                var auth = AccIssueSync.EnsureAuthDetailedAsync(creds).GetAwaiter().GetResult();
                if (!auth.Ok)
                    return $"ACC: {docNumber} NOT retired — not authenticated ({auth.Detail}).";

                var names = policy.DocsAttributeNames;
                var lines = new List<string>();
                bool allOk = true, ledgerDirty = false;
                foreach (var (version, folder, entry) in renditions)
                {
                    var res = AccDocsLifecycle.RetireAsync(creds.AccessToken, creds.ProjectId,
                        version, folder, archive, retireSuitability, names, creds).GetAwaiter().GetResult();
                    string label = entry != null ? $"{docNumber} ({entry.Format} {entry.Revision})" : docNumber;
                    allOk &= res.Ok;
                    if (res.Ok) StingLog.Info($"ACC retire {label}: {res.Detail}");
                    else StingLog.Warn($"ACC retire {label} INCOMPLETE:\n{res.Detail}");
                    lines.Add((res.Ok ? $"ACC: {label} retired to ARCHIVE as {retireSuitability}.\n"
                                      : $"ACC: {label} retirement INCOMPLETE — finish it in ACC:\n") + res.Detail);
                    if (res.Ok && entry != null)
                    {
                        entry.RetiredUtc = DateTime.UtcNow;
                        entry.RetiredAs = retireSuitability;
                        ledgerDirty = true;
                    }
                }
                if (ledgerDirty && !ledger.TrySave(ledgerPath, out string saveErr))
                    lines.Add($"(The retirement is done in ACC, but the upload ledger could not be updated: {saveErr} — " +
                              "a later supersede would try to archive it again.)");

                if (row != null)
                {
                    row["acc_retire_status"] = allOk ? "RETIRED" : "INCOMPLETE";
                    row["acc_retire_detail"] = string.Join("\n", lines);
                    BIMManager.BIMManagerEngine.SaveJsonFile(regPath, register);
                }
                return string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                StingLog.Error($"ACC retire {docNumber}", ex);
                return $"ACC: {docNumber} NOT retired — {ex.Message}";
            }
        }
    }
}
