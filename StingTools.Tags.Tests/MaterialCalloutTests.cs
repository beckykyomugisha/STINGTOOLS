using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Material callouts (SPECIALIST_TAG_BUILD_SHEET §5): one identity per material a tag can
    /// read, one callout per material instead of one per wall, paint first, build-up stacks,
    /// and a keynote table whose rows have their text in the text column.
    /// </summary>
    public class MaterialCalloutTests
    {
        private static MaterialRegistry _reg;
        private static MaterialRegistry Shipped()
        {
            if (_reg != null) return _reg;
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data"))) d = d.Parent;
            Assert.True(d != null, "StingTools/Data not found");
            string data = Path.Combine(d.FullName, "StingTools", "Data");
            _reg = MaterialRegistry.Parse(File.ReadAllText(Path.Combine(data, "BLE_MATERIALS.csv")),
                                          File.ReadAllText(Path.Combine(data, "MEP_MATERIALS.csv")));
            Assert.True(_reg.Count > 1000, $"register loaded {_reg.Count} rows");
            return _reg;
        }

        private static MaterialRow ARow() =>
            Shipped().Rows.First(r => r.Code.Length > 0 && r.Name.Length > 0 && r.Name.Length <= 60 && r.Iso19650Id.Length > 0);

        private const string Enriched = "Standard gypsum board | Category: Ceilings | Application: Internal | Durability: 25 years";

        // ── identity ──

        [Fact]
        public void A_material_STING_created_is_brought_to_one_identity()
        {
            var r = ARow();
            var row = MaterialIdentityPlanner.Plan(new MaterialIdentityInput
            {
                Name = r.Name, SharedCode = r.Code, Mark = r.Code, Description = Enriched, Keynote = r.Iso19650Id,
            }, Shipped());
            Assert.Equal(MaterialIdentityVerdict.Update, row.Verdict);
            var w = row.Writes.ToDictionary(x => x.Field, x => x.To);
            Assert.Equal(r.Code, w["Keynote"]);                 // the ISO id STING wrote is superseded
            Assert.Equal(r.Name, w["Description"]);             // the paragraph becomes the short name …
            Assert.Equal(Enriched, w["MAT_SPECIFICATIONS"]);    // … and moves, not vanishes
            Assert.False(w.ContainsKey("Mark"));                // already right
            Assert.Empty(row.Conflicts);
        }

        [Fact]
        public void A_register_material_with_no_code_gets_it_everywhere()
        {
            var r = ARow();
            var row = MaterialIdentityPlanner.Plan(new MaterialIdentityInput { Name = r.Name }, Shipped());
            var w = row.Writes.ToDictionary(x => x.Field, x => x.To);
            Assert.Equal("register", row.CodeSource);
            Assert.Equal(r.Code, w["MAT_CODE"]);
            Assert.Equal(r.Code, w["Mark"]);
            Assert.Equal(r.Code, w["Keynote"]);
            Assert.Equal(r.Name, w["Description"]);
            Assert.Equal(r.Name, w["MAT_NAME"]);   // row 2 of STING - Materials Tag
        }

        [Fact]
        public void An_existing_MAT_NAME_is_never_overwritten()
        {
            var r = ARow();
            var row = MaterialIdentityPlanner.Plan(new MaterialIdentityInput
            {
                Name = r.Name, SharedCode = r.Code, SharedName = "Our own name", Mark = r.Code, Keynote = r.Code, Description = r.Name,
            }, Shipped(), force: true);
            Assert.DoesNotContain(row.Writes, w => w.Field == "MAT_NAME");
        }

        [Fact]
        public void A_value_someone_typed_is_reported_not_replaced_unless_forced()
        {
            var r = ARow();
            var input = new MaterialIdentityInput
            {
                Name = r.Name, SharedCode = r.Code, SharedName = r.Name,
                Mark = "W-01", Description = "Office partition board", Keynote = "09 21 16",
            };
            var row = MaterialIdentityPlanner.Plan(input, Shipped());
            Assert.Equal(MaterialIdentityVerdict.InSync, row.Verdict);
            Assert.Equal(3, row.Conflicts.Count);

            var forced = MaterialIdentityPlanner.Plan(input, Shipped(), force: true);
            var w = forced.Writes.ToDictionary(x => x.Field, x => x.To);
            Assert.Equal(r.Code, w["Mark"]);
            Assert.Equal(r.Code, w["Keynote"]);
            Assert.Equal(r.Name, w["Description"]);
        }

        [Fact]
        public void No_code_and_no_register_row_writes_nothing()
        {
            var row = MaterialIdentityPlanner.Plan(new MaterialIdentityInput { Name = "Somebody's Special Render 2" }, Shipped());
            Assert.Equal(MaterialIdentityVerdict.NoCode, row.Verdict);
            Assert.Empty(row.Writes);
        }

        [Fact]
        public void A_project_code_is_kept_and_the_material_name_stands_in_for_the_description()
        {
            var row = MaterialIdentityPlanner.Plan(new MaterialIdentityInput { Name = "Site render", SharedCode = "PRJ-R01" }, Shipped());
            var w = row.Writes.ToDictionary(x => x.Field, x => x.To);
            Assert.Equal("shared MAT_CODE", row.CodeSource);
            Assert.False(w.ContainsKey("MAT_CODE"));
            Assert.Equal("PRJ-R01", w["Mark"]);
            Assert.Equal("Site render", w["Description"]);
        }

        [Theory]
        [InlineData(Enriched, true)]
        [InlineData("Category: Walls | Application: External", true)]
        [InlineData("Facing brick, red multi", false)]
        [InlineData("See spec 04 20 00 | clause 2.1", false)]
        [InlineData("", false)]
        public void STING_paragraphs_are_recognised_and_peoples_text_is_not(string d, bool sting)
            => Assert.Equal(sting, MaterialIdentityPlanner.IsStingEnrichedDescription(d));

        [Fact]
        public void The_plan_is_measured_against_the_whole_shipped_register()
        {
            // Every register row, as CreateBLEMaterials leaves it, must plan to a clean identity.
            var inputs = Shipped().Rows.Where(r => r.Code.Length > 0 && r.Name.Length > 0).Select(r => new MaterialIdentityInput
            {
                Name = r.Name, SharedCode = r.Code, Mark = r.Code, Description = Enriched, Keynote = r.Iso19650Id,
            }).ToList();
            var plan = MaterialIdentityPlanner.PlanAll(inputs, Shipped());
            Assert.All(plan, p => Assert.Empty(p.Conflicts));
            Assert.All(plan, p => Assert.Contains(p.Writes, w => w.Field == "Keynote"));
        }

        // ── thinning ──

        private static MaterialCalloutCandidate C(long mat, double u, double v, double area, bool painted = false)
            => new MaterialCalloutCandidate { Material = mat, U = u, V = v, Area = area, Painted = painted };

        [Fact]
        public void One_callout_per_material_within_the_spacing()
        {
            var cands = new[] { C(1, 0, 0, 10), C(1, 5, 0, 30), C(2, 5, 0, 5), C(1, 100, 0, 8) };
            var keep = MaterialCalloutPlan.Thin(cands, spacing: 20);
            // material 1: the larger face at u=5 wins over u=0; the far one at u=100 is kept too.
            Assert.Equal(new[] { 1, 2, 3 }, keep);
        }

        [Fact]
        public void A_callout_already_on_the_view_counts_so_a_rerun_adds_nothing()
        {
            var cands = new[] { C(1, 0, 0, 10), C(2, 0, 0, 10) };
            var keep = MaterialCalloutPlan.Thin(cands, 20, existing: new[] { C(1, 3, 3, 0) });
            Assert.Equal(new[] { 1 }, keep);
        }

        [Fact]
        public void Painted_faces_are_kept_before_bigger_unpainted_ones()
        {
            var cands = new[] { C(1, 0, 0, 100), C(1, 2, 0, 1, painted: true) };
            Assert.Equal(new[] { 1 }, MaterialCalloutPlan.Thin(cands, 20));
        }

        [Fact]
        public void Spacing_is_paper_distance_at_the_drawing_scale()
            => Assert.Equal(100 * 80 / 304.8, MaterialCalloutPlan.SpacingFeet(80, 100), 9);

        [Fact]
        public void A_painted_face_towards_the_viewer_beats_a_larger_bare_one_but_not_when_it_faces_away()
        {
            Assert.Equal(1, FaceChoice.Best(new[] { 1.0, 1.0 }, new[] { 50.0, 2.0 }, new[] { false, true }));
            Assert.Equal(0, FaceChoice.Best(new[] { 1.0, -1.0 }, new[] { 50.0, 2.0 }, new[] { false, true }));
        }

        // ── build-up stack ──

        [Fact]
        public void Build_up_heads_stack_in_one_column_in_layer_order()
        {
            // Three layers across a wall (U = 0.3, 0.1, 0.2): heads read outside-in by U.
            var heads = MaterialCalloutPlan.StackHeads(new[] { (0.3, 1.0), (0.1, 1.0), (0.2, 1.0) }, columnU: 2, topV: 5, pitch: 0.5);
            Assert.All(heads, h => Assert.Equal(2, h.U));
            Assert.Equal(5.0, heads[1].V);   // U = 0.1 first
            Assert.Equal(4.5, heads[2].V);
            Assert.Equal(4.0, heads[0].V);
        }

        // ── keynote table ──

        [Fact]
        public void Keynote_rows_put_the_text_in_the_text_column()
        {
            Assert.Equal("CLG-001\tGypsum board", KeynoteTableFormat.Row("CLG-001", "Gypsum board"));
            Assert.Equal("CLG-001\tGypsum board\tMAT", KeynoteTableFormat.Row("CLG-001", "Gypsum board", "MAT"));
            Assert.Equal("K\ta b", KeynoteTableFormat.Row("K", "a\tb"));
            Assert.Null(KeynoteTableFormat.Row(" ", "x"));
        }
    }
}
