// ══════════════════════════════════════════════════════════════════════════
//  ScheduleModelPocos.cs — pure schedule POCOs (no Revit). PM-4.
//
//  Extracted from ScheduleModel.cs (which keeps the Revit-coupled ScheduleStore)
//  so the model + the pure engines that consume it (ScheduleImporter,
//  ScheduleCpmBridge, CashFlowSCurve) are headlessly unit-tested in
//  StingTools.Scheduling.Tests with ZERO Autodesk.Revit.* imports — the same
//  split EvmPeriod uses against EvmCalculator.
//
//  The classes are the SAME names/namespace as before; only their file moved, so
//  every existing reference + the persisted schedule.json contract are unchanged.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

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

        [Newtonsoft.Json.JsonIgnore]
        public string StartStr
        {
            get => _start.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Start = d; }
        }
        [Newtonsoft.Json.JsonIgnore]
        public string EndStr
        {
            get => _end.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) End = d; }
        }
        /// <summary>Computed (BAC × duration share) when CostLoadUGX is 0; not
        /// persisted as authoritative.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public double PlannedCost { get; set; }

        // ── PM-4 CPM output (transient; stamped by the CPM command, not persisted
        //    as authoritative — recomputed from dependencies each run). ──
        [Newtonsoft.Json.JsonIgnore] public double TotalFloatDays { get; set; }
        [Newtonsoft.Json.JsonIgnore] public double FreeFloatDays { get; set; }
        [Newtonsoft.Json.JsonIgnore] public bool IsCritical { get; set; }
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
        [Newtonsoft.Json.JsonIgnore]
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
        [Newtonsoft.Json.JsonIgnore]
        public string DateStr
        {
            get => _date.ToString("yyyy-MM-dd");
            set { if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Date = d; }
        }
        public bool Done { get => _done; set { _done = value; N(nameof(Done)); } }
    }
}
