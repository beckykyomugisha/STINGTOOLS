// StingTools — Design Option sidecar metadata POCOs.
//
// Stored at <project>/_BIM_COORD/design_options.json. Captures the
// intent layer that Revit itself cannot hold: why this option exists,
// when a decision is due, who's responsible, BOQ delta, carbon delta,
// linked issues / deliverables. Merged at runtime with the live Revit
// state by DesignOptionRegistry.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.DesignOptions
{
    /// <summary>Sidecar entry per design option set.</summary>
    public class DesignOptionSetMetadata
    {
        [JsonProperty("setName")]
        public string SetName { get; set; }

        /// <summary>Free-text — what question is this set answering?</summary>
        [JsonProperty("purpose")]
        public string Purpose { get; set; }

        /// <summary>FACADE / LAYOUT / VE / FIT_OUT / MEP_ROUTE / OTHER.</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }

        /// <summary>RIBA / project stage when the decision is due.</summary>
        [JsonProperty("decisionStage")]
        public string DecisionStage { get; set; }

        [JsonProperty("decisionDate")]
        public DateTime? DecisionDate { get; set; }

        [JsonProperty("decided")]
        public bool Decided { get; set; }

        /// <summary>Option-name of the decided / preferred alternative.</summary>
        [JsonProperty("decidedOption")]
        public string DecidedOption { get; set; }

        [JsonProperty("clientFacing")]
        public bool ClientFacing { get; set; }

        [JsonProperty("notes")]
        public string Notes { get; set; }

        [JsonProperty("options")]
        public List<DesignOptionMetadata> Options { get; set; } = new List<DesignOptionMetadata>();
    }

    /// <summary>Sidecar entry per option inside a set.</summary>
    public class DesignOptionMetadata
    {
        [JsonProperty("optionName")]
        public string OptionName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Capex delta vs the set's primary, in project currency.</summary>
        [JsonProperty("costDelta")]
        public double CostDelta { get; set; }

        /// <summary>Embodied carbon delta vs the set's primary, kgCO2e.</summary>
        [JsonProperty("carbonDelta")]
        public double CarbonDelta { get; set; }

        /// <summary>Floor-area delta vs the set's primary, m².</summary>
        [JsonProperty("areaDelta")]
        public double AreaDelta { get; set; }

        /// <summary>Issue ids referencing this option (server BimIssue or local guid).</summary>
        [JsonProperty("linkedIssues")]
        public List<string> LinkedIssues { get; set; } = new List<string>();

        /// <summary>Deliverable ids referencing this option (template engine).</summary>
        [JsonProperty("linkedDeliverables")]
        public List<string> LinkedDeliverables { get; set; } = new List<string>();

        /// <summary>Sheet numbers locked to this option.</summary>
        [JsonProperty("lockedSheets")]
        public List<string> LockedSheets { get; set; } = new List<string>();

        [JsonProperty("notes")]
        public string Notes { get; set; }
    }

    /// <summary>Root sidecar document.</summary>
    public class DesignOptionSidecar
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonProperty("updated")]
        public DateTime Updated { get; set; } = DateTime.UtcNow;

        [JsonProperty("sets")]
        public List<DesignOptionSetMetadata> Sets { get; set; } = new List<DesignOptionSetMetadata>();
    }
}
