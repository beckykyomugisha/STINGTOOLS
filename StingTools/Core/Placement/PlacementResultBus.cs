using StingTools.Core;
// PlacementResultBus — event bus that routes run summaries from placement,
// tagging, and symbol commands to the Placement Centre panel and dock-panel
// inline result strips without creating direct dependencies between UI layers.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

namespace StingTools.Core.Placement
{
    /// <summary>Run summary posted by any placement/annotation/symbol command.</summary>
    public sealed class PlacementRunSummary
    {
        /// <summary>"Fixtures" | "Tags" | "Symbols" | "Routing" | "Annotation"</summary>
        public string Source { get; set; }
        /// <summary>Stamped DrawingType id on the active view, or null.</summary>
        public string DrawingTypeId { get; set; }
        /// <summary>Resolved ViewStylePack id, or null.</summary>
        public string PackId { get; set; }
        /// <summary>Short one-line result (e.g. "12 fixtures placed in 4 rooms").</summary>
        public string Headline { get; set; }
        /// <summary>Metric lines shown in result panel (count rows).</summary>
        public List<string> Metrics { get; set; } = new List<string>();
        /// <summary>Warning lines (mismatches, missing families, etc.).</summary>
        public List<string> Warnings { get; set; } = new List<string>();
        /// <summary>Element ids affected — used for selection/highlight.</summary>
        public List<ElementId> AffectedIds { get; set; } = new List<ElementId>();
        public DateTime RunUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Static event bus. Commands call Publish(); the Placement Centre and
    /// dock-panel result strips subscribe to ResultPublished.
    /// Thread-safe: invocation marshalled back through the WPF Dispatcher
    /// by the subscriber (not here) since Revit commands run on the API thread.
    /// </summary>
    public static class PlacementResultBus
    {
        /// <summary>Fired every time a placement/annotation/symbol run completes.</summary>
        public static event Action<PlacementRunSummary> ResultPublished;

        /// <summary>The most recent summary published this session.</summary>
        public static PlacementRunSummary LastResult { get; private set; }

        /// <summary>
        /// Publish a run summary. Exceptions in subscribers are swallowed
        /// so a bad subscriber can never abort the command that just ran.
        /// </summary>
        public static void Publish(PlacementRunSummary summary)
        {
            if (summary == null) return;
            LastResult = summary;
            var handlers = ResultPublished;
            if (handlers == null) return;
            foreach (var h in handlers.GetInvocationList())
            {
                try { ((Action<PlacementRunSummary>)h)(summary); }
                catch (Exception ex)
                { StingLog.Warn($"PlacementResultBus subscriber threw: {ex.Message}"); }
            }
        }
    }
}
