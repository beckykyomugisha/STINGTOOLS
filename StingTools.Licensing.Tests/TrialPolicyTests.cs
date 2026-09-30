using System;
using StingTools.Core.Licensing;
using Xunit;

public class TrialPolicyTests
{
    private const string Machine = "AAAA-BBBB-CCCC-DDDD-EEEE";
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact] public void New_trial_is_valid_for_90_days()
    {
        var r = TrialPolicy.Evaluate(TrialPolicy.Start(Machine, T0), Machine, T0);
        Assert.Equal(LicenseState.Trial, r.State);
        Assert.True(r.IsValid);
        Assert.Equal(90, r.DaysLeft);
    }

    [Fact] public void Trial_valid_on_day_89_and_expired_on_day_90()
    {
        var s = TrialPolicy.Start(Machine, T0);
        Assert.Equal(LicenseState.Trial, TrialPolicy.Evaluate(s, Machine, T0.AddDays(89)).State);
        Assert.Equal(1, TrialPolicy.Evaluate(s, Machine, T0.AddDays(89)).DaysLeft);
        var end = TrialPolicy.Evaluate(s, Machine, T0.AddDays(90));
        Assert.Equal(LicenseState.TrialExpired, end.State);
        Assert.False(end.IsValid);
    }

    [Fact] public void Winding_the_clock_back_does_not_add_days()
    {
        var s = TrialPolicy.Touch(TrialPolicy.Start(Machine, T0), Machine, T0.AddDays(91));
        Assert.Equal(LicenseState.TrialExpired, TrialPolicy.Evaluate(s, Machine, T0.AddDays(1)).State);
    }

    [Fact] public void Edited_start_date_is_rejected()
    {
        var s = TrialPolicy.Start(Machine, T0);
        s.StartUnix += 60 * 86400L;
        Assert.Equal(LicenseState.TrialExpired, TrialPolicy.Evaluate(s, Machine, T0.AddDays(80)).State);
    }

    [Fact] public void Stamp_copied_from_another_machine_is_rejected()
    {
        var s = TrialPolicy.Start("ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ", T0);
        Assert.False(TrialPolicy.IsSealed(s, Machine));
        Assert.Equal(LicenseState.TrialExpired, TrialPolicy.Evaluate(s, Machine, T0).State);
    }

    [Fact] public void Merge_keeps_earliest_start_and_latest_last_seen()
    {
        var older = TrialPolicy.Start(Machine, T0);
        var newer = TrialPolicy.Touch(TrialPolicy.Start(Machine, T0.AddDays(30)), Machine, T0.AddDays(40));
        var m = TrialPolicy.Merge(newer, older, Machine);
        Assert.Equal(older.StartUnix, m.StartUnix);
        Assert.Equal(newer.LastSeenUnix, m.LastSeenUnix);
        Assert.True(TrialPolicy.IsSealed(m, Machine));
        Assert.Null(TrialPolicy.Merge(null, null, Machine));
    }

    [Fact] public void Missing_stamp_is_not_a_trial()
    {
        Assert.False(TrialPolicy.Evaluate(null, Machine, T0).IsValid);
    }
}
