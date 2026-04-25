// Phase 127-A — Placement Centre root view model.
//
// Owns the rule collection, the current selection, and the load/save
// pipeline. Delegates persistence to PlacementRuleLoader (defaults +
// project override merge); writes back through a small wrapper that
// honours the Pack 122 dual-surface convention (ES first, on-disk JSON
// as the legacy fallback).
//
// Phase A is offline-only — no engine wiring yet. Phase B reads the
// SelectedRule / Scope / RunOptions from this VM to drive
// FixturePlacementEngine.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.Core.Storage;

namespace StingTools.UI.PlacementCenter
{
    public class PlacementRulesViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<PlacementRuleViewModel> Rules { get; }
            = new ObservableCollection<PlacementRuleViewModel>();

        public ObservableCollection<string> Categories { get; }
            = new ObservableCollection<string>();

        // Anchor list MUST match the switch in PlacementScorer.GenerateAnchorPoints
        // — entries that the scorer doesn't recognise silently fall through to
        // ROOM_CENTRE. Order: most-used first, lighting-grid family second.
        public ObservableCollection<string> AnchorTypes { get; }
            = new ObservableCollection<string>
            {
                "ROOM_CENTRE",
                "ROOM_CENTROID",
                "CEILING_CENTRE",
                "LIGHTING_GRID",   // BS EN 12464-1 lumen-method grid
                "LUX_GRID",        // alias of LIGHTING_GRID
                "EN12464",         // alias of LIGHTING_GRID
                "WALL_MIDPOINT",
                "WALL_CORNER",
                "DOOR_HINGE",
                "DOOR_JAMB",
                "WINDOW_SILL",
            };

        public ObservableCollection<string> SideConstraints { get; }
            = new ObservableCollection<string>
            {
                "EITHER", "LEFT", "RIGHT", "FRONT", "BACK"
            };

        public RunOptions RunOpts { get; } = new RunOptions();

        // ── Selection ────────────────────────────────────────────────

        private PlacementRuleViewModel _selected;
        public PlacementRuleViewModel Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                if (_selected != null) _selected.IsSelected = false;
                _selected = value;
                if (_selected != null) _selected.IsSelected = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => _selected != null;

