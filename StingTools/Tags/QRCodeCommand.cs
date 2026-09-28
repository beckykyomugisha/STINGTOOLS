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
            // Phase 98: BCC dispatches into WorkflowEngine.ResolveCommand with
            // commandData = null, so `ParameterHelpers.GetApp(commandData).ActiveUIDocument`
            // used to throw NRE. Use the safe fallback that picks up
            // StingCommandHandler.CurrentApp when dispatched from modeless WPF.
            var uiApp = ParameterHelpers.GetApp(commandData);
            var uiDoc = uiApp?.ActiveUIDocument;
            if (uiDoc == null)
            {
                TaskDialog.Show("STING", "No active document — open a project to generate QR codes.");
                return Result.Failed;
            }
            var doc = uiDoc.Document;

            // Determine output folder: _bim_manager/qr/ next to .rvt
            string projectDir = string.IsNullOrEmpty(doc.PathName)
                ? Path.Combine(Path.GetTempPath(), "STING_QR")
                : ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER", "qr");
            Directory.CreateDirectory(projectDir);

            // Project code: the ISO 19650 project code the rest of the plugin uses,
            // NOT the .rvt filename. Reading the filename meant renaming the model
            // silently changed every QR payload it had ever produced.
            string projectCode = null;
            try { projectCode = ParameterHelpers.GetString(doc.ProjectInformation, ParamRegistry.ORG_PROJECT_CODE); }
            catch (Exception ex) { StingLog.Warn($"QRCode: could not read {ParamRegistry.ORG_PROJECT_CODE}: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(projectCode))
                projectCode = doc.ProjectInformation?.Number;
            if (string.IsNullOrWhiteSpace(projectCode))
                projectCode = "PRJ";

            // Collect selected elements (or active view elements if nothing selected).
            // Guard against ActiveView being null (family editor, dockable panel with
            // no graphical view selected) so we fail gracefully rather than NRE.
            var selIds = uiDoc.Selection.GetElementIds();
            ICollection<ElementId> targets;
            if (selIds.Count > 0)
            {
                targets = selIds;
            }
            else if (doc.ActiveView != null)
            {
                targets = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
            }
            else
            {
                TaskDialog.Show("QR Code", "Select elements or switch to a graphical view first.");
                return Result.Cancelled;
            }

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

                    // Carry the UniqueId so a scan can drive the commissioning path,
                    // which resolves elements by UniqueId and had no way to act on a
                    // tag-only payload.
                    string assetUrl = StingQrFormat.BuildElementUrl(projectCode, tagValue, el.UniqueId);
                    string safeTag  = SanitiseFileName(tagValue);
                    string pngPath  = Path.Combine(projectDir, $"{safeTag}.png");

                    StingQRHelper.SaveQRPng(assetUrl, pngPath, size: 200);
                    generated++;
                }
                catch (Exception ex)
                {
                    // A failure is NOT a skip. Counting it as one made a folder full of
                    // exceptions read as "these elements just had no tag".
                    StingLog.Error($"QRCode: element {id.Value} failed", ex);
                    errors.Add($"ID {id.Value}: {ex.Message}");
                }
            }

            string summary = $"QR Code Generation\n\n" +
                             $"Generated : {generated}\n" +
                             $"Skipped   : {skipped} (no ASS_TAG_1_TXT)\n" +
                             $"Failed    : {errors.Count}\n\n" +
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
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            return Result.Succeeded;
        }

        /// <summary>Make a tag safe as a filename. Replaces every character Windows
        /// rejects, not the three (<c>/ \ :</c>) this used to handle — a tag carrying
        /// <c>* ? " &lt; &gt; |</c> threw, and the throw was counted as "skipped, no
        /// tag", which is a different and misleading thing.</summary>
        internal static string SanitiseFileName(string value)
        {
            var chars = value.ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            var name = new string(chars).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "unnamed" : name;
        }
    }
}
