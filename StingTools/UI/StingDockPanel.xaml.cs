using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using StingTools.Core;

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

        // Phase 74c: Removed dead SelectionMemory field — actual memory logic uses
        // StingCommandHandler._memorySlots (Dictionary<string, List<ElementId>>)

        // SDP-HIGH-01: Single shared BrushConverter — avoids creating one per colour swatch
        private static readonly BrushConverter _brushConverter = new BrushConverter();

        // SDP-HIGH-02: Pre-allocated frozen brushes for status methods (UpdateTagsStatus / UpdateComplianceStatus)
        private static readonly SolidColorBrush _statusGreenBrush  = FZ(Color.FromRgb(46,  105, 55));
        private static readonly SolidColorBrush _statusAmberBrush  = FZ(Color.FromRgb(183, 149, 11));
        private static readonly SolidColorBrush _statusRedBrush    = FZ(Color.FromRgb(169, 50,  38));
        private static readonly SolidColorBrush _statusDefaultBrush = FZ(Color.FromRgb(102, 102, 102));
        private static readonly SolidColorBrush _complianceGreenBrush  = FZ(Color.FromRgb(76,  175, 80));
        private static readonly SolidColorBrush _complianceAmberBrush  = FZ(Color.FromRgb(255, 152, 0));
        private static readonly SolidColorBrush _complianceRedBrush    = FZ(Color.FromRgb(244, 67,  54));
        private static readonly SolidColorBrush _complianceDefaultBrush = FZ(Color.FromRgb(206, 147, 216));
        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

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
            // Register this panel as the theme target before seeding resources
            ThemeManager.RegisterTarget(this);
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

        /// <summary>
        /// Public dispatch method for modeless dialogs (e.g. Sheet Manager).
        /// Sets the command tag and raises the external event so the operation
        /// executes on the Revit API thread.
        /// </summary>
        public static bool DispatchCommand(string tag, string param1 = "", string param2 = "")
        {
            if (_handler == null || _externalEvent == null) return false;
            _handler.SetCommand(tag, param1, param2);
            return _externalEvent.Raise() == ExternalEventRequest.Accepted;
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
                // SDP-MEDIUM-01: Pre-compute repeated string tests once to avoid duplicate scans
                bool isPlaceCmd     = cmdTag.Contains("Place");
                bool isSmartPlace   = isPlaceCmd && cmdTag.Contains("SmartPlace");
                bool isArrangeCmd   = cmdTag.Contains("Arrange");
                bool isFreezingCmd  = _freezingTagCommands.Contains(cmdTag);

                // UI-05: Pass placement scope radios to SmartPlace commands
                if (cmdTag.StartsWith("TagStudio_SmartPlace") || cmdTag == "SmartPlaceTags" ||
                    cmdTag == "BatchPlaceTags")
                {
                    SetPlacementScopeParams();
                }

                // UI-06: Pass DirOverride radio state to placement commands
                if (isPlaceCmd || isArrangeCmd || isSmartPlace)
                {
                    SetDirOverrideParams();
                }

                // UI-07: Pass batch warning suppression state
                if (isFreezingCmd)
                {
                    SetBatchWarningParams();
                }

                // UI-08: Pass preferred compass position to SmartPlace
                if (isSmartPlace || isPlaceCmd)
                {
                    SetPreferredPositionParam();
                }

                // FIX-4.2: Pass Leader & Elbow slider values to elbow/arrow commands
                if (cmdTag == "TagStudio_AdjustElbows" || cmdTag == "TagStudio_SetArrows"
                    || cmdTag == "SnapElbow90" || cmdTag == "SnapElbow45"
                    || cmdTag == "SnapElbowStraight" || cmdTag == "SnapElbowFree")
                {
                    SetLeaderElbowParams();
                }
                // FIX-4.2: Pass Style & Color slider values to style commands
                if (cmdTag == "TagStudio_ApplyStyle" || cmdTag == "ApplyTagStyle"
                    || cmdTag == "BatchTagTextSize")
                {
                    SetTagStyleParams();
                }

                // Handle theme cycling directly in WPF thread (no Revit API needed)
                if (cmdTag == "CycleTheme")
                {
                    string next = ThemeManager.CycleTheme();
                    // Force WPF to re-evaluate DynamicResource bindings in Revit's hosted pane
                    InvalidateVisual();
                    UpdateLayout();
                    UpdateStatus($"Theme: {next}");
                    return;
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

        /// <summary>FIX-4.1: Read Leader &amp; Elbow sliders and pass as ExtraParams.</summary>
        private void SetLeaderElbowParams()
        {
            try
            {
                string em = "0";
                if (rbElbow90?.IsChecked == true)    em = "1";
                else if (rbElbow45?.IsChecked == true)   em = "2";
                else if (rbElbowFree?.IsChecked == true)  em = "3";
                StingCommandHandler.SetExtraParam("ElbowMode", em);
                StingCommandHandler.SetExtraParam("ElbowX",    (sldElbowX?.Value    ?? 0   ).ToString("F1"));
                StingCommandHandler.SetExtraParam("ElbowY",    (sldElbowY?.Value    ?? -16 ).ToString("F1"));
                StingCommandHandler.SetExtraParam("ElbowDist", (sldElbowDist?.Value ?? 8   ).ToString("F1"));
                string lm = "Auto";
                if (rbLeaderAlways?.IsChecked == true) lm = "Always";
                else if (rbLeaderNever?.IsChecked == true) lm = "Never";
                else if (rbLeaderSmart?.IsChecked == true) lm = "Smart";
                StingCommandHandler.SetExtraParam("LeaderMode", lm);
                StingCommandHandler.SetExtraParam("LeaderLen",       (sldLeaderLen?.Value       ?? 14).ToString("F0"));
                StingCommandHandler.SetExtraParam("LeaderMin",       (sldLeaderMin?.Value       ?? 5 ).ToString("F0"));
                StingCommandHandler.SetExtraParam("LeaderMax",       (sldLeaderMax?.Value       ?? 43).ToString("F0"));
                StingCommandHandler.SetExtraParam("LeaderThreshold", (sldLeaderThreshold?.Value ?? 20).ToString("F0"));
                StingCommandHandler.SetExtraParam("ArrowStyle",
                    (cmbArrowStyle?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "None");
                StingCommandHandler.SetExtraParam("ArrowSize",  (sldArrowSize?.Value ?? 4).ToString("F0"));
            }
            catch (Exception ex) { StingLog.Warn($"Read leader/elbow params failed: {ex.Message}"); }
        }

        /// <summary>FIX-4.1: Read Style &amp; Color sliders for style commands.</summary>
        private void SetTagStyleParams()
        {
            try
            {
                StingCommandHandler.SetExtraParam("TagTextSize",     (sldTextSize?.Value     ?? 2.5).ToString("F2"));
                StingCommandHandler.SetExtraParam("TagLetterSpacing",(sldLetterSpacing?.Value ?? 0  ).ToString("F0"));
                string w = "Normal";
                if (rbWeightBold?.IsChecked == true)   w = "Bold";
                else if (rbWeightItalic?.IsChecked == true) w = "Italic";
                StingCommandHandler.SetExtraParam("TagTextWeight", w);
                string c = "Black";
                if (rbColorRed?.IsChecked == true)   c = "Red";
                else if (rbColorBlue?.IsChecked == true)  c = "Blue";
                else if (rbColorWhite?.IsChecked == true) c = "White";
                StingCommandHandler.SetExtraParam("TagTextColor", c);
            }
            catch (Exception ex) { StingLog.Warn($"Read tag style params failed: {ex.Message}"); }
        }

        /// <summary>UI-05: Read scope radio state and pass to commands.</summary>
        private void SetPlacementScopeParams()
        {
            try
            {
                // SDP-MEDIUM-02: Cache FindName results on first call
                if (_cachedRbScopeView == null)
                {
                    _cachedRbScopeView      = FindName("rbScopeView")      as System.Windows.Controls.RadioButton;
                    _cachedRbScopeAllViews  = FindName("rbScopeAllViews")  as System.Windows.Controls.RadioButton;
                    _cachedRbScopeSelection = FindName("rbScopeSelection") as System.Windows.Controls.RadioButton;
                    _cachedChkSkipPlaced    = FindName("chkSkipPlaced")    as System.Windows.Controls.CheckBox;
                    _cachedChkIncludeLinks  = FindName("chkIncludeLinks")  as System.Windows.Controls.CheckBox;
                }

                string scope = "View"; // default
                if (_cachedRbScopeAllViews?.IsChecked == true)  scope = "AllViews";
                else if (_cachedRbScopeSelection?.IsChecked == true) scope = "Selection";
                StingCommandHandler.SetExtraParam("PlacementScope", scope);
                StingCommandHandler.SetExtraParam("SkipPlaced",    _cachedChkSkipPlaced?.IsChecked  == true ? "1" : "0");
                StingCommandHandler.SetExtraParam("IncludeLinks",  _cachedChkIncludeLinks?.IsChecked == true ? "1" : "0");
            }
            catch (Exception ex) { StingLog.Warn($"Scope controls may not exist in all layouts: {ex.Message}"); }
        }

        // SDP-MEDIUM-02: Cached FindName results — populated on first use to avoid repeated traversal
        private System.Windows.Controls.RadioButton   _cachedRbScopeView;
        private System.Windows.Controls.RadioButton   _cachedRbScopeAllViews;
        private System.Windows.Controls.RadioButton   _cachedRbScopeSelection;
        private System.Windows.Controls.CheckBox      _cachedChkSkipPlaced;
        private System.Windows.Controls.CheckBox      _cachedChkIncludeLinks;
        private System.Windows.Controls.CheckBox      _cachedChkSuppressBatchWarnings;
        private System.Windows.Controls.RadioButton[] _cachedPosRadios;

        /// <summary>UI-06: Read direction override radio state.</summary>
        private System.Windows.Controls.RadioButton[] _cachedDirRadios;
        private void SetDirOverrideParams()
        {
            try
            {
                // Cache FindName results on first call to avoid 16 FindName lookups per placement
                if (_cachedDirRadios == null)
                {
                    string[] dirNames = { "rbDirN", "rbDirNE", "rbDirE", "rbDirSE",
                        "rbDirS", "rbDirSW", "rbDirW", "rbDirNW",
                        "rbDirNfar", "rbDirNEfar", "rbDirEfar", "rbDirSEfar",
                        "rbDirSfar", "rbDirSWfar", "rbDirWfar", "rbDirNWfar" };
                    _cachedDirRadios = new System.Windows.Controls.RadioButton[dirNames.Length];
                    for (int i = 0; i < dirNames.Length; i++)
                        _cachedDirRadios[i] = FindName(dirNames[i]) as System.Windows.Controls.RadioButton;
                }
                for (int i = 0; i < _cachedDirRadios.Length; i++)
                {
                    if (_cachedDirRadios[i]?.IsChecked == true)
                    {
                        StingCommandHandler.SetExtraParam("DirOverride", i.ToString());
                        return;
                    }
                }
                // No direction override selected — clear
                StingCommandHandler.ClearExtraParam("DirOverride");
            }
            catch (Exception ex) { StingLog.Warn($"Read direction override params failed: {ex.Message}"); }
        }

        /// <summary>UI-07: Read batch warning suppression checkbox.</summary>
        private void SetBatchWarningParams()
        {
            try
            {
                // SDP-MEDIUM-02: Cache FindName on first call
                if (_cachedChkSuppressBatchWarnings == null)
                    _cachedChkSuppressBatchWarnings = FindName("chkSuppressBatchWarnings") as System.Windows.Controls.CheckBox;

                if (_cachedChkSuppressBatchWarnings?.IsChecked == true)
                    StingCommandHandler.SetExtraParam("SuppressWarningsDuringBatch", "1");
                else
                    StingCommandHandler.ClearExtraParam("SuppressWarningsDuringBatch");
            }
            catch (Exception ex) { StingLog.Warn($"Read batch warning params failed: {ex.Message}"); }
        }

        /// <summary>UI-08: Read 16-position compass preferred position.</summary>
        private void SetPreferredPositionParam()
        {
            try
            {
                // SDP-MEDIUM-02: Cache all 16 position radio buttons on first call
                if (_cachedPosRadios == null)
                {
                    _cachedPosRadios = new System.Windows.Controls.RadioButton[16];
                    for (int i = 0; i < 16; i++)
                        _cachedPosRadios[i] = FindName($"rbPos{i + 1}") as System.Windows.Controls.RadioButton;
                }

                for (int i = 0; i < _cachedPosRadios.Length; i++)
                {
                    if (_cachedPosRadios[i]?.IsChecked == true)
                    {
                        StingCommandHandler.SetExtraParam("PreferredTagPos", (i + 1).ToString());
                        return;
                    }
                }
                // Default to position 1 (above/north)
                StingCommandHandler.SetExtraParam("PreferredTagPos", "1");
            }
            catch (Exception ex) { StingLog.Warn($"Read preferred position param failed: {ex.Message}"); }
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
            if (_deferredTabContent.TryGetValue(idx, out _) && !_tabsLoading.Contains(idx))
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
                    Background = (SolidColorBrush)_brushConverter.ConvertFromString(hex),
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
                            (SolidColorBrush)_brushConverter.ConvertFromString(h);
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
                    Background = (SolidColorBrush)_brushConverter.ConvertFromString(hex),
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
                            (SolidColorBrush)_brushConverter.ConvertFromString(h);
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
            // R1-UI-02: Snapshot to local to prevent race on _instance
            var inst = _instance;
            if (inst == null) return;
            var list = paramNames is IList<string> l ? l : new List<string>(paramNames);
            inst.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // R2-UI-02: Re-check _instance inside dispatcher callback — inst may reference
                    // a disposed Page if document switched between BeginInvoke queue and execution
                    var current = _instance;
                    if (current == null || !current.IsLoaded) return;
                    PopulateCombo(current.cmbBulkParam, list);
                    PopulateCombo(current.cmbLookupParam, list);
                    PopulateCombo(current.cmbAnomalyParam, list);
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
            // R1-UI-03: Snapshot to local to prevent race on _instance
            var inst = _instance;
            if (inst == null) return;
            try
            {
                if (!inst.Dispatcher.CheckAccess())
                {
                    inst.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { inst.UpdateStatus(statusText); }
                        catch (Exception ex) { StingLog.Warn($"Status bar update failed: {ex.Message}"); }
                    }));
                }
                else
                {
                    inst.UpdateStatus(statusText);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Non-critical UI update: {ex.Message}"); }
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
                // SDP-HIGH-02: Use pre-allocated frozen brushes instead of new SolidColorBrush per call
                _instance.txtTagsStatus.Foreground =
                    rag == "GREEN" ? _statusGreenBrush :
                    rag == "AMBER" ? _statusAmberBrush :
                    rag == "RED"   ? _statusRedBrush :
                    _statusDefaultBrush;
            });
        }

        /// <summary>
        /// ENH-003: Static method to update compliance status bar from command handler.
        /// </summary>
        public static void UpdateComplianceStatus(string statusText, string ragStatus)
        {
            // R1-UI-01: Snapshot _instance to local to prevent race between null check and use
            var inst = _instance;
            if (inst?.txtStatus == null) return;
            try
            {
                // SDP-HIGH-02: Use pre-allocated frozen brushes instead of new SolidColorBrush per call
                var brush = ragStatus switch
                {
                    "GREEN" => _complianceGreenBrush,
                    "AMBER" => _complianceAmberBrush,
                    "RED"   => _complianceRedBrush,
                    _       => _complianceDefaultBrush,
                };

                if (!inst.txtStatus.Dispatcher.CheckAccess())
                {
                    // CRASH FIX: Use BeginInvoke to avoid deadlock (see UpdateStatus).
                    inst.txtStatus.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        inst.txtStatus.Text = statusText;
                        inst.txtStatus.Foreground = brush;
                    }));
                }
                else
                {
                    inst.txtStatus.Text = statusText;
                    inst.txtStatus.Foreground = brush;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Non-critical UI update: {ex.Message}"); }
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
