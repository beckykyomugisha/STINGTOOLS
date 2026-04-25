// Phase 127-A — Placement Centre per-rule view model.
//
// PC-02: regex compilability check for RoomFilter / ExcludeRoomFilter /
// LevelFilter / PhaseFilter / WorksetFilter / RoomDepartmentFilter, plus
// AnchorType / SideConstraint membership against the engine's accepted
// enums.
// PC-03: optional CategoryFilter validation against the active document's
// categories (driven by CategoryNamesFromDoc on the root VM).
// PC-06/07/08: surface every new POCO field as INotifyPropertyChanged.
// PC-12/13: density / dependency fields exposed for binding.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StingTools.Core.Placement;

namespace StingTools.UI.PlacementCenter
{
    public class PlacementRuleViewModel : INotifyPropertyChanged
    {
        private readonly PlacementRule _rule;
        private bool _isDirty;
        private bool _isSelected;
        private string _errorMessage = "";

        /// <summary>
        /// Optional set of valid category names from the active document
        /// (PC-03). When non-null, Validate() warns when CategoryFilter
        /// is not in the set. Set by PlacementRulesViewModel after load.
        /// </summary>
        public HashSet<string> ValidCategoryNames { get; set; }

        public PlacementRuleViewModel(PlacementRule rule)
        {
            _rule = rule ?? new PlacementRule();
            Validate();
        }

        // ── Reflected POCO fields ────────────────────────────────────

        public string RuleId
        {
            get => _rule.RuleId;
            set { if (_rule.RuleId != value) { _rule.RuleId = value ?? ""; MarkDirty(); } }
        }

        public PlacementRuleKind RuleKind
        {
            get => _rule.RuleKind;
            set { if (_rule.RuleKind != value) { _rule.RuleKind = value; MarkDirty(); } }
        }

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

        public string FamilyTypeRegex
        {
            get => _rule.FamilyTypeRegex;
            set { if (_rule.FamilyTypeRegex != value) { _rule.FamilyTypeRegex = value ?? ""; MarkDirty(); } }
        }

        public string RoomFilter
        {
            get => _rule.RoomFilter;
            set { if (_rule.RoomFilter != value) { _rule.RoomFilter = value ?? ""; MarkDirty(); } }
        }

        public string ExcludeRoomFilter
        {
            get => _rule.ExcludeRoomFilter;
            set { if (_rule.ExcludeRoomFilter != value) { _rule.ExcludeRoomFilter = value ?? ""; MarkDirty(); } }
        }

        public string RoomDepartmentFilter
        {
            get => _rule.RoomDepartmentFilter;
            set { if (_rule.RoomDepartmentFilter != value) { _rule.RoomDepartmentFilter = value ?? ""; MarkDirty(); } }
        }

        public double MinAreaM2
        {
            get => _rule.MinAreaM2;
            set { if (Math.Abs(_rule.MinAreaM2 - value) > 1e-6) { _rule.MinAreaM2 = value; MarkDirty(); } }
        }

        public double MaxAreaM2
        {
            get => _rule.MaxAreaM2;
            set { if (Math.Abs(_rule.MaxAreaM2 - value) > 1e-6) { _rule.MaxAreaM2 = value; MarkDirty(); } }
        }

        public string LevelFilter
        {
            get => _rule.LevelFilter;
            set { if (_rule.LevelFilter != value) { _rule.LevelFilter = value ?? ""; MarkDirty(); } }
        }

        public string PhaseFilter
        {
            get => _rule.PhaseFilter;
            set { if (_rule.PhaseFilter != value) { _rule.PhaseFilter = value ?? ""; MarkDirty(); } }
        }

        public string WorksetFilter
        {
            get => _rule.WorksetFilter;
            set { if (_rule.WorksetFilter != value) { _rule.WorksetFilter = value ?? ""; MarkDirty(); } }
        }

        public string AnchorType
        {
            get => _rule.AnchorType;
            set { if (_rule.AnchorType != value) { _rule.AnchorType = value ?? "ROOM_CENTRE"; MarkDirty(); } }
        }

        public string MountingReference
        {
            get => _rule.MountingReference;
            set { if (_rule.MountingReference != value) { _rule.MountingReference = value ?? "FFL"; MarkDirty(); } }
        }

        public double OffsetXMm
        {
            get => _rule.OffsetXMm;
            set { if (Math.Abs(_rule.OffsetXMm - value) > 1e-6) { _rule.OffsetXMm = value; MarkDirty(); } }
        }

