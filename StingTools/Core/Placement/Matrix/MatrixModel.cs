// StingTools — Matrix Place model + persistence store.
//
// "Matrix Place" is the reverse of rule-based placement: the user declares, in an
// Excel-like grid, WHAT + HOW MANY elements go in WHICH rooms; STING places them,
// and power/load is calculated afterward (M7). The grid is the control surface;
// placement/hosting/height/tagging/circuiting all REUSE the existing engines
// (FixturePlacementEngine, SeedEnsurer, PlacementHostPreflight, CategoryToSeedRegistry,
// CategoryHeightDefaults, MepCircuitBuilder).
//
// This file is the pure data layer:
//   - MatrixColumnDef   : one element-type column (category -> seed, variant, anchor,
//                         mounting height, auto-grid flag).
//   - MatrixTypeCounts  : per-room-type cell counts + per-instance-room overrides.
//   - MatrixDocument    : the whole persisted matrix + idempotency ledger.
//   - MatrixStore       : load/save to <project>/_BIM_COORD/placement_matrix.json,
//                         mirroring the CategoryToSeedRegistry override-path pattern.
//
// The persisted JSON is re-runnable, idempotent and versioned. Placements are keyed
// by room UniqueId -> column id -> the instance UniqueIds the matrix placed, so a
// re-run skips rooms already populated for that (room, column) unless replace-mode
// deletes+replaces them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Placement.Matrix
{
    /// <summary>One element-type column in the matrix: the WHAT. Carries everything the
    /// synthetic PlacementRule needs (category -> seed via CategoryToSeedRegistry, variant,
    /// host anchor, mounting height) plus the single-vs-auto-grid toggle.</summary>
    public sealed class MatrixColumnDef
    {
        /// <summary>Stable id (e.g. "col1"); the cell dictionaries key on this.</summary>
        public string Id { get; set; } = "";
        /// <summary>STING fixture category (must be in the DWG_SYMBOL_MAP allowlist and
        /// resolvable to a seed via STING_CATEGORY_TO_SEED_MAP.json).</summary>
        public string Category { get; set; } = "";
        /// <summary>Type-variant hint (STING seeds name each type after its variant), else "".</summary>
        public string Variant { get; set; } = "";
        /// <summary>Host anchor: CEILING_CENTRE / LIGHTING_GRID / WALL_MIDPOINT / ROOM_CENTRE / ...
        /// Defaults per category via MatrixDefaults.DefaultAnchor.</summary>
        public string Anchor { get; set; } = "ROOM_CENTRE";
        /// <summary>Mounting height in mm above FFL (or below ceiling for overhead anchors).</summary>
        public double MountingHeightMm { get; set; } = 0.0;
        /// <summary>Named height standard key (STING_HEIGHT_STANDARDS.json), or "".</summary>
        public string HeightStandard { get; set; } = "";
        /// <summary>When true a cell count &gt; 1 is distributed as an even grid (ceiling) or spaced
        /// row (wall); when false all N are placed but without the even-grid seeding.</summary>
        public bool AutoGrid { get; set; } = true;
        /// <summary>Optional per-VA override for the M7 load estimate; 0 ⇒ use the category default.</summary>
        public double LoadVaOverride { get; set; } = 0.0;
        /// <summary>Human display label for the column header (defaults to category + variant).</summary>
        public string Label { get; set; } = "";

        public string DisplayLabel()
        {
            if (!string.IsNullOrWhiteSpace(Label)) return Label;
            return string.IsNullOrWhiteSpace(Variant) ? Category : $"{Category} ({Variant})";
        }

        public MatrixColumnDef Clone() => new MatrixColumnDef
        {
            Id = Id, Category = Category, Variant = Variant, Anchor = Anchor,
            MountingHeightMm = MountingHeightMm, HeightStandard = HeightStandard,
            AutoGrid = AutoGrid, LoadVaOverride = LoadVaOverride, Label = Label
        };
    }

    /// <summary>Per-room-instance override of a room-type's counts (the expand-a-type-to-its-rooms
    /// path). Keyed by room UniqueId; only columns the user actually overrode are stored.</summary>
    public sealed class MatrixRoomOverride
    {
        public string RoomUniqueId { get; set; } = "";
        /// <summary>column id -> count.</summary>
        public Dictionary<string, int> Cells { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>Counts for one room-type (rooms grouped by name), the HOW-MANY x WHICH.</summary>
    public sealed class MatrixTypeCounts
    {
        /// <summary>Room-type key (grouped room name, e.g. "Office").</summary>
        public string Key { get; set; } = "";
        /// <summary>column id -> count applied to every member room unless overridden.</summary>
        public Dictionary<string, int> Cells { get; set; } = new Dictionary<string, int>();
        /// <summary>Per-instance-room overrides.</summary>
        public List<MatrixRoomOverride> Overrides { get; set; } = new List<MatrixRoomOverride>();

        public int CountFor(string roomUniqueId, string columnId)
        {
            var ov = Overrides?.FirstOrDefault(o =>
                string.Equals(o.RoomUniqueId, roomUniqueId, StringComparison.OrdinalIgnoreCase));
            if (ov != null && ov.Cells != null && ov.Cells.TryGetValue(columnId, out var n)) return n;
            return (Cells != null && Cells.TryGetValue(columnId, out var c)) ? c : 0;
        }
    }

    /// <summary>The whole persisted matrix plus the idempotency ledger.</summary>
    public sealed class MatrixDocument
    {
        public string Version { get; set; } = "v1";
        public string SavedUtc { get; set; } = "";
        public List<MatrixColumnDef> Columns { get; set; } = new List<MatrixColumnDef>();
        public List<MatrixTypeCounts> RoomTypes { get; set; } = new List<MatrixTypeCounts>();

        /// <summary>Idempotency ledger: room UniqueId -> column id -> the instance UniqueIds the
        /// matrix placed there. A re-run skips (room, column) pairs whose ids still resolve;
        /// replace-mode deletes them first.</summary>
        public Dictionary<string, Dictionary<string, List<string>>> Placements { get; set; }
            = new Dictionary<string, Dictionary<string, List<string>>>();

        public MatrixColumnDef Column(string id)
            => Columns?.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        public MatrixTypeCounts Type(string key)
            => RoomTypes?.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Mint the next unused "colN" id.</summary>
        public string NextColumnId()
        {
            int n = 1;
            var used = new HashSet<string>(
                (Columns ?? new List<MatrixColumnDef>()).Select(c => c.Id ?? ""),
                StringComparer.OrdinalIgnoreCase);
            while (used.Contains($"col{n}")) n++;
            return $"col{n}";
        }

        // ── Idempotency ledger helpers ───────────────────────────────────────
        public List<string> LedgerFor(string roomUid, string colId)
        {
            if (Placements == null || string.IsNullOrEmpty(roomUid)) return new List<string>();
            if (Placements.TryGetValue(roomUid, out var byCol) && byCol != null
                && byCol.TryGetValue(colId, out var ids) && ids != null) return ids;
            return new List<string>();
        }

        public void SetLedger(string roomUid, string colId, IEnumerable<string> instanceUids)
        {
            if (Placements == null) Placements = new Dictionary<string, Dictionary<string, List<string>>>();
            if (!Placements.TryGetValue(roomUid, out var byCol) || byCol == null)
            {
                byCol = new Dictionary<string, List<string>>();
                Placements[roomUid] = byCol;
            }
            byCol[colId] = (instanceUids ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public void ClearLedger(string roomUid, string colId)
        {
            if (Placements != null && Placements.TryGetValue(roomUid, out var byCol) && byCol != null)
                byCol.Remove(colId);
        }
    }

    /// <summary>Loads/saves the matrix to &lt;project&gt;/_BIM_COORD/placement_matrix.json.
    /// Follows the CategoryToSeedRegistry override-path pattern (dir of doc.PathName +
    /// "_BIM_COORD"). No corporate baseline — the matrix is inherently project-scoped.</summary>
    public static class MatrixStore
    {
        public const string FileName = "placement_matrix.json";

        /// <summary>Resolve &lt;project&gt;/_BIM_COORD/placement_matrix.json (creating _BIM_COORD on save,
        /// not here). Returns null when the document is unsaved (no PathName).</summary>
        public static string ResolvePath(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) return null;
                string baseDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(baseDir)) return null;
                return Path.Combine(baseDir, "_BIM_COORD", FileName);
            }
            catch (Exception ex) { StingLog.Warn($"MatrixStore.ResolvePath: {ex.Message}"); return null; }
        }

        /// <summary>Load the persisted matrix, or a fresh empty one when none / unsaved / unreadable.</summary>
        public static MatrixDocument Load(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new MatrixDocument();
                var m = JsonConvert.DeserializeObject<MatrixDocument>(File.ReadAllText(path));
                if (m == null) return new MatrixDocument();
                m.Columns ??= new List<MatrixColumnDef>();
                m.RoomTypes ??= new List<MatrixTypeCounts>();
                m.Placements ??= new Dictionary<string, Dictionary<string, List<string>>>();
                return m;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MatrixStore.Load: {ex.Message}");
                return new MatrixDocument();
            }
        }

        /// <summary>Persist the matrix. Returns the path written, or null on failure / unsaved doc.</summary>
        public static string Save(Autodesk.Revit.DB.Document doc, MatrixDocument matrix)
        {
            if (matrix == null) return null;
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path))
                {
                    StingLog.Warn("MatrixStore.Save: document is unsaved — cannot persist matrix.");
                    return null;
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                matrix.SavedUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(path, JsonConvert.SerializeObject(matrix, Formatting.Indented));
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MatrixStore.Save: {ex.Message}");
                return null;
            }
        }
    }
}
