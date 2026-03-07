using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  FM / O&M HANDOVER EXPORT COMMANDS
    //
    //  ISO 19650-aligned Facilities Management and Operations & Maintenance
    //  handover document exports. Five commands:
    //    1. COBie Export — COBie 2.4 spreadsheet (11 sheets: Contact, Facility,
    //       Floor, Space, Type, Component, System, Zone, Attribute, Job, Resource)
    //    2. Maintenance Schedule — PPM with ASTM E2018 condition grades, CIBSE
    //       failure modes, spare parts, and CDM safety precautions
    //    3. O&M Manual — comprehensive manual with failure modes, safety, spares
    //    4. Asset Health Report — 0-100 health scoring (5 dimensions)
    //    5. Space Handover Report — room-by-room asset inventory with density
    //
    //  Enhanced from StingBIM.AI repos: FacilityManagement (predictive analytics,
    //  equipment health scoring), Maintenance (ASTM E2018, work orders, failure
    //  modes), TenantManagement (space management, BOMA 2017).
    //
    //  All commands extract data from STING shared parameters + Revit built-in
    //  parameters and export to CSV/text files alongside the project.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export COBie-lite asset data spreadsheet.
    /// Generates CSV files for the core COBie sheets: Facility, Floor, Space,
    /// Type, Component, System, Zone — populated from STING tag data and
    /// Revit model information.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string prefix = $"STING_COBie_{timestamp}";

            try
            {
                // ── Facility sheet ──
                var facilityLines = new List<string>();
                facilityLines.Add("Name,CreatedBy,CreatedOn,Category,ProjectName,SiteName,Phase,Description");
                string projName = doc.Title ?? "Unknown";
                string siteName = HandoverHelper.GetProjectInfoParam(doc, "Project Address");
                if (string.IsNullOrEmpty(siteName)) siteName = HandoverHelper.GetProjectInfoParam(doc, "Client Name");
                string phase = HandoverHelper.GetCurrentPhase(doc);
                facilityLines.Add($"{Esc(projName)},STING Tools,{DateTime.Now:yyyy-MM-dd},Facility,{Esc(projName)},{Esc(siteName)},{Esc(phase)},ISO 19650 BIM Handover");

                // ── Floor sheet ──
                var floorLines = new List<string>();
                floorLines.Add("Name,CreatedBy,CreatedOn,Category,ExternalSystem,ExternalObject,ExternalIdentifier,Description,Elevation,Height");
                foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
                {
                    string elev = (level.Elevation * 304.8).ToString("F0");
                    floorLines.Add($"{Esc(level.Name)},STING Tools,{DateTime.Now:yyyy-MM-dd},Floor,Revit,Level,{level.Id},{Esc(level.Name)},{elev},");
                }

                // ── Space sheet ──
                var spaceLines = new List<string>();
                spaceLines.Add("Name,CreatedBy,CreatedOn,Category,FloorName,Description,RoomTag,UsableHeight,GrossArea,NetArea");
                foreach (Room room in new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Room>().Where(r => r.Area > 0))
                {
                    string levelName = room.Level?.Name ?? "";
                    string roomNum = room.Number ?? "";
                    string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    double area = room.Area * 0.092903; // sq ft to sq m
                    spaceLines.Add($"{Esc(roomNum + " " + roomName)},STING Tools,{DateTime.Now:yyyy-MM-dd},Room,{Esc(levelName)},{Esc(roomName)},{Esc(roomNum)},,{area:F2},");
                }

                // ── Type sheet ──
                var typeLines = new List<string>();
                typeLines.Add("Name,CreatedBy,CreatedOn,Category,Description,Manufacturer,ModelNumber,Warranty,ReplacementCost,ExpectedLife,NominalLength,NominalWidth,NominalHeight");
                var typesSeen = new HashSet<string>();
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    string typeName = ParameterHelpers.GetFamilySymbolName(el);
                    string familyName = ParameterHelpers.GetFamilyName(el);
                    string typeKey = $"{familyName}:{typeName}";
                    if (typesSeen.Contains(typeKey)) continue;
                    typesSeen.Add(typeKey);

                    string desc = HandoverHelper.Gs(el, ParamRegistry.DESC);
                    if (string.IsNullOrEmpty(desc)) desc = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    string mfr = HandoverHelper.Gs(el, ParamRegistry.MFR);
                    if (string.IsNullOrEmpty(mfr)) mfr = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                    string model = HandoverHelper.Gs(el, ParamRegistry.MODEL);
                    if (string.IsNullOrEmpty(model)) model = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MODEL);
                    string cost = HandoverHelper.Gs(el, ParamRegistry.COST);

                    typeLines.Add($"{Esc(typeKey)},STING Tools,{DateTime.Now:yyyy-MM-dd},{Esc(cat)},{Esc(desc)},{Esc(mfr)},{Esc(model)},,{cost},,,,");
                }

                // ── Component sheet ──
                var compLines = new List<string>();
                compLines.Add("Name,CreatedBy,CreatedOn,TypeName,Space,Description,SerialNumber,InstallationDate,WarrantyStartDate,TagNumber,AssetIdentifier,SystemName");
                int compCount = 0;
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    compCount++;

                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    string typeName = ParameterHelpers.GetFamilySymbolName(el);
                    string familyName = ParameterHelpers.GetFamilyName(el);
                    string typeKey = $"{familyName}:{typeName}";
                    string space = HandoverHelper.Gs(el, ParamRegistry.ROOM_NAME);
                    if (string.IsNullOrEmpty(space)) space = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NAME);
                    string desc = HandoverHelper.Gs(el, ParamRegistry.DESC);
                    if (string.IsNullOrEmpty(desc)) desc = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    string mark = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MARK);
                    string sysName = HandoverHelper.Gs(el, ParamRegistry.SYS);

                    compLines.Add($"{Esc(tag1)},STING Tools,{DateTime.Now:yyyy-MM-dd},{Esc(typeKey)},{Esc(space)},{Esc(desc)},,,,{Esc(tag1)},{el.Id},{Esc(sysName)}");
                }

                // ── System sheet ──
                var sysLines = new List<string>();
                sysLines.Add("Name,CreatedBy,CreatedOn,Category,Description,ComponentNames");
                var systemGroups = new Dictionary<string, List<string>>();
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    string sys = HandoverHelper.Gs(el, ParamRegistry.SYS);
                    if (string.IsNullOrEmpty(sys)) continue;
                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    if (!systemGroups.ContainsKey(sys)) systemGroups[sys] = new List<string>();
                    if (!string.IsNullOrEmpty(tag1) && systemGroups[sys].Count < 20)
                        systemGroups[sys].Add(tag1);
                }
                foreach (var sg in systemGroups.OrderBy(x => x.Key))
                {
                    string comps = string.Join("; ", sg.Value);
                    sysLines.Add($"{Esc(sg.Key)},STING Tools,{DateTime.Now:yyyy-MM-dd},System,{Esc(sg.Key)} system,{Esc(comps)}");
                }

                // ── Zone sheet ──
                var zoneLines = new List<string>();
                zoneLines.Add("Name,CreatedBy,CreatedOn,Category,Description,SpaceNames");
                var zoneGroups = new Dictionary<string, HashSet<string>>();
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    string zone = HandoverHelper.Gs(el, ParamRegistry.ZONE);
                    if (string.IsNullOrEmpty(zone)) continue;
                    string space = HandoverHelper.Gs(el, ParamRegistry.ROOM_NAME);
                    if (string.IsNullOrEmpty(space)) space = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NAME);
                    if (!zoneGroups.ContainsKey(zone)) zoneGroups[zone] = new HashSet<string>();
                    if (!string.IsNullOrEmpty(space)) zoneGroups[zone].Add(space);
                }
                foreach (var zg in zoneGroups.OrderBy(x => x.Key))
                {
                    string spaces = string.Join("; ", zg.Value.Take(20));
                    zoneLines.Add($"{Esc(zg.Key)},STING Tools,{DateTime.Now:yyyy-MM-dd},Zone,{Esc(zg.Key)} zone,{Esc(spaces)}");
                }

                // ── Contact sheet (COBie 2.4) ──
                var contactLines = new List<string>();
                contactLines.Add("Email,CreatedBy,CreatedOn,Category,Company,Phone,Department,Street,PostalCode,Country");
                contactLines.Add($"bim@stingtools.com,STING Tools,{DateTime.Now:yyyy-MM-dd},BIM Manager,STING BIM,,BIM,,," );
                string clientName = HandoverHelper.GetProjectInfoParam(doc, "Client Name");
                if (!string.IsNullOrEmpty(clientName))
                    contactLines.Add($",STING Tools,{DateTime.Now:yyyy-MM-dd},Owner,{Esc(clientName)},,,,,");

                // ── Attribute sheet (COBie 2.4 — extended asset properties) ──
                var attrLines = new List<string>();
                attrLines.Add("Name,CreatedBy,CreatedOn,Category,SheetName,RowName,Value,Unit,Description,AllowedValues");
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    // Export key attributes as COBie Attribute rows
                    void AddAttr(string name, string val, string unit, string desc)
                    {
                        if (!string.IsNullOrEmpty(val))
                            attrLines.Add($"{Esc(name)},STING Tools,{DateTime.Now:yyyy-MM-dd},Attribute,Component,{Esc(tag1)},{Esc(val)},{unit},{Esc(desc)},");
                    }
                    AddAttr("Voltage", HandoverHelper.Gs(el, ParamRegistry.ELC_VOLTAGE), "V", "Electrical voltage");
                    AddAttr("Power", HandoverHelper.Gs(el, ParamRegistry.ELC_POWER), "kW", "Electrical power");
                    AddAttr("AirFlow", HandoverHelper.Gs(el, ParamRegistry.HVC_DUCT_FLOW), "L/s", "Air flow rate");
                    AddAttr("PipeFlow", HandoverHelper.Gs(el, ParamRegistry.PLM_PIPE_FLOW), "L/s", "Pipe flow rate");
                    AddAttr("Status", HandoverHelper.Gs(el, ParamRegistry.STATUS), "", "Asset status");
                    AddAttr("MaintenanceType", HandoverHelper.Gs(el, ParamRegistry.MNT_TYPE), "", "Maintenance type");
                    AddAttr("Uniformat", HandoverHelper.Gs(el, ParamRegistry.UNIFORMAT), "", "Uniformat classification");
                    AddAttr("OmniClass", HandoverHelper.Gs(el, ParamRegistry.OMNICLASS), "", "OmniClass classification");
                }

                // ── Job sheet (COBie 2.4 — maintenance tasks) ──
                var jobLines = new List<string>();
                jobLines.Add("Name,CreatedBy,CreatedOn,Category,Status,TypeName,Description,Duration,DurationUnit,Start,TaskStartUnit,Frequency,FrequencyUnit,Priors");
                var jobsSeen = new HashSet<string>();
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    string disc = HandoverHelper.Gs(el, ParamRegistry.DISC);
                    string sys = HandoverHelper.Gs(el, ParamRegistry.SYS);
                    string typName = ParameterHelpers.GetFamilySymbolName(el);
                    string jobKey = $"{disc}-{sys}-{typName}";
                    if (jobsSeen.Contains(jobKey)) continue;
                    jobsSeen.Add(jobKey);

                    string freq = HandoverHelper.GetMaintenanceFrequency(disc, sys);
                    string mntType = HandoverHelper.GetDefaultMaintenanceType(disc, sys);
                    string priority = HandoverHelper.GetMaintenancePriority(disc, sys);
                    string freqUnit = freq == "Quarterly" ? "3" : freq == "6-Monthly" ? "6" : "12";

                    jobLines.Add($"{Esc($"PPM-{jobKey}")},STING Tools,{DateTime.Now:yyyy-MM-dd},Preventive,Required," +
                        $"{Esc(typName)},{Esc(mntType)},2,hour,,month,{freqUnit},month,");
                }

                // ── Resource sheet (COBie 2.4 — spare parts/materials) ──
                var resLines = new List<string>();
                resLines.Add("Name,CreatedBy,CreatedOn,Category,Description");
                var resSeen = new HashSet<string>();
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    string disc = HandoverHelper.Gs(el, ParamRegistry.DISC);
                    string sys = HandoverHelper.Gs(el, ParamRegistry.SYS);
                    var spares = HandoverHelper.GetSpareParts(disc, sys);
                    foreach (string spare in spares)
                    {
                        if (resSeen.Contains(spare)) continue;
                        resSeen.Add(spare);
                        resLines.Add($"{Esc(spare)},STING Tools,{DateTime.Now:yyyy-MM-dd},Spare Part,{Esc(spare)}");
                    }
                }

                // Write all 12 COBie CSV files
                string facilityPath = Path.Combine(outputDir, $"{prefix}_Facility.csv");
                string floorPath = Path.Combine(outputDir, $"{prefix}_Floor.csv");
                string spacePath = Path.Combine(outputDir, $"{prefix}_Space.csv");
                string typePath = Path.Combine(outputDir, $"{prefix}_Type.csv");
                string compPath = Path.Combine(outputDir, $"{prefix}_Component.csv");
                string sysPath = Path.Combine(outputDir, $"{prefix}_System.csv");
                string zonePath = Path.Combine(outputDir, $"{prefix}_Zone.csv");
                string contactPath = Path.Combine(outputDir, $"{prefix}_Contact.csv");
                string attrPath = Path.Combine(outputDir, $"{prefix}_Attribute.csv");
                string jobPath = Path.Combine(outputDir, $"{prefix}_Job.csv");
                string resPath = Path.Combine(outputDir, $"{prefix}_Resource.csv");

                File.WriteAllText(facilityPath, "\uFEFF" + string.Join("\n", facilityLines));
                File.WriteAllText(floorPath, "\uFEFF" + string.Join("\n", floorLines));
                File.WriteAllText(spacePath, "\uFEFF" + string.Join("\n", spaceLines));
                File.WriteAllText(typePath, "\uFEFF" + string.Join("\n", typeLines));
                File.WriteAllText(compPath, "\uFEFF" + string.Join("\n", compLines));
                File.WriteAllText(sysPath, "\uFEFF" + string.Join("\n", sysLines));
                File.WriteAllText(zonePath, "\uFEFF" + string.Join("\n", zoneLines));
                File.WriteAllText(contactPath, "\uFEFF" + string.Join("\n", contactLines));
                File.WriteAllText(attrPath, "\uFEFF" + string.Join("\n", attrLines));
                File.WriteAllText(jobPath, "\uFEFF" + string.Join("\n", jobLines));
                File.WriteAllText(resPath, "\uFEFF" + string.Join("\n", resLines));

                string report = $"COBie 2.4 Export Complete\n\n" +
                    $"11 COBie sheets exported:\n" +
                    $"  Contact:    {contactLines.Count - 1} contacts\n" +
                    $"  Facility:   1 record\n" +
                    $"  Floor:      {floorLines.Count - 1} levels\n" +
                    $"  Space:      {spaceLines.Count - 1} rooms\n" +
                    $"  Type:       {typesSeen.Count} asset types\n" +
                    $"  Component:  {compCount} assets\n" +
                    $"  System:     {systemGroups.Count} systems\n" +
                    $"  Zone:       {zoneGroups.Count} zones\n" +
                    $"  Attribute:  {attrLines.Count - 1} properties\n" +
                    $"  Job:        {jobLines.Count - 1} maintenance tasks\n" +
                    $"  Resource:   {resLines.Count - 1} spare parts\n\n" +
                    $"Output: {outputDir}\\{prefix}_*.csv";

                TaskDialog.Show("COBie Export", report);
                StingLog.Info($"COBie export: {compCount} components, {typesSeen.Count} types → {outputDir}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("COBie export failed", ex);
                TaskDialog.Show("COBie Export", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        private static string Esc(string v) => HandoverHelper.Esc(v);
    }

    /// <summary>
    /// Export Planned Preventive Maintenance (PPM) schedule.
    /// Generates a CSV with all tagged assets, their maintenance type,
    /// system, location, and recommended maintenance intervals based on
    /// discipline and system type.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaintenanceScheduleExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(outputDir, $"STING_MaintenanceSchedule_{timestamp}.csv");

            try
            {
                var sb = new StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("AssetTag,Category,FamilyName,TypeName,Discipline,System,Function,Product," +
                    "Location,Zone,Level,Room,MaintenanceType,Frequency,Priority," +
                    "ConditionGrade,LikelyFailureMode,SpareParts,SafetyPrecautions," +
                    "Manufacturer,Model,Description,Status,Mark");

                int total = 0;
                var discCounts = new Dictionary<string, int>();

                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;

                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    total++;
                    string disc = HandoverHelper.Gs(el, ParamRegistry.DISC);
                    string sys = HandoverHelper.Gs(el, ParamRegistry.SYS);
                    string func = HandoverHelper.Gs(el, ParamRegistry.FUNC);
                    string prod = HandoverHelper.Gs(el, ParamRegistry.PROD);
                    string loc = HandoverHelper.Gs(el, ParamRegistry.LOC);
                    string zone = HandoverHelper.Gs(el, ParamRegistry.ZONE);
                    string lvl = HandoverHelper.Gs(el, ParamRegistry.LVL);
                    string roomName = HandoverHelper.Gs(el, ParamRegistry.ROOM_NAME);
                    if (string.IsNullOrEmpty(roomName)) roomName = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NAME);
                    string mntType = HandoverHelper.Gs(el, ParamRegistry.MNT_TYPE);
                    string mfr = HandoverHelper.Gs(el, ParamRegistry.MFR);
                    if (string.IsNullOrEmpty(mfr)) mfr = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                    string model = HandoverHelper.Gs(el, ParamRegistry.MODEL);
                    if (string.IsNullOrEmpty(model)) model = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MODEL);
                    string desc = HandoverHelper.Gs(el, ParamRegistry.DESC);
                    if (string.IsNullOrEmpty(desc)) desc = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    string status = HandoverHelper.Gs(el, ParamRegistry.STATUS);
                    string mark = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MARK);
                    string familyName = ParameterHelpers.GetFamilyName(el);
                    string typeName = ParameterHelpers.GetFamilySymbolName(el);

                    // Derive maintenance frequency from discipline/system
                    string freq = HandoverHelper.GetMaintenanceFrequency(disc, sys);
                    string priority = HandoverHelper.GetMaintenancePriority(disc, sys);
                    if (string.IsNullOrEmpty(mntType))
                        mntType = HandoverHelper.GetDefaultMaintenanceType(disc, sys);

                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                        discCounts[disc]++;
                    }

                    // ASTM E2018 condition grade + CIBSE failure mode + spare parts
                    string condGrade = HandoverHelper.GetConditionGrade(status);
                    string failureMode = HandoverHelper.GetLikelyFailureMode(disc, sys, cat);
                    string spares = string.Join("; ", HandoverHelper.GetSpareParts(disc, sys));
                    string safety = HandoverHelper.GetSafetyPrecautions(disc, sys);

                    sb.Append(Esc(tag1)).Append(',');
                    sb.Append(Esc(cat)).Append(',');
                    sb.Append(Esc(familyName)).Append(',');
                    sb.Append(Esc(typeName)).Append(',');
                    sb.Append(disc).Append(',');
                    sb.Append(sys).Append(',');
                    sb.Append(func).Append(',');
                    sb.Append(prod).Append(',');
                    sb.Append(loc).Append(',');
                    sb.Append(zone).Append(',');
                    sb.Append(lvl).Append(',');
                    sb.Append(Esc(roomName)).Append(',');
                    sb.Append(Esc(mntType)).Append(',');
                    sb.Append(freq).Append(',');
                    sb.Append(priority).Append(',');
                    sb.Append(condGrade).Append(',');
                    sb.Append(Esc(failureMode)).Append(',');
                    sb.Append(Esc(spares)).Append(',');
                    sb.Append(Esc(safety)).Append(',');
                    sb.Append(Esc(mfr)).Append(',');
                    sb.Append(Esc(model)).Append(',');
                    sb.Append(Esc(desc)).Append(',');
                    sb.Append(status).Append(',');
                    sb.AppendLine(Esc(mark));
                }

                File.WriteAllText(path, sb.ToString());

                string discSummary = string.Join(", ", discCounts.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}:{x.Value}"));

                TaskDialog.Show("Maintenance Schedule Export",
                    $"Maintenance schedule exported.\n\n" +
                    $"Tagged assets: {total}\n" +
                    $"By discipline: {discSummary}\n\n" +
                    $"File: {Path.GetFileName(path)}");

                StingLog.Info($"Maintenance schedule: {total} assets → {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Maintenance schedule export failed", ex);
                TaskDialog.Show("Maintenance Schedule", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        private static string Esc(string v) => HandoverHelper.Esc(v);
    }

    /// <summary>
    /// Export O&amp;M (Operations &amp; Maintenance) Manual.
    /// Generates a comprehensive text/CSV document organized by discipline
    /// and system, listing all tagged assets with their specifications,
    /// spatial context, maintenance requirements, and manufacturer data.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OAndMManualExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(outputDir, $"STING_OM_Manual_{timestamp}.txt");

            try
            {
                var sb = new StringBuilder();

                // ── Title page ──
                string projName = doc.Title ?? "Unknown Project";
                string projNum = HandoverHelper.GetProjectInfoParam(doc, "Project Number");
                string client = HandoverHelper.GetProjectInfoParam(doc, "Client Name");
                string address = HandoverHelper.GetProjectInfoParam(doc, "Project Address");

                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("  OPERATIONS & MAINTENANCE MANUAL");
                sb.AppendLine("  ISO 19650 Compliant Asset Handover Document");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine();
                sb.AppendLine($"  Project:     {projName}");
                if (!string.IsNullOrEmpty(projNum))
                    sb.AppendLine($"  Project No:  {projNum}");
                if (!string.IsNullOrEmpty(client))
                    sb.AppendLine($"  Client:      {client}");
                if (!string.IsNullOrEmpty(address))
                    sb.AppendLine($"  Address:     {address}");
                sb.AppendLine($"  Generated:   {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"  Generator:   STING Tools BIM Automation");
                sb.AppendLine();

                // ── Collect all assets by discipline → system ──
                var assets = new Dictionary<string, Dictionary<string, List<AssetRecord>>>();
                int totalAssets = 0;

                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;

                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    totalAssets++;
                    var rec = new AssetRecord
                    {
                        Tag = tag1,
                        Category = cat,
                        Family = ParameterHelpers.GetFamilyName(el),
                        Type = ParameterHelpers.GetFamilySymbolName(el),
                        Disc = HandoverHelper.Gs(el, ParamRegistry.DISC),
                        Sys = HandoverHelper.Gs(el, ParamRegistry.SYS),
                        Func = HandoverHelper.Gs(el, ParamRegistry.FUNC),
                        Prod = HandoverHelper.Gs(el, ParamRegistry.PROD),
                        Loc = HandoverHelper.Gs(el, ParamRegistry.LOC),
                        Zone = HandoverHelper.Gs(el, ParamRegistry.ZONE),
                        Level = HandoverHelper.Gs(el, ParamRegistry.LVL),
                        Room = HandoverHelper.Gs(el, ParamRegistry.ROOM_NAME),
                        Mfr = HandoverHelper.Gs(el, ParamRegistry.MFR),
                        Model = HandoverHelper.Gs(el, ParamRegistry.MODEL),
                        Desc = HandoverHelper.Gs(el, ParamRegistry.DESC),
                        Status = HandoverHelper.Gs(el, ParamRegistry.STATUS),
                        MntType = HandoverHelper.Gs(el, ParamRegistry.MNT_TYPE),
                    };
                    if (string.IsNullOrEmpty(rec.Room))
                        rec.Room = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NAME);
                    if (string.IsNullOrEmpty(rec.Mfr))
                        rec.Mfr = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                    if (string.IsNullOrEmpty(rec.Model))
                        rec.Model = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MODEL);
                    if (string.IsNullOrEmpty(rec.Desc))
                        rec.Desc = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_DESCRIPTION);

                    // MEP specifics
                    rec.Power = HandoverHelper.Gs(el, ParamRegistry.ELC_POWER);
                    rec.Voltage = HandoverHelper.Gs(el, ParamRegistry.ELC_VOLTAGE);
                    rec.Flow = HandoverHelper.Gs(el, ParamRegistry.HVC_DUCT_FLOW);
                    if (string.IsNullOrEmpty(rec.Flow))
                        rec.Flow = HandoverHelper.Gs(el, ParamRegistry.PLM_PIPE_FLOW);

                    string discKey = string.IsNullOrEmpty(rec.Disc) ? "XX" : rec.Disc;
                    string sysKey = string.IsNullOrEmpty(rec.Sys) ? "GEN" : rec.Sys;

                    if (!assets.ContainsKey(discKey))
                        assets[discKey] = new Dictionary<string, List<AssetRecord>>();
                    if (!assets[discKey].ContainsKey(sysKey))
                        assets[discKey][sysKey] = new List<AssetRecord>();
                    assets[discKey][sysKey].Add(rec);
                }

                // ── Table of Contents ──
                sb.AppendLine("───────────────────────────────────────────────────────────────");
                sb.AppendLine("  TABLE OF CONTENTS");
                sb.AppendLine("───────────────────────────────────────────────────────────────");
                sb.AppendLine();
                int section = 1;
                foreach (var disc in assets.OrderBy(x => x.Key))
                {
                    string discName = HandoverHelper.GetDisciplineName(disc.Key);
                    int discTotal = disc.Value.Values.Sum(v => v.Count);
                    sb.AppendLine($"  {section}. {discName} ({disc.Key}) — {discTotal} assets");
                    int sub = 1;
                    foreach (var sys in disc.Value.OrderBy(x => x.Key))
                    {
                        sb.AppendLine($"     {section}.{sub} {sys.Key} System — {sys.Value.Count} assets");
                        sub++;
                    }
                    section++;
                }
                sb.AppendLine();

                // ── Asset sections by discipline/system ──
                section = 1;
                foreach (var disc in assets.OrderBy(x => x.Key))
                {
                    string discName = HandoverHelper.GetDisciplineName(disc.Key);
                    sb.AppendLine("═══════════════════════════════════════════════════════════════");
                    sb.AppendLine($"  SECTION {section}: {discName} ({disc.Key})");
                    sb.AppendLine("═══════════════════════════════════════════════════════════════");
                    sb.AppendLine();

                    int sub = 1;
                    foreach (var sys in disc.Value.OrderBy(x => x.Key))
                    {
                        sb.AppendLine($"  {section}.{sub} {sys.Key} System");
                        sb.AppendLine("  ─────────────────────────────────────────");
                        sb.AppendLine();

                        foreach (var rec in sys.Value.OrderBy(x => x.Tag))
                        {
                            sb.AppendLine($"    Asset Tag:     {rec.Tag}");
                            sb.AppendLine($"    Category:      {rec.Category}");
                            sb.AppendLine($"    Family/Type:   {rec.Family} : {rec.Type}");
                            if (!string.IsNullOrEmpty(rec.Desc))
                                sb.AppendLine($"    Description:   {rec.Desc}");
                            sb.AppendLine($"    Location:      {rec.Loc} / {rec.Zone} / {rec.Level}");
                            if (!string.IsNullOrEmpty(rec.Room))
                                sb.AppendLine($"    Room:          {rec.Room}");
                            if (!string.IsNullOrEmpty(rec.Mfr))
                                sb.AppendLine($"    Manufacturer:  {rec.Mfr}");
                            if (!string.IsNullOrEmpty(rec.Model))
                                sb.AppendLine($"    Model:         {rec.Model}");
                            if (!string.IsNullOrEmpty(rec.Status))
                                sb.AppendLine($"    Status:        {rec.Status}");
                            if (!string.IsNullOrEmpty(rec.Power))
                                sb.AppendLine($"    Power:         {rec.Power} kW");
                            if (!string.IsNullOrEmpty(rec.Voltage))
                                sb.AppendLine($"    Voltage:       {rec.Voltage} V");
                            if (!string.IsNullOrEmpty(rec.Flow))
                                sb.AppendLine($"    Flow:          {rec.Flow}");

                            // Maintenance & Safety info (CIBSE Guide M / ASTM E2018)
                            string freq = HandoverHelper.GetMaintenanceFrequency(rec.Disc, rec.Sys);
                            string mntType = !string.IsNullOrEmpty(rec.MntType) ? rec.MntType
                                : HandoverHelper.GetDefaultMaintenanceType(rec.Disc, rec.Sys);
                            string condGrade = HandoverHelper.GetConditionGrade(rec.Status);
                            string failMode = HandoverHelper.GetLikelyFailureMode(rec.Disc, rec.Sys, rec.Category);
                            string safety = HandoverHelper.GetSafetyPrecautions(rec.Disc, rec.Sys);
                            var spares = HandoverHelper.GetSpareParts(rec.Disc, rec.Sys);

                            sb.AppendLine($"    Condition:     Grade {condGrade}");
                            sb.AppendLine($"    Maintenance:   {mntType} ({freq})");
                            sb.AppendLine($"    Failure Mode:  {failMode}");
                            if (spares.Count > 0)
                                sb.AppendLine($"    Spare Parts:   {string.Join(", ", spares)}");
                            sb.AppendLine($"    Safety:        {safety}");
                            sb.AppendLine();
                        }

                        sub++;
                    }
                    section++;
                }

                // ── Summary ──
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("  SUMMARY");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine();
                sb.AppendLine($"  Total assets:      {totalAssets}");
                sb.AppendLine($"  Disciplines:       {assets.Count}");
                sb.AppendLine($"  Systems:           {assets.Values.Sum(d => d.Count)}");
                sb.AppendLine($"  Levels:            {new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount()}");
                sb.AppendLine();
                sb.AppendLine("  Generated by STING Tools — ISO 19650 BIM Automation");
                sb.AppendLine($"  {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine();

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

                TaskDialog.Show("O&M Manual Export",
                    $"Operations & Maintenance Manual exported.\n\n" +
                    $"Total assets: {totalAssets}\n" +
                    $"Disciplines: {assets.Count}\n" +
                    $"Systems: {assets.Values.Sum(d => d.Count)}\n\n" +
                    $"File: {Path.GetFileName(path)}");

                StingLog.Info($"O&M Manual: {totalAssets} assets → {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("O&M Manual export failed", ex);
                TaskDialog.Show("O&M Manual", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        private class AssetRecord
        {
            public string Tag, Category, Family, Type;
            public string Disc, Sys, Func, Prod;
            public string Loc, Zone, Level, Room;
            public string Mfr, Model, Desc, Status, MntType;
            public string Power, Voltage, Flow;
        }
    }

    /// <summary>
    /// Export Asset Health &amp; Condition Report.
    /// Inspired by StingBIM.AI.FacilityManagement predictive analytics.
    /// Scores each tagged asset on a 0-100 health scale based on tag completeness,
    /// maintenance type assignment, manufacturer data completeness, and ISO 19650
    /// compliance. Exports CSV with health scores, risk levels, and recommendations.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssetHealthReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(outputDir, $"STING_AssetHealth_{timestamp}.csv");

            try
            {
                var sb = new StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("AssetTag,Category,FamilyName,TypeName,Discipline,System,Level,Room," +
                    "HealthScore,HealthStatus,RiskLevel," +
                    "TagScore,DataScore,MfrScore,MaintenanceScore,SpatialScore," +
                    "Issues,Recommendation");

                int total = 0;
                int healthy = 0;
                int atRisk = 0;
                int critical = 0;
                var discScores = new Dictionary<string, List<int>>();

                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;

                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    total++;
                    string disc = HandoverHelper.Gs(el, ParamRegistry.DISC);
                    string sys = HandoverHelper.Gs(el, ParamRegistry.SYS);
                    string lvl = HandoverHelper.Gs(el, ParamRegistry.LVL);
                    string room = HandoverHelper.Gs(el, ParamRegistry.ROOM_NAME);
                    if (string.IsNullOrEmpty(room)) room = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NAME);
                    string familyName = ParameterHelpers.GetFamilyName(el);
                    string typeName = ParameterHelpers.GetFamilySymbolName(el);

                    // Calculate health score components (0-20 each, total 0-100)
                    var issues = new List<string>();

                    // 1. Tag completeness score (0-20)
                    int tagScore = 20;
                    if (!TagConfig.TagIsComplete(tag1)) { tagScore = 5; issues.Add("Incomplete tag"); }
                    else if (!TagConfig.TagIsFullyResolved(tag1)) { tagScore = 12; issues.Add("Tag has placeholders"); }

                    // 2. Data completeness score (0-20)
                    int dataScore = 20;
                    string desc = HandoverHelper.Gs(el, ParamRegistry.DESC);
                    if (string.IsNullOrEmpty(desc)) desc = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (string.IsNullOrEmpty(desc)) { dataScore -= 7; issues.Add("No description"); }
                    string mark = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MARK);
                    if (string.IsNullOrEmpty(mark)) { dataScore -= 5; issues.Add("No mark"); }
                    string status = HandoverHelper.Gs(el, ParamRegistry.STATUS);
                    if (string.IsNullOrEmpty(status)) { dataScore -= 4; issues.Add("No status"); }
                    string cost = HandoverHelper.Gs(el, ParamRegistry.COST);
                    if (string.IsNullOrEmpty(cost)) dataScore -= 4;

                    // 3. Manufacturer data score (0-20)
                    int mfrScore = 20;
                    string mfr = HandoverHelper.Gs(el, ParamRegistry.MFR);
                    if (string.IsNullOrEmpty(mfr)) mfr = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                    string model = HandoverHelper.Gs(el, ParamRegistry.MODEL);
                    if (string.IsNullOrEmpty(model)) model = HandoverHelper.Gp(el, BuiltInParameter.ALL_MODEL_MODEL);
                    if (string.IsNullOrEmpty(mfr)) { mfrScore -= 10; issues.Add("No manufacturer"); }
                    if (string.IsNullOrEmpty(model)) { mfrScore -= 10; issues.Add("No model"); }

                    // 4. Maintenance readiness score (0-20)
                    int maintScore = 20;
                    string mntType = HandoverHelper.Gs(el, ParamRegistry.MNT_TYPE);
                    if (string.IsNullOrEmpty(mntType)) { maintScore -= 10; issues.Add("No maintenance type"); }
                    string priority = HandoverHelper.GetMaintenancePriority(disc, sys);
                    if (priority == "Critical" && string.IsNullOrEmpty(mntType)) maintScore -= 5;

                    // 5. Spatial data score (0-20)
                    int spatialScore = 20;
                    if (string.IsNullOrEmpty(room)) { spatialScore -= 10; issues.Add("No room assignment"); }
                    if (string.IsNullOrEmpty(lvl) || lvl == "XX") { spatialScore -= 5; issues.Add("No level"); }
                    string zone = HandoverHelper.Gs(el, ParamRegistry.ZONE);
                    if (string.IsNullOrEmpty(zone) || zone == "XX") { spatialScore -= 5; issues.Add("No zone"); }

                    int healthScore = Math.Max(0, tagScore + dataScore + mfrScore + maintScore + spatialScore);
                    string healthStatus, riskLevel;
                    if (healthScore >= 80) { healthStatus = "Excellent"; riskLevel = "Low"; healthy++; }
                    else if (healthScore >= 60) { healthStatus = "Good"; riskLevel = "Low"; healthy++; }
                    else if (healthScore >= 40) { healthStatus = "Fair"; riskLevel = "Medium"; atRisk++; }
                    else if (healthScore >= 20) { healthStatus = "Poor"; riskLevel = "High"; atRisk++; }
                    else { healthStatus = "Critical"; riskLevel = "Critical"; critical++; }

                    // Recommendation based on worst scoring area
                    string recommendation = "";
                    int minScore = Math.Min(tagScore, Math.Min(dataScore, Math.Min(mfrScore, Math.Min(maintScore, spatialScore))));
                    if (minScore == tagScore && tagScore < 15) recommendation = "Complete ISO 19650 tag";
                    else if (minScore == mfrScore && mfrScore < 15) recommendation = "Add manufacturer/model data";
                    else if (minScore == maintScore && maintScore < 15) recommendation = "Assign maintenance type";
                    else if (minScore == spatialScore && spatialScore < 15) recommendation = "Verify spatial assignment";
                    else if (minScore == dataScore && dataScore < 15) recommendation = "Complete asset data fields";

                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!discScores.ContainsKey(disc)) discScores[disc] = new List<int>();
                        discScores[disc].Add(healthScore);
                    }

                    string issueStr = issues.Count > 0 ? string.Join("; ", issues) : "";
                    sb.Append(Esc(tag1)).Append(',');
                    sb.Append(Esc(cat)).Append(',');
                    sb.Append(Esc(familyName)).Append(',');
                    sb.Append(Esc(typeName)).Append(',');
                    sb.Append(disc).Append(',');
                    sb.Append(sys).Append(',');
                    sb.Append(lvl).Append(',');
                    sb.Append(Esc(room)).Append(',');
                    sb.Append(healthScore).Append(',');
                    sb.Append(healthStatus).Append(',');
                    sb.Append(riskLevel).Append(',');
                    sb.Append(tagScore).Append(',');
                    sb.Append(dataScore).Append(',');
                    sb.Append(mfrScore).Append(',');
                    sb.Append(maintScore).Append(',');
                    sb.Append(spatialScore).Append(',');
                    sb.Append(Esc(issueStr)).Append(',');
                    sb.AppendLine(Esc(recommendation));
                }

                File.WriteAllText(path, sb.ToString());

                string discSummary = string.Join("\n", discScores.OrderBy(x => x.Key)
                    .Select(x => $"  {HandoverHelper.GetDisciplineName(x.Key)}: avg {x.Value.Average():F0}/100 ({x.Value.Count} assets)"));

                TaskDialog.Show("Asset Health Report",
                    $"Asset Health Report exported.\n\n" +
                    $"Total assets: {total}\n" +
                    $"  Healthy (60+): {healthy}\n" +
                    $"  At Risk (<60): {atRisk}\n" +
                    $"  Critical (<20): {critical}\n\n" +
                    $"By discipline:\n{discSummary}\n\n" +
                    $"File: {Path.GetFileName(path)}");

                StingLog.Info($"Asset Health: {total} assets, {healthy} healthy, {atRisk} at-risk → {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Asset Health Report failed", ex);
                TaskDialog.Show("Asset Health", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        private static string Esc(string v) => HandoverHelper.Esc(v);
    }

    /// <summary>
    /// Export Space Handover Report.
    /// Inspired by StingBIM.AI.TenantManagement space management.
    /// Lists all rooms/spaces with their tagged asset counts, discipline breakdown,
    /// area, department, and asset density — useful for FM space planning and
    /// tenant handover documentation.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpaceHandoverReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(outputDir, $"STING_SpaceHandover_{timestamp}.csv");

            try
            {
                // Build room → asset index
                var roomAssets = new Dictionary<string, SpaceInfo>();

                // First, collect all rooms
                foreach (Room room in new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement)).OfType<Room>().Where(r => r.Area > 0))
                {
                    string key = $"{room.Number}|{room.Name}";
                    if (!roomAssets.ContainsKey(key))
                    {
                        roomAssets[key] = new SpaceInfo
                        {
                            RoomNumber = room.Number ?? "",
                            RoomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                            Level = room.Level?.Name ?? "",
                            Department = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "",
                            Area = room.Area * 0.092903, // sq ft to sq m
                        };
                    }
                }

                // Map assets to rooms
                int totalMapped = 0;
                int unmapped = 0;
                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;

                    string tag1 = HandoverHelper.Gs(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;

                    string roomName = HandoverHelper.Gs(el, ParamRegistry.ROOM_NAME);
                    if (string.IsNullOrEmpty(roomName)) roomName = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NAME);
                    string roomNum = HandoverHelper.Gs(el, ParamRegistry.ROOM_NUM);
                    if (string.IsNullOrEmpty(roomNum)) roomNum = HandoverHelper.Gs(el, ParamRegistry.BLE_ROOM_NUM);

                    string key = $"{roomNum}|{roomName}";
                    if (string.IsNullOrEmpty(roomName) && string.IsNullOrEmpty(roomNum))
                    {
                        key = "(Unassigned)|(No Room)";
                        unmapped++;
                    }
                    else
                    {
                        totalMapped++;
                    }

                    if (!roomAssets.ContainsKey(key))
                    {
                        roomAssets[key] = new SpaceInfo
                        {
                            RoomNumber = roomNum ?? "",
                            RoomName = roomName ?? "",
                            Level = HandoverHelper.Gs(el, ParamRegistry.LVL),
                        };
                    }

                    var info = roomAssets[key];
                    info.TotalAssets++;
                    string disc = HandoverHelper.Gs(el, ParamRegistry.DISC);
                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!info.ByDiscipline.ContainsKey(disc)) info.ByDiscipline[disc] = 0;
                        info.ByDiscipline[disc]++;
                    }
                    string sys = HandoverHelper.Gs(el, ParamRegistry.SYS);
                    if (!string.IsNullOrEmpty(sys))
                    {
                        if (!info.BySystems.ContainsKey(sys)) info.BySystems[sys] = 0;
                        info.BySystems[sys]++;
                    }
                }

                // Write CSV
                var sb = new StringBuilder();
                sb.Append('\uFEFF');
                sb.AppendLine("RoomNumber,RoomName,Level,Department,Area_m2,TotalAssets,AssetDensity," +
                    "MechanicalAssets,ElectricalAssets,PlumbingAssets,FireProtectionAssets,OtherAssets," +
                    "Systems,TopSystem");

                foreach (var kvp in roomAssets.OrderBy(x => x.Value.Level).ThenBy(x => x.Value.RoomNumber))
                {
                    var info = kvp.Value;
                    int mechCount = info.ByDiscipline.ContainsKey("M") ? info.ByDiscipline["M"] : 0;
                    int elecCount = info.ByDiscipline.ContainsKey("E") ? info.ByDiscipline["E"] : 0;
                    int plumCount = info.ByDiscipline.ContainsKey("P") ? info.ByDiscipline["P"] : 0;
                    int fireCount = (info.ByDiscipline.ContainsKey("FP") ? info.ByDiscipline["FP"] : 0);
                    int otherCount = info.TotalAssets - mechCount - elecCount - plumCount - fireCount;
                    string density = info.Area > 0 ? (info.TotalAssets / info.Area).ToString("F2") : "";
                    string systems = string.Join("; ", info.BySystems.Keys.OrderBy(x => x));
                    string topSys = info.BySystems.Count > 0
                        ? info.BySystems.OrderByDescending(x => x.Value).First().Key : "";

                    sb.Append(Esc(info.RoomNumber)).Append(',');
                    sb.Append(Esc(info.RoomName)).Append(',');
                    sb.Append(Esc(info.Level)).Append(',');
                    sb.Append(Esc(info.Department)).Append(',');
                    sb.Append(info.Area > 0 ? info.Area.ToString("F2") : "").Append(',');
                    sb.Append(info.TotalAssets).Append(',');
                    sb.Append(density).Append(',');
                    sb.Append(mechCount).Append(',');
                    sb.Append(elecCount).Append(',');
                    sb.Append(plumCount).Append(',');
                    sb.Append(fireCount).Append(',');
                    sb.Append(otherCount).Append(',');
                    sb.Append(Esc(systems)).Append(',');
                    sb.AppendLine(Esc(topSys));
                }

                File.WriteAllText(path, sb.ToString());

                TaskDialog.Show("Space Handover Report",
                    $"Space Handover Report exported.\n\n" +
                    $"Rooms/spaces: {roomAssets.Count}\n" +
                    $"Assets mapped to rooms: {totalMapped}\n" +
                    $"Assets without room: {unmapped}\n\n" +
                    $"File: {Path.GetFileName(path)}");

                StingLog.Info($"Space Handover: {roomAssets.Count} rooms, {totalMapped} assets → {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Space Handover Report failed", ex);
                TaskDialog.Show("Space Handover", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        private class SpaceInfo
        {
            public string RoomNumber = "", RoomName = "", Level = "", Department = "";
            public double Area;
            public int TotalAssets;
            public Dictionary<string, int> ByDiscipline = new Dictionary<string, int>();
            public Dictionary<string, int> BySystems = new Dictionary<string, int>();
        }

        private static string Esc(string v) => HandoverHelper.Esc(v);
    }

    /// <summary>
    /// Shared helper methods for FM/O&amp;M handover export commands.
    /// </summary>
    internal static class HandoverHelper
    {
        /// <summary>Read STING shared parameter as string.</summary>
        internal static string Gs(Element el, string paramName)
        {
            return ParameterHelpers.GetString(el, paramName);
        }

        /// <summary>Read Revit built-in parameter as string.</summary>
        internal static string Gp(Element el, BuiltInParameter bip)
        {
            try
            {
                Parameter p = el.get_Parameter(bip);
                if (p == null) return "";
                return p.StorageType == StorageType.String
                    ? p.AsString() ?? ""
                    : p.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        /// <summary>CSV-escape a field value.</summary>
        internal static string Esc(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Contains(",") || v.Contains("\"") || v.Contains("\n"))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        /// <summary>Get a project information parameter value.</summary>
        internal static string GetProjectInfoParam(Document doc, string paramName)
        {
            try
            {
                Element projInfo = doc.ProjectInformation;
                if (projInfo == null) return "";
                foreach (Parameter p in projInfo.Parameters)
                {
                    if (p.Definition.Name == paramName)
                        return p.AsString() ?? "";
                }
                return "";
            }
            catch { return ""; }
        }

        /// <summary>Get the current phase name from the document.</summary>
        internal static string GetCurrentPhase(Document doc)
        {
            try
            {
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();
                return phases.LastOrDefault()?.Name ?? "New Construction";
            }
            catch { return "New Construction"; }
        }

        /// <summary>Get discipline full name from code.</summary>
        internal static string GetDisciplineName(string code)
        {
            switch (code)
            {
                case "M": return "Mechanical";
                case "E": return "Electrical";
                case "P": return "Plumbing";
                case "A": return "Architectural";
                case "S": return "Structural";
                case "FP": return "Fire Protection";
                case "LV": return "Low Voltage";
                case "G": return "General";
                default: return code;
            }
        }

        /// <summary>
        /// Derive recommended maintenance frequency from discipline and system type.
        /// Based on CIBSE Guide M maintenance intervals.
        /// </summary>
        internal static string GetMaintenanceFrequency(string disc, string sys)
        {
            if (string.IsNullOrEmpty(sys)) sys = "";
            switch (sys)
            {
                case "HVAC": return "Quarterly";
                case "DCW":
                case "DHW":
                case "HWS": return "6-Monthly";
                case "SAN":
                case "RWD": return "Annually";
                case "GAS": return "Annually";
                case "FP":
                case "FLS": return "6-Monthly";
                case "LV":
                case "COM":
                case "ICT":
                case "SEC":
                case "NCL": return "Annually";
                default:
                    switch (disc)
                    {
                        case "M": return "Quarterly";
                        case "E": return "Annually";
                        case "P": return "6-Monthly";
                        case "FP": return "6-Monthly";
                        case "A": return "Annually";
                        default: return "Annually";
                    }
            }
        }

        /// <summary>Derive maintenance priority from discipline and system.</summary>
        internal static string GetMaintenancePriority(string disc, string sys)
        {
            if (string.IsNullOrEmpty(sys)) sys = "";
            switch (sys)
            {
                case "FP":
                case "FLS":
                case "GAS": return "Critical";
                case "HVAC":
                case "DCW":
                case "DHW":
                case "HWS": return "High";
                case "SAN":
                case "RWD": return "Medium";
                case "LV":
                case "COM":
                case "ICT":
                case "SEC": return "Medium";
                default:
                    switch (disc)
                    {
                        case "FP": return "Critical";
                        case "M": return "High";
                        case "E": return "High";
                        case "P": return "Medium";
                        default: return "Low";
                    }
            }
        }

        /// <summary>Derive default maintenance type from discipline and system.</summary>
        internal static string GetDefaultMaintenanceType(string disc, string sys)
        {
            if (string.IsNullOrEmpty(sys)) sys = "";
            switch (sys)
            {
                case "HVAC": return "Filter change, belt inspection, coil clean";
                case "DCW":
                case "DHW":
                case "HWS": return "TMV check, temperature monitoring, flush";
                case "SAN":
                case "RWD": return "Drain inspection, trap clean";
                case "GAS": return "Leak test, ventilation check";
                case "FP":
                case "FLS": return "Alarm test, detector clean, extinguisher check";
                case "LV":
                case "COM":
                case "ICT": return "Connection test, firmware update";
                case "SEC": return "Sensor test, camera clean";
                default:
                    switch (disc)
                    {
                        case "M": return "General mechanical inspection";
                        case "E": return "Electrical safety test, thermal scan";
                        case "P": return "Plumbing inspection, valve check";
                        case "A": return "Visual inspection, finish check";
                        default: return "General inspection";
                    }
            }
        }

        /// <summary>
        /// ASTM E2018 condition grade based on asset status.
        /// A=New/Excellent, B=Good, C=Fair, D=Poor, E=Very Poor, F=Failed/End of Life.
        /// </summary>
        internal static string GetConditionGrade(string status)
        {
            if (string.IsNullOrEmpty(status)) return "B";
            switch (status.ToUpper())
            {
                case "NEW": return "A";
                case "EXISTING": return "B";
                case "REFURBISHED": return "B";
                case "TEMPORARY": return "C";
                case "DEMOLISHED": return "F";
                default: return "B";
            }
        }

        /// <summary>
        /// Likely failure mode based on discipline, system, and category.
        /// From CIBSE Guide M failure mode analysis.
        /// </summary>
        internal static string GetLikelyFailureMode(string disc, string sys, string category)
        {
            if (string.IsNullOrEmpty(sys)) sys = "";
            switch (sys)
            {
                case "HVAC": return "Wear (bearings, belts), Contamination (coils, filters)";
                case "DCW":
                case "DHW":
                case "HWS": return "Corrosion (pipes, fittings), Leakage (valves, joints)";
                case "SAN":
                case "RWD": return "Corrosion, Blockage, Leakage (traps, seals)";
                case "GAS": return "Leakage (joints, valves), Corrosion";
                case "FP":
                case "FLS": return "Electrical (sensor drift, wiring), Contamination (detector heads)";
                case "LV":
                case "COM":
                case "ICT": return "Electrical (connection failure), ThermalDegradation";
                case "SEC": return "Electrical (sensor failure), Wear (mechanical locks)";
                default:
                    switch (disc ?? "")
                    {
                        case "M": return "Wear, Fatigue, Misalignment";
                        case "E": return "Electrical (insulation breakdown), Overload, ThermalDegradation";
                        case "P": return "Corrosion, Leakage, Wear (valves, seals)";
                        case "A": return "Wear (finishes, hardware), Fatigue (structural sealants)";
                        default: return "General wear and degradation";
                    }
            }
        }

        /// <summary>
        /// Recommended spare parts for a given discipline/system.
        /// Based on typical PPM spare parts inventory from FM best practice.
        /// </summary>
        internal static List<string> GetSpareParts(string disc, string sys)
        {
            if (string.IsNullOrEmpty(sys)) sys = "";
            switch (sys)
            {
                case "HVAC": return new List<string> { "Filters", "Drive belts", "Bearings", "Contactors", "Thermostats" };
                case "DCW":
                case "DHW":
                case "HWS": return new List<string> { "TMV cartridges", "Flexible hoses", "Isolation valves", "Gaskets" };
                case "SAN":
                case "RWD": return new List<string> { "Trap seals", "Rodding eyes", "Waste fittings" };
                case "GAS": return new List<string> { "Gas valves", "Flame sensors", "Burner nozzles" };
                case "FP":
                case "FLS": return new List<string> { "Detector heads", "Call point glass", "Sounder units", "Batteries" };
                case "LV":
                case "COM":
                case "ICT": return new List<string> { "Patch cables", "Network switches", "UPS batteries" };
                case "SEC": return new List<string> { "PIR sensors", "Door contacts", "Camera lenses" };
                default:
                    switch (disc ?? "")
                    {
                        case "M": return new List<string> { "Bearings", "Seals", "Gaskets" };
                        case "E": return new List<string> { "Fuses", "MCBs", "Contactors", "Lamps" };
                        case "P": return new List<string> { "Washers", "Valves", "Float valves" };
                        default: return new List<string>();
                    }
            }
        }

        /// <summary>
        /// Safety precautions for maintenance by discipline/system.
        /// Based on CDM Regulations and CIBSE safe working practices.
        /// </summary>
        internal static string GetSafetyPrecautions(string disc, string sys)
        {
            if (string.IsNullOrEmpty(sys)) sys = "";
            switch (sys)
            {
                case "HVAC": return "Isolate power before access. LOTO required. Check for Legionella risk.";
                case "DCW":
                case "DHW":
                case "HWS": return "Isolate water supply. Hot water scald risk. Legionella control required.";
                case "SAN":
                case "RWD": return "PPE required (gloves, eye protection). Biological hazard.";
                case "GAS": return "Gas Safe registered engineer only. Ventilation check. LEL monitoring.";
                case "FP":
                case "FLS": return "Notify building occupants. Isolate zone. Fire watch during testing.";
                case "LV":
                case "COM":
                case "ICT": return "ESD precautions. Check UPS status before work.";
                case "SEC": return "Coordinate with security team. Log access.";
                default:
                    switch (disc ?? "")
                    {
                        case "M": return "LOTO required. Check rotating parts.";
                        case "E": return "Isolate at distribution board. LOTO. Prove dead before work.";
                        case "P": return "Isolate water supply. Drain down if needed.";
                        case "A": return "Access equipment as needed. Check for asbestos.";
                        default: return "Risk assessment required before work.";
                    }
            }
        }
    }
}
