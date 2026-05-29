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
                this.Title = $"STING — Placement Centre  [build {StingTools.Core.Placement.FixturePlacementEngine.BuildStamp}  {StingTools.Core.Placement.FixturePlacementEngine.PhaseTag}]";
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
            }
            catch (Exception evEx)
            {
                StingLog.Warn($"PlacementCenter: ExternalEvent.Create at ctor: {evEx.Message}");
            }

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
            if (_doc == null) { TaskDialog.Show("STING — Placement Centre", "No document open."); return; }

            var rules = PlacementCenterBridge.ToRules(VM.Rules);
            if (rules.Count == 0)
            {
                TaskDialog.Show("STING — Placement Centre",
                    "No valid rules to run. Add at least one rule with a non-empty CategoryFilter.");
                return;
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
                        helpfulHints.Add($"~{doorsTotal - doorsWithSpatial} of {doorsTotal} doors have no FromRoom/ToRoom — those will be skipped by Phase 139.18 filter.");
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
                    RaiseRevitToFront();
                    var dlg = new TaskDialog("STING — Placement Centre · Prerequisites missing")
                    {
                        MainInstruction = $"{blockers.Count} prerequisite(s) failed — run aborted.",
                        MainContent = string.Join("\n\n", blockers)
                            + "\n\nPhase 139.21 hard-fails the run when these are present so we don't produce silently-wrong placements. "
                            + (helpfulHints.Count > 0 ? "\n\nAlso noted:\n  " + string.Join("\n  ", helpfulHints) : ""),
                        CommonButtons = TaskDialogCommonButtons.Close,
                    };
                    dlg.Show();
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
                TaskDialog.Show("STING — Placement Centre",
                    $"Scope '{VM.RunOpts.Scope}' resolved zero rooms. Switch scope or open a view that contains rooms.");
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
                try { if (btnRunPlacement != null) btnRunPlacement.IsEnabled = true; } catch { }
                return;
            }
            // Click-handler returns here. Engine runs on API thread; the
            // handler will invoke OnRunCompleted on the WPF thread when
            // done.
            return;
        }

        // Phase 139.13 — completion callback. Runs on the WPF thread
        // (Dispatcher.BeginInvoke from the IExternalEventHandler).
        private void OnRunCompleted(PlacementRunRequest req, PlacementResult result, Exception err)
        {
            try { req?.HeartbeatCts?.Cancel(); } catch { }
            try { req?.Progress?.Close(); } catch { }
            StingTools.Commands.Placement.PlaceFixturesOptions.StampProvenance = req?.PrevStamp ?? false;
            StingTools.Commands.Placement.PlaceFixturesOptions.HonourLearned   = req?.PrevLearn ?? false;

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
                panel.SetSubtitle($"{placed} placed · {skipped} skipped · {warns} warning(s) · build {StingTools.Core.Placement.FixturePlacementEngine.BuildStamp} ({StingTools.Core.Placement.FixturePlacementEngine.PhaseTag})");

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
                RaiseRevitToFront();
                panel.Show();
            }
            catch (Exception pEx) { StingLog.Warn($"PlacementCenter post-run panel: {pEx.Message}"); }

            try { VM.SetHistory(HistoryBridge.ReadHistory(_doc)); }
            catch (Exception hEx) { StingLog.Warn($"PlacementCenter post-run history refresh: {hEx.Message}"); }

            if (VM.RunOpts.AutoHeatmap && placed > 0)
            {
                try { OnHeatmap_Click(this, null); }
                catch (Exception hmEx) { StingLog.Warn($"PlacementCenter auto-heatmap: {hmEx.Message}"); }
            }

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
                grpRunResult.Visibility = System.Windows.Visibility.Visible;
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
