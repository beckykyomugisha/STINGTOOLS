// ClashHistory.cs — diff current run against prior run by identity hash.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Clash
{
    public static class ClashHistory
    {
        // E1 / F1: Maximum centroid drift (mm) that still counts as the
        // "same physical clash" for fuzzy match. ClashIdentity quantises
        // to 250 mm bins, so any drift beyond ~half a bin already changes
        // the identity hash. 500 mm is a forgiving but still tight band.
        internal const double FuzzyCentroidThresholdMm = 500.0;
        // F1: Severity escalation threshold — a clash that has been
        // reintroduced this many times auto-promotes Severity one tier.
        internal const int RepeatOffenderEscalateAt = 3;

        public static void MergeWithPrior(ClashRunRecord current, ClashRunRecord prior)
        {
            if (current == null) return;
            var priorByIdentity = prior?.Clashes?.ToDictionary(c => c.Identity, c => c) ?? new Dictionary<string, ClashRecord>();
            var now = DateTime.UtcNow;
            current.Stats.New = 0;
            current.Stats.Active = 0;
            current.Stats.Reintroduced = 0;

            // E1: Build a fuzzy-match index keyed on (elementA, elementB, pairId)
            //     so a clash whose centroid drifted >250 mm can still carry state
            //     across runs. Without this, a coordinator nudging a duct 7 mm
            //     re-mints the identity, surfacing as "Resolved + New" with all
            //     state lost. Index is keyed on a canonical (smaller-id-first)
            //     pair so the order in ElementA/ElementB doesn't break matches.
            var fuzzyIndex = new Dictionary<(int A, int B, string Pair), List<ClashRecord>>();
            if (prior?.Clashes != null)
            {
                foreach (var pc in prior.Clashes)
                {
                    if (pc.ElementA == null || pc.ElementB == null) continue;
                    var key = MakeFuzzyKey(pc);
                    if (!fuzzyIndex.TryGetValue(key, out var lst))
                        fuzzyIndex[key] = lst = new List<ClashRecord>();
                    lst.Add(pc);
                }
            }
            // Track which prior records were claimed by fuzzy match so
            // they don't double-count as "Resolved" later.
            var fuzzyClaimedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var c in current.Clashes)
            {
                ClashRecord old = null;
                bool fuzzyMatch = false;
                if (priorByIdentity.TryGetValue(c.Identity ?? "", out old))
                {
                    priorByIdentity.Remove(c.Identity);
                }
                else
                {
                    // E1: Fall through to fuzzy match if exact identity didn't hit.
                    var key = MakeFuzzyKey(c);
                    if (fuzzyIndex.TryGetValue(key, out var bucket) && bucket.Count > 0)
                    {
                        // Pick the closest centroid in the bucket within threshold.
                        ClashRecord best = null;
                        double bestDistMm = double.MaxValue;
                        foreach (var candidate in bucket)
                        {
                            if (fuzzyClaimedIds.Contains(candidate.Identity ?? "")) continue;
                            double distMm = CentroidDistanceMm(c, candidate);
                            if (distMm <= FuzzyCentroidThresholdMm && distMm < bestDistMm)
                            {
                                best = candidate;
                                bestDistMm = distMm;
                            }
                        }
                        if (best != null)
                        {
                            old = best;
                            fuzzyMatch = true;
                            fuzzyClaimedIds.Add(best.Identity ?? "");
                            // Drop from priorByIdentity so it doesn't count as resolved.
                            priorByIdentity.Remove(best.Identity ?? "");
                        }
                    }
                }

                if (old != null)
                {
                    // Carry over first-seen, ID, state (unless old was resolved — then reintroduce).
                    c.Id = old.Id;
                    c.FirstSeenUtc = old.FirstSeenUtc;
                    c.LastSeenUtc = now;
                    c.LinkedIssueGuid = old.LinkedIssueGuid;
                    // E10: Carry recurrence count forward. Increment on
                    //      Reintroduced (resolved-then-back), preserve on
                    //      Active. Persisted on the new ClashRecord field.
                    c.RecurrenceCount = old.RecurrenceCount;
                    if (old.State == "Resolved" || old.State == "Void")
                    {
                        c.State = "Reintroduced";
                        c.StateHistory = old.StateHistory ?? new List<StateTransition>();
                        c.RecurrenceCount = old.RecurrenceCount + 1;
                        string by = fuzzyMatch ? "system (fuzzy match)" : "system";
                        c.StateHistory.Add(new StateTransition { AtUtc = now, To = "Reintroduced", By = by });
                        current.Stats.Reintroduced++;

                        // F1: Repeat-offender severity auto-escalation. Three
                        //     resolve→reintroduce cycles bumps the severity one
                        //     tier so it surfaces in coordinator triage. One-way:
                        //     never auto-demotes.
                        if (c.RecurrenceCount >= RepeatOffenderEscalateAt)
                        {
                            string promoted = PromoteSeverity(c.Severity);
                            if (!string.Equals(promoted, c.Severity, StringComparison.OrdinalIgnoreCase))
                            {
                                c.StateHistory.Add(new StateTransition
                                {
                                    AtUtc = now,
                                    To = $"Severity escalated {c.Severity} → {promoted} (recurrence {c.RecurrenceCount})",
                                    By = "system"
                                });
                                c.Severity = promoted;
                            }
                        }
                    }
                    else
                    {
                        c.State = old.State ?? "Active";
                        c.StateHistory = old.StateHistory ?? new List<StateTransition>();
                        current.Stats.Active++;
                    }
                }
                else
                {
                    c.FirstSeenUtc = now;
                    c.LastSeenUtc = now;
                    c.State = "New";
                    c.RecurrenceCount = 0;
                    c.StateHistory.Add(new StateTransition { AtUtc = now, To = "New", By = "system" });
                    current.Stats.New++;
                }
            }

            // Anything left in priorByIdentity → resolved (not present this run).
            // F13: Annotate the prior records' StateHistory before their
            //      identities lapse, so the audit trail shows WHY they
            //      resolved. Calling code is responsible for archiving the
            //      modified prior record alongside the current run (F3).
            current.Stats.Resolved = priorByIdentity.Count;
            foreach (var kv in priorByIdentity)
            {
                var resolved = kv.Value;
                if (resolved.StateHistory == null) resolved.StateHistory = new List<StateTransition>();
                if (resolved.State != "Resolved" && resolved.State != "Void")
                {
                    resolved.State = "Resolved";
                    resolved.StateHistory.Add(new StateTransition
                    {
                        AtUtc = now,
                        To = "Resolved",
                        By = "geometric (no longer detected)"
                    });
                }
            }

            current.PreviousRunId = prior?.RunId;
        }

        /// <summary>
        /// E1: Canonical (elementA, elementB, pairId) key with sorted ids
        /// so (A,B) and (B,A) hit the same fuzzy bucket.
        /// </summary>
        private static (int A, int B, string Pair) MakeFuzzyKey(ClashRecord c)
        {
            int a = c.ElementA?.ElementId ?? 0;
            int b = c.ElementB?.ElementId ?? 0;
            int lo = Math.Min(a, b);
            int hi = Math.Max(a, b);
            return (lo, hi, c.MatrixPairId ?? "");
        }

        private static double CentroidDistanceMm(ClashRecord a, ClashRecord b)
        {
            if (a?.Centroid == null || b?.Centroid == null) return double.MaxValue;
            if (a.Centroid.Length < 3 || b.Centroid.Length < 3) return double.MaxValue;
            double dx = (a.Centroid[0] - b.Centroid[0]) * 304.8;
            double dy = (a.Centroid[1] - b.Centroid[1]) * 304.8;
            double dz = (a.Centroid[2] - b.Centroid[2]) * 304.8;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// F1: Severity tier ladder. Returns the next tier, or input if at top.
        /// Unknown / null severity treated as MED so first promotion lands at HIGH.
        /// </summary>
        internal static string PromoteSeverity(string current)
        {
            switch ((current ?? "").ToUpperInvariant())
            {
                case "LOW": return "MED";
                case "MED":
                case "MEDIUM": return "HIGH";
                case "HIGH": return "CRITICAL";
                case "CRITICAL": return "CRITICAL";
                default: return "HIGH";
            }
        }
    }
}
