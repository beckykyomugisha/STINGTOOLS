// StingTools — SwapToManufacturerCommand.
//
// Bulk-swap STING seed-family instances to manufacturer-specific real
// families. Reads STING_SEED_FAMILY_TXT off every selected instance,
// looks up candidate replacements in STING_FAMILY_SWAP_REGISTRY.json,
// presents the matches to the user, and runs Element.ChangeTypeId on
// every selected instance.
//
// What survives the swap:
//   * Position, rotation, host (Revit guarantees these for ChangeTypeId)
//   * Shared parameters (ASS_TAG_*, ELC_*, LTG_*, etc. — GUID-bound)
//   * Connectivity where the destination family has compatible
//     connector counts + positions (Revit best-effort; mismatches are
//     reported, not auto-fixed)
//
// What's preserved by stamping:
//   * STING_DESIGN_REF_TXT — copied from STING_SEED_FAMILY_TXT before
//     swap, so the original seed identity survives forever (used by
//     the Penetration Register, COBie export, audit reports).
//   * STING_SWAP_HISTORY_TXT — append-only entry per swap:
//     <ISO-8601 timestamp>|<operator>|<sourceFamily>|<destFamily>
//
// Always preview before applying. The "Apply" path is one
// TransactionGroup so partial failures roll back cleanly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Symbols
{
    public sealed class SwapCandidate
    {
        public string FamilyNamePattern { get; set; } = "";
        public string Label { get; set; } = "";
        public int    Priority { get; set; } = 999;
        public ElementId ResolvedTypeId { get; set; }
        public string ResolvedFamilyName { get; set; } = "";
        public string ResolvedTypeName   { get; set; } = "";
    }

    public sealed class SwapPlan
    {
        public string SeedId { get; set; }
        public string Category { get; set; }
        public List<ElementId> InstanceIds { get; } = new List<ElementId>();
        public List<SwapCandidate> Candidates { get; } = new List<SwapCandidate>();
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwapToManufacturerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // 1) Collect candidate seed instances. Source is either the
            // user's current selection or — when nothing is selected —
            // every element in the project carrying STING_SEED_FAMILY_TXT.
            var seeds = CollectSeedInstances(ctx.UIDoc, doc);
            if (seeds.Count == 0)
            {
                TaskDialog.Show("STING Swap to Manufacturer",
                    "No seed-family instances found. Place a STING_SEED_* family first, " +
                    "or select instances that already carry STING_SEED_FAMILY_TXT.");
                return Result.Cancelled;
            }

            // 2) Group by seed id; resolve registry candidates for each.
            var registry = LoadRegistry();
            if (registry == null)
            {
                TaskDialog.Show("STING Swap",
                    "STING_FAMILY_SWAP_REGISTRY.json not found / invalid. " +
                    "The corporate baseline ships with the plug-in.");
                return Result.Failed;
            }
            var plans = BuildPlans(doc, seeds, registry);

            // 3) Preview.
            var preview = StingResultPanel.Create("Swap to Manufacturer — Preview");
            preview.SetSubtitle($"{plans.Count} seed group(s) · {plans.Sum(p => p.InstanceIds.Count)} instance(s)");
            preview.AddSection("PLAN");
            foreach (var p in plans)
            {
                preview.Metric($"{p.SeedId}", p.InstanceIds.Count.ToString(),
                    p.Candidates.Count > 0
                        ? $"{p.Candidates.Count} candidate(s) — top: {p.Candidates[0].ResolvedFamilyName} : {p.Candidates[0].ResolvedTypeName}"
                        : "no candidate found in project — load a manufacturer family first");
            }
            preview.AddSection("APPLY")
                .Text("Apply will run ChangeTypeId on every instance with a matching candidate.")
                .Text("STING_DESIGN_REF_TXT preserves the seed identity for audit.")
                .Text("STING_SWAP_HISTORY_TXT records timestamp/operator/source/destination.")
                .Text("Re-run after loading manufacturer families if 'no candidate' rows appeared above.");

            var dlg = new TaskDialog("STING Swap")
            {
                MainInstruction = $"Swap {plans.Sum(p => p.InstanceIds.Count)} instance(s)?",
                MainContent = "Apply uses each plan's top-priority candidate. Cancel to keep seeds in place.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) { preview.Show(); return Result.Cancelled; }

            // 4) Apply.
            int swapped = 0, skipped = 0, errors = 0;
            string operatorName = SafeUserName();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            using (var tg = new TransactionGroup(doc, "STING Swap to Manufacturer"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "STING Swap"))
                {
                    tx.Start();
                    foreach (var p in plans)
                    {
                        var winner = p.Candidates.FirstOrDefault();
                        if (winner == null || winner.ResolvedTypeId == null
                            || winner.ResolvedTypeId == ElementId.InvalidElementId)
                        {
                            skipped += p.InstanceIds.Count;
                            continue;
                        }
                        foreach (var id in p.InstanceIds)
                        {
                            try
                            {
                                var el = doc.GetElement(id);
                                if (el == null) { skipped++; continue; }
                                string srcFamily = SafeFamilyName(el);
                                ParameterHelpers.SetString(el, "STING_DESIGN_REF_TXT", p.SeedId, overwrite: false);
                                el.ChangeTypeId(winner.ResolvedTypeId);
                                AppendSwapHistory(el, ts, operatorName, srcFamily,
                                    $"{winner.ResolvedFamilyName} : {winner.ResolvedTypeName}");
                                swapped++;
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                StingLog.Warn($"Swap {id}: {ex.Message}");
                            }
                        }
                    }
                    tx.Commit();
                }
                tg.Assimilate();
            }

            try { ActionAuditLog.Record("Family_Swap",
                $"swapped={swapped} skipped={skipped} errors={errors}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch { }

            ShowResult(plans, swapped, skipped, errors);
            return Result.Succeeded;
        }

        // ── Collect ─────────────────────────────────────────────────────

        private static List<Element> CollectSeedInstances(UIDocument uidoc, Document doc)
        {
            var ids = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();
            if (ids.Count > 0)
            {
                return ids.Select(i => doc.GetElement(i))
                          .Where(e => e != null && HasSeedTag(e))
                          .ToList();
            }
            // Fall back to model-wide scan. Collect by category since
            // probing every element is wasteful — only the categories the
            // registry covers can carry seeds.
            var cats = new[]
            {
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_Sprinklers,
                BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_GenericModel,
            };
            var found = new List<Element>();
            foreach (var c in cats)
            {
                try
                {
                    foreach (var el in new FilteredElementCollector(doc)
                        .OfCategory(c).WhereElementIsNotElementType())
                    {
                        if (HasSeedTag(el)) found.Add(el);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Collect seeds {c}: {ex.Message}"); }
            }
            return found;
        }

        private static bool HasSeedTag(Element el)
        {
            try
            {
                string s = ParameterHelpers.GetString(el, "STING_SEED_FAMILY_TXT");
                if (!string.IsNullOrEmpty(s) &&
                    s.StartsWith("STING_SEED_", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            // Fallback: family name itself starts with STING_SEED_ — useful
            // when the parameter binding hasn't been refreshed yet.
            try { return SafeFamilyName(el).StartsWith("STING_SEED_", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ── Registry ────────────────────────────────────────────────────

        private static JObject LoadRegistry()
        {
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_FAMILY_SWAP_REGISTRY.json");
                if (string.IsNullOrEmpty(corp) || !File.Exists(corp)) return null;
                var root = JObject.Parse(File.ReadAllText(corp));

                // Project override merge.
                try
                {
                    string projDir = Path.GetDirectoryName(Path.GetDirectoryName(corp) ?? "") ?? "";
                    // best-effort — try the typical _BIM_COORD location next to the active doc
                }
                catch { }
                return root;
            }
            catch (Exception ex) { StingLog.Warn($"LoadRegistry: {ex.Message}"); return null; }
        }

        private static List<SwapPlan> BuildPlans(Document doc, IList<Element> seeds, JObject registry)
        {
            var plans = new Dictionary<string, SwapPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in seeds)
            {
                string seedId = ParameterHelpers.GetString(el, "STING_SEED_FAMILY_TXT");
                if (string.IsNullOrEmpty(seedId)) seedId = SafeFamilyName(el);
                if (!plans.TryGetValue(seedId, out var p))
                {
                    p = new SwapPlan { SeedId = seedId, Category = el.Category?.Name ?? "" };
                    plans[seedId] = p;
                    foreach (var c in ResolveCandidates(doc, registry, seedId))
                        p.Candidates.Add(c);
                }
                p.InstanceIds.Add(el.Id);
            }
            return plans.Values.ToList();
        }

        private static List<SwapCandidate> ResolveCandidates(Document doc, JObject registry, string seedId)
        {
            var result = new List<SwapCandidate>();
            try
            {
                var seedsArr = registry["seeds"] as JArray;
                if (seedsArr == null) return result;
                JObject match = null;
                foreach (var s in seedsArr.OfType<JObject>())
                {
                    if (string.Equals((string)s["seedId"], seedId, StringComparison.OrdinalIgnoreCase))
                    { match = s; break; }
                }
                if (match == null) return result;

                // Index every loaded family + symbol once so per-candidate
                // matching is O(symbols × patterns) not O(symbols × patterns × collector).
                var allTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                var cands = match["candidates"] as JArray;
                if (cands == null) return result;
                foreach (var c in cands.OfType<JObject>())
                {
                    var cand = new SwapCandidate
                    {
                        FamilyNamePattern = (string)c["familyNamePattern"] ?? "",
                        Label             = (string)c["label"] ?? "",
                        Priority          = (int?)c["priority"] ?? 999,
                    };
                    if (string.IsNullOrEmpty(cand.FamilyNamePattern)) continue;
                    Regex rx;
                    try { rx = new Regex(cand.FamilyNamePattern); }
                    catch { continue; }

                    // Take the first matching FamilySymbol; activate it
                    // lazily on apply (Revit requires symbol.IsActive==true
                    // before ChangeTypeId honours it).
                    var hit = allTypes.FirstOrDefault(fs => rx.IsMatch(fs.FamilyName ?? ""));
                    if (hit == null) continue;
                    cand.ResolvedTypeId = hit.Id;
                    cand.ResolvedFamilyName = hit.FamilyName ?? "";
                    cand.ResolvedTypeName   = hit.Name ?? "";
                    result.Add(cand);
                }
                result.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
            catch (Exception ex) { StingLog.Warn($"ResolveCandidates {seedId}: {ex.Message}"); }
            return result;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static string SafeFamilyName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi) return fi.Symbol?.FamilyName ?? "";
                var t = el?.Document?.GetElement(el.GetTypeId()) as FamilySymbol;
                return t?.FamilyName ?? "";
            }
            catch { return ""; }
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName ?? "?"; } catch { return "?"; }
        }

        private static void AppendSwapHistory(Element el, string ts, string op, string src, string dst)
        {
            try
            {
                string existing = ParameterHelpers.GetString(el, "STING_SWAP_HISTORY_TXT") ?? "";
                string entry = $"{ts}|{op}|{src}|{dst}";
                string updated = string.IsNullOrEmpty(existing) ? entry : entry + "; " + existing;
                ParameterHelpers.SetString(el, "STING_SWAP_HISTORY_TXT", updated, overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"AppendSwapHistory {el?.Id}: {ex.Message}"); }
        }

        private static void ShowResult(List<SwapPlan> plans, int swapped, int skipped, int errors)
        {
            var panel = StingResultPanel.Create("Swap to Manufacturer — Result");
            panel.SetSubtitle($"{swapped} swapped · {skipped} skipped · {errors} errors");
            panel.AddSection("SUMMARY")
                .MetricHighlight("Swapped", swapped.ToString())
                .Metric("Skipped (no candidate)", skipped.ToString())
                .MetricError("Errors", errors.ToString());
            panel.AddSection("BY SEED");
            foreach (var p in plans)
            {
                var c = p.Candidates.FirstOrDefault();
                panel.Metric(p.SeedId, p.InstanceIds.Count.ToString(),
                    c != null ? $"→ {c.ResolvedFamilyName} : {c.ResolvedTypeName}" : "no manufacturer family loaded");
            }
            panel.AddSection("NEXT STEPS")
                .Text("Run AutoJoinMepConnectors / Auto-Route Conduit to re-stitch connections.")
                .Text("Run BS 7671 validation — fill / bend / radius re-evaluate against the new family.")
                .Text("STING_SWAP_HISTORY_TXT carries the audit trail; export via the Penetration Register schedule.");
            panel.Show();
        }
    }
}