        // ── Status ───────────────────────────────────────────────────

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value ?? ""; OnPropertyChanged(); } }
        }

        private string _projectFilePath = "";
        public string ProjectFilePath
        {
            get => _projectFilePath;
            set { if (_projectFilePath != value) { _projectFilePath = value ?? ""; OnPropertyChanged(); } }
        }

        public int DirtyCount => Rules.Count(r => r.IsDirty);

        // ── Filter ───────────────────────────────────────────────────

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (_searchText != value) { _searchText = value ?? ""; OnPropertyChanged(); ApplyFilter(); } }
        }

        public ICollectionView FilteredRules { get; private set; }

        // ── Operations ───────────────────────────────────────────────

        /// <summary>
        /// Loads rules from defaults + project override. ES (Pack 122/C
        /// StingDrawingTypesSchema) deliberately not consumed here — that
        /// schema holds DrawingType overrides, not placement rules. The
        /// placement counterpart (StingPlacementRulesSchema) is a future
        /// pack; until then we read the on-disk pair.
        /// </summary>
        public void LoadFromProject(Autodesk.Revit.DB.Document doc)
        {
            Rules.Clear();
            try
            {
                var all = PlacementRuleLoader.Load(doc?.PathName ?? "");
                foreach (var r in all.OrderBy(r => r?.CategoryFilter ?? ""))
                {
                    if (r == null) continue;
                    Rules.Add(new PlacementRuleViewModel(r));
                }
                RebuildCategories();

                ProjectFilePath = doc?.PathName ?? "(unsaved)";
                Status = $"Loaded {Rules.Count} rule(s) — {Categories.Count} categor{(Categories.Count==1?"y":"ies")}.";
                OnPropertyChanged(nameof(DirtyCount));
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementRulesViewModel.LoadFromProject", ex);
                Status = $"Load failed: {ex.Message}";
            }
        }

        public void ReloadDefaults()
        {
            Rules.Clear();
            try
            {
                foreach (var r in PlacementRuleLoader.LoadDefaults().OrderBy(r => r.CategoryFilter))
                    Rules.Add(new PlacementRuleViewModel(r));
                RebuildCategories();
                Status = $"Reloaded {Rules.Count} default rule(s) (project overrides ignored).";
                OnPropertyChanged(nameof(DirtyCount));
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementRulesViewModel.ReloadDefaults", ex);
                Status = $"Reload defaults failed: {ex.Message}";
            }
        }

        public PlacementRuleViewModel AddRule()
        {
            var vm = new PlacementRuleViewModel(new PlacementRule
            {
                CategoryFilter = "(new category)",
                AnchorType = "ROOM_CENTRE",
                SideConstraint = "EITHER",
                MinSpacingMm = 1000,
                MountingHeightMm = 300,
                Priority = 50,
            });
            vm.IsDirty = true;
            Rules.Add(vm);
            Selected = vm;
            Status = "Added new rule — fill in CategoryFilter then save.";
            OnPropertyChanged(nameof(DirtyCount));
            return vm;
        }

        public void DeleteSelected()
        {
            if (_selected == null) return;
            int idx = Rules.IndexOf(_selected);
            Rules.Remove(_selected);
            Selected = idx < Rules.Count ? Rules[idx]
                     : (Rules.Count > 0 ? Rules[Rules.Count - 1] : null);
            Status = "Rule removed (not yet persisted).";
            OnPropertyChanged(nameof(DirtyCount));
        }

        /// <summary>
        /// Persist rules to &lt;project&gt;/STING_PLACEMENT_RULES.project.json
        /// next to the .rvt file. Uses the existing PlacementRuleSet wrapper
        /// the loader already understands.
        /// </summary>
        public bool SaveProject(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName))
                {
                    Status = "Save aborted — project must be saved on disk first.";
                    return false;
                }
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir))
                {
                    Status = "Save aborted — could not derive project directory.";
                    return false;
                }
                string path = Path.Combine(dir, "STING_PLACEMENT_RULES.project.json");
                var set = new StingTools.Core.Placement.PlacementRuleSet
                {
                    Version = "v4",
                    Rules = Rules.Select(r => r.Model).ToList(),
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(set, Formatting.Indented));
                foreach (var r in Rules) r.ClearDirty();
                ProjectFilePath = path;
                Status = $"Saved {Rules.Count} rule(s) → {path}";
                OnPropertyChanged(nameof(DirtyCount));
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementRulesViewModel.SaveProject", ex);
                Status = $"Save failed: {ex.Message}";
                return false;
            }
        }

        public void RebuildCategories()
        {
            Categories.Clear();
            foreach (var c in Rules.Select(r => r.CategoryFilter)
                                   .Where(s => !string.IsNullOrWhiteSpace(s))
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                Categories.Add(c);
        }

        public void AttachFilteredView()
        {
            FilteredRules = System.Windows.Data.CollectionViewSource.GetDefaultView(Rules);
            FilteredRules.Filter = obj =>
            {
                if (string.IsNullOrEmpty(_searchText)) return true;
                if (obj is not PlacementRuleViewModel vm) return false;
                string q = _searchText.Trim();
                return (vm.CategoryFilter?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (vm.VariantHint?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (vm.RoomFilter?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (vm.Notes?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            };
        }

        public void ApplyFilter() => FilteredRules?.Refresh();

        // ── INPC ─────────────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Run options POCO ─────────────────────────────────────────

        public class RunOptions : INotifyPropertyChanged
        {
            private bool _stampProvenance = true;
            private bool _honourLearned   = true;
            private bool _runValidators   = true;
            private bool _autoHeatmap     = false;
            private string _scope         = "ActiveView";

            public bool StampProvenance { get => _stampProvenance; set { if (_stampProvenance != value) { _stampProvenance = value; Raise(); } } }
            public bool HonourLearned   { get => _honourLearned;   set { if (_honourLearned   != value) { _honourLearned   = value; Raise(); } } }
            public bool RunValidators   { get => _runValidators;   set { if (_runValidators   != value) { _runValidators   = value; Raise(); } } }
            public bool AutoHeatmap     { get => _autoHeatmap;     set { if (_autoHeatmap     != value) { _autoHeatmap     = value; Raise(); } } }
            public string Scope         { get => _scope;           set { if (_scope           != value) { _scope           = value ?? "ActiveView"; Raise(); } } }

            public event PropertyChangedEventHandler PropertyChanged;
            private void Raise([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
