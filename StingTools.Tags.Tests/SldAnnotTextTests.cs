// ══════════════════════════════════════════════════════════════════════════
//  SldAnnotTextTests.cs — ELEC-10.
//
//  The SLD annotation text, split out of the Revit command so the three
//  formats can be pinned. Compact / Full / Reference used to be one chooser
//  (every button opened it), and "Reference" only changed the Cable text.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using StingTools.Core.SLD;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SldAnnotTextTests
    {
        private static readonly Dictionary<string, string> Full = new Dictionary<string, string>
        {
            [SldAnnotText.P_VOLTAGE] = "400 V",
            [SldAnnotText.P_CURRENT] = "32",
            [SldAnnotText.P_FAULT]   = "6",
            [SldAnnotText.P_CABLE]   = "4",
            [SldAnnotText.P_BREAKER] = "B32",
            [SldAnnotText.P_LOAD]    = "18.5",
            [SldAnnotText.P_PANEL]   = "DB-L1",
            [SldAnnotText.P_CIRCUIT] = "C07",
        };

        private static string Get(Dictionary<string, string> d, string p) => d.TryGetValue(p, out var v) ? v : "";

        private static string B(SldAnnotKind k, SldAnnotFormat f, Dictionary<string, string> d = null)
            => SldAnnotText.Build(k, f, p => Get(d ?? Full, p));

        [Fact]
        public void Cable_compact_joins_csa_current_breaker()
            => Assert.Equal("4 / 32 / B32", B(SldAnnotKind.Cable, SldAnnotFormat.Compact));

        [Fact]
        public void Cable_full_is_labelled()
            => Assert.Equal("Cable: 4 mm² | Ib: 32A | Breaker: B32", B(SldAnnotKind.Cable, SldAnnotFormat.Full));

        [Theory]
        [InlineData(SldAnnotKind.Cable)]
        [InlineData(SldAnnotKind.Voltage)]
        [InlineData(SldAnnotKind.All)]
        [InlineData(SldAnnotKind.Load)]
        public void Reference_format_is_panel_and_circuit_only_whatever_the_kind(object kind)
            => Assert.Equal("DB-L1 — C07", B((SldAnnotKind)kind, SldAnnotFormat.Reference));

        [Fact]
        public void The_three_formats_differ_for_All()
        {
            var compact = B(SldAnnotKind.All, SldAnnotFormat.Compact);
            var full    = B(SldAnnotKind.All, SldAnnotFormat.Full);
            var refr    = B(SldAnnotKind.All, SldAnnotFormat.Reference);
            Assert.Equal("4 / 32 / B32  400 V  6kA  18.5kW", compact);
            Assert.Equal("Cable: 4 mm² | Ib: 32A | Breaker: B32 | Voltage: 400 V | Icc: 6 kA | Load: 18.5 kW", full);
            Assert.Equal("DB-L1 — C07", refr);
        }

        [Fact]
        public void Missing_values_give_empty_text_not_placeholders()
        {
            var empty = new Dictionary<string, string>();
            foreach (SldAnnotKind k in System.Enum.GetValues(typeof(SldAnnotKind)))
                foreach (SldAnnotFormat f in System.Enum.GetValues(typeof(SldAnnotFormat)))
                    Assert.Equal("", B(k, f, empty));
        }

        [Theory]
        [InlineData("400 V", "3Ph+N+PE")]
        [InlineData("0.4kV", "3Ph+N+PE")]
        [InlineData("230", "1Ph+N")]
        [InlineData("11 kV", "3Ph+N+PE")]
        public void Phase_label_from_voltage(string v, string expected)
        {
            var d = new Dictionary<string, string> { [SldAnnotText.P_VOLTAGE] = v };
            Assert.Equal(expected, B(SldAnnotKind.Phase, SldAnnotFormat.Compact, d));
        }

        [Fact]
        public void Unparseable_voltage_gets_no_phase_label_rather_than_a_guess()
        {
            var d = new Dictionary<string, string> { [SldAnnotText.P_VOLTAGE] = "LV" };
            Assert.Equal("", B(SldAnnotKind.Phase, SldAnnotFormat.Compact, d));
        }

        [Theory]
        [InlineData("Voltage", true)]
        [InlineData("All", true)]
        [InlineData("Diversity", true)]
        [InlineData("3", false)]      // numeric strings parse as enums in .NET — reject them
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("voltage", false)] // stamps are written with ToString(); case must match
        public void Stamped_kind_round_trips(string s, bool ok)
            => Assert.Equal(ok, SldAnnotText.TryParseKind(s, out _));

        [Fact]
        public void Every_kind_round_trips_through_its_stamp()
        {
            foreach (SldAnnotKind k in System.Enum.GetValues(typeof(SldAnnotKind)))
            {
                Assert.True(SldAnnotText.TryParseKind(k.ToString(), out var back));
                Assert.Equal(k, back);
            }
            foreach (SldAnnotFormat f in System.Enum.GetValues(typeof(SldAnnotFormat)))
            {
                Assert.True(SldAnnotText.TryParseFormat(f.ToString(), out var back));
                Assert.Equal(f, back);
            }
        }
    }
}
