// Healthcare Pack HC-DEF-10 — clinical-equipment COBie handover entry point.
//
// Thin healthcare-panel command that runs the standard COBie 2.4 handover
// export. The exporter (COBieHandoverExportCommand) now emits the clinical
// Attribute/Job/Spare rows via ClinicalCobieBridge, so this is simply the
// discoverable healthcare-tagged entry that produces the enriched COBie set —
// not a parallel exporter.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Healthcare
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HealthcareCobieClinicalExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => new StingTools.Docs.COBieHandoverExportCommand().Execute(commandData, ref message, elements);
    }
}
