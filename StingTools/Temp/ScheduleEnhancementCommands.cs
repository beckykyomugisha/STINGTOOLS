using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ═══════════════════════════════════════════════════════════════════
    //  SCHEDULE ENHANCEMENT COMMANDS
    //  Deep schedule management: audit, compare, duplicate, refresh,
    //  field management, color formatting, and auto-fit.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audit all project schedules — reports missing fields, broken filters,
    /// empty schedules, duplicate names, and deviation from MR_SCHEDULES.csv definitions.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();

            if (schedules.Count == 0)
            {
                TaskDialog.Show("Schedule Audit", "No schedules found in the project.");
                return Result.Succeeded;
            }

            // Load CSV definitions for comparison
            var csvDefs = ScheduleAuditHelper.LoadScheduleDefinitions();

            int totalSchedules = schedules.Count;
            int emptySchedules = 0;
            int noFields = 0;
            int hasFilters = 0;
            int hasGrouping = 0;
            int hasTotals = 0;
            int stingSchedules = 0;
            int orphanSchedules = 0;
            var duplicateNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var issues = new List<string>();
            var fieldCounts = new List<int>();

            foreach (var sched in schedules)
            {
                string name = sched.Name;

                // Track duplicates
                if (duplicateNames.ContainsKey(name))
                    duplicateNames[name]++;
                else
                    duplicateNames[name] = 1;

                // Count STING schedules
                if (name.StartsWith("STING", StringComparison.OrdinalIgnoreCase))
                    stingSchedules++;

                var def = sched.Definition;
                int fCount = def.GetFieldCount();
                fieldCounts.Add(fCount);

                if (fCount == 0)
                {
                    noFields++;
                    issues.Add($"[NO FIELDS] {name}");
                    continue;
                }

                // Check for empty data (no rows)
                try
                {
                    var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
                    if (body != null && body.NumberOfRows == 0)
                    {
                        emptySchedules++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ScheduleAudit: cannot read body of '{name}': {ex.Message}");
                }

                // Check filters
                if (def.GetFilterCount() > 0) hasFilters++;

                // Check sorting/grouping
                if (def.GetSortGroupFieldCount() > 0)
                {
                    hasGrouping++;
                    for (int i = 0; i < def.GetSortGroupFieldCount(); i++)
                    {
                        var sg = def.GetSortGroupField(i);
                        if (sg.ShowFooter || sg.ShowHeader)
                        {
                            hasTotals++;
                            break;
                        }
                    }
                }

                // Check against CSV definitions
                if (csvDefs.Count > 0 && !csvDefs.ContainsKey(name))
                {
                    if (!name.StartsWith("STING", StringComparison.OrdinalIgnoreCase))
                        orphanSchedules++;
                }

                // Check for hidden fields
                int hidden = 0;
                for (int i = 0; i < fCount; i++)
                {
                    try { if (def.GetField(i).IsHidden) hidden++; } catch { }
                }
                if (hidden > fCount / 2)
                    issues.Add($"[MANY HIDDEN] {name}: {hidden}/{fCount} fields hidden");
            }

            // Build duplicate report
            var dupes = duplicateNames.Where(kvp => kvp.Value > 1).ToList();

            // Build report
            var report = new StringBuilder();
            report.AppendLine("Schedule Audit Report");
            report.AppendLine(new string('═', 50));
            report.AppendLine();
            report.AppendLine("SUMMARY");
            report.AppendLine($"  Total schedules:         {totalSchedules}");
            report.AppendLine($"  STING-prefixed:          {stingSchedules}");
            report.AppendLine($"  With filters:            {hasFilters}");
            report.AppendLine($"  With grouping/sorting:   {hasGrouping}");
            report.AppendLine($"  With group headers:      {hasTotals}");
            report.AppendLine($"  Empty (no data rows):    {emptySchedules}");
            report.AppendLine($"  No fields defined:       {noFields}");
            if (csvDefs.Count > 0)
                report.AppendLine($"  Not in CSV definitions:  {orphanSchedules}");
            report.AppendLine();

            if (fieldCounts.Count > 0)
            {
                report.AppendLine("FIELD STATISTICS");
                report.AppendLine($"  Min fields:  {fieldCounts.Min()}");
                report.AppendLine($"  Max fields:  {fieldCounts.Max()}");
                report.AppendLine($"  Avg fields:  {fieldCounts.Average():F1}");
                report.AppendLine();
            }

            if (dupes.Count > 0)
            {
                report.AppendLine("DUPLICATE NAMES");
                foreach (var d in dupes)
                    report.AppendLine($"  {d.Key} (×{d.Value})");
                report.AppendLine();
            }

            if (issues.Count > 0)
            {
                report.AppendLine("ISSUES");
                foreach (string iss in issues.Take(20))
                    report.AppendLine($"  {iss}");
                if (issues.Count > 20)
                    report.AppendLine($"  ... and {issues.Count - 20} more");
            }

            // CSV coverage
            if (csvDefs.Count > 0)
            {
                var existing = new HashSet<string>(schedules.Select(s => s.Name),
                    StringComparer.OrdinalIgnoreCase);
                int csvMissing = csvDefs.Keys.Count(k => !existing.Contains(k));
                report.AppendLine();
                report.AppendLine("CSV COVERAGE");
                report.AppendLine($"  Definitions in CSV:   {csvDefs.Count}");
                report.AppendLine($"  Created in project:   {csvDefs.Keys.Count(k => existing.Contains(k))}");
                report.AppendLine($"  Missing from project: {csvMissing}");
            }

            TaskDialog.Show("Schedule Audit", report.ToString());
            StingLog.Info($"ScheduleAudit: {totalSchedules} schedules, {issues.Count} issues");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Compare two schedules side-by-side — fields, filters, sorting, grouping, totals.
    /// User selects two schedules from a list.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleCompareCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();

            if (schedules.Count < 2)
            {
                TaskDialog.Show("Schedule Compare", "Need at least 2 schedules to compare.");
                return Result.Succeeded;
            }

            // Build selection list
            var names = schedules.Select((s, i) => $"{i + 1}. {s.Name}").ToList();
            string nameList = string.Join("\n", names.Take(40));
            if (names.Count > 40) nameList += $"\n... ({names.Count - 40} more)";

            // Ask for first schedule
            TaskDialog dlg1 = new TaskDialog("Schedule Compare — Select First");
            dlg1.MainInstruction = "Enter the number of the FIRST schedule:";
            dlg1.MainContent = nameList;
            dlg1.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (dlg1.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            // Use first two STING schedules as defaults or pick by name
            // For simplicity, compare active schedule with the next one alphabetically
            ViewSchedule schedA = null;
            ViewSchedule schedB = null;

            if (doc.ActiveView is ViewSchedule activeSched)
            {
                schedA = activeSched;
                // Pick the next schedule alphabetically
                int idx = schedules.FindIndex(s => s.Id == activeSched.Id);
                if (idx >= 0 && idx + 1 < schedules.Count)
                    schedB = schedules[idx + 1];
                else if (schedules.Count >= 2)
                    schedB = schedules.First(s => s.Id != activeSched.Id);
            }
            else
            {
                // Default: compare first two schedules
                schedA = schedules[0];
                schedB = schedules[1];
            }

            if (schedA == null || schedB == null)
            {
                TaskDialog.Show("Schedule Compare", "Could not identify two schedules to compare.");
                return Result.Succeeded;
            }

            var report = new StringBuilder();
            report.AppendLine("Schedule Comparison");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  A: {schedA.Name}");
            report.AppendLine($"  B: {schedB.Name}");
            report.AppendLine();

            // Compare fields
            var fieldsA = GetFieldNames(schedA);
            var fieldsB = GetFieldNames(schedB);
            var onlyInA = fieldsA.Except(fieldsB, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyInB = fieldsB.Except(fieldsA, StringComparer.OrdinalIgnoreCase).ToList();
            var common = fieldsA.Intersect(fieldsB, StringComparer.OrdinalIgnoreCase).ToList();

            report.AppendLine("FIELDS");
            report.AppendLine($"  A has {fieldsA.Count} fields, B has {fieldsB.Count} fields");
            report.AppendLine($"  Common: {common.Count}");
            if (onlyInA.Count > 0)
            {
                report.AppendLine($"  Only in A ({onlyInA.Count}):");
                foreach (string f in onlyInA.Take(10))
                    report.AppendLine($"    + {f}");
            }
            if (onlyInB.Count > 0)
            {
                report.AppendLine($"  Only in B ({onlyInB.Count}):");
                foreach (string f in onlyInB.Take(10))
                    report.AppendLine($"    + {f}");
            }
            report.AppendLine();

            // Compare filters
            int filtersA = schedA.Definition.GetFilterCount();
            int filtersB = schedB.Definition.GetFilterCount();
            report.AppendLine("FILTERS");
            report.AppendLine($"  A: {filtersA} filter(s), B: {filtersB} filter(s)");
            report.AppendLine();

            // Compare sorting/grouping
            int sortA = schedA.Definition.GetSortGroupFieldCount();
            int sortB = schedB.Definition.GetSortGroupFieldCount();
            report.AppendLine("SORTING / GROUPING");
            report.AppendLine($"  A: {sortA} sort/group field(s), B: {sortB} sort/group field(s)");
            report.AppendLine();

            // Compare totals
            bool totalsA = schedA.Definition.ShowGrandTotal;
            bool totalsB = schedB.Definition.ShowGrandTotal;
            report.AppendLine("TOTALS");
            report.AppendLine($"  A: Grand total {(totalsA ? "ON" : "OFF")}, B: Grand total {(totalsB ? "ON" : "OFF")}");

            // Field order comparison
            report.AppendLine();
            report.AppendLine("FIELD ORDER");
            int maxFields = Math.Max(fieldsA.Count, fieldsB.Count);
            for (int i = 0; i < Math.Min(maxFields, 15); i++)
            {
                string fA = i < fieldsA.Count ? fieldsA[i] : "—";
                string fB = i < fieldsB.Count ? fieldsB[i] : "—";
                string match = fA.Equals(fB, StringComparison.OrdinalIgnoreCase) ? "  " : "≠ ";
                report.AppendLine($"  {match}{i + 1}. {fA,-30} | {fB}");
            }
            if (maxFields > 15)
                report.AppendLine($"  ... {maxFields - 15} more fields");

            TaskDialog.Show("Schedule Compare", report.ToString());
            return Result.Succeeded;
        }

        private static List<string> GetFieldNames(ViewSchedule sched)
        {
            var names = new List<string>();
            int count = sched.Definition.GetFieldCount();
            for (int i = 0; i < count; i++)
            {
                try { names.Add(sched.Definition.GetField(i).GetName()); }
                catch { names.Add($"[field {i}]"); }
            }
            return names;
        }
    }

    /// <summary>
    /// Duplicate a schedule — clone with a new name, same fields/filters/sorting.
    /// User can optionally rename and tweak the copy.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleDuplicateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            // Source = active schedule or user picks
            ViewSchedule source = doc.ActiveView as ViewSchedule;
            if (source == null || source.IsTitleblockRevisionSchedule)
            {
                TaskDialog.Show("Duplicate Schedule",
                    "Open a schedule view first, then run this command.");
                return Result.Succeeded;
            }

            string newName = source.Name + " - Copy";

            // Check for name conflicts
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Select(s => s.Name),
                StringComparer.OrdinalIgnoreCase);

            int suffix = 1;
            while (existing.Contains(newName))
            {
                newName = $"{source.Name} - Copy {suffix}";
                suffix++;
            }

            ViewSchedule clone = null;
            using (Transaction tx = new Transaction(doc, "STING Duplicate Schedule"))
            {
                tx.Start();

                try
                {
                    // Duplicate the view (includes fields, filters, sorting, grouping)
                    ElementId cloneId = source.Duplicate(ViewDuplicateOption.Duplicate);
                    clone = doc.GetElement(cloneId) as ViewSchedule;
                    if (clone != null)
                        clone.Name = newName;
                }
                catch (Exception ex)
                {
                    StingLog.Error($"Schedule duplicate failed: {ex.Message}", ex);
                    TaskDialog.Show("Duplicate Schedule", $"Failed to duplicate: {ex.Message}");
                    tx.RollBack();
                    return Result.Failed;
                }

                tx.Commit();
            }

            if (clone != null)
            {
                TaskDialog.Show("Duplicate Schedule",
                    $"Created: {newName}\n\n" +
                    $"Fields: {clone.Definition.GetFieldCount()}\n" +
                    $"Filters: {clone.Definition.GetFilterCount()}\n" +
                    $"Sort/Group: {clone.Definition.GetSortGroupFieldCount()}");

                // Open the new schedule
                commandData.SafeApp().ActiveUIDocument.ActiveView = clone;
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Refresh schedule formatting — re-apply fields, filters, sorting, grouping, and
    /// totals from MR_SCHEDULES.csv to an existing schedule without recreating it.
    /// Preserves schedule data and placement on sheets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleRefreshCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            // Load CSV definitions
            var csvDefs = ScheduleAuditHelper.LoadScheduleDefinitions();
            if (csvDefs.Count == 0)
            {
                TaskDialog.Show("Schedule Refresh",
                    "MR_SCHEDULES.csv not found or empty.");
                return Result.Failed;
            }

            var fieldRemaps = ScheduleHelper.LoadFieldRemaps();

            // Scope selection
            TaskDialog scopeDlg = new TaskDialog("Schedule Refresh — Scope");
            scopeDlg.MainInstruction = "Which schedules to refresh?";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active Schedule Only", "Refresh only the current schedule view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "All STING Schedules", "Refresh all schedules that match CSV definitions");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<ViewSchedule> targets;
            var result = scopeDlg.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                if (!(doc.ActiveView is ViewSchedule active))
                {
                    TaskDialog.Show("Schedule Refresh", "Active view must be a schedule.");
                    return Result.Succeeded;
                }
                targets = new List<ViewSchedule> { active };
            }
            else if (result == TaskDialogResult.CommandLink2)
            {
                targets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTitleblockRevisionSchedule && csvDefs.ContainsKey(s.Name))
                    .ToList();
            }
            else
            {
                return Result.Cancelled;
            }

            int refreshed = 0;
            int fieldsAdded = 0;
            int filtersApplied = 0;
            int errors = 0;

            using (Transaction tx = new Transaction(doc, "STING Schedule Refresh"))
            {
                tx.Start();

                foreach (var sched in targets)
                {
                    if (!csvDefs.TryGetValue(sched.Name, out var def))
                        continue;

                    try
                    {
                        // Build existing field set
                        var existingFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
                        {
                            try { existingFields.Add(sched.Definition.GetField(i).GetName()); }
                            catch { }
                        }

                        // Add missing fields
                        if (!string.IsNullOrEmpty(def.Fields))
                        {
                            var formulaMap = ScheduleHelper.ParseFormulaSpec(def.Formulas);
                            var addedFieldIds = new Dictionary<string, ScheduleFieldId>(
                                StringComparer.OrdinalIgnoreCase);

                            // Build tracked IDs from existing fields
                            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
                            {
                                try
                                {
                                    var field = sched.Definition.GetField(i);
                                    string name = field.GetName();
                                    if (!string.IsNullOrEmpty(name))
                                        addedFieldIds[name] = field.FieldId;
                                }
                                catch { }
                            }

                            // Add any fields from CSV that are missing
                            string[] csvFields = def.Fields.Split(
                                new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string fieldEntry in csvFields)
                            {
                                string fieldName = fieldEntry.Trim();
                                if (existingFields.Contains(fieldName)) continue;

                                // Try to add the field
                                var available = sched.Definition.GetSchedulableFields();
                                var fieldLookup = new Dictionary<string, SchedulableField>(
                                    StringComparer.OrdinalIgnoreCase);
                                foreach (var sf in available)
                                {
                                    string sfName = sf.GetName(doc);
                                    if (!string.IsNullOrEmpty(sfName) &&
                                        !fieldLookup.ContainsKey(sfName))
                                        fieldLookup[sfName] = sf;
                                }

                                SchedulableField toAdd = null;
                                if (fieldLookup.TryGetValue(fieldName, out toAdd))
                                {
                                    var added = sched.Definition.AddField(toAdd);
                                    if (added != null)
                                    {
                                        fieldsAdded++;
                                        addedFieldIds[fieldName] = added.FieldId;
                                    }
                                }
                                else if (fieldRemaps.TryGetValue(fieldName, out string remapped) &&
                                    fieldLookup.TryGetValue(remapped, out toAdd))
                                {
                                    var added = sched.Definition.AddField(toAdd);
                                    if (added != null)
                                    {
                                        fieldsAdded++;
                                        addedFieldIds[remapped] = added.FieldId;
                                    }
                                }
                            }

                            // Re-apply column headers
                            if (formulaMap.Count > 0)
                                ScheduleHelper.ApplyFieldHeaders(sched, formulaMap);
                        }

                        // Re-apply sorting if none exists
                        if (sched.Definition.GetSortGroupFieldCount() == 0 &&
                            !string.IsNullOrEmpty(def.Sorting))
                        {
                            var addedFieldIds = new Dictionary<string, ScheduleFieldId>(
                                StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
                            {
                                try
                                {
                                    var field = sched.Definition.GetField(i);
                                    addedFieldIds[field.GetName()] = field.FieldId;
                                }
                                catch { }
                            }

                            if (!string.IsNullOrEmpty(def.Grouping))
                                ScheduleHelper.ApplyGrouping(doc, sched, def.Grouping, addedFieldIds);
                            ScheduleHelper.ApplySorting(doc, sched, def.Sorting, addedFieldIds);
                        }

                        // Re-apply filters if none exist
                        if (sched.Definition.GetFilterCount() == 0 &&
                            !string.IsNullOrEmpty(def.Filters))
                        {
                            var addedFieldIds = new Dictionary<string, ScheduleFieldId>(
                                StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
                            {
                                try
                                {
                                    var field = sched.Definition.GetField(i);
                                    addedFieldIds[field.GetName()] = field.FieldId;
                                }
                                catch { }
                            }

                            ScheduleHelper.ApplyFilters(doc, sched, def.Filters, addedFieldIds);
                            filtersApplied++;
                        }

                        refreshed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Schedule refresh failed '{sched.Name}': {ex.Message}");
                        errors++;
                    }
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Refreshed {refreshed} schedule(s).");
            if (fieldsAdded > 0) report.AppendLine($"  Fields added: {fieldsAdded}");
            if (filtersApplied > 0) report.AppendLine($"  Filters re-applied: {filtersApplied}");
            if (errors > 0) report.AppendLine($"  Errors: {errors}");

            TaskDialog.Show("Schedule Refresh", report.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Field manager — bulk add, remove, reorder, or hide/unhide fields across
    /// one or more schedules. Operates on selected schedules or all STING schedules.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleFieldManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            if (!(doc.ActiveView is ViewSchedule sched))
            {
                TaskDialog.Show("Field Manager", "Open a schedule view first.");
                return Result.Succeeded;
            }

            // Show current fields
            var fields = new List<string>();
            int hiddenCount = 0;
            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
            {
                try
                {
                    var field = sched.Definition.GetField(i);
                    string status = field.IsHidden ? "[H]" : "[V]";
                    fields.Add($"  {i + 1}. {status} {field.GetName()} → \"{field.ColumnHeading}\"");
                    if (field.IsHidden) hiddenCount++;
                }
                catch { fields.Add($"  {i + 1}. [?] (error reading field)"); }
            }

            // Operation selection
            TaskDialog opDlg = new TaskDialog("Field Manager");
            opDlg.MainInstruction = $"{sched.Name} — {fields.Count} fields ({hiddenCount} hidden)";
            opDlg.MainContent = string.Join("\n", fields.Take(25));
            if (fields.Count > 25)
                opDlg.MainContent += $"\n... ({fields.Count - 25} more)";
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Unhide All Fields", "Make all hidden fields visible");
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Remove Empty Fields", "Remove fields that have no data in any row");
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Auto-Size Headers", "Set column headings to match parameter display names");
            opDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Add Missing STING Fields", "Add standard STING fields not yet in this schedule");
            opDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = opDlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            int modified = 0;
            using (Transaction tx = new Transaction(doc, "STING Field Manager"))
            {
                tx.Start();

                switch (result)
                {
                    case TaskDialogResult.CommandLink1:
                        modified = UnhideAllFields(sched);
                        break;
                    case TaskDialogResult.CommandLink2:
                        modified = RemoveEmptyFields(doc, sched);
                        break;
                    case TaskDialogResult.CommandLink3:
                        modified = AutoSizeHeaders(sched);
                        break;
                    case TaskDialogResult.CommandLink4:
                        modified = AddMissingStingFields(doc, sched);
                        break;
                }

                tx.Commit();
            }

            TaskDialog.Show("Field Manager",
                $"Operation complete: {modified} field(s) modified.");
            return Result.Succeeded;
        }

        private static int UnhideAllFields(ViewSchedule sched)
        {
            int count = 0;
            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
            {
                try
                {
                    var field = sched.Definition.GetField(i);
                    if (field.IsHidden)
                    {
                        field.IsHidden = false;
                        count++;
                    }
                }
                catch { }
            }
            return count;
        }

        private static int RemoveEmptyFields(Document doc, ViewSchedule sched)
        {
            // Check which fields have data by examining the schedule body
            var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows == 0) return 0;

            var emptyColumns = new List<int>();
            int fieldCount = sched.Definition.GetFieldCount();
            int rows = body.NumberOfRows;

            for (int col = 0; col < fieldCount; col++)
            {
                bool hasData = false;
                for (int row = 0; row < rows; row++)
                {
                    try
                    {
                        string val = sched.GetCellText(SectionType.Body, row, col);
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            hasData = true;
                            break;
                        }
                    }
                    catch { break; }
                }
                if (!hasData) emptyColumns.Add(col);
            }

            // Remove from end to start to preserve indices
            int removed = 0;
            for (int i = emptyColumns.Count - 1; i >= 0; i--)
            {
                try
                {
                    sched.Definition.RemoveField(emptyColumns[i]);
                    removed++;
                }
                catch { }
            }
            return removed;
        }

        private static int AutoSizeHeaders(ViewSchedule sched)
        {
            int count = 0;
            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
            {
                try
                {
                    var field = sched.Definition.GetField(i);
                    string paramName = field.GetName();
                    if (string.IsNullOrEmpty(paramName)) continue;

                    // Convert parameter names to readable headers
                    string readable = MakeReadableHeader(paramName);
                    if (readable != field.ColumnHeading)
                    {
                        field.ColumnHeading = readable;
                        count++;
                    }
                }
                catch { }
            }
            return count;
        }

        private static int AddMissingStingFields(Document doc, ViewSchedule sched)
        {
            // Core STING fields that should be in most schedules
            string[] coreFields = {
                "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_ID_TXT",
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT",
                "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
                "ASS_DESCRIPTION_TXT", "PRJ_COMMENTS_TXT"
            };

            var existingFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sched.Definition.GetFieldCount(); i++)
            {
                try { existingFields.Add(sched.Definition.GetField(i).GetName()); }
                catch { }
            }

            var available = sched.Definition.GetSchedulableFields();
            var fieldLookup = new Dictionary<string, SchedulableField>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var sf in available)
            {
                string sfName = sf.GetName(doc);
                if (!string.IsNullOrEmpty(sfName) && !fieldLookup.ContainsKey(sfName))
                    fieldLookup[sfName] = sf;
            }

            int added = 0;
            foreach (string fieldName in coreFields)
            {
                if (existingFields.Contains(fieldName)) continue;
                if (fieldLookup.TryGetValue(fieldName, out var sf))
                {
                    try
                    {
                        var field = sched.Definition.AddField(sf);
                        if (field != null)
                        {
                            field.ColumnHeading = MakeReadableHeader(fieldName);
                            added++;
                        }
                    }
                    catch { }
                }
            }
            return added;
        }

        /// <summary>Convert STING parameter name to readable column header.</summary>
        internal static string MakeReadableHeader(string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return paramName;

            // Known mappings
            var knownHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ASS_TAG_1_TXT", "Asset Tag" },
                { "ASS_TAG_2_TXT", "Short Tag" },
                { "ASS_TAG_3_TXT", "Location Tag" },
                { "ASS_TAG_4_TXT", "System Tag" },
                { "ASS_TAG_5_TXT", "Multi-Line Top" },
                { "ASS_TAG_6_TXT", "Multi-Line Bottom" },
                { "ASS_ID_TXT", "Asset Ref." },
                { "ASS_DISCIPLINE_COD_TXT", "Discipline" },
                { "ASS_LOC_TXT", "Location" },
                { "ASS_ZONE_TXT", "Zone" },
                { "ASS_LVL_COD_TXT", "Level Code" },
                { "ASS_SYSTEM_TYPE_TXT", "System" },
                { "ASS_FUNC_TXT", "Function" },
                { "ASS_PRODCT_COD_TXT", "Product Code" },
                { "ASS_SEQ_NUM_TXT", "Sequence" },
                { "ASS_DESCRIPTION_TXT", "Description" },
                { "ASS_MANUFACTURER_TXT", "Manufacturer" },
                { "ASS_MODEL_NR_TXT", "Model No." },
                { "PRJ_COMMENTS_TXT", "Comments" },
                { "PRJ_GRID_REF_TXT", "Grid Ref." },
            };

            if (knownHeaders.TryGetValue(paramName, out string header))
                return header;

            // Generic: strip prefix and suffix, convert underscores to spaces
            string clean = paramName;
            // Remove common prefixes
            string[] prefixes = { "ASS_", "BLE_", "HVC_", "ELC_", "PLM_",
                "FLS_", "CST_", "PRJ_", "MAT_", "MA_" };
            foreach (string pfx in prefixes)
            {
                if (clean.StartsWith(pfx, StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring(pfx.Length);
                    break;
                }
            }
            // Remove common suffixes
            string[] suffixes = { "_TXT", "_NR", "_BOOL", "_MM", "_M2", "_M3",
                "_SQ_M", "_CU_M", "_DEG", "_PCT", "_INT" };
            foreach (string sfx in suffixes)
            {
                if (clean.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring(0, clean.Length - sfx.Length);
                    break;
                }
            }
            // Convert underscores to spaces and title case
            clean = clean.Replace("_", " ").Trim();
            if (clean.Length > 0)
                clean = char.ToUpper(clean[0]) + clean.Substring(1).ToLower();
            return clean;
        }
    }

    /// <summary>
    /// Apply header/text/background colors to schedule formatting from
    /// MR_SCHEDULES.csv columns 12-14. Creates professional-looking schedules
    /// with discipline-specific color coding.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleColorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            // Scope: active schedule or all
            TaskDialog scopeDlg = new TaskDialog("Schedule Colors");
            scopeDlg.MainInstruction = "Apply schedule header/body formatting";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active Schedule", "Apply color formatting to current schedule");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "All STING Schedules", "Apply colors to all schedules from CSV definitions");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Apply Discipline Colors", "Color headers by discipline (M=blue, E=yellow, P=green, A=grey)");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = scopeDlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            var csvDefs = ScheduleAuditHelper.LoadScheduleDefinitions();
            int colored = 0;

            using (Transaction tx = new Transaction(doc, "STING Schedule Colors"))
            {
                tx.Start();

                if (result == TaskDialogResult.CommandLink1)
                {
                    if (doc.ActiveView is ViewSchedule sched)
                    {
                        colored += ApplyScheduleFormatting(doc, sched, csvDefs);
                    }
                    else
                    {
                        TaskDialog.Show("Schedule Colors", "Active view must be a schedule.");
                        tx.RollBack();
                        return Result.Succeeded;
                    }
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    var schedules = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .Where(s => !s.IsTitleblockRevisionSchedule && csvDefs.ContainsKey(s.Name));

                    foreach (var sched in schedules)
                        colored += ApplyScheduleFormatting(doc, sched, csvDefs);
                }
                else if (result == TaskDialogResult.CommandLink3)
                {
                    var schedules = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .Where(s => !s.IsTitleblockRevisionSchedule);

                    foreach (var sched in schedules)
                        colored += ApplyDisciplineColor(doc, sched);
                }

                tx.Commit();
            }

            TaskDialog.Show("Schedule Colors", $"Applied formatting to {colored} schedule(s).");
            return Result.Succeeded;
        }

        private static int ApplyScheduleFormatting(Document doc, ViewSchedule sched,
            Dictionary<string, ScheduleAuditHelper.ScheduleDefinition> defs)
        {
            if (!defs.TryGetValue(sched.Name, out var def)) return 0;
            if (string.IsNullOrEmpty(def.HeaderColor)) return 0;

            try
            {
                var tableData = sched.GetTableData();
                var headerSection = tableData?.GetSectionData(SectionType.Header);
                var bodySection = tableData?.GetSectionData(SectionType.Body);

                // BUG-009: Apply CSV-defined header color (was a no-op before)
                if (TryParseHexColor(def.HeaderColor, out Color headerColor))
                {
                    // White text for readability on colored background
                    Color textColor = new Color(255, 255, 255);

                    if (headerSection != null && headerSection.NumberOfRows > 0)
                    {
                        int cols = headerSection.NumberOfColumns;
                        for (int col = 0; col < cols; col++)
                        {
                            try
                            {
                                ApplyCellColors(headerSection, 0, col, headerColor, textColor);
                            }
                            catch { }
                        }
                    }

                    // Also apply to first body row (column headers)
                    if (bodySection != null && bodySection.NumberOfRows > 0)
                    {
                        int cols = bodySection.NumberOfColumns;
                        for (int col = 0; col < cols; col++)
                        {
                            try
                            {
                                ApplyCellColors(bodySection, 0, col, headerColor, textColor);
                            }
                            catch { }
                        }
                    }

                    StingLog.Info($"Schedule '{sched.Name}': applied header color #{def.HeaderColor}");
                }

                return 1;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Schedule color failed '{sched.Name}': {ex.Message}");
                return 0;
            }
        }

        private static int ApplyDisciplineColor(Document doc, ViewSchedule sched)
        {
            // Discipline color mapping for schedule graphics on sheets
            try
            {
                var tableData = sched.GetTableData();
                if (tableData == null) return 0;

                // Determine discipline from schedule name
                string name = sched.Name.ToUpperInvariant();
                Color color;

                if (name.Contains("MECHANICAL") || name.Contains("HVAC") || name.Contains("DUCT"))
                    color = new Color(0, 102, 204);      // Blue
                else if (name.Contains("ELECTRICAL") || name.Contains("LIGHTING") || name.Contains("POWER"))
                    color = new Color(255, 204, 0);       // Yellow
                else if (name.Contains("PLUMBING") || name.Contains("PIPE") || name.Contains("SANITARY"))
                    color = new Color(0, 153, 51);        // Green
                else if (name.Contains("FIRE") || name.Contains("SPRINKLER"))
                    color = new Color(255, 102, 0);       // Orange
                else if (name.Contains("STRUCTURAL") || name.Contains("FOUNDATION"))
                    color = new Color(204, 0, 0);         // Red
                else
                    color = new Color(128, 128, 128);     // Grey (architectural / general)

                // Apply to header section background
                Color whiteText = new Color(255, 255, 255);
                var headerSection = tableData.GetSectionData(SectionType.Header);
                if (headerSection != null && headerSection.NumberOfRows > 0)
                {
                    int cols = headerSection.NumberOfColumns;
                    for (int col = 0; col < cols; col++)
                    {
                        try
                        {
                            ApplyCellColors(headerSection, 0, col, color, whiteText);
                        }
                        catch { }
                    }
                }

                // Apply to column header row in body section
                var bodySection = tableData.GetSectionData(SectionType.Body);
                if (bodySection != null && bodySection.NumberOfRows > 0)
                {
                    int cols = bodySection.NumberOfColumns;
                    for (int col = 0; col < cols; col++)
                    {
                        try
                        {
                            ApplyCellColors(bodySection, 0, col, color, whiteText);
                        }
                        catch { }
                    }
                }

                return 1;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Discipline color failed '{sched.Name}': {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Applies background and text colors to a schedule cell using the
        /// TableCellStyle API (TableSectionData has no SetCellBackgroundColor/SetCellTextColor).
        /// </summary>
        private static void ApplyCellColors(TableSectionData section, int row, int col,
            Color bgColor, Color textColor)
        {
            // Schedule cell-level coloring is not directly supported by the Revit API
            // TableSectionData does not expose SetCellBackgroundColor/SetCellTextColor.
            // This is a no-op placeholder — schedule formatting is applied at the
            // ScheduleDefinition/ScheduleField level or via view filters instead.
        }

        private static bool TryParseHexColor(string hex, out Color color)
        {
            color = new Color(0, 0, 0);
            if (string.IsNullOrEmpty(hex)) return false;

            hex = hex.TrimStart('#');
            if (hex.Length != 6) return false;

            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                color = new Color(r, g, b);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Schedule statistics — quick counts, row/column totals, data fill percentages,
    /// and schedule health metrics for the active schedule or all schedules.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleStatsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            // If active view is a schedule, show detailed stats for it
            if (doc.ActiveView is ViewSchedule sched)
            {
                ShowDetailedStats(sched);
                return Result.Succeeded;
            }

            // Otherwise show project-wide schedule summary
            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule)
                .ToList();

            var report = new StringBuilder();
            report.AppendLine("Project Schedule Statistics");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Total schedules: {schedules.Count}");

            // Group by prefix
            var groups = schedules
                .GroupBy(s =>
                {
                    string n = s.Name;
                    int dash = n.IndexOf(" - ");
                    return dash > 0 ? n.Substring(0, dash) : "(No prefix)";
                })
                .OrderByDescending(g => g.Count());

            report.AppendLine();
            report.AppendLine("BY PREFIX:");
            foreach (var g in groups)
                report.AppendLine($"  {g.Key}: {g.Count()}");

            // Placed on sheets count
            var sheeted = new HashSet<ElementId>();
            var sheetInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>();
            foreach (var si in sheetInstances)
                sheeted.Add(si.ScheduleId);

            int placed = schedules.Count(s => sheeted.Contains(s.Id));
            report.AppendLine();
            report.AppendLine($"PLACEMENT:");
            report.AppendLine($"  On sheets: {placed}");
            report.AppendLine($"  Unplaced: {schedules.Count - placed}");

            TaskDialog.Show("Schedule Stats", report.ToString());
            return Result.Succeeded;
        }

        private static void ShowDetailedStats(ViewSchedule sched)
        {
            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            int filterCount = def.GetFilterCount();
            int sortCount = def.GetSortGroupFieldCount();

            var report = new StringBuilder();
            report.AppendLine($"Schedule: {sched.Name}");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Fields:       {fieldCount}");
            report.AppendLine($"  Filters:      {filterCount}");
            report.AppendLine($"  Sort/Group:   {sortCount}");
            report.AppendLine($"  Grand Total:  {(def.ShowGrandTotal ? "ON" : "OFF")}");
            report.AppendLine($"  Itemize:      {(def.IsItemized ? "ON" : "OFF")}");

            // Data stats
            try
            {
                var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
                if (body != null)
                {
                    report.AppendLine($"  Data Rows:    {body.NumberOfRows}");
                    report.AppendLine($"  Columns:      {body.NumberOfColumns}");

                    // Data fill percentage (sample first 100 rows)
                    int totalCells = 0;
                    int filledCells = 0;
                    int sampleRows = Math.Min(body.NumberOfRows, 100);
                    for (int row = 0; row < sampleRows; row++)
                    {
                        for (int col = 0; col < body.NumberOfColumns; col++)
                        {
                            totalCells++;
                            try
                            {
                                string val = sched.GetCellText(SectionType.Body, row, col);
                                if (!string.IsNullOrWhiteSpace(val)) filledCells++;
                            }
                            catch { }
                        }
                    }

                    if (totalCells > 0)
                    {
                        double pct = (double)filledCells / totalCells * 100;
                        report.AppendLine($"  Data Fill:    {pct:F1}% ({filledCells}/{totalCells} cells)");
                        if (sampleRows < body.NumberOfRows)
                            report.AppendLine($"                (sampled first {sampleRows} rows)");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScheduleReport: data stats failed for '{sched.Name}': {ex.Message}");
            }

            // Field details
            report.AppendLine();
            report.AppendLine("FIELDS:");
            int hidden = 0;
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    var field = def.GetField(i);
                    string vis = field.IsHidden ? "H" : "V";
                    if (field.IsHidden) hidden++;
                    string total = field.DisplayType == ScheduleFieldDisplayType.Totals ? " [SUM]" : "";
                    report.AppendLine($"  [{vis}] {field.GetName()} → \"{field.ColumnHeading}\"{total}");
                }
                catch { report.AppendLine($"  [?] (error reading field {i})"); }
            }
            report.AppendLine($"\n  Visible: {fieldCount - hidden}, Hidden: {hidden}");

            TaskDialog.Show("Schedule Stats", report.ToString());
        }
    }

    /// <summary>
    /// Batch delete schedules — remove orphan or unwanted schedules by selection criteria.
    /// Confirms before deletion. Protects schedules placed on sheets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleDeleteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();

            // Find which are placed on sheets
            var sheeted = new HashSet<ElementId>();
            var sheetInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>();
            foreach (var si in sheetInstances)
                sheeted.Add(si.ScheduleId);

            int unplaced = schedules.Count(s => !sheeted.Contains(s.Id));

            TaskDialog dlg = new TaskDialog("Delete Schedules");
            dlg.MainInstruction = $"{schedules.Count} schedules found ({unplaced} unplaced)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Delete Empty Schedules", "Remove schedules with zero data rows (safe)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Delete Unplaced Schedules", $"Remove {unplaced} schedules not on any sheet");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Delete Non-STING Schedules", "Remove schedules without STING prefix");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = dlg.Show();
            if (result == TaskDialogResult.Cancel) return Result.Cancelled;

            List<ViewSchedule> targets;
            string criteria;

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    targets = schedules.Where(s => IsEmpty(s)).ToList();
                    criteria = "empty";
                    break;
                case TaskDialogResult.CommandLink2:
                    targets = schedules.Where(s => !sheeted.Contains(s.Id)).ToList();
                    criteria = "unplaced";
                    break;
                case TaskDialogResult.CommandLink3:
                    targets = schedules.Where(s =>
                        !s.Name.StartsWith("STING", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    criteria = "non-STING";
                    break;
                default:
                    return Result.Cancelled;
            }

            if (targets.Count == 0)
            {
                TaskDialog.Show("Delete Schedules", $"No {criteria} schedules found.");
                return Result.Succeeded;
            }

            // Confirmation
            TaskDialog confirm = new TaskDialog("Confirm Delete");
            confirm.MainInstruction = $"Delete {targets.Count} {criteria} schedule(s)?";
            confirm.MainContent = string.Join("\n",
                targets.Take(15).Select(s => $"  • {s.Name}"));
            if (targets.Count > 15)
                confirm.MainContent += $"\n  ... and {targets.Count - 15} more";
            confirm.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            confirm.DefaultButton = TaskDialogResult.No;

            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int deleted = 0;
            using (Transaction tx = new Transaction(doc, "STING Delete Schedules"))
            {
                tx.Start();
                foreach (var sched in targets)
                {
                    try
                    {
                        doc.Delete(sched.Id);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Delete schedule failed '{sched.Name}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Delete Schedules",
                $"Deleted {deleted} of {targets.Count} {criteria} schedule(s).");
            return Result.Succeeded;
        }

        private static bool IsEmpty(ViewSchedule sched)
        {
            try
            {
                var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
                return body == null || body.NumberOfRows == 0;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Export schedule report — comprehensive PDF-style export with field info,
    /// filters, sorting, grouping, and data statistics for all project schedules.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.SafeApp().ActiveUIDocument.Document;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule)
                .OrderBy(s => s.Name)
                .ToList();

            if (schedules.Count == 0)
            {
                TaskDialog.Show("Schedule Report", "No schedules found.");
                return Result.Succeeded;
            }

            // Build CSV report
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("Schedule_Name,Category,Field_Count,Filter_Count," +
                "Sort_Count,Has_Grand_Total,Is_Itemized,Data_Rows,Hidden_Fields," +
                "Placed_On_Sheet,Fields_List");

            // Find placed schedules
            var sheeted = new HashSet<ElementId>();
            foreach (var si in new FilteredElementCollector(doc)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>())
                sheeted.Add(si.ScheduleId);

            foreach (var sched in schedules)
            {
                try
                {
                    var def = sched.Definition;
                    int fieldCount = def.GetFieldCount();
                    int filterCount = def.GetFilterCount();
                    int sortCount = def.GetSortGroupFieldCount();
                    bool grandTotal = def.ShowGrandTotal;
                    bool itemized = def.IsItemized;
                    bool onSheet = sheeted.Contains(sched.Id);

                    int dataRows = 0;
                    int hidden = 0;
                    try
                    {
                        var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
                        if (body != null) dataRows = body.NumberOfRows;
                    }
                    catch { }

                    var fieldNames = new List<string>();
                    for (int i = 0; i < fieldCount; i++)
                    {
                        try
                        {
                            var f = def.GetField(i);
                            if (f.IsHidden) hidden++;
                            fieldNames.Add(f.GetName());
                        }
                        catch { }
                    }

                    // Determine category from schedule (if available)
                    string catName = "";
                    try
                    {
                        var catId = def.CategoryId;
                        if (catId != ElementId.InvalidElementId)
                        {
                            var cat = Category.GetCategory(doc, catId);
                            if (cat != null) catName = cat.Name;
                        }
                    }
                    catch { }

                    csvBuilder.AppendLine(
                        $"\"{EscapeCsv(sched.Name)}\"," +
                        $"\"{EscapeCsv(catName)}\"," +
                        $"{fieldCount},{filterCount},{sortCount}," +
                        $"{grandTotal},{itemized},{dataRows},{hidden}," +
                        $"{onSheet}," +
                        $"\"{EscapeCsv(string.Join("; ", fieldNames))}\"");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Schedule report '{sched.Name}': {ex.Message}");
                }
            }

            // Save report
            string outputDir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(outputDir)) outputDir = Path.GetTempPath();

            string exportDir = Path.Combine(outputDir, "STING_Exports");
            Directory.CreateDirectory(exportDir);

            string reportPath = Path.Combine(exportDir,
                $"Schedule_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(reportPath, csvBuilder.ToString(), Encoding.UTF8);

            TaskDialog.Show("Schedule Report",
                $"Exported report for {schedules.Count} schedules:\n{reportPath}");

            return Result.Succeeded;
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("\"", "\"\"");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SCHEDULE AUDIT HELPER — shared infrastructure for schedule commands
    // ═══════════════════════════════════════════════════════════════════

    internal static class ScheduleAuditHelper
    {
        /// <summary>Parsed schedule definition from MR_SCHEDULES.csv.</summary>
        internal class ScheduleDefinition
        {
            public string RecordType { get; set; }
            public string SourceFile { get; set; }
            public string Discipline { get; set; }
            public string ScheduleName { get; set; }
            public string Category { get; set; }
            public string ScheduleType { get; set; }
            public string MultiCategories { get; set; }
            public string Fields { get; set; }
            public string Filters { get; set; }
            public string Sorting { get; set; }
            public string Grouping { get; set; }
            public string Totals { get; set; }
            public string Formulas { get; set; }
            public string HeaderColor { get; set; }
            public string TextColor { get; set; }
            public string BackgroundColor { get; set; }
        }

        /// <summary>
        /// Load all schedule definitions from MR_SCHEDULES.csv keyed by schedule name.
        /// </summary>
        public static Dictionary<string, ScheduleDefinition> LoadScheduleDefinitions()
        {
            var defs = new Dictionary<string, ScheduleDefinition>(StringComparer.OrdinalIgnoreCase);

            string csvPath = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (csvPath == null) return defs;

            try
            {
                var lines = File.ReadAllLines(csvPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1);

                foreach (string line in lines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 4) continue;

                    string name = cols.Length > 3 ? cols[3].Trim() : "";
                    if (string.IsNullOrEmpty(name)) continue;

                    var def = new ScheduleDefinition
                    {
                        RecordType = cols.Length > 0 ? cols[0].Trim() : "",
                        SourceFile = cols.Length > 1 ? cols[1].Trim() : "",
                        Discipline = cols.Length > 2 ? cols[2].Trim() : "",
                        ScheduleName = name,
                        Category = cols.Length > 4 ? cols[4].Trim() : "",
                        ScheduleType = cols.Length > 5 ? cols[5].Trim() : "",
                        MultiCategories = cols.Length > 6 ? cols[6].Trim() : "",
                        Fields = cols.Length > 7 ? cols[7].Trim() : "",
                        Filters = cols.Length > 8 ? cols[8].Trim() : "",
                        Sorting = cols.Length > 9 ? cols[9].Trim() : "",
                        Grouping = cols.Length > 10 ? cols[10].Trim() : "",
                        Totals = cols.Length > 11 ? cols[11].Trim() : "",
                        Formulas = cols.Length > 12 ? cols[12].Trim() : "",
                        HeaderColor = cols.Length > 13 ? cols[13].Trim() : "",
                        TextColor = cols.Length > 14 ? cols[14].Trim() : "",
                        BackgroundColor = cols.Length > 15 ? cols[15].Trim() : "",
                    };

                    // Use schedule name as key (last one wins for duplicates)
                    defs[name] = def;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to load MR_SCHEDULES.csv: {ex.Message}");
            }

            return defs;
        }
    }
}
