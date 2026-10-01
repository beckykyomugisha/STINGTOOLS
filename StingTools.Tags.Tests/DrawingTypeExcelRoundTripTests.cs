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

        // ── DTW-181 ───────────────────────────────────────────────────────

        [Fact]
        public void An_unedited_round_trip_records_no_change_and_writes_an_empty_override()
        {
            var dt = ShippedTypes();
            var packs = ShippedPacks();
            using var wb = Export(dt, packs);
            var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, dt, packs);

            Assert.True(imp.Changes.Count == 0,
                imp.Changes.Count + " change(s) from an unedited workbook:\n" + string.Join("\n", imp.Changes.Take(25)));
            var (types, packDoc) = DrawingTypeExcelEngine.BuildProjectOverride(imp.UpdatedDtLib, imp.UpdatedPackLib);
            Assert.Empty(types.DrawingTypes);
            Assert.Empty(types.Routing);
            Assert.Empty(packDoc.StylePacks);
            Assert.All(imp.UpdatedDtLib.DrawingTypes, t => Assert.False(string.IsNullOrEmpty(t.Checksum), t.Id + " lost its checksum"));
        }

        [Fact]
        public void Pack_json_survives_the_import_model_byte_for_byte()
        {
            // Every key a shipped pack carries — the 'filters' alias, long-form
            // VG keys, filter-rule surfFgColor / projLinePattern, tagFamilies,
            // viewRange, farClipMm … — must come back out of the import model.
            var path = Path.Combine(DrawingCatalogueFixture.RepoRoot(), "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json");
            var raw = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
            var dt = ShippedTypes();
            var packs = ShippedPacks();
            using var wb = Export(dt, packs);
            var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, dt, packs);
            var byId = imp.UpdatedPackLib.StylePacks.ToDictionary(p => p.Id);
            var lost = new List<string>();
            foreach (Newtonsoft.Json.Linq.JObject src in raw["stylePacks"])
            {
                var id = (string)src["id"];
                var back = Newtonsoft.Json.Linq.JObject.FromObject(byId[id],
                    JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
                var a = Normalise(src); var b = Normalise(back);
                if (!Newtonsoft.Json.Linq.JToken.DeepEquals(a, b))
                    lost.Add(id + ": first difference at " + FirstDifference(a, b));
            }
            Assert.True(lost.Count == 0, lost.Count + " pack(s) changed shape:\n" + string.Join("\n", lost.Take(4)));
        }

        [Fact]
        public void Editing_one_VG_row_keeps_its_spelling_and_the_rest_of_the_pack()
        {
            var dt = ShippedTypes();
            var packs = ShippedPacks();
            var pack = packs.StylePacks.First(p => p.VgOverrides != null
                && p.VgOverrides.Values.Any(v => !string.IsNullOrEmpty(v.ProjectionLineColor)));
            var cat = pack.VgOverrides.First(kv => !string.IsNullOrEmpty(kv.Value.ProjectionLineColor)).Key;
            var fPack = packs.StylePacks.First(p => p.FilterRules != null
                && p.FilterRules.Any(f => f.Extra != null && f.Extra.ContainsKey("surfFgColor")));
            var keptFilter = fPack.FilterRules.First(f => f.Extra != null && f.Extra.ContainsKey("surfFgColor"));
            using var wb = Export(dt, packs);
            var ws = wb.Worksheet("VgOverrides");
            var row = ws.RowsUsed().First(r => r.Cell(1).GetString() == pack.Id && r.Cell(2).GetString() == cat);
            row.Cell(3).Value = "#123456";
            var fws = wb.Worksheet("FilterRules");
            var frow = fws.RowsUsed().First(r => r.Cell(1).GetString() == fPack.Id && r.Cell(2).GetString() == keptFilter.Name);
            frow.Cell(9).Value = 42;

            var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, dt, packs);
            var after = imp.UpdatedPackLib.StylePacks.First(p => p.Id == pack.Id);
            Assert.Equal("#123456", after.VgOverrides[cat].ProjectionLineColor);
            Assert.Null(after.VgOverrides[cat].ProjColor);
            Assert.Equal("project", after.Origin);
            var fAfter = imp.UpdatedPackLib.StylePacks.First(p => p.Id == fPack.Id);
            var f = fAfter.FilterRules.First(x => x.Name == keptFilter.Name);
            Assert.Equal(42, f.Transparency);
            Assert.True(f.Extra.ContainsKey("surfFgColor"), "surfFgColor dropped by an edit to the same rule");
            var (_, packDoc) = DrawingTypeExcelEngine.BuildProjectOverride(imp.UpdatedDtLib, imp.UpdatedPackLib);
            var written = packDoc.StylePacks.Select(p => p.Id).OrderBy(x => x).ToList();
            Assert.Equal(new[] { pack.Id, fPack.Id }.Distinct().OrderBy(x => x), written);
        }

        private static string FirstDifference(Newtonsoft.Json.Linq.JToken a, Newtonsoft.Json.Linq.JToken b, string path = "$")
        {
            if (a is Newtonsoft.Json.Linq.JObject oa && b is Newtonsoft.Json.Linq.JObject ob)
            {
                foreach (var name in oa.Properties().Select(p => p.Name).Union(ob.Properties().Select(p => p.Name)))
                {
                    var va = oa[name]; var vb = ob[name];
                    if (va == null || vb == null) return $"{path}.{name}: {(va == null ? "missing in input" : "missing in output")}";
                    if (!Newtonsoft.Json.Linq.JToken.DeepEquals(va, vb)) return FirstDifference(va, vb, path + "." + name);
                }
            }
            if (a is Newtonsoft.Json.Linq.JArray aa && b is Newtonsoft.Json.Linq.JArray ab)
            {
                if (aa.Count != ab.Count) return $"{path}: {aa.Count} vs {ab.Count} items";
                for (int i = 0; i < aa.Count; i++)
                    if (!Newtonsoft.Json.Linq.JToken.DeepEquals(aa[i], ab[i])) return FirstDifference(aa[i], ab[i], $"{path}[{i}]");
            }
            return $"{path}: {a.ToString(Formatting.None)} vs {b.ToString(Formatting.None)}";
        }

        // Key order is irrelevant; the canonical "filterRules" and its alias
        // "filters" are the same field to the runtime.
        private static Newtonsoft.Json.Linq.JToken Normalise(Newtonsoft.Json.Linq.JToken t)
        {
            if (t is Newtonsoft.Json.Linq.JObject o)
            {
                var n = new Newtonsoft.Json.Linq.JObject();
                // An explicit JSON null is the same as an absent key to the runtime.
                foreach (var p in o.Properties().Where(p => p.Value.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                                   .OrderBy(p => p.Name == "filters" ? "filterRules" : p.Name, StringComparer.Ordinal))
                    n[p.Name == "filters" ? "filterRules" : p.Name] = Normalise(p.Value);
                return n;
            }
            if (t is Newtonsoft.Json.Linq.JArray a) return new Newtonsoft.Json.Linq.JArray(a.Select(Normalise));
            return t.DeepClone();
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
