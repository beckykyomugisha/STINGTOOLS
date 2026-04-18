// ClashHistory.cs — diff current run against prior run by identity hash.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Clash
{
    public static class ClashHistory
    {
        public static void MergeWithPrior(ClashRunRecord current, ClashRunRecord prior)
        {
            if (current == null) return;
            var priorByIdentity = prior?.Clashes?.ToDictionary(c => c.Identity, c => c) ?? new Dictionary<string, ClashRecord>();
            var now = DateTime.UtcNow;
            current.Stats.New = 0;
            current.Stats.Active = 0;
            current.Stats.Reintroduced = 0;

            foreach (var c in current.Clashes)
            {
                if (priorByIdentity.TryGetValue(c.Identity, out var old))
                {
                    // Carry over first-seen, ID, state (unless old was resolved — then reintroduce).
                    c.Id = old.Id;
                    c.FirstSeenUtc = old.FirstSeenUtc;
                    c.LastSeenUtc = now;
                    c.LinkedIssueGuid = old.LinkedIssueGuid;
                    if (old.State == "Resolved" || old.State == "Void")
                    {
                        c.State = "Reintroduced";
                        c.StateHistory = old.StateHistory ?? new List<StateTransition>();
                        c.StateHistory.Add(new StateTransition { AtUtc = now, To = "Reintroduced", By = "system" });
                        current.Stats.Reintroduced++;
                    }
                    else
                    {
                        c.State = old.State ?? "Active";
                        c.StateHistory = old.StateHistory ?? new List<StateTransition>();
                        current.Stats.Active++;
                    }
                    priorByIdentity.Remove(c.Identity);
                }
                else
                {
                    c.FirstSeenUtc = now;
                    c.LastSeenUtc = now;
                    c.State = "New";
                    c.StateHistory.Add(new StateTransition { AtUtc = now, To = "New", By = "system" });
                    current.Stats.New++;
                }
            }

            // Anything left in priorByIdentity → resolved (not present this run).
            current.Stats.Resolved = priorByIdentity.Count;

            current.PreviousRunId = prior?.RunId;
        }
    }
}
