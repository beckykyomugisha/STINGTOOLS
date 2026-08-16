// StingTools — Visibility Center · dropdown view-models
//
// A deliberately small tri-state row/group model. It follows the shape of RevitVgEditor's
// VgRow (checkbox row + All/None/Invert group header) without importing that file — the
// runner asks for the pattern, not the dependency.
//
// Two things here are load-bearing:
//   · IsChecked is bool? so a parent category with part-hidden subcategories can sit in the
//     tri-state middle instead of lying in one direction or the other.
//   · A row's OPENING state comes from VisibilityStateReader, never from a default. A row
//     that defaults to ticked asserts "this is visible", and the next Apply is computed from
//     that assertion — which is the correctness bug this pass exists to fix.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using StingTools.Core.Visibility;

namespace StingTools.UI.VisibilityCenter
{
    /// <summary>One ticked/unticked line: a category, or one value of a tag token.</summary>
    public sealed class VisRowVm : INotifyPropertyChanged
    {
        private bool? _isChecked = true;
        private bool _suppressCascade;

        /// <summary>Token value, or the category display name.</summary>
        public string Key { get; set; }

        /// <summary>BuiltInCategory int for category rows; 0 for token rows.</summary>
        public int CategoryId { get; set; }

        public int Count { get; set; }

        /// <summary>Subcategory rows nested under this one. Empty for token rows.</summary>
        public List<VisRowVm> Children { get; } = new List<VisRowVm>();

        /// <summary>Set for a child row so a tick can roll the parent's tri-state up.</summary>
        public VisRowVm Parent { get; set; }

        public bool HasChildren => Children.Count > 0;

        /// <summary>Row margin — children sit one step in from their parent.</summary>
        public System.Windows.Thickness IndentMargin =>
            new System.Windows.Thickness(Parent == null ? 0 : 16, 1, 0, 1);

        /// <summary>Elements this row and its children account for.</summary>
        public int TotalCount => Count + Children.Sum(c => c.TotalCount);

        /// <summary>"Ducts (412)".</summary>
        public string Display => $"{Key} ({TotalCount:N0})";

        /// <summary>Why this row opened unticked, for the row tooltip. Null when visible.</summary>
        public string HiddenReason { get; set; }

        /// <summary>
        /// Row tooltip. It says "Reset all, then re-apply" and NOT "re-tick and Apply",
        /// because Apply is additive — it hides what is unticked and does not un-hide what is
        /// ticked. Promising the shorter route would be a lie; un-hiding by re-tick is logged
        /// in docs/ROADMAP.md.
        /// </summary>
        public string Tooltip => HiddenReason == null
            ? null
            : $"Already hidden — {HiddenReason}.\n" +
              "Apply only ever hides. To bring this back, use 'Reset all' and re-apply what you still want hidden.";

        /// <summary>
        /// Ticked = visible. Unticking is what asks for a hide. Null is the tri-state middle:
        /// some children hidden, some not.
        /// </summary>
        public bool? IsChecked
        {
            get { return _isChecked; }
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                Raise(nameof(IsChecked));

                if (_suppressCascade) return;

                // An explicit tick supersedes what was read off the view, or the roll-up would
                // keep reasserting the opening state and a re-ticked parent could never go
                // fully on.
                if (value.HasValue) OwnHidden = !value.Value;

                // A parent tick drives its children; a child tick rolls the parent up.
                if (value.HasValue && HasChildren)
                    foreach (var c in Children) c.SetChecked(value.Value);

                Parent?.RecomputeFromChildren();
            }
        }

        /// <summary>Effective visibility for rule building: the tri-state middle counts as ticked,
        /// because a part-hidden row must not hide the half that is still visible.</summary>
        public bool IsVisible => _isChecked != false;

