// PromoteTagLibraryCommand - publish the finished tag library to the shared
// content root.
//
// The shared root is searched BEFORE the deployed library and a deploy cannot
// touch it, so whatever is promoted wins every lookup until somebody promotes
// again. That is what makes it the right home for a finished library, and the
// reason this command refuses to publish a source that differs from the
// version-controlled copy - see TagLibraryPromotion for the full reasoning and
// the 2026-09-21 case that prompted it.
//
// It never deletes from the target. A family in the shared library that is absent
// from the source is reported, and moved to _retired only if the user agrees,
// because this command cannot know whether it is stale or something another team
// published.

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Content;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PromoteTagLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Panel dispatch passes null commandData - GetApp is the sanctioned
            // accessor, enforced by tools/check_command_app_acquisition.ps1.
            var app = ParameterHelpers.GetApp(commandData);
            if (app == null)
            {
                StingLog.Warn("PromoteTagLibrary: no Revit application");
                return Result.Failed;
            }

            string source = Tags.TagFamilyConfig.LegacyTagDirectory();
            string target = Tags.TagFamilyConfig.SharedTagDirectory();
            string git = Tags.TagFamilyConfig.RepoTagDirectory();

            if (string.IsNullOrWhiteSpace(target))
            {
                TaskDialog.Show("Promote Tag Library",
                    "No shared content root is configured, so there is nowhere to promote to.\n\n" +
                    "Set STING_CONTENT_LIB, or %APPDATA%\\STING\\sting_content.json \"content_root\", " +
                    "to the folder the team's libraries live in.");
                return Result.Cancelled;
            }

            var plan = TagLibraryPromotion.Plan(source, target, git);

            StingLog.Info($"PromoteTagLibrary: source='{source}' target='{target}' " +
                          $"git='{git ?? "(not discoverable)"}' — {plan.Summary()}");

            if (!plan.CanProceed)
            {
                foreach (var b in plan.Blockers) StingLog.Warn("PromoteTagLibrary blocked: " + b);
                TaskDialog.Show("Promote Tag Library — refused",
                    "The library was NOT promoted.\n\n" + string.Join("\n\n", plan.Blockers));
                return Result.Cancelled;
            }

            // A share promoted before records listed each file cannot be checked by
            // Load Tag Families, so rewriting the record is work even with no copies.
            bool needsRecord = true;
            try
            {
                string existing = Path.Combine(target, TagLibraryPromotion.ManifestFileName);
                needsRecord = !File.Exists(existing) ||
                              TagLibraryPromotion.ParsePublished(File.ReadAllText(existing)).Count == 0;
            }
            catch (Exception ex) { StingLog.Warn($"PromoteTagLibrary: reading the existing record: {ex.Message}"); }

            if (plan.WouldWrite == 0 && plan.ExtraInTarget.Count == 0 && !needsRecord)
            {
                TaskDialog.Show("Promote Tag Library",
                    $"Nothing to do — the shared library already matches.\n\n{plan.Summary()}\n\n" +
                    $"Target: {target}");
                return Result.Succeeded;
            }

            var confirm = new TaskDialog("Promote Tag Library")
            {
                MainInstruction = plan.WouldWrite > 0
                    ? $"Publish {plan.WouldWrite} family/families to the shared library?"
                    : "The shared library already matches. Update its promotion record?",
                MainContent =
                    $"From: {source}\nTo:   {target}\n\n{plan.Summary()}\n\n" +
                    "The shared library is searched BEFORE the deployed one and a deploy cannot " +
                    "update it, so what is published here wins until it is published again.\n\n" +
                    "Nothing is deleted. A family already in the target and not in the source is " +
                    "kept, unless you choose to move it to _retired in the next step." +
                    (plan.NewerInTarget.Count > 0
                        ? $"\n\nWARNING: {plan.NewerInTarget.Count} of the families to update are NEWER in the " +
                          "shared library than here, so this plugin may be older than the one that published " +
                          "them. Promoting replaces them with these copies: " +
                          string.Join(", ", plan.NewerInTarget.Take(5)) + (plan.NewerInTarget.Count > 5 ? ", ..." : "")
                        : ""),
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            int written = 0;
            var failures = new System.Collections.Generic.List<string>();
            try
            {
                Directory.CreateDirectory(target);
                foreach (var name in plan.ToAdd.Concat(plan.ToUpdate))
                {
                    try
                    {
                        File.Copy(Path.Combine(source, name), Path.Combine(target, name), true);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{name}: {ex.Message}");
                        StingLog.Warn($"PromoteTagLibrary: {name} failed: {ex.Message}");
                        // The share still holds the old copy (or none), so the record must
                        // not vouch for it; Load Tag Families then reports it.
                        plan.SourceHashes.Remove(name);
                    }
                }

                // Families in the share that the source no longer has would keep
                // winning lookups. Offer to move them aside; never delete.
                if (plan.ExtraInTarget.Count > 0)
                {
                    var retire = new TaskDialog("Promote Tag Library")
                    {
                        MainInstruction = $"{plan.ExtraInTarget.Count} family/families in the shared library are not in the source",
                        MainContent =
                            "They still answer lookups for everyone. Move them to a dated _retired " +
                            "folder in the shared library? Nothing is deleted; moving a file back restores it.\n\n" +
                            string.Join("\n", plan.ExtraInTarget.Take(12)) +
                            (plan.ExtraInTarget.Count > 12 ? $"\n… and {plan.ExtraInTarget.Count - 12} more" : ""),
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    if (retire.Show() == TaskDialogResult.Yes)
                    {
                        plan.RetiredFolder = Path.Combine(TagLibraryPromotion.RetiredFolderName, DateTime.Now.ToString("yyyyMMdd_HHmm"));
                        string dest = Path.Combine(target, plan.RetiredFolder);
                        Directory.CreateDirectory(dest);
                        foreach (var name in plan.ExtraInTarget)
                        {
                            try
                            {
                                File.Move(Path.Combine(target, name), Path.Combine(dest, name));
                                plan.Retired.Add(name);
                            }
                            catch (Exception ex)
                            {
                                failures.Add($"retire {name}: {ex.Message}");
                                StingLog.Warn($"PromoteTagLibrary: retiring {name}: {ex.Message}");
                            }
                        }
                    }
                }

                // The manifest is written LAST and only for what actually copied,
                // so a partial promotion never claims a complete one.
                string manifest = TagLibraryPromotion.BuildManifest(
                    plan, Environment.UserName, DateTime.UtcNow);
                File.WriteAllText(Path.Combine(target, TagLibraryPromotion.ManifestFileName), manifest);
            }
            catch (Exception ex)
            {
                StingLog.Error("PromoteTagLibrary", ex);
                message = ex.Message;
                return Result.Failed;
            }

            StingLog.Info($"PromoteTagLibrary: wrote {written} of {plan.WouldWrite}, " +
                          $"{failures.Count} failed, manifest written to {target}");

            TaskDialog.Show("Promote Tag Library — done",
                (plan.WouldWrite > 0
                    ? $"Published {written} of {plan.WouldWrite} family/families."
                    : "No family needed copying; the shared library already matched.") +
                $" The promotion record now lists {plan.SourceHashes.Count} family/families," +
                " which Load Tag Families checks the shared library against." +
                (plan.Retired.Count > 0 ? $" Retired {plan.Retired.Count} to {plan.RetiredFolder}." : "") + "\n\n" +
                (failures.Count > 0
                    ? "FAILED:\n  " + string.Join("\n  ", failures.Take(8)) + "\n\n"
                    : "") +
                $"Target: {target}\nManifest: _STING_PROMOTION_MANIFEST.txt\n\n" +
                "Re-open a project to see the content-root lines in the log confirm which " +
                "library now answers family lookups.");

            return failures.Count > 0 ? Result.Failed : Result.Succeeded;
        }
    }
}
