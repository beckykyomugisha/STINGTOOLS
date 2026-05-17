using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Commands.Electrical.ArcFlash;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Export
{
    /// <summary>
    /// Shared aggregator that flattens the Revit electrical model + Phase
    /// 178 / 179 cached results into a single <see cref="ExportModel"/>.
    /// Phase 179 export commands (EasyPower / DIALux / ETAP) all consume
    /// this so the field set is consistent across formats.
    /// </summary>
    public class CircuitSummary
    {
        public string CircuitId { get; set; } = "";
        public string PanelName { get; set; } = "";
        public string LoadName  { get; set; } = "";
        public int    Poles     { get; set; }
        public double RatingA   { get; set; }
        public double LoadVA    { get; set; }
        public double LoadKW    { get; set; }
        public double LoadKVAR  { get; set; }
        public double VoltageV  { get; set; }
        public double LengthM   { get; set; }
        public double CsaMm2    { get; set; }
        public double VDPct     { get; set; }
        public string Phase     { get; set; } = "";
    }

    public class PanelSummary
    {
        public string PanelName { get; set; } = "";
        public double VoltageV  { get; set; }
        public int    Phases    { get; set; } = 3;
        public double FaultKa   { get; set; }
        public double LoadKW    { get; set; }
    }

    public class LightingRoom
    {
        public string RoomName { get; set; } = "";
        public double AreaM2   { get; set; }
        public double TotalW   { get; set; }
        public double LuxCalc  { get; set; }
    }

    public class CableData
    {
        public string CableId            { get; set; } = "";
        public string CircuitId          { get; set; } = "";
        public string PanelName          { get; set; } = "";
        public string DestPanel          { get; set; } = "";
        public double CsaMm2             { get; set; }
        public double OuterDiameterMm    { get; set; }
        public int    CoreCount          { get; set; }
        public string ConductorMaterial  { get; set; } = "CU";
        public string InsulationType     { get; set; } = "PVC";
        public double TotalLengthM       { get; set; }
        public double VoltageDropPct     { get; set; }
        public string Phase              { get; set; } = "";
        public double WeightPerMetreKg   { get; set; }
    }

    public class FeederSummary
    {
        public string UpstreamPanel   { get; set; } = "";
        public string DownstreamPanel { get; set; } = "";
        public double CsaMm2          { get; set; }
        public double LengthM         { get; set; }
        public double ResistanceOhm   { get; set; }  // ρ × L / A  (Cu: ρ=0.0175 Ω·mm²/m)
        public double ReactanceOhm    { get; set; }  // 0.08 mΩ/m default
        public double RatingA         { get; set; }
    }

    public class ExportModel
    {
        public string ProjectName    { get; set; } = "";
        public string ProjectNumber  { get; set; } = "";
        public DateTime ExportDate   { get; set; } = DateTime.Now;
        public List<CircuitSummary>          Circuits        { get; set; } = new();
        public List<FaultPropagationResult>  FaultResults    { get; set; } = new();
        public List<ArcFlashRow>             ArcFlashResults { get; set; } = new();
        public List<PanelSummary>            Panels          { get; set; } = new();
        public List<LightingRoom>            LightingRooms   { get; set; } = new();
        public List<CableData>               Cables          { get; set; } = new();
        public List<FeederSummary>           Feeders         { get; set; } = new();
    }

    public static class ExternalExportEngine
    {
        public static ExportModel Build(Document doc, double powerFactor = 0.85)
        {
            var m = new ExportModel
            {
                ProjectName = doc?.ProjectInformation?.Name ?? "",
                ProjectNumber = doc?.ProjectInformation?.Number ?? "",
                FaultResults = FaultCurrentCommand.LastResults ?? new List<FaultPropagationResult>(),
                ArcFlashResults = ArcFlashCommand.LastResults ?? new List<ArcFlashRow>()
            };
            if (doc == null) return m;
            try
            {
                m.Circuits = BuildCircuits(doc, powerFactor);
                m.Panels = BuildPanels(doc, m.FaultResults);
                m.LightingRooms = BuildLightingRooms(doc);
                m.Cables = BuildCables(doc);
                m.Feeders = BuildFeeders(doc, m.Panels);
            }
            catch (Exception ex) { StingLog.Warn($"ExternalExportEngine.Build: {ex.Message}"); }
            return m;
        }

        private static List<CircuitSummary> BuildCircuits(Document doc, double pf = 0.85)
        {
            var rows = new List<CircuitSummary>();
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                try
                {
                    if (sys.SystemType != ElectricalSystemType.PowerCircuit) continue;
                    double va = SafeDouble(sys, BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    double iA = SafeDouble(sys, BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM);
                    double v  = SafeDouble(sys, BuiltInParameter.RBS_ELEC_VOLTAGE);
                    double lengthFt = TrySafe(() => sys.Length);
                    string wire = SafeStr(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM);
                    string num  = SafeStr(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER);
                    double loadKW = va / 1000.0;
                    double loadKVAR = loadKW * Math.Tan(Math.Acos(pf));
                    rows.Add(new CircuitSummary
                    {
                        CircuitId  = num,
                        PanelName  = TrySafe(() => sys.PanelName) ?? "",
                        LoadName   = TrySafe(() => sys.LoadName) ?? sys.Name,
                        Poles      = TrySafe(() => sys.PolesNumber, 1),
                        RatingA    = SafeDouble(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM),
                        LoadVA     = va,
                        LoadKW     = loadKW,
                        LoadKVAR   = loadKVAR,
                        VoltageV   = v,
                        LengthM    = lengthFt * 0.3048,
                        CsaMm2     = ParseCsa(wire),
                        VDPct      = ParseDouble(ParameterHelpers.GetString(sys, ParamRegistry.ELC_CKT_VD_PCT)),
                        Phase      = ReadPhase(sys)
                    });
                }
                catch (Exception ex) { StingLog.Warn($"BuildCircuits: {ex.Message}"); }
            }
            return rows;
        }

        private static List<PanelSummary> BuildPanels(Document doc,
            List<FaultPropagationResult> faults)
        {
            var rows = new List<PanelSummary>();
            var byId = (faults ?? new List<FaultPropagationResult>())
                .Where(r => r.PanelId is ElementId)
                .ToDictionary(r => ((ElementId)r.PanelId).Value, r => r);
            foreach (var p in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                double v = 0;
                try { v = p.LookupParameter("Voltage")?.AsDouble() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (v <= 0) v = ParseDouble(ParameterHelpers.GetString(p, ParamRegistry.ELC_PNL_VOLTAGE));
                double connectedKw = SafeDouble(p, BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM) / 1000.0;
                double faultKa = byId.TryGetValue(p.Id.Value, out var f) ? f.FaultKa : 0;
                rows.Add(new PanelSummary
                {
                    PanelName = p.Name ?? "", VoltageV = v, Phases = 3,
                    FaultKa = faultKa, LoadKW = connectedKw
                });
            }
            return rows;
        }

        private static List<LightingRoom> BuildLightingRooms(Document doc)
        {
            var rows = new List<LightingRoom>();
            foreach (var room in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<SpatialElement>())
            {
                try
                {
                    double areaFt2 = room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
                    if (areaFt2 <= 0) continue;
                    double areaM2 = areaFt2 * 0.0929;
                    double totalW = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_LPD_W_M2)) * areaM2;
                    double luxCalc = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX));
                    rows.Add(new LightingRoom
                    {
                        RoomName = room.Name ?? "", AreaM2 = areaM2,
                        TotalW = totalW, LuxCalc = luxCalc
                    });
                }
                catch (Exception ex) { StingLog.Warn($"BuildLightingRooms: {ex.Message}"); }
            }
            return rows;
        }

        private static List<CableData> BuildCables(Document doc)
        {
            var rows = new List<CableData>();
            try
            {
                // Use reflection to call CableManifest.Load(doc) so this file doesn't need a direct using
                var cableManifestType = Type.GetType("StingTools.Core.Electrical.CableManifest, StingTools");
                if (cableManifestType == null) return rows;
                var manifest = cableManifestType.GetMethod("Load")?.Invoke(null, new object[] { doc });
                if (manifest == null) return rows;
                var cables = manifest.GetType().GetProperty("Cables")?.GetValue(manifest) as IEnumerable;
                if (cables == null) return rows;

                foreach (var cable in cables)
                {
                    T Get<T>(string prop)
                    {
                        try { return (T)cable.GetType().GetProperty(prop)?.GetValue(cable); }
                        catch { return default; }
                    }
                    rows.Add(new CableData
                    {
                        CableId           = Get<string>("Guid") ?? "",
                        CircuitId         = Get<string>("CircuitId") ?? "",
                        PanelName         = Get<string>("PanelName") ?? "",
                        DestPanel         = Get<string>("DestPanel") ?? "",
                        CsaMm2            = Get<double>("CsaMm2"),
                        OuterDiameterMm   = Get<double>("OuterDiameterMm"),
                        CoreCount         = Get<int>("CoreCount"),
                        ConductorMaterial = Get<string>("ConductorMaterial") ?? "CU",
                        InsulationType    = Get<string>("InsulationType") ?? "PVC",
                        TotalLengthM      = Get<double>("TotalLengthM"),
                        VoltageDropPct    = Get<double>("VoltageDropPct"),
                        Phase             = Get<string>("Phase") ?? "",
                        WeightPerMetreKg  = Get<double>("WeightPerMetreKg")
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildCables: {ex.Message}"); }
            return rows;
        }

        private static List<FeederSummary> BuildFeeders(Document doc, List<PanelSummary> panels)
        {
            var rows = new List<FeederSummary>();
            try
            {
                // Build a quick lookup of known panel names for downstream detection
                var panelNames = new HashSet<string>(
                    panels.Select(p => p.PanelName),
                    StringComparer.OrdinalIgnoreCase);

                // First pass: try to derive feeders from ElectricalSystem connections
                // where the load element is itself a panel (equipment → equipment feeder)
                var panelElements = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .ToList();

                foreach (var panelEl in panelElements)
                {
                    try
                    {
                        string downstreamName = panelEl.Name ?? "";
                        if (string.IsNullOrEmpty(downstreamName)) continue;

                        // Find any ElectricalSystem whose load set includes this panel element
                        foreach (var sys in new FilteredElementCollector(doc)
                            .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
                        {
                            try
                            {
                                if (sys.SystemType != ElectricalSystemType.PowerCircuit) continue;
                                var elements = TrySafe(() => sys.Elements);
                                if (elements == null) continue;
                                bool containsPanel = false;
                                foreach (Element loadEl in elements)
                                {
                                    if (loadEl?.Id == panelEl.Id) { containsPanel = true; break; }
                                }
                                if (!containsPanel) continue;

                                string upstreamName = TrySafe(() => sys.PanelName) ?? "";
                                if (string.IsNullOrEmpty(upstreamName) || upstreamName == downstreamName) continue;

                                double lengthFt = TrySafe(() => sys.Length);
                                double lengthM  = lengthFt * 0.3048;
                                string wire     = SafeStr(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM);
                                double csa      = ParseCsa(wire);
                                double ratingA  = SafeDouble(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);

                                // Determine conductor material from cable manifest if available
                                string material = "CU";
                                double rho = material == "AL" ? 0.0285 : 0.0175;
                                double r   = csa > 0 ? rho * lengthM / csa : 0;
                                double x   = 0.00008 * lengthM; // 0.08 mΩ/m

                                rows.Add(new FeederSummary
                                {
                                    UpstreamPanel   = upstreamName,
                                    DownstreamPanel = downstreamName,
                                    CsaMm2          = csa,
                                    LengthM         = lengthM,
                                    ResistanceOhm   = r,
                                    ReactanceOhm    = x,
                                    RatingA         = ratingA
                                });
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"BuildFeeders (panel scan): {ex.Message}"); }
                }

                // Second pass: fall back to CircuitSummary rows that aren't already captured
                // and whose LoadName looks like a panel name in the known panel set.
                // (This covers cases where the Revit model has no direct equipment-to-equipment
                //  system but the panel name is reflected in the circuit's LoadName.)
                var alreadyCaptured = new HashSet<string>(
                    rows.Select(r => r.UpstreamPanel + "→" + r.DownstreamPanel),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var sys in new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
                {
                    try
                    {
                        if (sys.SystemType != ElectricalSystemType.PowerCircuit) continue;
                        string loadName     = TrySafe(() => sys.LoadName) ?? "";
                        string upstreamName = TrySafe(() => sys.PanelName) ?? "";
                        if (!panelNames.Contains(loadName)) continue;
                        if (string.IsNullOrEmpty(upstreamName) || upstreamName == loadName) continue;
                        string key = upstreamName + "→" + loadName;
                        if (alreadyCaptured.Contains(key)) continue;

                        double lengthFt = TrySafe(() => sys.Length);
                        double lengthM  = lengthFt * 0.3048;
                        string wire     = SafeStr(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM);
                        double csa      = ParseCsa(wire);
                        double ratingA  = SafeDouble(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);
                        double rho      = 0.0175; // default CU
                        double r        = csa > 0 ? rho * lengthM / csa : 0;
                        double x        = 0.00008 * lengthM;

                        rows.Add(new FeederSummary
                        {
                            UpstreamPanel   = upstreamName,
                            DownstreamPanel = loadName,
                            CsaMm2          = csa,
                            LengthM         = lengthM,
                            ResistanceOhm   = r,
                            ReactanceOhm    = x,
                            RatingA         = ratingA
                        });
                        alreadyCaptured.Add(key);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildFeeders: {ex.Message}"); }
            return rows;
        }

        private static double SafeDouble(Element e, BuiltInParameter bip)
        { try { return e.get_Parameter(bip)?.AsDouble() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; } }
        private static string SafeStr(Element e, BuiltInParameter bip)
        { try { return e.get_Parameter(bip)?.AsString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; } }
        private static T TrySafe<T>(Func<T> f, T fallback = default) { try { return f(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return fallback; } }
        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
        private static double ParseCsa(string wireSize)
        {
            if (string.IsNullOrEmpty(wireSize)) return 0;
            string digits = "";
            foreach (char ch in wireSize)
            {
                if (char.IsDigit(ch) || ch == '.') digits += ch;
                else if (digits.Length > 0) break;
            }
            return ParseDouble(digits);
        }
        private static string ReadPhase(ElectricalSystem sys)
        {
            try
            {
                var p = sys.LookupParameter("Phase")
                     ?? sys.LookupParameter("Circuit Phase")
                     ?? sys.LookupParameter("Starting Phase");
                if (p == null) return "A";
                if (p.StorageType == StorageType.Integer)
                {
                    int v = p.AsInteger();
                    return v switch { 1 => "B", 2 => "C", _ => "A" };
                }
                if (p.StorageType == StorageType.String)
                {
                    string v = (p.AsString() ?? "").Trim().ToUpperInvariant();
                    if (v.StartsWith("B")) return "B";
                    if (v.StartsWith("C")) return "C";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "A";
        }
    }
}
