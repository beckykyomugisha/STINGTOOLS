// Round 8 authoring / config defects (DTW-178..193) in the Drawing Type
// editor, the Excel round-trip and the registry's project-override layer.
// The Revit-free halves are exercised directly; the Revit-bound halves are
// held by source guards where nothing else can reach them.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DrawingTypeAuthoringTests
    {
        private static DrawingRoutingRule Rule(string disc, string docType, string target,
            string option = null, string origin = null)
            => new DrawingRoutingRule { Discipline = disc, Phase = "*", DocType = docType,
                                        DrawingTypeId = target, OptionMatches = option, Origin = origin };

        // ── DTW-185 ───────────────────────────────────────────────────────

        [Fact]
        public void Signature_distinguishes_rules_that_differ_only_by_optionMatches()
        {
            var ve = Rule("A", "PLAN", "pres-plan", option: "^VE");
            var baseline = Rule("A", "PLAN", "arch-plan-A1-1to100", option: "^Main Model$");
            Assert.NotEqual(DrawingRoutingMatcher.Signature(ve), DrawingRoutingMatcher.Signature(baseline));
        }

        [Fact]
        public void Signature_covers_every_predicate_the_matcher_reads()
        {
            // Each predicate property, set alone, must move the signature. A new
            // predicate added to DrawingRoutingRule without joining the signature
            // fails here instead of collapsing rules silently at load.
            var predicates = typeof(DrawingRoutingRule).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.CanWrite
                            && p.Name != nameof(DrawingRoutingRule.DrawingTypeId)
                            && p.Name != nameof(DrawingRoutingRule.Origin))
                .ToList();
            Assert.True(predicates.Count >= 9, "predicate reflection found " + predicates.Count);
            var plain = DrawingRoutingMatcher.Signature(Rule("A", "PLAN", "x"));
            foreach (var p in predicates)
            {
                var r = Rule("A", "PLAN", "x");
                p.SetValue(r, "^Z9$");
                Assert.True(DrawingRoutingMatcher.Signature(r) != plain, p.Name + " is not part of the signature");
            }
        }

        [Fact]
        public void Registry_merge_uses_the_shared_signature()
        {
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeRegistry.cs");
            Assert.Contains("DrawingRoutingMatcher.", src);
            Assert.DoesNotContain("rule.ProjectCodeMatches ?? \"\");", src);
        }

        // ── DTW-191 ───────────────────────────────────────────────────────

        [Fact]
        public void A_frozen_copy_of_the_whole_corporate_table_loads_as_corporate()
        {
            var corp = DrawingCatalogueFixture.Shipped().Routing;
            foreach (var r in corp) r.Origin = "corporate";
            // An override written before routing had an origin: every corporate
            // rule copied in, stamped "project" by the loader.
            var frozen = corp.Select(r => new DrawingRoutingRule {
                Discipline = r.Discipline, Phase = r.Phase, DocType = r.DocType,
                DisciplineMatches = r.DisciplineMatches, PhaseMatches = r.PhaseMatches,
                DocTypeMatches = r.DocTypeMatches, LevelMatches = r.LevelMatches,
                ProjectCodeMatches = r.ProjectCodeMatches, OptionMatches = r.OptionMatches,
                DrawingTypeId = r.DrawingTypeId, Origin = "project" }).ToList();
            var mine = Rule("M", "PLAN", "my-mep-plan", origin: "project");
            frozen.Insert(0, mine);

            var merged = DrawingRoutingMatcher.MergeRouting(corp, frozen, out int stale);

            Assert.Equal(corp.Count, stale);
            Assert.Single(merged.Where(r => r.IsProjectRule));
            Assert.Same(mine, merged[0]);
            // The corporate table survives intact behind the project rule, minus
            // only rules the project rule shadows by signature.
            Assert.True(merged.Count >= corp.Count, $"{merged.Count} < {corp.Count}");
        }

        [Fact]
        public void A_project_rule_with_a_different_target_still_wins()
        {
            var corp = new List<DrawingRoutingRule> { Rule("A", "PLAN", "arch-plan", origin: "corporate") };
            var proj = new List<DrawingRoutingRule> { Rule("A", "PLAN", "my-plan", origin: "project") };
            var merged = DrawingRoutingMatcher.MergeRouting(corp, proj, out int stale);
            Assert.Equal(0, stale);
            Assert.Single(merged);
            Assert.Equal("my-plan", merged[0].DrawingTypeId);
        }

        // ── DTW-184 ───────────────────────────────────────────────────────

        [Fact]
        public void A_file_written_after_the_ES_migration_wins()
        {
            var migrated = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
            Assert.Equal(DrawingOverrideOrigin.File,
                DrawingOverrideSource.Choose(true, migrated.Ticks, true, migrated.AddMinutes(5)));
        }

        [Fact]
        public void The_ES_copy_wins_over_the_file_it_was_migrated_from()
        {
            var migrated = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
            Assert.Equal(DrawingOverrideOrigin.ExtensibleStorage,
                DrawingOverrideSource.Choose(true, migrated.Ticks, true, migrated.AddDays(-3)));
            Assert.Equal(DrawingOverrideOrigin.ExtensibleStorage,
                DrawingOverrideSource.Choose(true, migrated.Ticks, false, DateTime.MinValue));
            Assert.Equal(DrawingOverrideOrigin.ExtensibleStorage,
                DrawingOverrideSource.Choose(true, 0, true, migrated));
        }

        [Fact]
        public void No_ES_entry_falls_back_to_the_file_or_nothing()
        {
            Assert.Equal(DrawingOverrideOrigin.File, DrawingOverrideSource.Choose(false, 0, true, DateTime.UtcNow));
            Assert.Equal(DrawingOverrideOrigin.None, DrawingOverrideSource.Choose(false, 0, false, DateTime.MinValue));
        }

        [Fact]
        public void Registry_consults_the_chooser_rather_than_reading_ES_unconditionally()
        {
            var src = DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeRegistry.cs");
            Assert.Contains("DrawingOverrideSource.Choose(", src);
        }

        // ── DTW-187 ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("UI/DrawingTypeEditorDialog.cs")]
        [InlineData("BIMManager/DrawingTypeExcelCommands.cs")]
        [InlineData("Core/Drawing/ProductionPresetRegistry.cs")]
        [InlineData("Core/Drawing/ScopeBoxPlannerService.cs")]
        public void Override_writers_are_atomic(string relPath)
        {
            var src = DrawingCatalogueFixture.Source(relPath.Split('/'));
            Assert.DoesNotContain("File.WriteAllText(", src);
            Assert.Contains("WriteAllTextAtomic(", src);
        }

        [Fact]
        public void Writers_refuse_while_the_override_failed_to_load()
        {
            Assert.Contains("public static string ProjectOverrideLoadError(",
                DrawingCatalogueFixture.Source("Core", "Drawing", "DrawingTypeRegistry.cs"));
            Assert.Contains("DrawingTypeRegistry.ProjectOverrideLoadError(_doc)",
                DrawingCatalogueFixture.Source("UI", "DrawingTypeEditorDialog.cs"));
            Assert.Contains("DrawingTypeRegistry.ProjectOverrideLoadError(doc)",
                DrawingCatalogueFixture.Source("BIMManager", "DrawingTypeExcelCommands.cs"));
        }

        // ── DTW-178 ───────────────────────────────────────────────────────

        private static string EditorMethod(string signature)
        {
            var src = DrawingCatalogueFixture.Source("UI", "DrawingTypeEditorDialog.cs");
            int i = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(i >= 0, signature + " not found");
            int j = src.IndexOf("\n        private ", i + signature.Length, StringComparison.Ordinal);
            return src.Substring(i, (j < 0 ? src.Length : j) - i);
        }

        [Fact]
        public void Editor_pack_load_layers_the_project_override_over_corporate()
        {
            var body = EditorMethod("private List<ViewStylePack> LoadViewStylePacks()");
            Assert.Contains("STING_VIEW_STYLE_PACKS.json", body);
            Assert.Contains("\"view_style_packs.json\"", body);
            Assert.Contains("p.Origin = \"project\"", body);
            // The snapshot is taken over the merged list, not the corporate one.
            Assert.True(body.IndexOf("view_style_packs.json\"", StringComparison.Ordinal)
                        < body.IndexOf("_packSnapshot = new", StringComparison.Ordinal));
        }

        [Fact]
        public void Editor_pack_save_refuses_when_the_project_packs_did_not_load()
        {
            var body = EditorMethod("private int SaveStylePacksToProjectOverride(");
            Assert.Contains("_packOverrideError != null", body);
        }

        // ── DTW-193 ───────────────────────────────────────────────────────

        [Fact]
        public void Every_editor_promotion_to_project_clears_the_corporate_checksum()
        {
            var src = DrawingCatalogueFixture.Source("UI", "DrawingTypeEditorDialog.cs");
            int from = 0, promotions = 0;
            while ((from = src.IndexOf("t.Origin = \"project\";", from, StringComparison.Ordinal)) >= 0)
            {
                promotions++;
                var window = src.Substring(from, Math.Min(400, src.Length - from));
                Assert.Contains(".Checksum = null;", window);
                from += 10;
            }
            Assert.True(promotions >= 2, "found " + promotions + " promotions");
        }

        // ── DTW-190 ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("Core/StingToolsApp.cs")]
        [InlineData("Commands/Drawing/DrawingTypesInspectCommand.cs")]
        public void Reload_points_drop_every_drawing_registry(string relPath)
        {
            var src = DrawingCatalogueFixture.Source(relPath.Split('/'));
            Assert.Contains("DrawingTypeRegistry.Reload(", src);
            Assert.Contains("ViewStylePackRegistry.Reload(", src);
            Assert.Contains("MatchLineConfigRegistry.Reload(", src);
        }

        // ── DTW-186 ───────────────────────────────────────────────────────

        [Fact]
        public void Repeated_clones_get_distinct_ids()
        {
            var ids = new List<string> { "arch-plan" };
            var a = CatalogueIds.UniqueCopyId("arch-plan", ids); ids.Add(a);
            var b = CatalogueIds.UniqueCopyId("arch-plan", ids); ids.Add(b);
            var c = CatalogueIds.UniqueCopyId("arch-plan", ids);
            Assert.Equal("arch-plan-copy", a);
            Assert.Equal("arch-plan-copy-2", b);
            Assert.Equal("arch-plan-copy-3", c);
        }

        [Fact]
        public void Blank_and_duplicate_ids_are_reported()
        {
            var p = CatalogueIds.IdProblems("Drawing type", new[] { "a", "", "A", "b", null });
            Assert.Equal(2, p.Count);
            Assert.Contains(p, x => x.Contains("no id"));
            Assert.Contains(p, x => x.Contains("'a'"));
            Assert.Empty(CatalogueIds.IdProblems("Drawing type", new[] { "a", "b" }));
        }

        [Fact]
        public void Editor_snapshots_by_object_not_by_editable_id()
        {
            var src = DrawingCatalogueFixture.Source("UI", "DrawingTypeEditorDialog.cs");
            Assert.DoesNotContain("_typeSnapshot.TryGetValue(t.Id", src);
            Assert.DoesNotContain("_packSnapshot.TryGetValue(p.Id", src);
            Assert.Contains("CatalogueIds.IdProblems(", EditorMethod("private bool SaveToProjectOverride()"));
        }
    }
}
