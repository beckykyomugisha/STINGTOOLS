using StingBIM.Shared.Helpers;
using Xunit;

namespace StingBIM.Tests;

/// <summary>
/// Unit tests for TagFormatHelper — the ISO 19650 tag validation and parsing utilities.
/// These run in CI without any Revit or database dependencies.
/// </summary>
public class TagFormatHelperTests
{
    // ── IsValidTag ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("M-BLD1-Z01-L02-HVAC-SUP-FCU-0001", true)]
    [InlineData("E-EXT-ZZ-L03-LV-LTG-DWL-0042",   true)]
    [InlineData("FP-BLD1-Z02-B01-FP-SUP-SPK-0015", true)]
    public void IsValidTag_ValidTags_ReturnsTrue(string tag, bool expected)
    {
        Assert.Equal(expected, TagFormatHelper.IsValidTag(tag, "-"));
    }

    [Theory]
    [InlineData("",                            false)] // empty
    [InlineData("M-BLD1-Z01-L02-HVAC-SUP-FCU",false)] // only 7 segments
    [InlineData("INVALID",                     false)] // no separator
    public void IsValidTag_InvalidTags_ReturnsFalse(string tag, bool expected)
    {
        Assert.Equal(expected, TagFormatHelper.IsValidTag(tag, "-"));
    }

    [Fact]
    public void IsValidTag_Null_ReturnsFalse()
    {
        Assert.False(TagFormatHelper.IsValidTag(null!, "-"));
    }

    // ── IsFullyResolved ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("M-BLD1-Z01-L02-HVAC-SUP-FCU-0001", true)]   // all real values
    [InlineData("M-XX-Z01-L02-HVAC-SUP-FCU-0001",   false)]  // XX in location
    [InlineData("M-BLD1-ZZ-L02-HVAC-SUP-FCU-0001",  false)]  // ZZ in zone
    [InlineData("M-BLD1-Z01-L02-HVAC-GEN-FCU-0001", false)]  // GEN placeholder
    [InlineData("M-BLD1-Z01-L02-HVAC-SUP-FCU-0000", false)]  // 0000 seq
    public void IsFullyResolved_ReturnsCorrectResult(string tag, bool expected)
    {
        Assert.Equal(expected, TagFormatHelper.IsFullyResolved(tag, "-"));
    }

    // ── GetRagStatus ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100.0, "GREEN")]
    [InlineData(80.0,  "GREEN")]
    [InlineData(79.9,  "AMBER")]
    [InlineData(50.0,  "AMBER")]
    [InlineData(49.9,  "RED")]
    [InlineData(0.0,   "RED")]
    public void GetRagStatus_Thresholds_AreCorrect(double percent, string expectedRag)
    {
        Assert.Equal(expectedRag, TagFormatHelper.GetRagStatus(percent));
    }

    // ── ParseTag ──────────────────────────────────────────────────────────────

    [Fact]
    public void ParseTag_ValidTag_ReturnsCorrectSegments()
    {
        var result = TagFormatHelper.ParseTag("M-BLD1-Z01-L02-HVAC-SUP-FCU-0001", "-");

        Assert.NotNull(result);
        Assert.Equal("M",    result.Value.disc);
        Assert.Equal("BLD1", result.Value.loc);
        Assert.Equal("Z01",  result.Value.zone);
        Assert.Equal("L02",  result.Value.lvl);
        Assert.Equal("HVAC", result.Value.sys);
        Assert.Equal("SUP",  result.Value.func);
        Assert.Equal("FCU",  result.Value.prod);
        Assert.Equal("0001", result.Value.seq);
    }

    [Fact]
    public void ParseTag_InvalidTag_ReturnsNull()
    {
        var result = TagFormatHelper.ParseTag("NOT-A-VALID-TAG", "-");
        Assert.Null(result);
    }

    [Fact]
    public void ParseTag_CustomSeparator_Works()
    {
        var result = TagFormatHelper.ParseTag("M.BLD1.Z01.L02.HVAC.SUP.FCU.0001", ".");
        Assert.NotNull(result);
        Assert.Equal("M",    result.Value.disc);
        Assert.Equal("0001", result.Value.seq);
    }
}
