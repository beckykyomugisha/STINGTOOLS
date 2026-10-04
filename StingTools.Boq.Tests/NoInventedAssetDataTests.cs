using System;
using System.IO;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// KUT deep review MEP-14. Four writers put values nobody recorded onto assets:
    ///   - Asset Condition: "A - Good" + today on every unassessed asset;
    ///   - Maintenance Schedule: a per-category interval and next-due = today + interval;
    ///   - Sensor Point Mapper: "BMS/{type}/{tag}" in ASS_BMS_ADDRESS_TXT (the real address field);
    ///   - NativeParamMapper (every tagging run): installation date = the tagging day, serial = Mark.
    /// The first source checks are red on main.
    /// </summary>
    public class NoInventedAssetDataTests
    {
        private static string Src(params string[] rel)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, Path.Combine(rel))).Replace("\r\n", "\n");
        }

        private static string Between(string src, string from, string to)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            Assert.True(a >= 0, "not found: " + from);
            int b = src.IndexOf(to, a, StringComparison.Ordinal);
            Assert.True(b > a, "not found: " + to);
            return src.Substring(a, b - a);
        }

        [Fact]
        public void IoTMaintenanceWritesNoDefaults()
        {
            string s = Src("StingTools", "Temp", "IoTMaintenanceCommands.cs");
            Assert.DoesNotContain("\"ASS_CONDITION_TXT\", \"A - Good\"", s);
            string maint = Between(s, "class MaintenanceScheduleCommand", "class DigitalTwinExportCommand");
            Assert.DoesNotContain("DateTime.Now.AddMonths", maint);
            Assert.DoesNotContain("MaintenanceIntervals", maint);
            string sensor = Between(s, "class SensorPointMapperCommand", "\n    }\n");
            Assert.DoesNotMatch(new Regex(@"SetString\([^;]*ASS_BMS_ADDRESS_TXT"), sensor);
        }

        [Fact]
        public void TaggingDoesNotInventAnInstallDateOrSerial()
        {
            string s = Src("StingTools", "Core", "ParameterHelpers.cs");
            string map = Between(s, "public static class NativeParamMapper", "private static int MapBuiltIn");
            Assert.DoesNotMatch(new Regex(@"Set\w*\(el,\s*""ASS_INSTALLATION_DATE_TXT"""), map);
            Assert.DoesNotContain("BuiltInParameter.ALL_MODEL_MARK, \"ASS_SERIAL_NR_TXT\"", map);
        }

        [Theory]
        [InlineData("6", "", 6)]
        [InlineData("", "12 months", 12)]
        [InlineData("", "1 year", 12)]
        [InlineData("", "3", 3)]          // bare interval text is months
        [InlineData("0", "", null)]
        [InlineData("", "", null)]
        [InlineData("", "quarterly", null)]
        public void IntervalComesOnlyFromWhatIsRecorded(string freq, string text, int? expected) =>
            Assert.Equal(expected, ServiceDue.IntervalMonths(freq, text));

        [Fact]
        public void NextDueNeedsALastServiceDate()
        {
            Assert.Equal("", ServiceDue.NextDue("", 6));
            Assert.Equal("", ServiceDue.NextDue("2026-01-15", null));
            Assert.Equal("2026-07-15", ServiceDue.NextDue("2026-01-15", 6));
        }
    }
}
