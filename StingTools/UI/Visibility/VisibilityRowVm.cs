// StingTools — Visibility Center · dropdown view-models
//
// A deliberately small tri-state row/group model. It follows the shape of RevitVgEditor's
// VgRow (checkbox row + All/None/Invert group header) without importing that file — the
// runner asks for the pattern, not the dependency.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace StingTools.UI.VisibilityCenter
{
    /// <summary>One ticked/unticked line: a category, or one value of a tag token.</summary>
    public sealed class VisRowVm : INotifyPropertyChanged
    {
        private bool _isChecked = true;

        /// <summary>Token value, or the category display name.</summary>
        public string Key { get; set; }

        /// <summary>BuiltInCategory int for category rows; 0 for token rows.</summary>
        public int CategoryId { get; set; }

        public int Count { get; set; }

        /// <summary>"Ducts (412)".</summary>
        public string Display => $"{Key} ({Count:N0})";

        /// <summary>Ticked = visible. Unticking is what asks for a hide.</summary>
        public bool IsChecked
        {
            get { return _isChecked; }
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public bool Matches(string search) =>
            string.IsNullOrWhiteSpace(search) ||
            (Key ?? string.Empty).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>A collapsible group: CATEGORIES, or one of the seven tag tokens.</summary>
    public sealed class VisGroupVm : INotifyPropertyChanged
    {
        private bool _isExpanded;

        /// <summary>Null for the categories group; otherwise a <c>VisibilityTokens</c> key.</summary>
        public string TokenKey { get; set; }

        public string Header { get; set; }

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

        public List<VisRowVm> Rows { get; set; } = new List<VisRowVm>();

        /// <summary>Rows surviving the live search box.</summary>
        public ObservableCollection<VisRowVm> VisibleRows { get; }
            = new ObservableCollection<VisRowVm>();

        public void ApplySearch(string search)
        {
            VisibleRows.Clear();
            foreach (var r in Rows.Where(r => r.Matches(search))) VisibleRows.Add(r);
            Raise(nameof(VisibleRows));
        }

        public void SetAll(bool value)
        {
            foreach (var r in Rows) r.IsChecked = value;
        }

        public void Invert()
        {
            foreach (var r in Rows) r.IsChecked = !r.IsChecked;
        }

        /// <summary>Rows the user has unticked — the hide set for this group.</summary>
        public List<VisRowVm> Unchecked() => Rows.Where(r => !r.IsChecked).ToList();

        /// <summary>Rows still ticked — the keep set, used by Isolate.</summary>
        public List<VisRowVm> Checked() => Rows.Where(r => r.IsChecked).ToList();

        /// <summary>True when some but not all rows are ticked (the tri-state middle).</summary>
        public bool IsPartial => Rows.Count > 0 && Rows.Any(r => r.IsChecked) && Rows.Any(r => !r.IsChecked);

        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
