using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review DES-3. The corporate owner-standards pack applies to EVERY project; the
    /// workset rule in it carried KUT's six buildings (^(BLD[1-6]|EXT)_) and was enabled, so every
    /// other project got a warning on every workset — and KUT itself failed it, because its own
    /// mobilisation creates discipline worksets (A-ARCH-Walls …).
    /// </summary>
    public class CorporateOwnerPackNeutralityTests
    {
        [Fact]
        public void NoEnabledCorporateRuleCarriesAProjectsBuildingList()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var pack = JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_OWNER_STANDARDS_PACK.json")));
            var rules = pack["rules"].ToList();
            Assert.True(rules.Count > 3, "the corporate pack parsed to almost nothing");

            var offenders = rules
                .Where(r => (bool?)r["enabled"] == true)
                .Where(r => ((string)r["pattern"] ?? "").Contains("BLD[1-6]"))
                .Select(r => (string)r["id"])
                .ToList();
            Assert.True(offenders.Count == 0,
                "enabled corporate rules carrying a project's building list: " + string.Join(", ", offenders));
        }
    }
}
