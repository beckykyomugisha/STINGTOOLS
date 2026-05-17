// StingTools v4 MVP — Fabrication Workspace dialog (light-themed, rich).
//
// Replaces the old single-pane "Configure" dialog with the full
// workspace from the spec: PRESET row, SCOPE row with live category
// breakdown + per-discipline summary + system / level filters, a
// tabbed body (Package / Cut list / Weld map / Isometrics / PCF /
// MAJ) and a footer strip wiring every fabrication export.
//
// All state still flows through StingTools.Commands.Fabrication.
// FabricationOptions; every action button raises the matching
// Fabrication_* tag through StingDockPanel.DispatchCommand so the
// existing IExternalCommand pipeline is reused without duplication.
//
// Light palette only — DarkDialogTheme.LightPalette throughout.

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
        // ── palette (light, contrast-safe) ──
        private static readonly Color BgColor      = Color.FromRgb(0xFA, 0xFA, 0xFA);
        private static readonly Color AccentColor  = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CardBg       = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CardBorder   = Color.FromRgb(0xCF, 0xD8, 0xDC);
        private static readonly Color FgColor      = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color SubtleColor  = Color.FromRgb(0x66, 0x66, 0x66);
        private static readonly Color GreenBtn     = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color OrangeBtn    = Color.FromRgb(0xFF, 0x98, 0x00);
        private static readonly Color NeutralBtn   = Color.FromRgb(0xE8, 0xE8, 0xE8);
        private static readonly Color AltRowBg     = Color.FromRgb(0xF5, 0xF7, 0xF9);

        // ── state ──
        private readonly Document _doc;

        // PRESET row
        private ComboBox _cmbPreset;

        // SCOPE row
        private RadioButton _rbScopeSel, _rbScopeView, _rbScopeProj;
        private TextBlock   _txtScopeStatus;

        // CATEGORY breakdown
        private StackPanel _categoryListHost;
        private TextBlock  _txtDisciplineSummary;
        private readonly Dictionary<BuiltInCategory, CategoryRow> _catRows
            = new Dictionary<BuiltInCategory, CategoryRow>();

        // FILTER (system / level text)
        private TextBox _txtSystemFilter, _txtLevelFilter;

        // Rules / Output / Content mode are configured via the dock-panel
        // Fabrication tab and read by the engine directly off
        // FabricationOptions — the workspace dialog deliberately does not
        // mirror them here so the layout matches the spec mockup.

        // Tabs
        private TabControl _tabs;
        private StackPanel _packageGridHost;
        private TextBlock  _packageHeader;

        // Title block strip
        private TextBlock _txtShopDrawingStatus;

        // ── MEP categories — single source of truth ──
        private static readonly (BuiltInCategory bic, string label, string disc)[] MepCategories =
        {
            (BuiltInCategory.OST_PipeCurves,        "Pipes",              "Pipe"),
            (BuiltInCategory.OST_FlexPipeCurves,    "Flex pipes",         "Pipe"),
            (BuiltInCategory.OST_PipeFitting,       "Pipe fittings",      "Pipe"),
            (BuiltInCategory.OST_PipeAccessory,     "Pipe accessories",   "Pipe"),
            (BuiltInCategory.OST_DuctCurves,        "Ducts",              "Duct"),
            (BuiltInCategory.OST_FlexDuctCurves,    "Flex ducts",         "Duct"),
            (BuiltInCategory.OST_DuctFitting,       "Duct fittings",      "Duct"),
            (BuiltInCategory.OST_DuctAccessory,     "Duct accessories",   "Duct"),
            (BuiltInCategory.OST_Conduit,           "Conduit",            "Electrical"),
            (BuiltInCategory.OST_ConduitFitting,    "Conduit fittings",   "Electrical"),
            (BuiltInCategory.OST_CableTray,         "Cable trays",        "Electrical"),
            (BuiltInCategory.OST_CableTrayFitting,  "Tray fittings",      "Electrical"),
        };

        public FabricationWorkspaceDialog(Document doc)
        {
            _doc = doc;

            Title = "STING v4 — Fabrication Workspace";
            Width = 1100; Height = 780;
            MinWidth = 920; MinHeight = 600;
            Background  = new SolidColorBrush(BgColor);
            Foreground  = new SolidColorBrush(FgColor);
            FontFamily  = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Force-reset inherited theme resources so the workspace
            // always renders light, even when the host dock panel is
            // running a dark or non-default theme.
            Resources["PrimaryBg"]   = new SolidColorBrush(BgColor);
            Resources["SecondaryBg"] = new SolidColorBrush(CardBg);
            Resources["AccentBrush"] = new SolidColorBrush(AccentColor);
            Resources["BorderColor"] = new SolidColorBrush(CardBorder);
            Resources["ButtonBg"]    = new SolidColorBrush(NeutralBtn);
            Resources["ButtonFg"]    = new SolidColorBrush(FgColor);
            DarkDialogTheme.ApplyComboBoxFix(this, CardBg, FgColor, CardBorder);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            Content = BuildLayout();
            HydrateFromOptions();
            RefreshScopeAndCategories();
            RefreshShopDrawingStatus();
            RefreshPackagePreview();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYOUT — top header strip + tab body + footer
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12), LastChildFill = true };

            var header   = BuildHeaderStrip();    DockPanel.SetDock(header,   Dock.Top);    root.Children.Add(header);
            var scope    = BuildScopeStrip();     DockPanel.SetDock(scope,    Dock.Top);    root.Children.Add(scope);
            var cats     = BuildCategoryCard();   DockPanel.SetDock(cats,     Dock.Top);    root.Children.Add(cats);
            var filters  = BuildFiltersCard();    DockPanel.SetDock(filters,  Dock.Top);    root.Children.Add(filters);
            var footer   = BuildFooter();         DockPanel.SetDock(footer,   Dock.Bottom); root.Children.Add(footer);
            var shop     = BuildShopDrawingStrip();DockPanel.SetDock(shop,    Dock.Bottom); root.Children.Add(shop);
            root.Children.Add(BuildTabBody()); // last child fills remainder

            return root;
        }

        // ─── Top: PRESET row ────────────────────────────────────────────

        private UIElement BuildHeaderStrip()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = "PRESET",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            _cmbPreset = new ComboBox { Height = 24, IsEditable = true, Margin = new Thickness(0, 0, 8, 0) };
            DarkDialogTheme.StyleInput(_cmbPreset, CardBg, FgColor, CardBorder);
            foreach (var name in ListPresetNames()) _cmbPreset.Items.Add(name);
            Grid.SetColumn(_cmbPreset, 1);
            grid.Children.Add(_cmbPreset);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(MakeSmallBtn("Load",   () => LoadPreset()));
            actions.Children.Add(MakeSmallBtn("Save…",  () => SavePreset()));
            actions.Children.Add(MakeSmallBtn("Delete", () => DeletePreset()));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            return grid;
        }

        // ─── SCOPE row ──────────────────────────────────────────────────

        private UIElement BuildScopeStrip()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = "SCOPE",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 0); Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            var radios = new StackPanel { Orientation = Orientation.Horizontal };
            _rbScopeSel  = MakeRadio("Selection",   "FabScope", FabricationOptions.ScopeSelection);
            _rbScopeView = MakeRadio("Active view", "FabScope", FabricationOptions.ScopeActiveView);
            _rbScopeProj = MakeRadio("Project",     "FabScope", FabricationOptions.ScopeProject);
            _rbScopeSel.Checked  += (s, e) => { CommitScope(); RefreshScopeAndCategories(); RefreshPackagePreview(); };
            _rbScopeView.Checked += (s, e) => { CommitScope(); RefreshScopeAndCategories(); RefreshPackagePreview(); };
            _rbScopeProj.Checked += (s, e) => { CommitScope(); RefreshScopeAndCategories(); RefreshPackagePreview(); };
            radios.Children.Add(_rbScopeSel);
            radios.Children.Add(_rbScopeView);
            radios.Children.Add(_rbScopeProj);
            Grid.SetColumn(radios, 1); Grid.SetRow(radios, 0);
            grid.Children.Add(radios);

            var refresh = MakeSmallBtn("Refresh", () => { RefreshScopeAndCategories(); RefreshPackagePreview(); });
            Grid.SetColumn(refresh, 2); Grid.SetRow(refresh, 0);
            grid.Children.Add(refresh);

            _txtScopeStatus = new TextBlock
            {
                Text = "Scope: …",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(_txtScopeStatus, 0); Grid.SetRow(_txtScopeStatus, 1); Grid.SetColumnSpan(_txtScopeStatus, 4);
            grid.Children.Add(_txtScopeStatus);

            return grid;
        }

        // ─── CATEGORY breakdown card — checkbox + count rows ────────────

        private UIElement BuildCategoryCard()
        {
            var body = new StackPanel();

            _categoryListHost = new StackPanel();
            body.Children.Add(_categoryListHost);

            _txtDisciplineSummary = new TextBlock
            {
                Text = "By discipline: …",
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4),
            };
            body.Children.Add(_txtDisciplineSummary);

            // Build one row per category — populated/refreshed in
            // RefreshScopeAndCategories.
            foreach (var info in MepCategories)
            {
                var row = new CategoryRow(info.bic, info.label, info.disc,
                    FgColor, AccentColor, CardBorder,
                    onToggle: () => RefreshPackagePreview());
                _catRows[info.bic] = row;
                _categoryListHost.Children.Add(row.RowElement);
            }

            return Card("Categories", body);
        }

        // ─── FILTERS — single inline System / Level row ─────────────────

        private UIElement BuildFiltersCard()
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lblSys = new TextBlock
            {
                Text = "System:",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lblSys, 0); Grid.SetRow(lblSys, 0);
            grid.Children.Add(lblSys);

            _txtSystemFilter = new TextBox { Height = 22, Margin = new Thickness(0, 0, 12, 0) };
            DarkDialogTheme.StyleInput(_txtSystemFilter, CardBg, FgColor, CardBorder);
            _txtSystemFilter.LostFocus += (s, e) => RefreshPackagePreview();
            Grid.SetColumn(_txtSystemFilter, 1); Grid.SetRow(_txtSystemFilter, 0);
            grid.Children.Add(_txtSystemFilter);

            var lblLvl = new TextBlock
            {
                Text = "Level:",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lblLvl, 2); Grid.SetRow(lblLvl, 0);
            grid.Children.Add(lblLvl);

            _txtLevelFilter = new TextBox { Height = 22 };
            DarkDialogTheme.StyleInput(_txtLevelFilter, CardBg, FgColor, CardBorder);
            _txtLevelFilter.LostFocus += (s, e) => RefreshPackagePreview();
            Grid.SetColumn(_txtLevelFilter, 3); Grid.SetRow(_txtLevelFilter, 0);
            grid.Children.Add(_txtLevelFilter);

            return grid;
        }

        // ─── TAB body — Package / Cut list / Weld map / Iso / PCF / MAJ ─

        private UIElement BuildTabBody()
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(CardBg),
                Margin = new Thickness(0, 4, 0, 4),
            };
            _tabs = new TabControl
            {
                Background = new SolidColorBrush(CardBg),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
            };
            _tabs.Items.Add(BuildPackageTab());
            _tabs.Items.Add(BuildExportTab("Cut list",   "Fabrication_ExportCutList",
                "Re-emits STING_v4_pipe_cut_list.csv. Includes element id, system, size, length and material per pipe."));
            _tabs.Items.Add(BuildExportTab("Weld map",   "Fabrication_ExportWeldMap",
                "Re-emits STING_v4_weld_map.csv. Lists every weld with face id, joint type and discipline."));
            _tabs.Items.Add(BuildExportTab("Isometrics", "Fabrication_ExportIsometrics",
                "Indexes SP-… isometric sheets to STING_v4_isometric_index.csv for register intake."));
            _tabs.Items.Add(BuildExportTab("PCF",        "Fabrication_ExportPcf",
                "Exports SpoolGen-compatible PCF to STING_v4_export.pcf — pipe component file."));
            _tabs.Items.Add(BuildExportTab("MAJ",        "Fabrication_ExportMaj",
                "Exports MAJiC manufacturing-job XML for shop floor handover."));
            border.Child = _tabs;
            return border;
        }

        private TabItem BuildPackageTab()
        {
            var tab = new TabItem { Header = "Package" };

            var dock = new DockPanel { LastChildFill = true };

            // Toolbar — Tick all / Untick all / Smart group / Clash pre-flight / Undo
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(toolbar, Dock.Top);
            _packageHeader = new TextBlock
            {
                Text = "0 assemblies will be created",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 11,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            toolbar.Children.Add(_packageHeader);
            toolbar.Children.Add(MakeSmallBtn("Tick all",          () => TickAllAssemblies(true)));
            toolbar.Children.Add(MakeSmallBtn("Untick all",        () => TickAllAssemblies(false)));
            toolbar.Children.Add(MakeSmallBtn("Smart group",       () => Dispatch("Fabrication_SmartGroup")));
            toolbar.Children.Add(MakeSmallBtn("Clash pre-flight",  () => Dispatch("Fabrication_ClashPreflight")));
            toolbar.Children.Add(MakeSmallBtn("Undo last run",     () => Dispatch("Fabrication_UndoPackage")));
            dock.Children.Add(toolbar);

            // Assembly preview grid
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(CardBg),
            };
            _packageGridHost = new StackPanel();

            // Header row
            var hdr = new Grid();
            DefineAssemblyColumns(hdr);
            AddCell(hdr, 0, "✓",          accent: true);
            AddCell(hdr, 1, "DISC",       accent: true);
            AddCell(hdr, 2, "SYS",        accent: true);
            AddCell(hdr, 3, "LVL",        accent: true);
            AddCell(hdr, 4, "Count",      accent: true, alignRight: true);
            AddCell(hdr, 5, "Spool name", accent: true);
            _packageGridHost.Children.Add(hdr);

            scroll.Content = _packageGridHost;
            dock.Children.Add(scroll);

            tab.Content = dock;
            return tab;
        }

        private TabItem BuildExportTab(string header, string commandTag, string description)
        {
            var tab = new TabItem { Header = header };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Click Run to dispatch this export against the current scope.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var btn = MakeAccentBtn("Run " + header, commandTag);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(btn);
            tab.Content = stack;
            return tab;
        }

        private void DefineAssemblyColumns(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        private void AddCell(Grid g, int col, string text, bool accent = false, bool alignRight = false)
        {
            var t = new TextBlock
            {
                Text = text ?? "",
                Foreground = new SolidColorBrush(accent ? AccentColor : FgColor),
                FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = accent ? 11 : 11,
                Margin = new Thickness(4, 4, 8, 4),
                TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left,
            };
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        // ─── SHOP-DRAWING strip ─────────────────────────────────────────

        private UIElement BuildShopDrawingStrip()
        {
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 4) };

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(actions, Dock.Right);
            actions.Children.Add(MakeSmallBtn("Configure…", ConfigureShopDrawing));
            actions.Children.Add(MakeSmallBtn("Clear",      ClearShopDrawing));
            dock.Children.Add(actions);

            _txtShopDrawingStatus = new TextBlock
            {
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            dock.Children.Add(_txtShopDrawingStatus);

            return Card("Title block & view template", dock);
        }

        // ─── FOOTER — every action surfaced ─────────────────────────────
        //
        // Three rows now, all light-themed:
        //   1) Generate package (green primary) + main exports each
        //      paired with a format dropdown (CSV / TXT / JSON for cut
        //      list + weld map; CSV index / PDF for iso index).
        //   2) Secondary exports + Doc Register link.
        //   3) Utility row: Settings…, Refresh, View log, Open last
        //      sheet, Help, Close.

        private ComboBox _cmbCutListFormat;
        private ComboBox _cmbWeldMapFormat;
        private ComboBox _cmbIsoIndexFormat;

        private UIElement BuildFooter()
        {
            var outer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            // ── Row 1: Generate + main exports with format combos ──
            var row1 = new Grid();
            for (int i = 0; i < 4; i++)
                row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row1.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddFooterCell(row1, 0, MakeFooterBtn("Generate package", GreenBtn,   true,
                () => Dispatch("Fabrication_GeneratePackage")), null);
            _cmbCutListFormat = MakeFormatCombo(new[] { "CSV", "TXT", "JSON" }, "CSV");
            AddFooterCell(row1, 1, MakeFooterBtn("Export cut list",  NeutralBtn, false,
                () => Dispatch("Fabrication_ExportCutList")),    _cmbCutListFormat);
            _cmbWeldMapFormat = MakeFormatCombo(new[] { "CSV", "TXT", "JSON" }, "CSV");
            AddFooterCell(row1, 2, MakeFooterBtn("Export weld map",  OrangeBtn,  false,
                () => Dispatch("Fabrication_ExportWeldMap")),    _cmbWeldMapFormat);
            _cmbIsoIndexFormat = MakeFormatCombo(new[] { "CSV index", "PDF index" }, "CSV index");
            AddFooterCell(row1, 3, MakeFooterBtn("Export iso index", NeutralBtn, false,
                () => Dispatch("Fabrication_ExportIsometrics")), _cmbIsoIndexFormat);
            outer.Children.Add(row1);

            // ── Row 2: secondary exports ──
            var row2 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            row2.Children.Add(MakeFooterBtn("Incremental rebuild", NeutralBtn, false, () => Dispatch("Fabrication_IncrementalRebuild")));
            row2.Children.Add(MakeFooterBtn("BOM roll-up (XLSX)",  NeutralBtn, false, () => Dispatch("Fabrication_BomRollup")));
            row2.Children.Add(MakeFooterBtn("Export PCF",          NeutralBtn, false, () => Dispatch("Fabrication_ExportPcf")));
            row2.Children.Add(MakeFooterBtn("Export MAJ",          NeutralBtn, false, () => Dispatch("Fabrication_ExportMaj")));
            row2.Children.Add(MakeFooterBtn("Link → Doc Register", NeutralBtn, false, () => Dispatch("Fabrication_LinkDocRegister")));
            outer.Children.Add(row2);

            // ── Row 3: utility row ──
            var row3 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            row3.Children.Add(MakeFooterBtn("Settings…",        NeutralBtn, false, OpenSettingsPopup));
            row3.Children.Add(MakeFooterBtn("Refresh preview",  NeutralBtn, false, () => { RefreshScopeAndCategories(); RefreshPackagePreview(); }));
            row3.Children.Add(MakeFooterBtn("Open last sheet",  NeutralBtn, false, OpenLastGeneratedSheet));
            row3.Children.Add(MakeFooterBtn("View log",         NeutralBtn, false, OpenStingLog));
            row3.Children.Add(MakeFooterBtn("Help",             NeutralBtn, false, OpenHelp));
            row3.Children.Add(MakeFooterBtn("Close",            NeutralBtn, false, () => { DialogResult = false; }));
            outer.Children.Add(row3);

            return outer;
        }

        private static void AddFooterCell(Grid g, int col, UIElement btn, UIElement combo)
        {
            Grid.SetColumn(btn, col); Grid.SetRow(btn, 0); g.Children.Add(btn);
            if (combo != null)
            {
                Grid.SetColumn(combo, col); Grid.SetRow(combo, 1);
                g.Children.Add(combo);
            }
        }

        private ComboBox MakeFormatCombo(string[] values, string defaultValue)
        {
            var cb = new ComboBox
            {
                Height = 22,
                Margin = new Thickness(0, 2, 6, 0),
                FontSize = 10,
            };
            DarkDialogTheme.StyleInput(cb, CardBg, FgColor, CardBorder);
            foreach (var v in values) cb.Items.Add(v);
            cb.Text = defaultValue;
            cb.SelectedIndex = 0;
            return cb;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SCOPE / CATEGORY refresh
        // ═══════════════════════════════════════════════════════════════

        private void CommitScope()
        {
            FabricationOptions.ScopeSelection  = _rbScopeSel?.IsChecked  == true;
            FabricationOptions.ScopeActiveView = _rbScopeView?.IsChecked == true;
            FabricationOptions.ScopeProject    = _rbScopeProj?.IsChecked == true;
        }

        /// <summary>
        /// Recompute element counts per category and per discipline,
        /// honouring the selected scope (with auto-fallback hint).
        /// </summary>
        private void RefreshScopeAndCategories()
        {
            try
            {
                var ids = CollectScopeIds(out var scopeLabel, out var fellBack);

                // Per-category counts (within the resolved id set).
                var catCounts = new Dictionary<BuiltInCategory, int>();
                foreach (var info in MepCategories) catCounts[info.bic] = 0;
                if (_doc != null)
                {
                    foreach (var id in ids)
                    {
                        var el = _doc.GetElement(id);
                        if (el?.Category == null) continue;
                        var bic = (BuiltInCategory)(int)el.Category.Id.Value;
                        if (catCounts.ContainsKey(bic)) catCounts[bic]++;
                    }
                }

                // Apply to UI rows + collect discipline rollup.
                var byDisc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var info in MepCategories)
                {
                    int n = catCounts[info.bic];
                    if (_catRows.TryGetValue(info.bic, out var row)) row.SetCount(n);
                    if (n == 0) continue;
                    byDisc.TryGetValue(info.disc, out var sub);
                    byDisc[info.disc] = sub + n;
                }

                int total = ids.Count;
                if (_txtScopeStatus != null)
                {
                    _txtScopeStatus.Text = fellBack
                        ? $"Scope: {scopeLabel} — {total} MEP element(s) in scope."
                        : $"Scope: {scopeLabel} — {total} MEP element(s) in scope.";
                }
                if (_txtDisciplineSummary != null)
                {
                    _txtDisciplineSummary.Text = byDisc.Count == 0
                        ? "By discipline: (none)"
                        : "By discipline: " + string.Join(" · ",
                            byDisc.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} = {kv.Value}"));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationWorkspaceDialog.RefreshScopeAndCategories: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve element ids honouring scope with the same auto-fallback
        /// chain used by GenerateFabPackageCommand. Returns a friendly
        /// scope label and a flag indicating whether the dialog had to
        /// fall through to the next scope tier.
        /// </summary>
        private List<ElementId> CollectScopeIds(out string scopeLabel, out bool fellBack)
        {
            scopeLabel = "selection"; fellBack = false;
            var ids = new List<ElementId>();
            if (_doc == null) return ids;

            var bics = MepCategories.Select(c => c.bic).ToArray();
            var filter = new ElementMulticategoryFilter(bics);

            try
            {
                if (FabricationOptions.ScopeProject)
                {
                    scopeLabel = "project";
                    foreach (var e in new FilteredElementCollector(_doc)
                        .WherePasses(filter).WhereElementIsNotElementType()) ids.Add(e.Id);
                    return ids;
                }
                if (FabricationOptions.ScopeActiveView)
                {
                    scopeLabel = "active view";
                    var view = _doc.ActiveView;
                    if (view == null || view is ViewSheet) return ids;
                    foreach (var e in new FilteredElementCollector(_doc, view.Id)
                        .WherePasses(filter).WhereElementIsNotElementType()) ids.Add(e.Id);
                    return ids;
                }

                // Selection scope → mirror the dock-panel's current selection
                // when accessible, otherwise fall back to active view.
                var uidoc = StingTools.UI.StingCommandHandler.CurrentApp?.ActiveUIDocument;
                if (uidoc != null)
                {
                    var sel = uidoc.Selection?.GetElementIds();
                    if (sel != null)
                    {
                        foreach (var id in sel)
                        {
                            var el = _doc.GetElement(id);
                            if (el?.Category == null) continue;
                            var bic = (BuiltInCategory)(int)el.Category.Id.Value;
                            if (bics.Contains(bic)) ids.Add(id);
                        }
                    }
                }
                if (ids.Count > 0) { scopeLabel = "selection"; return ids; }

                // Auto-fallback: active view (skip sheet views — the
                // collector throws on those).
                fellBack = true;
                var v2 = _doc.ActiveView;
                if (v2 != null && !(v2 is ViewSheet))
                {
                    foreach (var e in new FilteredElementCollector(_doc, v2.Id)
                        .WherePasses(filter).WhereElementIsNotElementType()) ids.Add(e.Id);
                }
                if (ids.Count > 0) { scopeLabel = "active view (auto-fallback from empty selection)"; return ids; }

                // Auto-fallback: project.
                foreach (var e in new FilteredElementCollector(_doc)
                    .WherePasses(filter).WhereElementIsNotElementType()) ids.Add(e.Id);
                scopeLabel = "project (auto-fallback from empty selection)";
            }
            catch (Exception ex) { StingLog.Warn($"FabricationWorkspaceDialog.CollectScopeIds: {ex.Message}"); }
            return ids;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PACKAGE preview — predicts the assemblies that will be created
        // ═══════════════════════════════════════════════════════════════

        private readonly List<AssemblyPreviewRow> _previewRows = new List<AssemblyPreviewRow>();

        private void RefreshPackagePreview()
        {
            try
            {
                if (_packageGridHost == null) return;
                _previewRows.Clear();

                // Strip body rows (keep header).
                while (_packageGridHost.Children.Count > 1)
                    _packageGridHost.Children.RemoveAt(1);

                if (_doc == null)
                {
                    _packageHeader.Text = "(no active document)";
                    return;
                }

                var ids = CollectScopeIds(out _, out _);

                // Build (DISC, SYS, LVL) groups using the engine's discipline
                // mapping. Categories that are unticked in the rules card
                // are excluded so the preview tracks what GeneratePackage
                // would actually emit.
                var groups = new Dictionary<(string disc, string sys, string lvl), int>();
                string sysFilter = (_txtSystemFilter?.Text ?? "").Trim();
                string lvlFilter = (_txtLevelFilter?.Text  ?? "").Trim();

                foreach (var id in ids)
                {
                    var el = _doc.GetElement(id);
                    if (el?.Category == null) continue;
                    var bic = (BuiltInCategory)(int)el.Category.Id.Value;
                    // Skip categories the user unticked in the breakdown.
                    if (_catRows.TryGetValue(bic, out var crow) && !crow.IsChecked) continue;
                    string disc = DisciplineFor(el);
                    if (!RuleEnabledFor(disc)) continue;
                    string sys = ParameterHelpers.GetString(el, "PLM_SYS_TXT");
                    if (string.IsNullOrWhiteSpace(sys)) sys = ParameterHelpers.GetString(el, "HVC_SYS_TXT");
                    if (string.IsNullOrWhiteSpace(sys)) sys = ParameterHelpers.GetString(el, "ELC_SYS_TXT");
                    if (string.IsNullOrWhiteSpace(sys)) sys = "GEN";
                    string lvl = ParameterHelpers.GetLevelCode(_doc, el);
                    if (string.IsNullOrWhiteSpace(lvl)) lvl = "XX";
                    if (!string.IsNullOrEmpty(sysFilter) && sys.IndexOf(sysFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!string.IsNullOrEmpty(lvlFilter) && lvl.IndexOf(lvlFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var key = (disc, sys, lvl);
                    groups.TryGetValue(key, out var n);
                    groups[key] = n + 1;
                }

                int seq = 1;
                foreach (var kv in groups.OrderBy(g => g.Key.disc).ThenBy(g => g.Key.sys).ThenBy(g => g.Key.lvl))
                {
                    var row = new AssemblyPreviewRow
                    {
                        Selected = true,
                        Disc = ShortDiscCode(kv.Key.disc),
                        System = kv.Key.sys,
                        Level = kv.Key.lvl,
                        Count = kv.Value,
                        SpoolName = $"SP-{ShortDiscCode(kv.Key.disc)}-{kv.Key.sys}-{kv.Key.lvl}-{seq:D4}",
                    };
                    _previewRows.Add(row);
                    _packageGridHost.Children.Add(BuildPreviewRow(row));
                    seq++;
                }

                int active = _previewRows.Count(r => r.Selected);
                _packageHeader.Text = $"{active} assembly(ies) will be created (of {_previewRows.Count} group(s))";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationWorkspaceDialog.RefreshPackagePreview: {ex.Message}");
            }
        }

        private UIElement BuildPreviewRow(AssemblyPreviewRow row)
        {
            var g = new Grid
            {
                Background = new SolidColorBrush((_packageGridHost.Children.Count % 2 == 0) ? AltRowBg : CardBg),
            };
            DefineAssemblyColumns(g);

            var cb = new CheckBox
            {
                IsChecked = row.Selected,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2),
            };
            cb.Checked   += (s, e) => { row.Selected = true;  UpdatePackageHeader(); };
            cb.Unchecked += (s, e) => { row.Selected = false; UpdatePackageHeader(); };
            Grid.SetColumn(cb, 0); g.Children.Add(cb);
            AddCell(g, 1, row.Disc);
            AddCell(g, 2, row.System);
            AddCell(g, 3, row.Level);
            AddCell(g, 4, row.Count.ToString(), alignRight: true);
            AddCell(g, 5, row.SpoolName);
            return g;
        }

        private void TickAllAssemblies(bool selected)
        {
            foreach (var row in _previewRows) row.Selected = selected;
            // Re-render so the checkboxes reflect the new state.
            RefreshPackagePreview();
        }

        private void UpdatePackageHeader()
        {
            if (_packageHeader == null) return;
            int active = _previewRows.Count(r => r.Selected);
            _packageHeader.Text = $"{active} assembly(ies) will be created (of {_previewRows.Count} group(s))";
        }

        private static string DisciplineFor(Element el)
        {
            if (el?.Category == null) return "";
            int bic = (int)el.Category.Id.Value;
            switch ((BuiltInCategory)bic)
            {
                case BuiltInCategory.OST_PipeCurves:
                case BuiltInCategory.OST_FlexPipeCurves:
                case BuiltInCategory.OST_PipeFitting:
                case BuiltInCategory.OST_PipeAccessory:
                    return "Pipe";
                case BuiltInCategory.OST_DuctCurves:
                case BuiltInCategory.OST_FlexDuctCurves:
                case BuiltInCategory.OST_DuctFitting:
                case BuiltInCategory.OST_DuctAccessory:
                    return "Duct";
                case BuiltInCategory.OST_Conduit:
                case BuiltInCategory.OST_ConduitFitting:
                case BuiltInCategory.OST_CableTray:
                case BuiltInCategory.OST_CableTrayFitting:
                    return "Electrical";
            }
            return "";
        }

        private static string ShortDiscCode(string disc)
        {
            if (string.IsNullOrEmpty(disc)) return "X";
            switch (disc)
            {
                case "Pipe":       return "P";
                case "Duct":       return "D";
                case "Electrical": return "E";
            }
            return disc.Substring(0, 1).ToUpperInvariant();
        }

        private static bool RuleEnabledFor(string disc)
        {
            switch (disc)
            {
                case "Pipe":       return FabricationOptions.RulePipe || FabricationOptions.RulePipeLB;
                case "Duct":       return FabricationOptions.RuleDuct || FabricationOptions.RuleDuctPitt;
                case "Electrical": return FabricationOptions.RuleConduit;
            }
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  SHOP-DRAWING actions
        // ═══════════════════════════════════════════════════════════════

        private void ConfigureShopDrawing()
        {
            if (_doc == null) return;
            try
            {
                var dlg = new ShopDrawingOptionsDialog(_doc);
                try { dlg.Owner = this; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
                    "Auto-resolved per discipline (STING_TB_ASSEMBLY_*). " +
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
            string s = $"Title block: {tb}  ·  View template: {vt}";
            if (!string.IsNullOrWhiteSpace(opts.SheetNumberPattern))
                s += $"  ·  Sheet #: {opts.SheetNumberPattern}";
            if (!string.IsNullOrWhiteSpace(opts.SheetNamePattern))
                s += $"  ·  Sheet name: {opts.SheetNamePattern}";
            _txtShopDrawingStatus.Text = s;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRESET persistence
        // ═══════════════════════════════════════════════════════════════

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
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(presets, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Error("FabricationWorkspaceDialog.SaveAllPresets", ex); }
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
            RefreshScopeAndCategories();
            RefreshPackagePreview();
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
            _cmbPreset.Text = string.IsNullOrEmpty(select) ? "" : select;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Hydrate UI from FabricationOptions
        // ═══════════════════════════════════════════════════════════════

        private void HydrateFromOptions()
        {
            // Workspace mirrors the SCOPE radios only — the rules /
            // output / content-mode toggles are read by the engine
            // directly off FabricationOptions and configured via the
            // dock panel.
            if (_rbScopeSel  != null) _rbScopeSel.IsChecked  = FabricationOptions.ScopeSelection;
            if (_rbScopeView != null) _rbScopeView.IsChecked = FabricationOptions.ScopeActiveView;
            if (_rbScopeProj != null) _rbScopeProj.IsChecked = FabricationOptions.ScopeProject;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Dispatch
        // ═══════════════════════════════════════════════════════════════

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
        //  UI primitives
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
                MinWidth = 100,
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
                MinWidth = 130,
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(fg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                FontWeight = isDefault ? FontWeights.Bold : FontWeights.Normal,
                IsDefault = isDefault,
                FontSize = 12,
                Padding = new Thickness(8, 0, 8, 0),
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        /// <summary>
        /// Footer button: stretches to fill its grid cell so the
        /// row 1 buttons line up perfectly with the format combos
        /// underneath them.
        /// </summary>
        private Button MakeFooterBtn(string label, Color bg, bool isDefault, Action onClick)
        {
            var fg = bg == NeutralBtn ? FgColor : Color.FromRgb(0xFF, 0xFF, 0xFF);
            var b = new Button
            {
                Content = label,
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(fg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                FontWeight = isDefault ? FontWeights.Bold : FontWeights.Normal,
                IsDefault = isDefault,
                FontSize = 12,
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        // ═══════════════════════════════════════════════════════════════
        //  UTILITY ACTIONS — surfaced on row 3 of the footer
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Open a small popup mirroring the dock-panel Rules / Output /
        /// Content-mode controls so the user doesn't have to leave the
        /// workspace to flip them. State still flows through
        /// FabricationOptions.
        /// </summary>
        private void OpenSettingsPopup()
        {
            var dlg = new Window
            {
                Title = "STING v4 — Fabrication Settings",
                Width = 440, Height = 540,
                Background = new SolidColorBrush(BgColor),
                Foreground = new SolidColorBrush(FgColor),
                FontFamily = new FontFamily("Segoe UI"),
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
            };
            DarkDialogTheme.ApplyComboBoxFix(dlg, CardBg, FgColor, CardBorder);

            var stack = new StackPanel { Margin = new Thickness(12) };

            // Rules
            var rulesBody = new StackPanel();
            rulesBody.Children.Add(CheckRow("Pipe (max 6 m / 4 bends)",                FabricationOptions.RulePipe,     v => FabricationOptions.RulePipe     = v));
            rulesBody.Children.Add(CheckRow("Pipe large bore (max 3 m)",               FabricationOptions.RulePipeLB,   v => FabricationOptions.RulePipeLB   = v));
            rulesBody.Children.Add(CheckRow("Duct TDF (max 3 m / 3 bends)",            FabricationOptions.RuleDuct,     v => FabricationOptions.RuleDuct     = v));
            rulesBody.Children.Add(CheckRow("Duct Pittsburgh (max 2.4 m)",             FabricationOptions.RuleDuctPitt, v => FabricationOptions.RuleDuctPitt = v));
            rulesBody.Children.Add(CheckRow("Conduit (max 6 m / 3 bends, BS 7671)",    FabricationOptions.RuleConduit,  v => FabricationOptions.RuleConduit  = v));
            stack.Children.Add(Card("Discipline rules", rulesBody));

            // Output
            var outBody = new StackPanel();
            outBody.Children.Add(CheckRow("Generate AssemblyInstances",  FabricationOptions.GenerateAssemblies,  v => FabricationOptions.GenerateAssemblies  = v));
            outBody.Children.Add(CheckRow("Generate views (5 + BOM)",    FabricationOptions.GenerateViews,       v => FabricationOptions.GenerateViews       = v));
            outBody.Children.Add(CheckRow("Generate shop drawing sheets",FabricationOptions.GenerateSheets,      v => FabricationOptions.GenerateSheets      = v));
            outBody.Children.Add(CheckRow("Place ISO 6412 symbols",      FabricationOptions.PlaceISO6412Symbols, v => FabricationOptions.PlaceISO6412Symbols = v));
            outBody.Children.Add(CheckRow("Emit per-discipline CSVs",    FabricationOptions.EmitPerDisciplineCsv,v => FabricationOptions.EmitPerDisciplineCsv= v));
            stack.Children.Add(Card("Output", outBody));

            // Content mode
            var contentBody = new StackPanel { Orientation = Orientation.Horizontal };
            var rbIso = MakeRadio("ISO 6412 (workshop)",     "FabSettingsContent",  FabricationOptions.ContentModeIso6412);
            var rbGen = MakeRadio("Generic (geometry only)", "FabSettingsContent", !FabricationOptions.ContentModeIso6412);
            rbIso.Checked += (s, e) => FabricationOptions.ContentModeIso6412 = true;
            rbGen.Checked += (s, e) => FabricationOptions.ContentModeIso6412 = false;
            contentBody.Children.Add(rbIso);
            contentBody.Children.Add(rbGen);
            stack.Children.Add(Card("Content mode", contentBody));

            // Close
            var closeRow = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            closeRow.Children.Add(MakeBigBtn("Done", AccentColor, true, () => dlg.Close()));
            stack.Children.Add(closeRow);

            dlg.Content = stack;
            dlg.ShowDialog();
            RefreshPackagePreview();
        }

        /// <summary>
        /// Open the most recently generated shop drawing sheet from the
        /// FabricationUndoManager session history. Falls back to a hint
        /// when the session has no successful run yet.
        /// </summary>
        private void OpenLastGeneratedSheet()
        {
            try
            {
                var last = FabricationUndoManager.Peek(_doc);
                if (last == null || last.SheetIds == null || last.SheetIds.Count == 0)
                {
                    MessageBox.Show(
                        "No fabrication package on the session history.\nRun Generate Package first.",
                        Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var app = StingTools.UI.StingCommandHandler.CurrentApp;
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null || _doc == null) return;
                var sheet = _doc.GetElement(new Autodesk.Revit.DB.ElementId(last.SheetIds[0])) as Autodesk.Revit.DB.View;
                if (sheet == null)
                {
                    MessageBox.Show(
                        "The last generated sheet has been deleted.",
                        Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                uidoc.ActiveView = sheet;
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationWorkspaceDialog.OpenLastGeneratedSheet", ex);
            }
        }

        private void OpenStingLog()
        {
            try
            {
                string assyDir = StingToolsApp.AssemblyPath;
                if (string.IsNullOrEmpty(assyDir))
                {
                    MessageBox.Show("Plugin assembly path is not initialised yet.",
                        Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string logPath = Path.Combine(assyDir, "StingTools.log");
                if (!File.Exists(logPath))
                {
                    MessageBox.Show($"Log file not found: {logPath}",
                        Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationWorkspaceDialog.OpenStingLog", ex);
            }
        }

        private void OpenHelp()
        {
            MessageBox.Show(
                "STING v4 — Fabrication Workspace\n\n" +
                "1. Pick scope (Selection / Active view / Project) → Refresh.\n" +
                "2. Tick categories you want included; the live counts " +
                "show how many MEP elements feed the package.\n" +
                "3. Optional: filter by System / Level free-text.\n" +
                "4. Configure title block + view template (Configure…).\n" +
                "5. Click Generate package — assemblies, views, sheets " +
                "and ISO 6412 symbols are created in one transaction.\n" +
                "6. Re-emit individual exports (cut list, weld map, iso " +
                "index, PCF, MAJ) without rebuilding the whole package.\n\n" +
                "Settings… surfaces the discipline rules + output " +
                "toggles + content mode (ISO 6412 vs generic).",
                "STING v4 — Fabrication Workspace help",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helper types
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>One row in the live category-breakdown card. The CheckBox
    /// gates whether elements of that category contribute to the package
    /// preview / scope, and the right-aligned count tracks the live
    /// element count returned by RefreshScopeAndCategories.</summary>
    internal sealed class CategoryRow
    {
        private readonly CheckBox _check;
        private readonly TextBlock _txtCount;
        public BuiltInCategory Category { get; }
        public Grid RowElement { get; }
        public bool IsChecked => _check?.IsChecked == true;

        public CategoryRow(BuiltInCategory bic, string label, string disc,
            Color fg, Color accent, Color border, Action onToggle)
        {
            Category = bic;

            RowElement = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            RowElement.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RowElement.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            _check = new CheckBox
            {
                Content = label,
                IsChecked = true,
                Foreground = new SolidColorBrush(fg),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Discipline: {disc}",
            };
            _check.Checked   += (s, e) => onToggle?.Invoke();
            _check.Unchecked += (s, e) => onToggle?.Invoke();
            Grid.SetColumn(_check, 0); RowElement.Children.Add(_check);

            _txtCount = new TextBlock
            {
                Text = "0",
                Foreground = new SolidColorBrush(fg),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 4, 4, 4),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_txtCount, 1); RowElement.Children.Add(_txtCount);
        }

        public void SetCount(int n)
        {
            if (_txtCount != null) _txtCount.Text = n.ToString();
            RowElement.Opacity = n == 0 ? 0.45 : 1.0;
        }
    }

    /// <summary>Single row in the Package preview grid.</summary>
    internal sealed class AssemblyPreviewRow
    {
        public bool   Selected  { get; set; }
        public string Disc      { get; set; }
        public string System    { get; set; }
        public string Level     { get; set; }
        public int    Count     { get; set; }
        public string SpoolName { get; set; }
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
