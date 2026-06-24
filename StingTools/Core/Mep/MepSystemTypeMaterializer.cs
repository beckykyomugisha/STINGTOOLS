// StingTools — MEP System Type Materializer (Phase A).
//
// Turns MepSystemTypeRules (loaded from STING_MEP_SYSTEM_TYPES.json + project
// override) into live Revit system types:
//   duct -> MechanicalSystemType.Create(doc, classification, name)
//   pipe -> PipingSystemType.Create(doc, classification, name)
// then stamps Abbreviation + graphic overrides (LineColor / LineWeight /
// LinePatternId / MaterialId).
//
// Idempotent: matches existing types by name (never duplicates). New types are
// always graphically stamped; existing types are only restamped when the caller
// passes overwriteGraphics = true (so a project's hand-tuned colours survive a
// re-run unless the user explicitly asks to re-apply the baseline).
//
// CALLER OWNS THE ACTIVE TRANSACTION — same contract as AecFilterFactory.FindOrCreate.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Mep
{
    public enum MepTypeAction { Created, Updated, Skipped, Failed }

    public sealed class MepSystemTypeRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string Classification { get; set; } = "";
        public MepTypeAction Action { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class MepSystemTypeResult
    {
        public List<MepSystemTypeRow> Rows { get; } = new List<MepSystemTypeRow>();
        public List<string> Warnings { get; } = new List<string>();

        public int Created => Rows.Count(r => r.Action == MepTypeAction.Created);
        public int Updated => Rows.Count(r => r.Action == MepTypeAction.Updated);
        public int Skipped => Rows.Count(r => r.Action == MepTypeAction.Skipped);
        public int Failed  => Rows.Count(r => r.Action == MepTypeAction.Failed);
    }

    public static class MepSystemTypeMaterializer
    {
        /// <summary>
        /// Create/update every enabled definition. Requires an open transaction.
        /// </summary>
        public static MepSystemTypeResult Materialize(
            Document doc, MepSystemTypeRules rules, bool overwriteGraphics)
        {
            var result = new MepSystemTypeResult();
            if (doc == null || rules == null) { result.Warnings.Add("No document / rules."); return result; }

            // Index existing types once.
            var mechByName = new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var pipeByName = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var def in rules.Enabled)
            {
                var row = new MepSystemTypeRow
                {
                    Id = def.Id, Name = def.Name, Discipline = def.Discipline,
                    Classification = def.Classification
                };
                result.Rows.Add(row);

                if (string.IsNullOrWhiteSpace(def.Name) || (!def.IsDuct && !def.IsPipe))
                {
                    row.Action = MepTypeAction.Skipped;
                    row.Note = "missing name or unknown discipline (expect 'duct' or 'pipe')";
                    continue;
                }

                if (!Enum.TryParse<MEPSystemClassification>(def.Classification, true, out var cls))
                {
                    row.Action = MepTypeAction.Skipped;
                    row.Note = $"classification '{def.Classification}' not valid in this Revit version";
                    result.Warnings.Add($"{def.Name}: classification '{def.Classification}' not recognised — skipped.");
                    continue;
                }

                try
                {
                    MEPSystemType st;
                    bool created = false;

                    if (def.IsDuct)
                    {
                        if (!mechByName.TryGetValue(def.Name, out var mt))
                        {
                            mt = MechanicalSystemType.Create(doc, cls, def.Name);
                            mechByName[def.Name] = mt;
                            created = true;
                        }
                        st = mt;
                    }
                    else
                    {
                        if (!pipeByName.TryGetValue(def.Name, out var pt))
                        {
                            pt = PipingSystemType.Create(doc, cls, def.Name);
                            pipeByName[def.Name] = pt;
                            created = true;
                        }
                        st = pt;
                    }

                    bool stampGraphics = created || overwriteGraphics;
                    int applied = ApplyGraphics(doc, st, def, stampGraphics, result.Warnings);

                    // Untouched existing types report as Skipped (not Updated) so the
                    // counter reflects reality — "updated" only when graphics were re-applied.
                    row.Action = created ? MepTypeAction.Created
                               : stampGraphics ? MepTypeAction.Updated
                               : MepTypeAction.Skipped;
                    row.Note = created
                        ? $"created ({applied} graphic prop(s) set)"
                        : (stampGraphics ? $"updated ({applied} graphic prop(s) re-applied)"
                                         : "exists — graphics left untouched (no overwrite)");
                }
                catch (Exception ex)
                {
                    row.Action = MepTypeAction.Failed;
                    row.Note = ex.Message;
                    result.Warnings.Add($"{def.Name}: {ex.Message}");
                    StingLog.Warn($"MepSystemTypeMaterializer: '{def.Name}' failed — {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Stamp Abbreviation + graphic overrides. Each property is set
        /// independently so one read-only/edge-case property never aborts the rest.
        /// Returns the count of properties successfully applied.
        /// </summary>
        private static int ApplyGraphics(
            Document doc, MEPSystemType st, MepSystemTypeDef def, bool stamp, List<string> warnings)
        {
            if (st == null || !stamp) return 0;
            int n = 0;

            if (!string.IsNullOrWhiteSpace(def.Abbreviation))
                if (TrySet(() => st.Abbreviation = def.Abbreviation, def.Name, "Abbreviation", warnings)) n++;

            if (def.LineColor != null && def.LineColor.Length >= 3)
            {
                var c = new Color(Clamp(def.LineColor[0]), Clamp(def.LineColor[1]), Clamp(def.LineColor[2]));
                if (TrySet(() => st.LineColor = c, def.Name, "LineColor", warnings)) n++;
            }

            if (def.LineWeight >= 1 && def.LineWeight <= 16)
                if (TrySet(() => st.LineWeight = def.LineWeight, def.Name, "LineWeight", warnings)) n++;

            if (!string.IsNullOrWhiteSpace(def.LinePattern))
            {
                var pid = ResolveLinePattern(doc, def.LinePattern);
                if (pid != null && pid != ElementId.InvalidElementId)
                {
                    if (TrySet(() => st.LinePatternId = pid, def.Name, "LinePatternId", warnings)) n++;
                }
                else warnings?.Add($"{def.Name}: line pattern '{def.LinePattern}' not found — left default.");
            }

            if (!string.IsNullOrWhiteSpace(def.Material))
            {
                var mid = ResolveMaterial(doc, def.Material);
                if (mid != null && mid != ElementId.InvalidElementId)
                {
                    if (TrySet(() => st.MaterialId = mid, def.Name, "MaterialId", warnings)) n++;
                }
                else warnings?.Add($"{def.Name}: material '{def.Material}' not found — left default.");
            }

            return n;
        }

        private static bool TrySet(Action setter, string typeName, string prop, List<string> warnings)
        {
            try { setter(); return true; }
            catch (Exception ex)
            {
                warnings?.Add($"{typeName}: could not set {prop} ({ex.Message}).");
                return false;
            }
        }

        private static byte Clamp(int v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

        private static ElementId ResolveLinePattern(Document doc, string name)
        {
            if (string.Equals(name, "Solid", StringComparison.OrdinalIgnoreCase))
            {
                try { return LinePatternElement.GetSolidPatternId(); } catch { /* fall through */ }
            }
            var lp = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return lp?.Id ?? ElementId.InvalidElementId;
        }

        private static ElementId ResolveMaterial(Document doc, string name)
        {
            var m = new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return m?.Id ?? ElementId.InvalidElementId;
        }
    }
}
