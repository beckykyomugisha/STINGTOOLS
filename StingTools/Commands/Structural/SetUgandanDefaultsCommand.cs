// SetUgandanDefaultsCommand.cs — Phase 189
//
// Lets the user pick a Uganda region and writes the corresponding
// load defaults (wind / seismic / soil bearing / rainfall / live load)
// onto ProjectInformation in one transaction. The walkers then read
// these via ProjectLoadCombinationEngine.ForProject.
//
// Tag: "StrSetUgandanDefaults" — wired in StingCommandHandler.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;       // StingListPicker
using StingTools.UI;

namespace StingTools.Commands.Structural
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetUgandanDefaultsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }

                // Picker — every loaded region.
                var profiles = UgandaRegionalDefaults.All;
                if (profiles == null || profiles.Count == 0)
                {
                    TaskDialog.Show("STING Uganda Defaults",
                        "No regional profiles loaded. Check that STING_UGANDA_REGIONAL_LOADS.json is present in the data folder.");
                    return Result.Cancelled;
                }

                var items = profiles.Select(p => new StingListPicker.ListItem
                {
                    Label  = p.Label,
                    Detail = $"vb,0 {p.WindBasicMps:F0} m/s · agR {p.SeismicAgrG:F2}g · soil {p.SoilBearingKpa:F0} kPa · " +
                             $"rain {p.RainIntensityMmh:F0} mm/h · {p.SoilClass}"
                }).ToList();

                var picked = StingListPicker.Show(
                    "STING — Uganda regional load defaults",
                    "Pick the project region. Values match Uganda NBC + EC1 / EC8 regional hazard. " +
                    "ProjectLoadCombinationEngine will pick these up automatically. Per-project tweaks " +
                    "can override the regional baseline by editing the project parameters directly.",
                    items);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                var pickedLabel = picked[0].Label;
                var selected = profiles.FirstOrDefault(p => p.Label == pickedLabel);
                if (selected == null) return Result.Cancelled;

                var pi = ctx.Doc.ProjectInformation;
                if (pi == null) { message = "ProjectInformation not available."; return Result.Failed; }

                int written = 0;
                using (var tx = new Transaction(ctx.Doc, $"STING Uganda Defaults — {selected.Id}"))
                {
                    tx.Start();
                    // Tier 189 fields
                    if (ParameterHelpers.SetString(pi, "PRJ_ORG_REGION_TXT",      selected.Id,                       overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_WIND_BASIC_MPS",      $"{selected.WindBasicMps:F0}",     overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_SEISMIC_AGR",         $"{selected.SeismicAgrG:F3}",      overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_SOIL_BEARING_KPA",    $"{selected.SoilBearingKpa:F0}",   overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_RAIN_INTENSITY_MMH",  $"{selected.RainIntensityMmh:F0}", overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "BLE_LIVE_LOAD_KPA",       $"{selected.LiveLoadKpa:F2}",      overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_AREA_LOAD_KN_M2",     $"{selected.DeadLoadKpa:F2}",      overwrite: true)) written++;
                    // Phase 190 EC1 + EC8 completeness fields
                    if (ParameterHelpers.SetString(pi, "STR_GROUND_TYPE_TXT",     selected.GroundType,               overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_Q_FACTOR_NR",         $"{selected.QFactor:F2}",          overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_IMPORTANCE_CLASS_TXT",selected.ImportanceClass,          overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_WIND_TERRAIN_CAT_TXT",selected.WindTerrainCat,           overwrite: true)) written++;
                    if (ParameterHelpers.SetString(pi, "STR_WIND_DIRECTIONAL_NR", $"{selected.WindCdir:F2}",         overwrite: true)) written++;
                    // Black-cotton: YESNO param stored as Integer 0/1 in Revit
                    try
                    {
                        var bp = pi.LookupParameter("STR_BLACK_COTTON_RISK_BOOL");
                        if (bp != null && !bp.IsReadOnly && bp.StorageType == StorageType.Integer)
                        { bp.Set(selected.BlackCottonRisk ? 1 : 0); written++; }
                    }
                    catch (Exception exB) { StingLog.Warn($"Black-cotton write: {exB.Message}"); }
                    tx.Commit();
                }

                var panel = StingResultPanel.Create($"Uganda defaults — {selected.Label}");
                panel.SetSubtitle("Loaded into ProjectInformation; pick this up via ProjectLoadCombinationEngine.");
                panel.AddSection("APPLIED")
                     .Metric("Region",                     selected.Id)
                     .Metric("Wind basic vb,0 (m/s)",      $"{selected.WindBasicMps:F0}")
                     .Metric("Wind terrain category",      selected.WindTerrainCat)
                     .Metric("Wind cdir",                  $"{selected.WindCdir:F2}")
                     .Metric("Seismic agR / g",            $"{selected.SeismicAgrG:F2}")
                     .Metric("EC8 ground type",            selected.GroundType)
                     .Metric("EC8 behaviour factor q",     $"{selected.QFactor:F2}")
                     .Metric("EC8 importance class",       selected.ImportanceClass)
                     .Metric("Soil bearing (kPa)",         $"{selected.SoilBearingKpa:F0}")
                     .Metric("Black-cotton risk",          selected.BlackCottonRisk ? "YES — verify on site" : "no")
                     .Metric("Rain intensity (mm/h)",      $"{selected.RainIntensityMmh:F0}")
                     .Metric("Live load (kPa)",            $"{selected.LiveLoadKpa:F2}")
                     .Metric("Dead load (kPa)",            $"{selected.DeadLoadKpa:F2}")
                     .Metric("Soil class",                 selected.SoilClass)
                     .Metric("Params written",             written.ToString());
                panel.AddSection("NOTES").Text(selected.Notes);
                panel.AddSection("⚠ DO NOT USE AS FINAL VALUES")
                     .Text("These are conservative engineering defaults, NOT verified extracts from Uganda NBC, NEMA hazard maps, or the Department of Meteorology IDF curves.")
                     .Text("Every project requires:")
                     .Text("  • Site investigation per BS 5930 — soil bearing depends on geology, depth, and site-specific tests, not the regional default.")
                     .Text("  • Approved Uganda NEMA earthquake hazard map values — agR here is unstated return period.")
                     .Text("  • Local met-office IDF curves — rainfall here is approximate 5-yr return.")
                     .Text("  • EC8 ground type (A-E) and q-factor — not captured by this profile.")
                     .Text("  • Wind terrain category and direction factor — not captured by this profile.")
                     .Text("  • A structural engineer's sign-off on every value before design.");
                panel.AddSection("DOWNSTREAM EFFECTS")
                     .Text("• Structural Auto-Size + Apply uses STR_SOIL_BEARING_KPA (project-level) when sizing foundations.")
                     .Text("• Frame / Punching / RC / Wind walkers read all four load fields via ProjectLoadCombinationEngine.")
                     .Text("• RCDesignOrchestrator also reads PlumbingSystemConfig.BuildingType so live load tracks occupancy (Office 2.5 kPa, Healthcare 2.5, Education 3.0, Retail 4.0, Industrial 5.0).")
                     .Text("• Plumbing storm sizing (PlumbRoofDrainage / PlumbSuDS) reads STR_RAIN_INTENSITY_MMH.")
                     .Text("• Override individual values by editing the project params directly; explicit overrides win over the regional baseline.");
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SetUgandanDefaultsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
