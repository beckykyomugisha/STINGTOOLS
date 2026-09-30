// StingTools — Repair Tag Library.
//
// The shipped tag families carry STING parameters as TEXT that MR_PARAMETERS.txt
// gives other types, so every project set up with Load Params refuses them. Load Tag
// Families works round it in memory on each load; this corrects the library files
// themselves, once, so they load as they are.
//
// It repairs the git-tracked library (<repo>/StingTools/Data/TagFamilies and its
// _master) when the plugin runs from a checkout, because a deploy overwrites the
// deployed copies with the repo's. Each repaired file is also copied into the
// deployed library so it takes effect at once. The repair is the one Load Tag
// Families uses (TagFamilyLoadRepair), judged against MR_PARAMETERS.txt rather than
// a project. Revit backups (*.0001.rfa) are deleted from the deployed library.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RepairTagLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var app = ctx.Doc.Application;

            string deployed = TagFamilyConfig.LegacyTagDirectory();
            string repo = TagFamilyConfig.RepoTagDirectory();
            string target = repo ?? deployed;
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                TaskDialog.Show("Repair Tag Library", "No tag library folder was found.\n\nLooked for: " + (target ?? "(none)"));
                return Result.Failed;
            }

            var files = Directory.GetFiles(target, "*.rfa")
                .Concat(Directory.Exists(Path.Combine(target, "_master"))
                    ? Directory.GetFiles(Path.Combine(target, "_master"), "*.rfa") : new string[0])
                .Where(f => !RevitBackupFiles.IsBackup(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            var reference = TagFamilyLoadRepair.ReadParameterFile(app, out string refError);
            if (reference == null || reference.Count == 0)
            {
                TaskDialog.Show("Repair Tag Library", "Cannot repair: " + (refError ?? "MR_PARAMETERS.txt has no definitions") + ".");
                return Result.Failed;
            }

            var ask = new TaskDialog("Repair Tag Library")
            {
                MainInstruction = $"Correct {files.Count} tag families against MR_PARAMETERS.txt?",
                MainContent =
                    $"Folder: {target}\n" +
                    (repo != null
                        ? "This is the git-tracked library. Commit the changed files afterwards; " +
                          "`git checkout -- StingTools/Data/TagFamilies` undoes it.\n"
                        : "No repository checkout was found, so the DEPLOYED library is repaired. " +
                          "The next deploy overwrites it with the repository copy.\n") +
                    "\nA parameter a family carries as Text but MR_PARAMETERS.txt types otherwise is " +
                    "swapped for its Text display mirror, or, where only a label reads it, removed from " +
                    "the label. Each family is opened, so this takes several minutes.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            if (ask.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            int repaired = 0, clean = 0, failed = 0, copiedToDeployed = 0;
            var report = new StringBuilder();
            UI.StingProgressDialog progress = UI.StingProgressDialog.Show("STING — repairing tag library", files.Count);
            try
            {
                using (var repair = new TagFamilyLoadRepair(app, reference))
                {
                    foreach (string path in files)
                    {
                        if (progress.IsCancelled) { report.Insert(0, "Cancelled part-way; files already repaired stay repaired.\n\n"); break; }
                        string name = Path.GetFileNameWithoutExtension(path);
                        progress.Increment(name);
                        var it = repair.Prepare(path);
                        if (it.Blocked != null)
                        {
                            failed++;
                            report.AppendLine($"  [NOT REPAIRED] {name} — {it.Blocked}");
                            continue;
                        }
                        if (it.Repairs.Count == 0) { clean++; continue; }
                        try
                        {
                            File.Copy(it.LoadPath, path, true);
                            repaired++;
                            report.AppendLine($"  [REPAIRED] {name}: {string.Join("; ", it.Repairs)}");
                            if (repo != null && !string.IsNullOrEmpty(deployed) && Directory.Exists(deployed))
                            {
                                string rel = Path.GetRelativePath(target, path);
                                string dst = Path.Combine(deployed, rel);
                                if (Directory.Exists(Path.GetDirectoryName(dst)))
                                {
                                    File.Copy(it.LoadPath, dst, true);
                                    copiedToDeployed++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            report.AppendLine($"  [NOT REPAIRED] {name} — writing the file failed: {ex.Message}");
                            StingLog.Error($"RepairTagLibrary: writing {path}", ex);
                        }
                    }
                }
            }
            finally { progress.Close(); }

            int backupsRemoved = RemoveBackups(deployed);

            StingLog.Info($"RepairTagLibrary: {target}: repaired={repaired}, clean={clean}, failed={failed}, " +
                          $"copiedToDeployed={copiedToDeployed}, backupsRemoved={backupsRemoved}");
            var td = new TaskDialog("Repair Tag Library")
            {
                MainInstruction = failed == 0
                    ? $"Repaired {repaired} tag families; {clean} needed nothing"
                    : $"Repaired {repaired}, {clean} needed nothing, {failed} not repaired",
                MainContent =
                    $"Library: {target}\n" +
                    (copiedToDeployed > 0 ? $"Also copied into the deployed library: {copiedToDeployed}\n" : "") +
                    (backupsRemoved > 0 ? $"Revit backup files removed from the deployed library: {backupsRemoved}\n" : "") +
                    (repo != null && repaired > 0
                        ? "\nNext: commit StingTools/Data/TagFamilies, then re-stamp the content manifest " +
                          "(tools/restamp_content_manifest.py --apply StingTools/Data/TagFamilies)." : "") +
                    (repaired > 0 && TagFamilyConfig.IsPromotionManaged(TagFamilyConfig.SharedTagDirectory())
                        ? "\n\nThe shared library (" + TagFamilyConfig.SharedTagDirectory() + ") is read first and " +
                          "still holds the old families. Run Promote Library (MODEL > Family quick edit > " +
                          "Advanced family ops) to publish the repaired ones." : "")
            };
            if (report.Length > 0) td.ExpandedContent = report.ToString();
            td.Show();
            return Result.Succeeded;
        }

        /// <summary>Deletes Revit backups (*.0001.rfa) from the deployed library.</summary>
        private static int RemoveBackups(string dir)
        {
            int n = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            foreach (string f in Directory.GetFiles(dir, "*.rfa", SearchOption.AllDirectories).Where(RevitBackupFiles.IsBackup))
            {
                try { File.Delete(f); n++; }
                catch (Exception ex) { StingLog.Warn($"RepairTagLibrary: deleting backup {f}: {ex.Message}"); }
            }
            return n;
        }
    }
}
