using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StingTools.Core;
// Note: `Autodesk.Revit.DB` is not imported (it would collide with WPF's
// Color, Visibility, View, etc.). Document is referenced via its full name.

namespace StingTools.UI
{
    /// <summary>
    /// Modeless STING Tag Center — single command surface for the entire tagging
    /// workflow. Five tabs (Place / Position / Style / Tokens / Audit), category
    /// filter rail on the left, and a header scope chip strip.
    ///
    /// Every action button dispatches through <see cref="StingDockPanel.DispatchCommand"/>
    /// after pushing the current category checkbox state into
    /// <see cref="TagConfig.CategorySkipList"/> and invalidating the auto-tagger /
    /// compliance caches, so every placement command honours the ticked categories.
    ///
    /// Visual style mirrors the Tag Studio sub-tabs (compact WrapPanels, brown
    /// section headings, GroupBorder-style cards).
    /// </summary>
    internal static class TagCenterDialog
    {
        private static Window _window;
        private static Autodesk.Revit.DB.Document _doc;

        // Category filter state
        private static readonly Dictionary<string, CheckBox> _categoryCheckboxes =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private static TextBox _txtCategoryFilter;
        private static TextBlock _txtCategoryStatus;
        private static TextBlock _txtFooterStatus;

        // Visual constants — picked to match Tag Studio palette
        private static readonly Brush BrHeader      = new SolidColorBrush(Color.FromRgb(0x5D, 0x40, 0x37)); // brown headings
        private static readonly Brush BrAccentBlue  = new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2));
        private static readonly Brush BrAccentGreen = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush BrAccentOrange= new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
        private static readonly Brush BrAccentRed   = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
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
                Title = "STING Tag Center",
                Width = 1000,
                Height = 720,
                MinWidth = 820,
                MinHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
            };
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"TagCenter owner: {ex.Message}"); }

            _window.Closed += (s, e) =>
            {
                _window = null;
                _categoryCheckboxes.Clear();
            };

            _window.Content = BuildRoot();

            try { _window.Show(); }
            catch (Exception ex)
            {
                StingLog.Error("TagCenterDialog.Show failed", ex);
                _window = null;
            }
        }

        // ── Root layout ────────────────────────────────────────────────────

        private static FrameworkElement BuildRoot()
        {
            var root = new DockPanel { LastChildFill = true };

            // Header strip
            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Footer status bar
            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Body grid: Categories rail | TabControl
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
                Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "■ STING Tag Center",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = "  Place • Position • Style • Tokens • Audit",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            border.Child = stack;
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
            closeBtn.Click += (s, e) => { try { _window?.Close(); } catch (Exception ex) { StingLog.Warn($"TagCenter close: {ex.Message}"); } };
            DockPanel.SetDock(closeBtn, Dock.Right);
            dock.Children.Add(closeBtn);

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

            // Heading
            dock.Children.Add(SectionLabel("CATEGORIES TO TAG", Dock.Top));

            var hint = new TextBlock
            {
                Text = "Tick to include in placement actions. Untick to skip. Filter applies project-wide to AutoTag, BatchTag, Tag&Combine, SmartPlace, real-time auto-tagger.",
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

            // Tick all / none + discipline chips
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

            // Save / reload + status row
            var actionRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _txtCategoryStatus = new TextBlock
            {
                Text = "0 of 0 ticked",
                FontSize = 9,
                Foreground = BrSubtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtCategoryStatus, 0);
            actionRow.Children.Add(_txtCategoryStatus);

            var reloadBtn = new Button { Content = "Reload", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), FontSize = 10 };
            reloadBtn.Click += (s, e) => ReloadFromConfig();
            Grid.SetColumn(reloadBtn, 1);
            actionRow.Children.Add(reloadBtn);

            var saveBtn = new Button
            {
                Content = "Save & Apply",
                Padding = new Thickness(10, 2, 10, 2),
                FontSize = 10,
                Background = BrAccentOrange,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                ToolTip = "Persist current ticked set to project_config.json (CATEGORY_SKIP) and invalidate caches"
            };
            saveBtn.Click += (s, e) => SaveAndApply();
            Grid.SetColumn(saveBtn, 2);
            actionRow.Children.Add(saveBtn);
            DockPanel.SetDock(actionRow, Dock.Bottom);
            dock.Children.Add(actionRow);

            // Scrollable category list
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

        /// <summary>Push the current checkbox state into TagConfig.CategorySkipList
        /// (in-memory only) and invalidate caches so the next dispatched command
        /// honours it. Does NOT persist to project_config.json.</summary>
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
                StingLog.Warn($"TagCenter PushFilterToConfig: {ex.Message}");
            }
        }

        private static void SaveAndApply()
        {
            try
            {
                PushFilterToConfig();
                var skipList = TagConfig.CategorySkipList?.ToList() ?? new List<string>();
                TagConfig.SetConfigValue("CATEGORY_SKIP", skipList);
                int kept = _categoryCheckboxes.Count - skipList.Count;
                StingLog.Info($"TagCenter: CATEGORY_SKIP saved — {kept} included, {skipList.Count} skipped");
                SetFooter($"Categories saved: {kept} include, {skipList.Count} skip — applied to all subsequent tag commands");
            }
            catch (Exception ex)
            {
                StingLog.Error("TagCenter SaveAndApply", ex);
                SetFooter($"Save failed: {ex.Message}");
            }
        }

        private static void ReloadFromConfig()
        {
            try
            {
                var skipSet = TagConfig.CategorySkipList ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _categoryCheckboxes)
                    kvp.Value.IsChecked = !skipSet.Contains(kvp.Key);
                UpdateCategoryStatus();
                SetFooter($"Reloaded from project_config.json: {_categoryCheckboxes.Count - skipSet.Count} include, {skipSet.Count} skip");
            }
            catch (Exception ex) { SetFooter($"Reload failed: {ex.Message}"); }
        }

        // ── Tabs ───────────────────────────────────────────────────────────

        private static TabControl BuildTabs()
        {
            var tabs = new TabControl { Margin = new Thickness(4) };
            tabs.Items.Add(new TabItem { Header = "Place",    Content = BuildPlaceTab() });
            tabs.Items.Add(new TabItem { Header = "Position", Content = BuildPositionTab() });
            tabs.Items.Add(new TabItem { Header = "Style",    Content = BuildStyleTab() });
            tabs.Items.Add(new TabItem { Header = "Tokens",   Content = BuildTokensTab() });
            tabs.Items.Add(new TabItem { Header = "Audit",    Content = BuildAuditTab() });
            return tabs;
        }

        // 1. Place tab — every "create / fill in tag values" command
        private static FrameworkElement BuildPlaceTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("AUTO-TAG (DATA — populates parameters)"));
            stack.Children.Add(Card(new[]
            {
                Btn("Auto Tag (view)",     "AutoTag",          BrAccentGreen, "Tag elements in active view; populate all 9 tokens, build ASS_TAG_1, write 53 containers + TAG7"),
                Btn("Tag New Only",        "TagNewOnly",       BrAccentBlue,  "Only tag elements that have no existing ASS_TAG_1"),
                Btn("Batch Tag (project)", "BatchTag",         BrAccentBlue,  "Tag every taggable element in the project across all views"),
                Btn("Tag & Combine",       "TagAndCombine",    BrAccentGreen, "One-click: populate + tag + combine ALL 36 containers"),
                Btn("Tag 3D",              "Tag3D",            BrAccentBlue,  "Tag elements in active 3D view with spatial auto-detect"),
                Btn("Tag Selected",        "TagSelected",      BrAccentBlue,  "Tag only currently-selected elements"),
                Btn("Full Auto-Populate",  "FullAutoPopulate", BrAccentGreen, "Populate all schedule fields (199 formulas) without rebuilding the tag string"),
                Btn("Family-Stage Pop.",   "FamilyStagePopulate", BrAccentBlue, "Pre-populate all 7 source tokens before tagging"),
                Btn("Pre-Tag Audit",       "PreTagAudit",      BrAccentOrange,"Dry-run: predict tag assignments, collisions, and ISO violations BEFORE committing"),
            }));

            stack.Children.Add(SectionLabel("VISUAL TAG PLACEMENT (annotation)"));
            stack.Children.Add(Card(new[]
            {
                Btn("Smart Place",         "SmartPlaceTags",   BrAccentGreen, "Priority-based placement with 8-position collision avoidance"),
                Btn("Batch Place",         "BatchPlaceTags",   BrAccentBlue,  "Place visual annotation tags across multiple views"),
                Btn("Batch Place Linked",  "BatchPlaceLinkedTags", BrAccentBlue, "Place tags on elements in linked models"),
                Btn("Apply Template",      "ApplyTagTemplate", BrAccentBlue,  "Apply saved placement template to active view"),
                Btn("Learn Placement",     "LearnTagPlacement",BrAccentBlue,  "Analyse existing tag placements in this view to learn rules"),
                Btn("Remove Annotation",   "RemoveAnnotationTags", BrAccentRed, "Remove all IndependentTag annotations from active view"),
            }));

            stack.Children.Add(SectionLabel("SCOPE PRESETS"));
            stack.Children.Add(Card(new[]
            {
                Btn("Apply ticked filter now", "<filter>", BrAccentOrange, "Push the current Category checkbox state into the in-memory CategorySkipList without persisting; the next dispatched command will respect it. Same as ticking + clicking Smart Place once."),
            }));

            return dock;
        }

        // 2. Position tab — leader / arrange / nudge
        private static FrameworkElement BuildPositionTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("ARRANGEMENT"));
            stack.Children.Add(Card(new[]
            {
                Btn("Arrange",        "ArrangeTags",        BrAccentGreen, "Auto-arrange placed tags into aligned grid patterns"),
                Btn("Align bands",    "AlignTagBands",      BrAccentBlue,  "Align tags into horizontal bands by row"),
                Btn("Align tags H",   "AlignTagsH",         BrAccentBlue,  "Align selected tag heads horizontally"),
                Btn("Align tags V",   "AlignTagsV",         BrAccentBlue,  "Align selected tag heads vertically"),
                Btn("Distribute",     "DistributeViewports",BrAccentBlue,  "Distribute selected tags evenly"),
                Btn("Reset positions","ResetTagPositions",  BrAccentOrange,"Move tags back to element centres (remove manual offsets)"),
            }));

            stack.Children.Add(SectionLabel("MOVEMENT / FLIP"));
            stack.Children.Add(Card(new[]
            {
                Btn("Nudge",                "NudgeTags",            BrAccentBlue, "Fine-adjust tag positions by small increments"),
                Btn("Flip",                 "FlipTags",             BrAccentBlue, "Mirror tag position across element centre"),
                Btn("Toggle orientation",   "ToggleTagOrientation", BrAccentBlue, "Switch tags between horizontal and vertical"),
                Btn("Switch position",      "SwitchTagPos",         BrAccentBlue, "Cycle to next preferred tag position"),
                Btn("Pin / Unpin",          "PinTags",              BrAccentBlue, "Lock tags in place (or unlock) to prevent accidental moves"),
            }));

            stack.Children.Add(SectionLabel("LEADER & ELBOW"));
            stack.Children.Add(Card(new[]
            {
                Btn("Toggle leaders",   "ToggleLeaders",          BrAccentBlue,  "Toggle leaders on/off for selected tags"),
                Btn("Add leaders",      "AddLeaders",             BrAccentBlue,  "Add leaders to selected tags"),
                Btn("Remove leaders",   "RemoveLeaders",          BrAccentRed,   "Remove leaders from selected tags"),
                Btn("Adjust elbows",    "TagStudio_AdjustElbows", BrAccentBlue,  "Auto-adjust leader elbows for clean layout"),
                Btn("Snap elbows 45°",  "SnapElbow45",            BrAccentBlue,  "Snap leader elbows to 45° angles"),
                Btn("Snap elbows 90°",  "SnapElbow90",            BrAccentBlue,  "Snap leader elbows to 90° angles"),
                Btn("Snap straight",    "SnapElbowStraight",      BrAccentBlue,  "Straighten leader elbows"),
                Btn("Set arrowhead",    "TagStudio_SetArrows",    BrAccentBlue,  "Set arrowhead style on selected tags"),
            }));

            stack.Children.Add(SectionLabel("OVERLAP / COLLISION"));
            stack.Children.Add(Card(new[]
            {
                Btn("Overlap analysis", "TagOverlapAnalysis", BrAccentOrange, "Detect and report overlapping tags in active view"),
                Btn("Decluster",        "DeclusterTags",      BrAccentOrange, "Spread out densely-packed tags to reduce overlap"),
            }));

            return dock;
        }

        // 3. Style tab — typography / colour / paragraphs
        private static FrameworkElement BuildStyleTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("TYPOGRAPHY"));
            stack.Children.Add(Card(new[]
            {
                Btn("Apply tag style",      "ApplyTagStyle",          BrAccentGreen, "Multi-step picker: size → style → colour combination (128 combinations)"),
                Btn("Switch by discipline", "SwitchTagStyleByDisc",   BrAccentBlue,  "Switch tag types per discipline (M=blue, E=gold, P=green…)"),
                Btn("Batch text size",      "BatchTagTextSize",       BrAccentBlue,  "Set text size for all tags in view/selection"),
                Btn("Set tag line weight",  "SetTagLineWeight",       BrAccentBlue,  "Set line weight on selected tags"),
                Btn("Tag style report",     "TagStyleReport",         BrAccentBlue,  "Report current tag style status per element type"),
            }));

            stack.Children.Add(SectionLabel("COLOUR SCHEMES"));
            var schemes = new WrapPanel { Margin = new Thickness(2, 0, 2, 4) };
            void AddScheme(string label, string tag, byte r, byte g, byte b)
            {
                var btn = ColourChipBtn(label, tag, Color.FromRgb(r, g, b));
                schemes.Children.Add(btn);
            }
            AddScheme("Discipline","TagStudio_SchemeDiscipline", 0xFF, 0xE0, 0xB2);
            AddScheme("Warm",      "TagStudio_SchemeWarm",       0xFF, 0xCC, 0xBC);
            AddScheme("Cool",      "TagStudio_SchemeCool",       0xB2, 0xEB, 0xF2);
            AddScheme("Red",       "TagStudio_SchemeRed",        0xFF, 0xCD, 0xD2);
            AddScheme("Yellow",    "TagStudio_SchemeYellow",     0xFF, 0xF9, 0xC4);
            AddScheme("Blue",      "TagStudio_SchemeBlue",       0xBB, 0xDE, 0xFB);
            AddScheme("Mono",      "TagStudio_SchemeMono",       0xE0, 0xE0, 0xE0);
            AddScheme("Status",    "TagStudio_SchemeStatus",     0xD1, 0xC4, 0xE9);
            AddScheme("Zone",      "TagStudio_SchemeZone",       0xC8, 0xE6, 0xC9);
            AddScheme("Level",     "TagStudio_SchemeLevel",      0xB3, 0xE5, 0xFC);
            AddScheme("Function",  "TagStudio_SchemeFunction",   0xF0, 0xF4, 0xC3);
            stack.Children.Add(WrapInCard(schemes));

            stack.Children.Add(SectionLabel("APPLY / CLEAR"));
            stack.Children.Add(Card(new[]
            {
                Btn("Apply colour scheme",   "ApplyColorScheme",       BrAccentGreen, "Apply selected scheme to active view"),
                Btn("Clear colour scheme",   "ClearColorScheme",       BrAccentRed,   "Remove all graphic overrides from active view"),
                Btn("Colour by discipline",  "ColorTagsByDiscipline",  BrAccentBlue,  "Colour-code annotation tags by discipline"),
                Btn("Colour by parameter",   "ColorTagsByParam",       BrAccentBlue,  "Colour-code annotation tags by any parameter"),
                Btn("Colour by variable",    "ColorByVariable",        BrAccentBlue,  "Colour elements by any parameter value"),
                Btn("Batch apply scheme",    "BatchApplyColorScheme",  BrAccentBlue,  "Apply colour scheme across all views"),
            }));

            stack.Children.Add(SectionLabel("PARAGRAPH / NARRATIVE"));
            stack.Children.Add(Card(new[]
            {
                Btn("Set paragraph depth",   "SetParagraphDepth",     BrAccentBlue, "Set TAG7 paragraph depth tier (1-10)"),
                Btn("TAG7 heading style",    "SetTag7HeadingStyle",   BrAccentBlue, "Pick a TAG7 heading visual style"),
                Btn("Toggle warning vis.",   "ToggleWarningVisibility",BrAccentBlue,"Show/hide TAG7 warning paragraphs"),
                Btn("Set presentation mode", "SetPresentationMode",   BrAccentBlue, "Compact / Technical / Full Spec / Presentation / BOQ"),
            }));

            return dock;
        }

        // 4. Tokens tab — DISC/LOC/ZONE/STATUS, numbers, combine
        private static FrameworkElement BuildTokensTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("WRITE TOKENS"));
            stack.Children.Add(Card(new[]
            {
                Btn("Set discipline",      "SetDisc",        BrAccentGreen, "Set DISC (M, E, P, A, S, FP, LV)"),
                Btn("Set location",        "SetLoc",         BrAccentBlue,  "Set LOC (BLD1, BLD2, BLD3, EXT)"),
                Btn("Set zone",            "SetZone",        BrAccentBlue,  "Set ZONE (Z01-Z04)"),
                Btn("Set status",          "SetStatus",      BrAccentBlue,  "Set STATUS (NEW, EXISTING, DEMOLISHED, TEMPORARY)"),
                Btn("Set SEQ scheme",      "SetSeqScheme",   BrAccentBlue,  "Choose sequence numbering scheme"),
            }));

            stack.Children.Add(SectionLabel("ASSEMBLE / NUMBER"));
            stack.Children.Add(Card(new[]
            {
                Btn("Assign numbers",      "AssignNumbers",   BrAccentGreen, "Sequential numbering by DISC/SYS/LVL (continues from max existing)"),
                Btn("Build tags",          "BuildTags",       BrAccentGreen, "Rebuild ASS_TAG_1 from existing tokens"),
                Btn("Renumber tags",       "RenumberTags",    BrAccentBlue,  "Re-sequence tags within (DISC, SYS, LVL) groups"),
                Btn("Repair duplicate SEQ","RepairDuplicateSeq",BrAccentOrange, "Smart duplicate SEQ repair with spatial proximity"),
                Btn("Combine parameters",  "CombineParameters",BrAccentGreen, "Interactive multi-mode combine into all 36 tag containers"),
                Btn("Combine pre-flight",  "CombinePreFlight", BrAccentBlue,  "Audit before combining"),
            }));

            stack.Children.Add(SectionLabel("BULK / SYSTEM"));
            stack.Children.Add(Card(new[]
            {
                Btn("Bulk param write",    "BulkParamWrite",      BrAccentBlue, "Multi-page bulk operations: set LOC/ZONE/STATUS, auto-populate, clear, retag"),
                Btn("System param push",   "SystemParamPush",     BrAccentBlue, "MEP system parameter propagation to connected elements"),
                Btn("Batch system push",   "BatchSystemPush",     BrAccentBlue, "Push system parameters across all systems"),
                Btn("Sync param schema",   "SyncParameterSchema", BrAccentBlue, "Sync parameter schema (rename, remap, add)"),
            }));

            stack.Children.Add(SectionLabel("COMPLIANCE FIX"));
            stack.Children.Add(Card(new[]
            {
                Btn("Resolve all issues",  "ResolveAllIssues",     BrAccentGreen, "One-click ISO 19650 compliance resolution (500-element batched)"),
                Btn("Tag format migration","TagFormatMigration",   BrAccentBlue,  "Migrate existing tags to current format"),
                Btn("Tag changed",         "TagChanged",           BrAccentBlue,  "Detect 6 stale token types (LVL/LOC/ZONE/SYS/FUNC/PROD) and report mismatches"),
                Btn("Retag stale",         "RetagStale",           BrAccentOrange,"Re-derive tokens for elements marked STALE"),
            }));

            return dock;
        }

        // 5. Audit tab — validate / overlap / export
        private static FrameworkElement BuildAuditTab()
        {
            var dock = MakeTabContent(out StackPanel stack);

            stack.Children.Add(SectionLabel("VALIDATE"));
            stack.Children.Add(Card(new[]
            {
                Btn("Validate tags",         "ValidateTags",          BrAccentGreen, "Validate tag completeness against ISO 19650 codes"),
                Btn("Find duplicates",       "FindDuplicates",        BrAccentOrange,"Find duplicate tag values; select affected elements"),
                Btn("Highlight invalid",     "HighlightInvalid",      BrAccentOrange,"Colour-code missing (red) and incomplete (orange) tags"),
                Btn("Clear overrides",       "ClearOverrides",        BrAccentBlue,  "Reset graphic overrides in active view"),
                Btn("Pre-tag audit",         "PreTagAudit",           BrAccentBlue,  "Predict tag assignments / collisions / ISO violations"),
            }));

            stack.Children.Add(SectionLabel("DASHBOARD / METRICS"));
            stack.Children.Add(Card(new[]
            {
                Btn("Completeness dashboard","CompletenessDashboard", BrAccentGreen, "Per-discipline compliance dashboard with %"),
                Btn("Tag stats",             "TagStats",              BrAccentBlue,  "Quick tag counts by discipline/system/level for active view"),
                Btn("Tag style report",      "TagStyleReport",        BrAccentBlue,  "Report current tag style status per element type"),
                Btn("Quick tag preview",     "QuickTagPreview",       BrAccentBlue,  "Preview tags before committing"),
                Btn("Disc compliance",       "DiscCompliance",        BrAccentBlue,  "Per-discipline compliance breakdown"),
            }));

            stack.Children.Add(SectionLabel("EXPORT"));
            stack.Children.Add(Card(new[]
            {
                Btn("Audit to CSV",          "AuditTagsCSV",          BrAccentBlue,  "Export full tag audit to CSV"),
                Btn("Tag register",          "TagRegisterExport",     BrAccentBlue,  "Comprehensive asset register (40+ columns)"),
                Btn("Export positions",      "ExportTagPositions",    BrAccentBlue,  "Export tag positions for round-trip"),
                Btn("Export linked manifest","ExportLinkedManifest",  BrAccentBlue,  "Export linked-model tag manifest"),
                Btn("Export rich tag report","ExportRichTagReport",   BrAccentBlue,  "Detailed rich-tag report"),
                Btn("Export label guide",    "ExportLabelGuide",      BrAccentBlue,  "Export presentation-mode label guide"),
            }));

            stack.Children.Add(SectionLabel("INTELLIGENCE"));
            stack.Children.Add(Card(new[]
            {
                Btn("Tag rule engine",       "TagRuleEngine",         BrAccentBlue, "Define rules for automatic tag assignment"),
                Btn("Quality analysis",      "TagQualityAnalysis",    BrAccentBlue, "Analyse overall tagging quality"),
                Btn("Smart suggestion",      "TagSmartSuggestion",    BrAccentBlue, "Suggest improvements based on patterns"),
                Btn("Anomaly auto-fix",      "AnomalyAutoFix",        BrAccentOrange,"Detect and fix tag anomalies"),
            }));

            return dock;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the host for one tab: a DockPanel containing a ScrollViewer
        /// wrapping a StackPanel. The StackPanel is returned via the out param
        /// so callers can append section labels and cards to it directly.
        /// </summary>
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

        private static Border SectionLabel(string text, Dock? dock = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = BrHeader,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var b = new Border { Child = tb };
            if (dock.HasValue) DockPanel.SetDock(b, dock.Value);
            return b;
        }

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
                Width = 130,
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

        private static Button ColourChipBtn(string label, string tag, Color bg)
        {
            var b = new Button
            {
                Content = label,
                Width = 70,
                Height = 26,
                Margin = new Thickness(2),
                FontSize = 9,
                ToolTip = $"Apply '{label}' colour scheme to active view",
                Background = new SolidColorBrush(bg),
                Foreground = bg.R + bg.G + bg.B < 380 ? Brushes.White : Brushes.Black,
            };
            b.Click += (s, e) => DispatchWithFilter(label, tag);
            return b;
        }

        private static void DispatchWithFilter(string label, string tag)
        {
            try
            {
                if (string.Equals(tag, "<filter>", StringComparison.Ordinal))
                {
                    PushFilterToConfig();
                    var skipCount = TagConfig.CategorySkipList?.Count ?? 0;
                    var keptCount = _categoryCheckboxes.Count - skipCount;
                    SetFooter($"Category filter applied (in-memory): {keptCount} include, {skipCount} skip — next dispatched command will respect it");
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
                StingLog.Error($"TagCenter dispatch '{tag}'", ex);
                SetFooter($"✗ {label} failed: {ex.Message}");
            }
        }

        private static void SetFooter(string msg)
        {
            if (_txtFooterStatus == null) return;
            try { _txtFooterStatus.Text = msg; } catch (Exception ex) { StingLog.Warn($"TagCenter SetFooter: {ex.Message}"); }
        }
    }
}
