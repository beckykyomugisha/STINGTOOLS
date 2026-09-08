using System;
using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// BS EN ISO 22014:2024 — which superseded BS 8541-1 in May 2024 — names a library
    /// object in three underscore-separated fields: <c>Source_Type_Subtype</c>.
    ///
    /// <code>
    ///     PLNS_WBL_Hollow200-PlasteredBothFaces
    ///     ^^^^ ^^^ ^^^^^^^^^^^^^^^^^^^^^^^^^^^
    ///     |    |   Subtype — hyphens between components
    ///     |    Type — the ISO 19650 PROD code
    ///     Source — originator
    /// </code>
    ///
    /// <para><b>Why the Type field is the PROD code.</b> Before this, a conforming name
    /// carried the WORD "Blockwork" while the resolver derived the CODE "WBL" from it by
    /// pattern. Two vocabularies for one fact, free to disagree the moment either was
    /// edited — the exact defect class this codebase has spent a long session removing.
    /// Naming the code directly means the resolver READS it.</para>
    ///
    /// <para>ISO 19650 is NOT what governs a type name — it governs information
    /// containers. Saying otherwise is a common and wrong shorthand.</para>
    /// </summary>
    public class IsoTypeNameTests
    {
        private static readonly HashSet<string> Codes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "WBL", "WBK", "SLB", "RSH", "DR", "WIN", "PMP" };

        // ══════════════════════════════════════════════════════════════════════
        //  Reading a name that states its code
        // ══════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData("PLNS_WBL_Hollow200-PlasteredBothFaces", "WBL")]
        [InlineData("PLNS_SLB_RC150-CeramicTiled", "SLB")]
        [InlineData("PLNS_RSH_IT4-OnTimberPurlins", "RSH")]
        [InlineData("ABC_WBK_Clay295", "WBK")]
        public void The_Type_Field_Is_Read_As_The_Product_Code(string name, string expected)
        {
            Assert.Equal(expected, ProdNameCode.Extract(name, Codes));
        }

        [Fact]
        public void Only_The_SECOND_Field_Counts()
        {
            // The safety property. "DR" is a real code, and a subtype component that
            // happens to spell one must not hijack the name.
            Assert.Equal("WBL", ProdNameCode.Extract("PLNS_WBL_DR-Set", Codes));
        }

        [Fact]
        public void An_Unknown_Token_Is_Not_A_Code()
        {
            // Without this, any underscore-delimited word becomes a classification and a
            // naming convention turns into a guessing game.
            Assert.Null(ProdNameCode.Extract("PLNS_ZZZ_Whatever", Codes));
            Assert.Null(ProdNameCode.Extract("Exterior_CreamWhite_230", Codes));
        }

        [Theory]
        [InlineData("PLNS")]                    // one field
        [InlineData("Generic - 225mm")]         // the real delivered name
        [InlineData("")]
        [InlineData(null)]
        public void A_Name_With_No_Type_Field_Declares_Nothing(string name)
        {
            Assert.Null(ProdNameCode.Extract(name, Codes));
        }

        [Fact]
        public void With_No_Known_Codes_Nothing_Is_Declared()
        {
            // A caller that has not loaded the rule set must get inference, not a
            // free-for-all where every second field becomes a code.
            Assert.Null(ProdNameCode.Extract("PLNS_WBL_Hollow200", null));
            Assert.Null(ProdNameCode.Extract("PLNS_WBL_Hollow200", new HashSet<string>()));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Composing one
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Fields_Take_Underscores_And_Components_Take_Hyphens()
        {
            // The separator rule is the whole of the format. Mixing them makes the name
            // unparseable, which would make the Type field decorative again.
            Assert.Equal("PLNS_WBL_Hollow200-Plastered",
                ProdNameCode.Compose("PLNS", "WBL", "Hollow200", "Plastered"));
        }

        [Fact]
        public void A_Subtype_Is_Optional()
        {
            Assert.Equal("PLNS_WBL", ProdNameCode.Compose("PLNS", "WBL"));
            Assert.Equal("PLNS_WBL", ProdNameCode.Compose("PLNS", "WBL", null, "  "));
        }

        [Fact]
        public void Spaces_And_Stray_Separators_Are_Stripped()
        {
            // ISO 22014 permits neither. A space would break the field split, and a
            // stray underscore inside a component would invent a fourth field.
            Assert.Equal("PLNS_WBL_Hollow200-CeramicTiled",
                ProdNameCode.Compose(" PLNS ", "WBL", "Hollow 200", "Ceramic_Tiled"));
        }

        [Fact]
        public void A_Missing_Originator_Falls_Back_To_PLNS()
        {
            Assert.Equal("PLNS_WBL_Hollow200", ProdNameCode.Compose("", "WBL", "Hollow200"));
        }

        [Fact]
        public void No_Code_Means_No_Name()
        {
            // Composing a name with an empty Type field would produce PLNS__Hollow200,
            // which parses to nothing and looks deliberate.
            Assert.Null(ProdNameCode.Compose("PLNS", "", "Hollow200"));
        }

        [Fact]
        public void What_Compose_Writes_Extract_Reads()
        {
            // The round trip. If these two ever disagree, a conforming name stops
            // resolving and silently falls back to pattern inference.
            foreach (string code in new[] { "WBL", "SLB", "RSH" })
            {
                string name = ProdNameCode.Compose("PLNS", code, "Core200", "Finished");
                Assert.Equal(code, ProdNameCode.Extract(name, Codes));
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  The resolver tier
        // ══════════════════════════════════════════════════════════════════════

        private static readonly List<(string, string)> WallRules =
            new List<(string, string)> { ("*BLOCKWORK*", "WBL"), ("*BRICK*", "WBK") };

        [Fact]
        public void A_Declared_Code_Beats_Pattern_Inference()
        {
            // The name says WBK. A pattern would say WBL from the word "Blockwork" in the
            // subtype. The statement wins — that is the point of stating it.
            string got = ProdResolver.Resolve("Basic Wall", "PLNS_WBK_BlockworkLook", "Walls",
                null, WallRules, null, out string src, Codes);

            Assert.Equal("WBK", got);
            Assert.Equal(ProdResolver.Sources.Declared, src);
            Assert.True(ProdResolver.IsSpecific(src));
        }

        [Fact]
        public void A_PROJECT_Overlay_Still_Beats_A_Declared_Code()
        {
            // The tier order is deliberate: a project overlay is an explicit instruction
            // for THIS job and outranks even a stated code, exactly as it outranks
            // corporate rules.
            string got = ProdResolver.Resolve("Basic Wall", "PLNS_WBL_Hollow200", "Walls",
                new List<(string, string)> { ("*HOLLOW200*", "PRJ") }, WallRules, null,
                out string src, Codes);

            Assert.Equal("PRJ", got);
            Assert.Equal(ProdResolver.Sources.Project, src);
        }

        [Fact]
        public void A_NON_Conforming_Name_Still_Resolves_By_Pattern()
        {
            // The rescue path is unchanged. Every delivered model is full of names like
            // this, and the standard does not retire the rules that carry them.
            string got = ProdResolver.Resolve("Basic Wall", "230 Blockwork Rendered", "Walls",
                null, WallRules, null, out string src, Codes);

            Assert.Equal("WBL", got);
            Assert.Equal(ProdResolver.Sources.Corporate, src);
        }

        [Fact]
        public void A_Coded_Name_Resolves_Even_With_No_Family_Name()
        {
            // A wall's family name is "Basic Wall" for every wall ever made, and the
            // empty-family guard exists because a family name usually discriminates
            // nothing. A name carrying its own answer does not need that guard.
            string got = ProdResolver.Resolve("", "PLNS_SLB_RC150", "Floors",
                null, null, new Dictionary<string, string> { ["Floors"] = "FL" },
                out string src, Codes);

            Assert.Equal("SLB", got);
            Assert.Equal(ProdResolver.Sources.Declared, src);
        }

        [Fact]
        public void Callers_That_Pass_No_Codes_Behave_Exactly_As_Before()
        {
            // knownCodes defaults to null, so the ~10 existing call sites are untouched
            // and the tier is opt-in rather than a change under everyone's feet.
            string got = ProdResolver.Resolve("Basic Wall", "PLNS_WBK_BlockworkLook", "Walls",
                null, WallRules, null, out string src);

            Assert.Equal("WBL", got);                       // inferred, not declared
            Assert.Equal(ProdResolver.Sources.Corporate, src);
        }
    }
}
