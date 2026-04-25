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

        // Variant hints surface as suggestions in the editable cmbVariant
        // ComboBox. Users can type any value; this list documents the
        // canonical STING_FIXTURE_VARIANT_TXT vocabulary so projects stay
        // consistent across teams.
        public ObservableCollection<string> VariantHints { get; }
            = new ObservableCollection<string>
            {
                "FLUSH", "SURFACE", "RECESSED",
                "IP65", "IP66", "IP67",
                "EM",        // emergency-rated luminaire
                "DALI",      // DALI-controlled
                "TWIN",
                "SINGLE",
            };

        public RunOptions RunOpts { get; } = new RunOptions();

        // Live-bound grids (gap 12) — drained + repopulated by the centre's
        // bridges. ObservableCollection means UI refreshes without an
        // imperative gridX.ItemsSource = ... reassignment per call.
        public ObservableCollection<FamilyHintsBridge.HintRow> FamilyHints { get; }
            = new ObservableCollection<FamilyHintsBridge.HintRow>();

        public ObservableCollection<HistoryBridge.HistoryRow> History { get; }
            = new ObservableCollection<HistoryBridge.HistoryRow>();

        public void SetFamilyHints(System.Collections.Generic.IEnumerable<FamilyHintsBridge.HintRow> rows)
        {
            FamilyHints.Clear();
            if (rows == null) return;
            foreach (var r in rows) FamilyHints.Add(r);
        }

        public void SetHistory(System.Collections.Generic.IEnumerable<HistoryBridge.HistoryRow> rows)
        {
            History.Clear();
            if (rows == null) return;
            foreach (var r in rows) History.Add(r);
        }

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

        private bool _showDirtyOnly;
        public bool ShowDirtyOnly
        {
            get => _showDirtyOnly;
            set { if (_showDirtyOnly != value) { _showDirtyOnly = value; OnPropertyChanged(); ApplyFilter(); } }
        }

        private bool _showInvalidOnly;
        public bool ShowInvalidOnly
        {
            get => _showInvalidOnly;
            set { if (_showInvalidOnly != value) { _showInvalidOnly = value; OnPropertyChanged(); ApplyFilter(); } }
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

        /// <summary>True when at least one rule has unsaved edits — caller
        /// (centre's Reload Defaults handler) prompts before nuking edits.</summary>
        public bool HasUnsavedEdits => Rules.Any(r => r.IsDirty);

        /// <summary>Append rules from an external JSON file (same schema as
        /// STING_PLACEMENT_RULES.json). Existing rules are kept; rules with
        /// the same MergeKey are skipped so an import can't silently
        /// shadow a project override. Returns the number actually appended.</summary>
        public int ImportFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
            try
            {
                var json = File.ReadAllText(path);
                var set = JsonConvert.DeserializeObject<StingTools.Core.Placement.PlacementRuleSet>(json);
                if (set?.Rules == null || set.Rules.Count == 0) return 0;

                var existingKeys = new HashSet<string>(Rules.Select(r => r.MergeKey ?? ""), StringComparer.OrdinalIgnoreCase);
                int n = 0;
                foreach (var r in set.Rules)
                {
                    if (r == null) continue;
                    string key = r.MergeKey ?? "";
                    if (existingKeys.Contains(key)) continue;
                    var vm = new PlacementRuleViewModel(r) { IsDirty = true };
                    Rules.Add(vm);
                    existingKeys.Add(key);
                    n++;
                }
                if (n > 0)
                {
                    RebuildCategories();
                    Status = $"Imported {n} rule(s) from {Path.GetFileName(path)} (Save Project to persist).";
                    OnPropertyChanged(nameof(DirtyCount));
                }
                else
                {
                    Status = $"Import skipped — every rule in {Path.GetFileName(path)} already present (matched on MergeKey).";
                }
                return n;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementRulesViewModel.ImportFromFile", ex);
                Status = $"Import failed: {ex.Message}";
                return 0;
            }
        }

        /// <summary>Write the current valid rules to an arbitrary path —
        /// used for sharing rule sets between projects/teams without
        /// touching the project's STING_PLACEMENT_RULES.project.json.</summary>
        public int ExportToFile(string path)
        {
            try
            {
                var validVms = Rules.Where(r => r.IsValid).ToList();
                if (validVms.Count == 0) { Status = "Export skipped — no valid rules."; return 0; }
                var set = new StingTools.Core.Placement.PlacementRuleSet
                {
                    Version = "v4",
                    Rules = validVms.Select(r => r.Model).ToList(),
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(set, Formatting.Indented));
                Status = $"Exported {validVms.Count} rule(s) → {path}";
                return validVms.Count;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementRulesViewModel.ExportToFile", ex);
                Status = $"Export failed: {ex.Message}";
                return 0;
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

        /// <summary>Bulk-delete a set of rules. Caller is responsible for
        /// confirming with the user; the VM just removes them and updates
        /// status/categories/dirty count.</summary>
        public int DeleteMany(IEnumerable<PlacementRuleViewModel> victims)
        {
            int n = 0;
            foreach (var v in (victims ?? System.Array.Empty<PlacementRuleViewModel>()).ToList())
            {
                if (v == null || !Rules.Contains(v)) continue;
                Rules.Remove(v);
                n++;
            }
            if (n > 0)
            {
                Selected = Rules.Count > 0 ? Rules[0] : null;
                RebuildCategories();
                Status = $"Removed {n} rule(s) (not yet persisted).";
                OnPropertyChanged(nameof(DirtyCount));
            }
            return n;
        }

        /// <summary>Clone a set of rules. The new rules carry the original
        /// category + " (copy)" so they're easy to find and the user can
        /// rename. All clones are flagged dirty.</summary>
        public int CloneMany(IEnumerable<PlacementRuleViewModel> sources)
        {
            int n = 0;
            PlacementRuleViewModel last = null;
            foreach (var s in (sources ?? System.Array.Empty<PlacementRuleViewModel>()).ToList())
            {
                if (s == null) continue;
                var copy = s.Clone();
                copy.CategoryFilter = string.IsNullOrEmpty(s.CategoryFilter)
                    ? "(new category)"
                    : s.CategoryFilter + " (copy)";
                Rules.Add(copy);
                last = copy;
                n++;
            }
            if (n > 0)
            {
                Selected = last;
                RebuildCategories();
                Status = $"Cloned {n} rule(s) — fix the category names then save.";
                OnPropertyChanged(nameof(DirtyCount));
            }
            return n;
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

                // Strip invalid rules from the on-disk file so a rule with
                // (e.g.) blank CategoryFilter doesn't round-trip through every
                // future load. Invalid rules stay in-memory so the user can
                // still fix them — they're just not persisted.
                var invalidVms = Rules.Where(r => !r.IsValid).ToList();
                var validVms   = Rules.Where(r =>  r.IsValid).ToList();

                var set = new StingTools.Core.Placement.PlacementRuleSet
                {
                    Version = "v4",
                    Rules = validVms.Select(r => r.Model).ToList(),
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(set, Formatting.Indented));
                foreach (var r in validVms) r.ClearDirty();
                ProjectFilePath = path;
                Status = invalidVms.Count > 0
                    ? $"Saved {validVms.Count} valid rule(s) → {path} · {invalidVms.Count} invalid skipped (still in-memory)"
                    : $"Saved {Rules.Count} rule(s) → {path}";
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
                if (obj is not PlacementRuleViewModel vm) return false;

                // Chip filters short-circuit when active.
                if (_showDirtyOnly   && !vm.IsDirty) return false;
                if (_showInvalidOnly &&  vm.IsValid) return false;

                if (string.IsNullOrEmpty(_searchText)) return true;
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
