// ══════════════════════════════════════════════════════════════════════════
//  MaterialScheduleBuilder.cs — MAT-SCHED Revit-side gather.
//
//  Thin by design: read the BOQ, the manual store and the data files, hand
//  plain POCOs to the engine, return its document. No arithmetic here — every
//  number is computed in Core/MaterialSchedule where it is unit-tested.
//
//  C1: compound take-off is OFF by default (COST_COMPOUND_TAKEOFF). With it off
//  there are no constituent rows and the schedule would build EMPTY. We detect
//  that and report it rather than shipping a blank document.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.BOQ.MaterialSchedule
{
    internal sealed class MaterialScheduleBuildResult
    {
        public MaterialScheduleDocument Document;
        public bool CompoundTakeoffWasOff;
        public int ConstituentRowsSeen;
        public int RowsWithoutKind;
        public List<string> Warnings = new List<string>();
    }

    internal static class MaterialScheduleBuilder
    {
        /// <summary>Programme length for the tools model. 0 = not stated; the
        /// command asks the user rather than the builder guessing.</summary>
        public static int DurationDaysOverride;

        public static MaterialScheduleBuildResult Build(Document doc, MaterialScheduleOptions options)
        {
            var result = new MaterialScheduleBuildResult();
            if (doc == null) { result.Document = new MaterialScheduleDocument(); return result; }

            result.CompoundTakeoffWasOff = !Takeoff.CompoundTakeoffBuilder.Enabled();

            // Before the take-off, not after: the scan counts what THIS run
            // inspected, and a stale count from a previous export would answer
            // the wrong question.
            Takeoff.CompoundTakeoffBuilder.ResetLayerScans();

            var boq = BOQCostManager.BuildBOQDocument(doc);
            var inputs = new AggregatorInputs
            {
                Units = LoadUnits(doc),
                StageDefs = new List<StageDefinition>(),
                Options = options ?? new MaterialScheduleOptions()
            };

            var lib = LoadStages(doc);
            inputs.StageDefs = lib.Stages;
            inputs.DefaultStageId = lib.DefaultStageId;
            inputs.ExcludedCategories = lib.ExcludedCategories;
            inputs.ExcludedDescriptionPatterns = lib.ExcludedDescriptionPatterns;
            inputs.ExclusionProtectedCategories = lib.ExclusionProtectedCategories;
            inputs.IntermediateMeasures = lib.IntermediateMeasures;
            inputs.Rates = LoadRates(doc);

            foreach (var item in boq.AllItems.Where(i => i.Source == BOQRowSource.Model))
            {
                result.ConstituentRowsSeen++;
                if (string.IsNullOrWhiteSpace(item.ConstituentKind)) result.RowsWithoutKind++;

                inputs.Constituents.Add(new ConstituentInput
                {
                    ConstituentKind = item.ConstituentKind ?? "",
                    Category = item.Category ?? "",
                    TypeName = item.TypeName ?? "",
                    Description = item.ItemName ?? "",
                    Unit = BoqUnits.Normalise(item.Unit),
                    Quantity = item.Quantity,
                    LevelCode = item.Level ?? "",
                    TraceRef = string.IsNullOrEmpty(item.BOQLineRef) ? item.Id : item.BOQLineRef
                });
            }

            // SECOND finish source. The layer source reads a TYPE's compound
            // structure; this reads what the ROOM says. Gated so the two can
            // never measure the same surface: if any type carried a tiled
            // layer, room tiling is skipped entirely. Skirting is never
            // suppressed — no layer source produces it.
            var roomTally = new RoomFinishTally();
            try
            {
                bool layerTiling = Takeoff.CompoundTakeoffBuilder.TileFinishScan.Tally.TypesMatched > 0;
                inputs.Constituents.AddRange(RoomFinishGatherer.Gather(doc, layerTiling, roomTally));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Room finishes could not be read: {ex.Message}");
                StingLog.Warn($"MaterialScheduleBuilder room finishes: {ex.Message}");
            }

            // MATSCHED-T4 — ratio-derived consumables, appended as ordinary
            // constituent rows BEFORE aggregation so they are staged,
            // unit-checked, converted and priced by exactly the same machinery
            // as a measured commodity. The site-tools section bypasses all of
            // that; a second untested path to the page is not worth repeating.
            //
            // Drivers are read from the rows that already exist, and the derived
            // rows are added AFTER that read, so a consumable can never become
            // the driver of another consumable.
            var consumablesTally = new ConsumablesTally();
            try
            {
                var conLib = LoadConsumables(doc);
                var drivers = ConsumableDrivers.From(inputs.Constituents, inputs.Units);
                foreach (string m in drivers.UnitMismatches) consumablesTally.UnitMismatches.Add(m);
                consumablesTally.RoofCoveringUnattributedM2 = drivers.RoofCoveringUnattributedM2;
                inputs.Constituents.AddRange(
                    ConsumablesCalculator.Quantify(drivers, conLib.Rules, consumablesTally));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Ratio-derived consumables could not be estimated: {ex.Message}");
                StingLog.Warn($"MaterialScheduleBuilder consumables: {ex.Message}");
            }

            var msDoc = CommodityAggregator.Build(inputs);
            msDoc.ProjectName = doc.ProjectInformation?.Name ?? "";
            msDoc.ProjectCode = doc.ProjectInformation?.Number ?? "";

            AppendManualRows(doc, msDoc, boq, lib.Stages, result);
            AppendSiteTools(doc, msDoc, lib, inputs.Rates, result);
            Reconciler.Check(msDoc);

            // After every row exists and every rate is resolved, and after the
            // site-tools and manual rows are appended so their rates count too.
            // The column says it per row; this says it once.
            if (msDoc.Options.ShowPrices)
            {
                string provenance = RateProvenanceLabel.Summary(
                    msDoc.Stages.SelectMany(st => st.Commodities));
                if (!string.IsNullOrEmpty(provenance)) msDoc.Warnings.Add(provenance);
            }

            result.Document = msDoc;
            if (result.CompoundTakeoffWasOff)
                result.Warnings.Add(
                    "Compound take-off is disabled (COST_COMPOUND_TAKEOFF). Walls and slabs were "
                  + "priced as single composite rates, so no cement / sand / block commodities were "
                  + "produced. Enable it in project config and re-run for a full material schedule.");
            if (msDoc.ExcludedRowCount > 0)
                result.Warnings.Add(
                    $"{msDoc.ExcludedRowCount} row(s) excluded as not-a-material "
                  + $"({string.Join(", ", msDoc.ExcludedByCategory.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} x{kv.Value}"))}"
                  + (msDoc.ExcludedByCategory.Count > 5 ? ", …" : "") + "). "
                  + "Edit excludedCategories in the stage library to change this.");
            if (result.RowsWithoutKind > 0)
                result.Warnings.Add(
                    $"{result.RowsWithoutKind} of {result.ConstituentRowsSeen} model rows carried no "
                  + $"constituent kind and were routed to the default stage.");

            // Reported whether or not tiling was found. A schedule with no tiling
            // rows is either a model that describes no finishes or a pattern that
            // failed to recognise them, and only the denominator tells them apart.
            string tileScan = Takeoff.CompoundTakeoffBuilder.TileFinishScan.Summary();
            if (!string.IsNullOrEmpty(tileScan)) result.Warnings.Add(tileScan);

            // Same contract, same reason (MATSCHED-T1): a schedule with no screed
            // cement means one of four unrelated things, and only the denominator
            // tells them apart.
            string screedScan = Takeoff.CompoundTakeoffBuilder.ScreedScan.Summary();
            if (!string.IsNullOrEmpty(screedScan)) result.Warnings.Add(screedScan);

            // MATSCHED-T2 — ceilings decomposed into nothing at all before this,
            // and an empty result is indistinguishable from a model with no
            // ceilings unless the scan says which it was.
            string ceilingScan = Takeoff.CompoundTakeoffBuilder.CeilingScan.Summary();
            if (!string.IsNullOrEmpty(ceilingScan)) result.Warnings.Add(ceilingScan);

            // The furring banner is separate and CONDITIONAL: a ratio must never
            // be presented as a measurement, and a banner qualifying a row that
            // was never emitted is noise.
            string furringBanner = Takeoff.CompoundTakeoffBuilder.CeilingScan.Tally.FurringBanner();
            if (!string.IsNullOrEmpty(furringBanner)) result.Warnings.Add(furringBanner);

            // MATSCHED-T3 — membranes were ignored entirely. The scan also
            // reports the two things this take-off deliberately does NOT price
            // (insulation, and wall membranes), so each is a stated decision
            // rather than an unexplained absence.
            string membraneScan = Takeoff.CompoundTakeoffBuilder.MembraneScan.Summary();
            if (!string.IsNullOrEmpty(membraneScan)) result.Warnings.Add(membraneScan);

            // MATSCHED-T4 — the denominator, then the honesty banner. Separate
            // lines because they answer different questions: the first says what
            // was looked at, the second says what the numbers ARE. The banner is
            // conditional on something having been derived.
            string consumablesScan = consumablesTally.Summary();
            if (!string.IsNullOrEmpty(consumablesScan)) result.Warnings.Add(consumablesScan);

            string consumablesBanner = consumablesTally.Banner();
            if (!string.IsNullOrEmpty(consumablesBanner)) result.Warnings.Add(consumablesBanner);

            // MATSCHED-T5 — this one reports more about what was NOT measured
            // than about what was, on purpose: of fascia, barge board and ridge
            // cap, only the fascia has a length the footprint states outright.
            string roofScan = Takeoff.CompoundTakeoffBuilder.RoofAccessoryScan.Summary();
            if (!string.IsNullOrEmpty(roofScan)) result.Warnings.Add(roofScan);

            string roomScan = roomTally.Summary();
            if (!string.IsNullOrEmpty(roomScan)) result.Warnings.Add(roomScan);

            // AFTER every warning is collected, so the workbook and the dialog
            // can never disagree about what this run reported.
            msDoc.Warnings.AddRange(result.Warnings);

            StingLog.Info($"MaterialScheduleBuilder: {msDoc.Stages.Count} stage(s), "
                        + $"{msDoc.Stages.Sum(s => s.Commodities.Count)} commodity row(s), "
                        + $"{msDoc.Reconciliation.Issues.Count} reconciliation issue(s).");
            return result;
        }

        /// <summary>
        /// MATSCHED-9 — site tools, derived from the gang sizes the measured work
        /// implies. Needs a programme duration: without one there is no
        /// denominator, so nothing is produced and the reason is reported.
        /// </summary>
        private static void AppendSiteTools(Document doc, MaterialScheduleDocument msDoc,
            StageLibrary lib, CommodityRateResolver rates, MaterialScheduleBuildResult result)
        {
            try
            {
                int days = DurationDaysOverride > 0
                    ? DurationDaysOverride
                    : SiteToolsGatherer.ReadDurationDays(doc);
                if (days <= 0)
                {
                    result.Warnings.Add(
                        "Site tools omitted: no programme duration. Set "
                      + $"{SiteToolsGatherer.DurationParam} on Project Information, or enter it when "
                      + "prompted. Gang sizes divide by the programme, so without it every tool "
                      + "quantity would be invented.");
                    return;
                }

                var toolLib = LoadTools(doc);
                if (toolLib?.Rules == null || toolLib.Rules.Count == 0) return;

                int storeys = SiteToolsGatherer.CountStoreys(doc);
                var input = SiteToolsGatherer.FromDocument(msDoc, days, storeys);
                var gangs = SiteToolsCalculator.DeriveGangs(input, toolLib.TradeRates);
                var tools = SiteToolsCalculator.Quantify(gangs, toolLib.Rules, storeys);
                if (tools.Count == 0) return;

                var def = lib.Stages.FirstOrDefault(d =>
                    string.Equals(d.StageId, "tools", StringComparison.OrdinalIgnoreCase));
                var section = new StageSection
                {
                    StageId = "tools",
                    Title = def?.Title ?? "TOOLS AND EQUIPMENT",
                    Preamble = def?.Preamble ?? "Site establishment tools and small plant."
                };

                foreach (var t in tools)
                {
                    var rate = rates?.Resolve(t.ToolKey)
                               ?? new CommodityRate { RateUGX = 0, Source = "unpriced" };
                    section.Commodities.Add(new MaterialCommodity
                    {
                        CommodityKey = t.ToolKey,
                        Description = t.Description,
                        SupplierUnit = t.SupplierUnit,
                        NetQuantity = t.Quantity,
                        OrderQuantity = t.Quantity,
                        RateUGX = rate.RateUGX,
                        RateSource = rate.Source
                    });
                }

                ManualRowPlacer.InsertByDefinitionOrder(msDoc.Stages, lib.Stages, section);
                StageMapper.AssignLetters(msDoc.Stages);

                result.Warnings.Add(
                    $"Site tools estimated from a {days}-day programme and {storeys} storey(s): "
                  + $"{gangs.Masons} mason(s), {gangs.Helpers} helper(s), {gangs.BarBenders} bar-bender(s), "
                  + $"{gangs.Carpenters} carpenter(s). These are PRACTICE HEURISTICS, not a standard "
                  + "— NRM2 prices tools in preliminaries. Review before issue.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaterialScheduleBuilder.AppendSiteTools: {ex.Message}");
                result.Warnings.Add($"Site tools could not be estimated: {ex.Message}");
            }
        }

        /// <summary>
        /// Corporate consumable ratios plus the project override, merged by
        /// constituentKind so a project can re-rate one figure without restating
        /// the table. Same shape as LoadTools.
        /// </summary>
        private static ConsumablesLibrary LoadConsumables(Document doc)
        {
            var libr = ReadJson<ConsumablesLibrary>(StingToolsApp.FindDataFile("STING_CONSUMABLES.json"))
                       ?? new ConsumablesLibrary();
            var over = ReadJson<ConsumablesLibrary>(StingPaths.MetaFile(doc, "_BIM_COORD", "consumables.json"));
            if (over != null)
            {
                foreach (var r in over.Rules ?? new List<ConsumableRule>())
                {
                    if (r == null || string.IsNullOrWhiteSpace(r.ConstituentKind)) continue;
                    libr.Rules.RemoveAll(x => string.Equals(x.ConstituentKind, r.ConstituentKind,
                                                            StringComparison.OrdinalIgnoreCase));
                    libr.Rules.Add(r);
                }
            }
            return libr;
        }

        private static SiteToolsLibrary LoadTools(Document doc)
        {
            var libr = ReadJson<SiteToolsLibrary>(StingToolsApp.FindDataFile("STING_SITE_TOOLS.json"))
                       ?? new SiteToolsLibrary();
            var over = ReadJson<SiteToolsLibrary>(StingPaths.MetaFile(doc, "_BIM_COORD", "site_tools.json"));
            if (over != null)
            {
                if (over.TradeRates != null) libr.TradeRates = over.TradeRates;
                foreach (var r in over.Rules ?? new List<ToolRule>())
                {
                    libr.Rules.RemoveAll(x => string.Equals(x.ToolKey, r.ToolKey, StringComparison.OrdinalIgnoreCase));
                    libr.Rules.Add(r);
                }
            }
            return libr;
        }

        /// <summary>
        /// Tools become Manual rows; services become ProvisionalSum rows. Labour is a
        /// QS lump: the BOQ's L/P/M split is nulled on override and on modal-rate
        /// aggregation, so it is offered only as a SUGGESTION and only when every
        /// contributing row carries one.
        /// </summary>
        private static void AppendManualRows(Document doc, MaterialScheduleDocument msDoc,
            BOQDocument boq, List<StageDefinition> stageDefs, MaterialScheduleBuildResult result)
        {
            try
            {
                var ps = boq.AllItems.Where(i => i.Source == BOQRowSource.ProvisionalSum).ToList();
                foreach (var group in ps.GroupBy(i => i.Category ?? ""))
                {
                    // Route through the SAME Categories table the model rows use.
                    // Matching the category against section TITLES minted a
                    // duplicate section — "Electrical Equipment" does not appear
                    // in "ELEMENT 06: ELECTRICAL INSTALLATION" — while the correct
                    // routing sat unused in the stage library. A blank category
                    // still matches nothing.
                    var section = ManualRowPlacer.ResolveSection(msDoc.Stages, stageDefs, group.Key);
                    if (section == null)
                    {
                        string knownStageId = ManualRowPlacer.ResolveStageIdForCategory(stageDefs, group.Key);
                        var def = stageDefs.FirstOrDefault(d =>
                            string.Equals(d.StageId, knownStageId, StringComparison.OrdinalIgnoreCase));
                        bool named = !string.IsNullOrWhiteSpace(group.Key);

                        section = new StageSection
                        {
                            StageId = def?.StageId ?? (named ? "ps-" + group.Key : "ps-uncategorised"),
                            Title = def?.Title ?? (named ? group.Key.ToUpperInvariant()
                                                         : "PROVISIONAL SUMS (UNCATEGORISED)"),
                            Preamble = def?.Preamble ?? ""
                        };
                        // A known stage that carried no modelled commodities still
                        // reads in library order; an unknown one goes to the end.
                        ManualRowPlacer.InsertByDefinitionOrder(msDoc.Stages, stageDefs, section);
                    }
                    foreach (var row in group)
                        section.ProvisionalSums.Add(new ProvisionalSumLine
                        {
                            Description = string.IsNullOrEmpty(row.ResolvedNRM2Paragraph)
                                ? row.ItemName : row.ResolvedNRM2Paragraph,
                            AmountUGX = row.TotalUGX,
                            SourceRef = row.Id
                        });
                }

                // Flatten every model row's labour once, keyed by the SAME trace ref
                // the constituents carried into the aggregator, so each section's
                // suggestion counts only the rows that actually fed it. The previous
                // version summed the whole document per section, so every stage
                // advertised the project's total labour as its own.
                var contributions = boq.AllItems
                    .Where(i => i.Source == BOQRowSource.Model)
                    .Select(i => new LabourContribution
                    {
                        TraceRef = string.IsNullOrEmpty(i.BOQLineRef) ? i.Id : i.BOQLineRef,
                        LabourTotalUGX = i.LabourTotalUGX,
                        HasSplit = i.LabourUGX.HasValue
                    })
                    .ToList();

                // PERF: index once, then O(1) lookups per section instead of a
                // full rescan of every model row for each of them.
                var contributionIndex = ManualRowPlacer.IndexContributions(contributions);
                foreach (var section in msDoc.Stages)
                    section.Labour.Add(ManualRowPlacer.BuildLabourLine(section, contributionIndex));

                StageMapper.AssignLetters(msDoc.Stages);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaterialScheduleBuilder.AppendManualRows: {ex.Message}");
                result.Warnings.Add($"Manual / provisional-sum rows could not be appended: {ex.Message}");
            }
        }

        // ── data loading: corporate baseline, then project override ─────────

        private static SupplierUnitTable LoadUnits(Document doc)
        {
            var table = ReadJson<SupplierUnitTable>(StingToolsApp.FindDataFile("STING_SUPPLIER_UNITS.json"))
                        ?? new SupplierUnitTable();
            var over = ReadJson<SupplierUnitTable>(StingPaths.MetaFile(doc, "_BIM_COORD", "supplier_units.json"));
            if (over?.Rules != null)
                foreach (var r in over.Rules)
                {
                    table.Rules.RemoveAll(x => string.Equals(x.CommodityKey, r.CommodityKey, StringComparison.OrdinalIgnoreCase));
                    table.Rules.Add(r);
                }
            return table;
        }

        private static StageLibrary LoadStages(Document doc)
        {
            var lib = ReadJson<StageLibrary>(StingToolsApp.FindDataFile("STING_MATERIAL_STAGES.json"))
                      ?? new StageLibrary();
            var over = ReadJson<StageLibrary>(StingPaths.MetaFile(doc, "_BIM_COORD", "material_stages.json"));
            if (over != null)
            {
                if (!string.IsNullOrWhiteSpace(over.DefaultStageId)) lib.DefaultStageId = over.DefaultStageId;
                foreach (var s in over.Stages ?? new List<StageDefinition>())
                {
                    lib.Stages.RemoveAll(x => string.Equals(x.StageId, s.StageId, StringComparison.OrdinalIgnoreCase));
                    lib.Stages.Add(s);
                }
            }
            return lib;
        }

        private static CommodityRateResolver LoadRates(Document doc)
        {
            var baseline = new List<CommodityRate>();
            var project = new List<CommodityRate>();

            string basePath = StingToolsApp.FindDataFile("STING_COMMODITY_RATES.csv");
            if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
            {
                baseline = CommodityRateResolver.ParseCsv(File.ReadAllLines(basePath), out var skipped);
                foreach (string s in skipped) StingLog.Warn($"STING_COMMODITY_RATES.csv: unparsed row '{s}'");
            }

            string projPath = StingPaths.MetaFile(doc, "_BIM_COORD", "commodity_rates.csv");
            if (!string.IsNullOrEmpty(projPath) && File.Exists(projPath))
            {
                project = CommodityRateResolver.ParseCsv(File.ReadAllLines(projPath), out var skipped);
                foreach (string s in skipped) StingLog.Warn($"commodity_rates.csv: unparsed row '{s}'");
            }

            return new CommodityRateResolver(baseline, project);
        }

        private static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaterialScheduleBuilder.ReadJson '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
