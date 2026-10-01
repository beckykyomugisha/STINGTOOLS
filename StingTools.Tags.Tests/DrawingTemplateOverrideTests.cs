using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-62 — mixed-kind drawing types. A type names one view template, which
    /// fits one kind of view; a production rule may name the template for its
    /// own view (viewTemplateOverride), and Apply skips a template of the wrong
    /// kind instead of throwing.
    /// </summary>
    public class DrawingTemplateOverrideTests
    {
        private static DrawingTypeLibrary Shipped() => DrawingCatalogueFixture.Shipped();

        [Fact]
        public void Every_type_template_fits_at_least_one_view_the_type_produces()
        {
            // clar-markup-A1 named a Clarification (drafting) template but only
            // ever made a floor plan, so its template could never apply.
            var problems = new List<string>();
            int checkedTypes = 0;
            foreach (var t in Shipped().DrawingTypes)
            {
                if (t.ProductionRules == null || t.ProductionRules.Count == 0) continue;
                if (string.IsNullOrWhiteSpace(t.ViewTemplateName) || DrawingTemplateCatalogue.IsManagedName(t.ViewTemplateName)) continue;
                if (!DrawingPurposeViewKind.TryResolve(t.Purpose, out var kind)) continue;
                checkedTypes++;
                var made = t.ProductionRules.Select(r => DrawingTemplateCatalogue.ViewKindOf(r.ViewType)).ToList();
                if (!made.Contains(kind, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{t.Id}: '{t.ViewTemplateName}' is a {kind} template but the type makes {string.Join(", ", made)}");
            }
            Assert.True(checkedTypes >= 4, $"Only {checkedTypes} mixed types checked — binding broken?");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void Every_rule_override_is_created_from_the_rule_view_kind()
        {
            var lib = Shipped();
            var plan = DrawingTemplateCatalogue.Plan(lib.DrawingTypes);
            var byName = plan.Creatable.ToDictionary(s => s.Name, StringComparer.Ordinal);
            int overrides = 0;
            foreach (var t in lib.DrawingTypes)
                foreach (var r in t.ProductionRules ?? new List<ProductionRule>())
                {
                    if (string.IsNullOrWhiteSpace(r.ViewTemplateOverride)) continue;
                    overrides++;
                    Assert.True(byName.TryGetValue(r.ViewTemplateOverride, out var spec),
                        $"{t.Id} rule {r.Idx}: '{r.ViewTemplateOverride}' is not created. Not creatable:\n  " + string.Join("\n  ", plan.NotCreatable));
                    Assert.Equal(DrawingTemplateCatalogue.ViewKindOf(r.ViewType), spec.BaseKind);
                }
            Assert.True(overrides >= 1, "clar-markup-A1 should name its plan template on its production rule");
            Assert.Equal(DrawingViewKind.FloorPlan, byName["STING - Clarification Markup"].BaseKind);
        }

        [Fact]
        public void Plan_takes_an_override_kind_from_the_rule_and_reports_a_clash()
        {
            var types = new List<DrawingType>
            {
                new DrawingType { Id = "a", Purpose = DrawingPurpose.Spool, ViewTemplateName = "T3D",
                    ProductionRules = new List<ProductionRule>
                    {
                        new ProductionRule { Idx = 0, ViewType = "FloorPlan", ViewTemplateOverride = "TPlan" },
                        new ProductionRule { Idx = 1, ViewType = "ThreeD" },
                        new ProductionRule { Idx = 2, ViewType = "Section", ViewTemplateOverride = "T3D" },
                        new ProductionRule { Idx = 3, ViewType = "Schedule", ViewTemplateOverride = "STING:corp-x:Schedule" },
                    } },
            };
            var plan = DrawingTemplateCatalogue.Plan(types);
            var byName = plan.Creatable.ToDictionary(s => s.Name);
            Assert.Equal(DrawingViewKind.ThreeD, byName["T3D"].BaseKind);
            Assert.Equal(DrawingViewKind.FloorPlan, byName["TPlan"].BaseKind);
            Assert.Contains(plan.NotCreatable, p => p.StartsWith("T3D") && p.Contains("rule 2"));
            Assert.Contains("STING:corp-x:Schedule", plan.Managed);
        }

        [Theory]
        [InlineData("FloorPlan", DrawingViewKind.FloorPlan)]
        [InlineData("CeilingPlan", DrawingViewKind.Rcp)]
        [InlineData("ThreeD", DrawingViewKind.ThreeD)]
        [InlineData("Section", DrawingViewKind.Section)]
        [InlineData("Elevation", DrawingViewKind.Elevation)]
        [InlineData("DraftingView", DrawingViewKind.Drafting)]
        [InlineData("Schedule", DrawingViewKind.Schedule)]
        [InlineData("SomethingElse", null)]
        public void ViewKindOf_names_the_Revit_view_type(string viewType, string kind)
            => Assert.Equal(kind, DrawingTemplateCatalogue.ViewKindOf(viewType));

        [Fact]
        public void Apply_checks_the_template_fits_the_view_before_assigning_it()
        {
            // Revit-bound, so read as text: the template must be tested with
            // IsValidViewTemplate, and the name chosen per view.
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypePresentation.cs");
            Assert.Contains("view.IsValidViewTemplate(tplId)", src);
            Assert.Contains("ExplicitTemplateNameFor(dt, view)", src);
        }
    }
}
