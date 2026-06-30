// ══════════════════════════════════════════════════════════════════════════
//  BoqSnapshotHasher.cs — Canonical SHA-256 checksum for BOQ snapshots.
//
//  Two snapshots produced by the same inputs must produce the same
//  checksum so:
//    (a) the server can detect duplicate pushes (POST is idempotent),
//    (b) plugin and server can reconcile baseline state without a full
//        line-by-line diff.
//
//  Canonicalisation strategy:
//    - Build a normalised projection of BOQDocument that excludes wall-clock
//      fields (LastCosted, SnapshotDate) and any field carrying user-display
//      formatting (Note, ResolvedNRM2Paragraph).
//    - Sort sections by NRM2Section then Discipline, items by Id within
//      section.
//    - Format numbers with invariant culture, fixed precision.
//    - Serialise as compact JSON (no whitespace), UTF-8.
//    - SHA-256 hex digest.
//
//  Lower-case hex matches the server's BoqBaseline.Checksum convention.
//
//  P1 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ.Sync
{
    internal static class BoqSnapshotHasher
    {
        private static readonly JsonSerializerSettings _canonical = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            // PM-1 — DO NOT drop default/zero values. An unmeasured (qty 0) or
            // zero-rate row was invisible to the checksum, so a model change that
            // only zeroed a line slipped past dedupe/drift detection. Zeros are
            // now part of the canonical projection.
            DefaultValueHandling = DefaultValueHandling.Include,
            Culture = CultureInfo.InvariantCulture
        };

        /// <summary>
        /// Compute the lower-case hex SHA-256 of a canonical BOQDocument
        /// projection. Returns empty string on failure — callers should
        /// log and continue (checksum is advisory, not load-bearing).
        /// </summary>
        public static string ComputeChecksum(BOQDocument boq)
        {
            if (boq == null) return "";
            try
            {
                var projection = BuildProjection(boq);
                string canonicalJson = JsonConvert.SerializeObject(projection, _canonical);
                byte[] bytes = Encoding.UTF8.GetBytes(canonicalJson);
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(bytes);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BoqSnapshotHasher.ComputeChecksum: {ex.Message}");
                return "";
            }
        }

        private static object BuildProjection(BOQDocument boq)
        {
            return new
            {
                project = boq.ProjectName ?? "",
                title = boq.DocumentTitle ?? "",
                snapType = boq.SnapshotType ?? "",
                snapLabel = boq.SnapshotLabel ?? "",
                budget = Round(boq.ProjectBudgetUGX, 0),
                prelim = Round(boq.PrelimPct, 2),
                contingency = Round(boq.ContingencyPct, 2),
                overhead = Round(boq.OverheadPct, 2),
                vat = Round(boq.VatPct, 2),            // WP1 — VAT now part of the canonical total → must be hashed
                currency = boq.Currency ?? "UGX",
                fx = Round(boq.ExchangeRateUgxPerUsd, 4),
                sections = boq.Sections
                    .OrderBy(s => s.NRM2Section, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.Discipline, StringComparer.OrdinalIgnoreCase)
                    .Select(s => new
                    {
                        nrm2 = s.NRM2Section ?? "",
                        name = s.Name ?? "",
                        disc = s.Discipline ?? "",
                        // P0-2 — order by a STABLE key, never BOQLineItem.Id
                        // (a fresh Guid.NewGuid() per build). UniqueId survives
                        // save/reopen; RevitElementId + BOQLineRef + ItemName
                        // disambiguate manual/PS rows that share an empty
                        // UniqueId. The random Id is also dropped from the
                        // hashed projection below so two builds of an unchanged
                        // model produce the same checksum.
                        items = s.Items
                            .OrderBy(i => i.UniqueId ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(i => i.RevitElementId)
                            .ThenBy(i => i.BOQLineRef ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(i => i.Category ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(i => i.ItemName ?? "", StringComparer.OrdinalIgnoreCase)
                            .Select(i => new
                            {
                                cat = i.Category ?? "",
                                disc = i.Discipline ?? "",
                                ref_ = i.BOQLineRef ?? "",
                                fam = i.FamilyName ?? "",
                                type = i.TypeName ?? "",
                                qty = Round(i.Quantity, 6),
                                unit = i.Unit ?? "",
                                rate = Round(i.RateUGX, 2),
                                source = i.RateSource ?? "",
                                src = (int)i.Source,
                                uid = i.UniqueId ?? "",
                                lvl = i.Level ?? "",
                                loc = i.Location ?? "",
                                carbon = Round(i.EmbodiedCarbonKg, 4)
                            })
                            .ToArray()
                    })
                    .ToArray()
            };
        }

        private static double Round(double v, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return Math.Round(v, decimals, MidpointRounding.AwayFromZero);
        }
    }
}
