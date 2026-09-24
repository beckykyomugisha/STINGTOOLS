using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>Shared loaders for the drawing-catalogue gates: the shipped
    /// STING_DRAWING_TYPES.json bound to the SHIPPED model, and plugin
    /// source text for Revit-bound files that cannot be compiled here.</summary>
    internal static class DrawingCatalogueFixture
    {
        internal static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repo root (StingTools/Data/STING_DRAWING_TYPES.json)");
            return dir.FullName;
        }

        internal static DrawingTypeLibrary Shipped()
        {
            var path = Path.Combine(RepoRoot(), "StingTools", "Data", "STING_DRAWING_TYPES.json");
            var lib = JsonConvert.DeserializeObject<DrawingTypeLibrary>(File.ReadAllText(path));
            Assert.NotNull(lib);
            // Non-vacuous: an empty bind would pass every gate below.
            Assert.True(lib.DrawingTypes.Count >= 50, $"Only {lib.DrawingTypes.Count} drawing types bound — binding broken?");
            Assert.True(lib.Routing.Count >= 50, $"Only {lib.Routing.Count} routing rules bound — binding broken?");
            return lib;
        }

        internal static string Source(params string[] parts)
            => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "StingTools" }.Concat(parts).ToArray()));
    }

    /// <summary>
    /// D-7 — every drawing purpose maps to a view kind explicitly
    /// (<see cref="DrawingPurposeViewKind"/>). DrawingProducer's single-view
    /// fallback used to default any unlisted purpose to a floor plan, so the
    /// eight shipped Schematic types (and every Clarification, Legend, Spool
    /// and Coordination type without productionRules) were produced as plans
    /// without a word.
    /// </summary>
    public class DrawingPurposeViewKindTests
    {
        private static DrawingTypeLibrary Shipped() => DrawingCatalogueFixture.Shipped();
        private static string Source(params string[] parts) => DrawingCatalogueFixture.Source(parts);

        // ── D-7: purpose -> view kind ───────────────────────────────────

        [Fact]
        public void All_lists_every_DrawingPurpose_constant_once()
        {
            var consts = typeof(DrawingPurpose)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(consts.Count >= 12);
            Assert.Equal(consts, DrawingPurpose.All.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }

        [Fact]
        public void Every_canonical_purpose_maps_explicitly()
        {
            var unmapped = DrawingPurpose.All.Where(p => !DrawingPurposeViewKind.TryResolve(p, out _)).ToList();
            Assert.True(unmapped.Count == 0, "Purposes with no view-kind mapping: " + string.Join(", ", unmapped));
            // …and the table maps nothing outside the canonical set.
            Assert.Equal(DrawingPurpose.All.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                         DrawingPurposeViewKind.MappedPurposes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void Every_purpose_in_the_shipped_catalogue_maps_explicitly()
        {
            var bad = Shipped().DrawingTypes
                .Where(t => !DrawingPurposeViewKind.TryResolve(t.Purpose, out _))
                .Select(t => $"{t.Id} ('{t.Purpose}')").ToList();
            Assert.True(bad.Count == 0, "Shipped drawing types whose purpose maps to no view kind: " + string.Join(", ", bad));
        }

        [Theory]
        // The five purposes the old switch sent to its FloorPlan default.
        [InlineData("Schematic",     "DraftingView")]
        [InlineData("Clarification", "DraftingView")]
        [InlineData("Legend",        "Legend")]
        [InlineData("Spool",         "ThreeD")]
        [InlineData("Coordination",  "FloorPlan")]
        // Purposes are case-insensitive on the way in.
        [InlineData("schematic",     "DraftingView")]
        [InlineData(" Plan ",        "FloorPlan")]
        public void Formerly_defaulted_purposes_resolve_to_a_deliberate_kind(string purpose, string kind)
        {
            Assert.True(DrawingPurposeViewKind.TryResolve(purpose, out var got));
            Assert.Equal(kind, got);
        }

        [Theory]
        [InlineData("Cover")]
        [InlineData("DesignReview")]
        [InlineData("")]
        [InlineData(null)]
        public void Unknown_purpose_is_reported_not_defaulted(string purpose)
        {
            Assert.False(DrawingPurposeViewKind.TryResolve(purpose, out var kind));
            Assert.Null(kind);
            var produced = DrawingPurposeViewKind.ResolveForProduction("x-type", purpose, out var problem);
            Assert.Null(produced);
            Assert.Contains("x-type", problem);
            Assert.Contains("maps to no view kind", problem);
        }

        [Fact]
        public void A_mapped_kind_the_producer_cannot_create_says_why()
        {
            foreach (var p in DrawingPurposeViewKind.MappedPurposes)
            {
                Assert.True(DrawingPurposeViewKind.TryResolve(p, out var kind));
                Assert.True(DrawingPurposeViewKind.IsProducible(kind) || DrawingPurposeViewKind.NotProducibleReason(kind) != null,
                    $"Purpose '{p}' maps to '{kind}', which is neither producible nor explained.");
            }
            // Legend specifically: no view, a loud reason, no guessed plan.
            Assert.Null(DrawingPurposeViewKind.ResolveForProduction("legend-A3", "Legend", out var problem));
            Assert.Contains("legend view", problem);
        }

        [Fact]
        public void Producible_kinds_are_cases_the_producer_actually_creates()
        {
            // DrawingProducer is Revit-bound; its CreateViewByType switch is
            // read from source so ProducibleKinds cannot claim a kind the
            // producer would reject with "unsupported".
            var src = Source("Core", "Drawing", "DrawingProducer.cs");
            int start = src.IndexOf("private static ElementId CreateViewByType(", StringComparison.Ordinal);
            Assert.True(start > 0, "CreateViewByType not found in DrawingProducer.cs");
            int end = src.IndexOf("CreateViewByType: unsupported", start, StringComparison.Ordinal);
            Assert.True(end > start, "CreateViewByType default arm not found");
            var cases = Regex.Matches(src.Substring(start, end - start), "case \"([^\"]+)\":")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            Assert.True(cases.Count >= 8, "Parsed too few cases — parser broken?");
            var missing = DrawingPurposeViewKind.ProducibleKinds.Where(k => !cases.Contains(k)).ToList();
            Assert.True(missing.Count == 0, "ProducibleKinds not handled by CreateViewByType: " + string.Join(", ", missing));
        }

        [Theory]
        // Both purpose pickers must BE the canonical set, not copies of it.
        // Iso19650Vocabulary (editor) and DrawingTypeExcelEngine (Excel
        // round-trip) are Revit-bound files, so the binding is read from
        // source. The hand-written copies lacked Schematic (the editor) and
        // Schematic + Clarification (Excel — its import rejected ten shipped
        // rows as "not a valid purpose").
        [InlineData("Core/Drawing/Iso19650Vocabulary.cs",       "DrawingPurposes")]
        [InlineData("BIMManager/DrawingTypeExcelCommands.cs",   "PurposeOptions")]
        public void Purpose_pickers_are_derived_from_the_canonical_set(string file, string field)
        {
            var src = Source(file.Split('/'));
            Assert.Matches(new Regex(@"\b" + field + @"\s*=\s*DrawingPurpose\.All\s*;"), src);
            var shippedNotCanonical = Shipped().DrawingTypes
                .Select(t => t.Purpose)
                .Where(p => !DrawingPurpose.All.Contains(p, StringComparer.OrdinalIgnoreCase))
                .Distinct().ToList();
            Assert.True(shippedNotCanonical.Count == 0,
                "Shipped purposes the pickers cannot offer: " + string.Join(", ", shippedNotCanonical));
        }

    }
}
