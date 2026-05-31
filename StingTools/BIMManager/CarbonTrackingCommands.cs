using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  G14: Carbon Tracking Commands
    //
    //  Whole-life carbon calculator using MATERIAL_LOOKUP.csv embodied carbon data.
    //  Supports per-element, per-category, and per-discipline carbon breakdown.
    //  Aligned with RICS Whole Life Carbon Assessment (2017) and EN 15978.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: CarbonTrackingEngine ──

    internal static class CarbonTrackingEngine
    {
        // Material name → embodied carbon kgCO2e/kg
        private static Dictionary<string, double> _carbonFactors;
        private static readonly object _lock = new object();

        /// <summary>Load embodied carbon factors from MATERIAL_LOOKUP.csv.</summary>
        /// <remarks>
        /// DEAD AT RUNTIME (Z-20 finding): the shipped MATERIAL_LOOKUP.csv is
        /// long-format (Category,TypeKey,Property,Value) and opens with a "# ..."
        /// comment banner. This loader reads lines[0] (the comment) as the header,
        /// finds no EMBODIED/CARBON/KGCO2 column, hits `if (carbonCol &lt; 0) return;`
        /// and leaves _carbonFactors empty — so Tier-3 here never contributes a
        /// value; resolution falls straight through to the keyword defaults.
        /// Delivered BOQ carbon therefore flows from Tier-1 (the material's
        /// STING_EMB_CARBON_NR, populated at material-creation from the
        /// MEP_/BLE_MATERIALS.csv PROP_CARBON_KG_M3 columns) — which is why the
        /// Z-20 ICE v3.0 fix was applied there, not here. Re-wiring this to parse
        /// the long format (or making MATERIAL_LOOKUP canonical) is a separate,
        /// test-backed PR — see docs/PHASE_Z_NUMERIC_AUDIT.md §2.4–2.6, §7.1.
        /// </remarks>
        internal static void EnsureLoaded()
        {
            if (_carbonFactors != null) return;
            lock (_lock)
            {
                if (_carbonFactors != null) return;
                _carbonFactors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    string file = StingToolsApp.FindDataFile("MATERIAL_LOOKUP.csv");
                    if (string.IsNullOrEmpty(file)) return;

                    var lines = File.ReadAllLines(file);
                    if (lines.Length < 2) return;

                    // Find embodied carbon column
                    var headers = StingToolsApp.ParseCsvLine(lines[0]);
                    int nameCol = -1, carbonCol = -1;
                    for (int i = 0; i < headers.Length; i++)
                    {
                        string h = headers[i].Trim().ToUpperInvariant();
                        if (h.Contains("MATERIAL") && h.Contains("NAME")) nameCol = i;
                        else if (h.Contains("EMBODIED") && h.Contains("CARBON")) carbonCol = i;
                        else if (h.Contains("KGCO2")) carbonCol = i;
                    }
                    if (nameCol < 0) nameCol = 0;
                    if (carbonCol < 0) return;

                    for (int i = 1; i < lines.Length; i++)
                    {
                        var parts = StingToolsApp.ParseCsvLine(lines[i]);
                        if (parts.Length <= Math.Max(nameCol, carbonCol)) continue;
                        string name = parts[nameCol].Trim();
                        // InvariantCulture: CSV decimal separator is always "." regardless of
                        // the Revit user's regional settings.
                        if (double.TryParse(parts[carbonCol].Trim(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double val) && val > 0)
                            _carbonFactors[name] = val;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CarbonTracking load: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Estimate embodied carbon for a material name.
        ///
        /// N+7 — Single-source resolution chain. The legacy carbon-factor
        /// dictionary is now last in line so a MAT-curated value always wins:
        ///   1) Material element's STING_EMB_CARBON_NR parameter (live MAT
        ///      panel writes / overrides / on-create auto-fill)
        ///   2) MaterialLookupCsv corporate library (MATERIAL_LOOKUP.csv)
        ///   3) Legacy CARBON_FACTORS.csv via _carbonFactors dictionary
        ///   4) GetDefaultCarbonFactor keyword fallback (existing behaviour)
        /// </summary>
        internal static double GetCarbonFactor(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return 0;

            // Tier 1 — Material element's parameter (live edits win).
            // P-2 — Cache lookup; was a per-call collector.
            try
            {
                var doc = StingTools.UI.StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    var mat = StingTools.UI.MaterialNameCache.ResolveMaterial(doc, materialName);
                    if (mat != null)
                    {
                        var p = mat.LookupParameter("STING_EMB_CARBON_NR");
                        if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                        {
                            double v = p.AsDouble();
                            if (v > 0) return v;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("GetCarbonFactor.MatParam", $"GetCarbonFactor MAT param: {ex.Message}"); }

            // Tier 2 — Corporate library lookup
            try
            {
                double libVal = StingTools.UI.MaterialLookupCsv.GetCarbon(materialName);
                if (libVal > 0) return libVal;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("GetCarbonFactor.Lookup", $"GetCarbonFactor lookup: {ex.Message}"); }

            // Tier 3 — Legacy CARBON_FACTORS.csv dictionary
            EnsureLoaded();
            if (_carbonFactors != null)
            {
                if (_carbonFactors.TryGetValue(materialName, out double exact)) return exact;
                string lower = materialName.ToLowerInvariant();
                var match = _carbonFactors.Keys
                    .Where(k => lower.Contains(k.ToLowerInvariant()) || k.ToLowerInvariant().Contains(lower))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();
                if (match != null) return _carbonFactors[match];
            }

            // Tier 4 — Hard-coded keyword fallback
            return GetDefaultCarbonFactor(materialName);
        }

        /// <summary>Default carbon factors by material type keywords.</summary>
        private static double GetDefaultCarbonFactor(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            // Z-20 (ICE v3.0): these per-kg fallbacks are kept in lock-step with the
            // per-m³ PROP_CARBON_KG_M3 columns in MEP_/BLE_MATERIALS.csv that feed the
            // Tier-1 material param (factor ÷ density = the value below). "galvanised"
            // is checked before "steel" so galvanised sheet (ducts) gets 2.85 not 1.55.
            if (lower.Contains("galvani")) return 2.85;   // galvanised steel sheet (= 22372 kgCO₂/m³ ÷ 7850)
            if (lower.Contains("steel")) return 1.55;     // plain/structural steel (= 12090 ÷ 7850)
            if (lower.Contains("concrete") || lower.Contains("cement")) return 0.13;
            if (lower.Contains("alumin")) return 6.67;
            if (lower.Contains("timber") || lower.Contains("wood")) return 0.31;
            if (lower.Contains("glass")) return 1.44;     // ICE v3.0 general glass (= 3600 ÷ 2500)
            if (lower.Contains("brick")) return 0.24;
            if (lower.Contains("copper")) return 3.50;    // copper pipe (= 31360 ÷ 8960)
            if (lower.Contains("plastic") || lower.Contains("pvc")) return 3.10;
            if (lower.Contains("insulation")) return 1.86;
            if (lower.Contains("plaster") || lower.Contains("gypsum")) return 0.12;
            return 0;
        }

        /// <summary>Calculate carbon for all elements in the model.</summary>
        internal static CarbonResult CalculateProjectCarbon(Document doc)
        {
            EnsureLoaded();
            var result = new CarbonResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // S1.4 (N-G1): pre-filter to carbon-relevant categories via
            // AllCategoryEnums rather than scanning the whole document.
            var elements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            foreach (var el in elements)
            {
                try
                {
                    // Get element volume
                    double volumeCuFt = 0;
                    try
                    {
                        var geomOpts = new Options();
                        var geom = el.get_Geometry(geomOpts);
                        if (geom != null)
                        {
                            foreach (var gObj in geom)
                            {
                                if (gObj is Solid solid && solid.Volume > 0)
                                    volumeCuFt += solid.Volume;
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Skip elements without geometry: {ex.Message}"); }

                    if (volumeCuFt <= 0) continue;

                    double volumeM3 = volumeCuFt * 0.0283168; // cu ft to m³

                    // Get material(s)
                    var matIds = el.GetMaterialIds(false);
                    if (matIds.Count == 0) continue;

                    string catName = el.Category?.Name ?? "Other";
                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);

                    foreach (var matId in matIds)
                    {
                        var mat = doc.GetElement(matId) as Material;
                        if (mat == null) continue;

                        double grossFactor = GetCarbonFactor(mat.Name);
                        if (grossFactor <= 0 && !StingTools.BOQ.BiogenicCarbon.IsBiogenic(mat.Name)) continue;

                        // Get material volume fraction (approximate — equal split across materials)
                        double matVolume = volumeM3 / matIds.Count;

                        // Estimate density (default 2400 kg/m³ for concrete-like)
                        double density = EstimateDensity(mat.Name);
                        double mass = matVolume * density;

                        // Z-25b — WLCA fossil/biogenic split. HEADLINE = A1-A3 FOSSIL
                        // (gross upfront, sequestration excluded; RICS WLCA 2nd ed /
                        // RIBA 2030 / LETI). Biogenic is a separate ≤0 line; net is
                        // fossil + biogenic for whole-life context.
                        double fossilFactor   = StingTools.BOQ.BiogenicCarbon.FossilFactorPerKg(mat.Name, grossFactor);
                        double biogenicFactor = StingTools.BOQ.BiogenicCarbon.BiogenicFactorPerKg(mat.Name);
                        double fossilKg   = mass * fossilFactor;
                        double biogenicKg = mass * biogenicFactor;

                        result.FossilCarbonKg   += fossilKg;
                        result.BiogenicCarbonKg += biogenicKg;
                        // Breakdowns track the HEADLINE (fossil) figure.
                        result.ByCategory[catName] = result.ByCategory.GetValueOrDefault(catName) + fossilKg;
                        if (!string.IsNullOrWhiteSpace(disc))
                            result.ByDiscipline[disc] = result.ByDiscipline.GetValueOrDefault(disc) + fossilKg;
                        result.ByMaterial[mat.Name] = result.ByMaterial.GetValueOrDefault(mat.Name) + fossilKg;
                        result.ElementCount++;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CarbonCalc element: {ex.Message}"); }
            }

            sw.Stop();
            result.Duration = sw.Elapsed;

            // Calculate area-based metric
            double totalFloorArea = 0;
            try
            {
                var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().Cast<Autodesk.Revit.DB.Architecture.Room>();
                totalFloorArea = rooms.Sum(r => r.Area) * 0.092903; // sqft to m²
            }
            catch (Exception ex) { StingLog.Warn($"CarbonCalc area: {ex.Message}"); }

            result.FloorAreaM2 = totalFloorArea;
            result.CarbonPerM2 = totalFloorArea > 0 ? result.TotalCarbonKg / totalFloorArea : 0;

            return result;
        }

        private static double EstimateDensity(string materialName)
        {
            string lower = (materialName ?? "").ToLowerInvariant();
            if (lower.Contains("steel")) return 7850;
            if (lower.Contains("concrete")) return 2400;
            if (lower.Contains("alumin")) return 2700;
            if (lower.Contains("timber") || lower.Contains("wood")) return 500;
            if (lower.Contains("glass")) return 2500;
            if (lower.Contains("brick")) return 1800;
            if (lower.Contains("copper")) return 8940;
            if (lower.Contains("insulation")) return 30;
            if (lower.Contains("plaster") || lower.Contains("gypsum")) return 1000;
            return 2000; // Default
        }
    }

    // ── Data types ──

    internal class CarbonResult
    {
        // Z-25b — WLCA three-line A1-A3 reporting.
        public double FossilCarbonKg { get; set; }     // HEADLINE — gross upfront (RICS WLCA / RIBA 2030 / LETI)
        public double BiogenicCarbonKg { get; set; }   // separate informational line (≤ 0)
        public double NetCarbonKg => FossilCarbonKg + BiogenicCarbonKg;  // whole-life context

        // Legacy alias — existing consumers read TotalCarbonKg as the headline.
        // The headline is fossil (gross upfront), matching the pre-Z-25b semantics
        // (sequestration was already gated out), so no consumer breaks.
        public double TotalCarbonKg => FossilCarbonKg;

        public double FloorAreaM2 { get; set; }
        public double CarbonPerM2 { get; set; }         // fossil headline / floor area
        public int ElementCount { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, double> ByCategory { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> ByDiscipline { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> ByMaterial { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public double TotalCarbonTonnes  => TotalCarbonKg / 1000.0;
        public double FossilCarbonTonnes => FossilCarbonKg / 1000.0;
        public double BiogenicCarbonTonnes => BiogenicCarbonKg / 1000.0;
        public double NetCarbonTonnes    => NetCarbonKg / 1000.0;
    }

    #endregion

    #region ── Commands ──

    /// <summary>
    /// Calculate whole-life embodied carbon for the project using MATERIAL_LOOKUP.csv data.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CarbonCalculatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var progress = StingProgressDialog.Show("Carbon Calculator", 1);
            CarbonResult result;
            try
            {
                progress.Increment("Calculating embodied carbon...");
                result = CarbonTrackingEngine.CalculateProjectCarbon(ctx.Doc);
            }
            finally { progress.Close(); }

            var sb = new StringBuilder();
            sb.AppendLine($"Embodied Carbon Assessment (A1-A3)");
            sb.AppendLine($"─────────────────────────────────────────────\n");
            // Z-25b — WLCA three-line reporting. Headline = FOSSIL (gross upfront).
            sb.AppendLine($"  A1-A3 Fossil (headline): {result.FossilCarbonTonnes:F1} tCO₂e ({result.FossilCarbonKg:F0} kgCO₂e)");
            sb.AppendLine($"  A1-A3 Biogenic (sep.):   {result.BiogenicCarbonTonnes:F1} tCO₂e ({result.BiogenicCarbonKg:F0} kgCO₂e)");
            sb.AppendLine($"  Net (fossil + biogenic): {result.NetCarbonTonnes:F1} tCO₂e ({result.NetCarbonKg:F0} kgCO₂e)");
            sb.AppendLine($"  Floor Area:       {result.FloorAreaM2:F0} m²");
            sb.AppendLine($"  Carbon Intensity: {result.CarbonPerM2:F1} kgCO₂e/m² (fossil, upfront)");
            sb.AppendLine($"  Elements:         {result.ElementCount:N0}");
            sb.AppendLine($"  Duration:         {result.Duration.TotalSeconds:F1}s\n");
            sb.AppendLine("  Methodology: headline = A1-A3 FOSSIL (gross upfront carbon);");
            sb.AppendLine("  biogenic sequestration is reported separately and is NOT");
            sb.AppendLine("  netted into the RIBA 2030 / LETI benchmark, per RICS Whole");
            sb.AppendLine("  Life Carbon Assessment 2nd ed. (2023). Net is shown for");
            sb.AppendLine("  whole-life context only.\n");

            // RIBA 2030 benchmark comparison
            string benchmark = result.CarbonPerM2 switch
            {
                < 300 => "Excellent (below RIBA 2030 target)",
                < 500 => "Good (within LETI range)",
                < 800 => "Average (typical new build)",
                < 1200 => "High (above average)",
                _ => "Very High (significant reduction needed)"
            };
            sb.AppendLine($"  Benchmark: {benchmark}\n");

            sb.AppendLine("── By Discipline ──");
            foreach (var kvp in result.ByDiscipline.OrderByDescending(d => d.Value))
                sb.AppendLine($"  {kvp.Key,-8} {kvp.Value / 1000:F1} tCO₂e ({(result.TotalCarbonKg > 0 ? 100 * kvp.Value / result.TotalCarbonKg : 0):F0}%)");

            sb.AppendLine("\n── Top Materials ──");
            foreach (var kvp in result.ByMaterial.OrderByDescending(m => m.Value).Take(10))
                sb.AppendLine($"  {kvp.Key,-30} {kvp.Value:F0} kgCO₂e");

            sb.AppendLine("\n── Top Categories ──");
            foreach (var kvp in result.ByCategory.OrderByDescending(c => c.Value).Take(10))
                sb.AppendLine($"  {kvp.Key,-30} {kvp.Value:F0} kgCO₂e");

            TaskDialog.Show("Carbon Calculator", sb.ToString());
            StingLog.Info($"CarbonCalc: {result.TotalCarbonTonnes:F1} tCO2e, {result.CarbonPerM2:F1} kgCO2e/m2");
            return Result.Succeeded;
        }
    }

    /// <summary>Export carbon data to CSV.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CarbonExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var result = CarbonTrackingEngine.CalculateProjectCarbon(ctx.Doc);
            string path = OutputLocationHelper.GetTimestampedPath(ctx.Doc, "CarbonReport", ".csv");

            var sb = new StringBuilder();
            // Z-25b — WLCA A1-A3 three-line reporting. Headline = FOSSIL (gross
            // upfront, sequestration excluded) per RICS WLCA 2nd ed (2023) /
            // RIBA 2030 / LETI; biogenic reported separately; net for context.
            sb.AppendLine("# Methodology: A1-A3. Headline = FOSSIL (gross upfront carbon); biogenic");
            sb.AppendLine("# reported separately, NOT netted into the RIBA 2030 / LETI benchmark");
            sb.AppendLine("# (RICS Whole Life Carbon Assessment 2nd ed., 2023). Net = whole-life context.");
            sb.AppendLine("Section,Item,Value_kgCO2e,Percentage");
            sb.AppendLine($"Summary,A1A3_Fossil_Headline,{result.FossilCarbonKg:F2},100");
            sb.AppendLine($"Summary,A1A3_Biogenic_Separate,{result.BiogenicCarbonKg:F2},");
            sb.AppendLine($"Summary,Net_Fossil_Plus_Biogenic,{result.NetCarbonKg:F2},");
            sb.AppendLine($"Summary,Per_m2_Fossil,{result.CarbonPerM2:F2},");
            sb.AppendLine($"Summary,Floor_Area_m2,{result.FloorAreaM2:F2},");
            foreach (var kvp in result.ByDiscipline.OrderByDescending(d => d.Value))
                sb.AppendLine($"Discipline,{kvp.Key},{kvp.Value:F2},{(result.TotalCarbonKg > 0 ? 100 * kvp.Value / result.TotalCarbonKg : 0):F1}");
            foreach (var kvp in result.ByCategory.OrderByDescending(c => c.Value))
                sb.AppendLine($"Category,\"{kvp.Key}\",{kvp.Value:F2},{(result.TotalCarbonKg > 0 ? 100 * kvp.Value / result.TotalCarbonKg : 0):F1}");
            foreach (var kvp in result.ByMaterial.OrderByDescending(m => m.Value))
                sb.AppendLine($"Material,\"{kvp.Key}\",{kvp.Value:F2},{(result.TotalCarbonKg > 0 ? 100 * kvp.Value / result.TotalCarbonKg : 0):F1}");

            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Carbon Export", $"Exported to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion
}
