// ══════════════════════════════════════════════════════════════════════════
//  CommitmentsRegister.cs — sub-contract / purchase-order commitments against
//  budget lines. PM-3.
//
//  The audit (§5 capability 1) flagged a commitments register as a "no PM should
//  lack this" primitive that was entirely absent: orders placed (sub-contracts,
//  POs, material orders) are a future cost the moment they are signed, well
//  before any invoice lands. Tracking committed-vs-budget shows the uncommitted
//  balance (head-room still to let) and over-commitment (ordered beyond budget)
//  per budget line and project-wide.
//
//  This is the pure data model + roll-up. Persistence (a JSON ledger under the
//  project sidecar) and the Revit command live at the command layer.
//
//  Pure (no Revit / no I/O) — headless xUnit tests in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Cost
{
    public enum CommitmentKind { Subcontract, PurchaseOrder, MaterialOrder, Other }

    public class Commitment
    {
        public string Id { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string Description { get; set; } = "";
        public CommitmentKind Kind { get; set; } = CommitmentKind.PurchaseOrder;
        /// <summary>Budget line / cost code this order draws down against.</summary>
        public string BudgetLineRef { get; set; } = "";
        /// <summary>Order value committed when signed.</summary>
        public double CommittedUGX { get; set; }
        /// <summary>Certified / invoiced against the order to date.</summary>
        public double CertifiedUGX { get; set; }
        /// <summary>Paid against the order to date.</summary>
        public double PaidUGX { get; set; }
        public string Status { get; set; } = "Open";   // Open / Closed / Cancelled
        public string Date { get; set; } = "";

        /// <summary>Committed but not yet certified — the future cost still to land.</summary>
        public double OutstandingUGX =>
            string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                ? 0 : Math.Max(0, CommittedUGX - CertifiedUGX);
    }

    public class BudgetLineCommitment
    {
        public string BudgetLineRef { get; set; } = "";
        public double BudgetUGX { get; set; }
        public double CommittedUGX { get; set; }
        public double CertifiedUGX { get; set; }
        public double OutstandingUGX { get; set; }
        /// <summary>Budget − Committed. &gt;0 = head-room still to let; &lt;0 = over-committed.</summary>
        public double UncommittedUGX { get; set; }
        public bool OverCommitted { get; set; }
    }

    public class CommitmentsSummary
    {
        public double TotalBudgetUGX { get; set; }
        public double TotalCommittedUGX { get; set; }
        public double TotalCertifiedUGX { get; set; }
        public double TotalOutstandingUGX { get; set; }
        public double TotalUncommittedUGX { get; set; }
        public List<BudgetLineCommitment> ByBudgetLine { get; set; } = new List<BudgetLineCommitment>();
        public List<string> OverCommittedLines { get; set; } = new List<string>();
    }

    public static class CommitmentsRegister
    {
        /// <summary>Roll commitments up against a per-budget-line budget map
        /// (ref → budget UGX). Lines with commitments but no budget entry appear
        /// with budget 0 (and therefore over-committed).</summary>
        public static CommitmentsSummary Rollup(
            IEnumerable<Commitment> commitments,
            IReadOnlyDictionary<string, double> budgetByLine)
        {
            var list = (commitments ?? Enumerable.Empty<Commitment>())
                .Where(c => c != null && !string.Equals(c.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .ToList();
            budgetByLine = budgetByLine ?? new Dictionary<string, double>();

            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in list) refs.Add(c.BudgetLineRef ?? "");
            foreach (var k in budgetByLine.Keys) refs.Add(k);

            var summary = new CommitmentsSummary();
            foreach (var r in refs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var rows = list.Where(c => string.Equals(c.BudgetLineRef ?? "", r, StringComparison.OrdinalIgnoreCase)).ToList();
                double committed = MoneyRound.Round(rows.Sum(c => c.CommittedUGX));
                double certified = MoneyRound.Round(rows.Sum(c => c.CertifiedUGX));
                double outstanding = MoneyRound.Round(rows.Sum(c => c.OutstandingUGX));
                budgetByLine.TryGetValue(r, out double budget);
                budget = MoneyRound.Round(budget);
                double uncommitted = MoneyRound.Round(budget - committed);
                bool over = committed > budget + 0.005;

                summary.ByBudgetLine.Add(new BudgetLineCommitment
                {
                    BudgetLineRef = r,
                    BudgetUGX = budget,
                    CommittedUGX = committed,
                    CertifiedUGX = certified,
                    OutstandingUGX = outstanding,
                    UncommittedUGX = uncommitted,
                    OverCommitted = over,
                });
                if (over) summary.OverCommittedLines.Add(r);

                summary.TotalBudgetUGX += budget;
                summary.TotalCommittedUGX += committed;
                summary.TotalCertifiedUGX += certified;
                summary.TotalOutstandingUGX += outstanding;
            }
            summary.TotalBudgetUGX = MoneyRound.Round(summary.TotalBudgetUGX);
            summary.TotalCommittedUGX = MoneyRound.Round(summary.TotalCommittedUGX);
            summary.TotalCertifiedUGX = MoneyRound.Round(summary.TotalCertifiedUGX);
            summary.TotalOutstandingUGX = MoneyRound.Round(summary.TotalOutstandingUGX);
            summary.TotalUncommittedUGX = MoneyRound.Round(summary.TotalBudgetUGX - summary.TotalCommittedUGX);
            return summary;
        }
    }
}
