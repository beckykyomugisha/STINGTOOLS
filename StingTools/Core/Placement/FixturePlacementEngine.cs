// StingTools v4 MVP — fixture placement engine.
//
// Public entry point: PlaceFixturesInScope(doc, roomIds, rules, dryRun)
// returns a PlacementResult containing placed ElementIds, skipped
// count, per-rule + per-room counts and a warnings list.
//
// The engine never throws: every per-element failure is trapped,
// surfaced as a warning and skipped so a single broken family does
// not abort the entire batch.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Engine result surfaced in StingResultPanel + logged.
    /// </summary>
    public class PlacementResult
    {
        public List<ElementId> PlacedIds { get; } = new List<ElementId>();
        public int SkippedCount { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, int> CountsByRule { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> CountsByRoom { get; } = new Dictionary<string, int>();
        public int RoomsVisited { get; set; }
        public int CandidatesEvaluated { get; set; }
        public bool DryRun { get; set; }
    }

    /// <summary>
    /// Stateless engine. Reads the rule library via PlacementRuleLoader
    /// and delegates per-candidate scoring to PlacementScorer.
    /// </summary>
    public static partial class FixturePlacementEngine
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>
        /// Entry point. If rules is null/empty, loads the default + project
        /// override library. If dryRun is true, returns candidates without
        /// placing anything (the UI shows a preview).
        /// </summary>
        /// <summary>Original 4-arg entry point — preserved for back-compat.
        /// Delegates to the 5-arg overload with a no-op progress callback.</summary>
        public static PlacementResult PlaceFixturesInScope(
            Document doc,
            IList<ElementId> roomIds,
            IList<PlacementRule> rules,
            bool dryRun)
            => PlaceFixturesInScope(doc, roomIds, rules, dryRun, progress: null);

        /// <summary>Long-form entry point with a progress hook. The
        /// callback is invoked once per room with (processed, total) so the
        /// Placement Centre can show a StingProgressDialog and let the user
        /// abort. Returning <c>true</c> from the callback aborts the run
        /// after the current room commits — partial work is kept (still
        /// inside the engine's Transaction so callers can roll back at the
        /// outer TransactionGroup level if they want all-or-nothing).</summary>
        public static PlacementResult PlaceFixturesInScope(
            Document doc,
            IList<ElementId> roomIds,
            IList<PlacementRule> rules,
            bool dryRun,
            Func<int, int, bool> progress)
        {
            var result = new PlacementResult { DryRun = dryRun };
            if (doc == null)
            {
                result.Warnings.Add("FixturePlacementEngine: document is null");
                return result;
            }

            if (rules == null || rules.Count == 0)
            {
                try { rules = PlacementRuleLoader.Load(doc.PathName); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Rule load failed: {ex.Message}");
                    rules = new List<PlacementRule>();
                }
            }

            if (rules.Count == 0)
            {
                result.Warnings.Add("No placement rules found. Ship STING_PLACEMENT_RULES.json or provide a project override.");
                return result;
            }

            var rooms = CollectRooms(doc, roomIds, result);
            result.RoomsVisited = rooms.Count;
            if (rooms.Count == 0) return result;

            var scorer = new PlacementScorer(doc)
            {
                // Carry the Fixtures-tab RejectInsideWall option through
                // to the scorer so ElementIntersectsSolidFilter runs on
                // every candidate. Default off for fast-path placement
                // in large rooms; on by default in the UI because
                // users expect fixtures not to sit inside walls.
                RejectInsideWall = StingTools.Commands.Placement.PlaceFixturesOptions.RejectInsideWall,
            };
            var perCategorySymbol = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

            // Rule ordering: higher Priority first so specialised rules
            // win room capacity slots before generic fallbacks.
            var ordered = rules.OrderByDescending(r => r.Priority).ToList();

            Transaction tx = null;
            if (!dryRun)
            {
                tx = new Transaction(doc, "STING v4 Place Fixtures");
                try { tx.Start(); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Transaction start failed: {ex.Message}");
                    return result;
                }
            }

            try
            {
                int processed = 0;
                int total = rooms.Count;
                bool cancelled = false;
                foreach (var room in rooms)
                {
                    foreach (var rule in ordered)
                    {
                        try
                        {
                            ProcessRoomRule(doc, room, rule, scorer,
                                perCategorySymbol, result, dryRun);
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Room {room.Id} / {rule.MergeKey}: {ex.Message}");
                            result.SkippedCount++;
                        }
                    }
                    processed++;
                    if (progress != null)
                    {
                        try
                        {
                            if (progress(processed, total))
                            {
                                cancelled = true;
                                result.Warnings.Add($"Cancelled after {processed} of {total} room(s).");
                                break;
                            }
                        }
                        catch (Exception pgEx)
                        {
                            result.Warnings.Add($"Progress callback: {pgEx.Message}");
                        }
                    }
                }

                if (!dryRun)
                {
                    tx.Commit();
                }
                if (cancelled)
                    StingLog.Info($"FixturePlacementEngine: run cancelled after {processed}/{total} rooms.");
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                result.Warnings.Add($"FixturePlacementEngine fatal: {ex.Message}");
            }
            finally
            {
                tx?.Dispose();
            }

            return result;
        }

        private static List<Room> CollectRooms(Document doc, IList<ElementId> roomIds, PlacementResult result)
        {
            var rooms = new List<Room>();
            try
            {
                if (roomIds == null || roomIds.Count == 0)
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType();
                    foreach (var e in collector)
                        if (e is Room r && r.Area > 0.0) rooms.Add(r);
                }
                else
                {
                    foreach (var id in roomIds)
                    {
                        var el = doc.GetElement(id);
                        if (el is Room r && r.Area > 0.0) rooms.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Room collection failed: {ex.Message}");
            }
            return rooms;
        }

        private static void ProcessRoomRule(
            Document doc,
            Room room,
            PlacementRule rule,
            PlacementScorer scorer,
            Dictionary<string, FamilySymbol> perCategorySymbol,
            PlacementResult result,
            bool dryRun)
        {
            string roomKey = $"{room.Id}::{SafeRoomName(room)}";
            int alreadyInRoom = result.CountsByRoom.ContainsKey(roomKey) ? result.CountsByRoom[roomKey] : 0;

            var placedPoints = new List<XYZ>(); // for spacing scoring
            var candidates = scorer.Score(room, rule, placedPoints, alreadyInRoom);
            result.CandidatesEvaluated += candidates.Count;
            if (candidates.Count == 0) return;

            // Cap candidates to rule.MaxPerRoom (0 = unlimited)
            int cap = rule.MaxPerRoom > 0
                ? Math.Max(0, rule.MaxPerRoom - alreadyInRoom)
                : candidates.Count;
            if (cap == 0) return;

            var chosen = candidates.Take(cap).ToList();

            if (dryRun)
            {
                foreach (var c in chosen)
                {
                    result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                    result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                }
                return;
            }

            FamilySymbol symbol = ResolveSymbol(doc, rule.CategoryFilter, rule, perCategorySymbol, result);
            if (symbol == null) return;

            if (!symbol.IsActive)
            {
                try { symbol.Activate(); doc.Regenerate(); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Activate symbol {symbol.Name} failed: {ex.Message}");
                    return;
                }
            }

            foreach (var c in chosen)
            {
                try
                {
                    // Pre-flight picks the right NewFamilyInstance overload
                    // for the family's FamilyPlacementType (level-based vs
                    // hosted) and locates a host element when the family
                    // template requires one. Falls through to skip + warn
                    // when the placement type isn't supported, instead of
                    // silently creating a host-less ghost instance that
                    // schedules later miss in QTO / COBie.
                    var pf = PlacementHostPreflight.Place(doc, symbol, room, c.Position, rule);
                    FamilyInstance fi = pf.Placed;
                    if (pf.Skipped || fi == null)
                    {
                        result.SkippedCount++;
                        if (!string.IsNullOrEmpty(pf.Reason))
                            result.Warnings.Add(pf.Reason);
                        continue;
                    }

                    WriteAnchorParameters(fi, rule);
                    // Pack 123 / Gap E — stamp provenance so BOQ / cleanup /
                    // audit can identify auto-created fixtures. Centre's
                    // "Stamp provenance" checkbox flips PlaceFixturesOptions.
                    if (StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance)
                    {
                        try
                        {
                            StingTools.Core.Storage.StingProvenanceSchema.Stamp(
                                fi, "FixturePlacementEngine", rule?.MergeKey ?? "");
                        }
                        catch (Exception pvEx) { result.Warnings.Add($"Provenance stamp: {pvEx.Message}"); }
                    }
                    result.PlacedIds.Add(fi.Id);
                    result.CountsByRule[rule.MergeKey] = result.CountsByRule.TryGetValue(rule.MergeKey, out var n) ? n + 1 : 1;
                    result.CountsByRoom[roomKey] = result.CountsByRoom.TryGetValue(roomKey, out var m) ? m + 1 : 1;
                    placedPoints.Add(c.Position);
                }
                catch (Exception ex)
                {
                    result.SkippedCount++;
                    result.Warnings.Add($"Place {rule.CategoryFilter} in {SafeRoomName(room)}: {ex.Message}");
                }
            }
        }

        private static FamilySymbol ResolveSymbol(
            Document doc,
            string categoryName,
            PlacementRule rule,
            Dictionary<string, FamilySymbol> cache,
            PlacementResult result)
        {
            // PC-08 — VariantHint is a comma-separated fallback chain
            // (FLUSH,SURFACE,RECESSED) or a single regex (^IP6[5-7]$).
            // FamilyTypeRegex (optional) further refines symbol matching
            // by symbol name.
            string hint  = rule?.VariantHint ?? "";
            string ftrx  = rule?.FamilyTypeRegex ?? "";
            string cacheKey = string.IsNullOrEmpty(hint) && string.IsNullOrEmpty(ftrx)
                ? categoryName
                : $"{categoryName}|{hint}|{ftrx}";
            if (cache.TryGetValue(cacheKey, out var cached)) return cached;

            // Build matcher and ordered fallback chain.
            var chain = SplitVariantChain(hint);
            System.Text.RegularExpressions.Regex variantRx = null;
            if (chain.Count == 0 && IsRegexLike(hint))
            {
                try { variantRx = new System.Text.RegularExpressions.Regex(hint, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch (Exception ex) { result.Warnings.Add($"VariantHint regex '{hint}': {ex.Message}"); }
            }
            System.Text.RegularExpressions.Regex typeRx = null;
            if (!string.IsNullOrEmpty(ftrx))
            {
                try { typeRx = new System.Text.RegularExpressions.Regex(ftrx, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch (Exception ex) { result.Warnings.Add($"FamilyTypeRegex '{ftrx}': {ex.Message}"); }
            }

            FamilySymbol picked = null;
            FamilySymbol firstForCategory = null;
            // For chain-mode resolution, remember the best match per chain index so
            // an earlier chain entry always beats a later one.
            int bestChainIndex = int.MaxValue;
            try
            {
                var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in collector)
                {
                    if (!(el is FamilySymbol fs)) continue;
                    if (fs.Category == null) continue;
                    if (!string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // FamilyTypeRegex is an additional gate, applied to symbol name.
                    if (typeRx != null && !typeRx.IsMatch(fs.Name ?? "")) continue;

                    if (firstForCategory == null) firstForCategory = fs;
                    string variant = fs.LookupParameter("STING_FIXTURE_VARIANT_TXT")?.AsString() ?? "";

                    if (chain.Count > 0)
                    {
                        for (int i = 0; i < chain.Count && i < bestChainIndex; i++)
                        {
                            if (string.Equals(variant, chain[i], StringComparison.OrdinalIgnoreCase))
                            {
                                picked = fs;
                                bestChainIndex = i;
                                if (i == 0) goto done;
                                break;
                            }
                        }
                    }
                    else if (variantRx != null)
                    {
                        if (variantRx.IsMatch(variant))
                        {
                            picked = fs;
                            goto done;
                        }
                    }
                    else if (!string.IsNullOrEmpty(hint))
                    {
                        if (string.Equals(variant, hint, StringComparison.OrdinalIgnoreCase))
                        {
                            picked = fs;
                            goto done;
                        }
                    }
                }
            done:
                if (picked == null) picked = firstForCategory;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Resolve symbol for '{categoryName}' (hint='{hint}'): {ex.Message}");
            }

            // PC-16 — auto-load missing families from the on-disk library so a
            // project that doesn't yet contain a sample of the rule's category
            // can still be served by the engine.
            if (picked == null && firstForCategory == null)
            {
                picked = TryAutoLoadFromLibrary(doc, categoryName, hint, result);
                firstForCategory = picked;
            }

            if (picked == null)
                result.Warnings.Add($"No FamilySymbol found for category '{categoryName}' — skipping its rules.");
            else if (!string.IsNullOrEmpty(hint) && firstForCategory != null && picked == firstForCategory && bestChainIndex == int.MaxValue && variantRx == null)
                result.Warnings.Add($"No FamilySymbol matching VariantHint='{hint}' in category '{categoryName}' — using first available symbol.");

            cache[cacheKey] = picked;
            return picked;
        }

        /// <summary>
        /// PC-16 — search the on-disk family library (resolved via
        /// StingToolsApp.AssemblyPath / Families/&lt;normalised category&gt;)
        /// for a .rfa whose top-level Family.OwnerFamily.FamilyCategory.Name
        /// matches the requested category. Loads the first match into the
        /// document and returns its first symbol. Caller already owns a
        /// Transaction (the engine's "STING v4 Place Fixtures" tx).
        /// </summary>
        private static FamilySymbol TryAutoLoadFromLibrary(Document doc, string categoryName, string hint, PlacementResult result)
        {
            try
            {
                string root = StingToolsApp.AssemblyPath;
                if (string.IsNullOrEmpty(root)) return null;
                // Look in Families/ + Families/<discipline>/ — discipline is unknown here, so glob across the tree.
                string famRoot = System.IO.Path.Combine(root, "Families");
                if (!System.IO.Directory.Exists(famRoot))
                    famRoot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(root) ?? "", "Families");
                if (!System.IO.Directory.Exists(famRoot)) return null;

                // Normalise category for filename matching.
                string token = categoryName.Replace(" ", "").Replace("/", "").Replace("\\", "");
                var rfas = System.IO.Directory.EnumerateFiles(famRoot, "*.rfa", System.IO.SearchOption.AllDirectories)
                    .Where(p =>
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(p) ?? "";
                        return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf(categoryName, StringComparison.OrdinalIgnoreCase) >= 0;
                    })
                    .ToList();
                if (rfas.Count == 0) return null;

                foreach (var path in rfas)
                {
                    try
                    {
                        Family loaded;
                        if (!doc.LoadFamily(path, out loaded) || loaded == null) continue;
                        FamilySymbol first = null;
                        foreach (var symId in loaded.GetFamilySymbolIds())
                        {
                            if (doc.GetElement(symId) is FamilySymbol fs)
                            {
                                if (fs.Category != null &&
                                    string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                                {
                                    first = fs;
                                    break;
                                }
                            }
                        }
                        if (first != null)
                        {
                            result.Warnings.Add($"PC-16 auto-loaded '{System.IO.Path.GetFileName(path)}' for category '{categoryName}'.");
                            return first;
                        }
                    }
                    catch (Exception lex) { StingLog.Warn($"PC-16 LoadFamily {path}: {lex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PC-16 TryAutoLoadFromLibrary: {ex.Message}"); }
            return null;
        }

        /// <summary>PC-08 — split a comma/semicolon/pipe-separated variant
        /// fallback chain into trimmed entries. Returns empty when input
        /// is regex-like (^…$) or empty.</summary>
        private static List<string> SplitVariantChain(string hint)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(hint) || IsRegexLike(hint)) return list;
            foreach (var part in hint.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (!string.IsNullOrEmpty(t)) list.Add(t);
            }
            return list;
        }

        private static bool IsRegexLike(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.StartsWith("^", StringComparison.Ordinal)
                || s.Contains("$")
                || s.Contains("\\d")
                || s.Contains("[");
        }

        private static void WriteAnchorParameters(FamilyInstance fi, PlacementRule rule)
        {
            TrySetString(fi, ParamRegistry.PLACE_ANCHOR, rule.AnchorType);
            TrySetDoubleMm(fi, ParamRegistry.PLACE_OFFSET_X_MM, rule.OffsetXMm);
            TrySetString(fi, ParamRegistry.PLACE_SIDE, rule.SideConstraint);

            // MNT_HGT_MM may be absent on some families; swallow failure.
            TrySetDoubleMm(fi, "MNT_HGT_MM", rule.MountingHeightMm);
        }

        private static void TrySetString(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(paramName) || value == null) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: set {paramName}={value} failed: {ex.Message}"); }
        }

        private static void TrySetDoubleMm(Element el, string paramName, double valueMm)
        {
            if (string.IsNullOrEmpty(paramName)) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                switch (p.StorageType)
                {
                    case StorageType.Double:  p.Set(valueMm * MmToFt); break;
                    case StorageType.String:  p.Set(valueMm.ToString("F1")); break;
                    case StorageType.Integer: p.Set((int)Math.Round(valueMm)); break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FixturePlacementEngine: set {paramName}={valueMm}mm failed: {ex.Message}"); }
        }

        private static string SafeRoomName(Room room)
        {
            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                return p?.AsString() ?? room.Name ?? "";
            }
            catch { return ""; }
        }
    }
}
