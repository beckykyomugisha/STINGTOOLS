using StingTools.Core.MaterialSchedule;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-SCHED-3 — the diagnostic that turns "no tiling appeared" into an
    /// answer. Its entire value is that the message is right, so the message is
    /// what is pinned here.
    /// </summary>
    public class TileScanTallyTests
    {
        [Fact]
        public void Nothing_Inspected_Reports_Nothing()
        {
            // A scan that never ran must stay silent. An invented "0 of 0" would
            // read as a finding and send someone looking for a fault.
            Assert.Null(new TileScanTally().Summary());
        }

        [Fact]
        public void No_Finish_Layers_Says_The_Model_Is_Silent_Not_The_Plugin_Broken()
        {
            var t = new TileScanTally { TypesInspected = 47 };

            string s = t.Summary();

            Assert.Contains("47 wall/floor type(s) inspected", s);
            Assert.Contains("0 carry a finish layer", s);
            Assert.Contains("does not describe its finishes", s);
        }

        [Fact]
        public void Unrecognised_Materials_Are_Named_So_The_Pattern_Can_Be_Fixed()
        {
            var t = new TileScanTally { TypesInspected = 12, TypesWithFinishLayer = 5 };
            t.RejectedMaterials.Add("FF-01 Floor Finish");
            t.RejectedMaterials.Add("Screed");

            string s = t.Summary();

            Assert.Contains("FF-01 Floor Finish", s);
            Assert.Contains("Screed", s);
            Assert.Contains("widen the tile pattern", s);
        }

        [Fact]
        public void The_Name_List_Is_Capped_And_Says_So()
        {
            var t = new TileScanTally { TypesInspected = 30, TypesWithFinishLayer = 30 };
            for (int i = 0; i < 20; i++) t.RejectedMaterials.Add($"Material {i:D2}");

            string s = t.Summary();

            Assert.Contains("…", s);
            Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(s, "Material ").Count);
        }

        [Fact]
        public void A_Successful_Scan_Reports_Counts_Without_The_Advice()
        {
            var t = new TileScanTally { TypesInspected = 12, TypesWithFinishLayer = 5, TypesMatched = 3 };
            t.RejectedMaterials.Add("Screed");   // present, but irrelevant once tiling was found

            string s = t.Summary();

            Assert.Contains("3 name a tile material", s);
            Assert.DoesNotContain("widen the tile pattern", s);
            Assert.DoesNotContain("Screed", s);
        }

        [Fact]
        public void Finish_Layers_With_No_Named_Material_Get_Their_Own_Advice()
        {
            // Distinct from "no finish layers": the layers exist, so the fix is
            // to assign materials, not to model finishes at all.
            var t = new TileScanTally { TypesInspected = 9, TypesWithFinishLayer = 4 };

            string s = t.Summary();

            Assert.Contains("carry no named material", s);
        }

        [Fact]
        public void Reset_Clears_Everything_So_A_Run_Never_Reports_A_Previous_One()
        {
            var t = new TileScanTally { TypesInspected = 5, TypesWithFinishLayer = 2, TypesMatched = 1 };
            t.RejectedMaterials.Add("Screed");

            t.Reset();

            Assert.Null(t.Summary());
            Assert.Empty(t.RejectedMaterials);
        }
    }
}
