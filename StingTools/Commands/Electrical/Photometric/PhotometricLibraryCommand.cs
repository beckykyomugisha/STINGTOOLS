using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Photometrics;

namespace StingTools.Commands.Electrical.Photometric
{
    /// <summary>
    /// Opens the modal library viewer; on first run prompts the user for
    /// a root directory of IES / LDT / GLDF files and persists it in
    /// <c>&lt;project&gt;/_BIM_COORD/photometric_roots.txt</c>. The dialog
    /// itself runs the assign / preflight workflows.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhotometricLibraryCommand : IExternalCommand
    {
        public const string ConfigFileName = "photometric_roots.txt";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var roots = LoadRoots(doc);
            if (roots.Count == 0)
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick any IES/LDT file in the photometric library root",
                    Filter = "Photometric files (*.ies;*.ldt)|*.ies;*.ldt|All files (*.*)|*.*"
                };
                if (ofd.ShowDialog() == true)
                {
                    string root = Path.GetDirectoryName(ofd.FileName) ?? "";
                    if (!string.IsNullOrEmpty(root))
                    {
                        roots.Add(root);
                        SaveRoots(doc, roots);
                    }
                }
                if (roots.Count == 0) return Result.Cancelled;
            }
            var lib = new PhotometricLibrary(roots);

            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var dlg = new StingTools.UI.PhotometricLibraryDialog(lib, doc);
                    try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                    dlg.ShowDialog();
                });
            }
            catch (Exception ex) { StingLog.Warn($"OpenPhotometricLibraryDialog: {ex.Message}"); }
            return Result.Succeeded;
        }

        public static List<string> LoadRoots(Document doc)
        {
            var list = new List<string>();
            try
            {
                string path = ResolveConfigPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
                foreach (var line in File.ReadAllLines(path))
                {
                    string trim = (line ?? "").Trim();
                    if (!string.IsNullOrEmpty(trim) && Directory.Exists(trim))
                        list.Add(trim);
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadRoots: {ex.Message}"); }
            return list;
        }

        public static void SaveRoots(Document doc, List<string> roots)
        {
            try
            {
                string path = ResolveConfigPath(doc);
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, roots ?? new List<string>());
            }
            catch (Exception ex) { StingLog.Warn($"SaveRoots: {ex.Message}"); }
        }

        private static string ResolveConfigPath(Document doc)
        {
            try
            {
                string projectFile = doc?.PathName ?? "";
                string projectDir = string.IsNullOrEmpty(projectFile)
                    ? OutputLocationHelper.GetOutputDirectory(doc)
                    : Path.GetDirectoryName(projectFile);
                return Path.Combine(projectDir ?? "", "_BIM_COORD", ConfigFileName);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveConfigPath: {ex.Message}"); return null; }
        }
    }
}
