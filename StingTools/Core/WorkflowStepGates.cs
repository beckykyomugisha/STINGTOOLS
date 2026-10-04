// ═════════════════════════════════════════════════════════════════════════════
//  WorkflowStepGates.cs — the step gates that depend only on facts about the
//  project and the clock, decided without the Revit API.
//
//  The Complete Guide (docs/StingTools_Complete_Guide.html, "Conditional Step
//  Operators") documented requiresPhase, requiresMinElements,
//  requiresIssueCount, onWeekday, afterTime and beforeTime, and no code read
//  any of them: a preset that set one got a step that ran every time, and
//  Newtonsoft dropped the key without a word. minElementCount had the same
//  shape for a different reason: the engine tested it only inside the block
//  for a `condition` string, so the two shipped steps that set it with no
//  condition (MorningHealthCheck step 7, WeeklyDataDrop step 5) were never
//  gated at run time — only warned about in the pre-flight check.
//
//  WorkflowEngine collects the facts (lazily — a count is only computed when a
//  step asks for it) and calls SkipReason once per step. The rules live here so
//  StingTools.Tags.Tests can assert them.
//
//  A gate whose value cannot be read SKIPS the step and says why. Running it
//  would ignore the gate the preset author wrote, which is the defect above.
//
//  KEEP THIS FILE REVIT-FREE.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core
{
    public static class WorkflowStepGates
    {
        /// <summary>What the gates need to know. Counts are lazy: a step that sets no
        /// count gate never pays for the scan.</summary>
        public sealed class Facts
        {
            /// <summary>PRJ_ORG_PHASE_TXT on Project Information ("" when not set).</summary>
            public Func<string> ProjectPhase { get; set; } = () => "";
            /// <summary>Total model elements (instances, not types).</summary>
            public Func<int> ElementCount { get; set; } = () => 0;
            /// <summary>Elements in the tagged categories that carry a tag (complete or not).</summary>
            public Func<int> TaggedCount { get; set; } = () => 0;
            /// <summary>Open issues in the project's issue store.</summary>
            public Func<int> OpenIssueCount { get; set; } = () => 0;
            /// <summary>Local time the step is about to run.</summary>
            public DateTime Now { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Null when every gate on <paramref name="step"/> passes; otherwise the reason the
        /// step is skipped, worded for the workflow report.
        /// </summary>
        public static string SkipReason(WorkflowStep step, Facts facts)
        {
            if (step == null || facts == null) return null;

            if (step.MinElementCount.HasValue || step.MaxElementCount.HasValue)
            {
                int n = facts.ElementCount();
                if (step.MinElementCount.HasValue && n < step.MinElementCount.Value)
                    return $"{n} elements < min {step.MinElementCount.Value}";
                if (step.MaxElementCount.HasValue && n > step.MaxElementCount.Value)
                    return $"{n} elements > max {step.MaxElementCount.Value}";
            }

            if (step.RequiresMinElements.HasValue)
            {
                int tagged = facts.TaggedCount();
                if (tagged < step.RequiresMinElements.Value)
                    return $"{tagged} tagged elements < {step.RequiresMinElements.Value}";
            }

            if (step.RequiresIssueCount.HasValue)
            {
                int open = facts.OpenIssueCount();
                if (open < step.RequiresIssueCount.Value)
                    return $"{open} open issues < {step.RequiresIssueCount.Value}";
            }

            if (!string.IsNullOrWhiteSpace(step.RequiresPhase))
            {
                var wanted = SplitList(step.RequiresPhase);
                string phase = (facts.ProjectPhase() ?? "").Trim();
                if (phase.Length == 0)
                    return $"project phase not set (PRJ_ORG_PHASE_TXT); step requires {string.Join("/", wanted)}";
                if (!wanted.Any(w => string.Equals(w, phase, StringComparison.OrdinalIgnoreCase)))
                    return $"project phase {phase} is not {string.Join("/", wanted)}";
            }

            if (!string.IsNullOrWhiteSpace(step.OnWeekday))
            {
                var days = new HashSet<DayOfWeek>();
                foreach (string d in SplitList(step.OnWeekday))
                {
                    if (!TryParseDay(d, out DayOfWeek day))
                        return $"onWeekday '{d}' is not a day name";
                    days.Add(day);
                }
                if (!days.Contains(facts.Now.DayOfWeek))
                    return $"today is {facts.Now.DayOfWeek}, step runs on {string.Join("/", days)}";
            }

            TimeSpan? after = null, before = null;
            if (!string.IsNullOrWhiteSpace(step.AfterTime))
            {
                if (!TryParseTime(step.AfterTime, out TimeSpan t)) return $"afterTime '{step.AfterTime}' is not HH:mm";
                after = t;
            }
            if (!string.IsNullOrWhiteSpace(step.BeforeTime))
            {
                if (!TryParseTime(step.BeforeTime, out TimeSpan t)) return $"beforeTime '{step.BeforeTime}' is not HH:mm";
                before = t;
            }
            if (after.HasValue || before.HasValue)
            {
                TimeSpan now = facts.Now.TimeOfDay;
                bool inWindow;
                if (after.HasValue && before.HasValue && after.Value > before.Value)
                    inWindow = now >= after.Value || now < before.Value;   // overnight, e.g. 22:00–06:00
                else
                    inWindow = (!after.HasValue || now >= after.Value) && (!before.HasValue || now < before.Value);
                if (!inWindow)
                {
                    var window = new List<string>();
                    if (after.HasValue) window.Add("after " + after.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
                    if (before.HasValue) window.Add("before " + before.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
                    return "time " + facts.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                         + " is outside the step's window (" + string.Join(", ", window) + ")";
                }
            }

            return null;
        }

        /// <summary>"Mon, Wed;Fri" → ["Mon","Wed","Fri"].</summary>
        public static List<string> SplitList(string value) =>
            (value ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>Full English day name or its first three letters, any case.</summary>
        public static bool TryParseDay(string text, out DayOfWeek day)
        {
            day = DayOfWeek.Sunday;
            string t = (text ?? "").Trim();
            if (t.Length < 3) return false;
            foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)))
            {
                string name = d.ToString();
                if (string.Equals(name, t, StringComparison.OrdinalIgnoreCase)
                    || (t.Length == 3 && name.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
                {
                    day = d;
                    return true;
                }
            }
            return false;
        }

        /// <summary>"07:30" or "7:30" (24-hour).</summary>
        public static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            string t = (text ?? "").Trim();
            if (!TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time))
                return false;
            return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
        }
    }
}
