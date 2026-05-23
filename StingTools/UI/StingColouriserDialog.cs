using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StingTools.Core;
// Note: Autodesk.Revit.DB is not imported (it would collide with WPF's
// Color, Visibility, View, etc.). Document is referenced via its full name.

namespace StingTools.UI
{
    /// <summary>
    /// Modeless STING Colouriser — comprehensive graphic-override surface for
    /// the entire Revit model. Four tabs (Parameter / Token Schemes /
    /// Annotations / Manage), category filter rail on the left, and a header
    /// strip showing scope + active palette.
    ///
    /// Inspired by:
    ///   • Naviate Color Elements (Symetri) — color-by-parameter + filter gen
    ///   • GRAITEC PowerPack Element Lookup — search + visual filtering
    ///   • DiRoots OneFilter Visualize — filter + colour combos
    ///   • BIM One Color Splasher — open-source color-by-parameter
    ///   • ModPlus mprColorizer — color by conditions with preset save
    ///
    /// Every action button dispatches through <see cref="StingDockPanel.DispatchCommand"/>
    /// after pushing the current category checkbox state into
    /// <see cref="TagConfig.CategorySkipList"/> and invalidating the
    /// auto-tagger / compliance caches, so colouring is restricted to the
    /// ticked categories.
    /// </summary>
    internal static class StingColouriserDialog
    {
        private static Window _window;
        private static Autodesk.Revit.DB.Document _doc;

        // Category filter state (mirrors Tag Center)
        private static readonly Dictionary<string, CheckBox> _categoryCheckboxes =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private static TextBox _txtCategoryFilter;
        private static TextBlock _txtCategoryStatus;
        private static TextBlock _txtFooterStatus;
        private static TextBlock _txtScopeStatus;

