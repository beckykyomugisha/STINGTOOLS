using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core
{
    /// <summary>
    /// Ported from tag_logic.py — parameter read/write helpers for Revit elements.
    /// </summary>
    public static class ParameterHelpers
    {
        /// <summary>Return the string value of a named parameter, or empty string.</summary>
        public static string GetString(Element el, string paramName)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p != null && p.StorageType == StorageType.String)
            {
                string v = p.AsString();
                return v ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>Set a TEXT parameter. Skips read-only params. Skips non-empty unless overwrite.</summary>
        public static bool SetString(Element el, string paramName, string value,
            bool overwrite = false)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                return false;

            string existing = p.AsString() ?? string.Empty;
            if (existing.Length > 0 && !overwrite)
                return false;

            p.Set(value);
            return true;
        }

        /// <summary>Set only when the parameter is currently empty.</summary>
        public static bool SetIfEmpty(Element el, string paramName, string value)
        {
            return SetString(el, paramName, value, overwrite: false);
        }

        /// <summary>Return a short level code from the element's host level.</summary>
        public static string GetLevelCode(Document doc, Element el)
        {
            try
            {
                ElementId lvlId = el.LevelId;

                // Fallback: MEP elements often use RBS_START_LEVEL_PARAM instead of LevelId
                if (lvlId == null || lvlId == ElementId.InvalidElementId)
                {
                    var startLvl = el.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                    if (startLvl != null && startLvl.HasValue)
                        lvlId = startLvl.AsElementId();
                }
                // Fallback: face-based families use FAMILY_LEVEL_PARAM
                if (lvlId == null || lvlId == ElementId.InvalidElementId)
                {
                    var famLvl = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (famLvl != null && famLvl.HasValue)
                        lvlId = famLvl.AsElementId();
                }

                if (lvlId == null || lvlId == ElementId.InvalidElementId)
                    return "XX";

                Level lvl = doc.GetElement(lvlId) as Level;
                if (lvl == null)
                    return "XX";

                string name = lvl.Name.Trim();
                string lower = name.ToLowerInvariant();

                if (lower.StartsWith("level "))
                    return "L" + name.Substring(6).Trim().PadLeft(2, '0');
                if (lower == "ground" || lower == "ground floor" || lower == "ground level")
                    return "GF";
                if (lower.StartsWith("basement") || lower.StartsWith("b"))
                {
                    string digits = ExtractDigits(name);
                    return "B" + (digits.Length > 0 ? digits : "1");
                }
                if (lower.StartsWith("roof"))
                    return "RF";

                // Use length of cleaned string to avoid substring overflow
                var cleaned = name.ToUpperInvariant().Replace(" ", "");
                return cleaned.Substring(0, Math.Min(4, cleaned.Length));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetLevelCode failed for element {el?.Id}: {ex.Message}");
                return "XX";
            }
        }

        /// <summary>Return the category name of an element, or empty string.</summary>
        public static string GetCategoryName(Element el)
        {
            try
            {
                return el.Category?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetCategoryName failed for element {el?.Id}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Check if an element is in a non-primary design option.
        /// Returns true if the element should be skipped (is in a non-primary option).
        /// Elements with no design option or in the primary option return false.
        /// </summary>
        public static bool IsInNonPrimaryDesignOption(Element el)
        {
            try
            {
                DesignOption opt = el.DesignOption;
                if (opt == null) return false;
                return !opt.IsPrimary;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get the family name of an element (from its FamilySymbol).
        /// Returns empty string if not a FamilyInstance or if family name unavailable.
        /// </summary>
        public static string GetFamilyName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi && fi.Symbol?.Family != null)
                    return fi.Symbol.Family.Name;
                return string.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetFamilyName failed for {el?.Id}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Get the family symbol (type) name of an element.
        /// Returns empty string if not a FamilyInstance.
        /// </summary>
        public static string GetFamilySymbolName(Element el)
        {
            try
            {
                if (el is FamilyInstance fi && fi.Symbol != null)
                    return fi.Symbol.Name;
                return string.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetFamilySymbolName failed for {el?.Id}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Find the Room containing an element, using the element's location point.
        /// Returns null if the element has no location point or is not in a room.
        /// </summary>
        public static Room GetRoomAtElement(Document doc, Element el)
        {
            try
            {
                // FamilyInstance has a direct Room property
                if (el is FamilyInstance fi)
                {
                    Room room = fi.Room;
                    if (room != null) return room;
                }

                // Fall back to location-based lookup
                LocationPoint lp = el.Location as LocationPoint;
                if (lp != null)
                {
                    return doc.GetRoomAtPoint(lp.Point);
                }

                // For curve-based elements (pipes, ducts), use midpoint
                LocationCurve lc = el.Location as LocationCurve;
                if (lc != null)
                {
                    XYZ mid = lc.Curve.Evaluate(0.5, true);
                    return doc.GetRoomAtPoint(mid);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetRoomAtElement failed for {el?.Id}: {ex.Message}");
            }
            return null;
        }

        private static string ExtractDigits(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Find the solid fill pattern element in the document.
        /// Caches per document to avoid repeated collector queries.
        /// </summary>
        public static FillPatternElement GetSolidFillPattern(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
        }
    }

    /// <summary>
    /// Spatial auto-detection helper for LOC and ZONE tokens.
    /// Eliminates the need for manual SetLoc / SetZone commands by
    /// deriving location and zone from Revit project info and room data.
    /// </summary>
    public static class SpatialAutoDetect
    {
        /// <summary>
        /// Pre-scan all rooms in the project and build a lookup by ElementId.
        /// Call once before a batch loop for performance.
        /// </summary>
        public static Dictionary<ElementId, Room> BuildRoomIndex(Document doc)
        {
            var index = new Dictionary<ElementId, Room>();
            try
            {
                foreach (Room room in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>())
                {
                    if (room.Area > 0) // only placed rooms
                        index[room.Id] = room;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildRoomIndex failed: {ex.Message}");
            }
            return index;
        }

        /// <summary>
        /// Detect the project-level LOC code from Revit Project Information.
        /// Checks BuildingName, Project Name, and Project Address fields
        /// for patterns like "BLD1", "BLD2", "Building 1", "Block A", etc.
        /// Returns the default LOC code or empty string if uncertain.
        /// </summary>
        public static string DetectProjectLoc(Document doc)
        {
            try
            {
                ProjectInfo info = doc.ProjectInformation;
                if (info == null) return "BLD1";

                // Check BuildingName parameter first
                string buildingName = info.BuildingName ?? "";
                string locFromName = ParseLocCode(buildingName);
                if (!string.IsNullOrEmpty(locFromName)) return locFromName;

                // Check project name
                string projName = info.Name ?? "";
                locFromName = ParseLocCode(projName);
                if (!string.IsNullOrEmpty(locFromName)) return locFromName;

                // Check address
                string address = info.Address ?? "";
                locFromName = ParseLocCode(address);
                if (!string.IsNullOrEmpty(locFromName)) return locFromName;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectProjectLoc: {ex.Message}");
            }

            return "BLD1"; // Safe default
        }

        /// <summary>
        /// Detect LOC code for a specific element from its room or spatial context.
        /// Priority: room name → room Number → element parameter → project default.
        /// </summary>
        public static string DetectLoc(Document doc, Element el,
            Dictionary<ElementId, Room> roomIndex, string projectLoc)
        {
            try
            {
                Room room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null)
                {
                    // Check room name for building/location patterns
                    string roomName = room.Name ?? "";
                    string loc = ParseLocCode(roomName);
                    if (!string.IsNullOrEmpty(loc)) return loc;

                    // Check room number prefix (e.g., "B1-101" → BLD1)
                    string roomNum = room.Number ?? "";
                    loc = ParseLocCode(roomNum);
                    if (!string.IsNullOrEmpty(loc)) return loc;
                }

                // Check if element is outside (no room) → could be EXT
                if (room == null && el.Location != null)
                {
                    // Elements with a valid level but no room might be exterior
                    // Only flag as EXT if we have rooms defined but element isn't in one
                    if (roomIndex.Count > 0)
                    {
                        // Don't auto-flag as EXT — too aggressive. Use project default.
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectLoc: {ex.Message}");
            }

            return !string.IsNullOrEmpty(projectLoc) ? projectLoc : "BLD1";
        }

        /// <summary>
        /// Detect ZONE code from room data. Checks room name, number, and
        /// Department parameter for zone patterns (Z01-Z04, Wing A/B/C/D, etc.).
        /// </summary>
        public static string DetectZone(Document doc, Element el,
            Dictionary<ElementId, Room> roomIndex)
        {
            try
            {
                Room room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null)
                {
                    // Check room Department parameter (commonly used for zone assignment)
                    Parameter deptParam = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                    if (deptParam != null)
                    {
                        string dept = deptParam.AsString() ?? "";
                        string zone = ParseZoneCode(dept);
                        if (!string.IsNullOrEmpty(zone)) return zone;
                    }

                    // Check room name for zone patterns
                    string roomName = room.Name ?? "";
                    string zoneFromName = ParseZoneCode(roomName);
                    if (!string.IsNullOrEmpty(zoneFromName)) return zoneFromName;

                    // Check room number prefix (e.g., "Z01-101", "A-201")
                    string roomNum = room.Number ?? "";
                    string zoneFromNum = ParseZoneCode(roomNum);
                    if (!string.IsNullOrEmpty(zoneFromNum)) return zoneFromNum;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectZone: {ex.Message}");
            }

            return "Z01"; // Safe default
        }

        /// <summary>
        /// Parse a string for LOC code patterns.
        /// Recognizes: BLD1/BLD2/BLD3, Building 1/2/3, Block A/B/C, EXT, External.
        /// </summary>
        private static string ParseLocCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string upper = text.ToUpperInvariant();

            // Direct match
            if (upper.Contains("BLD1") || upper.Contains("BUILDING 1") || upper.Contains("BLOCK A"))
                return "BLD1";
            if (upper.Contains("BLD2") || upper.Contains("BUILDING 2") || upper.Contains("BLOCK B"))
                return "BLD2";
            if (upper.Contains("BLD3") || upper.Contains("BUILDING 3") || upper.Contains("BLOCK C"))
                return "BLD3";
            if (upper.Contains("EXT") || upper.Contains("EXTERNAL") || upper.Contains("EXTERIOR"))
                return "EXT";

            return null;
        }

        /// <summary>
        /// Parse a string for ZONE code patterns.
        /// Recognizes: Z01-Z04, Zone 1-4, Wing A-D, North/South/East/West.
        /// </summary>
        private static string ParseZoneCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string upper = text.ToUpperInvariant();

            // Direct zone codes
            if (upper.Contains("Z01") || upper.Contains("ZONE 1") || upper.Contains("ZONE A") || upper.Contains("WING A") || upper.Contains("NORTH"))
                return "Z01";
            if (upper.Contains("Z02") || upper.Contains("ZONE 2") || upper.Contains("ZONE B") || upper.Contains("WING B") || upper.Contains("SOUTH"))
                return "Z02";
            if (upper.Contains("Z03") || upper.Contains("ZONE 3") || upper.Contains("ZONE C") || upper.Contains("WING C") || upper.Contains("EAST"))
                return "Z03";
            if (upper.Contains("Z04") || upper.Contains("ZONE 4") || upper.Contains("ZONE D") || upper.Contains("WING D") || upper.Contains("WEST"))
                return "Z04";

            return null;
        }
    }

    /// <summary>
    /// Maps Revit native/built-in parameters to STING shared parameters.
    /// Reads values that Revit populates automatically (Mark, Comments, Description,
    /// Room Name, Room Number, Area, Volume, etc.) and writes them to corresponding
    /// STING shared parameters for schedule/tag consistency.
    ///
    /// Also reads type parameters (from ElementType) when instance parameters are empty,
    /// providing type-level fallback for manufacturer, model, description, etc.
    ///
    /// This eliminates manual data entry for ~30 parameters that Revit already knows.
    /// </summary>
    public static class NativeParamMapper
    {
        /// <summary>
        /// Auto-map all applicable Revit native parameters to STING shared parameters.
        /// Only writes to empty STING parameters (non-destructive).
        /// Returns the number of values written.
        /// </summary>
        public static int MapAll(Document doc, Element el)
        {
            int written = 0;

            // ── Identity & Classification ──────────────────────────────────────
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MARK, "ASS_ID_TXT");
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "PRJ_COMMENTS_TXT");
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_DESCRIPTION, "ASS_DESCRIPTION_TXT");
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MODEL, "ASS_MODEL_NR_TXT");
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MANUFACTURER, "ASS_MANUFACTURER_TXT");

            // Type Name → ASS_TYPE_NAME_TXT (from the family symbol name)
            string typeName = GetFamilySymbolName(el);
            if (!string.IsNullOrEmpty(typeName))
                written += SetIfEmptyInt(el, "ASS_TYPE_NAME_TXT", typeName);

            // Family Name → ASS_FAMILY_NAME_TXT
            string familyName = GetFamilyName(el);
            if (!string.IsNullOrEmpty(familyName))
                written += SetIfEmptyInt(el, "ASS_FAMILY_NAME_TXT", familyName);

            // ── Spatial / Room data ────────────────────────────────────────────
            Room room = GetRoomAtElement(doc, el);
            if (room != null)
            {
                written += SetIfEmptyInt(el, "ASS_ROOM_NAME_TXT", room.Name ?? "");
                written += SetIfEmptyInt(el, "ASS_ROOM_NUM_TXT", room.Number ?? "");

                // Room area in m² (Revit stores in sq ft, convert)
                double areaSqFt = room.Area;
                if (areaSqFt > 0)
                {
                    string areaM2 = (areaSqFt * 0.092903).ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture);
                    written += SetIfEmptyInt(el, "ASS_ROOM_AREA_SQ_M", areaM2);
                }

                // Room Department
                try
                {
                    Parameter dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                    if (dept != null && dept.HasValue)
                        written += SetIfEmptyInt(el, "ASS_DEPARTMENT_ASSIGNMENT_TXT",
                            dept.AsString() ?? "");
                }
                catch (Exception ex) { StingLog.Warn("Reading Room Department parameter failed: " + ex.Message); }
            }

            // ── Dimensional parameters (BLE_ schedule fields) ──────────────────
            written += MapDimensionalParams(el);

            // ── MEP-specific parameters ────────────────────────────────────────
            written += MapMepParams(el);

            // ── Default values ─────────────────────────────────────────────────
            written += MapDefaults(el);

            // ── Type parameter fallback ────────────────────────────────────────
            // If instance params are still empty, try reading from the element type
            written += MapFromType(doc, el);

            return written;
        }

        /// <summary>
        /// Map Revit built-in dimensional parameters to STING BLE_ shared parameters.
        /// These are the parameters referenced in MR_SCHEDULES.csv Formulas column
        /// (e.g., BLE_WALL_HEIGHT_MM=Unconnected Height, BLE_DOOR_WIDTH_MM=Width).
        /// Converts from Revit internal units (feet) to metric mm/m²/degrees.
        /// </summary>
        private static int MapDimensionalParams(Element el)
        {
            int written = 0;
            string catName = (el.Category?.Name ?? "");

            const double ftToMm = 304.8;
            const double sqFtToSqM = 0.092903;
            const double radToDeg = 180.0 / Math.PI;

            try
            {
                switch (catName)
                {
                    case "Walls":
                        written += MapDimension(el, BuiltInParameter.WALL_USER_HEIGHT_PARAM,
                            "BLE_WALL_HEIGHT_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.CURVE_ELEM_LENGTH,
                            "BLE_WALL_LENGTH_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.WALL_ATTR_WIDTH_PARAM,
                            "BLE_WALL_THICKNESS_MM", ftToMm);
                        // Fire rating from type
                        written += MapStringParam(el, "Fire Rating",
                            "FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN");
                        break;

                    case "Doors":
                        written += MapDimension(el, BuiltInParameter.FAMILY_WIDTH_PARAM,
                            "BLE_DOOR_WIDTH_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.FAMILY_HEIGHT_PARAM,
                            "BLE_DOOR_HEIGHT_MM", ftToMm);
                        written += MapStringParam(el, "Fire Rating",
                            "FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN");
                        break;

                    case "Windows":
                        written += MapDimension(el, BuiltInParameter.FAMILY_WIDTH_PARAM,
                            "BLE_WINDOW_WIDTH_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.FAMILY_HEIGHT_PARAM,
                            "BLE_WINDOW_HEIGHT_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM,
                            "BLE_WINDOW_SILL_HEIGHT_FROM_FLR_MM", ftToMm);
                        break;

                    case "Floors":
                        written += MapFloorThickness(el, "BLE_FLR_THICKNESS_MM");
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            "BLE_ELE_AREA_SQ_M", sqFtToSqM);
                        break;

                    case "Ceilings":
                        written += MapDimension(el, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM,
                            "BLE_CEILING_HEIGHT_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            "BLE_ELE_AREA_SQ_M", sqFtToSqM);
                        written += MapStringParam(el, "Fire Rating",
                            "FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN");
                        break;

                    case "Roofs":
                        written += MapRoofSlope(el, "BLE_ROOF_SLOPE_DEG");
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            "BLE_ELE_AREA_SQ_M", sqFtToSqM);
                        break;

                    case "Stairs":
                        written += MapDimension(el, BuiltInParameter.STAIRS_ACTUAL_TREAD_DEPTH,
                            "BLE_STAIR_GOING_MM", ftToMm);
                        written += MapDimension(el, BuiltInParameter.STAIRS_ACTUAL_RISER_HEIGHT,
                            "BLE_STAIR_RISE_MM", ftToMm);
                        written += MapStairWidth(el, "BLE_STAIR_WIDTH_MM");
                        break;

                    case "Ramps":
                        written += MapRampSlope(el, "BLE_RAMP_SLOPE_PCT");
                        written += MapLookup(el, "Width", "BLE_RAMP_WIDTH_MM", ftToMm);
                        break;

                    case "Structural Framing":
                    case "Structural Columns":
                    case "Structural Foundations":
                        written += MapStructuralType(el, "BLE_STRUCT_ELE_TYPE_TXT");
                        break;

                    case "Rooms":
                        written += MapDimension(el, BuiltInParameter.ROOM_AREA,
                            "ASS_ROOM_AREA_SQ_M", sqFtToSqM);
                        written += MapDimension(el, BuiltInParameter.ROOM_VOLUME,
                            "ASS_ROOM_VOLUME_CU_M", 0.0283168); // cu ft → cu m
                        written += MapDimension(el, BuiltInParameter.ROOM_UPPER_OFFSET,
                            "BLE_CEILING_HEIGHT_MM", ftToMm);
                        written += MapRoomNameNumber(el);
                        break;
                }

                // Category name → ASS_CAT_TXT (all elements)
                if (!string.IsNullOrEmpty(catName))
                    written += SetIfEmptyInt(el, "ASS_CAT_TXT", catName);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MapDimensionalParams failed for {el?.Id}: {ex.Message}");
            }

            return written;
        }

        /// <summary>Map a built-in dimension parameter with unit conversion.</summary>
        private static int MapDimension(Element el, BuiltInParameter bip,
            string targetParam, double conversionFactor)
        {
            try
            {
                Parameter p = el.get_Parameter(bip);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return 0;

                double val = p.AsDouble() * conversionFactor;
                if (val <= 0.001) return 0;

                string formatted = conversionFactor > 1
                    ? Math.Round(val, 0).ToString("F0",
                        System.Globalization.CultureInfo.InvariantCulture)
                    : val.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture);

                return SetIfEmptyInt(el, targetParam, formatted);
            }
            catch (Exception ex) { StingLog.Warn("MapDimension failed for " + bip + " → " + targetParam + ": " + ex.Message); return 0; }
        }

        /// <summary>Map a named lookup parameter with unit conversion.</summary>
        private static int MapLookup(Element el, string paramName,
            string targetParam, double conversionFactor)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return 0;

                double val = p.AsDouble() * conversionFactor;
                if (val <= 0.001) return 0;

                string formatted = Math.Round(val, 0).ToString("F0",
                    System.Globalization.CultureInfo.InvariantCulture);
                return SetIfEmptyInt(el, targetParam, formatted);
            }
            catch (Exception ex) { StingLog.Warn("MapLookup failed for " + paramName + " → " + targetParam + ": " + ex.Message); return 0; }
        }

        /// <summary>Map a named parameter string value (e.g., Fire Rating).</summary>
        private static int MapStringParam(Element el, string sourceName, string targetParam)
        {
            try
            {
                Parameter p = el.LookupParameter(sourceName);
                if (p == null || !p.HasValue) return 0;

                string val = p.StorageType == StorageType.String
                    ? p.AsString()
                    : p.AsValueString();

                if (string.IsNullOrEmpty(val)) return 0;
                return SetIfEmptyInt(el, targetParam, val);
            }
            catch (Exception ex) { StingLog.Warn("MapStringParam failed for " + sourceName + " → " + targetParam + ": " + ex.Message); return 0; }
        }

        /// <summary>Get floor thickness from compound structure or parameter.</summary>
        private static int MapFloorThickness(Element el, string targetParam)
        {
            try
            {
                // Try FLOOR_ATTR_THICKNESS_PARAM first
                Parameter p = el.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    double mm = p.AsDouble() * 304.8;
                    if (mm > 0.1)
                        return SetIfEmptyInt(el, targetParam,
                            Math.Round(mm, 0).ToString("F0",
                                System.Globalization.CultureInfo.InvariantCulture));
                }
                // Fallback: try "Thickness" named parameter
                return MapLookup(el, "Thickness", targetParam, 304.8);
            }
            catch (Exception ex) { StingLog.Warn("MapFloorThickness failed for element " + el?.Id + ": " + ex.Message); return 0; }
        }

        /// <summary>Get roof slope in degrees.</summary>
        private static int MapRoofSlope(Element el, string targetParam)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.ROOF_SLOPE);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    // Revit stores slope as rise/12 ratio
                    double slope = p.AsDouble();
                    double degrees = Math.Atan(slope) * 180.0 / Math.PI;
                    if (degrees > 0)
                        return SetIfEmptyInt(el, targetParam,
                            degrees.ToString("F1",
                                System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { StingLog.Warn("MapRoofSlope failed for element " + el?.Id + ": " + ex.Message); }
            return 0;
        }

        /// <summary>Get stair actual run width.</summary>
        private static int MapStairWidth(Element el, string targetParam)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.STAIRS_ATTR_TREAD_WIDTH);
                if (p == null || !p.HasValue)
                    p = el.LookupParameter("Actual Run Width");

                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    double mm = p.AsDouble() * 304.8;
                    if (mm > 0)
                        return SetIfEmptyInt(el, targetParam,
                            Math.Round(mm, 0).ToString("F0",
                                System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { StingLog.Warn("MapStairWidth failed for element " + el?.Id + ": " + ex.Message); }
            return 0;
        }

        /// <summary>Get ramp slope as percentage.</summary>
        private static int MapRampSlope(Element el, string targetParam)
        {
            try
            {
                Parameter p = el.LookupParameter("Slope");
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    double slopePct = p.AsDouble() * 100.0;
                    if (slopePct > 0)
                        return SetIfEmptyInt(el, targetParam,
                            slopePct.ToString("F1",
                                System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { StingLog.Warn("MapRampSlope failed for element " + el?.Id + ": " + ex.Message); }
            return 0;
        }

        /// <summary>Get structural element type name for BLE_STRUCT_ELE_TYPE_TXT.</summary>
        private static int MapStructuralType(Element el, string targetParam)
        {
            try
            {
                string typeName = GetFamilySymbolName(el);
                if (!string.IsNullOrEmpty(typeName))
                    return SetIfEmptyInt(el, targetParam, typeName);
            }
            catch (Exception ex) { StingLog.Warn("MapStructuralType failed for element " + el?.Id + ": " + ex.Message); }
            return 0;
        }

        /// <summary>Map Room Name and Number for Room elements.</summary>
        private static int MapRoomNameNumber(Element el)
        {
            int written = 0;
            try
            {
                Parameter name = el.get_Parameter(BuiltInParameter.ROOM_NAME);
                if (name != null && name.HasValue)
                    written += SetIfEmptyInt(el, "BLE_ROOM_NAME_TXT", name.AsString() ?? "");

                Parameter num = el.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                if (num != null && num.HasValue)
                    written += SetIfEmptyInt(el, "BLE_ROOM_NUMBER_TXT", num.AsString() ?? "");

                Parameter dept = el.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                if (dept != null && dept.HasValue)
                    written += SetIfEmptyInt(el, "ASS_DEPARTMENT_ASSIGNMENT_TXT",
                        dept.AsString() ?? "");
            }
            catch (Exception ex) { StingLog.Warn("MapRoomNameNumber failed for element " + el?.Id + ": " + ex.Message); }
            return written;
        }

        /// <summary>
        /// Set default values for parameters that have sensible defaults.
        /// ASS_STATUS_TXT defaults to "NEW" for all elements.
        /// </summary>
        private static int MapDefaults(Element el)
        {
            int written = 0;
            written += SetIfEmptyInt(el, "ASS_STATUS_TXT", "NEW");
            return written;
        }

        /// <summary>
        /// Map MEP-specific native parameters (flow rates, voltages, pressures, etc.)
        /// to corresponding STING shared parameters.
        /// Expanded for comprehensive schedule field coverage.
        /// </summary>
        private static int MapMepParams(Element el)
        {
            int written = 0;
            string catName = (el.Category?.Name ?? "");
            string catUpper = catName.ToUpperInvariant();

            // ── Electrical Equipment & Fixtures ────────────────────────────────
            if (catUpper.Contains("ELECTRICAL") || catUpper.Contains("LIGHTING") ||
                catUpper.Contains("CONDUIT") || catUpper.Contains("CABLE"))
            {
                // Core electrical params
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_APPARENT_LOAD, "ELC_CKT_PWR_KW");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_VOLTAGE, "ELC_CKT_VLT_V");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER, "ELC_CKT_NR");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM, "ELC_PNL_DESIGNATION_NAME_TXT");

                // Also write to legacy param names used by schedules
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_VOLTAGE, "ELC_VLT_PRIMARY_RATING_V");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES, "ELC_CKT_PHASE_COUNT_NR");

                // Panel-specific params
                if (catUpper.Contains("EQUIPMENT"))
                {
                    written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM,
                        "ELC_PNL_CONNECTED_LOAD_KW");
                    written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_PANEL_FEED_PARAM,
                        "ELC_PNL_FED_FROM_PNL_TXT");
                    written += MapStringParam(el, "Mains", "ELC_PNL_MAIN_BRK_A");
                    written += MapStringParam(el, "Max #1 Pole Breakers",
                        "ELC_PNL_NUM_OF_WAYS_NR");
                    written += MapStringParam(el, "IP Rating", "ELC_IP_RATING_TXT");
                }

                // Lighting-specific params
                if (catUpper.Contains("LIGHTING"))
                {
                    written += MapStringParam(el, "Wattage", "LTG_FIX_LMP_WATTAGE_W");
                    written += MapStringParam(el, "Initial Intensity", "CST_FIX_LUMEN_OUTPUT_LM");
                    written += MapStringParam(el, "Efficacy", "LTG_FIX_EFFICACY_LM_W");
                    written += MapStringParam(el, "Lamp", "LTG_FIX_LAMP_TYPE_TXT");
                }
            }

            // ── Duct & Air Terminal parameters ─────────────────────────────────
            if (catUpper.Contains("DUCT") || catUpper.Contains("AIR TERMINAL"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM, "HVC_DCT_FLW_CFM");
                written += MapBuiltIn(el, BuiltInParameter.RBS_VELOCITY, "HVC_VEL_MPS");
                written += MapBuiltIn(el, BuiltInParameter.RBS_LOSS_COEFFICIENT, "HVC_PRESSURE_DROP_PA");
                written += MapBuiltIn(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM, "HVC_AIRFLOW_LPS");
                // System type name for schedule grouping
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    "ASS_SYSTEM_TYPE_TXT");
            }

            // ── Mechanical Equipment ───────────────────────────────────────────
            if (catName == "Mechanical Equipment")
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    "ASS_SYSTEM_TYPE_TXT");
            }

            // ── Pipe parameters ────────────────────────────────────────────────
            if (catUpper.Contains("PIPE") || catUpper.Contains("PLUMBING") ||
                catUpper.Contains("SPRINKLER"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_FLOW_PARAM, "PLM_PPE_FLW_LPS");
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, "PLM_PPE_SZ_MM");
                written += MapBuiltIn(el, BuiltInParameter.RBS_VELOCITY, "PLM_VEL_MPS");
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_FLOW_PARAM, "PLM_FLOW_RATE_LPS");
                // Pipe length
                written += MapDimension(el, BuiltInParameter.CURVE_ELEM_LENGTH,
                    "PLM_PPE_LENGTH_M", 0.3048); // ft → m
                // System type
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    "ASS_SYSTEM_TYPE_TXT");
            }

            // ── Fire Alarm Devices ─────────────────────────────────────────────
            if (catName == "Fire Alarm Devices")
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    "ASS_SYSTEM_TYPE_TXT");
            }

            // ── Size parameters (generic MEP) ──────────────────────────────────
            written += MapBuiltIn(el, BuiltInParameter.RBS_CALCULATED_SIZE, "ASS_SIZE_TXT");

            return written;
        }

        /// <summary>
        /// Read type-level parameters as fallback when instance parameters are empty.
        /// Useful for manufacturer, model, description which are often on the type.
        /// </summary>
        private static int MapFromType(Document doc, Element el)
        {
            int written = 0;
            try
            {
                ElementId typeId = el.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return 0;

                Element elType = doc.GetElement(typeId);
                if (elType == null) return 0;

                // Only fill STING params that are still empty after instance-level mapping
                if (string.IsNullOrEmpty(GetString(el, "ASS_DESCRIPTION_TXT")))
                    written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_DESCRIPTION,
                        "ASS_DESCRIPTION_TXT", el);
                if (string.IsNullOrEmpty(GetString(el, "ASS_MODEL_NR_TXT")))
                    written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_MODEL,
                        "ASS_MODEL_NR_TXT", el);
                if (string.IsNullOrEmpty(GetString(el, "ASS_MANUFACTURER_TXT")))
                    written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_MANUFACTURER,
                        "ASS_MANUFACTURER_TXT", el);

                // Type Mark → ASS_TYPE_MARK_TXT (commonly used for spec references)
                written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_TYPE_MARK,
                    "ASS_TYPE_MARK_TXT", el);

                // Type Comments
                written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
                    "ASS_TYPE_COMMENTS_TXT", el);

                // Keynote
                written += MapBuiltIn(elType, BuiltInParameter.KEYNOTE_PARAM,
                    "ASS_KEYNOTE_TXT", el);

                // Assembly Code (Uniformat)
                written += MapBuiltIn(elType, BuiltInParameter.UNIFORMAT_CODE,
                    "ASS_UNIFORMAT_TXT", el);

                // Assembly Description
                written += MapBuiltIn(elType, BuiltInParameter.UNIFORMAT_DESCRIPTION,
                    "ASS_UNIFORMAT_DESC_TXT", el);

                // OmniClass Title
                written += MapBuiltIn(elType, BuiltInParameter.OMNICLASS_CODE,
                    "ASS_OMNICLASS_TXT", el);

                // Cost (if available)
                written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_COST,
                    "ASS_CST_UNIT_PRICE_UGX_NR", el);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MapFromType failed for {el?.Id}: {ex.Message}");
            }
            return written;
        }

        /// <summary>
        /// Read a built-in parameter from a source element and write to a shared
        /// parameter on a target element (or same element if target is null).
        /// Only writes if the target parameter is empty.
        /// </summary>
        private static int MapBuiltIn(Element source, BuiltInParameter bip,
            string targetParamName, Element target = null)
        {
            try
            {
                Parameter p = source.get_Parameter(bip);
                if (p == null || !p.HasValue) return 0;

                string val;
                switch (p.StorageType)
                {
                    case StorageType.String:
                        val = p.AsString();
                        break;
                    case StorageType.Double:
                        val = p.AsDouble().ToString("G6",
                            System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case StorageType.Integer:
                        val = p.AsInteger().ToString();
                        break;
                    default:
                        val = p.AsValueString();
                        break;
                }

                if (string.IsNullOrEmpty(val) || val == "0") return 0;

                Element writeTarget = target ?? source;
                return SetIfEmptyInt(writeTarget, targetParamName, val);
            }
            catch (Exception ex)
            {
                StingLog.Warn("MapBuiltIn failed for " + bip + " → " + targetParamName + ": " + ex.Message);
                return 0;
            }
        }

        /// <summary>SetIfEmpty returning 1 on success, 0 on skip/failure.</summary>
        private static int SetIfEmptyInt(Element el, string paramName, string value)
        {
            return ParameterHelpers.SetIfEmpty(el, paramName, value) ? 1 : 0;
        }
    }
}
