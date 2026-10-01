using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// Drops a detail-circle annotation at every panel's footprint on the
    /// active plan view, sized to its arc-flash boundary distance
    /// (ELC_ARC_FLASH_BOUNDARY_MM parameter, populated by ArcFlashCommand
    /// — accessed via the ParamRegistry alias for canonical resolution).
    /// Colour-codes red/orange/yellow/green by PPE category for instant
    /// safety-zone awareness on installation drawings. Boundaries are
    /// IEEE 1584-2018 values (<see cref="ArcFlashEngine.Basis"/>).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashBoundaryViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var view = doc.ActiveView;
            if (view == null || view.IsTemplate || view.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("STING Arc Flash Boundary",
                    "Activate a floor plan view first — boundary circles are drawn on the active plan.");
                return Result.Cancelled;
            }

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Arc Flash Boundary", "No electrical equipment found.");
                return Result.Cancelled;
            }

            int drawn = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Arc Flash Boundary Circles"))
            {
                tx.Start();
                foreach (var panel in panels)
                {
                    try
                    {
                        // Canonical via ParamRegistry: ELC_ARC_FLASH_BOUNDARY_MM
                        // and ELC_ARC_FLASH_PPE_CAT. ParamRegistry.ELC_ARC_FLASH_BD /
                        // _PPE alias these so the lookup matches whichever schema
                        // version the project ships.
                        double bdMm = ParseDouble(panel.LookupParameter(ParamRegistry.ELC_ARC_FLASH_BD)?.AsString());
                        if (bdMm <= 0) { skipped++; continue; }   // "N/A" = not calculated -> no circle
                        int ppe = (int)ParseDouble(panel.LookupParameter(ParamRegistry.ELC_ARC_FLASH_PPE)?.AsString());
                        XYZ origin = (panel.Location as LocationPoint)?.Point;
                        if (origin == null) { skipped++; continue; }
                        double bdFt = bdMm / 304.8;

                        // A detail curve must lie in the view plane. Project the panel
                        // location onto the plane through view.Origin normal to
                        // view.ViewDirection and draw the arc there. (The old code
                        // passed an ElementId to SketchPlane.Create and then built a
                        // plane at the panel's own elevation - neither is the view plane.)
                        XYZ n = view.ViewDirection.Normalize();
                        XYZ centre = origin - n.Multiply(n.DotProduct(origin - view.Origin));
                        Plane plane = Plane.CreateByNormalAndOrigin(n, centre);
                        Arc arc = Arc.Create(plane, bdFt, 0, 2 * Math.PI);
                        var circle = doc.Create.NewDetailCurve(view, arc);
                        // Colour the curve by PPE category via override
                        var ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLineColor(PpeColor(ppe));
                        ogs.SetProjectionLineWeight(5);
                        view.SetElementOverrides(circle.Id, ogs);
                        drawn++;
                    }
                    catch (Exception ex) { StingLog.Warn($"AF boundary {panel.Name}: {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Arc Flash Boundary",
                $"Drew {drawn} boundary circle(s) on {view.Name}.\n" +
                $"Skipped {skipped} (not calculated, no boundary value or no location).\n" +
                $"Boundaries are {ArcFlashEngine.BasisShort} - verify with a licensed study.\n\n" +
                "Run Elec_ClearOverrides on this view to remove the colour overrides; " +
                "delete the detail curves manually if you want to clear the geometry.");
            return Result.Succeeded;
        }

        private static Color PpeColor(int ppe)
        {
            var (r, g, b) = PpeRgb(ppe);
            return new Color(r, g, b);
        }

        // Built-in colours by PPE category; STING_ARC_FLASH_PPE.json ppeCategories[].colour
        // is used only when every category's colour matches these (see PpeRgb).
        private static (byte r, byte g, byte b) BuiltInPpeRgb(int ppe) => ppe switch
        {
            < 0  => ((byte)183, (byte)28,  (byte)28),    // dark red - exceeds 40 cal/cm2 (was drawn green)
            >= 4 => ((byte)244, (byte)67,  (byte)54),    // red
            3    => ((byte)255, (byte)87,  (byte)34),    // deep orange
            2    => ((byte)255, (byte)152, (byte)0),     // orange
            1    => ((byte)255, (byte)235, (byte)59),    // yellow
            _    => ((byte)76,  (byte)175, (byte)80)     // green
        };

        private static readonly Lazy<Dictionary<int, (byte r, byte g, byte b)>> _dataRgb =
            new Lazy<Dictionary<int, (byte r, byte g, byte b)>>(LoadDataRgb);

        private static (byte r, byte g, byte b) PpeRgb(int ppe)
        {
            var data = _dataRgb.Value;
            int key = ppe < 0 ? -1 : Math.Min(ppe, 4);
            return data != null && data.TryGetValue(key, out var c) ? c : BuiltInPpeRgb(ppe);
        }

        /// <summary>The data colours when they all agree with the built-in ones, else null
        /// (the built-in colours stand and the difference is logged once).</summary>
        private static Dictionary<int, (byte r, byte g, byte b)> LoadDataRgb()
        {
            try
            {
                string path = StingToolsApp.FindDataFile("STING_ARC_FLASH_PPE.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var arr = JObject.Parse(File.ReadAllText(path))["ppeCategories"] as JArray;
                if (arr == null) return null;
                var map = new Dictionary<int, (byte r, byte g, byte b)>();
                var diffs = new List<string>();
                foreach (var row in arr.OfType<JObject>())
                {
                    if (row["cat"]?.Type != JTokenType.Integer) continue;
                    int cat = row["cat"].Value<int>();
                    string hex = (row["colour"]?.ToString() ?? "").Trim().TrimStart('#');
                    if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out int rgb))
                    { diffs.Add($"cat {cat}: unreadable colour '{row["colour"]}'"); continue; }
                    var c = ((byte)(rgb >> 16), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
                    var bi = BuiltInPpeRgb(cat);
                    if (c != bi) diffs.Add($"cat {cat}: data #{hex.ToUpperInvariant()} vs built-in #{bi.r:X2}{bi.g:X2}{bi.b:X2}");
                    map[cat] = c;
                }
                if (diffs.Count == 0) return map;
                StingLog.WarnRateLimited("ArcFlashBoundaryView.PpeColours",
                    "STING_ARC_FLASH_PPE.json ppeCategories colours differ from the built-in boundary colours (" +
                    string.Join("; ", diffs) + "); the built-in colours are used.");
            }
            catch (Exception ex) { StingLog.Warn($"ArcFlashBoundaryView PPE colours: {ex.Message}"); }
            return null;
        }

        private static double ParseDouble(string s) =>
            StingTools.Core.Electrical.InvariantNumber.ParseOr(s);
    }
}
