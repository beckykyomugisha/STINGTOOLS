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
using Newtonsoft.Json;

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
            // SI units — every quantity below is written in m / m² / m³. Without an
            // IfcUnitAssignment the receiving tool has to guess.
            int uLen  = w.Entity("IFCSIUNIT", "*", ".LENGTHUNIT.", "$", ".METRE.");
            int uArea = w.Entity("IFCSIUNIT", "*", ".AREAUNIT.",   "$", ".SQUARE_METRE.");
            int uVol  = w.Entity("IFCSIUNIT", "*", ".VOLUMEUNIT.", "$", ".CUBIC_METRE.");
            int units = w.Entity("IFCUNITASSIGNMENT", $"(#{uLen},#{uArea},#{uVol})");
            int projId = w.Entity("IFCPROJECT",
                $"'{IfcGuid(projectName)}'", "$",
                $"'{EscIfc(projectName)}'",
                "$", "$", "$", "$", "$", $"#{units}");

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
                ".ELEMENT.", "$", "$", "$");   // CompositionType, ElevationOfRefHeight, ElevationOfTerrain, BuildingAddress

            w.RelAggregates(
                IfcGuid(projectName + ":relSiteBuilding"),
                siteId,
                new[] { buildingId });

            // ── IfcSpace entities (rooms) ────────────────────────────────
            var spaceIds = new List<int>(rooms.Count);

            int noHeight = 0;
            foreach (var room in rooms)
            {
                // IFC convention (and Revit's own exporter): Name = room NUMBER,
                // LongName = room NAME. Room.Name concatenates both.
                string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                string rName  = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
                int spaceId = w.Entity("IFCSPACE",
                    $"'{ToIfcGuid(room.UniqueId, room.Id.Value)}'", "$",
                    $"'{EscIfc(string.IsNullOrEmpty(number) ? rName : number)}'",
                    "$", "$", "$", "$",
                    $"'{EscIfc(rName)}'",
                    ".ELEMENT.", ".NOTDEFINED.", "$");

                spaceIds.Add(spaceId);

                EmitStingResultsPSet(w, spaceId, room);
                if (!EmitSpaceQuantities(w, spaceId, room)) noHeight++;
            }

            // IfcSpace is a spatial structure element: it is AGGREGATED into the
            // building (IFC4 IfcRelContainedInSpatialStructure WR31 forbids spatial
            // elements as RelatedElements).
            if (spaceIds.Count > 0)
            {
                w.RelAggregates(
                    IfcGuid(projectName + ":relSpacesInBuilding"),
                    buildingId,
                    spaceIds);
            }

            // ── IfcLightFixture entities ─────────────────────────────────
            int noLumens = 0;
            foreach (var fix in fixtures)
            {
                int fixId = w.Entity("IFCLIGHTFIXTURE",
                    $"'{ToIfcGuid(fix.UniqueId, fix.Id.Value)}'", "$",
                    $"'{EscIfc(fix.Name)}'",
                    "$", "$", "$", "$", "$",
                    ".NOTDEFINED.");

                if (!EmitStingLuminairePSet(w, fixId, fix, doc)) noLumens++;
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
                $"IFC 4 DATA HAND-OFF DRAFT written:\n{outPath}\n\n" +
                $"{fixtures.Count} luminaire(s) · {rooms.Count} room(s)\n" +
                "Contains spaces (number, name, area, height, volume in SI units), luminaire records and the " +
                "Pset_StingLightingResults / Pset_StingLuminaireData property sets.\n\n" +
                "⚠ It carries NO geometry and NO placement — rooms and luminaires will not appear " +
                "positioned in DIALux. For a calculation model use Revit's own IFC export (or the DIALux " +
                "Revit plug-in); use this file for the data round-trip. Review before use.\n" +
                (noHeight > 0 ? $"\n⚠ {noHeight} room(s) have no height — height/volume omitted, not guessed." : "") +
                (noLumens > 0 ? $"\n⚠ {noLumens} luminaire(s) have no lumen data — LuminousFlux omitted, not estimated." : "") +
                "\n\nAfter calculation, run STING → Photometrics → Import IFC Results to map values back.");
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

            // Absent values are omitted rather than written as 0 — a 0 lx / 0 UGR
            // property reads as a calculated result on the other side.
            var props = new List<int>();
            if (lux > 0)
            {
                props.Add(w.PropSingleValue(StingLightingPSet.IlluminanceLux, $"IFCREAL({Fmt(lux)})"));
                props.Add(w.PropSingleValue(StingLightingPSet.AverageLux,     $"IFCREAL({Fmt(lux)})"));
            }
            if (uniformity > 0) props.Add(w.PropSingleValue(StingLightingPSet.UniformityRatio, $"IFCREAL({Fmt(uniformity)})"));
            if (ugr > 0)        props.Add(w.PropSingleValue(StingLightingPSet.UGR,             $"IFCREAL({Fmt(ugr)})"));
            props.Add(w.PropSingleValue(StingLightingPSet.CalculationDate, $"IFCLABEL('{EscIfc(lastDate)}')"));
            props.Add(w.PropSingleValue(StingLightingPSet.EngineUsed,      $"IFCLABEL('{EscIfc(lastEngine)}')"));

            int psetId = w.PropertySet(
                IfcGuid(room.UniqueId + ":lightingResults"),
                StingLightingPSet.PSetName,
                props);

            w.RelDefinesByProperties(
                IfcGuid(room.UniqueId + ":relLR"),
                new[] { relatedEntityId },
                psetId);
        }

        /// <returns>false when the luminaire has no lumen data (LuminousFlux omitted).</returns>
        private static bool EmitStingLuminairePSet(IfcWriter w, int relatedEntityId,
            FamilyInstance fix, Document doc)
        {
            var symbol = doc.GetElement(fix.GetTypeId());

            double watts = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_WATTS));
            if (watts <= 0) watts = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_WATTAGE));
            if (watts <= 0)
                try { watts = StingTools.Core.Electrical.ElecUnits.Read(fix, BuiltInParameter.RBS_ELEC_APPARENT_LOAD); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            double lumens = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_LUMENS));
            if (lumens <= 0) lumens = ParseDouble(ParameterHelpers.GetString(fix, ParamRegistry.LTG_LUMENS));
            // No lumen data → no LuminousFlux / Efficacy. Never estimate it here: the
            // receiving tool would treat an invented flux as manufacturer data.

            double cct     = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_CCT));
            double cri     = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_CRI));
            double beam    = ParseDouble(ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_BEAM_ANGLE));
            string sym     = ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_SYMMETRY);
            string iesPath = ParameterHelpers.GetString(symbol ?? fix, ParamRegistry.ELC_PHOTO_FILE_PATH);

            var props = new List<int>();
            if (lumens > 0) props.Add(w.PropSingleValue("LuminousFlux", $"IFCREAL({Fmt(lumens)})"));
            if (watts > 0)  props.Add(w.PropSingleValue("InstalledPower", $"IFCREAL({Fmt(watts)})"));
            if (lumens > 0 && watts > 0)
                props.Add(w.PropSingleValue("Efficacy", $"IFCREAL({Fmt(lumens / watts)})"));
            if (cct > 0)  props.Add(w.PropSingleValue("CCT",          $"IFCREAL({Fmt(cct)})"));
            if (cri > 0)  props.Add(w.PropSingleValue("CRI",          $"IFCREAL({Fmt(cri)})"));
            if (beam > 0) props.Add(w.PropSingleValue("BeamAngleDeg", $"IFCREAL({Fmt(beam)})"));
            props.Add(w.PropSingleValue("Symmetry",        $"IFCLABEL('{EscIfc(sym)}')"));
            props.Add(w.PropSingleValue("PhotometricFile", $"IFCLABEL('{EscIfc(iesPath)}')"));

            int psetId = w.PropertySet(
                IfcGuid(fix.UniqueId + ":luminaireData"),
                "Pset_StingLuminaireData",
                props);

            w.RelDefinesByProperties(
                IfcGuid(fix.UniqueId + ":relLD"),
                new[] { relatedEntityId },
                psetId);
            return lumens > 0;
        }

        /// <summary>
        /// Writes Qto_SpaceBaseQuantities (IfcElementQuantity) for a room and
        /// links it back to the IfcSpace via IfcRelDefinesByProperties.
        /// Revit internal ft² / ft converted with UnitUtils to m² / m.
        /// </summary>
        /// <returns>false when the room has no height (height + volume omitted).</returns>
        private static bool EmitSpaceQuantities(IfcWriter w, int spaceId, SpatialElement room)
        {
            double areaFt2  = room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
            double heightFt = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT)?.AsDouble() ?? 0;
            // ROOM_UPPER_OFFSET is the offset above the UPPER LIMIT level, not a
            // height. It equals the room height only when the upper limit is the
            // room's own level; otherwise height + volume are omitted rather than
            // exported as a wrong dimension.
            if (heightFt <= 0)
            {
                ElementId upperLevel = ElementId.InvalidElementId;
                try { upperLevel = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL)?.AsElementId() ?? ElementId.InvalidElementId; }
                catch (Exception ex) { StingLog.Warn($"DIALux room {room.Id} upper level: {ex.Message}"); }
                if (upperLevel != ElementId.InvalidElementId && upperLevel == room.LevelId)
                    heightFt = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET)?.AsDouble() ?? 0;
            }

            double areaM2  = UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
            double heightM = heightFt > 0 ? UnitUtils.ConvertFromInternalUnits(heightFt, UnitTypeId.Meters) : 0;

            // Floor area always; height + gross volume only when the room has a height.
            // A default height would be an invented dimension handed to a calc tool.
            var qs = new List<int> { w.QuantityArea("NetFloorArea", areaM2) };
            if (heightM > 0)
            {
                qs.Add(w.Entity("IFCQUANTITYLENGTH",
                    "'Height'", "$", "$",
                    heightM.ToString("0.0####", CultureInfo.InvariantCulture), "$"));
                qs.Add(w.QuantityVolume("GrossVolume", areaM2 * heightM));
            }

            int qsetId = w.ElementQuantity(
                IfcGuid(room.UniqueId + ":qto"),
                "Qto_SpaceBaseQuantities",
                qs);

            w.RelDefinesByQuantities(
                IfcGuid(room.UniqueId + ":relQto"),
                new[] { spaceId },
                qsetId);
            return heightM > 0;
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
                return Path.Combine(StingPaths.Meta(doc, "_BIM_COORD"), "dialux_roundtrips.json");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string EscIfc(string s)    => (s ?? "").Replace("'", "''");
        private static double ParseDouble(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        private static string Fmt(double v)        => v.ToString("0.0####", CultureInfo.InvariantCulture);

        /// <summary>
        /// Convert a Revit UniqueId (45-char Guid + counter) to the
        /// 22-char base64-ish IFC GUID per buildingSMART specification.
        /// This is the same algorithm Revit's own
        /// Autodesk.Revit.DB.IFC.ExporterIFCUtils.CreateGUID uses but
        /// re-implemented in <see cref="IfcGuidEncoder"/> to avoid
        /// pulling the IFC export assembly into the electrical pipeline.
        ///
        /// CRITICAL: DIALux evo and ElumTools silently reject GUIDs that
        /// don't match the IFC schema's 22-char shape — they replace
        /// them with auto-generated values, which destroys the round-trip
        /// match-by-GUID logic in <c>IfcResultsImportCommand</c>.
        /// </summary>
        private static string ToIfcGuid(string revitUniqueId, long fallbackId)
        {
            if (!string.IsNullOrEmpty(revitUniqueId))
                return IfcGuidEncoder.FromRevitUniqueId(revitUniqueId);
            return IfcGuidEncoder.FromGuid(new Guid(unchecked((int)(fallbackId & 0xFFFFFFFF)),
                (short)((fallbackId >> 32) & 0xFFFF), (short)((fallbackId >> 48) & 0xFFFF),
                0, 0, 0, 0, 0, 0, 0, 0));
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
