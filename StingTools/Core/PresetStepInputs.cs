// StingTools — reading a command's inputs from a workflow step's "params"
//
// A command that asks its questions in dialogs cannot run unattended. Inside a
// workflow preset (WorkflowEngine.IsRunningPreset) such commands read the step's
// params (WorkflowEngine.StepParam) instead, and this file turns those strings
// into values. The rules every caller shares:
//
//   * an absent value takes the command's documented default;
//   * a value that cannot be read is an ERROR that names the param and what it
//     accepts — the step fails with it. A typo never quietly becomes the default;
//   * an absent NUMBER is null, not 0: engineering inputs have no default here,
//     and the caller decides whether a missing one fails the step.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core
{
    public static class PresetStepInputs
    {
        /// <summary>One accepted value and the spellings that mean it.</summary>
        public sealed class Choice
        {
            public string Canonical { get; }
            public IReadOnlyList<string> Aliases { get; }
            public Choice(string canonical, params string[] aliases)
            {
                Canonical = canonical;
                Aliases = aliases ?? new string[0];
            }
            internal bool Matches(string normalised)
                => Normalise(Canonical) == normalised || Aliases.Any(a => Normalise(a) == normalised);
        }

        // ── Shared vocabularies ────────────────────────────────────────────

        public const string ScopeSelection = "selection";
        public const string ScopeActiveView = "activeview";
        public const string ScopeProject = "project";

        /// <summary>params.scope: which elements / rooms a command works on.</summary>
        public static readonly IReadOnlyList<Choice> Scopes = new[]
        {
            new Choice(ScopeSelection, "selected", "selectedrooms", "sel"),
            new Choice(ScopeActiveView, "view", "active", "currentview"),
            new Choice(ScopeProject, "all", "allrooms", "model", "entireproject", "whole"),
        };

        public const string ModePlace = "place";
        public const string ModePreview = "preview";

        /// <summary>params.mode for a placing command: place for real, or score and report only.</summary>
        public static readonly IReadOnlyList<Choice> PlaceModes = new[]
        {
            new Choice(ModePlace, "live", "commit"),
            new Choice(ModePreview, "dryrun", "dry", "test"),
        };

        /// <summary>params.disciplines for Auto-drop.</summary>
        public static readonly IReadOnlyList<Choice> DropDisciplines = new[]
        {
            new Choice("electrical", "elec", "e", "conduit"),
            new Choice("plumbing", "plumb", "p", "pipe"),
            new Choice("hvac", "mechanical", "m", "duct"),
        };

        // ── Parsers ────────────────────────────────────────────────────────

        /// <summary>Lower case with spaces, underscores, hyphens and dots removed.</summary>
        public static string Normalise(string raw)
            => new string((raw ?? "").Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '.')
                                     .Select(char.ToLowerInvariant).ToArray());

        /// <summary>Split a list param on commas, semicolons or pipes; trimmed, blanks and repeats dropped.</summary>
        public static List<string> ParseList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim())
                      .Where(s => s.Length > 0)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }

        /// <summary>The requested names that are not in <paramref name="known"/> (case-insensitive).</summary>
        public static List<string> Unknown(IEnumerable<string> requested, IEnumerable<string> known)
        {
            var set = new HashSet<string>(known ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (requested ?? Enumerable.Empty<string>()).Where(r => !set.Contains(r)).ToList();
        }

        /// <summary>
        /// Read one of <paramref name="choices"/>. Blank → <paramref name="fallback"/>.
        /// Unreadable → false, <paramref name="value"/> null and an error naming the param.
        /// </summary>
        public static bool TryChoice(string name, string raw, string fallback, IReadOnlyList<Choice> choices,
            out string value, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) { value = fallback; return true; }
            var n = Normalise(raw);
            var hit = (choices ?? new Choice[0]).FirstOrDefault(c => c.Matches(n));
            if (hit != null) { value = hit.Canonical; return true; }
            value = null;
            error = $"params.{name} = '{raw.Trim()}' is not one of: {Accepted(choices)}.";
            return false;
        }

        /// <summary>A list of <paramref name="choices"/>, canonicalised in the order given. Blank → empty list.</summary>
        public static bool TryChoiceList(string name, string raw, IReadOnlyList<Choice> choices,
            out List<string> values, out string error)
        {
            values = new List<string>();
            error = null;
            var bad = new List<string>();
            foreach (var item in ParseList(raw))
            {
                var n = Normalise(item);
                var hit = (choices ?? new Choice[0]).FirstOrDefault(c => c.Matches(n));
                if (hit == null) bad.Add(item);
                else if (!values.Contains(hit.Canonical)) values.Add(hit.Canonical);
            }
            if (bad.Count == 0) return true;
            values = null;
            error = $"params.{name} names {string.Join(", ", bad.Select(b => "'" + b + "'"))}, not one of: {Accepted(choices)}.";
            return false;
        }

        /// <summary>true/false, yes/no, 1/0, on/off. Blank → <paramref name="fallback"/>.</summary>
        public static bool TryBool(string name, string raw, bool fallback, out bool value, out string error)
        {
            error = null;
            value = fallback;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            switch (Normalise(raw))
            {
                case "true": case "yes": case "y": case "1": case "on": value = true; return true;
                case "false": case "no": case "n": case "0": case "off": value = false; return true;
            }
            error = $"params.{name} = '{raw.Trim()}' is not true or false.";
            return false;
        }

        /// <summary>
        /// A number in [<paramref name="min"/>, <paramref name="max"/>], read culture-invariant
        /// (a decimal point, never a comma). Blank → true with <paramref name="value"/> null.
        /// </summary>
        public static bool TryNumber(string name, string raw, double min, double max, out double? value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            string t = raw.Trim();
            if (t.Contains(",")
                || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                || double.IsNaN(v) || double.IsInfinity(v))
            {
                error = $"params.{name} = '{t}' is not a number (use a decimal point).";
                return false;
            }
            if (v < min || v > max)
            {
                error = $"params.{name} = {v.ToString(CultureInfo.InvariantCulture)} is outside " +
                        $"{min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)}.";
                return false;
            }
            value = v;
            return true;
        }

        private static string Accepted(IReadOnlyList<Choice> choices)
            => string.Join(" | ", (choices ?? new Choice[0]).Select(c => c.Canonical));
    }
}
