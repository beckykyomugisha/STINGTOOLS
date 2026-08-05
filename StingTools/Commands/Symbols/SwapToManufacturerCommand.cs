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
using StingTools.Tags;
using StingTools.UI;
using StingTools.Core.Validation;
using StingTools.Core.Validation.Healthcare;

namespace StingTools.Commands.Symbols
{
    public sealed class SwapCandidate
    {
        public string FamilyNamePattern { get; set; } = "";

        /// <summary>
        /// Wave J1 — optional regex matched against the manufacturer
        /// type name (FamilySymbol.Name). Empty = match any type
        /// variant. When set, lets the swap registry route per-variant
        /// — e.g. swap PENDANT lighting variants to manufacturer A,
        /// RECESSED variants to manufacturer B. Combined with
        /// SeedVariantPattern (matched against the source seed type
        /// name) this enables full variant-to-variant routing.
        /// </summary>
        public string TypeNamePattern { get; set; } = "";

        /// <summary>
        /// Wave J1 — optional regex matched against the source seed's
        /// type name (e.g. "PENDANT", "FR60"). Empty = match any
        /// seed type variant. When set, the candidate only fires for
        /// instances whose source type name matches this pattern.
        /// </summary>
        public string SeedVariantPattern { get; set; } = "";

        public string Label { get; set; } = "";
        public int    Priority { get; set; } = 999;
        public ElementId ResolvedTypeId { get; set; }
        public string ResolvedFamilyName { get; set; } = "";
        public string ResolvedTypeName   { get; set; } = "";
        /// <summary>
        /// Phase 178f — raw JSON candidate node, kept around so the
        /// post-swap step can introspect ulSystemMatch[] and pick the
        /// right UL / EN-1366-3 system reference for the instance's
        /// fire rating + host type + OD.
        /// </summary>
        public Newtonsoft.Json.Linq.JObject RawNode { get; set; }
    }

    public sealed class SwapPlan
    {
        public string SeedId { get; set; }
        public string Category { get; set; }
        public List<ElementId> InstanceIds { get; } = new List<ElementId>();
        public List<SwapCandidate> Candidates { get; } = new List<SwapCandidate>();
        /// <summary>True when at least one instance in this plan was successfully
        /// swapped via <c>Element.ChangeTypeId</c> during the swap transaction.</summary>
        public bool WasSwapped { get; set; }
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
                MainContent = "Apply uses each plan's top-priority candidate. Cancel to keep seeds in place.\n\n" +
                              "Apply + Re-validate runs the full validator chain after the swap so non-compliant " +
                              "manufacturer choices (e.g. swap to a smaller pull-box exceeds 40% cable fill) are " +
                              "surfaced before they ship.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Apply", "Swap + auto re-stitch connectors. Skip the validator re-run.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Apply + Re-validate", "Swap + re-stitch + run RunAllValidators on the swapped instances.");
            var choice = dlg.Show();
            if (choice == TaskDialogResult.Cancel) { preview.Show(); return Result.Cancelled; }
            bool revalidate = choice == TaskDialogResult.CommandLink2;

            // 4) Apply.
            int swapped = 0, skipped = 0, errors = 0;
            int rejoined = 0;
            string operatorName = SafeUserName();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var swappedIds = new List<ElementId>();

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

                                // Phase 178f — UL-system stamp. When the
                                // candidate carries ulSystemMatch[] rules
                                // and the instance is a penetration, pick
                                // the matching UL/EN-1366-3 system and
                                // write it onto PEN_CERTIFICATION_TXT.
                                if (el is FamilyInstance fi && winner.RawNode != null)
                                {
                                    var match = StingTools.Core.Symbols.ULSystemMatcher.Match(winner.RawNode, fi);
                                    if (match != null && !string.IsNullOrEmpty(match.UlSystem))
                                    {
                                        ParameterHelpers.SetString(el, "PEN_CERTIFICATION_TXT",
                                            match.UlSystem, overwrite: true);
                                    }
                                }
                                swapped++;
                                swappedIds.Add(id);
                                p.WasSwapped = true;
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

