using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;   // CableTrayConduitBase, Conduit, CableTray
using Autodesk.Revit.DB.Mechanical;   // Duct, FlexDuct
using Autodesk.Revit.DB.Plumbing;     // Pipe, FlexPipe

namespace StingTools.Core.Mep
{
    /// <summary>
    /// Corporate MEP tagging convention: a long pipe/duct/conduit/tray run is a
    /// single logical thing and takes ONE tag, not one tag per modelled segment.
    /// A drawing that tags every segment reads as clutter, not thoroughness.
    ///
    /// <para>This grouper reduces a set of view candidates down to the segments
    /// that should carry a *visual* tag, by walking the MEP connector network and
    /// treating a "run" as a maximal chain of same-system, same-size segments
    /// connected through inline pass-through fittings (couplings / unions /
    /// elbows). The run breaks — and a new tag is warranted — at:</para>
    /// <list type="bullet">
    ///   <item>a size change (reducer / differing size);</item>
    ///   <item>a branch (tee / cross / wye — a fitting with 3+ connectors);</item>
    ///   <item>a system change;</item>
    ///   <item>a riser / drop — vertical segments are their own run so each riser
    ///         gets its own tag, matching drop/rise annotation convention.</item>
    /// </list>
    ///
    /// <para>This is a VISUAL-only reduction. The token parameters (ASS_TAG_1,
    /// containers) are still written to every segment upstream so schedules and
    /// BOQ are unaffected — only the drawn <c>IndependentTag</c> count drops.</para>
    /// </summary>
    public enum TagVisualPolicy
    {
        /// <summary>Place a visual tag on every segment (default for equipment / fixtures).</summary>
        All,
        /// <summary>Place one visual tag per connected run (default for linear MEP).</summary>
        PerRun,
        /// <summary>Never place a visual tag; token data only.</summary>
        None,
    }

    public static class MepRunGrouper
    {
        /// <summary>Result of a run-grouping pass over one view's candidate list.</summary>
        public sealed class RunGroupResult
        {
            /// <summary>Elements that should receive a visual tag after grouping.</summary>
            public List<Element> Representatives { get; } = new List<Element>();
            /// <summary>Number of segment tags suppressed (candidates − representatives) for PerRun categories.</summary>
            public int SuppressedCount { get; internal set; }
            /// <summary>Number of runs formed across all PerRun categories.</summary>
            public int RunCount { get; internal set; }
            /// <summary>Rep element id → member-segment count for the run it represents.</summary>
            public Dictionary<ElementId, int> MembersByRep { get; } = new Dictionary<ElementId, int>();
        }

        private static readonly HashSet<BuiltInCategory> LinearMepCats = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_FlexDuctCurves,
        };

        // Stable, locale-independent config keys for the linear-MEP categories the
        // visual-tag policy targets. The policy UI (SetMepTagPolicyCommand) writes these
        // English friendly names as keys; resolving them back from the element's
        // BuiltInCategory lets an override take effect regardless of Revit's UI language.
        // Matching only on the localized Category.Name silently ignores a "Pipes":"All"
        // override in a non-English office because the category reads "Rohre" /
        // "Canalisations" there, not "Pipes".
        private static readonly Dictionary<BuiltInCategory, string> LinearCatFriendlyKey = new Dictionary<BuiltInCategory, string>
        {
            { BuiltInCategory.OST_PipeCurves,     "Pipes" },
            { BuiltInCategory.OST_DuctCurves,     "Ducts" },
            { BuiltInCategory.OST_Conduit,        "Conduits" },
            { BuiltInCategory.OST_CableTray,      "Cable Trays" },
            { BuiltInCategory.OST_FlexPipeCurves, "Flex Pipes" },
            { BuiltInCategory.OST_FlexDuctCurves, "Flex Ducts" },
        };

