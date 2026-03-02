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
                if (lvlId == null || lvlId == ElementId.InvalidElementId)
                    return "XX";

                Level lvl = doc.GetElement(lvlId) as Level;
                if (lvl == null)
                    return "XX";

                string name = lvl.Name.Trim();
                string lower = name.ToLowerInvariant();

                if (lower.StartsWith("level ") && name.Length > 6)
                    return "L" + name.Substring(6).Trim().PadLeft(2, '0');
                if (lower == "ground" || lower == "ground floor" || lower == "ground level")
                    return "GF";
                if (lower.StartsWith("lower ground") || lower == "lg")
                    return "LG";
                if (lower.StartsWith("upper ground") || lower == "ug")
                    return "UG";
                if (lower.StartsWith("sub-basement") || lower.StartsWith("sub basement") || lower == "sb")
                {
                    string digits = ExtractDigits(name);
                    return "SB" + (digits.Length > 0 ? digits : "");
                }
                if (lower.StartsWith("basement") || lower == "b1" || lower == "b2" ||
                    lower == "b3" || lower == "b4" || lower == "b5" ||
                    (lower.Length >= 2 && lower[0] == 'b' && char.IsDigit(lower[1])))
                {
                    string digits = ExtractDigits(name);
                    return "B" + (digits.Length > 0 ? digits : "1");
                }
                if (lower.StartsWith("roof") || lower == "rf")
                    return "RF";
                if (lower.StartsWith("penthouse") || lower == "ph" || lower == "pent")
                    return "PH";
                if (lower.StartsWith("attic") || lower == "at" || lower == "att")
                    return "AT";
                if (lower.StartsWith("terrace") || lower == "tr")
                    return "TR";
                if (lower.StartsWith("podium") || lower == "pod")
                    return "POD";
                if (lower.StartsWith("mezzanine") || lower == "mezz")
                    return "MZ";
                if (lower.StartsWith("plant") && lower.Contains("room"))
                    return "PL";

                // Extract digits for "1st floor", "2nd floor", "L01" etc.
                if (lower.Contains("first") || lower.Contains("1st"))
                    return "L01";
                if (lower.Contains("second") || lower.Contains("2nd"))
                    return "L02";
                if (lower.Contains("third") || lower.Contains("3rd"))
                    return "L03";
                if (lower.Contains("fourth") || lower.Contains("4th"))
                    return "L04";
                if (lower.Contains("fifth") || lower.Contains("5th"))
                    return "L05";

                // Try to extract a floor number from patterns like "L01", "L1", "Floor 3"
                string digits = ExtractDigits(name);
                if (digits.Length > 0 && digits.Length <= 2)
                    return "L" + digits.PadLeft(2, '0');

                // Unrecognized pattern — return XX rather than truncating the name
                // which could produce nonsensical level codes
                StingLog.Info($"GetLevelCode: unrecognized level name '{name}', defaulting to XX");
                return "XX";
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

                // Check if element is likely exterior
                if (room == null && el.Location != null)
                {
                    // Heuristic: if the project has rooms defined and this element
                    // has a valid location but isn't in any room, check the element's
                    // category and family name for exterior indicators
                    if (roomIndex.Count > 0)
                    {
                        string familyName = ParameterHelpers.GetFamilyName(el).ToUpperInvariant();
                        string catName = ParameterHelpers.GetCategoryName(el).ToUpperInvariant();
                        // Only flag specific elements that are commonly exterior
                        if (familyName.Contains("EXTERNAL") || familyName.Contains("EXTERIOR") ||
                            familyName.Contains("OUTDOOR") || familyName.Contains("WEATHERPROOF") ||
                            familyName.Contains("BOLLARD") || familyName.Contains("FLOODLIGHT") ||
                            (catName.Contains("LIGHTING") && familyName.Contains("POLE")) ||
                            (catName.Contains("LIGHTING") && familyName.Contains("POST")))
                            return "EXT";
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
            // Require word-boundary match for EXT to avoid matching "NEXT", "TEXTILE", "EXTENSION"
            if (upper == "EXT" || upper.Contains("EXTERNAL") || upper.Contains("EXTERIOR") ||
                upper.StartsWith("EXT ") || upper.Contains(" EXT ") || upper.EndsWith(" EXT"))
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
            if (upper.Contains("Z01") || upper.Contains("ZONE 1") || upper.Contains("ZONE A") || upper.Contains("WING A"))
                return "Z01";
            if (upper.Contains("Z02") || upper.Contains("ZONE 2") || upper.Contains("ZONE B") || upper.Contains("WING B"))
                return "Z02";
            if (upper.Contains("Z03") || upper.Contains("ZONE 3") || upper.Contains("ZONE C") || upper.Contains("WING C"))
                return "Z03";
            if (upper.Contains("Z04") || upper.Contains("ZONE 4") || upper.Contains("ZONE D") || upper.Contains("WING D"))
                return "Z04";

            // Directional terms — require word-boundary match to avoid "NORTHAMPTON" etc.
            if (MatchesWord(upper, "NORTH")) return "Z01";
            if (MatchesWord(upper, "SOUTH")) return "Z02";
            if (MatchesWord(upper, "EAST")) return "Z03";
            if (MatchesWord(upper, "WEST")) return "Z04";

            return null;
        }

        /// <summary>Check if a word appears as a standalone token (not part of a longer word).</summary>
        private static bool MatchesWord(string text, string word)
        {
            int idx = text.IndexOf(word);
            while (idx >= 0)
            {
                bool startOk = idx == 0 || !char.IsLetter(text[idx - 1]);
                bool endOk = (idx + word.Length) >= text.Length || !char.IsLetter(text[idx + word.Length]);
                if (startOk && endOk) return true;
                idx = text.IndexOf(word, idx + 1);
            }
            return false;
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
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MARK, ParamRegistry.ID);
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, ParamRegistry.PRJ_COMMENTS);
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_DESCRIPTION, ParamRegistry.DESC);
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MODEL, ParamRegistry.MODEL);
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MANUFACTURER, ParamRegistry.MFR);

            // Type Name (from the family symbol name)
            string typeName = ParameterHelpers.GetFamilySymbolName(el);
            if (!string.IsNullOrEmpty(typeName))
                written += SetIfEmptyInt(el, ParamRegistry.TYPE_NAME, typeName);

            // Family Name
            string familyName = ParameterHelpers.GetFamilyName(el);
            if (!string.IsNullOrEmpty(familyName))
                written += SetIfEmptyInt(el, ParamRegistry.FAMILY_NAME, familyName);

            // ── Spatial / Room data ────────────────────────────────────────────
            Room room = ParameterHelpers.GetRoomAtElement(doc, el);
            if (room != null)
            {
                written += SetIfEmptyInt(el, ParamRegistry.ROOM_NAME, room.Name ?? "");
                written += SetIfEmptyInt(el, ParamRegistry.ROOM_NUM, room.Number ?? "");

                // Room area in m² (Revit stores in sq ft, convert)
                double areaSqFt = room.Area;
                if (areaSqFt > 0)
                {
                    string areaM2 = (areaSqFt * 0.092903).ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture);
                    written += SetIfEmptyInt(el, ParamRegistry.ROOM_AREA, areaM2);
                }

                // Room Department
                try
                {
                    Parameter dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                    if (dept != null && dept.HasValue)
                        written += SetIfEmptyInt(el, ParamRegistry.DEPT,
                            dept.AsString() ?? "");
                }
                catch { }
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
            const double cuFtToCuM = 0.0283168;
            try
            {
                switch (catName)
                {
                    case "Walls":
                        written += MapDimension(el, BuiltInParameter.WALL_USER_HEIGHT_PARAM,
                            ParamRegistry.WALL_HEIGHT, ftToMm);
                        written += MapDimension(el, BuiltInParameter.CURVE_ELEM_LENGTH,
                            ParamRegistry.WALL_LENGTH, ftToMm);
                        written += MapDimension(el, BuiltInParameter.WALL_ATTR_WIDTH_PARAM,
                            ParamRegistry.WALL_THICKNESS, ftToMm);
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            ParamRegistry.ELE_AREA, sqFtToSqM);
                        written += MapDimension(el, BuiltInParameter.HOST_VOLUME_COMPUTED,
                            ParamRegistry.ELE_VOLUME, cuFtToCuM);
                        written += MapStringParam(el, "Fire Rating",
                            ParamRegistry.FIRE_RATING);
                        break;

                    case "Doors":
                        written += MapDimension(el, BuiltInParameter.FAMILY_WIDTH_PARAM,
                            ParamRegistry.DOOR_WIDTH, ftToMm);
                        written += MapDimension(el, BuiltInParameter.FAMILY_HEIGHT_PARAM,
                            ParamRegistry.DOOR_HEIGHT, ftToMm);
                        written += MapDimension(el, BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM,
                            ParamRegistry.DOOR_HEAD_HT, ftToMm);
                        written += MapFunctionParam(el, ParamRegistry.DOOR_FUNC);
                        written += MapStringParam(el, "Fire Rating",
                            ParamRegistry.FIRE_RATING);
                        break;

                    case "Windows":
                        written += MapDimension(el, BuiltInParameter.FAMILY_WIDTH_PARAM,
                            ParamRegistry.WINDOW_WIDTH, ftToMm);
                        written += MapDimension(el, BuiltInParameter.FAMILY_HEIGHT_PARAM,
                            ParamRegistry.WINDOW_HEIGHT, ftToMm);
                        written += MapDimension(el, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM,
                            ParamRegistry.WINDOW_SILL, ftToMm);
                        written += MapDimension(el, BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM,
                            ParamRegistry.WINDOW_HEAD_HT, ftToMm);
                        break;

                    case "Floors":
                        written += MapFloorThickness(el, ParamRegistry.FLR_THICKNESS);
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            ParamRegistry.ELE_AREA, sqFtToSqM);
                        written += MapDimension(el, BuiltInParameter.HOST_VOLUME_COMPUTED,
                            ParamRegistry.ELE_VOLUME, cuFtToCuM);
                        break;

                    case "Ceilings":
                        written += MapDimension(el, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM,
                            ParamRegistry.CEILING_HEIGHT, ftToMm);
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            ParamRegistry.ELE_AREA, sqFtToSqM);
                        written += MapStringParam(el, "Fire Rating",
                            ParamRegistry.FIRE_RATING);
                        break;

                    case "Roofs":
                        written += MapRoofSlope(el, ParamRegistry.ROOF_SLOPE);
                        written += MapDimension(el, BuiltInParameter.HOST_AREA_COMPUTED,
                            ParamRegistry.ELE_AREA, sqFtToSqM);
                        break;

                    case "Stairs":
                        written += MapDimension(el, BuiltInParameter.STAIRS_ACTUAL_TREAD_DEPTH,
                            ParamRegistry.STAIR_TREAD, ftToMm);
                        written += MapDimension(el, BuiltInParameter.STAIRS_ACTUAL_RISER_HEIGHT,
                            ParamRegistry.STAIR_RISE, ftToMm);
                        written += MapStairWidth(el, ParamRegistry.STAIR_WIDTH);
                        break;

                    case "Ramps":
                        written += MapRampSlope(el, ParamRegistry.RAMP_SLOPE);
                        written += MapLookup(el, "Width", ParamRegistry.RAMP_WIDTH, ftToMm);
                        break;

                    case "Structural Framing":
                    case "Structural Columns":
                    case "Structural Foundations":
                        written += MapStructuralType(el, ParamRegistry.STRUCT_TYPE);
                        break;

                    case "Rooms":
                        written += MapDimension(el, BuiltInParameter.ROOM_AREA,
                            ParamRegistry.ROOM_AREA, sqFtToSqM);
                        written += MapDimension(el, BuiltInParameter.ROOM_VOLUME,
                            ParamRegistry.ROOM_VOLUME, cuFtToCuM);
                        written += MapDimension(el, BuiltInParameter.ROOM_UPPER_OFFSET,
                            ParamRegistry.CEILING_HEIGHT, ftToMm);
                        written += MapRoomNameNumber(el);
                        // Room finishes (commonly needed for fit-out schedules)
                        written += MapBuiltInString(el, BuiltInParameter.ROOM_FINISH_FLOOR,
                            ParamRegistry.ROOM_FINISH_FLR);
                        written += MapBuiltInString(el, BuiltInParameter.ROOM_FINISH_WALL,
                            ParamRegistry.ROOM_FINISH_WALL);
                        written += MapBuiltInString(el, BuiltInParameter.ROOM_FINISH_CEILING,
                            ParamRegistry.ROOM_FINISH_CLG);
                        written += MapBuiltInString(el, BuiltInParameter.ROOM_FINISH_BASE,
                            ParamRegistry.ROOM_FINISH_BASE);
                        break;
                }

                // Category name (all elements)
                if (!string.IsNullOrEmpty(catName))
                    written += SetIfEmptyInt(el, ParamRegistry.CAT, catName);
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
            catch { return 0; }
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
            catch { return 0; }
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
            catch { return 0; }
        }

        /// <summary>Map a built-in string parameter directly (e.g., room finishes).</summary>
        private static int MapBuiltInString(Element el, BuiltInParameter bip, string targetParam)
        {
            try
            {
                Parameter p = el.get_Parameter(bip);
                if (p == null || !p.HasValue) return 0;

                string val = p.StorageType == StorageType.String
                    ? p.AsString()
                    : p.AsValueString();

                if (string.IsNullOrEmpty(val)) return 0;
                return SetIfEmptyInt(el, targetParam, val);
            }
            catch { return 0; }
        }

        /// <summary>Map door/window function (Interior/Exterior) from built-in parameter.</summary>
        private static int MapFunctionParam(Element el, string targetParam)
        {
            try
            {
                Parameter p = el.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
                if (p == null || !p.HasValue) return 0;

                string val = p.AsValueString(); // "Interior", "Exterior", etc.
                if (string.IsNullOrEmpty(val)) return 0;
                return SetIfEmptyInt(el, targetParam, val);
            }
            catch { return 0; }
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
            catch { return 0; }
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
            catch { }
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
            catch { }
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
            catch { }
            return 0;
        }

        /// <summary>Get structural element type name for BLE_STRUCT_ELE_TYPE_TXT.</summary>
        private static int MapStructuralType(Element el, string targetParam)
        {
            try
            {
                string typeName = ParameterHelpers.GetFamilySymbolName(el);
                if (!string.IsNullOrEmpty(typeName))
                    return SetIfEmptyInt(el, targetParam, typeName);
            }
            catch { }
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
                    written += SetIfEmptyInt(el, ParamRegistry.BLE_ROOM_NAME, name.AsString() ?? "");

                Parameter num = el.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                if (num != null && num.HasValue)
                    written += SetIfEmptyInt(el, ParamRegistry.BLE_ROOM_NUM, num.AsString() ?? "");

                Parameter dept = el.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                if (dept != null && dept.HasValue)
                    written += SetIfEmptyInt(el, ParamRegistry.DEPT,
                        dept.AsString() ?? "");
            }
            catch { }
            return written;
        }

        /// <summary>
        /// Set default values for parameters that have sensible defaults.
        /// STATUS is derived from the element's phase (PHASE_CREATED / PHASE_DEMOLISHED)
        /// when available, falling back to "NEW" if no phase data exists.
        /// </summary>
        private static int MapDefaults(Element el)
        {
            int written = 0;

            // Derive STATUS from element phase lifecycle
            string status = "NEW";
            try
            {
                var phaseCreated = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                var phaseDemolished = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);

                if (phaseDemolished != null && phaseDemolished.AsElementId() != ElementId.InvalidElementId)
                {
                    // Element has a demolition phase — it's demolished
                    status = "DEMOLISHED";
                }
                else if (phaseCreated != null && phaseCreated.AsElementId() != ElementId.InvalidElementId)
                {
                    var doc = el.Document;
                    var phase = doc.GetElement(phaseCreated.AsElementId());
                    if (phase != null)
                    {
                        string phaseName = phase.Name.ToUpperInvariant();
                        if (phaseName.Contains("EXIST"))
                            status = "EXISTING";
                        else if (phaseName.Contains("DEMO"))
                            status = "DEMOLISHED";
                        else if (phaseName.Contains("TEMP"))
                            status = "TEMPORARY";
                        // else remains "NEW" (for "New Construction" etc.)
                    }
                }
            }
            catch { /* Phase parameters may not exist on all element types */ }

            written += SetIfEmptyInt(el, ParamRegistry.STATUS, status);
            return written;
        }

        /// <summary>
        /// Map MEP-specific native parameters (flow rates, voltages, pressures, etc.)
        /// to corresponding STING shared parameters.
        /// Expanded for comprehensive schedule field coverage.
        /// </summary>
        private static int MapMepParams(Element el)
        {
            const double ftToMm = 304.8;
            int written = 0;
            string catName = (el.Category?.Name ?? "");
            string catUpper = catName.ToUpperInvariant();

            // ── Electrical Equipment & Fixtures ────────────────────────────────
            if (catUpper.Contains("ELECTRICAL") || catUpper.Contains("LIGHTING") ||
                catUpper.Contains("CONDUIT") || catUpper.Contains("CABLE"))
            {
                // Core electrical params
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_APPARENT_LOAD, ParamRegistry.ELC_POWER);
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_VOLTAGE, ParamRegistry.ELC_VOLTAGE);
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER, ParamRegistry.ELC_CIRCUIT_NR);
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM, ParamRegistry.ELC_PNL_NAME);

                // Also write to legacy param names used by schedules
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_VOLTAGE, ParamRegistry.ELC_PNL_VOLTAGE);
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES, ParamRegistry.ELC_PHASES);

                // Panel-specific params
                if (catUpper.Contains("EQUIPMENT"))
                {
                    written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM,
                        ParamRegistry.ELC_PNL_LOAD);
                    written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_PANEL_FEED_PARAM,
                        ParamRegistry.ELC_PNL_FED_FROM);
                    written += MapStringParam(el, "Mains", ParamRegistry.ELC_MAIN_BRK);
                    written += MapStringParam(el, "Max #1 Pole Breakers",
                        ParamRegistry.ELC_WAYS);
                    written += MapStringParam(el, "IP Rating", ParamRegistry.ELC_IP_RATING);
                }

                // Lighting-specific params
                if (catUpper.Contains("LIGHTING"))
                {
                    written += MapStringParam(el, "Wattage", ParamRegistry.LTG_WATTAGE);
                    written += MapStringParam(el, "Initial Intensity", ParamRegistry.LTG_LUMENS);
                    written += MapStringParam(el, "Efficacy", ParamRegistry.LTG_EFFICACY);
                    written += MapStringParam(el, "Lamp", ParamRegistry.LTG_LAMP_TYPE);
                }
            }

            // ── Duct & Air Terminal parameters ─────────────────────────────────
            if (catUpper.Contains("DUCT") || catUpper.Contains("AIR TERMINAL"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM, ParamRegistry.HVC_DUCT_FLOW);
                written += MapBuiltIn(el, BuiltInParameter.RBS_VELOCITY, ParamRegistry.HVC_VELOCITY);
                written += MapBuiltIn(el, BuiltInParameter.RBS_LOSS_COEFFICIENT, ParamRegistry.HVC_PRESSURE);
                written += MapBuiltIn(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM, ParamRegistry.HVC_AIRFLOW);
                // Duct dimensions
                written += MapBuiltIn(el, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, ParamRegistry.HVC_DUCT_WIDTH);
                written += MapBuiltIn(el, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, ParamRegistry.HVC_DUCT_HEIGHT);
                // Duct insulation thickness
                written += MapLookup(el, "Insulation Thickness", ParamRegistry.HVC_INSULATION, ftToMm);
                // Duct length
                written += MapDimension(el, BuiltInParameter.CURVE_ELEM_LENGTH,
                    ParamRegistry.HVC_DUCT_LENGTH, 0.3048); // ft → m
            }

            // ── Conduit & Cable Tray length ─────────────────────────────────────
            if (catUpper.Contains("CONDUIT") || catUpper.Contains("CABLE TRAY"))
            {
                written += MapDimension(el, BuiltInParameter.CURVE_ELEM_LENGTH,
                    ParamRegistry.ELE_LENGTH, 0.3048); // ft → m
            }

            // ── Mechanical Equipment ───────────────────────────────────────────
            if (catName == "Mechanical Equipment")
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    ParamRegistry.SYS);
            }

            // ── Pipe parameters ────────────────────────────────────────────────
            if (catUpper.Contains("PIPE") || catUpper.Contains("PLUMBING") ||
                catUpper.Contains("SPRINKLER"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_FLOW_PARAM, ParamRegistry.PLM_PIPE_FLOW);
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, ParamRegistry.PLM_PIPE_SIZE);
                written += MapBuiltIn(el, BuiltInParameter.RBS_VELOCITY, ParamRegistry.PLM_VELOCITY);
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_FLOW_PARAM, ParamRegistry.PLM_FLOW_RATE);
                // Pipe length
                written += MapDimension(el, BuiltInParameter.CURVE_ELEM_LENGTH,
                    ParamRegistry.PLM_PIPE_LENGTH, 0.3048); // ft → m
                // System type
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    ParamRegistry.SYS);
            }

            // ── Fire Alarm Devices ─────────────────────────────────────────────
            if (catName == "Fire Alarm Devices")
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    ParamRegistry.SYS);
            }

            // ── Size parameters (generic MEP) ──────────────────────────────────
            written += MapBuiltIn(el, BuiltInParameter.RBS_CALCULATED_SIZE, ParamRegistry.SIZE);

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
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.DESC)))
                    written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_DESCRIPTION,
                        ParamRegistry.DESC, el);
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.MODEL)))
                    written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_MODEL,
                        ParamRegistry.MODEL, el);
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.MFR)))
                    written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_MANUFACTURER,
                        ParamRegistry.MFR, el);

                // Type Mark
                written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_TYPE_MARK,
                    ParamRegistry.TYPE_MARK, el);

                // Type Comments
                written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
                    ParamRegistry.TYPE_COMMENTS, el);

                // Keynote
                written += MapBuiltIn(elType, BuiltInParameter.KEYNOTE_PARAM,
                    ParamRegistry.KEYNOTE, el);

                // Assembly Code (Uniformat)
                written += MapBuiltIn(elType, BuiltInParameter.UNIFORMAT_CODE,
                    ParamRegistry.UNIFORMAT, el);

                // Assembly Description
                written += MapBuiltIn(elType, BuiltInParameter.UNIFORMAT_DESCRIPTION,
                    ParamRegistry.UNIFORMAT_DESC, el);

                // OmniClass Title
                written += MapBuiltIn(elType, BuiltInParameter.OMNICLASS_CODE,
                    ParamRegistry.OMNICLASS, el);

                // Cost (if available)
                written += MapBuiltIn(elType, BuiltInParameter.ALL_MODEL_COST,
                    ParamRegistry.COST, el);
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
            catch
            {
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
