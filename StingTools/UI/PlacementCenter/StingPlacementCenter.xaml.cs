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
using PlacementResult = StingTools.Core.Placement.PlacementResult;
using StingTools.Core.Validation;
using StingTools.Core.Visualization;
using ValidationSeverity = StingTools.Core.Validation.ValidationSeverity;
using TextBox = System.Windows.Controls.TextBox;

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

        // F1 — the DWG import the user last chose in the Map-DWG-Layers dialog, so the
        // subsequent "Place STING fixtures from DWG" run targets the SAME import (rather
        // than silently re-resolving to a different / only one). -1 = none chosen yet.
        private long _lastMappedImportId = -1;

        public StingPlacementCenter(UIApplication uiApp)
        {
            // Pre-register the IsDirty → "●" converter before InitializeComponent
            // so the DataGrid binding resolves on first paint.
            EnsureResources();

            InitializeComponent();
            ThemeManager.RegisterTarget(this);
            // Phase 139.21 — surface the assembly's build stamp on the
            // window title bar. If the user runs two consecutive
            // sessions and the stamp doesn't change, the plug-in DLL
            // wasn't refreshed (extract_plugin.sh skipped the copy or
            // Revit cached the old DLL).
            try
            {
                this.Title = $"STING — Placement Centre  [build {StingTools.Core.Placement.FixturePlacementEngine.BuildStamp}]";
            }
            catch { }
            ThemeManager.InitialiseResources();

        // Phase 139.10b — ExternalEvent.Create must be called from a
            // Revit API context. Constructor runs inside the
            // OpenPlacementCenterCommand.Execute, which IS an API context;
            // the button-click handler is NOT. Create the event eagerly
            // here so it's ready for OnRunPlacement_Click later.
            try
            {
                _runHandler = new PlacementRunHandler(this);
                _runEvent   = ExternalEvent.Create(_runHandler);
                // Same rule for the generic inline-action event (Rebuild Seeds,
                // Wall Chase, Import Overrides, …): ExternalEvent.Create must run
                // in this API context, NOT lazily from a button click — doing it
                // there throws "Attempting to create an ExternalEvent outside of a
                // standard API execution".
                _actionHandler = new PlacementActionHandler(this);
                _actionEvent   = ExternalEvent.Create(_actionHandler);
            }
            catch (Exception evEx)
            {
                StingLog.Warn($"PlacementCenter: ExternalEvent.Create at ctor: {evEx.Message}");
            }

            VM = new PlacementRulesViewModel();
            _uiApp = uiApp;
            _uiDoc = uiApp?.ActiveUIDocument;
            _doc = _uiDoc?.Document;

            // Findings parsed out of report text (Diagnose/Audit) carry an
            // ElementId payload; route their clicks to this window's selector.
            StingResultPanel.ElementSelectAction =
                id => { try { SelectInModel(new ElementId(id)); } catch { } };

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

            // Phase 139 — pack-chip + new-card combobox sources.
            if (cmbSourcePack != null)
            {
                cmbSourcePack.ItemsSource = VM.SourcePackChips;
                cmbSourcePack.SelectedIndex = 0;
            }
            if (cmbProfileBuildingType != null)    cmbProfileBuildingType.ItemsSource = VM.BuildingTypes;
            if (cmbBuildingType        != null)    cmbBuildingType.ItemsSource        = VM.BuildingTypes;
            if (cmbWetZone             != null)    cmbWetZone.ItemsSource             = VM.WetZoneOptions;
            if (cmbHeightStandard      != null)    cmbHeightStandard.ItemsSource      = VM.HeightStandardKeys;
            if (cmbRoutingMode         != null)    cmbRoutingMode.ItemsSource         = VM.RoutingModes;
            if (cmbRouteFace           != null)    cmbRouteFace.ItemsSource           = VM.RouteFaces;
            if (cmbRouteSegmentCategory != null)   cmbRouteSegmentCategory.ItemsSource = VM.RouteSegmentCategories;
            if (cmbGlazingSpec         != null)    cmbGlazingSpec.ItemsSource         = VM.GlazingSpecs;
            if (cmbMaintenanceClearance != null)   cmbMaintenanceClearance.ItemsSource = VM.MaintenanceClearances;

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

            // Phase 139.8 — Auto-place category checklist toggles.
            btnCatAll.Click  += (_,__) => SetAllCategoryChecks(true);
            btnCatNone.Click += (_,__) => SetAllCategoryChecks(false);

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
            // A6 (door/window clearance) — 0 = off
            if (txtDoorClearance != null)
                txtDoorClearance.LostFocus   += (_,__) => CommitField(() => VM.Selected.Model.DoorClearanceMm   = ParseDouble(txtDoorClearance.Text,   VM.Selected.Model.DoorClearanceMm));
            if (txtWindowClearance != null)
                txtWindowClearance.LostFocus += (_,__) => CommitField(() => VM.Selected.Model.WindowClearanceMm = ParseDouble(txtWindowClearance.Text, VM.Selected.Model.WindowClearanceMm));
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
            // Guard the inputs the window's construction depends on. The
            // Placement Centre loads project rules and places fixtures into
            // rooms, so it needs a live UIApplication + open document. Failing
            // fast here with a clear message avoids constructing a window bound
            // to a null document (the most common "Object reference not set"
            // on open).
            if (uiApp == null)
            {
                TaskDialog.Show("STING — Placement Centre",
                    "Revit application context unavailable — try again once Revit has finished loading.");
                return;
            }
            if (uiApp.ActiveUIDocument?.Document == null)
            {
                TaskDialog.Show("STING — Placement Centre",
                    "Open a Revit project before launching the Placement Centre.");
                return;
            }

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
                // Log the FULL exception (message + stack + inner) so a
                // surviving failure mode is pinpointable from StingTools.log.
                StingLog.Error("StingPlacementCenter.ShowOrFocus\n" + ex.ToString(), ex);
                _instance = null; // never leave a half-constructed singleton cached
                string where = ex.StackTrace?.Split('\n')?.FirstOrDefault()?.Trim();
                TaskDialog.Show("STING — Placement Centre",
                    $"Failed to open Placement Centre.\n\n{ex.GetType().Name}: {ex.Message}"
                    + (string.IsNullOrEmpty(where) ? "" : $"\n\nAt: {where}")
                    + "\n\nFull details in StingTools.log.");
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
                RaiseRevitToFront();
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
            if (_doc == null) { Toast("No document open."); return; }

            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            if (rules.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre",
                    "No valid rules to run. Add at least one rule with a non-empty CategoryFilter.");
                return;
            }

            // Phase 195 — #2 pack scope. Honour the SourcePack dropdown at run time:
            // when the user selects a specific pack (not "All"), only that pack's
            // rules run — so a residential run can be scoped to MK-Electrical /
            // Lighting-Pendants without editing JSON. "All" runs every pack (now
            // safe: the engine's same-category crowding guard stops overlapping
            // packs stacking fixtures). The pick is the existing grid dropdown.
            var packSel = VM?.SelectedSourcePack;
            if (!string.IsNullOrEmpty(packSel) && !packSel.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                int beforePack = rules.Count;
                rules = rules.Where(r =>
                {
                    var p = string.IsNullOrEmpty(r.SourcePack) ? "Baseline" : r.SourcePack;
                    return p.Equals(packSel, StringComparison.OrdinalIgnoreCase);
                }).ToList();
                StingLog.Info($"PlacementCenter: pack scope '{packSel}' kept {rules.Count}/{beforePack} rules for the run.");
                if (rules.Count == 0)
                {
                    Report("Run", StingResultPanel.Create("Placement — pack scope")
                        .SetSubtitle($"Pack '{packSel}' has no rules.")
                        .AddSection("Nothing to place")
                        .Alert($"The selected pack '{packSel}' contains no rules. Pick a different pack or 'All' in the Rules-tab dropdown."));
                    Toast($"Pack '{packSel}' has no rules — see Report panel.");
                    return;
                }
            }

            // Phase 139.8 — apply the explicit category checklist if any
            // box is ticked. Empty checklist = "every category in the rule
            // pack is allowed" (legacy behaviour).
            // Phase 139.20 — also surface the filter outcome so the user
            // sees what is and isn't ticked. Without this, a run that
            // places fire-alarm devices when the user thought they only
            // ticked "lights" looks like a bug — but is actually either
            // (a) the checkbox really is ticked, or (b) all cb fields
            // are null (stale XAML build).
            // Read the category checklist once; preflight + filter both use it.
            var allowed = ReadCategoryChecklist();

            // Phase 139.21 — prerequisites preflight. Hard-fail the run
            // when the model is missing setup that makes correct
            // placement impossible. Up to now the engine ran in
            // "best-effort" mode regardless of family-type / door-data
            // problems, producing the silent wrong-position bug the
            // user kept reporting.
            try
            {
                var blockers = new List<string>();
                var helpfulHints = new List<string>();

                // 1. Wall-anchored rules vs family placement type.
                //   Phase 139.21d — categories like Specialty Equipment, Mechanical
                //   Equipment, Electrical Equipment legitimately ship with non-
                //   wall-hosted families (free-standing tanks, generators, panels).
                //   Don't HARD-FAIL the run; warn the user that wall-anchored rules
                //   in those categories will not attach correctly and point them at
                //   the existing FamilyQuickEdit > Change Host command which can
                //   re-host a family interactively.  The engine still runs and
                //   places non-wall stuff fine.
                var wallRules = rules.Where(r =>
                {
                    var a = (r.AnchorType ?? "").ToUpperInvariant();
                    return a == "WALL_MIDPOINT" || a == "WALL_CORNER" || a == "WALL_FACE_OFFSET"
                        || a.StartsWith("DOOR_") || a.StartsWith("WINDOW_");
                }).ToList();
                var wallCats = wallRules.Select(r => r.CategoryFilter ?? "").Distinct().ToList();
                foreach (var cat in wallCats)
                {
                    if (string.IsNullOrEmpty(cat)) continue;
                    var symbols = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>().Where(fs => fs.Category != null
                            && string.Equals(fs.Category.Name, cat, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (symbols.Count == 0) continue;
                    bool anyHosted = symbols.Any(fs => fs.Family?.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted);
                    bool anyFaceBased = symbols.Any(fs => fs.Family?.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased);
                    // Face-based / WorkPlaneBased families CAN be placed on a wall via
                    // doc.Create.NewFamilyInstance(face, point, ref, symbol). They count
                    // as "wall-attachable" too, so we only warn if NEITHER hosted nor
                    // face-based is loaded.
                    if (!anyHosted && !anyFaceBased)
                    {
                        var first = symbols.FirstOrDefault();
                        helpfulHints.Add(
                            $"Category '{cat}' has {symbols.Count} Family Type(s) loaded but none are wall-hostable " +
                            $"(none are OneLevelBasedHosted and none are WorkPlaneBased; e.g. '{first?.Family?.Name}' is " +
                            $"{first?.Family?.FamilyPlacementType}). Wall-anchored rules in this category will land but " +
                            $"won't attach — fixtures will float at the calculated XYZ. Use Tags > Change Host to " +
                            $"re-host one of these family instances after the run, or load a wall-hosted variant.");
                    }
                }

                // 2. Doors without FromRoom / ToRoom on the active level.
                var view = _doc.ActiveView;
                if (view is ViewPlan vp && vp.GenLevel != null)
                {
                    int doorsTotal = 0, doorsWithSpatial = 0;
                    foreach (var el in new FilteredElementCollector(_doc)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType())
                    {
                        if (!(el is FamilyInstance fi)) continue;
                        if (fi.LevelId != vp.GenLevel.Id) continue;
                        doorsTotal++;
                        Autodesk.Revit.DB.Architecture.Room from = null, to = null;
                        try { from = fi.FromRoom; } catch { }
                        try { to = fi.ToRoom; } catch { }
                        if (from != null || to != null) doorsWithSpatial++;
                    }
                    if (doorsTotal > 0 && doorsWithSpatial == 0)
                    {
                        blockers.Add($"• {doorsTotal} door(s) on the active level have no FromRoom or ToRoom set. Door-anchored rules will mis-target. Fix: select all rooms in the model, run \"Architecture > Recompute Areas / Volumes\" or reset the room boundaries so spatial relationships re-compute, then run again.");
                    }
                    else if (doorsTotal > 0 && doorsWithSpatial < doorsTotal / 2)
                    {
                        helpfulHints.Add($"~{doorsTotal - doorsWithSpatial} of {doorsTotal} doors have no FromRoom/ToRoom — those will be skipped by the spatial filter.");
                    }
                }

                // 3. The actual rule pack must include category-checklist entries.
                if (allowed.Count > 0)
                {
                    var rulesAllowed = rules.Where(r => allowed.Contains(r.CategoryFilter ?? "")).ToList();
                    if (rulesAllowed.Count == 0)
                    {
                        blockers.Add($"• None of the {rules.Count} active rules match any ticked category. Untick something or load a rule pack covering: {string.Join(", ", allowed)}.");
                    }
                }

                if (blockers.Count > 0)
                {
                    // Render the blockers INLINE in the Report panel (not a pop-up).
                    var rb = StingResultPanel.Create("Placement — prerequisites missing")
                        .SetSubtitle($"{blockers.Count} prerequisite(s) failed — run aborted.")
                        .AddSection("Blockers");
                    foreach (var bl in blockers) rb.Alert(bl);
                    rb.AddSection("Why")
                      .Text("The run is hard-failed when these are present so we don't produce silently-wrong placements.");
                    if (helpfulHints.Count > 0)
                    {
                        rb.AddSection("Also noted");
                        foreach (var h in helpfulHints) rb.Text(h);
                    }
                    Report("Run", rb);
                    Toast($"{blockers.Count} prerequisite(s) failed — see Report panel.");
                    StingLog.Warn($"PlacementCenter: prerequisites preflight failed — {blockers.Count} blocker(s). Run aborted.");
                    foreach (var b in blockers) StingLog.Warn("  " + b);
                    return;
                }
                if (helpfulHints.Count > 0)
                    foreach (var h in helpfulHints) StingLog.Info("PlacementCenter preflight hint: " + h);
            }
            catch (Exception preEx) { StingLog.Warn($"PlacementCenter preflight: {preEx.Message}"); }
            if (allowed.Count > 0)
            {
                int before = rules.Count;
                var filteredOutCats = rules
                    .Select(r => r.CategoryFilter ?? "")
                    .Where(c => !allowed.Contains(c))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();
                rules = rules.Where(r => allowed.Contains(r.CategoryFilter ?? "")).ToList();
                StingLog.Info($"PlacementCenter: category filter kept {rules.Count} / {before} rules. " +
                              $"Allowed: [{string.Join(", ", allowed.OrderBy(s => s))}]. " +
                              $"Excluded categories: [{string.Join(", ", filteredOutCats)}].");
                if (rules.Count == 0)
                {
                    TaskDialog.Show("STING — Placement Centre",
                        "Category checklist filtered every rule out. Tick more categories or clear the checklist.");
                    return;
                }
            }
            else
            {
                // Empty checklist — report explicitly so the user can't
                // misinterpret "everything placed" as "filter broken".
                StingLog.Info("PlacementCenter: category checklist is EMPTY → all rule categories will run. " +
                              "Tick boxes to restrict.");
            }

            var roomIds = PlacementCenterBridge.ResolveScope(_uiDoc, VM.RunOpts.Scope);
            if (roomIds.Count == 0)
            {
                // Render the guard INLINE in the Report panel (not a pop-up window).
                Report("Run", StingResultPanel.Create("Placement — no rooms in scope")
                    .SetSubtitle($"Scope '{VM.RunOpts.Scope}' resolved zero rooms or MEP spaces.")
                    .AddSection("Nothing to place")
                    .Alert($"Scope '{VM.RunOpts.Scope}' resolved zero rooms or MEP spaces.")
                    .Text("• Try scope = Project, or open a plan view that shows the rooms/spaces.")
                    .Text("• MEP model: place MEP Spaces (Analyze → Space) — the engine places into Spaces as well as Rooms.")
                    .Text("• Rooms that live only in a LINKED architecture model are not read yet — place Spaces in this model to drive placement."));
                Toast($"Scope '{VM.RunOpts.Scope}' has no rooms/spaces — see Report panel.");
                return;
            }

            // Phase 139.9 — pre-flight: warn the user about ticked categories
            // that have ZERO loaded FamilySymbols. Without this check the
            // engine prints "No FamilySymbol found for category 'X' — skipping
            // its rules" once per category and silently zeros that category's
            // placements, leaving the run with 0 placed for an unticked
            // reason. Mirrors the dock-panel PlaceFixturesCommand pre-flight.
            try
            {
                var categoriesInUse = new System.Collections.Generic.HashSet<string>(
                    rules.Select(r => r.CategoryFilter ?? "")
                         .Where(s => !string.IsNullOrEmpty(s)),
                    System.StringComparer.OrdinalIgnoreCase);
                var emptyCats = new System.Collections.Generic.List<string>();
                foreach (var cat in categoriesInUse)
                {
                    bool hasSymbol = false;
                    foreach (var el in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)))
                    {
                        if (el is FamilySymbol fs && fs.Category != null
                            && string.Equals(fs.Category.Name, cat, System.StringComparison.OrdinalIgnoreCase))
                        { hasSymbol = true; break; }
                    }
                    if (!hasSymbol) emptyCats.Add(cat);
                }
                if (emptyCats.Count > 0)
                {
                    var td2 = new TaskDialog("STING — Placement Centre · Categories without a placeable Type")
                    {
                        MainInstruction = $"{emptyCats.Count} categor{(emptyCats.Count == 1 ? "y has" : "ies have")} no Family Type loaded",
                        MainContent =
                            "These categories have no Family Type (FamilySymbol) loaded into the project:\n  " +
                            string.Join("\n  ", emptyCats.Take(15)) +
                            (emptyCats.Count > 15 ? $"\n  + {emptyCats.Count - 15} more" : "") +
                            "\n\nIn Revit a Family is the .rfa container; a Type (FamilySymbol) is one of the variants inside it. " +
                            "The placement engine creates instances of a Type, not a Family — a Family without any of its Types " +
                            "loaded into the project still drops every rule for its category.\n\n" +
                            "Insert > Load Family, expand the .rfa in the Project Browser and drag at least one Type into a view, " +
                            "or run Placement_AuditSetup for a full project setup check. Continue anyway?",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    if (td2.Show() != TaskDialogResult.Yes)
                    {
                        VM.Status = "Run cancelled — categories with no families loaded.";
                        UpdateStatus();
                        return;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter pre-flight family check: {ex.Message}"); }

            // Confirm with a summary so the run isn't silently destructive.
            string catLine = allowed.Count == 0
                ? "Categories: ALL (checklist empty)"
                : "Categories: " + string.Join(", ", allowed);
            var confirm = new TaskDialog("STING — Run Placement")
            {
                MainInstruction = $"Place fixtures in {roomIds.Count} room(s)?",
                MainContent = $"Rules: {rules.Count}\nScope: {VM.RunOpts.Scope}\n{catLine}\nValidators after: {VM.RunOpts.RunValidators}\n\nThe engine creates new FamilyInstance(s); use Undo or 'Undo last run' (Phase D) to revert.",
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
            // Push the live building-profile selection so the building-type /
            // standards gate + wet-zone / accessibility / coverage toggles take
            // effect WITHOUT requiring a Save first. Cleared in OnRunCompleted /
            // the error path below.
            try { SyncProfileToVm(); StingTools.Commands.Placement.PlaceFixturesOptions.SessionProfile = VM.Profile?.Clone(); }
            catch (Exception pex) { StingLog.Warn($"PlacementCenter: push session profile: {pex.Message}"); }

            DateTime startUtc = DateTime.UtcNow;
            // Show a modeless progress dialog so the user can see per-room
            // progress and abort. The placement engine commits per-room
            // ProcessRoomRule writes inside its single Transaction, so the
            // outer TransactionGroup keeps everything undoable as one step.
            var progress = StingProgressDialog.Show(
                "STING — Placement Centre · Run", roomIds.Count);
            // Phase 139.11 — coarse heartbeat so the user sees activity
            // during pre-flight (catalogue scan, two-phase shared-param
            // check, first-fix box placement) which can each take many
            // seconds before the first per-room progress increment fires.
            try { progress.SetStatus("Pre-flight — scanning loaded families…"); } catch { }
            // Background ticker so the dialog never looks frozen even when
            // a pre-flight step pauses the API thread for a stretch.
            var heartbeatCts = new System.Threading.CancellationTokenSource();
            System.Threading.Tasks.Task.Run(() =>
            {
                int n = 0;
                while (!heartbeatCts.IsCancellationRequested)
                {
                    System.Threading.Thread.Sleep(1500);
                    if (heartbeatCts.IsCancellationRequested) break;
                    n++;
                    string phase = StingTools.Core.Placement.FixturePlacementEngine.CurrentPhase ?? "";
                    string label = string.IsNullOrEmpty(phase)
                        ? $"Pre-flight in progress ({n * 1.5:F0}s)…"
                        : $"{phase} ({n * 1.5:F0}s)…";
                    try { progress.SetStatus(label); } catch { break; }
                }
            }, heartbeatCts.Token);
            // Phase 139.13 — non-blocking ExternalEvent dispatch.  The
            // previous PushFrame approach blocked the WPF thread waiting
            // for Revit to service the event, but Revit only services
            // ExternalEvents on its idle cycle and a nested WPF message
            // pump apparently doesn't trigger that idle reliably (user
            // saw "Pre-flight in progress (3776s)" with no engine
            // activity). The standard pattern is fire-and-forget: the
            // click handler returns immediately, the handler completes
            // asynchronously, and the post-run UI work is dispatched
            // back via Dispatcher.BeginInvoke.
            _runRequest = new PlacementRunRequest
            {
                Doc          = _doc,
                RoomIds      = roomIds,
                Rules        = rules,
                Progress     = progress,
                HeartbeatCts = heartbeatCts,
                StartUtc     = startUtc,
                PrevStamp    = prevStamp,
                PrevLearn    = prevLearn,
                DryRun       = (chkRunDryRun?.IsChecked == true),
            };
            try
            {
                EnsureRunEvent();
                try { if (btnRunPlacement != null) btnRunPlacement.IsEnabled = false; } catch { }
                VM.Status = "Run in progress — please wait…";
                UpdateStatus();
                _runEvent.Raise();
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnRunPlacement raise", ex);
                TaskDialog.Show("STING — Placement Centre", $"Could not start run: {ex.Message}");
                try { heartbeatCts.Cancel(); } catch { }
                try { progress?.Close(); } catch { }
                StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance = prevStamp;
                StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned   = prevLearn;
                StingTools.Commands.Placement.PlaceFixturesOptions.SessionProfile  = null;
                try { if (btnRunPlacement != null) btnRunPlacement.IsEnabled = true; } catch { }
                return;
            }
            // Click-handler returns here. Engine runs on API thread; the
            // handler will invoke OnRunCompleted on the WPF thread when
            // done.
            return;
        }

        // Real Run-Placement execution on the Revit API thread. The
        // PlacementRunHandler ExternalEvent (MergeRecoveryStubs) forwards here;
        // this runs the engine and marshals the result back to OnRunCompleted on
        // the WPF thread via Dispatcher.BeginInvoke.
        internal void ExecuteRun(UIApplication app)
        {
            var req = _runRequest;
            if (req == null) return;
            PlacementResult result = null;
            Exception err = null;
            try
            {
                var roomIds = (req.RoomIds != null && req.RoomIds.Count > 0)
                    ? new List<ElementId>(req.RoomIds) : null;
                var prog = req.Progress;
                // Engine calls this once per room with cumulative (done,total);
                // advance the modeless progress dialog and abort on cancel/Esc.
                Func<int, int, bool> onProgress = (done, total) =>
                {
                    try { prog?.Increment(); return prog?.IsCancelled ?? false; }
                    catch { return false; }
                };

                // Item 1 — EnsureSeeds pre-pass. Runs BEFORE the engine opens its
                // transaction so seed families build/load cleanly (no nested
                // transaction). Skipped for dry-run previews (nothing is
                // committed). For each ticked category with no loaded family,
                // the mapped STING seed is built+loaded so the run places a
                // swap-ready default instead of silently skipping. Failures are
                // non-fatal — the run proceeds and the rule surfaces the normal
                // "no symbol" skip.
                List<string> seedMsgs = null;
                if (!req.DryRun)
                {
                    try { prog?.SetStatus("Pre-flight — ensuring seed families…"); } catch { }
                    try
                    {
                        var seedRes = StingTools.Core.Placement.SeedEnsurer.EnsureSeedsForRules(req.Doc, req.Rules);
                        if (seedRes != null && (seedRes.SeedsBuiltOrLoaded > 0 || seedRes.Messages.Count > 0))
                        {
                            seedMsgs = new List<string>();
                            seedMsgs.Add($"EnsureSeeds: {seedRes.SeedsBuiltOrLoaded} seed family(ies) built/loaded; " +
                                         $"{seedRes.CategoriesAlreadyServed} category(ies) already had a family; " +
                                         $"{seedRes.CategoriesSeedless} seedless.");
                            seedMsgs.AddRange(seedRes.Messages);
                        }
                    }
                    catch (Exception sx) { StingLog.Warn($"PlacementCenter EnsureSeeds pre-pass: {sx.Message}"); }
                }

                result = FixturePlacementEngine.PlaceFixturesInScope(
                    req.Doc, roomIds, req.Rules, req.DryRun, onProgress);

                // Fold the seed-build report into the run warnings so the run
                // report lists what was built/loaded.
                if (result != null && seedMsgs != null && seedMsgs.Count > 0)
                    result.Warnings.InsertRange(0, seedMsgs);
            }
            catch (Exception ex) { err = ex; StingLog.Error("PlacementCenter.ExecuteRun", ex); }

            var r = result; var e2 = err;
            try { Dispatcher.BeginInvoke(new Action(() => OnRunCompleted(req, r, e2))); }
            catch (Exception ex) { StingLog.Error("PlacementCenter.ExecuteRun dispatch", ex); }
        }

        // Phase 139.13 — completion callback. Runs on the WPF thread
        // (Dispatcher.BeginInvoke from the IExternalEventHandler).
        private void OnRunCompleted(PlacementRunRequest req, PlacementResult result, Exception err)
        {
            try { req?.HeartbeatCts?.Cancel(); } catch { }
            try { req?.Progress?.Close(); } catch { }
            StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance = req?.PrevStamp ?? false;
            StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned   = req?.PrevLearn ?? false;
            StingTools.Commands.Placement.PlaceFixturesOptions.SessionProfile  = null;

            try { if (btnRunPlacement != null) btnRunPlacement.IsEnabled = true; } catch { }
            if (err != null)
            {
                StingLog.Error("PlacementCenter.OnRunCompleted err", err);
                TaskDialog.Show("STING — Placement Centre", $"Run failed: {err.Message}");
                VM.Status = $"Run failed: {err.Message}";
                UpdateStatus();
                return;
            }


            _lastRunUtc = req.StartUtc;
            _lastPlacedIds = result?.PlacedIds?.ToList() ?? new List<ElementId>();
            int placed  = _lastPlacedIds.Count;
            int skipped = result?.SkippedCount ?? 0;
            int warns   = result?.Warnings?.Count ?? 0;

            VM.Status = $"Placed {placed} · skipped {skipped} · warnings {warns}";
            UpdateStatus();

            try
            {
                var panel = StingResultPanel.Create("STING — Placement Centre · Run");
                panel.SetSubtitle($"{placed} placed · {skipped} skipped · {warns} warning(s) · build {StingTools.Core.Placement.FixturePlacementEngine.BuildStamp}");

                // Phase 139.20 — surface which categories were actually
                // allowed by the checklist, alongside the categories that
                // got placed. If "Fire Alarm Devices" appears in the
                // placed-by-category list when the user thought they
                // ticked only Lighting Devices + Lighting Fixtures, the
                // mismatch is visible here rather than buried in StingLog.
                var placedCats = (req.Rules ?? new List<PlacementRule>())
                    .Where(r => result?.CountsByRule != null && result.CountsByRule.ContainsKey(r.MergeKey))
                    .Select(r => r.CategoryFilter ?? "(none)")
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();
                var allowedCats = ReadCategoryChecklist();
                string allowedTxt = allowedCats.Count == 0
                    ? "(empty — every category allowed)"
                    : string.Join(", ", allowedCats.OrderBy(s => s));

                panel.AddSection("SUMMARY")
                    .Metric("Rooms scoped",         (req.RoomIds?.Count ?? 0).ToString())
                    .Metric("Rules considered",     (req.Rules?.Count ?? 0).ToString())
                    .Metric("Categories allowed",   allowedTxt)
                    .Metric("Categories placed",    placedCats.Count == 0 ? "(none)" : string.Join(", ", placedCats))
                    .Metric("Candidates evaluated", (result?.CandidatesEvaluated ?? 0).ToString())
                    .Metric("Placed",               placed.ToString())
                    .Metric("Skipped",              skipped.ToString());

                if (result?.CountsByRule != null && result.CountsByRule.Count > 0)
                {
                    panel.AddSection("PER-RULE COUNTS");
                    foreach (var kv in result.CountsByRule.OrderByDescending(k => k.Value).Take(20))
                        panel.Metric(kv.Key, kv.Value.ToString());
                }

                // A11 + A14 — per-rule placement diagnostics. Surfaces anchor
                // fallbacks (devices landed at the room centre), under-fill
                // (cap > candidates), and no-symbol skips so every algorithm
                // gap self-reports in the run report instead of silently
                // mis-placing.
                try
                {
                    var diags = (result?.Diagnostics?.Values ?? Enumerable.Empty<StingTools.Core.Placement.RuleDiagnostic>())
                        .Where(d => d != null && (d.AnchorMissRooms > 0 || d.UnderFilledRooms > 0 || d.SkippedNoSymbol > 0))
                        .OrderByDescending(d => d.AnchorMissRooms + d.UnderFilledRooms + d.SkippedNoSymbol)
                        .ToList();
                    if (diags.Count > 0)
                    {
                        panel.AddSection("PER-RULE DIAGNOSTICS (anchor / under-fill / no-symbol)");
                        foreach (var d in diags.Take(20))
                        {
                            var bits = new List<string>();
                            if (d.AnchorMissRooms > 0)
                                bits.Add($"anchor→centre in {d.AnchorMissRooms} room(s) ({d.FirstAnchorMiss})");
                            if (d.UnderFilledRooms > 0)
                                bits.Add($"under-filled {d.UnderFilledRooms} room(s), short {d.UnderFillShortfall} ({d.FirstUnderFill})");
                            if (d.SkippedNoSymbol > 0)
                                bits.Add($"no family symbol — {d.SkippedNoSymbol} skipped");
                            panel.Metric(d.MergeKey, string.Join(" · ", bits));
                        }
                    }
                }
                catch (Exception dEx) { StingLog.Warn($"PlacementCenter diagnostics section: {dEx.Message}"); }

                if (placed == 0)
                {
                    panel.AddSection("ZERO PLACED — common causes")
                        .Text("• Ticked category has no Family Type loaded. A Family (.rfa) is the container; the engine needs at least one Type loaded into the project (drag one from the .rfa in Project Browser into a view).")
                        .Text("• RoomFilter regex doesn't match the active rooms (check the rule's RoomFilter against the room name in Properties).")
                        .Text("• PlacementHostPreflight rejected every candidate (hosted family but no host wall/ceiling element nearby).")
                        .Text("• Run Placement_AuditSetup to confirm shared parameters bound + Types loaded.");
                }

                if (result?.Warnings != null && result.Warnings.Count > 0)
                {
                    panel.AddSection("WARNINGS");
                    foreach (var w in result.Warnings.Take(30)) panel.Text(w);
                    if (result.Warnings.Count > 30)
                        panel.Text($"(+{result.Warnings.Count - 30} more — see StingLog)");
                }
                Report("Run", panel);
            }
            catch (Exception pEx) { StingLog.Warn($"PlacementCenter post-run panel: {pEx.Message}"); }

            try { VM.SetHistory(HistoryBridge.ReadHistory(_doc)); }
            catch (Exception hEx) { StingLog.Warn($"PlacementCenter post-run history refresh: {hEx.Message}"); }

            if (VM.RunOpts.AutoHeatmap && placed > 0)
            {
                try { OnHeatmap_Click(this, null); }
                catch (Exception hmEx) { StingLog.Warn($"PlacementCenter auto-heatmap: {hmEx.Message}"); }
            }

            // RunValidators overwrites the inline Run report with the validation
            // findings (the more useful final view). Otherwise the Run summary
            // built above is already showing in the shared Report panel — no
            // pop-up needed.
            if (VM.RunOpts.RunValidators)
                ShowFindings(scopeToProvenance: true, headline: "Run + post-validation");
        }

        private void OnPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }

            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            if (rules.Count == 0)
            {
                Toast("No valid rules to preview."); 
                return;
            }
            var roomIds = PlacementCenterBridge.ResolveScope(_uiDoc, VM.RunOpts.Scope);
            if (roomIds.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre",
                    $"Scope '{VM.RunOpts.Scope}' resolved zero rooms — preview cancelled.");
                return;
            }
            // Preview honours the live building-profile selection too (dry-run,
            // so no commit). Set the session override; the next Run resets it.
            try { SyncProfileToVm(); StingTools.Commands.Placement.PlaceFixturesOptions.SessionProfile = VM.Profile?.Clone(); }
            catch (Exception pex) { StingLog.Warn($"PlacementCenter preview: push session profile: {pex.Message}"); }
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
            if (_doc == null) { Toast("No document open."); return; }
            ShowFindings(scopeToProvenance: false, headline: "Project-wide validation");
        }

        private void ShowFindings(bool scopeToProvenance, string headline, ISet<string> forceMask = null)
        {
            try
            {
                // Collect picked validators (or use the caller's forced mask, e.g.
                // "All Validators" runs the full set regardless of the checklist).
                ISet<string> mask;
                if (forceMask != null)
                {
                    mask = forceMask;
                }
                else
                {
                    var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (vClearance.IsChecked   == true) picked.Add("Clearance");
                    if (vMaintenance.IsChecked == true) picked.Add("Maintenance");
                    if (vConnectivity.IsChecked == true) picked.Add("Connectivity");
                    if (vFill.IsChecked        == true) picked.Add("Fill");
                    if (vSpec.IsChecked        == true) picked.Add("Spec");
                    if (vTermination.IsChecked == true) picked.Add("Termination");
                    if (vSlope.IsChecked       == true) picked.Add("Slope");
                    if (vSeparation.IsChecked  == true) picked.Add("Separation");
                    mask = picked;
                }
                var findings = PlacementCenterBridge.RunValidators(_doc, mask);
                if (scopeToProvenance && _lastRunUtc.HasValue)
                {
                    findings = PlacementCenterBridge.FilterToProvenance(_doc, findings, _lastRunUtc.Value);
                    headline += " · just-placed only";
                }

                int errs  = findings.Count(f => f.Severity == ValidationSeverity.Error);
                int warns = findings.Count(f => f.Severity == ValidationSeverity.Warning);
                int infos = findings.Count(f => f.Severity == ValidationSeverity.Info);

                // Phase 139.15 — surface the count of elements actually
                // checked. 0 / 0 / 0 / 0 against 153 placed used to read
                // as "nothing was checked"; it actually means "everything
                // checked is compliant". Show the validated element count
                // in the sub-title so the user can tell the difference.
                int validatedCount = scopeToProvenance ? (_lastPlacedIds?.Count ?? 0) : -1;
                string subtitle = headline + (validatedCount >= 0 ? $" · {validatedCount} element(s) checked" : "");

                var panel = StingResultPanel.Create("STING — Placement Centre · Validation")
                    .SetSubtitle(subtitle)
                    .AddSection("SUMMARY")
                    .Metric("Elements checked", validatedCount >= 0 ? validatedCount.ToString() : "(project-wide)")
                    .Metric("Total findings", findings.Count.ToString())
                    .Metric("Errors",   errs.ToString())
                    .Metric("Warnings", warns.ToString())
                    .Metric("Info",     infos.ToString());

                if (findings.Count == 0 && validatedCount > 0)
                {
                    panel.AddSection("RESULT")
                         .Text($"All {validatedCount} just-placed element(s) passed every active validator. No issues found.");
                }

                if (findings.Count > 0)
                {
                    panel.AddSection("FINDINGS (top 40) — click to select in model");
                    foreach (var f in findings.OrderByDescending(x => x.Severity).Take(40))
                    {
                        var fid = f.ElementId;
                        if (fid != null && fid != ElementId.InvalidElementId)
                            panel.Finding(f.ToString(), () => SelectInModel(fid));
                        else
                            panel.Text(f.ToString());
                    }
                }
                Report("Validation", panel);

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
            if (_doc == null) { Toast("No document open."); return; }
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
            if (_doc == null) { Toast("No document open."); return; }
            if (VM.Selected == null)
            {
                Toast("Pick a rule first."); 
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
            if (_doc == null) { Toast("No document open."); return; }
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

        private void OnLearnFromModel_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            try
            {
                // RunLearn only reads elements + writes a JSON file (no Revit
                // transaction), so it is safe on the modeless window's thread.
                // showDialog:false suppresses its own TaskDialog so the result
                // renders inline in the shared Report panel instead.
                int n = StingTools.Commands.Placement.LearnPlacementV4Command.RunLearn(_doc, out string summary, showDialog: false);
                int imported = 0;
                if (n > 0 && !string.IsNullOrEmpty(_doc.PathName))
                {
                    // Reload the just-written learned rules into the grid so they
                    // are reviewable and "Honour learned offsets" picks them up.
                    string dir = System.IO.Path.GetDirectoryName(_doc.PathName);
                    string learnedPath = System.IO.Path.Combine(dir ?? "", "STING_PLACEMENT_RULES.learned.json");
                    if (System.IO.File.Exists(learnedPath))
                    {
                        imported = VM.ImportFromFile(learnedPath);
                        VM.ApplyFilter();
                        VM.Status = $"Learned {n} rule(s); imported {imported} into the grid for review (Save Project to persist).";
                        UpdateStatus();
                    }
                }

                var panel = StingResultPanel.Create("STING — Learn from model")
                    .AddSection("RESULT")
                    .Text(string.IsNullOrEmpty(summary) ? $"Learned {n} rule(s)." : summary);
                if (imported > 0)
                    panel.Text($"Imported {imported} learned rule(s) into the grid for review (Save Project to persist).");
                Report("Learn", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnLearnFromModel", ex);
                TaskDialog.Show("STING — Placement Centre", $"Learn from model failed: {ex.Message}");
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
            if (_doc == null) { Toast("No document open."); return; }

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
            if (_doc == null) { Toast("No document open."); return; }
            var view = _doc.ActiveView;
            if (view == null) { TaskDialog.Show("STING — Placement Centre", "No active view."); return; }

            string presetName = $"PlacementCentre/{VM.RunOpts.Scope}/{DateTime.UtcNow:yyyyMMdd-HHmm}";
            try
            {
                using (var t = new Transaction(_doc, "STING — Save view preset"))
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

        // ── Shared inline Report panel ────────────────────────────────
        // Every reporting button renders into the right-hand panel via
        // Report(...) instead of opening an external window. _lastReport
        // keeps the builder so the pop-out button can still show the full
        // windowed version on demand.
        private StingResultPanel.Builder _lastReport;
        private readonly List<(string Title, StingResultPanel.Builder Builder)> _recentReports = new();
        private bool _suppressRecentSel;

        private void Report(string title, StingResultPanel.Builder b)
        {
            RenderReport(title, b);
            try
            {
                _recentReports.Insert(0, (title ?? "Report", b));
                if (_recentReports.Count > 12) _recentReports.RemoveRange(12, _recentReports.Count - 12);
                RefreshRecentCombo(selectIndex0: true);
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.Report recent: {ex.Message}"); }
        }

        private void RenderReport(string title, StingResultPanel.Builder b)
        {
            try
            {
                _lastReport = b;
                if (txtReportTitle != null) txtReportTitle.Text = title ?? "Report";
                if (reportHost != null)
                    reportHost.Child = StingResultPanel.BuildInlineContent(b, double.PositiveInfinity);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementCenter.RenderReport: {ex.Message}");
                try { b?.Show(); } catch { }   // last-resort fallback so the result is still visible
            }
        }

        private void RefreshRecentCombo(bool selectIndex0)
        {
            if (cmbRecentReports == null) return;
            _suppressRecentSel = true;
            try
            {
                cmbRecentReports.Items.Clear();
                foreach (var r in _recentReports) cmbRecentReports.Items.Add(r.Title);
                if (selectIndex0 && cmbRecentReports.Items.Count > 0) cmbRecentReports.SelectedIndex = 0;
            }
            finally { _suppressRecentSel = false; }
        }

        private void OnRecentReport_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRecentSel) return;
            int i = cmbRecentReports?.SelectedIndex ?? -1;
            if (i < 0 || i >= _recentReports.Count) return;
            var r = _recentReports[i];
            RenderReport(r.Title, r.Builder);   // re-show without re-pushing onto the recent list
        }

        private void OnReportClear_Click(object sender, RoutedEventArgs e)
        {
            _lastReport = null;
            if (txtReportTitle != null) txtReportTitle.Text = "Report";
            if (reportHost != null)
                reportHost.Child = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                    Text = "Run a placement, validate, audit, diagnose, learn, or import/export — results appear here instead of a pop-up window."
                };
        }

        private void OnReportPopOut_Click(object sender, RoutedEventArgs e)
        {
            try { _lastReport?.Show(); }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.ReportPopOut: {ex.Message}"); }
        }

        // ── Transient status toast (replaces one-line guard TaskDialogs) ──
        private System.Windows.Threading.DispatcherTimer _toastTimer;

        private void Toast(string msg)
        {
            try
            {
                if (txtToast == null || bdrToast == null) { VM.Status = msg; UpdateStatus(); return; }
                txtToast.Text = msg ?? "";
                bdrToast.Visibility = System.Windows.Visibility.Visible;
                if (_toastTimer == null)
                {
                    _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                    _toastTimer.Tick += (s, e) =>
                    {
                        _toastTimer.Stop();
                        if (bdrToast != null) bdrToast.Visibility = System.Windows.Visibility.Collapsed;
                    };
                }
                _toastTimer.Stop();
                _toastTimer.Start();
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.Toast: {ex.Message}"); }
        }

        // ── Generic API-thread action (real ExternalEvent) ──────────────
        // The placement run handler in MergeRecoveryStubs is a no-op stub, so
        // model-modifying / interactive actions (e.g. Wall Chase) get their own
        // real event. The work returns a result Builder which is reported inline.
        private ExternalEvent _actionEvent;
        private PlacementActionHandler _actionHandler;
        private Func<UIApplication, StingResultPanel.Builder> _pendingAction;
        private string _pendingActionTitle;

        private void RunInlineAction(string title, Func<UIApplication, StingResultPanel.Builder> work)
        {
            if (work == null) return;
            try
            {
                // _actionEvent is created in the ctor (the only valid API context).
                // Creating it here — off the API thread on a button click — throws
                // "Attempting to create an ExternalEvent outside of a standard API
                // execution", so if it's null, fail clearly instead.
                if (_actionEvent == null)
                {
                    Toast($"{title} unavailable — close and reopen the Placement Centre.");
                    return;
                }
                _pendingActionTitle = title;
                _pendingAction = work;
                Toast($"{title}…");
                _actionEvent.Raise();
            }
            catch (Exception ex)
            {
                StingLog.Error($"PlacementCenter.RunInlineAction {title}", ex);
                Toast($"{title} could not start: {ex.Message}");
            }
        }

        private sealed class PlacementActionHandler : IExternalEventHandler
        {
            private readonly StingPlacementCenter _o;
            public PlacementActionHandler(StingPlacementCenter o) { _o = o; }
            public string GetName() => "STING Placement Centre Action";
            public void Execute(UIApplication app)
            {
                var work = _o._pendingAction;
                var title = _o._pendingActionTitle;
                _o._pendingAction = null;
                if (work == null) return;
                StingResultPanel.Builder builder;
                try { builder = work(app); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                { builder = StingResultPanel.Create(title).AddSection("RESULT").Text("Cancelled."); }
                catch (Exception ex)
                {
                    StingLog.Error($"PlacementCenter action '{title}'", ex);
                    builder = StingResultPanel.Create(title).AddSection("ERROR").Text(ex.Message);
                }
                var b = builder;
                try { _o.Dispatcher.BeginInvoke(new Action(() => { if (b != null) _o.Report(title, b); })); } catch { }
            }
        }

        /// <summary>
        /// Select + zoom an element in the model from a clicked report finding.
        /// Selection / ShowElements need no transaction and are safe from this
        /// modeless window (same pattern as the History double-click select).
        /// </summary>
        private void SelectInModel(ElementId id)
        {
            if (_uiDoc == null || id == null || id == ElementId.InvalidElementId) return;
            try
            {
                _uiDoc.Selection.SetElementIds(new List<ElementId> { id });
                try { _uiDoc.ShowElements(id); } catch { }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.SelectInModel: {ex.Message}"); }
        }

        private static readonly System.Text.RegularExpressions.Regex _idRx =
            new System.Text.RegularExpressions.Regex(@"\b\d{5,}\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Render report text line-by-line; a line containing a 5+ digit token
        /// (an ElementId in a real model — counts / dimensions are shorter)
        /// becomes a clickable Finding that selects it. Non-element numbers
        /// resolve to nothing on click and are harmless.
        /// </summary>
        private void AddTextWithIds(StingResultPanel.Builder panel, string text)
        {
            if (panel == null || string.IsNullOrEmpty(text)) return;
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                var m = _idRx.Match(line);
                if (m.Success && long.TryParse(m.Value, out long id))
                    panel.Finding(line, id);
                else
                    panel.Text(line);
            }
        }

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

        // Phase 139 I1 — SourcePack chip filter
        private void OnSourcePack_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSourcePack?.SelectedItem is string s) VM.SelectedSourcePack = s;
        }

        // Phase 139 E3 — Excel round-trip buttons
        private void OnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rules = VM.Rules.Select(r => r.Model).ToList();
                if (rules.Count == 0)
                {
                    System.Windows.MessageBox.Show("No rules to export.", "STING Placement");
                    return;
                }
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title    = "Export STING Placement Rules to Excel",
                    Filter   = "Excel workbook (*.xlsx)|*.xlsx",
                    FileName = $"STING_PLACEMENT_RULES_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                };
                if (sfd.ShowDialog(this) != true) return;
                StingTools.Core.Placement.Excel.PlacementRulesExcelExporter.Export(rules, sfd.FileName);
                VM.Status = $"Exported {rules.Count} rule(s) → {System.IO.Path.GetFileName(sfd.FileName)}";
            }
            catch (Exception ex)
            {
                StingLog.Error("OnExportExcel_Click", ex);
                VM.Status = $"Excel export failed: {ex.Message}";
            }
        }

        private void OnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Import STING Placement Rules from Excel",
                    Filter = "Excel workbook (*.xlsx)|*.xlsx",
                };
                if (ofd.ShowDialog(this) != true) return;
                var (rules, errors) = StingTools.Core.Placement.Excel.PlacementRulesExcelImporter.Import(ofd.FileName);
                if (errors.Count > 0)
                {
                    var preview = string.Join("\n", errors.Take(20));
                    var res = System.Windows.MessageBox.Show(
                        $"{errors.Count} issue(s) reading workbook.\n\n{preview}\n\nAppend valid rules anyway?",
                        "STING Placement — Excel import",
                        System.Windows.MessageBoxButton.YesNo);
                    if (res != System.Windows.MessageBoxResult.Yes) return;
                }
                int added = 0;
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    VM.Rules.Add(new PlacementRuleViewModel(r) { IsDirty = true });
                    added++;
                }
                VM.RebuildCategories();
                VM.ApplyFilter();   // re-evaluate the live grid filter so imported rows appear under any active pack/search filter
                VM.Status = $"Imported {added} rule(s) from Excel (Save Project to persist).";
            }
            catch (Exception ex)
            {
                StingLog.Error("OnImportExcel_Click", ex);
                VM.Status = $"Excel import failed: {ex.Message}";
            }
        }

        // Phase 139 I3 — building profile load/save
        private void OnLoadProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = _uiApp?.ActiveUIDocument?.Document;
                if (doc == null || string.IsNullOrEmpty(doc.PathName))
                {
                    System.Windows.MessageBox.Show("Save the project before loading a building profile.", "STING Placement");
                    return;
                }
                VM.LoadProfile(doc.PathName);
                SyncProfileFromVm();
            }
            catch (Exception ex) { StingLog.Error("OnLoadProfile_Click", ex); }
        }

        private void OnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = _uiApp?.ActiveUIDocument?.Document;
                if (doc == null || string.IsNullOrEmpty(doc.PathName))
                {
                    System.Windows.MessageBox.Show("Save the project before saving the building profile.", "STING Placement");
                    return;
                }
                SyncProfileToVm();
                VM.SaveProfile(doc.PathName);
            }
            catch (Exception ex) { StingLog.Error("OnSaveProfile_Click", ex); }
        }

        private void SyncProfileFromVm()
        {
            if (cmbProfileBuildingType != null) cmbProfileBuildingType.SelectedItem = VM.Profile.BuildingType;
            if (txtProfileStandards    != null) txtProfileStandards.Text = VM.Profile.ActiveStandards == null ? "" : string.Join(",", VM.Profile.ActiveStandards);
            if (chkEnableWetZone       != null) chkEnableWetZone.IsChecked = VM.Profile.EnableWetZoneChecks;
            if (chkEnableAccessibility != null) chkEnableAccessibility.IsChecked = VM.Profile.EnableAccessibilityChecks;
            if (chkEnableCoverage      != null) chkEnableCoverage.IsChecked = VM.Profile.EnableCoverageGuarantee;
        }

        private void SyncProfileToVm()
        {
            var p = VM.Profile;
            if (cmbProfileBuildingType?.SelectedItem is string bt) p.BuildingType = bt;
            if (txtProfileStandards != null)
                p.ActiveStandards = (txtProfileStandards.Text ?? "")
                    .Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).ToArray();
            if (chkEnableWetZone?.IsChecked       == true) p.EnableWetZoneChecks       = true; else p.EnableWetZoneChecks = false;
            if (chkEnableAccessibility?.IsChecked == true) p.EnableAccessibilityChecks = true; else p.EnableAccessibilityChecks = false;
            if (chkEnableCoverage?.IsChecked      == true) p.EnableCoverageGuarantee   = true; else p.EnableCoverageGuarantee = false;
            VM.Profile = p;
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

                // Phase 139 — new card field sync.
                if (txtCoverageRadius        != null) txtCoverageRadius.Text        = s.CoverageRadiusMm.ToString("0.##",       CultureInfo.InvariantCulture);
                if (txtMaxSpacing            != null) txtMaxSpacing.Text            = s.MaxSpacingMm.ToString("0.##",           CultureInfo.InvariantCulture);
                if (txtWallClearance         != null) txtWallClearance.Text         = s.WallClearanceMm.ToString("0.##",        CultureInfo.InvariantCulture);
                if (txtObstructionClearance  != null) txtObstructionClearance.Text  = s.ObstructionClearanceMm.ToString("0.##", CultureInfo.InvariantCulture);
                if (txtDoorClearance         != null) txtDoorClearance.Text         = s.DoorClearanceMm.ToString("0.##",        CultureInfo.InvariantCulture);
                if (txtWindowClearance       != null) txtWindowClearance.Text       = s.WindowClearanceMm.ToString("0.##",      CultureInfo.InvariantCulture);
                if (chkGuaranteeCoverage     != null) chkGuaranteeCoverage.IsChecked = s.GuaranteeCoverage;

                if (cmbRoutingMode           != null) cmbRoutingMode.SelectedItem   = string.IsNullOrEmpty(s.RoutingMode) ? "NONE" : s.RoutingMode;
                if (cmbRouteFace             != null) cmbRouteFace.SelectedItem     = string.IsNullOrEmpty(s.RouteFace)   ? "INTERIOR" : s.RouteFace;
                if (txtRouteOffset           != null) txtRouteOffset.Text           = s.RouteOffsetMm.ToString("0.##",        CultureInfo.InvariantCulture);
                if (txtRouteMinBendRadius    != null) txtRouteMinBendRadius.Text    = s.RouteMinBendRadiusMm.ToString("0.##", CultureInfo.InvariantCulture);
                if (cmbRouteSegmentCategory  != null) cmbRouteSegmentCategory.SelectedItem = s.RouteSegmentCategory ?? "";

                if (txtSillHeight            != null) txtSillHeight.Text            = s.SillHeightMm.ToString("0.##",        CultureInfo.InvariantCulture);
                if (txtHeadHeight            != null) txtHeadHeight.Text            = s.HeadHeightMm.ToString("0.##",        CultureInfo.InvariantCulture);
                if (txtCillToFloor           != null) txtCillToFloor.Text           = s.CillToFloorMm.ToString("0.##",       CultureInfo.InvariantCulture);
                if (chkToughenedGlazing      != null) chkToughenedGlazing.IsChecked = s.ToughenedGlazingRequired;
                if (cmbGlazingSpec           != null) cmbGlazingSpec.SelectedItem   = s.GlazingSpec ?? "";

                if (cmbBuildingType          != null) cmbBuildingType.SelectedItem  = s.BuildingType ?? "";
                if (txtIpRatingMin           != null) txtIpRatingMin.Text           = s.IpRatingMin ?? "";
                if (txtStandardsCsv          != null) txtStandardsCsv.Text          = s.ApplicableStandardsCsv ?? "";
                if (cmbWetZone               != null) cmbWetZone.SelectedItem       = string.IsNullOrEmpty(s.WetZoneExclusion) ? "NONE" : s.WetZoneExclusion;
                if (chkAccessibilityCheck    != null) chkAccessibilityCheck.IsChecked = s.AccessibilityCheck;
                if (cmbHeightStandard        != null) cmbHeightStandard.SelectedItem = s.HeightStandard ?? "";

                if (chkRequiresCOBieFields   != null) chkRequiresCOBieFields.IsChecked = s.RequiresCOBieFields;
                if (chkRequiresIfcMapping    != null) chkRequiresIfcMapping.IsChecked  = s.RequiresIfcMapping;
                if (cmbMaintenanceClearance  != null) cmbMaintenanceClearance.SelectedItem = s.MaintenanceClearance ?? "";
                if (txtPostAuditTag          != null) txtPostAuditTag.Text          = s.PostAuditTag ?? "";

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

        // Phase 139.8 — checklist plumbing.
        private (System.Windows.Controls.CheckBox cb, string cat)[] CategoryChecklist()
            => new (System.Windows.Controls.CheckBox, string)[]
            {
                (cbCatElec,   "Electrical Fixtures"),
                (cbCatLtgDev, "Lighting Devices"),
                (cbCatLtgFix, "Lighting Fixtures"),
                (cbCatComm,   "Communication Devices"),
                (cbCatData,   "Data Devices"),
                (cbCatSec,    "Security Devices"),
                (cbCatFire,   "Fire Alarm Devices"),
                (cbCatPlm,    "Plumbing Fixtures"),
                (cbCatHvac,   "Air Terminals"),
                (cbCatSpr,    "Sprinklers"),
                (cbCatMech,   "Mechanical Equipment"),
                (cbCatCond,   "Conduits"),
                (cbCatJBox,   "Junction Boxes"),
                (cbCatPipe,   "Pipes"),
                (cbCatTray,   "Cable Trays"),
                (cbCatSpec,   "Specialty Equipment"),
                (cbCatFurn,   "Furniture"),
                (cbCatNurse,  "Nurse Call Devices"),
            };

        private System.Collections.Generic.HashSet<string> ReadCategoryChecklist()
        {
            var s = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            // Phase 139.20 — log every checkbox state + null-state. If
            // many cb fields are null, the XAML auto-generated bindings
            // didn't compile (stale build). If all fields exist but the
            // user thinks they ticked one and we report unchecked, the
            // problem is UI confusion not code. Either way the user can
            // read the StingLog and we know which root cause to chase.
            int totalCb = 0, nullCb = 0, checkedCb = 0;
            var checkedNames = new System.Collections.Generic.List<string>();
            var nullNames    = new System.Collections.Generic.List<string>();
            foreach (var (cb, cat) in CategoryChecklist())
            {
                totalCb++;
                if (cb == null) { nullCb++; nullNames.Add(cat); continue; }
                if (cb.IsChecked == true) { checkedCb++; checkedNames.Add(cat); s.Add(cat); }
            }
            StingLog.Info($"PlacementCenter: category checklist read — {totalCb} controls, " +
                          $"{nullCb} NULL ({(nullNames.Count > 0 ? string.Join(", ", nullNames) : "")}), " +
                          $"{checkedCb} ticked ({(checkedNames.Count > 0 ? string.Join(", ", checkedNames) : "<none>")}).");
            if (nullCb > 0)
                StingLog.Warn($"PlacementCenter: {nullCb} of {totalCb} category checkboxes are NULL — " +
                              "XAML auto-generated bindings did not compile. Rebuild the plug-in.");
            return s;
        }

        private void SetAllCategoryChecks(bool on)
        {
            foreach (var (cb, _) in CategoryChecklist())
                if (cb != null) cb.IsChecked = on;
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
            // Fires during InitializeComponent (XAML sets a radio's IsChecked)
            // BEFORE the ctor assigns VM — guard against the resulting NRE.
            if (VM?.RunOpts == null) return;
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
                var progress = StingProgressDialog.Show("Placing fixtures…", roomIds.Count);
                Func<int, int, bool> progressHook = (processed, total) =>
                {
                    progress.Increment($"Room {processed} of {total}");
                    return progress.IsCancelled;
                };
                List<ElementId> placed;
                using (var tg = new TransactionGroup(_doc, "STING Place Fixtures (Run & Routing)"))
                {
                    tg.Start();
                    var result = FixturePlacementEngine.PlaceFixturesInScope(
                        _doc, roomIds, rules, dry, progressHook);
                    placed = result?.PlacedIds ?? new List<ElementId>();
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
        {
            if (_doc == null) { Toast("No document open."); return; }
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Clearance", "Maintenance", "Connectivity", "Fill", "Spec", "Termination", "Slope", "Separation" };
            ShowFindings(false, "All validators", all);
        }

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
        {
            if (_doc == null) { Toast("No document open."); return; }
            try
            {
                var findings = StingTools.Core.Validation.PenetrationCoverageValidator.Validate(_doc);
                int errors   = findings.Count(f => f.Severity == ValidationSeverity.Error);
                int warnings = findings.Count(f => f.Severity == ValidationSeverity.Warning);
                var panel = StingResultPanel.Create("STING — Penetration Coverage Audit")
                    .SetSubtitle("Firestop register integrity + structural review (beams)")
                    .AddSection("SUMMARY")
                    .Metric("Errors", errors.ToString())
                    .Metric("Warnings", warnings.ToString())
                    .Metric("Total findings", findings.Count.ToString());
                if (findings.Count > 0)
                {
                    panel.AddSection("FINDINGS (top 50) — click to select in model");
                    foreach (var f in findings.OrderByDescending(x => x.Severity).Take(50))
                    {
                        var fid = f.ElementId;
                        if (fid != null && fid != ElementId.InvalidElementId)
                            panel.Finding(f.ToString(), () => SelectInModel(fid));
                        else
                            panel.Text(f.ToString());
                    }
                    if (findings.Count > 50) panel.Text($"(+{findings.Count - 50} more — see StingLog)");
                }
                Report("Penetration Coverage", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnPenetrationCoverage", ex);
                TaskDialog.Show("STING — Placement Centre", $"Penetration coverage failed: {ex.Message}");
            }
        }

        private void OnAutoPopulateCatalogue_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            try
            {
                var (created, updated, contributing) =
                    StingTools.Core.Placement.ManufacturerCatalogueRegistry.AutoPopulateFromFamilies(_doc);
                var panel = StingResultPanel.Create("STING — Manufacturer Catalogue")
                    .AddSection("SUMMARY")
                    .Metric("New entries", created.ToString())
                    .Metric("Updated entries", updated.ToString());
                if (contributing != null && contributing.Count > 0)
                {
                    panel.AddSection("CONTRIBUTING FAMILIES");
                    foreach (var c in contributing.Take(40)) panel.Text(c);
                    if (contributing.Count > 40) panel.Text($"(+{contributing.Count - 40} more)");
                }
                else
                {
                    panel.AddSection("RESULT")
                         .Text("No families carrying MK_* shared parameters were found in this project.");
                }
                Report("Catalogue", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnAutoPopulateCatalogue", ex);
                TaskDialog.Show("STING — Placement Centre", $"Auto-populate failed: {ex.Message}");
            }
        }

        private void OnNoggin_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            try
            {
                string text = StingTools.Commands.Placement.NogginRequirementExportCommand
                    .BuildReportText(_doc, out string csvPath, out int count);
                var panel = StingResultPanel.Create("STING — Noggin Requirements")
                    .SetSubtitle(string.IsNullOrEmpty(csvPath) ? "" : $"CSV: {csvPath}")
                    .AddSection("RESULT")
                    .Metric("Noggin requirements", count.ToString())
                    .Text(text);
                Report("Noggin Requirements", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnNoggin", ex);
                TaskDialog.Show("STING — Placement Centre", $"Noggin export failed: {ex.Message}");
            }
        }

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

        private void OnWallChase_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            // Interactive + model-modifying: runs on the API thread via the real
            // action event; the mid-flow Yes/No confirm stays, the final result
            // renders inline in the Report panel.
            RunInlineAction("Wall Chase",
                app => StingTools.Commands.Placement.RunWallChaseCommand.RunInteractive(app));
        }

        private void OnExportRulesExcel_Click(object sender, RoutedEventArgs e)
        {
            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            if (rules == null || rules.Count == 0) { TaskDialog.Show("STING — Placement Centre", "No rules to export."); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "STING — Export placement rules to Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = "STING_PLACEMENT_RULES.xlsx",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                StingTools.Core.Placement.Excel.PlacementRulesExcelExporter.Export(rules, dlg.FileName);
                var panel = StingResultPanel.Create("STING — Export Rules (Excel)")
                    .SetSubtitle(dlg.FileName)
                    .AddSection("RESULT")
                    .Metric("Rules exported", rules.Count.ToString())
                    .Text($"Saved to {dlg.FileName}");
                Report("Export Rules (Excel)", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnExportRulesExcel", ex);
                TaskDialog.Show("STING — Placement Centre", $"Excel export failed: {ex.Message}");
            }
        }

        private void OnImportRulesExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "STING — Import placement rules from Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var (rules, errors) = StingTools.Core.Placement.Excel.PlacementRulesExcelImporter.Import(dlg.FileName);
                int imported = 0;
                // Write the project overlay beside the .rvt then reload the grid so
                // the import is live (closes the round-trip refresh gap).
                if (rules != null && rules.Count > 0 && !string.IsNullOrEmpty(_doc?.PathName))
                {
                    string dir = System.IO.Path.GetDirectoryName(_doc.PathName);
                    string overridePath = System.IO.Path.Combine(dir ?? "", "STING_PLACEMENT_RULES.project.json");
                    var set = new PlacementRuleSet { Version = "v4", Rules = rules };
                    System.IO.File.WriteAllText(overridePath,
                        Newtonsoft.Json.JsonConvert.SerializeObject(set, Newtonsoft.Json.Formatting.Indented));
                    imported = VM.ImportFromFile(overridePath);
                    VM.ApplyFilter();
                    UpdateStatus();
                }
                var panel = StingResultPanel.Create("STING — Import Rules (Excel)")
                    .SetSubtitle(dlg.FileName)
                    .AddSection("RESULT")
                    .Metric("Rules in workbook", (rules?.Count ?? 0).ToString())
                    .Metric("Imported into grid", imported.ToString())
                    .Metric("Warnings", (errors?.Count ?? 0).ToString());
                if (errors != null && errors.Count > 0)
                {
                    panel.AddSection("WARNINGS (top 20)");
                    foreach (var w in errors.Take(20)) panel.Text(w);
                }
                if (string.IsNullOrEmpty(_doc?.PathName))
                    panel.AddSection("NOTE").Text("Project not saved on disk — rules were read but not written to a project overlay. Save the .rvt then re-import to persist.");
                Report("Import Rules (Excel)", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnImportRulesExcel", ex);
                TaskDialog.Show("STING — Placement Centre", $"Excel import failed: {ex.Message}");
            }
        }

        private void OnDiagnose_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            try
            {
                var (text, path) = StingTools.Commands.Placement.PlacementDiagnoseCommand.BuildReportText(_doc);
                var panel = StingResultPanel.Create("STING — Placement Diagnose");
                if (!string.IsNullOrEmpty(path)) panel.SetSubtitle($"Full report: {path}  ·  click a line with an element id to select it");
                panel.AddSection("DIAGNOSTIC");
                AddTextWithIds(panel, text);
                Report("Diagnose", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnDiagnose", ex);
                TaskDialog.Show("STING — Placement Centre", $"Diagnose failed: {ex.Message}");
            }
        }

        private void OnRebuildSeeds_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            RunInlineAction("Rebuild Seeds", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                // Force-regenerate the mapped seed families from JSON (latest
                // geometry + variants) and reload them into the project so placed
                // instances pick up the new definitions. Runs outside any
                // transaction (CreateAllFromFile opens its own).
                var res = StingTools.Core.Placement.SeedEnsurer.RebuildAllForRules(doc, rules);
                var panel = StingResultPanel.Create("STING — Rebuild Seeds")
                    .SetSubtitle("Regenerated seed families from JSON and reloaded them into the project.")
                    .AddSection("RESULT")
                    .Metric("Seeds rebuilt / reloaded", res.SeedsBuiltOrLoaded.ToString())
                    .Metric("Seedless categories", res.CategoriesSeedless.ToString());
                foreach (var m in res.Messages.Take(40)) panel.Text(m);
                if (res.SeedsBuiltOrLoaded == 0)
                    panel.Text("No seeds rebuilt — check that the rule categories map to a seed and the seed specs (Data/Seeds/*.json) ship with the build.");
                else
                    panel.Text("Done. Run Placement to use the refreshed seeds.");
                return panel;
            });
        }

        // ── Library tab (fixture-lifecycle hub) ──────────────────────────────
        // Every handler DISPATCHES an existing STING command — no logic is
        // duplicated here. Read-only commands with an engine report method render
        // inline; interactive/model-modifying commands own their TaskDialog/wizard
        // and run on the API thread via RunExternalCommand<T>.

        /// <summary>Generic dispatcher: instantiate an existing IExternalCommand and run
        /// its Execute on the Revit API thread (via the Centre's action event), mirroring
        /// StingCommandHandler.RunCommand&lt;T&gt;. The command owns its own UI; this reports a
        /// one-line outcome inline. No command body is copied.</summary>
        private void RunExternalCommand<T>(string title) where T : Autodesk.Revit.UI.IExternalCommand, new()
        {
            RunInlineAction(title, app =>
            {
                // Commands accept a null ExternalCommandData and fall back to
                // StingCommandHandler.CurrentApp — set it so the live API-thread app is used.
                try { StingTools.UI.StingCommandHandler.SetCurrentApp(app); } catch { }
                string message = "";
                var elSet = new Autodesk.Revit.DB.ElementSet();
                Autodesk.Revit.UI.Result result;
                try { result = new T().Execute(null, ref message, elSet); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                { result = Autodesk.Revit.UI.Result.Cancelled; }

                var panel = StingResultPanel.Create($"STING — {title}")
                    .AddSection("RESULT")
                    .Metric("Command", typeof(T).Name)
                    .Metric("Outcome", result.ToString());
                if (!string.IsNullOrWhiteSpace(message)) panel.Text(message);
                panel.Text(result == Autodesk.Revit.UI.Result.Succeeded
                    ? "Command ran. Any preview/confirmation it showed is its own dialog; full results render in the command's own panel."
                    : result == Autodesk.Revit.UI.Result.Cancelled ? "Cancelled."
                    : "Command reported failure — check the STING log.");
                return panel;
            });
        }

        private void OnLibInspect_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.InspectSymbolLibraryCommand>("Inspect Library");

        private void OnLibCoverage_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            // Read-only: call the engine's report generator directly so it renders inline
            // in the shared Report panel (no TaskDialog detour).
            RunInlineAction("Coverage Audit", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                string report = StingTools.Core.Symbols.SymbolCoverageAuditor.GenerateCoverageReport(doc);
                return StingResultPanel.Create("STING — Symbol Coverage Audit")
                    .SetSubtitle("Which placement categories have a resolvable symbol vs. gaps (read-only).")
                    .AddSection("COVERAGE")
                    .Text(string.IsNullOrWhiteSpace(report) ? "No coverage data." : report);
            });
        }

        private void OnLibHealOrphans_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.HealSymbolOrphansCommand>("Heal Orphans");

        private void OnLibFixDrift_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.FixSymbolDriftCommand>("Fix Drift");

        private void OnLibSwap_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.SwapToManufacturerCommand>("Swap to Manufacturer");

        private void OnLibAugment_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.AugmentProjectFamiliesCommand>("Augment Families");

        private void OnLibCreateLighting_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.CreateLightingSymbolsCommand>("Create Lighting Symbols");

        private void OnLibCreateFP_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.CreateFPSymbolsCommand>("Create Fire Protection Symbols");

        private void OnLibCreateSLD_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Commands.Symbols.CreateSLDSymbolsCommand>("Create SLD Symbols");

        private void OnLibDwgWizard_Click(object sender, RoutedEventArgs e)
            => RunExternalCommand<StingTools.Model.MepCadWizardCommand>("DWG-MEP Import Wizard");

        /// <summary>The DWG→seed→swap bridge: maps DWG MEP fixture symbols to STING seed
        /// instances at the symbol locations (swap-ready). Calls the same engine the
        /// Placement_DwgToSeedFixtures command does; reports counts inline.</summary>
        private void OnLibDwgToSeeds_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            RunInlineAction("DWG → STING fixtures", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                // Target the SAME import the Map-DWG-Layers dialog uses (selected > picked).
                var import = ResolveTargetImport(app, doc, out _);
                if (import == null)
                    return StingResultPanel.Create("STING — DWG → STING fixtures")
                        .AddSection("RESULT").Text("No DWG/DXF import found. Link or import a DWG first.");
                var res = StingTools.Core.Placement.DwgFixtureBridge.PlaceFromImport(doc, import, dryRun: false);
                return StingTools.Commands.Placement.DwgToSeedFixturesCommand.BuildPanel(res);
            });
        }

        /// <summary>Pick the DWG/DXF import to act on, so the Map-DWG-Layers dialog and the
        /// actual Place run always target the SAME import. One import -> use it. Multiple ->
        /// prefer the one selected in Revit; else show a picker sorted newest-first (by
        /// ElementId). Returns null when none exist. Runs on the API thread (called from
        /// inside RunInlineAction), so Selection and a modal picker are both valid here.</summary>
        private Autodesk.Revit.DB.ImportInstance ResolveTargetImport(
            Autodesk.Revit.UI.UIApplication app, Autodesk.Revit.DB.Document doc, out string note)
        {
            note = "";
            List<Autodesk.Revit.DB.ImportInstance> imports;
            try { imports = StingTools.Model.CADToModelEngine.FindImportInstances(doc) ?? new List<Autodesk.Revit.DB.ImportInstance>(); }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.ResolveTargetImport.find: {ex.Message}"); return null; }

            if (imports.Count == 0) return null;
            if (imports.Count == 1) { note = "Using the only DWG import."; return imports[0]; }

            // (a0) F1 — prefer the import the user chose in the Map-DWG-Layers dialog this
            // session, so Map and Place target the SAME import.
            if (_lastMappedImportId >= 0)
            {
                var prior = imports.FirstOrDefault(i => i.Id.Value == _lastMappedImportId);
                if (prior != null) { note = "Using the DWG import chosen in Map DWG Layers."; return prior; }
            }

            // (a) Prefer an ImportInstance currently selected in Revit.
            try
            {
                var sel = app?.ActiveUIDocument?.Selection?.GetElementIds();
                if (sel != null)
                {
                    foreach (var id in sel)
                    {
                        if (doc.GetElement(id) is Autodesk.Revit.DB.ImportInstance ii)
                        {
                            note = "Using the DWG import selected in Revit.";
                            return ii;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.ResolveTargetImport.selection: {ex.Message}"); }

            // (b) Otherwise let the user choose — newest first (highest ElementId first).
            var ordered = imports.OrderByDescending(i => i.Id.Value).ToList();
            var labels = new List<string>();
            var byLabel = new Dictionary<string, Autodesk.Revit.DB.ImportInstance>();
            foreach (var imp in ordered)
            {
                string name = DescribeImport(doc, imp);
                string label = $"{name}  [id {imp.Id.Value}]";
                if (!byLabel.ContainsKey(label)) { labels.Add(label); byLabel[label] = imp; }
            }
            string chosen;
            try { chosen = StingTools.Select.StingListPicker.Show("STING — Choose DWG import", "Multiple DWG/DXF imports found. Pick the one to map (newest first).", labels); }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.ResolveTargetImport.picker: {ex.Message}"); chosen = null; }
            if (string.IsNullOrEmpty(chosen)) { note = "No import chosen."; return null; }
            note = "Using the chosen DWG import.";
            return byLabel.TryGetValue(chosen, out var picked) ? picked : ordered[0];
        }

        /// <summary>A readable name for an ImportInstance (the CAD file/type name), id appended by the caller.</summary>
        private static string DescribeImport(Autodesk.Revit.DB.Document doc, Autodesk.Revit.DB.ImportInstance imp)
        {
            try
            {
                var typeId = imp.GetTypeId();
                var sym = typeId != null ? doc.GetElement(typeId) : null;
                string n = sym?.Name;
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch { }
            try { string cn = imp.Category?.Name; if (!string.IsNullOrWhiteSpace(cn)) return cn; } catch { }
            return "DWG import";
        }

        /// <summary>F1 — owner view name for a view-specific import, else "model" for a
        /// model-space import.</summary>
        private static string DescribeImportView(Autodesk.Revit.DB.Document doc, Autodesk.Revit.DB.ImportInstance imp)
        {
            try
            {
                if (imp != null && imp.ViewSpecific && imp.OwnerViewId != Autodesk.Revit.DB.ElementId.InvalidElementId)
                {
                    var v = doc.GetElement(imp.OwnerViewId) as Autodesk.Revit.DB.View;
                    if (!string.IsNullOrWhiteSpace(v?.Name)) return v.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.DescribeImportView: {ex.Message}"); }
            return "model";
        }

        /// <summary>F1 — build the dropdown choices for every detected import, newest first.
        /// Surfaces import-vs-link and owner view so a link / import-in-another-view is VISIBLE
        /// instead of silently substituted.</summary>
        private static List<StingTools.UI.DwgImportChoice> BuildImportChoices(
            Autodesk.Revit.DB.Document doc, List<Autodesk.Revit.DB.ImportInstance> imports)
        {
            var choices = new List<StingTools.UI.DwgImportChoice>();
            foreach (var imp in (imports ?? new List<Autodesk.Revit.DB.ImportInstance>())
                         .OrderByDescending(i => i.Id.Value))
            {
                bool isLink = false;
                try { isLink = imp.IsLinked; } catch { }
                choices.Add(new StingTools.UI.DwgImportChoice
                {
                    Import = imp,
                    Id = imp.Id.Value,
                    Name = DescribeImport(doc, imp),
                    IsLink = isLink,
                    ViewName = DescribeImportView(doc, imp)
                });
            }
            return choices;
        }

        /// <summary>F2/F3 — build the mounting-height options for the Map dialog: ONLY the named
        /// HeightStandards entries ("<key> - <PreferredMm>mm (<Standard>)"). The dialog's height
        /// combos are editable, so any raw custom height is typed directly (no hard-coded
        /// quick-list). Also returns the per-category default option so each row pre-fills from
        /// its category.</summary>
        private static List<StingTools.UI.DwgHeightOption> BuildHeightOptions(
            Autodesk.Revit.DB.Document doc,
            out Dictionary<string, StingTools.UI.DwgHeightOption> categoryDefaults)
        {
            var options = new List<StingTools.UI.DwgHeightOption>();
            // Named standards only (sorted by height for a sensible order).
            foreach (var kv in HeightStandardsTable.All.OrderBy(k => k.Value?.PreferredMm ?? 0))
            {
                var e = kv.Value;
                if (e == null) continue;
                double mm = e.PreferredMm > 0 ? e.PreferredMm : e.MinMm;
                options.Add(new StingTools.UI.DwgHeightOption
                {
                    Display = $"{kv.Key} - {mm:F0}mm ({e.Standard})",
                    Mm = mm,
                    Standard = kv.Key
                });
            }

            // Per-category default option (resolved to a list entry where possible). Categories
            // with a raw mountingHeightMm (no standard) pre-fill a typed-style "N mm" value.
            categoryDefaults = new Dictionary<string, StingTools.UI.DwgHeightOption>(StringComparer.OrdinalIgnoreCase);
            var fixtures = StingTools.Core.Placement.DwgSymbolMapRegistry.GetFixtureCategories(doc);
            foreach (var cat in fixtures)
            {
                var def = CategoryHeightDefaults.Resolve(doc, cat);
                if (def == null || def.MountingHeightMm <= 0) continue;
                var match = options.FirstOrDefault(o =>
                    Math.Abs(o.Mm - def.MountingHeightMm) < 0.5 &&
                    string.Equals(o.Standard ?? "", def.HeightStandard ?? "", StringComparison.OrdinalIgnoreCase));
                categoryDefaults[cat] = match ?? new StingTools.UI.DwgHeightOption
                {
                    Display = string.IsNullOrWhiteSpace(def.HeightStandard)
                        ? $"{def.MountingHeightMm:F0} mm"
                        : $"{def.HeightStandard} - {def.MountingHeightMm:F0}mm",
                    Mm = def.MountingHeightMm,
                    Standard = def.HeightStandard ?? ""
                };
            }
            return options;
        }

        /// <summary>F1 — read an import's layers and pre-fill seed rows from the existing
        /// resolution chain (override / detector). Shared by the initial dialog build and the
        /// dialog's import-switch callback so both target the SAME import consistently.</summary>
        private static List<(string Layer, int Count, string Category, string Variant, string Anchor)>
            BuildSeedRowsForImport(Autodesk.Revit.DB.Document doc, Autodesk.Revit.DB.ImportInstance import)
        {
            var rows = new List<(string, int, string, string, string)>();
            if (doc == null || import == null) return rows;
            try
            {
                var extraction = new StingTools.Model.CADToModelEngine(doc).PreviewImport(import);
                var layerCounts = extraction?.LayerCounts ?? new System.Collections.Generic.Dictionary<string, int>();
                foreach (var kv in layerCounts)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Key == "(unnamed)") continue;
                    var m = StingTools.Core.Placement.DwgSymbolMapRegistry.ResolveLayer(doc, kv.Key);
                    rows.Add((kv.Key, kv.Value, m?.Category ?? "", m?.VariantHint ?? "",
                              string.IsNullOrWhiteSpace(m?.Anchor) ? "WALL_MIDPOINT" : m.Anchor));
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlacementCenter.BuildSeedRowsForImport: {ex.Message}"); }
            return rows;
        }

        /// <summary>Tier 1 — open the Map DWG Layers grid over the import's real layers,
        /// pre-filled from the auto-detector, and save ByLayer rules to the project
        /// override. The dialog shows on the API thread (like the MEP wizard); the next
        /// Place run honours the mappings. No JSON hand-editing.</summary>
        private void OnMatrixPlace_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            RunInlineAction("Matrix Place", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                if (doc == null)
                    return StingResultPanel.Create("STING — Matrix Place")
                        .AddSection("RESULT").Text("No active document.");
                // Modal from the API thread — the dialog owns all Revit work (scan/place/save).
                var dlg = new StingTools.UI.MatrixPlaceDialog(app) { Owner = System.Windows.Window.GetWindow(this) };
                dlg.ShowDialog();
                return StingResultPanel.Create("STING — Matrix Place")
                    .SetSubtitle("Room x element-type grid — place-first, calculate-later.")
                    .AddSection("RESULT")
                    .Text("Matrix Place closed. Counts + placements persist in _BIM_COORD/placement_matrix.json; " +
                          "re-open Matrix Place to continue, or run Circuit / DIALux / load calc from inside it.");
            });
        }

        private void OnLibMapLayers_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            RunInlineAction("Map DWG layers", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;

                // F1 — enumerate ALL imports so the dialog can list them (import/link/view).
                List<Autodesk.Revit.DB.ImportInstance> allImports;
                try { allImports = StingTools.Model.CADToModelEngine.FindImportInstances(doc) ?? new List<Autodesk.Revit.DB.ImportInstance>(); }
                catch (Exception ex) { StingLog.Warn($"PlacementCenter.MapLayers.find: {ex.Message}"); allImports = new List<Autodesk.Revit.DB.ImportInstance>(); }
                if (allImports.Count == 0)
                    return StingResultPanel.Create("STING — Map DWG Layers")
                        .AddSection("RESULT").Text("No DWG/DXF import found. Link or import a DWG first. " +
                            "(Note: a Revit-format link is not a DWG import and will not appear here.)");

                // Default to the same import the Place run would pick (chosen > selected > newest).
                var import = ResolveTargetImport(app, doc, out _);
                if (import == null) import = allImports.OrderByDescending(i => i.Id.Value).First();

                // Bust the per-document layer-rule cache so re-opening after importing
                // another DWG re-reads the chosen import's layers (not a stale snapshot).
                try { StingTools.Core.Placement.DwgSymbolMapRegistry.Reload(doc); }
                catch (Exception ex) { StingLog.Warn($"PlacementCenter.MapLayers.Reload: {ex.Message}"); }

                // Category list = the FIXTURE allowlist (D1) ∩ seed-mappable categories — the
                // set the bridge can actually place. A fixture bridge never offers doors/
                // windows/furniture/structural. (Projects extend the allowlist via override.)
                var seedable = StingTools.Core.Placement.CategoryToSeedRegistry.GetMap(doc);
                var fixtures = StingTools.Core.Placement.DwgSymbolMapRegistry.GetFixtureCategories(doc);
                var categories = fixtures
                    .Where(c => seedable.TryGetValue(c, out var sid) && !string.IsNullOrWhiteSpace(sid))
                    .OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList();
                if (categories.Count == 0)   // legacy/no allowlist — fall back to all seedable
                    categories = seedable.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                        .Select(kv => kv.Key).OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList();

                // F1 — dropdown choices + which one we start on.
                var choices = BuildImportChoices(doc, allImports);
                int startIdx = choices.FindIndex(c => c.Id == import.Id.Value);
                if (startIdx < 0) startIdx = 0;

                // F2/F3 — height options + per-category defaults.
                var heightOptions = BuildHeightOptions(doc, out var categoryDefaults);

                // Seed rows for the starting import.
                var seedRows = BuildSeedRowsForImport(doc, import);
                if (seedRows.Count == 0 && choices.Count <= 1)
                    return StingResultPanel.Create("STING — Map DWG Layers")
                        .AddSection("RESULT").Text("The import has no layered geometry to map.");

                var dlg = new StingTools.UI.DwgLayerMapDialog(
                    categories, seedRows, heightOptions, categoryDefaults, choices, startIdx,
                    // F1 repopulate callback — runs on the API thread (dialog shows on it).
                    chosen => BuildSeedRowsForImport(doc, chosen as Autodesk.Revit.DB.ImportInstance));
                try { var owner = System.Windows.Window.GetWindow(this); if (owner != null) dlg.Owner = owner; } catch { }

                bool ok = dlg.ShowDialog() == true && dlg.Confirmed;
                if (!ok)
                    return StingResultPanel.Create("STING — Map DWG Layers").AddSection("RESULT").Text("Cancelled — no changes saved.");

                // F1 — remember the import the user mapped so the Place run targets the SAME one.
                if (dlg.SelectedImport is Autodesk.Revit.DB.ImportInstance picked)
                    _lastMappedImportId = picked.Id.Value;

                var inputs = dlg.Rows.Select(r => new StingTools.Core.Placement.DwgSymbolMapRegistry.LayerRuleInput
                {
                    Layer = r.Layer,
                    Category = r.IsMapped ? r.Category : "",   // "(skip)" → blank = unmap
                    VariantHint = r.Variant,
                    Anchor = r.Anchor,
                    MountingHeightMm = r.MountingHeightMm,      // F3 — per-layer override (0 = category default)
                    HeightStandard = r.HeightStandard
                }).ToList();

                int written;
                try { written = StingTools.Core.Placement.DwgSymbolMapRegistry.SaveLayerRulesToProjectOverride(doc, inputs); }
                catch (System.Exception ex)
                {
                    return StingResultPanel.Create("STING — Map DWG Layers").AddSection("ERROR").Text(ex.Message);
                }

                int mapped = inputs.Count(i => !string.IsNullOrWhiteSpace(i.Category));
                string importLabel = (dlg.SelectedImport is Autodesk.Revit.DB.ImportInstance pi)
                    ? $"{DescribeImport(doc, pi)} (id {pi.Id.Value})"
                    : "(unknown)";
                var panel = StingResultPanel.Create("STING — Map DWG Layers")
                    .SetSubtitle("Saved layer mappings to the project override (_BIM_COORD/dwg_symbol_map.json).")
                    .AddSection("RESULT")
                    .Metric("Import", importLabel)
                    .Metric("Layers mapped", mapped.ToString())
                    .Metric("Rules written", written.ToString());
                foreach (var i in inputs.Where(x => !string.IsNullOrWhiteSpace(x.Category)).Take(40))
                {
                    string h = i.MountingHeightMm > 0
                        ? $" @ {i.MountingHeightMm:F0}mm{(string.IsNullOrWhiteSpace(i.HeightStandard) ? "" : " " + i.HeightStandard)}"
                        : " @ category default";
                    panel.Text($"{i.Layer} -> {i.Category}{(string.IsNullOrWhiteSpace(i.VariantHint) ? "" : " / " + i.VariantHint)} ({i.Anchor}){h}");
                }
                panel.AddSection("NEXT").Text("Run 'Place STING fixtures from DWG symbols' — points on these layers now place at their mounting height.");
                return panel;
            });
        }

        /// <summary>Tier 2 experimental — exploded DWGs (no blocks/points): cluster loose
        /// lines on mapped layers into one point per symbol. ALWAYS dry-runs first and asks
        /// before committing (the cluster heuristic can over/under-count).</summary>
        private void OnLibDwgExploded_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            RunInlineAction("Exploded DWG fixtures (experimental)", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                // Target the SAME import as the dialog / Place run.
                var import = ResolveTargetImport(app, doc, out _);
                if (import == null)
                    return StingResultPanel.Create("STING — Exploded DWG capture (experimental)")
                        .AddSection("RESULT").Text("No DWG/DXF import found (or none chosen). Link or import a DWG first.");
                // Mandatory dry-run preview with the experimental cluster pass ON.
                var dry = StingTools.Core.Placement.DwgFixtureBridge.PlaceFromImport(doc, import, dryRun: true, includeLineClusters: true);

                var td = new TaskDialog("STING — Exploded DWG capture (experimental)")
                {
                    MainInstruction = $"Dry run: {dry.Placed} fixture(s) would be placed.",
                    MainContent = "Experimental line-cluster capture clusters loose lines/arcs on mapped layers " +
                                  "into one point per symbol. This is a HEURISTIC and can over/under-count on " +
                                  "messy DWGs. Review the counts, then choose whether to commit.",
                    ExpandedContent = string.Join("\n", dry.Messages.Take(20)),
                    CommonButtons = TaskDialogCommonButtons.None
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Place {dry.Placed} fixture(s) now");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel — preview only (no changes)");
                td.DefaultButton = TaskDialogResult.CommandLink2;

                var choice = td.Show();
                if (choice == TaskDialogResult.CommandLink1 && dry.Placed > 0)
                {
                    var real = StingTools.Core.Placement.DwgFixtureBridge.PlaceFromImport(doc, import, dryRun: false, includeLineClusters: true);
                    return StingTools.Commands.Placement.DwgToSeedFixturesCommand.BuildPanel(real);
                }
                var panel = StingTools.Commands.Placement.DwgToSeedFixturesCommand.BuildPanel(dry);
                panel.AddSection("NOTE").Text("Preview only — nothing was committed.");
                return panel;
            });
        }

        private void OnAuditSetup_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            try
            {
                var text = StingTools.Commands.Placement.PlacementSetupAuditCommand.BuildReportText(_doc, out int errs, out int warns, out string csv);
                var panel = StingResultPanel.Create("STING — Placement Setup Audit");
                panel.SetSubtitle((string.IsNullOrEmpty(csv) ? "" : $"CSV: {csv}"))
                     .AddSection("SUMMARY")
                     .Metric("Errors", errs.ToString())
                     .Metric("Warnings", warns.ToString())
                     .AddSection("DETAIL");
                AddTextWithIds(panel, text);
                Report("Audit Setup", panel);
            }
            catch (Exception ex)
            {
                StingLog.Error("PlacementCenter.OnAuditSetup", ex);
                TaskDialog.Show("STING — Placement Centre", $"Audit Setup failed: {ex.Message}");
            }
        }

        // ── Inline result panel ──────────────────────────────────────

        private void ShowInlineResult(string headline,
                                      IEnumerable<string> metrics,
                                      IEnumerable<string> findings)
        {
            // Route through the shared right-hand Report panel so every button
            // reports in one place (was: the separate grpRunResult group).
            var panel = StingResultPanel.Create(headline);
            var m = metrics?.ToList() ?? new List<string>();
            if (m.Count > 0) { panel.AddSection("SUMMARY"); foreach (var s in m) panel.Text(s); }
            var f = findings?.ToList() ?? new List<string>();
            if (f.Count > 0) { panel.AddSection("DETAIL"); foreach (var s in f) panel.Text(s); }
            Report(headline, panel);
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
                var pack = _doc != null ? DrawingTypeRegistry.TryGetPack(_doc, dtId) : null;
                if (pack != null)
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
                        var pk = DrawingTypeRegistry.TryGetPack(_doc, summary.DrawingTypeId);
                        if (pk != null)
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
                // Phase 139 — RAG status colour converter for the rule grid row style.
                if (!app.Resources.Contains("HexToBrushConverter"))
                    app.Resources.Add("HexToBrushConverter", new HexToBrushConverter());
                if (!app.Resources.Contains("EmptyStringToVisibility"))
                    app.Resources.Add("EmptyStringToVisibility", new EmptyStringToVisibilityConverter());
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPlacementCenter.EnsureResources: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        #region Family Converter
        // Family Converter tab — change a family's host / placement type and keep
        // it working across the project. All model/family work marshals to the
        // Revit API thread via RunInlineAction (mirrors OnRebuildSeeds_Click); all
        // output renders into the shared inline Report panel. The grid is the
        // single source of truth; buttons act on grid state. OS file/folder
        // pickers are the only allowed dialogs and always run on the WPF thread
        // FIRST, before the ExternalEvent is raised.

        private readonly FamilyHostConverter _fcConverter = new FamilyHostConverter();
        private readonly System.Collections.ObjectModel.ObservableCollection<FamilyConverterRow> _fcRows
            = new System.Collections.ObjectModel.ObservableCollection<FamilyConverterRow>();
        private FamilyHostTemplates _fcTemplates;
        private bool _fcGridBound;

        private void FcEnsureGrid()
        {
            if (_fcGridBound || gridConverter == null) return;
            gridConverter.ItemsSource = _fcRows;
            _fcGridBound = true;
        }

        // Repopulate the grid on the WPF thread from a fresh scan.
        private void FcPopulate(Document doc, IReadOnlyList<FamilyHostInfo> infos)
        {
            try
            {
                FcEnsureGrid();
                _fcTemplates = FamilyHostTemplateRegistry.Get(doc);
                _fcRows.Clear();
                if (infos != null)
                    foreach (var i in infos) _fcRows.Add(new FamilyConverterRow(_fcTemplates, i));
                Toast($"Loaded {_fcRows.Count} families into the converter grid.");
            }
            catch (Exception ex) { StingLog.Warn($"FcPopulate: {ex.Message}"); }
        }

        private void OnFcScan_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            RunInlineAction("Scan Families", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                var infos = _fcConverter.ScanProjectFamilies(doc);
                Dispatcher.BeginInvoke(new Action(() => FcPopulate(doc, infos)));
                return FcBuildScanReport("Family Converter — Scan", infos);
            });
        }

        private void OnFcImportFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            string folder = null;
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog
            { Description = "Pick a folder of .rfa families to load (searched recursively)" })
            {
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK) folder = fbd.SelectedPath;
            }
            if (string.IsNullOrEmpty(folder)) { Toast("Import cancelled."); return; }

            RunInlineAction("Import Folder", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                var infos = _fcConverter.ImportFolder(doc, folder);
                Dispatcher.BeginInvoke(new Action(() => FcPopulate(doc, infos)));
                return FcBuildScanReport($"Family Converter — Import Folder", infos)
                    .AddSection("SOURCE").Text(folder);
            });
        }

        private void OnFcLoadRfa_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Revit Family (*.rfa)|*.rfa",
                Multiselect = false,
                Title = "Load a family (.rfa)"
            };
            if (dlg.ShowDialog() != true) { Toast("Load cancelled."); return; }
            string path = dlg.FileName;

            RunInlineAction("Load .rfa", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                var infos = _fcConverter.ImportFile(doc, path);
                Dispatcher.BeginInvoke(new Action(() => FcPopulate(doc, infos)));
                return FcBuildScanReport("Family Converter — Load .rfa", infos)
                    .AddSection("SOURCE").Text(path);
            });
        }

        private StingResultPanel.Builder FcBuildScanReport(string title, IReadOnlyList<FamilyHostInfo> infos)
        {
            infos = infos ?? new List<FamilyHostInfo>();
            int conv = infos.Count(i => i.Convertible);
            var panel = StingResultPanel.Create($"STING — {title}")
                .SetSubtitle("Loaded model families with their current host / placement type (read-only).")
                .AddSection("SUMMARY")
                .Metric("Families", infos.Count.ToString())
                .Metric("Convertible", conv.ToString())
                .Metric("Not convertible", (infos.Count - conv).ToString());
            panel.AddSection("FAMILIES (top 60)");
            foreach (var i in infos.Take(60))
                panel.Text($"{i.Name}  ·  {i.Category}  ·  {i.CurrentPlacementType} ({i.CurrentHostKind})  ·  {i.InstanceCount} inst"
                    + (i.Convertible ? "" : $"  — {i.BlockReason}"));
            if (infos.Count > 60) panel.Text($"… and {infos.Count - 60} more (see the grid).");
            return panel;
        }

        // Dry run — no model changes. Reads the grid's current pending targets.
        private void OnFcAudit_Click(object sender, RoutedEventArgs e)
        {
            var rows = _fcRows.ToList();
            if (rows.Count == 0) { Toast("Run 'Scan Project Families' first."); return; }

            var pending = rows.Where(r => r.HasPendingTarget).ToList();
            var panel = StingResultPanel.Create("STING — Family Converter Audit (dry run)")
                .SetSubtitle("No model changes. Conversion path + fidelity risk per pending row.")
                .AddSection("SUMMARY")
                .Metric("Rows", rows.Count.ToString())
                .Metric("Pending", pending.Count.ToString())
                .Metric("P1 lossless", pending.Count(r => r.PathHint.StartsWith("P1")).ToString())
                .Metric("P2 rebuild", pending.Count(r => r.PathHint.StartsWith("P2")).ToString());

            if (pending.Count == 0)
            {
                panel.AddSection("NOTE").Text("No rows have a Target Host selected. Pick a target in the '→ Target Host' column, then Audit or Apply.");
            }
            else
            {
                panel.AddSection("PER-FAMILY");
                foreach (var r in pending)
                {
                    if (r.PathHint.StartsWith("P1"))
                        panel.Text($"{r.Name}: {r.CurrentPlacementType} → {r.TargetLabel}  ·  P1 lossless (checkbox toggle — geometry/params/connectors untouched, instances survive)");
                    else
                    {
                        string instNote = r.InstanceCount <= 0 ? ""
                            : IsHostedTarget(_fcTemplates?.ByLabel(r.TargetLabel))
                                ? $"; {r.InstanceCount} instance(s) WILL BE DELETED (hosted target — re-place from scratch)"
                                : $"; {r.InstanceCount} instance(s) will be re-placed free-standing";
                        panel.Text($"{r.Name}: {r.CurrentPlacementType} → {r.TargetLabel}  ·  P2 REBUILD (lossy — new .rfa from template; MEP connectors are harvested and re-created, any that cannot be placed are listed with coordinates; host-relative geometry re-anchors to template default; review after){instNote}");
                    }
                }
                if (pending.Any(r => r.PathHint.StartsWith("P2")))
                    panel.AddSection("NOTE").Text("Enable '⚠ Allow lossy rebuild (P2)' before Apply, or the P2 rows report as Skipped.");
            }

            // Shared-parameter pre-flight for the P2 rows — reading it needs the
            // Revit API thread (each family document is opened and closed), so it
            // runs through the standard inline-action bridge and reports there.
            var p2Ids = pending.Where(r => r.PathHint.StartsWith("P2")).Select(r => r.Id).ToList();
            if (p2Ids.Count == 0)
            {
                Report("Audit Only", panel);
                return;
            }

            Toast($"Auditing {p2Ids.Count} rebuild row(s) — checking shared parameters…");
            RunInlineAction("Audit Only", uiapp =>
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    panel.AddSection("SHARED-PARAMETER PRE-FLIGHT").Text("No active document — pre-flight skipped.");
                    return panel;
                }

                var pf = new FamilyHostConverter().PreflightSharedParameters(doc, p2Ids);

                panel.AddSection("SHARED-PARAMETER PRE-FLIGHT")
                     .Text($"Families inspected: {pf.FamiliesInspected}  ·  shared params seen: {pf.SharedParamsSeen}  ·  would fall back: {pf.WouldFallBack}")
                     .Text($"STING MR_PARAMETERS.txt: {(string.IsNullOrEmpty(pf.StingFile) ? "NOT FOUND" : pf.StingFile)}")
                     .Text($"Project's current file: {(string.IsNullOrEmpty(pf.UserFile) ? "(none set)" : pf.UserFile)}")
                     .Text("Both are searched by GUID during a rebuild — STING's first, then the project's. " +
                           "A parameter only degrades to a plain family parameter when it is in neither.");

                if (pf.WouldFallBack == 0)
                {
                    panel.Text("✓ No shared parameters would fall back. Tags, schedules, ExLink and COBie bindings survive the rebuild.");
                }
                else
                {
                    panel.Text($"⚠ {pf.WouldFallBack} shared parameter(s) would fall back to plain family parameters. " +
                               "Their shared binding is lost, so tags / schedules / ExLink / COBie that rely on them " +
                               "stop reading on the converted family until it is re-bound.")
                         .Text("Fix before applying: add the missing definitions to a shared-parameter file, or accept the loss knowingly.");
                    foreach (var n in pf.FallbackNames) panel.Text($"    • {n}");
                }

                if (pf.Warnings.Count > 0)
                {
                    panel.AddSection("PRE-FLIGHT WARNINGS");
                    foreach (var w in pf.Warnings) panel.Text(w);
                }
                return panel;
            });
        }

        private void OnFcApplySelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = gridConverter?.SelectedItems?.Cast<FamilyConverterRow>()
                .Where(r => r != null && r.HasPendingTarget).ToList() ?? new List<FamilyConverterRow>();
            FcApply(rows, "Apply Selected", batch: false);
        }

        private void OnFcApplyAll_Click(object sender, RoutedEventArgs e)
        {
            var rows = _fcRows.Where(r => r.HasPendingTarget).ToList();
            FcApply(rows, "Apply All Pending", batch: true);
        }

        private void FcApply(List<FamilyConverterRow> rows, string title, bool batch)
        {
            if (_doc == null) { Toast("No document open."); return; }
            if (rows == null || rows.Count == 0)
            {
                Toast("No pending conversions — pick a Target Host on a row first.");
                return;
            }

            bool rehost = chkFcRehost?.IsChecked == true;
            bool allowLossy = chkFcAllowLossy?.IsChecked == true;

            // A lossy P2 rebuild to a HOSTED placement (wall/ceiling/floor/roof)
            // cannot keep free-standing instances: Revit DELETES them on reload and
            // they must be re-placed from scratch. Confirm the destruction explicitly
            // — the "Allow lossy rebuild" checkbox alone is not clear enough.
            if (allowLossy)
            {
                var destructive = rows.Where(r =>
                    r != null && r.InstanceCount > 0 &&
                    string.Equals(r.PathHint, "P2 rebuild", StringComparison.OrdinalIgnoreCase) &&
                    IsHostedTarget(_fcTemplates?.ByLabel(r.TargetLabel))).ToList();

                if (destructive.Count > 0)
                {
                    int totalInst = destructive.Sum(r => r.InstanceCount);
                    var confirm = new TaskDialog("STING — Family Converter")
                    {
                        MainInstruction = $"{totalInst} placed instance(s) WILL BE DELETED",
                        MainContent =
                            $"{destructive.Count} family(ies) convert to a hosted placement (wall/ceiling/floor/roof) via a " +
                            "lossy rebuild. A free-standing instance cannot exist without a host, so Revit DELETES the " +
                            "existing placements on reload — they cannot be auto-rehosted and must be RE-PLACED from scratch " +
                            "on the new family.",
                        ExpandedContent = string.Join("\n",
                            destructive.Select(r => $"{r.Name} — {r.InstanceCount} instance(s) → {r.TargetLabel}")),
                        CommonButtons = TaskDialogCommonButtons.None
                    };
                    confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        $"Delete {totalInst} instance(s) and rebuild", "I will re-place them from scratch afterwards.");
                    confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel — make no changes");
                    confirm.DefaultButton = TaskDialogResult.CommandLink2;
                    if (confirm.Show() != TaskDialogResult.CommandLink1)
                    {
                        Toast("Family conversion cancelled — no changes made.");
                        return;
                    }
                }
            }

            // Capture request data on the WPF thread; the row objects travel with
            // the jobs so statuses can be updated after each conversion.
            var jobs = rows.Select(r => (Row: r, Request: new FamilyHostConversionRequest
            {
                FamilyId = r.Id,
                TargetId = _fcTemplates?.ByLabel(r.TargetLabel)?.Id ?? r.TargetLabel,
                RehostInstances = rehost,
                AllowLossyRebuild = allowLossy
            })).ToList();

            RunInlineAction(title, app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                var results = new List<(FamilyConverterRow Row, FamilyHostConversionResult Res)>();

                // Bracket the whole run — batch OR single selection — in a
                // TransactionGroup so it is one undo item; per-family failures are
                // isolated (continue on error) so one bad family never aborts the run.
                TransactionGroup grp = null;
                try { grp = new TransactionGroup(doc, batch ? "STING Family Convert (batch)" : "STING Family Convert (selected)"); grp.Start(); }
                catch (Exception ex) { StingLog.Warn($"FcApply group: {ex.Message}"); grp = null; }
                try
                {
                    foreach (var job in jobs)
                    {
                        FamilyHostConversionResult res;
                        try { res = _fcConverter.Convert(doc, job.Request); }
                        catch (Exception ex)
                        {
                            res = new FamilyHostConversionResult { FamilyName = job.Row.Name, PathUsed = "Skipped" };
                            res.Warnings.Add(ex.Message);
                            StingLog.Error($"FcApply Convert '{job.Row.Name}'", ex);
                        }
                        results.Add((job.Row, res));
                        var rr = res; var rn = job.Row.Name;
                        try { Dispatcher.BeginInvoke(new Action(() => Toast($"{title}: {rn} — {(rr.Success ? "ok" : rr.PathUsed)}"))); } catch { }
                    }
                }
                finally
                {
                    if (grp != null)
                    {
                        try { grp.Assimilate(); }
                        catch (Exception ex) { StingLog.Warn($"FcApply assimilate: {ex.Message}"); try { grp.RollBack(); } catch { } }
                    }
                }

                // Update per-row Status on the WPF thread.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var (row, res) in results) row.Status = FcStatus(res);
                }));

                return FcBuildApplyReport(title, results);
            });
        }

        // A target host is "hosted" (deletes free-standing instances on a P2
        // rebuild) unless it is a level-based / work-plane-based non-hosted type.
        private static bool IsHostedTarget(FamilyHostTarget tgt)
        {
            if (tgt == null) return false;
            return !string.Equals(tgt.PlacementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tgt.PlacementType, "OneLevelBased", StringComparison.OrdinalIgnoreCase);
        }

        private static string FcStatus(FamilyHostConversionResult res)
        {
            if (res == null) return "";
            if (res.PathUsed == "Skipped") return "⚠ skipped";
            if (res.Success) return $"✓ {res.PathUsed}" + (res.Warnings.Count > 0 ? " (warnings)" : "");
            return "✗ failed";
        }

        private StingResultPanel.Builder FcBuildApplyReport(string title,
            List<(FamilyConverterRow Row, FamilyHostConversionResult Res)> results)
        {
            int converted = results.Count(x => x.Res.Success);
            int skipped = results.Count(x => x.Res.PathUsed == "Skipped");
            int failed = results.Count(x => !x.Res.Success && x.Res.PathUsed != "Skipped");
            int rehosted = results.Sum(x => x.Res.InstancesRehosted);
            int rehostFail = results.Sum(x => x.Res.InstancesFailed);

            var panel = StingResultPanel.Create($"STING — {title}")
                .AddSection("SUMMARY")
                .Metric("Converted", converted.ToString())
                .Metric("Skipped", skipped.ToString())
                .Metric("Failed", failed.ToString())
                .Metric("Instances re-placed", rehosted.ToString())
                .Metric("Instances deleted / to re-place", rehostFail.ToString());

            panel.AddSection("PER-FAMILY");
            foreach (var (row, res) in results)
            {
                string outcome = res.Success ? "OK" : res.PathUsed == "Skipped" ? "skipped" : "FAILED";
                panel.Text($"{res.FamilyName}: {res.FromPlacement} → {res.ToPlacement}  ·  {res.PathUsed}  ·  {outcome}");
                foreach (var w in res.Warnings) panel.Text($"   ! {w}");
            }

            var nextSteps = results.SelectMany(x => x.Res.Notes).Where(n => n.Contains("NEXT STEP")).Distinct().ToList();
            var otherNotes = results.SelectMany(x => x.Res.Notes).Where(n => !n.Contains("NEXT STEP")).ToList();
            if (nextSteps.Count > 0)
            {
                panel.AddSection("NEXT STEPS");
                foreach (var n in nextSteps) panel.Text(n);
            }
            if (otherNotes.Count > 0)
            {
                panel.AddSection("NOTES");
                foreach (var n in otherNotes.Take(40)) panel.Text(n);
            }
            var rfas = results.Where(x => !string.IsNullOrEmpty(x.Res.NewRfaPath)).Select(x => x.Res.NewRfaPath).ToList();
            if (rfas.Count > 0)
            {
                panel.AddSection("NEW .RFA FILES");
                foreach (var p in rfas) panel.Text(p);
            }
            return panel;
        }

        private void OnFcSaveReload_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { Toast("No document open."); return; }
            RunInlineAction("Save / Reload", app =>
            {
                var doc = app?.ActiveUIDocument?.Document ?? _doc;
                string dir = FamilyHostConverter.ResolveConvertedDir(doc);
                var panel = StingResultPanel.Create("STING — Family Converter Save / Reload")
                    .SetSubtitle("Reload converted families from disk (belt-and-braces — P1/P2 already load on apply).")
                    .AddSection("RESULT");

                string[] files = System.IO.Directory.Exists(dir)
                    ? System.IO.Directory.GetFiles(dir, "*.rfa")
                    : Array.Empty<string>();
                int loaded = 0, failed = 0;
                var opts = new StingTools.Tags.StingFamilyLoadOptions(true);
                foreach (var f in files)
                {
                    try { if (doc.LoadFamily(f, opts, out _)) loaded++; else failed++; }
                    catch (Exception ex) { failed++; StingLog.Warn($"FcSaveReload '{f}': {ex.Message}"); }
                }
                panel.Metric("Converted-family folder", dir)
                     .Metric("Reloaded", loaded.ToString())
                     .Metric("Failed", failed.ToString());
                if (files.Length == 0)
                    panel.Text("No converted .rfa files on disk yet — run a P2 rebuild first (P1 conversions don't write a new file).");

                var infos = _fcConverter.ScanProjectFamilies(doc);
                Dispatcher.BeginInvoke(new Action(() => FcPopulate(doc, infos)));
                return panel;
            });
        }
        #endregion
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

    /// <summary>
    /// Phase 139 — convert "#RRGGBB" hex string to a SolidColorBrush so the
    /// rule grid row style can colour-code RAG status from the ViewModel.
    /// Falls back to white on null / parse failure.
    /// </summary>
    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is string s && !string.IsNullOrEmpty(s))
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s);
                    return new System.Windows.Media.SolidColorBrush(c);
                }
            }
            catch { }
            return System.Windows.Media.Brushes.White;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }

    /// <summary>
    /// One row in the Family Converter grid. Exposes the family's current host
    /// info plus a two-way <see cref="TargetLabel"/> whose setter recomputes the
    /// live <see cref="PathHint"/> (P1 lossless / P2 rebuild) so the Path cell
    /// updates the moment the user changes the Target Host combo.
    /// </summary>
    public sealed class FamilyConverterRow : INotifyPropertyChanged
    {
        private readonly FamilyHostTemplates _tpl;
        private readonly FamilyPlacementType _fpt;

        public ElementId Id { get; }
        public string Name { get; }
        public string Category { get; }
        public string CurrentPlacementType { get; }
        public string CurrentHostKind { get; }
        public string SourceTargetId { get; }
        public int InstanceCount { get; }
        public bool Convertible { get; }
        public string BlockReason { get; }
        public List<string> AllowedTargets { get; }
        public string CurrentHostDisplay => $"{CurrentPlacementType} · {CurrentHostKind}";

        public FamilyConverterRow(FamilyHostTemplates tpl, FamilyHostInfo info)
        {
            _tpl = tpl;
            _fpt = info.Placement;
            Id = info.Id;
            Name = info.Name;
            Category = info.Category;
            CurrentPlacementType = info.CurrentPlacementType;
            CurrentHostKind = info.CurrentHostKind;
            SourceTargetId = info.SourceTargetId;
            InstanceCount = info.InstanceCount;
            Convertible = info.Convertible;
            BlockReason = string.IsNullOrEmpty(info.BlockReason) ? "Convertible" : info.BlockReason;
            AllowedTargets = info.AllowedTargets ?? new List<string>();
        }

        private string _targetLabel;
        public string TargetLabel
        {
            get => _targetLabel;
            set { if (_targetLabel == value) return; _targetLabel = value; OnPropertyChanged(nameof(TargetLabel)); RecomputePath(); }
        }

        private string _pathHint = "";
        public string PathHint
        {
            get => _pathHint;
            private set { if (_pathHint == value) return; _pathHint = value; OnPropertyChanged(nameof(PathHint)); }
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public bool HasPendingTarget => Convertible && !string.IsNullOrEmpty(TargetLabel);

        private void RecomputePath()
        {
            if (string.IsNullOrEmpty(_targetLabel) || _tpl == null) { PathHint = ""; return; }
            var tgt = _tpl.ByLabel(_targetLabel);
            if (tgt == null) { PathHint = ""; return; }
            string p = FamilyHostConverter.ResolvePath(_tpl, SourceTargetId, tgt.Id, _fpt);
            PathHint = p == "P1_Checkbox" ? "P1 lossless" : "P2 rebuild";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Phase 139 — empty/null string → Visible, otherwise → Collapsed.
    /// Used by the Building Profile header banner that fires when no
    /// profile is loaded.
    /// </summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
