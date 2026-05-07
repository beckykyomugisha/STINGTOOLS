using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using StingTools.Core;

// Autodesk.Revit.UI ships TextBox + ComboBox types that collide with
// System.Windows.Controls equivalents used by the WPF dockable panel.
// Alias the WPF types so this file's controls code compiles without
// having to fully-qualify every call site.
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

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

        /// <summary>
        /// INT-07 — Most-recently constructed dock panel instance, used by the
        /// SyncScheduler hook in StingToolsApp to refresh the sync status chip.
        /// May be null until the dock is first opened.
        /// </summary>
        public static StingDockPanel LastInstance => _instance;

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

            // Pack 0 — reflect current offline state the moment the panel is realised.
            try { UpdateOfflineStatus(StingTools.Core.StingOfflineConfig.IsOffline, StingTools.Core.StingOfflineConfig.Source); }
            catch { /* non-fatal */ }

            // Standards — paint the active region into the header chip and
            // subscribe to ProjectStandardsManager.StandardsChanged so the
            // chip updates live whenever a region is applied (SetRegionCommand,
            // OnDocumentOpened sync, ProjectSetupWizard finish).
            try
            {
                RefreshRegionIndicator();
                StingTools.Standards.ProjectStandardsManager.Instance.StandardsChanged
                    += (_, __) => RefreshRegionIndicator();
            }
            catch { /* non-fatal */ }
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

        /// <summary>
        /// ORPHAN-FIX: commands that read the Tokens &amp; Depth sub-tab controls
        /// (TokenMask, separator, SEQ pad, segment order, paragraph depth, write mode,
        /// scope, COBie fields, tag containers) and the Categories sub-tab filter.
        /// </summary>
        private static readonly HashSet<string> _tokenDepthConsumers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "CombineParameters", "TagAndCombine", "FamilyStagePopulate",
            "FullAutoPopulate", "TagStudio_Pipeline",
            "AutoTag", "BatchTag", "BatchTagAll", "BatchTagView", "AutoTagSelected",
            "TagNewOnly", "ReTag", "ResolveAllIssues", "RetagStale",
            "SetParagraphDepth", "PreTagAudit", "ValidateTags",
            "SmartPlaceTags", "TagStudio_SmartPlace", "BatchPlaceTags",
        };

        private void Cmd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cmdTag)
            {
                // Phase 165 — Issue #14. After a mode-switch button fires,
                // refresh the depth-tier labels under the depth slider so
                // the user sees the correct tier-set for the new active mode.
                // Schedule the refresh via the dispatcher so it runs after
                // the IExternalEvent finishes writing the mode params.
                if (cmdTag == "SetPatternMode_DC" ||
                    cmdTag == "SetPatternMode_Handover" ||
                    cmdTag == "SetPatternMode_Custom")
                {
                    Dispatcher.BeginInvoke(new System.Action(RefreshParagraphTierLabels),
                        System.Windows.Threading.DispatcherPriority.Background);
                }

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

                // Pass Scale tab slider values to the tier persistence command
                if (cmdTag == "Scale_ApplyTiers") SetScaleTabParams();

                // ORPHAN-FIX: Pass Tokens & Depth controls to commands that honour them.
                // Covers the Combine / Stage populate / Full auto / tagging pipeline paths
                // plus the paragraph-depth command and the Categories sub-tab filter.
                if (_tokenDepthConsumers.Contains(cmdTag))
                {
                    SetTokenDepthParams();
                    SetCategoryFilterParams();
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

                // v4 Phase A — populate static option singletons from
                // Fixtures / Routing / Fabrication sub-tab state before
                // the command crosses the IExternalEventHandler boundary.
                if (cmdTag.StartsWith("Placement_"))  SetV4PlacementOptions();
                if (cmdTag.StartsWith("Routing_"))    SetV4RoutingOptions();
                if (cmdTag.StartsWith("Fabrication_")) SetV4FabricationOptions();

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

        /// <summary>
        /// Push the three Scale-tab info labels from
        /// <c>StingToolsApp.OnViewActivated</c>. Must be called on the WPF
        /// dispatcher thread; tolerates null controls when the tab is still
        /// in its deferred-loading placeholder.
        /// </summary>
        public void UpdateScaleInfoLabels(string scaleText, string tierText, string offsetText)
        {
            try
            {
                if (txtViewScale  != null) txtViewScale.Text  = scaleText  ?? "Scale: —";
                if (txtViewTier   != null) txtViewTier.Text   = tierText   ?? "Tier: —";
                if (txtViewOffset != null) txtViewOffset.Text = offsetText ?? "Offset: — mm (— ft)";
            }
            catch (Exception ex) { StingLog.Warn($"UpdateScaleInfoLabels: {ex.Message}"); }
        }

        /// <summary>
        /// Read the Scale tab sliders and pass them as ExtraParams for
        /// <c>ApplyScaleTiersCommand</c>. Keyed to the JSON schema that
        /// <c>Core.ScaleTiers.SaveProjectOverride</c> writes.
        /// </summary>
        private void SetScaleTabParams()
        {
            try
            {
                StingCommandHandler.SetExtraParam("Scale50Mm",   (sldScale50?.Value   ?? 2.0 ).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                StingCommandHandler.SetExtraParam("Scale100Mm",  (sldScale100?.Value  ?? 5.0 ).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                StingCommandHandler.SetExtraParam("Scale200Mm",  (sldScale200?.Value  ?? 8.0 ).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                StingCommandHandler.SetExtraParam("Scale500Mm",  (sldScale500?.Value  ?? 12.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                StingCommandHandler.SetExtraParam("Scale1000Mm", (sldScale1000?.Value ?? 20.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                StingCommandHandler.SetExtraParam("OffsetCapFt", (sldOffsetCap?.Value ?? 30.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

                // Phase 165 — per-category scale multipliers (previously orphaned).
                // Read by ApplyScaleTiersCommand and persisted to project_config.json
                // under SCALE_CATEGORY_MULTIPLIERS so SmartTagPlacementCommand can
                // multiply the base offset by the per-category factor at placement time.
                if (FindName("sldMultDucts") is System.Windows.Controls.Slider mD)
                    StingCommandHandler.SetExtraParam("MultDucts", mD.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                if (FindName("sldMultPipes") is System.Windows.Controls.Slider mP)
                    StingCommandHandler.SetExtraParam("MultPipes", mP.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                if (FindName("sldMultEquipment") is System.Windows.Controls.Slider mE)
                    StingCommandHandler.SetExtraParam("MultEquipment", mE.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                if (FindName("sldMultFixtures") is System.Windows.Controls.Slider mF)
                    StingCommandHandler.SetExtraParam("MultFixtures", mF.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { StingLog.Warn($"Read Scale tab sliders failed: {ex.Message}"); }
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

        /// <summary>
        /// ORPHAN-FIX: Read the Tokens &amp; Depth sub-tab controls and push them
        /// as ExtraParams so the tagging pipeline can honour them without
        /// changing command signatures.
        ///
        /// ExtraParam keys written:
        ///   TokenMask       — 8-char bitmask ("11111111") matching DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ
        ///   TagSeparator    — the active separator char ("-", "/", ".", "_")
        ///   SeqPad          — 3, 4, or 5 (SEQ zero-pad width)
        ///   SegOrder        — human-readable segment-order combo selection
        ///   ParaDepth       — 1..10 (paragraph depth tier from slider)
        ///   CobieFields     — comma-joined list of enabled COBie pre-seed fields
        ///   TagContainers   — comma-joined list of enabled tag-container group codes
        ///   WriteMode       — "FillEmpty" or "Overwrite"
        ///   TokenScope      — "Project", "View", or "Selection"
        ///
        /// All reads are wrapped in try/catch so missing controls (e.g. dialog
        /// invoked before the sub-tab has been lazy-loaded) never throw.
        /// </summary>
        private void SetTokenDepthParams()
        {
            try
            {
                // Token segment visibility → 8-char bitmask
                char[] mask = new char[8];
                mask[0] = (FindName("chkMaskDISC") is System.Windows.Controls.CheckBox cDisc && cDisc.IsChecked != false) ? '1' : '0';
                mask[1] = (FindName("chkMaskLOC")  is System.Windows.Controls.CheckBox cLoc  && cLoc.IsChecked  != false) ? '1' : '0';
                mask[2] = (FindName("chkMaskZONE") is System.Windows.Controls.CheckBox cZone && cZone.IsChecked != false) ? '1' : '0';
                mask[3] = (FindName("chkMaskLVL")  is System.Windows.Controls.CheckBox cLvl  && cLvl.IsChecked  != false) ? '1' : '0';
                mask[4] = (FindName("chkMaskSYS")  is System.Windows.Controls.CheckBox cSys  && cSys.IsChecked  != false) ? '1' : '0';
                mask[5] = (FindName("chkMaskFUNC") is System.Windows.Controls.CheckBox cFunc && cFunc.IsChecked != false) ? '1' : '0';
                mask[6] = (FindName("chkMaskPROD") is System.Windows.Controls.CheckBox cProd && cProd.IsChecked != false) ? '1' : '0';
                mask[7] = (FindName("chkMaskSEQ")  is System.Windows.Controls.CheckBox cSeq  && cSeq.IsChecked  != false) ? '1' : '0';
                StingCommandHandler.SetExtraParam("TokenMask", new string(mask));

                // Separator radios
                string sep = "-";
                if (FindName("rbSepSlash") is System.Windows.Controls.RadioButton rSl && rSl.IsChecked == true) sep = "/";
                else if (FindName("rbSepDot") is System.Windows.Controls.RadioButton rDt && rDt.IsChecked == true) sep = ".";
                else if (FindName("rbSepUnderscore") is System.Windows.Controls.RadioButton rUs && rUs.IsChecked == true) sep = "_";
                StingCommandHandler.SetExtraParam("TagSeparator", sep);

                // SEQ pad combo (4-digit default)
                string seqPad = "4";
                if (FindName("cmbSeqPad") is System.Windows.Controls.ComboBox cSeqPad
                    && cSeqPad.SelectedItem is System.Windows.Controls.ComboBoxItem cbiSeq
                    && cbiSeq.Content is string spText)
                {
                    if (spText.StartsWith("001 "))      seqPad = "3";
                    else if (spText.StartsWith("00001")) seqPad = "5";
                    else                                 seqPad = "4";
                }
                StingCommandHandler.SetExtraParam("SeqPad", seqPad);

                // Segment order combo — pass the raw text; consumers parse it
                if (FindName("cmbSegOrder") is System.Windows.Controls.ComboBox cSegOrder
                    && cSegOrder.SelectedItem is System.Windows.Controls.ComboBoxItem cbiOrder
                    && cbiOrder.Content is string orderText)
                {
                    StingCommandHandler.SetExtraParam("SegOrder", orderText);
                }

                // Paragraph depth slider (1..10)
                int depth = 10;
                if (FindName("sldParaDepth") is System.Windows.Controls.Slider sd)
                    depth = (int)Math.Round(sd.Value);
                if (depth < 1) depth = 1;
                if (depth > 10) depth = 10;
                StingCommandHandler.SetExtraParam("ParaDepth", depth.ToString());

                // Handover mode radios → ParagraphPreset + HandoverMode extra params
                string handoverMode = "Handover";
                if (FindName("rbModeDesign") is System.Windows.Controls.RadioButton rDes && rDes.IsChecked == true)
                    handoverMode = "DesignConstruction";
                else if (FindName("rbModeCustom") is System.Windows.Controls.RadioButton rCus && rCus.IsChecked == true)
                    handoverMode = "Custom";
                StingCommandHandler.SetExtraParam("HandoverMode", handoverMode);
                StingCommandHandler.SetExtraParam("ParagraphPreset", handoverMode);

                // COBie pre-seed field checkboxes
                var cobie = new List<string>();
                void AddIf(string name, string flag)
                {
                    if (FindName(name) is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
                        cobie.Add(flag);
                }
                AddIf("chkCobieUniclass",     "UniclassCode");
                AddIf("chkCobieSFG20",        "SFG20Code");
                AddIf("chkCobieAssetType",    "AssetType");
                AddIf("chkCobieWarranty",     "WarrantyYrs");
                AddIf("chkCobieExpectedLife", "ExpectedLife");
                AddIf("chkCobieMaintFreq",    "MaintFreq");
                AddIf("chkCobieReplaceCost",  "ReplaceCost");
                AddIf("chkCobieManufacturer", "Manufacturer");
                StingCommandHandler.SetExtraParam("CobieFields", string.Join(",", cobie));

                // Tag container checkboxes
                var containers = new List<string>();
                string[] cntNames =
                {
                    "chkCntARCH","chkCntMEP","chkCntSTR","chkCntGEN",
                    "chkCntM","chkCntE","chkCntP","chkCntFP",
                    "chkCntLV","chkCntA","chkCntS","chkCntG",
                    "chkCntTAG1","chkCntTAG2","chkCntTAG3","chkCntTAG4",
                    "chkCntTAG5","chkCntTAG6","chkCntTAG7"
                };
                foreach (string cn in cntNames)
                {
                    if (FindName(cn) is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
                        containers.Add(cn.Substring("chkCnt".Length));
                }
                StingCommandHandler.SetExtraParam("TagContainers", string.Join(",", containers));

                // Write-mode radios
                string writeMode = "FillEmpty";
                if (FindName("rbWriteOverwrite") is System.Windows.Controls.RadioButton rOv && rOv.IsChecked == true)
                    writeMode = "Overwrite";
                StingCommandHandler.SetExtraParam("WriteMode", writeMode);

                // Scope combo
                string tokenScope = "Project";
                if (FindName("cmbTokenScope") is System.Windows.Controls.ComboBox cScope
                    && cScope.SelectedItem is System.Windows.Controls.ComboBoxItem cbiScope
                    && cbiScope.Content is string scopeText)
                {
                    if (scopeText.IndexOf("Active view", StringComparison.OrdinalIgnoreCase) >= 0) tokenScope = "View";
                    else if (scopeText.IndexOf("Selected", StringComparison.OrdinalIgnoreCase) >= 0) tokenScope = "Selection";
                    else tokenScope = "Project";
                }
                StingCommandHandler.SetExtraParam("TokenScope", tokenScope);
            }
            catch (Exception ex) { StingLog.Warn($"Read Tokens & Depth params failed: {ex.Message}"); }
        }

        /// <summary>
        /// ORPHAN-FIX: Read the Categories sub-tab selection and push it as
        /// ExtraParams so <see cref="Core.StingAutoTagger.CreateMultiCategoryFilterStatic"/>
        /// can apply the user's include/exclude list.
        /// Silently no-ops when the Categories sub-tab has not been loaded yet —
        /// downstream helper treats an empty TagCategoryFilter as "accept all".
        /// </summary>
        private void SetCategoryFilterParams()
        {
            try
            {
                if (!(FindName("lstTagCategories") is System.Windows.Controls.ListBox lstInc))
                {
                    // Sub-tab not loaded — clear any stale filter so the default list is used.
                    StingCommandHandler.ClearExtraParam("TagCategoryFilter");
                    StingCommandHandler.ClearExtraParam("TagCategoryExclusions");
                    StingCommandHandler.ClearExtraParam("TagCategoryMode");
                    return;
                }
                var inc = new List<string>();
                foreach (var item in lstInc.SelectedItems)
                {
                    if (item is System.Windows.Controls.ListBoxItem lbi
                        && lbi.Tag is string bic && !string.IsNullOrEmpty(bic))
                    {
                        inc.Add(bic);
                    }
                }
                StingCommandHandler.SetExtraParam("TagCategoryFilter", string.Join(",", inc));

                var exc = new List<string>();
                if (FindName("lstExcludeCategories") is System.Windows.Controls.ListBox lstExc)
                {
                    foreach (var item in lstExc.SelectedItems)
                    {
                        if (item is System.Windows.Controls.ListBoxItem lbi
                            && lbi.Tag is string bic && !string.IsNullOrEmpty(bic))
                        {
                            exc.Add(bic);
                        }
                    }
                }
                StingCommandHandler.SetExtraParam("TagCategoryExclusions", string.Join(",", exc));
                StingCommandHandler.SetExtraParam("TagCategoryMode",
                    inc.Count > 0 ? "Include" : (exc.Count > 0 ? "Exclude" : ""));
            }
            catch (Exception ex) { StingLog.Warn($"Read Category filter failed: {ex.Message}"); }
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

        // ---- v4 Phase A: Fixtures / Routing / Fabrication option capture ----
        //
        // Each of the three sub-tabs under TAGS exposes CheckBox /
        // RadioButton controls that describe the command's options.
        // Instead of passing them through SetExtraParam stringly-typed,
        // we hydrate static option singletons (PlaceFixturesOptions,
        // AutoDropOptions, FabricationOptions) so commands can read
        // typed state with compile-time checking.

        private static bool ChkState(DependencyObject root, string name, bool def)
        {
            if (root == null) return def;
            try
            {
                var cb = FindVisualChild<CheckBox>(root, name);
                if (cb != null) return cb.IsChecked == true;
            }
            catch { }
            return def;
        }

        // Phase 139.7 — read a named RadioButton's IsChecked state.
        private static bool RadState(DependencyObject root, string name, bool def)
        {
            if (root == null) return def;
            try
            {
                var rb = FindVisualChild<System.Windows.Controls.RadioButton>(root, name);
                if (rb != null) return rb.IsChecked == true;
            }
            catch { }
            return def;
        }

        private static bool RadioState(DependencyObject root, string name, bool def)
        {
            if (root == null) return def;
            try
            {
                var rb = FindVisualChild<RadioButton>(root, name);
                if (rb != null) return rb.IsChecked == true;
            }
            catch { }
            return def;
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name)
            where T : FrameworkElement
        {
            if (parent == null) return null;
            try
            {
                var named = LogicalTreeHelper.FindLogicalNode(parent, name) as T;
                if (named != null) return named;
            }
            catch { }
            return null;
        }

        private void SetV4PlacementOptions()
        {
            try
            {
                DependencyObject root = this;
                StingTools.Commands.Placement.PlaceFixturesOptions.DryRunPreference = ChkState(root, "chkFxDryRun", true);
                StingTools.Commands.Placement.PlaceFixturesOptions.SnapTo300mmGrid  = ChkState(root, "chkFxSnap300", true);

                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeElectricalFixtures   = ChkState(root, "chkFxElec",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeLightingDevices      = ChkState(root, "chkFxLtgDev",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeLightingFixtures     = ChkState(root, "chkFxLtgFix",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeCommunicationDevices = ChkState(root, "chkFxComm",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeDataDevices          = ChkState(root, "chkFxData",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeSecurityDevices      = ChkState(root, "chkFxSec",     true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeFireAlarmDevices     = ChkState(root, "chkFxFire",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludePlumbingFixtures     = ChkState(root, "chkFxPlm",     true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeAirTerminals         = ChkState(root, "chkFxHvac",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.IncludeSprinklers           = ChkState(root, "chkFxSpr",     true);

                // Phase 139.7 — Fixtures scope radios. Default to
                // SelectedRooms so an unset state preserves historic
                // behaviour. The radios are mutually exclusive
                // (GroupName = "FxScope") so the first checked wins.
                if (RadState(root, "rbFxScopeView", false))
                    StingTools.Commands.Placement.PlaceFixturesOptions.ScopeMode = StingTools.Commands.Placement.PlaceFixturesOptions.FixtureScopeMode.ActiveView;
                else if (RadState(root, "rbFxScopeAll", false))
                    StingTools.Commands.Placement.PlaceFixturesOptions.ScopeMode = StingTools.Commands.Placement.PlaceFixturesOptions.FixtureScopeMode.AllRooms;
                else
                    StingTools.Commands.Placement.PlaceFixturesOptions.ScopeMode = StingTools.Commands.Placement.PlaceFixturesOptions.FixtureScopeMode.SelectedRooms;

                StingTools.Commands.Placement.PlaceFixturesOptions.EnforceDocM    = ChkState(root, "chkFxDocM",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.EnforceBS7671  = ChkState(root, "chkFxBS7671",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.EnforceBS5266  = ChkState(root, "chkFxBS5266",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.EnforceBS5839  = ChkState(root, "chkFxBS5839",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.EnforceBS6465  = ChkState(root, "chkFxBS6465",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.EnforceEN12464 = ChkState(root, "chkFxEN12464", true);

                StingTools.Commands.Placement.PlaceFixturesOptions.RejectInsideWall      = ChkState(root, "chkFxNoWall",     true);
                StingTools.Commands.Placement.PlaceFixturesOptions.RejectOutsideRoom     = ChkState(root, "chkFxNoRoomOut",  true);
                StingTools.Commands.Placement.PlaceFixturesOptions.MinDoorClearance300   = ChkState(root, "chkFxDoorClr",    true);
                StingTools.Commands.Placement.PlaceFixturesOptions.MinWindowClearance100 = ChkState(root, "chkFxWinClr",     true);
            }
            catch (Exception ex) { StingLog.Warn($"SetV4PlacementOptions failed: {ex.Message}"); }
        }

        private void SetV4RoutingOptions()
        {
            try
            {
                DependencyObject root = this;
                StingTools.Commands.Routing.AutoDropOptions.IncludeElectrical  = ChkState(root, "chkRtElec",  true);
                StingTools.Commands.Routing.AutoDropOptions.IncludePlumbing    = ChkState(root, "chkRtPlm",   true);
                StingTools.Commands.Routing.AutoDropOptions.IncludeHvac        = ChkState(root, "chkRtHvac",  true);
                StingTools.Commands.Routing.AutoDropOptions.SnapToCorridorBand = ChkState(root, "chkRtSnapZone", true);

                // Max search radius — parse from txtRtSearchMm if present.
                try
                {
                    var tb = FindVisualChild<TextBox>(root, "txtRtSearchMm");
                    if (tb != null && double.TryParse(tb.Text, out var mm) && mm > 0)
                        StingTools.Commands.Routing.AutoDropOptions.MaxSearchRadiusMm = mm;
                }
                catch { }

                try
                {
                    var cb = FindVisualChild<ComboBox>(root, "cboRtCdtInstall");
                    if (cb != null && cb.SelectedItem is ComboBoxItem ci && ci.Content is string s)
                        StingTools.Commands.Routing.AutoDropOptions.ConduitInstallMethod = s;
                }
                catch { }
                try
                {
                    var cb = FindVisualChild<ComboBox>(root, "cboRtDuctSeam");
                    if (cb != null && cb.SelectedItem is ComboBoxItem ci && ci.Content is string s)
                        StingTools.Commands.Routing.AutoDropOptions.DuctSeamType = ExtractSeamCode(s);
                }
                catch { }
                try
                {
                    var cb = FindVisualChild<ComboBox>(root, "cboRtPipeHanger");
                    if (cb != null && cb.SelectedItem is ComboBoxItem ci && ci.Content is string s)
                        StingTools.Commands.Routing.AutoDropOptions.PipeHangerType = s;
                }
                catch { }
            }
            catch (Exception ex) { StingLog.Warn($"SetV4RoutingOptions failed: {ex.Message}"); }
        }

        /// <summary>
        /// Duct seam combo items look like "A — Pittsburgh lock".
        /// Strip down to the single-letter SMACNA code.
        /// </summary>
        private static string ExtractSeamCode(string comboText)
        {
            if (string.IsNullOrEmpty(comboText)) return "A";
            var t = comboText.TrimStart();
            return t.Length >= 1 ? t.Substring(0, 1).ToUpperInvariant() : "A";
        }

        /// <summary>
        /// Refreshes the "TITLE BLOCK &amp; VIEW TEMPLATE" status line on the
        /// Fabrication tab after the user picks / clears a ShopDrawingOptions
        /// bundle via Fabrication_ConfigureShopDrawing. Called from
        /// StingCommandHandler on the Revit API thread — marshals to WPF.
        /// Pass null for both args to show the Auto fallback message.
        /// </summary>
        public void UpdateFabShopDrawingStatus(Autodesk.Revit.DB.Document doc, UI.ShopDrawingOptions opts)
        {
            try
            {
                string msg;
                if (opts == null)
                {
                    msg = "Auto-resolved per discipline (STING_TB_ASSEMBLY_*).\nFalls back to first available title block when missing.";
                }
                else
                {
                    string tb = "Auto (per-discipline)";
                    if (doc != null && opts.TitleBlockSymbolId != null
                        && opts.TitleBlockSymbolId != Autodesk.Revit.DB.ElementId.InvalidElementId)
                    {
                        var fs = doc.GetElement(opts.TitleBlockSymbolId) as Autodesk.Revit.DB.FamilySymbol;
                        if (fs != null) tb = $"{fs.FamilyName} : {fs.Name}";
                    }
                    string vt = "None";
                    if (doc != null && opts.ViewTemplateId != null
                        && opts.ViewTemplateId != Autodesk.Revit.DB.ElementId.InvalidElementId)
                    {
                        var v = doc.GetElement(opts.ViewTemplateId) as Autodesk.Revit.DB.View;
                        if (v != null) vt = v.Name;
                    }
                    msg = $"Title block: {tb}\nView template: {vt}";
                    if (!string.IsNullOrWhiteSpace(opts.SheetNumberPattern))
                        msg += $"\nSheet #: {opts.SheetNumberPattern}";
                    if (!string.IsNullOrWhiteSpace(opts.SheetNamePattern))
                        msg += $"\nSheet name: {opts.SheetNamePattern}";
                }
                Dispatcher.Invoke(() =>
                {
                    if (FindName("txtFabShopDrawingStatus") is TextBlock tb) tb.Text = msg;
                });
            }
            catch (Exception ex) { StingLog.Warn($"UpdateFabShopDrawingStatus failed: {ex.Message}"); }
        }

        private void SetV4FabricationOptions()
        {
            try
            {
                DependencyObject root = this;
                // Scope radios (only one true at a time).
                bool sel = RadioState(root, "rbFabScopeSel",  true);
                bool av  = RadioState(root, "rbFabScopeView", false);
                bool prj = RadioState(root, "rbFabScopeAll",  false);
                StingTools.Commands.Fabrication.FabricationOptions.ScopeSelection  = sel;
                StingTools.Commands.Fabrication.FabricationOptions.ScopeActiveView = av;
                StingTools.Commands.Fabrication.FabricationOptions.ScopeProject    = prj;

                StingTools.Commands.Fabrication.FabricationOptions.RulePipe     = ChkState(root, "chkFabPipe",    true);
                StingTools.Commands.Fabrication.FabricationOptions.RulePipeLB   = ChkState(root, "chkFabPipeLB",  false);
                StingTools.Commands.Fabrication.FabricationOptions.RuleDuct     = ChkState(root, "chkFabDuct",    true);
                StingTools.Commands.Fabrication.FabricationOptions.RuleDuctPitt = ChkState(root, "chkFabDuctPit", false);
                StingTools.Commands.Fabrication.FabricationOptions.RuleConduit  = ChkState(root, "chkFabConduit", true);

                StingTools.Commands.Fabrication.FabricationOptions.GenerateAssemblies   = ChkState(root, "chkFabAssy",     true);
                StingTools.Commands.Fabrication.FabricationOptions.GenerateViews        = ChkState(root, "chkFabViews",    true);
                StingTools.Commands.Fabrication.FabricationOptions.GenerateSheets       = ChkState(root, "chkFabSheets",   true);
                StingTools.Commands.Fabrication.FabricationOptions.PlaceISO6412Symbols  = ChkState(root, "chkFabSymbols",  true);
                StingTools.Commands.Fabrication.FabricationOptions.EmitPerDisciplineCsv = ChkState(root, "chkFabCsv",      true);

                StingTools.Commands.Fabrication.FabricationOptions.ContentModeIso6412   = RadioState(root, "rbFabIso6412", true);
            }
            catch (Exception ex) { StingLog.Warn($"SetV4FabricationOptions failed: {ex.Message}"); }
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            // Pin toggle is handled by Revit docking framework
        }

        // INT-07 — Sync status indicator click handler.
        // Triggers an immediate sync via SyncScheduler if it's running, otherwise
        // surfaces a hint that the user needs to log in / configure Planscape.
        private async void SyncIndicator_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var inst = Planscape.PluginSync.SyncScheduler.Instance;
                if (inst == null)
                {
                    if (txtSync != null) txtSync.Text = "Sync: not configured";
                    return;
                }
                if (txtSync != null) txtSync.Text = "Sync: working…";
                var result = await Planscape.PluginSync.SyncScheduler.SyncNow();
                RefreshSyncIndicator();
            }
            catch (System.Exception ex)
            {
                Core.StingLog.Warn($"SyncIndicator_Click failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Repaint the active Standards region chip in the header. Reads
        /// ProjectStandardsManager.Instance and is safe to call from any thread.
        /// </summary>
        public void RefreshRegionIndicator()
        {
            void Apply()
            {
                try
                {
                    if (txtRegion == null) return;
                    var mgr = StingTools.Standards.ProjectStandardsManager.Instance;
                    string region = mgr?.Region;
                    txtRegion.Text = string.IsNullOrEmpty(region)
                        ? "Region: —"
                        : $"Region: {region}";
                    if (bdrRegion != null && mgr != null)
                    {
                        bdrRegion.ToolTip =
                            $"Active Standards region: {region ?? "(unset)"}.\n" +
                            $"Electrical: {mgr.ElectricalStandard}\n" +
                            $"HVAC: {mgr.HVACStandard}\n" +
                            $"Plumbing: {mgr.PlumbingStandard}\n" +
                            $"Structural: {mgr.StructuralStandard}\n" +
                            $"Fire: {mgr.FireProtectionStandard}\n" +
                            $"Lighting: {mgr.LightingStandard}\n" +
                            $"Energy: {mgr.EnergyStandard}\n" +
                            $"Units: {mgr.UnitSystem}\n\n" +
                            "Click to switch (writes PROJECT_REGION onto ProjectInformation).";
                    }
                }
                catch (System.Exception ex) { Core.StingLog.Warn($"RefreshRegionIndicator: {ex.Message}"); }
            }
            if (Dispatcher.CheckAccess()) Apply();
            else Dispatcher.BeginInvoke(new System.Action(Apply));
        }

        // Region chip click — fires StdExt_SetRegion via the existing handler.
        // The command's ApplyRegionalPreset triggers StandardsChanged which
        // refreshes the chip automatically.
        private void RegionIndicator_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                _handler?.SetCommand("StdExt_SetRegion");
                _externalEvent?.Raise();
            }
            catch (System.Exception ex) { Core.StingLog.Warn($"RegionIndicator_Click: {ex.Message}"); }
        }

        /// <summary>
        /// Push the latest sync status into the header chip. Safe to call from any thread.
        /// </summary>
        public void RefreshSyncIndicator()
        {
            void Apply()
            {
                try
                {
                    var inst = Planscape.PluginSync.SyncScheduler.Instance;
                    if (txtSync == null) return;
                    if (inst == null) { txtSync.Text = "Sync: off"; return; }
                    txtSync.Text = inst.Status.ShortLabel;
                }
                catch (System.Exception ex)
                {
                    Core.StingLog.Warn($"RefreshSyncIndicator failed: {ex.Message}");
                }
            }
            if (Dispatcher.CheckAccess()) Apply();
            else Dispatcher.BeginInvoke(new System.Action(Apply));
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
                        try
                        {
                            // R3-FIX-02: Re-check _instance inside dispatcher callback — inst may reference
                            // a disposed Page if document switched between BeginInvoke queue and execution
                            var current = _instance;
                            if (current == null || !current.IsLoaded) return;
                            current.UpdateStatus(statusText);
                        }
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
        /// Pack 0 — update the header online / offline mode indicator. Safe
        /// to call from any thread; no-ops silently if the panel isn't yet
        /// realised. Online is the default posture and the normal badge.
        /// </summary>
        public static void UpdateOfflineStatus(bool isOffline, string source = null)
        {
            var inst = _instance;
            if (inst == null) return;
            try
            {
                inst.Dispatcher.InvokeAsync(() =>
                {
                    var current = _instance;
                    if (current?.txtOffline == null) return;
                    current.txtOffline.Text = isOffline ? "\U0001F512 Offline" : "\U0001F310 Online";
                    if (current.bdrOffline != null)
                    {
                        current.bdrOffline.ToolTip = isOffline
                            ? $"STING in offline mode for this project — the four network commands (ACC Publish, SharePoint Export, Platform Sync, Planscape Connect) are disabled. Click to switch back to online.\nSource: {source ?? "(defaults)"}"
                            : $"STING in online mode — every command available. Click to switch this project to offline for air-gapped / secure-estate work.\nSource: {source ?? "(defaults)"}";
                    }
                });
            }
            catch { /* headless / early-startup contexts */ }
        }

        /// <summary>
        /// Pack 0 — dock-panel badge click handler. Flips the project's
        /// online/offline mode, persists to &lt;project&gt;/_BIM_COORD/sting_config.json,
        /// and refreshes the indicator. Shows a short confirmation so users
        /// notice the mode change.
        /// </summary>
        private void OfflineIndicator_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var uiApp = StingCommandHandler.CurrentApp;
                var doc = uiApp?.ActiveUIDocument?.Document;
                bool newOffline = !StingTools.Core.StingOfflineConfig.IsOffline;
                string bimDir = null;
                if (doc != null)
                {
                    try { bimDir = StingTools.BIMManager.BIMManagerEngine.GetBIMManagerDir(doc); }
                    catch { /* fall back to memory-only toggle */ }
                }
                StingTools.Core.StingOfflineConfig.SetOffline(newOffline, bimDir);
                UpdateOfflineStatus(newOffline, StingTools.Core.StingOfflineConfig.Source);

                Autodesk.Revit.UI.TaskDialog.Show("STING — project mode",
                    newOffline
                        ? "Project switched to OFFLINE.\n\nThe four network commands are disabled:\n  • Planscape Connect\n  • ACC Publish\n  • SharePoint Export\n  • Platform Sync\n\nEvery other STING command works normally."
                        : "Project switched to ONLINE.\n\nAll commands available — including live server sync and ACC / SharePoint / Planscape integration.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"OfflineIndicator_Click: {ex.Message}");
            }
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
                        // R3-FIX-03: Re-check _instance inside dispatcher callback — inst may reference
                        // a disposed Page if document switched between BeginInvoke queue and execution
                        var current = _instance;
                        if (current?.txtStatus == null || !current.IsLoaded) return;
                        current.txtStatus.Text = statusText;
                        current.txtStatus.Foreground = brush;
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

        // ── Categories sub-tab (ORPHAN-FIX) ──────────────────────────────────

        /// <summary>
        /// Build lists of tag-eligible Revit categories. Mirrors the default
        /// set in <see cref="Core.StingAutoTagger.CreateMultiCategoryFilterStatic"/>
        /// plus common architectural and structural categories so a BIM
        /// coordinator can include/exclude them without editing code.
        /// Items store the BuiltInCategory name (e.g. "OST_PlumbingFixtures")
        /// in <see cref="System.Windows.Controls.ListBoxItem.Tag"/>.
        /// </summary>
        private static readonly (string Label, string Bic, string Group)[] _catRows =
        {
            ("Mechanical Equipment",    "OST_MechanicalEquipment",  "MEP"),
            ("Electrical Equipment",    "OST_ElectricalEquipment",  "MEP"),
            ("Electrical Fixtures",     "OST_ElectricalFixtures",   "MEP"),
            ("Lighting Fixtures",       "OST_LightingFixtures",     "MEP"),
            ("Lighting Devices",        "OST_LightingDevices",      "MEP"),
            ("Plumbing Fixtures",       "OST_PlumbingFixtures",     "PLUMBING"),
            ("Sprinklers",              "OST_Sprinklers",           "MEP"),
            ("Fire Alarm Devices",      "OST_FireAlarmDevices",     "MEP"),
            ("Data Devices",            "OST_DataDevices",          "MEP"),
            ("Communication Devices",   "OST_CommunicationDevices", "MEP"),
            ("Security Devices",        "OST_SecurityDevices",      "MEP"),
            ("Nurse Call Devices",      "OST_NurseCallDevices",     "MEP"),
            ("Duct Accessory",          "OST_DuctAccessory",        "MEP"),
            ("Duct Fitting",            "OST_DuctFitting",          "MEP"),
            ("Duct Terminal",           "OST_DuctTerminal",         "MEP"),
            ("Pipe Accessory",          "OST_PipeAccessory",        "PLUMBING"),
            ("Pipe Fitting",            "OST_PipeFitting",          "PLUMBING"),
            ("Ducts",                   "OST_DuctCurves",           "MEP"),
            ("Pipes",                   "OST_PipeCurves",           "PLUMBING"),
            ("Cable Tray",              "OST_CableTray",            "MEP"),
            ("Conduit",                 "OST_Conduit",              "MEP"),
            ("Furniture",               "OST_Furniture",            "ARCH"),
            ("Doors",                   "OST_Doors",                "ARCH"),
            ("Windows",                 "OST_Windows",              "ARCH"),
            ("Walls",                   "OST_Walls",                "ARCH"),
            ("Floors",                  "OST_Floors",               "ARCH"),
            ("Ceilings",                "OST_Ceilings",             "ARCH"),
            ("Roofs",                   "OST_Roofs",                "ARCH"),
            ("Rooms",                   "OST_Rooms",                "ARCH"),
            ("Structural Columns",      "OST_StructuralColumns",    "STR"),
            ("Structural Framing",      "OST_StructuralFraming",    "STR"),
            ("Structural Foundations",  "OST_StructuralFoundation", "STR"),
            ("Generic Models",          "OST_GenericModel",         "GEN"),
        };

        private bool _catListsBuilt;

        /// <summary>
        /// Populate the include/exclude lists on first access so the sub-tab
        /// costs nothing when never opened. Called from the quick-select,
        /// selection-changed and search handlers.
        /// </summary>
        private void EnsureCategoryListsBuilt()
        {
            if (_catListsBuilt) return;
            try
            {
                var lstInc = FindName("lstTagCategories") as System.Windows.Controls.ListBox;
                var lstExc = FindName("lstExcludeCategories") as System.Windows.Controls.ListBox;
                if (lstInc == null && lstExc == null) return;

                foreach (var row in _catRows)
                {
                    if (lstInc != null)
                    {
                        lstInc.Items.Add(new System.Windows.Controls.ListBoxItem
                        {
                            Content = $"{row.Label}  ({row.Group})",
                            Tag = row.Bic,
                            ToolTip = row.Bic,
                        });
                    }
                    if (lstExc != null)
                    {
                        lstExc.Items.Add(new System.Windows.Controls.ListBoxItem
                        {
                            Content = $"{row.Label}  ({row.Group})",
                            Tag = row.Bic,
                            ToolTip = row.Bic,
                        });
                    }
                }
                _catListsBuilt = true;
            }
            catch (Exception ex) { StingLog.Warn($"Build Categories sub-tab failed: {ex.Message}"); }
        }

        private void CatSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            EnsureCategoryListsBuilt();
            string filter = (sender as System.Windows.Controls.TextBox)?.Text?.Trim().ToLowerInvariant() ?? "";
            FilterCatList(FindName("lstTagCategories") as System.Windows.Controls.ListBox, filter);
            FilterCatList(FindName("lstExcludeCategories") as System.Windows.Controls.ListBox, filter);
        }

        private static void FilterCatList(System.Windows.Controls.ListBox lb, string filter)
        {
            if (lb == null) return;
            foreach (var item in lb.Items)
            {
                if (item is System.Windows.Controls.ListBoxItem lbi)
                {
                    string label = lbi.Content?.ToString()?.ToLowerInvariant() ?? "";
                    lbi.Visibility = (string.IsNullOrEmpty(filter) || label.Contains(filter))
                        ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void CatSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            EnsureCategoryListsBuilt();
            UpdateCatStatus();
        }

        private void UpdateCatStatus()
        {
            try
            {
                var lstInc = FindName("lstTagCategories") as System.Windows.Controls.ListBox;
                var lstExc = FindName("lstExcludeCategories") as System.Windows.Controls.ListBox;
                int inc = lstInc?.SelectedItems.Count ?? 0;
                int exc = lstExc?.SelectedItems.Count ?? 0;
                if (FindName("txtCatStatus") is TextBlock tb)
                {
                    string note = (inc == 0 && exc == 0) ? "defaults in use" : "filter active";
                    tb.Text = $"{inc} categories selected · {exc} excluded · {note}";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Category status update failed: {ex.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Categories sub-tab (tagging-category-selection-XDngT merge) —
        // checkbox-based CATEGORY_SKIP editor. Handlers wired from StingDockPanel.xaml
        // ─────────────────────────────────────────────────────────────────────

        private bool _categoryListBuilt;
        private readonly Dictionary<string, System.Windows.Controls.CheckBox> _categoryCheckboxes =
            new Dictionary<string, System.Windows.Controls.CheckBox>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Populate the scrollable checkbox list from ParamRegistry.CategoryEnumMap.
        /// Ticked = "tag this category"; unticked = "skip" (goes into TagConfig.CategorySkipList on save).</summary>
        private void BuildCategoryList()
        {
            if (_categoryListBuilt) return;
            if (pnlTagCategories == null) return;

            var allCats = StingTools.Core.ParamRegistry.CategoryEnumMap.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            pnlTagCategories.Children.Clear();
            _categoryCheckboxes.Clear();

            foreach (string cat in allCats)
            {
                bool isSkipped = StingTools.Core.TagConfig.CategorySkipList != null
                    && StingTools.Core.TagConfig.CategorySkipList.Contains(cat);
                string disc = (StingTools.Core.TagConfig.DiscMap != null
                    && StingTools.Core.TagConfig.DiscMap.TryGetValue(cat, out string d)) ? d : "";

                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = string.IsNullOrEmpty(disc) ? cat : $"{cat}  ({disc})",
                    Tag = cat,
                    IsChecked = !isSkipped,
                    FontSize = 10,
                    Margin = new Thickness(2, 1, 2, 1),
                    ToolTip = string.IsNullOrEmpty(disc)
                        ? $"{cat} — included in batch tagging when ticked"
                        : $"{cat} — discipline {disc} — included in batch tagging when ticked",
                };
                cb.Checked += CategoryCheckbox_Changed;
                cb.Unchecked += CategoryCheckbox_Changed;
                pnlTagCategories.Children.Add(cb);
                _categoryCheckboxes[cat] = cb;
            }

            _categoryListBuilt = true;
            UpdateCategoryCount();
            StingLog.Info($"Tag Categories sub-tab built with {_categoryCheckboxes.Count} categories " +
                $"({StingTools.Core.TagConfig.CategorySkipList?.Count ?? 0} currently skipped)");
        }

        private void CategoryCheckbox_Changed(object sender, RoutedEventArgs e) => UpdateCategoryCount();

        private void UpdateCategoryCount()
        {
            if (txtCategoryCount == null) return;
            int total = _categoryCheckboxes.Count;
            int enabled = 0, visible = 0, visibleEnabled = 0;
            foreach (var cb in _categoryCheckboxes.Values)
            {
                if (cb.IsChecked == true) enabled++;
                if (cb.Visibility == Visibility.Visible)
                {
                    visible++;
                    if (cb.IsChecked == true) visibleEnabled++;
                }
            }
            string filter = txtCategoryFilter?.Text ?? string.Empty;
            txtCategoryCount.Text = string.IsNullOrEmpty(filter)
                ? $"{enabled} of {total} enabled"
                : $"{visibleEnabled} of {visible} enabled in filter ({enabled} of {total} overall)";
        }

        private void CategoryFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_categoryListBuilt) BuildCategoryList();
            string needle = (txtCategoryFilter?.Text ?? string.Empty).Trim();
            foreach (var kvp in _categoryCheckboxes)
            {
                bool match = string.IsNullOrEmpty(needle)
                    || kvp.Key.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                kvp.Value.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateCategoryCount();
        }

        private void ClearCategoryFilter_Click(object sender, RoutedEventArgs e)
        {
            if (txtCategoryFilter != null) txtCategoryFilter.Text = string.Empty;
        }

        private void SelectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            if (!_categoryListBuilt) BuildCategoryList();
            foreach (var cb in _categoryCheckboxes.Values)
                if (cb.Visibility == Visibility.Visible) cb.IsChecked = true;
            UpdateCategoryCount();
        }

        private void SelectNoCategories_Click(object sender, RoutedEventArgs e)
        {
            if (!_categoryListBuilt) BuildCategoryList();
            foreach (var cb in _categoryCheckboxes.Values)
                if (cb.Visibility == Visibility.Visible) cb.IsChecked = false;
            UpdateCategoryCount();
        }

        /// <summary>Additive discipline multi-select — toggles only the categories whose
        /// TagConfig.DiscMap matches this checkbox's Tag; leaves other disciplines alone.</summary>
        private void DiscCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_categoryListBuilt) BuildCategoryList();
            if (!(sender is System.Windows.Controls.CheckBox cb) || !(cb.Tag is string disc) || string.IsNullOrEmpty(disc))
                return;

            bool targetState = cb.IsChecked == true;
            var discMap = StingTools.Core.TagConfig.DiscMap;
            if (discMap == null) return;

            int affected = 0;
            foreach (var kvp in _categoryCheckboxes)
            {
                if (discMap.TryGetValue(kvp.Key, out string d)
                    && string.Equals(d, disc, StringComparison.OrdinalIgnoreCase))
                {
                    if (kvp.Value.IsChecked != targetState)
                    {
                        kvp.Value.IsChecked = targetState;
                        affected++;
                    }
                }
            }
            UpdateCategoryCount();
            string verb = targetState ? "ticked" : "unticked";
            UpdateStatus($"Categories: {verb} {affected} {disc}-discipline categories");
        }

        private void SaveCategorySkip_Click(object sender, RoutedEventArgs e)
        {
            if (!_categoryListBuilt)
            {
                UpdateStatus("Categories: nothing to save (list not opened)");
                return;
            }
            try
            {
                var skip = new List<string>();
                foreach (var kvp in _categoryCheckboxes)
                    if (kvp.Value.IsChecked != true) skip.Add(kvp.Key);

                StingTools.Core.TagConfig.CategorySkipList = new HashSet<string>(skip, StringComparer.OrdinalIgnoreCase);
                StingTools.Core.TagConfig.SetConfigValue("CATEGORY_SKIP", skip);

                try { StingTools.Core.ComplianceScan.InvalidateCache(); }
                catch (Exception ex) { StingLog.Warn($"ComplianceScan.InvalidateCache failed: {ex.Message}"); }
                try { StingTools.Core.StingAutoTagger.InvalidateContext(); }
                catch (Exception ex) { StingLog.Warn($"StingAutoTagger.InvalidateContext failed: {ex.Message}"); }

                int kept = _categoryCheckboxes.Count - skip.Count;
                StingLog.Info($"CATEGORY_SKIP saved: {kept} included, {skip.Count} skipped (of {_categoryCheckboxes.Count} categories)");
                UpdateStatus($"Categories: saved — {kept} tag, {skip.Count} skip");
            }
            catch (Exception ex)
            {
                StingLog.Error("SaveCategorySkip failed", ex);
                UpdateStatus($"Categories: save failed — {ex.Message}");
            }
        }

        private void ReloadCategorySkip_Click(object sender, RoutedEventArgs e)
        {
            if (!_categoryListBuilt)
            {
                BuildCategoryList();
                return;
            }
            var skipSet = StingTools.Core.TagConfig.CategorySkipList
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _categoryCheckboxes)
                kvp.Value.IsChecked = !skipSet.Contains(kvp.Key);
            UpdateCategoryCount();
            UpdateStatus($"Categories: reloaded ({_categoryCheckboxes.Count - skipSet.Count} tag, {skipSet.Count} skip)");
        }

        private void CatQuick_Click(object sender, RoutedEventArgs e)
        {
            EnsureCategoryListsBuilt();
            if (!(FindName("lstTagCategories") is System.Windows.Controls.ListBox lstInc)) return;
            string tag = (sender as Button)?.Tag as string ?? "";

            bool Match(string bic, string mode) => mode switch
            {
                "CatMEP"  => _catRows.Any(r => r.Bic == bic && r.Group == "MEP"),
                "CatArch" => _catRows.Any(r => r.Bic == bic && r.Group == "ARCH"),
                "CatStr"  => _catRows.Any(r => r.Bic == bic && r.Group == "STR"),
                "CatPlb"  => _catRows.Any(r => r.Bic == bic && r.Group == "PLUMBING"),
                _          => false,
            };

            lstInc.SelectedItems.Clear();
            foreach (var item in lstInc.Items)
            {
                if (item is System.Windows.Controls.ListBoxItem lbi && lbi.Tag is string bic)
                {
                    bool select = tag switch
                    {
                        "CatAll"  => true,
                        "CatNone" => false,
                        "CatInv"  => !lbi.IsSelected,
                        _         => Match(bic, tag),
                    };
                    if (select) lstInc.SelectedItems.Add(lbi);
                }
            }
            UpdateCatStatus();
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

        // ─── Phase 165 — Issue #14. Mode-aware depth-tier labels ───
        // Reads ParamRegistry.GetActiveTagMode() against the active document
        // and updates the 10 lblParaTier{N} TextBlocks under the depth slider
        // so the visible tier names match what WriteTag7All will persist.
        private void RefreshParagraphTierLabels()
        {
            try
            {
                var app = StingCommandHandler.CurrentApp;
                var doc = app?.ActiveUIDocument?.Document;
                var mode = doc != null
                    ? StingTools.Core.ParamRegistry.GetActiveTagMode(doc)
                    : StingTools.Core.ParamRegistry.TagMode.DC;

                string[] labels = new string[10];
                if (mode == StingTools.Core.ParamRegistry.TagMode.DC)
                {
                    labels[0] = "T1 Identity";
                    labels[1] = "T2 System";
                    labels[2] = "T3 Spatial";
                    labels[3] = "T4 Lifecycle";
                    labels[4] = "T5 Technical";
                    labels[5] = "T6 Class.";
                    labels[6] = "T7 —";
                    labels[7] = "T8 —";
                    labels[8] = "T9 —";
                    labels[9] = "T10 —";
                }
                else
                {
                    string p = mode == StingTools.Core.ParamRegistry.TagMode.Custom ? "C:" : "";
                    labels[0] = "T1 Identity";
                    labels[1] = "T2 System";
                    labels[2] = "T3 Spatial";
                    labels[3] = $"{p}T4 Comm.";
                    labels[4] = $"{p}T5 Cost";
                    labels[5] = $"{p}T6 Carbon";
                    labels[6] = $"{p}T7 Fab";
                    labels[7] = $"{p}T8 Clash";
                    labels[8] = $"{p}T9 As-Built";
                    labels[9] = $"{p}T10 Audit";
                }

                ApplyTierLabel("lblParaTier1",  labels[0]);
                ApplyTierLabel("lblParaTier2",  labels[1]);
                ApplyTierLabel("lblParaTier3",  labels[2]);
                ApplyTierLabel("lblParaTier4",  labels[3]);
                ApplyTierLabel("lblParaTier5",  labels[4]);
                ApplyTierLabel("lblParaTier6",  labels[5]);
                ApplyTierLabel("lblParaTier7",  labels[6]);
                ApplyTierLabel("lblParaTier8",  labels[7]);
                ApplyTierLabel("lblParaTier9",  labels[8]);
                ApplyTierLabel("lblParaTier10", labels[9]);

                if (FindName("lblParaDepthHint") is System.Windows.Controls.TextBlock hint)
                {
                    hint.Text = mode == StingTools.Core.ParamRegistry.TagMode.DC
                        ? "DC mode — T4-T6 shows Lifecycle / Technical / Classification."
                        : (mode == StingTools.Core.ParamRegistry.TagMode.Custom
                            ? "Custom mode — project-defined T4-T10 payload (Custom: prefix)."
                            : "Handover mode — T4-T10 shows Commissioning / Cost / Carbon / Fab / Clash / As-Built / Audit.");
                }
            }
            catch (System.Exception ex)
            {
                StingTools.Core.StingLog.Warn("RefreshParagraphTierLabels failed: " + ex.Message);
            }
        }

        private void ApplyTierLabel(string controlName, string text)
        {
            if (FindName(controlName) is System.Windows.Controls.TextBlock tb)
                tb.Text = text;
        }
    }
}
