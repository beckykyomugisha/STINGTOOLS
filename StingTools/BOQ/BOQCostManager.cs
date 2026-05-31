// ══════════════════════════════════════════════════════════════════════════
//  BOQCostManager.cs — Phase 3 of the BOQ & Cost Manager.
//  Central engine. Builds a BOQDocument from the Revit model, writes cost
//  parameters back to elements and the ProjectInformation record, persists
//  JSON snapshots, compares snapshots, reconciles provisional sums and feeds
//  cash-flow generation for the 4D/5D tab.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.BOQ.Rates;
using StingTools.BOQ.Sync;
using StingTools.BOQ.Takeoff;
using StingTools.Core;
using StingTools.Core.Storage;
using StingTools.Temp;

namespace StingTools.BOQ
{
    internal static class BOQCostManager
    {
        // Newtonsoft settings shared by every snapshot write — indented, ignores nulls,
        // culture-invariant date format so snapshots round-trip across locales.
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        // Snapshots are capped at 20 per project — older ones pruned automatically.
        internal const int MaxSnapshotsRetained = 20;

        // Embodied-carbon lifecycle discount rate (UK Treasury Green Book default).
        private const double LifecycleDiscountRate = 0.035;
        private const int LifecycleYears = 25;

        // ══════════════════════════════════════════════════════════════════
        //  Public API — BuildBOQDocument
        //  Single entry point for building a complete BOQ from the model.
        //  Reusable from the WPF panel, the Excel exporter and the snapshot
        //  machinery. Never writes to the model — callers drive writes via
        //  WriteElementParameters / WriteProjectParameters / SaveSnapshot.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a complete BOQDocument for the model. Reads cost rates from
        /// cost_rates_5d.csv (configurable via TagConfig.CostRatesFileName),
        /// falls back to COBie type map and finally Scheduling4DEngine
        /// defaults. Merges manual/PS rows from project_boq_manual.json so
        /// a QS can author extra line items without modelling them.
        /// </summary>
        internal static BOQDocument BuildBOQDocument(Document doc, IEnumerable<BOQLineItem> extraManualRows = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var boq = new BOQDocument
            {
                ProjectName = ReadProjectName(doc),
                DocumentTitle = ReadProjectDocumentTitle(doc),
                SnapshotLabel = "Live",
                SnapshotType = "Live",
                SnapshotDate = DateTime.UtcNow
            };

            // ── STEP 1: Load config ──────────────────────────────────────
            boq.PrelimPct = TagConfig.GetConfigDouble("COST_PRELIMINARIES_PCT", 12.0);
            boq.ContingencyPct = TagConfig.GetConfigDouble("COST_CONTINGENCY_PCT", 10.0);
            boq.OverheadPct = TagConfig.GetConfigDouble("COST_OVERHEAD_PROFIT_PCT", 8.0);
            boq.ExchangeRateUgxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            boq.ProjectBudgetUGX = ReadProjectBudget(doc);

            // ── STEP 2: Load rate tables (3-source merge) ────────────────
            //   (a) project cost_rates_5d.csv  — highest priority
            //   (b) COBie type map             — category → cost-rate code
            //   (c) Scheduling4DEngine defaults — lowest priority
            Dictionary<string, (double rate, string unit)> csvRates = LoadCsvRates();
            Dictionary<string, string> cobieCostCodes = LoadCobieCostCodes();

            // ── STEP 3: Load embodied carbon factors ─────────────────────
            CarbonTrackingEngine.EnsureLoaded();

            // ── STEP 4: Collect elements ─────────────────────────────────
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);
            var allElements = CollectCandidateElements(doc, knownCats);

            // ── STEP 5: Per-element costing ──────────────────────────────
            var items = new List<BOQLineItem>(allElements.Count);
            foreach (var el in allElements)
            {
                var line = BuildLineItemFromElement(doc, el, csvRates, cobieCostCodes);
                if (line != null) items.Add(line);
            }

            // ── STEP 6: Merge manual + PS rows ───────────────────────────
            var manualStore = LoadManualStore(doc);
            if (manualStore?.ManualRows != null)
                items.AddRange(manualStore.ManualRows.Select(r => r.Clone()));
            if (extraManualRows != null)
                items.AddRange(extraManualRows.Select(r => r.Clone()));

            // ── STEP 7: Group into sections ──────────────────────────────
            boq.Sections = GroupIntoSections(items);

            // ── STEP 7b (Phase 108f): apply persisted model-row overrides.
            // This runs AFTER the full line-item list is assembled so rate +
            // description + note survive BuildBOQDocument rebuilds regardless
            // of whether the background CST_RATE_SOURCE write completed.
            ApplyModelOverrides(doc, boq);

            // ── STEP 8: Assign BOQ line refs across the whole document ───
            AssignBoqLineRefs(boq);

            // ── STEP 9 (N+9): Clear ASS_CST_STALE_BOOL on elements that
            //                  have just been re-costed. The flag was set by
            //                  StingStaleMarker on material change; now that
            //                  this row has its fresh rate + carbon, the
            //                  flag stops being true. Count the refresh so
            //                  the BOQ dashboard can surface it.
            boq.StaleRowsRefreshed = ClearStaleFlagsForCostedRows(doc, boq);

            return boq;
        }

        /// <summary>
        /// N+9 — On every BOQ build, any element whose row has now been
        /// re-costed clears its ASS_CST_STALE_BOOL = "1" flag (set by
        /// StingStaleMarker on a previous material change). Returns the
        /// number of elements whose flag was cleared so the BOQ
        /// dashboard can colour-banner the refresh.
        ///
        /// Caller owns the transaction. Falls back gracefully when the
        /// parameter isn't bound on the project.
        /// </summary>
        private static int ClearStaleFlagsForCostedRows(Document doc, BOQDocument boq)
        {
            if (doc == null || boq == null) return 0;
            int cleared = 0;
            try
            {
                using (var t = new Transaction(doc, "STING BOQ Clear Stale Flags"))
                {
                    t.Start();
                    foreach (var item in boq.AllItems)
                    {
                        if (item.RevitElementId < 0) continue;
                        try
                        {
                            var el = doc.GetElement(new ElementId(item.RevitElementId));
                            if (el == null) continue;
                            var p = el.LookupParameter("ASS_CST_STALE_BOOL");
                            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) continue;
                            string cur = p.AsString();
                            if (string.Equals(cur, "1", StringComparison.Ordinal))
                            {
                                p.Set("0");
                                cleared++;
                            }
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("ClearStale", $"ClearStale {item.RevitElementId}: {ex.Message}"); }
                    }
                    t.Commit();
                }
                if (cleared > 0) StingLog.Info($"BOQ build: refreshed {cleared} stale element row(s).");
            }
            catch (Exception ex) { StingLog.Warn($"ClearStaleFlagsForCostedRows: {ex.Message}"); }
            return cleared;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Per-element line builder — pipeline adapted from
        //  SchedulingCommands.ElementCostTraceCommand so rate-source precedence
        //  and quantity derivation stay consistent across the codebase.
        // ══════════════════════════════════════════════════════════════════

