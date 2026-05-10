// StingTools — PenetrationCoverageValidator.
//
// Audits that every member that crosses a fire-rated barrier has a
// matching FRP (fire-stop) family instance in the project. Catches:
//
//   PEN.MEMBER.ORPHAN   — a Pipe / Duct / Conduit / CableTray has
//                         STING_PENETRATION_REF_TXT stamped but no
//                         FRP family instance carries the matching
//                         PEN_PFV_UUID_TXT. The detector has fired
//                         but the placer hasn't (or the family was
//                         deleted by hand after placement).
//   PEN.FRP.ORPHAN      — an FRP family instance with PEN_PFV_UUID_TXT
//                         set but no member element carries the
//                         corresponding STING_PENETRATION_REF_TXT.
//                         Indicates a member was deleted / moved
//                         after the FRP was placed; the firestop is
//                         now floating and probably wrong.
//   PEN.STRUCT.FAIL     — a beam-host FRP carries
//                         PEN_STRUCTURAL_FLAG_TXT == STRUCT_FAIL
//                         (location / size violates AISC DG2 + BS EN
//                         1992). Must be re-routed before fab.
//   PEN.STRUCT.REVIEW   — same parameter == STRUCT_REVIEW. Engineer
//                         sign-off required before fab.
//   PEN.NO.RATING       — a member crosses a fire-rated barrier but
//                         the FRP variant carries empty fire rating.
//                         Either the host's STING_FIRE_RATING_TXT is
//                         out of date, or the wrong type variant
//                         was picked at placement.
//
// Designed to be fast on large models: a single sweep of all FRP
// instances (typically 100–2000) and one filtered collector pass
// over the categories that can carry STING_PENETRATION_REF_TXT.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public static class PenetrationCoverageValidator
    {
        public static List<ValidationResult> Validate(Document doc)
        {
            var findings = new List<ValidationResult>();
            if (doc == null) return findings;

            var frpByUuid = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
            var frpReferencedMember = new HashSet<long>();

            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_SpecialityEquipment))
                {
                    if (!(el is FamilyInstance fi)) continue;
                    string seedTag = ParameterHelpers.GetString(fi, "STING_SEED_FAMILY_TXT");
                    bool isPenetrationFamily =
                        string.Equals(seedTag, "STING_SEED_SpecialityEquipment", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(seedTag, "STING_SEED_FireDamper",          StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(seedTag, "STING_SEED_AcousticSeal",        StringComparison.OrdinalIgnoreCase);
                    if (!isPenetrationFamily
                        && string.IsNullOrEmpty(ParameterHelpers.GetString(fi, "PEN_CONTROL_NUMBER_TXT")))
                        continue;

                    string uuid = ParameterHelpers.GetString(fi, "PEN_PFV_UUID_TXT");
                    if (!string.IsNullOrEmpty(uuid)) frpByUuid[uuid] = fi;

                    string memberIdTxt = ParameterHelpers.GetString(fi, "PEN_MEMBER_ID_TXT");
                    if (long.TryParse(memberIdTxt, out long mid)) frpReferencedMember.Add(mid);

                    // Structural flag review (beams).
                    string flag = ParameterHelpers.GetString(fi, "PEN_STRUCTURAL_FLAG_TXT");
                    if (string.Equals(flag, "STRUCT_FAIL", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Error,
                            "PEN.STRUCT.FAIL",
                            "Beam penetration violates AISC DG2 / BS EN 1992 location or size limits — must reroute before fabrication.",
                            "PenetrationCoverage"));
                    }
                    else if (string.Equals(flag, "STRUCT_REVIEW", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Warning,
                            "PEN.STRUCT.REVIEW",
                            "Beam penetration close to support or > 0.4 d depth ratio — structural-engineer sign-off required.",
                            "PenetrationCoverage"));
                    }

                    // Rating-presence sanity check.
                    string hostType = ParameterHelpers.GetString(fi, "PEN_HOST_TYPE_TXT");
                    string rating   = ParameterHelpers.GetString(fi, "PEN_FIRE_RATING_TXT");
                    bool isBeam = string.Equals(hostType, "BEAM", StringComparison.OrdinalIgnoreCase);
                    if (!isBeam && string.IsNullOrEmpty(rating))
                    {
                        findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Warning,
                            "PEN.NO.RATING",
                            "Penetration on fire-rated host but PEN_FIRE_RATING_TXT is empty — verify host rating + type variant.",
                            "PenetrationCoverage"));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PenetrationCoverageValidator: FRP collect: {ex.Message}"); }

            // Member-side audit. Categories that carry STING_PENETRATION_REF_TXT.
            var memberCats = new[]
            {
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_FlexDuctCurves,
            };

            try
            {
                var filter = new ElementMulticategoryFilter(memberCats);
                foreach (var el in new FilteredElementCollector(doc)
                    .WherePasses(filter)
                    .WhereElementIsNotElementType())
                {
                    string penRef = ParameterHelpers.GetString(el, "STING_PENETRATION_REF_TXT");
                    if (string.IsNullOrEmpty(penRef)) continue;

                    if (!frpReferencedMember.Contains(el.Id.Value))
                    {
                        findings.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "PEN.MEMBER.ORPHAN",
                            $"Member crosses fire-rated barrier ({penRef}) but no FRP instance references it — run Penetrations: Detect & Place.",
                            "PenetrationCoverage"));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PenetrationCoverageValidator: member sweep: {ex.Message}"); }

            // FRP orphan check — is the referenced member still alive?
            foreach (var kv in frpByUuid)
            {
                var fi = kv.Value;
                string memberIdTxt = ParameterHelpers.GetString(fi, "PEN_MEMBER_ID_TXT");
                if (!long.TryParse(memberIdTxt, out long mid)) continue;
                Element member = null;
                try { member = doc.GetElement(new ElementId(mid)); } catch { }
                if (member == null)
                {
                    findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Warning,
                        "PEN.FRP.ORPHAN",
                        $"FRP {ParameterHelpers.GetString(fi, "PEN_CONTROL_NUMBER_TXT")} references member id {mid} which no longer exists — delete or re-link.",
                        "PenetrationCoverage"));
                }
            }

            return findings;
        }
    }
}
