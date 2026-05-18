// Phase 127-A — Placement Centre window code-behind.
//
// Modeless WPF window. One instance per UIApplication, kept alive
// until the user closes it. Re-opening the centre brings the existing
// instance to the foreground rather than creating a new one.
//
// Phase A is intentionally engine-free: the toolbar buttons that need
// FixturePlacementEngine / ClearanceValidator / AVF / GD all show a
// "wired in Phase B/C/D" TaskDialog. The rule-list + per-rule edit
// path is fully functional and persists to disk via SaveProject.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Placement;
using StingTools.Core.Routing;
using StingTools.Core.Validation;
using StingTools.Core.Visualization;

namespace StingTools.UI.PlacementCenter
{
    public partial class StingPlacementCenter : Window
    {
        // Single instance — stays alive across opens; modeless.
        private static StingPlacementCenter _instance;

        // PC-21 — debounce timer for live preview after rule edits.
        private System.Windows.Threading.DispatcherTimer _previewDebounce;
        private bool _livePreviewEnabled;

        public PlacementRulesViewModel VM { get; }
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly UIApplication _uiApp;
        private bool _suppressUiSync;

        // Phase 127-B — track the most recent placement run so Validate
        // can scope its findings to "elements just placed" rather than
        // re-scanning the whole project.
        private DateTime? _lastRunUtc;
        private List<ElementId> _lastPlacedIds = new List<ElementId>();

        // Phase 177 (Option A) — Run & Routing tab result state.
        private List<ElementId> _runResultIds  = new List<ElementId>();
        private string          _runReportText = string.Empty;

