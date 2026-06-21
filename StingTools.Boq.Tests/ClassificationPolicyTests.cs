using System.Linq;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Phase 196 — host-free classification-order overlay (classification_policy.json).
    public class ClassificationPolicyTests
    {
        [Fact]
        public void Default_Reproduces_Historic_Order()
        {
            var p = ClassificationPolicy.Default;
            var ids = p.Order.Select(s => s.Id).ToArray();
            Assert.Equal(new[] { "uniclass.pr", "uniclass.ss", "uniclass.ef", "omniclass23", "native" }, ids);
            Assert.True(p.Order.Last().IsNative);
            // The Pr rung carries the historic param + key prefix.
            var pr = p.Order[0];
            Assert.Equal("UNICLASS_PR_TXT", pr.Param);
            Assert.Equal("PR", pr.Prefix);
            Assert.Equal("Uniclass.Pr", pr.Label);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ not json")]
        [InlineData("{ \"order\": [] }")]
        public void Blank_Malformed_Or_Empty_Falls_Back_To_Default(string raw)
        {
            var p = ClassificationPolicy.Parse(raw);
            var ids = p.Order.Select(s => s.Id).ToArray();
            Assert.Equal(new[] { "uniclass.pr", "uniclass.ss", "uniclass.ef", "omniclass23", "native" }, ids);
        }

        [Fact]
        public void American_Overlay_Puts_Csi_First()
        {
            string json = @"{
              ""order"": [
                { ""id"": ""csi"",         ""param"": ""CSI_SECTION_TXT"",    ""prefix"": ""CSI"",  ""label"": ""CSI.MasterFormat"" },
                { ""id"": ""omniclass23"", ""param"": ""STING_OMNICLASS_23"", ""prefix"": ""OMNI"", ""label"": ""OmniClass23"" },
                { ""id"": ""uniclass.pr"", ""param"": ""UNICLASS_PR_TXT"",    ""prefix"": ""PR"",   ""label"": ""Uniclass.Pr"" },
                { ""id"": ""native"" }
              ]
            }";
            var p = ClassificationPolicy.Parse(json);
            Assert.Equal("csi", p.Order[0].Id);
            Assert.Equal("CSI_SECTION_TXT", p.Order[0].Param);
            Assert.Equal("CSI", p.Order[0].Prefix);
            Assert.True(p.Order.Last().IsNative);
        }

        [Fact]
        public void Prefix_And_Label_Are_Synthesised_When_Omitted()
        {
            // A bespoke owner table named only by id + param — prefix/label derived.
            string json = @"{ ""order"": [ { ""id"": ""church.fcs"", ""param"": ""OWNER_FCS_CODE_TXT"" }, { ""id"": ""native"" } ] }";
            var p = ClassificationPolicy.Parse(json);
            var s = p.Order[0];
            Assert.Equal("OWNER_FCS_CODE_TXT", s.Param);
            Assert.Equal("FCS", s.Prefix);          // tail after the dot, upper-cased
            Assert.Equal("church.fcs", s.Label);    // falls back to the id
        }

        [Fact]
        public void Missing_Native_Rung_Is_Appended()
        {
            string json = @"{ ""order"": [ { ""id"": ""csi"", ""param"": ""CSI_SECTION_TXT"" } ] }";
            var p = ClassificationPolicy.Parse(json);
            Assert.Equal(2, p.Order.Count);
            Assert.True(p.Order.Last().IsNative);
        }

        [Fact]
        public void Rungs_After_Native_Are_Dropped_And_Duplicates_Collapsed()
        {
            string json = @"{
              ""order"": [
                { ""id"": ""csi"", ""param"": ""CSI_SECTION_TXT"" },
                { ""id"": ""native"" },
                { ""id"": ""native"" },
                { ""id"": ""uniclass.pr"", ""param"": ""UNICLASS_PR_TXT"" }
              ]
            }";
            var p = ClassificationPolicy.Parse(json);
            Assert.Equal(2, p.Order.Count);          // csi + single native; trailing rungs dropped
            Assert.Equal("csi", p.Order[0].Id);
            Assert.True(p.Order[1].IsNative);
        }

        [Fact]
        public void Entry_Without_Param_Is_Treated_As_Native_Terminal()
        {
            string json = @"{ ""order"": [ { ""id"": ""uniclass.pr"", ""param"": ""UNICLASS_PR_TXT"" }, { ""id"": ""fallback"" } ] }";
            var p = ClassificationPolicy.Parse(json);
            Assert.Equal(2, p.Order.Count);
            Assert.False(p.Order[0].IsNative);
            Assert.True(p.Order[1].IsNative);        // no param → terminal native
        }
    }
}
