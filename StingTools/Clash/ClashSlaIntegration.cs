// ClashSlaIntegration.cs — wire clash groups into the existing CoordIssue SLA engine.
//
// Prompt's heredoc assumed `CoordIssue` lived in StingTools.Core with CreatedUtc. In
// this repo it is Planscape.Shared.BCF.CoordIssue with CreationDate (Phase 95). Fields
// adjusted to match the real type per prompt's anticipated-mismatch note.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Planscape.Shared.BCF;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public static class ClashSlaIntegration
    {
        public static List<CoordIssue> CreateIssues(ClashRunRecord run, ClashMatrix matrix)
        {
            var result = new List<CoordIssue>();
            if (run?.Groups == null) return result;
            // F6: Build a GroupId → ClashRecord[] index so we can write the
            //     issue Guid back onto every member ClashRecord.IssueGuid.
            //     Closes the BCF round-trip loop — re-importing the BCF
            //     can reconstruct the (issue, clash[]) association without
            //     scanning prior runs.
            var byGroupId = new Dictionary<string, List<ClashRecord>>(StringComparer.Ordinal);
            if (run.Clashes != null)
            {
                foreach (var c in run.Clashes)
                {
                    if (string.IsNullOrEmpty(c.GroupId)) continue;
                    if (!byGroupId.TryGetValue(c.GroupId, out var lst))
                        byGroupId[c.GroupId] = lst = new List<ClashRecord>();
                    lst.Add(c);
                }
            }

            foreach (var g in run.Groups)
            {
                // A2: Element- and pattern-anchor strings are minted by
                // ClashGrouper as "{pairId} via {side}={cat}:{eid}" or
                // "{pairId} repetition (Z-stack, vol≈N)" — neither matches
                // a raw matrix cell PairId. Strip the suffix before the
                // matrix lookup so severity / OwnerDiscipline resolve from
                // the cell rather than always defaulting to MED / Coord.
                string pairId = ExtractPairId(g.Anchor);
                var cell = matrix?.Cells?.FirstOrDefault(c =>
                    string.Equals(c.PairId, pairId, StringComparison.OrdinalIgnoreCase));
                string issueGuid = Guid.NewGuid().ToString();
                var issue = new CoordIssue
                {
                    Guid = issueGuid,
                    Title = $"Clash group {g.Id} ({g.Size} clashes)",
                    Description = $"Matrix pair {pairId}. Severity {cell?.Severity ?? "MED"}.",
                    Status = "Open",
                    Priority = cell?.Severity ?? "MED",
                    Assignee = cell?.OwnerDiscipline ?? "Coord",
                    CreationDate = DateTime.UtcNow
                };
                g.Assignee = issue.Assignee;
                result.Add(issue);

                // F6: Write back-link onto every member ClashRecord.
                if (byGroupId.TryGetValue(g.Id ?? "", out var members))
                {
                    foreach (var c in members)
                    {
                        c.IssueGuid = issueGuid;
                        c.LinkedIssueGuid = issueGuid;   // legacy field — keep in sync
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// A2: ClashGrouper anchors carry one of three shapes:
        ///   "DUCT:STR_BEAM"                                 (spatial pass)
        ///   "DUCT:STR_BEAM via A=Ducts:12345"               (element pattern)
        ///   "DUCT:STR_BEAM repetition (Z-stack, vol≈12)"    (repetition pattern)
        /// All three must collapse to the leading PairId so the matrix
        /// lookup succeeds. Splitting on the literal " via " token catches
        /// element-pattern, splitting on " repetition " catches repetition.
        /// </summary>
        internal static string ExtractPairId(string anchor)
        {
            if (string.IsNullOrEmpty(anchor)) return anchor ?? "";
            int viaIdx = anchor.IndexOf(" via ", StringComparison.Ordinal);
            if (viaIdx > 0) return anchor.Substring(0, viaIdx);
            int repIdx = anchor.IndexOf(" repetition", StringComparison.Ordinal);
            if (repIdx > 0) return anchor.Substring(0, repIdx);
            return anchor;
        }

        /// <summary>
        /// C2: Resolve workset owner names for each issue and overwrite
        /// CoordIssue.Assignee when the workset name maps to a known discipline
        /// prefix. Falls back to the discipline-default when no clash element
        /// resolves cleanly. Best-effort — any Revit failure logs and leaves
        /// the existing matrix-cell assignment in place.
        /// </summary>
        public static void EnrichAssignees(Document doc, List<CoordIssue> issues, ClashRunRecord run)
        {
            if (doc == null || issues == null || run == null) return;
            if (!doc.IsWorkshared) return;
            try
            {
                // F12: Element-level workset resolution with majority vote.
                //      Prior code took the workset owner of the FIRST member
                //      of each group, but two clashes in the same spatial
                //      cell can come from different worksets (M services +
                //      E services often co-locate). Now: resolve the owner
                //      for every member ClashRecord, take the most-frequent
                //      owner; tie-break by lex order. Mixed-owner groups
                //      get a "(mixed)" suffix so coordinators see the
                //      escalation cue.
                var byGroupId = new Dictionary<string, List<ClashRecord>>(StringComparer.Ordinal);
                foreach (var c in run.Clashes ?? new List<ClashRecord>())
                {
                    if (string.IsNullOrEmpty(c.GroupId)) continue;
                    if (!byGroupId.TryGetValue(c.GroupId, out var lst))
                        byGroupId[c.GroupId] = lst = new List<ClashRecord>();
                    lst.Add(c);
                }
                // Cache owner-name lookups so the same element doesn't pay
                // the GetWorksharingTooltipInfo cost twice across groups.
                var ownerCache = new Dictionary<int, string>();
                int i = 0;
                foreach (var g in run.Groups ?? new List<ClashGroupRecord>())
                {
                    if (i >= issues.Count) break;
                    var issue = issues[i++];
                    if (!byGroupId.TryGetValue(g.Id ?? "", out var members) || members.Count == 0) continue;

                    var votes = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var c in members)
                    {
                        string ownerA = ResolveOwnerForElementSide(doc, c.ElementA, ownerCache);
                        string ownerB = ResolveOwnerForElementSide(doc, c.ElementB, ownerCache);
                        foreach (var owner in new[] { ownerA, ownerB })
                        {
                            if (string.IsNullOrWhiteSpace(owner)) continue;
                            votes.TryGetValue(owner, out int n);
                            votes[owner] = n + 1;
                        }
                    }
                    if (votes.Count == 0) continue;
                    var winner = votes
                        .OrderByDescending(kv => kv.Value)
                        .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                        .First();
                    string assignee = winner.Key;
                    bool mixed = votes.Count > 1;
                    if (mixed) assignee = $"{winner.Key} (mixed)";
                    issue.Assignee = assignee;
                    g.Assignee = assignee;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ClashSlaIntegration.EnrichAssignees: {ex.Message}"); }
        }

        private static string ResolveOwnerForElementSide(Document doc, ClashElementRecord side,
            Dictionary<int, string> cache)
        {
            if (side == null || side.LinkInstanceId != -1 || side.ElementId <= 0) return "";
            if (cache.TryGetValue(side.ElementId, out var cached)) return cached;
            string owner = "";
            try
            {
                var el = doc.GetElement(new ElementId((long)side.ElementId));
                if (el != null)
                {
                    var info = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id);
                    owner = info?.Owner ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveOwnerForElementSide({side.ElementId}): {ex.Message}"); }
            cache[side.ElementId] = owner;
            return owner;
        }

        private static string ResolveWorksetOwner(Document doc, ClashRecord c)
        {
            try
            {
                if (c == null) return "";
                // Prefer a host element (LinkInstanceId == -1) so we resolve
                // worksets in the active document, not in a linked-doc.
                int eid = -1;
                if (c.ElementA != null && c.ElementA.LinkInstanceId == -1) eid = c.ElementA.ElementId;
                else if (c.ElementB != null && c.ElementB.LinkInstanceId == -1) eid = c.ElementB.ElementId;
                if (eid <= 0) return "";
                var el = doc.GetElement(new ElementId((long)eid));
                if (el == null) return "";
                var info = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id);
                string owner = info?.Owner ?? "";
                return owner;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveWorksetOwner({c?.Id}): {ex.Message}"); return ""; }
        }
    }
}
