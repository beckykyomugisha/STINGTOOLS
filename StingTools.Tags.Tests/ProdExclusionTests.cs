using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// `Prod_CoverageAudit` reported **0.8% coverage** on a real model — 13 of 1,688.
    /// 1,187 of those 1,688 were `M_GM_OpeningWall_Instance`: wall VOIDS, which can
    /// never carry a product code. Add the filled regions, the rooms and the muntin
    /// patterns and 1,266 of the "failures" were not products at all. Over the 422
    /// elements that ARE products the figure is 3.1%.
    ///
    /// <para>Still low — that part is a genuine finding. But 0.8% overstated it
    /// four-fold and pointed the reader at the wrong problem, which is the same
    /// failure shape as a denominator that quietly counts the wrong thing.</para>
    ///
    /// <para><b>That 1,187 is not a coincidence.</b> ROADMAP MATSCHED-8 records the
    /// material schedule hitting the identical family on the identical model, and #724
    /// adding description-pattern exclusion for it. Two features asked "is this a
    /// thing?" and each had its own answer. The MECHANISM is now shared
    /// (<see cref="ProductExclusion"/>); the LISTS deliberately are not, and the tests
    /// below pin why.</para>
    /// </summary>
    public class ProdExclusionTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static ProductExclusion Shipped()
        {
            var doc = ProdExclusionPolicy.Parse(
                File.ReadAllText(Path.Combine(DataDir(), "STING_PROD_EXCLUSIONS.json")), out string err);
            Assert.True(doc != null, "STING_PROD_EXCLUSIONS.json did not parse: " + err);
            return ProdExclusionPolicy.Build(doc, null);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The model that produced the number
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verbatim from the audit CSV. The counts are the real ones, so this is a
        /// replay of the run rather than an illustration of it.
        /// </summary>
        private static readonly (string Category, string Family, string Type, int Count, bool IsProduct)[] Herring =
        {
            ("Generic Models",     "M_GM_OpeningWall_Instance",          "Opening",            1187, false),
            ("Detail Items",       "Filled region",                      "",                     34, false),
            ("Generic Models",     "M_Muntin Pattern_2x2",               "",                     22, false),
            ("Rooms",              "",                                   "",                     23, false),
            // ── and the things that ARE products ──────────────────────────────
            ("Curtain Panels",     "RD_Breeze Block 01_Panel",           "",                    128, true),
            ("Walls",              "Basic Wall",                         "",                     67, true),
            ("Furniture",          "Couch_-SINGLEl_13162 DOUBLE",        "",                     43, true),
            ("Roofs",              "Basic Roof",                         "",                     22, true),
            ("Windows",            "M_Window-Awning-Double-Vertical",    "",                     22, true),
            ("Doors",              "Single-Standard Frame w Vent",       "",                     20, true),
            ("Structural Columns", "Concrete-Rectangular-Column",        "200x200",              15, true),
            ("Plumbing Fixtures",  "M_Lavatory - Vanity1",               "650x450",              14, true),
            ("Generic Models",     "2022_RoofCap_Eagle_HighProfileTiles", "",                    29, true),
        };

        [Fact]
        public void The_1187_Wall_Voids_Leave_The_Denominator()
        {
            var ex = Shipped();
            int removed = Herring.Where(r => !r.IsProduct)
                                 .Where(r => ex.Classify(r.Category, r.Family, r.Type) != ExclusionVerdict.Included)
                                 .Sum(r => r.Count);
            Assert.Equal(1187 + 34 + 22 + 23, removed);
        }

        [Fact]
        public void And_Nothing_That_IS_A_Product_Leaves_With_Them()
        {
            // The failure mode of any exclusion list: it gets greedy and the number
            // improves for the wrong reason.
            var ex = Shipped();
            var wrongly = Herring.Where(r => r.IsProduct)
                                 .Where(r => ex.Classify(r.Category, r.Family, r.Type) != ExclusionVerdict.Included)
                                 .Select(r => $"{r.Category} / {r.Family} ({r.Count})")
                                 .ToList();
            Assert.True(wrongly.Count == 0,
                "Excluded as not-a-product, but these ARE products:\n  " + string.Join("\n  ", wrongly));
        }

        [Fact]
        public void The_Reported_Coverage_Goes_From_0_8_To_3_1_Percent()
        {
            // Measured on the model, not invented: 1,688 taggable elements, 13
            // family-specific, 1,266 of the rest not products at all.
            const int Total = 1688, Specific = 13, NotProducts = 1266;
            const int Products = Total - NotProducts;          // 422

            Assert.Equal(422, Products);
            Assert.Equal(0.8, Math.Round(100.0 * Specific / Total, 1));
            Assert.Equal(3.1, Math.Round(100.0 * Specific / Products, 1));

            // And the shipped policy is what removes exactly those 1,266 — the
            // sample below is the model's non-product families with their real
            // counts, so this ties the arithmetic to the data rather than to a
            // number typed into a test.
            var ex = Shipped();
            Assert.Equal(NotProducts,
                Herring.Where(r => ex.Classify(r.Category, r.Family, r.Type) != ExclusionVerdict.Included)
                       .Sum(r => r.Count));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The two lists are NOT the same list
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The whole reason this is a second list rather than a reuse of the material
        /// schedule's. `STING_MATERIAL_STAGES.json` excludes Furniture and Casework —
        /// correct there, you do not buy a sofa by the cubic metre — but FUR is a real
        /// PROD code and 43 of this model's elements carry it. Sharing the list would
        /// have traded a wrong number for a differently wrong one.
        /// </summary>
        [Theory]
        [InlineData("Furniture")]
        [InlineData("Furniture Systems")]
        [InlineData("Casework")]
        [InlineData("Planting")]
        [InlineData("Parking")]
        public void A_Category_The_MATERIAL_Schedule_Excludes_Is_Still_A_PRODUCT(string category)
        {
            Assert.Equal(ExclusionVerdict.Included, Shipped().Classify(category, "Anything", "Anything"));
        }

        [Fact]
        public void The_Material_Schedule_Really_Does_Exclude_Those()
        {
            // Without this, the test above asserts a difference that may not exist —
            // it would keep passing if the material list changed underneath it.
            string json = File.ReadAllText(Path.Combine(DataDir(), "STING_MATERIAL_STAGES.json"));
            foreach (string cat in new[] { "Furniture", "Casework" })
                Assert.Contains("\"" + cat + "\"", json);
        }

        [Fact]
        public void A_Curtain_Panel_Is_A_Product_Even_Though_128_Of_Them_Have_No_Rule()
        {
            // 128 breeze-block panels resolve generically. That is a COVERAGE gap and
            // exactly what the audit is for — excluding them would hide the finding
            // instead of reporting it.
            Assert.Equal(ExclusionVerdict.Included,
                Shipped().Classify("Curtain Panels", "RD_Breeze Block 01_Panel", ""));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Mechanism
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void A_Protected_Category_Outranks_A_Pattern()
        {
            // A door called "Opening Leaf" is still a door. The material schedule
            // dropped a real Windows row exactly this way.
            Assert.Equal(ExclusionVerdict.Included, Shipped().Classify("Doors", "Opening Leaf 900", ""));
            Assert.Equal(ExclusionVerdict.Included, Shipped().Classify("Windows", "Muntin Awning", ""));
        }

        [Fact]
        public void A_Blank_Pattern_Is_Inert_Not_A_Wildcard()
        {
            // "".IndexOf returns 0, so one empty cell would exclude the entire model.
            var ex = ProductExclusion.Build(null, new[] { "", "   ", null }, null);
            Assert.Equal(ExclusionVerdict.Included, ex.Classify("Walls", "Basic Wall", "Generic 200"));
            Assert.True(ex.IsEmpty);
        }

        [Fact]
        public void No_Policy_Excludes_NOTHING_Rather_Than_Guessing()
        {
            // The safe failure. A policy that silently removes rows nobody asked it to
            // remove is worse than no policy — it improves the number invisibly.
            Assert.Equal(ExclusionVerdict.Included,
                ProductExclusion.None.Classify("Rooms", "anything", "opening muntin"));
            Assert.True(ProductExclusion.None.IsEmpty);
        }

        [Fact]
        public void A_Missing_File_Is_Distinguishable_From_One_That_Excludes_Nothing()
        {
            // Parse returns NULL for absent/unreadable, not an empty document, so the
            // command can log "not deployed" rather than reporting a silent zero.
            Assert.Null(ProdExclusionPolicy.Parse(null, out _));
            Assert.Null(ProdExclusionPolicy.Parse("   ", out _));
            Assert.Null(ProdExclusionPolicy.Parse("{ not json", out string err));
            Assert.False(string.IsNullOrEmpty(err));

            var empty = ProdExclusionPolicy.Parse("{\"notAProductCategories\":[]}", out _);
            Assert.NotNull(empty);
            Assert.Empty(empty.NotAProductCategories);
        }

        [Fact]
        public void A_Project_Override_Replaces_Only_The_List_It_Declares()
        {
            // null = "not stated" (inherit), [] = "exclude nothing" (override). If the
            // two collapse, the key becomes impossible to override — the trap the
            // visibility presets already document.
            var corp = ProdExclusionPolicy.Parse(
                "{\"notAProductCategories\":[\"Rooms\"],\"notAProductPatterns\":[\"opening\"]}", out _);

            var silent = ProdExclusionPolicy.Build(corp, ProdExclusionPolicy.Parse("{}", out _));
            Assert.Equal(ExclusionVerdict.ByCategory, silent.Classify("Rooms", "", ""));
            Assert.Equal(ExclusionVerdict.ByPattern, silent.Classify("Walls", "Opening", ""));

            var cleared = ProdExclusionPolicy.Build(corp,
                ProdExclusionPolicy.Parse("{\"notAProductCategories\":[]}", out _));
            Assert.Equal(ExclusionVerdict.Included, cleared.Classify("Rooms", "", ""));
            Assert.Equal(ExclusionVerdict.ByPattern, cleared.Classify("Walls", "Opening", ""));
        }
    }
}
