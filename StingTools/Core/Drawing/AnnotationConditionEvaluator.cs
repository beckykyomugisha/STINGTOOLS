using StingTools.Core;
// StingTools — Drawing Template Manager
//
// AnnotationConditionEvaluator evaluates the optional Condition string on
// AutoAnnotationRule before the rule is processed by AnnotationRunner.
//
// Supported grammar (case-insensitive keywords):
//   Scale   < | > | == | <= | >= <integer>
//   Phase   == | !=  <name>
//   Category == | != <name>
//   ViewType == | != <name>       (FloorPlan, CeilingPlan, Section, …)
//   Fill    < | >  <decimal>      (conduit fill pct, 0-100)
//   ViewName contains <substring>
//
// Compound expressions:
//   <expr> AND <expr>
//   <expr> OR  <expr>
//
// Fail-open: unknown tokens or parse failures return true so no rules
// silently drop. All parse failures are logged via StingLog.Warn.

using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Evaluates the Condition string field on <see cref="AutoAnnotationRule"/>.
    /// Called by AnnotationRunner before processing each rule.
    /// </summary>
    public static class AnnotationConditionEvaluator
    {
        /// <summary>
        /// Evaluates <paramref name="condition"/> against <paramref name="ctx"/>.
        /// Returns <c>true</c> when the condition is null or empty (no filter = always run).
        /// Returns <c>true</c> on any parse failure (fail-open).
        /// </summary>
        public static bool Evaluate(string condition, ConditionContext ctx)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;
            if (ctx == null) return true;

            try
            {
                return EvaluateExpression(condition.Trim(), ctx);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AnnotationConditionEvaluator: failed to parse '{condition}': {ex.Message}");
                return true; // fail-open
            }
        }

        // ── Expression parser ────────────────────────────────────────────────

        private static bool EvaluateExpression(string expr, ConditionContext ctx)
        {
            // Split on top-level OR first (lowest precedence)
            int orIdx = FindKeyword(expr, " OR ");
            if (orIdx >= 0)
            {
                string left  = expr.Substring(0, orIdx).Trim();
                string right = expr.Substring(orIdx + 4).Trim();
                return EvaluateExpression(left, ctx) || EvaluateExpression(right, ctx);
            }

            // Split on top-level AND
            int andIdx = FindKeyword(expr, " AND ");
            if (andIdx >= 0)
            {
                string left  = expr.Substring(0, andIdx).Trim();
                string right = expr.Substring(andIdx + 5).Trim();
                return EvaluateExpression(left, ctx) && EvaluateExpression(right, ctx);
            }

            // Single predicate
            return EvaluatePredicate(expr, ctx);
        }

        /// <summary>
        /// Finds the first occurrence of <paramref name="keyword"/> in
        /// <paramref name="expr"/> using a case-insensitive search on an
        /// uppercase copy, returning -1 if not found.
        /// </summary>
        private static int FindKeyword(string expr, string keyword)
        {
            string upper = expr.ToUpperInvariant();
            string kw    = keyword.ToUpperInvariant();
            return upper.IndexOf(kw, StringComparison.Ordinal);
        }

        private static bool EvaluatePredicate(string pred, ConditionContext ctx)
        {
            // ViewName contains <substring>
            if (StartsWithI(pred, "ViewName contains "))
            {
                string sub = pred.Substring("ViewName contains ".Length).Trim();
                return (ctx.ViewName ?? "").IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // Numeric comparisons: Scale, Fill
            if (TryNumericPredicate(pred, "Scale", out string scaleOp, out double scaleVal))
                return CompareDouble(ctx.ViewScale, scaleOp, scaleVal);

            if (TryNumericPredicate(pred, "Fill", out string fillOp, out double fillVal))
                return CompareDouble(ctx.ConduitFillPct, fillOp, fillVal);

            // String equality/inequality: Phase, Category, ViewType
            if (TryStringPredicate(pred, "Phase", out string phaseOp, out string phaseVal))
                return CompareString(ctx.PhaseName, phaseOp, phaseVal);

            if (TryStringPredicate(pred, "Category", out string catOp, out string catVal))
                return CompareString(ctx.Category, catOp, catVal);

            if (TryStringPredicate(pred, "ViewType", out string vtOp, out string vtVal))
                return CompareString(ctx.ViewTypeName, vtOp, vtVal);

            // Unknown predicate — fail open with a warning
            StingLog.Warn($"AnnotationConditionEvaluator: unrecognised predicate '{pred}' — treating as true");
            return true;
        }

        // ── Predicate helpers ────────────────────────────────────────────────

        private static bool TryNumericPredicate(string pred, string keyword,
            out string op, out double value)
        {
            op = null; value = 0;
            if (!StartsWithI(pred, keyword)) return false;

            string rest = pred.Substring(keyword.Length).TrimStart();

            foreach (string candidate in new[] { "<=", ">=", "==", "<", ">" })
            {
                if (rest.StartsWith(candidate, StringComparison.Ordinal))
                {
                    op = candidate;
                    string numPart = rest.Substring(candidate.Length).Trim();
                    if (double.TryParse(numPart,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                        return true;
                    break;
                }
            }
            return false;
        }

        private static bool TryStringPredicate(string pred, string keyword,
            out string op, out string value)
        {
            op = null; value = null;
            if (!StartsWithI(pred, keyword)) return false;

            string rest = pred.Substring(keyword.Length).TrimStart();

            foreach (string candidate in new[] { "==", "!=" })
            {
                if (rest.StartsWith(candidate, StringComparison.Ordinal))
                {
                    op    = candidate;
                    value = rest.Substring(candidate.Length).Trim();
                    // Strip optional surrounding quotes
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                        value = value.Substring(1, value.Length - 2);
                    return true;
                }
            }
            return false;
        }

        private static bool CompareDouble(double lhs, string op, double rhs)
        {
            switch (op)
            {
                case "<":  return lhs <  rhs;
                case ">":  return lhs >  rhs;
                case "==": return Math.Abs(lhs - rhs) < 1e-9;
                case "<=": return lhs <= rhs;
                case ">=": return lhs >= rhs;
                default:   return true; // fail-open
            }
        }

        private static bool CompareString(string lhs, string op, string rhs)
        {
            bool eq = string.Equals(lhs ?? "", rhs ?? "", StringComparison.OrdinalIgnoreCase);
            switch (op)
            {
                case "==": return eq;
                case "!=": return !eq;
                default:   return true; // fail-open
            }
        }

        private static bool StartsWithI(string s, string prefix) =>
            s != null && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Context struct passed to <see cref="AnnotationConditionEvaluator.Evaluate"/>.
    /// Build from a Revit view via <see cref="FromView"/>.
    /// </summary>
    public sealed class ConditionContext
    {
        /// <summary>View.Scale (e.g. 100 for 1:100).</summary>
        public int ViewScale { get; set; }

        /// <summary>Active project phase name (may be empty).</summary>
        public string PhaseName { get; set; }

        /// <summary>View.ViewType.ToString() — "FloorPlan", "Section", etc.</summary>
        public string ViewTypeName { get; set; }

        /// <summary>View.Name.</summary>
        public string ViewName { get; set; }

        /// <summary>Current rule's category string (may be null when building view-level context).</summary>
        public string Category { get; set; }

        /// <summary>Conduit fill percentage read from ELC_CDT_CBL_FILL_PCT (0–100).</summary>
        public double ConduitFillPct { get; set; }

        /// <summary>
        /// Build a <see cref="ConditionContext"/> from a Revit <paramref name="view"/>.
        /// </summary>
        /// <param name="doc">Active document.</param>
        /// <param name="view">The view being annotated.</param>
        /// <param name="category">Optional current rule category string.</param>
        public static ConditionContext FromView(Document doc, View view, string category = null)
        {
            string phaseName = "";
            try
            {
                if (doc != null)
                {
                    var phaseParam = view?.get_Parameter(BuiltInParameter.VIEW_PHASE);
                    if (phaseParam != null)
                    {
                        var phaseEl = doc.GetElement(phaseParam.AsElementId()) as Phase;
                        phaseName = phaseEl?.Name ?? "";
                    }
                }
            }
            catch { /* phase unavailable — leave empty */ }

            return new ConditionContext
            {
                ViewScale    = view?.Scale ?? 0,
                PhaseName    = phaseName,
                ViewTypeName = view?.ViewType.ToString() ?? "",
                ViewName     = view?.Name ?? "",
                Category     = category
            };
        }
    }
}
