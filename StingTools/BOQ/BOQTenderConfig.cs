// ══════════════════════════════════════════════════════════════════════════
//  BOQTenderConfig.cs — Phase 108h
//  Data class aggregating every field a senior QS fills in before issuing
//  a Tender BOQ. Collected by BOQTenderDialog, consumed by
//  BOQProfessionalExportCommand. Also persisted to project_config.json
//  under BOQ_TENDER_* keys so reopening the dialog for the same project
//  restores every field.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ
{
    /// <summary>
    /// How rates and totals are rendered on the bill sheets. Standard QS
    /// practice issues THREE versions of the BOQ during a contract:
    ///   TenderIssue  — blank rate and amount columns; bidder prices.
    ///   PricedCopy   — rates and totals visible (typically internal QS).
    ///   ContractCopy — rates and totals visible AND signed as contract doc.
    ///   AsBuilt      — final agreed rates after variations.
    /// </summary>
    public enum BOQPricingMode
    {
        TenderIssue = 0,
        PricedCopy = 1,
        ContractCopy = 2,
        AsBuilt = 3
    }

    public class BOQTenderConfig
    {
        // ── Project identity ───────────────────────────────────────────────
        public string Employer = "";
        public string EmployerAddress = "";
        public string ProjectName = "";
        public string ProjectNumber = "";
        public string ProjectAddress = "";
        public string WorkStage = "RIBA Stage 4 — Technical Design";
        public string ProjectType = "Commercial Office";
        public double GrossInternalAreaM2 = 0;
        public double NetInternalAreaM2 = 0;

        // ── Professional team ──────────────────────────────────────────────
        public string QsFirm = "";
        public string QsAddress = "";
        public string QsContact = "";
        public string QsEmail = "";
        public string Architect = "";
        public string StructuralEngineer = "";
        public string ServicesEngineer = "";
        public string PrincipalDesigner = "";
        public string ProjectManager = "";
        public string EmployersAgent = "";
        public string Contractor = "";

        // ── Contract ───────────────────────────────────────────────────────
        public string FormOfContract = "JCT Standard Building Contract with Quantities (SBC/Q)";
        public string ContractPeriod = "";         // "52 weeks" / "12 months"
        public string DateForPossession = "";
        public string SectionalCompletion = "";
        public string LiquidatedDamages = "";      // "UGX 2,500,000 per week"
        public string DefectsLiabilityPeriod = "12 months from Practical Completion";
        public string RetentionScheme = "5% reducing to 2.5% on Practical Completion";
        public string Fluctuations = "Fixed Price — no fluctuations";
        public string BondRequirement = "10% Performance Bond on acceptance";
        public string WarrantyRequirement = "Collateral warranties from all design sub-contractors";

        // ── Pricing ────────────────────────────────────────────────────────
        public BOQPricingMode PricingMode = BOQPricingMode.TenderIssue;
        public string TenderSubmissionDeadline = "";
        public string Currency = "UGX";
        public double ExchangeRateUgxPerUsd = 3700;
        public double PreliminariesPct = 12.0;
        public double ContingencyPct = 10.0;
        public double OverheadProfitPct = 8.0;
        public double VatPct = 18.0;

        // ── Revision ───────────────────────────────────────────────────────
        public string Revision = "P01";
        public string RevisionDate = "";          // ISO date string
        public string RevisionDescription = "Issued for Tender";
        public string Author = "";
        public string CheckedBy = "";
        public string ApprovedBy = "";

        // ── Output options ─────────────────────────────────────────────────
        public bool IncludeTimestamp = true;
        public bool IncludeCover = true;
        public bool IncludeDocumentControl = true;
        public bool IncludeContents = true;
        public bool IncludePreliminaries = true;
        public bool IncludePreambles = true;
        public bool IncludeCollections = true;
        public bool IncludeGrandSummary = true;
        public bool IncludeAnnexure = true;
        public bool IncludeCarbonData = false;
        public bool ShowProvisionalSumsSeparately = true;
        public bool ShowDualCurrency = false;    // USD alongside UGX on totals
        public bool UseClientVocabulary = false; // overlay templates with client phrasing (P7)
        public string Watermark = "";            // "DRAFT" / "TENDER" / "CONFIDENTIAL" / "" for none
        public string PricingNotes = "";         // free-text pricing notes / basis of tender

        // ── Phase 108i — Paragraph automation flags ────────────────────────
        // Each toggles one of the 10 priority enhancements implemented in
        // BOQParagraphEnhancer. Defaults are "on for the good ones, opt-in
        // for the disruptive ones" — P3 restructures bills, P8 rewrites
        // item names, so both default off until the user enables them.
        public bool EnablePerformanceClauses   = true;   // P1
        public bool EnableComplianceClauses    = true;   // P2
        public bool EnableDimensionalGroupings = false;  // P3
        public bool EnableAutoInclusionBoiler  = true;   // P4
        public bool EnableOrApprovedEquivalent = true;   // P5
        public bool EnableConditionalClauses   = true;   // P6
        public bool EnableSmartItemNaming      = false;  // P8
        public bool EnableSpecClauseCrossRefs  = true;   // P9
        public bool EmitCsvJsonSidecars        = true;   // P10

        // ── Schedules to include ───────────────────────────────────────────
        public bool IncludeDrawingSchedule = true;
        public bool IncludeSpecificationSchedule = false;
        public bool IncludePrimeCostSchedule = true;
        public bool IncludeDayworksSchedule = true;

        /// <summary>
        /// Returns true when rates / totals / grand summary figures should
        /// be hidden (Tender Issue mode). Bidders receive blank cells to fill.
        /// </summary>
        public bool HidePrices => PricingMode == BOQPricingMode.TenderIssue;

        /// <summary>
        /// Human-readable stamp for the cover page "TENDER STATUS" strip.
        /// </summary>
        public string StatusLabel
        {
            get
            {
                switch (PricingMode)
                {
                    case BOQPricingMode.TenderIssue:  return "FOR TENDER — BIDDERS TO PRICE";
                    case BOQPricingMode.PricedCopy:   return "PRICED COPY — QS INTERNAL";
                    case BOQPricingMode.ContractCopy: return "CONTRACT COPY — EXECUTED";
                    case BOQPricingMode.AsBuilt:      return "AS-BUILT — FINAL ACCOUNT";
                    default:                          return "FOR REVIEW";
                }
            }
        }
    }
}