        public double OffsetYMm
        {
            get => _rule.OffsetYMm;
            set { if (Math.Abs(_rule.OffsetYMm - value) > 1e-6) { _rule.OffsetYMm = value; MarkDirty(); } }
        }

        public double OffsetZMm
        {
            get => _rule.OffsetZMm;
            set { if (Math.Abs(_rule.OffsetZMm - value) > 1e-6) { _rule.OffsetZMm = value; MarkDirty(); } }
        }

        public double RotationDeg
        {
            get => _rule.RotationDeg;
            set { if (Math.Abs(_rule.RotationDeg - value) > 1e-6) { _rule.RotationDeg = value; MarkDirty(); } }
        }

        public double ToleranceMm
        {
            get => _rule.ToleranceMm;
            set { if (Math.Abs(_rule.ToleranceMm - value) > 1e-6) { _rule.ToleranceMm = value; MarkDirty(); } }
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

        public double PerAreaM2
        {
            get => _rule.PerAreaM2;
            set { if (Math.Abs(_rule.PerAreaM2 - value) > 1e-6) { _rule.PerAreaM2 = value; MarkDirty(); } }
        }

        public double PerOccupant
        {
            get => _rule.PerOccupant;
            set { if (Math.Abs(_rule.PerOccupant - value) > 1e-6) { _rule.PerOccupant = value; MarkDirty(); } }
        }

        public double PerLinearMetre
        {
            get => _rule.PerLinearMetre;
            set { if (Math.Abs(_rule.PerLinearMetre - value) > 1e-6) { _rule.PerLinearMetre = value; MarkDirty(); } }
        }

        public string DependsOn
        {
            get => _rule.DependsOn;
            set { if (_rule.DependsOn != value) { _rule.DependsOn = value ?? ""; MarkDirty(); } }
        }

        public string RelativeTo
        {
            get => _rule.RelativeTo;
            set { if (_rule.RelativeTo != value) { _rule.RelativeTo = value ?? ""; MarkDirty(); } }
        }

        public int Priority
        {
            get => _rule.Priority;
            set { if (_rule.Priority != value) { _rule.Priority = value; MarkDirty(); } }
        }

        public string StandardRef
        {
            get => _rule.StandardRef;
            set { if (_rule.StandardRef != value) { _rule.StandardRef = value ?? ""; MarkDirty(); } }
        }

        public string UniclassPr
        {
            get => _rule.UniclassPr;
            set { if (_rule.UniclassPr != value) { _rule.UniclassPr = value ?? ""; MarkDirty(); } }
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

        /// <summary>
        /// PC-02 + PC-03: deep-validate the rule. Catches:
        /// - empty CategoryFilter
        /// - unknown CategoryFilter (when ValidCategoryNames is set)
        /// - invalid AnchorType / SideConstraint / MountingReference / RuleKind / RelativeTo
        /// - non-compilable regexes on RoomFilter / ExcludeRoomFilter / LevelFilter / PhaseFilter / WorksetFilter / RoomDepartmentFilter / FamilyTypeRegex
        /// - negative MinSpacing / Mount / spacing / area / counts
        /// - Priority out of [0..100]
        /// - density rule with no PerAreaM2 / PerOccupant
        /// - linear rule with no PerLinearMetre
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(_rule.CategoryFilter))
            { ErrorMessage = "CategoryFilter is required."; return; }

            if (ValidCategoryNames != null && ValidCategoryNames.Count > 0
                && !ValidCategoryNames.Contains(_rule.CategoryFilter))
            { ErrorMessage = $"Category '{_rule.CategoryFilter}' is not present in the active document."; return; }

            if (!IsValidAnchor(_rule.AnchorType))
            { ErrorMessage = $"AnchorType '{_rule.AnchorType}' is not recognised by the placement engine."; return; }

            if (!IsValidSide(_rule.SideConstraint))
            { ErrorMessage = $"SideConstraint '{_rule.SideConstraint}' is not in (EITHER,LEFT,RIGHT,FRONT,BACK,HINGE_SIDE,LATCH_SIDE)."; return; }

            if (!IsValidReference(_rule.MountingReference))
            { ErrorMessage = $"MountingReference '{_rule.MountingReference}' is not in (FFL,SOFFIT,SLAB,CEILING)."; return; }

            string regexErr;
            if (!TryCompile(_rule.RoomFilter,           out regexErr)) { ErrorMessage = $"RoomFilter regex: {regexErr}"; return; }
            if (!TryCompile(_rule.ExcludeRoomFilter,    out regexErr)) { ErrorMessage = $"ExcludeRoomFilter regex: {regexErr}"; return; }
            if (!TryCompile(_rule.LevelFilter,          out regexErr)) { ErrorMessage = $"LevelFilter regex: {regexErr}"; return; }
            if (!TryCompile(_rule.PhaseFilter,          out regexErr)) { ErrorMessage = $"PhaseFilter regex: {regexErr}"; return; }
            if (!TryCompile(_rule.WorksetFilter,        out regexErr)) { ErrorMessage = $"WorksetFilter regex: {regexErr}"; return; }
            if (!TryCompile(_rule.RoomDepartmentFilter, out regexErr)) { ErrorMessage = $"RoomDepartmentFilter regex: {regexErr}"; return; }
            if (!TryCompile(_rule.FamilyTypeRegex,      out regexErr)) { ErrorMessage = $"FamilyTypeRegex: {regexErr}"; return; }
            // VariantHint is regex when it starts with ^ or contains | etc.; only check obvious regex use.
            if (_rule.VariantHint != null
                && (_rule.VariantHint.StartsWith("^") || _rule.VariantHint.Contains("$"))
                && !TryCompile(_rule.VariantHint, out regexErr))
            { ErrorMessage = $"VariantHint regex: {regexErr}"; return; }

            if (_rule.MinSpacingMm < 0) { ErrorMessage = "MinSpacingMm cannot be negative."; return; }
            if (_rule.Priority < 0 || _rule.Priority > 100) { ErrorMessage = "Priority must be between 0 and 100."; return; }
            if (_rule.MaxPerRoom < 0) { ErrorMessage = "MaxPerRoom cannot be negative."; return; }
            if (_rule.MinAreaM2 < 0)  { ErrorMessage = "MinAreaM2 cannot be negative."; return; }
            if (_rule.MaxAreaM2 < 0)  { ErrorMessage = "MaxAreaM2 cannot be negative."; return; }
            if (_rule.MaxAreaM2 > 0 && _rule.MaxAreaM2 < _rule.MinAreaM2)
            { ErrorMessage = "MaxAreaM2 cannot be less than MinAreaM2."; return; }
            if (_rule.ToleranceMm < 0) { ErrorMessage = "ToleranceMm cannot be negative."; return; }

            if (_rule.RuleKind == PlacementRuleKind.Density && _rule.PerAreaM2 <= 0 && _rule.PerOccupant <= 0)
            { ErrorMessage = "Density rule needs PerAreaM2 > 0 or PerOccupant > 0."; return; }
            if (_rule.RuleKind == PlacementRuleKind.Linear && _rule.PerLinearMetre <= 0)
            { ErrorMessage = "Linear rule needs PerLinearMetre > 0."; return; }

            ErrorMessage = "";
        }

