// ══════════════════════════════════════════════════════════════════════════
//  MidpEngine.cs — Master/Task Information Delivery Plan engine. PM-8.
//
//  The audit (§3) found MIDP/TIDP existed only as a CSV template, never a data
//  model — so "is delivery X on programme?" was unanswerable. This promotes the
//  plan to a tracked register: each deliverable carries a planned issue date, a
//  required suitability (S-code) and milestone, joined to the live lifecycle
//  (actual issued date + actual suitability). Drift detection then classifies
//  every deliverable against the programme:
//
//    NotDue      — planned date in the future, not yet issued
//    OnTrack     — issued on/before plan at the required suitability
//    AtRisk      — due within the look-ahead window, not yet issued
//    Overdue     — planned date passed, not issued
//    SuitShort   — issued but below the required suitability (e.g. S2 vs S4)
//
//  Pure (no Revit / no I/O) — headless xUnit tests in StingTools.Cost.Tests.
//  CSV parse + the lifecycle join live at the command layer.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Delivery
{
    public class DeliverablePlanItem
    {
        public string Code { get; set; } = "";            // deliverable / container code
        public string Title { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string Milestone { get; set; } = "";       // e.g. "RIBA 3" / "Data Drop 2"
        public DateTime PlannedDate { get; set; }
        public string RequiredSuitability { get; set; } = "S2";  // S0..S7 / A / B

        // Live lifecycle join (filled from deliverables.json):
        public bool Issued { get; set; }
        public DateTime? ActualDate { get; set; }
        public string ActualSuitability { get; set; } = "";
    }

    public enum DeliveryDriftState { NotDue, OnTrack, AtRisk, Overdue, SuitShort }

    public class DeliverableDrift
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime PlannedDate { get; set; }
        public DeliveryDriftState State { get; set; }
        public int DaysLateOrToGo { get; set; }           // +ve = days overdue, -ve = days to go
        public string RequiredSuitability { get; set; } = "";
        public string ActualSuitability { get; set; } = "";
    }

    public class MidpSummary
    {
        public int Total { get; set; }
        public int OnTrack { get; set; }
        public int AtRisk { get; set; }
        public int Overdue { get; set; }
        public int SuitShort { get; set; }
        public int NotDue { get; set; }
        public double OnProgrammePct { get; set; }
        public List<DeliverableDrift> Drifts { get; set; } = new List<DeliverableDrift>();
    }

    public static class MidpEngine
    {
        // ISO 19650 suitability rank — higher is more mature/shareable.
        private static readonly Dictionary<string, int> SuitRank =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["S0"] = 0, ["S1"] = 1, ["S2"] = 2, ["S3"] = 3, ["S4"] = 4,
                ["S5"] = 5, ["S6"] = 6, ["S7"] = 7,
                ["A"] = 8, ["B"] = 6, ["AB"] = 8,
            };

        public static int Rank(string suit) =>
            !string.IsNullOrWhiteSpace(suit) && SuitRank.TryGetValue(suit.Trim(), out int r) ? r : -1;

        /// <summary>Classify one deliverable against the programme as at
        /// <paramref name="asOf"/>, with an <paramref name="atRiskWindowDays"/>
        /// look-ahead.</summary>
        public static DeliverableDrift Classify(DeliverablePlanItem d, DateTime asOf, int atRiskWindowDays = 14)
        {
            var drift = new DeliverableDrift
            {
                Code = d.Code,
                Title = d.Title,
                PlannedDate = d.PlannedDate,
                RequiredSuitability = d.RequiredSuitability,
                ActualSuitability = d.ActualSuitability,
            };
            int daysToPlanned = (int)Math.Round((d.PlannedDate.Date - asOf.Date).TotalDays);

            if (d.Issued && d.ActualDate.HasValue)
            {
                drift.DaysLateOrToGo = (int)Math.Round((d.ActualDate.Value.Date - d.PlannedDate.Date).TotalDays);
                // Issued but below the required suitability ⇒ a real shortfall.
                if (Rank(d.ActualSuitability) >= 0 && Rank(d.ActualSuitability) < Rank(d.RequiredSuitability))
                    drift.State = DeliveryDriftState.SuitShort;
                else
                    drift.State = DeliveryDriftState.OnTrack;
                return drift;
            }

            // Not issued.
            drift.DaysLateOrToGo = -daysToPlanned;        // +ve once planned date has passed
            if (daysToPlanned < 0) drift.State = DeliveryDriftState.Overdue;
            else if (daysToPlanned <= atRiskWindowDays) drift.State = DeliveryDriftState.AtRisk;
            else drift.State = DeliveryDriftState.NotDue;
            return drift;
        }

        public static MidpSummary Detect(IEnumerable<DeliverablePlanItem> plan, DateTime asOf, int atRiskWindowDays = 14)
        {
            var items = (plan ?? Enumerable.Empty<DeliverablePlanItem>()).Where(d => d != null).ToList();
            var s = new MidpSummary { Total = items.Count };
            foreach (var d in items)
            {
                var drift = Classify(d, asOf, atRiskWindowDays);
                s.Drifts.Add(drift);
                switch (drift.State)
                {
                    case DeliveryDriftState.OnTrack: s.OnTrack++; break;
                    case DeliveryDriftState.AtRisk: s.AtRisk++; break;
                    case DeliveryDriftState.Overdue: s.Overdue++; break;
                    case DeliveryDriftState.SuitShort: s.SuitShort++; break;
                    case DeliveryDriftState.NotDue: s.NotDue++; break;
                }
            }
            // "On programme" = on-track or not-yet-due; at-risk/overdue/suit-short are off.
            int onProgramme = s.OnTrack + s.NotDue;
            s.OnProgrammePct = items.Count > 0 ? Math.Round(100.0 * onProgramme / items.Count, 1) : 0;
            // Order drifts worst-first for the report.
            s.Drifts = s.Drifts
                .OrderByDescending(x => StateSeverity(x.State))
                .ThenByDescending(x => x.DaysLateOrToGo)
                .ToList();
            return s;
        }

        private static int StateSeverity(DeliveryDriftState st) => st switch
        {
            DeliveryDriftState.Overdue => 4,
            DeliveryDriftState.SuitShort => 3,
            DeliveryDriftState.AtRisk => 2,
            DeliveryDriftState.OnTrack => 1,
            _ => 0,
        };
    }
}
