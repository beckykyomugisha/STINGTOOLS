// ══════════════════════════════════════════════════════════════════════════
//  VariationModels.cs — POCOs for variation / change order tracking.
//
//  P5.2 of the Cost Management Implementation Plan. State machine
//  matches the server-side BoqVariation entity for round-trip with the
//  Planscape API:
//
//    Draft → Submitted → Reviewed → (Approved | Rejected) → Incorporated
//
//  Star rates: when a varied item has no matching BOQ rate, the QS
//  builds one from first principles — Labour + Plant + Materials +
//  Overhead + Profit. The StarRate model carries those build-up
//  components so the audit trail is preserved.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Variation
{
    public enum VariationStatus
    {
        Draft,
        Submitted,
        Reviewed,
        Approved,
        Rejected,
        Incorporated
    }

    public enum VariationKind
    {
        /// <summary>Architect's/engineer's instruction (JCT AI).</summary>
        Instruction,
        /// <summary>NEC4 compensation event.</summary>
        CompensationEvent,
        /// <summary>FIDIC Engineer's instruction.</summary>
        EngineerInstruction,
        /// <summary>Contractor claim.</summary>
        ContractorClaim
    }

    /// <summary>
    /// Phase 184o — *why* the variation arose. Drives liability, EOT
    /// entitlement, insurance recovery and month-end pattern analysis.
    /// Distinct from <see cref="VariationKind"/> (the contractual route).
    ///
    /// Mapping to common contract clauses (informational only — the QS
    /// confirms before approval):
    ///   DesignChange       → JCT 2.16 / NEC4 60.1(1)  / FIDIC 13.1
    ///   ClientRequest      → JCT 5.1.1 / NEC4 60.1(1) / FIDIC 13.1
    ///   SiteCondition      → JCT 2.29 / NEC4 60.1(12) / FIDIC 4.12
    ///   StatutoryChange    → JCT 2.17 / NEC4 60.1(5)  / FIDIC 13.6/13.7
    ///   ErrorOmission      → JCT 2.16 / NEC4 60.1(1)  / FIDIC 1.9
    ///   ContractorProposal → JCT 5.1.2 / NEC4 16      / FIDIC 13.2 VE
    ///   ScopeAddition      → JCT 5.1.1 / NEC4 60.1(1) / FIDIC 13.1
    ///   ScopeOmission      → JCT 5.1.1 / NEC4 60.1(1) / FIDIC 13.1
    ///   Specification      → JCT 2.16 / NEC4 60.1(1)  / FIDIC 13.1
    ///   Quality            → JCT 2.16 / NEC4 60.1(1)  / FIDIC 13.1
    ///   ProgrammeChange    → JCT 2.16 / NEC4 60.1(2)  / FIDIC 8.4
    ///   Other              → bespoke / non-standard cause
    /// </summary>
    public enum VariationReason
    {
        DesignChange,
        ClientRequest,
        SiteCondition,
        StatutoryChange,
        ErrorOmission,
        ContractorProposal,
        ScopeAddition,
        ScopeOmission,
        Specification,
        Quality,
        ProgrammeChange,
        Other
    }

    /// <summary>
    /// Phase 184o — who pays. Drives cost recovery (PI insurance,
    /// risk-allowance drawdown, employer liability). Informational at
    /// the model layer; final assignment requires QS + legal sign-off.
    /// </summary>
    public enum VariationLiability
    {
        /// <summary>Employer / client absorbs the cost.</summary>
        Employer,
        /// <summary>Contractor absorbs the cost.</summary>
        Contractor,
        /// <summary>Designer / consultant — typically routed via PI insurance.</summary>
        Designer,
        /// <summary>Cost shared by agreement / mechanism (proportionate split).</summary>
        Shared,
        /// <summary>Force majeure / unforeseen — typically employer with insurance route.</summary>
        ForceMajeure
    }

    /// <summary>
    /// One variation instruction. Items are individual measured lines
    /// that price the change — typically minted from a BOQSnapshotDiff
    /// cluster.
    /// </summary>
    public class VariationInstruction
    {
        public string Id = Guid.NewGuid().ToString("N");

        /// <summary>Series number per contract — "VO-007", "CE-042", etc.</summary>
        public string Number { get; set; } = "";

        public string ContractRef { get; set; } = "";
        public VariationKind Kind { get; set; } = VariationKind.Instruction;
        public VariationStatus Status { get; set; } = VariationStatus.Draft;

        /// <summary>
        /// Why the variation arose (Phase 184o). Defaults to
        /// <see cref="VariationReason.Other"/> until the QS confirms.
        /// </summary>
        public VariationReason Reason { get; set; } = VariationReason.Other;

        /// <summary>Who pays. Defaults to Employer until QS reviews.</summary>
        public VariationLiability Liability { get; set; } = VariationLiability.Employer;

        /// <summary>Free-text rationale captured at submission.</summary>
        public string ReasonDetail { get; set; } = "";

        /// <summary>EOT entitlement in calendar days; 0 = no time impact.</summary>
        public int EotDays { get; set; } = 0;

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string IssuedBy { get; set; } = "";
        public DateTime InstructionDate { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovalDate { get; set; }
        public string ApprovedBy { get; set; } = "";

        public string Currency { get; set; } = "GBP";
        public List<VariationItem> Items { get; set; } = new List<VariationItem>();

        /// <summary>Reference to the BOQ snapshot diff that produced this VO (optional).</summary>
        public string SourceSnapshotDiff { get; set; } = "";

        /// <summary>Reference to the parent baseline this VO modifies (server BoqBaseline.Id).</summary>
        public string ParentBaselineId { get; set; } = "";

        public double TotalValue => Math.Round(Items.Sum(i => i.TotalValue), 2);

        public string Note { get; set; } = "";
    }

    public class VariationItem
    {
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "each";
        public double Quantity { get; set; }
        public double UnitRate { get; set; }
        public string RateSource { get; set; } = ""; // "BOQ" | "BOQ+pct" | "StarRate" | "Daywork"

        /// <summary>Optional link to a StarRate build-up.</summary>
        public string StarRateId { get; set; } = "";

        public double TotalValue => Math.Round(Quantity * UnitRate, 2);
    }

    /// <summary>
    /// Labour + plant + materials + overhead + profit build-up used when
    /// the BOQ has no comparable rate.
    /// </summary>
    public class StarRate
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "each";
        public string Currency { get; set; } = "GBP";

        public List<StarRateLine> LabourLines { get; set; } = new List<StarRateLine>();
        public List<StarRateLine> PlantLines { get; set; } = new List<StarRateLine>();
        public List<StarRateLine> MaterialsLines { get; set; } = new List<StarRateLine>();

        public double OverheadPercent { get; set; } = 8.0;
        public double ProfitPercent { get; set; } = 5.0;

        public string Author { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public double LabourTotal => Math.Round(LabourLines.Sum(l => l.LineTotal), 2);
        public double PlantTotal => Math.Round(PlantLines.Sum(l => l.LineTotal), 2);
        public double MaterialsTotal => Math.Round(MaterialsLines.Sum(l => l.LineTotal), 2);
        public double Subtotal => Math.Round(LabourTotal + PlantTotal + MaterialsTotal, 2);
        public double OverheadAmount => Math.Round(Subtotal * OverheadPercent / 100.0, 2);
        public double ProfitAmount => Math.Round((Subtotal + OverheadAmount) * ProfitPercent / 100.0, 2);
        public double FinalRate => Math.Round(Subtotal + OverheadAmount + ProfitAmount, 2);
    }

    public class StarRateLine
    {
        public string Resource { get; set; } = "";   // "Skilled labourer", "JCB excavator", "Concrete C30/37"
        public double Hours { get; set; }            // for labour / plant
        public double Quantity { get; set; }         // for materials
        public double UnitRate { get; set; }         // £/hr or £/unit
        public string Unit { get; set; } = "hr";

        public double LineTotal => Math.Round(Math.Max(Hours, Quantity) * UnitRate, 2);
    }
}
