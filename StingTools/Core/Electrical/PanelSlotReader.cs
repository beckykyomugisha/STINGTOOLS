// PanelSlotReader — the Revit half of PanelSlotRules.
//
// One place that answers "how many slots does this panel have?" and "how far
// apart are a multi-pole breaker's slots on it?", for BatchAssignCircuits and
// the panel door diagram (ELEC-15 / ELEC-16).

using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    internal static class PanelSlotReader
    {
        /// <summary>
        /// RBS_ELEC_MAX_POLE_BREAKERS ("Max Number of Single Pole Breakers"), then
        /// RBS_ELEC_NUMBER_OF_CIRCUITS ("Max Number of Circuits"). Null when the
        /// family reports neither — callers report the panel instead of inventing a
        /// size.
        /// </summary>
        public static int? PanelSlotCount(FamilyInstance panel)
        {
            if (panel == null) return null;
            return PanelSlotRules.ChooseSlotCount(
                ReadInt(panel, BuiltInParameter.RBS_ELEC_MAX_POLE_BREAKERS),
                ReadInt(panel, BuiltInParameter.RBS_ELEC_NUMBER_OF_CIRCUITS));
        }

        /// <summary>
        /// The slot step for multi-pole breakers on this panel, from its panel
        /// schedule's configuration (PanelScheduleData.PanelConfiguration) and type
        /// (a switchboard schedule is consecutive). <paramref name="known"/> is false
        /// when the panel has no schedule view to read; the result is then 2.
        /// </summary>
        public static int SlotStep(Document doc, FamilyInstance panel, out bool known)
            => SlotStep(FindScheduleView(doc, panel), out known);

        /// <summary>The slot step read from a panel schedule view (see above).</summary>
        public static int SlotStep(PanelScheduleView psv, out bool known)
        {
            var numbering = SlotNumbering.Unknown;
            bool switchboard = false;
            try
            {
                var data = psv?.GetTableData();
                if (data != null)
                {
                    switchboard = data.ScheduleType == PanelScheduleType.Switchboard;
                    switch (data.PanelConfiguration)
                    {
                        case PanelConfiguration.OneColumn:              numbering = SlotNumbering.OneColumn; break;
                        case PanelConfiguration.TwoColumnsCircuitsAcross: numbering = SlotNumbering.TwoColumnsAcross; break;
                        case PanelConfiguration.TwoColumnsCircuitsDown: numbering = SlotNumbering.TwoColumnsDown; break;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PanelSlotReader.SlotStep {psv?.Id}: {ex.Message}"); }
            return PanelSlotRules.StepFor(numbering, switchboard, out known);
        }

        /// <summary>The panel's instance panel-schedule view, or null.</summary>
        public static PanelScheduleView FindScheduleView(Document doc, FamilyInstance panel)
        {
            if (doc == null || panel == null) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(PanelScheduleView))
                    .Cast<PanelScheduleView>()
                    .FirstOrDefault(v =>
                    {
                        try { return !v.IsPanelScheduleTemplate() && v.GetPanel() == panel.Id; }
                        catch { return false; }
                    });
            }
            catch (Exception ex) { StingLog.Warn($"PanelSlotReader.FindScheduleView {panel.Id}: {ex.Message}"); return null; }
        }

        private static int? ReadInt(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null || !p.HasValue) return null;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double) return (int)Math.Round(p.AsDouble());
            }
            catch (Exception ex) { StingLog.Warn($"PanelSlotReader.ReadInt {el?.Id} {bip}: {ex.Message}"); }
            return null;
        }
    }
}
