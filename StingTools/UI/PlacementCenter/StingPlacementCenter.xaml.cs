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
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.Core.Validation;
using StingTools.Core.Visualization;

namespace StingTools.UI.PlacementCenter
{
    public partial class StingPlacementCenter : Window
    {
        // Single instance — stays alive across opens; modeless.
        private static StingPlacementCenter _instance;

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
            cmbCategory.ItemsSource = VM.Categories;
            cmbAnchor.ItemsSource   = VM.AnchorTypes;
            cmbSide.ItemsSource     = VM.SideConstraints;

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
            rbScopeView.Checked      += (_,__) => VM.RunOpts.Scope = "ActiveView";
            rbScopeSel.Checked       += (_,__) => VM.RunOpts.Scope = "Selection";
            rbScopeProj.Checked      += (_,__) => VM.RunOpts.Scope = "Project";

            // Per-rule field handlers — wired manually so we can validate after each edit
            cmbCategory.LostFocus       += (_,__) => CommitField(() => VM.Selected.CategoryFilter   = (cmbCategory.Text ?? "").Trim());
            cmbCategory.SelectionChanged+= (_,__) => CommitField(() => VM.Selected.CategoryFilter   = (cmbCategory.Text ?? "").Trim());
            txtVariant.LostFocus        += (_,__) => CommitField(() => VM.Selected.VariantHint      = txtVariant.Text);
            txtRoom.LostFocus           += (_,__) => CommitField(() => VM.Selected.RoomFilter       = txtRoom.Text);
            cmbAnchor.SelectionChanged  += (_,__) => CommitField(() => VM.Selected.AnchorType       = cmbAnchor.SelectedItem as string ?? VM.Selected.AnchorType);
            cmbSide.SelectionChanged    += (_,__) => CommitField(() => VM.Selected.SideConstraint   = cmbSide.SelectedItem   as string ?? VM.Selected.SideConstraint);
            txtPriority.LostFocus       += (_,__) => CommitField(() => VM.Selected.Priority         = ParseInt(txtPriority.Text, VM.Selected.Priority));
            txtOffsetX.LostFocus        += (_,__) => CommitField(() => VM.Selected.OffsetXMm        = ParseDouble(txtOffsetX.Text, VM.Selected.OffsetXMm));
            txtMountH.LostFocus         += (_,__) => CommitField(() => VM.Selected.MountingHeightMm = ParseDouble(txtMountH.Text,  VM.Selected.MountingHeightMm));
            txtSpacing.LostFocus        += (_,__) => CommitField(() => VM.Selected.MinSpacingMm     = ParseDouble(txtSpacing.Text, VM.Selected.MinSpacingMm));
            txtMaxRoom.LostFocus        += (_,__) => CommitField(() => VM.Selected.MaxPerRoom       = ParseInt(txtMaxRoom.Text,    VM.Selected.MaxPerRoom));

            // VM → status bar binding
            VM.PropertyChanged += OnVmPropertyChanged;

            // Load
            VM.LoadFromProject(_doc);
            VM.AttachFilteredView();
            gridRules.ItemsSource = VM.FilteredRules;
            if (VM.Rules.Count > 0) gridRules.SelectedIndex = 0;
            UpdateStatus();
        }

        // ── Singleton orchestration ──────────────────────────────────

