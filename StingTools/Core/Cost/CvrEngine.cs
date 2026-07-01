// ══════════════════════════════════════════════════════════════════════════
//  CvrEngine.cs — Cost-Value Reconciliation. PM-3.
//
//  The CVR is the contractor's internal monthly truth statement: the VALUE of
//  work done (what the job has earned) set against the COST of doing it (what it
//  has spent), at a common cut-off — so the real margin, the over/under-claim
//  against the client certificate, and the forecast out-turn are all visible in
//  one place. The audit (§5 capability 7) found this entirely absent: certs and
//  EVM exist, but nothing reconciles cost vs value into a CVR.
//
//  Definitions (RICS / standard contractor CVR):
//    Value of work done  V  — internal valuation of progress (≈ earned value)
//    Cost to date        C  — actual cost incurred
//    Amount certified    Q  — what the client has certified/paid to date
//    Provisions          P  — known future liabilities (defects, claims, retn)
//    Gross margin           = V − C − P
//    WIP (under/over cert)  = V − Q  (>0 work done not yet billed; <0 over-billed)
//    Cost-to-complete       = ForecastFinalCost − C
//    Forecast margin        = ForecastFinalValue − ForecastFinalCost
//
//  Pure (no Revit / no I/O) — headless xUnit tests in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.Cost
{
    public class CvrInput
    {
        /// <summary>Internal valuation of work physically done (the contractor's
        /// own measure, ≈ EVM earned value). The CVR "Value".</summary>
        public double ValueOfWorkDoneUGX { get; set; }
        /// <summary>Actual cost incurred to date (labour + plant + materials +
        /// subcontract + prelims). The CVR "Cost".</summary>
        public double CostToDateUGX { get; set; }
        /// <summary>What the client has certified/paid to date (gross of retention
        /// is fine — pick one basis and keep it). Drives the WIP / claim position.</summary>
        public double AmountCertifiedUGX { get; set; }
        /// <summary>Known future liabilities held against the job (defects,
        /// disputed claims, contra-charges). Reduces declared margin prudently.</summary>
        public double ProvisionsUGX { get; set; }
        /// <summary>Forecast final cost at completion (e.g. from the AFC / EVM EAC).
        /// 0 ⇒ forecast metrics are reported as not-available.</summary>
        public double ForecastFinalCostUGX { get; set; }
        /// <summary>Forecast final value at completion (e.g. contract sum + agreed
        /// variations). 0 ⇒ forecast margin not reported.</summary>
        public double ForecastFinalValueUGX { get; set; }
    }

    public class CvrResult
    {
        public double ValueUGX { get; set; }
        public double CostUGX { get; set; }
        public double ProvisionsUGX { get; set; }

        /// <summary>V − C − P. The job's gross margin earned to date.</summary>
        public double GrossMarginUGX { get; set; }
        /// <summary>Margin as a % of value. 0 when value is 0.</summary>
        public double MarginPct { get; set; }

        /// <summary>V − Q. Positive = under-certified (work done the client has not
        /// yet paid for — cash drag); negative = over-certified (billed ahead of
        /// progress — a cash benefit but a risk if the job slips).</summary>
        public double WipUGX { get; set; }
        /// <summary>"UnderClaimed" (V&gt;Q), "OverClaimed" (V&lt;Q), or "OnClaim".</summary>
        public string ClaimPosition { get; set; } = "OnClaim";

        /// <summary>ForecastFinalCost − CostToDate (project-level cost-to-complete).
        /// 0 when no forecast cost supplied.</summary>
        public double CostToCompleteUGX { get; set; }
        /// <summary>ForecastFinalValue − ForecastFinalCost. The out-turn margin.</summary>
        public double ForecastMarginUGX { get; set; }
        public double ForecastMarginPct { get; set; }
        public bool HasForecast { get; set; }
    }

    public static class CvrEngine
    {
        public static CvrResult Compute(CvrInput i)
        {
            i = i ?? new CvrInput();
            double v = i.ValueOfWorkDoneUGX;
            double c = i.CostToDateUGX;
            double p = i.ProvisionsUGX;
            double q = i.AmountCertifiedUGX;

            double margin = MoneyRound.Round(v - c - p);
            double wip = MoneyRound.Round(v - q);

            string pos = "OnClaim";
            if (wip > 0.005) pos = "UnderClaimed";
            else if (wip < -0.005) pos = "OverClaimed";

            var r = new CvrResult
            {
                ValueUGX = MoneyRound.Round(v),
                CostUGX = MoneyRound.Round(c),
                ProvisionsUGX = MoneyRound.Round(p),
                GrossMarginUGX = margin,
                MarginPct = v > 0 ? MoneyRound.Round(margin / v * 100.0, 2) : 0,
                WipUGX = wip,
                ClaimPosition = pos,
            };

            if (i.ForecastFinalCostUGX > 0 || i.ForecastFinalValueUGX > 0)
            {
                r.HasForecast = true;
                r.CostToCompleteUGX = i.ForecastFinalCostUGX > 0
                    ? MoneyRound.Round(i.ForecastFinalCostUGX - c) : 0;
                double fMargin = MoneyRound.Round(i.ForecastFinalValueUGX - i.ForecastFinalCostUGX);
                r.ForecastMarginUGX = fMargin;
                r.ForecastMarginPct = i.ForecastFinalValueUGX > 0
                    ? MoneyRound.Round(fMargin / i.ForecastFinalValueUGX * 100.0, 2) : 0;
            }
            return r;
        }
    }
}
