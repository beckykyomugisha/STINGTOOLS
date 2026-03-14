using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    /// <summary>
    /// Ported from tag_logic.py — parameter read/write helpers for Revit elements.
    /// </summary>
    /// <summary>
    /// Safe command execution context — null-checked UIApplication, UIDocument,
    /// Document, and ActiveView. Use <see cref="ParameterHelpers.GetContext"/> to obtain.
    /// </summary>
    public class CommandContext
    {
        public UIApplication App { get; init; }
        public UIDocument UIDoc { get; init; }
        public Document Doc { get; init; }
        /// <summary>Active view — may be null if in family editor or schedule is active.
        /// Check before using with FilteredElementCollector(doc, view.Id).</summary>
        public View ActiveView { get; init; }
        /// <summary>True when ActiveView is a graphical view suitable for element collection.</summary>
        public bool HasGraphicalView => ActiveView != null
            && ActiveView.ViewType != ViewType.Schedule
            && ActiveView.ViewType != ViewType.DrawingSheet
            && ActiveView.ViewType != ViewType.Internal;
    }

    public static class ParameterHelpers
    {
        /// <summary>
        /// Get a fully null-checked command execution context.
        /// Returns null if no document is open (caller should return Result.Failed).
        /// Usage:
        ///   var ctx = ParameterHelpers.GetContext(commandData);
        ///   if (ctx == null) { message = "No document open."; return Result.Failed; }
        /// </summary>
        public static CommandContext GetContext(ExternalCommandData commandData)
        {
            var app = GetApp(commandData);
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return null;
            var doc = uidoc.Document;
            if (doc == null) return null;
            View activeView = null;
            try { activeView = doc.ActiveView; } catch (Exception ex) { StingLog.Warn($"GetContext: no active view: {ex.Message}"); }
            return new CommandContext { App = app, UIDoc = uidoc, Doc = doc, ActiveView = activeView };
        }

        /// <summary>
        /// Get UIApplication from ExternalCommandData with null-safe fallback to
        /// StingCommandHandler.CurrentApp. Use this at the top of every command:
        ///   UIApplication uiApp = ParameterHelpers.GetApp(commandData);
        ///   UIDocument uidoc = uiApp.ActiveUIDocument;
        ///   Document doc = uidoc.Document;
        /// Prefer <see cref="GetContext"/> for full null-safety.
        /// </summary>
        public static UIApplication GetApp(ExternalCommandData commandData)
        {
            if (commandData?.Application != null)
                return commandData.Application;

            // Fallback: when invoked from dockable panel via IExternalEventHandler,
            // ExternalCommandData is null. Use the static UIApplication reference
            // set by StingCommandHandler.Execute().
            var app = UI.StingCommandHandler.CurrentApp;
            if (app != null)
                return app;

            throw new InvalidOperationException(
                "No UIApplication available. Command must be invoked from " +
                "Revit ribbon or the STING dockable panel.");
        }

        // Parameter lookup cache: avoids O(n) LookupParameter on every call.
        // Keyed by (ElementId typeId, string paramName) → Definition.
        // Null values are cached to avoid repeated miss lookups.
        private static readonly ConcurrentDictionary<(ElementId, string), Definition> _paramCache
            = new ConcurrentDictionary<(ElementId, string), Definition>();

        /// <summary>Clear the parameter lookup cache. Call on document close or when
        /// shared parameters change (e.g., after LoadSharedParams).</summary>
        public static void ClearParamCache()
        {
            _paramCache.Clear();
        }

        /// <summary>Cached parameter lookup. Uses element's TypeId + paramName as cache key.
        /// Falls back to LookupParameter on first access per type, then O(1) thereafter.</summary>
        private static Parameter CachedLookup(Element el, string paramName)
        {
            ElementId typeId = el.GetTypeId();
            var key = (typeId, paramName);

            if (!_paramCache.TryGetValue(key, out Definition cachedDef))
            {
                Parameter found = el.LookupParameter(paramName);
                // Cache the Definition (not Parameter — that's per-element)
                _paramCache[key] = found?.Definition;
                return found;
            }

            if (cachedDef == null) return null; // Known miss
            return el.get_Parameter(cachedDef);
        }

        /// <summary>Return the string value of a named parameter, or empty string.</summary>
        public static string GetString(Element el, string paramName)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return string.Empty;
            Parameter p = CachedLookup(el, paramName);
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
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            Parameter p = CachedLookup(el, paramName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                return false;

            string existing = p.AsString() ?? string.Empty;
            if (existing.Length > 0 && !overwrite)
                return false;

            try
            {
                p.Set(value ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetString '{paramName}' on {el.Id} failed: {ex.Message}");
                return false;
            }
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
                {
                    string suffix = ExtractDigits(name.Substring(6));
                    if (suffix.Length > 0 && suffix.Length <= 3)
                        return "L" + suffix.PadLeft(2, '0');
                    // Non-numeric suffix (e.g., "Level 1a") — fall through to digit extraction
                }
                if (lower == "ground" || lower == "ground floor" || lower == "ground level")
                    return "GF";
                if (lower.StartsWith("lower ground") || lower == "lg")
                    return "LG";
                if (lower.StartsWith("upper ground") || lower == "ug")
                    return "UG";
                if (lower.StartsWith("sub-basement") || lower.StartsWith("sub basement") || lower == "sb")
                {
                    string sbDigits = ExtractDigits(name);
                    return "SB" + (sbDigits.Length > 0 ? sbDigits : "");
                }
                if (lower.StartsWith("basement") || lower == "b1" || lower == "b2" ||
                    lower == "b3" || lower == "b4" || lower == "b5" ||
                    (lower.Length >= 2 && lower[0] == 'b' && char.IsDigit(lower[1])))
                {
                    string bDigits = ExtractDigits(name);
                    return "B" + (bDigits.Length > 0 ? bDigits : "1");
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
                if (digits.Length > 0 && digits.Length <= 3)
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

                // Workset-based fallback: check workset name for LOC patterns
                string wsLoc = DetectLocFromWorkset(el);
                if (!string.IsNullOrEmpty(wsLoc)) return wsLoc;
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

                // Workset-based fallback: check workset name for ZONE patterns
                string wsZone = DetectZoneFromWorkset(el);
                if (!string.IsNullOrEmpty(wsZone)) return wsZone;
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

        /// <summary>
        /// Detect LOC from workset name patterns when room-based detection fails.
        /// Worksets often follow naming like "M-BLD1-Mechanical", "A-BLD2-Architecture",
        /// "EXT-External Works" per AEC UK BIM Protocol and ISO 19650-2.
        /// </summary>
        public static string DetectLocFromWorkset(Element el)
        {
            try
            {
                if (!el.Document.IsWorkshared) return null;
                WorksetId wsId = el.WorksetId;
                if (wsId == null || wsId == WorksetId.InvalidWorksetId) return null;

                WorksetTable table = el.Document.GetWorksetTable();
                Workset ws = table.GetWorkset(wsId);
                if (ws == null) return null;

                string loc = ParseLocCode(ws.Name);
                if (!string.IsNullOrEmpty(loc)) return loc;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectLocFromWorkset: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Detect ZONE from workset name patterns when room-based detection fails.
        /// Worksets may contain zone designators like "M-Z01-Mechanical", "E-Z02-Electrical".
        /// </summary>
        public static string DetectZoneFromWorkset(Element el)
        {
            try
            {
                if (!el.Document.IsWorkshared) return null;
                WorksetId wsId = el.WorksetId;
                if (wsId == null || wsId == WorksetId.InvalidWorksetId) return null;

                WorksetTable table = el.Document.GetWorksetTable();
                Workset ws = table.GetWorkset(wsId);
                if (ws == null) return null;

                string zone = ParseZoneCode(ws.Name);
                if (!string.IsNullOrEmpty(zone)) return zone;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectZoneFromWorkset: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Auto-derives construction STATUS from Revit's native phase system and workset
    /// naming conventions. The Revit Phase model (CreatedPhaseId / DemolishedPhaseId)
    /// provides authoritative construction status that previously required manual input
    /// via the SetStatus command.
    ///
    /// Intelligence layers (evaluated in order, first non-null wins):
    ///   1. Element DemolishedPhaseId — if set, the element is DEMOLISHED
    ///   2. Created Phase name patterns — "Existing", "As-Built" → EXISTING;
    ///      "New Construction", "New" → NEW; "Temporary" → TEMPORARY
    ///   3. Workset name patterns — "EXISTING_*", "DEMO_*", "TEMP_*", "NEW_*"
    ///   4. Phase filter context — element phase status relative to the project's
    ///      current/active phase
    ///
    /// Also derives REV (revision code) from the most recent Revit sheet revision
    /// associated with the project, providing automatic revision tracking.
    /// </summary>
    public static class PhaseAutoDetect
    {
        /// <summary>Valid STATUS values per ISO 19650.</summary>
        public static readonly string[] ValidStatuses =
            { "EXISTING", "NEW", "DEMOLISHED", "TEMPORARY" };

        /// <summary>
        /// Detect the construction STATUS for an element from its Revit phase assignments.
        /// Returns one of: EXISTING, NEW, DEMOLISHED, TEMPORARY, or null if uncertain.
        ///
        /// Layer 1: If the element has a DemolishedPhaseId, it is explicitly demolished.
        /// Layer 2: The CreatedPhase name is matched against status patterns — elements
        ///          created in an "Existing" phase are existing infrastructure, those
        ///          created in "New Construction" are new work.
        /// Layer 3: Workset name patterns provide a fallback when phase data is absent
        ///          or when the project does not use phasing.
        /// Layer 4: If the project has a defined active phase and the element was created
        ///          in an earlier phase, it is treated as EXISTING relative to current work.
        /// </summary>
        public static string DetectStatus(Document doc, Element el)
        {
            try
            {
                // Layer 1: Demolished phase — definitive
                Parameter demolParam = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (demolParam != null && demolParam.HasValue)
                {
                    ElementId demolPhaseId = demolParam.AsElementId();
                    if (demolPhaseId != null && demolPhaseId != ElementId.InvalidElementId)
                        return "DEMOLISHED";
                }

                // Layer 2: Created phase name pattern matching
                Parameter createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (createdParam != null && createdParam.HasValue)
                {
                    ElementId createdPhaseId = createdParam.AsElementId();
                    if (createdPhaseId != null && createdPhaseId != ElementId.InvalidElementId)
                    {
                        Phase phase = doc.GetElement(createdPhaseId) as Phase;
                        if (phase != null)
                        {
                            string phaseName = (phase.Name ?? "").ToUpperInvariant();
                            string status = ParseStatusFromPhaseName(phaseName);
                            if (!string.IsNullOrEmpty(status)) return status;

                            // Layer 4: Compare created phase against active/last phase
                            // If the element was created in a phase earlier than the last
                            // defined phase, treat it as EXISTING relative to current work.
                            var phases = new FilteredElementCollector(doc)
                                .OfClass(typeof(Phase))
                                .Cast<Phase>()
                                .ToList();
                            if (phases.Count > 1)
                            {
                                Phase lastPhase = phases.Last();
                                if (createdPhaseId != lastPhase.Id)
                                    return "EXISTING";
                            }
                        }
                    }
                }

                // Layer 3: Workset name patterns
                string fromWorkset = DetectStatusFromWorkset(el);
                if (!string.IsNullOrEmpty(fromWorkset)) return fromWorkset;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PhaseAutoDetect.DetectStatus: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Cached overload of DetectStatus that uses pre-built phase list from PopulationContext.
        /// Eliminates per-element FilteredElementCollector calls for phases — O(1) instead of O(n).
        /// Use this in batch operations where PopulationContext.CachedPhases is available.
        /// </summary>
        public static string DetectStatusCached(Document doc, Element el,
            List<Phase> cachedPhases, ElementId lastPhaseId)
        {
            try
            {
                // Layer 1: Demolished phase — definitive
                Parameter demolParam = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (demolParam != null && demolParam.HasValue)
                {
                    ElementId demolPhaseId = demolParam.AsElementId();
                    if (demolPhaseId != null && demolPhaseId != ElementId.InvalidElementId)
                        return "DEMOLISHED";
                }

                // Layer 2: Created phase name pattern matching
                Parameter createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (createdParam != null && createdParam.HasValue)
                {
                    ElementId createdPhaseId = createdParam.AsElementId();
                    if (createdPhaseId != null && createdPhaseId != ElementId.InvalidElementId)
                    {
                        Phase phase = doc.GetElement(createdPhaseId) as Phase;
                        if (phase != null)
                        {
                            string phaseName = (phase.Name ?? "").ToUpperInvariant();
                            string status = ParseStatusFromPhaseName(phaseName);
                            if (!string.IsNullOrEmpty(status)) return status;

                            // Layer 4: Compare created phase against last phase (using cached data)
                            if (cachedPhases != null && cachedPhases.Count > 1
                                && lastPhaseId != ElementId.InvalidElementId
                                && createdPhaseId != lastPhaseId)
                                return "EXISTING";
                        }
                    }
                }

                // Layer 3: Workset name patterns
                string fromWorkset = DetectStatusFromWorkset(el);
                if (!string.IsNullOrEmpty(fromWorkset)) return fromWorkset;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PhaseAutoDetect.DetectStatusCached: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Detect STATUS from workset naming conventions.
        /// Common patterns: "EXISTING_Walls", "DEMO_Mechanical", "NEW_Electrical",
        /// "TEMP_Hoarding", "01-Existing Structure", "02-New Build".
        /// </summary>
        public static string DetectStatusFromWorkset(Element el)
        {
            try
            {
                if (!el.Document.IsWorkshared) return null;
                WorksetId wsId = el.WorksetId;
                if (wsId == null || wsId == WorksetId.InvalidWorksetId) return null;

                WorksetTable table = el.Document.GetWorksetTable();
                Workset ws = table.GetWorkset(wsId);
                if (ws == null) return null;

                string wsName = (ws.Name ?? "").ToUpperInvariant();
                return ParseStatusFromText(wsName);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectStatusFromWorkset: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Parse a phase name into a STATUS value.
        /// Recognizes standard Revit phase names and ISO 19650 naming conventions.
        /// </summary>
        private static string ParseStatusFromPhaseName(string phaseName)
        {
            if (string.IsNullOrEmpty(phaseName)) return null;

            if (phaseName.Contains("EXISTING") || phaseName.Contains("AS-BUILT") ||
                phaseName.Contains("AS BUILT") || phaseName.Contains("SURVEY") ||
                phaseName.Contains("CURRENT") || phaseName.Contains("RETAINED"))
                return "EXISTING";

            if (phaseName.Contains("DEMOLITION") || phaseName.Contains("DEMOLISHED") ||
                phaseName.Contains("DEMO") || phaseName.Contains("REMOVAL") ||
                phaseName.Contains("STRIP OUT") || phaseName.Contains("STRIP-OUT"))
                return "DEMOLISHED";

            if (phaseName.Contains("TEMPORARY") || phaseName.Contains("TEMP WORKS") ||
                phaseName.Contains("ENABLEMENT") || phaseName.Contains("HOARDING") ||
                phaseName.Contains("PROPPING"))
                return "TEMPORARY";

            if (phaseName.Contains("NEW CONSTRUCTION") || phaseName.Contains("NEW BUILD") ||
                phaseName.Contains("PROPOSED") || phaseName.Contains("NEW WORK") ||
                phaseName == "NEW")
                return "NEW";

            return null;
        }

        /// <summary>
        /// Parse a text string (workset name, parameter value) for STATUS patterns.
        /// </summary>
        private static string ParseStatusFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            if (text.StartsWith("EXISTING") || text.Contains("_EXISTING") ||
                text.Contains("-EXISTING") || text.Contains(" EXISTING"))
                return "EXISTING";

            if (text.StartsWith("DEMO") || text.Contains("_DEMO") ||
                text.Contains("-DEMO") || text.Contains(" DEMOLISHED"))
                return "DEMOLISHED";

            if (text.StartsWith("TEMP") || text.Contains("_TEMP") ||
                text.Contains("-TEMP") || text.Contains(" TEMPORARY"))
                return "TEMPORARY";

            if (text.StartsWith("NEW") || text.Contains("_NEW") ||
                text.Contains("-NEW") || text.Contains(" NEW"))
                return "NEW";

            return null;
        }

        /// <summary>
        /// Detect the current project revision code from the most recent Revision element.
        /// Revit maintains a built-in revision sequence; this reads the latest revision's
        /// numbering value to auto-populate the REV token on elements.
        /// Returns null if no revisions are defined or the project has no revision history.
        /// </summary>
        public static string DetectProjectRevision(Document doc)
        {
            try
            {
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .Where(r => r.Issued || r.RevisionNumber != null)
                    .OrderByDescending(r => r.SequenceNumber)
                    .ToList();

                if (revisions.Count > 0)
                {
                    Revision latest = revisions[0];
                    string revNum = latest.RevisionNumber;
                    if (!string.IsNullOrEmpty(revNum))
                        return revNum;
                    // Fallback: use sequence number formatted as letter code
                    return SequenceToRevCode(latest.SequenceNumber);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectProjectRevision: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Convert a numeric sequence to a revision letter code (1→A, 2→B, ..., 26→Z, 27→AA).
        /// Follows standard ISO 19650 / BS 1192 revision numbering conventions.
        /// </summary>
        private static string SequenceToRevCode(int seq)
        {
            if (seq <= 0) return "P01"; // Pre-revision = Planning issue 01
            if (seq <= 26) return ((char)('A' + seq - 1)).ToString();
            // Beyond Z: AA, AB, AC...
            int first = (seq - 1) / 26;
            int second = (seq - 1) % 26;
            return ((char)('A' + first - 1)).ToString() + ((char)('A' + second)).ToString();
        }

        /// <summary>
        /// Build a phase-index mapping for efficient batch operations.
        /// Maps each phase name to its ordinal position in the project's phase sequence.
        /// Returns a dictionary of phase ElementId to phase ordinal (0-based).
        /// </summary>
        public static Dictionary<ElementId, int> BuildPhaseIndex(Document doc)
        {
            var index = new Dictionary<ElementId, int>();
            try
            {
                int ordinal = 0;
                foreach (Phase phase in new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .OrderBy(p => p.Id.Value))
                {
                    index[phase.Id] = ordinal++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildPhaseIndex: {ex.Message}");
            }
            return index;
        }
    }

    /// <summary>
    /// Shared token auto-population logic used by all tagging commands.
    /// Eliminates code duplication across AutoTag, BatchTag, TagNewOnly,
    /// TagAndCombine, FullAutoPopulate, and BulkParamWrite.
    ///
    /// Populates all 9 tokens: DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV.
    /// Each token uses the highest-intelligence detection available:
    ///   - DISC: category-based with system-aware correction for pipes
    ///   - LOC: spatial auto-detect (room → project info → workset)
    ///   - ZONE: spatial auto-detect (room department → room name → workset)
    ///   - LVL: deterministic from element level
    ///   - SYS: 6-layer MEP system-aware detection
    ///   - FUNC: smart subsystem differentiation (SUP/RTN/EXH, HTG/DHW)
    ///   - PROD: family-aware (35+ specific codes)
    ///   - STATUS: phase-aware (4-layer: demolished → phase name → workset → ordinal)
    ///   - REV: project revision sequence
    /// </summary>
    public static class TokenAutoPopulator
    {
        /// <summary>
        /// Pre-built context for batch operations. Build once, reuse for all elements.
        /// Wraps all the indexes and project-level values needed for token population.
        /// </summary>
        public class PopulationContext
        {
            public Dictionary<ElementId, Room> RoomIndex { get; set; }
            public string ProjectLoc { get; set; }
            public string ProjectRev { get; set; }
            public HashSet<string> KnownCategories { get; set; }
            /// <summary>Cached phase list for DetectStatus — avoids per-element FilteredElementCollector.</summary>
            public List<Phase> CachedPhases { get; set; }
            /// <summary>Last phase ID in the project sequence — used for EXISTING inference.</summary>
            public ElementId LastPhaseId { get; set; }

            /// <summary>GAP-019: Configurable default STATUS (from project_config.json or "NEW").</summary>
            public string DefaultStatus { get; set; } = "NEW";

            /// <summary>GAP-019: Configurable default REV (from project_config.json or "P01").</summary>
            public string DefaultRev { get; set; } = "P01";

            /// <summary>
            /// Build a PopulationContext once for a batch operation.
            /// Caches all project-level lookups: room index, LOC, REV, phases.
            /// </summary>
            public static PopulationContext Build(Document doc)
            {
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();
                return new PopulationContext
                {
                    RoomIndex = SpatialAutoDetect.BuildRoomIndex(doc),
                    ProjectLoc = SpatialAutoDetect.DetectProjectLoc(doc),
                    ProjectRev = PhaseAutoDetect.DetectProjectRevision(doc),
                    KnownCategories = new HashSet<string>(TagConfig.DiscMap.Keys),
                    CachedPhases = phases,
                    LastPhaseId = phases.Count > 0 ? phases.Last().Id : ElementId.InvalidElementId,
                    // GAP-019: Apply config overrides for STATUS/REV defaults
                    DefaultStatus = !string.IsNullOrEmpty(TagConfig.StatusDefault) ? TagConfig.StatusDefault : "NEW",
                    DefaultRev = !string.IsNullOrEmpty(TagConfig.RevDefault) ? TagConfig.RevDefault : "P01",
                };
            }
        }

        /// <summary>
        /// Result of populating tokens on a single element.
        /// Provides granular counts for reporting.
        /// </summary>
        public class PopulationResult
        {
            public int TokensSet { get; set; }
            public bool LocDetected { get; set; }
            public bool ZoneDetected { get; set; }
            public bool StatusDetected { get; set; }
            public bool RevSet { get; set; }
            public bool FamilyProdUsed { get; set; }
        }

        /// <summary>
        /// Populate all 9 tokens on a single element using the highest-intelligence
        /// detection available. Only fills empty values (non-destructive) unless
        /// overwrite is true.
        /// </summary>
        public static PopulationResult PopulateAll(Document doc, Element el,
            PopulationContext ctx, bool overwrite = false)
        {
            var result = new PopulationResult();

            // GAP-026: Skip ElementType instances — only populate element instances
            if (el is ElementType)
                return result;

            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName) || !ctx.KnownCategories.Contains(catName))
                return result;

            // DISC — deterministic from category (default "A" for unmapped categories)
            string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";

            // SYS — 6-layer MEP system-aware detection (must come before DISC correction)
            string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
            // Guaranteed SYS default: derive from discipline when MEP detection returns empty
            if (string.IsNullOrEmpty(sys))
                sys = TagConfig.GetDiscDefaultSysCode(disc);

            // DISC correction — system-aware override for pipes
            disc = TagConfig.GetSystemAwareDisc(disc, sys, catName);

            if (overwrite)
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.DISC, disc, overwrite: true)) result.TokensSet++;
            }
            else
            {
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc)) result.TokensSet++;
            }

            // LOC — from spatial context (room → project info → workset)
            // Guaranteed default: "BLD1" when detection returns empty
            string existingLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
            if (string.IsNullOrEmpty(existingLoc) || overwrite)
            {
                string loc = SpatialAutoDetect.DetectLoc(doc, el, ctx.RoomIndex, ctx.ProjectLoc);
                bool locFromSpatial = !string.IsNullOrEmpty(loc) && loc != "BLD1";
                if (string.IsNullOrEmpty(loc)) loc = "BLD1";
                if (overwrite)
                {
                    if (ParameterHelpers.SetString(el, ParamRegistry.LOC, loc, overwrite: true)) result.TokensSet++;
                }
                else
                {
                    if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, loc)) result.TokensSet++;
                }
                result.LocDetected = locFromSpatial;
            }

            // ZONE — from room data (department → name → workset)
            // Guaranteed default: "Z01" when detection returns empty
            string existingZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            if (string.IsNullOrEmpty(existingZone) || overwrite)
            {
                string zone = SpatialAutoDetect.DetectZone(doc, el, ctx.RoomIndex);
                bool zoneFromSpatial = !string.IsNullOrEmpty(zone) && zone != "Z01";
                if (string.IsNullOrEmpty(zone)) zone = "Z01";
                if (overwrite)
                {
                    if (ParameterHelpers.SetString(el, ParamRegistry.ZONE, zone, overwrite: true)) result.TokensSet++;
                }
                else
                {
                    if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, zone)) result.TokensSet++;
                }
                result.ZoneDetected = zoneFromSpatial;
            }

            // LVL — deterministic from element level
            // Guaranteed default: replace unresolved "XX" with "L00" for levelless elements
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            if (lvl == "XX") lvl = "L00";
            if (overwrite)
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.LVL, lvl, overwrite: true)) result.TokensSet++;
            }
            else
            {
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl)) result.TokensSet++;
            }

            // SYS — always write a guaranteed value (never empty)
            if (overwrite)
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.SYS, sys, overwrite: true)) result.TokensSet++;
            }
            else
            {
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys)) result.TokensSet++;
            }

            // FUNC — smart subsystem differentiation (SUP/RTN/EXH/FRA, HTG/DHW)
            // Guaranteed default: derive from SYS via FuncMap when smart detection is empty
            string func = TagConfig.GetSmartFuncCode(el, sys);
            if (string.IsNullOrEmpty(func))
                func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
            if (overwrite)
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.FUNC, func, overwrite: true)) result.TokensSet++;
            }
            else
            {
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func)) result.TokensSet++;
            }

            // PROD — family-aware (35+ specific codes)
            string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
            string catProd = TagConfig.ProdMap.TryGetValue(catName, out string cp) ? cp : "GEN";
            if (prod != catProd) result.FamilyProdUsed = true;
            if (overwrite)
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.PROD, prod, overwrite: true)) result.TokensSet++;
            }
            else
            {
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod)) result.TokensSet++;
            }

            // STATUS — phase-aware (4-layer detection using cached phases for batch perf)
            string existingStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
            if (string.IsNullOrEmpty(existingStatus) || overwrite)
            {
                // Use cached phase data when available (batch), fall back to uncached (single element)
                string status = (ctx.CachedPhases != null)
                    ? PhaseAutoDetect.DetectStatusCached(doc, el, ctx.CachedPhases, ctx.LastPhaseId)
                    : PhaseAutoDetect.DetectStatus(doc, el);
                if (string.IsNullOrEmpty(status)) status = ctx.DefaultStatus;
                if (overwrite)
                {
                    if (ParameterHelpers.SetString(el, ParamRegistry.STATUS, status, overwrite: true))
                    {
                        result.TokensSet++;
                        result.StatusDetected = true;
                    }
                }
                else
                {
                    if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, status))
                    {
                        result.TokensSet++;
                        result.StatusDetected = true;
                    }
                }
            }

            // REV — from project revision sequence
            // Guaranteed default: "P01" when no project revisions exist
            {
                string rev = !string.IsNullOrEmpty(ctx.ProjectRev) ? ctx.ProjectRev : ctx.DefaultRev;
                if (overwrite)
                {
                    if (ParameterHelpers.SetString(el, ParamRegistry.REV, rev, overwrite: true))
                    {
                        result.TokensSet++;
                        result.RevSet = true;
                    }
                }
                else
                {
                    if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.REV, rev))
                    {
                        result.TokensSet++;
                        result.RevSet = true;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Populate only the core 7 tag tokens (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD)
        /// without STATUS and REV. Used when only tag-building tokens are needed.
        /// </summary>
        public static int PopulateTagTokens(Document doc, Element el,
            PopulationContext ctx)
        {
            int count = 0;
            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName) || !ctx.KnownCategories.Contains(catName))
                return count;

            string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";
            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc)) count++;

            if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.LOC)))
            {
                string loc = SpatialAutoDetect.DetectLoc(doc, el, ctx.RoomIndex, ctx.ProjectLoc);
                if (string.IsNullOrEmpty(loc)) loc = "BLD1";
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, loc)) count++;
            }

            if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.ZONE)))
            {
                string zone = SpatialAutoDetect.DetectZone(doc, el, ctx.RoomIndex);
                if (string.IsNullOrEmpty(zone)) zone = "Z01";
                if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, zone)) count++;
            }

            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            if (lvl == "XX") lvl = "L00";
            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl)) count++;

            string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
            if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys)) count++;

            string func = TagConfig.GetSmartFuncCode(el, sys);
            if (string.IsNullOrEmpty(func)) func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func)) count++;

            string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod)) count++;

            return count;
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
            written += MapDefaults(doc, el);

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
        private static int MapDefaults(Document doc, Element el)
        {
            int written = 0;

            // STATUS: auto-detect from Revit phase/workset, fallback to "NEW"
            string status = PhaseAutoDetect.DetectStatus(doc, el);
            if (string.IsNullOrEmpty(status)) status = "NEW";
            written += SetIfEmptyInt(el, ParamRegistry.STATUS, status);

            // REV: auto-detect from project revision sequence
            string rev = PhaseAutoDetect.DetectProjectRevision(doc);
            if (!string.IsNullOrEmpty(rev))
                written += SetIfEmptyInt(el, ParamRegistry.REV, rev);

            // ORIGIN: set from project originator field if available
            try
            {
                string origin = doc.ProjectInformation?.OrganizationName;
                if (!string.IsNullOrEmpty(origin))
                    written += SetIfEmptyInt(el, ParamRegistry.ORIGIN, origin);
            }
            catch { }

            // PROJECT: set from project name if available
            try
            {
                string projName = doc.ProjectInformation?.Name;
                if (!string.IsNullOrEmpty(projName))
                    written += SetIfEmptyInt(el, ParamRegistry.PROJECT, projName);
            }
            catch { }
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

        /// <summary>Find the solid fill pattern element in the document. Cached per document.</summary>
        public static FillPatternElement GetSolidFillPattern(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
        }
    }
}
