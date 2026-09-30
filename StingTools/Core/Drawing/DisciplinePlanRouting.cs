// StingTools — Drawing Template Manager · which drawing types a discipline's plans are
//
// Three places make "a plan per level for each discipline": the Project Setup Wizard's
// documentation phase, the HVAC panel's "Per-level + sheets" button and the default
// selection in "Produce & Export". Each used to decide on its own — the wizard made
// unstamped views named "<Discipline> Plan - <Level>", the HVAC button a hand-rolled
// coordination plan with the first title block — so none of their output was a drawing
// type's output, and a later drawing-type production made a second set.
//
// This is the one answer: ask the routing table which type a discipline's PLAN (and
// RCP) drawing is, exactly as DrawingDispatcher.Resolve does for everything else, and
// keep only the types that can be produced per level. What routes nowhere, and what
// routes to a type that is not a plan, is returned so the caller can say so instead of
// quietly producing less.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One routed (discipline, docType) → drawing type answer.</summary>
    public sealed class RoutedPick
    {
        public string Discipline { get; set; }
        public string DocType { get; set; }
        public DrawingType Type { get; set; }
    }

    public sealed class DisciplineRouting
    {
        /// <summary>Per-level producible picks, in discipline then docType order.</summary>
        public List<RoutedPick> Picks { get; } = new List<RoutedPick>();
        /// <summary>Disciplines whose required docType (PLAN) routes to no drawing type.</summary>
        public List<string> Unrouted { get; } = new List<string>();
        /// <summary>"E / PLAN → elec-riser-A3-1to200 (Section)": routed, but not a per-level plan.</summary>
        public List<string> NotPerLevel { get; } = new List<string>();

        /// <summary>The distinct drawing types picked, first occurrence order.</summary>
        public List<DrawingType> Types
        {
            get
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var list = new List<DrawingType>();
                foreach (var p in Picks)
                    if (p.Type != null && seen.Add(p.Type.Id ?? "")) list.Add(p.Type);
                return list;
            }
        }

        /// <summary>The disciplines a drawing type was picked for.</summary>
        public List<string> DisciplinesFor(string typeId)
            => Picks.Where(p => string.Equals(p.Type?.Id, typeId, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Discipline).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static class DisciplinePlanRouting
    {
        /// <summary>
        /// The docTypes asked for each discipline. PLAN is required — a discipline that
        /// routes no PLAN is reported. RCP is asked too, silently: only some disciplines
        /// have a ceiling-plan drawing type.
        /// </summary>
        public static readonly string[] PerLevelDocTypes = { "PLAN", "RCP" };

        /// <summary>True for the purposes a per-level producer makes: Plan and RCP.</summary>
        public static bool IsPerLevel(DrawingType t)
            => t != null
               && (string.Equals((t.Purpose ?? "").Trim(), DrawingPurpose.Plan, StringComparison.OrdinalIgnoreCase)
                || string.Equals((t.Purpose ?? "").Trim(), DrawingPurpose.Rcp, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Route each discipline's <paramref name="docTypes"/> (default PLAN, RCP; the
        /// first is the required one) through <paramref name="route"/> — the caller's
        /// DrawingDispatcher.Resolve, or a table walk in tests. Blank and repeated
        /// disciplines are ignored.
        /// </summary>
        public static DisciplineRouting Select(IEnumerable<string> disciplines,
            Func<string, string, DrawingType> route, IList<string> docTypes = null)
        {
            var res = new DisciplineRouting();
            if (disciplines == null || route == null) return res;
            var kinds = (docTypes == null || docTypes.Count == 0) ? PerLevelDocTypes : docTypes.ToArray();
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in disciplines)
            {
                var disc = (raw ?? "").Trim();
                if (disc.Length == 0 || !done.Add(disc)) continue;
                for (int i = 0; i < kinds.Length; i++)
                {
                    var docType = kinds[i];
                    var t = route(disc, docType);
                    if (t == null)
                    {
                        if (i == 0) res.Unrouted.Add(disc);
                        continue;
                    }
                    if (!IsPerLevel(t))
                    {
                        res.NotPerLevel.Add($"{disc} / {docType} → {t.Id} ({t.Purpose})");
                        continue;
                    }
                    if (res.Picks.Any(p => string.Equals(p.Discipline, disc, StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(p.Type?.Id, t.Id, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    res.Picks.Add(new RoutedPick { Discipline = disc, DocType = docType, Type = t });
                }
            }
            return res;
        }
    }
}
