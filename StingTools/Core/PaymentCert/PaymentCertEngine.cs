// ══════════════════════════════════════════════════════════════════════════
//  PaymentCertEngine.cs — Build / save / load / supersede payment certs.
//
//  Per cert:
//    1. Sequence number monotonically increases per contract.
//    2. Each SovLine reads PercentComplete from elements (via the new
//       ASS_PMT_PCT_COMPLETE_NR shared param) when bound to a BOQ row.
//    3. Retention auto-halves once OverallPercentComplete crosses
//       HalfRetentionAtPercent (JCT 2024 §4.10 / NEC4 X16 / FIDIC §14.9).
//    4. Issue stamps element params:
//         ASS_PMT_CERT_NO_NR     ← cert number
//         ASS_PMT_CERT_DATE_DT   ← cert valuation date
//         ASS_PMT_LAST_VALUED_DT ← timestamp
//
//  P5.1 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;

namespace StingTools.Core.PaymentCert
{
    internal static class PaymentCertEngine
    {
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        // ── Create ─────────────────────────────────────────────────────

        /// <summary>
        /// Build a new draft certificate. Loads the previous-cert
        /// cumulative-paid figures so this one is correctly subtractive.
        /// </summary>
        public static PaymentCertificate CreateDraft(Document doc, string contractRef,
            ContractForm form, List<SovLine> currentSov)
        {
            var prior = ListCerts(doc, contractRef)
                .Select(p => Load(p))
                .Where(c => c != null && c.Status != PaymentCertStatus.Superseded)
                .OrderByDescending(c => c.CertNumber)
                .ToList();

            // Carry previously-certified per SOV line.
            var priorByLine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (prior.Count > 0)
            {
                foreach (var line in prior[0].Lines)
                {
                    priorByLine[line.Section] =
                        line.PreviouslyCertified + line.GrossThisCert - line.MaterialsOnSite;
                }
            }
            foreach (var line in currentSov)
            {
                priorByLine.TryGetValue(line.Section, out double prev);
                line.PreviouslyCertified = prev;
            }

            int nextNum = (prior.FirstOrDefault()?.CertNumber ?? 0) + 1;
            return new PaymentCertificate
            {
                ContractRef = contractRef,
                Form = form,
                CertNumber = nextNum,
                ValuationDate = DateTime.UtcNow,
                ProjectName = doc?.ProjectInformation?.Name ?? "",
                Lines = currentSov,
                Currency = "GBP",
                RetentionPercent = 3.0,
                HalfRetentionAtPercent = 100.0
            };
        }

        /// <summary>
        /// Build SOV lines from BOQ sections — each NRM2 section
        /// becomes one SOV line with ContractValue = its
        /// snapshot total. Caller can edit before persistence.
        /// </summary>
        public static List<SovLine> SovFromSnapshot(BOQ.BOQDocument snapshot)
        {
            if (snapshot == null) return new List<SovLine>();
            return snapshot.Sections.Select(s => new SovLine
            {
                Section = string.IsNullOrEmpty(s.NRM2Section) ? s.Name : s.NRM2Section,
                Description = s.Name,
                ContractValue = s.TotalUGX,
                PercentComplete = 0,
                PreviouslyCertified = 0,
                MaterialsOnSite = 0
            }).ToList();
        }

        // ── Persistence ────────────────────────────────────────────────

        public static string Save(Document doc, PaymentCertificate cert)
        {
            if (doc == null || cert == null) throw new ArgumentNullException();
            string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "payment_certs");
            Directory.CreateDirectory(dir);
            string safeRef = SafeName(cert.ContractRef);
            string path = Path.Combine(dir,
                $"pmt_cert_{safeRef}_{cert.CertNumber:D4}_{cert.ValuationDate:yyyyMMdd}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(cert, _jsonSettings));
            StingLog.Info(
                $"Payment cert {cert.CertNumber} ({cert.ContractRef}) saved — " +
                $"gross {cert.Currency} {cert.GrossValuation:N2}, " +
                $"retention {cert.RetentionAmount:N2}, payable {cert.TotalPayable:N2}");
            return path;
        }

