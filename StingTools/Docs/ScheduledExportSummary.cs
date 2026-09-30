using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ScheduledExportSummary — what a scheduled-export run actually produced (R9).
    //
    //  ExportCenterRunSchedules returned Succeeded whether or not anything was
    //  exported: no job due, a job blocked by pre-flight, a set resolving no sheets
    //  and every sheet failing all read as a green step 5 in the KUT fortnightly
    //  issue, and the ACC steps after it went on. The runner now records one outcome
    //  per due job, and Verdict turns them into the step's result.
    //
    //  Revit-free on purpose: StingTools.Tags.Tests compiles this file.
    // ════════════════════════════════════════════════════════════════════════════

    public enum ScheduledJobState { Ran, ProfileMissing, NoSheets, Blocked, Error }

    public enum ScheduledExportVerdict { Succeeded, NothingDue, Failed }

    public sealed class ScheduledJobOutcome
    {
        public string Name { get; set; }
        public ScheduledJobState State { get; set; }
        public int FilesOk { get; set; }
        /// <summary>Rows the engine skipped by rule (file exists, duplicate output name) —
        /// not produced this run, but not a failure either.</summary>
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public string Detail { get; set; }
    }

    public sealed class ScheduledExportSummary
    {
        public List<ScheduledJobOutcome> Jobs { get; } = new List<ScheduledJobOutcome>();
        /// <summary>The Export Centre state could not be read, so no job could be looked at.</summary>
        public string LoadError { get; set; }

        public int Ran => Jobs.Count(j => j.State == ScheduledJobState.Ran);
        public int FilesOk => Jobs.Sum(j => j.FilesOk);
        public int FilesFailed => Jobs.Sum(j => j.FilesFailed);

        /// <summary>
        /// Failed when the state could not be read, any due job did not run (profile
        /// missing, no sheets, pre-flight block, error), or any file failed. Inside a
        /// workflow preset a run that exported nothing is also Failed — the step exists to
        /// produce the issue set, and "nothing was due" must not let the steps after it
        /// publish. Outside a preset, nothing due is only NothingDue.
        /// </summary>
        public ScheduledExportVerdict Verdict(bool inPreset)
        {
            if (!string.IsNullOrEmpty(LoadError)) return ScheduledExportVerdict.Failed;
            if (Jobs.Any(j => j.State != ScheduledJobState.Ran)) return ScheduledExportVerdict.Failed;
            if (FilesFailed > 0) return ScheduledExportVerdict.Failed;
            if (FilesOk == 0)
                return inPreset ? ScheduledExportVerdict.Failed : ScheduledExportVerdict.NothingDue;
            return ScheduledExportVerdict.Succeeded;
        }

        public string Describe()
        {
            if (!string.IsNullOrEmpty(LoadError))
                return "The Export Centre settings could not be read, so no scheduled job was run: " + LoadError;
            if (Jobs.Count == 0)
                return "No scheduled export job was due, so nothing was exported. Set up or re-time a job in the Export Centre (⏱).";
            var lines = new List<string>();
            foreach (var j in Jobs)
            {
                string what = j.State switch
                {
                    ScheduledJobState.Ran => $"{j.FilesOk} exported, {j.FilesFailed} failed" +
                                             (j.FilesSkipped > 0 ? $", {j.FilesSkipped} skipped" : ""),
                    ScheduledJobState.ProfileMissing => "NOT RUN — its export profile no longer exists",
                    ScheduledJobState.NoSheets => "NOT RUN — its sheet set resolved no sheets",
                    ScheduledJobState.Blocked => "NOT RUN — blocked by pre-flight",
                    _ => "NOT RUN — error",
                };
                lines.Add($"• {j.Name}: {what}" + (string.IsNullOrEmpty(j.Detail) ? "" : $" ({j.Detail})"));
            }
            return $"{Ran} of {Jobs.Count} due job(s) ran; {FilesOk} file(s) exported, {FilesFailed} failed.\n" +
                   string.Join("\n", lines);
        }
    }
}
