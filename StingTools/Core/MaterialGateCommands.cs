using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.UI;

namespace StingTools.Core
{
    /// <summary>
    /// I-3 — Workflow-step adapters for the four material gates.
    /// Each is thin: invoke the matching MatActions method. Workflow
    /// JSON references them via commandTag = "MaterialGate_*".
    /// </summary>
    public static class MaterialGateCommands
    {
        [Transaction(TransactionMode.ReadOnly)]
        [Regeneration(RegenerationOption.Manual)]
        public class CoverageGateCommand : IExternalCommand
        {
            public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            { MatActions.RunCoverageCheck(data?.Application); return Result.Succeeded; }
        }

        [Transaction(TransactionMode.ReadOnly)]
        [Regeneration(RegenerationOption.Manual)]
        public class SustainabilityGateCommand : IExternalCommand
        {
            public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            { MatActions.RunSustainabilityGate(data?.Application); return Result.Succeeded; }
        }

        [Transaction(TransactionMode.ReadOnly)]
        [Regeneration(RegenerationOption.Manual)]
        public class HealthcareGateCommand : IExternalCommand
        {
            public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            { MatActions.RunHealthcareGate(data?.Application); return Result.Succeeded; }
        }

        [Transaction(TransactionMode.ReadOnly)]
        [Regeneration(RegenerationOption.Manual)]
        public class FireWallGateCommand : IExternalCommand
        {
            public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            { MatActions.RunFireWallGate(data?.Application); return Result.Succeeded; }
        }
    }
}
