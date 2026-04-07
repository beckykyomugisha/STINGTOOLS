using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ══════════════════════════════════════════════════════════════════════
    //  QRCodeCommand — generate QR codes for selected elements
    //  Phase 76 Item 10
    //
    //  For each selected element, reads ASS_TAG_1_TXT, builds a sting://
    //  asset URL, generates a QR code PNG, and saves to _bim_manager/qr/.
    //  Reports summary via TaskDialog.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class QRCodeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc   = uiDoc.Document;

            // Determine output folder: _bim_manager/qr/ next to .rvt
            string projectDir = string.IsNullOrEmpty(doc.PathName)
                ? Path.Combine(Path.GetTempPath(), "STING_QR")
                : Path.Combine(Path.GetDirectoryName(doc.PathName), "_bim_manager", "qr");
            Directory.CreateDirectory(projectDir);

            // Project code from doc title
            string projectCode = Path.GetFileNameWithoutExtension(doc.Title) ?? "PRJ";

            // Collect selected elements (or active view elements if nothing selected)
            var selIds = uiDoc.Selection.GetElementIds();
            ICollection<ElementId> targets = selIds.Count > 0
                ? selIds
                : new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds();

            int generated = 0, skipped = 0;
            var errors = new List<string>();

            foreach (var id in targets)
            {
                try
                {
                    var el = doc.GetElement(id);
                    if (el == null) { skipped++; continue; }

                    string tagValue = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
                    if (string.IsNullOrWhiteSpace(tagValue))
                    {
                        skipped++;
                        continue;
                    }

                    string assetUrl = StingQRHelper.BuildAssetUrl(projectCode, tagValue);
                    string safeTag  = tagValue.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
                    string pngPath  = Path.Combine(projectDir, $"{safeTag}.png");

                    StingQRHelper.SaveQRPng(assetUrl, pngPath, size: 200);
                    generated++;
                }
                catch (Exception ex)
                {
                    errors.Add($"ID {id.Value}: {ex.Message}");
                    skipped++;
                }
            }

            string summary = $"QR Code Generation\n\n" +
                             $"Generated : {generated}\n" +
                             $"Skipped   : {skipped} (no ASS_TAG_1_TXT)\n\n" +
                             $"Output folder:\n{projectDir}";

            if (errors.Count > 0)
                summary += $"\n\nErrors ({errors.Count}):\n" + string.Join("\n", errors.Take(5));

            var td = new TaskDialog("QR Codes")
            {
                MainInstruction = generated > 0 ? $"Generated {generated} QR code(s)" : "No QR codes generated",
                MainContent = summary,
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            if (generated > 0)
            {
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open QR output folder");
            }
            var result = td.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", projectDir) { UseShellExecute = true })?.Dispose(); }
                catch { }
            }

            return Result.Succeeded;
        }
    }
}