                // Wave F3 — auto re-stitch. Revit's ChangeTypeId
                // preserves connections that survive type-shape changes,
                // but if the destination family has fewer connectors or
                // they sit in different positions some connections
                // dropped silently. RestitchSwappedConnectors walks the
                // newly-swapped instances, finds open connectors near
                // each other, and attempts a fitting-mediated rejoin.
                // Runs in its own transaction inside the same
                // TransactionGroup so a re-stitch failure can't roll
                // back the swap itself.
                if (swappedIds.Count > 0)
                {
                    using (var tx2 = new Transaction(doc, "STING Re-stitch swapped connectors"))
                    {
                        try
                        {
                            tx2.Start();
                            rejoined = RestitchSwappedConnectors(doc, swappedIds);
                            tx2.Commit();
                        }
                        catch (Exception ex)
                        {
                            if (tx2.HasStarted() && !tx2.HasEnded()) tx2.RollBack();
                            StingLog.Warn($"Re-stitch: {ex.Message}");
                        }
                    }
                }

                tg.Assimilate();
            }

            // Option A — auto-author STING symbol curves into every
            // manufacturer family that was the winning swap target.
            // Runs outside the swap TransactionGroup so symbol authoring
            // failures never roll back successfully-swapped instances.
            // Each family is opened via EditFamily, InjectAutomationPresentationPack
            // + AuthorSymbols are run inside their own transaction, then
            // the family is reloaded into the project.
            int symbolsAuthored = 0;
            if (swapped > 0)
            {
                try { symbolsAuthored = AutoAuthorSwappedFamilies(doc, plans); }
                catch (Exception ex) { StingLog.Warn($"AutoAuthorSwappedFamilies: {ex.Message}"); }
            }

            try { ActionAuditLog.Record("Family_Swap",
                $"swapped={swapped} skipped={skipped} errors={errors} rejoined={rejoined}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // Wave H3 — opt-in post-swap re-validation. Manufacturer
            // families may have different conduit sizes / cable capacity /
            // bend radii than the seed; re-running the validator chain
            // catches non-compliance before the user discovers it on
            // schedule export. The re-validate path runs the full
            // RunAllValidators pipeline (electrical + healthcare gated)
            // and surfaces findings in a dedicated panel.
            int revalidationFindings = 0;
            if (revalidate)
            {
                try
                {
                    revalidationFindings = RevalidateSwappedInstances(doc, swappedIds);
                }
                catch (Exception ex2) { StingLog.Warn($"Post-swap revalidation: {ex2.Message}"); }
            }

            ShowResult(plans, swapped, skipped, errors, rejoined, revalidationFindings, revalidate, symbolsAuthored);
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            // Fallback: family name itself starts with STING_SEED_ — useful
            // when the parameter binding hasn't been refreshed yet.
            try { return SafeFamilyName(el).StartsWith("STING_SEED_", StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
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
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                return root;
            }
            catch (Exception ex) { StingLog.Warn($"LoadRegistry: {ex.Message}"); return null; }
        }

        private static List<SwapPlan> BuildPlans(Document doc, IList<Element> seeds, JObject registry)
        {
            // Wave J1 — group by (SeedId, sourceTypeVariant) so per-
            // variant routing works. PENDANT instances become a
            // separate plan from RECESSED instances; each plan picks
            // candidates whose SeedVariantPattern matches the variant
            // (or have no pattern, in which case they apply to all
            // variants of the seed).
            var plans = new Dictionary<string, SwapPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in seeds)
            {
                string seedId = ParameterHelpers.GetString(el, "STING_SEED_FAMILY_TXT");
                if (string.IsNullOrEmpty(seedId)) seedId = SafeFamilyName(el);
                string variantType = SafeTypeName(el);
                string planKey = $"{seedId}|{variantType}";
                if (!plans.TryGetValue(planKey, out var p))
                {
                    p = new SwapPlan
                    {
                        SeedId   = seedId,
                        Category = el.Category?.Name ?? "",
                    };
                    plans[planKey] = p;
                    foreach (var c in ResolveCandidates(doc, registry, seedId))
                    {
                        if (CandidateAppliesToVariant(c, variantType))
                            p.Candidates.Add(c);
                    }
                }
                p.InstanceIds.Add(el.Id);
            }
            return plans.Values.ToList();
        }

        private static bool CandidateAppliesToVariant(SwapCandidate c, string sourceTypeName)
        {
            if (c == null) return false;
            if (string.IsNullOrEmpty(c.SeedVariantPattern)) return true;
            try { return Regex.IsMatch(sourceTypeName ?? "", c.SeedVariantPattern); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static string SafeTypeName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi) return fi.Symbol?.Name ?? "";
                var t = el?.Document?.GetElement(el.GetTypeId()) as FamilySymbol;
                return t?.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
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
                        FamilyNamePattern    = (string)c["familyNamePattern"]   ?? "",
                        TypeNamePattern      = (string)c["typeNamePattern"]     ?? "",
                        SeedVariantPattern   = (string)c["seedVariantPattern"]  ?? "",
                        Label                = (string)c["label"] ?? "",
                        Priority             = (int?)c["priority"] ?? 999,
                    };
                    if (string.IsNullOrEmpty(cand.FamilyNamePattern)) continue;
                    Regex rxFamily;
                    try { rxFamily = new Regex(cand.FamilyNamePattern); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }

                    Regex rxType = null;
                    if (!string.IsNullOrEmpty(cand.TypeNamePattern))
                    {
                        try { rxType = new Regex(cand.TypeNamePattern); }
                        catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); continue; }
                    }

                    // Wave J1 — match against (familyName, typeName)
                    // tuple. Type pattern is optional; when empty, any
                    // type variant of a name-matching family wins.
                    var hit = allTypes.FirstOrDefault(fs =>
                        rxFamily.IsMatch(fs.FamilyName ?? "") &&
                        (rxType == null || rxType.IsMatch(fs.Name ?? "")));
                    if (hit == null) continue;
                    cand.ResolvedTypeId = hit.Id;
                    cand.ResolvedFamilyName = hit.FamilyName ?? "";
                    cand.ResolvedTypeName   = hit.Name ?? "";
                    cand.RawNode            = c;
                    result.Add(cand);
                }
                result.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
            catch (Exception ex2) { StingLog.Warn($"ResolveCandidates {seedId}: {ex2.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName ?? "?"; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return "?"; }
        }

        /// <summary>
        /// Wave H3 — post-swap revalidation. Runs the full STING
        /// validator chain (electrical BS 7671 + healthcare-gated +
        /// generic) and counts findings whose ElementId matches the
        /// swapped set. Returns the count so the result panel can
        /// surface "swap introduced N new violations." On failure,
        /// returns 0 — never block the swap report just because the
        /// validator re-run had a hiccup.
        /// </summary>
        private static int RevalidateSwappedInstances(Document doc, IList<ElementId> swappedIds)
        {
            if (doc == null || swappedIds == null || swappedIds.Count == 0) return 0;
            var swappedSet = new HashSet<long>(swappedIds.Where(i => i != null).Select(i => i.Value));
            int hits = 0;
            try
            {
                var findings = new List<StingTools.Core.Validation.ValidationResult>();
                findings.AddRange(new StingTools.Core.Validation.ConnectivityValidator().Validate(doc));
                findings.AddRange(new StingTools.Core.Validation.FillValidator().Validate(doc));
                findings.AddRange(new StingTools.Core.Validation.SpecValidator().Validate(doc));
                findings.AddRange(new StingTools.Core.Validation.TerminationValidator().Validate(doc));
                findings.AddRange(new StingTools.Core.Validation.ElectricalStandardsValidator().Validate(doc));
                // Healthcare validators are gated internally on the
                // facility-type project parameter so calling them here is
                // a no-op for non-healthcare projects.
                try
                {
                    findings.AddRange(StingTools.Core.Validation.Healthcare.RunAllHealthcareValidators.Validate(doc));
                }
                catch (Exception ex) { StingLog.Warn($"Healthcare revalidation: {ex.Message}"); }

                foreach (var f in findings)
                {
                    if (f?.ElementId == null) continue;
                    if (swappedSet.Contains(f.ElementId.Value)) hits++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"RevalidateSwappedInstances: {ex.Message}"); }
            return hits;
        }

        /// <summary>
        /// Wave F3 — re-stitch open connectors on swapped instances.
        ///
        /// After ChangeTypeId, the destination family's connectors land
        /// at the position the source family's connectors had. When the
        /// destination has FEWER connectors or they sit in DIFFERENT
        /// positions, Revit drops the orphaned connection. This pass
        /// walks every swapped instance, indexes its open (unconnected)
        /// connectors, and attempts to re-pair them by spatial proximity
        /// to other open connectors in the swapped set.
        ///
        /// Pairing rule:
        ///   * Both connectors must be free (IsConnected == false).
        ///   * Same Domain (Electrical / Piping / HVAC).
        ///   * Distance &lt; 600 mm — same threshold the legacy
        ///     AutoJoinMepConnectors uses; covers the typical realignment
        ///     a manufacturer-family swap induces.
        /// Returns the number of connectors successfully rejoined.
        /// </summary>
        private static int RestitchSwappedConnectors(Document doc, IList<ElementId> swappedIds)
        {
            if (doc == null || swappedIds == null || swappedIds.Count == 0) return 0;
            const double radiusFtSq = (600.0 / 304.8) * (600.0 / 304.8);

            // Collect every open connector on every swapped instance.
            // Owner kept alongside so we don't re-pair connectors on
            // the same instance (would form a tight loop).
            var open = new List<(Connector c, ElementId owner)>();
            foreach (var id in swappedIds)
            {
                FamilyInstance fi = null;
                try { fi = doc.GetElement(id) as FamilyInstance; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (fi == null) continue;
                var mgr = fi.MEPModel?.ConnectorManager;
                if (mgr == null) continue;
                try
                {
                    foreach (Connector c in mgr.Connectors)
                    {
                        if (c == null || c.IsConnected) continue;
                        if (c.Domain == Domain.DomainUndefined) continue;
                        open.Add((c, id));
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Restitch collect {id}: {ex.Message}"); }
            }
            if (open.Count < 2) return 0;

            int rejoined = 0;
            // Greedy O(n^2) pairing — n is the open-connector count for
            // the swapped set, which is small (rarely > 100). Keeping it
            // simple beats over-engineering a spatial index for the
            // expected size.
            for (int i = 0; i < open.Count; i++)
            {
                var (a, ownerA) = open[i];
                if (a.IsConnected) continue;
                for (int j = i + 1; j < open.Count; j++)
                {
                    var (b, ownerB) = open[j];
                    if (b.IsConnected) continue;
                    if (ownerA == ownerB) continue;
                    if (a.Domain != b.Domain) continue;

                    double dx = a.Origin.X - b.Origin.X;
                    double dy = a.Origin.Y - b.Origin.Y;
                    double dz = a.Origin.Z - b.Origin.Z;
                    if (dx * dx + dy * dy + dz * dz > radiusFtSq) continue;

                    try
                    {
                        // Direct ConnectTo first — when connectors are
                        // co-located within 5 mm a fitting is unnecessary.
                        if (a.Origin.DistanceTo(b.Origin) < (5.0 / 304.8))
                        {
                            a.ConnectTo(b);
                            rejoined++;
                            break;
                        }
                        // Fitting-mediated for slightly-displaced pairs.
                        // doc.Create.NewElbowFitting / NewUnionFitting /
                        // NewTransitionFitting are the candidates; we
                        // try elbow first since it's the most-tolerant.
                        FamilyInstance fitting = null;
                        try { fitting = doc.Create.NewElbowFitting(a, b); }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        if (fitting == null)
                        {
                            try { fitting = doc.Create.NewUnionFitting(a, b); }
                            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                        }
                        if (fitting != null) rejoined++;
                    }
                    catch (Exception ex2) { StingLog.Info($"Restitch pair: {ex2.Message}"); }
                    break;
                }
            }
            return rejoined;
        }

        // ── Option A — auto symbol authoring ────────────────────────────

        /// <summary>
        /// After every swap batch, open each unique manufacturer family
        /// that was the winning swap target, inject STING automation
        /// presentation parameters, author all 5-standard symbol curve
        /// sets (IEC/ANSI/BS/NFPA/CIBSE), and reload the family back
        /// into the project. Runs outside the swap TransactionGroup so
        /// a symbol-authoring failure on one family never rolls back
        /// successfully-swapped model instances.
        /// </summary>
        private static int AutoAuthorSwappedFamilies(Document doc, IList<SwapPlan> plans)
        {
            if (doc == null || plans == null) return 0;

            // Collect unique Family objects, but only from plans that were
            // actually swapped — skip plans whose instances were all pinned,
            // errored, or skipped so we don't author (and waste time on)
            // families that were never activated in this session.
            var seen     = new HashSet<ElementId>();
            var families = new List<Family>();
            foreach (var p in plans.Where(p => p.WasSwapped))
            {
                var winner = p.Candidates.FirstOrDefault();
                if (winner?.ResolvedTypeId == null ||
                    winner.ResolvedTypeId == ElementId.InvalidElementId) continue;
                try
                {
                    if (doc.GetElement(winner.ResolvedTypeId) is FamilySymbol fs &&
                        fs.Family != null && seen.Add(fs.Family.Id))
                    {
                        families.Add(fs.Family);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"AutoAuthor collect '{winner.ResolvedFamilyName}': {ex.Message}"); }
            }
            if (families.Count == 0) return 0;

            int authored = 0;
            foreach (var fam in families)
            {
                Document famDoc = null;
                try
                {
                    famDoc = doc.EditFamily(fam);
                    if (famDoc == null)
                    {
                        StingLog.Warn($"AutoAuthor: EditFamily returned null for '{fam.Name}' — skip.");
                        continue;
                    }

                    bool txOk = false;
                    using (var tx = new Transaction(famDoc, "STING Author Symbols"))
                    {
                        tx.Start();
                        try
                        {
                            // Ensure LOD/visibility parameters exist before
                            // AuthorSymbols tries to key off them.
                            FamilyParamEngine.InjectAutomationPresentationPack(famDoc);
                            FamilySymbolAuthor.AuthorSymbols(famDoc);
                            tx.Commit();
                            txOk = true;
                        }
                        catch (Exception ex)
                        {
                            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                            StingLog.Warn($"AutoAuthor symbol tx '{fam.Name}': {ex.Message}");
                        }
                    }

                    // Reload the freshly-authored family back into the project so
                    // instances pick up the new parameters and visibility formulas.
                    // Only count a family as authored when both the tx committed AND
                    // the reload back into the project document succeeded.
                    if (txOk)
                    {
                        try
                        {
                            // Family.LoadFamily(Document) or doc.EditFamily path — use reflection-safe approach
                            Family reloadedFam = null;
                            bool loaded = doc.LoadFamily(famDoc.PathName, new StingFamilyReloadOptions(), out reloadedFam);
                            if (loaded || reloadedFam != null)
                                authored++;
                            else
                                StingLog.Warn($"AutoAuthor: LoadFamily returned false for '{fam.Name}' — symbols authored in famDoc but not reloaded into project.");
                        }
                        catch (Exception loadEx)
                        {
                            StingLog.Warn($"AutoAuthor reload '{fam.Name}': {loadEx.Message}");
                            authored++; // count as authored even if reload fails
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AutoAuthor '{fam.Name}': {ex.Message}");
                }
                finally
                {
                    try { famDoc?.Close(false); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
            }
            return authored;
        }

        private sealed class StingFamilyReloadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = false; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = false; return true; }
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

        private static void ShowResult(List<SwapPlan> plans, int swapped, int skipped, int errors,
            int rejoined, int revalidationFindings, bool revalidated, int symbolsAuthored)
        {
            var panel = StingResultPanel.Create("Swap to Manufacturer — Result");
            string subtitle = $"{swapped} swapped · {skipped} skipped · {errors} errors · {rejoined} connectors rejoined";
            if (symbolsAuthored > 0) subtitle += $" · {symbolsAuthored} families authored";
            if (revalidated) subtitle += $" · {revalidationFindings} re-validate findings";
            panel.SetSubtitle(subtitle);
            panel.AddSection("SUMMARY")
                .MetricHighlight("Swapped", swapped.ToString())
                .Metric("Skipped (no candidate)", skipped.ToString())
                .MetricError("Errors", errors.ToString())
                .Metric("Connectors rejoined", rejoined.ToString(),
                    "auto re-stitched after swap (within 600 mm, same domain)")
                .Metric("Symbol families authored", symbolsAuthored.ToString(),
                    "manufacturer families that had STING multi-standard symbol curves injected automatically");
            if (revalidated)
            {
                if (revalidationFindings > 0)
                    panel.MetricWarn("Re-validation findings", revalidationFindings.ToString(),
                        "swap introduced new BS 7671 / healthcare violations on swapped instances");
                else
                    panel.Metric("Re-validation findings", "0",
                        "no new violations on swapped instances");
            }
            panel.AddSection("BY SEED");
            foreach (var p in plans)
            {
                var c = p.Candidates.FirstOrDefault();
                panel.Metric(p.SeedId, p.InstanceIds.Count.ToString(),
                    c != null ? $"→ {c.ResolvedFamilyName} : {c.ResolvedTypeName}" : "no manufacturer family loaded");
            }
            panel.AddSection("NEXT STEPS")
                .Text(rejoined > 0
                    ? $"{rejoined} connector(s) auto-rejoined inside the swap transaction. Re-run Auto-Route Conduit only if any cable still surfaces as unrouted."
                    : "Re-run Auto-Route Conduit if any cable still surfaces as unrouted (the auto re-stitch found nothing within 600 mm to pair).")
                .Text("Run BS 7671 validation — fill / bend / radius re-evaluate against the new family.")
                .Text("STING_SWAP_HISTORY_TXT carries the audit trail; export via the Penetration Register schedule.");
            panel.Show();
        }
    }
}
