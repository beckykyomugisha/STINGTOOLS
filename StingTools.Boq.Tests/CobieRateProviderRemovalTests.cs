using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// DSCH-28. The COBie type-map rate provider keyed COBIE_TYPE_MAP.csv by its COBie
    /// <c>Category</c> list ("HVAC Equipment") but was asked with a Revit category name
    /// ("Mechanical Equipment"), so with the shipped data it never decided a price.
    /// Re-keying it by RevitCategory would have priced Communication and Security
    /// Devices at the distribution-board rate and Duct Accessories per metre. It was
    /// removed rather than re-keyed; these facts stop it coming back by accident.
    /// </summary>
    public class CobieRateProviderRemovalTests
    {
        private static string RepoFile(params string[] parts)
        {
            string rel = Path.Combine(parts);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, rel)))
                dir = dir.Parent;
            Assert.True(dir != null, rel + " not found above " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, rel);
        }

        [Fact]
        public void RateChain_HasNoCobieTypeMapProvider()
        {
            string providers = File.ReadAllText(RepoFile("StingTools", "BOQ", "Rates", "RateProviders.cs"));
            string registry = File.ReadAllText(RepoFile("StingTools", "BOQ", "Rates", "RateProviderRegistry.cs"));
            Assert.DoesNotContain("class CobieRateProvider", providers);
            Assert.DoesNotContain("new CobieRateProvider", registry);
        }

        [Fact]
        public void CobieTypeMap_CarriesNoCostRateCodeColumn()
        {
            string header = File.ReadLines(RepoFile("StingTools", "Data", "COBIE_TYPE_MAP.csv"))
                .First(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"));
            Assert.Contains("TypeCode", header);
            Assert.DoesNotContain("CostRateCode", header.Split(',').Select(h => h.Trim()));
        }
    }
}
