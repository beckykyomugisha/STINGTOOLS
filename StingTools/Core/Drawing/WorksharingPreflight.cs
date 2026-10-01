// StingTools — Drawing Template Manager · can this item be edited at all? (DTW-195)
//
// In a workshared model one view owned by a colleague, or changed in central since the
// last reload, made the whole per-level transaction fail at commit: every type on that
// level was rolled back, Revit put up a modal dialog per item (a preset stalled on it),
// and the refused stamp read as "Run LoadSharedParams". This asks first. Before an item
// runs, the views and sheets it would reuse — found by their drawing-type and context
// stamps, the keys the producer itself reuses them by — are checked for ownership and
// for being up to date, and borrowed. An item that cannot be edited is skipped with
// the reason; the rest of the run carries on.
//
// A model that is not workshared: every call is a no-op and nothing is collected.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    internal sealed class WorksharingPreflight
    {
        private readonly Document _doc;
        private readonly bool _active;
        private Dictionary<string, List<Stamped>> _byType;   // built on first use

        private sealed class Stamped
        {
            public ElementId Id;
            public string Context;
            public bool IsSheet;
            public string Package;
        }

        private WorksharingPreflight(Document doc)
        {
            _doc = doc;
            try { _active = doc != null && doc.IsWorkshared && !doc.IsFamilyDocument; }
            catch (Exception ex) { StingLog.Warn($"WorksharingPreflight: {ex.Message} — treated as not workshared."); _active = false; }
        }

        internal static WorksharingPreflight For(Document doc) => new WorksharingPreflight(doc);

        /// <summary>True when the model is workshared and checks are made.</summary>
        internal bool Active => _active;

        /// <summary>
        /// The existing elements producing <paramref name="types"/> for
        /// <paramref name="ctx"/> would edit: each matching stamped view and sheet, the
        /// title blocks and viewports on those sheets, and — for a scope-box context on a
        /// level — the level's own plan a dependent view hangs from. Empty when the model
        /// is not workshared.
        /// </summary>
        internal ICollection<ElementId> ProductionElements(IEnumerable<DrawingType> types, DrawingContext ctx)
        {
            var ids = new HashSet<ElementId>();
            if (!_active || types == null || ctx == null) return ids;
            try
            {
                EnsureIndex();
                var contexts = new List<(string Current, string Legacy)> { Compose(ctx, includeBox: true) };
                if (ctx.ScopeBox != null && ctx.Level != null) contexts.Add(Compose(ctx, includeBox: false));
                foreach (var dt in types)
                {
                    if (dt == null || string.IsNullOrEmpty(dt.Id)) continue;
                    var keys = new List<string> { dt.Id };
                    keys.AddRange(ProductionEdgeDecisions.FormerIds(dt, ctx.FormerDrawingTypeIds));   // DTW-203
                    foreach (var key in keys)
                    {
                        if (!_byType.TryGetValue(key, out var list)) continue;
                        foreach (var s in list)
                        {
                            if (!contexts.Any(c => ProductionContextKey.Matches(s.Context, c.Current, c.Legacy))) continue;
                            if (s.IsSheet && !string.IsNullOrEmpty(ctx.PackageId) && !string.IsNullOrEmpty(s.Package)
                                && !string.Equals(s.Package, ctx.PackageId, StringComparison.OrdinalIgnoreCase)) continue;
                            ids.Add(s.Id);
                            if (s.IsSheet) AddSheetContents(s.Id, ids);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Cannot tell what the item touches: check nothing and let the failures
                // preprocessor report a refusal at commit, rather than skip on a guess.
                StingLog.Warn($"WorksharingPreflight elements: {ex.Message} — the item is not pre-checked.");
            }
            return ids;
        }

        /// <summary>Elements a sheet carries that production rewrites with it.</summary>
        private void AddSheetContents(ElementId sheetId, HashSet<ElementId> ids)
        {
            try
            {
                foreach (var e in new FilteredElementCollector(_doc, sheetId).OfCategory(BuiltInCategory.OST_TitleBlocks)
                             .WhereElementIsNotElementType())
                    ids.Add(e.Id);
                foreach (var e in new FilteredElementCollector(_doc, sheetId).OfClass(typeof(Viewport)))
                    ids.Add(e.Id);
            }
            catch (Exception ex) { StingLog.Warn($"WorksharingPreflight sheet {sheetId}: {ex.Message}"); }
        }

        /// <summary>
        /// Null when every element in <paramref name="ids"/> can be edited by this user
        /// now (borrowing what is free); otherwise why not — "owned by X", "not up to
        /// date — reload latest". Always null for a model that is not workshared.
        /// </summary>
        internal string Check(ICollection<ElementId> ids)
        {
            if (!_active || ids == null || ids.Count == 0) return null;
            var owned = new List<KeyValuePair<string, string>>();
            var stale = new List<string>();
            var toBorrow = new List<ElementId>();
            foreach (var id in ids)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                try
                {
                    var status = WorksharingUtils.GetCheckoutStatus(_doc, id, out string owner);
                    if (status == CheckoutStatus.OwnedByOtherUser)
                    {
                        owned.Add(new KeyValuePair<string, string>(Label(id), owner));
                        continue;
                    }
                    var updates = WorksharingUtils.GetModelUpdatesStatus(_doc, id);
                    if (updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral)
                    {
                        stale.Add(Label(id));
                        continue;
                    }
                    if (status == CheckoutStatus.NotOwned) toBorrow.Add(id);
                }
                catch (Exception ex) { StingLog.Warn($"WorksharingPreflight status {id}: {ex.Message}"); }
            }
            var notObtained = new List<string>();
            if (owned.Count == 0 && stale.Count == 0 && toBorrow.Count > 0)
            {
                try
                {
                    var got = new HashSet<ElementId>(WorksharingUtils.CheckoutElements(_doc, toBorrow));
                    foreach (var id in toBorrow.Where(i => !got.Contains(i)))
                    {
                        string who = null;
                        try { WorksharingUtils.GetCheckoutStatus(_doc, id, out who); }
                        catch (Exception ex) { StingLog.Warn($"WorksharingPreflight owner {id}: {ex.Message}"); }
                        if (!string.IsNullOrWhiteSpace(who)) owned.Add(new KeyValuePair<string, string>(Label(id), who));
                        else notObtained.Add(Label(id));
                    }
                }
                catch (Exception ex)
                {
                    return "editing permission could not be obtained from the central model (" + ex.Message + ")";
                }
            }
            return ProductionRunReport.BlockReason(owned, stale, notObtained);
        }

        /// <summary>
        /// DTW-220: the run-level note when new sheet numbers cannot be reserved (the
        /// counters are Extensible Storage on Project Information, owned by a colleague or
        /// changed in central), or null when they can. Status is only READ — Project
        /// Information is not borrowed here. A run whose items all reuse their sheets never
        /// writes the counters, so borrowing up front blocked colleagues until sync for
        /// nothing. The write that reserves a number edits Project Information inside the
        /// item's transaction, and Revit borrows it at that moment (CheckoutElements cannot
        /// run inside a transaction); DrawingProducer refuses an item needing a new number
        /// first, through SheetSequenceStore.WriteBlockReason. Status that cannot be read is
        /// logged and treated as writable: the per-item check and the write remain the arbiters.
        /// </summary>
        internal string SheetCountersNote()
        {
            if (!_active) return null;
            try
            {
                var pi = _doc.ProjectInformation?.Id;
                if (pi == null || pi == ElementId.InvalidElementId) return null;
                var status = WorksharingUtils.GetCheckoutStatus(_doc, pi, out string owner);
                string other = status == CheckoutStatus.OwnedByOtherUser
                    ? (string.IsNullOrWhiteSpace(owner) ? "another user" : owner)
                    : null;
                var updates = WorksharingUtils.GetModelUpdatesStatus(_doc, pi);
                bool stale = updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral;
                return ProductionEdgeDecisions.CountersNote(true, other, stale);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorksharingPreflight counters: {ex.Message} — not pre-checked; each new sheet is checked when it is numbered.");
                return null;
            }
        }

        /// <summary>Both checks for one item: null when it may run.</summary>
        internal string CheckItem(IEnumerable<DrawingType> types, DrawingContext ctx)
            => _active ? Check(ProductionElements(types, ctx)) : null;

        private string Label(ElementId id)
        {
            try
            {
                var e = _doc.GetElement(id);
                if (e is ViewSheet s) return $"{s.SheetNumber} - {s.Name}";
                if (e is ProjectInfo) return "Project Information";
                if (e is View v) return v.Name;
                if (e != null) return $"{e.Category?.Name ?? e.GetType().Name} {id.Value}";
            }
            catch (Exception ex) { StingLog.Warn($"WorksharingPreflight label {id}: {ex.Message}"); }
            return "element " + id.Value;
        }

        private static (string Current, string Legacy) Compose(DrawingContext ctx, bool includeBox)
        {
            string lvl = ctx.Level?.Name ?? "";
            long? levelId = ctx.Level?.Id.Value;
            string room = ctx.Room?.Id?.ToString() ?? "";
            string box = includeBox ? ctx.ScopeBox?.Name ?? "" : "";
            string boxUid = includeBox ? ctx.ScopeBox?.UniqueId : null;
            return (ProductionContextKey.Compose(lvl, levelId, room, ctx.Tag, box, boxUid),
                    ProductionContextKey.Legacy(lvl, room, ctx.Tag, box));
        }

        private void EnsureIndex()
        {
            if (_byType != null) return;
            var map = new Dictionary<string, List<Stamped>>(StringComparer.OrdinalIgnoreCase);
            void Add(string type, Stamped s)
            {
                if (string.IsNullOrEmpty(type)) return;
                if (!map.TryGetValue(type, out var list)) map[type] = list = new List<Stamped>();
                list.Add(s);
            }
            foreach (var el in new FilteredElementCollector(_doc).OfClass(typeof(View)))
            {
                if (!(el is View v) || v.IsTemplate || v is ViewSheet) continue;
                var type = ParameterHelpers.GetString(v, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                Add(type, new Stamped
                {
                    Id = v.Id,
                    Context = ParameterHelpers.GetString(v, ParamRegistry.STING_VIEW_CONTEXT_TAG) ?? "",
                });
            }
            foreach (var sheet in new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                var type = ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                Add(type, new Stamped
                {
                    Id = sheet.Id, IsSheet = true,
                    Context = DrawingTypeStamper.ReadSheetContext(sheet) ?? "",
                    Package = ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "",
                });
            }
            _byType = map;
        }
    }
}
