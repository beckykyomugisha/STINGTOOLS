using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Read-only utility over OperationRegistry.RequiresOps. Used by the
    /// dashboard to gate Run buttons when prerequisite ops haven't been
    /// satisfied (per the readiness snapshot).
    /// </summary>
    public static class DependencyGraph
    {
        /// <summary>True when all of op's RequiresOps have Status == Green in the snapshot.</summary>
        public static bool IsRunnable(OpDefinition op, ReadinessSnapshot snap, out List<string> unmet)
        {
            unmet = new List<string>();
            if (op == null) return false;
            if (op.RequiresOps == null || op.RequiresOps.Length == 0) return true;
            if (snap == null) return true; // no info — let it run
            foreach (var dep in op.RequiresOps)
            {
                var b = snap.BadgeOrDefault(dep);
                // If the badge has zero "Total" we don't know — be permissive.
                if (b == null || b.Total == 0) continue;
                if (b.Done == 0) unmet.Add(dep);
            }
            return unmet.Count == 0;
        }

        /// <summary>Topologically order the given op tags by their dependency edges.</summary>
        public static List<string> TopologicalOrder(IEnumerable<string> opTags)
        {
            var ops = opTags.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(OperationRegistry.Get).Where(o => o != null).ToList();
            var dict = ops.ToDictionary(o => o.Tag, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(string tag)
            {
                if (visited.Contains(tag)) return;
                if (visiting.Contains(tag))
                {
                    StingTools.Core.StingLog.Warn($"DependencyGraph: cycle detected at {tag}");
                    return;
                }
                visiting.Add(tag);
                if (dict.TryGetValue(tag, out var op) && op.RequiresOps != null)
                {
                    foreach (var dep in op.RequiresOps)
                        if (dict.ContainsKey(dep)) Visit(dep);
                }
                visiting.Remove(tag);
                visited.Add(tag);
                ordered.Add(tag);
            }
            foreach (var op in ops) Visit(op.Tag);
            return ordered;
        }

        /// <summary>Return every transitive dependency of a given op tag.</summary>
        public static IEnumerable<string> TransitiveDependencies(string opTag)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(opTag);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                var op = OperationRegistry.Get(t);
                if (op?.RequiresOps == null) continue;
                foreach (var dep in op.RequiresOps)
                {
                    if (seen.Add(dep)) { stack.Push(dep); yield return dep; }
                }
            }
        }
    }
}
