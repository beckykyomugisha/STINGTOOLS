// StingTools — COBie export settings for a workflow preset step
//
// COBieExportCommand configures its export in a four-page wizard. Inside a workflow
// preset (WorkflowEngine.IsRunningPreset) nobody can drive it, so the step's params
// build the same COBieExportSettings the wizard would return, with the wizard's own
// defaults:
//
//   preset              COBie project preset key (COMMERCIAL_OFFICE, HEALTHCARE_NHS, …)
//                       default: the COBiePresetKey extra param a caller set, else the
//                       healthcare preset for PRJ_ORG_HEALTH_PACK_PROFILE_TXT, else FULL
//                       (all worksheets, no preset). "FULL" names FULL explicitly.
//   worksheets          list of worksheet names       default: all
//   format              xlsx | csv | both             default: both (the wizard's default)
//   outputDir           folder                        default: the command's own
//                       <BIM manager dir>/COBie_V24_<yyyyMMdd>
//   exportBelowGate     true | false                  default: false — below the 60 %
//                       tag-compliance gate the step fails, as the dialog's default does
//   refreshContainers   true | false                  default: true — when discipline
//                       containers look stale, rewrite them first (the dialog's
//                       recommended choice)
//
// The data-quality warning gate (GapAnalysisEngine.CheckCOBieWarningQuality) is advisory
// and does not stop an unattended export; its reason is carried into the step message.
// An unknown preset or worksheet name fails the step and names it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BIMManager
{
    internal static class COBiePresetSettings
    {
        internal sealed class Gates
        {
            public bool ExportBelowGate;
            public bool RefreshContainers = true;
        }

        internal static bool Build(Document doc, out COBieExportSettings settings, out Gates gates, out string error)
        {
            settings = null;
            gates = new Gates();
            error = null;

            // Preset key.
            string preset = WorkflowEngine.StepParam("preset").Trim();
            if (preset.Length == 0) preset = StingCommandHandler.GetExtraParam("COBiePresetKey") ?? "";
            if (preset.Length == 0)
            {
                try
                {
                    string hp = doc?.ProjectInformation?.LookupParameter("PRJ_ORG_HEALTH_PACK_PROFILE_TXT")?.AsString();
                    if (!string.IsNullOrEmpty(hp)) preset = COBieExportWizard.PresetForHealthProfile(hp);
                }
                catch (Exception ex) { StingLog.Warn($"COBie preset step: health profile read: {ex.Message}"); }
            }
            string presetKey = null;
            if (preset.Length > 0 && !string.Equals(preset, "FULL", StringComparison.OrdinalIgnoreCase))
            {
                presetKey = BIMManagerEngine.COBiePresets.Keys
                    .FirstOrDefault(k => string.Equals(k, preset, StringComparison.OrdinalIgnoreCase));
                if (presetKey == null)
                {
                    error = $"params.preset = '{preset}' is not a COBie preset (FULL | " +
                            string.Join(" | ", BIMManagerEngine.COBiePresets.Keys.OrderBy(k => k)) + ").";
                    return false;
                }
            }

            // Worksheets.
            var wanted = PresetStepInputs.ParseList(WorkflowEngine.StepParam("worksheets"));
            var known = BIMManagerEngine.COBieWorksheets.Keys.ToList();
            var unknown = PresetStepInputs.Unknown(wanted, known);
            if (unknown.Count > 0)
            {
                error = $"params.worksheets names {string.Join(", ", unknown)} — not one of: {string.Join(", ", known)}.";
                return false;
            }
            var selected = new HashSet<string>(
                wanted.Select(w => known.First(k => string.Equals(k, w, StringComparison.OrdinalIgnoreCase))));

            // Format, gates.
            if (!PresetStepInputs.TryChoice("format", WorkflowEngine.StepParam("format"), "both",
                    new[] { new PresetStepInputs.Choice("both", "all", "xlsx+csv", "csv+xlsx"),
                            new PresetStepInputs.Choice("xlsx", "excel"),
                            new PresetStepInputs.Choice("csv") },
                    out var format, out error)
                || !PresetStepInputs.TryBool("exportBelowGate", WorkflowEngine.StepParam("exportBelowGate"), false,
                    out gates.ExportBelowGate, out error)
                || !PresetStepInputs.TryBool("refreshContainers", WorkflowEngine.StepParam("refreshContainers"), true,
                    out gates.RefreshContainers, out error))
                return false;

            string outDir = WorkflowEngine.StepParam("outputDir").Trim();
            if (outDir.Length > 0 && outDir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = $"params.outputDir = '{outDir}' is not a valid folder path.";
                return false;
            }

            settings = new COBieExportSettings
            {
                PresetKey = presetKey,
                SelectedWorksheets = selected,
                OutputDir = outDir,
                ExportXLSX = format != "csv",
                ExportCSV = format != "xlsx",
                // No progress window: nobody is watching it, and it is modeless UI.
                StreamingExport = false,
            };
            return true;
        }
    }
}
