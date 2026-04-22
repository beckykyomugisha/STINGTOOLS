// StingTools v4 MVP — SpecValidator.
//
// Walks pipes, cables, ducts and asserts material + rating match the
// system spec recorded in ASS_SYSTEM_TYPE_TXT and the discipline-
// specific spec parameters.
//
// First-pass implementation flags two common error modes:
//   1. Pipe with PLM_PPE_MAT_TXT empty when PLM_SYS_TXT is non-empty.
//   2. Cable with ELC_CBL_RATING_V missing or below ELC_SYS_NOMINAL_V.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public class SpecValidator
    {
        public string Name => "SpecValidator";
        private const string ValidatorTag = "SpecValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            CheckPipeMaterial(doc, results);
            CheckCableRating(doc, results);
            CheckDuctMaterial(doc, results);
            return results;
        }

        private void CheckPipeMaterial(Document doc, List<ValidationResult> results)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    string sys = ReadString(el, "PLM_SYS_TXT");
                    string mat = ReadString(el, "PLM_PPE_MAT_TXT");
                    if (string.IsNullOrEmpty(sys)) continue;
                    if (string.IsNullOrEmpty(mat))
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "SPEC.PPE.MAT.MISSING",
                            $"Pipe in system '{sys}' has no PLM_PPE_MAT_TXT", ValidatorTag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpecValidator: pipe scan failed: {ex.Message}"); }
        }

        private void CheckCableRating(Document doc, List<ValidationResult> results)
        {
            try
            {
                // OST_ElectricalCircuit covers cabling at the circuit level.
                var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_ElectricalCircuit)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    double cableV = ReadDouble(el, "ELC_CBL_RATING_V");
                    double sysV   = ReadDouble(el, "ELC_SYS_NOMINAL_V");
                    if (sysV <= 0) continue;
                    if (cableV <= 0)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "SPEC.CBL.RATING.MISSING",
                            $"Circuit at {sysV:F0}V has no ELC_CBL_RATING_V", ValidatorTag));
                    }
                    else if (cableV < sysV)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "SPEC.CBL.RATING.UNDER",
                            $"Cable rated {cableV:F0}V below system {sysV:F0}V", ValidatorTag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpecValidator: cable scan failed: {ex.Message}"); }
        }

        private void CheckDuctMaterial(Document doc, List<ValidationResult> results)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    string mat = ReadString(el, "HVC_DCT_MAT_TXT");
                    string sys = ReadString(el, "HVC_SYS_TXT");
                    if (string.IsNullOrEmpty(sys)) continue;
                    if (string.IsNullOrEmpty(mat))
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "SPEC.DCT.MAT.MISSING",
                            $"Duct in system '{sys}' has no HVC_DCT_MAT_TXT", ValidatorTag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpecValidator: duct scan failed: {ex.Message}"); }
        }

        private static string ReadString(Element el, string param)
        {
            try
            {
                var p = el.LookupParameter(param);
                return p?.AsString() ?? "";
            }
            catch { return ""; }
        }
        private static double ReadDouble(Element el, string param)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
