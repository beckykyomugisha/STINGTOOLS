using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// N+12 — Enrich material-takeoff schedules with cost / carbon / EPD
    /// columns from the corporate material library.
    ///
    /// Revit's ViewSchedule.CreateMaterialTakeoff ships with the standard
    /// Material-* fields (Name, Class, Area, Volume, Cost). It does NOT
    /// carry the STING parameters we add via the MAT library:
    ///   STING_EMB_CARBON_NR   — kgCO₂e/m³
    ///   STING_MAT_EPD_SRC_TXT — EPD provenance
    ///   STING_MAT_EPD_DATE_TXT — EPD age
    ///
    /// This helper walks every existing Material Takeoff schedule and
    /// appends those fields when they exist on the Material category but
    /// aren't already in the schedule. Idempotent — re-running is a
    /// no-op once the columns are in place.
    /// </summary>
    public class MaterialScheduleEnrichResult
    {
        public int SchedulesScanned { get; set; }
        public int SchedulesEnriched { get; set; }
        public int FieldsAdded { get; set; }
        public List<string> SkippedReasons { get; } = new List<string>();
    }

    public static class MaterialScheduleEnricher
    {
        // Field names to inject. Order matters — they'll be appended in
        // this order at the end of the schedule's field list.
        private static readonly string[] EnrichmentFields = new[]
        {
            "STING_EMB_CARBON_NR",
            "STING_MAT_EPD_SRC_TXT",
            "STING_MAT_EPD_DATE_TXT",
        };

        public static MaterialScheduleEnrichResult Run(Document doc)
        {
            var result = new MaterialScheduleEnrichResult();
            if (doc == null) return result;
            try
            {
                var schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => s.Definition?.IsMaterialTakeoff == true)
                    .ToList();
                if (schedules.Count == 0) return result;

                using (var t = new Transaction(doc, "STING Enrich Material Schedules"))
                {
                    t.Start();
                    foreach (var sched in schedules)
                    {
                        result.SchedulesScanned++;
                        try
                        {
                            int added = EnrichSchedule(doc, sched);
                            if (added > 0)
                            {
                                result.SchedulesEnriched++;
                                result.FieldsAdded += added;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.SkippedReasons.Add($"{sched.Name}: {ex.Message}");
                            StingLog.Warn($"MaterialScheduleEnricher '{sched.Name}': {ex.Message}");
                        }
                    }
                    t.Commit();
                }
                StingLog.Info($"MaterialScheduleEnricher: {result.SchedulesEnriched}/{result.SchedulesScanned} schedules enriched, {result.FieldsAdded} fields added.");
            }
            catch (Exception ex) { StingLog.Error("MaterialScheduleEnricher.Run", ex); }
            return result;
        }

        private static int EnrichSchedule(Document doc, ViewSchedule sched)
        {
            var def = sched.Definition;
            if (def == null) return 0;

            // Build the set of field ids already in the schedule so we
            // don't double-add.
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                var f = def.GetField(i);
                if (f != null) existing.Add(f.GetName() ?? "");
            }

            // Resolve every available schedulable field once.
            var available = def.GetSchedulableFields();

            int added = 0;
            foreach (var name in EnrichmentFields)
            {
                if (existing.Contains(name)) continue;
                var schedField = available.FirstOrDefault(sf =>
                {
                    try { return string.Equals(sf.GetName(doc), name, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                });
                if (schedField == null) continue; // param not bound to the Material category in this project
                try
                {
                    def.AddField(schedField);
                    added++;
                }
                catch (Exception ex) { StingLog.Warn($"AddField '{name}' to '{sched.Name}': {ex.Message}"); }
            }
            return added;
        }
    }
}
