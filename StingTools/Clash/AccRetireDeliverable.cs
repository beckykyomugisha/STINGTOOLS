// StingTools — take a superseded / replaced deliverable out of circulation in ACC.
//
// DeliverableLifecycle.Supersede / Replace used to touch deliverables.json only. This is
// the Revit-side half of the fix: it finds the deliverable's own ACC document, asks (or
// follows the project setting), and hands the retirement to the Revit-free
// AccDocsLifecycle client, recording what happened on the register row.
//
// Where the ACC document is found: a register row whose doc_number is the deliverable's
// number and which carries acc_version_urn (+ acc_folder_urn). A transmittal BUNDLE is
// deliberately NOT retired: one ZIP holds many deliverables, and archiving it to retire
// one of them would retire the rest. That case is reported, not guessed at.
//
// Setting (acc_settings.json, optional): "retireSupersededInAcc": "ask" (default) |
// "always" | "never". Unattended runs never prompt: "ask" becomes "not retired, reported".

using System;
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
                if (row == null)
                {
                    StingLog.Info($"ACC retire: {docNumber} has no per-document ACC record — nothing to retire in ACC.");
                    return $"ACC: no per-document ACC record for {docNumber}; if it reached ACC inside a transmittal " +
                           "bundle, retire it there by hand (a bundle is never archived for one of its documents).";
                }

                var policy = AccProjectSettingsFile.LoadFor(doc, "ACC retire");
                string archive = policy.CdeFolders.TryGetValue("ARCHIVE", out var a) ? a : null;
                if (!AccCdeRouting.LooksLikeFolderUrn(archive))
                    return $"ACC: {docNumber} NOT retired — acc_settings.json has no \"cdeFolders\".\"ARCHIVE\" folder.";

                string mode = ReadMode(doc);
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

                var res = AccDocsLifecycle.RetireAsync(creds.AccessToken, creds.ProjectId,
                    row["acc_version_urn"].ToString(), row["acc_folder_urn"]?.ToString(), archive, retireSuitability,
                    AccProjectSettingsFile.LoadFor(doc, "ACC retire").DocsAttributeNames)
                    .GetAwaiter().GetResult();

                if (!string.IsNullOrEmpty(res.ArchivedItemUrn)) row["acc_archived_item_urn"] = res.ArchivedItemUrn;
                if (!string.IsNullOrEmpty(res.ArchivedVersionUrn)) row["acc_archived_version_urn"] = res.ArchivedVersionUrn;
                row["acc_retire_status"] = res.Ok ? "RETIRED" : "INCOMPLETE";
                row["acc_retire_detail"] = res.Detail;
                BIMManager.BIMManagerEngine.SaveJsonFile(regPath, register);

                if (res.Ok) StingLog.Info($"ACC retire {docNumber}: {res.Detail}");
                else StingLog.Warn($"ACC retire {docNumber} INCOMPLETE:\n{res.Detail}");
                return (res.Ok ? $"ACC: {docNumber} retired to ARCHIVE as {retireSuitability}.\n"
                               : $"ACC: {docNumber} retirement INCOMPLETE — finish it in ACC:\n") + res.Detail;
            }
            catch (Exception ex)
            {
                StingLog.Error($"ACC retire {docNumber}", ex);
                return $"ACC: {docNumber} NOT retired — {ex.Message}";
            }
        }

        private static string ReadMode(Document doc)
        {
            try
            {
                string p = AccProjectSettingsFile.PathFor(doc);
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return "ask";
                string v = (JObject.Parse(File.ReadAllText(p))[SettingKey]?.ToString() ?? "").Trim().ToLowerInvariant();
                return v == "always" || v == "never" ? v : "ask";
            }
            catch (Exception ex) { StingLog.Warn("ACC retire setting: " + ex.Message); return "ask"; }
        }
    }
}
