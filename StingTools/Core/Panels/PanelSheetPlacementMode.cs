// StingTools — Panel_PlaceOnSheets: which placement mode runs
//
// The command has four modes. Three were for a person: GuidedManual (the default)
// lists the schedules still to drag onto sheets, ViewSchedule puts circuit schedules on
// ONE sheet picked in the panel, PDF is a stub. A workflow preset got GuidedManual — a
// list to read, in a modal dialog, with nothing placed.
//
// AutoSheets is the unattended mode: one circuit ViewSchedule per board (reused on a
// re-run), each on its own elec-panel-schedule-A3 sheet through the drawing-type sheet
// path, so it is stamped, numbered and found again next time. Inside a preset it is the
// default; params.mode can still name another mode, except the two that need a person.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Panels
{
    public static class PanelSheetPlacementMode
    {
        public const string GuidedManual = "GuidedManual";
        public const string ViewSchedule = "ViewSchedule";
        public const string Pdf = "PDF";
        public const string AutoSheets = "AutoSheets";

        /// <summary>
        /// The mode to run. Outside a preset: the panel's choice (GuidedManual when
        /// none). Inside one: params.mode, or AutoSheets when absent. Null with
        /// <paramref name="error"/> for an unknown mode, or — in a preset — a mode that
        /// needs a person (GuidedManual lists schedules to drag; ViewSchedule needs a
        /// sheet picked in the panel).
        /// </summary>
        public static string Resolve(bool inPreset, string stepParam, string panelMode, out string error)
        {
            error = null;
            if (!inPreset)
            {
                var m = Canonical(panelMode);
                return m ?? GuidedManual;
            }
            if (string.IsNullOrWhiteSpace(stepParam)) return AutoSheets;
            var c = Canonical(stepParam);
            if (c == null)
            { error = $"params.mode '{stepParam.Trim()}' is not AutoSheets, ViewSchedule, GuidedManual or PDF."; return null; }
            if (c == GuidedManual || c == ViewSchedule || c == Pdf)
            { error = $"params.mode '{c}' needs a person (it lists schedules to drag, or places on a sheet picked in the panel); use AutoSheets in a workflow."; return null; }
            return c;
        }

        private static string Canonical(string raw)
        {
            var s = (raw ?? "").Trim();
            if (s.Equals(GuidedManual, StringComparison.OrdinalIgnoreCase)) return GuidedManual;
            if (s.Equals(ViewSchedule, StringComparison.OrdinalIgnoreCase)) return ViewSchedule;
            if (s.Equals(Pdf, StringComparison.OrdinalIgnoreCase)) return Pdf;
            if (s.Equals(AutoSheets, StringComparison.OrdinalIgnoreCase) || s.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return AutoSheets;
            return null;
        }
    }
}
