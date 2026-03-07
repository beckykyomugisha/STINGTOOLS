using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING Tools dockable panel.
    /// Unified 6-tab layout: SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW.
    /// All button clicks dispatched via IExternalEventHandler for thread safety.
    /// Tab content is lazily loaded to prevent stack overflow (0xC00000FD) from
    /// deep WPF visual tree measure/arrange passes on Revit's limited stack.
    /// </summary>
    public partial class StingDockPanel : Page
    {
        private static ExternalEvent _externalEvent;
        private static StingCommandHandler _handler;
        private static UIApplication _uiApp;
        private static StingDockPanel _instance;

        private static readonly Dictionary<string, List<int>> SelectionMemory =
            new Dictionary<string, List<int>>();

        /// <summary>
        /// Stores detached tab content for lazy loading. Key = tab index.
        /// Content is created by InitializeComponent but removed from the visual tree
        /// until the user selects the tab, preventing stack overflow during layout.
        /// </summary>
        private readonly Dictionary<int, object> _deferredTabContent = new();

        public StingDockPanel()
        {
            InitializeComponent();

            // Defer non-active tab content to prevent stack overflow.
            // InitializeComponent creates all elements in memory, but we detach
            // tabs 1-5 from the visual tree so only the SELECT tab (index 0)
            // goes through measure/arrange on initial load.
            for (int i = 1; i < tabMain.Items.Count; i++)
            {
                if (tabMain.Items[i] is TabItem tab && tab.Content != null)
                {
                    _deferredTabContent[i] = tab.Content;
                    tab.Content = null;
                }
            }

            // Color swatches can be built now — the named panel elements exist in
            // memory even though they're detached from the visual tree. When the
            // VIEW tab is re-attached, the swatches will be visible.
            BuildColorSwatches();
            _instance = this;
        }

        /// <summary>Initialise the external event handler — called once from OnStartup.</summary>
        public static void Initialise(UIControlledApplication app)
        {
            _handler = new StingCommandHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <summary>Store UIApplication reference when available.</summary>
        public static void SetUIApplication(UIApplication uiApp)
        {
            _uiApp = uiApp;
        }

        // ── Unified button click dispatcher ──────────────────────────────

        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cmdTag)
            {
                _handler?.SetCommand(cmdTag);
                _externalEvent?.Raise();
                UpdateStatus($"Running: {cmdTag}...");
            }
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            // Pin toggle is handled by Revit docking framework
        }

        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != tabMain) return; // ignore child SelectionChanged bubbling

            int idx = tabMain.SelectedIndex;
            if (_deferredTabContent != null && _deferredTabContent.TryGetValue(idx, out object content))
            {
                // Re-attach the deferred content on first selection.
                // This spreads the measure/arrange cost across user interactions
                // instead of hitting the stack all at once on panel load.
                if (tabMain.Items[idx] is TabItem tab)
                {
                    tab.Content = content;
                    _deferredTabContent.Remove(idx);
                }
            }
        }

        // ── Bulk Parameter Write ──────────────────────────────────────

        private void BtnRefreshParams_Click(object sender, RoutedEventArgs e)
        {
            _handler?.SetCommand("RefreshParamList");
            _externalEvent?.Raise();
        }

        private void BtnBulkPreview_Click(object sender, RoutedEventArgs e)
        {
            string param = cmbBulkParam?.Text ?? "";
            string value = txtBulkValue?.Text ?? "";
            _handler?.SetCommand("BulkPreview", param, value);
            _externalEvent?.Raise();
        }

        private void BtnBulkWrite_Click(object sender, RoutedEventArgs e)
        {
            string param = cmbBulkParam?.Text ?? "";
            string value = txtBulkValue?.Text ?? "";
            _handler?.SetCommand("BulkWrite", param, value);
            _externalEvent?.Raise();
        }

        private void BtnBulkClear_Click(object sender, RoutedEventArgs e)
        {
            string param = cmbBulkParam?.Text ?? "";
            _handler?.SetCommand("BulkClear", param, "");
            _externalEvent?.Raise();
        }

        // ── Colour swatches ──────────────────────────────────────────

        private void BuildColorSwatches()
        {
            string[] fillColors = {
                "#F44336", "#E91E63", "#9C27B0", "#673AB7",
                "#3F51B5", "#2196F3", "#03A9F4", "#00BCD4",
                "#009688", "#4CAF50", "#8BC34A", "#CDDC39",
                "#FFEB3B", "#FFC107", "#FF9800", "#FF5722",
                "#795548", "#9E9E9E", "#607D8B", "#000000"
            };

            foreach (string hex in fillColors)
            {
                var swatch = new Border
                {
                    Width = 18, Height = 18,
                    Margin = new Thickness(1),
                    Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = hex,
                    ToolTip = hex
                };
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is Border b && b.Tag is string h)
                    {
                        txtHexColorView.Text = h.TrimStart('#');
                        brdColorPreviewView.Background =
                            (SolidColorBrush)new BrushConverter().ConvertFromString(h);
                    }
                };
                pnlSwatchesView?.Children.Add(swatch);
            }

            string[] outlineColors = {
                "#F44336", "#E91E63", "#9C27B0", "#3F51B5",
                "#2196F3", "#009688", "#4CAF50", "#FF9800",
                "#795548", "#000000", "#FFFFFF", "#9E9E9E"
            };

            foreach (string hex in outlineColors)
            {
                var swatch = new Border
                {
                    Width = 18, Height = 18,
                    Margin = new Thickness(1),
                    Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = hex,
                    ToolTip = hex
                };
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is Border b && b.Tag is string h)
                    {
                        brdOutlineColorView.Background =
                            (SolidColorBrush)new BrushConverter().ConvertFromString(h);
                    }
                };
                pnlOutlineSwatchesView?.Children.Add(swatch);
            }
        }

        // ── Status bar helper ──────────────────────────────────────

        public void UpdateStatus(string message)
        {
            if (txtStatus == null) return;
            if (!txtStatus.Dispatcher.CheckAccess())
            {
                txtStatus.Dispatcher.Invoke(() => txtStatus.Text = message);
                return;
            }
            txtStatus.Text = message;
        }

        public void UpdateBulkStatus(string message)
        {
            if (txtBulkStatus == null) return;
            if (!txtBulkStatus.Dispatcher.CheckAccess())
            {
                txtBulkStatus.Dispatcher.Invoke(() => txtBulkStatus.Text = message);
                return;
            }
            txtBulkStatus.Text = message;
        }

        /// <summary>
        /// ENH-003: Static method to update compliance status bar from command handler.
        /// </summary>
        public static void UpdateComplianceStatus(string statusText, string ragStatus)
        {
            if (_instance?.txtStatus == null) return;
            try
            {
                var brush = ragStatus switch
                {
                    "GREEN" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    "AMBER" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    "RED" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    _ => new SolidColorBrush(Color.FromRgb(206, 147, 216)),
                };

                if (!_instance.txtStatus.Dispatcher.CheckAccess())
                {
                    _instance.txtStatus.Dispatcher.Invoke(() =>
                    {
                        _instance.txtStatus.Text = statusText;
                        _instance.txtStatus.Foreground = brush;
                    });
                }
                else
                {
                    _instance.txtStatus.Text = statusText;
                    _instance.txtStatus.Foreground = brush;
                }
            }
            catch { /* Non-critical UI update */ }
        }
    }
}
