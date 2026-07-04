using System;
using System.Collections.Generic;
using StingTools.Core.Schedule;
using Xunit;

namespace StingTools.Scheduling.Tests
{
    /// <summary>PM-4 — Uganda working calendar (not UK bank holidays).</summary>
    public class WorkingCalendarTests
    {
        private readonly WorkingCalendarConfig _cfg = new WorkingCalendarConfig();

        [Fact]
        public void Weekend_IsNonWorking()
        {
            // 2026-02-07 is a Saturday, 2026-02-08 a Sunday.
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 2, 7), _cfg));
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 2, 8), _cfg));
            Assert.True(WorkingCalendar.IsWorkingDay(new DateTime(2026, 2, 9), _cfg)); // Monday
        }

        [Fact]
        public void UgandaStatutory_NotUk()
        {
            // 9 Oct = Uganda Independence Day (non-working); UK has no holiday then.
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 10, 9), _cfg));
            // 3 June = Uganda Martyrs' Day.
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 6, 3), _cfg));
            // UK's late-August bank holiday is a normal working day in Uganda.
            Assert.True(WorkingCalendar.IsWorkingDay(new DateTime(2026, 8, 31), _cfg));
        }

        [Fact]
        public void Computus_GoodFridayAndEasterMonday_AreHolidays()
        {
            // Easter Sunday 2026 = 2026-04-05; Good Friday 4/3, Easter Monday 4/6.
            Assert.Equal(new DateTime(2026, 4, 5), WorkingCalendar.EasterSunday(2026));
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 4, 3), _cfg));
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 4, 6), _cfg));
        }

        [Fact]
        public void ProjectOverride_ExtraHoliday_IsNonWorking()
        {
            var cfg = new WorkingCalendarConfig
            {
                ExtraHolidays = new List<DateTime> { new DateTime(2026, 3, 20) }  // e.g. Eid for the year
            };
            Assert.False(WorkingCalendar.IsWorkingDay(new DateTime(2026, 3, 20), cfg));
        }

        [Fact]
        public void WorkingDaysBetween_ExcludesWeekendsAndHolidays()
        {
            // Mon 2026-02-02 → Fri 2026-02-13 = 10 working days (two full weeks).
            int d = WorkingCalendar.WorkingDaysBetween(
                new DateTime(2026, 2, 2), new DateTime(2026, 2, 13), _cfg);
            Assert.Equal(10, d);
        }

        [Fact]
        public void AddWorkingDays_RollsOverWeekend()
        {
            // Start Fri 2026-02-06; +1 working day = same day (day 1), +2 = Mon 2/9.
            Assert.Equal(new DateTime(2026, 2, 6),
                WorkingCalendar.AddWorkingDays(new DateTime(2026, 2, 6), 1, _cfg));
            Assert.Equal(new DateTime(2026, 2, 9),
                WorkingCalendar.AddWorkingDays(new DateTime(2026, 2, 6), 2, _cfg));
        }
    }
}
