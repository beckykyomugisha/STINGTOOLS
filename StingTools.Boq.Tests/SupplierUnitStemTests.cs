// ══════════════════════════════════════════════════════════════════════════
//  SupplierUnitStemTests.cs — W5a. The supplier patterns are PREFIX STEMS, and
//  this drives the real resolver so that swapping the matcher fails HERE.
//
//  The brief listed SupplierUnitConverter:191,228 as the #863 shape and said to
//  construct the false positive first. Over 1,810 real names there is not one:
//  every substring-only hit is either correct, or in a category the rule's own
//  `matchCategories` already excludes. What the swap WOULD do is lose 93 hits,
//  including two names this plugin generates itself.
//
//  The dangerous part, and the reason this file drives `Resolve` rather than
//  `PatternMatch`: making that swap breaks NOTHING in either test project as it
//  stood. 827 Tags tests and 1,249 Boq tests all pass with both call sites
//  converted. A change that silently rounds a roof into the wrong supplier unit,
//  or into none, is a quantity on an order — and nothing was watching.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    public class SupplierUnitStemTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_SUPPLIER_UNITS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static SupplierUnitTable _table;

        /// <summary>The SHIPPED rules, read from source. A hand-built table would test the
        /// resolver against itself; the point here is the shipped patterns.</summary>
        private static SupplierUnitTable Shipped()
            => _table ??= JsonConvert.DeserializeObject<SupplierUnitTable>(
                   File.ReadAllText(Path.Combine(DataDir(), "STING_SUPPLIER_UNITS.json")))
               ?? throw new InvalidOperationException("STING_SUPPLIER_UNITS.json did not deserialise");

        [Fact]
        public void The_Shipped_Table_Actually_Loaded()
        {
            // An empty table would make every assertion below vacuous — the
            // empty-list-standing-in-for-an-error shape.
            Assert.True(Shipped().Rules.Count >= 20, "only " + Shipped().Rules.Count + " rules loaded");
            Assert.Contains(Shipped().Rules, r => r.CommodityKey == "roof-sheet");
            Assert.Contains(Shipped().Rules, r => r.CommodityKey == "roof-tile");
        }

        [Theory]
        // material name                          category   expected commodity
        [InlineData("Corrugated Iron Sheet IT4", "Roofs", "roof-sheet")]
        [InlineData("Steel - Galvanised Sheet G28", "Roofs", "roof-sheet")]
        [InlineData("Steel - Galvanized Sheet G30", "Roofs", "roof-sheet")]
        public void A_PREFIX_Stem_Still_Resolves_Its_Rule(string material, string category, string commodity)
        {
            // corrugat / galvanis / galvaniz are prefixes of words, not words. Whole-word
            // matching cannot express them, and these three rows are what it would cost.
            var res = Shipped().Resolve(null, category, "probe", material);
            Assert.NotNull(res.Rule);
            Assert.Equal(commodity, res.Rule.CommodityKey);
        }

        [Theory]
        [InlineData("PLNS_RTL_StoneCoatedTileRoof1")]
        [InlineData("PLNS_RTL_ClayTileRoof14")]
        public void A_Compacted_ISO_Type_Name_Still_Resolves(string typeName)
        {
            // The argument that settles W5a. TypeRenamePlanner composes these names, and
            // this converter is then asked about them. They contain no word boundary at
            // all, so a whole-word matcher can never see inside one — and the roof would
            // resolve to no supplier unit rather than to the wrong one, which is a row
            // that quietly stays in m² on an order priced per tile.
            var res = Shipped().Resolve(null, "Roofs", typeName);
            Assert.NotNull(res.Rule);
            Assert.Equal("roof-tile", res.Rule.CommodityKey);
        }

        [Fact]
        public void The_Category_Gate_Is_What_Stops_The_Substring_False_Positives()
        {
            // GALVANIZED STEEL PIPE and CORRUGATED HDPE PIPE both match the roof-sheet
            // rule's material patterns as substrings. They are not roofs, and
            // matchCategories is what says so — not the matcher. Asserted because the
            // refusal to make these whole-word rests on this gate doing the work.
            foreach (string mat in new[] { "GALVANIZED STEEL PIPE 100MM (4 INCH)",
                                           "CORRUGATED HDPE PIPE 300MM" })
            {
                var roof = Shipped().Resolve(null, "Roofs", "probe", mat);
                Assert.Equal("roof-sheet", roof.Rule?.CommodityKey);      // reachable on a roof

                var pipe = Shipped().Resolve(null, "Pipes", "probe", mat);
                Assert.True(pipe.Rule == null || pipe.Rule.CommodityKey != "roof-sheet",
                    mat + " reached the roof-sheet rule from the Pipes category — the "
                        + "category gate is what makes substring matching safe here.");
            }
        }

        [Fact]
        public void The_Shipped_Note_Says_These_Are_Stems()
        {
            // So the next person to reach for "tidy these into whole words" reads it in the
            // data file, not in a changelog.
            string json = File.ReadAllText(Path.Combine(DataDir(), "STING_SUPPLIER_UNITS.json"));
            Assert.Contains("DELIBERATE PREFIX STEMS", json, StringComparison.Ordinal);
            Assert.Contains("MatcherSwapRefusalTests", json, StringComparison.Ordinal);
        }
    }
}
