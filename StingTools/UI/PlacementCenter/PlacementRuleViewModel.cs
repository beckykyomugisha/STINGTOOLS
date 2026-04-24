// Phase 127-A — Placement Centre.
//
// Per-rule view model. Wraps a Core.Placement.PlacementRule with
// INotifyPropertyChanged so the centre's DataGrid + per-rule detail
// panels can two-way bind without re-implementing the POCO.
//
// IsDirty toggles whenever any field changes; "Save Project" only
// persists rows whose IsDirty == true OR which were added/removed.
//
// Validation lives here too — the detail panel surfaces ErrorMessage
// when the rule's invariants fail (empty CategoryFilter, negative
// MinSpacingMm, etc.). The DataGrid colours dirty rows + bad rows
// distinctly via a value-converter.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StingTools.Core.Placement;

namespace StingTools.UI.PlacementCenter
{
    public class PlacementRuleViewModel : INotifyPropertyChanged
    {
        private readonly PlacementRule _rule;
        private bool _isDirty;
        private bool _isSelected;
        private string _errorMessage = "";

        public PlacementRuleViewModel(PlacementRule rule)
        {
            _rule = rule ?? new PlacementRule();
            Validate();
        }

        // ── Reflected POCO fields ────────────────────────────────────

        public string CategoryFilter
        {
            get => _rule.CategoryFilter;
            set { if (_rule.CategoryFilter != value) { _rule.CategoryFilter = value ?? ""; MarkDirty(); } }
        }

        public string VariantHint
        {
            get => _rule.VariantHint;
            set { if (_rule.VariantHint != value) { _rule.VariantHint = value ?? ""; MarkDirty(); } }
        }

        public string RoomFilter
        {
            get => _rule.RoomFilter;
            set { if (_rule.RoomFilter != value) { _rule.RoomFilter = value ?? ""; MarkDirty(); } }
        }

        public string AnchorType
        {
            get => _rule.AnchorType;
            set { if (_rule.AnchorType != value) { _rule.AnchorType = value ?? "ROOM_CENTRE"; MarkDirty(); } }
        }

        public double OffsetXMm
        {
            get => _rule.OffsetXMm;
            set { if (Math.Abs(_rule.OffsetXMm - value) > 1e-6) { _rule.OffsetXMm = value; MarkDirty(); } }
        }

        public double MountingHeightMm
        {
            get => _rule.MountingHeightMm;
            set { if (Math.Abs(_rule.MountingHeightMm - value) > 1e-6) { _rule.MountingHeightMm = value; MarkDirty(); } }
        }

        public string SideConstraint
        {
            get => _rule.SideConstraint;
            set { if (_rule.SideConstraint != value) { _rule.SideConstraint = value ?? "EITHER"; MarkDirty(); } }
        }

        public double MinSpacingMm
        {
            get => _rule.MinSpacingMm;
            set { if (Math.Abs(_rule.MinSpacingMm - value) > 1e-6) { _rule.MinSpacingMm = value; MarkDirty(); } }
        }

        public int MaxPerRoom
        {
            get => _rule.MaxPerRoom;
            set { if (_rule.MaxPerRoom != value) { _rule.MaxPerRoom = value; MarkDirty(); } }
        }

        public int Priority
        {
            get => _rule.Priority;
            set { if (_rule.Priority != value) { _rule.Priority = value; MarkDirty(); } }
        }

        public string Notes
        {
            get => _rule.Notes;
            set { if (_rule.Notes != value) { _rule.Notes = value ?? ""; MarkDirty(); } }
        }

        // ── State ────────────────────────────────────────────────────

        public bool IsDirty
        {
            get => _isDirty;
            set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (_errorMessage != value) { _errorMessage = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsValid)); } }
        }

        public bool IsValid => string.IsNullOrEmpty(_errorMessage);

        // ── Operations ───────────────────────────────────────────────

        /// <summary>Underlying POCO — used by the loader/saver and engine bridge.</summary>
        public PlacementRule Model => _rule;

        /// <summary>MergeKey — drives uniqueness check and save grouping.</summary>
        public string MergeKey => _rule.MergeKey;

        public void ClearDirty() { IsDirty = false; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(_rule.CategoryFilter))
            { ErrorMessage = "CategoryFilter is required."; return; }
            if (_rule.MinSpacingMm < 0)
            { ErrorMessage = "MinSpacingMm cannot be negative."; return; }
            if (_rule.Priority < 0 || _rule.Priority > 100)
            { ErrorMessage = "Priority must be between 0 and 100."; return; }
            if (_rule.MaxPerRoom < 0)
            { ErrorMessage = "MaxPerRoom cannot be negative."; return; }
            ErrorMessage = "";
        }

        public PlacementRuleViewModel Clone() =>
            new PlacementRuleViewModel(_rule.Clone()) { IsDirty = true };

        // ── INPC ─────────────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void MarkDirty()
        {
            IsDirty = true;
            Validate();
            OnPropertyChanged(string.Empty); // refresh all bindings on the row
        }
    }
}
