// ══════════════════════════════════════════════════════════════════════════
//  VariationEngine.cs — Mint variations from BOQSnapshotDiff clusters,
//  save / load JSON sidecars, advance through the state machine.
//
//  Persistence path: <project>/_bim_manager/variations/<vo-number>.json.
//  Star-rate build-ups live alongside in star_rates/ so they can be
//  referenced from multiple VOs.
//
//  P5.2 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.BOQ;

namespace StingTools.Core.Variation
{
    internal static class VariationEngine
    {
        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        // ── Build from snapshot diff ──────────────────────────────────

        /// <summary>
        /// Mint a draft variation from a BOQSnapshotDiff. Each category
        /// diff with non-zero net change becomes one VariationItem. New
        /// items (ChangeType = NewItem) use their current rate; revisions
        /// use the delta rate × current quantity.
        /// </summary>
        public static VariationInstruction FromDiff(BOQSnapshotDiff diff, string contractRef,
            VariationKind kind = VariationKind.Instruction)
        {
            if (diff == null) throw new ArgumentNullException(nameof(diff));
            var vo = new VariationInstruction
            {
                ContractRef = contractRef ?? "",
                Kind = kind,
                Status = VariationStatus.Draft,
                Title = $"Variation from diff {diff.LabelA} → {diff.LabelB}",
                Description =
                    $"Auto-minted from BOQSnapshotDiff. Net change: " +
                    $"{diff.NetChange:N0} ({diff.NetChangePct:F1}%).",
                InstructionDate = DateTime.UtcNow,
                Currency = "GBP",
                IssuedBy = Environment.UserName ?? "",
                SourceSnapshotDiff = $"{diff.LabelA}→{diff.LabelB}"
            };

            foreach (var c in diff.CategoryDiffs)
            {
                if (Math.Abs(c.Delta) < 0.01) continue;
                var item = new VariationItem
                {
                    Description = $"{c.Name} ({c.NRM2Section}) — {c.ChangeType}",
                    Unit = "varies",
                    Quantity = c.QtyB,
                    UnitRate = c.RateB,
                    RateSource = c.ChangeType == BOQChangeType.NewItem
                        ? "BOQ" : "BOQ+pct"
                };
                // For revisions, the variation value is the DELTA, not the
                // full B-state value. Express that by overriding UnitRate
                // to the rate delta when quantity is unchanged.
                if (c.ChangeType == BOQChangeType.RateRevised &&
                    Math.Abs(c.QtyA - c.QtyB) < 0.001)
                {
                    item.Quantity = c.QtyB;
                    item.UnitRate = c.RateB - c.RateA;
                    item.RateSource = "Delta";
                }
                vo.Items.Add(item);
            }
            return vo;
        }

        // ── Numbering ─────────────────────────────────────────────────

        /// <summary>
        /// Allocate the next VO number for this contract. Format
        /// VO-{kind-prefix}-{NNNN}; e.g. "VO-CE-0042" for CompensationEvent.
        /// </summary>
        public static string AllocateNumber(Document doc, string contractRef, VariationKind kind)
        {
            string prefix = kind switch
            {
                VariationKind.CompensationEvent  => "VO-CE",
                VariationKind.EngineerInstruction => "VO-EI",
                VariationKind.ContractorClaim    => "VO-CC",
                _                                => "VO-AI"
            };
            int max = 0;
            foreach (var path in ListVariations(doc, contractRef))
            {
                var v = Load(path);
                if (v == null || string.IsNullOrEmpty(v.Number)) continue;
                if (!v.Number.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = v.Number.Split('-');
                if (parts.Length >= 3 && int.TryParse(parts[parts.Length - 1], out int n))
                    max = Math.Max(max, n);
            }
            return $"{prefix}-{(max + 1):D4}";
        }

        // ── Persistence ───────────────────────────────────────────────

        public static string Save(Document doc, VariationInstruction vo)
        {
            string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "variations");
            Directory.CreateDirectory(dir);
            if (string.IsNullOrEmpty(vo.Number))
                vo.Number = AllocateNumber(doc, vo.ContractRef, vo.Kind);
            string path = Path.Combine(dir, $"{SafeName(vo.Number)}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(vo, _json));
            StingLog.Info(
                $"Variation {vo.Number} ({vo.ContractRef}) saved — " +
                $"{vo.Items.Count} item(s), {vo.Currency} {vo.TotalValue:N2}, status {vo.Status}.");
            return path;
        }

        public static VariationInstruction Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<VariationInstruction>(File.ReadAllText(path), _json); }
            catch (Exception ex) { StingLog.Warn($"VariationEngine.Load: {ex.Message}"); return null; }
        }

        public static List<string> ListVariations(Document doc, string contractRef = null)
        {
            try
            {
                string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "variations");
                if (!Directory.Exists(dir)) return new List<string>();
                var all = Directory.EnumerateFiles(dir, "*.json").ToList();
                if (string.IsNullOrEmpty(contractRef)) return all;
                return all.Where(p =>
                {
                    var v = Load(p);
                    return v != null && string.Equals(v.ContractRef, contractRef, StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }
            catch (Exception ex) { StingLog.Warn($"ListVariations: {ex.Message}"); return new List<string>(); }
        }

        // ── Star rates ────────────────────────────────────────────────

        public static string SaveStarRate(Document doc, StarRate rate)
        {
            string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "star_rates");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{rate.Id}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(rate, _json));
            return path;
        }

        public static StarRate LoadStarRate(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<StarRate>(File.ReadAllText(path), _json); }
            catch (Exception ex) { StingLog.Warn($"VariationEngine.LoadStarRate: {ex.Message}"); return null; }
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "variation";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }));
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(invalid.Contains(c) ? '-' : c);
            return sb.ToString().Trim('-');
        }
    }
}
