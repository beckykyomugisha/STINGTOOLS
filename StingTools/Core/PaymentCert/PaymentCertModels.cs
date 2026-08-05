// ══════════════════════════════════════════════════════════════════════════
//  PaymentCertModels.cs — POCOs for the payment certificate engine.
//
//  Implements the data shape behind JCT 2024 §4 / NEC4 §50 / FIDIC Red
//  Book §14 interim certificates. A certificate is one of a numbered
//  series against a contract, valuing work-in-place + materials-on-site
//  − retention − previously certified.
//
//  P5.1 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.PaymentCert
{
    public enum PaymentCertStatus
    {
        Draft,
        Issued,
        Disputed,
        Agreed,
        Paid,
        Superseded
    }

    public enum ContractForm
    {
        JCT2024,
        NEC4,
        FIDIC2017Red
    }

    /// <summary>
    /// One line on the Schedule of Values — the contractor's breakdown
    /// of the contract sum used as the basis for monthly valuations.
    /// Bound to a BOQ section so the engine can recompute % complete
    /// from live geometry.
    /// </summary>
    public class SovLine
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Section { get; set; } = "";        // NRM2 section or trade
        public string Description { get; set; } = "";
        public double ContractValue { get; set; }        // GBP — fixed at contract signing
        public double PercentComplete { get; set; }      // 0..100
        public double PreviouslyCertified { get; set; }  // GBP — cumulative pre-this-cert
        public double MaterialsOnSite { get; set; }      // GBP — held on this cert

        /// <summary>Gross value earned this cert before retention.</summary>
        public double GrossThisCert
            => Math.Round(Math.Max(0, ContractValue * PercentComplete / 100.0 - PreviouslyCertified + MaterialsOnSite), 2);
    }

    /// <summary>
    /// Single interim payment certificate. Persisted as JSON sidecar
    /// under <project>/_bim_manager/payment_certs/. The sequence is
    /// monotonically increasing per contract; supersession is tracked
    /// via SupersededByCertNumber.
    /// </summary>
    public class PaymentCertificate
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string ContractRef { get; set; } = "";
        public ContractForm Form { get; set; } = ContractForm.NEC4;
        public int CertNumber { get; set; }
        public DateTime ValuationDate { get; set; } = DateTime.UtcNow;
        public DateTime IssuedDate { get; set; } = DateTime.UtcNow;
        public string Currency { get; set; } = "GBP";

        public string ContractorName { get; set; } = "";
        public string EmployerName { get; set; } = "";
        public string ProjectName { get; set; } = "";

        public List<SovLine> Lines { get; set; } = new List<SovLine>();

        /// <summary>Retention as a percentage of gross (typical 3% or 5%).</summary>
        public double RetentionPercent { get; set; } = 3.0;

        /// <summary>Retention release threshold (% of contract completed at which retention halves).</summary>
        public double HalfRetentionAtPercent { get; set; } = 100.0;

        /// <summary>Other deductions (LDs, contra-charges).</summary>
        public double OtherDeductions { get; set; }

        /// <summary>VAT rate applied to net amount before deductions.</summary>
        public double VatPercent { get; set; } = 20.0;

        public PaymentCertStatus Status { get; set; } = PaymentCertStatus.Draft;
        public int? SupersededByCertNumber { get; set; }

        public string SignedByContractor { get; set; } = "";
        public DateTime? ContractorSignedDate { get; set; }
        public string SignedByEmployer { get; set; } = "";
        public DateTime? EmployerSignedDate { get; set; }

        public string Note { get; set; } = "";

        // ── Derived totals ──────────────────────────────────────────

        public double GrossValuation => Math.Round(Lines.Sum(l => l.GrossThisCert), 2);

        /// <summary>Sum of all previously-certified values across SOV lines.</summary>
        public double CumulativePreviouslyCertified
            => Math.Round(Lines.Sum(l => l.PreviouslyCertified), 2);

        public double OverallPercentComplete
        {
            get
            {
                double contractTotal = Lines.Sum(l => l.ContractValue);
                if (contractTotal <= 0) return 0;
                double earned = Lines.Sum(l => l.ContractValue * l.PercentComplete / 100.0);
                return Math.Round(earned / contractTotal * 100.0, 2);
            }
        }

        /// <summary>Effective retention rate — halves above HalfRetentionAtPercent.</summary>
        public double EffectiveRetentionPercent
            => OverallPercentComplete >= HalfRetentionAtPercent ? RetentionPercent / 2.0 : RetentionPercent;

        public double RetentionAmount
            => Math.Round(GrossValuation * EffectiveRetentionPercent / 100.0, 2);

        public double NetThisCert
            => Math.Round(GrossValuation - RetentionAmount - OtherDeductions, 2);

        public double VatAmount
            => Math.Round(NetThisCert * VatPercent / 100.0, 2);

        public double TotalPayable
            => Math.Round(NetThisCert + VatAmount, 2);
    }

    /// <summary>
    /// Persistent retention ledger — tracks all retention withheld vs
    /// released across the certificate series.
    /// </summary>
    public class RetentionLedger
    {
        public string ContractRef { get; set; } = "";
        public string Currency { get; set; } = "GBP";
        public List<RetentionEntry> Entries { get; set; } = new List<RetentionEntry>();

        public double TotalWithheld => Entries.Where(e => e.Kind == "withhold").Sum(e => e.Amount);
        public double TotalReleased => Entries.Where(e => e.Kind == "release").Sum(e => e.Amount);
        public double Balance => Math.Round(TotalWithheld - TotalReleased, 2);
    }

    public class RetentionEntry
    {
        public int CertNumber { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string Kind { get; set; } = "withhold"; // "withhold" | "release"
        public double Amount { get; set; }
        public string Reason { get; set; } = "";
    }
}
