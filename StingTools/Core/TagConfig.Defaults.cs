using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

// TagConfig partial: existing-tag index construction, SEQ sidecar
// persistence, and the built-in default lookup maps (Disc/Sys/Prod/Func/
// Loc/Zone). Relocated from TagConfig.cs; same partial class.

namespace StingTools.Core
{
    public static partial class TagConfig
    {
        /// <summary>
        /// Build a HashSet of all existing ASS_TAG_1_TXT values in the project.
        /// Call once before a batch tagging loop and pass to BuildAndWriteTag
        /// for collision detection. O(n) scan, O(1) per lookup thereafter.
        /// </summary>
        public static HashSet<string> BuildExistingTagIndex(Document doc)
        {
            var index = new HashSet<string>(StringComparer.Ordinal);
            // Use ElementMulticategoryFilter to skip non-taggable elements
            // (views, sheets, annotations, text notes, dimensions, etc.)
            var cats = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> collector;
            if (cats != null && cats.Length > 0)
            {
                var catFilter = new ElementMulticategoryFilter(cats);
                collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(catFilter);
            }
            else
            {
                collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            }
            foreach (Element elem in collector)
            {
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                    index.Add(tag);
            }
            StingLog.Info($"Tag index built: {index.Count} existing tags");
            return index;
        }

        /// <summary>
        /// Scan the entire project and find the highest existing sequence number
        /// for each (DISC, SYS, LVL) group. Returns a dictionary that can be passed
        /// to BuildAndWriteTag so new tags continue from existing numbering.
        /// </summary>
        /// <remarks>
        /// <b>Obsolete</b>: Use <see cref="BuildTagIndexAndCounters"/> instead.
        /// That method merges the sidecar <c>.sting_seq.json</c> counter store with
        /// the live project scan so SEQ numbering survives Revit session boundaries.
        /// Calling this method directly skips the sidecar, which can produce SEQ
        /// collisions after the project has been reopened.
        /// </remarks>
        [Obsolete("Use BuildTagIndexAndCounters(doc) — it merges sidecar counters with the live scan, preventing SEQ collisions across sessions.")]
        public static Dictionary<string, int> GetExistingSequenceCounters(Document doc)
        {
            var maxSeq = new Dictionary<string, int>();
            var known = new HashSet<string>(DiscMap.Keys);

            // Use ElementMulticategoryFilter to skip non-taggable elements
            var seqCats = SharedParamGuids.AllCategoryEnums;
            FilteredElementCollector seqCollector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            if (seqCats != null && seqCats.Length > 0)
                seqCollector.WherePasses(new ElementMulticategoryFilter(
                    new List<BuiltInCategory>(seqCats)));
            foreach (Element elem in seqCollector)
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                string seqStr = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                if (string.IsNullOrEmpty(disc)) continue;

                // Normalise empty tokens to match BuildAndWriteTag key format
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
                if (string.IsNullOrEmpty(lvl) || lvl == "XX")
                    lvl = "L00";

                // Match SeqIncludeZone/SeqIncludeLoc key format used by BuildAndWriteTag/BuildSeqKey
                string scanZone = SeqIncludeZone ? ParameterHelpers.GetString(elem, ParamRegistry.ZONE) : null;
                string scanLoc = SeqIncludeLoc ? ParameterHelpers.GetString(elem, ParamRegistry.LOC) : null;
                string key = SeqAssigner.BuildSeqKey(disc, sys, lvl, scanZone, scanLoc, SeqIncludeZone, SeqIncludeLoc);

                if (int.TryParse(seqStr, out int seqNum) && seqNum >= 0)
                {
                    if (!maxSeq.TryGetValue(key, out int curMax) || seqNum > curMax)
                        maxSeq[key] = seqNum;
                }
                else if (CurrentSeqScheme == SeqScheme.Alpha && !string.IsNullOrEmpty(seqStr))
                {
                    int alphaNum = FromAlpha(seqStr);
                    if (alphaNum > 0 && (!maxSeq.TryGetValue(key, out int curAlphaMax) || alphaNum > curAlphaMax))
                        maxSeq[key] = alphaNum;
                }
            }

            return maxSeq;
        }