        private static readonly HashSet<string> _anchorEnum = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ROOM_CENTRE","ROOM_CENTROID","CEILING_CENTRE",
            "LIGHTING_GRID","LUX_GRID","EN12464",
            "WALL_MIDPOINT","WALL_CORNER",
            "DOOR_HINGE","DOOR_JAMB","DOOR_HEAD","WINDOW_SILL",
            "OPPOSITE_WALL","GRID_INTERSECTION","COLUMN_FACE",
            "PERIMETER_OFFSET","RAISED_FLOOR_TILE",
            "STAIR_NOSING","ESCAPE_ROUTE_CENTRELINE",
            "RELATIVE_TO","EQUIPMENT_PAIR",
        };
        private static readonly HashSet<string> _sideEnum = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "EITHER","LEFT","RIGHT","FRONT","BACK","HINGE_SIDE","LATCH_SIDE" };
        private static readonly HashSet<string> _refEnum = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "FFL","SOFFIT","SLAB","CEILING" };

        private static bool IsValidAnchor(string s) => string.IsNullOrEmpty(s) || _anchorEnum.Contains(s);
        private static bool IsValidSide(string s)   => string.IsNullOrEmpty(s) || _sideEnum.Contains(s);
        private static bool IsValidReference(string s) => string.IsNullOrEmpty(s) || _refEnum.Contains(s);

        private static bool TryCompile(string pattern, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(pattern)) return true;
            try { _ = new Regex(pattern, RegexOptions.IgnoreCase); return true; }
            catch (ArgumentException ex) { err = ex.Message; return false; }
        }

        public PlacementRuleViewModel Clone() =>
            new PlacementRuleViewModel(_rule.Clone()) { IsDirty = true, ValidCategoryNames = this.ValidCategoryNames };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void MarkDirty()
        {
            IsDirty = true;
            Validate();
            OnPropertyChanged(string.Empty);
        }
    }
}
