// StingTools v4 MVP — FillValidator.
//
// Walks conduit, cable tray, pipes and ducts and computes fill vs
// design. For electrical containment we read the cached fill values
// written by upstream design tools into ELC_CDT_CBL_FILL_PCT and
// ELC_CTR_FILL_PCT; for hydronic / pneumatic containment we compare
// computed velocity against CIBSE Guide C limits captured by the
// design tool in PLM_PPE_VELOCITY_MS / HVC_DCT_VELOCITY_MS.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public class FillValidator
    {
        public string Name => "FillValidator";
        private const string ValidatorTag = "FillValidator";

        // BS 7671 Appendix 4 / Table 4D5: containing >=2 conductors, 45%
        // fill maximum (single circuit). 35% common rule of thumb for
        // multi-circuit aggregation.
        public double ConduitMaxFillPct  { get; set; } = 45.0;
        public double CableTrayMaxFillPct { get; set; } = 50.0;

        // CIBSE Guide C 4.4.2: pipe velocity limits.
        public double PipeMaxVelocityMs  { get; set; } = 2.5;
        // CIBSE Guide B3 ductwork velocity guidance for low-velocity
        // commercial systems.
        public double DuctMaxVelocityMs  { get; set; } = 8.0;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            CheckCategory(doc, BuiltInCategory.OST_Conduit,    "ELC_CDT_CBL_FILL_PCT", ConduitMaxFillPct,  "FILL.CDT.OVER",  results);
            CheckCategory(doc, BuiltInCategory.OST_CableTray,  "ELC_CTR_FILL_PCT",     CableTrayMaxFillPct,"FILL.CTR.OVER",  results);
            CheckCategory(doc, BuiltInCategory.OST_PipeCurves, "PLM_PPE_VELOCITY_MS",  PipeMaxVelocityMs,  "VEL.PPE.OVER",   results);
            CheckCategory(doc, BuiltInCategory.OST_DuctCurves, "HVC_DCT_VELOCITY_MS",  DuctMaxVelocityMs,  "VEL.DCT.OVER",   results);
            return results;
        }

        private void CheckCategory(
            Document doc,
            BuiltInCategory cat,
            string paramName,
            double maxValue,
            string codeOver,
            List<ValidationResult> results)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    try
                    {
                        var p = el.LookupParameter(paramName);
                        if (p == null) continue;
                        double val = ReadDouble(p);
                        if (val <= 0) continue;

                        // §5.3 — family-level override via FILL_MAX_PCT.
                        // Lets a fire-alarm-only conduit type declare a
                        // tighter 25% limit (BS 5839) without touching the
                        // global BS 7671 ConduitMaxFillPct default. Electrical
                        // categories honour the override; velocity-based pipe
                        // and duct checks keep their CIBSE defaults because
                        // velocity isn't a fill ratio.
                        double effectiveMax = maxValue;
                        if (cat == BuiltInCategory.OST_Conduit || cat == BuiltInCategory.OST_CableTray)
                        {
                            var routing = Routing.RoutingParamReader.Read(el);
                            if (routing.FillMaxPct > 0 && routing.FillMaxPct < effectiveMax)
                                effectiveMax = routing.FillMaxPct;
                        }

                        if (val > effectiveMax)
                        {
                            string note = effectiveMax < maxValue ? $" (family FILL_MAX_PCT override)" : "";
                            results.Add(new ValidationResult(
                                el.Id,
                                ValidationSeverity.Warning,
                                codeOver,
                                $"{paramName} = {val:F1} exceeds {effectiveMax:F1}{note}",
                                ValidatorTag));
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"FillValidator: read {paramName} on {el?.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FillValidator: scan category {cat} failed: {ex.Message}");
            }
        }

        private double ReadDouble(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Double:
                    return p.AsDouble();
                case StorageType.Integer:
                    return p.AsInteger();
                case StorageType.String:
                    if (double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
                    return 0;
                default:
                    return 0;
            }
        }
    }
}
