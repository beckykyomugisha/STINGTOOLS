using StingBIM.Shared.Helpers;
using Xunit;

namespace StingBIM.Tests;

/// <summary>
/// Tests for compliance calculation logic that mirrors the server-side computation.
/// </summary>
public class ComplianceCalculationTests
{
    [Theory]
    [InlineData(100, 80,  80.0)]
    [InlineData(100, 0,   0.0)]
    [InlineData(0,   0,   0.0)]   // no elements → 0% (not divide-by-zero)
    [InlineData(1,   1,   100.0)]
    [InlineData(200, 150, 75.0)]
    public void CompliancePercent_IsComputedCorrectly(int total, int tagged, double expectedPct)
    {
        double pct = total > 0 ? (double)tagged / total * 100.0 : 0.0;
        Assert.Equal(expectedPct, pct, precision: 1);
    }

    [Fact]
    public void RagStatus_FullyCompliantProject_IsGreen()
    {
        // 120 / 150 = 80% → GREEN threshold
        double pct = (double)120 / 150 * 100;
        Assert.Equal("GREEN", TagFormatHelper.GetRagStatus(pct));
    }

    [Fact]
    public void RagStatus_PartialProject_IsAmber()
    {
        // 75 / 150 = 50% → AMBER threshold
        double pct = (double)75 / 150 * 100;
        Assert.Equal("AMBER", TagFormatHelper.GetRagStatus(pct));
    }

    [Fact]
    public void RagStatus_NewProject_IsRed()
    {
        // 0 / 150 = 0% → RED
        double pct = (double)0 / 150 * 100;
        Assert.Equal("RED", TagFormatHelper.GetRagStatus(pct));
    }
}
