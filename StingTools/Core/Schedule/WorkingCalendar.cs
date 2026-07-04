// ══════════════════════════════════════════════════════════════════════════
//  WorkingCalendar.cs — Uganda working calendar. PM-4.
//
//  The audit (§2) found UK bank holidays hard-wired into the only calendar
//  (SchedulingCommands GetUKBankHolidays / Computus) AND that calendar feeding
//  only a CSV — AutoGenerateSchedule used raw calendar days and merely nudged
//  weekend end-dates. For an East-Africa (Uganda) deployment that is the wrong
//  holiday set and the wrong working-day model.
//
//  This is the pure working-day engine: weekends + a Uganda public-holiday set
//  (fixed-date + Computus-derived Christian movable feasts; Islamic Eid dates are
//  lunar and supplied per-year via the project override). Working-day counting,
//  add-working-days, and a span's working-day length all derive from it.
//
//  Data-driven: corporate defaults below; project override (extra holidays, e.g.
//  Eid for the year, or a 6-day working week) is layered by the Revit-coupled
//  caller from <project>/_BIM_COORD/working_calendar.json — this pure type takes
//  the merged config as a constructor argument so it stays unit-testable.
//
//  Pure (no Revit / no I/O) — unit-tested in StingTools.Scheduling.Tests.
//  Holiday provenance: Uganda Public Holidays Act (Cap 289).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Schedule
{
    public class WorkingCalendarConfig
    {
        /// <summary>Days of the week that are NON-working. Uganda default: Sat+Sun.</summary>
        public List<DayOfWeek> NonWorkingDays { get; set; } =
            new List<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };

        /// <summary>Extra, project-specific holidays (e.g. lunar Eid dates for the
        /// year, company shutdown days). Merged on top of the statutory set.</summary>
        public List<DateTime> ExtraHolidays { get; set; } = new List<DateTime>();

        /// <summary>Include the statutory Uganda public-holiday set.</summary>
        public bool IncludeUgandaStatutory { get; set; } = true;
    }

    public static class WorkingCalendar
    {
        /// <summary>Statutory Uganda fixed-date + Christian-movable holidays for a
        /// year (Cap 289). Islamic Eid al-Fitr / Eid al-Adha are lunar — supplied
        /// per-year through <see cref="WorkingCalendarConfig.ExtraHolidays"/>.</summary>
        public static List<DateTime> UgandaStatutoryHolidays(int year)
        {
            var h = new List<DateTime>
            {
                new DateTime(year, 1, 1),    // New Year's Day
                new DateTime(year, 1, 26),   // NRM Liberation Day
                new DateTime(year, 2, 16),   // Archbishop Janani Luwum Day
                new DateTime(year, 3, 8),    // International Women's Day
                new DateTime(year, 5, 1),    // Labour Day
                new DateTime(year, 6, 3),    // Martyrs' Day
                new DateTime(year, 6, 9),    // National Heroes' Day
                new DateTime(year, 10, 9),   // Independence Day
                new DateTime(year, 12, 25),  // Christmas Day
                new DateTime(year, 12, 26),  // Boxing Day
            };
            // Christian movable feasts via Computus (Good Friday + Easter Monday).
            DateTime easter = EasterSunday(year);
            h.Add(easter.AddDays(-2));   // Good Friday
            h.Add(easter.AddDays(1));    // Easter Monday
            return h;
        }

        /// <summary>Anonymous Gregorian (Meeus/Jones/Butcher) Computus.</summary>
        public static DateTime EasterSunday(int year)
        {
            int a = year % 19, b = year / 100, c = year % 100;
            int d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
            int hh = (19 * a + b - d - g + 15) % 30;
            int i = c / 4, k = c % 4;
            int l = (32 + 2 * e + 2 * i - hh - k) % 7;
            int m = (a + 11 * hh + 22 * l) / 451;
            int month = (hh + l - 7 * m + 114) / 31;
            int day = ((hh + l - 7 * m + 114) % 31) + 1;
            return new DateTime(year, month, day);
        }

        /// <summary>True when <paramref name="date"/> is a working day under config.</summary>
        public static bool IsWorkingDay(DateTime date, WorkingCalendarConfig cfg)
        {
            cfg ??= new WorkingCalendarConfig();
            if (cfg.NonWorkingDays.Contains(date.DayOfWeek)) return false;
            // A statutory or project-extra holiday is non-working.
            return !HolidaySet(date.Year, cfg).Contains(date.Date);
        }

        private static HashSet<DateTime> HolidaySet(int year, WorkingCalendarConfig cfg)
        {
            var set = new HashSet<DateTime>();
            if (cfg.IncludeUgandaStatutory)
                foreach (var d in UgandaStatutoryHolidays(year)) set.Add(d.Date);
            if (cfg.ExtraHolidays != null)
                foreach (var d in cfg.ExtraHolidays) set.Add(d.Date);
            return set;
        }

        /// <summary>Whole working days in [start, end] inclusive of start, exclusive
        /// of end (duration semantics: a task that starts and finishes the same
        /// working day is 1 working day). Returns ≥ 0.</summary>
        public static int WorkingDaysBetween(DateTime start, DateTime end, WorkingCalendarConfig cfg)
        {
            if (end < start) (start, end) = (end, start);
            cfg ??= new WorkingCalendarConfig();
            int count = 0;
            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
                if (IsWorkingDay(d, cfg)) count++;
            return count;
        }

        /// <summary>Add <paramref name="workingDays"/> working days to a start date,
        /// returning the date that many working days later (start counts as day 1
        /// when it is itself a working day). Used to roll an early-finish into a
        /// calendar finish.</summary>
        public static DateTime AddWorkingDays(DateTime start, int workingDays, WorkingCalendarConfig cfg)
        {
            cfg ??= new WorkingCalendarConfig();
            if (workingDays <= 0) return NextWorkingDay(start, cfg);
            var d = NextWorkingDay(start, cfg);
            int counted = 1;
            while (counted < workingDays)
            {
                d = d.AddDays(1);
                if (IsWorkingDay(d, cfg)) counted++;
            }
            return d;
        }

        public static DateTime NextWorkingDay(DateTime date, WorkingCalendarConfig cfg)
        {
            cfg ??= new WorkingCalendarConfig();
            var d = date.Date;
            while (!IsWorkingDay(d, cfg)) d = d.AddDays(1);
            return d;
        }
    }
}