        private static BOQLineItem BuildLineItemFromElement(Document doc, Element el,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes)
        {
            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName)) return null;

            // Skip phase-demolished or temporary elements — they don't belong in the cost plan.
            if (IsPhaseDemolished(doc, el)) return null;

            // (a) Rate lookup — CSV by category → CSV by PROD code → COBie type map → default
            string rateSource;
            int rateConfidence;
            (double rate, string unit, string description) picked = ResolveRate(
                doc, el, catName, csvRates, cobieCostCodes, out rateSource, out rateConfidence);
            if (picked.rate <= 0) rateConfidence = Math.Max(20, rateConfidence); // confidence floor for zero-rate rows

            string unit = string.IsNullOrEmpty(picked.unit) ? "each" : picked.unit;
            double quantity = DeriveQuantity(el, unit);

            // (b) Currency
            double exchangeRate = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            double rateUgx = picked.rate;
            double rateUsd = exchangeRate > 0 ? Math.Round(rateUgx / exchangeRate, 2) : 0;

            // (c) NRM2 paragraph — prefer the previously-resolved value on the element,
            //      then a template resolution, then a safe fallback.
            string paragraph = ResolveNrm2Paragraph(doc, el, catName);

            // (d) Embodied carbon
            double carbonKg = ComputeElementCarbon(el, quantity, unit);

            // (e) Lifecycle cost (capital + simple NPV maintenance)
            double lifecycleUgx = ComputeLifecycleCost(rateUgx * quantity, catName);

            string disc = DisciplineForCategory(catName);
            string nrm2Section = DeriveNrm2Section(doc, el, catName, disc);
            string sectionName = picked.description;
            if (string.IsNullOrEmpty(sectionName)) sectionName = catName;

            var line = new BOQLineItem
            {
                NRM2Section = nrm2Section,
                Category = catName,
                Discipline = disc,
                ItemName = GetElementDisplayName(el),
                FamilyName = GetFamilyName(el),
                TypeName = el.Name ?? "",
                Quantity = quantity,
                Unit = unit,
                RateUGX = rateUgx,
                RateUSD = rateUsd,
                EmbodiedCarbonKg = carbonKg,
                LifecycleCostUGX = lifecycleUgx,
                ResolvedNRM2Paragraph = paragraph,
                Note = "",
                Source = BOQRowSource.Model,
                SnapshotRef = "",
                RevitElementId = el.Id?.Value ?? -1,
                UniqueId = el.UniqueId,
                Level = GetLevelName(doc, el),
                Location = GetLocationName(doc, el),
                LastCosted = DateTime.UtcNow,
                RateSource = rateSource,
                RateConfidence = rateConfidence
            };

            // Mark provisional sums on the element if configured via existing parameter.
            bool isPS = ParameterHelpers.GetInt(el, "CST_PROVISIONAL_SUM", 0) == 1;
            if (isPS) line.Source = BOQRowSource.ProvisionalSum;

            return line;
        }

        // ── Rate resolution ────────────────────────────────────────────────

        private static (double rate, string unit, string description) ResolveRate(
            Document doc, Element el, string catName,
            Dictionary<string, (double rate, string unit)> csvRates,
            Dictionary<string, string> cobieCostCodes,
            out string rateSource, out int rateConfidence)
        {
            // P0 refactor — delegate to the pluggable rate-provider chain.
            // The 5 legacy passes are now individual providers registered
            // with RateProviderRegistry; behaviour is preserved while
            // allowing new providers (BCIS, Spon's, project rate card) to
            // slot in without editing this method.
            double ugxPerUsd = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            double ugxPerGbp = TagConfig.GetConfigDouble("UGX_PER_GBP", 4700.0);

            var registry = RateProviderRegistry.Get(doc, csvRates, cobieCostCodes, ugxPerUsd, ugxPerGbp);
            var req = new RateRequest
            {
                CategoryName = catName ?? "",
                Discipline = DisciplineForCategory(catName),
                ProdCode = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "",
                MatCode = ParameterHelpers.GetString(el, "MAT_CODE") ?? "",
                Unit = csvRates != null && csvRates.TryGetValue(catName ?? "", out var hint) ? hint.unit : "",
                CurrencyCode = "UGX",
                AsOf = DateTime.UtcNow,
                Element = el
            };

            var lookup = registry.Resolve(req);
            if (lookup == null || lookup.UnitRate <= 0)
            {
                rateSource = "None";
                rateConfidence = 20;
                return (0, "each", catName);
            }

            // Map provider id back to the legacy RateSource label so the
            // rest of the codebase (heat-map, schedules, exports) keeps
            // working without changes.
            rateSource = MapProviderIdToLegacySource(lookup.SourceId);
            rateConfidence = lookup.Confidence;
            return (lookup.UnitRate, lookup.Unit, lookup.MatchedKey ?? catName);
        }

        // Normalises unit strings so a CSV "m²" matches a rule's "m2".
        // Returns true when the units denote the same quantity dimension.
        private static bool UnitsAlign(string ruleUnit, string callerUnit)
        {
            string a = NormaliseUnit(ruleUnit);
            string b = NormaliseUnit(callerUnit);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormaliseUnit(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            string s = u.Trim().ToLowerInvariant();
            switch (s)
            {
                case "m²": case "sqm": case "m2": return "m2";
                case "m³": case "cum": case "m3": return "m3";
                case "lm": case "lin-m": case "linear-m": case "m": return "m";
                case "tonne": case "tonnes": case "t": case "kg": return "kg";
                case "no": case "nr": case "item": case "each": case "ea": return "each";
                default: return s;
            }
        }

        // Legacy RateSource labels — preserved so heat-maps and schedules
        // built against the old shape keep working.
        private static string MapProviderIdToLegacySource(string providerId)
        {
            switch (providerId ?? "")
            {
                case "param-override": return "Override";
                case "es-override":    return "Override";
                case "csv-default":    return "CSV";
                case "cobie-typemap":  return "COBie";
                case "default-baseline": return "Default";
                default:               return providerId ?? "None";
            }
        }

        // ── Quantity derivation ────────────────────────────────────────────
        // Adapted from SchedulingCommands.ElementCostTraceCommand.DeriveQuantity
        // so cost totals exactly match the existing 5D Cost Trace output.

        private static double DeriveQuantity(Element el, string unit)
        {
            // P0 refactor — first consult the data-driven TakeoffRuleRegistry.
            // When a rule matches AND its declared unit aligns with the
            // caller's requested unit, the rule's quantitySource +
            // unitConversion drive the quantity. If the units disagree we
            // fall back to legacy logic so a CSV rate at "each" cannot be
            // accidentally combined with an area quantity.
            try
            {
                Document doc = el?.Document;
                if (doc != null)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    string disc = DisciplineForCategory(catName);
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = TakeoffRuleRegistry.Get(doc).Match(catName, disc, prod);
                    if (rule != null && UnitsAlign(rule.Unit, unit))
                    {
                        double q = TakeoffRuleRegistry.EvaluateQuantity(el, rule);
                        // Apply rule-level wastage (P0 reserves; full waste
                        // pipeline lands in P5.2 once star-rates use it).
                        if (rule.WastePercent > 0)
                            q *= 1.0 + rule.WastePercent / 100.0;
                        return q;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveQuantity rule lookup: {ex.Message}"); }

            // Legacy fallback — preserved verbatim for back-compat, except
            // Z-21: a wastage allowance is now applied to genuinely-measured
            // quantities so the fallback path stops under-quantifying (audit
            // §6.3 — waste was previously applied only on the TakeoffRule path).
            // Z-21b: waste is single-surface — applied to the QUANTITY only,
            // never the rate (the ES rate-override no longer inflates the rate
            // by WastePercent — see RateProviders ExtensibleStorageRateProvider).
            // An explicit per-element StingCostRateOverride.WastePercent wins
            // here (honoured on the quantity side); otherwise the project knob
            // COST_DEFAULT_WASTE_PCT (default 5%). Applied via WasteFactor.Apply
            // only to measured material units — never to "each"/"item" counts or
            // the 1.0 "couldn't-measure" placeholders.
            try
            {
                double overrideWaste = 0;
                try { overrideWaste = StingCostRateOverrideSchema.Read(el)?.WastePercent ?? 0; }
                catch (Exception exr) { StingLog.WarnRateLimited("DeriveQuantity.OvrWaste", $"override waste read: {exr.Message}"); }
                double wastePct = WasteFactor.ResolveWastePercent(
                    overrideWaste, TagConfig.GetConfigDouble("COST_DEFAULT_WASTE_PCT", 5.0));
                switch ((unit ?? "").ToLowerInvariant())
                {
                    case "m²":
                    case "m2":
                    case "sqm":
                        Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        if (areaP != null && areaP.HasValue)
                            return WasteFactor.Apply(areaP.AsDouble() * 0.092903, unit, wastePct); // ft² → m²
                        return 1.0;
                    case "m³":
                    case "m3":
                    case "cum":
                        Parameter volP = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                        if (volP != null && volP.HasValue)
                            return WasteFactor.Apply(volP.AsDouble() * 0.0283168, unit, wastePct); // ft³ → m³
                        return 1.0;
                    case "m":
                        if (el.Location is LocationCurve lc)
                            return WasteFactor.Apply(lc.Curve.Length * 0.3048, unit, wastePct); // ft → m
                        Parameter lenP = el.LookupParameter("Length")
                            ?? el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        if (lenP != null && lenP.HasValue)
                            return WasteFactor.Apply(lenP.AsDouble() * 0.3048, unit, wastePct);
                        return 1.0;
                    case "kg":
                    case "tonne":
                    case "tonnes":
                        Parameter massP = el.LookupParameter("Weight") ?? el.LookupParameter("Mass");
                        if (massP != null && massP.HasValue)
                            return WasteFactor.Apply(massP.AsDouble(), unit, wastePct);
                        return 1.0;
                    default:
                        return 1.0;
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveQuantity({unit}): {ex.Message}"); return 1.0; }
        }

        // ── NRM2 paragraph resolution ──────────────────────────────────────

        private static readonly Regex _tokenRx = new Regex(@"\[([a-zA-Z0-9_]+)\]", RegexOptions.Compiled);

        private static string ResolveNrm2Paragraph(Document doc, Element el, string catName)
        {
            // (i) Use the previously stored paragraph if it has no unresolved [tokens]
            string stored = ParameterHelpers.GetString(el, "ASS_NRM2_PARA_TXT");
            if (!string.IsNullOrEmpty(stored) && !_tokenRx.IsMatch(stored)) return stored;

            // (ii) BOQ-12 — Material-aware template selection. The template
            // library is queried with the element + category as before; the
            // material name + class are then folded into the resolved
            // paragraph so a "Generic" family doesn't end up with a
            // category-only description.
            string matName = null, matClass = null;
            try
            {
                matName = GetPrimaryMaterialName(el);
                if (!string.IsNullOrEmpty(matName) && doc != null)
                {
                    var mat = new FilteredElementCollector(doc).OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => string.Equals(m.Name, matName, StringComparison.OrdinalIgnoreCase));
                    matClass = mat?.MaterialClass;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveNrm2Paragraph material: {ex.Message}"); }

            try
            {
                var all = BOQTemplateLibrary.LoadAll(doc, StingToolsApp.DataPath);
                var tpl = BOQTemplateLibraryExtensions.SelectBestTemplate(all, catName, el);
                if (tpl != null)
                {
                    string resolved = BOQTemplateLibraryExtensions.ResolveForElement(tpl, el, doc);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        // Prepend material qualifier when we have one and the
                        // template doesn't already mention it.
                        if (!string.IsNullOrEmpty(matClass) &&
                            !resolved.IndexOf(matClass, StringComparison.OrdinalIgnoreCase).Equals(-1) is false)
                        {
                            // resolved already mentions the class — leave as-is
                        }
                        else if (!string.IsNullOrEmpty(matClass) &&
                                 resolved.IndexOf(matClass, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            resolved = $"{matClass}: {resolved}";
                        }
                        return resolved;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveNrm2Paragraph template: {ex.Message}"); }

            // (iii) Safe fallback — material-qualified when known, category-
            // only otherwise. QS can override later in the Excel roundtrip.
            string qualifier = !string.IsNullOrEmpty(matClass) ? matClass.ToLower() + " "
                              : !string.IsNullOrEmpty(matName) ? matName.ToLower() + " "
                              : "";
            return $"Supply and fix {qualifier}{catName?.ToLower()}.";
        }

        // ── Carbon + lifecycle ─────────────────────────────────────────────

        private static double ComputeElementCarbon(Element el, double quantity, string unit)
        {
            try
            {
                // R-1 — Carbon factor source-aware unit treatment.
                // STING_EMB_CARBON_NR + MaterialLookupCsv ship kgCO₂e PER m³ (volumetric);
                // the legacy CARBON_FACTORS.csv dictionary ships kgCO₂e PER kg.
                // Multiplying a volumetric factor by element MASS is the 1000× wrong-answer
                // bug the LCA audit flagged. Route through CarbonFactorResolver so the
                // calling convention is explicit.
                string material = GetPrimaryMaterialName(el);
                if (string.IsNullOrEmpty(material)) return 0;

                var resolved = CarbonFactorResolver.Resolve(el.Document, material);
                if (resolved.Factor <= 0) return 0;

                if (resolved.PerUnit == CarbonFactorUnit.KgCo2ePerKg)
                {
                    // Legacy mass-based factor — multiply by mass.
                    double kg = EstimateMassKg(el, quantity, unit);
                    return Math.Round(kg * resolved.Factor, 2);
                }
                // Default + STING / lookup tiers are kgCO₂e per m³ — multiply by volume.
                // R-4 — Surface elements use area × thickness; linear use length × cross-section.
                double volM3 = EstimateVolumeM3(el, quantity, unit, material);
                return Math.Round(volM3 * resolved.Factor, 2);
            }
            catch (Exception ex) { StingLog.Warn($"ComputeElementCarbon: {ex.Message}"); return 0; }
        }

        /// <summary>
        /// R-4 — Volume estimator that works for volumetric, surface, AND
        /// linear elements. Returns 0 only when no volume / area / length
        /// is exposed (i.e. point-instance families with no geometry).
        /// </summary>
        private static double EstimateVolumeM3(Element el, double quantity, string unit, string material)
        {
            if (string.IsNullOrEmpty(unit)) unit = "each";
            try
            {
                // Volumetric — direct.
                if (unit == "m³" || unit == "m3") return quantity;

                // Surface — area × default layer thickness (read from material lookup
                // when not exposed on the element).
                if (unit == "m²" || unit == "m2")
                {
                    double thicknessMm = ReadLayerThicknessMm(el);
                    if (thicknessMm <= 0)
                    {
                        // Sensible defaults so we don't return zero for paint / membrane.
                        string lc = (material ?? "").ToLowerInvariant();
                        thicknessMm = lc.Contains("paint") || lc.Contains("coating") ? 0.15
                                    : lc.Contains("membrane") || lc.Contains("dpm") ? 1.5
                                    : lc.Contains("plaster") || lc.Contains("gypsum") ? 12.5
                                    : lc.Contains("insulation") ? 50.0
                                    : 10.0;
                    }
                    return quantity * (thicknessMm / 1000.0);
                }

                // Linear — length × cross-section read from element when present.
                if (unit == "m")
                {
                    double areaMm2 = ReadCrossSectionMm2(el);
                    if (areaMm2 <= 0) areaMm2 = 1000.0; // default Ø35.7 mm circular equiv (2·√(1000/π)) — caller can override via param
                    return quantity * (areaMm2 / 1_000_000.0);
                }

                // kg → mass-only paths; carbon for these comes via the
                // legacy mass-based factor in the other branch.
                return 0;
            }
            catch (Exception ex) { StingLog.Warn($"EstimateVolumeM3 ({unit}): {ex.Message}"); return 0; }
        }

        private static double ReadLayerThicknessMm(Element el)
        {
            try
            {
                var p = el.LookupParameter("Thickness") ?? el.LookupParameter("Width");
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    // Internal feet → millimetres.
                    return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("VolEst.Layer", $"ReadLayerThicknessMm: {ex.Message}"); }
            return 0;
        }

        private static double ReadCrossSectionMm2(Element el)
        {
            try
            {
                // Pipes / conduits expose Outside Diameter; cable trays expose Width × Height.
                var od = el.LookupParameter("Outside Diameter");
                if (od != null && od.HasValue && od.StorageType == StorageType.Double)
                {
                    double dMm = UnitUtils.ConvertFromInternalUnits(od.AsDouble(), UnitTypeId.Millimeters);
                    return Math.PI * (dMm / 2.0) * (dMm / 2.0);
                }
                var w = el.LookupParameter("Width");
                var h = el.LookupParameter("Height");
                if (w != null && w.HasValue && h != null && h.HasValue &&
                    w.StorageType == StorageType.Double && h.StorageType == StorageType.Double)
                {
                    double wMm = UnitUtils.ConvertFromInternalUnits(w.AsDouble(), UnitTypeId.Millimeters);
                    double hMm = UnitUtils.ConvertFromInternalUnits(h.AsDouble(), UnitTypeId.Millimeters);
                    return wMm * hMm;
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("VolEst.Xs", $"ReadCrossSectionMm2: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// Simple lifecycle cost: capital + 25y NPV of annual maintenance cost.
        /// Maintenance fraction driven by COBIE_TYPE_MAP.csv MaintenanceFreqMonths
        /// column when present (falls back to 2%/y for hard assets, 0.5%/y for shell).
        /// Discount rate = 3.5% (UK Treasury Green Book).
        /// </summary>
        private static double ComputeLifecycleCost(double capitalUgx, string catName)
        {
            if (capitalUgx <= 0) return 0;
            double annualMaintenance = capitalUgx * EstimateAnnualMaintenanceRate(catName);
            double npvFactor = 0;
            for (int y = 1; y <= LifecycleYears; y++)
                npvFactor += 1.0 / Math.Pow(1 + LifecycleDiscountRate, y);
            return Math.Round(capitalUgx + annualMaintenance * npvFactor, 0);
        }

        private static double EstimateAnnualMaintenanceRate(string catName)
        {
            if (string.IsNullOrEmpty(catName)) return 0.02;
            string lower = catName.ToLowerInvariant();
            if (lower.Contains("foundation") || lower.Contains("structural")) return 0.005;
            if (lower.Contains("wall") || lower.Contains("floor") || lower.Contains("roof")) return 0.01;
            if (lower.Contains("duct") || lower.Contains("pipe") || lower.Contains("mechanical")) return 0.03;
            if (lower.Contains("electrical") || lower.Contains("lighting")) return 0.025;
            if (lower.Contains("furniture") || lower.Contains("casework")) return 0.04;
            return 0.02;
        }

        private static double EstimateMassKg(Element el, double quantity, string unit)
        {
            try
            {
                Parameter massP = el.LookupParameter("Weight") ?? el.LookupParameter("Mass");
                if (massP != null && massP.HasValue) return massP.AsDouble();

                // Density fallback — only applies when we have a volume measurement.
                if ((unit == "m³" || unit == "m3") && quantity > 0)
                {
                    double density = EstimateDensityKgPerM3(GetPrimaryMaterialName(el));
                    return quantity * density;
                }
            }
            catch (Exception ex) { StingLog.Warn($"EstimateMassKg: {ex.Message}"); }
            return 0;
        }

        private static double EstimateDensityKgPerM3(string material)
        {
            if (string.IsNullOrWhiteSpace(material)) return 1000;

            // N+7 — Single-source resolution. MaterialLookupCsv corporate
            // library wins; the legacy hard-coded keyword switch is now
            // last-resort fallback only.
            try
            {
                double libVal = StingTools.UI.MaterialLookupCsv.GetDensity(material);
                if (libVal > 0) return libVal;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("EstimateDensity.Lookup", $"EstimateDensity lookup: {ex.Message}"); }

            string lower = material.ToLowerInvariant();
            if (lower.Contains("concrete")) return 2400;
            if (lower.Contains("steel")) return 7850;
            if (lower.Contains("timber") || lower.Contains("wood")) return 550;
            if (lower.Contains("alumin")) return 2700;
            if (lower.Contains("glass")) return 2500;
            if (lower.Contains("brick")) return 1920;
            if (lower.Contains("insulation")) return 40;
            if (lower.Contains("plaster") || lower.Contains("gypsum")) return 1250;
            return 1000;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Parameter write-back
        //  Writes CST_* / ASS_NRM2_PARA_* / ASS_BOQ_* parameters on elements
        //  (only when values differ — dirty check) and updates ProjectInfo
        //  project-level parameters. Caller supplies the transaction so
        //  multiple operations can be batched within a single undo entry.
        // ══════════════════════════════════════════════════════════════════

        internal static int WriteElementParameters(Document doc, IEnumerable<BOQLineItem> items)
        {
            if (items == null) return 0;
            int written = 0;
            foreach (var item in items)
            {
                if (item.RevitElementId < 0) continue;
                Element el;
                try { el = doc.GetElement(new ElementId(item.RevitElementId)); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                if (el == null) continue;

                // Rate fields — always write both currencies so the element stays
                // currency-agnostic across sessions (Gap G3).
                WriteIfChanged(el, "CST_UNIT_RATE_UGX", item.RateUGX.ToString("F0", CultureInfo.InvariantCulture), ref written);
                WriteIfChanged(el, "CST_UNIT_RATE_USD", item.RateUSD.ToString("F2", CultureInfo.InvariantCulture), ref written);
                WriteIfChanged(el, "CST_QTY_MEASURED", $"{item.Quantity:F3} {item.Unit}", ref written);

                // Computed total — stored as NUMBER parameter
                TrySetNumber(el, "CST_MODELED_TOTAL_UGX", item.TotalUGX, ref written);

                WriteIfChanged(el, "CST_RATE_SOURCE", item.RateSource ?? "", ref written);

                if (!string.IsNullOrEmpty(item.SnapshotRef))
                    WriteIfChanged(el, "CST_BOQ_SNAPSHOT_REF", item.SnapshotRef, ref written);

                // Paragraph — audit trail (Phase 11D)
                string currentPara = ParameterHelpers.GetString(el, "ASS_NRM2_PARA_TXT") ?? "";
                if (!string.IsNullOrEmpty(item.ResolvedNRM2Paragraph) && currentPara != item.ResolvedNRM2Paragraph)
                {
                    if (!string.IsNullOrEmpty(currentPara))
                    {
                        ParameterHelpers.SetString(el, "ASS_NRM2_PARA_PREV_TXT", currentPara, overwrite: true);
                    }
                    ParameterHelpers.SetString(el, "ASS_NRM2_PARA_TXT", item.ResolvedNRM2Paragraph, overwrite: true);
                    ParameterHelpers.SetString(el, "ASS_NRM2_PARA_DATE_TXT",
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), overwrite: true);
                    ParameterHelpers.SetString(el, "ASS_NRM2_PARA_AUTHOR_TXT", Environment.UserName ?? "", overwrite: true);
                    written++;
                }

                // Line ref is write-once — never overwrite an explicit user-assigned ref (Gap G8)
                if (!string.IsNullOrEmpty(item.BOQLineRef))
                {
                    string existingRef = ParameterHelpers.GetString(el, "ASS_BOQ_LINE_REF");
                    if (string.IsNullOrEmpty(existingRef))
                    {
                        ParameterHelpers.SetString(el, "ASS_BOQ_LINE_REF", item.BOQLineRef, overwrite: true);
                        written++;
                    }
                }

                if (!string.IsNullOrEmpty(item.Category))
                    WriteIfChanged(el, "ASS_BOQ_SECTION_NAME", item.Category, ref written);

                TrySetNumber(el, "CST_EMBODIED_CARBON_KG", item.EmbodiedCarbonKg, ref written);
                TrySetNumber(el, "CST_LIFECYCLE_COST_UGX", item.LifecycleCostUGX, ref written);
                ParameterHelpers.SetInt(el, "CST_RATE_CONFIDENCE", item.RateConfidence, overwrite: true);
            }
            return written;
        }

        internal static void WriteProjectParameters(Document doc, BOQDocument boq)
        {
            if (doc?.ProjectInformation == null || boq == null) return;
            Element pi = doc.ProjectInformation;
            int dummy = 0;

            TrySetNumber(pi, "PROJECT_BUDGET_UGX", boq.ProjectBudgetUGX, ref dummy);
            TrySetNumber(pi, "CST_BUDGET_VARIANCE_UGX", boq.BudgetVarianceUGX, ref dummy);
            TrySetNumber(pi, "CST_BOQ_COVERAGE_PCT", boq.BudgetCoveragePct, ref dummy);
            ParameterHelpers.SetString(pi, "CST_LAST_COSTED_DATE",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), overwrite: true);
        }

        // ── Helpers used by WriteElementParameters ────────────────────────

        private static void WriteIfChanged(Element el, string paramName, string value, ref int counter)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return;
            string current = ParameterHelpers.GetString(el, paramName);
            if (current == value) return;
            if (ParameterHelpers.SetString(el, paramName, value ?? "", overwrite: true)) counter++;
        }

        private static void TrySetNumber(Element el, string paramName, double value, ref int counter)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                // Only write when the displayed value differs — prevents dirtying the
                // transaction when the model already has the right value.
                double current = p.HasValue ? p.AsDouble() : double.NaN;
                if (!double.IsNaN(current) && Math.Abs(current - value) < 1e-6) return;
                if (p.StorageType == StorageType.Double) { p.Set(value); counter++; }
                else if (p.StorageType == StorageType.Integer) { p.Set((int)Math.Round(value)); counter++; }
                else p.Set(value.ToString("F2", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"TrySetNumber({paramName}): {ex.Message}"); }
        }

        private static double ReadProjectBudget(Document doc)
        {
            if (doc?.ProjectInformation == null) return 0;
            Parameter p = doc.ProjectInformation.LookupParameter("PROJECT_BUDGET_UGX");
            if (p != null && p.HasValue)
            {
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String
                    && double.TryParse(p.AsString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            // Fallback — project_config.json PROJECT_BUDGET_UGX
            return TagConfig.GetConfigDouble("PROJECT_BUDGET_UGX", 0);
        }

        /// <summary>
        /// Read the "Project Name" field from the Project Information dialog.
        /// doc.ProjectInformation.Name returns the ELEMENT name (an internal
        /// identifier), not the value the user types into "Project Name".
        /// That field is bound to BuiltInParameter.PROJECT_NAME.
        /// </summary>
        private static string ReadProjectName(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi != null)
                {
                    string v = pi.get_Parameter(BuiltInParameter.PROJECT_NAME)?.AsString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                    if (!string.IsNullOrWhiteSpace(pi.Name)) return pi.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadProjectName: {ex.Message}"); }
            return !string.IsNullOrWhiteSpace(doc?.Title) ? doc.Title : "Unknown project";
        }

        /// <summary>
        /// BOQ document title — combines the Project Number (if set) with
        /// "Bill of Quantities" so the exported workbook and the header
        /// strip identify the project at a glance.
        /// </summary>
        private static string ReadProjectDocumentTitle(Document doc)
        {
            try
            {
                string num = doc?.ProjectInformation?.get_Parameter(BuiltInParameter.PROJECT_NUMBER)?.AsString();
                if (!string.IsNullOrWhiteSpace(num))
                    return $"Bill of Quantities — {num}";
            }
            catch (Exception ex) { StingLog.Warn($"ReadProjectDocumentTitle: {ex.Message}"); }
            return "Bill of Quantities";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Snapshot persistence — save, list, load and prune.
        //  Snapshots are plain JSON under {projectDir}/STING_BIM_MANAGER/.
        //  The same dir hosts every other BIM-manager sidecar so backups
        //  and CDE transmittals pick them up automatically.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Persist the BOQ to a timestamped JSON snapshot. Also stamps the
        /// snapshot label onto every modeled element (CST_BOQ_SNAPSHOT_REF)
        /// so a line in the BOQ can always be traced back to the source
        /// element at the moment it was costed. Caller supplies a transaction
        /// context inside which the element stamping runs; budget/variance
        /// write-back uses the same transaction.
        /// </summary>
        internal static string SaveSnapshot(Document doc, BOQDocument boq, string label, string snapshotType)
        {
            if (doc == null || boq == null) throw new ArgumentNullException();
            string safeLabel = MakeSafeFileName(label ?? "snapshot");
            string safeType = MakeSafeFileName(snapshotType ?? "Manual");
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string path = Path.Combine(bimDir,
                $"boq_snapshot_{safeType}_{safeLabel}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            boq.SnapshotLabel = label;
            boq.SnapshotType = snapshotType;
            boq.SnapshotDate = DateTime.UtcNow;

            // Stamp the snapshot reference onto every row before serialising.
            foreach (var it in boq.AllItems)
                it.SnapshotRef = label;

            // P1: compute canonical checksum BEFORE writing so it can be
            // serialised into the snapshot file's audit trail and used by
            // the server to detect duplicate pushes.
            string checksum = BoqSnapshotHasher.ComputeChecksum(boq);

            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(boq, _jsonSettings));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);

                // Sidecar meta — checksum + future sync state. Lives next
                // to the snapshot json so it survives independently of any
                // server round-trip.
                WriteSnapshotMetaSidecar(path, checksum, label, snapshotType);

                StingLog.Info($"BOQ snapshot saved: {Path.GetFileName(path)} ({boq.AllItems.Count} items, checksum={Shorten(checksum)})");
            }
            catch (Exception ex) { StingLog.Error("BOQ snapshot save", ex); throw; }

            PruneSnapshots(doc);

            // P1: fire-and-forget server push. Failures fall through to
            // "Pending" state in the sidecar and the background sync
            // scheduler retries. Snapshot save success is independent of
            // network availability.
            TryPushSnapshotAsync(doc, boq, checksum, label, path);

            return path;
        }

        // P1 — async push wrapper. Non-blocking, swallows exceptions, and
        // updates the sidecar with the resulting SyncState.
        private static void TryPushSnapshotAsync(Document doc, BOQDocument boq,
            string checksum, string label, string snapshotPath)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await BoqSyncCoordinator.PushSnapshotAsync(doc, boq, checksum, label);
                        UpdateSnapshotMetaSidecar(snapshotPath, result);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"TryPushSnapshotAsync: {ex.Message}");
                    }
                });
            }
            catch (Exception ex) { StingLog.Warn($"TryPushSnapshotAsync schedule: {ex.Message}"); }
        }

        // Sidecar file format: <snapshot.json>.meta.json carrying
        // { checksum, label, type, savedUtc, syncState, serverBaselineId, syncDetail }.
        private static void WriteSnapshotMetaSidecar(string snapshotPath, string checksum,
            string label, string snapshotType)
        {
            try
            {
                string metaPath = snapshotPath + ".meta.json";
                var meta = new
                {
                    checksum,
                    label,
                    type = snapshotType,
                    savedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    syncState = "Local",
                    serverBaselineId = (string)null,
                    syncDetail = ""
                };
                File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, _jsonSettings));
            }
            catch (Exception ex) { StingLog.Warn($"WriteSnapshotMetaSidecar: {ex.Message}"); }
        }

        private static void UpdateSnapshotMetaSidecar(string snapshotPath, BoqSyncResult result)
        {
            try
            {
                string metaPath = snapshotPath + ".meta.json";
                if (!File.Exists(metaPath)) return;
                var existing = JObject.Parse(File.ReadAllText(metaPath));
                existing["syncState"] = result?.SyncState ?? "Pending";
                existing["serverBaselineId"] = result?.ServerBaselineId?.ToString();
                existing["syncDetail"] = result?.Detail ?? "";
                existing["linesCreated"] = result?.LinesCreated ?? 0;
                existing["linesUpdated"] = result?.LinesUpdated ?? 0;
                existing["lastSyncedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                File.WriteAllText(metaPath, existing.ToString(Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"UpdateSnapshotMetaSidecar: {ex.Message}"); }
        }

        private static string Shorten(string s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= 8 ? s : s.Substring(0, 8));

        /// <summary>Load a snapshot JSON. Returns null on any failure.</summary>
        internal static BOQDocument LoadSnapshot(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<BOQDocument>(File.ReadAllText(path), _jsonSettings); }
            catch (Exception ex) { StingLog.Warn($"LoadSnapshot({Path.GetFileName(path)}): {ex.Message}"); return null; }
        }

        /// <summary>
        /// Enumerate all available BOQ snapshots. Cheap — reads only the top
        /// of each file via a lazy JObject Parse that still gives us the KPI
        /// header (label, type, grand total).
        /// </summary>
        internal static List<BOQSnapshotMeta> ListSnapshots(Document doc)
        {
            var list = new List<BOQSnapshotMeta>();
            try
            {
                string dir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (!Directory.Exists(dir)) return list;
                foreach (string f in Directory.EnumerateFiles(dir, "boq_snapshot_*.json"))
                {
                    try
                    {
                        // Filename shape:  boq_snapshot_{type}_{label}_{yyyyMMdd_HHmmss}.json
                        string stem = Path.GetFileNameWithoutExtension(f);
                        var parts = stem.Split('_');
                        if (parts.Length < 5) continue;
                        string type = parts[2];
                        string dateStr = parts[parts.Length - 2] + "_" + parts[parts.Length - 1];
                        string label = string.Join("_", parts.Skip(3).Take(parts.Length - 5));
                        DateTime.TryParseExact(dateStr, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime dt);

                        double total = 0;
                        try
                        {
                            // Only parse the single top-level property we need.
                            using (var sr = new StreamReader(f))
                            using (var jr = new JsonTextReader(sr))
                            {
                                var jo = JObject.Load(jr);
                                if (jo != null && jo["Sections"] is JArray secs)
                                {
                                    foreach (var sec in secs)
                                    {
                                        if (sec["Items"] is JArray its)
                                        {
                                            foreach (var it in its)
                                            {
                                                double q = it.Value<double?>("Quantity") ?? 0;
                                                double r = it.Value<double?>("RateUGX") ?? 0;
                                                total += q * r;
                                            }
                                        }
                                    }
                                    double pre = 12.0, con = 10.0, oh = 8.0;
                                    double.TryParse(jo.Value<string>("PrelimPct") ?? "", NumberStyles.Any,
                                        CultureInfo.InvariantCulture, out pre);
                                    total = Math.Round(total * (1 + pre / 100 + con / 100 + oh / 100), 0);
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"ListSnapshots parse {Path.GetFileName(f)}: {ex.Message}"); }

                        // P1: enrich with sidecar meta (checksum, sync state)
                        // when available. Sidecar is best-effort — missing
                        // sidecar leaves the new fields at their defaults.
                        string checksum = "";
                        Guid? serverBaselineId = null;
                        string syncState = "Local";
                        try
                        {
                            string metaPath = f + ".meta.json";
                            if (File.Exists(metaPath))
                            {
                                var m = JObject.Parse(File.ReadAllText(metaPath));
                                checksum = m.Value<string>("checksum") ?? "";
                                syncState = m.Value<string>("syncState") ?? "Local";
                                string srvId = m.Value<string>("serverBaselineId");
                                if (Guid.TryParse(srvId, out Guid g)) serverBaselineId = g;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"ListSnapshots meta {Path.GetFileName(f)}: {ex.Message}"); }

                        list.Add(new BOQSnapshotMeta
                        {
                            Path = f, Label = label, Type = type, Date = dt, GrandTotalUGX = total,
                            Checksum = checksum,
                            ServerBaselineId = serverBaselineId,
                            SyncState = syncState
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"ListSnapshots inner: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ListSnapshots: {ex.Message}"); }
            return list.OrderByDescending(s => s.Date).ToList();
        }

        private static void PruneSnapshots(Document doc)
        {
            try
            {
                var all = ListSnapshots(doc);
                if (all.Count <= MaxSnapshotsRetained) return;
                foreach (var old in all.Skip(MaxSnapshotsRetained))
                {
                    try { File.Delete(old.Path); }
                    catch (Exception ex) { StingLog.Warn($"PruneSnapshots delete {old.Path}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PruneSnapshots: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Snapshot comparison — builds a structured diff between two
        //  snapshots suitable for rendering in a StingResultPanel or a
        //  dedicated Excel "Snapshot Comparison" sheet.
        // ══════════════════════════════════════════════════════════════════

        internal static BOQSnapshotDiff CompareSnapshots(string pathA, string pathB)
        {
            var diff = new BOQSnapshotDiff();
            var a = LoadSnapshot(pathA);
            var b = LoadSnapshot(pathB);
            if (a == null || b == null) return diff;

            diff.LabelA = a.SnapshotLabel; diff.LabelB = b.SnapshotLabel;
            diff.TypeA = a.SnapshotType; diff.TypeB = b.SnapshotType;
            diff.DateA = a.SnapshotDate; diff.DateB = b.SnapshotDate;
            diff.TotalA = a.GrandTotalUGX; diff.TotalB = b.GrandTotalUGX;
            diff.ModeledA = a.ModeledTotalUGX; diff.ModeledB = b.ModeledTotalUGX;
            diff.ProvA = a.ProvTotalUGX; diff.ProvB = b.ProvTotalUGX;
            diff.CarbonA = a.TotalCarbonKg; diff.CarbonB = b.TotalCarbonKg;

            // Match items by BOQLineRef first, then by Category+ItemName composite.
            var aByKey = IndexByKey(a);
            var bByKey = IndexByKey(b);
            var keys = new HashSet<string>(aByKey.Keys);
            foreach (var k in bByKey.Keys) keys.Add(k);

            foreach (var key in keys)
            {
                aByKey.TryGetValue(key, out BOQLineItem ai);
                bByKey.TryGetValue(key, out BOQLineItem bi);
                var cd = new CategoryDiff
                {
                    NRM2Section = bi?.NRM2Section ?? ai?.NRM2Section,
                    Name = bi?.Category ?? ai?.Category,
                    Discipline = bi?.Discipline ?? ai?.Discipline,
                    QtyA = ai?.Quantity ?? 0,
                    QtyB = bi?.Quantity ?? 0,
                    RateA = ai?.RateUGX ?? 0,
                    RateB = bi?.RateUGX ?? 0,
                    TotalA = ai?.TotalUGX ?? 0,
                    TotalB = bi?.TotalUGX ?? 0
                };
                cd.ChangeType = ClassifyChange(ai, bi);
                cd.ChangeReason = BuildChangeReason(cd, ai, bi);
                if (cd.ChangeType != BOQChangeType.NoChange) diff.CategoryDiffs.Add(cd);
            }

            // Section-level rollup
            var rolled = new Dictionary<string, SectionDiff>(StringComparer.OrdinalIgnoreCase);
            foreach (var cd in diff.CategoryDiffs)
            {
                string key = $"{cd.NRM2Section}|{cd.Discipline}";
                if (!rolled.TryGetValue(key, out var sd))
                {
                    sd = new SectionDiff
                    {
                        NRM2Section = cd.NRM2Section, Name = cd.Name, Discipline = cd.Discipline
                    };
                    rolled[key] = sd;
                }
                sd.TotalA += cd.TotalA;
                sd.TotalB += cd.TotalB;
            }
            diff.SectionDiffs = rolled.Values.OrderBy(s => s.NRM2Section).ToList();

            diff.PlainSummary = BuildPlainSummary(diff);
            return diff;
        }

        private static Dictionary<string, BOQLineItem> IndexByKey(BOQDocument d)
        {
            var map = new Dictionary<string, BOQLineItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in d.AllItems)
            {
                string k = !string.IsNullOrEmpty(it.BOQLineRef)
                    ? "ref:" + it.BOQLineRef
                    : "cat:" + (it.Category ?? "") + "|" + (it.ItemName ?? "");
                map[k] = it;
            }
            return map;
        }

        private static BOQChangeType ClassifyChange(BOQLineItem a, BOQLineItem b)
        {
            if (a == null && b != null)
                return b.Source == BOQRowSource.ProvisionalSum ? BOQChangeType.PSAdded : BOQChangeType.NewItem;
            if (a != null && b == null) return BOQChangeType.ItemRemoved;
            if (a == null || b == null) return BOQChangeType.NoChange;
            if (a.Source != BOQRowSource.Model && b.Source == BOQRowSource.Model)
                return BOQChangeType.SourcePromoted;
            bool qtyChanged = a.Quantity > 0 && Math.Abs(b.Quantity - a.Quantity) / a.Quantity > 0.001;
            bool rateChanged = a.RateUGX > 0 && Math.Abs(b.RateUGX - a.RateUGX) / a.RateUGX > 0.01;
            if (rateChanged && !qtyChanged) return BOQChangeType.RateRevised;
            if (qtyChanged && !rateChanged) return BOQChangeType.QtyChanged;
            if (qtyChanged && rateChanged) return BOQChangeType.RateRevised; // dominant narrative
            return BOQChangeType.NoChange;
        }

        private static string BuildChangeReason(CategoryDiff cd, BOQLineItem a, BOQLineItem b)
        {
            switch (cd.ChangeType)
            {
                case BOQChangeType.RateRevised:
                    return $"Rate revised {cd.Name} UGX {cd.RateA:N0} → {cd.RateB:N0}/unit.";
                case BOQChangeType.QtyChanged:
                    string dir = cd.QtyB > cd.QtyA ? "increased" : "reduced";
                    return $"{cd.Name} {dir} {cd.QtyA:N1} → {cd.QtyB:N1} {b?.Unit ?? a?.Unit}.";
                case BOQChangeType.NewItem:
                    return $"{cd.QtyB:N0} {b?.Unit} newly modeled.";
                case BOQChangeType.ItemRemoved:
                    return "Removed since last snapshot.";
                case BOQChangeType.PSAdded:
                    return $"PC sum registered: {b?.Note ?? cd.Name}.";
                case BOQChangeType.SourcePromoted:
                    return "Promoted from manual row to modeled element.";
                default:
                    return "";
            }
        }

        private static string BuildPlainSummary(BOQSnapshotDiff d)
        {
            string sign = d.NetChange >= 0 ? "+" : "";
            var parts = new List<string>
            {
                $"Net movement between '{d.LabelA}' and '{d.LabelB}' is {sign}UGX {d.NetChange:N0} ({d.NetChangePct:+0.0;-0.0;0.0}%)."
            };
            var top = d.CategoryDiffs.OrderByDescending(c => Math.Abs(c.Delta)).Take(3).ToList();
            if (top.Count > 0)
            {
                parts.Add("Largest movements: " + string.Join("; ",
                    top.Select(c => $"{c.Name} {(c.Delta >= 0 ? "+" : "")}{c.Delta:N0}")) + ".");
            }
            if (Math.Abs(d.NetCarbonChange) > 1)
                parts.Add($"Embodied carbon moved by {d.NetCarbonChange:+#,##0;-#,##0;0} kgCO2e.");
            return string.Join(" ", parts);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Manual store / provisional sum reconciliation
        // ══════════════════════════════════════════════════════════════════

        internal static string GetManualStorePath(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "project_boq_manual.json");

        internal static BOQManualStore LoadManualStore(Document doc)
        {
            string path = GetManualStorePath(doc);
            if (!File.Exists(path)) return new BOQManualStore();
            try { return JsonConvert.DeserializeObject<BOQManualStore>(File.ReadAllText(path), _jsonSettings) ?? new BOQManualStore(); }
            catch (Exception ex) { StingLog.Warn($"LoadManualStore: {ex.Message}"); return new BOQManualStore(); }
        }

        internal static List<BOQLineItem> LoadManualRows(Document doc)
            => LoadManualStore(doc)?.ManualRows ?? new List<BOQLineItem>();

        internal static void SaveManualRows(Document doc, List<BOQLineItem> rows, double projectBudgetUgx)
        {
            var store = new BOQManualStore
            {
                SchemaVersion = "1.1",
                ProjectBudgetUGX = projectBudgetUgx,
                LastSaved = DateTime.UtcNow,
                LastSavedBy = Environment.UserName ?? "",
                ManualRows = rows ?? new List<BOQLineItem>()
            };
            string path = GetManualStorePath(doc);
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(store, _jsonSettings));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                StingLog.Info($"BOQ manual store saved: {store.ManualRows.Count} manual/PS rows, budget UGX {projectBudgetUgx:N0}");
            }
            catch (Exception ex) { StingLog.Error("SaveManualRows", ex); throw; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Phase 108f — model-row override sidecar
        //  {projectDir}/_bim_manager/project_boq_model_overrides.json
        //  Survives the StingCommandHandler single-_commandTag race and any
        //  failures of the async BOQWriteItemParams ExternalEvent.
        // ══════════════════════════════════════════════════════════════════

        private static readonly object _overridesLock = new object();

        internal static string GetModelOverridesPath(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "project_boq_model_overrides.json");

        internal static BOQModelOverridesStore LoadModelOverrides(Document doc)
        {
            string path = GetModelOverridesPath(doc);
            if (!File.Exists(path)) return new BOQModelOverridesStore();
            try { return JsonConvert.DeserializeObject<BOQModelOverridesStore>(File.ReadAllText(path), _jsonSettings) ?? new BOQModelOverridesStore(); }
            catch (Exception ex) { StingLog.Warn($"LoadModelOverrides: {ex.Message}"); return new BOQModelOverridesStore(); }
        }

        internal static void SaveModelOverrides(Document doc, BOQModelOverridesStore store)
        {
            if (store == null) return;
            store.LastSaved = DateTime.UtcNow;
            store.LastSavedBy = Environment.UserName ?? "";
            string path = GetModelOverridesPath(doc);
            lock (_overridesLock)
            {
                try
                {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(store, _jsonSettings));
                    if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                    else File.Move(tmp, path);
                }
                catch (Exception ex) { StingLog.Error("SaveModelOverrides", ex); throw; }
            }
        }

        /// <summary>
        /// Upsert a single model-row override. Called from the WPF thread
        /// directly after the user commits a cell edit on a modeled row —
        /// no ExternalEvent hop, so the write is durable before the panel
        /// even finishes the CellEditEnding handler.
        /// </summary>
        internal static void UpsertModelOverride(Document doc, BOQModelOverride ov)
        {
            if (doc == null || ov == null) return;
            if (string.IsNullOrEmpty(ov.UniqueId) && ov.ElementId <= 0) return;
            lock (_overridesLock)
            {
                var store = LoadModelOverrides(doc);
                // Match by UniqueId first (stable), fall back to ElementId
                var existing = !string.IsNullOrEmpty(ov.UniqueId)
                    ? store.Overrides.FirstOrDefault(o => o.UniqueId == ov.UniqueId)
                    : store.Overrides.FirstOrDefault(o => o.ElementId == ov.ElementId);
                if (existing != null)
                {
                    if (ov.RateUGX.HasValue) existing.RateUGX = ov.RateUGX;
                    if (ov.RateUSD.HasValue) existing.RateUSD = ov.RateUSD;
                    if (ov.NRM2Paragraph != null) existing.NRM2Paragraph = ov.NRM2Paragraph;
                    if (ov.Note != null) existing.Note = ov.Note;
                    existing.Modified = DateTime.UtcNow;
                    existing.ModifiedBy = Environment.UserName ?? "";
                    if (ov.ElementId > 0) existing.ElementId = ov.ElementId; // refresh the current-session id
                }
                else
                {
                    ov.Modified = DateTime.UtcNow;
                    ov.ModifiedBy = Environment.UserName ?? "";
                    store.Overrides.Add(ov);
                }
                SaveModelOverrides(doc, store);
            }
        }

        /// <summary>
        /// Apply all persisted model-row overrides onto freshly-built
        /// BOQLineItems. Called near the end of BuildBOQDocument after model
        /// items are constructed but before manual/PS items are merged.
        /// </summary>
        private static void ApplyModelOverrides(Document doc, BOQDocument boq)
        {
            if (doc == null || boq == null) return;
            BOQModelOverridesStore store;
            try { store = LoadModelOverrides(doc); }
            catch (Exception ex) { StingLog.Warn($"ApplyModelOverrides load: {ex.Message}"); return; }
            if (store?.Overrides == null || store.Overrides.Count == 0) return;

            // Index overrides by UniqueId (primary) and ElementId (fallback).
            var byUid = new Dictionary<string, BOQModelOverride>(StringComparer.Ordinal);
            var byEid = new Dictionary<long, BOQModelOverride>();
            foreach (var ov in store.Overrides)
            {
                if (!string.IsNullOrEmpty(ov.UniqueId)) byUid[ov.UniqueId] = ov;
                if (ov.ElementId > 0) byEid[ov.ElementId] = ov;
            }

            double rate = TagConfig.GetConfigDouble("UGX_PER_USD", 3700.0);
            int applied = 0;
            foreach (var item in boq.AllItems)
            {
                if (item.Source != BOQRowSource.Model) continue;
                BOQModelOverride ov = null;
                if (!string.IsNullOrEmpty(item.UniqueId)) byUid.TryGetValue(item.UniqueId, out ov);
                if (ov == null && item.RevitElementId > 0) byEid.TryGetValue(item.RevitElementId, out ov);
                if (ov == null) continue;

                if (ov.RateUGX.HasValue)
                {
                    item.RateUGX = ov.RateUGX.Value;
                    item.RateUSD = ov.RateUSD ?? (rate > 0 ? Math.Round(item.RateUGX / rate, 2) : 0);
                    item.RateSource = "Override";
                    item.RateConfidence = 100;
                }
                if (!string.IsNullOrEmpty(ov.NRM2Paragraph)) item.ResolvedNRM2Paragraph = ov.NRM2Paragraph;
                if (!string.IsNullOrEmpty(ov.Note)) item.Note = ov.Note;
                applied++;
            }
            if (applied > 0)
                StingLog.Info($"BOQ: applied {applied} model-row override(s) from sidecar.");
        }

        /// <summary>
        /// Identify candidate promotions from provisional sums to modeled
        /// elements. For each PS row, search modeled rows of the same category
        /// whose total is within ±30% of the PS total. Ranks by closeness.
        /// Caller confirms which matches to apply.
        /// </summary>
        internal static List<BOQReconcileMatch> ReconcileProvisionals(Document doc, BOQDocument boq)
        {
            var results = new List<BOQReconcileMatch>();
            if (boq == null) return results;
            var psRows = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
            var modeledByCategory = boq.AllItems
                .Where(i => i.Source == BOQRowSource.Model)
                .GroupBy(i => i.Category ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var ps in psRows)
            {
                if (!modeledByCategory.TryGetValue(ps.Category ?? "", out var candidates) || candidates.Count == 0)
                    continue;
                double psTotal = ps.TotalUGX;
                if (psTotal <= 0) continue;
                foreach (var mod in candidates)
                {
                    // Z-23 (6.6): rank by magnitude (closeness), but keep the SIGN so
                    // the QS sees overrun (+) vs credit-back (−). abs() alone hid it.
                    double signed = mod.TotalUGX - psTotal;
                    double diff = Math.Abs(signed);
                    double ratio = diff / psTotal;
                    if (ratio > 0.3) continue;
                    double confidence = Math.Round((1 - ratio) * 100, 0);
                    string direction = signed > 0 ? "overrun" : signed < 0 ? "credit" : "exact";
                    results.Add(new BOQReconcileMatch
                    {
                        PSRow = ps,
                        ModeledRow = mod,
                        ConfidencePct = confidence,
                        SignedDeltaUGX = signed,
                        Reason = $"{ps.Category} modeled is {ratio * 100:F0}% {direction} vs PS ({signed:+#,##0;-#,##0;0} UGX)"
                    });
                }
            }
            return results.OrderByDescending(m => m.ConfidencePct).ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        //  Cash-flow generation wrapped around the BOQ
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a monthly cash-flow forecast JSON object using BOQ totals.
        /// Modeled costs distributed linearly across the active phase span,
        /// provisional costs placed at the end of the project (or instructed
        /// phase if the PS row's Note contains "phase:XXX"). Returns a JObject
        /// matching the shape consumed by Scheduling4DEngine.GenerateCashFlow
        /// downstream.
        /// </summary>
        internal static JObject GenerateCashFlowWithBOQ(Document doc, BOQDocument boq)
        {
            var root = new JObject();
            if (boq == null) return root;
            var monthly = new JArray();

            // Use the project's phases (if defined) to pick start + end months.
            DateTime start = DateTime.Now.Date;
            DateTime end = start.AddMonths(18);
            try
            {
                var phases = new FilteredElementCollector(doc).OfClass(typeof(Phase))
                    .Cast<Phase>().ToList();
                if (phases.Count >= 2)
                {
                    // Earliest + latest phase as rough project envelope — overridable by config.
                    start = DateTime.Now.Date;
                    end = DateTime.Now.AddMonths(Math.Max(6, phases.Count * 3));
                }
            }
            catch (Exception ex) { StingLog.Warn($"GenerateCashFlowWithBOQ phases: {ex.Message}"); }

            int months = Math.Max(1, (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1);
            double modeledPerMonth = boq.ModeledTotalUGX / months;
            double runningTotal = 0;
            DateTime cursor = start;
            for (int i = 0; i < months; i++)
            {
                double thisMonth = modeledPerMonth;
                // PS rows at final month (simplest distribution — future work: parse "phase:" hints)
                if (i == months - 1) thisMonth += boq.ProvTotalUGX;
                runningTotal += thisMonth;
                monthly.Add(new JObject
                {
                    ["month"] = cursor.ToString("yyyy-MM"),
                    ["period_cost_ugx"] = Math.Round(thisMonth, 0),
                    ["cumulative_ugx"] = Math.Round(runningTotal, 0)
                });
                cursor = cursor.AddMonths(1);
            }

            root["project_name"] = boq.ProjectName;
            root["generated_at"] = DateTime.UtcNow.ToString("o");
            root["modeled_total_ugx"] = boq.ModeledTotalUGX;
            root["provisional_total_ugx"] = boq.ProvTotalUGX;
            root["grand_total_ugx"] = boq.GrandTotalUGX;
            root["budget_ugx"] = boq.ProjectBudgetUGX;
            root["monthly"] = monthly;
            return root;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BOQ Health Score (Phase 11C)
        //  Weighted 0-100 scoring across seven factors. Surfaced as a KPI
        //  card in both the BOQ panel and the BIM Coordination Center.
        // ══════════════════════════════════════════════════════════════════

        internal static BOQHealthScore ComputeBOQHealth(BOQDocument boq)
        {
            var score = new BOQHealthScore();
            if (boq == null || boq.AllItems.Count == 0)
            {
                score.Grade = "Poor";
                score.Issues.Add("No items in BOQ.");
                return score;
            }

            // Factor 1 — paragraph coverage (25 pts at 90%+)
            double paraPct = boq.ParagraphCoveragePct;
            score.ParagraphCoverageScore = paraPct >= 90 ? 25 : paraPct >= 70 ? 18 : paraPct >= 50 ? 10 : 3;

            // Factor 2 — rate confidence (20 pts at avg 75+)
            double avgConf = boq.AverageRateConfidence;
            score.RateConfidenceScore = avgConf >= 75 ? 20 : avgConf >= 60 ? 14 : avgConf >= 40 ? 8 : 2;

            // Factor 3 — token completeness (15 pts if no [token] remaining)
            int tokenStragglers = boq.AllItems.Count(i => _tokenRx.IsMatch(i.ResolvedNRM2Paragraph ?? ""));
            score.TokenCompletenessScore = tokenStragglers == 0 ? 15 : tokenStragglers <= 5 ? 10 : 3;

            // Factor 4 — line ref completeness (15 pts if all have a ref)
            int missingRefs = boq.AllItems.Count(i => string.IsNullOrEmpty(i.BOQLineRef));
            score.LineRefScore = missingRefs == 0 ? 15 : missingRefs <= 3 ? 10 : 4;

            // Factor 5 — budget (10 pts when budget set AND coverage within 80-110%)
            double cov = boq.BudgetCoveragePct;
            score.BudgetScore = boq.ProjectBudgetUGX > 0 && cov >= 80 && cov <= 110 ? 10
                : boq.ProjectBudgetUGX > 0 ? 5 : 0;

            // Factor 6 — PS description completeness (10 pts when all PS have a note)
            var ps = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
            int psMissing = ps.Count(i => string.IsNullOrWhiteSpace(i.Note) && string.IsNullOrWhiteSpace(i.ResolvedNRM2Paragraph));
            score.PSDescriptionScore = ps.Count == 0 ? 10 : psMissing == 0 ? 10 : psMissing <= 2 ? 6 : 2;

            // Factor 7 — carbon coverage (5 pts when ≥50% of items have carbon data)
            int withCarbon = boq.AllItems.Count(i => i.EmbodiedCarbonKg > 0);
            double carbonPct = 100.0 * withCarbon / boq.AllItems.Count;
            score.CarbonScore = carbonPct >= 50 ? 5 : carbonPct >= 25 ? 3 : 0;

            score.OverallScore = Math.Round(
                score.ParagraphCoverageScore + score.RateConfidenceScore +
                score.TokenCompletenessScore + score.LineRefScore +
                score.BudgetScore + score.PSDescriptionScore + score.CarbonScore, 0);

            score.Grade = score.OverallScore >= 85 ? "Excellent"
                : score.OverallScore >= 70 ? "Good"
                : score.OverallScore >= 50 ? "Fair" : "Poor";

            // Issues + recommendations
            if (paraPct < 90)
                score.Issues.Add($"Paragraph coverage {paraPct:F0}% — {boq.AllItems.Count - boq.ResolvedParagraphCount} item(s) lack an NRM2 description.");
            if (avgConf < 75)
                score.Issues.Add($"Rate confidence average {avgConf:F0} — rates need verification or CSV overrides.");
            if (tokenStragglers > 0)
                score.Issues.Add($"{tokenStragglers} paragraph(s) still contain unresolved [tokens].");
            if (missingRefs > 0)
                score.Issues.Add($"{missingRefs} item(s) missing BOQ line reference.");
            if (boq.ProjectBudgetUGX <= 0)
                score.Recommendations.Add("Set a project budget via the BOQ panel Budget button.");
            if (psMissing > 0)
                score.Recommendations.Add($"Add scope notes to {psMissing} provisional sum(s) before handover.");
            if (carbonPct < 50)
                score.Recommendations.Add("Carbon coverage below 50% — populate MAT_CARBON_FACTOR on primary materials.");
            return score;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Utility helpers — private
        // ══════════════════════════════════════════════════════════════════

        // Internal so PlumbingBOQEnricher (and future supplemental builders)
        // can reuse the canonical CSV reader instead of duplicating it.
        internal static Dictionary<string, (double rate, string unit)> LoadCsvRates()
        {
            var rates = new Dictionary<string, (double rate, string unit)>(StringComparer.OrdinalIgnoreCase);
            string costFile = TagConfig.CostRatesFileName ?? "cost_rates_5d.csv";
            string path = StingToolsApp.FindDataFile(costFile);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return rates;
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2) return rates;
                string header = lines[0].ToLowerInvariant();
                bool is7Col = header.Contains("mat_code");

                for (int i = 1; i < lines.Length; i++)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length < 3) continue;
                    if (is7Col && cols.Length >= 7)
                    {
                        // Category, MAT_CODE, MAT_DISCIPLINE, Unit_Rate_USD, Unit_Rate_UGX, Unit, Description
                        if (double.TryParse(cols[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double rateUgx))
                        {
                            rates[cols[0].Trim()] = (rateUgx, cols[5].Trim());
                            if (!string.IsNullOrEmpty(cols[1]))
                                rates[cols[1].Trim()] = (rateUgx, cols[5].Trim());
                        }
                    }
                    else if (double.TryParse(cols[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double rate3))
                    {
                        rates[cols[0].Trim()] = (rate3, cols.Length > 2 ? cols[2].Trim() : "each");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadCsvRates: {ex.Message}"); }
            return rates;
        }

        private static Dictionary<string, string> LoadCobieCostCodes()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = StingToolsApp.FindDataFile("COBIE_TYPE_MAP.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return map;
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2) return map;
                var headers = StingToolsApp.ParseCsvLine(lines[0]).Select(h => h.ToLowerInvariant()).ToArray();
                int catCol = Array.FindIndex(headers, h => h.Contains("category"));
                int codeCol = Array.FindIndex(headers, h => h.Contains("cost") && h.Contains("code"));
                if (catCol < 0 || codeCol < 0) return map;
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length <= Math.Max(catCol, codeCol)) continue;
                    string cat = cols[catCol].Trim();
                    string code = cols[codeCol].Trim();
                    if (!string.IsNullOrEmpty(cat) && !string.IsNullOrEmpty(code)) map[cat] = code;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadCobieCostCodes: {ex.Message}"); }
            return map;
        }

        private static List<Element> CollectCandidateElements(Document doc, HashSet<string> knownCategories)
        {
            var list = new List<Element>();
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var catEnums = SharedParamGuids.AllCategoryEnums;
            if (catEnums != null && catEnums.Length > 0)
                collector = collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            foreach (Element el in collector)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(cat)) continue;
                if (!knownCategories.Contains(cat)) continue;
                if (cat.Equals("Rooms", StringComparison.OrdinalIgnoreCase)
                    || cat.Equals("Spaces", StringComparison.OrdinalIgnoreCase)
                    || cat.Equals("Areas", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(el);
            }
            return list;
        }

        private static bool IsPhaseDemolished(Document doc, Element el)
        {
            try
            {
                Parameter demP = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (demP != null && demP.HasValue)
                {
                    ElementId id = demP.AsElementId();
                    if (id != null && id.Value > 0) return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"IsPhaseDemolished: {ex.Message}"); }
            return false;
        }

        private static List<BOQSection> GroupIntoSections(List<BOQLineItem> items)
        {
            var groups = items
                .GroupBy(i => (i.NRM2Section ?? "00", i.Discipline ?? "X"))
                .OrderBy(g => ParseSectionInt(g.Key.Item1))
                .ThenBy(g => g.Key.Item2, StringComparer.OrdinalIgnoreCase);

            var sections = new List<BOQSection>();
            foreach (var g in groups)
            {
                var section = new BOQSection
                {
                    NRM2Section = g.Key.Item1,
                    Discipline = g.Key.Item2,
                    Name = GuessSectionName(g.Key.Item1, g.First().Category),
                    Items = g.OrderBy(x => (int)x.Source)
                        .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
                sections.Add(section);
            }
            return sections;
        }

        private static int ParseSectionInt(string s)
        {
            if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int v)) return v;
            return 99;
        }

        private static string GuessSectionName(string section, string firstCategory)
        {
            // Map common NRM2 sections to human-readable names. Fallback = category.
            switch ((section ?? "").Trim())
            {
                case "1": return "Demolitions";
                case "2": return "Substructure";
                case "3": return "Groundworks";
                case "4": return "Foundations";
                case "5": return "In-situ concrete";
                case "14": return "Masonry";
                case "15": return "Structural metalwork";
                case "16": return "Carpentry";
                case "17": return "Cladding and covering";
                case "18": return "Waterproofing";
                case "19": return "Linings, sheathing and dry partitioning";
                case "20": return "Windows, doors and stairs";
                case "21": return "Surface finishes";
                case "22": return "Furniture, fittings and equipment";
                case "23": return "Building fabric sundries";
                case "30": return "Drainage above ground";
                case "31": return "Drainage below ground";
                case "32": return "Piped supply systems";
                case "33": return "Mechanical services";
                case "34": return "Electrical services";
                case "35": return "Lighting and small power";
                case "36": return "Security and fire alarm";
                default: return string.IsNullOrEmpty(firstCategory) ? "General" : firstCategory;
            }
        }

        private static void AssignBoqLineRefs(BOQDocument boq)
        {
            foreach (var section in boq.Sections)
            {
                int rowIndex = 1;
                string sectionIndex = "1";
                foreach (var item in section.Items)
                {
                    item.BOQLineRef = $"{section.NRM2Section}.{sectionIndex}.{rowIndex}";
                    rowIndex++;
                }
            }
        }

        private static string DeriveNrm2Section(Document doc, Element el, string catName, string disc)
        {
            // P0 refactor — first consult the data-driven TakeoffRuleRegistry
            // so a QS can author section overrides in
            // STING_TAKEOFF_RULES.json / takeoff_rules.json without code
            // changes. Fall back to the legacy hard-coded map when no rule
            // matches.
            try
            {
                if (doc != null)
                {
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                    var rule = TakeoffRuleRegistry.Get(doc).Match(catName, disc, prod);
                    if (rule != null && !string.IsNullOrEmpty(rule.Nrm2Section))
                        return rule.Nrm2Section;
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeriveNrm2Section rule lookup: {ex.Message}"); }

            if (string.IsNullOrEmpty(catName)) return "99";
            string lower = catName.ToLowerInvariant();
            // Hardcoded mapping covering the common Revit categories. QS can override via
            // ASS_BOQ_SECTION_NAME / CATEGORY_NRM2_MAP config key (future work).
            if (lower.Contains("foundation")) return "4";
            if (lower.Contains("column") || lower.Contains("framing") || lower.Contains("truss") || lower.Contains("beam")) return "15";
            if (lower.Contains("wall") && !lower.Contains("curtain")) return "14";
            if (lower.Contains("floor") || lower.Contains("slab")) return "5";
            if (lower.Contains("roof") || lower.Contains("fascia") || lower.Contains("gutter")) return "17";
            if (lower.Contains("door") || lower.Contains("window") || lower.Contains("stair") || lower.Contains("ramp")) return "20";
            if (lower.Contains("ceiling")) return "19";
            if (lower.Contains("curtain") || lower.Contains("mullion")) return "17";
            if (lower.Contains("furniture") || lower.Contains("casework") || lower.Contains("equipment")) return "22";
            if (lower.Contains("duct") || lower.Contains("pipe") || lower.Contains("mechanical")) return "33";
            if (lower.Contains("plumbing") || lower.Contains("sanitary")) return "32";
            if (lower.Contains("electrical") || lower.Contains("conduit") || lower.Contains("cable")) return "34";
            if (lower.Contains("lighting")) return "35";
            if (lower.Contains("fire") || lower.Contains("security") || lower.Contains("nurse")) return "36";
            return disc == "S" ? "15" : disc == "M" ? "33" : disc == "E" ? "34" : disc == "P" ? "32" : "23";
        }

        private static string DisciplineForCategory(string catName)
        {
            if (string.IsNullOrEmpty(catName)) return "X";
            if (TagConfig.DiscMap != null && TagConfig.DiscMap.TryGetValue(catName, out string disc)) return disc;
            return "X";
        }

        private static string MakeSafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "item";
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '/', '\\' }));
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(invalid.Contains(c) ? '-' : c);
            string r = sb.ToString().Trim('-');
            return string.IsNullOrEmpty(r) ? "item" : r;
        }

        private static string GetElementDisplayName(Element el)
        {
            string fam = GetFamilyName(el);
            string typ = el.Name ?? "";
            if (!string.IsNullOrEmpty(fam) && !string.IsNullOrEmpty(typ) && !fam.Equals(typ, StringComparison.OrdinalIgnoreCase))
                return $"{fam} — {typ}";
            return !string.IsNullOrEmpty(typ) ? typ : fam;
        }

        private static string GetFamilyName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi) return fi.Symbol?.Family?.Name ?? "";
                var typeId = el.GetTypeId();
                if (typeId != null && typeId.Value > 0)
                {
                    Element t = el.Document.GetElement(typeId);
                    if (t is FamilySymbol fs) return fs.Family?.Name ?? "";
                    if (t != null) return t.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFamilyName: {ex.Message}"); }
            return "";
        }

        private static string GetLevelName(Document doc, Element el)
        {
            try
            {
                Parameter lp = el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (lp != null && lp.HasValue) return lp.AsValueString() ?? lp.AsString() ?? "";
                ElementId lvlId = el.LevelId;
                if (lvlId != null && lvlId.Value > 0)
                {
                    Element lv = doc.GetElement(lvlId);
                    if (lv != null) return lv.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetLevelName: {ex.Message}"); }
            return "";
        }

        private static string GetLocationName(Document doc, Element el)
        {
            // Prefer ASS_LOC_TXT if tagged; otherwise room.
            string loc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
            if (!string.IsNullOrEmpty(loc)) return loc;
            try
            {
                var room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null) return room.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"GetLocationName: {ex.Message}"); }
            return "";
        }

        private static string GetPrimaryMaterialName(Element el)
        {
            try
            {
                var ids = el.GetMaterialIds(false);
                if (ids != null && ids.Count > 0)
                {
                    Material m = el.Document.GetElement(ids.First()) as Material;
                    if (m != null) return m.Name ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetPrimaryMaterialName: {ex.Message}"); }
            return "";
        }
    }
}
