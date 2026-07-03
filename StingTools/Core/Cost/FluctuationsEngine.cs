// ══════════════════════════════════════════════════════════════════════════
//  FluctuationsEngine.cs — index-linked fluctuations (price-rise recovery). PM-3.
//
//  High-inflation East-Africa contracts recover material/labour price rises via a
//  fluctuations clause. Two recognised methods:
//
//   • FORMULA / index method (JCT Fluctuations Option C, the NEDO/BCIS Price
//     Adjustment Formula Indices): each tranche of work done in a period is
//     adjusted by the movement of a published index between the base date and the
//     valuation date, less a non-adjustable element. A basket of weighted indices
//     (or a single CPI) drives it. This is the modern, auditable method.
//
//   • Traditional (Option A/B): recover actual proven increases on a defined list
//     of materials — captured as basket lines with base vs current unit price.
//
//  Pure (no Revit / no I/O) — unit-tested headlessly in StingTools.Cost.Tests.
//  All money rounds through MoneyRound (half-even). Provenance: JCT 2016/2024
//  Fluctuations Options; RICS guidance on the Price Adjustment Formula; for Uganda,
//  UBOS CPI is the documented local index proxy when no PAFI series applies.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Cost
{
    /// <summary>One basket line: a weighted index (formula method) or a priced
    /// material (traditional). For the formula method set BaseIndex/CurrentIndex +
    /// Weight; for the traditional method set BaseValue=quantity×base price and the
    /// indices to the price ratio (CurrentIndex/BaseIndex = currentPrice/basePrice).</summary>
    public class FluctuationLine
    {
        public string Label { get; set; } = "";
        public double Weight { get; set; } = 1.0;        // share of the adjustable work (formula method)
        public double BaseIndex { get; set; } = 100.0;   // index at base date
        public double CurrentIndex { get; set; } = 100.0;// index at valuation date

        /// <summary>Fractional movement (Current−Base)/Base. 0 when base ≤ 0.</summary>
        public double Movement => BaseIndex > 0 ? (CurrentIndex - BaseIndex) / BaseIndex : 0;
    }

    public class FluctuationsBasket
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string Currency { get; set; } = "UGX";
        /// <summary>Value of work done in the period that is subject to adjustment
        /// (project currency). The non-adjustable element is removed below.</summary>
        public double AdjustableWorkValue { get; set; }
        /// <summary>Non-adjustable element (JCT Option C, often 10%). 0..100.</summary>
        public double NonAdjustablePercent { get; set; } = 10.0;
        public List<FluctuationLine> Lines { get; set; } = new List<FluctuationLine>();
        public string Method { get; set; } = "formula"; // "formula" | "cpi"
        /// <summary>Single CPI movement used when Method = "cpi" (e.g. UBOS CPI).</summary>
        public double CpiBaseIndex { get; set; } = 100.0;
        public double CpiCurrentIndex { get; set; } = 100.0;
    }

    public static class FluctuationsEngine
    {
        /// <summary>
        /// The fluctuation amount recoverable (project currency, can be negative if
        /// indices fell). FORMULA: adjustableValue × (1 − nonAdj) × Σ(weightᵢ × movementᵢ)/Σweight.
        /// CPI: adjustableValue × (1 − nonAdj) × CPI movement.
        /// </summary>
        public static double Compute(FluctuationsBasket b)
        {
            if (b == null || b.AdjustableWorkValue == 0) return 0;
            double nonAdj = Math.Max(0, Math.Min(100, b.NonAdjustablePercent)) / 100.0;
            double adjustable = b.AdjustableWorkValue * (1.0 - nonAdj);

            double movement;
            if (string.Equals(b.Method, "cpi", StringComparison.OrdinalIgnoreCase))
            {
                movement = b.CpiBaseIndex > 0 ? (b.CpiCurrentIndex - b.CpiBaseIndex) / b.CpiBaseIndex : 0;
            }
            else
            {
                double wSum = b.Lines?.Sum(l => l.Weight) ?? 0;
                if (wSum <= 0 || b.Lines == null) return 0;
                movement = b.Lines.Sum(l => l.Weight * l.Movement) / wSum;
            }
            return MoneyRound.Round(adjustable * movement);
        }

        /// <summary>The blended index movement (for reporting), Σ(wᵢ·mᵢ)/Σw or CPI.</summary>
        public static double BlendedMovement(FluctuationsBasket b)
        {
            if (b == null) return 0;
            if (string.Equals(b.Method, "cpi", StringComparison.OrdinalIgnoreCase))
                return b.CpiBaseIndex > 0 ? (b.CpiCurrentIndex - b.CpiBaseIndex) / b.CpiBaseIndex : 0;
            double wSum = b.Lines?.Sum(l => l.Weight) ?? 0;
            if (wSum <= 0 || b.Lines == null) return 0;
            return b.Lines.Sum(l => l.Weight * l.Movement) / wSum;
        }
    }
}
