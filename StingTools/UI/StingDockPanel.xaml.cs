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
    /// Replicates the original pyRevit STINGTags dockable panel with 4 tabs:
    /// SELECT, ORGANISE, CREATE, VIEW — plus Docs/Temp commands in VIEW tab.
    /// All button clicks are dispatched via IExternalEventHandler for thread safety.
    /// </summary>
    public partial class StingDockPanel : Page
    {
        private static ExternalEvent _externalEvent;
        private static StingCommandHandler _handler;

        /// <summary>Singleton instance for cross-thread access from IExternalEventHandler.</summary>
        public static StingDockPanel Instance { get; private set; }

        public StingDockPanel()
        {
            InitializeComponent();
            Instance = this;
            BuildColorSwatches();
        }

        /// <summary>Initialise the external event handler — called once from OnStartup.</summary>
        public static void Initialise(UIControlledApplication app)
        {
            _handler = new StingCommandHandler();
            _externalEvent = ExternalEvent.Create(_handler);
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
            // Material Design 500 palette
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
                        txtHexColor.Text = h.TrimStart('#');
                        brdColorPreview.Background =
                            (SolidColorBrush)new BrushConverter().ConvertFromString(h);
                    }
                };
                pnlSwatches?.Children.Add(swatch);
            }

            // Outline swatches (subset)
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
                        brdOutlineColor.Background =
                            (SolidColorBrush)new BrushConverter().ConvertFromString(h);
                    }
                };
                pnlOutlineSwatches?.Children.Add(swatch);
            }
        }

        // ── Colouriser button handlers ──────────────────────────────

        private void BtnColorApply_Click(object sender, RoutedEventArgs e)
        {
            string paramName = (cmbColorBy?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            string palette = (cmbPalette?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            // Map display text to actual parameter names
            paramName = paramName switch
            {
                "By Category" => "Category",
                "By Discipline" => "ASS_DISCIPLINE_COD_TXT",
                "By System" => "ASS_SYSTEM_TYPE_TXT",
                "By Level" => "Level",
                "By Workset" => "Workset",
                "By Phase" => "Phase Created",
                _ => paramName
            };
            // Map palette display text to internal names
            palette = palette switch
            {
                "STING Discipline" => "discipline",
                "RAG Status" => "rag",
                "Monochrome" => "monochrome",
                _ => ""
            };
            _handler?.SetCommand("ColorApply", paramName, palette);
            _externalEvent?.Raise();
            UpdateStatus("Applying colour scheme...");
        }

        private void BtnColorApplyHex_Click(object sender, RoutedEventArgs e)
        {
            string hex = txtHexColor?.Text ?? "";
            _handler?.SetCommand("ColorApplyHex", hex);
            _externalEvent?.Raise();
            UpdateStatus("Applying hex colour...");
        }

        private void BtnColorApplyTransparency_Click(object sender, RoutedEventArgs e)
        {
            string transparency = ((int)(sldTransparency?.Value ?? 0)).ToString();
            _handler?.SetCommand("ColorApplyTransparency", transparency);
            _externalEvent?.Raise();
            UpdateStatus($"Applying {transparency}% transparency...");
        }

        // ── Colour preset button handlers ──────────────────────────

        private void BtnColorPresetSave_Click(object sender, RoutedEventArgs e)
        {
            string schemeName = (cmbColorScheme?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            _handler?.SetCommand("SaveColorPreset", schemeName);
            _externalEvent?.Raise();
            UpdateStatus("Saving colour preset...");
        }

        private void BtnColorPresetLoad_Click(object sender, RoutedEventArgs e)
        {
            string schemeName = (cmbColorScheme?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            _handler?.SetCommand("LoadColorPreset", schemeName);
            _externalEvent?.Raise();
            UpdateStatus("Loading colour preset...");
        }

        private void BtnColorPresetDelete_Click(object sender, RoutedEventArgs e)
        {
            string schemeName = (cmbColorScheme?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            _handler?.SetCommand("DeleteColorPreset", schemeName);
            _externalEvent?.Raise();
            UpdateStatus("Deleting colour preset...");
        }

        /// <summary>Populate the colour scheme combo box with saved preset names.</summary>
        public void PopulateColorPresets(IEnumerable<string> presetNames)
        {
            if (cmbColorScheme == null) return;
            cmbColorScheme.Items.Clear();
            cmbColorScheme.Items.Add(new ComboBoxItem { Content = "— saved schemes —" });
            foreach (var name in presetNames)
                cmbColorScheme.Items.Add(new ComboBoxItem { Content = name });
            cmbColorScheme.SelectedIndex = 0;
        }

        // ── Panel data helpers ──────────────────────────────────────

        /// <summary>Populate the bulk parameter combo box (thread-safe via Dispatcher).</summary>
        public void PopulateParamList(IEnumerable<string> paramNames)
        {
            if (cmbBulkParam == null) return;
            Dispatcher.Invoke(() =>
            {
                cmbBulkParam.Items.Clear();
                foreach (var name in paramNames)
                    cmbBulkParam.Items.Add(name);
            });
        }

        // ── Status bar helper ──────────────────────────────────────

        public void UpdateStatus(string message)
        {
            if (txtStatus != null)
                Dispatcher.Invoke(() => txtStatus.Text = message);
        }

        public void UpdateBulkStatus(string message)
        {
            if (txtBulkStatus != null)
                Dispatcher.Invoke(() => txtBulkStatus.Text = message);
        }
    }
}
