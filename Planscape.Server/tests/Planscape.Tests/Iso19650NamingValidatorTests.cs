using Planscape.Infrastructure.Validation;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// Phase 147 — exercises the ISO 19650 / UK 2021 NA file-name validator
/// added in Phase 143. Pure-Compute tests so they run without a DB or
/// HTTP context. Catches regressions in the segment splitter + the
/// type / role dictionary lookups.
/// </summary>
public class Iso19650NamingValidatorTests
{
    [Theory]
    [InlineData("PRJ-ABC-ZZ-01-DR-A-Zz_99-0001")]                  // canonical 8-segment
    [InlineData("PRJ-ABC-ZZ-01-DR-A-Zz_99-0001.dwg")]              // with extension
    [InlineData("PRJ-ABC-ZZ-01-DR-A-Zz_99-0001.pdf")]              // case insensitive ext
    [InlineData("PRJ-ABC-ZZ-01-M3-S-Zz_99-0042")]                  // 3D model + structural role
    public void Validate_AcceptsCanonical(string fileName)
    {
        var r = Iso19650NamingValidator.Validate(fileName);
        Assert.True(r.IsValid, r.Joined);
        Assert.Empty(r.Errors);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("PRJ-ABC-ZZ-01", "fields")]                         // too short
    [InlineData("P-ABC-ZZ-01-DR-A", "Project code")]                // 1-char project
    [InlineData("THISPROJECTCODE-ABC-ZZ-01-DR-A", "Project code")]  // > 6 chars
    [InlineData("PRJ-ABC-ZZ-01-XX-A", "document type")]             // unknown type code
    [InlineData("PRJ-ABC-ZZ-01-DR-V", "originator role")]           // unknown role
    [InlineData("PRJ ABC-DEF-ZZ-01-DR-A", "spaces")]                // whitespace in segment
    public void Validate_RejectsMalformed(string fileName, string substring)
    {
        var r = Iso19650NamingValidator.Validate(fileName);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains(substring, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_LongerThanEightSegments_AcceptsWhenWellFormed()
    {
        // 8+ fields are tolerated (the trailing "Number" can carry sub-suffixes
        // on some projects, e.g. -0001-A1).
        var r = Iso19650NamingValidator.Validate("PRJ-ABC-ZZ-01-DR-A-Zz_99-0001-A1");
        Assert.True(r.IsValid, r.Joined);
    }

    [Fact]
    public void Validate_RejectsForbiddenCharsInSequence()
    {
        // 8-segment well-formed up to the sequence which carries a slash.
        var r = Iso19650NamingValidator.Validate("PRJ-ABC-ZZ-01-DR-A-Zz_99-00 01");
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("forbidden", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pattern_IsExposed()
    {
        Assert.False(string.IsNullOrEmpty(Iso19650NamingValidator.ExpectedPattern));
        Assert.Contains("Project-Originator", Iso19650NamingValidator.ExpectedPattern);
    }

    [Fact]
    public void DocumentTypes_ContainsCoreCodes()
    {
        Assert.True(Iso19650NamingValidator.DocumentTypes.ContainsKey("DR"));
        Assert.True(Iso19650NamingValidator.DocumentTypes.ContainsKey("M3"));
        Assert.True(Iso19650NamingValidator.DocumentTypes.ContainsKey("SP"));
    }

    [Fact]
    public void RoleCodes_ContainsCoreRoles()
    {
        Assert.True(Iso19650NamingValidator.RoleCodes.ContainsKey("A"));
        Assert.True(Iso19650NamingValidator.RoleCodes.ContainsKey("M"));
        Assert.True(Iso19650NamingValidator.RoleCodes.ContainsKey("S"));
    }
}
