using StingTools.Core.Licensing;
using Xunit;

public class FingerprintComposerTests
{
    [Fact] public void Compute_is_deterministic()
    {
        var a = FingerprintComposer.Compute(new[] { "GUID-1", "CPU-1", "BB-1", "BIOS-1" });
        var b = FingerprintComposer.Compute(new[] { "guid-1", " CPU-1 ", "BB-1", "BIOS-1" });
        Assert.Equal(a, b); // normalization makes these identical
    }

    [Fact] public void Compute_changes_when_any_factor_changes()
    {
        var a = FingerprintComposer.Compute(new[] { "G", "C", "B", "I" });
        var c = FingerprintComposer.Compute(new[] { "G", "C", "B", "X" });
        Assert.NotEqual(a, c);
    }

    [Fact] public void Compute_format_is_five_groups_of_four()
    {
        var code = FingerprintComposer.Compute(new[] { "G", "C", "B", "I" });
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", code);
    }

    [Fact] public void Empty_and_oem_junk_count_as_NA()
    {
        Assert.Equal(0, FingerprintComposer.RealFactorCount(new[] { "", "  ", "To be filled by O.E.M.", "Default string" }));
        Assert.Equal(1, FingerprintComposer.RealFactorCount(new[] { "REAL", "None", "0", "" }));
    }
}
