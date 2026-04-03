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
    /// Safe command execution context — null-checked UIApplication, UIDocument,
    /// Document, and ActiveView. Use <see cref="ParameterHelpers.GetContext"/> to obtain.
    /// </summary>
    public class StingCommandContext
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
        public static StingCommandContext GetContext(ExternalCommandData commandData)
        {
            var app = GetApp(commandData);
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return null;
            var doc = uidoc.Document;
            if (doc == null) return null;
            View activeView = null;
            try { activeView = doc.ActiveView; } catch (Exception ex) { StingLog.Warn($"GetContext: no active view: {ex.Message}"); }
            return new StingCommandContext { App = app, UIDoc = uidoc, Doc = doc, ActiveView = activeView };
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

        /// <summary>
        /// Safe document accessor. Returns the active Document or null if no document is open.
        /// Use instead of chained dereferences like GetApp(cmd).ActiveUIDocument.Document
        /// which crash when ActiveUIDocument is null (no document open / family editor).
        /// </summary>
        public static Document GetDoc(ExternalCommandData commandData)
        {
            return GetApp(commandData)?.ActiveUIDocument?.Document;
        }

        /// <summary>
        /// Safe UIDocument accessor. Returns the active UIDocument or null if no document is open.
        /// </summary>
        public static UIDocument GetUIDoc(ExternalCommandData commandData)
        {
            return GetApp(commandData)?.ActiveUIDocument;
        }

        // Parameter lookup cache: avoids O(n) LookupParameter on every call.
        // BUG-05: Keyed by (int docHash, ElementId typeId, string paramName) → Definition.
        // docHash prevents cross-document cache collisions since ElementIds are document-relative.
        // Null values are cached to avoid repeated miss lookups.
        // PERF-05: Use stable document key (PathName/Title) instead of GetHashCode() which
        // can change across Revit sessions for the same document.
        private static readonly ConcurrentDictionary<(string, ElementId, string), Definition> _paramCache
            = new ConcurrentDictionary<(string, ElementId, string), Definition>();

        /// <summary>Invalidate all session-level caches (formulas, grid lines) in TagPipelineHelper.
        /// Forwarding method for callers that reference ParameterHelpers.</summary>
        public static void InvalidateSessionCaches()
        {
            TagPipelineHelper.InvalidateSessionCaches();
        }

        /// <summary>Clear the parameter lookup cache and solid fill cache. Call on document
        /// close or when shared parameters change (e.g., after LoadSharedParams).</summary>
        public static void ClearParamCache()
        {
            _paramCache.Clear();
            // CACHE-01: Also clear solid fill pattern cache to prevent stale entries
            // when switching between documents with different fill patterns.
            lock (_solidFillCache) { _solidFillCache.Clear(); }
            // PERF-03: Clear BIP availability cache on document switch
            NativeParamMapper.InvalidateBipCache();
            // PERF-CRIT-01: Clear spatial candidate cache
            TokenAutoPopulator.InvalidateSpatialCache();
            // PERF: Clear cached last phase on document switch
            _lastPhaseDocKey = null;
            _lastPhaseCache = null;
            // Phase 79b: Reset read-only skip counter on document switch
            // to prevent stale counts from previous document leaking through
            _readOnlySkipCount = 0;
        }

        /// <summary>PERF-05: Get a stable document key that survives Revit sessions.</summary>
        internal static string GetStableDocKey(Document doc)
        {
            return doc.PathName ?? doc.Title ?? "Untitled";
        }

        /// <summary>Cached parameter lookup. Uses stable document key + element's TypeId + paramName as cache key.
        /// Falls back to LookupParameter on first access per type, then O(1) thereafter.</summary>
        private static Parameter CachedLookup(Element el, string paramName)
        {
            string docKey = GetStableDocKey(el.Document);
            ElementId typeId = el.GetTypeId();
            var key = (docKey, typeId, paramName);

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

        /// <summary>Read an integer parameter with fallback. Handles Integer, Double, String storage.</summary>
        public static int GetInt(Element el, string paramName, int defaultValue = 0)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return defaultValue;
            Parameter p = CachedLookup(el, paramName);
            if (p == null) return defaultValue;
            switch (p.StorageType)
            {
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return (int)p.AsDouble();
                case StorageType.String:
                    string s = p.AsString();
                    return int.TryParse(s, out int v) ? v : defaultValue;
                default: return defaultValue;
            }
        }

        /// <summary>Set an INTEGER parameter. Skips read-only params. Skips non-zero unless overwrite.</summary>
        public static bool SetInt(Element el, string paramName, int value, bool overwrite = false)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            Parameter p = CachedLookup(el, paramName);
            if (p == null || p.IsReadOnly) return false;
            if (p.StorageType == StorageType.Integer)
            {
                if (!overwrite && p.AsInteger() != 0) return false;
                try { p.Set(value); return true; }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
            }
            if (p.StorageType == StorageType.String)
            {
                string existing = p.AsString() ?? string.Empty;
                if (!overwrite && existing.Length > 0) return false;
                try { p.Set(value.ToString()); return true; }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
            }
            return false;
        }

        /// <summary>Tracks cumulative read-only skip count for batch diagnostics (ERR-002).</summary>
        [ThreadStatic] private static int _readOnlySkipCount;
        /// <summary>Reset read-only skip counter at start of batch operation.</summary>
        public static void ResetReadOnlySkipCount() => _readOnlySkipCount = 0;
        /// <summary>Get cumulative read-only skip count since last reset.</summary>
        public static int ReadOnlySkipCount => _readOnlySkipCount;

        /// <summary>Set a TEXT parameter. Skips read-only params. Skips non-empty unless overwrite.</summary>
        public static bool SetString(Element el, string paramName, string value,
            bool overwrite = false)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            Parameter p = CachedLookup(el, paramName);
            if (p == null) return false;
            if (p.IsReadOnly)
            {
                // ERR-002: Diagnostic logging for read-only parameter skips
                _readOnlySkipCount++;
                if (_readOnlySkipCount <= 5 || _readOnlySkipCount % 100 == 0)
                    StingLog.Warn($"SetString '{paramName}' on {el.Id}: parameter is read-only (skip #{_readOnlySkipCount})");
                return false;
            }
            if (p.StorageType != StorageType.String)
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

        /// <summary>
        /// Set a Yes/No (integer) parameter. Works with YESNO StorageType.
        /// Also handles string-stored BOOL params for compatibility.
        /// </summary>
        public static bool SetYesNo(Element el, string paramName, bool value, bool overwrite = false)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            Parameter p = CachedLookup(el, paramName);
            if (p == null || p.IsReadOnly) return false;

            try
            {
                if (p.StorageType == StorageType.Integer)
                {
                    int existing = p.AsInteger();
                    if (existing != 0 && !overwrite) return false;
                    p.Set(value ? 1 : 0);
                    return true;
                }
                if (p.StorageType == StorageType.String)
                {
                    string existing = p.AsString() ?? "";
                    if (existing.Length > 0 && !overwrite) return false;
                    p.Set(value ? "Yes" : "No");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetYesNo '{paramName}' on {el.Id} failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set an integer parameter unconditionally (ignores existing value).
        /// Prefer <see cref="SetInt(Element,string,int,bool)"/> when overwrite control is needed.
        /// </summary>
        public static bool SetIntForce(Element el, string paramName, int value)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return false;
            Parameter p = CachedLookup(el, paramName);
            if (p == null || p.IsReadOnly) return false;
            try
            {
                if (p.StorageType == StorageType.Integer) { p.Set(value); return true; }
                if (p.StorageType == StorageType.Double) { p.Set((double)value); return true; }
                if (p.StorageType == StorageType.String) { p.Set(value.ToString()); return true; }
            }
            catch (Exception ex) { StingLog.Warn($"SetInt({paramName}): {ex.Message}"); }
            return false;
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
        /// Find the solid fill pattern element in the document.
        /// Used by color override commands and template configuration.
        /// </summary>
        /// <summary>PERF-04: Cached solid fill pattern lookup to avoid per-call FilteredElementCollector.</summary>
        private static readonly Dictionary<string, FillPatternElement> _solidFillCache = new Dictionary<string, FillPatternElement>();
        public static FillPatternElement GetSolidFillPattern(Document doc)
        {
            string key = doc.PathName ?? doc.Title ?? "Untitled";
            lock (_solidFillCache)
            {
                if (_solidFillCache.TryGetValue(key, out var cached))
                {
                    // Validate the cached element is still valid
                    if (cached != null && cached.IsValidObject) return cached;
                    _solidFillCache.Remove(key);
                }
            }
            var result = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            lock (_solidFillCache) { _solidFillCache[key] = result; }
            return result;
        }

        /// <summary>LG-02: Get the element's creation phase for phase-aware room lookup.</summary>
        // PERF: Cache last phase per document to avoid FilteredElementCollector per element in fallback
        private static string _lastPhaseDocKey;
        private static Phase _lastPhaseCache;

        private static Phase GetElementPhase(Document doc, Element el)
        {
            try
            {
                var phaseParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (phaseParam != null)
                {
                    var phaseEl = doc.GetElement(phaseParam.AsElementId()) as Phase;
                    if (phaseEl != null) return phaseEl;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetElementPhase for {el?.Id}: {ex.Message}"); }

            // Fallback: last phase in the project (cached per document)
            try
            {
                string docKey = GetStableDocKey(doc);
                if (_lastPhaseCache != null && _lastPhaseDocKey == docKey && _lastPhaseCache.IsValidObject)
                    return _lastPhaseCache;

                var phase = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .OrderBy(p => p.Id.Value)
                    .LastOrDefault();
                _lastPhaseDocKey = docKey;
                _lastPhaseCache = phase;
                return phase;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
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

                // LG-02: Phase-aware room lookup — use element's creation phase
                Phase elPhase = GetElementPhase(doc, el);

                // Fall back to location-based lookup
                LocationPoint lp = el.Location as LocationPoint;
                if (lp != null)
                {
                    return elPhase != null ? doc.GetRoomAtPoint(lp.Point, elPhase) : doc.GetRoomAtPoint(lp.Point);
                }

                // For curve-based elements (pipes, ducts), use midpoint
                LocationCurve lc = el.Location as LocationCurve;
                if (lc != null)
                {
                    XYZ mid = lc.Curve.Evaluate(0.5, true);
                    return elPhase != null ? doc.GetRoomAtPoint(mid, elPhase) : doc.GetRoomAtPoint(mid);
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
            // Phase 56: Log once when no rooms found so BIM coordinators know LOC detection
            // will fall back to project-level defaults instead of room-based spatial detection
            if (index.Count == 0)
                StingLog.Info("BuildRoomIndex: no placed rooms found — spatial detection (LOC/ZONE) will use project-level defaults");
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

        /// <summary>P5: Return grid reference string ("A/3") for nearest X/Y grids to element location.</summary>
        public static string GetGridRef(Element el, List<Grid> grids)
        {
            try
            {
                XYZ point = null;
                if (el.Location is LocationPoint lp) point = lp.Point;
                else if (el.Location is LocationCurve lc)
                    point = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
                if (point == null)
                {
                    BoundingBoxXYZ bb = el.get_BoundingBox(null);
                    if (bb != null) point = (bb.Min + bb.Max) / 2.0;
                }
                if (point == null) return null;

                string nearX = null, nearY = null;
                double minX = double.MaxValue, minY = double.MaxValue;
                foreach (Grid g in grids)
                {
                    try
                    {
                        Curve c = g.Curve;
                        XYZ dir = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize();
                        bool isHoriz = Math.Abs(dir.X) > Math.Abs(dir.Y);
                        double dist = c.Distance(point);
                        if (isHoriz && dist < minY) { minY = dist; nearY = g.Name; }
                        else if (!isHoriz && dist < minX) { minX = dist; nearX = g.Name; }
                    }
                    catch (Exception ex) { StingLog.Warn($"Grid distance calculation failed for grid '{g.Name}': {ex.Message}"); }
                }
                if (nearX != null && nearY != null) return $"{nearX}/{nearY}";
                return nearX ?? nearY;
            }
            catch (Exception ex) { StingLog.Warn($"Grid reference detection failed: {ex.Message}"); return null; }
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

            // Phase 86b: Exclude "TEMPLATE" false positive — StartsWith("TEMP") matched "TEMPLATE" worksets
            if ((text.StartsWith("TEMP") && !text.StartsWith("TEMPLATE")) ||
                (text.Contains("_TEMP") && !text.Contains("_TEMPLATE")) ||
                (text.Contains("-TEMP") && !text.Contains("-TEMPLATE")) ||
                text.Contains(" TEMPORARY"))
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
            if (doc == null) return null;
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
        // F-10: Static readonly token arrays for CopyTokensFromNearest — avoid per-element List+ToArray allocation
        private static readonly string[] _spatialLocOnly  = { ParamRegistry.LOC };
        private static readonly string[] _spatialZoneOnly = { ParamRegistry.ZONE };
        private static readonly string[] _spatialLocZone  = { ParamRegistry.LOC, ParamRegistry.ZONE };
        private static readonly string[] _proxSysOnly     = { ParamRegistry.SYS };
        private static readonly string[] _proxFuncOnly    = { ParamRegistry.FUNC };
        private static readonly string[] _proxSysFunc     = { ParamRegistry.SYS, ParamRegistry.FUNC };

        /// <summary>
        /// FIX-B05: MEP categories that benefit from connector traversal for SYS detection.
        /// Non-MEP categories (Walls, Doors, Furniture, etc.) skip the expensive MEP detection.
        /// </summary>
        private static readonly HashSet<string> _mepConnectorCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mechanical Equipment", "Ducts", "Duct Fittings", "Duct Accessories", "Duct Insulations", "Duct Linings",
            "Flex Ducts", "Air Terminals",
            "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulations", "Flex Pipes",
            "Plumbing Fixtures", "Sprinklers",
            "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures", "Lighting Devices",
            "Communication Devices", "Data Devices", "Fire Alarm Devices", "Nurse Call Devices", "Security Devices",
            "Cable Trays", "Cable Tray Fittings", "Conduits", "Conduit Fittings",
        };

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

            /// <summary>Cached grid lines for O(1) WriteGridReference instead of per-element O(n) collector.</summary>
            public List<Grid> CachedGrids { get; set; }

            /// <summary>GAP-019: Configurable default STATUS (from project_config.json or "NEW").</summary>
            public string DefaultStatus { get; set; } = "NEW";

            /// <summary>GAP-019: Configurable default REV (from project_config.json or "P01").</summary>
            public string DefaultRev { get; set; } = "P01";

            /// <summary>Phase 39: Validate that the context has all required data for reliable token population.
            /// Returns true if all critical fields are initialized. Use after Build() to catch partial init
            /// on corrupted documents (missing levels, rooms, phases, etc.).</summary>
            public bool IsValid()
            {
                // RoomIndex can be empty (no rooms placed yet) but must not be null
                if (RoomIndex == null) return false;
                if (KnownCategories == null || KnownCategories.Count == 0) return false;
                if (CachedPhases == null) return false;
                // ProjectLoc may be null/empty (no Project Info set) — acceptable
                // ProjectRev may be null/empty (no revisions defined) — acceptable
                return true;
            }

            /// <summary>Phase 39: Summary of context health for diagnostics.</summary>
            public string DiagnosticSummary =>
                $"Rooms={RoomIndex?.Count ?? 0}, Categories={KnownCategories?.Count ?? 0}, " +
                $"Phases={CachedPhases?.Count ?? 0}, Grids={CachedGrids?.Count ?? 0}, " +
                $"LOC={ProjectLoc ?? "null"}, REV={ProjectRev ?? "null"}";

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
                var grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .ToList();
                // PERF-CRIT-01: Build spatial candidate cache for CopyTokensFromNearest
                TokenAutoPopulator.BuildSpatialCandidateCache(doc);

                return new PopulationContext
                {
                    RoomIndex = SpatialAutoDetect.BuildRoomIndex(doc),
                    ProjectLoc = SpatialAutoDetect.DetectProjectLoc(doc),
                    ProjectRev = PhaseAutoDetect.DetectProjectRevision(doc),
                    KnownCategories = new HashSet<string>(TagConfig.DiscMap.Keys),
                    CachedPhases = phases,
                    LastPhaseId = phases.Count > 0 ? phases.Last().Id : ElementId.InvalidElementId,
                    CachedGrids = grids,
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
        /// P4: Inherit token values from the element's family type (ElementType) to
        /// the instance. If the type already has DISC/SYS/FUNC/PROD set (e.g. via
        /// family editor or BatchAddFamilyParams), copy those values to the instance
        /// only when the instance parameter is empty.
        /// </summary>
        public static void TypeTokenInherit(Document doc, Element el)
        {
            try
            {
                if (el is ElementType) return;
                ElementId typeId = el.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return;
                Element elType = doc.GetElement(typeId);
                if (elType == null) return;

                // Copy type-level tokens to instance (non-destructive)
                string[] tokenParams = { ParamRegistry.DISC, ParamRegistry.SYS,
                    ParamRegistry.FUNC, ParamRegistry.PROD };
                foreach (string paramName in tokenParams)
                {
                    string typeVal = ParameterHelpers.GetString(elType, paramName);
                    if (!string.IsNullOrEmpty(typeVal))
                        ParameterHelpers.SetIfEmpty(el, paramName, typeVal);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TypeTokenInherit: {ex.Message}");
            }
        }

        /// <summary>
        /// Populate all 9 tokens on a single element using the highest-intelligence
        /// detection available. Only fills empty values (non-destructive) unless
        /// overwrite is true.
        /// </summary>
        /// <summary>
        /// ENH-01: Inherit token values from connected MEP elements via connectors.
        /// Walks the connector graph one hop to find already-tagged connected elements
        /// and copies SYS, FUNC, and DISC tokens to this element if empty.
        /// Only operates on FamilyInstance elements with MEP connectors.
        /// </summary>
        public static void ConnectorInherit(Document doc, Element el)
        {
            if (el == null) return;
            try
            {
                FamilyInstance fi = el as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager == null) return;

                string[] tokensToCopy = { ParamRegistry.DISC, ParamRegistry.SYS,
                    ParamRegistry.FUNC, ParamRegistry.LOC, ParamRegistry.ZONE };

                // Check if element already has all tokens populated
                bool allPopulated = true;
                foreach (string t in tokensToCopy)
                {
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, t)))
                    { allPopulated = false; break; }
                }
                if (allPopulated) return;

                // Walk connectors to find tagged connected elements
                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn == null || !conn.IsConnected) continue;
                    var allRefs = conn.AllRefs;
                    if (allRefs == null) continue;
                    foreach (Connector otherConn in allRefs)
                    {
                        if (otherConn?.Owner == null || otherConn.Owner.Id == el.Id) continue;
                        Element connected = otherConn.Owner;

                        string connTag = ParameterHelpers.GetString(connected, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(connTag)) continue;

                        // Found a tagged connected element — copy empty tokens
                        int copied = 0;
                        foreach (string param in tokensToCopy)
                        {
                            string val = ParameterHelpers.GetString(connected, param);
                            if (!string.IsNullOrEmpty(val))
                            {
                                if (ParameterHelpers.SetIfEmpty(el, param, val))
                                    copied++;
                            }
                        }

                        // AE-04: Write connector inherit status for diagnostics
                        if (copied > 0)
                        {
                            string sourceTag = connTag.Length > 30 ? connTag.Substring(0, 30) : connTag;
                            ParameterHelpers.SetIfEmpty(el, "STING_TOKEN_COPY_SOURCE",
                                $"ConnectorInherit:{connected.Id.Value}:{sourceTag}:{copied}tok");
                        }

                        // Phase 56b CRIT-02 FIX: Check if ALL tokens now populated before returning
                        // Previously returned after first tagged element even if some tokens still empty
                        bool nowComplete = true;
                        foreach (string t in tokensToCopy)
                        {
                            if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, t)))
                            { nowComplete = false; break; }
                        }
                        if (nowComplete) return; // All tokens filled — done
                        // Else: continue scanning other connectors for remaining empty tokens
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConnectorInherit: {ex.Message}");
            }
        }

        public static PopulationResult PopulateAll(Document doc, Element el,
            PopulationContext ctx, bool overwrite = false)
        {
            var result = new PopulationResult();

            // GAP-026: Skip ElementType instances — only populate element instances
            if (el is ElementType)
                return result;

            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName) || !ctx.KnownCategories.Contains(catName))
            {
                // Phase 79b: Log once per unknown category to aid debugging
                // (e.g., custom families placed in unexpected categories)
                if (!string.IsNullOrEmpty(catName))
                    StingLog.Info($"PopulateAll: skipping unknown category '{catName}' for element {el.Id}");
                return result;
            }

            // FIX-B05: Only run MEP connector traversal for MEP categories
            bool isMepCategory = _mepConnectorCategories.Contains(catName);

            // ENH-01: Inherit tokens from connected MEP elements before population
            // so that PopulateAll's SetIfEmpty calls won't overwrite inherited values
            // PERF-04: Early-exit for elements with zero connectors to avoid expensive traversal
            if (isMepCategory)
            {
                try
                {
                    var fi = el as FamilyInstance;
                    if (fi?.MEPModel?.ConnectorManager?.Connectors?.Size > 0)
                        ConnectorInherit(doc, el);
                }
                catch (Exception ex) { StingLog.Warn($"ConnectorInherit check: {ex.Message}"); }
            }

            // DISC — deterministic from category (default "A" for unmapped categories)
            string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";

            // SYS — 6-layer MEP system-aware detection (must come before DISC correction)
            // LOG-01: Track which detection layer produced the SYS code
            // FIX-B05: Skip expensive MEP connector traversal for non-MEP categories
            string sys;
            int sysLayer;
            if (isMepCategory)
            {
                (sys, sysLayer) = TagConfig.GetMepSystemAwareSysCodeWithLayer(el, catName);
            }
            else
            {
                // Non-MEP: use category fallback directly (layers 5-6 in GetMepSystemAwareSysCodeWithLayer)
                sys = TagConfig.GetSysCode(catName);
                sysLayer = 6; // category fallback
            }
            // Guaranteed SYS default: derive from discipline when MEP detection returns empty
            if (string.IsNullOrEmpty(sys))
            {
                sys = TagConfig.GetDiscDefaultSysCode(disc);
                sysLayer = 7; // layer 7 = discipline default fallback
            }

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

            // Phase 19: Type-level LOC/ZONE overrides — check before spatial detection
            string typeLocOverride = null;
            string typeZoneOverride = null;
            ElementId popTypeId = el.GetTypeId();
            if (popTypeId != null && popTypeId != ElementId.InvalidElementId)
            {
                Element popTypeEl = doc.GetElement(popTypeId);
                if (popTypeEl != null)
                {
                    typeLocOverride = ParameterHelpers.GetString(popTypeEl, ParamRegistry.TYPE_LOC_OVERRIDE);
                    typeZoneOverride = ParameterHelpers.GetString(popTypeEl, ParamRegistry.TYPE_ZONE_OVERRIDE);
                }
            }

            // LOC — from type override → spatial context (room → project info → workset)
            // Guaranteed default: "BLD1" when detection returns empty
            string existingLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
            if (string.IsNullOrEmpty(existingLoc) || overwrite)
            {
                if (!string.IsNullOrEmpty(typeLocOverride))
                {
                    if (overwrite)
                        ParameterHelpers.SetString(el, ParamRegistry.LOC, typeLocOverride, overwrite: true);
                    else
                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, typeLocOverride);
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC_SOURCE, "TYPE_OVERRIDE");
                    result.LocDetected = true;
                    result.TokensSet++;
                }
                else
                {
                    string loc = SpatialAutoDetect.DetectLoc(doc, el, ctx.RoomIndex, ctx.ProjectLoc);
                    bool locFromSpatial = !string.IsNullOrEmpty(loc) && loc != "BLD1";

                    // Phase 67: LOC fallback chain — workset name extraction when spatial/project detection fails
                    // F-04: Hoist wsName to outer scope so the logging block can reuse it (avoids double GetWorkset call)
                    string _cachedWsName = null;
                    if (string.IsNullOrEmpty(loc) && doc.IsWorkshared)
                    {
                        try
                        {
                            var wsParam = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                            if (wsParam != null)
                            {
                                int wsId = wsParam.AsInteger();
                                if (wsId > 0)
                                {
                                    _cachedWsName = doc.GetWorksetTable().GetWorkset(new WorksetId(wsId))?.Name ?? "";
                                    // Extract LOC code from workset name (e.g., "BLD2_Mechanical" → "BLD2")
                                    foreach (string locCode in TagConfig.LocCodes ?? new List<string>())
                                    {
                                        if (_cachedWsName.StartsWith(locCode, StringComparison.OrdinalIgnoreCase) ||
                                            _cachedWsName.Contains("_" + locCode, StringComparison.OrdinalIgnoreCase) ||
                                            _cachedWsName.Contains(locCode + "_", StringComparison.OrdinalIgnoreCase))
                                        {
                                            loc = locCode;
                                            locFromSpatial = true; // workset-derived counts as detected
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception wsEx) { StingLog.Warn($"LOC workset fallback: {wsEx.Message}"); }
                    }

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

                    // LOG-01: Track LOC detection source (Phase 67: added Workset layer)
                    string locSource = locFromSpatial
                        ? (loc == ctx?.ProjectLoc ? "ProjectInfo" : "Room")
                        : (!string.IsNullOrEmpty(ctx?.ProjectLoc) ? "ProjectInfo" : "Default");
                    // Phase 67: Override source if workset-detected (was marked locFromSpatial=true above)
                    // F-04: Reuse _cachedWsName from detection block above — no second GetWorkset() call needed
                    if (locFromSpatial && doc.IsWorkshared)
                    {
                        try
                        {
                            // F-04: Use cached workset name; only fall back to a fresh lookup if cache is null
                            if (_cachedWsName == null)
                            {
                                var wsCheck = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                                if (wsCheck != null && wsCheck.AsInteger() > 0)
                                    _cachedWsName = doc.GetWorksetTable().GetWorkset(new WorksetId(wsCheck.AsInteger()))?.Name ?? "";
                            }
                            if (_cachedWsName != null && _cachedWsName.Contains(loc, StringComparison.OrdinalIgnoreCase))
                                locSource = "Workset";
                        }
                        catch (Exception ex) { StingLog.Warn($"PopulateAll LOC_SOURCE workset check: {ex.Message}"); }
                    }
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC_SOURCE, locSource);
                }
            }

            // ZONE — from type override → room data (department → name → workset)
            // Guaranteed default: "Z01" when detection returns empty
            string existingZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            if (string.IsNullOrEmpty(existingZone) || overwrite)
            {
                if (!string.IsNullOrEmpty(typeZoneOverride))
                {
                    if (overwrite)
                        ParameterHelpers.SetString(el, ParamRegistry.ZONE, typeZoneOverride, overwrite: true);
                    else
                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, typeZoneOverride);
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE_SOURCE, "TYPE_OVERRIDE");
                    result.ZoneDetected = true;
                    result.TokensSet++;
                }
                else
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

                    // LOG-01: Track ZONE detection source
                    string zoneSource = zoneFromSpatial ? "Room" : "Default";
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE_SOURCE, zoneSource);
                }
            }

            // Phase 68 (NEW-02): CopyTokensFromNearest for LOC/ZONE when spatial detection yields defaults
            if (!overwrite)
            {
                string curLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string curZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                bool locDefault = string.IsNullOrEmpty(curLoc) || curLoc == "XX" || curLoc == ctx?.ProjectLoc;
                bool zoneDefault = string.IsNullOrEmpty(curZone) || curZone == "Z01" || curZone == "ZZ";
                if (locDefault || zoneDefault)
                {
                    // F-10: Use pre-allocated static arrays instead of new List+ToArray per element
                    string[] spatialTokens = (locDefault && zoneDefault) ? _spatialLocZone
                                           : locDefault ? _spatialLocOnly : _spatialZoneOnly;
                    int spatialCopied = CopyTokensFromNearest(doc, el, spatialTokens);
                    result.TokensSet += spatialCopied;
                }
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

            // LOG-01: Write SYS detection layer (1-7) for confidence tracking
            // F-06: Use SetInt helper instead of manual LookupParameter + Set
            try { ParameterHelpers.SetInt(el, ParamRegistry.SYS_DETECT_LAYER, sysLayer); }
            catch (Exception ex) { StingLog.Warn($"advisory — parameter may not be bound yet: {ex.Message}"); }

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

            // HC-001: Proximity-based token copy for SYS/FUNC when detection yielded generic defaults
            // Uses configurable ProximityRadiusFt from project_config.json (default 10 ft)
            if (!overwrite)
            {
                string curSys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string curFunc = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
                bool sysGeneric = string.IsNullOrEmpty(curSys) || curSys == "GEN" || curSys == "ARC" || curSys == "STR";
                bool funcGeneric = string.IsNullOrEmpty(curFunc) || curFunc == "GEN";
                if (sysGeneric || funcGeneric)
                {
                    // F-10: Use pre-allocated static arrays instead of new List+ToArray per element
                    string[] tokensToInherit = (sysGeneric && funcGeneric) ? _proxSysFunc
                                             : sysGeneric ? _proxSysOnly : _proxFuncOnly;
                    int proxCopied = CopyTokensFromNearest(doc, el, tokensToInherit);
                    result.TokensSet += proxCopied;
                }
            }

            // Per-discipline profile defaults — apply after all detection to fill still-generic tokens
            {
                string curDisc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                var profile = TagConfig.GetDisciplineProfile(curDisc);
                if (profile != null)
                {
                    // Apply DefaultProd when PROD is still generic (GEN/XX)
                    if (!string.IsNullOrEmpty(profile.DefaultProd))
                    {
                        string curProd = ParameterHelpers.GetString(el, ParamRegistry.PROD);
                        if (string.IsNullOrEmpty(curProd) || curProd == "GEN" || curProd == "XX")
                        {
                            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, profile.DefaultProd))
                                result.TokensSet++;
                            else if (overwrite && (curProd == "GEN" || curProd == "XX"))
                            {
                                if (ParameterHelpers.SetString(el, ParamRegistry.PROD, profile.DefaultProd, overwrite: true))
                                    result.TokensSet++;
                            }
                        }
                    }

                    // Apply DefaultStatus when STATUS is still empty
                    if (!string.IsNullOrEmpty(profile.DefaultStatus))
                    {
                        string curStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                        if (string.IsNullOrEmpty(curStatus))
                        {
                            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, profile.DefaultStatus))
                                result.TokensSet++;
                        }
                    }
                }
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

            // Phase 19: Track level ElementId for stale detection on level changes
            try
            {
                ElementId levelId = el.LevelId;
                if (levelId != null && levelId != ElementId.InvalidElementId)
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL_ELEM_ID, levelId.Value.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"RunFullPipeline LevelId: {ex.Message}"); }

            // Phase 19: Write nearest grid intersection reference (uses cached grids for O(1) per element)
            WriteGridReference(doc, el, ctx?.CachedGrids);

            return result;
        }

        /// <summary>Phase 19: Write nearest grid intersection reference for element.
        /// Accepts pre-cached grids to avoid per-element FilteredElementCollector (O(n²) → O(n)).</summary>
        private static void WriteGridReference(Document doc, Element el, List<Grid> cachedGrids = null)
        {
            try
            {
                var loc = el.Location;
                XYZ point = null;
                if (loc is LocationPoint lp) point = lp.Point;
                else if (loc is LocationCurve lc) point = lc.Curve.Evaluate(0.5, true);
                if (point == null) return;

                var grids = cachedGrids ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .ToList();
                if (grids.Count == 0) return;

                // Find nearest X-direction and Y-direction grids
                Grid nearestX = null, nearestY = null;
                double minDistX = double.MaxValue, minDistY = double.MaxValue;
                foreach (var grid in grids)
                {
                    try
                    {
                        var curve = grid.Curve;
                        var dir = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                        double dist = curve.Distance(point);
                        // Roughly vertical grids (X-grids)
                        if (Math.Abs(dir.Y) > Math.Abs(dir.X))
                        {
                            if (dist < minDistX) { minDistX = dist; nearestX = grid; }
                        }
                        else // Roughly horizontal grids (Y-grids)
                        {
                            if (dist < minDistY) { minDistY = dist; nearestY = grid; }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"WriteGridReference grid '{grid?.Name}': {ex.Message}"); }
                }

                if (nearestX != null)
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_X_ID, nearestX.Name);
                if (nearestY != null)
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_Y_ID, nearestY.Name);

                double minDist = Math.Min(
                    nearestX != null ? minDistX : double.MaxValue,
                    nearestY != null ? minDistY : double.MaxValue);
                if (minDist < double.MaxValue)
                    ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_DIST,
                        (minDist * 304.8).ToString("F0")); // Convert ft to mm
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WriteGridReference: {ex.Message}");
            }
        }

        // PERF-CRIT-01: Spatial candidate cache — avoids O(n²) FilteredElementCollector per element.
        // Key: categoryId.Value. Value: list of (elementId, center point, tag1 value).
        // Built once per batch in PopulationContext, reused for all CopyTokensFromNearest calls.
        private static readonly Dictionary<long, List<(ElementId Id, XYZ Center, string Tag1)>>
            _spatialCandidateCache = new Dictionary<long, List<(ElementId, XYZ, string)>>();
        private static string _spatialCacheDocKey;

        /// <summary>Build spatial candidate cache for all taggable categories. Call once per batch.</summary>
        public static void BuildSpatialCandidateCache(Document doc)
        {
            string docKey = ParameterHelpers.GetStableDocKey(doc);
            if (_spatialCacheDocKey == docKey && _spatialCandidateCache.Count > 0) return;
            _spatialCandidateCache.Clear();
            _spatialCacheDocKey = docKey;
            try
            {
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums == null || catEnums.Length == 0) return;
                var coll = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                foreach (var e in coll)
                {
                    if (e?.Category == null) continue;
                    string tag1 = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue; // Only include already-tagged
                    XYZ center = null;
                    var loc = e.Location;
                    if (loc is LocationPoint lp) center = lp.Point;
                    else if (loc is LocationCurve lc) center = lc.Curve.Evaluate(0.5, true);
                    if (center == null) continue;
                    long catKey = e.Category.Id.Value;
                    if (!_spatialCandidateCache.TryGetValue(catKey, out var list))
                    {
                        list = new List<(ElementId, XYZ, string)>();
                        _spatialCandidateCache[catKey] = list;
                    }
                    list.Add((e.Id, center, tag1));
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildSpatialCandidateCache: {ex.Message}"); }
        }

        /// <summary>Invalidate spatial candidate cache (call after batch tagging completes).</summary>
        public static void InvalidateSpatialCache() { _spatialCandidateCache.Clear(); }

        /// <summary>Phase 79b: Throttle counter for CopyTokensFromNearest logging.</summary>
        [ThreadStatic] internal static int _copyTokensLogCount;

        /// <summary>
        /// Copies specified token values from the nearest already-tagged element of the same
        /// category within a configurable radius (TagConfig.ProximityRadiusFt, default 10 ft).
        /// Useful for inheriting SYS/FUNC from adjacent elements when MEP detection yields
        /// empty or generic defaults.
        /// PERF-CRIT-01: Uses pre-built spatial candidate cache instead of per-element collector.
        /// </summary>
        /// <param name="doc">The Revit document</param>
        /// <param name="el">Target element to copy tokens TO</param>
        /// <param name="tokensToCopy">Parameter names to copy (e.g., ParamRegistry.SYS, ParamRegistry.FUNC)</param>
        /// <param name="candidatePool">Pre-collected candidate elements (null = use spatial cache or collect from doc)</param>
        /// <returns>Number of tokens successfully copied</returns>
        public static int CopyTokensFromNearest(Document doc, Element el,
            string[] tokensToCopy, IList<Element> candidatePool = null)
        {
            if (el == null || tokensToCopy == null || tokensToCopy.Length == 0) return 0;

            try
            {
                // Get element location
                XYZ point = null;
                var loc = el.Location;
                if (loc is LocationPoint lp) point = lp.Point;
                else if (loc is LocationCurve lc) point = lc.Curve.Evaluate(0.5, true);
                if (point == null) return 0;

                string elCat = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(elCat)) return 0;

                double radiusFt = TagConfig.ProximityRadiusFt;

                // PERF-CRIT-01: Use pre-built spatial candidate cache for O(n) instead of O(n²).
                // Falls back to candidatePool or collector if cache is empty.
                Element nearest = null;
                double minDist = double.MaxValue;

                if (candidatePool != null)
                {
                    // Legacy path: use provided candidate pool
                    foreach (var candidate in candidatePool)
                    {
                        if (candidate == null || candidate.Id == el.Id) continue;
                        string tag1 = ParameterHelpers.GetString(candidate, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1)) continue;
                        XYZ cPoint = null;
                        var cLoc = candidate.Location;
                        if (cLoc is LocationPoint clp) cPoint = clp.Point;
                        else if (cLoc is LocationCurve clc) cPoint = clc.Curve.Evaluate(0.5, true);
                        if (cPoint == null) continue;
                        double dist = point.DistanceTo(cPoint);
                        if (dist < minDist && dist <= radiusFt) { minDist = dist; nearest = candidate; }
                    }
                }
                else
                {
                    // Fast path: use spatial candidate cache (pre-built, no collector needed)
                    // Phase 79b: Skip fast path for null-category elements (key 0 is a junk bucket)
                    long catKey = el.Category?.Id?.Value ?? 0;
                    if (catKey != 0 && _spatialCandidateCache.TryGetValue(catKey, out var cached) && cached.Count > 0)
                    {
                        ElementId nearestId = null;
                        foreach (var (cId, cCenter, cTag1) in cached)
                        {
                            if (cId == el.Id) continue;
                            // Cache already pre-filters to tagged elements (line 1910),
                            // but guard against stale cache with empty TAG1
                            if (string.IsNullOrEmpty(cTag1)) continue;
                            double dist = point.DistanceTo(cCenter);
                            if (dist < minDist && dist <= radiusFt) { minDist = dist; nearestId = cId; }
                        }
                        if (nearestId != null) nearest = doc.GetElement(nearestId);
                    }
                    else
                    {
                        // Fallback: collect from doc (cold cache scenario)
                        var catId = el.Category?.Id;
                        if (catId == null) return 0;
                        foreach (var candidate in new FilteredElementCollector(doc)
                            .OfCategoryId(catId).WhereElementIsNotElementType())
                        {
                            if (candidate.Id == el.Id) continue;
                            string tag1 = ParameterHelpers.GetString(candidate, ParamRegistry.TAG1);
                            if (string.IsNullOrEmpty(tag1)) continue;
                            XYZ cPoint = null;
                            var cLoc = candidate.Location;
                            if (cLoc is LocationPoint clp) cPoint = clp.Point;
                            else if (cLoc is LocationCurve clc) cPoint = clc.Curve.Evaluate(0.5, true);
                            if (cPoint == null) continue;
                            double dist = point.DistanceTo(cPoint);
                            if (dist < minDist && dist <= radiusFt) { minDist = dist; nearest = candidate; }
                        }
                    }
                }

                if (nearest == null) return 0;

                int copied = 0;
                foreach (string tokenName in tokensToCopy)
                {
                    string existingVal = ParameterHelpers.GetString(el, tokenName);
                    if (!string.IsNullOrEmpty(existingVal)) continue; // don't overwrite

                    string sourceVal = ParameterHelpers.GetString(nearest, tokenName);
                    if (string.IsNullOrEmpty(sourceVal)) continue;

                    if (ParameterHelpers.SetIfEmpty(el, tokenName, sourceVal))
                        copied++;
                }

                if (copied > 0)
                {
                    // Phase 79b: Throttle logging — log first 10 + every 100th to avoid
                    // 5K+ log lines on large batches
                    _copyTokensLogCount++;
                    if (_copyTokensLogCount <= 10 || _copyTokensLogCount % 100 == 0)
                        StingLog.Info($"CopyTokensFromNearest: Copied {copied} tokens from element {nearest.Id} to {el.Id} (distance: {minDist * 304.8:F0}mm)");
                }

                return copied;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CopyTokensFromNearest: {ex.Message}");
                return 0;
            }
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

            // ORF-02: Serial number mapping
            written += MapBuiltIn(el, BuiltInParameter.ALL_MODEL_MARK, "ASS_SERIAL_NR_TXT");

            // WARN-XS: Installation date from Phase Created
            try
            {
                Parameter phaseParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (phaseParam != null)
                {
                    ElementId phaseId = phaseParam.AsElementId();
                    if (phaseId != null && phaseId != ElementId.InvalidElementId)
                    {
                        Phase phase = doc.GetElement(phaseId) as Phase;
                        if (phase != null && !string.IsNullOrEmpty(phase.Name))
                        {
                            // LOGIC-04: Use phase name as installation context instead of DateTime.Now.
                            // DateTime.Now is incorrect for existing/demolished elements — they weren't
                            // installed today. Write phase name as a meaningful lifecycle marker instead.
                            string installContext = phase.Name;
                            // If phase looks like "New Construction" or "Existing", record it;
                            // only write today's date for elements in a construction phase
                            if (phase.Name.IndexOf("New", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                phase.Name.IndexOf("Construction", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // D-01: Use UtcNow for timestamp consistency across codebase
                                installContext = DateTime.UtcNow.ToString("yyyy-MM-dd");
                            }
                            ParameterHelpers.SetIfEmpty(el, "ASS_INSTALLATION_DATE_TXT", installContext);
                            written++;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Phase detection is advisory: {ex.Message}"); }

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
                catch (Exception ex) { StingLog.Warn($"Room department mapping failed: {ex.Message}"); }
            }

            // ── Dimensional parameters (BLE_ schedule fields) ──────────────────
            written += MapDimensionalParams(el);

            // ── MEP-specific parameters ────────────────────────────────────────
            written += MapMepParams(el);

            // ── WARN-XS: Warning-activation dimensional parameter mappings ────
            string catNameW = el.Category?.Name ?? "";
            string catUpperW = catNameW.ToUpperInvariant();
            const double ftToMmW = 304.8;

            // ASS_ROOM_HEIGHT_MM — Room upper offset
            if (catNameW == "Rooms")
                written += MapDimension(el, BuiltInParameter.ROOM_UPPER_OFFSET, "ASS_ROOM_HEIGHT_MM", ftToMmW);

            // BLE_STAIR_HEADROOM_MM — Stair headroom
            // Phase 87: Removed incorrect mapping. STAIRS_ACTUAL_TREAD_DEPTH is horizontal step depth,
            // NOT vertical clearance above stair. Revit has no built-in headroom parameter — headroom
            // is computed from geometry (stair-to-floor-above clearance), not stored as a BIP.

            // BLE_RAIL_HEIGHT_MM — Railing height
            if (catUpperW.Contains("RAILING"))
                written += MapLookup(el, "Top Rail Height", "BLE_RAIL_HEIGHT_MM", ftToMmW);

            // STR_FDN_DEPTH_MM — Foundation depth
            if (catUpperW.Contains("FOUNDATION"))
                written += MapDimension(el, BuiltInParameter.INSTANCE_ELEVATION_PARAM, "STR_FDN_DEPTH_MM", ftToMmW);

            // BLE_CEILING_HEIGHT_MM — Ceiling height
            if (catUpperW.Contains("CEILING"))
                written += MapDimension(el, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM, "BLE_CEILING_HEIGHT_MM", ftToMmW);

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

        // PERF-03: BIP availability cache per category — avoids calling el.get_Parameter(bip)
        // for BuiltInParameters that don't exist on that category. Each category is checked once;
        // subsequent elements of the same category skip the Revit API call entirely.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<BuiltInParameter, byte>>
            _bipMissingByCategory = new();

        private static bool IsBipKnownMissing(Element el, BuiltInParameter bip)
        {
            string catKey = el.Category?.Name ?? "";
            if (string.IsNullOrEmpty(catKey)) return false;
            return _bipMissingByCategory.TryGetValue(catKey, out var missing) && missing.ContainsKey(bip);
        }

        private static void MarkBipMissing(Element el, BuiltInParameter bip)
        {
            string catKey = el.Category?.Name ?? "";
            if (string.IsNullOrEmpty(catKey)) return;
            var set = _bipMissingByCategory.GetOrAdd(catKey, _ => new System.Collections.Concurrent.ConcurrentDictionary<BuiltInParameter, byte>());
            set.TryAdd(bip, 0);
        }

        /// <summary>Invalidate BIP availability cache (call on document switch).</summary>
        internal static void InvalidateBipCache() => _bipMissingByCategory.Clear();

        /// <summary>Map a built-in dimension parameter with unit conversion.</summary>
        private static int MapDimension(Element el, BuiltInParameter bip,
            string targetParam, double conversionFactor)
        {
            try
            {
                // PERF-03: Skip BIPs known to be missing for this category
                if (IsBipKnownMissing(el, bip)) return 0;

                Parameter p = el.get_Parameter(bip);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double)
                {
                    if (p == null) MarkBipMissing(el, bip);
                    return 0;
                }

                double val = p.AsDouble() * conversionFactor;
                if (val <= 0.001) return 0;

                string formatted = conversionFactor > 1
                    ? Math.Round(val, 0).ToString("F0",
                        System.Globalization.CultureInfo.InvariantCulture)
                    : val.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture);

                return SetIfEmptyInt(el, targetParam, formatted);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        /// <summary>Map native sheet parameters to STING shared parameters for all sheets.</summary>
        public static int MapSheets(Document doc)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();
            return MapSheets(doc, sheets);
        }

        /// <summary>Map native sheet parameters using a pre-collected sheet list.</summary>
        public static int MapSheets(Document doc, List<ViewSheet> sheets)
        {
            int written = 0;
            foreach (var sheet in sheets)
            {
                written += MapBuiltIn(sheet, BuiltInParameter.SHEET_NUMBER, ParamRegistry.SHT_NUMBER);
                written += MapBuiltIn(sheet, BuiltInParameter.SHEET_NAME, ParamRegistry.SHT_NAME);
            }
            return written;
        }

        /// <summary>
        /// Sheet-level tagging engine. Derives ISO 19650 document codes for all sheets
        /// by scanning viewport contents for discipline, level, and form data.
        /// Returns (sheetsProcessed, tokensWritten).
        /// </summary>
        public static (int sheets, int tokens) TagSheets(Document doc)
        {
            if (doc == null) return (0, 0);

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            if (sheets.Count == 0) return (0, 0);

            // Project-level tokens (shared across all sheets)
            string originator = SheetTagger.DetectOriginator(doc);
            string projectCode = SheetTagger.DetectProjectCode(doc);
            string rev = PhaseAutoDetect.DetectProjectRevision(doc) ?? "P01";

            int processed = 0, tokensWritten = 0;

            foreach (var sheet in sheets)
            {
                try
                {
                    int w = SheetTagger.TagSheet(doc, sheet, originator, projectCode, rev);
                    tokensWritten += w;
                    processed++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSheets: sheet {sheet.SheetNumber}: {ex.Message}");
                }
            }

            return (processed, tokensWritten);
        }

    // ── SheetTagger (nested helper for sheet-level ISO 19650 tagging) ─────────

        /// <summary>
        /// Sheet-level tagging engine. Derives ISO 19650 document naming tokens
        /// for ViewSheet elements by scanning viewport contents.
        /// </summary>
        internal static class SheetTagger
        {
        /// <summary>
        /// Tag a single sheet with ISO 19650 document code tokens.
        /// Returns number of parameters written.
        /// </summary>
        public static int TagSheet(Document doc, ViewSheet sheet,
            string originator, string projectCode, string rev)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int written = 0;

            // 1. Map native sheet number/name
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_NUMBER, sheet.SheetNumber);
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_NAME, sheet.Name);

            // P-01/A-01: Hoist GetAllViewports() + view resolution once for all derive methods
            IList<ElementId> vpIds = null;
            List<View> vpViews = null;
            try
            {
                vpIds = sheet.GetAllViewports();
                if (vpIds != null && vpIds.Count > 0)
                {
                    vpViews = new List<View>(vpIds.Count);
                    foreach (ElementId vpId in vpIds)
                    {
                        var vp = doc.GetElement(vpId) as Viewport;
                        if (vp == null) continue;
                        View view = doc.GetElement(vp.ViewId) as View;
                        if (view != null) vpViews.Add(view);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagSheet viewport resolution: {ex.Message}"); }
            vpViews ??= new List<View>();

            // D-03: Source tokens use SetIfEmptyStr to preserve user corrections
            // 2. Derive DISC from viewport element discipline majority vote
            string disc = DeriveSheetDiscipline(doc, sheet, vpViews);
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_DISC, disc);
            // Re-read actual stored value for TAG1 assembly (may differ if user-set)
            disc = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_DISC);
            if (string.IsNullOrEmpty(disc)) disc = "GEN";

            // 3. Derive FORM from viewport view types
            string form = DeriveSheetForm(vpViews);
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_FORM, form);
            form = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_FORM);
            if (string.IsNullOrEmpty(form)) form = "DR";

            // 4. Derive LEVEL from viewport view associated levels
            string level = DeriveSheetLevel(vpViews);
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_LEVEL, level);
            level = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_LEVEL);
            if (string.IsNullOrEmpty(level)) level = "XX";

            // 5. Write project-level tokens
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_ORIGINATOR, originator);
            written += SetIfEmptyStr(sheet, ParamRegistry.SHT_REV, rev);

            // 6. Assemble SHT_TAG_1 (ISO 19650 document code)
            // Format: PROJECT-ORIGINATOR-LEVEL-FORM-DISC-NUMBER-REV
            string sheetNum = sheet.SheetNumber ?? "00000";
            string tag1 = $"{projectCode}-{originator}-{level}-{form}-{disc}-{sheetNum}-{rev}";
            written += SetStr(sheet, ParamRegistry.SHT_TAG_1, tag1);

            // 7. Build SHT_TAG_7 narrative
            string tag7 = BuildSheetNarrative(sheet, disc, form, level, rev, vpViews.Count);
            written += SetStr(sheet, ParamRegistry.SHT_TAG_7, tag7);

            // INT-12: Performance warning for slow sheets
            sw.Stop();
            if (sw.ElapsedMilliseconds > 2000)
                StingLog.Warn($"TagSheet: {sheet.SheetNumber} took {sw.ElapsedMilliseconds}ms (slow — large viewport element count)");

            return written;
        }

        /// <summary>Extract originator code from Project Information.</summary>
        public static string DetectOriginator(Document doc)
        {
            try
            {
                var pi = doc.ProjectInformation;
                if (pi == null) return "XX";

                // Check for explicit originator parameter
                Parameter orgP = pi.LookupParameter("Organization Name")
                    ?? pi.LookupParameter("Client Name")
                    ?? pi.LookupParameter("Author");
                if (orgP != null && orgP.HasValue)
                {
                    string val = orgP.AsString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        // Take first 3-6 uppercase chars as code
                        string clean = new string(val.Where(c => char.IsLetterOrDigit(c)).ToArray());
                        return clean.Length <= 6
                            ? clean.ToUpperInvariant()
                            : clean.Substring(0, 6).ToUpperInvariant();
                    }
                }
                return "XX";
            }
            catch (Exception ex) { StingLog.Warn($"DetectOriginator: {ex.Message}"); return "XX"; }
        }

        /// <summary>Extract project code from Project Information.</summary>
        public static string DetectProjectCode(Document doc)
        {
            try
            {
                var pi = doc.ProjectInformation;
                if (pi == null) return "PR01";

                Parameter numP = pi.LookupParameter("Project Number");
                if (numP != null && numP.HasValue)
                {
                    string val = numP.AsString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        string clean = new string(val.Where(c => char.IsLetterOrDigit(c)).ToArray());
                        return clean.Length <= 8
                            ? clean.ToUpperInvariant()
                            : clean.Substring(0, 8).ToUpperInvariant();
                    }
                }
                return "PR01";
            }
            catch (Exception ex) { StingLog.Warn($"DetectProjectCode: {ex.Message}"); return "PR01"; }
        }

        /// <summary>
        /// Derive sheet discipline by majority vote of element DISC codes
        /// across all viewports on the sheet.
        /// P-01/A-01: Accepts pre-resolved vpViews to avoid redundant GetAllViewports() calls.
        /// </summary>
        private static string DeriveSheetDiscipline(Document doc, ViewSheet sheet, List<View> vpViews)
        {
            try
            {
                if (vpViews == null || vpViews.Count == 0) return DeriveDiscFromSheetName(sheet);

                var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (View view in vpViews)
                {
                    // Use view-level discipline detection first
                    var viewDiscs = TagConfig.GetViewRelevantDisciplines(view);
                    if (viewDiscs != null && viewDiscs.Count > 0)
                    {
                        foreach (string d in viewDiscs)
                        {
                            discCounts.TryGetValue(d, out int c);
                            discCounts[d] = c + 1;
                        }
                        continue;
                    }

                    // P-02: Fallback: sample elements in the view with category filter
                    try
                    {
                        var elements = new FilteredElementCollector(doc, view.Id)
                            .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                            .WhereElementIsNotElementType()
                            .ToElements();

                        int sampled = 0;
                        foreach (var el in elements)
                        {
                            if (sampled >= 200) break; // cap for performance
                            string catName = ParameterHelpers.GetCategoryName(el);
                            if (string.IsNullOrEmpty(catName)) continue;

                            // Check stored DISC first, then fall back to category map
                            string elDisc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                            if (string.IsNullOrEmpty(elDisc))
                            {
                                TagConfig.DiscMap.TryGetValue(catName, out elDisc);
                            }
                            if (!string.IsNullOrEmpty(elDisc))
                            {
                                discCounts.TryGetValue(elDisc, out int c);
                                discCounts[elDisc] = c + 1;
                                sampled++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DeriveSheetDiscipline view {view.Name}: {ex.Message}");
                    }
                }

                if (discCounts.Count == 0)
                {
                    // INT-15: Fallback to sheet name/number pattern matching
                    return DeriveDiscFromSheetName(sheet);
                }

                // If multiple disciplines with significant presence → COORD
                var sorted = discCounts.OrderByDescending(kv => kv.Value).ToList();
                if (sorted.Count >= 2)
                {
                    double total = sorted.Sum(kv => kv.Value);
                    double topPct = sorted[0].Value / total;
                    if (topPct < 0.75) return "COORD"; // No single discipline dominates
                }

                return sorted[0].Key;
            }
            catch (Exception ex) { StingLog.Warn($"DeriveSheetDiscipline: {ex.Message}"); return "GEN"; }
        }

        /// <summary>
        /// Derive document form code from view types on the sheet.
        /// DR=Drawing, SH=Schedule, M3=3D Model, SP=Specification, LG=Legend.
        /// P-01/A-01: Accepts pre-resolved vpViews to avoid redundant GetAllViewports() calls.
        /// </summary>
        private static string DeriveSheetForm(List<View> vpViews)
        {
            try
            {
                if (vpViews == null || vpViews.Count == 0) return "DR";

                bool hasSchedule = false, has3D = false, hasLegend = false;

                foreach (View view in vpViews)
                {
                    switch (view.ViewType)
                    {
                        case ViewType.Schedule:
                            hasSchedule = true;
                            break;
                        case ViewType.ThreeD:
                            has3D = true;
                            break;
                        case ViewType.Legend:
                        case ViewType.DraftingView:
                            hasLegend = true;
                            break;
                    }
                }

                // Priority: Schedule > 3D > Legend > Drawing
                if (hasSchedule) return "SH";
                if (has3D) return "M3";
                if (hasLegend) return "LG";
                return "DR";
            }
            catch (Exception ex) { StingLog.Warn($"DeriveSheetForm: {ex.Message}"); return "DR"; }
        }

        /// <summary>
        /// Derive level code from viewport views' associated levels.
        /// Returns the most common level code, or "XX" if mixed/none.
        /// P-01/A-01: Accepts pre-resolved vpViews to avoid redundant GetAllViewports() calls.
        /// </summary>
        private static string DeriveSheetLevel(List<View> vpViews)
        {
            try
            {
                if (vpViews == null || vpViews.Count == 0) return "XX";

                var levelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (View view in vpViews)
                {
                    // Get associated level via the view's GenLevel property
                    Level lvl = view.GenLevel;
                    if (lvl == null) continue;

                    string lvlCode = DeriveLevelCodeFromName(lvl.Name);
                    if (!string.IsNullOrEmpty(lvlCode))
                    {
                        levelCounts.TryGetValue(lvlCode, out int c);
                        levelCounts[lvlCode] = c + 1;
                    }
                }

                if (levelCounts.Count == 0) return "XX";
                if (levelCounts.Count == 1) return levelCounts.Keys.First();

                // Multiple levels — return most common
                return levelCounts.OrderByDescending(kv => kv.Value).First().Key;
            }
            catch (Exception ex) { StingLog.Warn($"DeriveSheetLevel: {ex.Message}"); return "XX"; }
        }

        /// <summary>Build human-readable sheet narrative for SHT_TAG_7.
        /// P-01/A-01: Accepts vpCount to avoid redundant GetAllViewports() call.</summary>
        private static string BuildSheetNarrative(ViewSheet sheet,
            string disc, string form, string level, string rev, int vpCount)
        {
            var parts = new List<string>();

            // Discipline description
            string discDesc = disc switch
            {
                "M" => "Mechanical",
                "E" => "Electrical",
                "P" => "Plumbing",
                "A" => "Architectural",
                "S" => "Structural",
                "FP" => "Fire Protection",
                "LV" => "Low Voltage",
                "G" => "General",
                "COORD" => "Coordination (Multi-discipline)",
                "GEN" => "General",
                _ => disc
            };

            // Form description
            string formDesc = form switch
            {
                "DR" => "Drawing",
                "SH" => "Schedule",
                "M3" => "3D Model",
                "SP" => "Specification",
                "LG" => "Legend",
                _ => form
            };

            parts.Add($"{discDesc} {formDesc}");

            // Level
            if (level != "XX")
            {
                string levelDesc = level switch
                {
                    "GF" => "Ground Floor",
                    "B1" => "Basement 1",
                    "B2" => "Basement 2",
                    "RF" => "Roof",
                    _ => $"Level {level}"
                };
                parts.Add(levelDesc);
            }

            // Sheet name
            if (!string.IsNullOrEmpty(sheet.Name))
                parts.Add(sheet.Name);

            // Revision
            parts.Add($"Rev {rev}");

            // Viewport count
            if (vpCount > 0)
                parts.Add($"{vpCount} viewport{(vpCount == 1 ? "" : "s")}");

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// INT-15: Fallback discipline detection from sheet name/number patterns
        /// when no viewport elements provide DISC data.
        /// </summary>
        private static string DeriveDiscFromSheetName(ViewSheet sheet)
        {
            string combined = $"{sheet.SheetNumber} {sheet.Name}".ToUpperInvariant();
            if (combined.Contains("MECHANICAL") || combined.Contains("HVAC") || combined.StartsWith("M-") || combined.StartsWith("M ")) return "M";
            if (combined.Contains("ELECTRICAL") || combined.Contains("LIGHTING") || combined.StartsWith("E-") || combined.StartsWith("E ")) return "E";
            if (combined.Contains("PLUMBING") || combined.Contains("SANITARY") || combined.StartsWith("P-") || combined.StartsWith("P ")) return "P";
            if (combined.Contains("ARCHITECTURAL") || combined.Contains("ARCH") || combined.StartsWith("A-") || combined.StartsWith("A ")) return "A";
            if (combined.Contains("STRUCTURAL") || combined.Contains("STRUCT") || combined.StartsWith("S-") || combined.StartsWith("S ")) return "S";
            if (combined.Contains("FIRE") || combined.Contains("SPRINKLER") || combined.StartsWith("FP")) return "FP";
            if (combined.Contains("LOW VOLTAGE") || combined.Contains("DATA") || combined.Contains("SECURITY")) return "LV";
            if (combined.Contains("COORDINATION") || combined.Contains("COMBINED") || combined.Contains("MULTI")) return "COORD";
            return "GEN";
        }

        /// <summary>Derive level code from level name string (same logic as GetLevelCode but from name).</summary>
        private static string DeriveLevelCodeFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "XX";
            string lower = name.Trim().ToLowerInvariant();
            if (lower.StartsWith("level ") && name.Length > 6)
            {
                string digits = new string(name.Substring(6).Where(char.IsDigit).ToArray());
                if (digits.Length > 0 && digits.Length <= 3) return "L" + digits.PadLeft(2, '0');
            }
            if (lower == "ground" || lower == "ground floor" || lower == "ground level") return "GF";
            // L-02: Extract basement number instead of hardcoding B1
            if (lower.StartsWith("basement"))
            {
                string bDigits = new string(name.Where(char.IsDigit).ToArray());
                return "B" + (bDigits.Length > 0 ? bDigits : "1");
            }
            if (lower.StartsWith("roof") || lower == "rf") return "RF";
            if (lower.StartsWith("mezzanine") || lower == "mezz") return "MZ";
            // Extract any trailing digits
            string trailingDigits = new string(name.Where(char.IsDigit).ToArray());
            if (trailingDigits.Length > 0) return "L" + trailingDigits.PadLeft(2, '0');
            return "XX";
        }
        } // end SheetTagger

    // ── NativeParamMapper private helpers (MapBuiltIn, SetIfEmptyInt, etc.) ────

        /// <summary>Write parameter, always overwrite. Returns 1 on success, 0 on failure.</summary>
        private static int SetStr(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return ParameterHelpers.SetString(el, paramName, value, overwrite: true) ? 1 : 0;
        }

        /// <summary>Write parameter only if empty. Returns 1 on success, 0 on failure.</summary>
        private static int SetIfEmptyStr(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return ParameterHelpers.SetIfEmpty(el, paramName, value) ? 1 : 0;
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
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
            catch (Exception ex) { StingLog.Warn($"Stair slope mapping failed: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Stair width mapping failed: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Ramp slope mapping failed: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Structural type mapping failed: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Room name/number mapping failed: {ex.Message}"); }
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

            // PERF-02: STATUS and REV are already populated by PopulateAll (which uses
            // cached context). Only set here as a safety net using SetIfEmpty — skip
            // the expensive uncached DetectStatus/DetectProjectRevision calls.
            // PopulateAll runs before NativeParamMapper in RunFullPipeline, so these
            // will almost always be non-empty already.
            written += SetIfEmptyInt(el, ParamRegistry.STATUS, "NEW");
            string existingRev = ParameterHelpers.GetString(el, ParamRegistry.REV);
            if (string.IsNullOrEmpty(existingRev))
            {
                string rev = PhaseAutoDetect.DetectProjectRevision(doc);
                if (!string.IsNullOrEmpty(rev))
                    written += SetIfEmptyInt(el, ParamRegistry.REV, rev);
            }

            // ORIGIN: set from project originator field if available
            try
            {
                string origin = doc.ProjectInformation?.OrganizationName;
                if (!string.IsNullOrEmpty(origin))
                    written += SetIfEmptyInt(el, ParamRegistry.ORIGIN, origin);
            }
            catch (Exception ex) { StingLog.Warn($"ORIGIN mapping from ProjectInformation failed: {ex.Message}"); }

            // PROJECT: set from project name if available
            try
            {
                string projName = doc.ProjectInformation?.Name;
                if (!string.IsNullOrEmpty(projName))
                    written += SetIfEmptyInt(el, ParamRegistry.PROJECT, projName);
            }
            catch (Exception ex) { StingLog.Warn($"PROJECT mapping from ProjectInformation failed: {ex.Message}"); }
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

            // ── SYN-01: Cross-write ASS_FLOW_RATE_TXT from PLM_PIPE_FLOW or HVC_AIRFLOW ──
            string flowRate = ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_FLOW);
            if (string.IsNullOrEmpty(flowRate))
                flowRate = ParameterHelpers.GetString(el, ParamRegistry.HVC_AIRFLOW);
            if (!string.IsNullOrEmpty(flowRate))
                written += SetIfEmptyInt(el, "ASS_FLOW_RATE_TXT", flowRate);

            // ── SYN-02: Cross-write ASS_POWER_RATING_TXT from ELC_POWER ──
            string powerRating = ParameterHelpers.GetString(el, ParamRegistry.ELC_POWER);
            if (!string.IsNullOrEmpty(powerRating))
                written += SetIfEmptyInt(el, "ASS_POWER_RATING_TXT", powerRating);

            // ── WARN-XS: Warning-activation parameter mappings ──
            // HVC_EFF_RATIO_NR — HVAC COP from Mechanical Equipment
            if (catName == "Mechanical Equipment")
            {
                written += MapLookup(el, "COP", "HVC_EFF_RATIO_NR", 1.0);
            }

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

                if (string.IsNullOrEmpty(val)) return 0;

                Element writeTarget = target ?? source;
                return SetIfEmptyInt(writeTarget, targetParamName, val);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        /// <summary>SetIfEmpty returning 1 on success, 0 on skip/failure.</summary>
        private static int SetIfEmptyInt(Element el, string paramName, string value)
        {
            return ParameterHelpers.SetIfEmpty(el, paramName, value) ? 1 : 0;
        }

        /// <summary>
        /// Pre-check utility: verifies that the core tag parameters (DISC, LOC, ZONE, LVL,
        /// SYS, FUNC, PROD, SEQ, TAG1) are writable on the given element. Returns false if
        /// any of these parameters are read-only, missing, or otherwise non-writable.
        /// Use this before a tagging operation to avoid partial tag state from write failures.
        /// </summary>
        public static bool AreTagParamsWritable(Element el)
        {
            if (el == null) return false;

            string[] coreParams = new[]
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.TAG1
            };

            foreach (string paramName in coreParams)
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly)
                    return false;
                if (p.StorageType != StorageType.String)
                    return false;
            }
            return true;
        }

    }

    /// <summary>
    /// G2.1 / GAP-P1-P4: Centralised per-element tagging pipeline.
    /// Executes the full sequence: TypeTokenInherit → PopulateAll → NativeParamMapper
    /// → FormulaEngine → BuildAndWriteTag → WriteContainers → WriteTag7All → GridRef.
    /// All tag commands delegate to this helper to guarantee pipeline consistency.
    /// </summary>
    internal static class TagPipelineHelper
    {
        // F-09: Cached timestamp string — avoids per-element DateTime.Now.ToString allocation in audit trail
        // Refreshed at most once per second (reused within same second for batch operations)
        [ThreadStatic] private static string _cachedTimestamp;
        [ThreadStatic] private static long _cachedTimestampTick;
        private static string GetCachedTimestamp()
        {
            long nowTick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            if (_cachedTimestamp == null || nowTick != _cachedTimestampTick)
            {
                _cachedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _cachedTimestampTick = nowTick;
            }
            return _cachedTimestamp;
        }

        // Phase 74d: Static token-param map — eliminates 2 Dictionary allocations per element in RunFullPipeline
        private static readonly Dictionary<string, string> TokenParamMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DISC"] = ParamRegistry.DISC, ["LOC"] = ParamRegistry.LOC,
            ["ZONE"] = ParamRegistry.ZONE, ["LVL"] = ParamRegistry.LVL,
            ["SYS"] = ParamRegistry.SYS, ["FUNC"] = ParamRegistry.FUNC,
            ["PROD"] = ParamRegistry.PROD, ["STATUS"] = ParamRegistry.STATUS,
            ["REV"] = ParamRegistry.REV
        };
        /// <summary>
        /// GAP-WS-01: Check whether the element can be modified in a workshared environment.
        /// Returns true if the element is safe to edit (not workshared, or owned by current user, or unowned).
        /// Returns false if owned by another user — the caller should skip/defer this element.
        /// </summary>
        public static bool IsEditableInWorksharing(Document doc, Element el)
        {
            if (!doc.IsWorkshared) return true;
            try
            {
                WorksetId wsId = el.WorksetId;
                if (wsId == null || wsId == WorksetId.InvalidWorksetId) return true;
                var wsInfo = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id);
                if (string.IsNullOrEmpty(wsInfo.Owner) || wsInfo.Owner == "")
                    return true; // unowned — safe to edit
                return wsInfo.Owner == doc.Application.Username;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IsEditableInWorksharing check failed for {el?.Id}: {ex.Message}");
                return true; // fail-open: allow edit attempt, Revit will throw if truly locked
            }
        }

        /// <summary>
        /// GAP-PH-01: Check whether element is demolished in the project's current phase.
        /// Returns true if element has a demolished phase set (PHASE_DEMOLISHED parameter is not InvalidElementId).
        /// </summary>
        public static bool IsDemolished(Element el)
        {
            try
            {
                Parameter demParam = el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                if (demParam != null && demParam.HasValue)
                {
                    ElementId demPhaseId = demParam.AsElementId();
                    return demPhaseId != null && demPhaseId != ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IsDemolished check failed for {el?.Id}: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Centralized post-tagging cleanup: saves SEQ sidecar, invalidates caches,
        /// checks compliance gate. Call after tx.Commit() in any tagging command.
        /// </summary>
        public static void PostTagCleanup(Document doc, Dictionary<string, int> seqCounters, string commandName)
        {
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ex) { StingLog.Warn($"{commandName} SaveSeqSidecar: {ex.Message}"); }
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            // DIAG-01: Reset read-only skip counter at batch boundary so each operation
            // gets fresh diagnostic logging (first 5 warnings + every 100th).
            ParameterHelpers.ResetReadOnlySkipCount();
            TagConfig.CheckComplianceGate(doc, commandName);
        }

        /// <summary>
        /// Run the complete tagging pipeline for a single element.
        /// All parameters are pre-built outside the loop for performance.
        /// </summary>
        public static bool RunFullPipeline(
            Document doc,
            Element el,
            TokenAutoPopulator.PopulationContext ctx,
            HashSet<string> tagIndex,
            Dictionary<string, int> seqCounters,
            List<Temp.FormulaEngine.FormulaDefinition> formulas,
            List<Grid> gridLines,
            bool overwrite,
            bool skipComplete,
            TagCollisionMode collisionMode,
            TaggingStats stats = null)
        {
            try
            {
                // MEDIUM-09: Reset read-only skip counter at pipeline entry to prevent
                // cross-batch counter leakage from [ThreadStatic] persistence
                ParameterHelpers.ResetReadOnlySkipCount();

                string catName = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(catName)) return false;

                // G1.1: Category skip list
                if (TagConfig.CategorySkipList.Contains(catName)) return false;

                // Early SKIP check from CategoryTokenOverrides — before expensive TypeTokenInherit/PopulateAll
                if (TagConfig.CategoryTokenOverrides.TryGetValue(catName, out var earlyOverrides)
                    && earlyOverrides.TryGetValue("SKIP", out string earlySkipVal)
                    && earlySkipVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return false;

                // AL-06: Capture previous tag value for audit trail (READ only — write deferred to after BuildAndWriteTag)
                // DI-004 / CRIT-PH-02: All audit trail writes (PREV_TXT, MODIFIED_DT, MODIFIED_BY)
                // happen AFTER successful BuildAndWriteTag only, preventing false audit trail on failure
                string _prevTag = null;
                try { _prevTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1); }
                catch (Exception auditEx)
                {
                    StingLog.Warn($"Tag history read on {el?.Id}: {auditEx.Message}");
                }

                // Plugin hook: notify third-party plugins before tagging
                StingPluginHooks.FireBeforeTag(doc, el);

                // P4: Inherit token values from family type before populating
                TokenAutoPopulator.TypeTokenInherit(doc, el);

                // FIX-DEEP01: Snapshot locked token values AFTER TypeTokenInherit so inherited
                // values are preserved, but BEFORE PopulateAll/overrides can change them
                // Phase 74d: Only allocate locked snapshot when token lock is non-empty (rare)
                Dictionary<string, string> lockedSnapshot = null;
                try
                {
                    string preLockStr = ParameterHelpers.GetString(el, "ASS_TOKEN_LOCK_TXT");
                    if (!string.IsNullOrWhiteSpace(preLockStr))
                    {
                        lockedSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string[] lockKeys = preLockStr.Split(',')
                            .Select(s => s.Trim().ToUpperInvariant()).ToArray();
                        foreach (string lockKey in lockKeys)
                        {
                            if (TokenParamMap.TryGetValue(lockKey, out string paramName))
                            {
                                string val = ParameterHelpers.GetString(el, paramName);
                                if (!string.IsNullOrEmpty(val))
                                    lockedSnapshot[lockKey] = val;
                            }
                        }
                    }
                }
                catch (Exception lockEx) { StingLog.Warn($"Token lock read: {lockEx.Message}"); }

                // P2 / PopulateAll: Populate all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV)
                TokenAutoPopulator.PopulateAll(doc, el, ctx, overwrite: overwrite);

                // G1.1: Apply CATEGORY_FORCE_SYS override after PopulateAll
                if (TagConfig.CategoryForceSys.TryGetValue(catName, out string forcedSys)
                    && !string.IsNullOrEmpty(forcedSys))
                    ParameterHelpers.SetString(el, ParamRegistry.SYS, forcedSys, overwrite: true);

                // FE-06: Apply full per-category token overrides
                if (TagConfig.CategoryTokenOverrides.TryGetValue(catName, out var tokenOverrides))
                {
                    // Check SKIP flag FIRST — avoid writing tokens to skipped categories
                    if (tokenOverrides.TryGetValue("SKIP", out string skipVal)
                        && skipVal.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return false;

                    foreach (var kv in tokenOverrides)
                    {
                        if (kv.Key.Equals("SKIP", StringComparison.OrdinalIgnoreCase)) continue;
                        string paramName = kv.Key.ToUpperInvariant() switch
                        {
                            "DISC" => ParamRegistry.DISC, "LOC" => ParamRegistry.LOC,
                            "ZONE" => ParamRegistry.ZONE, "LVL" => ParamRegistry.LVL,
                            "SYS" => ParamRegistry.SYS, "FUNC" => ParamRegistry.FUNC,
                            "PROD" => ParamRegistry.PROD, "STATUS" => ParamRegistry.STATUS,
                            _ => null
                        };
                        if (!string.IsNullOrEmpty(paramName))
                            ParameterHelpers.SetString(el, paramName, kv.Value, overwrite: true);
                    }
                }

                // FIX-DEEP01: Restore locked token values (overrides above may have changed them)
                // Phase 74d: Use static TokenParamMap + null check (lockedSnapshot only allocated when lock exists)
                if (lockedSnapshot != null && lockedSnapshot.Count > 0)
                {
                    foreach (var kvp in lockedSnapshot)
                    {
                        if (TokenParamMap.TryGetValue(kvp.Key, out string paramName))
                        {
                            try { ParameterHelpers.SetString(el, paramName, kvp.Value, overwrite: true); }
                            catch (Exception lockEx)
                            {
                                StingLog.Warn($"TagPipeline: failed to restore locked token {kvp.Key} on {el.Id}: {lockEx.Message}");
                            }
                        }
                    }
                    // Finding-5 FIX: Throttle locked token log — only log first 5 + every 100th
                    _lockedTokenRestoreCount++;
                    if (_lockedTokenRestoreCount <= 5 || _lockedTokenRestoreCount % 100 == 0)
                        StingLog.Info($"TagPipeline: restored {lockedSnapshot.Count} locked tokens on {el.Id}: {string.Join(",", lockedSnapshot.Keys)} (#{_lockedTokenRestoreCount})");
                }

                // P2: Bridge Revit native params → STING shared params
                NativeParamMapper.MapAll(doc, el);

                // P3: Evaluate formulas in dependency order
                // FUT-18: Lazy formula evaluation — skip formulas whose target parameter
                // doesn't exist on this element's category (saves ~40% formula iterations)
                if (formulas != null && formulas.Count > 0)
                {
                    foreach (var formula in formulas)
                    {
                        try
                        {
                            // FUT-18: Early-exit skip — avoids expensive BuildContext
                            Parameter targetParam = el.LookupParameter(formula.ParameterName);
                            if (targetParam == null || targetParam.IsReadOnly) continue;

                            var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                            if (fCtx == null) continue;

                            if (formula.DataType == "TEXT")
                            {
                                string result = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                if (result != null && targetParam.StorageType == StorageType.String
                                    && (overwrite || string.IsNullOrEmpty(targetParam.AsString())))
                                    targetParam.Set(result);
                            }
                            else
                            {
                                double? result = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                if (result.HasValue && !double.IsNaN(result.Value) && !double.IsInfinity(result.Value))
                                    Temp.FormulaEngine.WriteNumericResult(targetParam, result.Value);
                            }
                        }
                        catch (Exception fEx)
                        {
                            StingLog.Warn($"TagPipeline formula '{formula.ParameterName}' on {el.Id}: {fEx.Message}");
                        }
                    }
                }

                // C-01 FIX: Check BuildAndWriteTag return value — skip containers/TAG7 on failure
                bool tagWriteOk = TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                    skipComplete: skipComplete,
                    existingTags: tagIndex,
                    collisionMode: collisionMode,
                    stats: stats,
                    cachedRev: ctx?.ProjectRev,
                    cachedPhases: ctx?.CachedPhases,
                    lastPhaseId: ctx?.LastPhaseId);
                if (!tagWriteOk)
                {
                    StingLog.Warn($"TagPipeline: BuildAndWriteTag failed for {el.Id} — skipping containers/TAG7");
                    // Phase 79b: Balanced hook call — notify plugins that tagging failed (null tag)
                    StingPluginHooks.FireAfterTag(doc, el, null);
                    return false;
                }

                // DI-004 / CRIT-PH-02: Write ALL audit trail AFTER successful tag change only
                try
                {
                    if (!string.IsNullOrEmpty(_prevTag))
                        ParameterHelpers.SetString(el, "ASS_TAG_PREV_TXT", _prevTag, overwrite: true);
                    // F-09: Use cached timestamp (refreshed once per second) to avoid per-element allocation
                    ParameterHelpers.SetString(el, "ASS_TAG_MODIFIED_DT",
                        GetCachedTimestamp(), overwrite: true);
                    ParameterHelpers.SetString(el, "ASS_TAG_MODIFIED_BY_TXT", Environment.UserName, overwrite: true);
                }
                catch (Exception dtEx) { StingLog.Warn($"Tag audit trail write: {dtEx.Message}"); }

                // C-02 FIX: Re-read token values AFTER BuildAndWriteTag (which applies overrides
                // and SetIfEmpty) so container retry uses ACTUAL stored values, not stale pre-override values
                string[] tokenVals = ParamRegistry.ReadTokenValues(el);

                // Read TAG1 once — reused for hook, container guard, and downstream checks
                string tag1Check = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                // Plugin hook: notify third-party plugins after successful tagging
                StingPluginHooks.FireAfterTag(doc, el, tag1Check);

                // LOGIC-004 / BUG-01 FIX: Removed redundant WriteContainers call here.
                // BuildAndWriteTag already writes all 53 containers at TagConfig.cs:2834
                // using fresh ReadTokenValues. This second call was doubling SetString calls
                // (53 × 2 = 106 per element, 5.3M redundant calls on 50K-element batches).

                TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: overwrite);

                // P5: Auto-populate GRID_REF if empty and grids are available
                // LOGIC-010: Log once per session when no grids exist in document
                if (gridLines != null && gridLines.Count > 0)
                {
                    string existingRef = ParameterHelpers.GetString(el, ParamRegistry.GRID_REF);
                    if (string.IsNullOrEmpty(existingRef))
                    {
                        string gridRef = SpatialAutoDetect.GetGridRef(el, gridLines);
                        if (!string.IsNullOrEmpty(gridRef))
                            ParameterHelpers.SetString(el, ParamRegistry.GRID_REF, gridRef, overwrite: false);
                    }
                }
                else if ((gridLines == null || gridLines.Count == 0) && !_noGridsLoggedThisSession)
                {
                    _noGridsLoggedThisSession = true;
                    StingLog.Info("TagPipeline: No grids found in document — GRID_REF will be empty. Create grid lines if grid references are required.");
                }

                // PERF-02: Inline FUNC/PROD empty tracking from tokenVals (avoids 2 GetString calls per element)
                // tokenVals: [0]=DISC [1]=LOC [2]=ZONE [3]=LVL [4]=SYS [5]=FUNC [6]=PROD [7]=SEQ
                if (stats != null && tokenVals != null && tokenVals.Length >= 7)
                    stats.RecordEmptyTokens(tokenVals[5], tokenVals[6]);

                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagPipelineHelper.RunFullPipeline: element {el?.Id}: {ex.Message}");
                stats?.RecordWarning($"Pipeline error on {el?.Id}: {ex.Message}");
                return false;
            }
        }

        // ── LOGIC-010: Track whether "no grids" info has been logged this session ──
        private static bool _noGridsLoggedThisSession;

        // ── Finding-5: Throttle counter for locked token restoration logging ──
        private static int _lockedTokenRestoreCount;

        // ── Session caches for formulas and grid lines ──
        private static List<Temp.FormulaEngine.FormulaDefinition> _cachedFormulas;
        private static DateTime _formulaCacheTime;
        // GAP-FIX: Use configurable TTL from TagConfig instead of hardcoded value
        private static TimeSpan FormulaCacheTTL => TimeSpan.FromMinutes(TagConfig.FormulaCacheTTLMinutes);

        private static List<Grid> _cachedGridLines;
        private static string _gridCacheDocKey;
        private static DateTime _gridCacheTime;
        // GAP-FIX: Use configurable TTL from TagConfig instead of hardcoded value
        private static TimeSpan GridCacheTTL => TimeSpan.FromMinutes(TagConfig.GridCacheTTLMinutes);

        /// <summary>
        /// Build context objects required by RunFullPipeline. Call once before the element loop.
        /// Returns formulas sorted by DependencyLevel (empty list if no formula file found).
        /// Uses session cache with 5-minute TTL to prevent redundant CSV reads (40+ callers/session).
        /// </summary>
        public static List<Temp.FormulaEngine.FormulaDefinition> LoadFormulas()
        {
            // Return cached formulas if still valid
            if (_cachedFormulas != null && (DateTime.UtcNow - _formulaCacheTime) < FormulaCacheTTL)
                return _cachedFormulas;

            try
            {
                string csvPath = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
                if (csvPath == null) return new List<Temp.FormulaEngine.FormulaDefinition>();
                var formulas = Temp.FormulaEngine.LoadFormulas(csvPath);
                formulas.Sort((a, b) => a.DependencyLevel.CompareTo(b.DependencyLevel));
                _cachedFormulas = formulas;
                _formulaCacheTime = DateTime.UtcNow;
                return formulas;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagPipelineHelper.LoadFormulas: {ex.Message}");
                return new List<Temp.FormulaEngine.FormulaDefinition>();
            }
        }

        /// <summary>P5: Load all Grid elements once before the element loop.
        /// Uses session cache with 2-minute TTL keyed by document path.</summary>
        public static List<Grid> LoadGridLines(Document doc)
        {
            string docKey = ParameterHelpers.GetStableDocKey(doc);
            if (_cachedGridLines != null && _gridCacheDocKey == docKey &&
                (DateTime.UtcNow - _gridCacheTime) < GridCacheTTL)
                return _cachedGridLines;

            try
            {
                var grids = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .ToList();
                _cachedGridLines = grids;
                _gridCacheDocKey = docKey;
                _gridCacheTime = DateTime.UtcNow;
                return grids;
            }
            catch (Exception ex) { StingLog.Warn($"LoadGridLines: {ex.Message}"); return new List<Grid>(); }
        }

        /// <summary>Invalidate formula and grid line caches (call on document close/switch).</summary>
        public static void InvalidateSessionCaches()
        {
            _cachedFormulas = null;
            _cachedGridLines = null;
            _gridCacheDocKey = null;
            _noGridsLoggedThisSession = false; // LOGIC-010: Reset per-session flag
            _lockedTokenRestoreCount = 0; // Finding-5: Reset throttle counter
            TokenAutoPopulator._copyTokensLogCount = 0; // Phase 79b: Reset log throttle
        }

        // ══════════════════════════════════════════════════════════════════
        //  BATCH RUNNER — Reusable per-element error recovery for batch ops
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Result from a batch operation with per-element error tracking.</summary>
        public class BatchResult
        {
            public int Processed { get; set; }
            public int Succeeded { get; set; }
            public int Failed { get; set; }
            public int Skipped { get; set; }
            public List<(ElementId Id, string Reason)> Failures { get; set; } = new();

            /// <summary>Add failure summary to a StingResultPanel section.</summary>
            public void AddToPanel(UI.StingResultPanel.Builder panel)
            {
                panel.Metric("Processed", Processed.ToString());
                panel.MetricHighlight("Succeeded", Succeeded.ToString());
                if (Failed > 0) panel.MetricError("Failed", Failed.ToString());
                if (Skipped > 0) panel.MetricWarn("Skipped", Skipped.ToString());
                if (Failures.Count > 0)
                {
                    panel.Separator();
                    foreach (var (id, reason) in Failures.Take(10))
                        panel.Alert($"Element {id}: {reason}");
                    if (Failures.Count > 10)
                        panel.Text($"... and {Failures.Count - 10} more failures (see log)");
                }
            }
        }

        /// <summary>Run an action on each element with per-element error recovery.
        /// Failed elements are logged and skipped, not rolled back.</summary>
        public static BatchResult RunBatch(IList<Element> elements, Action<Element> action, string operationName)
        {
            var result = new BatchResult();
            foreach (var el in elements)
            {
                result.Processed++;
                try
                {
                    action(el);
                    result.Succeeded++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Failures.Add((el.Id, ex.Message));
                    StingLog.Warn($"{operationName}: Element {el.Id} failed: {ex.Message}");
                }
            }
            return result;
        }
    }
}
