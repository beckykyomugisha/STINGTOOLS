// StingTools — Matrix DIALux feedback (M6): update the grid's lighting counts from a
// DIALux (or ElumTools/Relux) IFC result, previewed as a diff before applying.
//
// Reuses the existing round-trip parser (StingTools.IfcResults.IfcSimpleParser) that
// backs IfcResultsImportCommand. DIALux evo exports IFC4 with per-space result PSets;
// we read a luminaire-count property per IfcSpace (common aliases), match the space to
// a matrix room-type by name, and diff it against the grid's current lighting-column
// total. The user accepts per-room-type or all; applying sets the primary lighting
// column's count to the DIALux quantity.
//
// Limitation (honest): IfcSimpleParser exposes per-space + per-fixture identity and
// numeric PSets but not the space->fixture containment relation or fixture positions,
// so the DIALux export MUST carry a per-space luminaire-count property. Rooms without
// one are reported as "no count in IFC" rather than guessed.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.IfcResults;

namespace StingTools.Core.Placement.Matrix
{
    public sealed class DialuxRoomDiff
    {
        public string RoomTypeKey = "";
        public string IfcSpaceName = "";
        public int CurrentCount;
        public int DialuxCount;
        public double IlluminanceLux;     // informational, when present
        public bool HasCount;
        public int Delta => DialuxCount - CurrentCount;
    }

    public sealed class DialuxReadResult
    {
        public bool Ok;
        public List<DialuxRoomDiff> Diffs = new List<DialuxRoomDiff>();
        public List<string> Messages = new List<string>();
    }

    public static class MatrixDialux
    {
        // Per-space luminaire-count property aliases seen across DIALux / ElumTools / Relux IFC.
        private static readonly string[] CountAliases =
        {
            "NumberOfLuminaires", "LuminaireCount", "NumberOfFittings", "Luminaires",
            "FittingCount", "Quantity", "Count", "NoOfLuminaires"
        };
        private static readonly string[] LuxAliases =
        {
            "Illuminance", "AverageIlluminance", "Eav", "Em", "MaintainedIlluminance", "AvgIlluminance"
        };

        /// <summary>Parse a DIALux IFC and build the lighting diff against the current matrix.</summary>
        public static DialuxReadResult Read(Document doc, string ifcPath, MatrixDocument matrix, MatrixScanResult scan)
        {
            var res = new DialuxReadResult();
            if (string.IsNullOrEmpty(ifcPath) || matrix == null || scan == null)
            { res.Messages.Add("No file / matrix."); return res; }

            IfcSimpleParser parsed;
            try { parsed = IfcSimpleParser.ParseFile(ifcPath); }
            catch (Exception ex) { res.Messages.Add($"Could not parse IFC: {ex.Message}"); return res; }
            if (parsed?.Spaces == null || parsed.Spaces.Count == 0)
            { res.Messages.Add("No IfcSpace entries in the file."); return res; }

            var lightingCols = (matrix.Columns ?? new List<MatrixColumnDef>())
                .Where(c => MatrixDefaults.LoadType(doc, c.Category) == "lighting").Select(c => c.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var typeKeys = scan.Types.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            int noCount = 0;
            foreach (var sp in parsed.Spaces)
            {
                string key = MatchType(sp.Name, typeKeys);
                if (key == null) continue;
                var tc = matrix.Type(key);
                int current = tc == null ? 0 : lightingCols.Sum(cid => tc.Cells != null && tc.Cells.TryGetValue(cid, out var v) ? v : 0);

                bool hasCount = TryExtract(sp.Numerics, CountAliases, out double cnt);
                if (!hasCount) noCount++;
                TryExtract(sp.Numerics, LuxAliases, out double lux);

                res.Diffs.Add(new DialuxRoomDiff
                {
                    RoomTypeKey = key, IfcSpaceName = sp.Name ?? "",
                    CurrentCount = current, DialuxCount = hasCount ? (int)Math.Round(cnt) : current,
                    IlluminanceLux = lux, HasCount = hasCount
                });
            }

            // Collapse duplicate room-types (DIALux exports per space; matrix is per type) —
            // keep the max DIALux count per type so a type reflects its worst-case space.
            res.Diffs = res.Diffs
                .GroupBy(d => d.RoomTypeKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(d => d.DialuxCount).First())
                .OrderBy(d => d.RoomTypeKey, StringComparer.OrdinalIgnoreCase).ToList();

            res.Ok = res.Diffs.Any(d => d.HasCount);
            res.Messages.Add($"Read {parsed.Spaces.Count} space(s); matched {res.Diffs.Count} room-type(s).");
            if (lightingCols.Count == 0)
                res.Messages.Add("No lighting column in the matrix — add a Lighting Fixtures column first.");
            if (noCount > 0)
                res.Messages.Add($"{noCount} space(s) had no luminaire-count property — DIALux must export a per-space count (e.g. NumberOfLuminaires) for those.");
            return res;
        }

        /// <summary>Apply accepted diffs: set the primary lighting column's count for each type to
        /// the DIALux quantity. Returns the number of room-types updated.</summary>
        public static int Apply(Document doc, MatrixDocument matrix, IEnumerable<DialuxRoomDiff> accepted)
        {
            int updated = 0;
            var lightingCols = (matrix.Columns ?? new List<MatrixColumnDef>())
                .Where(c => MatrixDefaults.LoadType(doc, c.Category) == "lighting").ToList();
            if (lightingCols.Count == 0) return 0;

            foreach (var d in accepted ?? Enumerable.Empty<DialuxRoomDiff>())
            {
                if (!d.HasCount) continue;
                var tc = matrix.Type(d.RoomTypeKey);
                if (tc == null) { tc = new MatrixTypeCounts { Key = d.RoomTypeKey }; matrix.RoomTypes.Add(tc); }
                // Choose the lighting column that currently carries the most for this type, else the first.
                var primary = lightingCols
                    .OrderByDescending(c => tc.Cells != null && tc.Cells.TryGetValue(c.Id, out var v) ? v : 0)
                    .First();
                // Zero the other lighting columns so the type total equals the DIALux count.
                foreach (var c in lightingCols) tc.Cells[c.Id] = 0;
                tc.Cells[primary.Id] = d.DialuxCount;
                updated++;
            }
            return updated;
        }

        private static bool TryExtract(Dictionary<string, double> numerics, string[] aliases, out double value)
        {
            value = 0;
            if (numerics == null) return false;
            foreach (var a in aliases)
            {
                var hit = numerics.FirstOrDefault(kv => kv.Key.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrEmpty(hit.Key)) { value = hit.Value; return true; }
            }
            return false;
        }

        private static string MatchType(string spaceName, HashSet<string> typeKeys)
        {
            if (string.IsNullOrWhiteSpace(spaceName)) return null;
            var direct = typeKeys.FirstOrDefault(k => string.Equals(k, spaceName, StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct;
            string norm = MatrixRoomScanner.NormaliseTypeKey(spaceName);
            return typeKeys.FirstOrDefault(k => string.Equals(k, norm, StringComparison.OrdinalIgnoreCase))
                ?? typeKeys.FirstOrDefault(k => norm.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0
                                             || k.IndexOf(norm, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
