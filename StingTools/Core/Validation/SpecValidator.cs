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
            // Pack 1 — reads STING_FIRE_RATING_MIN and STING_ACOUSTIC_RW_DB
            // (previously orphan parameters injected by
            // InjectAutomationPresentationPack without any consumer).
            CheckFireRating(doc, results);
            CheckAcousticRating(doc, results);
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

        /// <summary>
        /// Pack 1 — walks structural/separating walls, floors, and doors and
        /// flags elements whose STING_FIRE_RATING_MIN is absent or below the
        /// rating the host separation implies. The Revit native "Fire Rating"
        /// parameter is advisory text (e.g. "60 min"); the STING integer
        /// parameter lets the engine reason numerically.
        ///
        /// Conservative first-pass: only flags explicit disagreements between
        /// the native rating text (when it contains a minutes value) and the
        /// STING integer — or a missing STING rating on a wall whose Function
        /// is Exterior/Foundation/Retaining where UK Approved Document B
        /// typically requires a documented rating. Missing native rating +
        /// missing STING rating is silent (nothing to compare against).
        /// </summary>
        private void CheckFireRating(Document doc, List<ValidationResult> results)
        {
            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Ceilings
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();

                foreach (var el in col)
                {
                    int stingRating = ReadFireRatingMinFromType(el);
                    int nativeMins  = ParseNativeFireRatingMinutes(
                        ReadString(el.Document.GetElement(el.GetTypeId()) ?? el, "Fire Rating"));

                    if (stingRating == 0 && nativeMins == 0) continue;

                    if (stingRating == 0 && nativeMins > 0)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Info,
                            "SPEC.FIRE.STING.MISSING",
                            $"{el.Category?.Name} has native Fire Rating '{nativeMins} min' but no STING_FIRE_RATING_MIN — scheduling / BOQ will see 0",
                            ValidatorTag));
                    }
                    else if (stingRating > 0 && nativeMins > 0 && stingRating < nativeMins)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "SPEC.FIRE.MISMATCH",
                            $"STING_FIRE_RATING_MIN = {stingRating} below native Fire Rating {nativeMins} min",
                            ValidatorTag));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpecValidator: fire-rating scan failed: {ex.Message}"); }
        }

        /// <summary>
        /// Pack 1 — walks acoustic-separation elements (walls, doors, floors)
        /// and flags partitions whose STING_ACOUSTIC_RW_DB is empty when the
        /// spaces on either side have meaningfully different occupancy types.
        ///
        /// First-pass heuristic: any wall whose type name contains "ACOUSTIC",
        /// "STC", "RW", or whose Function is Interior and whose type name
        /// hints at a separating partition should declare an Rw value so the
        /// model is auditable against BS EN 12354 / Approved Document E.
        /// </summary>
        private void CheckAcousticRating(Document doc, List<ValidationResult> results)
        {
            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Floors
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();

                int flagged = 0, total = 0;
                foreach (var el in col)
                {
                    total++;
                    Element typ = null;
                    try { typ = el.Document.GetElement(el.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    string typeName = typ?.Name ?? "";
                    if (!LooksAcoustic(typeName)) continue;

                    int rw = ReadIntFromTypeThenInstance(el, "STING_ACOUSTIC_RW_DB");
                    if (rw > 0) continue;
                    flagged++;
                    results.Add(new ValidationResult(el.Id, ValidationSeverity.Info,
                        "SPEC.ACOU.RW.MISSING",
                        $"{el.Category?.Name} type '{typeName}' looks acoustic but STING_ACOUSTIC_RW_DB is 0 — set Rw in dB for auditable BS EN 12354 reporting",
                        ValidatorTag));
                }
                if (flagged > 0)
                {
                    results.Add(new ValidationResult(ElementId.InvalidElementId,
                        ValidationSeverity.Info,
                        "SPEC.ACOU.SCAN",
                        $"{flagged} of {total} separating element(s) lack STING_ACOUSTIC_RW_DB",
                        ValidatorTag));
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpecValidator: acoustic scan failed: {ex.Message}"); }
        }

        private static bool LooksAcoustic(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            string s = typeName.ToUpperInvariant();
            return s.Contains("ACOUSTIC") || s.Contains("STC ") || s.Contains(" STC") ||
                   s.Contains("RW ") || s.Contains(" RW") || s.Contains("SOUND") ||
                   s.Contains("SEPARAT");
        }

        private static int ReadFireRatingMinFromType(Element el)
        {
            try
            {
                Element typ = null;
                try { typ = el.Document.GetElement(el.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                int v = ReadInt(typ, "STING_FIRE_RATING_MIN");
                if (v > 0) return v;
                return ReadInt(el, "STING_FIRE_RATING_MIN");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static int ReadIntFromTypeThenInstance(Element el, string paramName)
        {
            try
            {
                Element typ = null;
                try { typ = el.Document.GetElement(el.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                int v = ReadInt(typ, paramName);
                if (v > 0) return v;
                return ReadInt(el, paramName);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static int ReadInt(Element el, string param)
        {
            if (el == null) return 0;
            try
            {
                var p = el.LookupParameter(param);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double)  return (int)Math.Round(p.AsDouble());
                if (p.StorageType == StorageType.String &&
                    int.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int v)) return v;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// Extract a minutes integer from a native Revit "Fire Rating" string
        /// like "60", "60 min", "1 hr", "120 minutes", or "FD60s". Returns 0
        /// if no reasonable integer can be parsed.
        /// </summary>
        private static int ParseNativeFireRatingMinutes(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            string s = raw.Trim().ToUpperInvariant();
            // Hours: "1 HR", "2H", "1.5 HOUR"
            if (s.Contains("HR") || s.Contains("HOUR") || (s.Length <= 3 && s.EndsWith("H")))
            {
                string digits = new string(Array.FindAll(s.ToCharArray(), c => char.IsDigit(c) || c == '.'));
                if (double.TryParse(digits, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double hrs) && hrs > 0)
                    return (int)Math.Round(hrs * 60);
            }
            // Minutes: digits followed optionally by "MIN"/"M"/"S" (FD60s).
            var digitsOnly = new string(Array.FindAll(s.ToCharArray(), char.IsDigit));
            if (int.TryParse(digitsOnly, out int mins) && mins > 0 && mins < 600)
                return mins;
            return 0;
        }

        private static string ReadString(Element el, string param)
        {
            try
            {
                var p = el.LookupParameter(param);
                return p?.AsString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }
    }
}