        public StingPlacementCenter(UIApplication uiApp)
        {
            // Pre-register the IsDirty → "●" converter before InitializeComponent
            // so the DataGrid binding resolves on first paint.
            EnsureResources();

            InitializeComponent();
            ThemeManager.RegisterTarget(this);
            ThemeManager.InitialiseResources();

            VM = new PlacementRulesViewModel();
            _uiApp = uiApp;
            _uiDoc = uiApp?.ActiveUIDocument;
            _doc = _uiDoc?.Document;

            // Combo data sources
            cmbCategory.ItemsSource          = VM.Categories;
            cmbAnchor.ItemsSource            = VM.AnchorTypes;
            cmbSide.ItemsSource              = VM.SideConstraints;
            cmbVariant.ItemsSource           = VM.VariantHints;
            cmbMountRef.ItemsSource          = VM.MountingReferences;
            cmbRuleKind.ItemsSource          = VM.RuleKinds;
            cmbRelativeTo.ItemsSource        = VM.RelativeToOptions;
            cmbRoutingMode.ItemsSource       = new[] { "NONE", "AUTO_CONDUIT", "AUTO_PIPE", "AUTO_DUCT", "WALL_FOLLOWER" };
            cmbRouteSegmentCategory.ItemsSource = new[] { "", "PIPE", "CONDUIT", "CABLE_TRAY", "DUCT" };
            cmbConstructionPhase.ItemsSource = new[] { "FINISHED", "FIRST_FIX", "SECOND_FIX" };

            // Run-option two-way bindings
            chkProvenance.IsChecked  = VM.RunOpts.StampProvenance;
            chkLearned.IsChecked     = VM.RunOpts.HonourLearned;
            chkValidators.IsChecked  = VM.RunOpts.RunValidators;
            chkAutoHeatmap.IsChecked = VM.RunOpts.AutoHeatmap;
            chkProvenance.Checked    += (_,__) => VM.RunOpts.StampProvenance = true;
            chkProvenance.Unchecked  += (_,__) => VM.RunOpts.StampProvenance = false;
            chkLearned.Checked       += (_,__) => VM.RunOpts.HonourLearned   = true;
            chkLearned.Unchecked     += (_,__) => VM.RunOpts.HonourLearned   = false;
            chkValidators.Checked    += (_,__) => VM.RunOpts.RunValidators   = true;
            chkValidators.Unchecked  += (_,__) => VM.RunOpts.RunValidators   = false;
            chkAutoHeatmap.Checked   += (_,__) => VM.RunOpts.AutoHeatmap     = true;
            chkAutoHeatmap.Unchecked += (_,__) => VM.RunOpts.AutoHeatmap     = false;
            // PC-17 — post-placement hooks
            chkRunTags.IsChecked     = StingTools.Core.Placement.PostPlacementHooks.RunDataTagPipeline;
            chkSeedCobie.IsChecked   = StingTools.Core.Placement.PostPlacementHooks.SeedCobieComponent;
            chkAssignMep.IsChecked   = StingTools.Core.Placement.PostPlacementHooks.AssignMepSystem;
            chkRunTags.Checked       += (_,__) => StingTools.Core.Placement.PostPlacementHooks.RunDataTagPipeline = true;
            chkRunTags.Unchecked     += (_,__) => StingTools.Core.Placement.PostPlacementHooks.RunDataTagPipeline = false;
            chkSeedCobie.Checked     += (_,__) => StingTools.Core.Placement.PostPlacementHooks.SeedCobieComponent = true;
            chkSeedCobie.Unchecked   += (_,__) => StingTools.Core.Placement.PostPlacementHooks.SeedCobieComponent = false;
            chkAssignMep.Checked     += (_,__) => StingTools.Core.Placement.PostPlacementHooks.AssignMepSystem = true;
            chkAssignMep.Unchecked   += (_,__) => StingTools.Core.Placement.PostPlacementHooks.AssignMepSystem = false;
            chkLivePreview.Checked   += (_,__) => _livePreviewEnabled = true;
            chkLivePreview.Unchecked += (_,__) => _livePreviewEnabled = false;
            rbScopeView.Checked      += (_,__) => VM.RunOpts.Scope = "ActiveView";
            rbScopeSel.Checked       += (_,__) => VM.RunOpts.Scope = "Selection";
            rbScopeProj.Checked      += (_,__) => VM.RunOpts.Scope = "Project";

            // Per-rule field handlers — wired manually so we can validate after each edit
            cmbCategory.LostFocus       += (_,__) => CommitField(() => VM.Selected.CategoryFilter   = (cmbCategory.Text ?? "").Trim());
            cmbCategory.SelectionChanged+= (_,__) => CommitField(() => VM.Selected.CategoryFilter   = (cmbCategory.Text ?? "").Trim());
            cmbVariant.LostFocus        += (_,__) => CommitField(() => VM.Selected.VariantHint      = (cmbVariant.Text ?? "").Trim());
            cmbVariant.SelectionChanged += (_,__) => CommitField(() => VM.Selected.VariantHint      = (cmbVariant.Text ?? "").Trim());
            txtRoom.LostFocus           += (_,__) => CommitField(() => VM.Selected.RoomFilter       = txtRoom.Text);
            txtNotes.LostFocus          += (_,__) => CommitField(() => VM.Selected.Notes            = txtNotes.Text);
            cmbAnchor.SelectionChanged  += (_,__) => CommitField(() => VM.Selected.AnchorType       = cmbAnchor.SelectedItem as string ?? VM.Selected.AnchorType);
            cmbSide.SelectionChanged    += (_,__) => CommitField(() => VM.Selected.SideConstraint   = cmbSide.SelectedItem   as string ?? VM.Selected.SideConstraint);
            txtPriority.LostFocus       += (_,__) => CommitField(() => VM.Selected.Priority         = ParseInt(txtPriority.Text, VM.Selected.Priority));
            txtOffsetX.LostFocus        += (_,__) => CommitField(() => VM.Selected.OffsetXMm        = ParseDouble(txtOffsetX.Text, VM.Selected.OffsetXMm));
            txtMountH.LostFocus         += (_,__) => CommitField(() => VM.Selected.MountingHeightMm = ParseDouble(txtMountH.Text,  VM.Selected.MountingHeightMm));
            txtSpacing.LostFocus        += (_,__) => CommitField(() => VM.Selected.MinSpacingMm     = ParseDouble(txtSpacing.Text, VM.Selected.MinSpacingMm));
            txtMaxRoom.LostFocus        += (_,__) => CommitField(() => VM.Selected.MaxPerRoom       = ParseInt(txtMaxRoom.Text,    VM.Selected.MaxPerRoom));

            // PC-06 — geometry expansion
            txtOffsetY.LostFocus        += (_,__) => CommitField(() => VM.Selected.OffsetYMm        = ParseDouble(txtOffsetY.Text, VM.Selected.OffsetYMm));
            txtOffsetZ.LostFocus        += (_,__) => CommitField(() => VM.Selected.OffsetZMm        = ParseDouble(txtOffsetZ.Text, VM.Selected.OffsetZMm));
            txtRotation.LostFocus       += (_,__) => CommitField(() => VM.Selected.RotationDeg      = ParseDouble(txtRotation.Text, VM.Selected.RotationDeg));
            cmbMountRef.SelectionChanged+= (_,__) => CommitField(() => VM.Selected.MountingReference = cmbMountRef.SelectedItem as string ?? VM.Selected.MountingReference);

            // PC-07 — room scoping suite
            txtRoomExclude.LostFocus    += (_,__) => CommitField(() => VM.Selected.ExcludeRoomFilter    = txtRoomExclude.Text);
            txtDepartment.LostFocus     += (_,__) => CommitField(() => VM.Selected.RoomDepartmentFilter = txtDepartment.Text);
            txtLevelFilter.LostFocus    += (_,__) => CommitField(() => VM.Selected.LevelFilter   = txtLevelFilter.Text);
            txtPhaseFilter.LostFocus    += (_,__) => CommitField(() => VM.Selected.PhaseFilter   = txtPhaseFilter.Text);
            txtWorksetFilter.LostFocus  += (_,__) => CommitField(() => VM.Selected.WorksetFilter = txtWorksetFilter.Text);
            txtMinArea.LostFocus        += (_,__) => CommitField(() => VM.Selected.MinAreaM2     = ParseDouble(txtMinArea.Text, VM.Selected.MinAreaM2));
            txtMaxArea.LostFocus        += (_,__) => CommitField(() => VM.Selected.MaxAreaM2     = ParseDouble(txtMaxArea.Text, VM.Selected.MaxAreaM2));

            // PC-12 — rule kind / density / linear
            cmbRuleKind.SelectionChanged+= (_,__) => CommitField(() => VM.Selected.RuleKind     = ParseRuleKind(cmbRuleKind.SelectedItem as string));
            txtPerArea.LostFocus        += (_,__) => CommitField(() => VM.Selected.PerAreaM2    = ParseDouble(txtPerArea.Text, VM.Selected.PerAreaM2));
            txtPerOcc.LostFocus         += (_,__) => CommitField(() => VM.Selected.PerOccupant  = ParseDouble(txtPerOcc.Text,  VM.Selected.PerOccupant));
            txtPerLin.LostFocus         += (_,__) => CommitField(() => VM.Selected.PerLinearMetre = ParseDouble(txtPerLin.Text, VM.Selected.PerLinearMetre));

            // PC-13 — dependencies
            txtRuleId.LostFocus         += (_,__) => CommitField(() => VM.Selected.RuleId        = txtRuleId.Text);
            txtDependsOn.LostFocus      += (_,__) => CommitField(() => VM.Selected.DependsOn     = txtDependsOn.Text);
            cmbRelativeTo.SelectionChanged += (_,__) => CommitField(() => VM.Selected.RelativeTo = cmbRelativeTo.SelectedItem as string ?? "");
            txtCoPlace.LostFocus        += (_,__) => CommitField(() => VM.Selected.Model.CoPlaceWith   = ParseList(txtCoPlace.Text));
            txtConflict.LostFocus       += (_,__) => CommitField(() => VM.Selected.Model.ConflictsWith = ParseList(txtConflict.Text));

            // Standards & classification
            txtStandardRef.LostFocus    += (_,__) => CommitField(() => VM.Selected.StandardRef = txtStandardRef.Text);
            txtUniclassPr.LostFocus     += (_,__) => CommitField(() => VM.Selected.UniclassPr  = txtUniclassPr.Text);

            // Rule core extensions (PC-08 / identity)
            txtFamilyTypeRegex.LostFocus += (_,__) => CommitField(() => VM.Selected.Model.FamilyTypeRegex = txtFamilyTypeRegex.Text);
            // txtSourcePack is read-only — no commit wire

            // Geometry extension (PC-06)
            txtToleranceMm.LostFocus     += (_,__) => CommitField(() => VM.Selected.Model.ToleranceMm  = ParseDouble(txtToleranceMm.Text, VM.Selected.Model.ToleranceMm));
            txtMaxSpacing.LostFocus      += (_,__) => CommitField(() => VM.Selected.Model.MaxSpacingMm = ParseDouble(txtMaxSpacing.Text,  VM.Selected.Model.MaxSpacingMm));

            // PC-12 density extensions
            txtPerBed.LostFocus          += (_,__) => CommitField(() => VM.Selected.Model.PerBed            = ParseDouble(txtPerBed.Text,          VM.Selected.Model.PerBed));
            txtPerWorkstation.LostFocus  += (_,__) => CommitField(() => VM.Selected.Model.PerWorkstation     = ParseDouble(txtPerWorkstation.Text,  VM.Selected.Model.PerWorkstation));
            txtPerPupil.LostFocus        += (_,__) => CommitField(() => VM.Selected.Model.PerPupil           = ParseDouble(txtPerPupil.Text,        VM.Selected.Model.PerPupil));
            txtPerToiletCubicle.LostFocus+= (_,__) => CommitField(() => VM.Selected.Model.PerToiletCubicle  = ParseDouble(txtPerToiletCubicle.Text, VM.Selected.Model.PerToiletCubicle));
            txtOccupancyParam.LostFocus  += (_,__) => CommitField(() => VM.Selected.Model.OccupancyParamName = txtOccupancyParam.Text);

            // PC-14 coverage grid
            txtCoverageRadius.LostFocus  += (_,__) => CommitField(() => VM.Selected.Model.CoverageRadiusMm = ParseDouble(txtCoverageRadius.Text, VM.Selected.Model.CoverageRadiusMm));
            chkGuaranteeCoverage.Checked += (_,__) => CommitField(() => VM.Selected.Model.GuaranteeCoverage = true);
            chkGuaranteeCoverage.Unchecked+=(_,__) => CommitField(() => VM.Selected.Model.GuaranteeCoverage = false);

            // PC-15 integrated routing
            cmbRoutingMode.SelectionChanged      += (_,__) => CommitField(() => VM.Selected.Model.RoutingMode          = cmbRoutingMode.SelectedItem as string ?? "NONE");
            cmbRouteSegmentCategory.SelectionChanged += (_,__) => CommitField(() => VM.Selected.Model.RouteSegmentCategory = cmbRouteSegmentCategory.SelectedItem as string ?? "");
            txtRouteOffset.LostFocus             += (_,__) => CommitField(() => VM.Selected.Model.RouteOffsetMm        = ParseDouble(txtRouteOffset.Text, VM.Selected.Model.RouteOffsetMm));

            // PC-16 construction phasing / PC-17 cluster
            chkTwoPhase.Checked          += (_,__) => CommitField(() => VM.Selected.Model.TwoPhaseEnabled  = true);
            chkTwoPhase.Unchecked        += (_,__) => CommitField(() => VM.Selected.Model.TwoPhaseEnabled  = false);
            cmbConstructionPhase.SelectionChanged += (_,__) => CommitField(() => VM.Selected.Model.ConstructionPhase = cmbConstructionPhase.SelectedItem as string ?? "FINISHED");
            chkClusterMember.Checked     += (_,__) => CommitField(() => VM.Selected.Model.IsClusterMember  = true);
            chkClusterMember.Unchecked   += (_,__) => CommitField(() => VM.Selected.Model.IsClusterMember  = false);
            txtClusterGroupId.LostFocus  += (_,__) => CommitField(() => VM.Selected.Model.ClusterGroupId   = txtClusterGroupId.Text);

            // VM → status bar binding
            VM.PropertyChanged += OnVmPropertyChanged;

            // Load
            VM.LoadFromProject(_doc);
            VM.AttachFilteredView();
            gridRules.ItemsSource = VM.FilteredRules;
            if (VM.Rules.Count > 0) gridRules.SelectedIndex = 0;

            // Phase D — populate history grid on first open so the
            // panel surface isn't a wall of empties.
            try { VM.SetHistory(HistoryBridge.ReadHistory(_doc)); }
            catch (Exception hEx) { StingLog.Warn($"History first-load: {hEx.Message}"); }

            UpdateStatus();

            // Keyboard shortcuts — see PlacementCentreCommands.cs.
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.SaveProject,    (s,a) => OnSaveProject_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.AddRule,        (s,a) => OnAdd_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.RunPlacement,   (s,a) => OnRunPlacement_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.Preview,        (s,a) => OnPreview_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.Validate,       (s,a) => OnValidate_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.UndoLast,       (s,a) => OnUndoLast_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.HistoryRefresh, (s,a) => OnHistoryRefresh_Click(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.ClearPreview,   (s,a) => OnClearPreview_Shortcut(s, a)));
            CommandBindings.Add(new CommandBinding(PlacementCentreCommands.DeleteSelected, (s,a) => OnDeleteSelected_Click(s, a)));

