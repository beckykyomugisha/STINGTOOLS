// ─────────────────────────────────────────────────────────────
// StickyDashboardDialog.cs — WPF dashboard for sticky notes
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;
using Autodesk.Revit.DB;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    /// <summary>
    /// WPF dialog for viewing, filtering, and managing sticky notes.
    /// Shows notes in a DataGrid with status/priority filters and KPI cards.
    /// </summary>
    internal sealed class StickyDashboardDialog : Window
    {
        // ── Data model (mirrors StickyNote from ExLink) ──
        internal class NoteRow
        {
            public int Id { get; set; }
            public long ElementId { get; set; }
            public string ElementTag { get; set; }
            public string Note { get; set; }
            public string Priority { get; set; }
            public string Owner { get; set; }
            public string DueDate { get; set; }
            public string CreatedDate { get; set; }
            public string Status { get; set; }
            public string Category { get; set; }
        }

        // ── UI elements ──
        private readonly DataGrid _grid;
        private readonly ComboBox _statusFilter;
        private readonly ComboBox _priorityFilter;
        private readonly TextBox _searchBox;
        private readonly TextBlock _countText;
        private readonly List<NoteRow> _allNotes;

        /// <summary>Result action tag (e.g., "SelectElement_123") or null.</summary>
        public string ResultAction { get; private set; }

        /// <summary>Show the dashboard and return an action tag or null.</summary>
        public static string Show(List<NoteRow> notes, string title = "Sticky Notes Dashboard")
        {
            var dlg = new StickyDashboardDialog(notes, title);
            dlg.ShowDialog();
            return dlg.ResultAction;
        }

        private StickyDashboardDialog(List<NoteRow> notes, string title)
        {
            _allNotes = notes ?? new List<NoteRow>();

            int total = _allNotes.Count;
            int open = _allNotes.Count(n => n.Status == "OPEN");
            int overdue = _allNotes.Count(n =>
            {
                if (string.IsNullOrEmpty(n.DueDate) || n.Status == "CLOSED") return false;
                return DateTime.TryParse(n.DueDate, out var due) && due < DateTime.Today;
            });
            int critical = _allNotes.Count(n => n.Priority == "CRITICAL" && n.Status != "CLOSED");

            Title = $"STING — {title} ({total} notes, {overdue} overdue)";
            Width = 900;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = ThemeManager.GetBrush("AltRowBg");

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this);
                hwnd.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"StickyDashboard owner set failed: {ex.Message}"); }

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // ── Header ──
            var header = new Border
            {
                Background = ThemeManager.GetBrush("HeaderBg"),
                Padding = new Thickness(12, 8, 12, 8)
            };
            header.Child = new TextBlock
            {
                Text = "Sticky Notes Dashboard",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Brushes.White
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // ── KPI cards ──
            var kpiPanel = new WrapPanel { Margin = new Thickness(8, 6, 8, 2) };
            kpiPanel.Children.Add(MakeKPICard("Total", total.ToString(), "#1565C0"));
            kpiPanel.Children.Add(MakeKPICard("Open", open.ToString(), "#E8912D"));
            kpiPanel.Children.Add(MakeKPICard("Overdue", overdue.ToString(), overdue > 0 ? "#D32F2F" : "#4CAF50"));
            kpiPanel.Children.Add(MakeKPICard("Critical", critical.ToString(), critical > 0 ? "#D32F2F" : "#4CAF50"));
            Grid.SetRow(kpiPanel, 1);
            mainGrid.Children.Add(kpiPanel);

            // ── Filters ──
            var filterPanel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };

            filterPanel.Children.Add(new TextBlock { Text = "Status:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _statusFilter = new ComboBox { Width = 100, Height = 24, FontSize = 11 };
            _statusFilter.Items.Add("All");
            _statusFilter.Items.Add("OPEN");
            _statusFilter.Items.Add("IN_PROGRESS");
            _statusFilter.Items.Add("RESOLVED");
            _statusFilter.Items.Add("CLOSED");
            _statusFilter.SelectedIndex = 0;
            _statusFilter.SelectionChanged += (s, e) => ApplyFilters();
            filterPanel.Children.Add(_statusFilter);

            filterPanel.Children.Add(new TextBlock { Text = "  Priority:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            _priorityFilter = new ComboBox { Width = 100, Height = 24, FontSize = 11 };
            _priorityFilter.Items.Add("All");
            _priorityFilter.Items.Add("CRITICAL");
            _priorityFilter.Items.Add("HIGH");
            _priorityFilter.Items.Add("MEDIUM");
            _priorityFilter.Items.Add("LOW");
            _priorityFilter.SelectedIndex = 0;
            _priorityFilter.SelectionChanged += (s, e) => ApplyFilters();
            filterPanel.Children.Add(_priorityFilter);

            filterPanel.Children.Add(new TextBlock { Text = "  Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            _searchBox = new TextBox { Width = 180, Height = 24, FontSize = 11, Padding = new Thickness(4, 2, 4, 2) };
            _searchBox.TextChanged += (s, e) => ApplyFilters();
            filterPanel.Children.Add(_searchBox);

            _countText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 11, Foreground = Brushes.Gray };
            filterPanel.Children.Add(_countText);

            Grid.SetRow(filterPanel, 2);
            mainGrid.Children.Add(filterPanel);

            // ── DataGrid ──
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                FontSize = 11,
                Margin = new Thickness(8, 4, 8, 4),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                CanUserSortColumns = true
            };
            _grid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id"), Width = 40 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Element", Binding = new Binding("ElementTag"), Width = 120 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Note", Binding = new Binding("Note"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Binding("Priority"), Width = 70 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 80 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Binding("Owner"), Width = 80 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Due", Binding = new Binding("DueDate"), Width = 80 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Created", Binding = new Binding("CreatedDate"), Width = 80 });
            _grid.MouseDoubleClick += OnGridDoubleClick;

            Grid.SetRow(_grid, 3);
            mainGrid.Children.Add(_grid);

            // ── Buttons ──
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 4, 8, 8)
            };
            var btnSelect = new Button { Content = "Select Element", Width = 110, Height = 28, Margin = new Thickness(0, 0, 6, 0) };
            btnSelect.Click += (s, e) =>
            {
                if (_grid.SelectedItem is NoteRow row)
                {
                    ResultAction = $"SelectElement_{row.ElementId}";
                    DialogResult = true;
                }
            };
            btnPanel.Children.Add(btnSelect);

            var btnClose = new Button { Content = "Close", Width = 80, Height = 28, IsCancel = true };
            btnPanel.Children.Add(btnClose);

            Grid.SetRow(btnPanel, 4);
            mainGrid.Children.Add(btnPanel);

            Content = mainGrid;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var status = _statusFilter.SelectedItem?.ToString() ?? "All";
            var priority = _priorityFilter.SelectedItem?.ToString() ?? "All";
            var search = _searchBox.Text?.Trim().ToLowerInvariant() ?? "";

            var filtered = _allNotes.AsEnumerable();

            if (status != "All")
                filtered = filtered.Where(n => n.Status == status);
            if (priority != "All")
                filtered = filtered.Where(n => n.Priority == priority);
            if (!string.IsNullOrEmpty(search))
                filtered = filtered.Where(n =>
                    (n.Note ?? "").ToLowerInvariant().Contains(search) ||
                    (n.ElementTag ?? "").ToLowerInvariant().Contains(search) ||
                    (n.Owner ?? "").ToLowerInvariant().Contains(search) ||
                    (n.Category ?? "").ToLowerInvariant().Contains(search));

            var list = filtered.ToList();
            _grid.ItemsSource = list;
            _countText.Text = $"{list.Count} of {_allNotes.Count} notes";
        }

        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_grid.SelectedItem is NoteRow row)
            {
                ResultAction = $"SelectElement_{row.ElementId}";
                DialogResult = true;
            }
        }

        private static Border MakeKPICard(string label, string value, string colorHex)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = Brushes.White,
                MinWidth = 90
            };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            border.Child = sp;
            return border;
        }
    }
}
