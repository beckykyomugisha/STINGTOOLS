// MEP drawing-type gaps: routing lands on the right kind of drawing, and every
// view template a drawing type names is one ViewTemplatesCommand creates.
//
// MDP-3: routing E / PLAN resolved to elec-riser-A3-1to200, a Section, so an
// electrical "plan" request produced a riser section at 1:200 with no error.
//
// Templates: 30+ drawing types named templates nothing created ("STING - Power
// Layout", "STING - HVAC Duct", "STING - MEP Plan" ...). DrawingTypePresentation
// warned "not found" and fell back to a managed pack template on every sheet.
// ViewTemplatesCommand now reads the names from the drawing types through
// DrawingTemplateCatalogue.Plan, so the list cannot drift from the catalogue.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTemplateCatalogueTests
    {
        private static DrawingTypeLibrary Shipped() => DrawingCatalogueFixture.Shipped();

        /// <summary>
        /// First-match walk over the routing table using the matcher the
        /// dispatcher calls, as DrawingDispatcher.Resolve does. Rules gated on
        /// level / project code / design option are skipped: a plain request
        /// carries none of those, so the dispatcher would not match them either.
        /// </summary>
        private static DrawingType Route(DrawingTypeLibrary lib, string disc, string phase, string docType)
        {
            var ids = lib.DrawingTypes.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var r in lib.Routing)
            {
                if (!string.IsNullOrEmpty(r.LevelMatches) || !string.IsNullOrEmpty(r.ProjectCodeMatches)
                    || !string.IsNullOrEmpty(r.OptionMatches)) continue;
                if (!DrawingRoutingMatcher.MatchesField(r.Discipline, r.DisciplineMatches, disc)) continue;
                if (!DrawingRoutingMatcher.MatchesField(r.Phase, r.PhaseMatches, phase)) continue;
                if (!DrawingRoutingMatcher.MatchesField(r.DocType, r.DocTypeMatches, docType)) continue;
                if (ids.TryGetValue(r.DrawingTypeId ?? "", out var t)) return t;
            }
            return null;
        }

        [Theory]
        [InlineData("E", "PLAN")]
        [InlineData("M", "PLAN")]
        [InlineData("P", "PLAN")]
        [InlineData("FP", "PLAN")]
        public void A_discipline_plan_request_resolves_to_a_plan(string disc, string docType)
        {
            var t = Route(Shipped(), disc, "WIP", docType);
            Assert.NotNull(t);
            Assert.True(t.Purpose == DrawingPurpose.Plan,
                $"{disc}/{docType} resolves to '{t.Id}', a {t.Purpose} — a plan request must produce a plan.");
            Assert.Equal(disc, t.Discipline);
        }

        [Theory]
        [InlineData("E", "PLAN",                 "elec-power-A1-1to100",              "Plan")]
        [InlineData("E", "RISER",                "elec-riser-A3-1to200",              "Section")]
        [InlineData("E", "DATA_COMMS",           "elec-data-comms-A1-1to100",         "Plan")]
        [InlineData("E", "SECURITY",             "elec-security-A1-1to100",           "Plan")]
        [InlineData("E", "CONTAINMENT",          "elec-containment-A1-1to100",        "Plan")]
        [InlineData("E", "EMERGENCY_LIGHTING",   "elec-emergency-lighting-A1-1to100", "Plan")]
        [InlineData("E", "FIRE_ALARM_SCHEMATIC", "elec-fire-alarm-schematic-A1",      "Schematic")]
        [InlineData("M", "HVAC_PIPE",            "mep-hvac-pipe-A1-1to100",           "Plan")]
        [InlineData("M", "HVAC_SCHEMATIC",       "mep-hvac-schematic-A1",             "Schematic")]
        [InlineData("M", "SCHEMATIC",            "mep-hvac-schematic-A1",             "Schematic")]
        [InlineData("P", "WATER_SUPPLY",         "plumb-water-supply-A1-1to100",      "Plan")]
        [InlineData("FP", "RISER_SCHEMATIC",     "fire-riser-schematic-A1",           "Schematic")]
        [InlineData("FP", "SCHEMATIC",           "fire-riser-schematic-A1",           "Schematic")]
        [InlineData("M", "SECTION",              "mep-section-A1-1to50",              "Section")]
        [InlineData("E", "SECTION",              "mep-section-A1-1to50",              "Section")]
        [InlineData("P", "SECTION",              "mep-section-A1-1to50",              "Section")]
        [InlineData("M", "DETAIL",               "mep-detail-A3-1to20",               "Detail")]
        [InlineData("P", "DETAIL",               "mep-detail-A3-1to20",               "Detail")]
        [InlineData("FP", "SECTION",             "fire-section-A1-1to50",             "Section")]
        public void MEP_doc_types_route_to_the_intended_type(string disc, string docType, string id, string purpose)
        {
            var t = Route(Shipped(), disc, "WIP", docType);
            Assert.NotNull(t);
            Assert.Equal(id, t.Id);
            Assert.Equal(purpose, t.Purpose);
        }

        [Fact]
        public void Every_shipped_viewTemplateName_is_one_ViewTemplatesCommand_creates()
        {
            var lib = Shipped();
            var plan = DrawingTemplateCatalogue.Plan(lib.DrawingTypes);
            var creatable = new HashSet<string>(plan.Creatable.Select(s => s.Name), StringComparer.Ordinal);

            var named = lib.DrawingTypes
                .Where(t => !string.IsNullOrWhiteSpace(t.ViewTemplateName))
                .ToList();
            Assert.True(named.Count >= 80, $"only {named.Count} types name a template — binding broken?");

            var missing = new List<string>();
            foreach (var t in named)
            {
                string name = t.ViewTemplateName.Trim();
                if (creatable.Contains(name)) continue;
                if (DrawingTemplateCatalogue.IsManagedName(name)) continue;          // ManagedTemplateSyncer owns these
                if (t.Purpose == DrawingPurpose.Legend) continue;                    // Revit legends take no template
                missing.Add($"{t.Id} ({t.Purpose}) -> '{name}'");
            }
            Assert.True(missing.Count == 0,
                "Drawing types name view templates ViewTemplatesCommand will not create:\n  " + string.Join("\n  ", missing)
                + "\nNot creatable:\n  " + string.Join("\n  ", plan.NotCreatable));
        }

        [Fact]
        public void The_named_MEP_templates_are_created_from_the_right_view_kind()
        {
            var plan = DrawingTemplateCatalogue.Plan(Shipped().DrawingTypes);
            var byName = plan.Creatable.ToDictionary(s => s.Name, StringComparer.Ordinal);

            foreach (var n in new[] { "STING - HVAC Duct", "STING - Power Layout", "STING - Lighting Layout",
                                      "STING - Fire Alarm", "STING - Drainage", "STING - MEP Plan",
                                      "STING - Fire Protection Plan", "STING - HVAC Pipework", "STING - Water Supply",
                                      "STING - Data & Comms Layout", "STING - Security Layout",
                                      "STING - Containment Layout", "STING - Emergency Lighting" })
            {
                Assert.True(byName.ContainsKey(n), $"'{n}' is not in the creatable set");
                Assert.Equal(DrawingViewKind.FloorPlan, byName[n].BaseKind);
            }
            Assert.Equal(DrawingViewKind.Drafting, byName["STING - Electrical Schematic"].BaseKind);
            Assert.Equal(DrawingViewKind.Drafting, byName["STING - Mechanical Schematic"].BaseKind);
            Assert.Equal(DrawingViewKind.Drafting, byName["STING - Fire Protection Schematic"].BaseKind);
            Assert.Equal(DrawingViewKind.Section, byName["STING - MEP Section"].BaseKind);
            Assert.Equal(DrawingViewKind.Detail, byName["STING - MEP Detail"].BaseKind);

            Assert.Equal("E", byName["STING - Power Layout"].VgCode);
            Assert.Equal("M", byName["STING - HVAC Pipework"].VgCode);
            Assert.Equal("P", byName["STING - Water Supply"].VgCode);
            Assert.Equal("FP", byName["STING - Fire Protection Plan"].VgCode);
            Assert.Equal("SEC_W", byName["STING - MEP Section"].VgCode);
            Assert.Equal("SEC_D", byName["STING - MEP Detail"].VgCode);
        }

        [Fact]
        public void ViewTemplatesCommand_creates_from_the_catalogue_plan()
        {
            // The gate above is only worth something if the command walks the
            // same plan. TemplateCommands.cs is Revit-bound, so read it as text.
            var src = DrawingCatalogueFixture.Source("Temp", "TemplateCommands.cs");
            Assert.Contains("DrawingTemplateCatalogue.Plan(", src);
            Assert.Contains("DrawingTypeRegistry.ListAll(doc)", src);
            Assert.Contains("CreateDrawingTypeTemplates(doc, baseViews, filterLookup, solidFill)", src);
            foreach (var kind in DrawingTemplateCatalogue.CreatableKinds)
            {
                // Every creatable kind gets a base — the model's or a temporary one
                // (TemporaryBaseViews) — and the kind that base is made as has a case
                // in CreateTemporaryBase. A detail's base is a section.
                Assert.True(TemporaryBaseViews.Plan(kind, new List<string>(), out var why) != null,
                    $"No base can be made for creatable kind '{kind}': {why}");
                string baseKind = TemporaryBaseViews.BaseKindFor(kind);
                string member = typeof(DrawingViewKind).GetFields()
                    .First(f => (string)f.GetRawConstantValue() == baseKind).Name;
                Assert.True(src.Contains("case StingTools.Core.Drawing.DrawingViewKind." + member + ":"),
                    $"CreateTemporaryBase has no case for '{baseKind}' (base of creatable kind '{kind}') — every such template would be reported uncreatable on a model without one.");
            }
        }

        [Fact]
        public void No_template_name_is_shared_by_two_view_kinds()
        {
            // A template only applies to views of the type it was made from, so
            // a name used by a plan type and a section type would fail on one of them.
            var plan = DrawingTemplateCatalogue.Plan(Shipped().DrawingTypes);
            var conflicts = plan.NotCreatable.Where(p => p.Contains("already a")).ToList();
            Assert.True(conflicts.Count == 0, "Template names shared across view kinds:\n  " + string.Join("\n  ", conflicts));
        }

        [Fact]
        public void Plan_is_not_vacuous_and_reports_what_it_cannot_make()
        {
            var types = new List<DrawingType>
            {
                new DrawingType { Id = "a", Purpose = DrawingPurpose.Plan,      Discipline = "E", ViewTemplateName = "T1", DetailLevel = "Medium" },
                new DrawingType { Id = "b", Purpose = DrawingPurpose.Plan,      Discipline = "E", ViewTemplateName = "T1", DetailLevel = "Fine" },
                new DrawingType { Id = "c", Purpose = DrawingPurpose.Section,   Discipline = "M", ViewTemplateName = "T1" },
                new DrawingType { Id = "d", Purpose = DrawingPurpose.Legend,    Discipline = "*", ViewTemplateName = "L" },
                new DrawingType { Id = "e", Purpose = DrawingPurpose.Schematic, Discipline = "P", ViewTemplateName = "S", DetailLevel = "Fine" },
                new DrawingType { Id = "f", Purpose = DrawingPurpose.Plan,      Discipline = "M", ViewTemplateName = "STING:corp-x:FloorPlan" },
                new DrawingType { Id = "g", Purpose = DrawingPurpose.Plan,      Discipline = "M" },
            };
            var plan = DrawingTemplateCatalogue.Plan(types);

            Assert.Equal(new[] { "T1", "S" }, plan.Creatable.Select(s => s.Name).ToArray());
            var t1 = plan.Creatable[0];
            Assert.Equal(new[] { "a", "b" }, t1.UsedBy.ToArray());
            Assert.Null(t1.DetailLevel);                         // a and b disagree -> left uncontrolled
            Assert.Equal("Fine", plan.Creatable[1].DetailLevel);
            Assert.Equal(DrawingViewKind.Drafting, plan.Creatable[1].BaseKind);
            Assert.Contains(plan.NotCreatable, p => p.StartsWith("T1") && p.Contains("'c'"));
            Assert.Contains(plan.NotCreatable, p => p.StartsWith("L") && p.Contains("Legend"));
            Assert.Equal(new[] { "STING:corp-x:FloorPlan" }, plan.Managed.ToArray());
        }
    }
}
