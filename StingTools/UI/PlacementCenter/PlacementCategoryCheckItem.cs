// StingTools — Placement Centre Auto-place checklist item.
//
// Bindable wrapper around a Core.Placement.PlacementCategorySupport row. The
// Auto-place checklist used to be 19 hand-declared CheckBox fields in XAML
// with hand-written tooltips; it is now generated from
// PlacementCategoryRegistry so a category the engine cannot place renders
// disabled with the registry's own reason, and adding a category to
// STING_CATEGORY_TO_SEED_MAP.json is the only step needed to surface it.
//
// IsChecked is the only mutable state — everything else is a projection of
// the registry row and is rebuilt whenever the rule set is reloaded.

using System.ComponentModel;
using StingTools.Core.Placement;

namespace StingTools.UI.PlacementCenter
{
    public class PlacementCategoryCheckItem : INotifyPropertyChanged
    {
        private readonly PlacementCategorySupport _support;
        private bool _isChecked;

        public PlacementCategoryCheckItem(PlacementCategorySupport support)
        {
            _support = support ?? new PlacementCategorySupport();
        }

        /// <summary>Revit category name — the value handed to the engine's category filter.</summary>
        public string Category => _support.Category ?? "";

        /// <summary>Checkbox label.</summary>
        public string Display => Category;

        /// <summary>Display grouping ("Electrical", "Routing outputs", …).</summary>
        public string Group => _support.Group ?? "";

        /// <summary>
        /// False for routing outputs / host categories the engine never
        /// point-places. Bound to CheckBox.IsEnabled, so these cannot be ticked.
        /// </summary>
        public bool IsPlaceable => _support.Placeable;

        /// <summary>Tooltip — the registry's reason, not a hand-written per-category string.</summary>
        public string Reason => _support.Reason ?? "";

        /// <summary>Rules in the active pack targeting this category.</summary>
        public int RuleCount => _support.RuleCount;

        /// <summary>True when a seed family lets placement work with no manufacturer family loaded.</summary>
        public bool HasSeed => _support.HasSeed;

        /// <summary>
        /// True when the category is placeable but nothing in the active pack
        /// targets it — ticking it is legal but will place nothing. Drives the
        /// muted foreground in the checklist template.
        /// </summary>
        public bool IsInert => _support.Placeable && _support.RuleCount == 0;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                // A non-placeable category can never be ticked, even if a binding
                // or a "select all" tries: the engine would silently place nothing.
                bool next = value && IsPlaceable;
                if (_isChecked == next) return;
                _isChecked = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
