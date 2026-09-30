// StingTools — which drawing-type pre-flight errors block (Revit-free; unit-tested)
//
// The pre-flight validates the whole catalogue (100+ types). An error in a type the
// project never produces — a healthcare or presentation type on an office job — is worth
// reporting but must not fail the setup step, or the gate fails on every project and
// stops meaning anything. A type blocks when the project uses it: already stamped on a
// view or sheet, or what its routing produces for the disciplines it models.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class PreflightScope
    {
        public sealed class Verdict
        {
            /// <summary>Failing types the project uses — these fail the step.</summary>
            public List<string> Blocking { get; } = new List<string>();
            /// <summary>Failing types the project does not use — reported, not blocking.</summary>
            public List<string> Unused { get; } = new List<string>();
            public bool Fails => Blocking.Count > 0;
        }

        public static Verdict Split(IEnumerable<string> failingTypeIds, IEnumerable<string> inUseTypeIds)
        {
            var inUse = new HashSet<string>((inUseTypeIds ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            var v = new Verdict();
            foreach (var id in (failingTypeIds ?? Enumerable.Empty<string>())
                         .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
                (inUse.Contains(id) ? v.Blocking : v.Unused).Add(id);
            return v;
        }
    }
}
