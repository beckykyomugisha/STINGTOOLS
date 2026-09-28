using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Routing
{
    public enum DiameterOutcome { Applied, Snapped, NotApplied }

    /// <summary>
    /// Writes a computed conduit diameter (mm) onto a created Conduit and
    /// reports what the model actually holds afterwards. Revit stores length in
    /// internal feet and snaps conduit diameters to the sizes its conduit
    /// standard defines, so the read-back — not the request — is the truth.
    /// Kept out of ConduitRouteEngine because that file is compiled into
    /// StingTools.Routing.Tests against Revit stubs that have no Conduit.
    /// </summary>
    internal static class ConduitDiameterApplier
    {
        public static DiameterOutcome Apply(Conduit conduit, double diameterMm,
            out double actualMm, out string note)
        {
            actualMm = 0; note = "";
            if (conduit == null || diameterMm <= 0)
            {
                note = "no conduit or no computed diameter";
                return DiameterOutcome.NotApplied;
            }
            try
            {
                var p = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (p == null || p.IsReadOnly)
                {
                    note = "diameter parameter not settable — left at type default";
                    return DiameterOutcome.NotApplied;
                }
                if (!p.Set(UnitUtils.ConvertToInternalUnits(diameterMm, UnitTypeId.Millimeters)))
                {
                    note = $"{diameterMm:0} mm rejected — left at type default";
                    return DiameterOutcome.NotApplied;
                }
                actualMm = UnitUtils.ConvertFromInternalUnits(conduit.Diameter, UnitTypeId.Millimeters);
                if (Math.Abs(actualMm - diameterMm) <= 0.5) return DiameterOutcome.Applied;
                note = $"requested {diameterMm:0} mm, model holds {actualMm:0.#} mm";
                return DiameterOutcome.Snapped;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConduitDiameterApplier {conduit.Id.Value} {diameterMm} mm: {ex.Message}");
                note = $"{diameterMm:0} mm rejected ({ex.Message}) — left at type default";
                return DiameterOutcome.NotApplied;
            }
        }
    }
}
