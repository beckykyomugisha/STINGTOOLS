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
using StingTools.Core;

namespace StingTools.Core.Electrical
{
    public class StingCable
    {
        public string Guid              { get; set; } = System.Guid.NewGuid().ToString();
        public int    SequenceNumber    { get; set; }
        /// <summary>
        /// Display reference. AddCableCommand writes "&lt;source&gt;-&lt;destination id&gt;"
        /// (see CableCircuitIdentity.BuildLegacyCircuitId); older/hand-written
        /// manifests may hold a circuit number. NOT a unique key on its own.
        /// </summary>
        public string CircuitId         { get; set; } = "";
        /// <summary>
        /// ElementId of the Revit ElectricalSystem this cable belongs to; 0 when
        /// not yet resolved. Authoritative when set — CableCircuitResolver
        /// falls back to source/destination and panel + circuit number, so
        /// manifests written before this field existed still resolve.
        /// </summary>
        public long   CircuitElementId  { get; set; }
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

        /// <summary>Where this instance was loaded from (not serialised).</summary>
        [JsonIgnore] public string SourcePath { get; private set; } = "";
        /// <summary>True when a manifest file existed on disk at load time.</summary>
        [JsonIgnore] public bool FileExisted { get; private set; }
        /// <summary>Non-empty when the file existed but could not be read/parsed.</summary>
        [JsonIgnore] public string LoadError { get; private set; } = "";

        /// <summary>
        /// Never returns null: a missing or unreadable file yields an EMPTY
        /// manifest. Callers must not read "non-null" as "cables on record" —
        /// check <see cref="DescribeEmpty"/> / Cables.Count, and
        /// <see cref="LoadError"/> before saving over an unreadable file.
        /// </summary>
        public static CableManifest Load(Document doc)
        {
            var path = PathFor(doc);
            if (!File.Exists(path))
                return new CableManifest { ProjectRef = doc?.PathName ?? "", SourcePath = path };
            try
            {
                var m = JsonConvert.DeserializeObject<CableManifest>(File.ReadAllText(path))
                        ?? new CableManifest();
                if (m.Cables == null) m.Cables = new List<StingCable>();
                m.SourcePath = path;
                m.FileExisted = true;
                return m;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CableManifest.Load: {ex.Message} — starting empty");
                return new CableManifest
                {
                    ProjectRef = doc?.PathName ?? "", SourcePath = path,
                    FileExisted = true, LoadError = ex.Message,
                };
            }
        }

        /// <summary>
        /// One-line honest state of the manifest, or null when it holds at
        /// least one cable. Use to explain why a manifest-driven command has
        /// nothing to work with.
        /// </summary>
        public string DescribeEmpty()
        {
            if (!string.IsNullOrEmpty(LoadError))
                return $"The cable manifest at {SourcePath} could not be read ({LoadError}). " +
                       "Fix or remove the file — nothing was evaluated.";
            if (!FileExisted)
                return $"No cable manifest exists yet ({SourcePath}). Add cables first (Add Cable / Cable Sizer).";
            if (Cables == null || Cables.Count == 0)
                return $"The cable manifest at {SourcePath} contains no cables. Add cables first (Add Cable / Cable Sizer).";
            return null;
        }

        public void Save(Document doc)
        {
            UpdatedUtc = DateTime.UtcNow;
            var path = PathFor(doc);
            // Loading an unreadable file yields an empty manifest; saving that
            // would silently replace every recorded cable with nothing.
            if (!string.IsNullOrEmpty(LoadError))
            {
                StingLog.Warn($"CableManifest.Save refused: {path} was unreadable at load ({LoadError}); " +
                              "not overwriting it with an empty manifest.");
                return;
            }
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
            return StingPaths.MetaFile(doc, "_BIM_COORD", "cables.json");
        }
    }
}
