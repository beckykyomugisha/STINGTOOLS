// StingTools Phase 181 — HVAC wizards.
//
// Replaces the TaskDialog stubs that the HVAC dock panel was routing
// Hvac_RunLoads and Hvac_ExportGbxml to with real Revit API calls.
//
//   HvacRunLoadsCommand   — posts PostableCommand.AnalyzeHeatingAndCoolingLoads
//                           so Revit's native loads engine runs against the
//                           current energy-analysis model. The native dialog
//                           opens for the user to confirm zone settings; on
//                           OK Revit computes loads and writes them onto
//                           Spaces. STING then refreshes the panel KPIs.
//
//   HvacExportGbxmlCommand — opens a folder picker, calls Document.Export
//                            with GBXMLExportOptions targeting the active 3D
//                            view. Hand-off target: IES VE, TRACE 3D Plus,
//                            Carrier HAP or EnergyPlus.
//
// Both commands are surfaced from the HVAC panel LOADS tab.

using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Hvac
{
    /// <summary>
    /// Posts the Heating-and-Cooling Loads command so Revit runs its
    /// native ASHRAE-derived loads engine against the current energy
    /// analytical model.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRunLoadsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var app   = commandData?.Application ?? ParameterHelpers.GetApp(commandData);
                var uidoc = app?.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // Quick pre-flight — the Revit loads engine requires Spaces with
                // a space type, an energy analytical model + at least one zone.
                int spaceCount = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                if (spaceCount == 0)
                {
                    var td = new TaskDialog("STING HVAC — Loads")
                    {
                        MainInstruction = "No MEP Spaces found",
                        MainContent =
                            "Revit's heating/cooling loads engine needs MEP Spaces with " +
                            "space types assigned. Place Spaces (Analyze tab → Space) " +
                            "before running loads.",
                        CommonButtons = TaskDialogCommonButtons.Close
                    };
                    td.Show();
                    return Result.Cancelled;
                }

                // Confirm before posting — the native dialog can take a few
                // minutes on large projects and we want the user prepared.
                var go = new TaskDialog("STING HVAC — Run loads")
                {
                    MainInstruction = $"Run heating/cooling loads against {spaceCount} space(s)?",
                    MainContent =
                        "STING will open Revit's native Heating and Cooling Loads dialog. " +
                        "Confirm zone + building-type settings there, then click Calculate. " +
                        "Results are written to the Space parameters Design Heating Load / " +
                        "Design Cooling Load. STING's LOADS tab will refresh after Revit returns.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Yes
                };
                if (go.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                // Post the native command. AnalyzeHeatingAndCoolingLoads has
                // been a public PostableCommand since Revit 2015.
                try
                {
                    var id = RevitCommandId.LookupPostableCommandId(
                        PostableCommand.AnalyzeHeatingAndCoolingLoads);
                    if (id == null)
                    {
                        message = "Revit did not expose PostableCommand.AnalyzeHeatingAndCoolingLoads.";
                        return Result.Failed;
                    }
                    app.PostCommand(id);
                    StingLog.Info("HvacRunLoadsCommand posted AnalyzeHeatingAndCoolingLoads.");
                }
                catch (Exception postEx)
                {
                    StingLog.Error("HvacRunLoadsCommand PostCommand", postEx);
                    message = postEx.Message;
                    return Result.Failed;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRunLoadsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Exports the active document to gbXML using <see cref="GBXMLExportOptions"/>.
    /// Falls back gracefully when no 3D view is active.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacExportGbxmlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                // gbXML export needs the active view to be a 3D view; bail
                // gracefully if it isn't so the user sees a clear next step.
                var av = doc.ActiveView;
                if (av == null || av.ViewType != ViewType.ThreeD)
                {
                    var td = new TaskDialog("STING HVAC — Export gbXML")
                    {
                        MainInstruction = "Active view must be a 3D view",
                        MainContent =
                            "Revit's gbXML exporter operates on the active 3D view's energy " +
                            "analytical model. Open the {3D} view (or any named 3D view) and " +
                            "run this command again.",
                        CommonButtons = TaskDialogCommonButtons.Close
                    };
                    td.Show();
                    return Result.Cancelled;
                }

                // Resolve target folder from OutputLocationHelper so the file
                // lands alongside other STING exports without prompting.
                string folder;
                try
                {
                    string baseDir = OutputLocationHelper.GetOutputDirectory(doc);
                    folder = string.IsNullOrEmpty(baseDir)
                        ? Path.GetTempPath()
                        : Path.Combine(baseDir, "gbXML");
                }
                catch
                {
                    folder = Path.GetTempPath();
                }
                try { if (!Directory.Exists(folder)) Directory.CreateDirectory(folder); }
                catch (Exception ex) { StingLog.Warn($"gbXML mkdir {folder}: {ex.Message}"); }

                string projectName = doc.Title;
                if (string.IsNullOrEmpty(projectName)) projectName = "Project";
                string sanitised = string.Join("_",
                    projectName.Split(System.IO.Path.GetInvalidFileNameChars()));
                string fileName  = $"{sanitised}_{DateTime.Now:yyyyMMdd_HHmm}.xml";
                string fullPath  = Path.Combine(folder, fileName);

                // GBXMLExportOptions defaults are sensible for a generic export.
                // ExportEnergyModelType.SpatialElement preserves Space topology
                // for the downstream loads engine (IES / TRACE / HAP / EnergyPlus).
                var opts = new GBXMLExportOptions();
                try
                {
                    // Property name + enum availability varies by Revit version,
                    // so the cast is defensive — fall through silently if the
                    // enum value is missing.
                    var p = opts.GetType().GetProperty("ExportEnergyModelType");
                    var enumType = p?.PropertyType;
                    if (p != null && enumType != null && enumType.IsEnum)
                    {
                        foreach (var name in enumType.GetEnumNames())
                        {
                            if (string.Equals(name, "SpatialElement", StringComparison.OrdinalIgnoreCase))
                            {
                                p.SetValue(opts, Enum.Parse(enumType, name));
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"gbXML opts: {ex.Message}"); }

                try
                {
                    bool ok = doc.Export(folder, fileName, opts);
                    if (!ok)
                    {
                        var td = new TaskDialog("STING HVAC — Export gbXML")
                        {
                            MainInstruction = "gbXML export returned false",
                            MainContent = "Revit refused to export. Most common causes:\n" +
                                "• Energy settings not configured (Analyze → Energy Settings)\n" +
                                "• No spatial elements (Spaces) bounding the analytical model\n" +
                                "• Active view's section box is empty",
                            CommonButtons = TaskDialogCommonButtons.Close
                        };
                        td.Show();
                        return Result.Failed;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Error("HvacExportGbxmlCommand Export", ex);
                    message = ex.Message;
                    return Result.Failed;
                }

                var done = new TaskDialog("STING HVAC — Export gbXML")
                {
                    MainInstruction = "gbXML exported",
                    MainContent = $"File written to:\n{fullPath}\n\n" +
                        "Hand off to IES VE, TRACE 3D Plus, Carrier HAP or EnergyPlus.",
                    CommonButtons = TaskDialogCommonButtons.Close
                };
                done.Show();

                StingLog.Info($"HvacExportGbxmlCommand wrote {fullPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacExportGbxmlCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
