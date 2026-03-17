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
            // FIX-3.2: Seed theme resource keys before DynamicResource bindings resolve
            ThemeManager.InitialiseResources();

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

        // ── Unified button click dispatcher ──────────────────────────────

        /// <summary>
        /// Tag Studio commands that trigger long-running batch operations —
        /// these freeze the sub-tab ribbon while running.
        /// </summary>
        private static readonly HashSet<string> _freezingTagCommands = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "TagStudio_SmartPlace", "TagStudio_Arrange", "TagStudio_AlignBands",
            "SmartPlaceTags", "BatchTagAll", "BatchTagView", "AutoTagSelected",
            "TagStudio_ApplyStyle", "TagStudio_ApplyScheme",
            "TagStudio_AdjustElbows", "TagStudio_SetArrows",
            "ResolveAllIssues", "PreTagAudit", "ValidateTags",
        };

        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cmdTag)
            {
                // UI-05: Pass placement scope radios to SmartPlace commands
                if (cmdTag.StartsWith("TagStudio_SmartPlace") || cmdTag == "SmartPlaceTags" ||
                    cmdTag == "BatchPlaceTags")
                {
                    SetPlacementScopeParams();
                }

                // UI-06: Pass DirOverride radio state to placement commands
                if (cmdTag.Contains("Place") || cmdTag.Contains("Arrange") ||
                    cmdTag.Contains("SmartPlace"))
                {
                    SetDirOverrideParams();
                }

                // UI-07: Pass batch warning suppression state
                if (_freezingTagCommands.Contains(cmdTag))
                {
                    SetBatchWarningParams();
                }

                // UI-08: Pass preferred compass position to SmartPlace
                if (cmdTag.Contains("SmartPlace") || cmdTag.Contains("Place"))
                {
                    SetPreferredPositionParam();
                }

                _handler?.SetCommand(cmdTag);
                var result = _externalEvent?.Raise() ?? ExternalEventRequest.Denied;
                if (result == ExternalEventRequest.Accepted)
                {
                    UpdateStatus($"Running: {cmdTag}...");
                    // Freeze Tag Studio sub-tabs for batch tag operations
                    if (_freezingTagCommands.Contains(cmdTag))
                        FreezeTagSubTabs();
                }
                else
                {
                    // CRASH FIX: If Raise() is denied (previous command still running),
                    // clear the command tag to prevent wrong command execution later.
                    _handler?.SetCommand("");
                    UpdateStatus("Busy — try again...");
                }
            }
        }

        /// <summary>UI-05: Read scope radio state and pass to commands.</summary>
        private void SetPlacementScopeParams()
        {
            try
            {
                // Look for scope radio buttons in the XAML tree
                string scope = "View"; // default
                var rbScopeView = FindName("rbScopeView") as System.Windows.Controls.RadioButton;
                var rbScopeAllViews = FindName("rbScopeAllViews") as System.Windows.Controls.RadioButton;
                var rbScopeSelection = FindName("rbScopeSelection") as System.Windows.Controls.RadioButton;
                if (rbScopeAllViews?.IsChecked == true) scope = "AllViews";
                else if (rbScopeSelection?.IsChecked == true) scope = "Selection";
                StingCommandHandler.SetExtraParam("PlacementScope", scope);

                var chkSkipPlaced = FindName("chkSkipPlaced") as System.Windows.Controls.CheckBox;
                StingCommandHandler.SetExtraParam("SkipPlaced", chkSkipPlaced?.IsChecked == true ? "1" : "0");

                var chkIncludeLinks = FindName("chkIncludeLinks") as System.Windows.Controls.CheckBox;
                StingCommandHandler.SetExtraParam("IncludeLinks", chkIncludeLinks?.IsChecked == true ? "1" : "0");
            }
            catch { /* Scope controls may not exist in all layouts */ }
        }

        /// <summary>UI-06: Read direction override radio state.</summary>
        private void SetDirOverrideParams()
        {
            try
            {
                // Check 16 direction radio buttons (rbDirN through rbDirNWfar)
                string[] dirNames = { "rbDirN", "rbDirNE", "rbDirE", "rbDirSE",
                    "rbDirS", "rbDirSW", "rbDirW", "rbDirNW",
                    "rbDirNfar", "rbDirNEfar", "rbDirEfar", "rbDirSEfar",
                    "rbDirSfar", "rbDirSWfar", "rbDirWfar", "rbDirNWfar" };
                for (int i = 0; i < dirNames.Length; i++)
                {
                    var rb = FindName(dirNames[i]) as System.Windows.Controls.RadioButton;
                    if (rb?.IsChecked == true)
                    {
                        StingCommandHandler.SetExtraParam("DirOverride", i.ToString());
                        return;
                    }
                }
                // No direction override selected — clear
                StingCommandHandler.ClearExtraParam("DirOverride");
            }
            catch { }
        }

        /// <summary>UI-07: Read batch warning suppression checkbox.</summary>
        private void SetBatchWarningParams()
        {
            try
            {
                var chk = FindName("chkSuppressBatchWarnings") as System.Windows.Controls.CheckBox;
                if (chk?.IsChecked == true)
                    StingCommandHandler.SetExtraParam("SuppressWarningsDuringBatch", "1");
                else
                    StingCommandHandler.ClearExtraParam("SuppressWarningsDuringBatch");
            }
            catch { }
        }

        /// <summary>UI-08: Read 16-position compass preferred position.</summary>
        private void SetPreferredPositionParam()
        {
            try
            {
                for (int i = 1; i <= 16; i++)
                {
                    var rb = FindName($"rbPos{i}") as System.Windows.Controls.RadioButton;
                    if (rb?.IsChecked == true)
                    {
                        StingCommandHandler.SetExtraParam("PreferredTagPos", i.ToString());
                        return;
                    }
                }
                // Default to position 1 (above/north)
                StingCommandHandler.SetExtraParam("PreferredTagPos", "1");
            }
            catch { }
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

        // ── Tag Studio Sub-Tab Freeze ────────────────────────────────

        /// <summary>
        /// Tracks whether a tag batch operation is currently running.
        /// When true, non-active Tag Studio sub-tabs are frozen (IsEnabled=false)
        /// to prevent mid-operation tab switching that could corrupt UI state.
        /// </summary>
        private bool _tagOpRunning = false;

        /// <summary>
        /// Called when the user switches between Tag Studio sub-tabs (Placement,
        /// Leader &amp; Elbow, Style &amp; Color, Tokens &amp; Depth, Tools, Scale).
        /// No freeze logic fires here — this hook is reserved for future sub-tab
        /// lazy-loading (same pattern as main TabMain_SelectionChanged).
        /// </summary>
        private void TagStudioTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: only handle direct child selection changes, not bubbled events
            // from inner controls (e.g. ComboBox inside a sub-tab).
            if (!ReferenceEquals(sender, tagStudioTabs)) return;
            e.Handled = true;

            // If a tag op is running, revert the selection to the locked tab.
            if (_tagOpRunning && tagStudioTabs != null)
            {
                int active = _lockedTagSubTabIndex;
                // Defer to avoid reentrancy inside SelectionChanged
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(() =>
                    {
                        if (_tagOpRunning && tagStudioTabs.SelectedIndex != active)
                            tagStudioTabs.SelectedIndex = active;
                    }));
            }
        }

        private int _lockedTagSubTabIndex = 0;

        /// <summary>
        /// Call before starting any long-running tag batch operation.
        /// Freezes all Tag Studio sub-tabs except the currently active one.
        /// </summary>
        internal void FreezeTagSubTabs()
        {
            if (tagStudioTabs == null) return;
            _tagOpRunning = true;
            _lockedTagSubTabIndex = tagStudioTabs.SelectedIndex;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                new Action(() =>
                {
                    int active = _lockedTagSubTabIndex;
                    for (int i = 0; i < tagStudioTabs.Items.Count; i++)
                    {
                        if (tagStudioTabs.Items[i] is System.Windows.Controls.TabItem ti)
                        {
                            bool isActive = (i == active);
                            ti.IsEnabled = isActive;
                            // Dim inactive tabs visually
                            ti.Opacity = isActive ? 1.0 : 0.4;
                        }
                    }
                    Core.StingLog.Info($"TagStudioTabs frozen: active sub-tab={active}");
                }));
        }

        /// <summary>
        /// Call after a tag batch operation completes (success or failure).
        /// Re-enables all Tag Studio sub-tabs.
        /// </summary>
        internal void UnfreezeTagSubTabs()
        {
            _tagOpRunning = false;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                new Action(() =>
                {
                    if (tagStudioTabs == null) return;
                    foreach (var item in tagStudioTabs.Items)
                    {
                        if (item is System.Windows.Controls.TabItem ti)
                        {
                            ti.IsEnabled = true;
                            ti.Opacity = 1.0;
                        }
                    }
                    Core.StingLog.Info("TagStudioTabs unfrozen.");
                }));
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
                txtStatus.Dispatcher.BeginInvoke(new Action(() =>
                {
                    txtStatus.Text = message;
                    // Auto-unfreeze sub-tabs whenever a command reports it has finished
                    if (_tagOpRunning && !message.StartsWith("Running:", StringComparison.OrdinalIgnoreCase))
                        UnfreezeTagSubTabs();
                }));
                return;
            }
            txtStatus.Text = message;
            if (_tagOpRunning && !message.StartsWith("Running:", StringComparison.OrdinalIgnoreCase))
                UnfreezeTagSubTabs();
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

        // ── Dropdown population (called from StingCommandHandler) ──

        /// <summary>
        /// Populate all three parameter ComboBoxes (Bulk, Lookup, Anomaly) from
        /// the Revit API thread via Dispatcher. Called after RefreshParamList scans
        /// element parameters.
        /// </summary>
        public static void PopulateParamDropdowns(IEnumerable<string> paramNames)
        {
            if (_instance == null) return;
            var list = paramNames is IList<string> l ? l : new List<string>(paramNames);
            _instance.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    PopulateCombo(_instance.cmbBulkParam, list);
                    PopulateCombo(_instance.cmbLookupParam, list);
                    PopulateCombo(_instance.cmbAnomalyParam, list);
                    Core.StingLog.Info($"Dropdowns populated with {list.Count} params");
                }
                catch (Exception ex)
                {
                    Core.StingLog.Warn($"PopulateParamDropdowns failed: {ex.Message}");
                }
            }));
        }

        private static void PopulateCombo(System.Windows.Controls.ComboBox cmb, IList<string> items)
        {
            if (cmb == null) return;
            string currentText = cmb.Text;
            cmb.Items.Clear();
            foreach (string item in items)
                cmb.Items.Add(item);
            // Restore previous text if it was typed in
            if (!string.IsNullOrEmpty(currentText))
                cmb.Text = currentText;
        }

        /// <summary>
        /// FIX-UI03: Called by StingCommandHandler.Execute() after every command
        /// completes. Triggers UnfreezeTagSubTabs() via UpdateStatus() so the
        /// Leader &amp; Elbow sub-tab (and all others) are re-enabled automatically.
        /// Must be called on any thread — uses BeginInvoke for safety.
        /// </summary>
        public static void NotifyCommandComplete(string statusText = "Ready")
        {
            if (_instance == null) return;
            try
            {
                if (!_instance.Dispatcher.CheckAccess())
                {
                    _instance.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { _instance.UpdateStatus(statusText); }
                        catch { }
                    }));
                }
                else
                {
                    _instance.UpdateStatus(statusText);
                }
            }
            catch { /* Non-critical UI update */ }
        }

        /// <summary>
        /// UI-03: Static method to update the Tags tab status strip from any thread.
        /// </summary>
        public static void UpdateTagsStatus(string text, string rag)
        {
            _instance?.Dispatcher.InvokeAsync(() =>
            {
                if (_instance?.txtTagsStatus == null) return;
                _instance.txtTagsStatus.Text = text;
                _instance.txtTagsStatus.Foreground = new SolidColorBrush(
                    rag == "GREEN" ? Color.FromRgb(46, 105, 55) :
                    rag == "AMBER" ? Color.FromRgb(183, 149, 11) :
                    rag == "RED" ? Color.FromRgb(169, 50, 38) :
                    Color.FromRgb(102, 102, 102));
            });
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
                    "RED"   => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    _       => new SolidColorBrush(Color.FromRgb(206, 147, 216)),
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

        // ── Warning level radio → ToggleWarningVisibilityCommand ─────────────

        /// <summary>
        /// Called by the WarnLevel radio buttons (rbWarnNone / rbWarnCritical /
        /// rbWarnHigh / rbWarnMedium / rbWarnAll) in the Tokens &amp; Depth sub-tab.
        /// Writes the selected mode to StingCommandHandler.SetExtraParam("WarnMode")
        /// then raises ToggleWarningVisibility without showing a dialog.
        /// </summary>
        private void WarnLevel_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                string mode = rb.Name switch
                {
                    "rbWarnNone"     => Tags.ToggleWarningVisibilityCommand.FILTER_NONE,
                    "rbWarnCritical" => Tags.ToggleWarningVisibilityCommand.FILTER_CRITICAL,
                    "rbWarnHigh"     => Tags.ToggleWarningVisibilityCommand.FILTER_HIGH,
                    "rbWarnMedium"   => Tags.ToggleWarningVisibilityCommand.FILTER_MEDIUM,
                    "rbWarnAll"      => Tags.ToggleWarningVisibilityCommand.FILTER_ALL,
                    _                => Tags.ToggleWarningVisibilityCommand.FILTER_ALL
                };

                StingCommandHandler.SetExtraParam("WarnMode", mode);
                _handler?.SetCommand("ToggleWarningVisibility");
                var result = _externalEvent?.Raise() ?? ExternalEventRequest.Denied;

                if (result == ExternalEventRequest.Accepted)
                    UpdateStatus($"Warnings: {mode}");
                else
                {
                    StingCommandHandler.ClearExtraParam("WarnMode");
                    UpdateStatus("Busy — warning mode not applied");
                }
            }
        }
    }
}
