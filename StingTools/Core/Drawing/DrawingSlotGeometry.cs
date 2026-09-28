// StingTools — Drawing Template Manager
//
// DrawingSlotGeometry — Revit-free slot sanity checks shared by
// DrawingTypeValidator (DT-055 / DT-056 / DT-SLT-03, run in Revit) and
// StingTools.Tags.Tests (run over the shipped catalogue in CI). One copy,
// so the gate and the in-app validator cannot disagree about what an
// overlapping or out-of-bounds slot is.
//
// Slots are normX/normY/normW/normH fractions (0..1) of the title block's
// drawable rect — see DrawingSlot in DrawingType.cs.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    public static class DrawingSlotGeometry
    {
        /// <summary>Same tolerance the validator has always used for the
        /// right/top edge (1.0001), applied to overlap too so two slots that
        /// share an edge up to floating-point noise are not an overlap.</summary>
        public const double Tolerance = 0.0001;

        public enum IssueKind { InvalidGeometry, OutOfBounds, Overlap }

        public sealed class SlotIssue
        {
            public IssueKind Kind { get; set; }
            /// <summary>DT-055 / DT-056 / DT-SLT-03 — the validator's codes.</summary>
            public string Code { get; set; }
            public int Index { get; set; }
            /// <summary>Second slot for an overlap; -1 otherwise.</summary>
            public int OtherIndex { get; set; } = -1;
            /// <summary>Overlap area as % of the drawable zone (overlaps only).</summary>
            public double OverlapPct { get; set; }
            public string Message { get; set; }
        }

        public static List<SlotIssue> Check(IList<DrawingSlot> slots)
        {
            var issues = new List<SlotIssue>();
            if (slots == null) return issues;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                if (s.NormX < 0 || s.NormY < 0 || s.NormW <= 0 || s.NormH <= 0)
                    issues.Add(new SlotIssue
                    {
                        Kind = IssueKind.InvalidGeometry, Code = "DT-055", Index = i,
                        Message = $"Slot '{s.Label}' has invalid geometry (normX={s.NormX} normY={s.NormY} normW={s.NormW} normH={s.NormH})."
                    });
                if (s.NormX + s.NormW > 1 + Tolerance || s.NormY + s.NormH > 1 + Tolerance)
                    issues.Add(new SlotIssue
                    {
                        Kind = IssueKind.OutOfBounds, Code = "DT-056", Index = i,
                        Message = $"Slot '{s.Label}' extends beyond the drawable zone (normX+W={s.NormX + s.NormW:F2} normY+H={s.NormY + s.NormH:F2})."
                    });
            }

            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    var a = slots[i];
                    var b = slots[j];
                    if (a == null || b == null) continue;
                    double ox = Math.Min(a.NormX + a.NormW, b.NormX + b.NormW) - Math.Max(a.NormX, b.NormX);
                    double oy = Math.Min(a.NormY + a.NormH, b.NormY + b.NormH) - Math.Max(a.NormY, b.NormY);
                    if (ox <= Tolerance || oy <= Tolerance) continue;
                    double pct = Math.Round(ox * oy * 100, 1);
                    issues.Add(new SlotIssue
                    {
                        Kind = IssueKind.Overlap, Code = "DT-SLT-03", Index = i, OtherIndex = j, OverlapPct = pct,
                        Message = $"Slots [{i}] '{a.Label ?? $"slot{i}"}' and [{j}] '{b.Label ?? $"slot{j}"}' overlap by {pct}% of sheet area."
                    });
                }
            }
            return issues;
        }
    }
}
