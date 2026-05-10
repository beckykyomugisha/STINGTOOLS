// STING Plumbing Center — 8-tab dockable panel UI. Phase 179.
// Programmatic WPF (no XAML) for a tight commit. Buttons dispatch via
// StingPlumbingCommandHandler on the Revit API thread.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Autodesk.Revit.UI;
using StingTools.Core.Plumbing;

namespace StingTools.UI.Plumbing
{
    public class StingPlumbingPanel : Page
    {
        public static StingPlumbingPanel Instance { get; private set; }

        public StingPlumbingPanel()
        {
            Title = "STING Plumbing";
            Content = BuildRoot();
            Instance = this;
        }

        private FrameworkElement BuildRoot()
        {
            var root = new DockPanel { LastChildFill = true };

            var status = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(33, 64, 96)),
                Height = 28
            };
            DockPanel.SetDock(status, Dock.Bottom);
            _statusText = new TextBlock
            {
                Text = "STING Plumbing — configure SYSTEM tab first · BS-EN-12056 default",
                Foreground = Brushes.White,
                Margin = new Thickness(10, 6, 10, 0),
                FontSize = 11
            };
            status.Child = _statusText;
            root.Children.Add(status);

            var tabs = new TabControl { Margin = new Thickness(2) };
            ApplyTwoRowTabStrip(tabs);            // 8 tabs at ~260px panel width — 4+4 grid header
            tabs.Items.Add(BuildSystemTab());     // 179a
            tabs.Items.Add(BuildSupplyTab());     // 179b
            tabs.Items.Add(BuildDrainageTab());   // 179b/d
            tabs.Items.Add(BuildRouteTab());      // 179c
            tabs.Items.Add(BuildStormTab());      // 179e
            tabs.Items.Add(BuildSpecialtyTab());  // existing + extensions
            tabs.Items.Add(BuildAuditTab());      // 179e dashboard
            tabs.Items.Add(BuildDocsTab());       // 179f
            root.Children.Add(tabs);

