using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// MAT-1 — slab void systems. A hollow-pot / rib / waffle / trough slab is
    /// modelled solid in Revit; the registry resolves the SOLID FRACTION so
    /// concrete, carbon and rebar are netted. Tests run against the shipped
    /// STING_SLAB_SYSTEMS.json so the seeded fractions are validated too.
    /// </summary>
    public class SlabSystemTests
    {
        private static SlabSystemRegistry Shipped()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "STING_SLAB_SYSTEMS.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            string paramName = root.TryGetProperty("paramName", out var p) ? p.GetString() : "BLE_SLAB_SYSTEM_TXT";
            var systems = new List<SlabSystem>();
            foreach (var s in root.GetProperty("systems").EnumerateArray())
            {
                var kws = new List<string>();
                if (s.TryGetProperty("keywords", out var ka))
                    foreach (var k in ka.EnumerateArray()) kws.Add(k.GetString());
                systems.Add(new SlabSystem
                {
                    Id = s.GetProperty("id").GetString(),
                    Label = s.TryGetProperty("label", out var l) ? l.GetString() : "",
                    SolidFraction = s.GetProperty("solidFraction").GetDouble(),
                    Indicative = !s.TryGetProperty("indicative", out var i) || i.GetBoolean(),
                    Keywords = kws
                });
            }
            return new SlabSystemRegistry(systems, paramName);
        }

        [Theory]
        [InlineData("300 Hollow Pot Slab", "hollow_pot")]
        [InlineData("Clay-Pot Slab 250", "hollow_pot")]
        [InlineData("250 Ribbed Slab", "ribbed")]
        [InlineData("Waffle Slab 400", "waffle")]
        [InlineData("Coffered Slab", "waffle")]
        [InlineData("Trough Slab 300", "trough")]
        public void Void_Slabs_Resolve_To_Their_System(string typeName, string expectedId)
        {
            var m = Shipped().Resolve(typeName);
            Assert.Equal(expectedId, m.Id);
            Assert.True(m.IsVoidSystem, $"{typeName} should be a void system");
            Assert.True(m.SolidFraction > 0.5 && m.SolidFraction < 0.75, $"solid fraction {m.SolidFraction} out of seeded range");
        }

        [Theory]
        [InlineData("200mm RC Slab")]
        [InlineData("Concrete Slab 150")]
        [InlineData("Generic Floor")]
        public void Solid_Slab_Is_Unchanged(string typeName)
        {
            var m = Shipped().Resolve(typeName);
            Assert.False(m.IsVoidSystem);
            Assert.Equal(1.0, m.SolidFraction, 6);
        }

        [Theory]
        [InlineData("Fibrous Plaster Slab")]   // contains "rib" inside "fibrous" — must NOT match
        [InlineData("Ribbon Floor")]            // "ribbon" must NOT match "rib"/"ribbed"
        [InlineData("Spotlight Recess Slab")]   // "pot" inside "spotlight" — must NOT match
        public void Word_Boundary_Prevents_False_Matches(string typeName)
        {
            Assert.False(Shipped().Resolve(typeName).IsVoidSystem, $"{typeName} false-matched a void system");
        }

        [Fact]
        public void Explicit_Parameter_Overrides_Type_Name()
        {
            // A solid-named type but tagged hollow_pot via BLE_SLAB_SYSTEM_TXT.
            var m = Shipped().Resolve("Generic 250 Slab", "hollow_pot");
            Assert.Equal("hollow_pot", m.Id);
            Assert.Equal("param", m.MatchedOn);
        }

        [Fact]
        public void Net_Concrete_Is_Gross_Times_Solid_Fraction()
        {
            // The arithmetic the BOQ + rebar estimator apply.
            double grossM3 = 100.0;
            double sf = Shipped().SolidFraction("Hollow Pot Slab");
            double net = grossM3 * sf;
            Assert.True(net < grossM3, "net must be below gross for a void slab");
            Assert.InRange(net, 55, 70); // ~0.62 → 62 m³
        }

        [Fact]
        public void Rebar_Uses_Net_Volume_Times_Band()
        {
            // rebar kg = net concrete × slab band (80 kg/m³).
            double grossM3 = 50.0, slabBand = 80.0;
            double sf = Shipped().SolidFraction("250 Ribbed Slab");
            double rebarNet = grossM3 * sf * slabBand;
            double rebarGross = grossM3 * slabBand;
            Assert.True(rebarNet < rebarGross);
            Assert.Equal(grossM3 * 0.60 * slabBand, rebarNet, 1); // ribbed sf 0.60
        }

        [Fact]
        public void Empty_Or_Null_Inputs_Are_Solid()
        {
            var r = Shipped();
            Assert.False(r.Resolve(null).IsVoidSystem);
            Assert.False(r.Resolve("").IsVoidSystem);
            Assert.Equal(1.0, r.SolidFraction(""), 6);
        }
    }
}
