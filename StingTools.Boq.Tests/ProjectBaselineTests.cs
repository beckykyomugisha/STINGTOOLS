using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// The STING model-authoring baseline.
    ///
    /// This decides what gets written into somebody's live model, so the audit
    /// has to be believable without Revit: what it proposes, what it refuses to
    /// touch, and what it admits it cannot do.
    /// </summary>
    public class BaselineAuditTests
    {
        private static ProjectBaseline Baseline()
        {
            var b = new ProjectBaseline();
            b.Materials.Add(new BaselineMaterial { Name = "Ceramic Tile" });
            b.Materials.Add(new BaselineMaterial { Name = "Concrete C25" });
            b.Materials.Add(new BaselineMaterial { Name = "Cement Screed" });
            b.FloorTypes.Add(new BaselineHostType
            {
                Name = "STING RC Slab 150 - Ceramic Tiled",
                Layers =
                {
                    new BaselineLayer { Function = "Finish1", Material = "Ceramic Tile", ThicknessMm = 10 },
                    new BaselineLayer { Function = "Substrate", Material = "Cement Screed", ThicknessMm = 40 },
                    new BaselineLayer { Function = "Structure", Material = "Concrete C25", ThicknessMm = 150 }
                }
            });
            return b;
        }

        [Fact]
        public void An_Empty_Model_Reports_Everything_As_Creatable()
        {
            var r = BaselineAuditor.Audit(Baseline(), new ModelInventory());

            Assert.Equal(4, r.MissingCount);          // 3 materials + 1 floor type
            Assert.False(r.NothingToMint);
            Assert.Contains("WILL CREATE", BaselineAuditor.Report(r));
        }

        [Fact]
        public void A_Conforming_Model_Has_Nothing_To_Do()
        {
            var inv = new ModelInventory();
            foreach (string m in new[] { "Ceramic Tile", "Concrete C25", "Cement Screed" }) inv.Materials.Add(m);
            inv.FloorTypes.Add("STING RC Slab 150 - Ceramic Tiled");
            inv.HostTypeHasTiledFinish["STING RC Slab 150 - Ceramic Tiled"] = true;

            var r = BaselineAuditor.Audit(Baseline(), inv);

            Assert.True(r.NothingToMint);
            Assert.Equal(0, r.ConflictCount);
            Assert.Contains("Nothing to create", BaselineAuditor.Report(r));
        }

        [Fact]
        public void A_Type_That_Exists_But_Lost_Its_Tiled_Layer_Is_A_Conflict_Not_A_Pass()
        {
            // The exact situation a real export hit: the type is there, so the
            // name check passes, but it carries no tiled finish and nothing
            // tiled can be measured from it.
            var inv = new ModelInventory();
            foreach (string m in new[] { "Ceramic Tile", "Concrete C25", "Cement Screed" }) inv.Materials.Add(m);
            inv.FloorTypes.Add("STING RC Slab 150 - Ceramic Tiled");
            inv.HostTypeHasTiledFinish["STING RC Slab 150 - Ceramic Tiled"] = false;

            var r = BaselineAuditor.Audit(Baseline(), inv);

            Assert.Equal(1, r.ConflictCount);
            Assert.Equal(0, r.MissingCount);          // never proposed for overwrite
            Assert.Contains("WILL NOT TOUCH", BaselineAuditor.Report(r));
        }

        [Fact]
        public void A_Conflict_Is_Never_Offered_As_Something_To_Create()
        {
            var inv = new ModelInventory();
            inv.FloorTypes.Add("STING RC Slab 150 - Ceramic Tiled");
            inv.HostTypeHasTiledFinish["STING RC Slab 150 - Ceramic Tiled"] = false;

            var r = BaselineAuditor.Audit(Baseline(), inv);

            Assert.DoesNotContain(r.Missing, f => f.Group == "Floor types");
        }

        [Fact]
        public void Family_Backed_Types_Are_Guidance_Never_Missing()
        {
            // Nothing here can be created without an .rfa loaded. Listing it as
            // actionable would promise work Apply cannot do.
            var b = new ProjectBaseline();
            b.FamilyExpectations.Add(new BaselineFamilyExpectation
            {
                Category = "Doors", MinimumTypes = 1, Guidance = "Load a door family."
            });

            var r = BaselineAuditor.Audit(b, new ModelInventory());

            Assert.Equal(0, r.MissingCount);
            Assert.Equal(1, r.GuidanceCount);
            Assert.Contains("CANNOT CREATE", BaselineAuditor.Report(r));
        }

        [Fact]
        public void A_Family_Category_With_Enough_Conforming_Types_Passes()
        {
            var b = new ProjectBaseline();
            b.FamilyExpectations.Add(new BaselineFamilyExpectation
            {
                Category = "Structural Columns", NamePatterns = { "Concrete" }, MinimumTypes = 1
            });
            var inv = new ModelInventory();
            inv.FamilyTypesByCategory["Structural Columns"] = new List<string> { "Concrete-Rectangular 300x300" };

            var r = BaselineAuditor.Audit(b, inv);

            Assert.Equal(0, r.GuidanceCount);
            Assert.Equal(1, r.PresentCount);
        }

        [Fact]
        public void Types_That_Do_Not_Match_The_Naming_Pattern_Do_Not_Count()
        {
            var b = new ProjectBaseline();
            b.FamilyExpectations.Add(new BaselineFamilyExpectation
            {
                Category = "Structural Columns", NamePatterns = { "Concrete" }, MinimumTypes = 1,
                Guidance = "Load a concrete column family."
            });
            var inv = new ModelInventory();
            inv.FamilyTypesByCategory["Structural Columns"] = new List<string> { "UC 254x254x73" };

            var r = BaselineAuditor.Audit(b, inv);

            Assert.Equal(1, r.GuidanceCount);
        }
    }

    public class BaselineValidationTests
    {
        [Fact]
        public void A_Layer_Naming_An_Undeclared_Material_Is_Caught()
        {
            // Otherwise the type mints with an empty layer and no error — the
            // same silent-zero class as a mistyped MATERIAL_LOOKUP key.
            var b = new ProjectBaseline();
            b.Materials.Add(new BaselineMaterial { Name = "Concrete C25" });
            b.WallTypes.Add(new BaselineHostType
            {
                Name = "W1",
                Layers = { new BaselineLayer { Function = "Structure", Material = "Concrete C30", ThicknessMm = 200 } }
            });

            Assert.Contains(b.Validate(), p => p.Contains("Concrete C30"));
        }

        [Fact]
        public void Zero_Thickness_And_Empty_Types_Are_Caught()
        {
            var b = new ProjectBaseline();
            b.Materials.Add(new BaselineMaterial { Name = "Concrete C25" });
            b.WallTypes.Add(new BaselineHostType
            {
                Name = "W1",
                Layers = { new BaselineLayer { Function = "Structure", Material = "Concrete C25", ThicknessMm = 0 } }
            });
            b.FloorTypes.Add(new BaselineHostType { Name = "F1" });

            var problems = b.Validate();

            Assert.Contains(problems, p => p.Contains("thickness"));
            Assert.Contains(problems, p => p.Contains("declares no layers"));
        }

        [Fact]
        public void A_Duplicated_Type_Name_Is_Caught()
        {
            var b = new ProjectBaseline();
            b.Materials.Add(new BaselineMaterial { Name = "Concrete C25" });
            for (int i = 0; i < 2; i++)
                b.WallTypes.Add(new BaselineHostType
                {
                    Name = "W1",
                    Layers = { new BaselineLayer { Function = "Structure", Material = "Concrete C25", ThicknessMm = 200 } }
                });

            Assert.Contains(b.Validate(), p => p.Contains("more than once"));
        }

        [Fact]
        public void A_Broken_Baseline_Is_Reported_Before_Any_Model_Comparison()
        {
            var b = new ProjectBaseline();
            b.WallTypes.Add(new BaselineHostType
            {
                Name = "W1",
                Layers = { new BaselineLayer { Function = "Structure", Material = "Nope", ThicknessMm = 200 } }
            });

            var r = BaselineAuditor.Audit(b, new ModelInventory());

            Assert.NotEmpty(r.BaselineProblems);
            Assert.Contains("THE BASELINE ITSELF HAS PROBLEMS", BaselineAuditor.Report(r));
        }
    }

    /// <summary>
    /// The shipped baseline against the classifiers that consume it. Material
    /// NAMES are load-bearing here: the take-off infers brick / block / RC from
    /// them and the finish classifier recognises tiling from them, so a rename
    /// that looks cosmetic can silence a whole section of the schedule.
    /// </summary>
    public class ShippedBaselineTests
    {
        private static ProjectBaseline Shipped() =>
            JsonConvert.DeserializeObject<ProjectBaseline>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Data", "STING_PROJECT_BASELINE.json")));

        [Fact]
        public void The_Shipped_Baseline_Validates()
        {
            Assert.Empty(Shipped().Validate());
        }

        [Fact]
        public void At_Least_One_Floor_And_One_Wall_Type_Carry_A_Tiled_Finish()
        {
            // The entire point: a model authored from this baseline can have its
            // tiling measured, which the real model could not.
            var b = Shipped();

            Assert.Contains(b.FloorTypes, t => t.HasTiledFinish);
            Assert.Contains(b.WallTypes, t => t.HasTiledFinish);
        }

        [Fact]
        public void The_Masonry_Materials_Are_Named_So_The_Takeoff_Can_Tell_Brick_From_Block()
        {
            // CompoundTakeoffBuilder.BuildWall decides by material name:
            // contains "brick" -> bricks, else blocks.
            var b = Shipped();
            var brickWall = b.WallTypes.First(t => t.Name.IndexOf("Brick", StringComparison.OrdinalIgnoreCase) >= 0);
            var blockWall = b.WallTypes.First(t => t.Name.IndexOf("Blockwork", StringComparison.OrdinalIgnoreCase) >= 0);

            Assert.Contains(brickWall.Layers, l => l.Material.ToLowerInvariant().Contains("brick"));
            Assert.DoesNotContain(blockWall.Layers, l => l.Material.ToLowerInvariant().Contains("brick"));
        }

        [Fact]
        public void The_Concrete_Roof_Names_Its_Material_Explicitly()
        {
            // A roof only decomposes when its material EXPLICITLY reads as
            // concrete — an unnamed roof is treated as unknown, never as
            // concrete, so 137 m3 is not invented.
            var roof = Shipped().RoofTypes.First(t => t.Name.IndexOf("Flat Roof", StringComparison.OrdinalIgnoreCase) >= 0);

            Assert.Contains(roof.Layers, l => l.Material.ToLowerInvariant().Contains("concrete"));
        }

        [Fact]
        public void The_Sheet_And_Tile_Roof_Names_Match_Their_Supplier_Rules()
        {
            // The supplier rules convert a roof by category NARROWED by type-name
            // pattern. A baseline type whose name matches no pattern would ship
            // pre-broken.
            var units = JsonConvert.DeserializeObject<StingTools.Core.MaterialSchedule.SupplierUnitTable>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "STING_SUPPLIER_UNITS.json")));

            foreach (string key in new[] { "roof-sheet", "roof-tile" })
            {
                var rule = units.Rules.First(r => r.CommodityKey == key);
                Assert.Contains(Shipped().RoofTypes, t => rule.MatchTypePatterns.Any(p =>
                    t.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0));
            }
        }

        [Fact]
        public void Every_Baseline_Tile_Material_Is_Recognised_As_Tiling()
        {
            var b = Shipped();
            var tileMaterials = b.Materials
                .Where(m => m.Name.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0
                         && m.Name.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();

            Assert.NotEmpty(tileMaterials);
            foreach (var m in tileMaterials)
                Assert.True(StingTools.Core.MaterialSchedule.FinishTextClassifier.IsTile(m.Name),
                    $"'{m.Name}' is in the baseline as tiling but the classifier does not recognise it");
        }

        [Fact]
        public void Levels_Ship_Empty_Because_They_Are_A_Project_Decision()
        {
            // Auditing every model against someone else's storey heights would
            // report noise on every project. The machinery supports levels; the
            // corporate list deliberately does not use it.
            Assert.Empty(Shipped().Levels);
        }
    }
}
