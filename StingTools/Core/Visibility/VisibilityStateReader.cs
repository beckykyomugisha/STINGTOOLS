// StingTools — Visibility Center · state reader (Revit-bound)
//
// Reads what a view is ALREADY hiding, so the dropdown can open showing the truth instead
// of asserting that everything is visible.
//
// ── The trap ────────────────────────────────────────────────────────────────────────────
// Revit exposes NO API to enumerate temporarily hidden elements. IsTemporaryHideIsolateActive()
// tells you the mode is on, not what it hid. So:
//
//   · a FilteredElementCollector scoped to a VIEW honours temporary hide/isolate;
//   · one scoped to the DOCUMENT does not;
//   · the difference is the candidate set — but on a plan view that difference is dominated
//     by elements the view would never have drawn anyway (other levels, other views'
//     view-specific content), so the raw difference massively over-reports.
//
// Each candidate is therefore confirmed with View.IsElementVisibleInTemporaryViewMode, which
// answers precisely "is the temporary mode hiding THIS element". Everything left unexplained
// is reported as out-of-scope rather than counted as hidden.
//
// What this deliberately does NOT do is keep a side-record of "what we hid". That
// desynchronises the moment the user reaches for Revit's own HH/HI, and a reader that
// disagrees with the model is worse than no reader at all. Derived from the model, every time.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Visibility
{
    /// <summary>The harvest the dropdown should render, and the state each row should open in.</summary>
    public sealed class VisibilityStateResult
    {
        /// <summary>
        /// Rows to render. Widened past the view-scoped harvest when something is hidden, so a
        /// hidden category still has a row the user can re-tick.
        /// </summary>
        public TokenHarvest Harvest { get; set; } = new TokenHarvest();

        public VisibilityReadback Readback { get; set; } = new VisibilityReadback();

        /// <summary>"STING VIS - " filters found on the view, hiding or not.</summary>
        public List<AppliedFilterState> Filters { get; set; } = new List<AppliedFilterState>();
    }

    /// <summary>Reads a view's current visibility state. Writes nothing; needs no transaction.</summary>
    public static class VisibilityStateReader
    {
        /// <summary>
        /// Read <paramref name="view"/>'s state. Cheap when nothing is hidden: one view-scoped
        /// harvest (which the dropdown needs anyway) plus a category-flag sweep. The expensive
        /// document-scoped pass runs only when the cheap sweep proves something IS hidden.
        /// </summary>
        public static VisibilityStateResult Read(Document doc, View view)
        {
            var result = new VisibilityStateResult();
            if (doc == null || view == null) return result;

            TokenHarvest visible;
            try
            {
                visible = TokenValueHarvester.Harvest(doc, view);
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityStateReader: view harvest", ex);
                return result;
            }
            result.Harvest = visible;

            bool temporaryActive = false;
            try { temporaryActive = view.IsTemporaryHideIsolateActive(); }
            catch (Exception ex) { StingLog.Warn($"VisibilityStateReader.IsTemporaryHideIsolateActive: {ex.Message}"); }

            result.Filters = ReadFilters(doc, view);
            var hiddenCats = ReadHiddenCategories(doc, view);

            bool anyHidingFilter = result.Filters.Any(f => f.Hides);
            bool needFullScan = temporaryActive || anyHidingFilter || hiddenCats.Count > 0;

            var visibleIds = new HashSet<long>(visible.Elements.Select(e => e.Id));

            if (!needFullScan)
            {
                // Nothing can be hidden, so the view-scoped scan IS the whole world. No
                // document pass, no per-element API calls.
                result.Readback = VisibilityStateReconciler.Reconcile(
                    visible.Elements, visibleIds,
                    new HashSet<long>(), hiddenCats, result.Filters, false);
                return result;
            }

            TokenHarvest all;
            try
            {
                all = TokenValueHarvester.Harvest(doc, null);
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityStateReader: document harvest", ex);
                // Degrade to the view-scoped truth rather than reporting a state we did not read.
                result.Readback = VisibilityStateReconciler.Reconcile(
                    visible.Elements, visibleIds,
                    new HashSet<long>(), hiddenCats, result.Filters, temporaryActive);
                return result;
            }

            var tempHidden = temporaryActive
                ? ReadTemporarilyHidden(view, all.Elements, visibleIds)
                : new HashSet<long>();

            result.Readback = VisibilityStateReconciler.Reconcile(
                all.Elements, visibleIds, tempHidden, hiddenCats, result.Filters, temporaryActive);

            // Render rows for everything in this view's world — visible plus hidden — but not
            // for the out-of-scope remainder the reconciler already discounted.
            var inScope = InScope(all.Elements, visibleIds, tempHidden, hiddenCats,
                                  result.Filters, temporaryActive);
            result.Harvest = TokenHarvest.Rebuild(inScope, all.Categories.Concat(visible.Categories));
            result.Harvest.ExcludedCategories = visible.ExcludedCategories;

            return result;
        }

        /// <summary>
        /// The cheap read used for the SELECT-tab badge between full reads: is anything hidden
        /// at all, and by what. No element scan.
        /// </summary>
        public static bool AnythingHidden(Document doc, View view)
        {
            if (doc == null || view == null) return false;
            try
            {
                if (view.IsTemporaryHideIsolateActive()) return true;
            }
            catch (Exception ex) { StingLog.Warn($"VisibilityStateReader.AnythingHidden temp: {ex.Message}"); }

            if (ReadFilters(doc, view).Any(f => f.Hides)) return true;
            return ReadHiddenCategories(doc, view).Count > 0;
        }

        // ── Revit reads ─────────────────────────────────────────────────

        /// <summary>"STING VIS - " filters on the view, parsed back through the naming contract.</summary>
        internal static List<AppliedFilterState> ReadFilters(Document doc, View view)
        {
            var list = new List<AppliedFilterState>();
            try
            {
                foreach (var fid in view.GetFilters())
                {
                    var pfe = doc.GetElement(fid) as ParameterFilterElement;
                    if (pfe == null || !VisibilityRuleMatcher.IsStingVisibilityFilter(pfe.Name)) continue;

                    VisibilityRuleKind kind;
                    string tokenKey, value;
                    if (!VisibilityRuleMatcher.TryParseFilterName(pfe.Name, out kind, out tokenKey, out value))
                    {
                        // The combined isolate filter ("STING VIS - NOT (isolate)") does not
                        // round-trip by design. Record it so it still counts as hiding.
                        list.Add(new AppliedFilterState
                        {
                            Name = pfe.Name,
                            Kind = VisibilityRuleKind.Category,
                            Value = null,
                            Hides = !GetFilterVisibility(view, fid)
                        });
                        continue;
                    }

                    list.Add(new AppliedFilterState
                    {
                        Name = pfe.Name,
                        Kind = kind,
                        TokenKey = tokenKey,
                        Value = value,
                        Hides = !GetFilterVisibility(view, fid)
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityStateReader.ReadFilters({view?.Name}): {ex.Message}");
            }
            return list;
        }

        private static bool GetFilterVisibility(View view, ElementId fid)
        {
            // Optional read: a filter can be present on a template-controlled view where the
            // per-view query throws. Treating that as "visible" understates rather than
            // inventing a hide, which is the safer direction for a read-back.
            try { return view.GetFilterVisibility(fid); }
            catch (Exception ex)
            {
                StingLog.Warn($"GetFilterVisibility({fid}) on '{view?.Name}': {ex.Message}");
                return true;
            }
        }

        /// <summary>Categories the view is hiding, whoever hid them — V/G, a template, or us.</summary>
        internal static HashSet<int> ReadHiddenCategories(Document doc, View view)
        {
            var hidden = new HashSet<int>();
            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    Collect(view, cat, hidden);
                    try
                    {
                        foreach (Category sub in cat.SubCategories) Collect(view, sub, hidden);
                    }
                    catch { /* optional read — many categories expose no SubCategories */ }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityStateReader.ReadHiddenCategories: {ex.Message}");
            }
            return hidden;
        }

        private static void Collect(View view, Category cat, HashSet<int> hidden)
        {
            try
            {
                if (!view.CanCategoryBeHidden(cat.Id)) return;
                if (view.GetCategoryHidden(cat.Id)) hidden.Add((int)cat.Id.Value);
            }
            catch { /* optional read — GetCategoryHidden throws for categories a view cannot control */ }
        }

        /// <summary>
        /// Ids the temporary mode is hiding. Only elements the view-scoped collector did NOT
        /// return are candidates; each is confirmed individually, so an element that was never
        /// in this view is not mistaken for one that was hidden.
        /// </summary>
        internal static HashSet<long> ReadTemporarilyHidden(
            View view, IList<VisibilityElementSnapshot> all, HashSet<long> visibleIds)
        {
            var hidden = new HashSet<long>();
            try
            {
                foreach (var el in all)
                {
                    if (el == null || visibleIds.Contains(el.Id)) continue;
                    try
                    {
                        if (!view.IsElementVisibleInTemporaryViewMode(
                                TemporaryViewMode.TemporaryHideIsolate, new ElementId(el.Id)))
                            hidden.Add(el.Id);
                    }
                    catch { /* optional read — the API throws for element kinds a view cannot host */ }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VisibilityStateReader.ReadTemporarilyHidden({view?.Name}): {ex.Message}");
            }
            return hidden;
        }

        /// <summary>
        /// Elements belonging to this view's world: drawn, or hidden by a mechanism we read.
        /// Mirrors the reconciler's attribution exactly so the rendered rows and the counts
        /// can never disagree.
        /// </summary>
        private static List<VisibilityElementSnapshot> InScope(
            IList<VisibilityElementSnapshot> all,
            HashSet<long> visibleIds,
            HashSet<long> tempHidden,
            HashSet<int> hiddenCats,
            IList<AppliedFilterState> filters,
            bool temporaryActive)
        {
            var hiding = filters.Where(f => f != null && f.Hides).ToList();
            var kept = new List<VisibilityElementSnapshot>();

            foreach (var el in all)
            {
                if (el == null) continue;
                if (visibleIds.Contains(el.Id)) { kept.Add(el); continue; }
                if (el.CategoryId != 0 && hiddenCats.Contains(el.CategoryId)) { kept.Add(el); continue; }
                if (temporaryActive && tempHidden.Contains(el.Id)) { kept.Add(el); continue; }
                if (hiding.Any(f => FilterCovers(f, el))) kept.Add(el);
            }
            return kept;
        }

        private static bool FilterCovers(AppliedFilterState f, VisibilityElementSnapshot el)
        {
            if (f.Kind == VisibilityRuleKind.Category)
                return !string.IsNullOrEmpty(f.Value) &&
                       string.Equals(f.Value, el.CategoryName, StringComparison.OrdinalIgnoreCase);

            string actual = el.Token(f.TokenKey);
            if (VisibilityTokens.IsUnset(f.Value)) return VisibilityTokens.IsUnset(actual);
            if (VisibilityTokens.IsUnset(actual)) return false;
            return string.Equals(actual.Trim(), (f.Value ?? string.Empty).Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }
    }
}
