// StingTools — ATEX equipment zone / Ex-rating classification check.
//
// IEC 60079-14 / ATEX Directive 2014/34/EU:
//   Zone 0 — explosive atmosphere present continuously or long periods.
//             Requires Category 1G equipment (Ex ia, Ex ma).
//   Zone 1 — explosive atmosphere likely in normal operation.
//             Requires Category 1G or 2G (Ex d, Ex e, Ex ia, Ex ib, Ex p, Ex q, Ex ma, Ex mb).
//   Zone 2 — explosive atmosphere not likely but possible in abnormal operation.
//             Requires Category 1G, 2G, or 3G (virtually all Ex types acceptable).
//
// NEC equivalent zones (Class I Division 1/2) are also flagged:
//   Class I Div 1 → treated as Zone 1 requirement.
//   Class I Div 2 → treated as Zone 2 requirement.
//
// Elements are checked when ATEX_ZONE_TXT is set (non-empty).
// Results are stamped to ATEX_CLASSIFICATION_OK ("1" = compliant, "0" = violation).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Validation
{
    /// <summary>
    /// Read-only command that validates Ex equipment ratings against declared
    /// ATEX zone classifications per IEC 60079-14.  Stamps
    /// ATEX_CLASSIFICATION_OK on each checked element and reports violations.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ATEXClassificationCommand : IExternalCommand
    {
        // ── Acceptable Ex protection types per zone ──────────────────────────

        // Zone 0 / Class I Div 1 intrinsically-safe: Ex ia, Ex ma only.
        private static readonly HashSet<string> Zone0AcceptableTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ia", "ma" };

        // Zone 1 / Class I Div 1: all Cat 1 + Cat 2 types.
        private static readonly HashSet<string> Zone1AcceptableTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "d", "e", "ia", "ib", "p", "q", "ma", "mb", "tb", "db" };

        // Zone 2 / Class I Div 2: all standard Ex types.
        private static readonly HashSet<string> Zone2AcceptableTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "d", "e", "ia", "ib", "ic", "n", "nA", "nR", "nC", "p", "q",
              "ma", "mb", "mc", "ta", "tb", "tc", "da", "db", "dc" };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── Collect all electrical elements with ATEX_ZONE_TXT set ───────
            // Check both electrical equipment and electrical fixtures.
            var candidates = new FilteredElementCollector(doc)
                .WherePasses(new LogicalOrFilter(
                    new ElementCategoryFilter(BuiltInCategory.OST_ElectricalEquipment),
                    new ElementCategoryFilter(BuiltInCategory.OST_ElectricalFixtures)))
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(el =>
                {
                    try
                    {
                        var p = el.LookupParameter("ATEX_ZONE_TXT");
                        return p != null && !string.IsNullOrWhiteSpace(p.AsString());
                    }
                    catch { return false; }
                })
                .ToList();

            if (candidates.Count == 0)
            {
                TaskDialog.Show("STING ATEX Classification",
                    "No elements with ATEX_ZONE_TXT parameter found.\n\n" +
                    "Set ATEX_ZONE_TXT (e.g. 'Zone 0', 'Zone 1', 'Zone 2', " +
                    "'Class I Div 1', 'Class I Div 2') on electrical equipment " +
                    "and fixtures located in hazardous areas.");
                return Result.Cancelled;
            }

            var violations = new List<string>();
            int passes     = 0;

            // We open a single transaction for all stamps.
            using (var tx = new Transaction(doc, "STING ATEX Classification Stamp"))
            {
                tx.Start();

                foreach (var el in candidates)
                {
                    string zone     = "";
                    string exRating = "";
                    string mark     = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                                   ?? el.Name ?? el.Id.ToString();

                    try
                    {
                        zone     = el.LookupParameter("ATEX_ZONE_TXT")?.AsString()?.Trim() ?? "";
                        exRating = el.LookupParameter("ATEX_EX_RATING_TXT")?.AsString()?.Trim() ?? "";
                    }
                    catch (Exception ex) { StingLog.Warn($"ATEXCheck read params: {ex.Message}"); }

                    // ── Missing Ex rating ────────────────────────────────────
                    if (string.IsNullOrEmpty(exRating))
                    {
                        violations.Add(
                            $"MISSING RATING  [{mark}]  Zone: {zone}  " +
                            "No ATEX_EX_RATING_TXT set (e.g. 'Ex d IIC T4').");
                        StampAtex(el, "0");
                        continue;
                    }

                    // ── Normalise zone string ────────────────────────────────
                    int zoneLevel = ParseZoneLevel(zone);
                    if (zoneLevel < 0)
                    {
                        // Unrecognised zone string — warn but do not fail.
                        violations.Add(
                            $"UNKNOWN ZONE  [{mark}]  Zone: '{zone}'  " +
                            "Use: Zone 0, Zone 1, Zone 2, Class I Div 1, or Class I Div 2.");
                        StampAtex(el, "0");
                        continue;
                    }

                    // ── Extract protection type tokens from Ex rating string ─
                    // A rating like "Ex d IIC T4 Gb" yields tokens: d, IIC, T4, Gb.
                    // We look for short alphabetic tokens that match known Ex types.
                    var exTypes = ExtractExTypes(exRating);

                    bool compliant = IsCompliantForZone(zoneLevel, exTypes);

                    if (compliant)
                    {
                        passes++;
                        StampAtex(el, "1");
                    }
                    else
                    {
                        string required = zoneLevel == 0 ? "Ex ia or Ex ma (Category 1G)"
                                        : zoneLevel == 1 ? "Category 1G or 2G (Ex d/e/ia/ib/p/q/ma/mb)"
                                                         : "any recognised Ex type";
                        violations.Add(
                            $"FAIL  [{mark}]  Zone: {zone}  Rating: '{exRating}'  " +
                            $"Required: {required}.");
                        StampAtex(el, "0");
                    }
                }

                tx.Commit();
            }

            // ── Report ───────────────────────────────────────────────────────
            string report =
                $"ATEX Classification Check — IEC 60079-14\n" +
                $"Elements checked: {candidates.Count}   " +
                $"Passes: {passes}   Violations: {violations.Count}\n\n";

            if (violations.Count > 0)
                report += "VIOLATIONS:\n" + string.Join("\n", violations.Take(20));
            else
                report += "All Ex equipment ratings comply with their declared hazardous-area zones.";

            TaskDialog.Show("STING ATEX Classification", report);
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns 0, 1, or 2 for Zone 0/1/2 and Class I Div 1/2.
        /// Returns -1 for unrecognised strings.
        /// </summary>
        private static int ParseZoneLevel(string zone)
        {
            string z = (zone ?? "").ToUpperInvariant().Trim();
            if (z.Contains("ZONE 0"))                       return 0;
            if (z.Contains("ZONE 1") || z.Contains("DIV 1") || z.Contains("DIV1")) return 1;
            if (z.Contains("ZONE 2") || z.Contains("DIV 2") || z.Contains("DIV2")) return 2;
            // Try bare numbers.
            if (z == "0") return 0;
            if (z == "1") return 1;
            if (z == "2") return 2;
            return -1;
        }

        /// <summary>
        /// Extracts individual protection-type codes from an Ex rating string.
        /// Input example: "Ex d IIC T4 Gb"  →  {"d"}
        /// Input example: "Ex e n IIC T3"  →  {"e","n"}
        /// Handles compound ratings like "Ex d e IIC".
        /// </summary>
        private static HashSet<string> ExtractExTypes(string rating)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rating)) return result;

            // Remove "Ex" prefix (case-insensitive), then tokenise.
            string cleaned = rating.Trim();
            if (cleaned.StartsWith("Ex", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(2).Trim();

            foreach (string token in cleaned.Split(new[] { ' ', ',', '+', '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                // Protection type tokens are alphabetic and 1-3 characters.
                // We accept "ia", "ib", "ic", "d", "e", "ma", "mb", "mc",
                // "nA", "nR", "nC", "p", "q", "t", "ta", "tb", "tc", "da", "db", "dc".
                if (token.Length >= 1 && token.Length <= 3 && token.All(c => char.IsLetter(c)))
                {
                    // Exclude gas-group tokens (roman numerals: I, II, IIA, IIB, IIC, III).
                    if (IsGasGroupToken(token)) continue;
                    // Exclude temperature-class tokens (T1..T6, Ta..Tc).
                    if (token.StartsWith("T", StringComparison.OrdinalIgnoreCase)
                        && token.Length <= 2) continue;
                    // Exclude EPL tokens (Ga, Gb, Gc, Da, Db, Dc, Ma, Mb).
                    if (token.Length == 2 && (token[0] == 'G' || token[0] == 'D' || token[0] == 'M')
                        && char.IsUpper(token[0]) && char.IsLetter(token[1])) { }
                    // Accept the token.
                    result.Add(token);
                }
            }
            return result;
        }

        private static bool IsGasGroupToken(string token)
        {
            string u = token.ToUpperInvariant();
            return u == "I" || u == "II" || u == "IIA" || u == "IIB" || u == "IIC" || u == "III";
        }

        private static bool IsCompliantForZone(int zoneLevel, HashSet<string> exTypes)
        {
            if (exTypes.Count == 0) return false;
            switch (zoneLevel)
            {
                case 0:  return exTypes.Any(t => Zone0AcceptableTypes.Contains(t));
                case 1:  return exTypes.Any(t => Zone1AcceptableTypes.Contains(t));
                case 2:  return exTypes.Any(t => Zone2AcceptableTypes.Contains(t));
                default: return false;
            }
        }

        private static void StampAtex(Element el, string value)
        {
            try { ParameterHelpers.SetString(el, "ATEX_CLASSIFICATION_OK", value, overwrite: true); }
            catch (Exception ex) { StingLog.Warn($"ATEXCheck stamp: {ex.Message}"); }
        }
    }
}