        /// <summary>
        /// Combined single-pass scan: builds both the tag index and sequence counters
        /// in one iteration over all project elements. Use this instead of calling
        /// BuildExistingTagIndex + GetExistingSequenceCounters separately.
        /// </summary>
        public static (HashSet<string> tagIndex, Dictionary<string, int> seqCounters)
            BuildTagIndexAndCounters(Document doc)
        {
            var index = new HashSet<string>(StringComparer.Ordinal);
            var maxSeq = new Dictionary<string, int>();
            var known = new HashSet<string>(DiscMap.Keys);

            // Use category filter to skip non-taggable elements (views, sheets, etc.)
            var catEnums = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> elements;
            if (catEnums != null && catEnums.Length > 0)
            {
                var bicList = new List<BuiltInCategory>(catEnums);
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(bicList));
            }
            else
            {
                elements = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            }

            foreach (Element elem in elements)
            {
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                    index.Add(tag);

                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                string seqStr = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                if (string.IsNullOrEmpty(disc)) continue;

                // Normalise empty tokens to match BuildAndWriteTag key format
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
                if (string.IsNullOrEmpty(lvl) || lvl == "XX")
                    lvl = "L00";

                // Match SeqIncludeZone/SeqIncludeLoc key format used by BuildAndWriteTag/BuildSeqKey
                string scanZone = SeqIncludeZone ? ParameterHelpers.GetString(elem, ParamRegistry.ZONE) : null;
                string scanLoc = SeqIncludeLoc ? ParameterHelpers.GetString(elem, ParamRegistry.LOC) : null;
                string key = SeqAssigner.BuildSeqKey(disc, sys, lvl, scanZone, scanLoc, SeqIncludeZone, SeqIncludeLoc);

                if (int.TryParse(seqStr, out int seqNum) && seqNum >= 0)
                {
                    if (!maxSeq.TryGetValue(key, out int curMax) || seqNum > curMax)
                        maxSeq[key] = seqNum;
                }
                else if (CurrentSeqScheme == SeqScheme.Alpha && !string.IsNullOrEmpty(seqStr))
                {
                    int alphaNum = FromAlpha(seqStr);
                    if (alphaNum > 0 && (!maxSeq.TryGetValue(key, out int curAlphaMax) || alphaNum > curAlphaMax))
                        maxSeq[key] = alphaNum;
                }
            }

            StingLog.Info($"Tag index built: {index.Count} existing tags, {maxSeq.Count} SEQ groups");

            // P6 / G3.1: Merge sidecar counters — take max(doc_count, sidecar_count) per key
            try
            {
                var sidecar = LoadSeqSidecar(doc);
                if (sidecar != null) MergeSeqSidecar(maxSeq, sidecar);
            }
            catch (Exception ex) { StingLog.Warn($"BuildTagIndexAndCounters sidecar merge: {ex.Message}"); }

