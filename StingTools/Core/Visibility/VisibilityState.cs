// StingTools — Visibility Center · read-back state model + reconciler
//
// Revit-free. VisibilityStateReader does the Revit reads (three of them: the category
// hidden flags, the view's filters, and the temporary-mode per-element test) and hands the
// raw facts here; this file turns them into per-row tick states and the footer line.
//
// Why the split: the dropdown used to default every row to ticked and so ASSERTED that
// nothing was hidden, whatever the view actually looked like. The next Apply was then
// computed from that wrong baseline. The fix has to be derived from the model on every
// open — never from a side-record of "what we hid", which desynchronises the moment the
// user reaches for Revit's own HH/HI.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Visibility
{
    /// <summary>A "STING VIS - " filter that is present on the view, and whether it is hiding.</summary>
    public sealed class AppliedFilterState
    {
        public string Name { get; set; }
        public VisibilityRuleKind Kind { get; set; }

        /// <summary>Token key for token filters; null for category filters.</summary>
        public string TokenKey { get; set; }

        /// <summary>Token value for token filters; category display name for category filters.</summary>
        public string Value { get; set; }

        /// <summary>
        /// <c>!View.GetFilterVisibility(id)</c>. A filter can be ADDED to a view and still be
        /// visible — that is a filter doing nothing, and the row must read as visible.
        /// </summary>
        public bool Hides { get; set; }
    }

    /// <summary>Which mechanism accounts for something being out of sight.</summary>
    public enum VisibilityHiddenBy
    {
        None,
        /// <summary>The view's own category visibility (V/G or a view template).</summary>
        Category,
        /// <summary>A "STING VIS - " view filter set to not-visible.</summary>
        Filter,
        /// <summary>Revit's temporary hide/isolate mode.</summary>
        Temporary
    }

    /// <summary>The tick state one dropdown row should open with.</summary>
    public sealed class VisibilityRowState
    {
        public bool IsHidden { get; set; }
        public VisibilityHiddenBy By { get; set; } = VisibilityHiddenBy.None;

        /// <summary>Elements of this row that are visible / hidden, for the tri-state middle.</summary>
        public int VisibleCount { get; set; }
        public int HiddenCount { get; set; }

        /// <summary>True when the row is part-hidden — some elements hidden, some not.</summary>
        public bool IsPartial => VisibleCount > 0 && HiddenCount > 0;

        public string Reason { get; set; }
    }

    /// <summary>Everything the dropdown needs to open showing the truth.</summary>
    public sealed class VisibilityReadback
    {
        /// <summary>Category id → state.</summary>
        public Dictionary<int, VisibilityRowState> Categories { get; set; }
            = new Dictionary<int, VisibilityRowState>();

        /// <summary>"ZONE|Z02" → state. Build the key with <see cref="TokenRowKey"/>.</summary>
        public Dictionary<string, VisibilityRowState> Tokens { get; set; }
            = new Dictionary<string, VisibilityRowState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Elements in this view's world that are currently drawn.</summary>
        public int VisibleCount { get; set; }

        /// <summary>Elements in this view's world that one of the three mechanisms is hiding.</summary>
        public int HiddenCount { get; set; }

        /// <summary>
        /// Elements the scan saw that this view would never have drawn anyway — another level,
        /// another view's view-specific content. Excluded from both counts on purpose: counting
        /// them as "hidden" is the exact over-report a raw document-vs-view diff produces.
        /// </summary>
        public int OutOfScopeCount { get; set; }

        public int TotalCount => VisibleCount + HiddenCount;

        public bool TemporaryActive { get; set; }

        /// <summary>True when at least one STING filter on the view is set to not-visible.</summary>
        public bool SavedToView { get; set; }

        /// <summary>
        /// Elements the scan attributed to a hidden category / hiding filter, and the elements
        /// only the temporary mode is hiding. Kept apart so the footer can name the mechanism.
        /// </summary>
        public int HiddenByCategory { get; set; }
        public int HiddenByFilter { get; set; }
        public int HiddenByTemporary { get; set; }

        /// <summary>
        /// Filters present on the view whose subject is not in this scan — a filter for a
        /// category or token value the model no longer contains. Surfaced rather than dropped.
        /// </summary>
        public List<string> UnmatchedFilterNames { get; set; } = new List<string>();

        public bool AnythingHidden => HiddenCount > 0 || HiddenByCategory > 0 ||
                                      HiddenByFilter > 0 || HiddenByTemporary > 0;

        public VisibilityRowState Category(int id)
        {
            VisibilityRowState s;
            return Categories.TryGetValue(id, out s) ? s : null;
        }

        public VisibilityRowState Token(string tokenKey, string value)
        {
            VisibilityRowState s;
            return Tokens.TryGetValue(TokenRowKey(tokenKey, value), out s) ? s : null;
        }

        public static string TokenRowKey(string tokenKey, string value) =>
            (tokenKey ?? string.Empty) + "|" + (value ?? VisibilityTokens.Unset);

        /// <summary>
        /// The dropdown footer when something IS hidden, e.g.
        /// <c>3 categories + ZONE Z02 hidden · 61 of 97 visible · saved to view</c>.
        /// Returns null when nothing is hidden, so the caller keeps its own "Nothing hidden" line.
        /// </summary>
        public string Footer()
        {
            if (!AnythingHidden) return null;

            var subjects = new List<string>();

            int hiddenCats = Categories.Count(kv => kv.Value != null && kv.Value.IsHidden);
            if (hiddenCats == 1)
                subjects.Add("1 category");
            else if (hiddenCats > 1)
                subjects.Add($"{hiddenCats} categories");

            // Name up to two token rows outright; past that a count reads better than a list.
            var hiddenTokens = Tokens
                .Where(kv => kv.Value != null && kv.Value.IsHidden)
                .Select(kv => kv.Key.Replace("|", " "))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (hiddenTokens.Count > 0 && hiddenTokens.Count <= 2)
                subjects.AddRange(hiddenTokens);
            else if (hiddenTokens.Count > 2)
                subjects.Add($"{hiddenTokens.Count} tag values");

            if (subjects.Count == 0)
                subjects.Add(HiddenCount == 1 ? "1 element" : $"{HiddenCount:N0} elements");

            string line = string.Join(" + ", subjects) + " hidden";
            line += $" · {VisibleCount:N0} of {TotalCount:N0} visible";

            var how = new List<string>();
            if (SavedToView) how.Add("saved to view");
            if (TemporaryActive) how.Add("temporary");
            if (HiddenByCategory > 0) how.Add("view categories");
            if (how.Count > 0) line += " · " + string.Join(" + ", how);

            if (UnmatchedFilterNames.Count > 0)
                line += $" · {UnmatchedFilterNames.Count} filter(s) match nothing here";

            return line;
        }

        /// <summary>One-line badge tooltip for the SELECT-tab button and the Hub button.</summary>
        public string BadgeTooltip()
        {
            if (!AnythingHidden)
                return "Nothing is hidden in this view by category, tag token or temporary hide.";

            var parts = new List<string>();
            if (HiddenByCategory > 0)  parts.Add($"{HiddenByCategory:N0} by hidden categories");
            if (HiddenByFilter > 0)    parts.Add($"{HiddenByFilter:N0} by saved STING filters");
            if (HiddenByTemporary > 0) parts.Add($"{HiddenByTemporary:N0} by temporary hide");

            return $"{HiddenCount:N0} of {TotalCount:N0} elements hidden in this view"
                   + (parts.Count > 0 ? " — " + string.Join(", ", parts) : "")
                   + ".\nOpen Show / Hide to see and undo it.";
        }
    }

    /// <summary>
    /// Turns raw read-back facts into row states. Pure — no Revit, no I/O.
    /// </summary>
    public static class VisibilityStateReconciler
    {
        /// <summary>
        /// Reconcile one view's state.
        /// </summary>
        /// <param name="scanned">
        /// Every element in this view's world. When nothing is hidden this is just the
        /// view-scoped harvest; when something IS hidden the caller widens it to a
        /// document-scoped scan, because a hidden element is by definition absent from a
        /// view-scoped collector and a row you cannot see is a row you cannot re-tick.
        /// </param>
        /// <param name="visibleIds">Ids the view-scoped collector returned.</param>
        /// <param name="temporarilyHiddenIds">
        /// Ids for which <c>View.IsElementVisibleInTemporaryViewMode</c> returned false. Empty
        /// when no temporary mode is active. This is derived per-element, never remembered.
        /// </param>
        /// <param name="hiddenCategoryIds"><c>View.GetCategoryHidden</c> was true for these.</param>
        /// <param name="filters">Parsed "STING VIS - " filters present on the view.</param>
        /// <param name="temporaryActive"><c>View.IsTemporaryHideIsolateActive()</c>.</param>
        public static VisibilityReadback Reconcile(
            IEnumerable<VisibilityElementSnapshot> scanned,
            ISet<long> visibleIds,
            ISet<long> temporarilyHiddenIds,
            ISet<int> hiddenCategoryIds,
            IList<AppliedFilterState> filters,
            bool temporaryActive)
        {
            var back = new VisibilityReadback { TemporaryActive = temporaryActive };

            var visible = visibleIds ?? new HashSet<long>();
            var tempHidden = temporarilyHiddenIds ?? new HashSet<long>();
            var hiddenCats = hiddenCategoryIds ?? new HashSet<int>();
            var applied = (filters ?? new List<AppliedFilterState>()).Where(f => f != null).ToList();

            var hidingFilters = applied.Where(f => f.Hides).ToList();
            back.SavedToView = hidingFilters.Count > 0;

            var matchedFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in scanned ?? Enumerable.Empty<VisibilityElementSnapshot>())
            {
                if (el == null) continue;

                bool isVisible = visible.Contains(el.Id);
                var by = VisibilityHiddenBy.None;
                string reason = null;

                if (!isVisible)
                {
                    // Attribution order matters: a category hidden in V/G explains the element
                    // even if a filter would also have caught it, and it is the cheaper fix
                    // to describe to the user.
                    if (el.CategoryId != 0 && hiddenCats.Contains(el.CategoryId))
                    {
                        by = VisibilityHiddenBy.Category;
                        reason = "category hidden in this view";
                    }
                    else
                    {
                        var hit = hidingFilters.FirstOrDefault(f => FilterMatches(f, el));
                        if (hit != null)
                        {
                            by = VisibilityHiddenBy.Filter;
                            reason = $"hidden by '{hit.Name}'";
                            matchedFilters.Add(hit.Name ?? string.Empty);
                        }
                        else if (temporaryActive && tempHidden.Contains(el.Id))
                        {
                            by = VisibilityHiddenBy.Temporary;
                            reason = "temporary hide/isolate";
                        }
                        else
                        {
                            // Not drawn, and no mechanism we track explains it — this element
                            // was never part of this view. Counting it would inflate every
                            // number on screen, so it is excluded, not guessed at.
                            back.OutOfScopeCount++;
                            continue;
                        }
                    }
                }

                if (isVisible) back.VisibleCount++;
                else
                {
                    back.HiddenCount++;
                    if (by == VisibilityHiddenBy.Category) back.HiddenByCategory++;
                    else if (by == VisibilityHiddenBy.Filter) back.HiddenByFilter++;
                    else back.HiddenByTemporary++;
                }

                if (el.CategoryId != 0)
                    Tally(RowFor(back.Categories, el.CategoryId), isVisible, by, reason);

                foreach (var token in VisibilityTokens.All)
                {
                    string raw = el.Token(token);
                    string value = VisibilityTokens.IsUnset(raw) ? VisibilityTokens.Unset : raw.Trim();
                    Tally(RowFor(back.Tokens, VisibilityReadback.TokenRowKey(token, value)),
                          isVisible, by, reason);
                }
            }

            // A category flagged hidden in V/G but with no element in the scan still deserves an
            // honest row state if the caller has a row for it.
            foreach (var id in hiddenCats)
            {
                VisibilityRowState row;
                if (!back.Categories.TryGetValue(id, out row)) continue;
                if (row.VisibleCount > 0) continue;
                row.IsHidden = true;
                row.By = VisibilityHiddenBy.Category;
                row.Reason = row.Reason ?? "category hidden in this view";
            }

            // Filters that hide a subject this scan never saw. Named, not swallowed — a stale
            // filter that matches nothing is exactly the thing a user cannot find by looking.
            foreach (var f in hidingFilters)
            {
                if (matchedFilters.Contains(f.Name ?? string.Empty)) continue;
                if (f.Kind == VisibilityRuleKind.Token && MarkTokenRow(back, f)) continue;
                back.UnmatchedFilterNames.Add(f.Name);
            }

            return back;
        }

        /// <summary>Mark a token row hidden from a filter alone. True when a row existed.</summary>
        private static bool MarkTokenRow(VisibilityReadback back, AppliedFilterState f)
        {
            VisibilityRowState row;
            if (!back.Tokens.TryGetValue(
                    VisibilityReadback.TokenRowKey(f.TokenKey, f.Value), out row))
                return false;
            if (row.VisibleCount > 0) return true;   // row exists and is visible — filter is inert here
            row.IsHidden = true;
            row.By = VisibilityHiddenBy.Filter;
            row.Reason = row.Reason ?? $"hidden by '{f.Name}'";
            return true;
        }

        /// <summary>Does this filter's subject cover this element?</summary>
        private static bool FilterMatches(AppliedFilterState f, VisibilityElementSnapshot el)
        {
            if (f == null || el == null) return false;

            if (f.Kind == VisibilityRuleKind.Category)
                return !string.IsNullOrEmpty(f.Value) &&
                       string.Equals(f.Value, el.CategoryName, StringComparison.OrdinalIgnoreCase);

            string actual = el.Token(f.TokenKey);
            if (VisibilityTokens.IsUnset(f.Value)) return VisibilityTokens.IsUnset(actual);
            if (VisibilityTokens.IsUnset(actual)) return false;
            return string.Equals(actual.Trim(), (f.Value ?? string.Empty).Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static VisibilityRowState RowFor<TKey>(
            IDictionary<TKey, VisibilityRowState> map, TKey key)
        {
            VisibilityRowState row;
            if (!map.TryGetValue(key, out row))
            {
                row = new VisibilityRowState();
                map[key] = row;
            }
            return row;
        }

        /// <summary>
        /// Fold one element into a row. A row reads as hidden only when EVERY element behind it
        /// is hidden — a part-hidden row stays ticked (and reports <c>IsPartial</c>), because
        /// unticking it on the next Apply would hide the half that is still visible.
        /// </summary>
        private static void Tally(
            VisibilityRowState row, bool isVisible, VisibilityHiddenBy by, string reason)
        {
            if (isVisible)
            {
                row.VisibleCount++;
                row.IsHidden = false;
            }
            else
            {
                row.HiddenCount++;
                if (row.VisibleCount == 0) row.IsHidden = true;
                if (row.By == VisibilityHiddenBy.None) row.By = by;
                if (row.Reason == null) row.Reason = reason;
            }
        }
    }
}
