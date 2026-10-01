// DTW-163 — a managed style pack's template must CONTROL the V/G it carries.
//
// ManagedTemplateSyncer used to map only scalar fields to template parameters;
// vgOverrides / filters mapped to nothing, so SetNonControlledTemplateParameterIds
// released V/G and every view under a managed template showed its own graphics
// — none of the pack's overrides or filters. These tests pin the field → BIP map
// against the real BuiltInParameter names, and pin that every shipped managed
// pack that carries overrides / filters ends up controlling them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ManagedTemplateFieldsTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        private static HashSet<string> BuiltInParameters()
        {
            var path = Path.Combine(RepoRoot(), "StingTools.Tags.Tests", "Fixtures", "BuiltInParameterNames.txt");
            return new HashSet<string>(File.ReadAllLines(path)
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")), StringComparer.Ordinal);
        }

        [Fact]
        public void EveryMappedNameIsARealBuiltInParameter()
        {
            var bips = BuiltInParameters();
            var bad = ManagedTemplateFields.FieldBipNames
                .SelectMany(kv => kv.Value.Select(n => (kv.Key, n)))
                .Where(x => !bips.Contains(x.n))
                .Select(x => $"{x.Key} -> {x.n}").ToList();
            Assert.True(bad.Count == 0, "Not BuiltInParameter members: " + string.Join(", ", bad));
        }

        [Theory]
        [InlineData("vgOverrides", "VIS_GRAPHICS_MODEL")]
        [InlineData("vgOverrides", "VIS_GRAPHICS_ANNOTATION")]
        [InlineData("filters", "VIS_GRAPHICS_FILTERS")]
        [InlineData("worksetVisibility", "VIS_GRAPHICS_WORKSETS")]
        [InlineData("linkOverrides", "VIS_GRAPHICS_RVT_LINKS")]
        [InlineData("viewRange", "PLAN_VIEW_RANGE")]
        public void VisibilityGraphicsFieldsMapToTheirTemplateParameter(string field, string bip)
            => Assert.Contains(bip, ManagedTemplateFields.BipNamesFor(new[] { field }));

        [Fact]
        public void APayloadIsControlledEvenWhenTheFieldIsNotListed()
        {
            var eff = ManagedTemplateFields.Effective(new[] { "detailLevel" },
                hasVgOverrides: true, hasFilters: true, hasWorksetVisibility: false, hasViewRange: false);
            Assert.Contains("vgOverrides", eff);
            Assert.Contains("filters", eff);
            Assert.DoesNotContain("worksetVisibility", eff);
        }

        [Fact]
        public void NoPayloadAndNoListingControlsNoVisibilityGraphics()
        {
            var eff = ManagedTemplateFields.Effective(new[] { "detailLevel", "discipline" },
                false, false, false, false);
            Assert.DoesNotContain(ManagedTemplateFields.BipNamesFor(eff), n => n.StartsWith("VIS_GRAPHICS_"));
        }

        // DTW-170: scale belongs to DrawingType.Scale; a template controlling
        // VIEW_SCALE pins every assigned view to the seed's scale.
        [Fact]
        public void ScaleIsNeverControlledAndIsReportedWhenListed()
        {
            var refused = new List<string>();
            var eff = ManagedTemplateFields.Effective(new[] { "scale", "detailLevel" },
                false, false, false, false, refused.Add);
            Assert.DoesNotContain("scale", eff);
            Assert.Equal(new[] { "scale" }, refused);
            Assert.DoesNotContain("VIEW_SCALE", ManagedTemplateFields.BipNamesFor(new[] { "scale" }));
        }

        [Fact]
        public void NoShippedPackListsScaleAsAManagedField()
        {
            var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(
                File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            var listing = lib.Packs.Where(p => p.ManagedFields != null && p.ManagedFields.Contains("scale"))
                .Select(p => p.Id).ToList();
            Assert.True(listing.Count == 0, "Packs listing 'scale' as managed: " + string.Join(", ", listing));
        }

        [Fact]
        public void EveryShippedManagedPackControlsTheGraphicsItCarries()
        {
            var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(
                File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            var managed = lib.Packs.Where(p => p.IsManaged).ToList();
            Assert.True(managed.Count >= 10, "expected the shipped managed packs");

            var failures = new List<string>();
            foreach (var p in managed)
            {
                bool hasVg = p.VgOverrides != null && p.VgOverrides.Count > 0;
                bool hasFilters = p.Filters != null && p.Filters.Count > 0;
                var bips = ManagedTemplateFields.BipNamesFor(ManagedTemplateFields.Effective(
                    p.ManagedFields ?? new List<string>(), hasVg, hasFilters,
                    !string.IsNullOrWhiteSpace(p.WorksetVisibility), p.ViewRange != null));
                if (hasVg && !bips.Contains("VIS_GRAPHICS_MODEL")) failures.Add(p.Id + " vgOverrides");
                if (hasFilters && !bips.Contains("VIS_GRAPHICS_FILTERS")) failures.Add(p.Id + " filters");
            }
            Assert.True(failures.Count == 0, "Managed packs whose template would release V/G: " + string.Join(", ", failures));
        }
    }
}
