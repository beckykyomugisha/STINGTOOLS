// StingTools Phase 182 — HVAC segment-role detector.
//
// Closes gap A1/D5 from the flexibility review: MepAutoSizeDuctCommand
// was defaulting every duct to the "branch" role, defeating the point
// of per-role velocity targets in STING_MEP_SIZING_RULES.json.
//
// Strategy:
//   1. If HVC_SEGMENT_ROLE_TXT is already set on the element, trust it
//      (user / upstream tool wins).
//   2. Otherwise walk the connector graph backwards from the duct to
//      find the source equipment (AHU / fan / VAV / etc). Count edges:
//        depth == 0 (no equipment found, or duct is on the trunk)  → main
//        depth == 1 (one branch fitting between duct + equipment)    → branch
//        depth >= 2                                                  → runout
//   3. Sniff by *outgoing terminal proximity*: if there's a diffuser or
//      grille within one fitting hop, force "runout".
//   4. Cache the detected role on the element by writing HVC_SEGMENT_ROLE_TXT
//      so subsequent runs are O(1) and the panel can display it.
//
// The detector is deliberately fail-soft — Revit's connector graph
// returns nulls for disconnected ducts and we don't want to break the
// whole sizing pass because of one orphan.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using StingTools.Core;

namespace StingTools.Core.Mep
{
    public static class HvacSegmentRoleDetector
    {
        // Role ids must match STING_MEP_SIZING_RULES.json duct.roles[].id
        public const string RoleMain   = "main";
        public const string RoleBranch = "branch";
        public const string RoleRunout = "runout";

        private const string SegmentRoleParam = ParamRegistry.HVC_SEGMENT_ROLE_TXT;
        private const int MaxTraversal = 12;     // safety guard for cyclic graphs

        /// <summary>
        /// Detect the segment role for a duct. Returns one of the
        /// <see cref="RoleMain"/> / <see cref="RoleBranch"/> / <see cref="RoleRunout"/>
        /// constants. Result is cached on the element via HVC_SEGMENT_ROLE_TXT.
        /// </summary>
        public static string DetectRole(Document doc, Element duct)
        {
            if (doc == null || duct == null) return RoleBranch;
            try
            {
                // 1. Respect existing classification.
                string existing = ParameterHelpers.GetString(duct, SegmentRoleParam);
                if (!string.IsNullOrEmpty(existing)) return Normalise(existing);

                // 2. Connector-graph walk.
                string role = WalkAndClassify(doc, duct);

                // 3. Cache (transaction must already be open; SetString is a no-op if read-only).
                try { ParameterHelpers.SetString(duct, SegmentRoleParam, role, overwrite: false); }
                catch (Exception ex) { StingLog.Warn($"SegmentRole cache write {duct.Id}: {ex.Message}"); }

                return role;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HvacSegmentRoleDetector.DetectRole: {ex.Message}");
                return RoleBranch;
            }
        }

