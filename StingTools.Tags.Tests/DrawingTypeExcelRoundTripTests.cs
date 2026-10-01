// DTW-179..183 — the Drawing Type / View Style Pack Excel round-trip, run
// against the SHIPPED catalogue. Exporting what ships and importing it back
// untouched must validate clean, record no change and write an empty
// project override; anything else means a user who only opened the
// workbook would freeze, corrupt or lose catalogue data.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.BIMManager;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypeExcelRoundTripTests
    {
        internal static DrawingTypeLibrary ShippedTypes()
        {
            var lib = DrawingCatalogueFixture.Shipped();
            // As DrawingTypeRegistry.LoadCorporate stamps them.
            foreach (var t in lib.DrawingTypes) if (string.IsNullOrEmpty(t.Origin)) t.Origin = "corporate";
            foreach (var r in lib.Routing) if (string.IsNullOrEmpty(r.Origin)) r.Origin = "corporate";
            return lib;
        }

        internal static StylePackDoc ShippedPacks()
        {
            var path = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json");
            var doc = JsonConvert.DeserializeObject<StylePackDoc>(File.ReadAllText(path));
            Assert.True(doc.StylePacks.Count >= 30, $"only {doc.StylePacks.Count} packs bound");
            foreach (var p in doc.StylePacks) if (string.IsNullOrEmpty(p.Origin)) p.Origin = "corporate";
            return doc;
        }

        internal static XLWorkbook Export(DrawingTypeLibrary dt, StylePackDoc packs)
        {
            using var ms = DrawingTypeExcelEngine.ExportWorkbook(dt, packs);
            var copy = new MemoryStream(ms.ToArray());
            return new XLWorkbook(copy);
        }

        // ── DTW-179 ───────────────────────────────────────────────────────

        [Fact]
        public void The_shipped_catalogue_exports_to_a_workbook_that_validates_clean()
        {
            var dt = ShippedTypes();
            var packs = ShippedPacks();
            using var wb = Export(dt, packs);
            var errors = DrawingTypeExcelEngine.ValidateImport(wb, dt, packs)
                .Where(r => r.Severity == ImportSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                errors.Count + " validation error(s):\n" + string.Join("\n", errors.Take(25)));
        }

        // ── DTW-180 ───────────────────────────────────────────────────────

        [Fact]
        public void Routing_rows_carry_every_predicate_and_stay_corporate_when_unedited()
        {
            var dt = ShippedTypes();
            var packs = ShippedPacks();
            Assert.True(dt.Routing.Count(r => !string.IsNullOrEmpty(r.DisciplineMatches) || !string.IsNullOrEmpty(r.DocTypeMatches)) >= 20,
                "fixture: the shipped regex-predicate rules are what this test is about");
            using var wb = Export(dt, packs);
            var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, dt, packs);

            Assert.DoesNotContain(imp.Changes, c => c.EntityType == "Routing");
            var (types, _) = DrawingTypeExcelEngine.BuildProjectOverride(imp.UpdatedDtLib, imp.UpdatedPackLib);
            Assert.Empty(types.Routing);
            // No rule lost its predicates on the way through.
            Assert.Equal(dt.Routing.Select(DrawingRoutingMatcher.SignatureWithTarget),
                         imp.UpdatedDtLib.Routing.Select(DrawingRoutingMatcher.SignatureWithTarget));
        }

        [Fact]
        public void An_added_routing_row_becomes_a_project_rule_with_all_its_predicates()
        {
            var dt = ShippedTypes();
            var packs = ShippedPacks();
            using var wb = Export(dt, packs);
            var ws = wb.Worksheet("Routing");
            int row = ws.LastRowUsed().RowNumber() + 1;
            var headers = DrawingTypeExcelEngine.RoutingHeaders.ToList();
            void Set(string h, string v) => ws.Cell(row, headers.IndexOf(h) + 1).Value = v;
            Set("discipline", "A"); Set("docType", "PLAN");
            Set("disciplineMatches", "^A$"); Set("optionMatches", "^VE");
            Set("drawingTypeId", dt.DrawingTypes[0].Id);

            var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, dt, packs);
            var (types, _) = DrawingTypeExcelEngine.BuildProjectOverride(imp.UpdatedDtLib, imp.UpdatedPackLib);
            var rule = Assert.Single(types.Routing);
            Assert.Equal("project", rule.Origin);
            Assert.Equal("^A$", rule.DisciplineMatches);
            Assert.Equal("^VE", rule.OptionMatches);
            Assert.Contains(imp.Changes, c => c.EntityType == "Routing");
        }

        // ── DTW-182 ───────────────────────────────────────────────────────

        [Fact]
        public void Decimal_cells_import_under_a_comma_decimal_culture()
        {
            var prior = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var dt = ShippedTypes();
                var packs = ShippedPacks();
                using var wb = Export(dt, packs);

                var slots = wb.Worksheet("Slots");
                var slotDt = slots.Cell(2, 1).GetString();
                slots.Cell(2, 4).Value = 0.37;                       // a number cell
                var types = wb.Worksheet("DrawingTypes");
                int row = 2;
                var typeId = types.Cell(row, 1).GetString();
                types.Cell(row, 18).Value = "212,5";                  // text typed the German way

                var errors = DrawingTypeExcelEngine.ValidateImport(wb, dt, packs)
                    .Where(r => r.Severity == ImportSeverity.Error).ToList();
                Assert.True(errors.Count == 0, string.Join("\n", errors.Take(10)));

                var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, dt, packs);
                var slot = imp.UpdatedDtLib.DrawingTypes.First(t => t.Id == slotDt).Slots[0];
                Assert.Equal(0.37, slot.NormX, 6);
                var t0 = imp.UpdatedDtLib.DrawingTypes.First(t => t.Id == typeId);
                Assert.Equal(212.5, t0.Crop.MarginMm, 6);
            }
            finally { CultureInfo.CurrentCulture = prior; }
        }

        [Fact]
        public void Slot_view_types_come_from_the_runtime_vocabulary()
        {
            foreach (var vt in SlotViewTypeCompatibility.KnownSlotViewTypes)
                Assert.Contains(vt, DrawingTypeExcelEngine.ViewTypeOptions);
        }

        [Fact]
        public void Print_colour_schemes_match_the_validator_set()
        {
            // DrawingTypeValidator is Revit-bound, so compare against its source.
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeValidator.cs");
            int i = src.IndexOf("PrintColourSchemes =", StringComparison.Ordinal);
            Assert.True(i > 0);
            var block = src.Substring(i, src.IndexOf("};", i, StringComparison.Ordinal) - i);
            var values = System.Text.RegularExpressions.Regex.Matches(block, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToList();
            Assert.True(values.Count >= 5);
            Assert.Equal(values.OrderBy(v => v), DrawingTypeExcelEngine.PrintColorOptions.OrderBy(v => v));
        }
    }
}
