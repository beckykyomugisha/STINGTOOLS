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

                return name.ToUpperInvariant()
                    .Replace(" ", "")
                    .Substring(0, Math.Min(4, name.Length));
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
                catch { }
            }

            // ── MEP-specific parameters ────────────────────────────────────────
            written += MapMepParams(el);

            // ── Type parameter fallback ────────────────────────────────────────
            // If instance params are still empty, try reading from the element type
            written += MapFromType(doc, el);

            return written;
        }

        /// <summary>
        /// Map MEP-specific native parameters (flow rates, voltages, pressures, etc.)
        /// to corresponding STING shared parameters.
        /// </summary>
        private static int MapMepParams(Element el)
        {
            int written = 0;
            string catName = (el.Category?.Name ?? "").ToUpperInvariant();

            // Electrical parameters
            if (catName.Contains("ELECTRICAL") || catName.Contains("LIGHTING") ||
                catName.Contains("CONDUIT") || catName.Contains("CABLE"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_APPARENT_LOAD, "ELC_CKT_PWR_KW");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_VOLTAGE, "ELC_CKT_VLT_V");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER, "ELC_CKT_NUM_TXT");
                written += MapBuiltIn(el, BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM, "ELC_CKT_PANEL_TXT");
            }

            // Duct parameters
            if (catName.Contains("DUCT") || catName.Contains("AIR TERMINAL"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM, "HVC_AIRFLOW_LPS");
                written += MapBuiltIn(el, BuiltInParameter.RBS_VELOCITY, "HVC_VEL_MPS");
                written += MapBuiltIn(el, BuiltInParameter.RBS_LOSS_COEFFICIENT, "HVC_PRESSURE_DROP_PA");
            }

            // Pipe parameters
            if (catName.Contains("PIPE") || catName.Contains("PLUMBING") ||
                catName.Contains("SPRINKLER"))
            {
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_FLOW_PARAM, "PLM_FLOW_RATE_LPS");
                written += MapBuiltIn(el, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, "PLM_PPE_SZ_MM");
            }

            // Size parameters (generic MEP)
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