        public static void ShowOrFocus(UIApplication uiApp)
        {
            if (_instance != null && !_instance._closed)
            {
                _instance.Activate();
                return;
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

        private bool _closed;

        // ── Toolbar handlers ─────────────────────────────────────────

        private void OnReloadDefaults_Click(object sender, RoutedEventArgs e)
        {
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

            DateTime startUtc = DateTime.UtcNow;
            PlacementResult result = null;
            try
            {
                using (var tg = new TransactionGroup(_doc, "STING Placement Centre — Run"))
                {
                    tg.Start();
                    using (var t = new Transaction(_doc, "STING PlaceFixtures"))
                    {
                        t.Start();
                        result = FixturePlacementEngine.PlaceFixturesInScope(_doc, roomIds, rules, dryRun: false);
                        t.Commit();
                    }
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

            _lastRunUtc = startUtc;
            _lastPlacedIds = result?.PlacedIds?.ToList() ?? new List<ElementId>();
            int placed  = _lastPlacedIds.Count;
            int skipped = result?.SkippedCount ?? 0;
            int warns   = result?.Warnings?.Count ?? 0;

            VM.Status = $"Placed {placed} · skipped {skipped} · warnings {warns}";
            UpdateStatus();

            // Optional: validators on what just happened
            if (VM.RunOpts.RunValidators)
                ShowFindings(scopeToProvenance: true, headline: "Run + post-validation");
            else
                TaskDialog.Show("STING — Placement Centre",
                    $"Placement run complete.\n\n" +
                    $"Placed: {placed}\nSkipped: {skipped}\nWarnings: {warns}\n\n" +
                    "Run Validate now to audit the result, or click Undo last run (Phase D) to revert.");
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
                var findings = PlacementCenterBridge.RunValidators(_doc);
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
                gridFamilyHints.ItemsSource = null;
                VM.Status = "Pick a rule with a non-empty CategoryFilter first.";
                UpdateStatus();
                return;
            }
            try
            {
                var rows = FamilyHintsBridge.Inspect(_doc, VM.Selected.CategoryFilter);
                gridFamilyHints.ItemsSource = rows;
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
                    "  • PLACE_ORIENTATION_RULE_TXT (cleared so the rule's anchor takes priority)\n" +
                    "  • PLACE_MOUNT_HEIGHT_MM\n" +
                    "  • STING_FIXTURE_VARIANT_TXT\n" +
                    "  • STING_ROOM_TYPE_FILTER_TXT\n\n" +
                    "Use Undo to reverse all writes as a single transaction.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return;

            try
            {
                var (types, writes) = FamilyHintsBridge.PushRuleToFamilyTypes(_doc, VM.Selected);
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
        private void OnHeatmap_Click(object sender, RoutedEventArgs e)
            => DeferToPhase("Heat-map", "D");
        private void OnGDStudy_Click(object sender, RoutedEventArgs e)
            => DeferToPhase("Generative Design Study", "D");

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

        private void OnSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            VM.SearchText = txtSearch.Text;
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
                VM.Selected.Validate();
                txtRuleError.Text = VM.Selected.IsValid ? "" : VM.Selected.ErrorMessage;
                VM.RebuildCategories();
                UpdateStatus();
                gridRules.Items.Refresh();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPlacementCenter.CommitField: {ex.Message}");
                txtRuleError.Text = ex.Message;
            }
        }

        private void SyncFieldsFromSelection()
        {
            _suppressUiSync = true;
            try
            {
                pnlDetail.IsEnabled = VM.HasSelection;
                if (VM.Selected == null) { txtRuleError.Text = ""; return; }
                cmbCategory.Text          = VM.Selected.CategoryFilter ?? "";
                txtVariant.Text           = VM.Selected.VariantHint    ?? "";
                txtRoom.Text              = VM.Selected.RoomFilter     ?? "";
                cmbAnchor.SelectedItem    = VM.Selected.AnchorType;
                cmbSide.SelectedItem      = VM.Selected.SideConstraint;
                txtPriority.Text          = VM.Selected.Priority.ToString(CultureInfo.InvariantCulture);
                txtOffsetX.Text           = VM.Selected.OffsetXMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtMountH.Text            = VM.Selected.MountingHeightMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtSpacing.Text           = VM.Selected.MinSpacingMm.ToString("0.##", CultureInfo.InvariantCulture);
                txtMaxRoom.Text           = VM.Selected.MaxPerRoom.ToString(CultureInfo.InvariantCulture);
                txtRuleError.Text         = VM.Selected.IsValid ? "" : VM.Selected.ErrorMessage;
            }
            finally { _suppressUiSync = false; }
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

        private void DeferToPhase(string label, string phase)
        {
            TaskDialog.Show("STING — Placement Centre",
                $"{label} wires in Phase 127-{phase}. Phase A ships the layout, edit, and persistence surface only.\n\n" +
                "Track the wiring schedule in docs/CHANGELOG.md.");
        }

        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        private static double ParseDouble(string s, double fallback) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : fallback;

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
            => Binding.DoNothing;
    }
}
