using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ═══════════════════════════════════════════════════════════════
    //  SCHEDULING COMMANDS — Phase filtering, weekend calendars,
    //  milestone tracking, and construction sequence management.
    // ═══════════════════════════════════════════════════════════════

    #region Phase Filter Commands

    /// <summary>
    /// Filter and select elements by construction phase.
    /// Supports multi-phase selection for cross-phase analysis.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhaseFilterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();

                if (phases.Count == 0)
                {
                    TaskDialog.Show("Phase Filter", "No phases found in the project.");
                    return Result.Succeeded;
                }

                // Build phase selection dialog
                var dlg = new TaskDialog("Phase Filter");
                dlg.MainInstruction = "Select elements by phase";
                dlg.MainContent = $"Project has {phases.Count} phases:\n" +
                    string.Join("\n", phases.Select((p, i) => $"  {i + 1}. {p.Name}"));

                if (phases.Count >= 1)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        phases[0].Name, $"Select elements created in '{phases[0].Name}'");
                if (phases.Count >= 2)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        phases[1].Name, $"Select elements created in '{phases[1].Name}'");
                if (phases.Count >= 3)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                        "Last Phase: " + phases.Last().Name,
                        $"Select elements created in '{phases.Last().Name}'");

                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var result = dlg.Show();

                Phase targetPhase = null;
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: targetPhase = phases[0]; break;
                    case TaskDialogResult.CommandLink2: targetPhase = phases.Count >= 2 ? phases[1] : null; break;
                    case TaskDialogResult.CommandLink3: targetPhase = phases.Last(); break;
                    default: return Result.Cancelled;
                }

                if (targetPhase == null) return Result.Cancelled;

                // Find elements in target phase
                var matchIds = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        Parameter cp = e.get_Parameter(BuiltInParameter.PHASE_CREATED);
                        return cp != null && cp.HasValue && cp.AsElementId() == targetPhase.Id;
                    })
                    .Select(e => e.Id)
                    .ToList();

                uidoc.Selection.SetElementIds(matchIds);
                TaskDialog.Show("Phase Filter",
                    $"Selected {matchIds.Count} elements in phase '{targetPhase.Name}'.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PhaseFilterCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Phase summary report — elements per phase with discipline breakdown.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhaseSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase)).Cast<Phase>().ToList();

                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .ToList();

                var report = new StringBuilder();
                report.AppendLine("Phase Summary Report");
                report.AppendLine(new string('═', 50));

                foreach (var phase in phases)
                {
                    var phaseElements = allElements.Where(e =>
                    {
                        Parameter cp = e.get_Parameter(BuiltInParameter.PHASE_CREATED);
                        return cp != null && cp.HasValue && cp.AsElementId() == phase.Id;
                    }).ToList();

                    report.AppendLine($"\n{phase.Name}: {phaseElements.Count} elements");

                    var byDisc = phaseElements
                        .Where(e => TagConfig.DiscMap.ContainsKey(e.Category.Name))
                        .GroupBy(e => TagConfig.DiscMap.TryGetValue(e.Category.Name, out string d) ? d : "?")
                        .OrderByDescending(g => g.Count());

                    foreach (var g in byDisc)
                        report.AppendLine($"  {g.Key}: {g.Count()}");
                }

                // Demolished elements
                var demolished = allElements.Where(e =>
                {
                    Parameter dp = e.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                    return dp != null && dp.HasValue
                        && dp.AsElementId() != null
                        && dp.AsElementId() != ElementId.InvalidElementId;
                }).Count();

                if (demolished > 0)
                    report.AppendLine($"\nDemolished: {demolished} elements");

                TaskDialog td = new TaskDialog("Phase Summary");
                td.MainInstruction = $"{phases.Count} phases, {allElements.Count} total elements";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PhaseSummaryCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Construction Milestone Commands

    /// <summary>
    /// Track construction milestones by mapping Revit phases to project dates.
    /// Exports milestone register for programme management.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MilestoneRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase)).Cast<Phase>().ToList();

                string dir = !string.IsNullOrEmpty(doc.PathName)
                    ? Path.GetDirectoryName(doc.PathName)
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string path = Path.Combine(dir, $"STING_Milestones_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("Phase,PhaseOrdinal,ElementCount,Categories,PrimaryDiscipline,MilestoneStatus");

                for (int i = 0; i < phases.Count; i++)
                {
                    var phase = phases[i];
                    var phaseElements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e =>
                        {
                            Parameter cp = e.get_Parameter(BuiltInParameter.PHASE_CREATED);
                            return cp != null && cp.HasValue && cp.AsElementId() == phase.Id;
                        }).ToList();

                    int catCount = phaseElements.Where(e => e.Category != null)
                        .Select(e => e.Category.Name).Distinct().Count();

                    string primaryDisc = "N/A";
                    if (phaseElements.Count > 0)
                    {
                        var discGroup = phaseElements
                            .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                            .GroupBy(e => TagConfig.DiscMap[e.Category.Name])
                            .OrderByDescending(g => g.Count())
                            .FirstOrDefault();
                        if (discGroup != null) primaryDisc = discGroup.Key;
                    }

                    string status = i < phases.Count - 1 ? "Complete" : "In Progress";

                    sb.AppendLine($"\"{Esc(phase.Name)}\",{i + 1},{phaseElements.Count},{catCount},\"{primaryDisc}\",\"{status}\"");
                }

                File.WriteAllText(path, sb.ToString());

                TaskDialog.Show("Milestone Register",
                    $"Exported {phases.Count} milestones to:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MilestoneRegisterCommand failed", ex);
                return Result.Failed;
            }
        }

        private static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    #endregion

    #region Working Calendar

    /// <summary>
    /// Working calendar configuration: mark weekends, bank holidays,
    /// and working hours for accurate 4D scheduling.
    /// Exports calendar data for Navisworks/Synchro import.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorkingCalendarCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string dir = !string.IsNullOrEmpty(doc.PathName)
                    ? Path.GetDirectoryName(doc.PathName)
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string path = Path.Combine(dir, $"STING_WorkingCalendar_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("Date,DayOfWeek,IsWorkingDay,WorkingHours,Notes");

                // Generate 12-month calendar from today
                DateTime start = DateTime.Today;
                DateTime end = start.AddMonths(12);

                // UK bank holidays (approximate for current year)
                var bankHolidays = GetUKBankHolidays(start.Year);
                if (end.Year != start.Year)
                    bankHolidays.AddRange(GetUKBankHolidays(end.Year));

                for (DateTime date = start; date <= end; date = date.AddDays(1))
                {
                    bool isWeekend = date.DayOfWeek == DayOfWeek.Saturday
                                  || date.DayOfWeek == DayOfWeek.Sunday;
                    bool isBankHol = bankHolidays.Contains(date.Date);
                    bool isWorking = !isWeekend && !isBankHol;
                    string hours = isWorking ? "08:00-17:00" : "N/A";
                    string notes = isBankHol ? "Bank Holiday" : isWeekend ? "Weekend" : "";

                    sb.AppendLine($"{date:yyyy-MM-dd},{date.DayOfWeek},{isWorking},{hours},{notes}");
                }

                File.WriteAllText(path, sb.ToString());

                int workingDays = 0;
                for (DateTime d = start; d <= end; d = d.AddDays(1))
                {
                    if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday
                        && !bankHolidays.Contains(d.Date))
                        workingDays++;
                }

                TaskDialog.Show("Working Calendar",
                    $"Calendar exported: {workingDays} working days in next 12 months.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WorkingCalendarCommand failed", ex);
                return Result.Failed;
            }
        }

        private static List<DateTime> GetUKBankHolidays(int year)
        {
            var holidays = new List<DateTime>();
            holidays.Add(new DateTime(year, 1, 1));     // New Year
            holidays.Add(new DateTime(year, 12, 25));    // Christmas
            holidays.Add(new DateTime(year, 12, 26));    // Boxing Day

            // Easter (approximate — Computus algorithm)
            int a = year % 19;
            int b = year / 100;
            int c = year % 100;
            int d = b / 4;
            int e = b % 4;
            int f = (b + 8) / 25;
            int g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4;
            int k = c % 4;
            int l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;
            DateTime easter = new DateTime(year, month, day);

            holidays.Add(easter.AddDays(-2));  // Good Friday
            holidays.Add(easter.AddDays(1));    // Easter Monday

            // May bank holiday (first Monday in May)
            DateTime mayFirst = new DateTime(year, 5, 1);
            while (mayFirst.DayOfWeek != DayOfWeek.Monday) mayFirst = mayFirst.AddDays(1);
            holidays.Add(mayFirst);

            // Spring bank holiday (last Monday in May)
            DateTime mayLast = new DateTime(year, 5, 31);
            while (mayLast.DayOfWeek != DayOfWeek.Monday) mayLast = mayLast.AddDays(-1);
            holidays.Add(mayLast);

            // August bank holiday (last Monday in August)
            DateTime augLast = new DateTime(year, 8, 31);
            while (augLast.DayOfWeek != DayOfWeek.Monday) augLast = augLast.AddDays(-1);
            holidays.Add(augLast);

            return holidays;
        }
    }

    #endregion
}