        /// <summary>
        /// Batch path: detect roles for every duct in one walk so a 500-duct
        /// view doesn't pay 500 separate upstream traversals. Reuses a
        /// shared "seen-equipment-depth" memo so duct C downstream of duct
        /// B downstream of AHU A only walks once.
        ///
        /// Caller is responsible for the surrounding Transaction so the
        /// HVC_SEGMENT_ROLE_TXT cache writes commit.
        /// </summary>
        public static Dictionary<ElementId, string> DetectRolesBatch(Document doc, IEnumerable<Element> ducts)
        {
            var result = new Dictionary<ElementId, string>();
            if (doc == null || ducts == null) return result;

            // Memo from connector-owner id → minimum depth to a piece of
            // mechanical equipment. Computed lazily on first request and
            // shared across the whole batch.
            var depthCache = new Dictionary<ElementId, int>();

            foreach (var d in ducts)
            {
                if (d == null) continue;
                try
                {
                    string existing = ParameterHelpers.GetString(d, SegmentRoleParam);
                    if (!string.IsNullOrEmpty(existing))
                    {
                        result[d.Id] = Normalise(existing);
                        continue;
                    }

                    var connectors = TryGetConnectors(d);
                    if (connectors == null || connectors.Count == 0)
                    {
                        result[d.Id] = RoleBranch;
                        continue;
                    }

                    if (TouchesAirTerminal(doc, connectors)) { result[d.Id] = RoleRunout; goto Cache; }

                    int minDepth = int.MaxValue;
                    foreach (Connector c in connectors)
                    {
                        int dp = WalkUpstreamCached(c, depthCache, new HashSet<ElementId>(), 0);
                        if (dp >= 0 && dp < minDepth) minDepth = dp;
                    }
                    string role = minDepth == int.MaxValue ? RoleBranch
                                : minDepth == 0            ? RoleMain
                                : minDepth == 1            ? RoleBranch
                                :                            RoleRunout;
                    result[d.Id] = role;

                Cache:
                    try { ParameterHelpers.SetString(d, SegmentRoleParam, result[d.Id], overwrite: false); }
                    catch (Exception ex) { StingLog.Warn($"SegmentRole batch cache write {d.Id}: {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"DetectRolesBatch {d.Id}: {ex.Message}");
                    result[d.Id] = RoleBranch;
                }
            }
            return result;
        }

        private static int WalkUpstreamCached(Connector startC, Dictionary<ElementId, int> memo,
            HashSet<ElementId> seen, int depth)
        {
            if (startC == null || depth > MaxTraversal) return -1;
            try
            {
                var refs = startC.AllRefs;
                if (refs == null) return -1;
                foreach (Connector other in refs)
                {
                    if (other == null || other.Owner == null) continue;
                    var owner = other.Owner;
                    if (!seen.Add(owner.Id)) continue;

                    if (memo.TryGetValue(owner.Id, out int cached))
                    {
                        if (cached >= 0) return depth + cached;
                        continue;
                    }
                    if (IsMechanicalEquipment(owner))
                    {
                        memo[owner.Id] = 0;
                        return depth;
                    }

                    var ownerCm = ConnectorsOf(owner);
                    if (ownerCm == null) continue;
                    int best = -1;
                    foreach (Connector cm in ownerCm)
                    {
                        if (cm == null || cm.Id == other.Id) continue;
                        int dp = WalkUpstreamCached(cm, memo, seen, depth + 1);
                        if (dp >= 0)
                        {
                            int local = dp - depth;
                            if (best < 0 || local < best) best = local;
                        }
                    }
                    if (best >= 0)
                    {
                        memo[owner.Id] = best;
                        return depth + best;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"WalkUpstreamCached: {ex.Message}"); }
            return -1;
        }

        private static string Normalise(string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return RoleBranch;
            string lc = roleId.Trim().ToLowerInvariant();
            if (lc == RoleMain || lc == RoleBranch || lc == RoleRunout) return lc;
            // Tolerate alternate names from older configs / hand-edited params.
            if (lc.StartsWith("trunk") || lc == "header" || lc == "riser") return RoleMain;
            if (lc.StartsWith("term")  || lc.StartsWith("twig") || lc == "drop") return RoleRunout;
            return RoleBranch;
        }

        private static string WalkAndClassify(Document doc, Element duct)
        {
            var connectors = TryGetConnectors(duct);
            if (connectors == null || connectors.Count == 0) return RoleBranch;

            // Terminal-proximity check: a terminal within one fitting wins.
            if (TouchesAirTerminal(doc, connectors)) return RoleRunout;

            int minDepthToEquip = int.MaxValue;
            foreach (Connector c in connectors)
            {
                int d = WalkUpstreamForEquipment(c, new HashSet<ElementId>(), 0);
                if (d >= 0 && d < minDepthToEquip) minDepthToEquip = d;
            }

            if (minDepthToEquip == int.MaxValue) return RoleBranch;      // no equipment reached
            if (minDepthToEquip == 0)            return RoleMain;        // directly off equipment
            if (minDepthToEquip == 1)            return RoleBranch;
            return RoleRunout;
        }

        private static int WalkUpstreamForEquipment(Connector startC, HashSet<ElementId> seen, int depth)
        {
            if (startC == null || depth > MaxTraversal) return -1;

            try
            {
                var refs = startC.AllRefs;
                if (refs == null) return -1;
                foreach (Connector other in refs)
                {
                    if (other == null || other.Owner == null) continue;
                    var owner = other.Owner;
                    if (!seen.Add(owner.Id)) continue;
                    if (IsMechanicalEquipment(owner)) return depth;

                    var ownerCm = ConnectorsOf(owner);
                    if (ownerCm == null) continue;
                    foreach (Connector cm in ownerCm)
                    {
                        if (cm == null || cm.Id == other.Id) continue;
                        int d = WalkUpstreamForEquipment(cm, seen, depth + 1);
                        if (d >= 0) return d;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"WalkUpstream: {ex.Message}"); }
            return -1;
        }

        private static bool IsMechanicalEquipment(Element el)
        {
            try
            {
                var cat = el?.Category;
                if (cat == null) return false;
                var bic = (BuiltInCategory)cat.Id.Value;
                return bic == BuiltInCategory.OST_MechanicalEquipment
                    || bic == BuiltInCategory.OST_DuctTerminal; // VAV boxes register as terminals on some templates
            }
            catch { return false; }
        }

        private static bool TouchesAirTerminal(Document doc, IList<Connector> connectors)
        {
            try
            {
                foreach (Connector c in connectors)
                {
                    var refs = c?.AllRefs;
                    if (refs == null) continue;
                    foreach (Connector other in refs)
                    {
                        if (other?.Owner?.Category == null) continue;
                        var bic = (BuiltInCategory)other.Owner.Category.Id.Value;
                        if (bic == BuiltInCategory.OST_DuctTerminal) return true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TouchesAirTerminal: {ex.Message}"); }
            return false;
        }

        private static IList<Connector> TryGetConnectors(Element el)
        {
            try
            {
                if (el is MEPCurve mc)
                {
                    var set = mc.ConnectorManager?.Connectors;
                    return ToList(set);
                }
                if (el is FamilyInstance fi)
                {
                    var set = fi.MEPModel?.ConnectorManager?.Connectors;
                    return ToList(set);
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryGetConnectors {el?.Id}: {ex.Message}"); }
            return null;
        }

        private static IList<Connector> ConnectorsOf(Element el) => TryGetConnectors(el);

        private static IList<Connector> ToList(ConnectorSet set)
        {
            var list = new List<Connector>();
            if (set == null) return list;
            foreach (Connector c in set) list.Add(c);
            return list;
        }
    }
}
