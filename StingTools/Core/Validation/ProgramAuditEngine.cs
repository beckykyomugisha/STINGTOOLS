using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B3) — Program Audit comparator (pure logic).
    //
    // Compares the Owner's program template rows against the model's rooms and
    // produces a per-row compliance + deficiency result. Deliberately FREE of
    // any Autodesk.Revit.* and ClosedXML dependency so it can be linked into the
    // pure-logic unit-test project; the command (ProgramAuditCommand) does the
    // Excel read, the Revit room read, and the XLSX deficiency-log write, then
    // calls Compare() here.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One row of the Owner's program template.</summary>
    public class ProgramRow
    {
        public string RoomName { get; set; } = "";
        public string RoomNumber { get; set; } = "";
        public string Department { get; set; } = "";
        public string Building { get; set; } = "";   // matches LOC when present
        public double? RequiredAreaM2 { get; set; }   // normalised to m² by the caller
        public int? RequiredCount { get; set; }
        public int SourceRowIndex { get; set; }       // 1-based row in the template
    }

    /// <summary>One placed room read from the model.</summary>
    public class ModelRoomRow
    {
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
        public string Loc { get; set; } = "";
        public double AreaM2 { get; set; }
        public long ElementId { get; set; }
    }

    public enum ProgramAuditStatus
    {
        Compliant,
        OverArea,
        UnderArea,
        MissingFromModel,   // template row with no model room
        ExtraInModel        // model room not in the program
    }

    public class ProgramAuditRow
    {
        public string RoomName { get; set; } = "";
        public string RoomNumber { get; set; } = "";
        public string Building { get; set; } = "";
        public double? RequiredAreaM2 { get; set; }
        public double? ActualAreaM2 { get; set; }
        public double? DeltaPct { get; set; }
        public int? RequiredCount { get; set; }
        public int? ActualCount { get; set; }
        public ProgramAuditStatus Status { get; set; }
        public string Note { get; set; } = "";
        public long ModelElementId { get; set; }
    }

    public class ProgramAuditResult
    {
        public List<ProgramAuditRow> Rows { get; } = new List<ProgramAuditRow>();
        public double TolerancePct { get; set; }
        public int Compliant { get; set; }
        public int Over { get; set; }
        public int Under { get; set; }
        public int Missing { get; set; }
        public int Extra { get; set; }
        public int CountMismatch { get; set; }

        public string StatusName(ProgramAuditStatus s)
        {
            switch (s)
            {
                case ProgramAuditStatus.Compliant: return "COMPLIANT";
                case ProgramAuditStatus.OverArea: return "OVER";
                case ProgramAuditStatus.UnderArea: return "UNDER";
                case ProgramAuditStatus.MissingFromModel: return "MISSING";
                case ProgramAuditStatus.ExtraInModel: return "EXTRA";
                default: return s.ToString();
            }
        }
    }

    public static class ProgramAuditEngine
    {
        /// <summary>Lower-case, trimmed, punctuation- and whitespace-stripped key
        /// for forgiving name matching ("Office 1.2" ≈ "office 12").</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>
        /// Join program rows to model rooms (Number → exact name → normalised
        /// name) and compare area (± tolerance %) and per-type count. Unjoined
        /// template rows = MissingFromModel; unjoined model rooms = ExtraInModel.
        /// </summary>
        public static ProgramAuditResult Compare(
            IEnumerable<ProgramRow> program, IEnumerable<ModelRoomRow> model, double tolerancePct)
        {
            var result = new ProgramAuditResult { TolerancePct = tolerancePct };
            var programList = (program ?? Enumerable.Empty<ProgramRow>()).ToList();
            var modelList = (model ?? Enumerable.Empty<ModelRoomRow>()).ToList();

            // Indexes for the join. First-wins so a duplicate name doesn't throw.
            var byNumber = new Dictionary<string, ModelRoomRow>(StringComparer.Ordinal);
            var byName = new Dictionary<string, ModelRoomRow>(StringComparer.Ordinal);
            var byNormName = new Dictionary<string, ModelRoomRow>(StringComparer.Ordinal);
            var normNameCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in modelList)
            {
                string nNum = Normalize(m.Number);
                if (!string.IsNullOrEmpty(nNum) && !byNumber.ContainsKey(nNum)) byNumber[nNum] = m;
                string exact = (m.Name ?? "").Trim();
                if (!string.IsNullOrEmpty(exact) && !byName.ContainsKey(exact)) byName[exact] = m;
                string nName = Normalize(m.Name);
                if (!string.IsNullOrEmpty(nName))
                {
                    if (!byNormName.ContainsKey(nName)) byNormName[nName] = m;
                    normNameCount.TryGetValue(nName, out int c);
                    normNameCount[nName] = c + 1;
                }
            }

            var usedModelIds = new HashSet<long>();

            foreach (var p in programList)
            {
                ModelRoomRow match = null;
                string pNum = Normalize(p.RoomNumber);
                if (!string.IsNullOrEmpty(pNum)) byNumber.TryGetValue(pNum, out match);
                if (match == null && !string.IsNullOrEmpty(p.RoomName))
                    byName.TryGetValue(p.RoomName.Trim(), out match);
                if (match == null)
                {
                    string nName = Normalize(p.RoomName);
                    if (!string.IsNullOrEmpty(nName)) byNormName.TryGetValue(nName, out match);
                }

                var row = new ProgramAuditRow
                {
                    RoomName = p.RoomName,
                    RoomNumber = p.RoomNumber,
                    Building = p.Building,
                    RequiredAreaM2 = p.RequiredAreaM2,
                    RequiredCount = p.RequiredCount,
                };

                if (match == null)
                {
                    row.Status = ProgramAuditStatus.MissingFromModel;
                    row.Note = "no model room matched by number or name";
                    result.Missing++;
                    result.Rows.Add(row);
                    continue;
                }

                usedModelIds.Add(match.ElementId);
                row.ActualAreaM2 = match.AreaM2;
                row.ModelElementId = match.ElementId;

                // Area comparison
                if (p.RequiredAreaM2.HasValue && p.RequiredAreaM2.Value > 0)
                {
                    double delta = (match.AreaM2 - p.RequiredAreaM2.Value) / p.RequiredAreaM2.Value * 100.0;
                    row.DeltaPct = Math.Round(delta, 1);
                    if (Math.Abs(delta) <= tolerancePct) { row.Status = ProgramAuditStatus.Compliant; result.Compliant++; }
                    else if (delta > 0) { row.Status = ProgramAuditStatus.OverArea; result.Over++; }
                    else { row.Status = ProgramAuditStatus.UnderArea; result.Under++; }
                }
                else
                {
                    row.Status = ProgramAuditStatus.Compliant;
                    row.Note = "matched (no required area)";
                    result.Compliant++;
                }

                // Count comparison (per normalised room-type name)
                if (p.RequiredCount.HasValue)
                {
                    normNameCount.TryGetValue(Normalize(p.RoomName), out int actual);
                    row.ActualCount = actual;
                    if (actual != p.RequiredCount.Value)
                    {
                        result.CountMismatch++;
                        row.Note = string.IsNullOrEmpty(row.Note)
                            ? $"count {actual}/{p.RequiredCount.Value}"
                            : row.Note + $"; count {actual}/{p.RequiredCount.Value}";
                    }
                }

                result.Rows.Add(row);
            }

            // Model rooms not claimed by any program row → extra
            foreach (var m in modelList)
            {
                if (usedModelIds.Contains(m.ElementId)) continue;
                result.Extra++;
                result.Rows.Add(new ProgramAuditRow
                {
                    RoomName = m.Name,
                    RoomNumber = m.Number,
                    Building = m.Loc,
                    ActualAreaM2 = m.AreaM2,
                    ModelElementId = m.ElementId,
                    Status = ProgramAuditStatus.ExtraInModel,
                    Note = "model room not in program"
                });
            }

            return result;
        }
    }
}
