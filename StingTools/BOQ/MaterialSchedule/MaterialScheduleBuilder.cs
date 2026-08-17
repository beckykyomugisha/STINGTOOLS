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
        public static MaterialScheduleBuildResult Build(Document doc, MaterialScheduleOptions options)
        {
            var result = new MaterialScheduleBuildResult();
            if (doc == null) { result.Document = new MaterialScheduleDocument(); return result; }

            result.CompoundTakeoffWasOff = !Takeoff.CompoundTakeoffBuilder.Enabled();

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

            var msDoc = CommodityAggregator.Build(inputs);
            msDoc.ProjectName = doc.ProjectInformation?.Name ?? "";
            msDoc.ProjectCode = doc.ProjectInformation?.Number ?? "";

            AppendManualRows(doc, msDoc, boq, lib.Stages, result);
            Reconciler.Check(msDoc);

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

            StingLog.Info($"MaterialScheduleBuilder: {msDoc.Stages.Count} stage(s), "
                        + $"{msDoc.Stages.Sum(s => s.Commodities.Count)} commodity row(s), "
                        + $"{msDoc.Reconciliation.Issues.Count} reconciliation issue(s).");
            return result;
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

                foreach (var section in msDoc.Stages)
                    section.Labour.Add(ManualRowPlacer.BuildLabourLine(section, contributions));

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
