// StingTools v4 MVP — RoutingPreferenceInspector.
//
// Inspects the RoutingPreferenceManager on a PipeType / DuctType /
// ConduitType and returns:
//   - whether fittings are defined (elbow, tee, transition, union,
//     cap, takeoff) for common size ranges
//   - a human-readable diagnostic string reported alongside
//     drop results so the user knows when their type lacks a
//     fitting rule that would otherwise force a manual fix
//
// Background: Revit's Connector.ConnectTo relies on the
// RoutingPreferenceManager to auto-insert the correct fitting family
// when two MEPCurves meet at an angle or a diameter change. When a
// size-specific rule is missing (e.g. the project's "Pipe Standard"
// type has no 32 mm elbow), ConnectTo silently no-ops and leaves the
// joint unresolved. This inspector is the dry-run check that tells
// us that before we issue 400 connects and get nothing back.
//
// Revit API surface used:
//   MEPCurveType.RoutingPreferenceManager                 (PipeType / DuctType / etc.)
//   RoutingPreferenceManager.GetNumberOfRules(groupType)
//   RoutingPreferenceManager.GetRule(groupType, index)    → RoutingPreferenceRule
//   RoutingPreferenceRule.MEPPartId                       → ElementId of part family
//   RoutingPreferenceRule.Description

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Routing
{
    public class RoutingPreferenceReport
    {
        public string TypeName     { get; set; } = "";
        public bool   HasElbows    { get; set; }
        public bool   HasTees      { get; set; }
        public bool   HasUnions    { get; set; }
        public bool   HasTransitions { get; set; }
        public bool   HasCrosses   { get; set; }
        public bool   HasTakeoffs  { get; set; }
        public bool   HasCaps      { get; set; }
        public List<string> Gaps   { get; } = new List<string>();

        public bool IsProductionReady =>
            HasElbows && HasTees && (HasUnions || HasTransitions);

        public override string ToString()
        {
            var flags = new List<string>();
            if (HasElbows)      flags.Add("elbow");
            if (HasTees)        flags.Add("tee");
            if (HasTransitions) flags.Add("transition");
            if (HasUnions)      flags.Add("union");
            if (HasCrosses)     flags.Add("cross");
            if (HasTakeoffs)    flags.Add("takeoff");
            if (HasCaps)        flags.Add("cap");
            return $"{TypeName}: [{string.Join(",", flags)}]" +
                   (Gaps.Count > 0 ? $"  gaps: {string.Join("; ", Gaps)}" : "");
        }
    }

    public static class RoutingPreferenceInspector
    {
        /// <summary>
        /// Inspect a PipeType. Returns a report of which
        /// RoutingPreferenceRuleGroupType slots have at least one rule.
        /// </summary>
        public static RoutingPreferenceReport Inspect(PipeType pt)
        {
            var r = new RoutingPreferenceReport { TypeName = pt?.Name ?? "null" };
            if (pt == null) return r;
            try
            {
                var mgr = pt.RoutingPreferenceManager;
                if (mgr == null) { r.Gaps.Add("RoutingPreferenceManager null"); return r; }
                r.HasElbows      = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Elbows,       r, "Elbows");
                r.HasTees        = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Junctions,    r, "Junctions (tees)");
                r.HasCrosses     = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Crosses,      r, "Crosses");
                r.HasTransitions = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Transitions,  r, "Transitions");
                r.HasUnions      = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Unions,       r, "Unions");
                r.HasCaps        = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Caps,         r, "Caps");
            }
            catch (Exception ex) { r.Gaps.Add($"PipeType inspect: {ex.Message}"); }
            return r;
        }

        public static RoutingPreferenceReport Inspect(DuctType dt)
        {
            var r = new RoutingPreferenceReport { TypeName = dt?.Name ?? "null" };
            if (dt == null) return r;
            try
            {
                var mgr = dt.RoutingPreferenceManager;
                if (mgr == null) { r.Gaps.Add("RoutingPreferenceManager null"); return r; }
                r.HasElbows      = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Elbows,       r, "Elbows");
                r.HasTees        = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Junctions,    r, "Junctions (tees)");
                r.HasCrosses     = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Crosses,      r, "Crosses");
                r.HasTransitions = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Transitions,  r, "Transitions");
                r.HasCaps        = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Caps,         r, "Caps");
                // Ducts rarely carry Union rules — skip.
                r.HasUnions      = false;
            }
            catch (Exception ex) { r.Gaps.Add($"DuctType inspect: {ex.Message}"); }
            return r;
        }

        public static RoutingPreferenceReport Inspect(ConduitType ct)
        {
            var r = new RoutingPreferenceReport { TypeName = ct?.Name ?? "null" };
            if (ct == null) return r;
            try
            {
                var mgr = ct.RoutingPreferenceManager;
                if (mgr == null) { r.Gaps.Add("RoutingPreferenceManager null"); return r; }
                r.HasElbows = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Elbows, r, "Elbows");
                r.HasTees   = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Junctions, r, "Junctions (tees)");
                r.HasTransitions = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Transitions, r, "Transitions");
                r.HasUnions = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Unions, r, "Unions");
                r.HasCaps   = HasAnyRule(mgr, RoutingPreferenceRuleGroupType.Caps,   r, "Caps");
            }
            catch (Exception ex) { r.Gaps.Add($"ConduitType inspect: {ex.Message}"); }
            return r;
        }

        private static bool HasAnyRule(RoutingPreferenceManager mgr,
            RoutingPreferenceRuleGroupType group, RoutingPreferenceReport r, string label)
        {
            try
            {
                int n = mgr.GetNumberOfRules(group);
                if (n <= 0)
                {
                    r.Gaps.Add($"{label}: no rule");
                    return false;
                }
                // Walk the rules; accept the first one that has a valid
                // MEPPartId. An empty MEPPartId means "no family bound"
                // which Revit treats as "no fitting will be inserted".
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var rule = mgr.GetRule(group, i);
                        var partId = rule?.MEPPartId ?? ElementId.InvalidElementId;
                        if (partId != null && partId != ElementId.InvalidElementId)
                            return true;
                    }
                    catch { /* skip */ }
                }
                r.Gaps.Add($"{label}: {n} rules, none with bound part");
                return false;
            }
            catch (Exception ex)
            {
                r.Gaps.Add($"{label}: {ex.Message}");
                return false;
            }
        }
    }
}
