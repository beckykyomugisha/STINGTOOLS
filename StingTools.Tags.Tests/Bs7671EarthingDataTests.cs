// ══════════════════════════════════════════════════════════════════════════
//  Bs7671EarthingDataTests.cs — holds STING_BS7671_DISCONNECTION.json to the
//  UK DNO maximum declared Ze values.
//
//  The file shipped with TN-S and TN-C-S swapped (0.35 / 0.80). Ze feeds
//  straight into Zs = Ze + (R1+R2), so the swap flipped earth-fault-loop
//  verdicts: a 32 A B-curve on 30 m of 2.5/1.5 under TN-C-S computed 1.63 ohm
//  and FAILED when the correct figure (1.05 ohm) passes. Valid JSON and a green
//  build could not catch it — only a check against the numbers can.
//
//  RED / GREEN, recorded 2026-09-24:
//    shipped (swapped) data  RED   TN-C-S 0.80, expected 0.35
//    corrected data          GREEN
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class Bs7671EarthingDataTests
    {
        private static JObject Load()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            string path = Path.Combine(dir.FullName, "StingTools", "Data", "STING_BS7671_DISCONNECTION.json");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static double Ze(JObject root, string system)
        {
            var token = root["earthingSystems"]?[system]?["ZeOhm"];
            Assert.True(token != null, $"earthingSystems.{system}.ZeOhm is missing");
            return token.Value<double>();
        }

        [Theory]
        [InlineData("TN-C-S", 0.35)]
        [InlineData("TN-S", 0.80)]
        public void Ze_matches_UK_DNO_maximum(string system, double expected)
        {
            Assert.Equal(expected, Ze(Load(), system), 3);
        }

        [Fact]
        public void PME_has_the_lower_Ze_than_TN_S()
        {
            var root = Load();
            Assert.True(Ze(root, "TN-C-S") < Ze(root, "TN-S"),
                "TN-C-S (PME) must have the lower declared Ze; the two values are swapped.");
        }
    }
}
