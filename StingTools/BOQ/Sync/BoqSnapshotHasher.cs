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
            DefaultValueHandling = DefaultValueHandling.Ignore,
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
                        items = s.Items
                            .OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                            .Select(i => new
                            {
                                id = i.Id ?? "",
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
