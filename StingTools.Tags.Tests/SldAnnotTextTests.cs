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

        // ── Fallbacks: the ELC_CIR_* keys are written by nothing, so a real model
        //    has only Revit natives + the params that ARE written. ───────────────

        private static readonly Dictionary<string, string> RealModel = new Dictionary<string, string>
        {
            [SldAnnotText.N_VOLTAGE_V]  = "400",        // ElecUnits already converted from internal units
            [SldAnnotText.N_CURRENT_A]  = "27.43",
            [SldAnnotText.N_LOAD_VA]    = "19000",
            [SldAnnotText.N_RATING_A]   = "32",
            [SldAnnotText.N_WIRE_SIZE]  = "3x2.5mm²",
            [SldAnnotText.P_FAULT_KA]   = "6.00",
            [SldAnnotText.N_PANEL]      = "DB-L1",
            [SldAnnotText.N_CIRCUIT_NO] = "7",
        };

        private static string BF(SldAnnotKind k, SldAnnotFormat f, Dictionary<string, string> d)
            => SldAnnotText.Build(k, f, SldAnnotText.WithFallbacks(p => Get(d, p)));

        [Fact]
        public void Real_model_values_reach_every_annotation_kind()
        {
            Assert.Equal("400 V", BF(SldAnnotKind.Voltage, SldAnnotFormat.Compact, RealModel));
            Assert.Equal("27.4A", BF(SldAnnotKind.Current, SldAnnotFormat.Compact, RealModel));
            Assert.Equal("6kA", BF(SldAnnotKind.Fault, SldAnnotFormat.Compact, RealModel));
            Assert.Equal("2.5 / 27.4 / 32 A", BF(SldAnnotKind.Cable, SldAnnotFormat.Compact, RealModel));
            Assert.Equal("3Ph+N+PE", BF(SldAnnotKind.Phase, SldAnnotFormat.Compact, RealModel));
            Assert.Equal("DB-L1 — 7", BF(SldAnnotKind.Reference, SldAnnotFormat.Compact, RealModel));
        }

        [Fact]
        public void Apparent_load_fallback_is_kVA_never_relabelled_kW()
        {
            // 19000 VA / 1000 = 19 kVA.
            Assert.Equal("19kVA", BF(SldAnnotKind.Load, SldAnnotFormat.Compact, RealModel));
            Assert.Equal("Load: 19 kVA", BF(SldAnnotKind.Load, SldAnnotFormat.Full, RealModel));
        }

        [Fact]
        public void Sting_value_wins_over_its_fallback()
        {
            var d = new Dictionary<string, string>(RealModel) { [SldAnnotText.P_VOLTAGE] = "230 V" };
            Assert.Equal("230 V", BF(SldAnnotKind.Voltage, SldAnnotFormat.Compact, d));
        }

        [Fact]
        public void Imported_csa_wins_over_revit_wire_size_and_fault_alias_is_read()
        {
            var d = new Dictionary<string, string>
            {
                [SldAnnotText.P_CABLE_MM2]   = "4",
                [SldAnnotText.N_WIRE_SIZE]   = "3x2.5mm²",
                [SldAnnotText.P_FAULT_ALIAS] = "10",
            };
            Assert.Equal("4", BF(SldAnnotKind.Cable, SldAnnotFormat.Compact, d));
            Assert.Equal("10kA", BF(SldAnnotKind.Fault, SldAnnotFormat.Compact, d));
        }

        [Theory]
        [InlineData(SldAnnotText.N_VOLTAGE_V, "0")]
        [InlineData(SldAnnotText.N_RATING_A, "-5")]
        [InlineData(SldAnnotText.N_WIRE_SIZE, "n/a")]
        public void Zero_or_unparseable_fallback_is_empty_not_a_placeholder(string key, string raw)
            => Assert.Equal("", SldAnnotText.FormatFallback(key, raw));

        [Fact]
        public void Every_data_param_has_a_fallback()
        {
            foreach (var p in SldAnnotText.DataParams)
                Assert.True(SldAnnotText.Fallbacks.ContainsKey(p), p);
        }
    }
}
