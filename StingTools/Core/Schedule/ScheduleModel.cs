// ══════════════════════════════════════════════════════════════════════════
//  ScheduleModel.cs — Phase 1 (slice a) of the BOQ 5D Enhanced Rebuild.
//
//  ONE canonical schedule model + ONE store. Until now there were two
//  schedule stores that did not share state:
//    • Cost Manager — <project>/_BIM_COORD/boq_schedule.json (BoqScheduleState:
//      Phases / Periods / Milestones), consumed by the BOQ panel Schedule tab.
//    • BCC 4D/5D    — <project>/STING_BIM_MANAGER/schedule_4d.json (a JObject of
//      tasks with WBS + predecessors + element links), consumed by
//      Scheduling4DEngine.
//  Edit one, the other was stale. This unifies them into a single
//  ScheduleModel persisted at <project>/_BIM_COORD/schedule.json. A one-time
//  importer (ScheduleStore.Migrate) reads either / both legacy files and merges
//  them; subsequent loads read the unified file directly.
//
//  Slice (a) ships the model + store + migration only. The two surfaces are
//  repointed at this store in slice (b); MSP/P6 XML import lands in slice (c).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Schedule
{
    // ── The schedule POCOs (ScheduleModel / ScheduleTask / SchedulePredecessor /
    //    SchedulePeriod / ScheduleMilestone) live in ScheduleModelPocos.cs — a
    //    Revit-free file so the pure engines (ScheduleImporter / ScheduleCpmBridge
    //    / CashFlowSCurve) can be headlessly unit-tested. This file keeps the
    //    Revit-coupled ScheduleStore (it needs Document for the project path). ──

    /// <summary>Single source of truth for the unified schedule. Reads/writes
    /// <c>&lt;project&gt;/_BIM_COORD/schedule.json</c>; migrates the two legacy
    /// stores on first load.</summary>
    public static class ScheduleStore
    {
        public const string FileName = "schedule.json";
        public const string LegacyBoqFileName = "boq_schedule.json";          // _BIM_COORD
        public const string Legacy4dFileName = "schedule_4d.json";            // STING_BIM_MANAGER

        /// <summary>Canonical store path, or null when the project is unsaved.</summary>
        public static string PathFor(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", FileName);
            }
            catch { return null; }
        }

        private static string BimCoordDir(Document doc)
        {
            string parent = Path.GetDirectoryName(doc?.PathName ?? "");
            return string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, "_BIM_COORD");
        }

        /// <summary>Load the unified schedule. If the canonical file is absent,
        /// migrate the legacy stores (writing schedule.json), else return empty.</summary>
        public static ScheduleModel Load(Document doc)
        {
            string path = PathFor(doc);
            if (string.IsNullOrEmpty(path)) return new ScheduleModel();
            try
            {
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<ScheduleModel>(File.ReadAllText(path)) ?? new ScheduleModel();
            }
            catch (Exception ex) { StingLog.Warn($"ScheduleStore.Load: {ex.Message}"); }

            var migrated = Migrate(doc);
            if (migrated != null) { Save(doc, migrated); return migrated; }
            return new ScheduleModel();
        }

        /// <summary>Persist the unified schedule. Returns false on an unsaved project.</summary>
        public static bool Save(Document doc, ScheduleModel model)
        {
            string path = PathFor(doc);
            if (string.IsNullOrEmpty(path) || model == null) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(model, Formatting.Indented));
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"ScheduleStore.Save: {ex.Message}"); return false; }
        }

        // ── 4D-engine JObject bridge ─────────────────────────────────────────
        //  The BCC 4D commands + Scheduling4DEngine speak the legacy
        //  schedule_4d.json JObject shape (tasks[] with start/finish/duration/
        //  predecessors/element_count). These two adapters let those commands
        //  read/write the SINGLE unified store instead of their own file — so
        //  there is one source of truth without rewriting their internals.

        /// <summary>Persist a 4D-engine JObject schedule into the unified store.
        /// Replaces the Tasks list (a regenerate / import) while preserving
        /// Periods / Milestones / actuals. Returns false on an unsaved project.</summary>
        public static bool Save4d(Document doc, JObject schedule4d)
        {
            if (schedule4d == null) return false;
            var model = Load(doc);
            if (string.IsNullOrEmpty(model.ProjectName))
                model.ProjectName = (string)schedule4d["project_name"] ?? model.ProjectName;

            var tasks = new List<ScheduleTask>();
            int nextId = 1;
            foreach (var t in schedule4d["tasks"] as JArray ?? new JArray())
            {
                string name = (string)t["name"] ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                var task = new ScheduleTask
                {
                    Id = (int?)t["task_id"] ?? nextId,
                    MsUid = (string)t["ms_project_uid"] ?? "",
                    Wbs = (string)t["wbs"] ?? "",
                    Name = name,
                    Start = ParseDate(t["start"], DateTime.Today),
                    End = ParseDate(t["finish"] ?? t["end"], DateTime.Today.AddMonths(1)),
                    PercentComplete = (double?)t["percent_complete"] ?? 0,
                    IsSummary = (bool?)t["is_summary"]
                        ?? string.Equals((string)t["category"], "SUMMARY", StringComparison.OrdinalIgnoreCase),
                    OutlineLevel = (int?)t["outline_level"] ?? 0,
                    Category = (string)t["category"] ?? "",
                    Notes = (string)t["notes"] ?? "",
                };
                foreach (var pred in t["predecessors"] as JArray ?? new JArray())
                    task.Predecessors.Add(new SchedulePredecessor
                    {
                        TaskId = (string)(pred["predecessor_uid"] ?? pred["TaskId"]) ?? "",
                        Type = (string)pred["type"] ?? "FS",
                    });
                foreach (var eid in t["element_ids"] as JArray ?? new JArray())
                    if (long.TryParse(eid.ToString(), out long lv)) task.ElementIds.Add(lv);
                tasks.Add(task);
                nextId = Math.Max(nextId, task.Id) + 1;
            }
            model.Tasks = tasks;
            model.Source = schedule4d["source_file"] != null
                ? "import:" + (string)schedule4d["source_file"]
                : "4d-generated";
            model.ImportedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            return Save(doc, model);
        }

        /// <summary>Project the unified model back into the legacy 4D-engine JObject
        /// shape so the existing timeline / export / cash-flow commands +
        /// Scheduling4DEngine.ExportToMSProjectXML keep working off the one store.</summary>
        public static JObject Load4dJObject(Document doc)
        {
            var model = Load(doc);
            var jt = new JArray();
            foreach (var t in model.Tasks)
            {
                var preds = new JArray();
                foreach (var p in t.Predecessors)
                    preds.Add(new JObject { ["predecessor_uid"] = p.TaskId, ["type"] = p.Type });
                jt.Add(new JObject
                {
                    ["task_id"] = t.Id,
                    ["ms_project_uid"] = t.MsUid,
                    ["wbs"] = t.Wbs,
                    ["name"] = t.Name,
                    ["category"] = t.Category,
                    ["start"] = t.Start.ToString("yyyy-MM-dd"),
                    ["finish"] = t.End.ToString("yyyy-MM-dd"),
                    ["duration_days"] = Math.Max(1, (int)(t.End - t.Start).TotalDays),
                    ["element_count"] = t.ElementIds.Count,
                    ["percent_complete"] = t.PercentComplete,
                    ["status"] = t.PercentComplete >= 100 ? "Complete"
                                : t.PercentComplete > 0 ? "In Progress" : "Not Started",
                    ["is_summary"] = t.IsSummary,
                    ["outline_level"] = t.OutlineLevel,
                    ["predecessors"] = preds,
                    ["notes"] = t.Notes,
                });
            }
            DateTime pStart = model.Tasks.Count > 0 ? model.Tasks.Min(t => t.Start) : DateTime.Today;
            DateTime pEnd = model.Tasks.Count > 0 ? model.Tasks.Max(t => t.End) : DateTime.Today;
            return new JObject
            {
                ["project_name"] = model.ProjectName,
                ["project_start"] = pStart.ToString("yyyy-MM-dd"),
                ["project_end"] = pEnd.ToString("yyyy-MM-dd"),
                ["total_tasks"] = model.Tasks.Count,
                ["total_duration_days"] = (int)(pEnd - pStart).TotalDays,
                ["total_duration_weeks"] = Math.Round((pEnd - pStart).TotalDays / 7.0, 1),
                ["tasks"] = jt,
            };
        }

        /// <summary>Idempotent migration trigger for DocumentOpened. Writes
        /// schedule.json only when it is absent AND a legacy store exists; returns
        /// true when it materialised the unified store this call.</summary>
        public static bool EnsureMigrated(Document doc)
        {
            string path = PathFor(doc);
            if (string.IsNullOrEmpty(path) || File.Exists(path)) return false;
            var migrated = Migrate(doc);
            if (migrated == null) return false;
            bool ok = Save(doc, migrated);
            if (ok)
                StingLog.Info($"Schedule unified → _BIM_COORD/{FileName} "
                    + $"({migrated.Tasks.Count} task(s), {migrated.Periods.Count} period(s), "
                    + $"{migrated.Milestones.Count} milestone(s); source {migrated.Source}).");
            return ok;
        }

        /// <summary>One-time importer: read either / both legacy stores and merge.
        /// Returns null when neither legacy file exists.</summary>
        public static ScheduleModel Migrate(Document doc)
        {
            string coordDir = BimCoordDir(doc);
            if (string.IsNullOrEmpty(coordDir)) return null;

            string boqPath = Path.Combine(coordDir, LegacyBoqFileName);
            string fourdPath = SafeBimManagerPath(doc);

            bool haveBoq = File.Exists(boqPath);
            bool haveFourd = !string.IsNullOrEmpty(fourdPath) && File.Exists(fourdPath);
            if (!haveBoq && !haveFourd) return null;

            var model = new ScheduleModel
            {
                ImportedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };
            var sources = new List<string>();

            // ── Cost Manager phases / periods / milestones (the 5D side) ──
            if (haveBoq)
            {
                try
                {
                    var j = JObject.Parse(File.ReadAllText(boqPath));
                    int id = 1;
                    foreach (var p in j["Phases"] as JArray ?? new JArray())
                    {
                        model.Tasks.Add(new ScheduleTask
                        {
                            Id = id++,
                            Name = (string)p["Name"] ?? "",
                            Start = ParseDate(p["Start"], DateTime.Today),
                            End = ParseDate(p["End"], DateTime.Today.AddMonths(1)),
                            PercentComplete = (double?)p["PercentComplete"] ?? 0,
                            Category = "Phase",
                        });
                    }
                    foreach (var pr in j["Periods"] as JArray ?? new JArray())
                        model.Periods.Add(new SchedulePeriod
                        {
                            Date = ParseDate(pr["Date"], DateTime.Today),
                            PercentComplete = (double?)pr["PercentComplete"] ?? 0,
                            Acwp = (double?)pr["Acwp"] ?? 0,
                        });
                    foreach (var ms in j["Milestones"] as JArray ?? new JArray())
                        model.Milestones.Add(new ScheduleMilestone
                        {
                            Name = (string)ms["Name"] ?? "Milestone",
                            Date = ParseDate(ms["Date"], DateTime.Today),
                            Done = (bool?)ms["Done"] ?? false,
                        });
                    model.ActualCostToDate = (double?)j["ActualCostToDate"] ?? 0;
                    model.AsOf = (string)j["AsOf"];
                    sources.Add("boq_schedule");
                }
                catch (Exception ex) { StingLog.Warn($"ScheduleStore.Migrate(boq): {ex.Message}"); }
            }

            // ── BCC 4D tasks (WBS + predecessors + element links) ──
            if (haveFourd)
            {
                try
                {
                    var j = JObject.Parse(File.ReadAllText(fourdPath));
                    if (string.IsNullOrEmpty(model.ProjectName))
                        model.ProjectName = (string)j["project_name"] ?? "";
                    var existingNames = new HashSet<string>(
                        model.Tasks.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
                    int nextId = model.Tasks.Count == 0 ? 1 : model.Tasks.Max(t => t.Id) + 1;
                    foreach (var t in j["tasks"] as JArray ?? new JArray())
                    {
                        string name = (string)t["name"] ?? "";
                        if (string.IsNullOrEmpty(name)) continue;
                        // Don't double-list a phase the boq store already carries.
                        if (existingNames.Contains(name)) continue;

                        var task = new ScheduleTask
                        {
                            Id = (int?)t["task_id"] ?? nextId,
                            MsUid = (string)t["ms_project_uid"] ?? "",
                            Wbs = (string)t["wbs"] ?? "",
                            Name = name,
                            Start = ParseDate(t["start"], DateTime.Today),
                            End = ParseDate(t["finish"], DateTime.Today.AddMonths(1)),
                            PercentComplete = (double?)t["percent_complete"] ?? 0,
                            IsSummary = (bool?)t["is_summary"] ?? false,
                            OutlineLevel = (int?)t["outline_level"] ?? 0,
                            Category = (string)t["category"] ?? "",
                            Notes = (string)t["notes"] ?? "",
                        };
                        foreach (var pred in t["predecessors"] as JArray ?? new JArray())
                            task.Predecessors.Add(new SchedulePredecessor
                            {
                                TaskId = (string)pred["predecessor_uid"] ?? "",
                                Type = (string)pred["type"] ?? "FS",
                            });
                        nextId = Math.Max(nextId, task.Id) + 1;
                        model.Tasks.Add(task);
                        existingNames.Add(name);
                    }
                    sources.Add("schedule_4d");
                }
                catch (Exception ex) { StingLog.Warn($"ScheduleStore.Migrate(4d): {ex.Message}"); }
            }

            model.Source = "migrated:" + string.Join("+", sources);
            return model;
        }

        /// <summary>Resolve STING_BIM_MANAGER/schedule_4d.json without pulling in
        /// the BIMManager engine — the dir layout is &lt;projectDir&gt;/STING_BIM_MANAGER.</summary>
        private static string SafeBimManagerPath(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                // path-discipline: legacy-fallback -- reads the PRE-consolidation location
                // on purpose, to find a 4D schedule written before the move.
                return Path.Combine(parent, "STING_BIM_MANAGER", Legacy4dFileName);
            }
            catch { return null; }
        }

        private static DateTime ParseDate(JToken tok, DateTime fallback)
        {
            string s = tok?.ToString();
            if (!string.IsNullOrEmpty(s)
                && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            return fallback;
        }
    }
}
