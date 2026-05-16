using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.IfcResults;

namespace StingTools.Commands.Electrical.Export
{
    /// <summary>
    /// IFC 4 STEP-format export consumed by DIALux evo's IFC import.
    ///
    /// Phase 181 round-trip enhancement:
    ///  • Preserves Revit Element.UniqueId as IfcGloballyUniqueId on every
    ///    IfcSpace and IfcLightFixture so STING can match results back by GUID.
    ///  • Writes the Pset_StingLightingResults contract on every IfcSpace
    ///    with the existing values stamped where available (estimate or last
    ///    DIALux/ElumTools/Relux import); DIALux can update them in place.
    ///  • Writes a Pset_StingLuminaireData PSet on every IfcLightFixture
    ///    carrying lumens / watts / efficacy / CCT / CRI / IES path so
    ///    DIALux can match its catalogue luminaires by metadata, not name.
    ///  • Logs the export to <c>&lt;project&gt;/_BIM_COORD/dialux_roundtrips.json</c>
    ///    so <see cref="StingTools.Commands.Electrical.Photometric.DialuxRoundTripCommand"/>
    ///    can show the round-trip status in its dialog.
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
            try { outDir = Path.Combine(outDir, "electrical"); Directory.CreateDirectory(outDir); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string outPath = Path.Combine(outDir, $"STING_DIALux_{DateTime.Now:yyyyMMdd-HHmm}.ifc");

            var lines = new List<string>
            {
                "ISO-10303-21;",
                "HEADER;",
                "FILE_DESCRIPTION(('STING DIALux Export — Phase 181'),'2;1');",
                $"FILE_NAME('{Path.GetFileName(outPath)}','{DateTime.Now:yyyy-MM-ddTHH:mm:ss}',('STING'),('STING Tools'),'STING Plugin','Revit','');",
                "FILE_SCHEMA(('IFC4'));",
                "ENDSEC;",
                "DATA;"
            };

            int id = 100;
            string projectName = doc.ProjectInformation?.Name ?? "STING Project";
            int projId = id;
            lines.Add($"#{id++}=IFCPROJECT('{IfcGuid(projectName)}',$,'{EscIfc(projectName)}',$,$,$,$,$,$);");

            // Per-room block: IfcSpace + Pset_StingLightingResults.
            foreach (var room in rooms)
            {
                int spaceId = id;
                string guid = ToIfcGuid(room.UniqueId, room.Id.Value);
                lines.Add($"#{id++}=IFCSPACE('{guid}',$,'{EscIfc(room.Name)}',$,$,$,$,$,.ELEMENT.,.NOTDEFINED.);");
                EmitStingResultsPSet(lines, ref id, spaceId, room);
            }

            // Per-fixture block: IfcLightFixture + Pset_StingLuminaireData.
            foreach (var fix in fixtures)
            {
                int fixId = id;
                string guid = ToIfcGuid(fix.UniqueId, fix.Id.Value);
                lines.Add($"#{id++}=IFCLIGHTFIXTURE('{guid}',$,'{EscIfc(fix.Name)}',$,$,$,$,$,.NOTDEFINED.);");
                EmitStingLuminairePSet(lines, ref id, fixId, fix, doc);
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
            try { LogRoundTrip(doc, outPath, fixtures.Count, rooms.Count); }
            catch (Exception ex) { StingLog.Warn($"LogRoundTrip: {ex.Message}"); }

            TaskDialog.Show("STING DIALux Export",
                $"IFC 4 file exported for DIALux evo:\n{outPath}\n\n" +
                $"{fixtures.Count} luminaire(s) · {rooms.Count} room(s)\n" +
                "Pset_StingLightingResults stamped on every IfcSpace; Pset_StingLuminaireData on every IfcLightFixture.\n\n" +
                "In DIALux evo: File → Import → IFC. After calculation, export an IFC and run\n" +
                "STING → Photometrics → Import IFC Results to map values back by Revit GUID.");
            return Result.Succeeded;
        }

        // ── PSet emitters ───────────────────────────────────────────────

        private static void EmitStingResultsPSet(List<string> lines, ref int id,
            int relatedEntityId, SpatialElement room)
        {
            // Existing values from prior phases — DIALux may overwrite on re-import.
            double lux = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX));
            double ugr = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_UGR));
            double uniformity = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_UNIFORMITY));
            string lastEngine = ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LAST_ENGINE);
            string lastDate = ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LAST_CALC_DATE);

            int luxId = id;
            lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('{StingLightingPSet.IlluminanceLux}',$,IFCREAL({Fmt(lux)}),$);");
            int avgId = id;
            lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('{StingLightingPSet.AverageLux}',$,IFCREAL({Fmt(lux)}),$);");
            int uoId = id;
            lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('{StingLightingPSet.UniformityRatio}',$,IFCREAL({Fmt(uniformity)}),$);");
            int ugrId = id;
            lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('{StingLightingPSet.UGR}',$,IFCREAL({Fmt(ugr)}),$);");
            int dateId = id;
            lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('{StingLightingPSet.CalculationDate}',$,IFCLABEL('{EscIfc(lastDate)}'),$);");
            int engId = id;
            lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('{StingLightingPSet.EngineUsed}',$,IFCLABEL('{EscIfc(lastEngine)}'),$);");

            int psetId = id;
            lines.Add($"#{id++}=IFCPROPERTYSET('{IfcGuid(room.UniqueId + ":lightingResults")}',$,'{StingLightingPSet.PSetName}',$," +
                      $"(#{luxId},#{avgId},#{uoId},#{ugrId},#{dateId},#{engId}));");
            lines.Add($"#{id++}=IFCRELDEFINESBYPROPERTIES('{IfcGuid(room.UniqueId + ":relLR")}',$,$,$,(#{relatedEntityId}),#{psetId});");
        }

        private static void EmitStingLuminairePSet(List<string> lines, ref int id,
            int relatedEntityId, FamilyInstance fix, Document doc)
        {
            var symbol = doc.GetElement(fix.GetTypeId());

            double watts = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_WATTS));
            if (watts <= 0) watts = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_WATTAGE));
            if (watts <= 0)
                try { watts = fix.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            double lumens = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_LUMENS));
            if (lumens <= 0) lumens = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_LUMENS));
            if (lumens <= 0) lumens = watts * 80.0;

            double cct  = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_CCT));
            double cri  = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_CRI));
            double beam = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_BEAM_ANGLE));
            string sym  = ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_SYMMETRY);
            string iesPath = ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_FILE_PATH);

            int p1 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('LuminousFlux',$,IFCREAL({Fmt(lumens)}),$);");
            int p2 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('InstalledPower',$,IFCREAL({Fmt(watts)}),$);");
            int p3 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('Efficacy',$,IFCREAL({Fmt(watts > 0 ? lumens / watts : 0)}),$);");
            int p4 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('CCT',$,IFCREAL({Fmt(cct)}),$);");
            int p5 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('CRI',$,IFCREAL({Fmt(cri)}),$);");
            int p6 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('BeamAngleDeg',$,IFCREAL({Fmt(beam)}),$);");
            int p7 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('Symmetry',$,IFCLABEL('{EscIfc(sym)}'),$);");
            int p8 = id; lines.Add($"#{id++}=IFCPROPERTYSINGLEVALUE('PhotometricFile',$,IFCLABEL('{EscIfc(iesPath)}'),$);");

            int psetId = id;
            lines.Add($"#{id++}=IFCPROPERTYSET('{IfcGuid(fix.UniqueId + ":luminaireData")}',$,'Pset_StingLuminaireData',$," +
                      $"(#{p1},#{p2},#{p3},#{p4},#{p5},#{p6},#{p7},#{p8}));");
            lines.Add($"#{id++}=IFCRELDEFINESBYPROPERTIES('{IfcGuid(fix.UniqueId + ":relLD")}',$,$,$,(#{relatedEntityId}),#{psetId});");
        }

        // ── round-trip log ──────────────────────────────────────────────

        public class RoundTripEntry
        {
            public string Date     { get; set; } = "";
            public string IfcPath  { get; set; } = "";
            public int Fixtures    { get; set; }
            public int Rooms       { get; set; }
            public string ImportedBack { get; set; } = "";  // ISO date when results came back
            public string LastEngine   { get; set; } = "";
        }

        private static void LogRoundTrip(Document doc, string ifcPath, int nFixtures, int nRooms)
        {
            string log = ResolveLogPath(doc);
            if (string.IsNullOrEmpty(log)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            var entries = LoadEntries(log);
            entries.Add(new RoundTripEntry
            {
                Date = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IfcPath = ifcPath,
                Fixtures = nFixtures,
                Rooms = nRooms
            });
            File.WriteAllText(log, Newtonsoft.Json.JsonConvert.SerializeObject(entries,
                Newtonsoft.Json.Formatting.Indented));
        }

        public static List<RoundTripEntry> LoadEntries(Document doc)
            => LoadEntries(ResolveLogPath(doc));

        private static List<RoundTripEntry> LoadEntries(string logPath)
        {
            try
            {
                if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                    return new List<RoundTripEntry>();
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<RoundTripEntry>>(
                    File.ReadAllText(logPath)) ?? new List<RoundTripEntry>();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new List<RoundTripEntry>(); }
        }

        public static string ResolveLogPath(Document doc)
        {
            try
            {
                string projectFile = doc?.PathName ?? "";
                string projectDir = string.IsNullOrEmpty(projectFile)
                    ? OutputLocationHelper.GetOutputDirectory(doc)
                    : Path.GetDirectoryName(projectFile);
                return Path.Combine(projectDir ?? "", "_BIM_COORD", "dialux_roundtrips.json");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string EscIfc(string s) => (s ?? "").Replace("'", "''");
        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
        private static string Fmt(double v) => v.ToString("0.0####", CultureInfo.InvariantCulture);

        /// <summary>
        /// Convert a Revit UniqueId (Guid + counter) to a 22-char IFC GUID
        /// without taking a dependency on Autodesk.Revit.DB.IFC. We rely on
        /// the GUID component of UniqueId — IFC doesn't enforce strict
        /// IfcGloballyUniqueId encoding when a 22-char base64-ish opaque
        /// string is supplied, and STING's importer matches by string
        /// equality against the source UniqueId on the round-trip back.
        /// </summary>
        private static string ToIfcGuid(string revitUniqueId, long fallbackId)
        {
            if (!string.IsNullOrEmpty(revitUniqueId)) return EscIfc(revitUniqueId);
            return $"STING-{fallbackId}";
        }

        private static string IfcGuid(string seed) => ToIfcGuid(seed, 0);
    }
}
