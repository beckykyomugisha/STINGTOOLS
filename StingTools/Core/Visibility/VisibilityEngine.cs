// StingTools — Visibility Center · engine
//
// Plan() computes and writes NOTHING. Apply() performs the write. That split is what
// lets the dropdown show "will hide 1,204 of 8,331 elements" before the user commits,
// and what lets the decision-making half carry unit tests (see VisibilityRuleMatcher).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Visibility
{
    /// <summary>Plans and applies category / tag-token visibility in either mode.</summary>
    public static class VisibilityEngine
    {
        // ── Plan ────────────────────────────────────────────────────────

        /// <summary>
        /// Compute what would happen. Touches nothing — no transaction required and none
        /// started. Revit-specific impossibilities land in <see cref="VisibilityPlan.Blockers"/>
        /// rather than throwing, so the caller can show them next to the count.
        /// </summary>
        public static VisibilityPlan Plan(Document doc, View view, VisibilitySet set)
        {
            if (doc == null)
                return new VisibilityPlan { RejectReason = "No document." };
            if (set == null)
                return new VisibilityPlan { RejectReason = "No visibility set." };

            ResolveCategoryRules(doc, set);

            var harvest = TokenValueHarvester.Harvest(doc, view);
            var plan = VisibilityRuleMatcher.PlanCore(harvest.Elements, set, set.Mode);
            plan.Set = set;
            plan.ScopeCategoryIds = harvest.Categories.Select(c => c.CategoryId).Distinct().ToList();
            if (plan.IsRejected) return plan;

            AppendViewBlockers(doc, view, set, plan);

            if (plan.MatchCount == 0)
            {
                plan.Blockers.Add(set.Rules == null || set.Rules.Count == 0
                    ? "Nothing selected to hide."
                    : $"No element matched these rules (scanned {plan.TotalScanned:N0}).");
            }
            return plan;
        }

        /// <summary>
        /// Preset JSON names categories as BuiltInCategory strings ("OST_DuctCurves") so it
        /// stays readable and version-safe. Resolve those to ids + display names in place.
        /// </summary>
        internal static void ResolveCategoryRules(Document doc, VisibilitySet set)
        {
            if (doc == null || set?.Rules == null) return;

            foreach (var r in set.Rules)
            {
                if (r == null || r.Kind != VisibilityRuleKind.Category) continue;
                if (r.CategoryId != 0 && !string.IsNullOrWhiteSpace(r.CategoryName)) continue;

                try
                {
                    if (r.CategoryId == 0 && !string.IsNullOrWhiteSpace(r.CategoryName))
                    {
                        BuiltInCategory bic;
                        if (Enum.TryParse(r.CategoryName, true, out bic))
                        {
                            var cat = Category.GetCategory(doc, bic);
                            if (cat != null)
                            {
                                r.CategoryId = (int)cat.Id.Value;
                                r.CategoryName = cat.Name;   // display name, for the filter name
                            }
                        }
                    }
                    else if (r.CategoryId != 0 && string.IsNullOrWhiteSpace(r.CategoryName))
                    {
                        var cat = Category.GetCategory(doc, (BuiltInCategory)r.CategoryId);
                        if (cat != null) r.CategoryName = cat.Name;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"VisibilityEngine.ResolveCategoryRules({r.CategoryName}): {ex.Message}");
                }
            }
        }

        /// <summary>View-level conditions that stop a plan applying. Reported, never thrown.</summary>
        private static void AppendViewBlockers(Document doc, View view, VisibilitySet set, VisibilityPlan plan)
        {
            if (view == null) { plan.Blockers.Add("No active view."); return; }

            if (view.ViewType == ViewType.Legend || view.ViewType == ViewType.Schedule ||
                view.ViewType == ViewType.DrawingSheet || view.ViewType == ViewType.ColumnSchedule ||
                view.ViewType == ViewType.PanelSchedule)
            {
                plan.Blockers.Add(
                    $"A {view.ViewType} view supports neither temporary hide nor view filters. " +
                    "Open a model view (plan, section, elevation or 3D) and try again.");
                return;
            }

            if (set.Mode == VisibilityMode.Temporary)
            {
                bool can = true;
                try { can = view.CanUseTemporaryVisibilityModes(); }
                catch (Exception ex) { StingLog.Warn($"CanUseTemporaryVisibilityModes: {ex.Message}"); }
                if (!can)
                    plan.Blockers.Add("This view does not support temporary hide/isolate. Switch to 'Saved to view' mode.");
                return;
            }

            bool overrides = true;
            try { overrides = view.AreGraphicsOverridesAllowed(); }
            catch (Exception ex) { StingLog.Warn($"AreGraphicsOverridesAllowed: {ex.Message}"); }
            if (!overrides)
            {
                plan.Blockers.Add("This view does not allow graphic overrides, so a saved filter cannot be added. Use Temporary mode.");
                return;
            }

            if (FiltersLockedByTemplate(doc, view))
            {
                plan.Blockers.Add(
                    "This view's filters are controlled by its view template, so a filter added here " +
                    "would have no effect — apply to the template instead (Vis_ApplyToTemplate), " +
                    "or use Temporary mode.");
            }
        }

        /// <summary>True when a view template controls this view's V/G filters.</summary>
        internal static bool FiltersLockedByTemplate(Document doc, View view)
        {
            try
            {
                if (doc == null || view == null) return false;
                if (view.ViewTemplateId == null || view.ViewTemplateId == ElementId.InvalidElementId) return false;

                var tpl = doc.GetElement(view.ViewTemplateId) as View;
                if (tpl == null) return false;

                var nonControlled = tpl.GetNonControlledTemplateParameterIds();
                var filtersParam = new ElementId((long)BuiltInParameter.VIS_GRAPHICS_FILTERS);
                // Absent from the non-controlled set == the template controls it.
                return nonControlled != null && !nonControlled.Contains(filtersParam);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityEngine.FiltersLockedByTemplate: {ex.Message}");
                return false;
            }
        }

        // ── Apply ───────────────────────────────────────────────────────

        /// <summary>
        /// Perform the write.
        /// <para><b>Temporary mode must be called with NO open transaction</b> — Revit's
        /// temporary view modes are not transactional and throw inside one.</para>
        /// <para><b>ViewFilter mode requires an open transaction</b> owned by the caller.</para>
        /// </summary>
        public static VisibilityResult Apply(Document doc, View view, VisibilityPlan plan)
        {
            var result = new VisibilityResult();
            if (doc == null || view == null) { result.Error = "No active view."; return result; }
            if (plan == null) { result.Error = "Nothing to apply."; return result; }
            if (plan.IsRejected) { result.Error = plan.RejectReason; return result; }
            if (plan.MatchCount == 0)
            {
                result.Error = "No element matched these rules — nothing was changed.";
                result.Blockers.AddRange(plan.Blockers);
                return result;
            }

            result.Blockers.AddRange(plan.Blockers);
            result.ViewsAffected = 1;

            return plan.Mode == VisibilityMode.Temporary
                ? ApplyTemporary(view, plan, result)
                : ApplyViewFilters(doc, view, plan, result);
        }

        private static VisibilityResult ApplyTemporary(View view, VisibilityPlan plan, VisibilityResult result)
        {
            var ids = plan.MatchedIds.Select(id => new ElementId(id)).ToList();
            try
            {
                if (plan.IsIsolate) view.IsolateElementsTemporary(ids);
                else view.HideElementsTemporary(ids);

                result.Ok = true;
                result.ElementsAffected = ids.Count;
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityEngine.ApplyTemporary", ex);
                result.Error = $"Temporary {(plan.IsIsolate ? "isolate" : "hide")} failed: {ex.Message}";
            }
            return result;
        }

        private static VisibilityResult ApplyViewFilters(Document doc, View view, VisibilityPlan plan, VisibilityResult result)
        {
            int created = 0, reused = 0, applied = 0;

            try
            {
                if (plan.IsIsolate)
                {
                    var isolateId = VisibilityFilterBuilder.FindOrCreateIsolate(
                        doc, plan.Set, plan.ScopeCategoryIds, result.Blockers, ref created, ref reused);
                    if (isolateId != ElementId.InvalidElementId && AddAndHide(view, isolateId, result)) applied++;
                }
                else
                {
                    foreach (var pf in plan.Filters)
                    {
                        var id = VisibilityFilterBuilder.FindOrCreate(
                            doc, pf, plan.ScopeCategoryIds, result.Blockers, ref created, ref reused);
                        if (id != ElementId.InvalidElementId && AddAndHide(view, id, result)) applied++;
                    }
                }

                result.FiltersCreated = created;
                result.FiltersReused = reused;
                result.ElementsAffected = applied > 0 ? plan.MatchCount : 0;
                result.Ok = applied > 0;
                if (!result.Ok && string.IsNullOrEmpty(result.Error))
                    result.Error = "No filter could be applied — see the details below.";
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityEngine.ApplyViewFilters", ex);
                result.Error = $"Applying view filters failed: {ex.Message}";
            }
            return result;
        }

        private static bool AddAndHide(View view, ElementId filterId, VisibilityResult result)
        {
            try
            {
                if (!view.GetFilters().Contains(filterId)) view.AddFilter(filterId);
                view.SetFilterVisibility(filterId, false);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error($"VisibilityEngine.AddAndHide({filterId})", ex);
                result.Blockers.Add($"Could not add a filter to '{view.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply one plan to several views inside a single <see cref="TransactionGroup"/>, so a
        /// mid-run failure rolls back cleanly rather than leaving half a sheet filtered.
        /// ViewFilter mode only — temporary modes are per-view and not transactional.
        /// </summary>
        public static VisibilityResult ApplyToViews(Document doc, IList<View> views, VisibilityPlan plan)
        {
            var total = new VisibilityResult { Ok = true };
            if (doc == null || views == null || views.Count == 0)
            { total.Ok = false; total.Error = "No target views."; return total; }

            using (var tg = new TransactionGroup(doc, "STING Visibility (multi-view)"))
            {
                tg.Start();
                foreach (var v in views)
                {
                    if (v == null) continue;
                    try
                    {
                        using (var t = new Transaction(doc, $"STING Visibility — {v.Name}"))
                        {
                            t.Start();
                            var r = Apply(doc, v, plan);
                            if (!r.Ok)
                            {
                                t.RollBack();
                                total.Blockers.Add($"{v.Name}: {r.Error ?? "not applied"}");
                                continue;
                            }
                            t.Commit();
                            total.ViewsAffected++;
                            total.ElementsAffected = Math.Max(total.ElementsAffected, r.ElementsAffected);
                            total.FiltersCreated += r.FiltersCreated;
                            total.FiltersReused += r.FiltersReused;
                            foreach (var b in r.Blockers)
                                if (!total.Blockers.Contains(b)) total.Blockers.Add(b);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"VisibilityEngine.ApplyToViews({v.Name})", ex);
                        total.Blockers.Add($"{v.Name}: {ex.Message}");
                    }
                }

                if (total.ViewsAffected == 0)
                {
                    tg.RollBack();
                    total.Ok = false;
                    total.Error = "No view could be updated.";
                }
                else tg.Assimilate();
            }
            return total;
        }

        // ── Reset ───────────────────────────────────────────────────────

        /// <summary>
        /// Clear <b>both</b> mechanisms on a view, whatever <paramref name="mode"/> says:
        /// disable the temporary hide/isolate AND remove every <c>STING VIS - </c> filter.
        /// "I hit Reset and it's still hidden" is the support ticket this prevents, so the
        /// mode argument deliberately does not narrow the work.
        /// <para><b>Call with NO open transaction.</b> The two halves have opposite
        /// requirements — disabling a temporary view mode throws inside a transaction, while
        /// removing a filter needs one — so Reset sequences them itself rather than leaving
        /// that trap to every caller.</para>
        /// </summary>
        public static VisibilityResult Reset(Document doc, View view, VisibilityMode mode)
        {
            var result = new VisibilityResult { Ok = true };
            if (doc == null || view == null) { result.Ok = false; result.Error = "No active view."; return result; }

            // Phase 1 — temporary hide/isolate, outside any transaction.
            try
            {
                if (view.IsTemporaryHideIsolateActive())
                {
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    result.ElementsAffected++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityEngine.Reset temporary: {ex.Message}");
                result.Blockers.Add($"Could not clear the temporary hide: {ex.Message}");
            }

            // Phase 2 — saved filters, inside one.
            try
            {
                using (var t = new Transaction(doc, "STING Visibility — reset filters"))
                {
                    t.Start();
                    result.FiltersReused = RemoveStingFilters(view, result);
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityEngine.Reset filters", ex);
                result.Blockers.Add($"Could not remove saved filters: {ex.Message}");
            }

            result.ViewsAffected = 1;
            return result;
        }

        /// <summary>Remove every STING VIS filter from a view. Returns how many were removed.
        /// Requires an open transaction.</summary>
        internal static int RemoveStingFilters(View view, VisibilityResult result)
        {
            int removed = 0;
            try
            {
                var doc = view.Document;
                foreach (var fid in view.GetFilters().ToList())
                {
                    var pfe = doc.GetElement(fid) as ParameterFilterElement;
                    if (pfe == null || !VisibilityRuleMatcher.IsStingVisibilityFilter(pfe.Name)) continue;
                    try { view.RemoveFilter(fid); removed++; }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"RemoveFilter({pfe.Name}) on {view.Name}: {ex.Message}");
                        result?.Blockers.Add($"Could not remove '{pfe.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"VisibilityEngine.RemoveStingFilters({view?.Name})", ex);
                result?.Blockers.Add($"Could not read this view's filters: {ex.Message}");
            }
            return removed;
        }
    }
}
