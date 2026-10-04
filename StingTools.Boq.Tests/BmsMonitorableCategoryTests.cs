using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Twin;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// KUT deep review API-7. The monitorable scope is matched against Revit's category NAME, so
    /// a misspelt name silently drops a whole category: "Duct Accessory" (Revit says "Duct
    /// Accessories") kept every damper and VAV box out of the reconcile, the gap push and the
    /// BMS valuation.
    /// </summary>
    public class BmsMonitorableCategoryTests
    {
        [Fact]
        public void EveryMonitorableCategoryIsARealRevitCategoryName()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var reg = JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "PARAMETER_REGISTRY.json")));
            var names = ((JObject)reg["category_enum_map"]).Properties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.True(names.Count > 50, "category_enum_map parsed to almost nothing");

            var unknown = BmsValuation.MonitorableCategories.Where(c => !names.Contains(c)).ToList();
            Assert.True(unknown.Count == 0, "not Revit category names: " + string.Join(", ", unknown));
        }

        [Theory]
        [InlineData("Duct Accessories", true)]
        [InlineData("Mechanical Control Devices", true)]
        [InlineData("Mechanical Equipment", true)]
        [InlineData("Walls", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsMonitorable(string category, bool expected) =>
            Assert.Equal(expected, BmsValuation.IsMonitorable(category));
    }
}
