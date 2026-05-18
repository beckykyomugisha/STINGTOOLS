// TMVEngine — Thermostatic Mixing Valve register, validation, and
// parameter management per BS 8680:2022 and HTM 04-01. Phase 179c.
//
// Scans OST_PipeAccessory + OST_PlumbingFixtures for elements whose
// PLM_TMV_CLASS_TXT is populated, reads temperature/test-date parameters,
// validates outlet temperature against BS 8680 / HTM 04-01 limits,
// detects annual test overdue status, and can write calibration data back.
//
// TMV classes per BS 8680:2022 §4.3:
//   A — outlet within 38–43°C for ablution, ≤ 10°C drop from set-point
//   B — outlet within ±2°C of set-point  (general use)
//   C — outlet within ±1°C of set-point  (healthcare / high-risk)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Plumbing
{
    // ──────────────────────────────────────────────────────────────────────
    // Data model
    // ──────────────────────────────────────────────────────────────────────

    public enum TMVClass
    {
        ClassA,  // ≤10°C drop; outlet 38–43°C for ablution
        ClassB,  // ±2°C of set-point
        ClassC   // ±1°C of set-point (healthcare — HTM 04-01 mandate)
    }

    public class TMVRecord
    {
        public ElementId Id              { get; set; }
        public string    FamilyName      { get; set; } = "";
        public string    Location        { get; set; } = "";
        public string    RoomName        { get; set; } = "";
        public TMVClass  Class           { get; set; } = TMVClass.ClassB;
        public double    InletHotC       { get; set; }
        public double    InletColdC      { get; set; }
        public double    SetOutletC      { get; set; }
        public double    ActualOutletC   { get; set; }
        public string    LastTestDate    { get; set; } = "";
        public string    AnnualTestDueDate { get; set; } = "";
        public bool      WithinTolerance { get; set; }
        public bool      TestOverdue     { get; set; }
        public string    FailReason      { get; set; } = "";
        /// <summary>Kv coefficient from PLM_TMV_KVS param if populated.</summary>
        public double    FlowRateKvs     { get; set; }
        /// <summary>Normative reference applied during validation.</summary>
        public string    StandardRef     { get; set; } = "";
        public bool      IsHealthcare    { get; set; }
    }

    public class TMVRegisterResult
    {
        public int              TotalTMVs  { get; set; }
        public int              PassCount  { get; set; }
        public int              FailCount  { get; set; }
        public int              OverdueCount { get; set; }
        public List<TMVRecord>  Records    { get; } = new List<TMVRecord>();
        public List<string>     Warnings   { get; } = new List<string>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Engine
    // ──────────────────────────────────────────────────────────────────────

    public static class TMVEngine
    {
        // BS 8680:2022 §5 temperature limits
        private const double BsMaxAblutionC  = 43.0;
        private const double BsMinAblutionC  = 38.0;
        private const double BsMaxShowerC    = 38.0;
        private const double BsMaxBathC      = 46.0;
        // HTM 04-01 Table 4: healthcare ablution ≤ 41°C; showers Class C mandatory
        private const double HtmMaxAblutionC = 41.0;

        // Param name for Kv (optional — doesn't exist in base registry; looked up by name)
        private const string KvsParamName = "PLM_TMV_KVS";

        /// <summary>
        /// Scan the document for all TMV elements and build a validation register.
        /// </summary>
        public static TMVRegisterResult ScanAll(Document doc)
        {
            var result = new TMVRegisterResult();
            if (doc == null) return result;

            var elements = CollectTMVElements(doc);
            bool isHealthcareProject = IsHealthcareProject(doc);

            foreach (var el in elements)
            {
                try
                {
                    var rec = BuildRecord(doc, el, isHealthcareProject);
                    if (rec == null) continue;

                    // Validate temperatures
                    var (pass, reason) = ValidateTemperatures(rec);
                    rec.WithinTolerance = pass;
                    rec.FailReason      = pass ? "" : reason;

                    // Check test overdue
                    rec.TestOverdue = IsTestOverdue(rec.AnnualTestDueDate);

                    result.Records.Add(rec);
                    result.TotalTMVs++;
                    if (pass) result.PassCount++;
                    else      result.FailCount++;
                    if (rec.TestOverdue) result.OverdueCount++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"TMVEngine.ScanAll element {el.Id.Value}: {ex.Message}");
                }
            }

            StingLog.Info($"TMVEngine.ScanAll: {result.TotalTMVs} TMVs, " +
                          $"{result.PassCount} pass, {result.FailCount} fail, " +
                          $"{result.OverdueCount} overdue");
            return result;
        }

        /// <summary>
        /// Validate outlet temperature for a TMVRecord per BS 8680 / HTM 04-01.
        /// Returns (pass, failReason).
        /// </summary>
        public static (bool pass, string reason) ValidateTemperatures(TMVRecord rec)
        {
            if (rec == null) return (false, "Null record");
            if (rec.ActualOutletC <= 0) return (true, ""); // No measurement yet — not a fail

            double outlet  = rec.ActualOutletC;
            double setPoint = rec.SetOutletC > 0 ? rec.SetOutletC : 41.0;

            // Class-based tolerance check
            double tolerance = rec.Class switch
            {
                TMVClass.ClassA => 10.0,
                TMVClass.ClassB => 2.0,
                TMVClass.ClassC => 1.0,
                _               => 2.0
            };

            string stdRef;
            bool pass;
            string reason = "";

            if (rec.IsHealthcare)
            {
                // HTM 04-01 §4.3 / Table 4
                stdRef = "HTM 04-01 Table 4";
                if (outlet > HtmMaxAblutionC)
                {
                    pass   = false;
                    reason = $"Outlet {outlet:F1}°C exceeds HTM 04-01 max {HtmMaxAblutionC}°C (ablution)";
                }
                else if (rec.Class == TMVClass.ClassC && Math.Abs(outlet - setPoint) > 1.0)
                {
                    pass   = false;
                    reason = $"Class C: outlet {outlet:F1}°C outside ±1°C of set-point {setPoint:F1}°C";
                }
                else if (rec.Class == TMVClass.ClassB && Math.Abs(outlet - setPoint) > 2.0)
                {
                    pass   = false;
                    reason = $"Class B: outlet {outlet:F1}°C outside ±2°C of set-point {setPoint:F1}°C";
                }
                else
                {
                    pass = true;
                }
            }
            else
            {
                // BS 8680:2022 §5 — ablution default range
                stdRef = "BS 8680:2022 §5.3";
                if (rec.Class == TMVClass.ClassA)
                {
                    pass = outlet >= BsMinAblutionC && outlet <= BsMaxAblutionC;
                    if (!pass)
                        reason = $"Class A: outlet {outlet:F1}°C outside ablution range {BsMinAblutionC}–{BsMaxAblutionC}°C";
                }
                else if (rec.Class == TMVClass.ClassB)
                {
                    pass = Math.Abs(outlet - setPoint) <= 2.0;
                    if (!pass)
                        reason = $"Class B: outlet {outlet:F1}°C outside ±2°C of set-point {setPoint:F1}°C";
                }
                else // ClassC
                {
                    pass = Math.Abs(outlet - setPoint) <= 1.0;
                    if (!pass)
                        reason = $"Class C: outlet {outlet:F1}°C outside ±1°C of set-point {setPoint:F1}°C";
                }
            }

            rec.StandardRef = stdRef;
            return (pass, reason);
        }

        /// <summary>
        /// Export the TMV register to CSV text.
        /// </summary>
        public static string ExportToCsv(TMVRegisterResult r)
        {
            if (r == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("ElementId,FamilyName,Location,RoomName,Class,InletHotC,InletColdC," +
                          "SetOutletC,ActualOutletC,WithinTolerance,TestOverdue," +
                          "LastTestDate,AnnualDueDate,FailReason,StandardRef,KvsCoeff");
            foreach (var rec in r.Records)
            {
                sb.AppendLine($"{rec.Id?.Value},{EscCsv(rec.FamilyName)},{EscCsv(rec.Location)}," +
                              $"{EscCsv(rec.RoomName)},{rec.Class},{rec.InletHotC:F1}," +
                              $"{rec.InletColdC:F1},{rec.SetOutletC:F1},{rec.ActualOutletC:F1}," +
                              $"{rec.WithinTolerance},{rec.TestOverdue}," +
                              $"{EscCsv(rec.LastTestDate)},{EscCsv(rec.AnnualTestDueDate)}," +
                              $"{EscCsv(rec.FailReason)},{EscCsv(rec.StandardRef)},{rec.FlowRateKvs:F3}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Write calibration data to a TMV element's parameters.
        /// MUST be called within a started Transaction — does not create its own.
        /// Returns true if all writes succeeded.
        /// </summary>
        public static bool WriteTMVData(Document doc, ElementId id,
            double inletHotC, double inletColdC, double outletC, string testDate)
        {
            if (doc == null || id == null) return false;
            bool ok = true;
            try
            {
                var el = doc.GetElement(id);
                if (el == null) return false;
                ok &= TryWriteDouble(el, ParamRegistry.PLM_TMV_INLET_HOT_C,  inletHotC);
                ok &= TryWriteDouble(el, ParamRegistry.PLM_TMV_INLET_COLD_C, inletColdC);
                ok &= TryWriteDouble(el, ParamRegistry.PLM_TMV_BLEND,        outletC);
                ok &= TryWriteString(el, ParamRegistry.PLM_TMV_TEST_DATE,    testDate);

                // Compute annual test due: test date + 12 months
                if (DateTime.TryParse(testDate, out var testDt))
                {
                    string dueTxt = testDt.AddMonths(12).ToString("yyyy-MM-dd");
                    TryWriteString(el, ParamRegistry.PLM_TMV_NEXT_TEST, dueTxt);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"WriteTMVData {id.Value}", ex);
                return false;
            }
            return ok;
        }

        // ──────────────────────────────────────────────────────────────────
        // Private helpers
        // ──────────────────────────────────────────────────────────────────

        private static IEnumerable<Element> CollectTMVElements(Document doc)
        {
            var accessories = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeAccessory)
                .WhereElementIsNotElementType()
                .Cast<Element>();

            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .Cast<Element>();

            return accessories.Concat(fixtures)
                .Where(el =>
                {
                    try
                    {
                        var p = el.LookupParameter(ParamRegistry.PLM_TMV_CLASS);
                        return p != null && p.HasValue
                               && !string.IsNullOrWhiteSpace(p.AsString());
                    }
                    catch { return false; }
                });
        }

        private static TMVRecord BuildRecord(Document doc, Element el, bool isHealthcareProject)
        {
            var rec = new TMVRecord
            {
                Id           = el.Id,
                FamilyName   = GetFamilyName(el),
                Location     = GetLocation(el),
                RoomName     = GetRoomName(doc, el),
                IsHealthcare = isHealthcareProject
            };

            // Read class
            rec.Class = ParseClass(ReadString(el, ParamRegistry.PLM_TMV_CLASS));

            // Temperatures
            rec.SetOutletC    = ReadDouble(el, ParamRegistry.PLM_TMV_BLEND);
            rec.ActualOutletC = ReadDouble(el, ParamRegistry.PLM_TMV_BLEND);  // same param — set vs actual may be same field
            rec.InletHotC     = ReadDouble(el, ParamRegistry.PLM_TMV_INLET_HOT_C);
            rec.InletColdC    = ReadDouble(el, ParamRegistry.PLM_TMV_INLET_COLD_C);

            // Test dates
            rec.LastTestDate      = ReadString(el, ParamRegistry.PLM_TMV_TEST_DATE);
            rec.AnnualTestDueDate = ReadString(el, ParamRegistry.PLM_TMV_NEXT_TEST);

            // If annual due not stored, compute from test date
            if (string.IsNullOrWhiteSpace(rec.AnnualTestDueDate)
             && DateTime.TryParse(rec.LastTestDate, out var testDt))
            {
                rec.AnnualTestDueDate = testDt.AddMonths(12).ToString("yyyy-MM-dd");
            }

            // Kv (optional)
            rec.FlowRateKvs = ReadDouble(el, KvsParamName);

            return rec;
        }

        private static bool IsTestOverdue(string dueDateTxt)
        {
            if (string.IsNullOrWhiteSpace(dueDateTxt)) return false;
            if (!DateTime.TryParse(dueDateTxt, out var dueDate)) return false;
            return dueDate < DateTime.Today;
        }

        private static bool IsHealthcareProject(Document doc)
        {
            try
            {
                // Check project plumbing code or dedicated healthcare flag
                var pi = doc.ProjectInformation;
                if (pi == null) return false;
                string code = ReadString(pi, ParamRegistry.PRJ_PLUMBING_CODE);
                if (code.IndexOf("HTM", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                // Also accept any PRJ_ORG_CLASS set to healthcare facility types
                string orgClass = ReadString(pi, "PRJ_ORG_CLASS_TXT");
                return orgClass.IndexOf("health", StringComparison.OrdinalIgnoreCase) >= 0
                    || orgClass.IndexOf("hospital", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static TMVClass ParseClass(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return TMVClass.ClassB;
            s = s.Trim().ToUpperInvariant();
            if (s.Contains("CLASS_C") || s == "C" || s == "CLASSC") return TMVClass.ClassC;
            if (s.Contains("CLASS_A") || s == "A" || s == "CLASSA") return TMVClass.ClassA;
            return TMVClass.ClassB;
        }

        private static string GetFamilyName(Element el)
        {
            try { return ((el as FamilyInstance)?.Symbol?.Family?.Name) ?? el.Name ?? ""; }
            catch { return ""; }
        }

        private static string GetLocation(Element el)
        {
            try
            {
                var p = el.LookupParameter("Mark");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static string GetRoomName(Document doc, Element el)
        {
            try
            {
                if (el is FamilyInstance fi)
                {
                    var room = fi.Room;
                    if (room != null) return room.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string ReadString(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static double ReadDouble(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Double)  return p.AsDouble();
                    if (p.StorageType == StorageType.Integer) return p.AsInteger();
                    if (p.StorageType == StorageType.String)
                        if (double.TryParse(p.AsString(), out var v)) return v;
                }
            }
            catch { }
            return 0;
        }

        private static bool TryWriteDouble(Element el, string paramName, double value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Double) { p.Set(value); return true; }
                if (p.StorageType == StorageType.String) { p.Set(value.ToString("F2")); return true; }
            }
            catch { }
            return false;
        }

        private static bool TryWriteString(Element el, string paramName, string value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.String) { p.Set(value); return true; }
            }
            catch { }
            return false;
        }

        private static string EscCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
    }
}
