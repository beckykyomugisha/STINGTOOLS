using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Export
{
    /// <summary>
    /// Minimal IFC 4 STEP-format export consumed by DIALux evo's IFC
    /// import. Emits IfcLightFixture entities for OST_LightingFixtures and
    /// IfcSpace entities for OST_Rooms with PropertySingleValue luminous
    /// flux + installed power. DIALux 12+ accepts this; older DIALux only
    /// reads IFC 2x3 and will fail.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DIALuxExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<SpatialElement>()
                .Where(r => (r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) > 0)
                .ToList();

            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            try { outDir = Path.Combine(outDir, "electrical"); Directory.CreateDirectory(outDir); } catch { }
            string outPath = Path.Combine(outDir, $"STING_DIALux_{DateTime.Now:yyyyMMdd-HHmm}.ifc");

            var lines = new List<string>
            {
                "ISO-10303-21;",
                "HEADER;",
                "FILE_DESCRIPTION(('STING DIALux Export'),'2;1');",
                $"FILE_NAME('{Path.GetFileName(outPath)}','{DateTime.Now:yyyy-MM-ddTHH:mm:ss}',(),(),'STING','','');",
                "FILE_SCHEMA(('IFC4'));",
                "ENDSEC;",
                "DATA;"
            };

            int id = 100;
            string projectName = doc.ProjectInformation?.Name ?? "STING Project";
            lines.Add($"#{id++}=IFCPROJECT('STING_PRJ',$,'{EscIfc(projectName)}',$,$,$,$,$,$);");

            foreach (var room in rooms)
            {
                lines.Add($"#{id++}=IFCSPACE('STING_RM_{room.Id.Value}',$," +
                          $"'{EscIfc(room.Name)}',$,$,$,$,$,.ELEMENT.,.NOTDEFINED.);");
            }
            foreach (var fix in fixtures)
            {
                double watts = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_WATTAGE));
                if (watts <= 0)
                {
                    try { watts = fix.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0; } catch { }
                }
                double lumens = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_LUMENS));
                if (lumens <= 0) lumens = watts * 80.0;

                lines.Add($"#{id++}=IFCLIGHTFIXTURE('STING_LF_{fix.Id.Value}',$," +
                          $"'{EscIfc(fix.Name)}',$,$,$,$,$,.NOTDEFINED.);");
                lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('LuminousFlux',$,IFCREAL({lumens.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}),$);");
                lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('InstalledPower',$,IFCREAL({watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}),$);");
            }
            lines.Add("ENDSEC;");
            lines.Add("END-ISO-10303-21;");

            try { File.WriteAllLines(outPath, lines, Encoding.UTF8); }
            catch (Exception ex)
            {
                StingLog.Error($"DIALux IFC write: {ex.Message}", ex);
                TaskDialog.Show("STING DIALux Export", $"Save failed: {ex.Message}");
                return Result.Failed;
            }

            TaskDialog.Show("STING DIALux Export",
                $"IFC 4 file exported for DIALux evo:\n{outPath}\n\n" +
                $"{fixtures.Count} luminaire(s) · {rooms.Count} room(s)\n\n" +
                "In DIALux evo: File → Import → IFC. Assign luminaire types after import.");
            return Result.Succeeded;
        }

        private static string EscIfc(string s) => (s ?? "").Replace("'", "''");
        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
    }
}
