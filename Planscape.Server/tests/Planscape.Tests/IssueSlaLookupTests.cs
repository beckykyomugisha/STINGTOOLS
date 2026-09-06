using Planscape.API.Controllers;

namespace Planscape.Tests;

/// <summary>
/// Regression guard for #551.
///
/// IssuesController.SLAHours was a Dictionary&lt;string,int&gt; built with the
/// DEFAULT comparer, which is ordinal and case-SENSITIVE. Its keys are upper
/// case ("CRITICAL"), but the priority reaching ComputeSLADeadline is whatever
/// the caller sent — CreateIssue at :323 passes `req.Priority ?? "MEDIUM"`
/// straight through with no normalisation, and nothing on the DTO constrains
/// the casing.
///
/// So a client sending "critical" missed the map entirely and
/// GetValueOrDefault(priority, 168) served the MEDIUM fallback: a 4-hour SLA
/// silently became one week. Nothing logged, nothing failed — the issue simply
/// got a due date six days too late.
///
/// The fix belongs in the CONTAINER, not at the call site. Normalising the
/// input where it is read would leave the next caller free to reintroduce the
/// bug; a case-insensitive comparer makes the container correct for every
/// caller, present and future.
///
/// Note the deliberate asymmetry asserted below: an UNKNOWN priority must still
/// fall back to 168. That fallback is intentional policy for a value we do not
/// recognise. What was wrong was reaching it for a value we DO recognise, only
/// spelled in a different case.
/// </summary>
public class IssueSlaLookupTests
{
    [Theory]
    [InlineData("CRITICAL")]
    [InlineData("Critical")]
    [InlineData("critical")]
    [InlineData("cRiTiCaL")]
    public void Critical_ResolvesTo4Hours_RegardlessOfCase(string priority)
    {
        Assert.Equal(4, IssuesController.SLAHours.GetValueOrDefault(priority, 168));
    }

    /// <remarks>
    /// The "medium" row is a deliberate trap-marker: it PASSED before the fix,
    /// because missing the map lands on the 168 fallback which happens to be
    /// MEDIUM's own value. It proves nothing on its own. Kept so the next
    /// reader can see why the surrounding rows are the ones that matter.
    /// Pre-fix this theory failed on "high" and "low" only.
    /// </remarks>
    [Theory]
    [InlineData("HIGH", 24)]
    [InlineData("high", 24)]
    [InlineData("MEDIUM", 168)]
    [InlineData("medium", 168)]
    [InlineData("LOW", 336)]
    [InlineData("low", 336)]
    public void EveryDefinedPriority_ResolvesTheSame_InEitherCase(string priority, int expectedHours)
    {
        Assert.Equal(expectedHours, IssuesController.SLAHours.GetValueOrDefault(priority, 168));
    }

    /// <summary>
    /// The 168 fallback must remain reachable for genuinely unknown input —
    /// but it has to be reached DELIBERATELY, not as the accidental
    /// consequence of a comparer mismatch. These values are not priorities in
    /// any casing, so the fallback is the correct answer for them.
    /// </summary>
    [Theory]
    [InlineData("URGENT")]
    [InlineData("blocker")]
    [InlineData("")]
    [InlineData("  ")]
    public void UnknownPriority_StillFallsBackTo168_Deliberately(string priority)
    {
        Assert.False(IssuesController.SLAHours.ContainsKey(priority));
        Assert.Equal(168, IssuesController.SLAHours.GetValueOrDefault(priority, 168));
    }

    /// <summary>
    /// Asserts the mechanism directly rather than only its symptom. If someone
    /// later rebuilds this dictionary with a collection initialiser and drops
    /// the comparer, the tests above would still pass for the upper-case rows
    /// and fail only for the lower-case ones — this one names the actual cause.
    /// </summary>
    [Fact]
    public void SlaHours_UsesACaseInsensitiveComparer()
    {
        Assert.Same(StringComparer.OrdinalIgnoreCase, IssuesController.SLAHours.Comparer);
    }

    /// <summary>
    /// Guards the four-row shape. A silently dropped row would show up as an
    /// unexplained one-week SLA, which is the same failure #551 produced.
    /// </summary>
    [Fact]
    public void SlaHours_DefinesExactlyTheFourKnownPriorities()
    {
        Assert.Equal(4, IssuesController.SLAHours.Count);
        Assert.All(new[] { "CRITICAL", "HIGH", "MEDIUM", "LOW" },
            p => Assert.True(IssuesController.SLAHours.ContainsKey(p)));
    }
}
