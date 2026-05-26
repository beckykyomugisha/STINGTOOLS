// StingTools — Wire profile system for the WireAnnotation feature.
//
// Mirrors ricaun's WireInConduit cable-type catalogue. Each profile
// is a named cable spec (cores, CSA, material, insulation, weight,
// rating) referenced by id from circuit configuration. The
// AnnotateCircuitCommand picks one profile per circuit and stamps
// its data onto every conduit/tray segment along the run.
//
// JSON catalogue: <data>/Electrical/STING_WIRE_PROFILES.json.
// Per-project overrides: <project>/_BIM_COORD/wire_profiles.json
// + per-circuit assignments: <project>/_BIM_COORD/circuit_wire_map.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    public class WireProfile
    {
        public string Id               { get; set; } = "";
        public string Name             { get; set; } = "";
        public string Standard         { get; set; } = "";
        public int    Cores            { get; set; } = 3;
        public double CsaMm2           { get; set; } = 2.5;
        public string ConductorMat     { get; set; } = "Cu";
        public string Insulation       { get; set; } = "PVC";
        public string Sheath           { get; set; } = "PVC";
        public string SegregationClass { get; set; } = "LV";
        public double RatedVoltageV    { get; set; } = 450;
        public double WeightKgPerKm    { get; set; } = 90;
        public double OuterDiameterMm  { get; set; } = 9.5;
        public double CurrentRatingA   { get; set; } = 24;
        public string Use              { get; set; } = "";
    }

    public class WireProfileLibrary
    {
        public int SchemaVersion        { get; set; } = 1;
        public string Description       { get; set; } = "";
        public List<WireProfile> Profiles { get; set; } = new List<WireProfile>();
    }

    public static class WireProfileRegistry
    {
        private static WireProfileLibrary _corporate;
        private static readonly object _lock = new object();

        public static List<WireProfile> ListAll(Document doc)
        {
            EnsureCorporateLoaded();
            var merged = new Dictionary<string, WireProfile>(StringComparer.OrdinalIgnoreCase);
            if (_corporate?.Profiles != null)
                foreach (var p in _corporate.Profiles) merged[p.Id] = p;

            var project = LoadProjectOverride(doc);
            if (project?.Profiles != null)
                foreach (var p in project.Profiles) merged[p.Id] = p;

            return merged.Values.OrderBy(p => p.SegregationClass)
                                .ThenBy(p => p.CsaMm2)
                                .ThenBy(p => p.Cores)
                                .ToList();
        }

        public static WireProfile Get(Document doc, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return ListAll(doc).FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static WireProfile FallbackForCircuit(ElectricalSystem sys)
        {
            var p = new WireProfile { Id = "AUTO" };
            try
            {
                p.Cores = SafePoles(sys);
                if (p.Cores <= 0) p.Cores = 3;
                var size = sys.LookupParameter("Wire Size")?.AsString() ?? "";
                p.CsaMm2 = ParseCsaFromWireSize(size);
                p.Name = $"Auto {p.Cores} × {p.CsaMm2:0.##} mm²";
            }
            catch { }
            return p;
        }

        public static void Reload()
        {
            lock (_lock) { _corporate = null; }
        }

        private static void EnsureCorporateLoaded()
        {
            lock (_lock)
            {
                if (_corporate != null) return;
                try
                {
                    var path = StingToolsApp.FindDataFile("STING_WIRE_PROFILES.json");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.Warn("WireProfileRegistry: STING_WIRE_PROFILES.json not found, using empty corporate set.");
                        _corporate = new WireProfileLibrary();
                        return;
                    }
                    _corporate = JsonConvert.DeserializeObject<WireProfileLibrary>(File.ReadAllText(path))
                              ?? new WireProfileLibrary();
                }
                catch (Exception ex)
                {
                    StingLog.Warn("WireProfileRegistry.Load: " + ex.Message);
                    _corporate = new WireProfileLibrary();
                }
            }
        }

        private static WireProfileLibrary LoadProjectOverride(Document doc)
        {
            try
            {
                var path = ProjectOverridePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<WireProfileLibrary>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn("WireProfileRegistry project override: " + ex.Message);
                return null;
            }
        }

        public static string ProjectOverridePath(Document doc)
        {
            if (doc == null) return null;
            var dir = Path.GetDirectoryName(doc.PathName ?? "");
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", "wire_profiles.json");
        }

        public static string CircuitMapPath(Document doc)
        {
            if (doc == null) return null;
            var dir = Path.GetDirectoryName(doc.PathName ?? "");
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", "circuit_wire_map.json");
        }

        private static int SafePoles(ElectricalSystem sys)
        {
            try
            {
                var p = sys.get_Parameter(BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES);
                if (p != null && p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }

        private static double ParseCsaFromWireSize(string size)
        {
            if (string.IsNullOrEmpty(size)) return 0.0;
            var digits = new string(size.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray())
                .Replace(',', '.');
            double.TryParse(digits, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double csa);
            return csa;
        }
    }

    public class CircuitWireAssignment
    {
        public string CircuitId { get; set; } = "";
        public string ProfileId { get; set; } = "";
    }

    public class CircuitWireMap
    {
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<CircuitWireAssignment> Assignments { get; set; } = new List<CircuitWireAssignment>();

        public string GetProfileId(string circuitId)
        {
            return Assignments.FirstOrDefault(a =>
                string.Equals(a.CircuitId, circuitId, StringComparison.Ordinal))?.ProfileId;
        }

        public void Set(string circuitId, string profileId)
        {
            var existing = Assignments.FirstOrDefault(a =>
                string.Equals(a.CircuitId, circuitId, StringComparison.Ordinal));
            if (existing != null) existing.ProfileId = profileId;
            else Assignments.Add(new CircuitWireAssignment { CircuitId = circuitId, ProfileId = profileId });
        }

        public static CircuitWireMap Load(Document doc)
        {
            try
            {
                var path = WireProfileRegistry.CircuitMapPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new CircuitWireMap();
                return JsonConvert.DeserializeObject<CircuitWireMap>(File.ReadAllText(path))
                    ?? new CircuitWireMap();
            }
            catch (Exception ex)
            {
                StingLog.Warn("CircuitWireMap.Load: " + ex.Message);
                return new CircuitWireMap();
            }
        }

        public void Save(Document doc)
        {
            try
            {
                UpdatedUtc = DateTime.UtcNow;
                var path = WireProfileRegistry.CircuitMapPath(doc);
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn("CircuitWireMap.Save: " + ex.Message); }
        }
    }
}
