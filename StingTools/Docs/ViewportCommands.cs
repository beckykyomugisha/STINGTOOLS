using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    /// <summary>
    /// Viewport alignment and numbering commands ported from StingDocs OrganizerDockPanel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AlignViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (!(view is ViewSheet sheet))
            {
                TaskDialog.Show("Align Viewports", "Active view must be a sheet.");
                return Result.Succeeded;
            }

            var vpIds = sheet.GetAllViewports().ToList();
            if (vpIds.Count < 2)
            {
                TaskDialog.Show("Align Viewports", "Need at least 2 viewports to align.");
                return Result.Succeeded;
            }

            TaskDialog td = new TaskDialog("Align Viewports");
            td.MainInstruction = $"Align {vpIds.Count} viewports on '{sheet.Name}'";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Align Top");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Align Left");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Center Vertically");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Center Horizontally");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            var viewports = vpIds.Select(id => doc.GetElement(id) as Viewport)
                .Where(v => v != null).ToList();

            using (Transaction tx = new Transaction(doc, "Align Viewports"))
            {
                tx.Start();

                switch (result)
                {
                    case TaskDialogResult.CommandLink1: // Align Top
                        double maxY = viewports.Max(v => v.GetBoxCenter().Y);
                        foreach (var vp in viewports)
                        {
                            XYZ center = vp.GetBoxCenter();
                            vp.SetBoxCenter(new XYZ(center.X, maxY, center.Z));
                        }
                        break;

                    case TaskDialogResult.CommandLink2: // Align Left
                        double minX = viewports.Min(v => v.GetBoxCenter().X);
                        foreach (var vp in viewports)
                        {
                            XYZ center = vp.GetBoxCenter();
                            vp.SetBoxCenter(new XYZ(minX, center.Y, center.Z));
                        }
                        break;

                    case TaskDialogResult.CommandLink3: // Center Vertically
                        double avgY = viewports.Average(v => v.GetBoxCenter().Y);
                        foreach (var vp in viewports)
                        {
                            XYZ center = vp.GetBoxCenter();
                            vp.SetBoxCenter(new XYZ(center.X, avgY, center.Z));
                        }
                        break;

                    case TaskDialogResult.CommandLink4: // Center Horizontally
                        double avgX = viewports.Average(v => v.GetBoxCenter().X);
                        foreach (var vp in viewports)
                        {
                            XYZ center = vp.GetBoxCenter();
                            vp.SetBoxCenter(new XYZ(avgX, center.Y, center.Z));
                        }
                        break;
                }

                tx.Commit();
            }

            TaskDialog.Show("Align Viewports", $"Aligned {viewports.Count} viewports.");
            return Result.Succeeded;
        }
    }

    /// <summary>Renumber viewports on a sheet left-to-right, top-to-bottom.</summary>
    [Transaction(TransactionMode.Manual)]
    public class RenumberViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (!(view is ViewSheet sheet))
            {
                TaskDialog.Show("Renumber Viewports", "Active view must be a sheet.");
                return Result.Succeeded;
            }

            var vpIds = sheet.GetAllViewports().ToList();
            var viewports = vpIds.Select(id => doc.GetElement(id) as Viewport)
                .Where(v => v != null)
                .OrderByDescending(v => v.GetBoxCenter().Y)
                .ThenBy(v => v.GetBoxCenter().X)
                .ToList();

            int num = 1;
            using (Transaction tx = new Transaction(doc, "Renumber Viewports"))
            {
                tx.Start();

                // Temporary names to avoid conflicts
                foreach (var vp in viewports)
                {
                    try { vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)
                        ?.Set($"_TEMP_{num++}"); }
                    catch (Exception ex) { StingLog.Warn($"Viewport temp rename: {ex.Message}"); }
                }

                num = 1;
                foreach (var vp in viewports)
                {
                    try { vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)
                        ?.Set(num.ToString()); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Viewport renumber: {ex.Message}");
                    }
                    num++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Renumber Viewports",
                $"Renumbered {viewports.Count} viewports (T→B, L→R).");
            return Result.Succeeded;
        }
    }

    /// <summary>Text case conversion for text notes.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TextCaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            var textNotes = selected
                .Select(id => doc.GetElement(id) as TextNote)
                .Where(t => t != null).ToList();

            if (textNotes.Count == 0)
            {
                // Fall back to all text notes in view
                textNotes = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>().ToList();
            }

            if (textNotes.Count == 0)
            {
                TaskDialog.Show("Text Case", "No text notes found.");
                return Result.Succeeded;
            }

            TaskDialog td = new TaskDialog("Text Case");
            td.MainInstruction = $"Convert {textNotes.Count} text notes";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "UPPERCASE");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "lowercase");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Title Case");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "Text Case Conversion"))
            {
                tx.Start();
                foreach (var note in textNotes)
                {
                    string text = note.Text;
                    switch (result)
                    {
                        case TaskDialogResult.CommandLink1:
                            note.Text = text.ToUpperInvariant();
                            break;
                        case TaskDialogResult.CommandLink2:
                            note.Text = text.ToLowerInvariant();
                            break;
                        case TaskDialogResult.CommandLink3:
                            note.Text = ToTitleCase(text);
                            break;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Text Case", $"Converted {textNotes.Count} text notes.");
            return Result.Succeeded;
        }

        private static string ToTitleCase(string text)
        {
            // Preserve BIM acronyms
            var preserve = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BIM", "HVAC", "MEP", "ISO", "ASHRAE", "LEED", "IFC", "BLE",
                "STING", "COBie", "LOD", "LOI", "AHU", "FCU", "VAV", "VRF",
                "LED", "UPS", "DB", "MCC", "SWB", "MCCB", "ACB", "RCD",
            };

            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i].Trim();
                if (string.IsNullOrEmpty(w)) continue;

                if (preserve.Contains(w))
                {
                    words[i] = w.ToUpperInvariant();
                }
                else
                {
                    words[i] = char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "");
                }
            }
            return string.Join(" ", words);
        }
    }

    /// <summary>Sum selected room areas.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SumAreasCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds();
            var rooms = selected.Select(id => doc.GetElement(id) as Room)
                .Where(r => r != null && r.Area > 0).ToList();

            if (rooms.Count == 0)
            {
                // Try all rooms
                rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0).ToList();
            }

            if (rooms.Count == 0)
            {
                TaskDialog.Show("Sum Areas", "No rooms with area found.");
                return Result.Succeeded;
            }

            double totalSqFt = rooms.Sum(r => r.Area);
            double totalSqM = totalSqFt * 0.092903;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Rooms: {rooms.Count}");
            report.AppendLine($"Total area: {totalSqM:F2} m² ({totalSqFt:F2} ft²)");
            report.AppendLine();
            foreach (var room in rooms.OrderBy(r => r.Name).Take(30))
            {
                double sqm = room.Area * 0.092903;
                report.AppendLine($"  {room.Number,-8} {room.Name,-25} {sqm,8:F2} m²");
            }
            if (rooms.Count > 30)
                report.AppendLine($"  ... and {rooms.Count - 30} more");

            TaskDialog td = new TaskDialog("Sum Areas");
            td.MainInstruction = $"{rooms.Count} rooms — {totalSqM:F2} m²";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
