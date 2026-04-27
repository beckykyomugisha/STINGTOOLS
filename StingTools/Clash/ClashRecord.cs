// ClashRecord.cs — persisted clash schema (clashes.json).
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Clash
{
    public sealed class ClashElementRecord
    {
        public string IfcGuid;
        public string UniqueId;
        public int ElementId;
        public string DocGuid;
        public int LinkInstanceId;
        public string Category;
        public string System;
    }

    public sealed class ClashRecord
    {
        public string Id;               // e.g. CLH-20260418-00001
        public string Identity;         // hash
        public string GroupId;
        public DateTime FirstSeenUtc;
        public DateTime LastSeenUtc;
        public string State;            // New / Active / Assigned / InReview / Resolved / Reintroduced / Void
        public List<StateTransition> StateHistory { get; set; } = new List<StateTransition>();
        public string MatrixPairId;
        public string Severity;
        public string Tolerance;
        public ClashElementRecord ElementA;
        public ClashElementRecord ElementB;
        public float VolumeMm3;
        public float[] AabbMin;
        public float[] AabbMax;
        public float[] Centroid;
        public string LinkedIssueGuid;
        public string ResolutionHint;   // from ResolutionHeuristics
        public double? MlScore;
        public string MlLabel;
        // A3: classification of the kept clash — "hard" for a true intersection,
        // "clearance" when the AABB overlap depth is within the matrix cell's
        // CLEARANCE_xx mm tolerance. Defaulted to null/empty so older
        // clashes.json round-trips cleanly.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Kind;
        // C1: triage score from ClashTriageEngine. 0..1, higher = more critical.
        // Defaulted to 0 so older clashes.json round-trips cleanly.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public double TriageScore;
    }

    public sealed class StateTransition
    {
        public DateTime AtUtc;
        public string To;
        public string By;
    }

    public sealed class ClashGroupRecord
    {
        public string Id;
        public string Kind;   // spatial / element / pattern
        public string Anchor;
        public int Size;
        public string Status;
        public string Assignee;
        public DateTime? DueDateUtc;
    }

    public sealed class ClashRunRecord
    {
        public string Schema = "planscape.clash/1";
        public string RunId;
        public string PreviousRunId;
        public string MatrixFile;
        public string RulesFile;
        public string ExclusionsFile;
        public long DurationMs;
        public ClashRunStats Stats = new ClashRunStats();
        public List<ClashRecord> Clashes { get; set; } = new List<ClashRecord>();
        public List<ClashGroupRecord> Groups { get; set; } = new List<ClashGroupRecord>();
    }

    public sealed class ClashRunStats
    {
        public int Raw;
        public int Tier1Filtered;
        public int Excluded;
        public int Groups;
        public int New;
        public int Active;
        public int Resolved;
        public int Reintroduced;
    }
}