        /// <summary>Set without cascading — used when seeding from the read-back.</summary>
        public void SetChecked(bool? value)
        {
            _suppressCascade = true;
            try
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    Raise(nameof(IsChecked));
                }
                if (value.HasValue)
                {
                    OwnHidden = !value.Value;
                    foreach (var c in Children) c.SetChecked(value.Value);
                }
            }
            finally { _suppressCascade = false; }
        }

        /// <summary>
        /// True when this row's OWN elements are hidden, independently of its children. A
        /// parent category can be hidden while a subcategory is not (and vice versa), so the
        /// roll-up has to fold in the row's own state, not just its children's — otherwise
        /// unticking "Railings" would silently claim its visible Runs are hidden too.
        /// </summary>
        public bool OwnHidden { get; set; }

        /// <summary>Recompute the tri-state from own state + children: all on, all off, or the middle.</summary>
        public void RecomputeFromChildren()
        {
            if (!HasChildren) return;
            bool anyOn = !OwnHidden || Children.Any(c => c.IsChecked != false);
            bool anyOff = OwnHidden || Children.Any(c => c.IsChecked != true);
            bool? next = anyOn && anyOff ? (bool?)null : anyOn;

            _suppressCascade = true;
            try
            {
                if (_isChecked != next) { _isChecked = next; Raise(nameof(IsChecked)); }
            }
            finally { _suppressCascade = false; }

            Parent?.RecomputeFromChildren();
        }

        /// <summary>This row and every descendant.</summary>
        public IEnumerable<VisRowVm> SelfAndDescendants()
        {
            yield return this;
            foreach (var c in Children)
                foreach (var d in c.SelfAndDescendants()) yield return d;
        }

        public bool Matches(string search) =>
            string.IsNullOrWhiteSpace(search) ||
            (Key ?? string.Empty).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            Children.Any(c => c.Matches(search));

        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>A collapsible group: one of the three category tabs, or one of the seven tokens.</summary>
    public sealed class VisGroupVm : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private string _header;
        private string _emptyMessage;

        /// <summary>Null for the category groups; otherwise a <c>VisibilityTokens</c> key.</summary>
        public string TokenKey { get; set; }

        /// <summary>Which of the three category groups this is. Ignored for token groups.</summary>
        public CategoryGroupKind CategoryGroup { get; set; } = CategoryGroupKind.Model;

        /// <summary>Header WITHOUT the count — <see cref="Header"/> appends it.</summary>
        public string Title { get; set; }

        /// <summary>"ZONE (4)" / "LEVEL (0)" — the count is the point, so you can see what is
        /// populated without expanding all seven groups.</summary>
        public string Header
        {
            get { return _header; }
            private set { if (_header != value) { _header = value; Raise(nameof(Header)); } }
        }

        /// <summary>
        /// Why this group has no rows, e.g. "no ZONE values in this view — run tagging first".
        /// An empty expander that says nothing reads identically to a failed scan; this is the
        /// difference between a user trusting the panel and filing a bug.
        /// </summary>
        public string EmptyMessage
        {
            get { return _emptyMessage; }
            private set { if (_emptyMessage != value) { _emptyMessage = value; Raise(nameof(EmptyMessage)); Raise(nameof(EmptyVisibility)); } }
        }

        public System.Windows.Visibility EmptyVisibility =>
            string.IsNullOrEmpty(EmptyMessage) ? System.Windows.Visibility.Collapsed
                                               : System.Windows.Visibility.Visible;

        /// <summary>Token groups start collapsed and populate on first expand, so opening the
        /// dropdown on a 50k-element model does not stall.</summary>
        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                Raise(nameof(IsExpanded));
            }
        }

        /// <summary>True once rows have been harvested for this group.</summary>
        public bool IsLoaded { get; set; }

        /// <summary>How many rows this group WILL have — known before it is expanded, so the
        /// header count is honest on a collapsed group.</summary>
        public int RowCount { get; set; }

        /// <summary>Top-level rows. Children hang off <see cref="VisRowVm.Children"/>.</summary>
        public List<VisRowVm> Rows { get; set; } = new List<VisRowVm>();

        /// <summary>Rows surviving the live search box, flattened parent-then-children.</summary>
        public ObservableCollection<VisRowVm> VisibleRows { get; }
            = new ObservableCollection<VisRowVm>();

        /// <summary>Set the header count and the empty message together.</summary>
        public void SetCount(int rowCount, string emptyMessage)
        {
            RowCount = rowCount;
            Header = $"{Title} ({rowCount:N0})";
            EmptyMessage = rowCount == 0 ? emptyMessage : null;
        }

        public void ApplySearch(string search)
        {
            VisibleRows.Clear();
            foreach (var r in Rows)
            {
                if (!r.Matches(search)) continue;
                VisibleRows.Add(r);
                foreach (var c in r.Children)
                    if (c.Matches(search) || r.Key.IndexOf(search ?? string.Empty,
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                        VisibleRows.Add(c);
            }
            Raise(nameof(VisibleRows));
        }

        /// <summary>Every row in the group, parents and children.</summary>
        public IEnumerable<VisRowVm> AllRows() => Rows.SelectMany(r => r.SelfAndDescendants());

        public void SetAll(bool value)
        {
            foreach (var r in Rows) r.SetChecked(value);
        }

        public void Invert()
        {
            // Invert the LEAVES, then roll the parents up — inverting a tri-state parent and
            // its children independently would fight each other.
            foreach (var r in AllRows().Where(r => !r.HasChildren))
                r.SetChecked(r.IsChecked == false);
            foreach (var r in Rows) r.RecomputeFromChildren();
        }

        /// <summary>Rows the user has unticked — the hide set for this group.</summary>
        public List<VisRowVm> Unchecked() => AllRows().Where(r => r.IsChecked == false).ToList();

        /// <summary>Rows still ticked (tri-state middle counts as ticked) — used by Isolate.</summary>
        public List<VisRowVm> Checked() => AllRows().Where(r => r.IsVisible).ToList();

        /// <summary>True when some but not all rows are ticked (the tri-state middle).</summary>
        public bool IsPartial =>
            Rows.Count > 0 && AllRows().Any(r => r.IsVisible) && AllRows().Any(r => !r.IsVisible);

        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
