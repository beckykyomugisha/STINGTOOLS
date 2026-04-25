// StingTools v4 MVP — Fabrication Workspace dialog.
//
// Light-themed corporate workspace mirroring the DrawingTypeEditorDialog
// pattern: 2-column grid (left = preset / scope / filter cards;
// right = output / content-mode / title-block cards), plus a footer
// row of action buttons (Generate package, Cut list, Weld map,
// Isometrics, PCF, MAJ, BOM roll-up, Doc Register link, Close).
//
// All state is funnelled through StingTools.Commands.Fabrication.
// FabricationOptions before the underlying commands are dispatched
// via the IExternalEventHandler / Cmd_Click pipeline. Pressing an
// action button raises the matching Fabrication_* tag on the dock
// panel command handler so the existing IExternalCommand wiring is
// reused without duplication.
//
// Palette is the same LightPalette used by DrawingTypeEditorDialog
// — no dark mode anywhere.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Commands.Fabrication;

// Disambiguate WPF vs Revit types.
using Color    = System.Windows.Media.Color;
using Grid     = System.Windows.Controls.Grid;
using TextBox  = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace StingTools.UI
{
    public sealed class FabricationWorkspaceDialog : Window
    {
        // ── palette (light, contrast-safe — same as DrawingTypeEditorDialog) ──
        private static readonly Color BgColor     = Color.FromRgb(0xFA, 0xFA, 0xFA);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CardBg      = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CardBorder  = Color.FromRgb(0xCF, 0xD8, 0xDC);
        private static readonly Color FgColor     = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color SubtleColor = Color.FromRgb(0x66, 0x66, 0x66);
        private static readonly Color GreenBtn    = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color OrangeBtn   = Color.FromRgb(0xFF, 0x98, 0x00);
        private static readonly Color NeutralBtn  = Color.FromRgb(0xE8, 0xE8, 0xE8);

        // ── state ──
        private readonly Document _doc;

        // Preset
        private ComboBox  _cmbPreset;

        // Scope
        private RadioButton _rbScopeSel;
        private RadioButton _rbScopeView;
        private RadioButton _rbScopeProj;
        private TextBlock   _txtScopeStatus;

        // Filter (discipline rules)
        private CheckBox _chkPipe, _chkPipeLB, _chkDuct, _chkDuctPitt, _chkConduit;

        // Output
        private CheckBox _chkAssy, _chkViews, _chkSheets, _chkSymbols, _chkCsv;

        // Content mode
        private RadioButton _rbIso6412, _rbGeneric;

        // Title block / view template
        private TextBlock _txtShopDrawingStatus;

        public FabricationWorkspaceDialog(Document doc)
        {
            _doc = doc;

            Title = "STING v4 — Fabrication Workspace";
            Width = 1080; Height = 720;
            MinWidth = 900; MinHeight = 560;
            Background = new SolidColorBrush(BgColor);
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            DarkDialogTheme.ApplyComboBoxFix(this, CardBg, FgColor, CardBorder);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { }

            Content = BuildLayout();
            HydrateFromOptions();
            RefreshScopeStatus();
            RefreshShopDrawingStatus();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYOUT — 2-column grid + footer
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = BuildLeftPanel();
            Grid.SetColumn(left, 0); Grid.SetRow(left, 0);
            root.Children.Add(left);

            var right = BuildRightPanel();
            Grid.SetColumn(right, 1); Grid.SetRow(right, 0);
            root.Children.Add(right);

            var footer = BuildFooter();
            Grid.SetColumn(footer, 0); Grid.SetRow(footer, 1); Grid.SetColumnSpan(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        // ─── Left panel ─────────────────────────────────────────────────

        private UIElement BuildLeftPanel()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 8, 0),
            };
            var stack = new StackPanel();
            stack.Children.Add(BuildPresetCard());
            stack.Children.Add(BuildScopeCard());
            stack.Children.Add(BuildFilterCard());
            scroll.Content = stack;
            return scroll;
        }

        private UIElement BuildPresetCard()
        {
            var body = new StackPanel();

            _cmbPreset = new ComboBox { Height = 24, IsEditable = true };
            DarkDialogTheme.StyleInput(_cmbPreset, CardBg, FgColor, CardBorder);
            foreach (var name in ListPresetNames()) _cmbPreset.Items.Add(name);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(LabelInline("Preset", 60));
            _cmbPreset.Width = 180;
            row.Children.Add(_cmbPreset);
            row.Children.Add(MakeSmallBtn("Load",   () => LoadPreset()));
            row.Children.Add(MakeSmallBtn("Save…",  () => SavePreset()));
            row.Children.Add(MakeSmallBtn("Delete", () => DeletePreset()));
            body.Children.Add(row);

            return Card("Preset", body);
        }

        private UIElement BuildScopeCard()
        {
            var body = new StackPanel();

            _rbScopeSel  = MakeRadio("Selection",   "FabScope", FabricationOptions.ScopeSelection);
            _rbScopeView = MakeRadio("Active view", "FabScope", FabricationOptions.ScopeActiveView);
            _rbScopeProj = MakeRadio("Project",     "FabScope", FabricationOptions.ScopeProject);
            _rbScopeSel.Checked  += (s, e) => { CommitScope(); RefreshScopeStatus(); };
            _rbScopeView.Checked += (s, e) => { CommitScope(); RefreshScopeStatus(); };
            _rbScopeProj.Checked += (s, e) => { CommitScope(); RefreshScopeStatus(); };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_rbScopeSel);
            row.Children.Add(_rbScopeView);
            row.Children.Add(_rbScopeProj);
            row.Children.Add(MakeSmallBtn("Refresh", RefreshScopeStatus));
            body.Children.Add(row);

            _txtScopeStatus = new TextBlock
            {
                Text = "Scope: …",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            body.Children.Add(_txtScopeStatus);

            return Card("Scope", body);
        }

        private UIElement BuildFilterCard()
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = "Discipline rules from STING_FAB_RULES.json",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
            });

            _chkPipe     = CheckRow("Pipe (max 6 m / 4 bends)",                   FabricationOptions.RulePipe,     v => FabricationOptions.RulePipe     = v);
            _chkPipeLB   = CheckRow("Pipe large bore (max 3 m)",                  FabricationOptions.RulePipeLB,   v => FabricationOptions.RulePipeLB   = v);
            _chkDuct     = CheckRow("Duct TDF (max 3 m / 3 bends)",               FabricationOptions.RuleDuct,     v => FabricationOptions.RuleDuct     = v);
            _chkDuctPitt = CheckRow("Duct Pittsburgh (max 2.4 m)",                FabricationOptions.RuleDuctPitt, v => FabricationOptions.RuleDuctPitt = v);
            _chkConduit  = CheckRow("Conduit (max 6 m / 3 bends, BS 7671)",       FabricationOptions.RuleConduit,  v => FabricationOptions.RuleConduit  = v);

            body.Children.Add(_chkPipe);
            body.Children.Add(_chkPipeLB);
            body.Children.Add(_chkDuct);
            body.Children.Add(_chkDuctPitt);
            body.Children.Add(_chkConduit);

            return Card("Filter", body);
        }

        // ─── Right panel ────────────────────────────────────────────────

        private UIElement BuildRightPanel()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8, 0, 0, 0),
            };
            var stack = new StackPanel();
            stack.Children.Add(BuildOutputCard());
            stack.Children.Add(BuildContentModeCard());
            stack.Children.Add(BuildShopDrawingCard());
            stack.Children.Add(BuildExportsCard());
            scroll.Content = stack;
            return scroll;
        }

        private UIElement BuildOutputCard()
        {
            var body = new StackPanel();
            _chkAssy    = CheckRow("Generate AssemblyInstances",  FabricationOptions.GenerateAssemblies,  v => FabricationOptions.GenerateAssemblies  = v);
            _chkViews   = CheckRow("Generate views (5 + BOM)",    FabricationOptions.GenerateViews,       v => FabricationOptions.GenerateViews       = v);
            _chkSheets  = CheckRow("Generate shop drawing sheets",FabricationOptions.GenerateSheets,      v => FabricationOptions.GenerateSheets      = v);
            _chkSymbols = CheckRow("Place ISO 6412 symbols",      FabricationOptions.PlaceISO6412Symbols, v => FabricationOptions.PlaceISO6412Symbols = v);
            _chkCsv     = CheckRow("Emit per-discipline CSVs",    FabricationOptions.EmitPerDisciplineCsv,v => FabricationOptions.EmitPerDisciplineCsv= v);
            body.Children.Add(_chkAssy);
            body.Children.Add(_chkViews);
            body.Children.Add(_chkSheets);
            body.Children.Add(_chkSymbols);
            body.Children.Add(_chkCsv);
            return Card("Output", body);
        }

        private UIElement BuildContentModeCard()
        {
            var body = new StackPanel { Orientation = Orientation.Horizontal };
            _rbIso6412 = MakeRadio("ISO 6412 (workshop)",     "FabContent", FabricationOptions.ContentModeIso6412);
            _rbGeneric = MakeRadio("Generic (geometry only)", "FabContent", !FabricationOptions.ContentModeIso6412);
            _rbIso6412.Checked += (s, e) => FabricationOptions.ContentModeIso6412 = true;
            _rbGeneric.Checked += (s, e) => FabricationOptions.ContentModeIso6412 = false;
            body.Children.Add(_rbIso6412);
            body.Children.Add(_rbGeneric);
            return Card("Content mode", body);
        }

        private UIElement BuildShopDrawingCard()
        {
            var body = new StackPanel();
            _txtShopDrawingStatus = new TextBlock
            {
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            };
            body.Children.Add(_txtShopDrawingStatus);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(MakeSmallBtn("Configure…", ConfigureShopDrawing));
            row.Children.Add(MakeSmallBtn("Clear",      ClearShopDrawing));
            body.Children.Add(row);

            return Card("Title block & view template", body);
        }

        private UIElement BuildExportsCard()
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = "Re-emit per-discipline sidecars without rebuilding the full package.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });

            var wrap = new WrapPanel();
            wrap.Children.Add(MakeAccentBtn("Cut list",    "Fabrication_ExportCutList"));
            wrap.Children.Add(MakeAccentBtn("Weld map",    "Fabrication_ExportWeldMap"));
            wrap.Children.Add(MakeAccentBtn("Isometrics",  "Fabrication_ExportIsometrics"));
            wrap.Children.Add(MakeAccentBtn("PCF",         "Fabrication_ExportPcf"));
            wrap.Children.Add(MakeAccentBtn("MAJ",         "Fabrication_ExportMaj"));
            wrap.Children.Add(MakeAccentBtn("ISO symbols", "Fabrication_PlaceISOSymbols"));
            body.Children.Add(wrap);

            return Card("Exports", body);
        }

        // ─── Footer ─────────────────────────────────────────────────────

        private UIElement BuildFooter()
        {
            var row = new DockPanel
            {
                Margin = new Thickness(0, 10, 0, 0),
                LastChildFill = false,
            };

            var hint = new TextBlock
            {
                Text = "Settings persist on FabricationOptions for the Revit session. " +
                       "Generate package builds assemblies, views, sheets and (optionally) ISO 6412 symbols.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
            };
            DockPanel.SetDock(hint, Dock.Left);
            row.Children.Add(hint);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(right, Dock.Right);

            right.Children.Add(MakeBigBtn("Close",            NeutralBtn, false, () => { DialogResult = false; }));
            right.Children.Add(MakeBigBtn("Generate package", GreenBtn,   true,  () => Dispatch("Fabrication_GeneratePackage")));
            row.Children.Add(right);

            return row;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ACTIONS
        // ═══════════════════════════════════════════════════════════════

        private void CommitScope()
        {
            FabricationOptions.ScopeSelection  = _rbScopeSel?.IsChecked  == true;
            FabricationOptions.ScopeActiveView = _rbScopeView?.IsChecked == true;
            FabricationOptions.ScopeProject    = _rbScopeProj?.IsChecked == true;
        }

        private void RefreshScopeStatus()
        {
            try
            {
                int n = CountScopeElements();
                string label =
                    FabricationOptions.ScopeProject    ? "project"     :
                    FabricationOptions.ScopeActiveView ? "active view" :
                                                         "selection";
                string msg = $"Scope: {label} — {n} MEP element(s) in scope.";
                if (n == 0 && FabricationOptions.ScopeSelection)
                    msg += " (Generate package will fall back to active view automatically.)";
                if (_txtScopeStatus != null) _txtScopeStatus.Text = msg;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationWorkspaceDialog.RefreshScopeStatus: {ex.Message}");
                if (_txtScopeStatus != null) _txtScopeStatus.Text = "Scope: (refresh failed — see log)";
            }
        }

        private int CountScopeElements()
        {
            if (_doc == null) return 0;
            var cats = new[]
            {
                BuiltInCategory.OST_PipeCurves,    BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_PipeFitting,   BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_DuctCurves,    BuiltInCategory.OST_FlexDuctCurves,
                BuiltInCategory.OST_DuctFitting,   BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_Conduit,       BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_CableTray,     BuiltInCategory.OST_CableTrayFitting,
            };
            try
            {
                if (FabricationOptions.ScopeProject)
                {
                    return new FilteredElementCollector(_doc)
                        .WherePasses(new ElementMulticategoryFilter(cats))
                        .WhereElementIsNotElementType()
                        .GetElementCount();
                }
                if (FabricationOptions.ScopeActiveView)
                {
                    var view = _doc.ActiveView;
                    if (view == null) return 0;
                    return new FilteredElementCollector(_doc, view.Id)
                        .WherePasses(new ElementMulticategoryFilter(cats))
                        .WhereElementIsNotElementType()
                        .GetElementCount();
                }
                // Selection scope — no UIDocument here; the count is approximated.
                return 0;
            }
            catch { return 0; }
        }

        private void ConfigureShopDrawing()
        {
            if (_doc == null) return;
            try
            {
                var dlg = new ShopDrawingOptionsDialog(_doc);
                try { dlg.Owner = this; } catch { }
                if (dlg.ShowDialog() == true)
                {
                    FabricationOptions.ShopDrawing = dlg.Result;
                    RefreshShopDrawingStatus();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationWorkspaceDialog.ConfigureShopDrawing", ex);
            }
        }

        private void ClearShopDrawing()
        {
            FabricationOptions.ShopDrawing = null;
            RefreshShopDrawingStatus();
        }

        private void RefreshShopDrawingStatus()
        {
            if (_txtShopDrawingStatus == null) return;
            var opts = FabricationOptions.ShopDrawing;
            if (opts == null)
            {
                _txtShopDrawingStatus.Text =
                    "Auto-resolved per discipline (STING_TB_ASSEMBLY_*).\n" +
                    "Falls back to first available title block when missing.";
                return;
            }
            string tb = "Auto (per-discipline)";
            if (_doc != null && opts.TitleBlockSymbolId != null
                && opts.TitleBlockSymbolId != ElementId.InvalidElementId)
            {
                var fs = _doc.GetElement(opts.TitleBlockSymbolId) as FamilySymbol;
                if (fs != null) tb = $"{fs.FamilyName} : {fs.Name}";
            }
            string vt = "None";
            if (_doc != null && opts.ViewTemplateId != null
                && opts.ViewTemplateId != ElementId.InvalidElementId)
            {
                var v = _doc.GetElement(opts.ViewTemplateId) as Autodesk.Revit.DB.View;
                if (v != null) vt = v.Name;
            }
            string s = $"Title block: {tb}\nView template: {vt}";
            if (!string.IsNullOrWhiteSpace(opts.SheetNumberPattern))
                s += $"\nSheet #: {opts.SheetNumberPattern}";
            if (!string.IsNullOrWhiteSpace(opts.SheetNamePattern))
                s += $"\nSheet name: {opts.SheetNamePattern}";
            _txtShopDrawingStatus.Text = s;
        }

        // ─── Preset persistence ─────────────────────────────────────────

        private static string PresetPath()
        {
            string dir = StingToolsApp.DataPath ?? Path.GetTempPath();
            return Path.Combine(dir, "FABRICATION_PRESETS.json");
        }

        private static Dictionary<string, FabricationPreset> LoadAllPresets()
        {
            try
            {
                string path = PresetPath();
                if (!File.Exists(path)) return new Dictionary<string, FabricationPreset>(StringComparer.OrdinalIgnoreCase);
                var json = File.ReadAllText(path);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, FabricationPreset>>(json);
                return dict ?? new Dictionary<string, FabricationPreset>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationWorkspaceDialog.LoadAllPresets: {ex.Message}");
                return new Dictionary<string, FabricationPreset>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAllPresets(Dictionary<string, FabricationPreset> presets)
        {
            try
            {
                string path = PresetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(presets, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationWorkspaceDialog.SaveAllPresets", ex);
            }
        }

        private static IEnumerable<string> ListPresetNames()
            => LoadAllPresets().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        private void LoadPreset()
        {
            string name = (_cmbPreset?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            var all = LoadAllPresets();
            if (!all.TryGetValue(name, out var p))
            {
                MessageBox.Show($"Preset '{name}' not found.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            p.ApplyTo();
            HydrateFromOptions();
            RefreshScopeStatus();
        }

        private void SavePreset()
        {
            string name = (_cmbPreset?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Type a preset name first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var all = LoadAllPresets();
            all[name] = FabricationPreset.Capture();
            SaveAllPresets(all);
            RefreshPresetCombo(name);
        }

        private void DeletePreset()
        {
            string name = (_cmbPreset?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            var all = LoadAllPresets();
            if (all.Remove(name))
            {
                SaveAllPresets(all);
                RefreshPresetCombo(null);
            }
        }

        private void RefreshPresetCombo(string select)
        {
            if (_cmbPreset == null) return;
            _cmbPreset.Items.Clear();
            foreach (var name in ListPresetNames()) _cmbPreset.Items.Add(name);
            if (!string.IsNullOrEmpty(select)) _cmbPreset.Text = select; else _cmbPreset.Text = "";
        }

        // ─── Hydrate UI from FabricationOptions ─────────────────────────

        private void HydrateFromOptions()
        {
            if (_rbScopeSel  != null) _rbScopeSel.IsChecked  = FabricationOptions.ScopeSelection;
            if (_rbScopeView != null) _rbScopeView.IsChecked = FabricationOptions.ScopeActiveView;
            if (_rbScopeProj != null) _rbScopeProj.IsChecked = FabricationOptions.ScopeProject;

            if (_chkPipe     != null) _chkPipe.IsChecked     = FabricationOptions.RulePipe;
            if (_chkPipeLB   != null) _chkPipeLB.IsChecked   = FabricationOptions.RulePipeLB;
            if (_chkDuct     != null) _chkDuct.IsChecked     = FabricationOptions.RuleDuct;
            if (_chkDuctPitt != null) _chkDuctPitt.IsChecked = FabricationOptions.RuleDuctPitt;
            if (_chkConduit  != null) _chkConduit.IsChecked  = FabricationOptions.RuleConduit;

            if (_chkAssy    != null) _chkAssy.IsChecked    = FabricationOptions.GenerateAssemblies;
            if (_chkViews   != null) _chkViews.IsChecked   = FabricationOptions.GenerateViews;
            if (_chkSheets  != null) _chkSheets.IsChecked  = FabricationOptions.GenerateSheets;
            if (_chkSymbols != null) _chkSymbols.IsChecked = FabricationOptions.PlaceISO6412Symbols;
            if (_chkCsv     != null) _chkCsv.IsChecked     = FabricationOptions.EmitPerDisciplineCsv;

            if (_rbIso6412 != null) _rbIso6412.IsChecked =  FabricationOptions.ContentModeIso6412;
            if (_rbGeneric != null) _rbGeneric.IsChecked = !FabricationOptions.ContentModeIso6412;
        }

        // ─── Dispatch to the dock-panel command handler ─────────────────

        private void Dispatch(string tag)
        {
            try
            {
                if (!StingDockPanel.DispatchCommand(tag))
                {
                    MessageBox.Show("Could not dispatch command: " + tag +
                        "\n(Open the STING dock panel once so the command pipeline initialises.)",
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationWorkspaceDialog.Dispatch " + tag, ex);
                MessageBox.Show("Could not dispatch command: " + tag,
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  UI PRIMITIVES — Card / inputs / buttons
        // ═══════════════════════════════════════════════════════════════

        private UIElement Card(string title, UIElement body)
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(10, 6, 10, 10),
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(AccentColor),
                Margin = new Thickness(0, 0, 0, 6),
            });
            stack.Children.Add(body);
            outer.Child = stack;
            return outer;
        }

        private CheckBox CheckRow(string label, bool value, Action<bool> setter)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 11,
            };
            cb.Checked   += (s, e) => setter?.Invoke(true);
            cb.Unchecked += (s, e) => setter?.Invoke(false);
            return cb;
        }

        private RadioButton MakeRadio(string label, string group, bool isChecked)
        {
            return new RadioButton
            {
                Content = label,
                GroupName = group,
                IsChecked = isChecked,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(0, 2, 12, 2),
                FontSize = 11,
            };
        }

        private TextBlock LabelInline(string text, double width)
        {
            return new TextBlock
            {
                Text = text,
                Width = width,
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };
        }

        private Button MakeSmallBtn(string label, Action onClick)
        {
            var b = new Button
            {
                Content = label,
                Height = 22,
                MinWidth = 60,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(NeutralBtn),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        private Button MakeAccentBtn(string label, string commandTag)
        {
            var b = new Button
            {
                Content = label,
                Height = 26,
                MinWidth = 80,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(OrangeBtn),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            b.Click += (s, e) => Dispatch(commandTag);
            return b;
        }

        private Button MakeBigBtn(string label, Color bg, bool isDefault, Action onClick)
        {
            var fg = bg == NeutralBtn ? FgColor : Color.FromRgb(0xFF, 0xFF, 0xFF);
            var b = new Button
            {
                Content = label,
                Width = 140,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(fg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                FontWeight = isDefault ? FontWeights.Bold : FontWeights.Normal,
                IsDefault = isDefault,
                IsCancel = !isDefault,
                FontSize = 12,
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRESET DTO — captures + replays the FabricationOptions surface
    // ═══════════════════════════════════════════════════════════════════

    public sealed class FabricationPreset
    {
        public bool ScopeSelection  { get; set; }
        public bool ScopeActiveView { get; set; }
        public bool ScopeProject    { get; set; }

        public bool RulePipe     { get; set; }
        public bool RulePipeLB   { get; set; }
        public bool RuleDuct     { get; set; }
        public bool RuleDuctPitt { get; set; }
        public bool RuleConduit  { get; set; }

        public bool GenerateAssemblies   { get; set; }
        public bool GenerateViews        { get; set; }
        public bool GenerateSheets       { get; set; }
        public bool PlaceISO6412Symbols  { get; set; }
        public bool EmitPerDisciplineCsv { get; set; }

        public bool ContentModeIso6412 { get; set; }

        public static FabricationPreset Capture()
        {
            return new FabricationPreset
            {
                ScopeSelection       = FabricationOptions.ScopeSelection,
                ScopeActiveView      = FabricationOptions.ScopeActiveView,
                ScopeProject         = FabricationOptions.ScopeProject,
                RulePipe             = FabricationOptions.RulePipe,
                RulePipeLB           = FabricationOptions.RulePipeLB,
                RuleDuct             = FabricationOptions.RuleDuct,
                RuleDuctPitt         = FabricationOptions.RuleDuctPitt,
                RuleConduit          = FabricationOptions.RuleConduit,
                GenerateAssemblies   = FabricationOptions.GenerateAssemblies,
                GenerateViews        = FabricationOptions.GenerateViews,
                GenerateSheets       = FabricationOptions.GenerateSheets,
                PlaceISO6412Symbols  = FabricationOptions.PlaceISO6412Symbols,
                EmitPerDisciplineCsv = FabricationOptions.EmitPerDisciplineCsv,
                ContentModeIso6412   = FabricationOptions.ContentModeIso6412,
            };
        }

        public void ApplyTo()
        {
            FabricationOptions.ScopeSelection       = ScopeSelection;
            FabricationOptions.ScopeActiveView      = ScopeActiveView;
            FabricationOptions.ScopeProject         = ScopeProject;
            FabricationOptions.RulePipe             = RulePipe;
            FabricationOptions.RulePipeLB           = RulePipeLB;
            FabricationOptions.RuleDuct             = RuleDuct;
            FabricationOptions.RuleDuctPitt         = RuleDuctPitt;
            FabricationOptions.RuleConduit          = RuleConduit;
            FabricationOptions.GenerateAssemblies   = GenerateAssemblies;
            FabricationOptions.GenerateViews        = GenerateViews;
            FabricationOptions.GenerateSheets       = GenerateSheets;
            FabricationOptions.PlaceISO6412Symbols  = PlaceISO6412Symbols;
            FabricationOptions.EmitPerDisciplineCsv = EmitPerDisciplineCsv;
            FabricationOptions.ContentModeIso6412   = ContentModeIso6412;
        }
    }
}
