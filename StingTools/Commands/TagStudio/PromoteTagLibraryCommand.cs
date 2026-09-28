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
// Copies only. It never deletes from the target: a family in the shared library
// that is absent from the source is reported and kept, because this command
// cannot know whether it is stale or something another team published.

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
            string git = FindRepoLibrary(source);

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

            if (plan.WouldWrite == 0)
            {
                TaskDialog.Show("Promote Tag Library",
                    $"Nothing to do — the shared library already matches.\n\n{plan.Summary()}\n\n" +
                    $"Target: {target}");
                return Result.Succeeded;
            }

            var confirm = new TaskDialog("Promote Tag Library")
            {
                MainInstruction = $"Publish {plan.WouldWrite} family/families to the shared library?",
                MainContent =
                    $"From: {source}\nTo:   {target}\n\n{plan.Summary()}\n\n" +
                    "The shared library is searched BEFORE the deployed one and a deploy cannot " +
                    "update it, so what is published here wins until it is published again.\n\n" +
                    "Nothing is deleted. A family already in the target and not in the source is " +
                    "kept and listed in the manifest.",
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
                    }
                }

                // The manifest is written LAST and only for what actually copied,
                // so a partial promotion never claims a complete one.
                string manifest = TagLibraryPromotion.BuildManifest(
                    plan, Environment.UserName, DateTime.UtcNow);
                File.WriteAllText(Path.Combine(target, "_STING_PROMOTION_MANIFEST.txt"), manifest);
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
                $"Published {written} of {plan.WouldWrite} family/families.\n\n" +
                (failures.Count > 0
                    ? "FAILED:\n  " + string.Join("\n  ", failures.Take(8)) + "\n\n"
                    : "") +
                $"Target: {target}\nManifest: _STING_PROMOTION_MANIFEST.txt\n\n" +
                "Re-open a project to see the content-root lines in the log confirm which " +
                "library now answers family lookups.");

            return failures.Count > 0 ? Result.Failed : Result.Succeeded;
        }

        /// <summary>
        /// The version-controlled copy, if this machine has the checkout.
        ///
        /// <para>The deployed library sits at &lt;repo&gt;/CompiledPlugin/data/TagFamilies
        /// on a developer machine, so the git copy is a short walk up and back
        /// down. Returns null on an end-user machine, where the check is skipped
        /// rather than failed - refusing there would block the people the library
        /// is for.</para>
        /// </summary>
        private static string FindRepoLibrary(string deployedDir)
        {
            try
            {
                var dir = new DirectoryInfo(deployedDir);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "StingTools", "Data", "TagFamilies");
                    if (Directory.Exists(candidate) &&
                        !string.Equals(candidate.TrimEnd('\\'), deployedDir.TrimEnd('\\'),
                                       StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            catch (Exception ex) { StingLog.Warn($"PromoteTagLibrary.FindRepoLibrary: {ex.Message}"); }
            return null;
        }
    }
}
