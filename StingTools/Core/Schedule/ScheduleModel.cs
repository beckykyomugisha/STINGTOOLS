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
    /// <summary>The single canonical 4D/5D schedule for a project. Persisted to
    /// <c>&lt;project&gt;/_BIM_COORD/schedule.json</c>. Cost Manager owns the 5D
    /// view; BCC reads the same model.</summary>
    public class ScheduleModel
    {
        /// <summary>Schema version — lets future migrations upgrade in place.</summary>
        public int Version { get; set; } = 1;
        public string ProjectName { get; set; } = "";
        /// <summary>Provenance — e.g. "migrated:boq_schedule+schedule_4d",
        /// "msproject", "p6", "manual".</summary>
        public string Source { get; set; } = "";
        public string ImportedDate { get; set; } = "";
        /// <summary>EVM "as of" date (yyyy-MM-dd) carried from the legacy store.</summary>
        public string AsOf { get; set; }
        /// <summary>Cumulative actual cost to date (ACWP driver).</summary>
        public double ActualCostToDate { get; set; }

        /// <summary>Phases / tasks — the unified work breakdown. A Cost Manager
        /// "phase" and a 4D "task" are both a <see cref="ScheduleTask"/>.</summary>
        public List<ScheduleTask> Tasks { get; set; } = new List<ScheduleTask>();
        /// <summary>Reporting periods (the PV/EV/AC timeline).</summary>
        public List<SchedulePeriod> Periods { get; set; } = new List<SchedulePeriod>();
        /// <summary>Dated programme milestones plotted on the S-curve.</summary>
        public List<ScheduleMilestone> Milestones { get; set; } = new List<ScheduleMilestone>();
    }

    /// <summary>One phase / task. Carries everything both legacy models needed:
    /// id, WBS, dates, % complete, predecessors, cost-load, linked element ids,
    /// milestone / summary flags.</summary>
    public class ScheduleTask : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private string _name = "";
        private DateTime _start = DateTime.Today;
        private DateTime _end = DateTime.Today.AddMonths(1);
        private double _pct;
        private double _costLoad;

        /// <summary>Stable task id within the schedule.</summary>
        public int Id { get; set; }
        /// <summary>MS Project / P6 UID, when imported from a programme.</summary>
        public string MsUid { get; set; } = "";
        /// <summary>Work breakdown structure code (e.g. "1.2.3").</summary>
        public string Wbs { get; set; } = "";
        public string Name { get => _name; set { _name = value ?? ""; N(nameof(Name)); } }
        public DateTime Start { get => _start; set { _start = value; N(nameof(Start)); N(nameof(StartStr)); } }
        public DateTime End { get => _end; set { _end = value; N(nameof(End)); N(nameof(EndStr)); } }
        public double PercentComplete { get => _pct; set { _pct = Math.Max(0, Math.Min(100, value)); N(nameof(PercentComplete)); } }
        /// <summary>Cost loaded onto this task (the 5D driver). 0 ⇒ derive from
        /// BAC × duration share at compute time.</summary>
        public double CostLoadUGX { get => _costLoad; set { _costLoad = Math.Max(0, value); N(nameof(CostLoadUGX)); } }

        public List<SchedulePredecessor> Predecessors { get; set; } = new List<SchedulePredecessor>();
        /// <summary>Revit element ids assigned to this task (the 4D link).</summary>
        public List<long> ElementIds { get; set; } = new List<long>();

        public bool IsMilestone { get; set; }
        public bool IsSummary { get; set; }
        public int OutlineLevel { get; set; }
        public string Category { get; set; } = "";
        public string Notes { get; set; } = "";

        [JsonIgnore]
        public string StartStr
        {
            get => _start.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Start = d; }
        }
        [JsonIgnore]
        public string EndStr
        {
            get => _end.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) End = d; }
        }
        /// <summary>Computed (BAC × duration share) when CostLoadUGX is 0; not
        /// persisted as authoritative.</summary>
        [JsonIgnore]
        public double PlannedCost { get; set; }
    }

    /// <summary>A finish-to-start (etc.) dependency on another task.</summary>
    public class SchedulePredecessor
    {
        /// <summary>Id (or MS/P6 UID) of the predecessor task.</summary>
        public string TaskId { get; set; } = "";
        /// <summary>FS / SS / FF / SF.</summary>
        public string Type { get; set; } = "FS";
    }

    /// <summary>One reporting period: overall % complete (EV driver) + cumulative
    /// actual cost (AC) at the period end. PV is derived from the cost-loaded
    /// baseline.</summary>
    public class SchedulePeriod : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        private DateTime _date = DateTime.Today;
        private double _pct;
        private double _acwp;

        public DateTime Date { get => _date; set { _date = value; N(nameof(Date)); N(nameof(DateStr)); } }
        [JsonIgnore]
        public string DateStr
        {
            get => _date.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Date = d; }
        }
        public double PercentComplete { get => _pct; set { _pct = Math.Max(0, Math.Min(100, value)); N(nameof(PercentComplete)); } }
        public double Acwp { get => _acwp; set { _acwp = Math.Max(0, value); N(nameof(Acwp)); } }
    }

    /// <summary>A dated programme milestone.</summary>
    public class ScheduleMilestone : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        private string _name = "Milestone";
        private DateTime _date = DateTime.Today;
        private bool _done;

        public string Name { get => _name; set { _name = value ?? ""; N(nameof(Name)); } }
        public DateTime Date { get => _date; set { _date = value; N(nameof(Date)); N(nameof(DateStr)); } }
        [JsonIgnore]
        public string DateStr
        {
            get => _date.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Date = d; }
        }
        public bool Done { get => _done; set { _done = value; N(nameof(Done)); } }
    }

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
