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
    /// </summary>
    public partial class StingDockPanel : Page
    {
        private static ExternalEvent _externalEvent;
        private static StingCommandHandler _handler;
        private static UIApplication _uiApp;

        private static readonly Dictionary<string, List<int>> SelectionMemory =
            new Dictionary<string, List<int>>();

        public StingDockPanel()
        {
            InitializeComponent();
            BuildColorSwatches();
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
            // Update status on tab change
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
    }
}