            // Subscribe to the result bus so any placement/tag/symbol command updates this panel.
            PlacementResultBus.ResultPublished += OnBusResult;
            // Show whatever the last result was (e.g., if centre re-opened mid-session).
            if (PlacementResultBus.LastResult != null) OnBusResult(PlacementResultBus.LastResult);

            RefreshDrawingTypeContext();

            HookDocumentLifecycle();
        }

        // ESC = clear any active DirectContext3D preview without closing the window.
        private void OnClearPreview_Shortcut(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                StingTools.Core.Visualization.PreviewController.Cancel();
                VM.Status = "Preview cleared.";
                UpdateStatus();
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter ESC clear preview: {ex.Message}"); }
        }

        // ── Singleton orchestration ──────────────────────────────────

        public static void ShowOrFocus(UIApplication uiApp)
        {
            if (_instance != null && !_instance._closed)
            {
                // If the cached instance is bound to a different document
                // than the active one, close it and reopen against the
                // current doc so rules + history come from the right project.
                var activeDoc = uiApp?.ActiveUIDocument?.Document;
                if (activeDoc != null && _instance._doc != null &&
                    !ReferenceEquals(activeDoc, _instance._doc))
                {
                    try { _instance.Close(); } catch { }
                    _instance = null;
                }
                else
                {
                    _instance.Activate();
                    _instance.RefreshDrawingTypeContext();
                    return;
                }
            }
            try
            {
                _instance = new StingPlacementCenter(uiApp);
                _instance.Closed += (_, __) => { _instance._closed = true; _instance = null; };
                _instance.Show();
            }
            catch (Exception ex)
            {
                StingLog.Error("StingPlacementCenter.ShowOrFocus", ex);
                TaskDialog.Show("STING — Placement Centre",
                    $"Failed to open Placement Centre.\n\n{ex.Message}");
            }
        }

        // Wire DocumentClosing so a stale singleton can't outlive its
        // backing document and silently operate on the wrong project.
        private void HookDocumentLifecycle()
        {
            if (_uiApp == null || _uiApp.Application == null) return;
            try
            {
                _uiApp.Application.DocumentClosing += OnRevitDocumentClosing;
                this.Closed += (_, __) =>
                {
                    try { _uiApp.Application.DocumentClosing -= OnRevitDocumentClosing; } catch { }
                };
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter HookDocumentLifecycle: {ex.Message}"); }
        }

        private void OnRevitDocumentClosing(object sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
        {
            try
            {
                if (e?.Document != null && ReferenceEquals(e.Document, _doc))
                {
                    StingLog.Info("PlacementCenter: document closing, auto-closing centre.");
                    Dispatcher.Invoke(() =>
                    {
                        try { Close(); } catch (Exception ex) { StingLog.Warn($"PlacementCenter auto-close: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter OnRevitDocumentClosing: {ex.Message}"); }
        }

        private bool _closed;

        protected override void OnClosed(EventArgs e)
        {
            PlacementResultBus.ResultPublished -= OnBusResult;
            base.OnClosed(e);
        }

        // ── Toolbar handlers ─────────────────────────────────────────

        private void OnReloadDefaults_Click(object sender, RoutedEventArgs e)
        {
            // Reload Defaults nukes the entire in-memory set including any
            // unsaved edits. Confirm before destroying user work.
            if (VM.HasUnsavedEdits)
            {
                int dirty = VM.Rules.Count(r => r.IsDirty);
                var confirm = new TaskDialog("STING — Reload Defaults")
                {
                    MainInstruction = $"Discard {dirty} unsaved edit(s)?",
                    MainContent =
                        "Reload Defaults replaces every rule in the centre with the shipped " +
                        "STING_PLACEMENT_RULES.json baseline. Project overrides on disk are " +
                        "untouched until you click Save Project, but in-memory edits will be lost.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (confirm.Show() != TaskDialogResult.Yes) return;
            }
            VM.ReloadDefaults();
            VM.AttachFilteredView();
            gridRules.ItemsSource = VM.FilteredRules;
            UpdateStatus();
        }

        private void OnSaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (VM.SaveProject(_doc))
                UpdateStatus();
        }

        private void OnImportRules_Click(object sender, RoutedEventArgs e)
        {
            // Wrap WinForms-flavoured OpenFileDialog with the WPF default paths.
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "STING — Import placement rules",
                Filter = "STING placement rules (*.json)|*.json|All files (*.*)|*.*",
                FileName = "STING_PLACEMENT_RULES.project.json",
            };
            if (dlg.ShowDialog(this) != true) return;
            int n = VM.ImportFromFile(dlg.FileName);
            UpdateStatus();
            TaskDialog.Show("STING — Import placement rules",
                n > 0
                    ? $"Imported {n} rule(s) from {dlg.FileName}.\nClick Save Project to persist."
                    : $"No rules imported. Check the file format (expects {{ \"version\": \"v4\", \"rules\": [...] }}).");
        }

        private void OnExportRules_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "STING — Export placement rules",
                Filter = "STING placement rules (*.json)|*.json|All files (*.*)|*.*",
                FileName = "STING_PLACEMENT_RULES.export.json",
            };
            if (dlg.ShowDialog(this) != true) return;
            int n = VM.ExportToFile(dlg.FileName);
            UpdateStatus();
            TaskDialog.Show("STING — Export placement rules",
                n > 0
                    ? $"Exported {n} valid rule(s) to {dlg.FileName}."
                    : "No valid rules to export.");
        }

        // ── Phase 127-B engine wiring ────────────────────────────────

        private void OnRunPlacement_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }

            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            if (rules.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre",
                    "No valid rules to run. Add at least one rule with a non-empty CategoryFilter.");
                return;
            }

            var roomIds = PlacementCenterBridge.ResolveScope(_uiDoc, VM.RunOpts.Scope);
            if (roomIds.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre",
                    $"Scope '{VM.RunOpts.Scope}' resolved zero rooms. Switch scope or open a view that contains rooms.");
                return;
            }

