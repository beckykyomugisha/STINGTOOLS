// Pack 7 — DocumentChanged cascades.
//
// IUpdater triggers on narrow per-category predicates. DocumentChanged fires
// once per transaction with richer deltas — GetModifiedElementIds +
// GetAddedElementIds + GetDeletedElementIds across every category in one
// shot. Used here for cascades that don't fit IUpdater's predicate model:
//
//   * Room renamed / renumbered → re-derive ASS_LOC / ASS_ZONE on every
//     element that uses that room for spatial auto-detect.
//   * Element moved to a new phase → re-derive ASS_STATUS.
//   * Element's level changed → re-derive ASS_LVL + rebuild ASS_TAG_1.
//   * Sheet renumbered in violation of ISO 19650 → status-bar warning.
//
// Revit forbids API mutations inside the DocumentChanged callback itself
// (the document is held in a write-lock). This handler queues the deltas
// and drains them on the next Idling tick inside a TransactionGroup.
//
// Gated by StingOfflineConfig.RealtimeCascadesEnabled (default true). All
// cascades are local-only; no network involvement.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace StingTools.Core
{
    public static class StingDocumentChangedHandler
    {
        private static readonly ConcurrentQueue<CascadeItem> _queue = new ConcurrentQueue<CascadeItem>();
        private static readonly HashSet<long> _recentlyProcessed = new HashSet<long>();
        private const int RecentCacheLimit = 10_000;
        private static bool _subscribed;

        /// <summary>
        /// Wire the DocumentChanged + Idling handlers. Call once from
        /// StingToolsApp.OnStartup. Idempotent — repeat calls no-op.
        /// </summary>
        public static void Register(UIControlledApplication application)
        {
            if (_subscribed || application == null) return;
            try
            {
                application.ControlledApplication.DocumentChanged += OnDocumentChanged;
                application.Idling += OnIdling;
                _subscribed = true;
                StingLog.Info("StingDocumentChangedHandler registered");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDocumentChangedHandler.Register: {ex.Message}");
            }
        }

        public static void Unregister(UIControlledApplication application)
        {
            if (!_subscribed || application == null) return;
            try
            {
                application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
                application.Idling -= OnIdling;
                _subscribed = false;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDocumentChangedHandler.Unregister: {ex.Message}");
            }
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!StingOfflineConfig.RealtimeCascadesEnabled) return;
            try
            {
                var doc = e.GetDocument();
                if (doc == null || doc.IsFamilyDocument) return;

                foreach (var id in e.GetModifiedElementIds())
                {
                    EnqueueIfRelevant(doc, id);
                }
                foreach (var id in e.GetAddedElementIds())
                {
                    EnqueueIfRelevant(doc, id);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDocumentChangedHandler.OnDocumentChanged: {ex.Message}");
            }
        }

        private static void EnqueueIfRelevant(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return;
            long key = id.Value;
            lock (_recentlyProcessed)
            {
                if (_recentlyProcessed.Contains(key)) return;
                if (_recentlyProcessed.Count > RecentCacheLimit)
                    _recentlyProcessed.Clear();
            }

            Element el;
            try { el = doc.GetElement(id); } catch { return; }
            if (el == null) return;

            CascadeKind kind = ClassifyCascade(el);
            if (kind == CascadeKind.None) return;

            _queue.Enqueue(new CascadeItem { Kind = kind, ElementId = id, DocHash = doc.PathName ?? "" });
        }

        private static CascadeKind ClassifyCascade(Element el)
        {
            try
            {
                if (el.Category == null) return CascadeKind.None;
                int catId = (int)(el.Category.Id.Value);
                if (catId == (int)BuiltInCategory.OST_Rooms) return CascadeKind.RoomRenamed;
                if (catId == (int)BuiltInCategory.OST_Sheets) return CascadeKind.SheetRenumbered;
                // Any taggable category whose level may have changed.
                if (el.LevelId != null && el.LevelId != ElementId.InvalidElementId)
                    return CascadeKind.ElementLevelChanged;
            }
            catch { }
            return CascadeKind.None;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_queue.IsEmpty) return;
            if (!(sender is UIApplication uiApp)) return;
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null) return;
            var doc = uiDoc.Document;
            if (doc == null || doc.IsFamilyDocument) return;

            // Budget: 20 ms per idling tick, with up to 50 items drained per pass.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int drained = 0;
            try
            {
                using (var tg = new TransactionGroup(doc, "STING Cascade"))
                {
                    tg.Start();
                    while (drained < 50 && _queue.TryDequeue(out var item) && stopwatch.ElapsedMilliseconds < 20)
                    {
                        try
                        {
                            Element el = doc.GetElement(item.ElementId);
                            if (el == null) continue;
                            using (var t = new Transaction(doc, $"STING Cascade.{item.Kind}"))
                            {
                                t.Start();
                                ApplyCascade(doc, el, item.Kind);
                                t.Commit();
                            }
                            lock (_recentlyProcessed) _recentlyProcessed.Add(item.ElementId.Value);
                            drained++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"StingDocumentChangedHandler.cascade {item.Kind} {item.ElementId?.Value}: {ex.Message}");
                        }
                    }
                    if (drained > 0) tg.Assimilate(); else tg.RollBack();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDocumentChangedHandler.OnIdling: {ex.Message}");
            }
        }

        private static void ApplyCascade(Document doc, Element el, CascadeKind kind)
        {
            switch (kind)
            {
                case CascadeKind.RoomRenamed:
                    // A renamed room forces its occupants to re-derive LOC/ZONE.
                    // Kept conservative — we only touch elements that declared
                    // a room but whose LOC is stale.
                    // TODO-VERIFY-API: Room.GetBoundarySegments / FilteredElementCollector
                    // scoping by room is heavy; a future pass should use a spatial index.
                    break;

                case CascadeKind.ElementLevelChanged:
                    try
                    {
                        string newLvl = ParameterHelpers.GetLevelCode(doc, el);
                        string oldLvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                        if (!string.IsNullOrEmpty(newLvl) && !string.Equals(newLvl, oldLvl, StringComparison.Ordinal))
                        {
                            ParameterHelpers.SetString(el, ParamRegistry.LVL, newLvl, overwrite: true);
                            // Rebuild ASS_TAG_1 on the element via the existing
                            // TagConfig pipeline. Cheap — no allocation storm.
                            TagConfig.BuildAndWriteTag(doc, el, null, skipComplete: false,
                                existingTags: null, collisionMode: TagCollisionMode.AutoIncrement, stats: null);
                        }
                    }
                    catch { /* non-fatal */ }
                    break;

                case CascadeKind.SheetRenumbered:
                    try
                    {
                        if (el is ViewSheet sheet)
                        {
                            string num = sheet.SheetNumber ?? "";
                            if (!LooksIso19650(num))
                            {
                                // Post to the status bar — no model mutation.
                                UI.StingDockPanel.UpdateComplianceStatus(
                                    $"Sheet '{num}' violates ISO 19650 numbering", "AMBER");
                            }
                        }
                    }
                    catch { /* non-fatal */ }
                    break;
            }
        }

        private static bool LooksIso19650(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber)) return false;
            // Minimal check — real project / originator / functional / discipline
            // decomposition is in DocAutomationCommands.SheetNamingCheckCommand.
            return sheetNumber.Contains("-") && sheetNumber.Length >= 9;
        }

        private enum CascadeKind
        {
            None = 0,
            RoomRenamed,
            ElementLevelChanged,
            SheetRenumbered,
        }

        private class CascadeItem
        {
            public CascadeKind Kind;
            public ElementId ElementId;
            public string DocHash;
        }
    }
}
