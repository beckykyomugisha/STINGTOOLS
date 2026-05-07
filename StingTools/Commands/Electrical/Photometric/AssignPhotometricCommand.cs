using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Photometrics;

namespace StingTools.Commands.Electrical.Photometric
{
    /// <summary>
    /// Stamps photometric data from a parsed file onto the selected
    /// lighting fixture's TYPE. Writing to the type means every instance
    /// gets the same data without re-stamping. The file binding plus the
    /// numeric snapshot lets downstream consumers (DIALux export / IFC
    /// export / Compliance) work without reopening the IES/LDT file.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssignPhotometricCommand : IExternalCommand
    {
        /// <summary>
        /// Set by the dialog before raising the command — the photometric
        /// file to apply, plus the target element ids. ElementId.InvalidElementId
        /// in TargetTypeIds means "use the active selection".
        /// </summary>
        public static PhotometricFile PendingFile;
        public static List<ElementId> PendingTargetTypeIds;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var file = PendingFile;
            if (file == null)
            {
                TaskDialog.Show("STING Photometric",
                    "No file selected. Pick a luminaire in the photometric library, then click Assign.");
                return Result.Cancelled;
            }

            var targetTypeIds = PendingTargetTypeIds ?? new List<ElementId>();
            if (targetTypeIds.Count == 0)
            {
                // Fall back to the type of every selected lighting fixture instance.
                var sel = ctx.UIDoc.Selection?.GetElementIds() ?? new List<ElementId>();
                foreach (var id in sel)
                {
                    if (doc.GetElement(id) is FamilyInstance fi
                        && fi.Category?.Id?.Value == (long)BuiltInCategory.OST_LightingFixtures)
                    {
                        var typeId = fi.GetTypeId();
                        if (typeId != null && typeId != ElementId.InvalidElementId
                            && !targetTypeIds.Contains(typeId)) targetTypeIds.Add(typeId);
                    }
                }
            }
            if (targetTypeIds.Count == 0)
            {
                TaskDialog.Show("STING Photometric",
                    "Select one or more lighting fixtures in the model first, then click Assign.");
                return Result.Cancelled;
            }

            int stamped = 0;
            using (var tx = new Transaction(doc, "STING Assign Photometric File"))
            {
                tx.Start();
                foreach (var typeId in targetTypeIds)
                {
                    var symbol = doc.GetElement(typeId);
                    if (symbol == null) continue;
                    StampType(symbol, file);
                    stamped++;
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Photometric",
                $"Assigned '{file.LuminaireName}' to {stamped} luminaire type(s).\n\n" +
                $"Lumens: {file.TotalLumens:0} · Watts: {file.TotalWatts:0.0} · Efficacy: {file.Efficacy:0.0} lm/W");
            return Result.Succeeded;
        }

        public static void StampType(Element typeElement, PhotometricFile file)
        {
            if (typeElement == null || file == null) return;
            try
            {
                ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_FILE_PATH, file.FilePath ?? "", overwrite: true);
                ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_LUMENS,    $"{file.TotalLumens:0.0}", overwrite: true);
                ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_WATTS,     $"{file.TotalWatts:0.0}",  overwrite: true);
                ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_EFFICACY,  $"{file.Efficacy:0.0}",    overwrite: true);
                ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_BEAM_ANGLE,$"{file.BeamAngleDeg:0.0}",overwrite: true);
                if (file.CCT > 0) ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_CCT, $"{file.CCT:0}", overwrite: true);
                if (file.CRI > 0) ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_CRI, $"{file.CRI:0}", overwrite: true);
                if (!string.IsNullOrEmpty(file.Symmetry))
                    ParameterHelpers.SetString(typeElement, ParamRegistry.ELC_PHOTO_SYMMETRY, file.Symmetry, overwrite: true);
                // Mirror lumens/watts onto the existing LTG_* family params if the
                // family exposes them — this keeps Phase 178 LPD calculations honest.
                ParameterHelpers.SetString(typeElement, ParamRegistry.LTG_LUMENS,  $"{file.TotalLumens:0.0}", overwrite: false);
                ParameterHelpers.SetString(typeElement, ParamRegistry.LTG_WATTAGE, $"{file.TotalWatts:0.0}",  overwrite: false);
            }
            catch (Exception ex) { StingLog.Warn($"AssignPhotometric stamp: {ex.Message}"); }
        }
    }
}
