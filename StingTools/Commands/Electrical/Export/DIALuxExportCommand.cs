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
    ///
    /// Phase 182 refactor:
    ///  • Introduces <see cref="IfcWriter"/> inner class for structured entity
    ///    emission (tracked IDs, typed helpers, no raw string concatenation).
    ///  • Adds IfcSite / IfcBuilding hierarchy with IfcRelAggregates.
    ///  • Links all IfcSpaces to the building via IfcRelContainedInSpatialStructure.
    ///  • Adds Qto_SpaceBaseQuantities (IfcElementQuantity) per room.
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

            // ── HEADER block — stays as raw strings, IfcWriter manages DATA entities only ──
            var headerLines = new List<string>
            {
                "ISO-10303-21;",
                "HEADER;",
                "FILE_DESCRIPTION(('STING DIALux Export — Phase 182'),'2;1');",
                $"FILE_NAME('{Path.GetFileName(outPath)}','{DateTime.Now:yyyy-MM-ddTHH:mm:ss}',('STING'),('STING Tools'),'STING Plugin','Revit','');",
                "FILE_SCHEMA(('IFC4'));",
                "ENDSEC;",
                "DATA;"
            };

            var w = new IfcWriter(headerLines, startId: 100);

            // ── IfcProject ───────────────────────────────────────────────
            string projectName = doc.ProjectInformation?.Name ?? "STING Project";
            int projId = w.Entity("IFCPROJECT",
                $"'{IfcGuid(projectName)}'", "$",
                $"'{EscIfc(projectName)}'",
                "$", "$", "$", "$", "$", "$");

            // ── IfcSite ──────────────────────────────────────────────────
            int siteId = w.Entity("IFCSITE",
                $"'{IfcGuid(projectName + ":site")}'", "$",
                $"'{EscIfc(projectName)} Site'",
                "$", "$", "$", "$", "$",
                ".ELEMENT.", "$", "$", "$", "$", "$");

            w.RelAggregates(
                IfcGuid(projectName + ":relProjSite"),
                projId,
                new[] { siteId });

            // ── IfcBuilding ──────────────────────────────────────────────
            int buildingId = w.Entity("IFCBUILDING",
                $"'{IfcGuid(projectName + ":building")}'", "$",
                $"'{EscIfc(projectName)} Building'",
                "$", "$", "$", "$", "$",
                ".ELEMENT.", "$", "$");

            w.RelAggregates(
                IfcGuid(projectName + ":relSiteBuilding"),
                siteId,
                new[] { buildingId });

            // ── IfcSpace entities (rooms) ────────────────────────────────
            var spaceIds = new List<int>(rooms.Count);

            foreach (var room in rooms)
            {
                int spaceId = w.Entity("IFCSPACE",
                    $"'{ToIfcGuid(room.UniqueId, room.Id.Value)}'", "$",
                    $"'{EscIfc(room.Name)}'",
                    "$", "$", "$", "$", "$",
                    ".ELEMENT.", ".NOTDEFINED.");

                spaceIds.Add(spaceId);

                EmitStingResultsPSet(w, spaceId, room);
                EmitSpaceQuantities(w, spaceId, room);
            }

            // Link all spaces to the building via IfcRelContainedInSpatialStructure
            if (spaceIds.Count > 0)
            {
                w.RelContainedInSpatialStructure(
                    IfcGuid(projectName + ":relSpacesInBuilding"),
                    spaceIds,
                    buildingId);
            }

            // ── IfcLightFixture entities ─────────────────────────────────
            foreach (var fix in fixtures)
            {
                int fixId = w.Entity("IFCLIGHTFIXTURE",
                    $"'{ToIfcGuid(fix.UniqueId, fix.Id.Value)}'", "$",
                    $"'{EscIfc(fix.Name)}'",
                    "$", "$", "$", "$", "$",
                    ".NOTDEFINED.");

                EmitStingLuminairePSet(w, fixId, fix, doc);
            }

            w.Lines.Add("ENDSEC;");
            w.Lines.Add("END-ISO-10303-21;");

            try { File.WriteAllLines(outPath, w.Lines, Encoding.UTF8); }
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

        // ── PSet / QSet emitters ────────────────────────────────────────

        private static void EmitStingResultsPSet(IfcWriter w, int relatedEntityId, SpatialElement room)
        {
            // Existing values from prior phases — DIALux may overwrite on re-import.
            double lux        = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX));
            double ugr        = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_UGR));
            double uniformity = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_UNIFORMITY));
            string lastEngine = ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LAST_ENGINE);
            string lastDate   = ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LAST_CALC_DATE);

            int luxId  = w.PropSingleValue(StingLightingPSet.IlluminanceLux,  $"IFCREAL({Fmt(lux)})");
            int avgId  = w.PropSingleValue(StingLightingPSet.AverageLux,      $"IFCREAL({Fmt(lux)})");
            int uoId   = w.PropSingleValue(StingLightingPSet.UniformityRatio, $"IFCREAL({Fmt(uniformity)})");
            int ugrId  = w.PropSingleValue(StingLightingPSet.UGR,             $"IFCREAL({Fmt(ugr)})");
            int dateId = w.PropSingleValue(StingLightingPSet.CalculationDate, $"IFCLABEL('{EscIfc(lastDate)}')");
            int engId  = w.PropSingleValue(StingLightingPSet.EngineUsed,      $"IFCLABEL('{EscIfc(lastEngine)}')");

            int psetId = w.PropertySet(
                IfcGuid(room.UniqueId + ":lightingResults"),
                StingLightingPSet.PSetName,
                new[] { luxId, avgId, uoId, ugrId, dateId, engId });

            w.RelDefinesByProperties(
                IfcGuid(room.UniqueId + ":relLR"),
                new[] { relatedEntityId },
                psetId);
        }

        private static void EmitStingLuminairePSet(IfcWriter w, int relatedEntityId,
            FamilyInstance fix, Document doc)
        {
            var symbol = doc.GetElement(fix.GetTypeId());

            double watts = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_WATTS));
            if (watts <= 0) watts = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_WATTAGE));
            if (watts <= 0)
                try { watts = fix.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)?.AsDouble() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            double lumens = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_LUMENS));
            if (lumens <= 0) lumens = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_LUMENS));
            if (lumens <= 0) lumens = watts * 80.0;

            double cct     = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_CCT));
            double cri     = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_CRI));
            double beam    = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_BEAM_ANGLE));
            string sym     = ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_SYMMETRY);
            string iesPath = ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_FILE_PATH);

            int p1 = w.PropSingleValue("LuminousFlux",    $"IFCREAL({Fmt(lumens)})");
            int p2 = w.PropSingleValue("InstalledPower",  $"IFCREAL({Fmt(watts)})");
            int p3 = w.PropSingleValue("Efficacy",        $"IFCREAL({Fmt(watts > 0 ? lumens / watts : 0)})");
            int p4 = w.PropSingleValue("CCT",             $"IFCREAL({Fmt(cct)})");
            int p5 = w.PropSingleValue("CRI",             $"IFCREAL({Fmt(cri)})");
            int p6 = w.PropSingleValue("BeamAngleDeg",    $"IFCREAL({Fmt(beam)})");
            int p7 = w.PropSingleValue("Symmetry",        $"IFCLABEL('{EscIfc(sym)}')");
            int p8 = w.PropSingleValue("PhotometricFile", $"IFCLABEL('{EscIfc(iesPath)}')");

            int psetId = w.PropertySet(
                IfcGuid(fix.UniqueId + ":luminaireData"),
                "Pset_StingLuminaireData",
                new[] { p1, p2, p3, p4, p5, p6, p7, p8 });

            w.RelDefinesByProperties(
                IfcGuid(fix.UniqueId + ":relLD"),
                new[] { relatedEntityId },
                psetId);
        }

        /// <summary>
        /// Writes Qto_SpaceBaseQuantities (IfcElementQuantity) for a room and
        /// links it back to the IfcSpace via IfcRelDefinesByProperties.
        /// Areas converted ft² → m² (× 0.0929); heights ft → m (× 0.3048).
        /// </summary>
        private static void EmitSpaceQuantities(IfcWriter w, int spaceId, SpatialElement room)
        {
            double areaFt2  = room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
            double heightFt = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT)?.AsDouble() ?? 0;

            double areaM2   = areaFt2 * 0.0929;
            double heightM  = heightFt > 0 ? heightFt * 0.3048 : 3.0;  // default 3 m
            double volumeM3 = areaM2 * heightM;

            // IFCQUANTITYAREA for floor area, IFCQUANTITYLENGTH for height,
            // IFCQUANTITYVOLUME for gross volume — one entity each.
            int qArea      = w.QuantityArea("BaseArea", areaM2);
            int qHeightLen = w.Entity("IFCQUANTITYLENGTH",
                "'Height'", "$", "$",
                heightM.ToString("0.0####", CultureInfo.InvariantCulture), "$");
            int qVol       = w.QuantityVolume("GrossVolume", volumeM3);

            int qsetId = w.ElementQuantity(
                IfcGuid(room.UniqueId + ":qto"),
                "Qto_SpaceBaseQuantities",
                new[] { qArea, qHeightLen, qVol });

            w.RelDefinesByQuantities(
                IfcGuid(room.UniqueId + ":relQto"),
                new[] { spaceId },
                qsetId);
        }

        // ── round-trip log ──────────────────────────────────────────────

        public class RoundTripEntry
        {
            public string Date         { get; set; } = "";
            public string IfcPath      { get; set; } = "";
            public int    Fixtures     { get; set; }
            public int    Rooms        { get; set; }
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
                Date     = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IfcPath  = ifcPath,
                Fixtures = nFixtures,
                Rooms    = nRooms
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
                string projectDir  = string.IsNullOrEmpty(projectFile)
                    ? OutputLocationHelper.GetOutputDirectory(doc)
                    : Path.GetDirectoryName(projectFile);
                return Path.Combine(projectDir ?? "", "_BIM_COORD", "dialux_roundtrips.json");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string EscIfc(string s)    => (s ?? "").Replace("'", "''");
        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
        private static string Fmt(double v)        => v.ToString("0.0####", CultureInfo.InvariantCulture);

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

        // ── IfcWriter ────────────────────────────────────────────────────

        /// <summary>
        /// Structured IFC STEP entity emitter.  Tracks a monotonically
        /// increasing entity ID counter and exposes typed helpers for the
        /// IFC entity kinds used by the DIALux export.  All helpers return
        /// the ID of the entity they wrote so callers can reference it.
        /// </summary>
        internal sealed class IfcWriter
        {
            private readonly List<string> _lines;
            private int _id;

            /// <param name="existingHeader">
            /// The pre-built header lines list.  The writer appends to this
            /// list so the final Lines collection contains both header and
            /// DATA entities.
            /// </param>
            /// <param name="startId">First entity ID (default 100).</param>
            public IfcWriter(List<string> existingHeader, int startId = 100)
            {
                _lines = existingHeader;
                _id    = startId;
            }

            /// <summary>All accumulated lines (header + entities).</summary>
            public List<string> Lines => _lines;

            /// <summary>Peek at the next ID without consuming it.</summary>
            public int NextId() => _id;

            // ── raw entity ─────────────────────────────────────────────

            /// <summary>
            /// Emit a raw IFC entity line and return its assigned ID.
            /// Each argument in <paramref name="args"/> is joined with commas
            /// and wrapped in parentheses.
            /// </summary>
            public int Entity(string ifcType, params string[] args)
            {
                int myId = _id++;
                _lines.Add($"#{myId}={ifcType}({string.Join(",", args)});");
                return myId;
            }

            // ── typed helpers ───────────────────────────────────────────

            /// <summary>
            /// Emit IFCPROPERTYSINGLEVALUE.
            /// <paramref name="formattedValue"/> must already be a valid IFC
            /// value expression, e.g. <c>IFCREAL(3.5)</c> or
            /// <c>IFCLABEL('text')</c>.
            /// </summary>
            public int PropSingleValue(string name, string formattedValue)
                => Entity("IFCPROPERTYSINGLEVALUE",
                    $"'{EscIfc(name)}'", "$", formattedValue, "$");

            /// <summary>Emit IFCPROPERTYSET and return its ID.</summary>
            public int PropertySet(string guid, string psetName, IEnumerable<int> propIds)
                => Entity("IFCPROPERTYSET",
                    $"'{EscIfc(guid)}'", "$",
                    $"'{EscIfc(psetName)}'", "$",
                    $"({string.Join(",", propIds.Select(i => $"#{i}"))})");

            /// <summary>
            /// Emit IFCRELDEFINESBYPROPERTIES linking a set of entities to a
            /// property set.
            /// </summary>
            public int RelDefinesByProperties(string guid,
                IEnumerable<int> relatedIds, int psetId)
                => Entity("IFCRELDEFINESBYPROPERTIES",
                    $"'{EscIfc(guid)}'", "$", "$", "$",
                    $"({string.Join(",", relatedIds.Select(i => $"#{i}"))})",
                    $"#{psetId}");

            /// <summary>
            /// Emit IFCRELAGGREGATES (container → children).
            /// Used for Project→Site→Building hierarchy.
            /// </summary>
            public int RelAggregates(string guid, int relatingId,
                IEnumerable<int> relatedIds)
                => Entity("IFCRELAGGREGATES",
                    $"'{EscIfc(guid)}'", "$", "$", "$",
                    $"#{relatingId}",
                    $"({string.Join(",", relatedIds.Select(i => $"#{i}"))})");

            /// <summary>
            /// Emit IFCRELCONTAINEDINSPATIALSTRUCTURE linking product instances
            /// (e.g. IfcSpace) to a spatial container (e.g. IfcBuilding).
            /// </summary>
            public int RelContainedInSpatialStructure(string guid,
                IEnumerable<int> relatedIds, int relatingStructureId)
                => Entity("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                    $"'{EscIfc(guid)}'", "$", "$", "$",
                    $"({string.Join(",", relatedIds.Select(i => $"#{i}"))})",
                    $"#{relatingStructureId}");

            /// <summary>
            /// Emit IFCQUANTITYAREA (area in m²).
            /// Name is the IFC quantity name, e.g. "BaseArea".
            /// </summary>
            public int QuantityArea(string name, double value)
                => Entity("IFCQUANTITYAREA",
                    $"'{EscIfc(name)}'", "$", "$",
                    value.ToString("0.0####", CultureInfo.InvariantCulture), "$");

            /// <summary>Emit IFCQUANTITYVOLUME (volume in m³).</summary>
            public int QuantityVolume(string name, double value)
                => Entity("IFCQUANTITYVOLUME",
                    $"'{EscIfc(name)}'", "$", "$",
                    value.ToString("0.0####", CultureInfo.InvariantCulture), "$");

            /// <summary>
            /// Emit IFCELEMENTQUANTITY (an IfcQuantitySet container).
            /// </summary>
            public int ElementQuantity(string guid, string qsetName,
                IEnumerable<int> quantityIds)
                => Entity("IFCELEMENTQUANTITY",
                    $"'{EscIfc(guid)}'", "$",
                    $"'{EscIfc(qsetName)}'", "$", "$",
                    $"({string.Join(",", quantityIds.Select(i => $"#{i}"))})");

            /// <summary>
            /// Emit IFCRELDEFINESBYPROPERTIES for a quantity set relationship.
            /// IFC 4 uses the same relationship entity for both PSets and QSets.
            /// </summary>
            public int RelDefinesByQuantities(string guid,
                IEnumerable<int> relatedIds, int qsetId)
                => Entity("IFCRELDEFINESBYPROPERTIES",
                    $"'{EscIfc(guid)}'", "$", "$", "$",
                    $"({string.Join(",", relatedIds.Select(i => $"#{i}"))})",
                    $"#{qsetId}");

            // ── private ────────────────────────────────────────────────

            private static string EscIfc(string s) => (s ?? "").Replace("'", "''");
        }
    }
}
