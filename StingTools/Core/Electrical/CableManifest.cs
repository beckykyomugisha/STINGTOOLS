// StingTools v4 MVP — Phase J cable manifest (persistent cable records).
//
// StingCable is a lightweight logical cable tied to a tray / conduit
// network by a tray-id list rather than its own Revit geometry.
// Matches MagiCAD's "cable packet" pattern: the cable is a data
// record carrying CSA, phase, core count, insulation, OD, conductor
// material — tagged via the existing STING-Wire-Tag family.
//
// Persistence: <project>/_BIM_COORD/cables.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

namespace StingTools.Core.Electrical
{
    public class StingCable
    {
        public string Guid              { get; set; } = System.Guid.NewGuid().ToString();
        public int    SequenceNumber    { get; set; }
        public string CircuitId         { get; set; } = "";
        public string PanelName         { get; set; } = "";
        public string Phase             { get; set; } = "L1";
        public int    CoreCount         { get; set; } = 3;
        public double CsaMm2            { get; set; } = 2.5;
        public double OuterDiameterMm   { get; set; }
        public string ConductorMaterial { get; set; } = "CU";
        public string InsulationType    { get; set; } = "PVC";
        public string SegregationClass  { get; set; } = "UTP";
        public string SourceEquipmentId { get; set; } = "";
        public string DestEquipmentId   { get; set; } = "";
        public List<long> RouteTrayIds  { get; set; } = new List<long>();
        public List<long> JunctionBoxIds { get; set; } = new List<long>();
        public double TotalLengthM     { get; set; }
        public double VoltageDropPct   { get; set; }

        /// <summary>Approximate weight per metre: Cu 9 kg/km/mm²
        /// (conductor + insulation), Al 3 kg/km/mm².</summary>
        public double WeightPerMetreKg =>
            (ConductorMaterial == "AL" ? 0.003 : 0.009) * CsaMm2 * CoreCount;
    }

    public class CableManifest
    {
        public string   ProjectRef   { get; set; } = "";
        public DateTime UpdatedUtc   { get; set; } = DateTime.UtcNow;
        public int      NextSequence { get; set; } = 1;
        public List<StingCable> Cables { get; set; } = new List<StingCable>();

        public static CableManifest Load(Document doc)
        {
            var path = PathFor(doc);
            if (!File.Exists(path)) return new CableManifest { ProjectRef = doc?.PathName ?? "" };
            try
            {
                var m = JsonConvert.DeserializeObject<CableManifest>(File.ReadAllText(path));
                return m ?? new CableManifest();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CableManifest.Load: {ex.Message} — starting empty");
                return new CableManifest { ProjectRef = doc?.PathName ?? "" };
            }
        }

        public void Save(Document doc)
        {
            UpdatedUtc = DateTime.UtcNow;
            var path = PathFor(doc);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            { StingLog.Warn($"CableManifest.Save: {ex.Message}"); }
        }

        public StingCable Add(StingCable c)
        {
            if (c == null) return null;
            c.SequenceNumber = NextSequence++;
            Cables.Add(c);
            return c;
        }

        public static string PathFor(Document doc)
        {
            var dir = Path.GetDirectoryName(doc?.PathName ?? "") ?? Path.GetTempPath();
            return Path.Combine(dir, "_BIM_COORD", "cables.json");
        }
    }
}
