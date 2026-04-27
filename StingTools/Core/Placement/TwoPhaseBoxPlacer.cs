// Phase 139.2 — Two-phase conduiting box placer.
//
// First-fix conduit boxes are placed in the Construction phase and
// stamped with STING_BOX_LOCATION_ID (a fresh GUID). Second-fix
// devices, placed later in the Completion phase, look up their
// matching first-fix box by XYZ proximity within ToleranceMm and
// inherit the same ID — a stable two-phase link that survives moves
// and re-creation.
//
// Caller owns the Transaction. Both passes are no-ops when a rule
// does not declare TwoPhaseEnabled.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Placement
{
    public static class TwoPhaseBoxPlacer
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Pre-flight check that the BoxLocationIdParam shared parameter
        /// is bound on the document. Returns true when bound, false otherwise.
        /// Emits a warning when not bound — caller continues in degraded mode.</summary>
        public static bool ValidateSharedParams(Document doc, IList<PlacementRule> rules, List<string> warnings)
        {
            if (doc == null || rules == null) return false;
            // Did any rule actually declare two-phase usage?
            bool any = rules.Any(r => r != null && r.TwoPhaseEnabled);
            if (!any) return true;

            string paramName = rules.FirstOrDefault(r => r != null && !string.IsNullOrEmpty(r.BoxLocationIdParam))
                                    ?.BoxLocationIdParam ?? ParamRegistry.BOX_LOCATION_ID;

            try
            {
                bool found = false;
                var col = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement));
                foreach (var el in col)
                {
                    if (el is SharedParameterElement spe && string.Equals(spe.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    { found = true; break; }
                }
                if (!found)
                {
                    string msg = $"TwoPhaseBoxPlacer: shared parameter '{paramName}' not bound on this project. Two-phase matching will run in degraded mode.";
                    warnings?.Add(msg);
                    StingLog.Warn(msg);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                warnings?.Add($"TwoPhaseBoxPlacer.ValidateSharedParams: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PASS 1 — Construction phase. Places first-fix boxes for every
        /// TwoPhaseEnabled rule and stamps each with a fresh GUID. Returns
        /// dictionary keyed by GUID → XYZ for the matching pass.
        /// </summary>
        public static Dictionary<string, XYZ> PlaceFirstFixBoxes(
            Document doc,
            IList<ElementId> roomIds,
            IList<PlacementRule> rules,
            PlacementResult result)
        {
            var index = new Dictionary<string, XYZ>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || rules == null) return index;

            var firstFixRules = rules.Where(r => r != null && r.TwoPhaseEnabled
                                                && !string.IsNullOrEmpty(r.ConstructionPhase)
                                                && !string.IsNullOrEmpty(r.BoxFamilyTypeRegex)).ToList();
            if (firstFixRules.Count == 0) return index;

            var rooms = CollectRooms(doc, roomIds);
            if (rooms.Count == 0) return index;

            var scorer = new PlacementScorer(doc);
            var perCategorySymbol = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in firstFixRules)
            {
                Phase phase = ResolvePhase(doc, rule.ConstructionPhase);
                FamilySymbol symbol = ResolveBoxSymbol(doc, rule);
                if (symbol == null)
                {
                    result?.Warnings.Add($"TwoPhase: no first-fix box family matched '{rule.BoxFamilyTypeRegex}' for rule {rule.MergeKey}.");
                    continue;
                }
                if (!symbol.IsActive) { try { symbol.Activate(); doc.Regenerate(); } catch { continue; } }

                foreach (var room in rooms)
                {
                    var candidates = scorer.Score(room, rule, new List<XYZ>(), 0);
                    if (candidates.Count == 0) continue;
                    int cap = rule.MaxPerRoom > 0 ? Math.Min(rule.MaxPerRoom, candidates.Count) : candidates.Count;
                    foreach (var cand in candidates.Take(cap))
                    {
                        try
                        {
                            var pf = PlacementHostPreflight.Place(doc, symbol, room, cand.Position, rule);
                            if (pf.Skipped || pf.Placed == null)
                            {
                                if (!string.IsNullOrEmpty(pf.Reason)) result?.Warnings.Add(pf.Reason);
                                continue;
                            }
                            // Phase 139.3 — encode workset id alongside the GUID so the
                            // matcher survives a coordination-model swap that re-creates
                            // boxes on a different workset. Format: "<guid>|ws=<wsId>".
                            string guid = Guid.NewGuid().ToString();
                            string wsTag = "";
                            try
                            {
                                if (doc.IsWorkshared && pf.Placed.WorksetId != null)
                                    wsTag = "|ws=" + pf.Placed.WorksetId.IntegerValue;
                            }
                            catch { }
                            string composite = guid + wsTag;
                            string paramName = string.IsNullOrEmpty(rule.BoxLocationIdParam)
                                ? ParamRegistry.BOX_LOCATION_ID : rule.BoxLocationIdParam;
                            TrySetString(pf.Placed, paramName, composite);
                            TrySetPhase(pf.Placed, phase);
                            index[composite] = cand.Position;
                            result?.PlacedIds.Add(pf.Placed.Id);
                            string ck = "two-phase:first-fix:" + rule.MergeKey;
                            result.CountsByRule[ck] = result.CountsByRule.TryGetValue(ck, out var n) ? n + 1 : 1;
                            // Phase 139.4 — fire post-placement hooks so two-phase
                            // boxes get the same data-tag / COBie / system pipeline
                            // as a normal Place run. RunFor swallows its own errors.
                            try { PostPlacementHooks.RunFor(pf.Placed, rule); }
                            catch (Exception hkEx) { result?.Warnings.Add($"PostHook(first-fix): {hkEx.Message}"); }
                        }
                        catch (Exception ex)
                        {
                            result?.Warnings.Add($"TwoPhase.PlaceFirstFix {rule.MergeKey} in room {room.Id}: {ex.Message}");
                            if (result != null) result.SkippedCount++;
                        }
                    }
                }
            }
            return index;
        }

        /// <summary>
        /// PASS 2 — Completion phase. For each TwoPhaseEnabled rule, look
        /// up the nearest entry in firstFixIndex within ToleranceMm, place
        /// the device at the box XYZ, and copy the GUID into the device's
        /// BoxLocationIdParam.
        /// </summary>
        public static void PlaceSecondFixDevices(
            Document doc,
            IList<ElementId> roomIds,
            IList<PlacementRule> rules,
            Dictionary<string, XYZ> firstFixIndex,
            PlacementResult result)
        {
            if (doc == null || rules == null || firstFixIndex == null || firstFixIndex.Count == 0) return;

            var secondFixRules = rules.Where(r => r != null && r.TwoPhaseEnabled
                                                && !string.IsNullOrEmpty(r.CompletionPhase)).ToList();
            if (secondFixRules.Count == 0) return;

            var rooms = CollectRooms(doc, roomIds);
            if (rooms.Count == 0) return;

            var perCategorySymbol = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Phase 139.2 F9 — pre-bucket first-fix entries by containing room
            // so the inner loop is O(boxes-in-room) instead of O(all-boxes).
            // Use a generous tolerance (1 ft) for the bucket step; the per-rule
            // ToleranceMm is applied only as the placement match threshold.
            const double bucketSlackFt = 1.0;
            var roomBboxes = new Dictionary<ElementId, BoundingBoxXYZ>();
            foreach (var rm in rooms)
            {
                try { roomBboxes[rm.Id] = rm.get_BoundingBox(null); } catch { }
            }
            var indexByRoom = new Dictionary<ElementId, List<KeyValuePair<string, XYZ>>>();
            foreach (var rm in rooms) indexByRoom[rm.Id] = new List<KeyValuePair<string, XYZ>>();
            foreach (var kv in firstFixIndex)
            {
                foreach (var rm in rooms)
                {
                    if (FastRoomContains(rm, roomBboxes.TryGetValue(rm.Id, out var bb) ? bb : null, kv.Value, bucketSlackFt))
                    {
                        indexByRoom[rm.Id].Add(kv);
                        break;
                    }
                }
            }

            foreach (var rule in secondFixRules)
            {
                Phase phase = ResolvePhase(doc, rule.CompletionPhase);
                FamilySymbol symbol = ResolveDeviceSymbol(doc, rule, perCategorySymbol);
                if (symbol == null)
                {
                    result?.Warnings.Add($"TwoPhase.SecondFix: no device family for rule {rule.MergeKey} (cat='{rule.CategoryFilter}').");
                    continue;
                }
                if (!symbol.IsActive) { try { symbol.Activate(); doc.Regenerate(); } catch { continue; } }

                double toleranceFt = Math.Max(rule.ToleranceMm, 50.0) * MmToFt;

                foreach (var room in rooms)
                {
                    if (!indexByRoom.TryGetValue(room.Id, out var roomEntries)) continue;
                    foreach (var entry in roomEntries)
                    {
                        if (consumed.Contains(entry.Key)) continue;
                        var pos = entry.Value;

                        try
                        {
                            var pf = PlacementHostPreflight.Place(doc, symbol, room, pos, rule);
                            if (pf.Skipped || pf.Placed == null)
                            {
                                if (!string.IsNullOrEmpty(pf.Reason)) result?.Warnings.Add(pf.Reason);
                                continue;
                            }
                            string paramName = string.IsNullOrEmpty(rule.BoxLocationIdParam)
                                ? ParamRegistry.BOX_LOCATION_ID : rule.BoxLocationIdParam;
                            TrySetString(pf.Placed, paramName, entry.Key);
                            TrySetPhase(pf.Placed, phase);
                            consumed.Add(entry.Key);
                            result?.PlacedIds.Add(pf.Placed.Id);
                            string ck = "two-phase:second-fix:" + rule.MergeKey;
                            result.CountsByRule[ck] = result.CountsByRule.TryGetValue(ck, out var n) ? n + 1 : 1;
                            try { PostPlacementHooks.RunFor(pf.Placed, rule); }
                            catch (Exception hkEx) { result?.Warnings.Add($"PostHook(second-fix): {hkEx.Message}"); }
                        }
                        catch (Exception ex)
                        {
                            result?.Warnings.Add($"TwoPhase.PlaceSecondFix {rule.MergeKey}: {ex.Message}");
                            if (result != null) result.SkippedCount++;
                        }
                    }
                }
            }

            // Report unmatched first-fix boxes.
            foreach (var entry in firstFixIndex)
            {
                if (!consumed.Contains(entry.Key))
                {
                    result?.Warnings.Add($"TwoPhase: first-fix box {entry.Key} at {Fmt(entry.Value)} has no matching second-fix device.");
                }
            }
        }

        // ── Internal ────────────────────────────────────────────────

        private static List<Room> CollectRooms(Document doc, IList<ElementId> roomIds)
        {
            var rooms = new List<Room>();
            try
            {
                if (roomIds == null || roomIds.Count == 0)
                {
                    var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType();
                    foreach (var e in col) if (e is Room r && r.Area > 0.0) rooms.Add(r);
                }
                else
                {
                    foreach (var id in roomIds) if (doc.GetElement(id) is Room r && r.Area > 0.0) rooms.Add(r);
                }
            }
            catch { }
            return rooms;
        }

        private static FamilySymbol ResolveBoxSymbol(Document doc, PlacementRule rule)
        {
            if (string.IsNullOrEmpty(rule?.BoxFamilyTypeRegex)) return null;
            System.Text.RegularExpressions.Regex rx;
            try { rx = new System.Text.RegularExpressions.Regex(rule.BoxFamilyTypeRegex,
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return null; }

            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                {
                    if (!(el is FamilySymbol fs)) continue;
                    string famName = fs.Family?.Name ?? "";
                    string typeName = fs.Name ?? "";
                    if (rx.IsMatch(famName) || rx.IsMatch(typeName) || rx.IsMatch(famName + ":" + typeName))
                        return fs;
                }
            }
            catch { }
            return null;
        }

        private static FamilySymbol ResolveDeviceSymbol(Document doc, PlacementRule rule,
            Dictionary<string, FamilySymbol> cache)
        {
            if (string.IsNullOrEmpty(rule?.CategoryFilter)) return null;
            if (cache.TryGetValue(rule.CategoryFilter, out var hit)) return hit;

            // Compile FamilyTypeRegex once outside the symbol loop.
            System.Text.RegularExpressions.Regex typeRx = null;
            if (!string.IsNullOrEmpty(rule.FamilyTypeRegex))
            {
                try { typeRx = new System.Text.RegularExpressions.Regex(rule.FamilyTypeRegex,
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch { typeRx = null; }
            }

            try
            {
                FamilySymbol first = null;
                var col = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                {
                    if (!(el is FamilySymbol fs)) continue;
                    if (fs.Category == null) continue;
                    if (!string.Equals(fs.Category.Name, rule.CategoryFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    if (first == null) first = fs;
                    if (typeRx != null && typeRx.IsMatch(fs.Name ?? ""))
                    {
                        cache[rule.CategoryFilter] = fs;
                        return fs;
                    }
                }
                cache[rule.CategoryFilter] = first;
                return first;
            }
            catch { return null; }
        }

        private static Phase ResolvePhase(Document doc, string phaseName)
        {
            if (doc == null || string.IsNullOrEmpty(phaseName)) return null;
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(Phase));
                foreach (var el in col)
                    if (el is Phase p && string.Equals(p.Name, phaseName, StringComparison.OrdinalIgnoreCase))
                        return p;
            }
            catch { }
            return null;
        }

        private static bool RoomContains(Room room, XYZ pt, double slackFt)
        {
            if (room == null || pt == null) return false;
            try
            {
                if (room.IsPointInRoom(pt)) return true;
                var bb = room.get_BoundingBox(null);
                return FastBboxContains(bb, pt, slackFt);
            }
            catch { return false; }
        }

        // Bbox-only containment using a pre-fetched BoundingBoxXYZ. Cheaper
        // than RoomContains when the caller has already cached bboxes.
        private static bool FastRoomContains(Room room, BoundingBoxXYZ bb, XYZ pt, double slackFt)
        {
            if (room == null || pt == null) return false;
            return FastBboxContains(bb, pt, slackFt);
        }

        private static bool FastBboxContains(BoundingBoxXYZ bb, XYZ pt, double slackFt)
        {
            if (bb == null || pt == null) return false;
            return pt.X >= bb.Min.X - slackFt && pt.X <= bb.Max.X + slackFt
                && pt.Y >= bb.Min.Y - slackFt && pt.Y <= bb.Max.Y + slackFt;
        }

        private static void TrySetString(Element el, string paramName, string value)
        {
            if (el == null || string.IsNullOrEmpty(paramName) || value == null) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"TwoPhaseBoxPlacer: set {paramName}={value}: {ex.Message}"); }
        }

        private static void TrySetPhase(Element el, Phase phase)
        {
            if (el == null || phase == null) return;
            try
            {
                var pCreated = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (pCreated != null && !pCreated.IsReadOnly) pCreated.Set(phase.Id);
            }
            catch (Exception ex) { StingLog.Warn($"TwoPhaseBoxPlacer: set phase: {ex.Message}"); }
        }

        private static string Fmt(XYZ p) => p == null ? "(null)" : $"({p.X:F2},{p.Y:F2},{p.Z:F2})";
    }
}
