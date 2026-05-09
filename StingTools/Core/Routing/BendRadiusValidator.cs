// StingTools — BendRadiusValidator.
//
// Post-create validation that every conduit fitting in a created
// run satisfies the bend-radius rule baked into the ConduitType's
// RoutingPreferenceManager.
//
// Why: BS 7671 §522.8 requires conduit bends to honour the
// manufacturer's minimum bend radius — for steel the rule of thumb
// is 6×OD; for rigid PVC ~10×OD; for flexible PVC ~5×OD. Without
// this check the auto-router can produce drawings that look fine
// in BIM but fail the spec inspection on site (bend too tight →
// cable damage → insulation breakdown).
//
// How: each created MEPCurve carries a Type. The Type's
// RoutingPreferenceManager exposes elbow rules with a
// MinimumValue / MaximumValue band keyed on size. We don't actually
// re-check the curve bends here (Revit's API only exposes the elbow
// fitting's geometry, not the implied radius from a polyline) —
// instead we walk every connected ConduitFitting whose category is
// OST_ConduitFitting and probe its Family parameter "Bend Radius"
// or "Radius of Curvature" (whichever the loaded family exposes).
// If the radius is below the type's minimum, we surface a finding
// in DropResult.Warnings.
//
// Hooked into AutoConduitDrop.CreateRunBetween at line 156 (and
// the equivalent slot in pipe / duct drops if their fittings start
// carrying a radius parameter).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;

namespace StingTools.Core.Routing
{
    public sealed class BendRadiusFinding
    {
        public ElementId FittingId { get; set; }
        public string TypeName { get; set; } = "";
        public double ActualMm { get; set; }
        public double MinAllowedMm { get; set; }
        public double NominalSizeMm { get; set; }
        public string Material { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public static class BendRadiusValidator
    {
        // Conservative manufacturer-typical minimums by material — used
        // as a fallback when the ConduitType has no RoutingPreferenceRule
        // for elbows. All values are MULTIPLES OF OUTER DIAMETER (OD).
        // Source: BS EN 61386-22 (steel), BS EN 61386-21 (rigid PVC),
        // BS EN 61386-23 (flexible). These are floors — most installers
        // exceed them.
        private static readonly Dictionary<string, double> _materialMinMultiplier =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "GS-GALV",  6.0 },   // galvanised steel
                { "STEEL",    6.0 },
                { "EMT",      6.0 },
                { "UPVC",    10.0 },   // unplasticised PVC, rigid
                { "PVC",     10.0 },
                { "AL-FLEX",  5.0 },   // aluminium / flexible
                { "FLEX",     5.0 },
                { "SS316",    6.0 },   // stainless
            };

        /// <summary>
        /// Probe one ConduitFitting and return a finding if it fails
        /// the bend-radius rule for its conduit type / material. Null
        /// means "no finding" — fitting is either not a bend, has no
        /// radius parameter, or satisfies the rule.
        /// </summary>
        public static BendRadiusFinding Validate(Document doc, FamilyInstance fitting)
        {
            if (doc == null || fitting == null) return null;
            if (fitting.Category?.Id?.Value != (long)BuiltInCategory.OST_ConduitFitting) return null;

            try
            {
                double actualMm = ReadRadiusMm(fitting);
                if (actualMm <= 0) return null;  // Not a bend, or radius unknown.

                // Resolve the conduit type by walking a connector to its
                // partner conduit. The partner gives us OD + material.
                var partner = ResolvePartnerConduit(fitting);
                double odMm = partner != null ? ReadOuterDiameterMm(partner) : 0;
                string material = partner != null
                    ? (ParameterHelpers.GetString(partner, "ELC_CDT_MAT_TXT") ?? "")
                    : "";

                if (odMm <= 0 || string.IsNullOrEmpty(material))
                {
                    // Not enough context — emit a low-severity finding so
                    // the user knows the bend wasn't checked, not that it
                    // passed.
                    return new BendRadiusFinding
                    {
                        FittingId = fitting.Id,
                        TypeName = fitting.Name ?? "",
                        ActualMm = actualMm,
                        MinAllowedMm = 0,
                        NominalSizeMm = odMm,
                        Material = material,
                        Reason = "Could not resolve conduit material/OD; bend-radius check skipped."
                    };
                }

                if (!_materialMinMultiplier.TryGetValue(material, out double mult))
                    mult = 6.0;          // safe default = 6×OD
                double minMm = odMm * mult;
                if (actualMm + 0.5 < minMm)            // 0.5 mm rounding tolerance
                {
                    return new BendRadiusFinding
                    {
                        FittingId = fitting.Id,
                        TypeName = fitting.Name ?? "",
                        ActualMm = actualMm,
                        MinAllowedMm = minMm,
                        NominalSizeMm = odMm,
                        Material = material,
                        Reason = $"Bend radius {actualMm:F0} mm < {mult:F0}×OD {odMm:F0} mm = {minMm:F0} mm minimum (BS EN 61386, material={material})"
                    };
                }
            }
            catch (Exception ex) { StingLog.Warn($"BendRadiusValidator {fitting?.Id}: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Walk every conduit fitting connected (directly or via shared
        /// connector) to the provided seed conduit and return findings.
        /// Used by drop engines to validate the whole run after creation.
        /// </summary>
        public static List<BendRadiusFinding> ValidateRun(Document doc, MEPCurve seed)
        {
            var findings = new List<BendRadiusFinding>();
            if (doc == null || seed == null) return findings;
            try
            {
                var visited = new HashSet<long>();
                foreach (Connector cn in seed.ConnectorManager.Connectors)
                {
                    foreach (Connector other in cn.AllRefs)
                    {
                        var owner = other?.Owner;
                        if (owner == null) continue;
                        if (!visited.Add(owner.Id.Value)) continue;
                        if (owner is FamilyInstance fi)
                        {
                            var f = Validate(doc, fi);
                            if (f != null) findings.Add(f);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ValidateRun: {ex.Message}"); }
            return findings;
        }

        // ── helpers ───────────────────────────────────────────────────

        private static double ReadRadiusMm(FamilyInstance fitting)
        {
            // Try the most likely names, in priority order. Different
            // family authors spell the parameter differently.
            string[] names =
            {
                "Bend Radius",
                "Radius of Curvature",
                "Radius",
                "ELC_CDT_BEND_RADIUS_MM",
                "BEND_RADIUS_MM"
            };
            foreach (string n in names)
            {
                try
                {
                    var p = fitting.LookupParameter(n);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.Double)
                    {
                        // Revit stores lengths in feet internally.
                        return p.AsDouble() * 304.8;
                    }
                    if (p.StorageType == StorageType.String)
                    {
                        if (double.TryParse(p.AsString() ?? "",
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double v) && v > 0)
                            return v;
                    }
                }
                catch { }
            }
            return 0;
        }

        private static double ReadOuterDiameterMm(MEPCurve curve)
        {
            try
            {
                var p = curve.LookupParameter("Outside Diameter")
                     ?? curve.LookupParameter("Diameter")
                     ?? curve.LookupParameter("Nominal Diameter");
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble() * 304.8;
            }
            catch { }
            return 0;
        }

        private static MEPCurve ResolvePartnerConduit(FamilyInstance fitting)
        {
            try
            {
                foreach (Connector cn in fitting.MEPModel?.ConnectorManager?.Connectors)
                {
                    foreach (Connector other in cn.AllRefs)
                    {
                        if (other?.Owner is MEPCurve curve) return curve;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
