// ══════════════════════════════════════════════════════════════════════════
//  ScheduleImporter.cs — ONE converged programme parser. PM-4.
//
//  The audit (§2 Scheduling) found three diverging import paths:
//    • SchedulingCommands.ImportMSProjectXML (XmlConvert.ToTimeSpan / 8h)
//    • SchedulingCommands.ImportP6XML        (predecessors dropped, % un-normalised)
//    • V6/FourdGanttReader                   (dates from Start/Finish only, no preds,
//                                             over-strict XER date formats)
//  …giving the SAME .xml/.xer different durations, different % complete, and no
//  schedule logic. Without predecessors there is no CPM/float, so this is the
//  PM-4 foundation the cash-flow S-curve (PM-3) sits on.
//
//  This is the single pure parser. It emits the unified Core.Schedule.ScheduleTask
//  (which already carries Predecessors / Start / End / PercentComplete / Wbs), so
//  ScheduleStore becomes the one sink. It:
//    • reads MS Project XML <PredecessorLink> AND P6 <Relationship> / XER TASKPRED;
//    • normalises % complete to 0..100 ONCE, centrally (0..1 fraction → ×100);
//    • parses XER dates seconds-tolerant and WARNS (not silently drops) on skip;
//    • leaves working-calendar duration conversion to the Revit-coupled caller.
//
//  Pure (no Revit / no I/O beyond File.Read on the given path) — unit-tested
//  headlessly in StingTools.Scheduling.Tests with fixture .xml / .xer strings.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace StingTools.Core.Schedule
{
    /// <summary>Result of an import — the converged task list plus any warnings
    /// (skipped rows, unparseable dates) so nothing is silently dropped.</summary>
    public class ScheduleImportResult
    {
        public List<ScheduleTask> Tasks { get; set; } = new List<ScheduleTask>();
        public List<string> Warnings { get; set; } = new List<string>();
        /// <summary>"msproject" | "p6xml" | "xer" | "" (unrecognised).</summary>
        public string Source { get; set; } = "";
    }

    public static class ScheduleImporter
    {
        // XER dates appear with and without time, sometimes with seconds. P6 also
        // emits "yyyy-MM-dd HH:mm:ss" and "dd-MMM-yy". MS Project XML is ISO.
        private static readonly string[] XerDateFormats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "dd-MMM-yy HH:mm", "dd-MMM-yy", "MM/dd/yyyy HH:mm", "MM/dd/yyyy",
        };

        /// <summary>Auto-detect and parse a programme file by extension + content.</summary>
        public static ScheduleImportResult Parse(string path)
        {
            var r = new ScheduleImportResult();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                r.Warnings.Add($"File not found: {path}");
                return r;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".xer") return ParseXer(File.ReadAllText(path));
                if (ext == ".xml")
                {
                    string text = File.ReadAllText(path);
                    return text.IndexOf("<Activity", StringComparison.OrdinalIgnoreCase) >= 0
                        ? ParseP6Xml(text)
                        : ParseMsProjectXml(text);
                }
                r.Warnings.Add($"Unrecognised schedule extension '{ext}'.");
            }
            catch (Exception ex) { r.Warnings.Add($"Parse failed: {ex.Message}"); }
            return r;
        }

        // ── MS Project XML ──────────────────────────────────────────────────
        public static ScheduleImportResult ParseMsProjectXml(string xml)
        {
            var r = new ScheduleImportResult { Source = "msproject" };
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (Exception ex) { r.Warnings.Add($"MSP XML: {ex.Message}"); return r; }
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            int auto = 1;
            foreach (var te in doc.Descendants(ns + "Task"))
            {
                string uid = te.Element(ns + "UID")?.Value ?? "";
                string name = te.Element(ns + "Name")?.Value ?? "";
                // MSP marks the project summary row as UID 0 with no name — skip it.
                if (string.IsNullOrWhiteSpace(name)) continue;

                DateTime start = ParseIso(te.Element(ns + "Start")?.Value);
                DateTime finish = ParseIso(te.Element(ns + "Finish")?.Value);
                double pct = NormalisePercent(te.Element(ns + "PercentComplete")?.Value);
                bool summary = ParseBool(te.Element(ns + "Summary")?.Value);
                bool milestone = ParseBool(te.Element(ns + "Milestone")?.Value);
                int outline = ParseInt(te.Element(ns + "OutlineLevel")?.Value);
                string wbs = te.Element(ns + "WBS")?.Value ?? "";

                var task = new ScheduleTask
                {
                    Id = string.IsNullOrEmpty(uid) ? auto : (int.TryParse(uid, out int u) ? u : auto),
                    MsUid = uid,
                    Wbs = wbs,
                    Name = name,
                    Start = start,
                    End = finish < start ? start : finish,
                    PercentComplete = pct,
                    IsSummary = summary,
                    IsMilestone = milestone,
                    OutlineLevel = outline,
                };
                // <PredecessorLink><PredecessorUID>..<Type>..</PredecessorLink>
                foreach (var pl in te.Elements(ns + "PredecessorLink"))
                {
                    string puid = pl.Element(ns + "PredecessorUID")?.Value ?? "";
                    if (string.IsNullOrEmpty(puid)) continue;
                    task.Predecessors.Add(new SchedulePredecessor
                    {
                        TaskId = puid,
                        Type = MsLinkType(pl.Element(ns + "Type")?.Value),
                    });
                }
                r.Tasks.Add(task);
                auto = Math.Max(auto, task.Id) + 1;
            }
            return r;
        }

        // MSP link Type: 0=FF 1=FS 2=SF 3=SS (MS Project XML schema).
        private static string MsLinkType(string raw) => (raw ?? "").Trim() switch
        {
            "0" => "FF",
            "2" => "SF",
            "3" => "SS",
            _ => "FS",
        };

        // ── Primavera P6 XML ────────────────────────────────────────────────
        public static ScheduleImportResult ParseP6Xml(string xml)
        {
            var r = new ScheduleImportResult { Source = "p6xml" };
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (Exception ex) { r.Warnings.Add($"P6 XML: {ex.Message}"); return r; }

            // Match by LocalName so the P6 namespace doesn't matter.
            var activities = doc.Descendants().Where(e => e.Name.LocalName == "Activity").ToList();
            int auto = 1;
            var byObjectId = new Dictionary<string, ScheduleTask>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in activities)
            {
                string ev(string ln) => a.Elements().FirstOrDefault(e => e.Name.LocalName == ln)?.Value;
                string objId = ev("ObjectId") ?? ev("Id") ?? "";
                string id = ev("Id") ?? objId;
                string name = ev("Name") ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                DateTime start = ParseAny(ev("PlannedStartDate") ?? ev("StartDate") ?? ev("ActualStartDate"));
                DateTime finish = ParseAny(ev("PlannedFinishDate") ?? ev("FinishDate") ?? ev("ActualFinishDate"));
                double pct = NormalisePercent(ev("PercentComplete") ?? ev("PhysicalPercentComplete")
                                              ?? ev("DurationPercentComplete"));

                var task = new ScheduleTask
                {
                    Id = int.TryParse(id, out int iv) ? iv : auto,
                    MsUid = id,
                    Wbs = ev("WBSCode") ?? ev("WBSObjectId") ?? "",
                    Name = name,
                    Start = start,
                    End = finish < start ? start : finish,
                    PercentComplete = pct,
                };
                r.Tasks.Add(task);
                if (!string.IsNullOrEmpty(objId)) byObjectId[objId] = task;
                if (!string.IsNullOrEmpty(id)) byObjectId[id] = task;
                auto = Math.Max(auto, task.Id) + 1;
            }

            // P6 <Relationship> rows live OUTSIDE <Activity> — wire predecessors.
            foreach (var rel in doc.Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                string ev(string ln) => rel.Elements().FirstOrDefault(e => e.Name.LocalName == ln)?.Value;
                string predId = ev("PredecessorActivityObjectId") ?? ev("PredecessorActivityId");
                string succId = ev("SuccessorActivityObjectId") ?? ev("SuccessorActivityId");
                string type = NormaliseRelType(ev("Type"));
                if (string.IsNullOrEmpty(predId) || string.IsNullOrEmpty(succId)) continue;
                if (byObjectId.TryGetValue(succId, out var succ) && byObjectId.TryGetValue(predId, out var pred))
                    succ.Predecessors.Add(new SchedulePredecessor { TaskId = pred.MsUid, Type = type });
            }
            return r;
        }

        // ── Primavera XER (tab-delimited) ───────────────────────────────────
        public static ScheduleImportResult ParseXer(string text)
        {
            var r = new ScheduleImportResult { Source = "xer" };
            // Two passes — TASK then TASKPRED — over the %T/%F/%R table blocks.
            var taskRows = new List<Dictionary<string, string>>();
            var predRows = new List<Dictionary<string, string>>();
            string[] fields = null;
            string table = "";
            int skipped = 0;

            foreach (var line in text.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.StartsWith("%T\t")) { table = l.Substring(3).Trim(); fields = null; continue; }
                if (l.StartsWith("%F\t")) { fields = l.Substring(3).Split('\t'); continue; }
                if (!l.StartsWith("%R\t") || fields == null) continue;
                var cells = l.Substring(3).Split('\t');
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fields.Length && i < cells.Length; i++) dict[fields[i]] = cells[i];
                if (table == "TASK") taskRows.Add(dict);
                else if (table == "TASKPRED") predRows.Add(dict);
            }

            var byTaskId = new Dictionary<string, ScheduleTask>(StringComparer.OrdinalIgnoreCase);
            int auto = 1;
            foreach (var d in taskRows)
            {
                string taskId = d.GetValueOrDefault("task_id", "");
                string code = d.GetValueOrDefault("task_code", taskId);
                string name = d.GetValueOrDefault("task_name", "");
                if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

                string sRaw = First(d, "act_start_date", "target_start_date", "early_start_date", "restart_date");
                string fRaw = First(d, "act_end_date", "target_end_date", "early_end_date", "reend_date");
                if (!TryParseXerDate(sRaw, out var start)) { skipped++; r.Warnings.Add($"XER task '{code}': unparseable start '{sRaw}'."); continue; }
                if (!TryParseXerDate(fRaw, out var finish)) { skipped++; r.Warnings.Add($"XER task '{code}': unparseable finish '{fRaw}'."); continue; }

                double pct = NormalisePercent(First(d, "phys_complete_pct", "complete_pct", "act_work_qty"));

                var task = new ScheduleTask
                {
                    Id = int.TryParse(taskId, out int iv) ? iv : auto,
                    MsUid = taskId,
                    Wbs = d.GetValueOrDefault("wbs_id", ""),
                    Name = name,
                    Start = start,
                    End = finish < start ? start : finish,
                    PercentComplete = pct,
                    IsMilestone = (d.GetValueOrDefault("task_type", "") ?? "").Contains("Mile"),
                };
                r.Tasks.Add(task);
                byTaskId[taskId] = task;
                auto = Math.Max(auto, task.Id) + 1;
            }

            // TASKPRED: pred_task_id → task_id, pred_type ("PR_FS"/"PR_SS"/"PR_FF"/"PR_SF").
            foreach (var d in predRows)
            {
                string predId = d.GetValueOrDefault("pred_task_id", "");
                string succId = d.GetValueOrDefault("task_id", "");
                string type = NormaliseRelType(d.GetValueOrDefault("pred_type", "PR_FS"));
                if (byTaskId.TryGetValue(succId, out var succ) && byTaskId.TryGetValue(predId, out var pred))
                    succ.Predecessors.Add(new SchedulePredecessor { TaskId = pred.MsUid, Type = type });
            }

            if (skipped > 0) r.Warnings.Add($"XER import skipped {skipped} task row(s) (missing name / unparseable dates).");
            return r;
        }

        // ── Shared helpers ──────────────────────────────────────────────────

        /// <summary>Normalise a % value to 0..100 ONCE. A fraction in (0,1] is
        /// scaled ×100; values already &gt;1 are taken as percent and clamped.</summary>
        public static double NormalisePercent(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (!double.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return 0;
            if (v > 0 && v <= 1.0) v *= 100.0;          // 0..1 fraction → percent
            return Math.Max(0, Math.Min(100, v));
        }

        /// <summary>Map any relationship-type spelling to FS/SS/FF/SF.</summary>
        public static string NormaliseRelType(string raw)
        {
            string s = (raw ?? "").Trim().ToUpperInvariant().Replace("PR_", "");
            return s switch
            {
                "SS" => "SS",
                "FF" => "FF",
                "SF" => "SF",
                "FINISH_START" => "FS",
                "START_START" => "SS",
                "FINISH_FINISH" => "FF",
                "START_FINISH" => "SF",
                _ => "FS",
            };
        }

        public static bool TryParseXerDate(string raw, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            raw = raw.Trim();
            if (DateTime.TryParseExact(raw, XerDateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dt)) return true;
            // Last resort — culture-invariant flexible parse (seconds, T-separators).
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        }

        private static DateTime ParseIso(string raw)
        {
            if (!string.IsNullOrWhiteSpace(raw) &&
                DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
            return DateTime.Today;
        }

        private static DateTime ParseAny(string raw)
        {
            if (TryParseXerDate(raw, out var d)) return d;
            return DateTime.Today;
        }

        private static bool ParseBool(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant();
            return s == "1" || s == "true" || s == "yes";
        }

        private static int ParseInt(string raw)
            => int.TryParse((raw ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static string First(Dictionary<string, string> d, params string[] keys)
        {
            foreach (var k in keys)
                if (d.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }
    }
}
