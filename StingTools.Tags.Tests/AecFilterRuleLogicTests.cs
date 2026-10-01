// DTW-166 / DTW-167 / DTW-171 — the Revit-free decisions of AecFilterFactory.

using System.Collections.Generic;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class AecFilterRuleLogicTests
    {
        // ── DTW-171: compound rules ──

        [Fact]
        public void AndWithAFailedChildFailsTheWholeFilter()
        {
            var w = new List<string>();
            Assert.Null(AecFilterRuleLogic.Combine("and", new List<string> { "a", null }, w));
            Assert.Contains(w, m => m.Contains("refused"));
        }

        [Fact]
        public void MissingLogicIsTreatedAsAnd()
            => Assert.Null(AecFilterRuleLogic.Combine<string>(null, new List<string> { "a", null }, null));

        [Fact]
        public void OrWithAFailedChildDropsItAndSaysSo()
        {
            var w = new List<string>();
            var kept = AecFilterRuleLogic.Combine("OR", new List<string> { "a", null, "b" }, w);
            Assert.Equal(new[] { "a", "b" }, kept);
            Assert.Contains(w, m => m.Contains("left out"));
        }

        [Fact]
        public void OrWithNothingBuiltFails()
            => Assert.Null(AecFilterRuleLogic.Combine("or", new List<string> { null, null }, new List<string>()));

        [Fact]
        public void AllChildrenBuiltPassesThrough()
            => Assert.Equal(new[] { "a", "b" }, AecFilterRuleLogic.Combine("and", new List<string> { "a", "b" }, null));

        [Theory]
        [InlineData("3", true, 3)]
        [InlineData(" -2 ", true, -2)]
        [InlineData("true", true, 1)]
        [InlineData("False", true, 0)]
        [InlineData("", false, 0)]
        [InlineData("Shear", false, 0)]
        [InlineData("2.5", false, 0)]
        public void IntegerValuesAreParsedStrictly(string text, bool ok, int expected)
        {
            Assert.Equal(ok, AecFilterRuleLogic.TryParseInt(text, out var v));
            if (ok) Assert.Equal(expected, v);
        }

        [Theory]
        [InlineData("2.5", true)]
        [InlineData("1000", true)]
        [InlineData("2,5", false)]
        [InlineData("high", false)]
        [InlineData(null, false)]
        public void NumberValuesAreParsedStrictly(string text, bool ok)
            => Assert.Equal(ok, AecFilterRuleLogic.TryParseDouble(text, out _));

        // ── DTW-166: "None" phase ──

        [Theory]
        [InlineData("None", true)]
        [InlineData("<None>", true)]
        [InlineData("", true)]
        [InlineData(null, true)]
        [InlineData("New Construction", false)]
        public void NoneMeansNoElement(string value, bool none)
            => Assert.Equal(none, AecFilterRuleLogic.IsNoneValue(value));

        // ── DTW-167: definition hash ──

        private static AecFilterRule Leaf(string value) =>
            new AecFilterRule { Param = "FUNCTION_PARAM", Op = "equals", Value = value, Type = "int" };

        [Fact]
        public void HashChangesWhenTheRuleChanges()
            => Assert.NotEqual(
                AecFilterRuleLogic.DefinitionHash(new[] { "OST_Walls" }, Leaf("3")),
                AecFilterRuleLogic.DefinitionHash(new[] { "OST_Walls" }, Leaf("1")));

        [Fact]
        public void HashChangesWhenTheCategoriesChange()
            => Assert.NotEqual(
                AecFilterRuleLogic.DefinitionHash(new[] { "OST_Walls" }, Leaf("3")),
                AecFilterRuleLogic.DefinitionHash(new[] { "OST_Walls", "OST_Floors" }, Leaf("3")));

        [Fact]
        public void HashIgnoresCategoryOrder()
            => Assert.Equal(
                AecFilterRuleLogic.DefinitionHash(new[] { "OST_Floors", "OST_Walls" }, Leaf("3")),
                AecFilterRuleLogic.DefinitionHash(new[] { "OST_Walls", "OST_Floors" }, Leaf("3")));
    }
}