        public static PaymentCertificate Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<PaymentCertificate>(File.ReadAllText(path), _jsonSettings); }
            catch (Exception ex)
            {
                StingLog.Warn($"PaymentCertEngine.Load({Path.GetFileName(path)}): {ex.Message}");
                return null;
            }
        }

        public static List<string> ListCerts(Document doc, string contractRef = null)
        {
            try
            {
                string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "payment_certs");
                if (!Directory.Exists(dir)) return new List<string>();
                string pattern = string.IsNullOrEmpty(contractRef)
                    ? "pmt_cert_*.json"
                    : $"pmt_cert_{SafeName(contractRef)}_*.json";
                return Directory.EnumerateFiles(dir, pattern)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch (Exception ex) { StingLog.Warn($"ListCerts: {ex.Message}"); return new List<string>(); }
        }

        // ── Retention ledger ───────────────────────────────────────────

        public static RetentionLedger ComputeLedger(Document doc, string contractRef)
        {
            var ledger = new RetentionLedger { ContractRef = contractRef };
            var paths = ListCerts(doc, contractRef);
            foreach (var p in paths)
            {
                var cert = Load(p);
                if (cert == null || cert.Status == PaymentCertStatus.Superseded ||
                    cert.Status == PaymentCertStatus.Draft) continue;
                ledger.Entries.Add(new RetentionEntry
                {
                    CertNumber = cert.CertNumber,
                    Date = cert.ValuationDate,
                    Kind = "withhold",
                    Amount = cert.RetentionAmount,
                    Reason = $"Cert {cert.CertNumber} retention @ {cert.EffectiveRetentionPercent}%"
                });
            }
            return ledger;
        }

        // ── Element write-back ─────────────────────────────────────────

        /// <summary>
        /// Stamp every BOQ-bound element with the cert number + date so
        /// future BOQ rebuilds know what was last valued.
        /// Caller must have an active transaction.
        /// </summary>
        public static int StampElements(Document doc, PaymentCertificate cert,
            Dictionary<string, IList<long>> elementIdsBySection)
        {
            if (doc == null || cert == null || elementIdsBySection == null) return 0;
            int stamped = 0;
            string today = cert.ValuationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var line in cert.Lines)
            {
                if (!elementIdsBySection.TryGetValue(line.Section, out var ids)) continue;
                foreach (var idVal in ids)
                {
                    try
                    {
                        var el = doc.GetElement(new ElementId(idVal));
                        if (el == null) continue;
                        if (TryWriteInt(el, ParamRegistry.PMT_CERT_NO_NR, cert.CertNumber)) stamped++;
                        TryWriteString(el, ParamRegistry.PMT_CERT_DATE_DT, today);
                        TryWriteString(el, ParamRegistry.PMT_LAST_VALUED_DT, today);
                        TryWriteNumber(el, ParamRegistry.PMT_PCT_COMPLETE_NR, line.PercentComplete);
                    }
                    catch (Exception ex) { StingLog.Warn($"StampElements {idVal}: {ex.Message}"); }
                }
            }
            return stamped;
        }

        private static bool TryWriteInt(Element el, string p, int v)
        {
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return false;
                par.Set(v);
                return true;
            }
            catch { return false; }
        }

        private static void TryWriteNumber(Element el, string p, double v)
        {
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return;
                par.Set(v);
            }
            catch { /* swallow — best-effort */ }
        }

        private static void TryWriteString(Element el, string p, string v)
        {
            try
            {
                Parameter par = el.LookupParameter(p);
                if (par == null || par.IsReadOnly) return;
                par.Set(v ?? "");
            }
            catch { /* swallow */ }
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "contract";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }));
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(invalid.Contains(c) ? '-' : c);
            return sb.ToString().Trim('-');
        }
    }
}
