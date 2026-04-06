using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StingTools.UI
{
    /// <summary>
    /// ISO 19650 AEC/FM multi-role assignee picker dialog.
    /// Displays roles grouped by discipline with real-time search filtering.
    /// </summary>
    internal class StingRolePickerDialog : Window
    {
        // ── Theme colours matching BCC ──
        private static readonly Color CNavy  = Color.FromRgb(0x1E, 0x3A, 0x5F);
        private static readonly Color CAmber = Color.FromRgb(0xE8, 0xA0, 0x20);
        private static readonly Color CGrey  = Color.FromRgb(0x78, 0x78, 0x78);
        private static readonly Color CPageBg = Color.FromRgb(0xF4, 0xF5, 0xF7);

        private static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>The roles selected by the user (populated when dialog closes with OK).</summary>
        public List<string> SelectedRoles { get; private set; } = new List<string>();

        // ── Role groups data ──
        private static readonly List<(string GroupName, List<string> Roles)> RoleGroups = new List<(string, List<string>)>
        {
            ("Client/Employer", new List<string>
            {
                "Client Representative (K)",
                "Employer Representative",
                "Funder",
                "Building Owner"
            }),
            ("Design Team", new List<string>
            {
                "Architect (A)",
                "Structural Engineer (S)",
                "Mechanical Engineer (M)",
                "Electrical Engineer (E)",
                "HVAC Engineer (H)",
                "Public Health Engineer (P)",
                "Civil Engineer (C)",
                "Landscape Architect (L)",
                "Interior Designer (I)"
            }),
            ("BIM/Digital", new List<string>
            {
                "BIM Manager",
                "BIM Coordinator",
                "Information Manager (I)",
                "Digital Engineer"
            }),
            ("Construction", new List<string>
            {
                "Contractor (W)",
                "Site Manager",
                "Project Manager",
                "Quantity Surveyor (Q)"
            }),
            ("Specialist Trades", new List<string>
            {
                "Fire Protection",
                "Acoustics",
                "Façade",
                "Vertical Transport"
            }),
            ("FM/Operations", new List<string>
            {
                "Facilities Manager (F)",
                "Operations Manager",
                "Asset Manager"
            }),
            ("Compliance/QA", new List<string>
            {
                "QA/QC Manager",
                "CDM Coordinator",
                "Health & Safety"
            }),
            ("Project Management", new List<string>
            {
                "Programme Manager",
                "Risk Manager",
                "Commercial Manager"
            })
        };

        // Track checkboxes per group for "Select All" feature
        private readonly List<(string GroupName, List<CheckBox> CheckBoxes)> _groupCheckBoxes
            = new List<(string, List<CheckBox>)>();

        private TextBox _searchBox;
        private StackPanel _rolesPanel;

        /// <summary>
        /// Creates the dialog with optional pre-selected roles.
        /// </summary>
        /// <param name="preSelected">Roles that should be pre-checked (by role name).</param>
        public StingRolePickerDialog(List<string> preSelected = null)
        {
            Title = "Assign Roles — ISO 19650 AEC/FM";
            Width = 600;
            Height = 700;
            MinWidth = 480;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Br(CPageBg);
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            try
            {
                var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(this).Owner = handle;
            }
            catch { /* ignore */ }

            BuildUI(preSelected ?? new List<string>());
        }

        private void BuildUI(List<string> preSelected)
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });  // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });  // search
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // role list
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });  // footer

            // ── Header ──
            var header = new Border
            {
                Background = Br(CNavy),
                Padding = new Thickness(16, 0, 16, 0)
            };
            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(new TextBlock
            {
                Text = "Assign Roles",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "ISO 19650 AEC/FM",
                Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Search bar ──
            var searchBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var searchStack = new StackPanel { Orientation = Orientation.Horizontal };
            searchStack.Children.Add(new TextBlock
            {
                Text = "🔍",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            _searchBox = new TextBox
            {
                Width = 480,
                Height = 26,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            _searchBox.TextChanged += OnSearchChanged;
            searchStack.Children.Add(_searchBox);
            searchBorder.Child = searchStack;
            Grid.SetRow(searchBorder, 1);
            root.Children.Add(searchBorder);

            // ── Role groups scroll area ──
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12, 8, 12, 8)
            };
            _rolesPanel = new StackPanel();
            BuildRoleGroups(preSelected);
            scroll.Content = _rolesPanel;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            // ── Footer ──
            var footer = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };
            var footerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                Foreground = new SolidColorBrush(CGrey),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };

            var okBtn = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 32,
                Background = Br(CNavy),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            okBtn.Click += OnOKClicked;

            footerRow.Children.Add(cancelBtn);
            footerRow.Children.Add(okBtn);
            footer.Child = footerRow;
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;

            // Keyboard: Enter = OK, Esc = Cancel
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); }
                if (e.Key == System.Windows.Input.Key.Enter) { OnOKClicked(null, null); }
            };
        }

        private void BuildRoleGroups(List<string> preSelected)
        {
            _groupCheckBoxes.Clear();
            _rolesPanel.Children.Clear();

            foreach (var (groupName, roles) in RoleGroups)
            {
                var groupCheckBoxes = new List<CheckBox>();

                // Group header with "Select All" link
                var groupHeader = new Grid { Margin = new Thickness(0, 8, 0, 2) };
                groupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                groupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var groupLabel = new TextBlock
                {
                    Text = groupName,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Br(CNavy),
                    VerticalAlignment = VerticalAlignment.Center
                };
                groupHeader.Children.Add(groupLabel);

                var selectAllLink = new TextBlock
                {
                    Text = "Select All",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    TextDecorations = TextDecorations.Underline
                };
                string capturedGroup = groupName; // capture for lambda
                selectAllLink.MouseLeftButtonDown += (s, e) => ToggleGroupAll(capturedGroup, true);
                Grid.SetColumn(selectAllLink, 1);
                groupHeader.Children.Add(selectAllLink);

                _rolesPanel.Children.Add(groupHeader);

                // Amber underline
                _rolesPanel.Children.Add(new Border
                {
                    BorderBrush = Br(CAmber),
                    BorderThickness = new Thickness(0, 0, 0, 1.5),
                    Margin = new Thickness(0, 0, 0, 4)
                });

                // Role checkboxes
                var groupBox = new StackPanel { Margin = new Thickness(8, 0, 0, 4) };
                foreach (string role in roles)
                {
                    var cb = new CheckBox
                    {
                        Content = role,
                        FontSize = 12,
                        Margin = new Thickness(0, 2, 0, 2),
                        IsChecked = preSelected.Any(p => string.Equals(p, role, StringComparison.OrdinalIgnoreCase))
                    };
                    groupCheckBoxes.Add(cb);
                    groupBox.Children.Add(cb);
                }

                _rolesPanel.Children.Add(groupBox);
                _groupCheckBoxes.Add((groupName, groupCheckBoxes));
            }
        }

        private void ToggleGroupAll(string groupName, bool check)
        {
            var group = _groupCheckBoxes.FirstOrDefault(g => g.GroupName == groupName);
            foreach (var cb in group.CheckBoxes)
            {
                if (cb.Visibility == Visibility.Visible)
                    cb.IsChecked = check;
            }
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            string query = _searchBox.Text?.Trim() ?? "";
            bool showAll = string.IsNullOrEmpty(query);

            foreach (var (groupName, checkBoxes) in _groupCheckBoxes)
            {
                foreach (var cb in checkBoxes)
                {
                    if (showAll)
                    {
                        cb.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        string roleText = (cb.Content as string) ?? "";
                        cb.Visibility = roleText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }
                }
            }
        }

        private void OnOKClicked(object sender, RoutedEventArgs e)
        {
            SelectedRoles = _groupCheckBoxes
                .SelectMany(g => g.CheckBoxes)
                .Where(cb => cb.IsChecked == true)
                .Select(cb => (cb.Content as string) ?? "")
                .Where(r => !string.IsNullOrEmpty(r))
                .ToList();

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Shows the role picker and returns the selected roles, or null if cancelled.
        /// </summary>
        /// <param name="preSelected">Optional list of pre-checked role names.</param>
        internal static List<string> Show(List<string> preSelected = null)
        {
            var dlg = new StingRolePickerDialog(preSelected);
            return dlg.ShowDialog() == true ? dlg.SelectedRoles : null;
        }
    }
}
