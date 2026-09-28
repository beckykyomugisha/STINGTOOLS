// ══════════════════════════════════════════════════════════════════════════
//  EmergencyNameMatcherTests.cs — emergency keywords match whole tokens only.
//
//  The audit's substring list included "e-" and "em-", so "Surface-Mounted"
//  and "System-Panel" were classed as emergency fittings and a room with no
//  emergency cover could read as covered.
//
//  RED / GREEN, recorded 2026-09-24 (old rule = substring list):
//    "Surface-Mounted LED" / "System-Panel 600"   RED   old rule: emergency
//    whole-token matcher                           GREEN not emergency
// ══════════════════════════════════════════════════════════════════════════
using System.Linq;
using StingTools.Core.Electrical;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class EmergencyNameMatcherTests
    {
        [Theory]
        [InlineData("Emergency Bulkhead")]
        [InlineData("LED Panel EM")]
        [InlineData("EM-Downlight 3h")]
        [InlineData("Exit Sign Maintained")]
        [InlineData("Twin Spot_Non-Maintained")]
        [InlineData("Emerg Twinspot")]
        [InlineData("EmergencyLight_LED")]
        [InlineData("ExitSign-Wall")]
        [InlineData("EMBulkhead")]
        [InlineData("NonMaintained Downlight")]
        public void Recognises_emergency_fittings(string family)
        {
            Assert.True(EmergencyNameMatcher.IsEmergencyName(family));
        }

        [Theory]
        [InlineData("Surface-Mounted LED")]
        [InlineData("System-Panel 600")]
        [InlineData("Pendant - Linear")]
        [InlineData("Recessed Downlight")]
        [InlineData("Item-Luminaire")]
        [InlineData("SYSTEM-PANEL")]
        [InlineData("Emerald Pendant")]
        [InlineData("EMBASSY Wall Light")]
        [InlineData("EMPIRE Pendant")]
        [InlineData("EMBOSSED Downlight")]
        [InlineData("")]
        [InlineData(null)]
        public void Does_not_flag_ordinary_fittings(string family)
        {
            Assert.False(EmergencyNameMatcher.IsEmergencyName(family));
        }

        [Fact]
        public void Old_substring_rule_flagged_these_ordinary_fittings()
        {
            // The rule this replaced - kept here so the regression is visible.
            string[] old = { "emergency", "emerg", "exit", "em-", "e-", "maintained", "non-maintained" };
            bool OldRule(string n) => old.Any(p => n.ToLowerInvariant().Contains(p));
            Assert.True(OldRule("Surface-Mounted LED"));
            Assert.True(OldRule("System-Panel 600"));
        }
    }
}
