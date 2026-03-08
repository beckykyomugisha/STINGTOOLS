using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// Code-behind for the STING Tools dockable panel.
    /// Unified 6-tab layout: SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW.
    /// All button clicks dispatched via IExternalEventHandler for thread safety.
    ///
    /// CRASH FIX: Implements lazy tab content loading to prevent WPF stack overflow.
    /// The full visual tree (493 buttons, 652 StaticResources) would exhaust the
    /// 1MB thread stack during the recursive Measure/Arrange layout pass.
    /// Solution: detach non-active tab content immediately after InitializeComponent(),
    /// before the first layout pass runs. Content is re-attached on demand when
    /// the user selects a tab.
    /// </summary>
    public partial class StingDockPanel : Page
    {
        private static ExternalEvent _externalEvent;
        private static StingCommandHandler _handler;
        private static UIApplication _uiApp;
        private static StingDockPanel _instance;

        private static readonly Dictionary<string, List<int>> SelectionMemory =
            new Dictionary<string, List<int>>();

        // ── Lazy tab loading state ────────────────────────────────────
        // Stores detached tab content keyed by tab index.
        // Content is re-attached on first tab selection.
        private readonly Dictionary<int, object> _deferredTabContent =
            new Dictionary<int, object>();
        // Tracks tabs that have a pending BeginInvoke to prevent double-queuing
        private readonly HashSet<int> _tabsLoading = new HashSet<int>();
        private bool _tabLoadingInitialized;

        public StingDockPanel()
        {
            InitializeComponent();

            // CRASH FIX: Immediately after XAML parsing, detach content from all
            // non-active tabs BEFORE the first WPF layout pass (Measure/Arrange).
            // This prevents the stack overflow caused by laying out 493 buttons
            // in deeply nested panels all at once.
            DeferNonActiveTabContent();

            BuildColorSwatches();
            _instance = this;
        }

        /// <summary>
        /// Detach content from all non-active tabs to prevent stack overflow
        /// during the initial WPF layout pass. Content is re-attached lazily
        /// when the user selects each tab for the first time.
        /// </summary>
        private void DeferNonActiveTabContent()
        {
            if (tabMain == null) return;

            int activeIndex = tabMain.SelectedIndex;
            if (activeIndex < 0) activeIndex = 0;

            for (int i = 0; i < tabMain.Items.Count; i++)
            {
                if (i == activeIndex) continue; // Keep the active tab loaded

                if (tabMain.Items[i] is TabItem tab && tab.Content != null)
                {
                    _deferredTabContent[i] = tab.Content;
                    tab.Content = CreateLoadingPlaceholder();
                }
            }

            _tabLoadingInitialized = true;
            Core.StingLog.Info($"DeferNonActiveTabContent: deferred {_deferredTabContent.Count} tabs, " +
                $"active tab={activeIndex}");
        }

        /// <summary>Create a lightweight placeholder shown while tab content loads.</summary>
        private static UIElement CreateLoadingPlaceholder()
        {
            return new TextBlock
            {
                Text = "Loading...",
                FontSize = 12,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            };
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
                var result = _externalEvent?.Raise() ?? ExternalEventRequest.Denied;
                if (result == ExternalEventRequest.Accepted)
                {
                    UpdateStatus($"Running: {cmdTag}...");
                }
                else
                {
                    // CRASH FIX: If Raise() is denied (previous command still running),
                    // clear the command tag to prevent wrong command execution later.
                    _handler?.SetCommand("");
                    UpdateStatus($"Busy — try again...");
                }
            }
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            // Pin toggle is handled by Revit docking framework
        }

        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_tabLoadingInitialized) return;
            if (tabMain == null) return;

            int idx = tabMain.SelectedIndex;
            if (idx < 0) return;

            // Lazy-load deferred tab content on first selection.
            // Guard against double-queuing if user clicks the same tab rapidly
            // before the BeginInvoke callback has run.
            if (_deferredTabContent.ContainsKey(idx) && !_tabsLoading.Contains(idx))
            {
                if (tabMain.Items[idx] is TabItem tab)
                {
                    _tabsLoading.Add(idx);
                    int capturedIdx = idx;

                    // Use Dispatcher.BeginInvoke to load content AFTER the tab
                    // switch animation completes, preventing layout stutter.
                    // Content is removed from _deferredTabContent only on success
                    // so it can be retried if the callback fails.
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        try
                        {
                            if (_deferredTabContent.TryGetValue(capturedIdx, out object content))
                            {
                                tab.Content = content;
                                _deferredTabContent.Remove(capturedIdx);
                                Core.StingLog.Info($"Tab {capturedIdx} content loaded (lazy)");
                            }
                        }
                        catch (Exception ex)
                        {
                            Core.StingLog.Error($"Tab {capturedIdx} lazy load failed", ex);
                        }
                        finally
                        {
                            _tabsLoading.Remove(capturedIdx);
                        }
                    }));
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
                swatch.MouseLeftButtonDown += (s, ev) =>
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
                swatch.MouseLeftButtonDown += (s, ev) =>
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
                // CRASH FIX: Use BeginInvoke (async) instead of Invoke (sync).
                // Synchronous Invoke can deadlock when the Revit API thread is
                // waiting for the WPF dispatcher during modal dialog display.
                txtStatus.Dispatcher.BeginInvoke(new Action(() => txtStatus.Text = message));
                return;
            }
            txtStatus.Text = message;
        }

        public void UpdateBulkStatus(string message)
        {
            if (txtBulkStatus == null) return;
            if (!txtBulkStatus.Dispatcher.CheckAccess())
            {
                // CRASH FIX: Use BeginInvoke to avoid deadlock (see UpdateStatus).
                txtBulkStatus.Dispatcher.BeginInvoke(new Action(() => txtBulkStatus.Text = message));
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
                    // CRASH FIX: Use BeginInvoke to avoid deadlock (see UpdateStatus).
                    _instance.txtStatus.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _instance.txtStatus.Text = statusText;
                        _instance.txtStatus.Foreground = brush;
                    }));
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