            return (index, maxSeq);
        }

        // ── ENH-02: SEQ Sidecar JSON Persistence ────────────────────────────

        /// <summary>
        /// ENH-02: Save SEQ counters to a sidecar JSON file alongside the Revit project.
        /// File name: {ProjectFileName}_STING_SEQ.json. This provides crash-safe
        /// sequence continuity — if Revit crashes during tagging, the last known
        /// counters are preserved on disk.
        /// </summary>
        public static void SaveSeqSidecar(Document doc, Dictionary<string, int> seqCounters)
        {
            try
            {
                string sidecarPath = GetSeqSidecarPath(doc);
                if (sidecarPath == null) return;

                // CRASH-04 fix: ensure parent directory exists before writing
                string sidecarDir = System.IO.Path.GetDirectoryName(sidecarPath);
                if (!string.IsNullOrEmpty(sidecarDir) && !System.IO.Directory.Exists(sidecarDir))
                    System.IO.Directory.CreateDirectory(sidecarDir);

                BIMManager.SidecarVersioning.WriteSidecar(sidecarPath, seqCounters, "1.0");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SaveSeqSidecar: {ex.Message}");
            }
        }

        /// <summary>
        /// ENH-02: Load SEQ counters from sidecar JSON. Returns null if file doesn't exist.
        /// Merges with document-scanned counters by taking the MAX of each key.
        /// </summary>
        public static Dictionary<string, int> LoadSeqSidecar(Document doc)
        {
            try
            {
                string sidecarPath = GetSeqSidecarPath(doc);
                if (sidecarPath == null || !System.IO.File.Exists(sidecarPath)) return null;

                // S3.6.2 — version gate before deserialise.
                StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                    sidecarPath, "planscape.sting-seq-sidecar",
                    StingTools.Core.PluginSchemaVersion.CurrentSeqSidecar);

                var (loaded, ver) = BIMManager.SidecarVersioning.ReadSidecar<Dictionary<string, int>>(sidecarPath, "1.0");
                if (loaded != null && loaded.Count > 0)
                    StingLog.Info($"SEQ sidecar loaded: {loaded.Count} groups (v{ver ?? "legacy"}) from {sidecarPath}");
                return loaded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadSeqSidecar: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ENH-02: Merge sidecar counters with document-scanned counters.
        /// Takes the MAX of each key to ensure continuity after crashes.
        /// </summary>
        public static void MergeSeqSidecar(Dictionary<string, int> target, Dictionary<string, int> sidecar)
        {
            if (sidecar == null) return;
            foreach (var kvp in sidecar)
            {
                string key = kvp.Key;

                // Key format migration: if SeqIncludeZone changed between sessions,
                // translate old-format keys to new-format keys using max-value strategy
                if (!target.TryGetValue(key, out _))
                {
                    // Try stripping zone segment: "M_Z01_HVAC_L01" → "M_HVAC_L01"
                    // Old format (no zone): DISC_SYS_LVL (3 parts)
                    // New format (with zone): DISC_ZONE_SYS_LVL (4 parts)
                    string[] parts = key.Split('_');
                    string altKey = null;
                    if (SeqIncludeZone && parts.Length == 3)
                    {
                        // H-02 FIX: Sidecar has old format (no zone), current format includes zone.
                        // Previously set altKey=null which caused old counters to be added as-is
                        // with the wrong key format, effectively losing them and restarting SEQ from 1.
                        // Now merge into default zone key so counters are preserved.
                        altKey = $"{parts[0]}_Z01_{parts[1]}_{parts[2]}";
                    }
                    else if (!SeqIncludeZone && parts.Length == 4)
                    {
                        // Sidecar has zone format, current format excludes zone — strip zone
                        altKey = $"{parts[0]}_{parts[2]}_{parts[3]}";
                    }

                    if (altKey != null && target.TryGetValue(altKey, out int altVal))
                    {
                        if (kvp.Value > altVal)
                            target[altKey] = kvp.Value;
                        continue;
                    }
                }

                if (!target.TryGetValue(key, out int tVal) || kvp.Value > tVal)
                    target[key] = kvp.Value;
            }
        }

        private static string GetSeqSidecarPath(Document doc)
        {
            if (doc == null || !doc.IsValidObject) return null;
            string projectPath = doc.PathName;
            if (string.IsNullOrEmpty(projectPath)) return null;
            // Phase 167: prefer _data folder
            try
            {
                string p = StingTools.Core.ProjectFolderEngine.GetDataPath(doc, "seq_counters.json");
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string dir = System.IO.Path.GetDirectoryName(projectPath);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(projectPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName)) return null;
            return System.IO.Path.Combine(dir, $"{fileName}_STING_SEQ.json");
        }

        private static T TryDeserialize<T>(Dictionary<string, object> data, string key)
            where T : class
        {
            if (!data.TryGetValue(key, out object val)) return null;
            try
            {
                string json = JsonConvert.SerializeObject(val);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex) { StingLog.Warn($"TagConfig deserialize '{key}': {ex.Message}"); return null; }
        }

        /// <summary>FLEX-001: Load custom validation codes from config as a HashSet.</summary>
        private static HashSet<string> LoadCustomCodes(Dictionary<string, object> data, string key)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = TryDeserialize<List<string>>(data, key);
            if (codes != null)
                foreach (var code in codes)
                    if (!string.IsNullOrWhiteSpace(code)) result.Add(code.Trim());
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        // Built-in defaults — mirror Sheet 02-TAG-FAMILY-CONFIG
        // ══════════════════════════════════════════════════════════════════

        private static Dictionary<string, string> DefaultDiscMap()
        {
            return new Dictionary<string, string>
            {
                // MEP — Mechanical
                { "Air Terminals", "M" }, { "Duct Accessories", "M" },
                { "Duct Fittings", "M" }, { "Ducts", "M" },
                { "Duct Insulation", "M" }, { "Duct Lining", "M" },
                { "Flex Ducts", "M" }, { "Mechanical Equipment", "M" },
                { "Mechanical Control Devices", "M" }, { "Mechanical Equipment Sets", "M" },
                { "Pipes", "M" }, { "Pipe Fittings", "M" },
                { "Pipe Accessories", "M" }, { "Pipe Insulation", "M" },
                { "Flex Pipes", "M" },
                // MEP — Plumbing
                { "Plumbing Fixtures", "P" }, { "Plumbing Equipment", "P" },
                // MEP — Fire Protection
                { "Sprinklers", "FP" }, { "Fire Alarm Devices", "FLS" },
                { "Fire Protection", "FP" },
                // MEP — Electrical
                { "Electrical Equipment", "E" }, { "Electrical Fixtures", "E" },
                { "Electrical Connectors", "E" },
                { "Lighting Fixtures", "E" }, { "Lighting Devices", "E" },
                { "Conduits", "E" }, { "Conduit Fittings", "E" },
                { "Cable Trays", "E" }, { "Cable Tray Fittings", "E" },
                // MEP — Low Voltage / ICT
                { "Communication Devices", "LV" },
                { "Data Devices", "LV" }, { "Nurse Call Devices", "LV" },
                { "Security Devices", "LV" }, { "Telephone Devices", "LV" },
                { "Audio Visual Devices", "LV" },
                // MEP — Fabrication
                { "MEP Fabrication Containment", "E" },
                { "MEP Fabrication Ductwork", "M" },
                { "MEP Fabrication Ductwork Stiffeners", "M" },
                { "MEP Fabrication Hangers", "M" },
                { "MEP Fabrication Pipework", "M" },
                { "MEP Ancillary", "M" },
                // MEP — Analytical
                { "Analytical Duct Segments", "M" }, { "Analytical Pipe Segments", "M" },
                // Architecture — Enclosure
                { "Doors", "A" }, { "Windows", "A" },
                { "Walls", "A" }, { "Floors", "A" },
                { "Ceilings", "A" }, { "Roofs", "A" },
                { "Curtain Panels", "A" }, { "Curtain Wall Mullions", "A" },
                { "Curtain Systems", "A" },
                { "Wall Sweeps", "A" }, { "Slab Edges", "A" },
                { "Roof Soffits", "A" }, { "Fascia", "A" }, { "Gutter", "A" },
                // Architecture — Interior
                { "Rooms", "A" }, { "Furniture", "A" },
                { "Furniture Systems", "A" }, { "Casework", "A" },
                { "Food Service Equipment", "A" }, { "Signage", "A" },
                // Architecture — Circulation
                { "Railings", "A" }, { "Handrails", "A" }, { "Top Rails", "A" },
                { "Stairs", "A" }, { "Stair Runs", "A" },
                { "Stair Landings", "A" }, { "Stair Supports", "A" },
                { "Ramps", "A" }, { "Vertical Circulation", "A" },
                // Architecture — Site/Misc
                { "Parking", "A" }, { "Site", "A" }, { "Entourage", "A" },
                { "Planting", "A" }, { "Hardscape", "A" }, { "Roads", "A" },
                { "Pads", "A" }, { "Toposolid", "A" }, { "Toposolid Links", "A" },
                { "Temporary Structures", "A" }, { "Wash", "A" },
                { "Areas", "A" }, { "Spaces", "A" },
                { "Property Lines", "A" }, { "Property Line Segments", "A" },
                // Structure
                { "Structural Columns", "S" }, { "Structural Framing", "S" },
                { "Structural Foundations", "S" }, { "Columns", "S" },
                { "Structural Stiffeners", "S" }, { "Structural Trusses", "S" },
                { "Structural Connections", "S" }, { "Structural Beam Systems", "S" },
                { "Structural Rebar", "S" }, { "Structural Rebar Couplers", "S" },
                { "Structural Area Reinforcement", "S" },
                { "Structural Path Reinforcement", "S" },
                { "Structural Fabric Reinforcement", "S" },
                // Structure — Analytical
                { "Analytical Members", "S" }, { "Analytical Nodes", "S" },
                { "Analytical Links", "S" }, { "Analytical Openings", "S" },
                { "Analytical Panels", "S" },
                // Loads
                { "Area Based Loads", "S" }, { "Area Loads", "S" },
                { "Line Loads", "S" }, { "Point Loads", "S" },
                { "Internal Area Loads", "S" }, { "Internal Line Loads", "S" },
                { "Internal Point Loads", "S" },
                // Generic
                { "Generic Models", "G" }, { "Specialty Equipment", "G" },
                { "Medical Equipment", "G" }, { "Mass", "G" },
                { "Parts", "G" }, { "Assemblies", "G" },
                { "Detail Items", "G" }, { "Model Groups", "G" },
                { "Materials", "G" }, { "Profiles", "G" },
                { "RVT Links", "G" }, { "Zones", "G" },
            };
        }

        private static Dictionary<string, List<string>> DefaultSysMap()
        {
            return new Dictionary<string, List<string>>
            {
                { "HVAC", new List<string> { "Air Terminals", "Duct Accessories", "Duct Fittings", "Ducts", "Duct Insulation", "Duct Lining", "Flex Ducts", "Mechanical Equipment", "Mechanical Control Devices", "Mechanical Equipment Sets", "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes", "MEP Fabrication Ductwork", "MEP Fabrication Ductwork Stiffeners", "MEP Fabrication Hangers", "MEP Ancillary", "Analytical Duct Segments" } },
                // Pipes default to DCW (cold water bias); runtime MEP detection overrides.
                // All pipe categories appear in every applicable system entry so
                // GetAllSysCodes() returns the full list for validation (BUG-001 fix).
                { "DCW", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes", "Plumbing Fixtures", "Plumbing Equipment", "MEP Fabrication Pipework", "Analytical Pipe Segments" } },
                { "DHW", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "HWS", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "SAN", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes", "Plumbing Fixtures", "Plumbing Equipment" } },
                { "RWD", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "GAS", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "FP", new List<string> { "Sprinklers", "Fire Protection", "Fire Alarm Devices", "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes" } },
                { "LV", new List<string> { "Electrical Equipment", "Electrical Fixtures", "Electrical Connectors", "Lighting Fixtures", "Lighting Devices", "Conduits", "Conduit Fittings", "Cable Trays", "Cable Tray Fittings", "MEP Fabrication Containment" } },
                // Lightning Protection — BS EN 62305. LPS-bearing elements may be modelled as
                // Electrical Equipment (SPDs, test clamps), Generic Models (rods, mesh, ring earth),
                // Conduits / Conduit Fittings (down-conductor channels), or Specialty Equipment.
                // Family-name discrimination in GetFamilyAwareProdCode picks the correct LPS sub-tag.
                // Phase 176 cross-discipline integration:
                //   - Structural Foundations / Rebar / Reinforcement reused as Type-B foundation earth
                //     (BS EN 62305-3 Annex E.5.3) → STR Tag #22 STING - LPS Foundation Earth Tag
                //   - Roofs / Walls / Curtain Wall / Wall Sweeps / Fascia / Gutter / Roof Soffits acting
                //     as natural air termination (BS EN 62305-3 §5.2.5) → ARCH Tag #36 STING - LPS Natural
                //     Air Termination Tag
                //   - Detail Items used for LPS schematic / installation details → GEN Tag #34 STING - LPS
                //     Generic Component Tag (catch-all)
                { "LPS", new List<string>
                    {
                        // Electrical / generic LPS modelling
                        "Electrical Equipment", "Generic Models", "Conduits", "Conduit Fittings", "Specialty Equipment", "Detail Items",
                        // Structural reuse — Type-B foundation earth (BS EN 62305-3 Annex E.5.3)
                        "Structural Foundations", "Structural Rebar", "Structural Area Reinforcement", "Structural Path Reinforcement", "Structural Fabric Reinforcement",
                        // Architectural reuse — natural air termination (BS EN 62305-3 §5.2.5)
                        "Roofs", "Walls", "Curtain Wall Mullions", "Wall Sweeps", "Fascia", "Gutter", "Roof Soffits"
                    } },
                { "FLS", new List<string> { "Fire Alarm Devices", "Fire Protection" } },
                { "COM", new List<string> { "Communication Devices", "Telephone Devices", "Audio Visual Devices" } },
                { "ICT", new List<string> { "Data Devices" } },
                { "NCL", new List<string> { "Nurse Call Devices" } },
                { "SEC", new List<string> { "Security Devices" } },
                // Architecture
                { "ARC", new List<string> { "Doors", "Windows", "Walls", "Floors", "Ceilings", "Roofs", "Rooms", "Furniture", "Furniture Systems", "Casework", "Railings", "Handrails", "Top Rails", "Stairs", "Stair Runs", "Stair Landings", "Stair Supports", "Ramps", "Vertical Circulation", "Curtain Panels", "Curtain Wall Mullions", "Curtain Systems", "Wall Sweeps", "Slab Edges", "Roof Soffits", "Fascia", "Gutter", "Food Service Equipment", "Signage", "Parking", "Site", "Entourage", "Planting", "Hardscape", "Roads", "Pads", "Toposolid", "Temporary Structures", "Areas", "Spaces" } },
                // Structure
                { "STR", new List<string> { "Structural Columns", "Structural Framing", "Structural Foundations", "Columns", "Structural Stiffeners", "Structural Trusses", "Structural Connections", "Structural Beam Systems", "Structural Rebar", "Structural Rebar Couplers", "Structural Area Reinforcement", "Structural Path Reinforcement", "Structural Fabric Reinforcement", "Analytical Members", "Analytical Nodes", "Analytical Links", "Analytical Openings", "Analytical Panels" } },
                // Generic
                { "GEN", new List<string> { "Generic Models", "Specialty Equipment", "Medical Equipment", "Mass", "Parts", "Assemblies", "Detail Items", "Model Groups", "Materials", "Profiles", "RVT Links", "Zones" } },
            };
        }

        private static Dictionary<string, string> DefaultProdMap()
        {
            return new Dictionary<string, string>
            {
                // MEP — Mechanical
                { "Air Terminals", "GRL" }, { "Duct Accessories", "DAC" },
                { "Duct Fittings", "DFT" }, { "Ducts", "DU" },
                { "Duct Insulation", "DIN" }, { "Duct Lining", "DLN" },
                { "Flex Ducts", "FDU" }, { "Mechanical Equipment", "AHU" },
                { "Mechanical Control Devices", "MCD" }, { "Mechanical Equipment Sets", "MES" },
                { "Pipes", "PP" }, { "Pipe Fittings", "PFT" },
                { "Pipe Accessories", "PAC" }, { "Pipe Insulation", "PIN" },
                { "Flex Pipes", "FPP" },
                // MEP — Plumbing
                { "Plumbing Fixtures", "FIX" }, { "Plumbing Equipment", "PEQ" },
                // MEP — Fire Protection
                { "Sprinklers", "SPR" }, { "Fire Alarm Devices", "FAD" },
                { "Fire Protection", "FPR" },
                // MEP — Electrical
                { "Electrical Equipment", "DB" }, { "Electrical Fixtures", "SKT" },
                { "Electrical Connectors", "ECN" },
                { "Lighting Fixtures", "LUM" }, { "Lighting Devices", "LDV" },
                { "Conduits", "CDT" }, { "Conduit Fittings", "CFT" },
                { "Cable Trays", "CBLT" }, { "Cable Tray Fittings", "CTF" },
                // MEP — Low Voltage / ICT
                { "Communication Devices", "COM" },
                { "Data Devices", "DAT" }, { "Nurse Call Devices", "NCL" },
                { "Security Devices", "SEC" }, { "Telephone Devices", "TEL" },
                { "Audio Visual Devices", "AVD" },
                // MEP — Fabrication
                { "MEP Fabrication Containment", "FCN" },
                { "MEP Fabrication Ductwork", "FDW" },
                { "MEP Fabrication Ductwork Stiffeners", "FDS" },
                { "MEP Fabrication Hangers", "FHG" },
                { "MEP Fabrication Pipework", "FPW" },
                { "MEP Ancillary", "ANC" },
                // MEP — Analytical
                { "Analytical Duct Segments", "ADS" }, { "Analytical Pipe Segments", "APS" },
                // Architecture — Enclosure
                { "Doors", "DR" }, { "Windows", "WIN" },
                { "Walls", "WL" }, { "Floors", "FL" },
                { "Ceilings", "CLG" }, { "Roofs", "RF" },
                { "Curtain Panels", "CPN" }, { "Curtain Wall Mullions", "MUL" },
                { "Curtain Systems", "CWS" },
                { "Wall Sweeps", "WSP" }, { "Slab Edges", "SLE" },
                { "Roof Soffits", "RSF" }, { "Fascia", "FAS" }, { "Gutter", "GTR" },
                // Architecture — Interior
                { "Rooms", "RM" }, { "Furniture", "FUR" },
                { "Furniture Systems", "FUS" }, { "Casework", "CWK" },
                { "Food Service Equipment", "FSE" }, { "Signage", "SGN" },
                // Architecture — Circulation
                { "Railings", "RLG" }, { "Handrails", "HRL" }, { "Top Rails", "TRL" },
                { "Stairs", "STR" }, { "Stair Runs", "SRN" },
                { "Stair Landings", "SLN" }, { "Stair Supports", "SSP" },
                { "Ramps", "RMP" }, { "Vertical Circulation", "VCR" },
                // Architecture — Site/Misc
                { "Parking", "PKG" }, { "Site", "STE" }, { "Entourage", "ENT" },
                { "Planting", "PLT" }, { "Hardscape", "HSC" }, { "Roads", "RD" },
                { "Pads", "PAD" }, { "Toposolid", "TPO" }, { "Toposolid Links", "TPL" },
                { "Temporary Structures", "TMP" }, { "Wash", "WSH" },
                { "Areas", "ARA" }, { "Spaces", "SPC" },
                { "Property Lines", "PRL" }, { "Property Line Segments", "PLS" },
                // Structure
                { "Structural Columns", "COL" }, { "Structural Framing", "BM" },
                { "Structural Foundations", "FDN" }, { "Columns", "COL" },
                { "Structural Stiffeners", "STF" }, { "Structural Trusses", "TRS" },
                { "Structural Connections", "SCN" }, { "Structural Beam Systems", "SBS" },
                { "Structural Rebar", "RBR" }, { "Structural Rebar Couplers", "RBC" },
                { "Structural Area Reinforcement", "SAR" },
                { "Structural Path Reinforcement", "SPT" },
                { "Structural Fabric Reinforcement", "SFR" },
                // Structure — Analytical
                { "Analytical Members", "AMB" }, { "Analytical Nodes", "AND" },
                { "Analytical Links", "ALK" }, { "Analytical Openings", "AOP" },
                { "Analytical Panels", "APN" },
                // Loads
                { "Area Based Loads", "ABL" }, { "Area Loads", "ARL" },
                { "Line Loads", "LNL" }, { "Point Loads", "PTL" },
                { "Internal Area Loads", "IAL" }, { "Internal Line Loads", "ILL" },
                { "Internal Point Loads", "IPL" },
                // Generic
                { "Generic Models", "GEN" }, { "Specialty Equipment", "SPE" },
                { "Medical Equipment", "MED" }, { "Mass", "MAS" },
                { "Parts", "PRT" }, { "Assemblies", "ASM" },
                { "Detail Items", "DTL" }, { "Model Groups", "GRP" },
                { "Materials", "MAT" }, { "Profiles", "PRF" },
                { "RVT Links", "LNK" }, { "Zones", "ZNE" },
            };
        }

        private static Dictionary<string, string> DefaultFuncMap()
        {
            return new Dictionary<string, string>
            {
                { "HVAC", "SUP" }, { "HWS", "HTG" }, { "DHW", "DHW" },
                { "DCW", "DCW" }, { "SAN", "SAN" }, { "RWD", "RWD" }, { "GAS", "GAS" },
                { "FP", "FP" }, { "LV", "PWR" }, { "FLS", "FLS" },
                // Lightning protection — default FUNC. The 6 LPS sub-functions (AT / DC / EE / BOND / SPD / TC)
                // are family-aware overrides resolved by GetFamilyAwareProdCode + ResolveLpsFunc.
                { "LPS", "LPS" },
                { "COM", "COM" }, { "ICT", "ICT" }, { "NCL", "NCL" },
                { "SEC", "SEC" },
                { "ARC", "FIT" }, { "STR", "STR" }, { "GEN", "GEN" },
            };
        }

        private static List<string> DefaultLocCodes()
        {
            return new List<string> { "BLD1", "BLD2", "BLD3", "EXT" };
        }

        private static List<string> DefaultZoneCodes()
        {
            return new List<string> { "Z01", "Z02", "Z03", "Z04" };
        }
    }
}