        // A run may pass through fittings and inline accessories (couplings, elbows,
        // valves, dampers) but NOT through equipment or fixtures — a pump / tank /
        // AHU is a genuine break where a new run (and tag) begins, even if it has
        // only two connectors.
        private static readonly HashSet<BuiltInCategory> PassThroughFittingCats = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_DuctAccessory,
        };

        /// <summary>
        /// Reduce a view's candidate elements to the set that should carry visual
        /// tags. Non-linear categories (equipment, fixtures) pass through unchanged
        /// unless overridden to None; linear MEP categories are collapsed to one
        /// representative per run. Cheap no-op (returns all candidates) when no
        /// PerRun-policy category is present.
        /// </summary>
        public static RunGroupResult Reduce(Document doc, View view, IList<Element> candidates)
        {
            var result = new RunGroupResult();
            if (doc == null || candidates == null || candidates.Count == 0) return result;

            // Bucket candidates by resolved policy.
            var perRunByCat = new Dictionary<ElementId, List<Element>>();
            foreach (var el in candidates)
            {
                if (el?.Category == null) { result.Representatives.Add(el); continue; }
                TagVisualPolicy policy = ResolvePolicy(el);
                switch (policy)
                {
                    case TagVisualPolicy.None:
                        break; // dropped entirely — data only
                    case TagVisualPolicy.PerRun:
                        if (!perRunByCat.TryGetValue(el.Category.Id, out var list))
                        {
                            list = new List<Element>();
                            perRunByCat[el.Category.Id] = list;
                        }
                        list.Add(el);
                        break;
                    default: // All
                        result.Representatives.Add(el);
                        break;
                }
            }

            if (perRunByCat.Count == 0) return result; // no linear MEP to declutter

            foreach (var kvp in perRunByCat)
            {
                try { GroupOneCategory(kvp.Value, result); }
                catch (Exception ex)
                {
                    // Fail safe: on any error, keep all this category's segments rather
                    // than silently dropping tags the drafter expected.
                    StingLog.Warn($"MepRunGrouper: category {kvp.Key} grouping failed, keeping all segments: {ex.Message}");
                    result.Representatives.AddRange(kvp.Value);
                }
            }

            return result;
        }

        /// <summary>Resolve the visual-tag policy for an element: explicit config override, else linear-MEP default.</summary>
        public static TagVisualPolicy ResolvePolicy(Element el)
        {
            var overrides = TagConfig.CategoryVisualPolicy;
            if (el?.Category != null && overrides != null && overrides.Count > 0)
            {
                // Locale-independent match first: resolve the element's BuiltInCategory
                // and look the override up by a stable key — the English friendly name
                // the policy UI writes (e.g. "Pipes") or the BuiltInCategory enum name
                // (e.g. "OST_PipeCurves"). This is what makes a non-English office's
                // "Pipes":"All" actually take effect; keying on the localized
                // Category.Name alone drops it silently.
                try
                {
                    var bic = (BuiltInCategory)el.Category.Id.Value;
                    if (LinearCatFriendlyKey.TryGetValue(bic, out string friendly)
                        && overrides.TryGetValue(friendly, out string sf)
                        && Enum.TryParse(sf, ignoreCase: true, out TagVisualPolicy pf))
                        return pf;
                    if (overrides.TryGetValue(bic.ToString(), out string se)
                        && Enum.TryParse(se, ignoreCase: true, out TagVisualPolicy pe))
                        return pe;
                }
                catch { /* fall through to localized-name match */ }

                // Back-compat + non-linear categories: match on the running session's
                // localized Category.Name (works when the key was written in this locale).
                string cat = el.Category.Name;
                if (!string.IsNullOrEmpty(cat)
                    && overrides.TryGetValue(cat, out string s)
                    && Enum.TryParse(s, ignoreCase: true, out TagVisualPolicy p))
                    return p;
            }
            return IsLinearMepCategory(el) ? TagVisualPolicy.PerRun : TagVisualPolicy.All;
        }

        public static bool IsLinearMepCategory(Element el)
        {
            try
            {
                if (el?.Category == null) return false;
                var bic = (BuiltInCategory)el.Category.Id.Value;
                return LinearMepCats.Contains(bic);
            }
            catch { return false; }
        }

        // ── run grouping for a single category ──────────────────────────────

        private static void GroupOneCategory(List<Element> segs, RunGroupResult result)
        {
            int n = segs.Count;
            if (n == 0) return;
            if (n == 1) { result.Representatives.Add(segs[0]); result.RunCount++; return; }

            // Index candidate segments by element id for O(1) membership.
            var idx = new Dictionary<ElementId, int>(n);
            for (int i = 0; i < n; i++) idx[segs[i].Id] = i;

            var uf = new UnionFind(n);
            var vertical = new bool[n];
            var sizeKey = new string[n];
            var sysKey = new string[n];
            for (int i = 0; i < n; i++)
            {
                vertical[i] = IsVertical(segs[i]);
                sizeKey[i] = SizeKey(segs[i]);
                sysKey[i] = SystemKey(segs[i]);
            }

            for (int i = 0; i < n; i++)
            {
                if (vertical[i]) continue; // risers/drops never merge — own run
                Element c = segs[i];
                ConnectorManager cm = GetConnectorManager(c);
                if (cm == null) continue;

                foreach (Connector conn in cm.Connectors)
                {
                    if (conn == null || !conn.IsConnected) continue;
                    foreach (Connector other in Refs(conn))
                    {
                        Element owner = other?.Owner;
                        if (owner == null || owner.Id == c.Id) continue;

                        if (idx.TryGetValue(owner.Id, out int j))
                        {
                            // Direct curve-to-curve join.
                            TryUnion(uf, i, j, vertical, sizeKey, sysKey);
                        }
                        else if (owner is FamilyInstance fi && IsPassThroughFitting(fi))
                        {
                            // Hop across an inline fitting to the curve on its far side.
                            foreach (Element far in FarCurves(fi, other))
                            {
                                if (idx.TryGetValue(far.Id, out int k))
                                    TryUnion(uf, i, k, vertical, sizeKey, sysKey);
                            }
                        }
                    }
                }
            }

            // Collect run groups and pick the longest segment as each run's tag host.
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = uf.Find(i);
                if (!groups.TryGetValue(root, out var g)) { g = new List<int>(); groups[root] = g; }
                g.Add(i);
            }

            foreach (var g in groups.Values)
            {
                int repIdx = g[0];
                double bestLen = SegLength(segs[repIdx]);
                foreach (int m in g)
                {
                    double len = SegLength(segs[m]);
                    if (len > bestLen ||
                        (Math.Abs(len - bestLen) < 1e-9 && segs[m].Id.Value < segs[repIdx].Id.Value))
                    {
                        bestLen = len; repIdx = m;
                    }
                }
                Element rep = segs[repIdx];
                result.Representatives.Add(rep);
                result.MembersByRep[rep.Id] = g.Count;
                result.RunCount++;
                result.SuppressedCount += g.Count - 1;
            }
        }

        private static void TryUnion(UnionFind uf, int a, int b, bool[] vertical, string[] sizeKey, string[] sysKey)
        {
            if (vertical[a] || vertical[b]) return;                       // never merge a riser
            if (!string.Equals(sizeKey[a], sizeKey[b], StringComparison.OrdinalIgnoreCase)) return; // size change → break
            if (!string.Equals(sysKey[a], sysKey[b], StringComparison.OrdinalIgnoreCase)) return;    // system change → break
            uf.Union(a, b);
        }

        // ── Revit helpers ───────────────────────────────────────────────────

        private static ConnectorManager GetConnectorManager(Element el)
        {
            try
            {
                if (el is MEPCurve mc) return mc.ConnectorManager;              // Pipe, Duct, FlexPipe, FlexDuct
                if (el is CableTrayConduitBase ctc) return ctc.ConnectorManager; // Conduit, CableTray
            }
            catch (Exception ex) { StingLog.Warn($"MepRunGrouper.GetConnectorManager {el?.Id}: {ex.Message}"); }
            return null;
        }

        private static IEnumerable<Connector> Refs(Connector conn)
        {
            ConnectorSet set = null;
            try { set = conn.AllRefs; } catch { yield break; }
            if (set == null) yield break;
            foreach (Connector c in set) yield return c;
        }

        /// <summary>
        /// An inline pass-through fitting has exactly two physical connectors
        /// (coupling / union / elbow / reducer). A fitting with 3+ connectors is a
        /// branch (tee / cross / wye) and must break the run.
        /// </summary>
        private static bool IsPassThroughFitting(FamilyInstance fi)
        {
            try
            {
                // Must be a fitting/accessory category — never equipment or a fixture.
                if (fi?.Category == null) return false;
                if (!PassThroughFittingCats.Contains((BuiltInCategory)fi.Category.Id.Value)) return false;

                ConnectorManager cm = fi.MEPModel?.ConnectorManager;
                if (cm == null) return false;
                int count = 0;
                foreach (Connector c in cm.Connectors)
                    if (c != null && c.ConnectorType == ConnectorType.End) count++;
                return count == 2;
            }
            catch { return false; }
        }

        /// <summary>Curves connected to a pass-through fitting's connectors other than the one we arrived on.</summary>
        private static IEnumerable<Element> FarCurves(FamilyInstance fi, Connector arrivedOn)
        {
            ConnectorManager cm = null;
            try { cm = fi?.MEPModel?.ConnectorManager; } catch { }
            if (cm == null) yield break;

            XYZ arrivedOrigin = null;
            try { arrivedOrigin = arrivedOn?.Origin; } catch { }

            foreach (Connector fc in cm.Connectors)
            {
                if (fc == null || fc.ConnectorType != ConnectorType.End) continue;
                // Skip the fitting connector coincident with the one we came in on.
                if (arrivedOrigin != null)
                {
                    XYZ o = null;
                    try { o = fc.Origin; } catch { }
                    if (o != null && o.IsAlmostEqualTo(arrivedOrigin)) continue;
                }
                if (!SafeIsConnected(fc)) continue;
                foreach (Connector r in Refs(fc))
                {
                    Element owner = r?.Owner;
                    if (owner != null && owner.Id != fi.Id) yield return owner;
                }
            }
        }

        private static bool SafeIsConnected(Connector c)
        {
            try { return c.IsConnected; } catch { return false; }
        }

        private static bool IsVertical(Element el)
        {
            try
            {
                Curve crv = (el.Location as LocationCurve)?.Curve;
                if (crv == null) return false;
                XYZ a = crv.GetEndPoint(0), b = crv.GetEndPoint(1);
                XYZ d = b - a;
                if (d.GetLength() < 1e-6) return false;
                return Math.Abs(d.Normalize().Z) > 0.7; // steeper than ~45° → riser/drop
            }
            catch { return false; }
        }

        private static double SegLength(Element el)
        {
            try { return (el.Location as LocationCurve)?.Curve?.Length ?? 0.0; }
            catch { return 0.0; }
        }

        private static string SizeKey(Element el)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE);
                string s = p?.AsString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            catch { }
            // Fallback: diameter or width×height rounded to the mm.
            try
            {
                double dia = TryDouble(el, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (dia <= 0) dia = TryDouble(el, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                if (dia > 0) return $"D{Math.Round(dia * 304.8)}";
                double w = TryDouble(el, BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                double h = TryDouble(el, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                if (w > 0 || h > 0) return $"{Math.Round(w * 304.8)}x{Math.Round(h * 304.8)}";
            }
            catch { }
            return ""; // unknown size — treat all-unknown as mergeable
        }

        private static string SystemKey(Element el)
        {
            try
            {
                if (el is MEPCurve mc)
                {
                    string sys = mc.MEPSystem?.Name;
                    if (!string.IsNullOrWhiteSpace(sys)) return sys.Trim();
                }
            }
            catch { }
            // Cross-type fallback: STING SYS token, else system-type param.
            try
            {
                string tok = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                if (!string.IsNullOrWhiteSpace(tok)) return tok.Trim();
            }
            catch { }
            try
            {
                string st = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString();
                if (!string.IsNullOrWhiteSpace(st)) return st.Trim();
            }
            catch { }
            return "";
        }

        private static double TryDouble(Element el, BuiltInParameter bip)
        {
            try { Parameter p = el.get_Parameter(bip); return p != null && p.HasValue ? p.AsDouble() : 0.0; }
            catch { return 0.0; }
        }

        // ── minimal union-find ──────────────────────────────────────────────

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly int[] _rank;
            public UnionFind(int n)
            {
                _parent = new int[n];
                _rank = new int[n];
                for (int i = 0; i < n; i++) _parent[i] = i;
            }
            public int Find(int x)
            {
                while (_parent[x] != x) { _parent[x] = _parent[_parent[x]]; x = _parent[x]; }
                return x;
            }
            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
                _parent[rb] = ra;
                if (_rank[ra] == _rank[rb]) _rank[ra]++;
            }
        }
    }
}