            return root;
        }

        private TextBlock _statusText;

        public void SetStatus(string text)
        {
            try { _statusText.Text = text; } catch { }
        }

        // ── SYSTEM tab — inline foundation config (no modal dialog) ──
        // Controls populated from PlumbingSystemConfig.Defaults() at construction;
        // refreshed by Plumb_LoadSystemConfig command via LoadSystemConfigIntoInputs;
        // read by Plumb_SaveSystemConfig command via ReadSystemConfigFromInputs.

        private ComboBox _sysBldgType, _sysDrainStd, _sysSupplyStd;
        private TextBox  _sysKFactor, _sysOccupancy, _sysBeds, _sysSupplyPres;
        private CheckBox _sysFlushValve;
        private readonly Dictionary<string, ComboBox> _sysMaterials = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, TextBox>  _sysVelocity  = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox>  _sysSlope     = new Dictionary<string, TextBox>();

        private TabItem BuildSystemTab()
        {
            var t = new TabItem { Header = "SYSTEM" };
            var sp = NewSection();
            var seed = PlumbingSystemConfig.Defaults();

            // ── Project & Standards ──
            var projGrid = NewFormGrid();
            AddFormRow(projGrid, 0, "Building",
                _sysBldgType = NewCombo(new[] { "Dwelling", "Office", "Hospital", "School", "Hotel",
                                                "Restaurant", "Factory", "Sports", "PublicWC", "Custom" },
                                        seed.BuildingType));
            _sysBldgType.SelectionChanged += (s, e) =>
            {
                if (_sysBldgType.SelectedItem is string bt)
                    _sysKFactor.Text = PlumbingSystemConfig.DefaultsForBuildingType(bt).KFactor.ToString("F2");
            };
            AddFormRow(projGrid, 1, "Drainage std",
                _sysDrainStd = NewCombo(new[] { "BS-EN-12056", "IPC-2021", "MANUAL" }, seed.DrainStandard));
            AddFormRow(projGrid, 2, "Supply std",
                _sysSupplyStd = NewCombo(new[] { "BS-EN-806", "HUNTER-WSFU", "MANUAL" }, seed.SupplyStandard));
            AddFormRow(projGrid, 3, "K factor",
                _sysKFactor = NewTextBox(seed.KFactor.ToString("F2")));
            AddFormRow(projGrid, 4, "Flush-valve majority",
                _sysFlushValve = NewCheck(seed.FlushValveMajority));
            AddFormRow(projGrid, 5, "Occupancy",
                _sysOccupancy = NewTextBox(seed.OccupancyCount.ToString()));
            AddFormRow(projGrid, 6, "Beds / Workst.",
                _sysBeds = NewTextBox(seed.BedsOrWorkstations.ToString()));
            sp.Children.Add(NewExpander("Project & Standards", projGrid, expanded: true));

            // ── Pipe materials ──
            var matGrid = NewFormGrid();
            var matKeys = new[] { "DCW", "DHW", "Drainage", "Storm", "Vent" };
            var matOptions = (PlumbingTables.Materials != null && PlumbingTables.Materials.Count > 0)
                ? PlumbingTables.Materials.Select(m => m.Key).ToArray()
                : new[] { "COPPER_R250", "UPVC_DRAIN", "MDPE_BLUE", "PEX_AL_PEX" };
            for (int i = 0; i < matKeys.Length; i++)
            {
                var key = matKeys[i];
                var current = seed.Materials.TryGetValue(key, out var m) ? m : matOptions.FirstOrDefault();
                var combo = NewCombo(matOptions, current);
                _sysMaterials[key] = combo;
                AddFormRow(matGrid, i, key, combo);
            }
            sp.Children.Add(NewExpander("Pipe Materials", matGrid, expanded: false));

            // ── Velocity limits (m/s) ──
            var velGrid = NewFormGrid();
            var velKeys = new[] { "DCW_Max", "DHW_Max", "Drain_SelfCleansing", "Drain_Max" };
            for (int i = 0; i < velKeys.Length; i++)
            {
                var k = velKeys[i];
                var v = seed.VelocityMps.TryGetValue(k, out var vv) ? vv : 2.0;
                var tb = NewTextBox(v.ToString("F2"));
                _sysVelocity[k] = tb;
                AddFormRow(velGrid, i, k.Replace('_', ' '), tb);
            }
            sp.Children.Add(NewExpander("Velocity Limits (m/s)", velGrid, expanded: false));

            // ── Slope (%) ──
            var slopeGrid = NewFormGrid();
            var slopeKeys = new[] { "DN32_50", "DN75_100", "DN150", "Target" };
            for (int i = 0; i < slopeKeys.Length; i++)
            {
                var k = slopeKeys[i];
                var v = seed.SlopePctMin.TryGetValue(k, out var vv) ? vv : 1.0;
                var tb = NewTextBox(v.ToString("F2"));
                _sysSlope[k] = tb;
                AddFormRow(slopeGrid, i, k.Replace('_', '–'), tb);
            }
            sp.Children.Add(NewExpander("Slope (%)", slopeGrid, expanded: false));

            // ── Supply pressure ──
            var presGrid = NewFormGrid();
            AddFormRow(presGrid, 0, "Entry pressure (bar)",
                _sysSupplyPres = NewTextBox(seed.SupplyPressureBarAtEntry.ToString("F2")));
            sp.Children.Add(NewExpander("Supply Pressure", presGrid, expanded: false));

            // ── Action buttons ──
            AddCard(sp, "Actions");
            AddBtn(sp, "Plumb_SaveSystemConfig", "▶ Save System Config",
                "Reads the inputs above and writes _BIM_COORD/plumbing_system_config.json + stamps PRJ_ORG_PLM_* parameters.");
            AddBtn(sp, "Plumb_LoadSystemConfig", "⟳ Load from Project",
                "Re-reads the saved plumbing system config from disk + ProjectInformation and refreshes the inputs above.");
            AddBtn(sp, "Plumb_FullAudit", "⚐ Run Full Plumbing Audit (RAG)",
                "Runs all five compliance domains (Supply / Drainage / Vents / Backflow / HTM 04-01).");

            t.Content = WrapScroll(sp);
            return t;
        }

        // Builds a PlumbingSystemConfig from the SYSTEM-tab inputs. Called by
        // PlumbSaveSystemConfigCommand on the Revit thread, so we marshal the
        // read back to the WPF dispatcher to avoid cross-thread access errors.
        public PlumbingSystemConfig ReadSystemConfigFromInputs()
        {
            return Dispatcher.Invoke(() =>
            {
                var c = PlumbingSystemConfig.Defaults();
                c.BuildingType   = (_sysBldgType?.SelectedItem  as string) ?? c.BuildingType;
                c.DrainStandard  = (_sysDrainStd?.SelectedItem  as string) ?? c.DrainStandard;
                c.SupplyStandard = (_sysSupplyStd?.SelectedItem as string) ?? c.SupplyStandard;
                c.FlushValveMajority = _sysFlushValve?.IsChecked == true;
                if (double.TryParse(_sysKFactor?.Text,    out var k))  c.KFactor = k;
                if (int.TryParse(_sysOccupancy?.Text,     out var oc)) c.OccupancyCount = oc;
                if (int.TryParse(_sysBeds?.Text,          out var bd)) c.BedsOrWorkstations = bd;
                if (double.TryParse(_sysSupplyPres?.Text, out var sp)) c.SupplyPressureBarAtEntry = sp;
                c.Materials   = _sysMaterials.ToDictionary(kv => kv.Key, kv => (kv.Value.SelectedItem as string) ?? "");
                c.VelocityMps = _sysVelocity .ToDictionary(kv => kv.Key, kv => double.TryParse(kv.Value.Text, out var d) ? d : 0);
                c.SlopePctMin = _sysSlope    .ToDictionary(kv => kv.Key, kv => double.TryParse(kv.Value.Text, out var d) ? d : 0);
                return c;
            });
        }

        // Pushes a loaded config back into the SYSTEM-tab inputs. Called by
        // PlumbLoadSystemConfigCommand after PlumbingSystemConfig.Load().
        public void LoadSystemConfigIntoInputs(PlumbingSystemConfig cfg)
        {
            if (cfg == null) return;
            Dispatcher.Invoke(() =>
            {
                if (_sysBldgType   != null && _sysBldgType .Items.Contains(cfg.BuildingType))   _sysBldgType .SelectedItem = cfg.BuildingType;
                if (_sysDrainStd   != null && _sysDrainStd .Items.Contains(cfg.DrainStandard))  _sysDrainStd .SelectedItem = cfg.DrainStandard;
                if (_sysSupplyStd  != null && _sysSupplyStd.Items.Contains(cfg.SupplyStandard)) _sysSupplyStd.SelectedItem = cfg.SupplyStandard;
                if (_sysKFactor    != null) _sysKFactor.Text     = cfg.KFactor.ToString("F2");
                if (_sysFlushValve != null) _sysFlushValve.IsChecked = cfg.FlushValveMajority;
                if (_sysOccupancy  != null) _sysOccupancy.Text   = cfg.OccupancyCount.ToString();
                if (_sysBeds       != null) _sysBeds.Text        = cfg.BedsOrWorkstations.ToString();
                if (_sysSupplyPres != null) _sysSupplyPres.Text  = cfg.SupplyPressureBarAtEntry.ToString("F2");
                if (cfg.Materials != null)
                    foreach (var kv in _sysMaterials)
                        if (cfg.Materials.TryGetValue(kv.Key, out var v) && kv.Value.Items.Contains(v))
                            kv.Value.SelectedItem = v;
                if (cfg.VelocityMps != null)
                    foreach (var kv in _sysVelocity)
                        if (cfg.VelocityMps.TryGetValue(kv.Key, out var v))
                            kv.Value.Text = v.ToString("F2");
                if (cfg.SlopePctMin != null)
                    foreach (var kv in _sysSlope)
                        if (cfg.SlopePctMin.TryGetValue(kv.Key, out var v))
                            kv.Value.Text = v.ToString("F2");
                SetStatus($"STING Plumbing — {cfg.BuildingType} · {cfg.DrainStandard} · {cfg.SupplyStandard} · K={cfg.KFactor:F2}");
            });
        }

        private TabItem BuildSupplyTab()
        {
            var t = new TabItem { Header = "SUPPLY" };
            var sp = NewSection();
            AddCard(sp, "Fixture unit scan");
            AddBtn(sp, "Plumb_ScanFixtures", "Scan Fixtures (DU / LU / WSFU)",
                "Walks OST_PlumbingFixtures, matches against the registry, writes PLM_DRN_DU / PLM_SUP_LU_CW / PLM_SUP_LU_HW / PLM_SUP_WSFU.");
            AddCard(sp, "Cold + hot water sizing");
            AddBtn(sp, "Plumb_SizeSupply", "Size DCW / DHW pipes",
                "Hazen-Williams + Swamee-Jain. Uses BS EN 806-3 LU table or Hunter WSFU per SYSTEM config.");
            AddCard(sp, "DHW recirculation");
            AddBtn(sp, "Plumbing_RecircBalance", "Recirc loop + DRV pre-set",
                "Pipe heat-loss → pump duty + DRV kV pre-sets per branch.");
            AddCard(sp, "Pressure analysis");
            AddBtn(sp, "Plumb_PressureCheck", "Pressure Check (per level)",
                "Static pressure per level against BS 8558 minimums; flags PRV requirements.");
            AddBtn(sp, "Plumbing_PRVSchedule", "PRV Schedule (legacy)",
                "Pre-existing static PRV scheduler — retained for backwards compat.");
            AddCard(sp, "Expansion vessel");
            AddBtn(sp, "Plumb_ExpVessel", "Size Expansion Vessel (BS 7074-1)",
                "DHW expansion vessel sizing with default 200 L / ΔT 50 °C — calculator surface for now.");
            AddCard(sp, "TMV register");
            AddBtn(sp, "Plumb_TMVRegister", "Build TMV Register",
                "Scans elements with PLM_TMV_CLASS_TXT set and reports the register.");
            AddCard(sp, "Legionella");
            AddBtn(sp, "Plumbing_DeadLegScan", "Dead-Leg Scan",
                "HSG 274 — flag legs > 5×D or > 5 m on DCW/DHW/blended.");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildDrainageTab()
        {
            var t = new TabItem { Header = "DRAINAGE" };
            var sp = NewSection();
            AddCard(sp, "Fixture unit aggregation");
            AddBtn(sp, "Plumb_ScanFixtures", "Scan Fixtures (DU)",
                "Same scanner as SUPPLY tab — reports the DU column for drainage.");
            AddCard(sp, "Pipe sizing");
            AddBtn(sp, "Plumb_SizeDrainage", "Size Drainage (BS EN 12056-2 / IPC)",
                "DU accumulation → branch / stack DN → slope / self-cleansing audit.");
            AddBtn(sp, "Plumbing_AutoSizeDrainage", "Auto-Size Drainage (full pipeline)",
                "DU → DN → slope correct → vent design → stack capacity (preview / apply).");
            AddCard(sp, "Slope correction");
            AddBtn(sp, "Plumb_FixSlopes", "Fix Slopes (auto-correct)",
                "Wraps SlopeAutoCorrector with TransactionGroup rollback and preview dialog.");
            AddCard(sp, "Trap & vent");
            AddBtn(sp, "Plumb_VentDesign", "Design Vents",
                "BS EN 12056-2 Annex B vent sizing per drain DU.");
            AddBtn(sp, "Plumbing_TrapVentAudit", "Trap & Vent Audit",
                "Audit trap type + seal depth + max branch length + vent DN.");
            AddCard(sp, "Stack capacity");
            AddBtn(sp, "Plumbing_StackCapacity", "Stack Capacity (BS EN 12056-2 §6.5)",
                "Cumulative DU vs Table 11 capacity — flags >70% induced-siphonage risk.");
            AddCard(sp, "Invert levels");
            AddBtn(sp, "Plumb_InvertLevels", "Calculate Invert Levels",
                "InvertLevelEngine — US/DS invert + cover depth, optional writeback to PLM_DRN_INV_*.");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildRouteTab()
        {
            var t = new TabItem { Header = "ROUTE" };
            var sp = NewSection();
            AddCard(sp, "Auto-drop connections");
            AddBtn(sp, "Plumb_AutoRoute", "Auto-Route Selected Fixtures",
                "Wraps AutoPipeDrop on the current selection — drops pipework to nearest host within radius.");
            AddCard(sp, "Slope correction (quick access)");
            AddBtn(sp, "Plumb_FixSlopes", "Fix Slopes (project-wide)",
                "Mirrors the DRAINAGE tab Fix Slopes button.");
            AddCard(sp, "P-trap insertion");
            AddBtn(sp, "Plumb_InsertPTraps", "Insert P-Traps (missing only)",
                "Walks fixtures lacking a trap and inserts a P-trap family at the drainage connector.");
            AddCard(sp, "Penetrations / sleeves");
            AddBtn(sp, "Plumb_PlaceSleeves", "Place Sleeves on Plumbing Pipes",
                "Wraps SleeveEngine — places STING_SLEEVE_ROUND/RECT, inherits fire rating, writes IFC PfV.");
            AddCard(sp, "Hangers & supports");
            AddBtn(sp, "Plumb_PlaceHangers", "Plan Hanger Positions",
                "BS 5572 / MSS SP-58 hanger spacing + trapeze grouping (planning pass).");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildStormTab()
        {
            var t = new TabItem { Header = "STORM" };
            var sp = NewSection();
            AddCard(sp, "Roof drainage");
            AddBtn(sp, "Plumb_RoofDrainage", "Roof Drainage (BS EN 12056-3)",
                "Q_r = A · C_r · r · f. Computes outlet DN + count.");
            AddCard(sp, "Surface water & SuDS");
            AddBtn(sp, "Plumb_SuDS", "SuDS Attenuation (CIRIA C753)",
                "Post-dev minus pre-dev runoff over storm duration with climate uplift.");
            AddCard(sp, "Rainwater harvesting");
            AddBtn(sp, "Plumb_RWH", "RWH Yield (BS 8515)",
                "Annual yield + recommended storage volume from roof area + rainfall.");
            AddBtn(sp, "Plumbing_RainwaterCalc", "RWH / SuDS / Soakaway / Septic (legacy)",
                "Pre-existing combined calculator — retained.");
            AddCard(sp, "Soakaway / septic");
            AddBtn(sp, "Plumb_Soakaway", "Soakaway (BRE Digest 365)",
                "Catchment + infiltration → soakaway volume.");
            AddBtn(sp, "Plumb_SepticTank", "Septic Tank (BS EN 12566-1)",
                "Primary chamber sizing from population equivalent.");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildSpecialtyTab()
        {
            var t = new TabItem { Header = "SPECIALTY" };
            var sp = NewSection();
            AddCard(sp, "Backflow / Cross-connection (BS EN 1717)");
            AddBtn(sp, "Plumbing_BackflowAudit", "Fluid Category Audit",
                "Classify pipes Cat 1-5 and recommend SCV/DCV/RPZ/Air-gap.");
            AddBtn(sp, "Plumbing_CrossConnection", "Cross-Connection Scan",
                "Graph walk potable → non-potable.");
            AddCard(sp, "Materials & jointing");
            AddBtn(sp, "Plumbing_MaterialAudit", "Material & Jointing Audit",
                "Material × jointing × service compatibility + galvanic-pair walk + WRAS check.");
            AddCard(sp, "Med gas / lab / pool");
            var stub = new TextBlock
            {
                Text = "Med gas (HTM 02-01) lives in the HEALTHCARE tab (Mgas suite). Lab water + pool — future enhancement.",
                Margin = new Thickness(6, 4, 6, 8),
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(stub);
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildAuditTab()
        {
            var t = new TabItem { Header = "AUDIT" };
            var sp = NewSection();
            AddCard(sp, "Full audit (RAG dashboard)");
            AddBtn(sp, "Plumb_FullAudit", "Run Full Audit",
                "Single dashboard with Supply / Drainage / Vents / Backflow / HTM 04-01 RAG tiles.");
            AddCard(sp, "Per-domain audits");
            AddBtn(sp, "Plumbing_TrapVentAudit",   "Trap & Vent Audit", "");
            AddBtn(sp, "Plumbing_StackCapacity",   "Stack Capacity",    "");
            AddBtn(sp, "Plumbing_BackflowAudit",   "Fluid Category Audit", "");
            AddBtn(sp, "Plumbing_CrossConnection", "Cross-Connection Scan", "");
            AddBtn(sp, "Plumbing_DeadLegScan",     "Dead-Leg Scan",     "");
            AddBtn(sp, "Plumbing_MaterialAudit",   "Material Audit",    "");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildDocsTab()
        {
            var t = new TabItem { Header = "DOCS" };
            var sp = NewSection();
            AddCard(sp, "Schedules");
            AddBtn(sp, "Plumb_PipeSchedule", "Pipe Schedule",
                "Group pipes by system + DN + material with totals.");
            AddBtn(sp, "Plumb_ManholeSchedule", "Manhole / Access Chamber Schedule",
                "Reads PLM_DRN_INV_* params from manholes / inspection chambers.");
            AddBtn(sp, "Plumb_TMVRegister", "TMV Register",
                "Cross-listed with the SUPPLY tab.");
            AddCard(sp, "BOQ + isometrics");
            AddBtn(sp, "Plumb_BOQ", "Plumbing BOQ",
                "Pipes (m) + fittings (nr) + accessories (nr) — full BoQ row dump.");
            AddBtn(sp, "Plumb_Isometric", "Plumbing Isometric (drawing-type)",
                "Routes through DrawingTypeRegistry — plumb-drainage-A1-1to100 / plumb-supply-A1-1to100.");
            AddCard(sp, "Commissioning shell");
            AddBtn(sp, "Plumb_CommPack", "Stage Commissioning Pack",
                "Plans the commissioning artefact folder under _BIM_COORD/plumbing/commissioning.");
            t.Content = WrapScroll(sp);
            return t;
        }

        // Default TabPanel lays headers in a single row and clips / scrolls when
        // they overflow. At ~260px panel width with 8 tabs that's unreadable —
        // swap to a UniformGrid (2 rows × 4 cols) so every header stays visible.
        private static void ApplyTwoRowTabStrip(TabControl tabs)
        {
            var panel = new FrameworkElementFactory(typeof(UniformGrid));
            panel.SetValue(UniformGrid.RowsProperty, 2);
            panel.SetValue(UniformGrid.ColumnsProperty, 4);
            tabs.ItemsPanel = new ItemsPanelTemplate { VisualTree = panel };

            var tabItemStyle = new Style(typeof(TabItem));
            tabItemStyle.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(2, 4, 2, 4)));
            tabItemStyle.Setters.Add(new Setter(TabItem.FontSizeProperty, 10.0));
            tabItemStyle.Setters.Add(new Setter(TabItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            tabItemStyle.Setters.Add(new Setter(TabItem.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            tabs.Resources.Add(typeof(TabItem), tabItemStyle);
        }

        private static StackPanel NewSection() => new StackPanel { Margin = new Thickness(8) };

        private static ScrollViewer WrapScroll(UIElement content) =>
            new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };

        private static void AddCard(Panel host, string title)
        {
            host.Children.Add(new TextBlock
            {
                Text = "── " + title.ToUpperInvariant() + " ──",
                Margin = new Thickness(2, 12, 2, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 64, 96))
            });
        }

        // ── Inline form-control helpers ──

        private static Expander NewExpander(string header, FrameworkElement content, bool expanded)
        {
            return new Expander
            {
                Header     = header,
                IsExpanded = expanded,
                Content    = content,
                Margin     = new Thickness(2, 8, 2, 0),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 64, 96))
            };
        }

        private static Grid NewFormGrid()
        {
            // Two-column form: 110px label + remaining for control.
            // Tuned for ~260px panel width with 8px outer margin on each side.
            var g = new Grid { Margin = new Thickness(4, 4, 4, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        private static void AddFormRow(Grid g, int row, string label, FrameworkElement control)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 4, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Normal,
                FontSize = 11
            };
            Grid.SetColumn(lbl, 0); Grid.SetRow(lbl, row);
            g.Children.Add(lbl);
            control.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetColumn(control, 1); Grid.SetRow(control, row);
            g.Children.Add(control);
        }

        private static ComboBox NewCombo(IEnumerable<string> items, string selected)
        {
            var c = new ComboBox { FontSize = 11, FontWeight = FontWeights.Normal };
            foreach (var i in items) c.Items.Add(i);
            if (!string.IsNullOrEmpty(selected) && c.Items.Contains(selected)) c.SelectedItem = selected;
            else if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        private static TextBox NewTextBox(string value) => new TextBox
        {
            Text = value ?? "",
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 11,
            FontWeight = FontWeights.Normal
        };

        private static CheckBox NewCheck(bool isChecked) => new CheckBox
        {
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Normal
        };

        private static void AddBtn(Panel host, string tag, string label, string tooltip)
        {
            var b = new Button
            {
                Content = label,
                Tag     = tag,
                ToolTip = tooltip,
                Margin  = new Thickness(2, 2, 2, 4),
                Padding = new Thickness(8, 6, 8, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            b.Click += (s, e) =>
            {
                try
                {
                    var t = ((Button)s).Tag as string;
                    if (string.IsNullOrEmpty(t)) return;
                    StingPlumbingCommandHandler.Instance?.SetCommand(t);
                    StingPlumbingCommandHandler.Event?.Raise();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("STING Plumbing dispatch error: " + ex.Message);
                }
            };
            host.Children.Add(b);
        }
    }
}
