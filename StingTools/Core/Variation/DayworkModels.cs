// ══════════════════════════════════════════════════════════════════════════
//  DayworkModels.cs — instructed-daywork capture + build-up. PM-3.
//
//  The gap this closes: the tender annexure ships a DAYWORKS SCHEDULE, but it is
//  a RATES FRAMEWORK only — its own wording defers quantities to be "priced at
//  final account" (BOQProfessionalExportCommand, Annexure C.3). Nothing captured
//  the instructed sheets, so there was no path from "CA instructed dayworks under
//  Clause 5.7" to a priced value in the Final Account. VariationItem.RateSource
//  already reserved "Daywork" as distinct from "StarRate" — the seam was cut and
//  left unfilled. This fills it.
//
//  Shape mirrors StarRate (labour / plant / materials resource lines, reusing
//  StarRateLine and its basis-by-Unit pricing), with ONE deliberate difference:
//  a star rate rolls up via overhead% then profit%; a daywork sheet rolls up via
//  NRM2 / RICS PERCENTAGE ADDITIONS applied per resource section.
//
//  ── Convention (read this before changing a number) ──────────────────────
//  A percentage addition is an addition ON TOP OF net prime cost:
//      gross = net × (1 + pct/100)
//  so 15 means "net + 15%", NOT "×15" and NOT "×0.15". This is the RICS
//  "Definition of Prime Cost of Daywork" meaning and it is what the annexure
//  says on its face ("DW.10 Materials — percentage addition for OH&P").
//
//  The defaults below (115 / 110 / 112) are carried over from the server so the
//  plugin and Planscape agree numerically (BoqDocument.DayworkLabourPct et al).
//  NOTE: under the addition convention a 110% materials addition prices
//  materials at 2.1× net, which is not a commercially sane daywork percentage
//  (RICS-typical materials/plant additions are 10-20%; labour 115% IS typical
//  because it absorbs statutory on-costs, supervision and profit). Those three
//  server defaults look like uniform placeholders rather than tendered values.
//  Rather than silently price at them, the build-up flags any materials/plant
//  addition above ImplausibleNonLabourAdditionPct so a placeholder can never
//  pass into a priced sheet unnoticed. See Warnings().
//
//  Pure (no Revit / no I/O) — unit-tested in StingTools.Cost.Tests.
//  All money rounds through MoneyRound (half-even).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Variation
{
    /// <summary>Lifecycle of an instructed daywork sheet.</summary>
    public enum DayworkStatus
    {
        /// <summary>Resources recorded on site, not yet countersigned.</summary>
        Recorded = 0,
        /// <summary>Countersigned by the CA — the record of fact is agreed.</summary>
        Signed = 1,
        /// <summary>Valued at the build-up rates + percentage additions.</summary>
        Priced = 2
    }

    /// <summary>
    /// NRM2 / RICS percentage additions applied to net prime cost, per resource
    /// section. See the file header for the convention (gross = net × (1+pct/100)).
    /// </summary>
    public class DayworkBuildUp
    {
        /// <summary>Materials / plant additions above this read as placeholders,
        /// not tendered percentages, and raise a warning.</summary>
        public const double ImplausibleNonLabourAdditionPct = 50.0;

        // Defaults mirror Planscape BoqDocument.Daywork*Pct so both sides agree.
        public double LabourAdditionPct { get; set; } = 115.0;
        public double MaterialsAdditionPct { get; set; } = 110.0;
        public double PlantAdditionPct { get; set; } = 112.0;

        /// <summary>Negative additions are meaningless in a prime-cost build-up
        /// (a discount would be a rate reduction, not a negative addition), so
        /// they clamp to zero rather than silently crediting the employer.</summary>
        internal static double Sane(double pct) => pct > 0 && !double.IsNaN(pct) && !double.IsInfinity(pct) ? pct : 0;

        /// <summary>Commercial-sanity warnings — surfaced by the pricing command
        /// so a placeholder percentage cannot pass into a priced sheet unseen.</summary>
        public List<string> Warnings()
        {
            var w = new List<string>();
            if (MaterialsAdditionPct > ImplausibleNonLabourAdditionPct)
                w.Add($"Materials addition {MaterialsAdditionPct:0.##}% prices materials at "
                    + $"{1 + Sane(MaterialsAdditionPct) / 100.0:0.##}× net — RICS-typical is 10-20%. "
                    + "Confirm this is the tendered percentage, not a placeholder.");
            if (PlantAdditionPct > ImplausibleNonLabourAdditionPct)
                w.Add($"Plant addition {PlantAdditionPct:0.##}% prices plant at "
                    + $"{1 + Sane(PlantAdditionPct) / 100.0:0.##}× net — RICS-typical is 10-20%. "
                    + "Confirm this is the tendered percentage, not a placeholder.");
            return w;
        }
    }

    /// <summary>
    /// One instructed daywork sheet: the CA instruction it was raised under, the
    /// resources expended, and its valuation at the build-up percentages.
    /// </summary>
    public class DayworkRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>CA instruction reference the dayworks were instructed under
        /// (Clause 5.7 in the annexure wording).</summary>
        public string InstructionRef { get; set; } = "";
        public DateTime InstructionDate { get; set; } = DateTime.UtcNow.Date;

        public string Description { get; set; } = "";
        public DayworkStatus Status { get; set; } = DayworkStatus.Recorded;
        public string Currency { get; set; } = "UGX";   // project currency — from BOQDocument.Currency

        // Resource lines reuse StarRateLine: labour/plant price on Hours, materials
        // on Quantity, selected by Unit (StarRateLine.BasisQuantity). PM-1.
        public List<StarRateLine> LabourLines { get; set; } = new List<StarRateLine>();
        public List<StarRateLine> PlantLines { get; set; } = new List<StarRateLine>();
        public List<StarRateLine> MaterialsLines { get; set; } = new List<StarRateLine>();

        public DayworkBuildUp BuildUp { get; set; } = new DayworkBuildUp();

        // ── Trail ────────────────────────────────────────────────────
        public string RecordedBy { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string SignedBy { get; set; } = "";
        public DateTime? SignedDate { get; set; }
        public string PricedBy { get; set; } = "";
        public DateTime? PricedDate { get; set; }

        /// <summary>Set when this sheet has been valued INTO a variation
        /// (VariationItem.RateSource = "Daywork"). An attached sheet's value
        /// reaches the Final Account through that VO, so it is excluded from the
        /// standalone daywork total to prevent double-counting.</summary>
        public string VariationNumber { get; set; } = "";

        public bool IsAttached => !string.IsNullOrWhiteSpace(VariationNumber);

        // ── Net prime cost per section ───────────────────────────────
        public double LabourNet => Money(LabourLines);
        public double PlantNet => Money(PlantLines);
        public double MaterialsNet => Money(MaterialsLines);
        public double NetTotal => MoneyRound.Round(LabourNet + PlantNet + MaterialsNet, 2);

        // ── Percentage additions ─────────────────────────────────────
        public double LabourAddition => Addition(LabourNet, BuildUp?.LabourAdditionPct ?? 0);
        public double PlantAddition => Addition(PlantNet, BuildUp?.PlantAdditionPct ?? 0);
        public double MaterialsAddition => Addition(MaterialsNet, BuildUp?.MaterialsAdditionPct ?? 0);
        public double AdditionTotal => MoneyRound.Round(LabourAddition + PlantAddition + MaterialsAddition, 2);

        // ── Gross per section + sheet total ──────────────────────────
        public double LabourGross => MoneyRound.Round(LabourNet + LabourAddition, 2);
        public double PlantGross => MoneyRound.Round(PlantNet + PlantAddition, 2);
        public double MaterialsGross => MoneyRound.Round(MaterialsNet + MaterialsAddition, 2);

        /// <summary>The valued sheet — net prime cost plus percentage additions.</summary>
        public double GrossTotal => MoneyRound.Round(LabourGross + PlantGross + MaterialsGross, 2);

        private static double Money(List<StarRateLine> lines)
            => MoneyRound.Round((lines ?? new List<StarRateLine>()).Sum(l => l?.LineTotal ?? 0), 2);

        private static double Addition(double net, double pct)
            => MoneyRound.Round(net * DayworkBuildUp.Sane(pct) / 100.0, 2);

        /// <summary>True when the sheet carries at least one priced resource line.</summary>
        public bool HasResources => NetTotal > 0;
    }

    /// <summary>
    /// The captured daywork register — every instructed sheet on the project.
    /// Persisted to _BIM_COORD/dayworks.json (DayworkEngine owns the I/O).
    /// </summary>
    public class DayworkRegister
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string Currency { get; set; } = "UGX";
        public List<DayworkRecord> Records { get; set; } = new List<DayworkRecord>();

        public IEnumerable<DayworkRecord> WithStatus(DayworkStatus s)
            => Records.Where(r => r != null && r.Status == s);

        /// <summary>Gross value of every priced sheet, attached or not — the
        /// register headline.</summary>
        public double PricedGrossTotal
            => MoneyRound.Round(WithStatus(DayworkStatus.Priced).Sum(r => r.GrossTotal), 2);

        /// <summary>
        /// Gross value of priced sheets NOT attached to a variation — the figure
        /// that flows into the Final Account / AFC waterfalls.
        ///
        /// Attached sheets are deliberately excluded: their value is already
        /// carried by the VariationItem they were valued into, and the waterfalls
        /// add agreed VOs in full. Counting both would double-count the dayworks.
        /// </summary>
        public double UnattachedPricedGrossTotal
            => MoneyRound.Round(WithStatus(DayworkStatus.Priced)
                .Where(r => !r.IsAttached).Sum(r => r.GrossTotal), 2);

        /// <summary>Net prime cost recorded but not yet priced — the QS's
        /// outstanding valuation workload.</summary>
        public double UnpricedNetTotal
            => MoneyRound.Round(Records.Where(r => r != null && r.Status != DayworkStatus.Priced)
                .Sum(r => r.NetTotal), 2);

        /// <summary>The sheet with this id, or null when absent / id is empty.
        /// (The null-forgiving operators keep this file warning-clean when it is
        /// linked into the nullable-enabled test project; StingTools itself
        /// compiles with Nullable disabled, so it cannot carry `?` annotations.)</summary>
        public DayworkRecord ById(string id)
            => string.IsNullOrEmpty(id) ? null!
             : Records.FirstOrDefault(r => r != null && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))!;
    }
}
