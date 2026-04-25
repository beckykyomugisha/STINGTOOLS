using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned by CombineConfigDialog.
    /// </summary>
    public class CombineConfigResult
    {
        public HashSet<string> SelectedGroupCodes { get; set; } = new();
        public bool Cancelled { get; set; } = true;
    }

    /// <summary>
    /// Unified WPF dialog for Combine Parameters configuration.
    /// Left panel: mode selector (All / Universal / Discipline / Custom).
    /// Right panel: container group tree with search (enabled in Custom mode).
    /// Bottom info bar: selection summary + OK/Cancel.
    /// Replaces the 2-step StingModePicker + StingListPicker flow.
    /// </summary>
    public class CombineConfigDialog : Window
    {
        // ── Data model ───────────────────────────────────────────────
        public class GroupItem
        {
            public string GroupCode { get; set; }
            public string GroupName { get; set; }
            public int ParamCount { get; set; }
            public int ElementCount { get; set; }
            public bool IsChecked { get; set; } = true;
        }

        // ── Theme (light, contrast-safe; flipped from old dark palette) ──
        private static readonly Color BgColor = Color.FromRgb(0xFA, 0xFA, 0xFA);          // window bg
        private static readonly Color PanelBg = Color.FromRgb(0xFF, 0xFF, 0xFF);          // card/input bg
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);      // STING orange
        private static readonly Color FgColor = Color.FromRgb(0x22, 0x22, 0x22);          // body text
        private static readonly Color DimFg = Color.FromRgb(0x66, 0x66, 0x66);            // muted text
        private static readonly Color BorderClr = Color.FromRgb(0xCF, 0xD8, 0xDC);        // subtle border

        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }
        private static SolidColorBrush FZ(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }
        private static readonly SolidColorBrush BgBrush = FZ(new(BgColor));
        private static readonly SolidColorBrush PanelBrush = FZ(new(PanelBg));
        private static readonly SolidColorBrush AccentBrush = FZ(new(AccentColor));
        private static readonly SolidColorBrush FgBrush = FZ(new(FgColor));
        private static readonly SolidColorBrush DimBrush = FZ(new(DimFg));
        private static readonly new SolidColorBrush BorderBrush = FZ(new(BorderClr));
        // Zebra / darker rows re-mapped to light greys so body text reads.
        private static readonly SolidColorBrush BrDark25 = FZ(0xF0, 0xF0, 0xF0);
        private static readonly SolidColorBrush BrDark1E = FZ(0xE5, 0xE5, 0xE5);
        private static readonly SolidColorBrush BrMid50 = FZ(0xD0, 0xD0, 0xD0);
        private static readonly HashSet<string> _universalSet = new HashSet<string> { "UNIVERSAL" };

        // ── Controls ─────────────────────────────────────────────────
        private readonly List<GroupItem> _allGroups;
        private readonly RadioButton _rbAll;
        private readonly RadioButton _rbUniversal;
        private readonly RadioButton _rbDiscipline;
        private readonly RadioButton _rbCustom;
        private readonly StackPanel _groupPanel;
        private readonly TextBox _searchBox;
        private readonly Button _selectAllBtn;
        private readonly Button _clearAllBtn;
        private readonly TextBlock _infoText;
        private readonly ScrollViewer _groupScroll;
        private readonly Border _rightBorder;
        private readonly List<CheckBox> _groupCheckBoxes = new();

        private CombineConfigResult _result;

        // ── Constructor ──────────────────────────────────────────────

        private CombineConfigDialog(
            ParamRegistry.ContainerGroupDef[] groups,
            Dictionary<string, int> elementCounts)
        {
            _allGroups = groups.Select(g => new GroupItem
            {
                GroupCode = g.GroupCode,
                GroupName = g.Group,
                ParamCount = g.Params.Length,
                ElementCount = g.Categories != null
                    ? g.Categories.Sum(c => elementCounts.TryGetValue(c, out int n) ? n : 0)
                    : elementCounts.Values.Sum(),
                IsChecked = true
            }).ToList();

            Title = "Combine Parameters — Configuration";
            Width = 700;
            Height = 500;
            MinWidth = 650;
            MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BgBrush;
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            // Set Revit as owner for modality
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CombineConfigDialog owner: {ex.Message}");
            }

            // ── Root layout ──────────────────────────────────────────
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Title
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Info bar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Buttons

            // ── Title bar ────────────────────────────────────────────
            var titleBar = new Border
            {
                Background = BrDark25,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var titlePanel = new StackPanel();
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Combine Parameters",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Assemble token parameters into tag containers",
                FontSize = 11,
                Foreground = DimBrush,
                Margin = new Thickness(0, 2, 0, 0)
            });
            titleBar.Child = titlePanel;
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            // ── Body: left + right panels ────────────────────────────
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // LEFT PANEL — Mode selector
            var leftBorder = new Border
            {
                Background = PanelBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(12, 12, 12, 12)
            };
            var leftStack = new StackPanel();
            leftStack.Children.Add(new TextBlock
            {
                Text = "MODE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = AccentBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });

            int totalContainers = groups.Sum(g => g.Params.Length);

            _rbAll = CreateModeRadio(
                $"All Containers",
                $"All {groups.Length} groups ({totalContainers} parameters)",
                true);
            _rbUniversal = CreateModeRadio(
                "Universal Only (ASS_TAG_1-6)",
                "6 universal containers for all tagged elements",
                false);
            _rbDiscipline = CreateModeRadio(
                "Discipline Only",
                "MEP + Comms containers (excludes Universal, Material)",
                false);
            _rbCustom = CreateModeRadio(
                "Custom Selection",
                "Choose specific groups from the list",
                false);

            leftStack.Children.Add(_rbAll);
            leftStack.Children.Add(_rbUniversal);
            leftStack.Children.Add(_rbDiscipline);
            leftStack.Children.Add(_rbCustom);

            _rbAll.Checked += (s, e) => UpdateRightPanel();
            _rbUniversal.Checked += (s, e) => UpdateRightPanel();
            _rbDiscipline.Checked += (s, e) => UpdateRightPanel();
            _rbCustom.Checked += (s, e) => UpdateRightPanel();

            leftBorder.Child = leftStack;
            Grid.SetColumn(leftBorder, 0);
            body.Children.Add(leftBorder);

            // RIGHT PANEL — Container group list
            _rightBorder = new Border
            {
                Padding = new Thickness(12, 12, 12, 12)
            };
            var rightStack = new DockPanel { LastChildFill = true };

            // Top: label + search + select/clear buttons
            var topBar = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            headerRow.Children.Add(new TextBlock
            {
                Text = "CONTAINER GROUPS",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = AccentBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _selectAllBtn = CreateSmallButton("Select All");
            _clearAllBtn = CreateSmallButton("Clear All");
            _selectAllBtn.Click += (s, e) => SetAllChecked(true);
            _clearAllBtn.Click += (s, e) => SetAllChecked(false);
            btnPanel.Children.Add(_selectAllBtn);
            btnPanel.Children.Add(_clearAllBtn);
            DockPanel.SetDock(btnPanel, Dock.Right);
            headerRow.Children.Add(btnPanel);

            topBar.Children.Add(headerRow);

            _searchBox = new TextBox
            {
                Height = 26,
                FontSize = 12,
                Background = BrDark1E,
                Foreground = FgBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Search groups..."
            };
            // Placeholder text via GotFocus/LostFocus
            _searchBox.Text = "Search groups...";
            _searchBox.Foreground = DimBrush;
            _searchBox.GotFocus += (s, e) =>
            {
                if (_searchBox.Text == "Search groups...")
                {
                    _searchBox.Text = "";
                    _searchBox.Foreground = FgBrush;
                }
            };
            _searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(_searchBox.Text))
                {
                    _searchBox.Text = "Search groups...";
                    _searchBox.Foreground = DimBrush;
                }
            };
            _searchBox.TextChanged += (s, e) => FilterGroups();
            topBar.Children.Add(_searchBox);

            DockPanel.SetDock(topBar, Dock.Top);
            rightStack.Children.Add(topBar);

            // Scrollable group list
            _groupPanel = new StackPanel();
            _groupScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _groupPanel
            };
            rightStack.Children.Add(_groupScroll);

            _rightBorder.Child = rightStack;
            Grid.SetColumn(_rightBorder, 1);
            body.Children.Add(_rightBorder);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // ── Info bar ─────────────────────────────────────────────
            var infoBorder = new Border
            {
                Background = BrDark25,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            _infoText = new TextBlock
            {
                FontSize = 11,
                Foreground = DimBrush
            };
            infoBorder.Child = _infoText;
            Grid.SetRow(infoBorder, 2);
            root.Children.Add(infoBorder);

            // ── OK / Cancel buttons ──────────────────────────────────
            var btnBar = new Border
            {
                Background = BrDark25,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var btnBarPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                FontSize = 12,
                Background = BrMid50,
                Foreground = FgBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                IsCancel = true
            };
            cancelBtn.Click += (s, e) => { _result = new CombineConfigResult { Cancelled = true }; Close(); };

            var okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = AccentBrush,
                Foreground = BrDark1E,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            okBtn.Click += (s, e) => OnOk();

            btnBarPanel.Children.Add(cancelBtn);
            btnBarPanel.Children.Add(okBtn);
            btnBar.Child = btnBarPanel;
            Grid.SetRow(btnBar, 3);
            root.Children.Add(btnBar);

            Content = root;

            // Build group checkboxes and set initial state
            BuildGroupCheckBoxes();
            UpdateRightPanel();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private RadioButton CreateModeRadio(string header, string description, bool isChecked)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(new TextBlock
            {
                Text = header,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FgBrush
            });
            panel.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = DimBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

            var rb = new RadioButton
            {
                Content = panel,
                GroupName = "CombineMode",
                IsChecked = isChecked,
                Foreground = FgBrush,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(4, 4, 4, 4)
            };
            return rb;
        }

        private Button CreateSmallButton(string text)
        {
            return new Button
            {
                Content = text,
                FontSize = 10,
                Height = 22,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 0, 0, 0),
                Background = BrMid50,
                Foreground = FgBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private void BuildGroupCheckBoxes()
        {
            _groupPanel.Children.Clear();
            _groupCheckBoxes.Clear();

            foreach (var g in _allGroups)
            {
                var cb = new CheckBox
                {
                    IsChecked = g.IsChecked,
                    Foreground = FgBrush,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = g
                };

                var contentPanel = new StackPanel { Orientation = Orientation.Horizontal };
                contentPanel.Children.Add(new TextBlock
                {
                    Text = g.GroupName,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = FgBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"  {g.ParamCount} params | {g.ElementCount} elements",
                    FontSize = 10,
                    Foreground = DimBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });

                cb.Content = contentPanel;
                cb.Checked += (s, e) => { g.IsChecked = true; UpdateInfoBar(); };
                cb.Unchecked += (s, e) => { g.IsChecked = false; UpdateInfoBar(); };

                _groupCheckBoxes.Add(cb);
                _groupPanel.Children.Add(cb);
            }
        }

        private void FilterGroups()
        {
            string query = _searchBox.Text;
            if (query == "Search groups..." || string.IsNullOrWhiteSpace(query))
            {
                foreach (var cb in _groupCheckBoxes)
                    cb.Visibility = Visibility.Visible;
                return;
            }

            query = query.ToLowerInvariant();
            for (int i = 0; i < _groupCheckBoxes.Count; i++)
            {
                var g = _allGroups[i];
                bool match = g.GroupName.ToLowerInvariant().Contains(query)
                          || g.GroupCode.ToLowerInvariant().Contains(query);
                _groupCheckBoxes[i].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetAllChecked(bool check)
        {
            foreach (var cb in _groupCheckBoxes)
            {
                if (cb.Visibility == Visibility.Visible)
                    cb.IsChecked = check;
            }
        }

        private void UpdateRightPanel()
        {
            bool isCustom = _rbCustom.IsChecked == true;
            _rightBorder.IsEnabled = isCustom;
            _rightBorder.Opacity = isCustom ? 1.0 : 0.4;
            UpdateInfoBar();
        }

        private void UpdateInfoBar()
        {
            var selected = GetSelectedGroups();
            int groupCount = selected.Count;
            int paramCount = _allGroups
                .Where(g => selected.Contains(g.GroupCode))
                .Sum(g => g.ParamCount);
            int elemCount = _allGroups
                .Where(g => selected.Contains(g.GroupCode))
                .Max(g => g.ElementCount); // Use max since elements overlap across groups

            _infoText.Text = $"{groupCount} groups selected, {paramCount} total parameters, {elemCount} elements affected";
        }

        private HashSet<string> GetSelectedGroups()
        {
            if (_rbAll.IsChecked == true)
                return new HashSet<string>(_allGroups.Select(g => g.GroupCode));
            if (_rbUniversal.IsChecked == true)
                return _universalSet;
            if (_rbDiscipline.IsChecked == true)
                return new HashSet<string>(
                    _allGroups.Where(g => g.GroupCode != "UNIVERSAL" && g.GroupCode != "MAT_TAG")
                              .Select(g => g.GroupCode));
            // Custom
            return new HashSet<string>(
                _allGroups.Where(g => g.IsChecked).Select(g => g.GroupCode));
        }

        private void OnOk()
        {
            var selected = GetSelectedGroups();
            if (selected.Count == 0)
            {
                MessageBox.Show("No container groups selected.", "STING",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _result = new CombineConfigResult
            {
                SelectedGroupCodes = selected,
                Cancelled = false
            };
            Close();
        }

        // ── Static entry point ───────────────────────────────────────

        /// <summary>
        /// Show the Combine Configuration dialog.
        /// </summary>
        /// <param name="groups">All container group definitions from ParamRegistry.</param>
        /// <param name="elementCounts">Category name to element count mapping.</param>
        /// <returns>Result with selected group codes, or Cancelled=true.</returns>
        public static CombineConfigResult Show(
            ParamRegistry.ContainerGroupDef[] groups,
            Dictionary<string, int> elementCounts)
        {
            var dlg = new CombineConfigDialog(groups, elementCounts);
            StingWindowHelper.ApplyOwner(dlg);
            dlg.ShowDialog();
            return dlg._result ?? new CombineConfigResult { Cancelled = true };
        }
    }
}
