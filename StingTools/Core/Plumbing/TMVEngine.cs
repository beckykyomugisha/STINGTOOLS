// TMVEngine — Thermostatic Mixing Valve register and validation.
//
// Scans OST_PipeAccessory + OST_PlumbingFixtures for elements whose
// PLM_TMV_CLASS_TXT is populated, and checks the design set point
// (PLM_TMV_BLEND_TEMP_C) and the commissioning reading (PLM_TMV_MEASURED_C)
// against the limit for the outlet the valve serves, the TMV scheme and,
// for a TMV3 bath, whether bathing is assisted. The limits come only from
// Data/Plumbing/STING_TMV_STANDARDS.json via WaterSafetyLimits (DSCH-25):
// TMV3 per HTM 04-01 Part A Table 2 / NHS D08, TMV2 per the TMV2 scheme.
// Where any of the three is unknown, or the data file is unusable, the TMV
// is NOT CHECKED — never passed on a guessed limit.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using StingTools.Standards.HTM;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Plumbing
{
    // ──────────────────────────────────────────────────────────────────────
    // Data model
    // ──────────────────────────────────────────────────────────────────────

    public class TMVRecord
    {
        public ElementId Id              { get; set; }
        public string    FamilyName      { get; set; } = "";
        public string    Location        { get; set; } = "";
        public string    RoomName        { get; set; } = "";
        /// <summary>TMV2 / TMV3 from PLM_TMV_CLASS_TXT (or PLM_TMV_TYPE_TXT); "" when unknown.</summary>
        public string    Scheme          { get; set; } = "";
        /// <summary>BATH / SHOWER / BASIN / BIDET from PLM_FIX_TYPE_TXT; "" when unknown.</summary>
        public string    Outlet          { get; set; } = "";
        /// <summary>PLM_TMV_ASSISTED_BOOL; null when not recorded.</summary>
        public bool?     Assisted        { get; set; }
        /// <summary>PLM_TMV_PAEDIATRIC_BOOL; null when not recorded (DSCH-45).</summary>
        public bool?     Paediatric      { get; set; }
        public WaterCheckStatus Status   { get; set; } = WaterCheckStatus.NotChecked;
        public string    StatusText      => Status == WaterCheckStatus.Pass ? "PASS"
                                          : Status == WaterCheckStatus.Fail ? "FAIL" : "NOT CHECKED";
        public double    InletHotC       { get; set; }
        public double    InletColdC      { get; set; }
        public double    SetOutletC      { get; set; }
        public double    ActualOutletC   { get; set; }
        public string    LastTestDate    { get; set; } = "";
        public string    AnnualTestDueDate { get; set; } = "";
        /// <summary>True only when the check ran and passed.</summary>
        public bool      WithinTolerance => Status == WaterCheckStatus.Pass;
        public bool      TestOverdue     { get; set; }
        public string    FailReason      { get; set; } = "";
        /// <summary>Kv coefficient from PLM_TMV_KVS param if populated.</summary>
        public double    FlowRateKvs     { get; set; }
        /// <summary>Normative reference applied during validation.</summary>
        public string    StandardRef     { get; set; } = "";
        public bool      IsHealthcare    { get; set; }
        /// <summary>What the check assumed or could not establish (see TmvCheck.Notes).</summary>
        public List<string> Notes        { get; } = new List<string>();
    }

    public class TMVRegisterResult
    {
        public int              TotalTMVs  { get; set; }
        public int              PassCount  { get; set; }
        public int              FailCount  { get; set; }
        public int              NotCheckedCount { get; set; }
        public int              OverdueCount { get; set; }
        public List<TMVRecord>  Records    { get; } = new List<TMVRecord>();
        public List<string>     Warnings   { get; } = new List<string>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Engine
    // ──────────────────────────────────────────────────────────────────────

    public static class TMVEngine
    {
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
            var limits = PlumbingTables.WaterSafety;
            if (limits == null)
                result.Warnings.Add("STING_TMV_STANDARDS.json unusable — every TMV is NOT CHECKED: "
                                    + string.Join("; ", PlumbingTables.WaterSafetyErrors));
            bool isHealthcareProject = IsHealthcareProject(doc);
            var region = ProjectHtmRegion(doc);
            var noteCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var el in elements)
            {
                try
                {
                    var rec = BuildRecord(doc, el, isHealthcareProject);
                    if (rec == null) continue;

                    ApplyCheck(rec, limits, region);
                    foreach (var n in rec.Notes)
                        noteCounts[n] = noteCounts.TryGetValue(n, out var k) ? k + 1 : 1;

                    // Check test overdue
                    rec.TestOverdue = IsTestOverdue(rec.AnnualTestDueDate);

                    result.Records.Add(rec);
                    result.TotalTMVs++;
                    if (rec.Status == WaterCheckStatus.Pass) result.PassCount++;
                    else if (rec.Status == WaterCheckStatus.Fail) result.FailCount++;
                    else result.NotCheckedCount++;
                    if (rec.TestOverdue) result.OverdueCount++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"TMVEngine.ScanAll element {el.Id.Value}: {ex.Message}");
                }
            }

            // Notes go to the register warnings once each, with how many TMVs they cover,
            // so a passing row still shows what was assumed (e.g. jurisdiction, paediatric).
            foreach (var kv in noteCounts.OrderByDescending(k => k.Value))
                result.Warnings.Add($"{kv.Key} ({kv.Value} TMV{(kv.Value == 1 ? "" : "s")})");

            StingLog.Info($"TMVEngine.ScanAll: {result.TotalTMVs} TMVs, " +
                          $"{result.PassCount} pass, {result.FailCount} fail, {result.NotCheckedCount} not checked, " +
                          $"{result.OverdueCount} overdue");
            return result;
        }

        /// <summary>Checks one record against the limits (null = data unusable) for the
        /// project's HTM region (null = not recorded) and sets Status, FailReason,
        /// StandardRef and Notes.</summary>
        public static void ApplyCheck(TMVRecord rec, WaterSafetyLimitsFile limits, HtmRegion? region)
        {
            if (rec == null) return;
            var c = WaterSafetyLimits.CheckTmv(limits,
                string.IsNullOrEmpty(rec.Outlet) ? null : rec.Outlet,
                string.IsNullOrEmpty(rec.Scheme) ? null : rec.Scheme,
                rec.Assisted, rec.Paediatric, rec.IsHealthcare, rec.SetOutletC, rec.ActualOutletC, region);
            rec.Status      = c.Status;
            rec.FailReason  = c.Reason;
            rec.StandardRef = c.StandardRef;
            rec.Notes.Clear();
            rec.Notes.AddRange(c.Notes);
        }

        /// <summary>The project's HTM region from PRJ_ORG_HEALTH_HTM_REGION_TXT; null when
        /// blank or unrecognised (the TMV check then says England rows were assumed).</summary>
        public static HtmRegion? ProjectHtmRegion(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return null;
                string raw = ReadString(pi, ParamRegistry.PRJ_ORG_HEALTH_HTM_REGION_TXT);
                if (HtmRegionalVariants.TryParseRegion(raw, out var r)) return r;
                if (!string.IsNullOrWhiteSpace(raw))
                    StingLog.Warn($"TMVEngine: PRJ_ORG_HEALTH_HTM_REGION_TXT '{raw}' names no region — treated as not recorded");
            }
            catch (Exception ex) { StingLog.Warn("TMVEngine.ProjectHtmRegion: " + ex.Message); }
            return null;
        }

        /// <summary>
        /// Export the TMV register to CSV text.
        /// </summary>
        public static string ExportToCsv(TMVRegisterResult r)
        {
            if (r == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("ElementId,FamilyName,Location,RoomName,Scheme,Outlet,Assisted,Paediatric,InletHotC,InletColdC," +
                          "SetOutletC,ActualOutletC,Status,TestOverdue," +
                          "LastTestDate,AnnualDueDate,FailReason,StandardRef,KvsCoeff");
            foreach (var rec in r.Records)
            {
                sb.AppendLine($"{rec.Id?.Value},{EscCsv(rec.FamilyName)},{EscCsv(rec.Location)}," +
                              $"{EscCsv(rec.RoomName)},{rec.Scheme},{rec.Outlet},{YesNoText(rec.Assisted)},{YesNoText(rec.Paediatric)},{rec.InletHotC:F1}," +
                              $"{rec.InletColdC:F1},{rec.SetOutletC:F1},{rec.ActualOutletC:F1}," +
                              $"{rec.StatusText},{rec.TestOverdue}," +
                              $"{EscCsv(rec.LastTestDate)},{EscCsv(rec.AnnualTestDueDate)}," +
                              $"{EscCsv(rec.FailReason)},{EscCsv(rec.StandardRef)},{rec.FlowRateKvs:F3}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes one TMV test result (HTM 04-01 Supplement D 08 §11: commissioning or
        /// in-service test) to the TMV's parameters: the outlet reading to
        /// PLM_TMV_MEASURED_C, the inlet temperatures when recorded (null = left as they
        /// are), the test date, and the next test due at test date + 12 months.
        /// Called by Plumb_TMVImportTests (DSCH-46) for each row of a filled TMV register.
        /// MUST be called within a started Transaction — does not create its own.
        /// Returns false, with the parameters that could not be written in
        /// <paramref name="failed"/>, when any write did not take (typically a parameter
        /// not bound to the element's category).
        /// </summary>
        public static bool WriteTMVData(Document doc, ElementId id, double outletC,
            double? inletHotC, double? inletColdC, string testDate, out List<string> failed)
        {
            failed = new List<string>();
            if (doc == null || id == null) { failed.Add("no element"); return false; }
            try
            {
                var el = doc.GetElement(id);
                if (el == null) { failed.Add("element not found"); return false; }
                // The design set point (PLM_TMV_BLEND_TEMP_C) is set during design and left untouched.
                if (!ParameterHelpers.SetDoubleInNamedUnit(el, ParamRegistry.PLM_TMV_MEASURED_C, outletC)) failed.Add(ParamRegistry.PLM_TMV_MEASURED_C);
                if (inletHotC.HasValue && !ParameterHelpers.SetDoubleInNamedUnit(el, ParamRegistry.PLM_TMV_INLET_HOT_C, inletHotC.Value))
                    failed.Add(ParamRegistry.PLM_TMV_INLET_HOT_C);
                if (inletColdC.HasValue && !ParameterHelpers.SetDoubleInNamedUnit(el, ParamRegistry.PLM_TMV_INLET_COLD_C, inletColdC.Value))
                    failed.Add(ParamRegistry.PLM_TMV_INLET_COLD_C);
                if (!TryWriteString(el, ParamRegistry.PLM_TMV_TEST_DATE_TXT, testDate)) failed.Add(ParamRegistry.PLM_TMV_TEST_DATE_TXT);
                if (DateTime.TryParseExact(testDate, TmvTestImport.DateFormat, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var testDt)
                    && !TryWriteString(el, ParamRegistry.PLM_TMV_NEXT_TEST_TXT, testDt.AddMonths(12).ToString(TmvTestImport.DateFormat)))
                    failed.Add(ParamRegistry.PLM_TMV_NEXT_TEST_TXT);
            }
            catch (Exception ex)
            {
                StingLog.Error($"WriteTMVData {id.Value}", ex);
                failed.Add(ex.Message);
            }
            return failed.Count == 0;
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

            // Scheme (TMV2 / TMV3): PLM_TMV_CLASS_TXT, else PLM_TMV_TYPE_TXT.
            rec.Scheme = WaterSafetyLimits.NormaliseScheme(ReadString(el, ParamRegistry.PLM_TMV_CLASS))
                      ?? WaterSafetyLimits.NormaliseScheme(ReadString(el, ParamRegistry.PLM_TMV_TYPE_TXT)) ?? "";
            // Outlet the valve serves: PLM_FIX_TYPE_TXT (bound on Plumbing Fixtures).
            // A valve modelled as a pipe accessory carries none, so it is NOT CHECKED.
            rec.Outlet = WaterSafetyLimits.NormaliseOutlet(ReadString(el, ParamRegistry.PLM_FIX_TYPE_TXT)) ?? "";
            rec.Assisted = ReadYesNo(el, ParamRegistry.PLM_TMV_ASSISTED_BOOL);
            rec.Paediatric = ReadYesNo(el, ParamRegistry.PLM_TMV_PAEDIATRIC_BOOL);

            // Temperatures: SetOutletC is the design set-point (PLM_TMV_BLEND),
            // ActualOutletC is the commissioning reading (PLM_TMV_MEASURED_C).
            // Pre-Phase-187 families that don't have PLM_TMV_MEASURED_C bound
            // will report ActualOutletC = 0, which ValidateTemperatures treats
            // as "no measurement yet" — better than the historic behaviour of
            // mirroring the set-point and silently passing every tolerance.
            rec.SetOutletC    = ReadDouble(el, ParamRegistry.PLM_TMV_BLEND);
            rec.ActualOutletC = ReadDouble(el, ParamRegistry.PLM_TMV_MEASURED_C);
            rec.InletHotC     = ReadDouble(el, ParamRegistry.PLM_TMV_INLET_HOT_C);
            rec.InletColdC    = ReadDouble(el, ParamRegistry.PLM_TMV_INLET_COLD_C);

            // Test dates
            rec.LastTestDate      = ReadString(el, ParamRegistry.PLM_TMV_TEST_DATE_TXT);
            rec.AnnualTestDueDate = ReadString(el, ParamRegistry.PLM_TMV_NEXT_TEST_TXT);

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

        /// <summary>HTM project: PRJ_PLUMBING_CODE mentions HTM, or the org class is healthcare. Shared by the TMV and dead-leg checks.</summary>
        public static bool IsHealthcareProject(Document doc)
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

        private static bool? ReadYesNo(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer) return p.AsInteger() != 0;
            }
            catch (Exception ex) { StingLog.Warn($"TMVEngine.ReadYesNo {paramName}: {ex.Message}"); }
            return null;
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
                        if (StingTools.Core.NumberText.TryParse(p.AsString(), out var v)) return v;
                }
            }
            catch { }
            return 0;
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

        private static string YesNoText(bool? b) => b.HasValue ? (b.Value ? "Yes" : "No") : "";

        private static string EscCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
    }
}
