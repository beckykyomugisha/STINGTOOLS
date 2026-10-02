using Autodesk.Revit.DB;
using StingTools.Standards.NEC2023;

namespace StingTools.Core.Electrical
{
    /// <summary>
    /// The conductor material an element carries, through the one reader
    /// (<see cref="ConductorMaterialText.Resolve"/>): ELC_WIRE_COND_MAT_TXT recorded on the
    /// circuit / conduit / cable tray wins; else the caller's setting (a panel choice); else
    /// copper, ASSUMED — and the result says so for the command to show.
    /// </summary>
    public static class ConductorMaterialSource
    {
        public const string Param = "ELC_WIRE_COND_MAT_TXT";

        public static ResolvedConductorMaterial ForElement(Element el, string setting)
        {
            string recorded = el != null ? ParameterHelpers.GetString(el, "ELC_WIRE_COND_MAT_TXT") : null;
            return ConductorMaterialText.Resolve(recorded, setting, Param);
        }
    }
}