        // Visual constants
        private static readonly Brush BrHeader      = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37));
        private static readonly Brush BrAccentBlue  = new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2));
        private static readonly Brush BrAccentGreen = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush BrAccentOrange= new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
        private static readonly Brush BrAccentRed   = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly Brush BrAccentPurple= new SolidColorBrush(Color.FromRgb(0x8E, 0x24, 0xAA));
        private static readonly Brush BrCardBg      = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        private static readonly Brush BrCardBorder  = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        private static readonly Brush BrSubtle      = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

        public static void Show(Autodesk.Revit.DB.Document doc)
        {
            if (doc == null) return;
            _doc = doc;

            // Modeless: re-show existing window if already open
            if (_window != null)
            {
                try
                {
                    _window.Show();
                    _window.Activate();
                    return;
                }
                catch (Exception)
                {
                    _window = null; // fall through and create new
                }
            }

            _window = new Window
            {
                Title = "STING Colouriser",
                Width = 980,
                Height = 680,
                MinWidth = 820,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
            };
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"Colouriser owner: {ex.Message}"); }

            _window.Closed += (s, e) =>
            {
                _window = null;
                _categoryCheckboxes.Clear();
            };

            _window.Content = BuildRoot();

            try { _window.Show(); }
            catch (Exception ex)
            {
                StingLog.Error("StingColouriserDialog.Show failed", ex);
                _window = null;
            }
        }

        // ── Root layout ────────────────────────────────────────────────────

        private static FrameworkElement BuildRoot()
        {
            var root = new DockPanel { LastChildFill = true };

            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var catRail = BuildCategoryRail();
            Grid.SetColumn(catRail, 0);
            body.Children.Add(catRail);

            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = BrCardBorder
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            var tabs = BuildTabs();
            Grid.SetColumn(tabs, 2);
            body.Children.Add(tabs);

            root.Children.Add(body);
            return root;
        }

        private static Border BuildHeader()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x14, 0x8C)),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var dock = new DockPanel { LastChildFill = true };

            _txtScopeStatus = new TextBlock
            {
                Text = "Scope: Active view  •  No category filter applied yet",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(_txtScopeStatus, Dock.Right);
            dock.Children.Add(_txtScopeStatus);

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "■ STING Colouriser",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = "  Parameter • Token Schemes • Annotations • Manage",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            dock.Children.Add(stack);

            border.Child = dock;
            return border;
        }

        private static Border BuildFooter()
        {
            var border = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrCardBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 4, 10, 4)
            };
            var dock = new DockPanel { LastChildFill = true };

            var closeBtn = new Button
            {
                Content = "✕  Close",
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 10
            };
            closeBtn.Click += (s, e) => { try { _window?.Close(); } catch (Exception ex) { StingLog.Warn($"Colouriser close: {ex.Message}"); } };
            DockPanel.SetDock(closeBtn, Dock.Right);
            dock.Children.Add(closeBtn);

            var clearAllBtn = new Button
            {
                Content = "Clear all overrides in view",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 10,
                Background = BrAccentRed,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                ToolTip = "Reset all per-element graphic overrides in the active view"
            };
            clearAllBtn.Click += (s, e) => DispatchWithFilter("Clear overrides", "ClearOverrides");
            DockPanel.SetDock(clearAllBtn, Dock.Right);
            dock.Children.Add(clearAllBtn);

            _txtFooterStatus = new TextBlock
            {
                Text = "Ready.",
                FontSize = 10,
                Foreground = BrSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            dock.Children.Add(_txtFooterStatus);

            border.Child = dock;
            return border;
        }

        // ── Category rail ─────────────────────────────────────────────────

        private static FrameworkElement BuildCategoryRail()
        {
            var border = new Border
            {
                Background = BrCardBg,
                BorderBrush = BrCardBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(8, 8, 8, 8)
            };
            var dock = new DockPanel { LastChildFill = true };

            var heading = new TextBlock
            {
                Text = "CATEGORIES TO COLOUR",
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = BrHeader,
                Margin = new Thickness(0, 0, 0, 4)
            };
            DockPanel.SetDock(heading, Dock.Top);
            dock.Children.Add(heading);

            var hint = new TextBlock
            {
                Text = "Tick to include in colour actions. Unticked categories are skipped by every dispatch from this dialog.",
                FontSize = 9,
                Foreground = BrSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel.SetDock(hint, Dock.Top);
            dock.Children.Add(hint);

            // Filter row
            var filterRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filterRow.Children.Add(new TextBlock { Text = "Filter:", FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _txtCategoryFilter = new TextBox { Height = 22, FontSize = 10, VerticalContentAlignment = VerticalAlignment.Center };
            _txtCategoryFilter.TextChanged += (s, e) => ApplyFilter();
            Grid.SetColumn(_txtCategoryFilter, 1);
            filterRow.Children.Add(_txtCategoryFilter);
            var clearBtn = new Button { Content = "✕", Width = 22, Height = 22, Margin = new Thickness(2, 0, 0, 0), FontSize = 10 };
            clearBtn.Click += (s, e) => { if (_txtCategoryFilter != null) _txtCategoryFilter.Text = string.Empty; };
            Grid.SetColumn(clearBtn, 2);
            filterRow.Children.Add(clearBtn);
            DockPanel.SetDock(filterRow, Dock.Top);
            dock.Children.Add(filterRow);

            // Bulk tick / discipline chips
            var bulkRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            bulkRow.Children.Add(MakeBulkBtn("Tick All",  () => SetAllVisible(true)));
            bulkRow.Children.Add(MakeBulkBtn("Untick All",() => SetAllVisible(false)));
            bulkRow.Children.Add(MakeBulkBtn("Invert",    InvertVisible));
            DockPanel.SetDock(bulkRow, Dock.Top);
            dock.Children.Add(bulkRow);

            var discRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            discRow.Children.Add(new TextBlock { Text = "Disc:", FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            foreach (var d in new[] { "M", "E", "P", "A", "S", "FP", "LV" })
                discRow.Children.Add(MakeDiscChip(d));
            DockPanel.SetDock(discRow, Dock.Top);
            dock.Children.Add(discRow);

            // Status row
            var statusRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _txtCategoryStatus = new TextBlock
            {
                Text = "0 of 0 ticked",
                FontSize = 9,
                Foreground = BrSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtCategoryStatus, 0);
            statusRow.Children.Add(_txtCategoryStatus);

            var applyBtn = new Button
            {
                Content = "Apply filter",
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 10,
                Background = BrAccentOrange,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                ToolTip = "Push the ticked categories into TagConfig.CategorySkipList in-memory; next dispatched colour command will respect it"
            };
            applyBtn.Click += (s, e) =>
            {
                PushFilterToConfig();
                int skip = TagConfig.CategorySkipList?.Count ?? 0;
                int kept = _categoryCheckboxes.Count - skip;
                SetFooter($"Filter applied (in-memory): {kept} include, {skip} skip");
                UpdateScopeStatus();
            };
            Grid.SetColumn(applyBtn, 1);
            statusRow.Children.Add(applyBtn);
            DockPanel.SetDock(statusRow, Dock.Bottom);
            dock.Children.Add(statusRow);

            // Scrollable category list
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var list = new StackPanel();
            BuildCategoryCheckboxes(list);
            scroll.Content = list;
            var listBorder = new Border
            {
                BorderBrush = BrCardBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Child = scroll
            };
            dock.Children.Add(listBorder);

            border.Child = dock;
            UpdateCategoryStatus();
            return border;
        }

        private static Button MakeBulkBtn(string label, Action onClick)
        {
            var b = new Button { Content = label, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 2), FontSize = 9 };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        private static CheckBox MakeDiscChip(string disc)
        {
            var cb = new CheckBox
            {
                Content = disc,
                FontSize = 9,
                Margin = new Thickness(0, 0, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = disc,
                ToolTip = $"Tick all {disc}-discipline categories; untick to remove them"
            };
            RoutedEventHandler handler = (s, e) =>
            {
                if (!(cb.Tag is string d)) return;
                bool target = cb.IsChecked == true;
                var discMap = TagConfig.DiscMap;
                if (discMap == null) return;
                int affected = 0;
                foreach (var kvp in _categoryCheckboxes)
                {
                    if (discMap.TryGetValue(kvp.Key, out string elDisc)
                        && string.Equals(elDisc, d, StringComparison.OrdinalIgnoreCase))
                    {
                        if (kvp.Value.IsChecked != target)
                        {
                            kvp.Value.IsChecked = target;
                            affected++;
                        }
                    }
                }
                UpdateCategoryStatus();
                SetFooter($"Categories: {(target ? "ticked" : "unticked")} {affected} {d}-discipline categories");
            };
            cb.Checked += handler;
            cb.Unchecked += handler;
            return cb;
        }

        private static void BuildCategoryCheckboxes(StackPanel host)
        {
            host.Children.Clear();
            _categoryCheckboxes.Clear();

            var allCats = ParamRegistry.CategoryEnumMap.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var skipSet = TagConfig.CategorySkipList ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discMap = TagConfig.DiscMap ?? new Dictionary<string, string>();

            foreach (var cat in allCats)
            {
                bool ticked = !skipSet.Contains(cat);
                string disc = discMap.TryGetValue(cat, out string d) ? d : "";
                var cb = new CheckBox
                {
                    Content = string.IsNullOrEmpty(disc) ? cat : $"{cat}  ({disc})",
                    Tag = cat,
                    IsChecked = ticked,
                    FontSize = 10,
                    Margin = new Thickness(4, 1, 4, 1)
                };
                cb.Checked += (s, e) => UpdateCategoryStatus();
                cb.Unchecked += (s, e) => UpdateCategoryStatus();
                host.Children.Add(cb);
                _categoryCheckboxes[cat] = cb;
            }
        }

        private static void ApplyFilter()
        {
            string needle = (_txtCategoryFilter?.Text ?? string.Empty).Trim();
            foreach (var kvp in _categoryCheckboxes)
            {
                bool match = string.IsNullOrEmpty(needle)
                    || kvp.Key.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                kvp.Value.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateCategoryStatus();
        }

        private static void SetAllVisible(bool ticked)
        {
            foreach (var cb in _categoryCheckboxes.Values)
                if (cb.Visibility == Visibility.Visible) cb.IsChecked = ticked;
            UpdateCategoryStatus();
        }

        private static void InvertVisible()
        {
            foreach (var cb in _categoryCheckboxes.Values)
                if (cb.Visibility == Visibility.Visible) cb.IsChecked = !(cb.IsChecked == true);
            UpdateCategoryStatus();
        }

        private static void UpdateCategoryStatus()
        {
            if (_txtCategoryStatus == null) return;
            int total = _categoryCheckboxes.Count;
            int ticked = _categoryCheckboxes.Values.Count(c => c.IsChecked == true);
            int visible = _categoryCheckboxes.Values.Count(c => c.Visibility == Visibility.Visible);
            int visTicked = _categoryCheckboxes.Values.Count(c => c.Visibility == Visibility.Visible && c.IsChecked == true);
            string filter = _txtCategoryFilter?.Text ?? string.Empty;
            _txtCategoryStatus.Text = string.IsNullOrEmpty(filter)
                ? $"{ticked} of {total} ticked"
                : $"{visTicked} of {visible} ticked in filter ({ticked} of {total} total)";
        }

        private static void UpdateScopeStatus()
        {
            if (_txtScopeStatus == null) return;
            int skip = TagConfig.CategorySkipList?.Count ?? 0;
            int kept = _categoryCheckboxes.Count - skip;
            _txtScopeStatus.Text = $"Scope: Active view  •  Filter: {kept} include / {skip} skip";
        }

        private static void PushFilterToConfig()
        {
            try
            {
                var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _categoryCheckboxes)
                    if (kvp.Value.IsChecked != true) skip.Add(kvp.Key);
                TagConfig.CategorySkipList = skip;
                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Colouriser PushFilterToConfig: {ex.Message}");
            }
        }

        // ── Tabs ───────────────────────────────────────────────────────────

        private static TabControl BuildTabs()
        {
            var tabs = new TabControl { Margin = new Thickness(4) };
            tabs.Items.Add(new TabItem { Header = "Parameter",      Content = BuildParameterTab() });
            tabs.Items.Add(new TabItem { Header = "Token Schemes",  Content = BuildTokenSchemesTab() });
            tabs.Items.Add(new TabItem { Header = "Annotations",    Content = BuildAnnotationsTab() });
            tabs.Items.Add(new TabItem { Header = "Manage",         Content = BuildManageTab() });
            return tabs;
        }

        // 1. Parameter tab — color elements by any parameter
        private static FrameworkElement BuildParameterTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("COLOUR ELEMENTS BY PARAMETER"));
            stack.Children.Add(IntroLine("Pick any instance/type parameter; choose a palette; apply graphic overrides to all elements grouped by their value. Empty values highlighted in red."));
            stack.Children.Add(Card(new[]
            {
                Btn("Colour by parameter",   "ColorByParameter",  BrAccentGreen, "Pick a parameter, choose palette, apply graphic overrides grouped by value (10 built-in palettes)"),
                Btn("Colour by variable",    "ColorByVariable",   BrAccentBlue,  "Pick numeric parameter; auto-bin and colour with continuous spectrum (heat/load mapping)"),
                Btn("Colour tags by param.", "ColorTagsByParam",  BrAccentBlue,  "Apply colours to annotation tags based on parameter value"),
            }));

            stack.Children.Add(SectionLabel("OVERRIDE OPTIONS"));
            stack.Children.Add(IntroLine("These options are applied per element via OverrideGraphicSettings on the active view."));

            var optsCard = WrapInCard(BuildOverrideOptionsGrid());
            stack.Children.Add(optsCard);

            stack.Children.Add(SectionLabel("PALETTE PRESETS"));
            stack.Children.Add(IntroLine("Click any palette to colour by parameter using that palette."));
            var palettes = new WrapPanel { Margin = new Thickness(2) };
            void AddPalette(string label, byte[] sw, string tip)
            {
                var btn = PaletteChipBtn(label, sw, tip);
                palettes.Children.Add(btn);
            }
            // Each preset shown as a horizontal swatch strip
            AddPalette("STING Disc",  new byte[] { 0x19, 0x76, 0xD2,  0xFB, 0xC0, 0x2D,  0x2E, 0x7D, 0x32,  0x9E, 0x9E, 0x9E,  0xC6, 0x28, 0x28,  0xE6, 0x7E, 0x22 }, "Discipline palette: M=Blue, E=Yellow, P=Green, A=Grey, S=Red, FP=Orange");
            AddPalette("RAG",         new byte[] { 0xC6, 0x28, 0x28,  0xFB, 0xC0, 0x2D,  0x2E, 0x7D, 0x32 }, "Red / Amber / Green — compliance and severity coding");
            AddPalette("Spectral",    new byte[] { 0x9E, 0x01, 0x42,  0xD5, 0x32, 0x4F,  0xFD, 0xAE, 0x61,  0xFE, 0xE0, 0x8B,  0xAB, 0xDD, 0xA4,  0x66, 0xC2, 0xA5,  0x32, 0x88, 0xBD,  0x5E, 0x4F, 0xA2 }, "Continuous rainbow gradient — best for ordered numeric ranges");
            AddPalette("Warm",        new byte[] { 0xC6, 0x28, 0x28,  0xE6, 0x7E, 0x22,  0xFB, 0xC0, 0x2D,  0xFF, 0xF1, 0x76 }, "Heat-map palette: red → orange → yellow → cream");
            AddPalette("Cool",        new byte[] { 0x0D, 0x47, 0xA1,  0x19, 0x76, 0xD2,  0x40, 0xC4, 0xFF,  0xB2, 0xEB, 0xF2 }, "Cool palette: navy → blue → cyan → mint");
            AddPalette("Pastel",      new byte[] { 0xFF, 0xCC, 0xBC,  0xFF, 0xF9, 0xC4,  0xC8, 0xE6, 0xC9,  0xBB, 0xDE, 0xFB,  0xD1, 0xC4, 0xE9 }, "Soft muted tones — presentation views");
            AddPalette("High contrast",new byte[] { 0xFF, 0x00, 0x00,  0x00, 0xFF, 0x00,  0x00, 0x00, 0xFF,  0xFF, 0xFF, 0x00,  0x00, 0x00, 0x00 }, "Saturated primaries with black — QA / checking");
            AddPalette("Accessible",  new byte[] { 0x44, 0x01, 0x54,  0x3B, 0x52, 0x8B,  0x21, 0x91, 0x8C,  0x5E, 0xC9, 0x62,  0xFD, 0xE7, 0x25 }, "Viridis-like palette — colourblind safe");
            AddPalette("Monochrome",  new byte[] { 0x21, 0x21, 0x21,  0x61, 0x61, 0x61,  0x9E, 0x9E, 0x9E,  0xE0, 0xE0, 0xE0 }, "Grey-scale — print-friendly");
            stack.Children.Add(WrapInCard(palettes));

            stack.Children.Add(SectionLabel("EMPTY-VALUE QA"));
            stack.Children.Add(Card(new[]
            {
                Btn("Highlight nulls",       "HighlightInvalid", BrAccentOrange, "Highlight elements with missing/incomplete tag tokens (red = missing, orange = incomplete)"),
                Btn("Find duplicates",       "FindDuplicates",   BrAccentOrange, "Select all elements with duplicate tag values"),
                Btn("Element lookup",        "<lookup>",         BrAccentBlue,   "Open parameter-driven element lookup (Graitec-style)"),
            }));

            return dock;
        }

        // 2. Token Schemes tab — quick chips for STING tokens
        private static FrameworkElement BuildTokenSchemesTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("QUICK SCHEMES (per token)"));
            stack.Children.Add(IntroLine("One-click colour schemes keyed off STING tag tokens. Element graphic overrides are applied to every element whose token matches; the legend updates automatically."));

            var schemes = new WrapPanel { Margin = new Thickness(2, 0, 2, 4) };
            void AddScheme(string label, string tag, byte r, byte g, byte b, string tip)
            {
                var btn = ColourChipBtn(label, tag, Color.FromRgb(r, g, b), tip);
                schemes.Children.Add(btn);
            }
            AddScheme("Discipline","TagStudio_SchemeDiscipline", 0xFF, 0xE0, 0xB2, "Colour by DISC token (M / E / P / A / S / FP / LV)");
            AddScheme("System",    "TagStudio_SchemeFunction",   0xF0, 0xF4, 0xC3, "Colour by SYS / FUNC token");
            AddScheme("Status",    "TagStudio_SchemeStatus",     0xD1, 0xC4, 0xE9, "Colour by STATUS (NEW / EXISTING / DEMOLISHED / TEMPORARY)");
            AddScheme("Zone",      "TagStudio_SchemeZone",       0xC8, 0xE6, 0xC9, "Colour by ZONE (Z01–Z04)");
            AddScheme("Level",     "TagStudio_SchemeLevel",      0xB3, 0xE5, 0xFC, "Colour by LVL token");
            AddScheme("Function",  "TagStudio_SchemeFunction",   0xF0, 0xF4, 0xC3, "Colour by FUNC token (CIBSE / Uniclass)");
            stack.Children.Add(WrapInCard(schemes));

            stack.Children.Add(SectionLabel("PALETTE STYLE"));
            stack.Children.Add(IntroLine("Override the palette used by the next quick scheme click."));
            var stylePalettes = new WrapPanel { Margin = new Thickness(2, 0, 2, 4) };
            stylePalettes.Children.Add(ColourChipBtn("Warm",  "TagStudio_SchemeWarm",   Color.FromRgb(0xFF, 0xCC, 0xBC), "Use warm palette for next scheme"));
            stylePalettes.Children.Add(ColourChipBtn("Cool",  "TagStudio_SchemeCool",   Color.FromRgb(0xB2, 0xEB, 0xF2), "Use cool palette for next scheme"));
            stylePalettes.Children.Add(ColourChipBtn("Red",   "TagStudio_SchemeRed",    Color.FromRgb(0xFF, 0xCD, 0xD2), "Use red palette for next scheme"));
            stylePalettes.Children.Add(ColourChipBtn("Yellow","TagStudio_SchemeYellow", Color.FromRgb(0xFF, 0xF9, 0xC4), "Use yellow palette for next scheme"));
            stylePalettes.Children.Add(ColourChipBtn("Blue",  "TagStudio_SchemeBlue",   Color.FromRgb(0xBB, 0xDE, 0xFB), "Use blue palette for next scheme"));
            stylePalettes.Children.Add(ColourChipBtn("Mono",  "TagStudio_SchemeMono",   Color.FromRgb(0xE0, 0xE0, 0xE0), "Use mono palette for next scheme"));
            stylePalettes.Children.Add(ColourChipBtn("Dark",  "TagStudio_SchemeDark",   Color.FromRgb(0x42, 0x42, 0x42), "Use dark palette (white on dark) for next scheme"));
            stack.Children.Add(WrapInCard(stylePalettes));

            stack.Children.Add(SectionLabel("APPLY / BATCH"));
            stack.Children.Add(Card(new[]
            {
                Btn("Apply to active view", "ApplyColorScheme",      BrAccentGreen, "Apply currently-loaded scheme to the active view"),
                Btn("Batch apply (all views)", "BatchApplyColorScheme", BrAccentBlue, "Apply scheme across every view in the project"),
                Btn("Clear scheme",         "ClearColorScheme",      BrAccentRed,   "Remove all graphic overrides from active view"),
            }));

            return dock;
        }

        // 3. Annotations tab — tag / leader colours
        private static FrameworkElement BuildAnnotationsTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("ANNOTATION TAG COLOURS"));
            stack.Children.Add(IntroLine("Colour the annotation tags themselves (text, leader, box) — distinct from element overrides."));
            stack.Children.Add(Card(new[]
            {
                Btn("By discipline",       "ColorTagsByDiscipline", BrAccentGreen, "Colour-code annotation tags by discipline"),
                Btn("By parameter",        "ColorTagsByParam",      BrAccentBlue,  "Colour annotation tags by any parameter value"),
                Btn("Set tag text colour", "SetTagTextColor",       BrAccentBlue,  "Set text colour for selected annotation tags"),
                Btn("Set leader colour",   "SetLeaderColor",        BrAccentBlue,  "Set leader-line colour for selected tags"),
                Btn("Split tag/leader",    "SplitTagLeaderColor",   BrAccentBlue,  "Apply different colours to leader vs tag text"),
                Btn("Set box colour",      "SetBoxColor",           BrAccentBlue,  "Set tag box / border colour"),
                Btn("Clear annotation",    "ClearAnnotationColors", BrAccentRed,   "Clear all annotation colour overrides in active view"),
            }));

            stack.Children.Add(SectionLabel("LEGENDS"));
            stack.Children.Add(Card(new[]
            {
                Btn("VG category legend",  "VGCategoryLegend",  BrAccentBlue,  "Build legend from per-category VG overrides in active view"),
                Btn("Discipline legend",   "DisciplineLegend",  BrAccentBlue,  "Build legend grouped by discipline"),
                Btn("System legend",       "SystemLegend",      BrAccentBlue,  "Build legend grouped by MEP system"),
                Btn("Material legend",     "MaterialLegend",    BrAccentBlue,  "Build legend grouped by material"),
                Btn("Status legend",       "StatusLegend",      BrAccentBlue,  "Build legend grouped by element status"),
                Btn("Color legend",        "ColorLegend",       BrAccentBlue,  "Build legend showing parameter-value to colour mapping"),
            }));

            return dock;
        }

        // 4. Manage tab — presets, filters, view-wide actions
        private static FrameworkElement BuildManageTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("PRESETS"));
            stack.Children.Add(IntroLine("Save the current colouring as a named preset (JSON) for re-use across projects."));
            stack.Children.Add(Card(new[]
            {
                Btn("Save preset",   "SaveColorPreset", BrAccentGreen, "Save current colour scheme as a named JSON preset to COLOR_PRESETS.json"),
                Btn("Load preset",   "LoadColorPreset", BrAccentBlue,  "Load and apply a saved colour preset"),
            }));

            stack.Children.Add(SectionLabel("CONVERT TO VIEW FILTERS"));
            stack.Children.Add(IntroLine("Promote the temporary graphic overrides into persistent ParameterFilterElement rules so the colouring follows the project."));
            stack.Children.Add(Card(new[]
            {
                Btn("Create filters from colours", "CreateFiltersFromColors", BrAccentGreen, "Convert active colour scheme to persistent Revit ParameterFilterElement rules"),
                Btn("AEC filter library",          "AecFiltersCreate",        BrAccentBlue,  "Mint corporate ParameterFilterElement library (199 filters covering BS 1192 / ISO 19650 / Uniclass / ASME / CIBSE-SDE)"),
                Btn("Inspect filter library",      "AecFiltersInspect",       BrAccentBlue,  "Read-only diagnostic of the AEC filter library"),
            }));

            stack.Children.Add(SectionLabel("VIEW-WIDE OVERRIDES"));
            stack.Children.Add(Card(new[]
            {
                Btn("Clear element overrides",    "ClearOverrides",        BrAccentRed,    "Reset all per-element graphic overrides in active view"),
                Btn("Clear annotation colours",   "ClearAnnotationColors", BrAccentRed,    "Clear all annotation colour overrides"),
                Btn("Clear tag colour scheme",    "ClearColorScheme",      BrAccentRed,    "Remove the active tag colour scheme"),
            }));

            stack.Children.Add(SectionLabel("BATCH OPS"));
            stack.Children.Add(Card(new[]
            {
                Btn("Batch apply scheme",   "BatchApplyColorScheme", BrAccentBlue,  "Apply current colour scheme across all views in the project"),
                Btn("Apply category filter","<filter>",              BrAccentOrange,"Push the current Category checkbox state into TagConfig.CategorySkipList in-memory; next dispatched colour command will respect it"),
            }));

            return dock;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static DockPanel MakeTabContent(out StackPanel stack)
        {
            stack = new StackPanel { Margin = new Thickness(8) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = stack
            };
            var dock = new DockPanel { LastChildFill = true };
            dock.Children.Add(scroll);
            return dock;
        }

        private static Border SectionLabel(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = BrHeader,
                Margin = new Thickness(0, 6, 0, 4)
            };
            return new Border { Child = tb };
        }

        private static TextBlock IntroLine(string text)
            => new TextBlock { Text = text, FontSize = 9, Foreground = BrSubtle, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

        private static Border Card(IEnumerable<Button> buttons)
        {
            var wrap = new WrapPanel { Margin = new Thickness(2) };
            foreach (var b in buttons) wrap.Children.Add(b);
            return WrapInCard(wrap);
        }

        private static Border WrapInCard(FrameworkElement child)
        {
            return new Border
            {
                Background = BrCardBg,
                BorderBrush = BrCardBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 4),
                Child = child
            };
        }

        private static Button Btn(string label, string tag, Brush accent, string tooltip)
        {
            var b = new Button
            {
                Content = label,
                Width = 150,
                Height = 30,
                Margin = new Thickness(2),
                FontSize = 10,
                ToolTip = tooltip,
                Background = accent,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            b.Click += (s, e) => DispatchWithFilter(label, tag);
            return b;
        }

        private static Button ColourChipBtn(string label, string tag, Color bg, string tip)
        {
            var b = new Button
            {
                Content = label,
                Width = 90,
                Height = 28,
                Margin = new Thickness(2),
                FontSize = 9,
                ToolTip = tip,
                Background = new SolidColorBrush(bg),
                Foreground = bg.R + bg.G + bg.B < 380 ? Brushes.White : Brushes.Black,
            };
            b.Click += (s, e) => DispatchWithFilter(label, tag);
            return b;
        }

        /// <summary>Build a palette chip showing the swatch strip + label.
        /// Clicking the chip pushes the palette name as an ExtraParam ("PaletteHint")
        /// then dispatches ColorByParameter so the user can immediately pick a
        /// parameter to colour by using this palette.</summary>
        private static Button PaletteChipBtn(string label, byte[] swatchRgb, string tooltip)
        {
            var swatchPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            int swatchCount = swatchRgb.Length / 3;
            for (int i = 0; i < swatchCount; i++)
            {
                var c = Color.FromRgb(swatchRgb[i * 3], swatchRgb[i * 3 + 1], swatchRgb[i * 3 + 2]);
                swatchPanel.Children.Add(new Border
                {
                    Width = 12,
                    Height = 12,
                    Background = new SolidColorBrush(c),
                    BorderBrush = BrCardBorder,
                    BorderThickness = new Thickness(0.5),
                    Margin = new Thickness(0)
                });
            }
            var contentStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            contentStack.Children.Add(swatchPanel);
            contentStack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });

            var b = new Button
            {
                Width = 110,
                Height = 38,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                Content = contentStack,
                ToolTip = tooltip,
                Background = BrCardBg,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            b.Click += (s, e) =>
            {
                try
                {
                    StingCommandHandler.SetExtraParam("PaletteHint", label);
                    DispatchWithFilter($"Colour by parameter ({label})", "ColorByParameter");
                }
                catch (Exception ex) { SetFooter($"Palette dispatch failed: {ex.Message}"); }
            };
            return b;
        }

        private static FrameworkElement BuildOverrideOptionsGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Halftone toggle
            grid.Children.Add(MakeOptionLabel("Halftone", 0));
            var halftone = new CheckBox { IsChecked = false, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4), Content = "Apply halftone to non-matching elements" };
            Grid.SetRow(halftone, 0); Grid.SetColumn(halftone, 1); Grid.SetColumnSpan(halftone, 2);
            halftone.Checked   += (s, e) => { StingCommandHandler.SetExtraParam("ColourHalftone", "1"); SetFooter("Halftone option set: ON"); };
            halftone.Unchecked += (s, e) => { StingCommandHandler.SetExtraParam("ColourHalftone", "0"); SetFooter("Halftone option set: OFF"); };
            grid.Children.Add(halftone);

            // Row 1: Surface transparency
            grid.Children.Add(MakeOptionLabel("Transparency", 1));
            var sldTrans = new Slider { Minimum = 0, Maximum = 100, Value = 0, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(sldTrans, 1); Grid.SetColumn(sldTrans, 1);
            grid.Children.Add(sldTrans);
            var lblTrans = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Text = "0%" };
            Grid.SetRow(lblTrans, 1); Grid.SetColumn(lblTrans, 2);
            grid.Children.Add(lblTrans);
            sldTrans.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(sldTrans.Value);
                lblTrans.Text = v + "%";
                StingCommandHandler.SetExtraParam("ColourTransparency", v.ToString());
            };

            // Row 2: Projection line weight
            grid.Children.Add(MakeOptionLabel("Line weight", 2));
            var sldWeight = new Slider { Minimum = 1, Maximum = 16, Value = 1, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
            Grid.SetRow(sldWeight, 2); Grid.SetColumn(sldWeight, 1);
            grid.Children.Add(sldWeight);
            var lblWeight = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Text = "1" };
            Grid.SetRow(lblWeight, 2); Grid.SetColumn(lblWeight, 2);
            grid.Children.Add(lblWeight);
            sldWeight.ValueChanged += (s, e) =>
            {
                int v = (int)Math.Round(sldWeight.Value);
                lblWeight.Text = v.ToString();
                StingCommandHandler.SetExtraParam("ColourLineWeight", v.ToString());
            };

            // Row 3: Surface fill checkbox
            grid.Children.Add(MakeOptionLabel("Surface fill", 3));
            var fill = new CheckBox { IsChecked = true, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4), Content = "Solid-fill the surface (otherwise outline only)" };
            Grid.SetRow(fill, 3); Grid.SetColumn(fill, 1); Grid.SetColumnSpan(fill, 2);
            fill.Checked   += (s, e) => StingCommandHandler.SetExtraParam("ColourSurfaceFill", "1");
            fill.Unchecked += (s, e) => StingCommandHandler.SetExtraParam("ColourSurfaceFill", "0");
            grid.Children.Add(fill);

            return grid;
        }

        private static TextBlock MakeOptionLabel(string text, int row)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = BrSubtle,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, 0);
            return tb;
        }

        private static void DispatchWithFilter(string label, string tag)
        {
            try
            {
                if (string.Equals(tag, "<filter>", StringComparison.Ordinal))
                {
                    PushFilterToConfig();
                    int skipCount = TagConfig.CategorySkipList?.Count ?? 0;
                    int keptCount = _categoryCheckboxes.Count - skipCount;
                    UpdateScopeStatus();
                    SetFooter($"Category filter applied: {keptCount} include, {skipCount} skip — next colour command will respect it");
                    return;
                }
                if (string.Equals(tag, "<lookup>", StringComparison.Ordinal))
                {
                    SetFooter("Element lookup: open the parameter lookup dialog from the SELECT tab → Bulk Param");
                    return;
                }

                PushFilterToConfig();
                bool ok = StingDockPanel.DispatchCommand(tag);
                SetFooter(ok
                    ? $"✓ Dispatched: {label} ({tag})"
                    : $"✗ Dispatch declined: {label} ({tag}) — Revit busy?");
            }
            catch (Exception ex)
            {
                StingLog.Error($"Colouriser dispatch '{tag}'", ex);
                SetFooter($"✗ {label} failed: {ex.Message}");
            }
        }

        private static void SetFooter(string msg)
        {
            if (_txtFooterStatus == null) return;
            try { _txtFooterStatus.Text = msg; } catch (Exception ex) { StingLog.Warn($"Colouriser SetFooter: {ex.Message}"); }
        }
    }
}