            // Confirm with a summary so the run isn't silently destructive.
            var confirm = new TaskDialog("STING — Run Placement")
            {
                MainInstruction = $"Place fixtures in {roomIds.Count} room(s)?",
                MainContent = $"Rules: {rules.Count}\nScope: {VM.RunOpts.Scope}\nValidators after: {VM.RunOpts.RunValidators}\n\nThe engine creates new FamilyInstance(s); use Undo or 'Undo last run' (Phase D) to revert.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return;

            VM.Status = "Running placement…";
            UpdateStatus();

            // Push run-options into the static option-bag the engine reads.
            // Snapshot the previous values so other call sites (Fixtures-tab
            // PlaceFixturesCommand) get their settings restored after this run.
            bool prevStamp = StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance;
            bool prevLearn = StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned;
            StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance = VM.RunOpts.StampProvenance;
            StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned   = VM.RunOpts.HonourLearned;

            DateTime startUtc = DateTime.UtcNow;
            PlacementResult result = null;
            // Show a modeless progress dialog so the user can see per-room
            // progress and abort. The placement engine commits per-room
            // ProcessRoomRule writes inside its single Transaction, so the
            // outer TransactionGroup keeps everything undoable as one step.
            var progress = StingProgressDialog.Show(
                "STING — Placement Centre · Run", roomIds.Count);
            try
            {
                // FixturePlacementEngine opens its own Transaction inside the
                // supplied document. Wrap it in a TransactionGroup so the run
                // is undoable as a single step but DO NOT open another
                // Transaction here — Revit forbids nested Transactions and
                // the engine would silently fail with "Transaction start
                // failed: …" in result.Warnings.
                using (var tg = new TransactionGroup(_doc, "STING Placement Centre — Run"))
                {
                    tg.Start();
                    result = FixturePlacementEngine.PlaceFixturesInScope(
                        _doc, roomIds, rules, dryRun: false,
                        progress: (done, total) =>
                        {
                            try
                            {
                                progress.Increment($"Room {done} of {total}…");
                                return progress.IsCancelled;
                            }
                            catch { return false; }
                        });
                    tg.Assimilate();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnRunPlacement", ex);
                TaskDialog.Show("STING — Placement Centre", $"Run failed: {ex.Message}");
                VM.Status = $"Run failed: {ex.Message}";
                UpdateStatus();
                return;
            }
            finally
            {
                // Restore the option-bag so other entry points aren't affected.
                StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance = prevStamp;
                StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned   = prevLearn;
                try { progress?.Close(); } catch { }
            }

            _lastRunUtc = startUtc;
            _lastPlacedIds = result?.PlacedIds?.ToList() ?? new List<ElementId>();
            int placed  = _lastPlacedIds.Count;
            int skipped = result?.SkippedCount ?? 0;
            int warns   = result?.Warnings?.Count ?? 0;

            VM.Status = $"Placed {placed} · skipped {skipped} · warnings {warns}";
            UpdateStatus();

            // History panel is the source of truth for "what just happened" —
            // refresh it so the new bucket appears immediately, before any
            // dialog steals focus.
            try { VM.SetHistory(HistoryBridge.ReadHistory(_doc)); }
            catch (Exception hEx) { StingLog.Warn($"PlacementCenter post-run history refresh: {hEx.Message}"); }

            // Auto-paint the AVF compliance heat-map when the toggle is on so
            // the user sees coverage immediately after the commit.
            if (VM.RunOpts.AutoHeatmap && placed > 0)
            {
                try { OnHeatmap_Click(sender, e); }
                catch (Exception hmEx) { StingLog.Warn($"PlacementCenter auto-heatmap: {hmEx.Message}"); }
            }

            // Optional: validators on what just happened
            if (VM.RunOpts.RunValidators)
                ShowFindings(scopeToProvenance: true, headline: "Run + post-validation");
            else
                TaskDialog.Show("STING — Placement Centre",
                    $"Placement run complete.\n\n" +
                    $"Placed: {placed}\nSkipped: {skipped}\nWarnings: {warns}\n\n" +
                    "Run Validate now to audit the result, or click Undo last run to revert.");
        }

        private void OnPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }

            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            if (rules.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre", "No valid rules to preview.");
                return;
            }
            var roomIds = PlacementCenterBridge.ResolveScope(_uiDoc, VM.RunOpts.Scope);
            if (roomIds.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre",
                    $"Scope '{VM.RunOpts.Scope}' resolved zero rooms — preview cancelled.");
                return;
            }
            try
            {
                var src = new PlacementPreviewSource(_doc, roomIds, rules);
                PreviewController.Start(_doc.ActiveView, src);
                VM.Status = $"Preview: {roomIds.Count} room(s) · ESC or click Preview again to clear";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnPreview", ex);
                TaskDialog.Show("STING — Placement Centre", $"Preview failed: {ex.Message}");
            }
        }

        private void OnValidate_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }
            ShowFindings(scopeToProvenance: false, headline: "Project-wide validation");
        }

        private void ShowFindings(bool scopeToProvenance, string headline)
        {
            try
            {
                // PC-23 — collect picked validators.
                var mask = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (vClearance.IsChecked   == true) mask.Add("Clearance");
                if (vMaintenance.IsChecked == true) mask.Add("Maintenance");
                if (vConnectivity.IsChecked == true) mask.Add("Connectivity");
                if (vFill.IsChecked        == true) mask.Add("Fill");
                if (vSpec.IsChecked        == true) mask.Add("Spec");
                if (vTermination.IsChecked == true) mask.Add("Termination");
                if (vSlope.IsChecked       == true) mask.Add("Slope");
                if (vSeparation.IsChecked  == true) mask.Add("Separation");
                var findings = PlacementCenterBridge.RunValidators(_doc, mask);
                if (scopeToProvenance && _lastRunUtc.HasValue)
                {
                    findings = PlacementCenterBridge.FilterToProvenance(_doc, findings, _lastRunUtc.Value);
                    headline += " · just-placed only";
                }

                int errs  = findings.Count(f => f.Severity == ValidationSeverity.Error);
                int warns = findings.Count(f => f.Severity == ValidationSeverity.Warning);
                int infos = findings.Count(f => f.Severity == ValidationSeverity.Info);

                var panel = StingResultPanel.Create("STING — Placement Centre · Validation")
                    .SetSubtitle(headline)
                    .AddSection("SUMMARY")
                    .Metric("Total findings", findings.Count.ToString())
                    .Metric("Errors",   errs.ToString())
                    .Metric("Warnings", warns.ToString())
                    .Metric("Info",     infos.ToString());

                if (findings.Count > 0)
                {
                    panel.AddSection("FINDINGS (top 40)");
                    foreach (var f in findings.OrderByDescending(x => x.Severity).Take(40))
                        panel.Text(f.ToString());
                }
                panel.Show();

                VM.Status = $"Validate complete · {errs}e {warns}w {infos}i";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.ShowFindings", ex);
                TaskDialog.Show("STING — Placement Centre", $"Validate failed: {ex.Message}");
            }
        }
        // ── Phase 127-C — Family-side ────────────────────────────────

        private void OnInspectFamily_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }
            if (VM.Selected == null || string.IsNullOrEmpty(VM.Selected.CategoryFilter))
            {
                VM.SetFamilyHints(null);
                VM.Status = "Pick a rule with a non-empty CategoryFilter first.";
                UpdateStatus();
                return;
            }
            try
            {
                var rows = FamilyHintsBridge.Inspect(_doc, VM.Selected.CategoryFilter);
                VM.SetFamilyHints(rows);
                int populated = rows.Count(r => !string.IsNullOrEmpty(r.Value));
                VM.Status = $"Inspected {VM.Selected.CategoryFilter} · {populated} of {rows.Count} hint param(s) populated";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnInspectFamily", ex);
                TaskDialog.Show("STING — Placement Centre", $"Inspect failed: {ex.Message}");
            }
        }

        private void OnPushFamilies_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }
            if (VM.Selected == null)
            {
                TaskDialog.Show("STING — Placement Centre", "Pick a rule first.");
                return;
            }
            if (string.IsNullOrEmpty(VM.Selected.CategoryFilter))
            {
                TaskDialog.Show("STING — Placement Centre", "Selected rule has no CategoryFilter.");
                return;
            }
            var confirm = new TaskDialog("STING — Push placement hints to family types")
            {
                MainInstruction = $"Write rule values to every family type in '{VM.Selected.CategoryFilter}'?",
                MainContent =
                    "Writes the following parameters on each matching FamilySymbol:\n" +
                    "  Identity:  PLACE_ORIENTATION_RULE_TXT (cleared), STING_FIXTURE_VARIANT_TXT, STING_ROOM_TYPE_FILTER_TXT\n" +
                    "  Geometry:  PLACE_MOUNT_HEIGHT_MM, PLACE_OFFSET_X_MM, PLACE_MIN_SPACING_MM\n" +
                    "  Rule:      PLACE_ANCHOR_TXT, PLACE_SIDE_TXT, PLACE_PRIORITY_INT, PLACE_MAX_PER_ROOM_INT\n" +
                    "  Notes:     STING_PLACEMENT_NOTES_TXT\n\n" +
                    "Parameters that don't exist on the family type are skipped silently. " +
                    "Use Undo to reverse all writes as a single transaction.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return;

            try
            {
                // PC-11 — collect optional clearance/envelope/weight overrides from the editor.
                var extras = new FamilyHintsBridge.PushExtras
                {
                    ClearanceMm      = TryParseOpt(txtClr.Text),
                    ClearanceFrontMm = TryParseOpt(txtClrFront.Text),
                    ClearanceBackMm  = TryParseOpt(txtClrBack.Text),
                    ClearanceSideMm  = TryParseOpt(txtClrSide.Text),
                    ClearanceTopMm   = TryParseOpt(txtClrTop.Text),
                    WeightKg         = TryParseOpt(txtWeightKg.Text),
                    EnvWMm           = TryParseOpt(txtEnvW.Text),
                    EnvDMm           = TryParseOpt(txtEnvD.Text),
                    EnvHMm           = TryParseOpt(txtEnvH.Text),
                    FireSepMm        = TryParseOpt(txtFireSep.Text),
                };
                var (types, writes) = FamilyHintsBridge.PushRuleToFamilyTypes(_doc, VM.Selected, extras);
                VM.Status = $"Pushed to {types} family type(s) · {writes} parameter write(s)";
                UpdateStatus();
                TaskDialog.Show("STING — Placement Centre",
                    $"Push complete.\n\nFamily types updated: {types}\nParameters written: {writes}\n\n" +
                    (types == 0
                        ? "No family types matched the category. Confirm the CategoryFilter exactly matches the Revit category name."
                        : "Re-run Inspect to confirm the new values."));
                // Refresh inspect grid if a category is on screen
                if (VM.Selected != null) OnInspectFamily_Click(sender, e);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnPushFamilies", ex);
                TaskDialog.Show("STING — Placement Centre", $"Push failed: {ex.Message}");
            }
        }
        // ── Phase 127-D — polish ─────────────────────────────────────

        private void OnHeatmap_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }
            try
            {
                using (var t = new Transaction(_doc, "STING — Placement Centre · AVF compliance heat-map"))
                {
                    t.Start();
                    AvfHeatmapEngine.Clear(_doc.ActiveView);
                    int n = AvfHeatmapEngine.Paint(_doc.ActiveView, new ComplianceHeatmapAdapter());
                    t.Commit();
                    VM.Status = $"Heat-map painted · {n} primitive(s) on '{_doc.ActiveView.Name}'";
                    UpdateStatus();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnHeatmap", ex);
                TaskDialog.Show("STING — Placement Centre", $"Heat-map failed: {ex.Message}");
            }
        }

        private void OnGDStudy_Click(object sender, RoutedEventArgs e)
        {
            string path = "Data\\GenerativeDesign\\STING_FIXTURE_PLACEMENT.dyn";
            var td = new TaskDialog("STING — Generative Design Study")
            {
                MainInstruction = "Run the placement study in Generative Design",
                MainContent =
                    "STING ships a Generative Design study at\n  " + path + "\n\n" +
                    "Open Manage ▸ Generative Design ▸ Create Study, pick STING Fixture Placement, " +
                    "and tune SpacingBias / CoverageTarget / ClearancePenalty. The study reuses the " +
                    "in-memory rule set this centre is editing.",
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            td.Show();
        }

        private void OnHistoryRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            try
            {
                var rows = HistoryBridge.ReadHistory(_doc);
                VM.SetHistory(rows);
                int total = rows.Sum(r => r.Count);
                VM.Status = $"History · {rows.Count} bucket(s), {total} placement(s) on record";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnHistoryRefresh", ex);
                TaskDialog.Show("STING — Placement Centre", $"History refresh failed: {ex.Message}");
            }
        }

        // Double-click any history row to select its placed instances in
        // Revit. Uses the ids the bucket already carries — no second
        // provenance scan. Skips ids that have since been deleted.
        private void OnHistory_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_uiDoc == null) return;
            var row = gridHistory.SelectedItem as HistoryBridge.HistoryRow;
            if (row == null || row.Ids == null || row.Ids.Count == 0)
            {
                VM.Status = "History row has no element ids attached.";
                UpdateStatus();
                return;
            }
            try
            {
                var live = row.Ids
                    .Where(id => id != null && id != ElementId.InvalidElementId &&
                                 _doc.GetElement(id) != null)
                    .ToList();
                _uiDoc.Selection.SetElementIds(live);
                VM.Status = $"Selected {live.Count} of {row.Ids.Count} element(s) from history bucket {row.CreatedUtc}";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementCenter.OnHistory_DoubleClick: {ex.Message}");
            }
        }

        private void OnUndoLast_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }

            // Prefer the in-memory _lastPlacedIds when this centre instance ran a
            // placement; fall back to the most-recent provenance bucket otherwise.
            List<ElementId> ids;
            string source;
            if (_lastPlacedIds != null && _lastPlacedIds.Count > 0)
            {
                ids = _lastPlacedIds.ToList();
                source = "this centre's last run";
            }
            else
            {
                var hist = HistoryBridge.MostRecent(_doc);
                if (hist == null || hist.Ids.Count == 0)
                {
                    TaskDialog.Show("STING — Placement Centre",
                        "No prior STING placements found in the model. Run Placement first.");
                    return;
                }
                ids = hist.Ids;
                source = $"provenance bucket {hist.CreatedUtc} · {hist.Engine}";
            }

            var confirm = new TaskDialog("STING — Undo last placement run")
            {
                MainInstruction = $"Delete {ids.Count} element(s)?",
                MainContent =
                    "Source: " + source + "\n\n" +
                    "Deletion runs in a single Revit transaction so a manual Undo will " +
                    "still restore the elements as one step.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return;

            try
            {
                var (deleted, skipped) = HistoryBridge.DeleteIds(_doc, ids);
                _lastPlacedIds.Clear();
                _lastRunUtc = null;
                VM.Status = $"Undo last · deleted {deleted}, skipped {skipped}";
                UpdateStatus();
                TaskDialog.Show("STING — Placement Centre",
                    $"Deleted: {deleted}\nSkipped: {skipped}\n\n" +
                    (skipped > 0 ? "Skipped elements were already removed or refused deletion (hosted, pinned)." : "All elements removed cleanly."));
                OnHistoryRefresh_Click(sender, e);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnUndoLast", ex);
                TaskDialog.Show("STING — Placement Centre", $"Undo failed: {ex.Message}");
            }
        }

        private void OnSaveViewPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }
            var view = _doc.ActiveView;
            if (view == null) { TaskDialog.Show("STING — Placement Centre", "No active view."); return; }

            string presetName = $"PlacementCentre/{VM.RunOpts.Scope}/{DateTime.UtcNow:yyyyMMdd-HHmm}";
            try
            {
                using (var t = new Transaction(_doc, "STING — Save view preset (Pack 125/M)"))
                {
                    t.Start();
                    StingTools.Core.Storage.StingViewPresetSchema.Write(view, presetName, "");
                    t.Commit();
                }
                VM.Status = $"Saved preset '{presetName}' on view '{view.Name}'";
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnSaveViewPreset", ex);
                TaskDialog.Show("STING — Placement Centre", $"Save preset failed: {ex.Message}");
            }
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── List + add/delete ────────────────────────────────────────

        private void OnAdd_Click(object sender, RoutedEventArgs e)
        {
            VM.AddRule();
            UpdateStatus();
        }

        private void OnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (VM.Selected == null) { return; }
            var td = new TaskDialog("STING — Delete rule")
            {
                MainInstruction = $"Remove rule for category '{VM.Selected.CategoryFilter}'?",
                MainContent = "The rule is removed from the in-memory set; click Save Project to persist.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (td.Show() == TaskDialogResult.Yes)
            {
                VM.DeleteSelected();
                UpdateStatus();
            }
        }

        private void OnCloneSelected_Click(object sender, RoutedEventArgs e)
        {
            var picks = gridRules.SelectedItems
                .OfType<PlacementRuleViewModel>().ToList();
            if (picks.Count == 0) return;
            int n = VM.CloneMany(picks);
            UpdateStatus();
            if (n > 0) gridRules.ScrollIntoView(VM.Selected);
        }

        private void OnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var picks = gridRules.SelectedItems
                .OfType<PlacementRuleViewModel>().ToList();
            if (picks.Count == 0) return;
            var confirm = new TaskDialog("STING — Delete selected rules")
            {
                MainInstruction = $"Remove {picks.Count} rule(s)?",
                MainContent = "The rules are removed from the in-memory set; click Save Project to persist.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return;
            VM.DeleteMany(picks);
            UpdateStatus();
        }

        private void OnSelectInvalid_Click(object sender, RoutedEventArgs e)
        {
            gridRules.SelectedItems.Clear();
            foreach (var r in VM.Rules.Where(r => !r.IsValid))
                gridRules.SelectedItems.Add(r);
        }

        private void OnSelectDirty_Click(object sender, RoutedEventArgs e)
        {
            gridRules.SelectedItems.Clear();
            foreach (var r in VM.Rules.Where(r => r.IsDirty))
                gridRules.SelectedItems.Add(r);
        }

        private void OnSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            VM.SearchText = txtSearch.Text;
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            VM.ShowDirtyOnly   = chipDirty?.IsChecked   == true;
            VM.ShowInvalidOnly = chipInvalid?.IsChecked == true;
            UpdateStatus();
        }

        private void OnGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            VM.Selected = gridRules.SelectedItem as PlacementRuleViewModel;
            SyncFieldsFromSelection();
        }

        // ── Rule field commit ────────────────────────────────────────

        private void CommitField(Action setter)
        {
            if (_suppressUiSync || VM.Selected == null) return;
            try
            {
                setter();
                // Ensure dirty flag is set even for wires that bypass ViewModel
                // properties and write directly to the underlying Model POCO.
                if (!VM.Selected.IsDirty) VM.Selected.IsDirty = true;
                VM.Selected.Validate();
                txtRuleError.Text = VM.Selected.IsValid ? "" : VM.Selected.ErrorMessage;
                VM.RebuildCategories();
                UpdateStatus();
                gridRules.Items.Refresh();

                // PC-21 — debounce a live preview refresh.
                if (_livePreviewEnabled) RestartPreviewDebounce();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPlacementCenter.CommitField: {ex.Message}");
                txtRuleError.Text = ex.Message;
            }
        }

        private void RestartPreviewDebounce()
        {
            if (_previewDebounce == null)
            {
                _previewDebounce = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500),
                };
                _previewDebounce.Tick += (s, e) =>
                {
                    _previewDebounce.Stop();
                    if (_livePreviewEnabled && VM.HasSelection && VM.Selected.IsValid)
                    {
                        try { OnPreview_Click(this, new RoutedEventArgs()); }
                        catch (Exception ex) { StingLog.Warn($"PC-21 live preview: {ex.Message}"); }
                    }
                };
            }
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private void SyncFieldsFromSelection()
        {
            _suppressUiSync = true;
            try
            {
                pnlRuleDetail.IsEnabled = VM.HasSelection;
                if (VM.Selected == null) { txtRuleError.Text = ""; return; }
                var s = VM.Selected;

                // Rule core
                cmbCategory.Text          = s.CategoryFilter ?? "";
                cmbVariant.Text           = s.VariantHint    ?? "";
                txtRoom.Text              = s.RoomFilter     ?? "";
                txtNotes.Text             = s.Notes          ?? "";
                cmbAnchor.SelectedItem    = s.AnchorType;
                cmbSide.SelectedItem      = s.SideConstraint;
                txtPriority.Text          = s.Priority.ToString(CultureInfo.InvariantCulture);

                // Geometry (PC-06)
                txtOffsetX.Text           = s.OffsetXMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtOffsetY.Text           = s.OffsetYMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtOffsetZ.Text           = s.OffsetZMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtRotation.Text          = s.RotationDeg.ToString("0.##", CultureInfo.InvariantCulture);
                cmbMountRef.SelectedItem  = string.IsNullOrEmpty(s.MountingReference) ? "FFL" : s.MountingReference;
                txtMountH.Text            = s.MountingHeightMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtSpacing.Text           = s.MinSpacingMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtMaxRoom.Text           = s.MaxPerRoom.ToString(CultureInfo.InvariantCulture);

                // Room scoping (PC-07)
                txtRoomExclude.Text       = s.ExcludeRoomFilter    ?? "";
                txtDepartment.Text        = s.RoomDepartmentFilter ?? "";
                txtLevelFilter.Text       = s.LevelFilter ?? "";
                txtPhaseFilter.Text       = s.PhaseFilter ?? "";
                txtWorksetFilter.Text     = s.WorksetFilter ?? "";
                txtMinArea.Text           = s.MinAreaM2.ToString("0.##", CultureInfo.InvariantCulture);
                txtMaxArea.Text           = s.MaxAreaM2.ToString("0.##", CultureInfo.InvariantCulture);

                // Rule kind / density / linear (PC-12)
                cmbRuleKind.SelectedItem  = s.RuleKind.ToString();
                txtPerArea.Text           = s.PerAreaM2.ToString("0.##", CultureInfo.InvariantCulture);
                txtPerOcc.Text            = s.PerOccupant.ToString("0.##", CultureInfo.InvariantCulture);
                txtPerLin.Text            = s.PerLinearMetre.ToString("0.##", CultureInfo.InvariantCulture);

                // Dependencies (PC-13)
                txtRuleId.Text            = s.RuleId ?? "";
                txtDependsOn.Text         = s.DependsOn ?? "";
                cmbRelativeTo.SelectedItem = s.RelativeTo ?? "";
                txtCoPlace.Text           = s.Model?.CoPlaceWith != null ? string.Join(",", s.Model.CoPlaceWith) : "";
                txtConflict.Text          = s.Model?.ConflictsWith != null ? string.Join(",", s.Model.ConflictsWith) : "";

                // Standards & classification
                txtStandardRef.Text       = s.StandardRef ?? "";
                txtUniclassPr.Text        = s.UniclassPr ?? "";

                // Rule core extensions (PC-08 / identity)
                txtFamilyTypeRegex.Text   = s.Model.FamilyTypeRegex ?? "";
                txtSourcePack.Text        = s.Model.SourcePack ?? "";

                // Geometry extensions
                txtToleranceMm.Text       = s.Model.ToleranceMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtMaxSpacing.Text        = s.Model.MaxSpacingMm.ToString("0.##", CultureInfo.InvariantCulture);

                // PC-12 density extensions
                txtPerBed.Text            = s.Model.PerBed.ToString("0.##", CultureInfo.InvariantCulture);
                txtPerWorkstation.Text    = s.Model.PerWorkstation.ToString("0.##", CultureInfo.InvariantCulture);
                txtPerPupil.Text          = s.Model.PerPupil.ToString("0.##", CultureInfo.InvariantCulture);
                txtPerToiletCubicle.Text  = s.Model.PerToiletCubicle.ToString("0.##", CultureInfo.InvariantCulture);
                txtOccupancyParam.Text    = s.Model.OccupancyParamName ?? "";

                // PC-14 coverage grid
                txtCoverageRadius.Text    = s.Model.CoverageRadiusMm.ToString("0.##", CultureInfo.InvariantCulture);
                chkGuaranteeCoverage.IsChecked = s.Model.GuaranteeCoverage;

                // PC-15 integrated routing
                cmbRoutingMode.SelectedItem          = s.Model.RoutingMode ?? "NONE";
                cmbRouteSegmentCategory.SelectedItem = s.Model.RouteSegmentCategory ?? "";
                txtRouteOffset.Text       = s.Model.RouteOffsetMm.ToString("0.##", CultureInfo.InvariantCulture);

                // PC-16 construction phasing / PC-17 cluster
                chkTwoPhase.IsChecked     = s.Model.TwoPhaseEnabled;
                cmbConstructionPhase.SelectedItem = s.Model.ConstructionPhase ?? "FINISHED";
                chkClusterMember.IsChecked = s.Model.IsClusterMember;
                txtClusterGroupId.Text    = s.Model.ClusterGroupId ?? "";

                // PC-11 — clearance / envelope / weight fields are per-push extras,
                // not part of the rule. Clear them when selection changes.
                txtClr.Text = ""; txtClrFront.Text = ""; txtClrBack.Text = "";
                txtClrSide.Text = ""; txtClrTop.Text = ""; txtWeightKg.Text = "";
                txtEnvW.Text = ""; txtEnvD.Text = ""; txtEnvH.Text = ""; txtFireSep.Text = "";

                txtRuleError.Text         = s.IsValid ? "" : s.ErrorMessage;
            }
            finally { _suppressUiSync = false; }
        }

        // PC-12 helper — string from RuleKinds combo to enum.
        private static StingTools.Core.Placement.PlacementRuleKind ParseRuleKind(string s)
        {
            if (string.Equals(s, "Density", StringComparison.OrdinalIgnoreCase))
                return StingTools.Core.Placement.PlacementRuleKind.Density;
            if (string.Equals(s, "Linear", StringComparison.OrdinalIgnoreCase))
                return StingTools.Core.Placement.PlacementRuleKind.Linear;
            return StingTools.Core.Placement.PlacementRuleKind.Point;
        }

        // PC-13 helper — comma/semicolon-separated id list → trimmed list.
        private static List<string> ParseList(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var part in s.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (!string.IsNullOrEmpty(t)) list.Add(t);
            }
            return list;
        }

        // ── Status bar ───────────────────────────────────────────────

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VM.Status) ||
                e.PropertyName == nameof(VM.DirtyCount) ||
                e.PropertyName == nameof(VM.ProjectFilePath))
                UpdateStatus();
        }

        private void UpdateStatus()
        {
            int dirty = VM.Rules.Count(r => r.IsDirty);
            int invalid = VM.Rules.Count(r => !r.IsValid);
            txtStatus.Text = VM.Status ?? "Ready";
            txtMeta.Text   = $"{VM.Rules.Count} rule(s) · {VM.Categories.Count} categor{(VM.Categories.Count == 1 ? "y" : "ies")} · " +
                             $"{(dirty   > 0 ? $"{dirty} unsaved · "   : "")}" +
                             $"{(invalid > 0 ? $"{invalid} invalid · " : "")}" +
                             $"{(string.IsNullOrEmpty(VM.ProjectFilePath) ? "(no project)" : System.IO.Path.GetFileName(VM.ProjectFilePath))}";
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        private static double ParseDouble(string s, double fallback) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        // PC-11 helper — empty / non-numeric input → null (don't push that field).
        private static double? TryParseOpt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        // ── Run & Routing tab ────────────────────────────────────────

        private void OnRunScope_Changed(object sender, RoutedEventArgs e)
        {
            if (rbRunScopeView?.IsChecked == true) VM.RunOpts.Scope = "ActiveView";
            else if (rbRunScopeSel?.IsChecked  == true) VM.RunOpts.Scope = "Selection";
            else if (rbRunScopeProj?.IsChecked == true) VM.RunOpts.Scope = "Project";
        }

        private void OnRunAllRules_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING", "No active document."); return; }
            bool dry = chkRunDryRun?.IsChecked == true;
            try
            {
                var rules   = PlacementRuleLoader.Load(_doc.PathName);
                var roomIds = PlacementCenterBridge.ResolveScope(_uiDoc, VM.RunOpts.Scope);
                var progress = new StingProgressDialog("Placing fixtures…", roomIds.Count);
                List<ElementId> placed;
                using (var tg = new TransactionGroup(_doc, "STING Place Fixtures (Run & Routing)"))
                {
                    tg.Start();
                    placed = FixturePlacementEngine.PlaceFixturesInScope(
                        _doc, roomIds, rules, dry, progress);
                    tg.Assimilate();
                }
                progress.Close();
                _runResultIds  = placed ?? new List<ElementId>();
                _runReportText = $"Placed {_runResultIds.Count} fixture(s) in {roomIds.Count} room(s) [{(dry ? "DRY-RUN" : "live")}]";
                _lastPlacedIds = _runResultIds;
                _lastRunUtc    = DateTime.UtcNow;
                ShowInlineResult(
                    _runResultIds.Count > 0 ? $"✓ {_runResultIds.Count} fixtures placed" : "Nothing placed",
                    new[] { $"Rooms: {roomIds.Count}", $"Fixtures: {_runResultIds.Count}", dry ? "Dry-run" : "Live" },
                    PlacementRuleLoader.LastValidationWarnings.Take(10).ToArray());
                RefreshDrawingTypeContext();
            }
            catch (Exception ex)
            {
                StingLog.Error("OnRunAllRules_Click", ex);
                TaskDialog.Show("STING Error", ex.Message);
            }
        }

        private void OnRunToiletRooms_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING", "No active document."); return; }
            bool dry = chkRunDryRun?.IsChecked == true;
            try
            {
                var provision = ToiletRoomPlacerService.ComputeProvision(100, BuildingUse.Office, OccupantSplit.Equal5050);
                var svc = new ToiletRoomPlacerService
                {
                    OccupantCount     = 100,
                    Use               = BuildingUse.Office,
                    Split             = OccupantSplit.Equal5050,
                    DryRun            = dry,
                    AutoRoutePlumbing = false,
                };
                ToiletRoomPlacementResult result;
                using (var txn = new Transaction(_doc, "STING Toilet Room Fixtures"))
                {
                    txn.Start();
                    result = svc.PlaceAll(_doc, txn);
                    txn.Commit();
                }
                _runResultIds  = result.PlacementResult?.PlacedIds ?? new List<ElementId>();
                _runReportText = result.ReportText();
                _lastPlacedIds = _runResultIds;
                _lastRunUtc    = DateTime.UtcNow;
                var gaps = result.ComplianceGaps.Count > 0
                    ? result.ComplianceGaps.Take(6).ToArray()
                    : new[] { "All BS 6465-1 provisions met." };
                ShowInlineResult(
                    result.IsCompliant ? $"✓ Compliant — {result.FixturesPlaced} fixtures" : $"⚠ {result.ComplianceGaps.Count} gap(s)",
                    new[] { $"Rooms: {result.RoomsProcessed}", $"Fixtures: {result.FixturesPlaced}", dry ? "Dry-run" : "Live" },
                    gaps);
            }
            catch (Exception ex)
            {
                StingLog.Error("OnRunToiletRooms_Click", ex);
                TaskDialog.Show("STING Error", ex.Message);
            }
        }

        private void OnRunLightingGrid_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Placement_LightingGrid");

        private void OnLearnPlacement_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Placement_Learn");

        private void OnRunPreview_Click(object sender, RoutedEventArgs e)
            => OnPreview_Click(sender, e);

        private void OnAutoDropRouting_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Routing_AutoDrop");

        private void OnGenerateLayout_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Routing_GenerateLayout");

        private void OnPlumbingRouter_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING", "No active document."); return; }
            try
            {
                var fixtures = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .ToList();
                if (fixtures.Count == 0)
                {
                    ShowInlineResult("No plumbing fixtures found", new[] { "Fixtures: 0" }, new[] { "Place fixtures first, then route." });
                    return;
                }
                using (var txn = new Transaction(_doc, "STING Auto-Route Plumbing"))
                {
                    txn.Start();
                    var router = new PlumbingFixtureRouter();
                    router.RouteAll(_doc, fixtures, txn);
                    txn.Commit();
                }
                _runReportText = $"Routed drainage for {fixtures.Count} plumbing fixture(s).";
                ShowInlineResult($"✓ Routing complete — {fixtures.Count} fixture(s)", new[] { $"Fixtures: {fixtures.Count}" },
                    new[] { "Gravity slope 2.5%", "AAV placed at runs >3000 mm" });
            }
            catch (Exception ex)
            {
                StingLog.Error("OnPlumbingRouter_Click", ex);
                TaskDialog.Show("STING Error", ex.Message);
            }
        }

        private void OnValidateFills_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Routing_ValidateFills");

        private void OnPlaceHangers_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Routing_PlaceHangers");

        private void OnRunAllValidators_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Validation_RunAll");

        private void OnBS6465Audit_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { TaskDialog.Show("STING", "No active document."); return; }
            try
            {
                var provision = ToiletRoomPlacerService.ComputeProvision(100, BuildingUse.Office, OccupantSplit.Equal5050);
                ShowInlineResult("BS 6465-1 Provision Preview (100 occ, Office, 50/50)",
                    new[]
                    {
                        $"WCs (M): {provision.MinWcsMale}",
                        $"WCs (F): {provision.MinWcsFemale}",
                        $"Urinals: {provision.MinUrinalsMale}",
                        $"Basins: {provision.MinBasins}",
                        $"Accessible: {provision.MinAccessibleWcs}",
                        $"Baby change: {(provision.BabyChangeRequired ? "Yes" : "No")}",
                    },
                    new[] { provision.Summary() });
            }
            catch (Exception ex)
            {
                StingLog.Error("OnBS6465Audit_Click", ex);
                TaskDialog.Show("STING Error", ex.Message);
            }
        }

        private void OnClearanceScan_Click(object sender, RoutedEventArgs e)
            => ShowFindings(false, "Clearance scan");

        private void OnPenetrationCoverage_Click(object sender, RoutedEventArgs e)
            => StingDockPanel.DispatchCommand("Validation_PenetrationCoverage");

        private void OnScoreThreshold_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb &&
                double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) &&
                v > 0.0 && v <= 1.0)
            {
                PlacementScorer.ScoreThreshold = v;
            }
            else if (sender is TextBox tb2)
            {
                tb2.Text = PlacementScorer.ScoreThreshold.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        private void OnSelectLastPlaced_Click(object sender, RoutedEventArgs e)
        {
            if (_uiDoc == null || _runResultIds == null || _runResultIds.Count == 0)
            {
                TaskDialog.Show("STING", "No placed elements to select from the last run.");
                return;
            }
            try { _uiDoc.Selection.SetElementIds(_runResultIds); }
            catch (Exception ex) { StingLog.Warn($"OnSelectLastPlaced_Click: {ex.Message}"); }
        }

        private void OnCopyRunReport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_runReportText)) return;
            try { System.Windows.Clipboard.SetText(_runReportText); }
            catch (Exception ex) { StingLog.Warn($"OnCopyRunReport_Click: {ex.Message}"); }
        }

        // ── Tools tab ────────────────────────────────────────────────

        private void OnToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
                StingDockPanel.DispatchCommand(tag);
        }

        // ── Inline result panel ──────────────────────────────────────

        private void ShowInlineResult(string headline,
                                      IEnumerable<string> metrics,
                                      IEnumerable<string> findings)
        {
            if (txtRunResultHeadline != null)
                txtRunResultHeadline.Text = headline ?? "";

            if (lstRunMetrics != null)
                lstRunMetrics.ItemsSource = metrics?.ToList() ?? new List<string>();

            if (lstRunFindings != null)
                lstRunFindings.ItemsSource = findings?.ToList() ?? new List<string>();

            if (grpRunResult != null)
                grpRunResult.Visibility = Visibility.Visible;
        }

        // ── DrawingType context strip ────────────────────────────────

        private void RefreshDrawingTypeContext()
        {
            try
            {
                var view = _uiDoc?.ActiveView;
                if (view == null)
                {
                    SetDtContextLabels("—", "—", "—");
                    return;
                }
                var dtId = DrawingTypeStamper.Read(view);
                if (string.IsNullOrEmpty(dtId))
                {
                    SetDtContextLabels("(not stamped)", "—", "—");
                    return;
                }
                var dt = _doc != null ? DrawingTypeRegistry.Get(_doc, dtId) : null;
                string packId = "—";
                if (_doc != null && DrawingTypeRegistry.TryGetPack(_doc, dtId, out var pack) && pack != null)
                    packId = pack.Id ?? "—";
                SetDtContextLabels(
                    dt?.Id ?? dtId,
                    packId,
                    string.IsNullOrEmpty(dt?.Discipline) ? "—" : dt.Discipline);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementCenter.RefreshDrawingTypeContext: {ex.Message}");
                SetDtContextLabels("error", "—", "—");
            }
        }

        private void SetDtContextLabels(string dtId, string packId, string disc)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (txtDtContextId   != null) txtDtContextId.Text   = dtId;
                    if (txtDtContextPack != null) txtDtContextPack.Text = packId;
                    if (txtDtContextDisc != null) txtDtContextDisc.Text = disc;
                }
                catch { }
            });
        }

        private void BtnRefreshDtContext_Click(object sender, RoutedEventArgs e)
            => RefreshDrawingTypeContext();

        private void OnBusResult(PlacementRunSummary summary)
        {
            if (summary == null) return;
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Update context strip headline.
                    if (txtLastResultSource != null)
                        txtLastResultSource.Text = $"[{summary.Source}]";
                    if (txtLastResultHeadlineStrip != null)
                        txtLastResultHeadlineStrip.Text = summary.Headline;

                    // Also update the Run tab's result panel if those controls exist.
                    var headline = FindName("txtRunResultHeadline") as System.Windows.Controls.TextBlock;
                    if (headline != null) headline.Text = summary.Headline;

                    var metricsList = FindName("lstRunMetrics") as System.Windows.Controls.ItemsControl;
                    if (metricsList != null) metricsList.ItemsSource = summary.Metrics;

                    var findingsList = FindName("lstRunFindings") as System.Windows.Controls.ItemsControl;
                    if (findingsList != null) findingsList.ItemsSource = summary.Warnings;

                    // Update the result ids for selection.
                    _runResultIds = summary.AffectedIds ?? new List<Autodesk.Revit.DB.ElementId>();
                    _runReportText = summary.Headline;

                    // Show the result panel if it's collapsed.
                    var resultPanel = FindName("grpRunResult") as System.Windows.Controls.GroupBox;
                    if (resultPanel != null) resultPanel.Visibility = System.Windows.Visibility.Visible;

                    // Refresh context strip DT info if the summary carries a new DT id.
                    if (!string.IsNullOrEmpty(summary.DrawingTypeId) && _doc != null)
                    {
                        var dt = DrawingTypeRegistry.Get(_doc, summary.DrawingTypeId);
                        string packId = "—";
                        if (DrawingTypeRegistry.TryGetPack(_doc, summary.DrawingTypeId, out var pk) && pk != null)
                            packId = pk.Id ?? "—";
                        SetDtContextLabels(
                            dt?.Id ?? summary.DrawingTypeId,
                            summary.PackId ?? packId,
                            dt?.Discipline ?? "—");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"PlacementCenter.OnBusResult: {ex.Message}"); }
            });
        }

        // ── Resources ────────────────────────────────────────────────

        private static bool _resEnsured;
        private static void EnsureResources()
        {
            if (_resEnsured) return;
            _resEnsured = true;
            try
            {
                var app = Application.Current;
                if (app == null) return;
                if (!app.Resources.Contains("DirtyDotConverter"))
                    app.Resources.Add("DirtyDotConverter", new DirtyDotConverter());
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPlacementCenter.EnsureResources: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// IsDirty (bool) → "●" / "" — visual indicator in the rule grid's
    /// first column. Cheap converter, no resource bloat.
    /// </summary>
    public class DirtyDotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "●" : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
