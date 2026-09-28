// StingTools — Drawing Template Manager · one-click drawing-production setup
//
// Runs WORKFLOW_DrawingProductionSetup.json (params → title blocks → tag
// families → text / dimension styles → view templates → AEC filters → view
// types → presentation fills → flow arrow → managed templates → pre-flight).
//
// It is a COMMAND rather than a preset-name button so that it is a workflow
// step too: a project-kickoff workflow can include "set up drawing
// production" as one line instead of repeating twelve.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingProductionSetupCommand : IExternalCommand
    {
        public const string PresetName = "DrawingProductionSetup";

        // A preset that (one day) lists this command must not run itself forever.
        [ThreadStatic] private static bool _running;

        /// <summary>
        /// Run without the workflow's report dialog; the caller reads <see cref="LastOutcome"/>
        /// and reports it. The result is then Failed when a required step failed.
        /// </summary>
        public bool Unattended { get; set; }

        /// <summary>What the last run of this instance did (null until it has run).</summary>
        internal WorkflowEngine.WorkflowOutcome LastOutcome { get; private set; }

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            if (_running) { msg = "Drawing-production setup is already running."; return Result.Cancelled; }
            var doc = ParameterHelpers.GetContext(data)?.Doc;
            var preset = WorkflowEngine.GetAvailablePresets(doc)
                .FirstOrDefault(p => string.Equals(p.Name, PresetName, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
            {
                msg = $"Workflow preset '{PresetName}' was not found — WORKFLOW_{PresetName}.json is missing from the data folder.";
                LastOutcome = new WorkflowEngine.WorkflowOutcome { PresetName = PresetName, Failed = 1, Report = msg };
                if (!Unattended) TaskDialog.Show("STING — Drawing Production Setup", msg);
                return Result.Failed;
            }
            try
            {
                _running = true;
                var result = WorkflowEngine.ExecutePreset(preset, data, els, showReport: !Unattended, out var outcome);
                LastOutcome = outcome;
                return result;
            }
            finally { _running = false; }
        }
    }
}
