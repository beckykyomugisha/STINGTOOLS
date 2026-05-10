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
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox  = System.Windows.Controls.TextBox;

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

        // ── SUPPLY tab — fixture scan + DCW/DHW sizing + result grids ──
        private ComboBox _supSizingStandard;
        private TextBox  _supMaxDpPam;
        private Expander _supplyFixtureScanExpander, _supplySizingExpander, _supplyTmvExpander;
        public DataGrid SupplyFixtureScanGrid { get; private set; }
        public DataGrid SupplySizingGrid     { get; private set; }
        public DataGrid SupplyTmvGrid        { get; private set; }

        private TabItem BuildSupplyTab()
        {
            var t = new TabItem { Header = "SUPPLY" };
            var sp = NewSection();

            AddCard(sp, "Fixture unit scan");
            AddBtn(sp, "Plumb_ScanFixtures", "Scan Fixtures (DU / LU / WSFU)",
                "Walks OST_PlumbingFixtures, matches against the registry, writes PLM_DRN_DU / PLM_SUP_LU_CW / PLM_SUP_LU_HW / PLM_SUP_WSFU.");
            SupplyFixtureScanGrid = NewResultGrid(("Fixture", "Fixture"), ("Count", "Count"), ("LU CW", "LuCw"), ("LU HW", "LuHw"));
            sp.Children.Add(_supplyFixtureScanExpander = NewExpander("Scan results", WithEmptyHint(SupplyFixtureScanGrid, "(run Scan Fixtures to populate)"), expanded: false));

            AddCard(sp, "Cold + hot water sizing");
            var supOpts = NewFormGrid();
            AddFormRow(supOpts, 0, "Standard",
                _supSizingStandard = NewCombo(new[] { "(SYSTEM default)", "BS-EN-806", "HUNTER-WSFU", "MANUAL" }, "(SYSTEM default)"));
            AddFormRow(supOpts, 1, "Max ΔP/m (Pa/m)",
                _supMaxDpPam = NewTextBox("300"));
            sp.Children.Add(supOpts);
            AddBtn(sp, "Plumb_SizeSupply", "Size DCW / DHW pipes",
                "Hazen-Williams + Swamee-Jain. Uses BS EN 806-3 LU table or Hunter WSFU per SYSTEM config (override above).");
            SupplySizingGrid = NewResultGrid(("Section", "Section"), ("ΣLU", "SigmaLu"), ("DN", "Dn"), ("V", "VelocityMps"), ("Status", "Status"));
            sp.Children.Add(_supplySizingExpander = NewExpander("Sizing results", WithEmptyHint(SupplySizingGrid, "(run Size DCW / DHW to populate)"), expanded: false));

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
            SupplyTmvGrid = NewResultGrid(("Ref", "Ref"), ("Location", "Location"), ("Set °C", "SetC"));
            sp.Children.Add(_supplyTmvExpander = NewExpander("TMV register", WithEmptyHint(SupplyTmvGrid, "(run Build TMV Register to populate)"), expanded: false));

            AddCard(sp, "Legionella");
            AddBtn(sp, "Plumbing_DeadLegScan", "Dead-Leg Scan",
                "HSG 274 — flag legs > 5×D or > 5 m on DCW/DHW/blended.");

            t.Content = WrapScroll(sp);
            return t;
        }

        public (string standard, double maxDpPam) ReadSupplySizingOptions()
        {
            return Dispatcher.Invoke(() =>
            {
                var std = (_supSizingStandard?.SelectedItem as string) ?? "(SYSTEM default)";
                var dp  = double.TryParse(_supMaxDpPam?.Text, out var d) ? d : 300.0;
                return (std, dp);
            });
        }

        // ── DRAINAGE tab — DU aggregation + sizing + slope + vents + inverts ──
        private ComboBox   _drnSizingStandard;
        private TextBox    _drnMaxHd;
        private StackPanel _drnSlopeScope;
        private Expander   _drnDuScanExpander, _drnSizingExpander, _drnSlopeExpander, _drnVentExpander, _drnInvertExpander;
        public DataGrid DrainageDuScanGrid { get; private set; }
        public DataGrid DrainageSizingGrid { get; private set; }
        public DataGrid DrainageSlopeGrid  { get; private set; }
        public DataGrid DrainageVentGrid   { get; private set; }
        public DataGrid DrainageInvertGrid { get; private set; }

        private TabItem BuildDrainageTab()
        {
            var t = new TabItem { Header = "DRAINAGE" };
            var sp = NewSection();

            AddCard(sp, "Fixture unit aggregation");
            AddBtn(sp, "Plumb_ScanFixtures", "Scan Fixtures (DU)",
                "Same scanner as SUPPLY tab — reports the DU column for drainage.");
            DrainageDuScanGrid = NewResultGrid(("Fixture", "Fixture"), ("Count", "Count"), ("DU each", "DuEach"), ("ΣDU", "SigmaDu"));
            sp.Children.Add(_drnDuScanExpander = NewExpander("DU scan", WithEmptyHint(DrainageDuScanGrid, "(run Scan Fixtures to populate)"), expanded: false));

            AddCard(sp, "Pipe sizing");
            var drnOpts = NewFormGrid();
            AddFormRow(drnOpts, 0, "Standard",
                _drnSizingStandard = NewCombo(new[] { "(SYSTEM default)", "BS-EN-12056", "IPC-2021", "MANUAL" }, "(SYSTEM default)"));
            AddFormRow(drnOpts, 1, "Max H/D",
                _drnMaxHd = NewTextBox("0.50"));
            sp.Children.Add(drnOpts);
            AddBtn(sp, "Plumb_SizeDrainage", "Size Drainage (BS EN 12056-2 / IPC)",
                "DU accumulation → branch / stack DN → slope / self-cleansing audit.");
            AddBtn(sp, "Plumbing_AutoSizeDrainage", "Auto-Size Drainage (full pipeline)",
                "DU → DN → slope correct → vent design → stack capacity (preview / apply).");
            DrainageSizingGrid = NewResultGrid(("Pipe", "Pipe"), ("ΣDU", "SigmaDu"), ("DN", "Dn"), ("V", "VelocityMps"), ("H/D", "HdRatio"), ("Status", "Status"));
            sp.Children.Add(_drnSizingExpander = NewExpander("Sizing results", WithEmptyHint(DrainageSizingGrid, "(run Size Drainage to populate)"), expanded: false));

            AddCard(sp, "Slope correction");
            sp.Children.Add(_drnSlopeScope = NewRadioGroup("DrainageSlopeScope",
                new[] { "Selected", "View", "Project" }, "Project"));
            AddBtn(sp, "Plumb_FixSlopes", "Fix Slopes (auto-correct)",
                "Wraps SlopeAutoCorrector with TransactionGroup rollback. Reads scope above. The compact grid below populates after each run.");
            DrainageSlopeGrid = NewSlopeFixGrid();
            sp.Children.Add(_drnSlopeExpander = NewExpander("Slope fix preview (compact)", WithEmptyHint(DrainageSlopeGrid, "(run Fix Slopes to populate)"), expanded: false));

            AddCard(sp, "Trap & vent");
            AddBtn(sp, "Plumb_VentDesign", "Design Vents",
                "BS EN 12056-2 Annex B vent sizing per drain DU.");
            DrainageVentGrid = NewResultGrid(("Drain", "Drain"), ("DU", "Du"), ("Vent DN", "VentDn"), ("Max len (m)", "MaxLenM"), ("Flag", "Flag"));
            sp.Children.Add(_drnVentExpander = NewExpander("Vent design results", WithEmptyHint(DrainageVentGrid, "(run Design Vents to populate)"), expanded: false));
            AddBtn(sp, "Plumbing_TrapVentAudit", "Trap & Vent Audit",
                "Audit trap type + seal depth + max branch length + vent DN.");

            AddCard(sp, "Stack capacity");
            AddBtn(sp, "Plumbing_StackCapacity", "Stack Capacity (BS EN 12056-2 §6.5)",
                "Cumulative DU vs Table 11 capacity — flags >70% induced-siphonage risk.");

            AddCard(sp, "Invert levels");
            AddBtn(sp, "Plumb_InvertLevels", "Calculate Invert Levels",
                "InvertLevelEngine — US/DS invert + cover depth, optional writeback to PLM_DRN_INV_*.");
            DrainageInvertGrid = NewResultGrid(("Pipe", "Pipe"), ("US inv", "UsInvM"), ("DS inv", "DsInvM"), ("Cover", "CoverM"));
            sp.Children.Add(_drnInvertExpander = NewExpander("Invert levels", WithEmptyHint(DrainageInvertGrid, "(run Calculate Invert Levels to populate)"), expanded: false));

            t.Content = WrapScroll(sp);
            return t;
        }

        public (string standard, double maxHd) ReadDrainageSizingOptions()
        {
            return Dispatcher.Invoke(() =>
            {
                var std = (_drnSizingStandard?.SelectedItem as string) ?? "(SYSTEM default)";
                var hd  = double.TryParse(_drnMaxHd?.Text, out var v) ? v : 0.50;
                return (std, hd);
            });
        }

        public string ReadDrainageSlopeScope() => Dispatcher.Invoke(() => ReadRadioGroup(_drnSlopeScope));

        // ── Inline result rendering (replaces StingResultPanel.Show popups) ──
        // Each Set*Result method is a thin wrapper around ApplyResult, which
        // marshals to the WPF dispatcher, sets ItemsSource on the grid, opens
        // the surrounding Expander, and updates the status strip with a
        // one-line summary. Commands fall back to the popup result panel only
        // when StingPlumbingPanel.Instance is null (e.g. ribbon entry).

        private void ApplyResult(DataGrid grid, Expander exp, System.Collections.IList rows, string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (grid != null)
                {
                    grid.ItemsSource = null;
                    grid.ItemsSource = rows;
                }
                if (exp != null) exp.IsExpanded = true;
                if (!string.IsNullOrEmpty(status)) SetStatus(status);
            });
        }

        public void SetSupplyFixtureScanResult(IList<SupplyFixtureScanRow> rows, string status) =>
            ApplyResult(SupplyFixtureScanGrid, _supplyFixtureScanExpander, (System.Collections.IList)rows, status);

        public void SetSupplySizingResult(IList<SupplySizingRow> rows, string status) =>
            ApplyResult(SupplySizingGrid, _supplySizingExpander, (System.Collections.IList)rows, status);

        public void SetSupplyTmvResult(IList<SupplyTmvRow> rows, string status) =>
            ApplyResult(SupplyTmvGrid, _supplyTmvExpander, (System.Collections.IList)rows, status);

        public void SetDrainageDuScanResult(IList<DrainageDuScanRow> rows, string status) =>
            ApplyResult(DrainageDuScanGrid, _drnDuScanExpander, (System.Collections.IList)rows, status);

        public void SetDrainageSizingResult(IList<DrainageSizingRow> rows, string status) =>
            ApplyResult(DrainageSizingGrid, _drnSizingExpander, (System.Collections.IList)rows, status);

        public void SetDrainageSlopeResult(IList<DrainageSlopeRow> rows, string status) =>
            ApplyResult(DrainageSlopeGrid, _drnSlopeExpander, (System.Collections.IList)rows, status);

        public void SetDrainageVentResult(IList<DrainageVentRow> rows, string status) =>
            ApplyResult(DrainageVentGrid, _drnVentExpander, (System.Collections.IList)rows, status);

        public void SetDrainageInvertResult(IList<DrainageInvertRow> rows, string status) =>
            ApplyResult(DrainageInvertGrid, _drnInvertExpander, (System.Collections.IList)rows, status);

        // ── ROUTE tab — option panels feed AutoDrop / PTraps / Sleeves / Hangers ──
        private StackPanel _routeAutoScope, _routePTrapScope, _routeSleeveScope, _routeHangerScope;
        private TextBox    _routeMaxRadius, _routeSleeveMinOd;
        private CheckBox   _routeEnforceSlope, _routeAutoTrap, _routeEmitHangers;
        private ComboBox   _routePref;
        private CheckBox _rtFxWc, _rtFxBasin, _rtFxShower, _rtFxBath, _rtFxSink, _rtFxGully, _rtFxFloor;
        private ComboBox _routePTrapFamily, _routeSleeveFireRating;
        private CheckBox _routeIfcPfv;
        private CheckBox _routeHangerHorz, _routeHangerVert, _routeHangerTempCorr;
        private ComboBox _routeHangerRod;

        private TabItem BuildRouteTab()
        {
            var t = new TabItem { Header = "ROUTE" };
            var sp = NewSection();

            // Auto-drop
            AddCard(sp, "Auto-drop connections");
            sp.Children.Add(_routeAutoScope = NewRadioGroup("RouteAutoScope",
                new[] { "Selected", "View", "Project" }, "Selected"));
            var autoOpts = NewFormGrid();
            AddFormRow(autoOpts, 0, "Max radius (mm)", _routeMaxRadius = NewTextBox("3000"));
            AddFormRow(autoOpts, 1, "Route preference",
                _routePref = NewCombo(new[] { "Shortest", "Corridor-preferred", "Wall-chase" }, "Shortest"));
            AddFormRow(autoOpts, 2, "Enforce slope",      _routeEnforceSlope = NewCheck(true));
            AddFormRow(autoOpts, 3, "Auto-insert P-trap", _routeAutoTrap     = NewCheck(true));
            AddFormRow(autoOpts, 4, "Emit hangers",       _routeEmitHangers  = NewCheck(true));
            sp.Children.Add(autoOpts);
            AddBtn(sp, "Plumb_AutoRoute", "Auto-Route Fixtures",
                "Wraps AutoPipeDrop — drops pipework to nearest host within the radius above. Reads the scope and option flags above.");

            // Slope (quick access)
            AddCard(sp, "Slope correction (quick access)");
            AddBtn(sp, "Plumb_FixSlopes", "Fix Slopes (project-wide)",
                "Mirrors the DRAINAGE tab Fix Slopes button.");

            // P-traps
            AddCard(sp, "P-trap insertion");
            sp.Children.Add(_routePTrapScope = NewRadioGroup("RoutePTrapScope",
                new[] { "Selected", "View", "Project" }, "View"));
            var fxBox = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            void addFx(CheckBox cb, string label) { cb.Content = label; cb.Margin = new Thickness(0, 0, 8, 0); cb.FontSize = 11; cb.IsChecked = true; fxBox.Children.Add(cb); }
            addFx(_rtFxWc     = new CheckBox(), "WC");
            addFx(_rtFxBasin  = new CheckBox(), "Basin");
            addFx(_rtFxShower = new CheckBox(), "Shower");
            addFx(_rtFxBath   = new CheckBox(), "Bath");
            addFx(_rtFxSink   = new CheckBox(), "Sink");
            addFx(_rtFxGully  = new CheckBox(), "Gully");
            addFx(_rtFxFloor  = new CheckBox(), "Floor drain");
            sp.Children.Add(fxBox);
            var ptOpts = NewFormGrid();
            AddFormRow(ptOpts, 0, "Trap family",
                _routePTrapFamily = NewCombo(new[] { "STING_SEED_PTrap", "Project default" }, "STING_SEED_PTrap"));
            sp.Children.Add(ptOpts);
            AddBtn(sp, "Plumb_InsertPTraps", "Insert P-Traps (missing only)",
                "Walks fixtures lacking a trap and inserts a P-trap family at the drainage connector. Reads scope + fixture filters + family above.");

            // Sleeves
            AddCard(sp, "Penetrations / sleeves");
            sp.Children.Add(_routeSleeveScope = NewRadioGroup("RouteSleeveScope",
                new[] { "Selected", "View", "Project" }, "View"));
            var slOpts = NewFormGrid();
            AddFormRow(slOpts, 0, "Fire rating",
                _routeSleeveFireRating = NewCombo(new[] { "Inherit from host", "30 min", "60 min", "90 min", "120 min" }, "Inherit from host"));
            AddFormRow(slOpts, 1, "Min OD (mm)",   _routeSleeveMinOd = NewTextBox("15"));
            AddFormRow(slOpts, 2, "Export IFC PfV", _routeIfcPfv     = NewCheck(true));
            sp.Children.Add(slOpts);
            AddBtn(sp, "Plumb_PlaceSleeves", "Place Sleeves on Plumbing Pipes",
                "Wraps SleeveEngine — places STING_SLEEVE_ROUND/RECT, inherits fire rating, writes IFC PfV when enabled.");

            // Hangers
            AddCard(sp, "Hangers & supports");
            sp.Children.Add(_routeHangerScope = NewRadioGroup("RouteHangerScope",
                new[] { "Selected", "View", "Project" }, "View"));
            var hgOpts = NewFormGrid();
            AddFormRow(hgOpts, 0, "Rod size",
                _routeHangerRod = NewCombo(new[] { "Auto", "M8", "M10", "M12" }, "Auto"));
            AddFormRow(hgOpts, 1, "Horizontal",     _routeHangerHorz     = NewCheck(true));
            AddFormRow(hgOpts, 2, "Vertical",       _routeHangerVert     = NewCheck(true));
            AddFormRow(hgOpts, 3, "Temp correction", _routeHangerTempCorr = NewCheck(true));
            sp.Children.Add(hgOpts);
            AddBtn(sp, "Plumb_PlaceHangers", "Plan Hanger Positions",
                "BS 5572 / MSS SP-58 hanger spacing + trapeze grouping (planning pass).");

            t.Content = WrapScroll(sp);
            return t;
        }

        public RouteAutoOptions ReadRouteAutoOptions() => Dispatcher.Invoke(() => new RouteAutoOptions
        {
            Scope             = ReadRadioGroup(_routeAutoScope),
            MaxRadiusMm       = double.TryParse(_routeMaxRadius?.Text, out var r) ? r : 3000,
            Preference        = (_routePref?.SelectedItem as string) ?? "Shortest",
            EnforceSlope      = _routeEnforceSlope?.IsChecked == true,
            AutoInsertPTrap   = _routeAutoTrap?.IsChecked == true,
            EmitHangers       = _routeEmitHangers?.IsChecked == true
        });

        public RoutePTrapOptions ReadRoutePTrapOptions() => Dispatcher.Invoke(() => new RoutePTrapOptions
        {
            Scope         = ReadRadioGroup(_routePTrapScope),
            TrapFamily    = (_routePTrapFamily?.SelectedItem as string) ?? "STING_SEED_PTrap",
            IncludeWc     = _rtFxWc?.IsChecked     == true,
            IncludeBasin  = _rtFxBasin?.IsChecked  == true,
            IncludeShower = _rtFxShower?.IsChecked == true,
            IncludeBath   = _rtFxBath?.IsChecked   == true,
            IncludeSink   = _rtFxSink?.IsChecked   == true,
            IncludeGully  = _rtFxGully?.IsChecked  == true,
            IncludeFloor  = _rtFxFloor?.IsChecked  == true
        });

        public RouteSleeveOptions ReadRouteSleeveOptions() => Dispatcher.Invoke(() => new RouteSleeveOptions
        {
            Scope         = ReadRadioGroup(_routeSleeveScope),
            FireRating    = (_routeSleeveFireRating?.SelectedItem as string) ?? "Inherit from host",
            MinOdMm       = double.TryParse(_routeSleeveMinOd?.Text, out var od) ? od : 15.0,
            ExportIfcPfv  = _routeIfcPfv?.IsChecked == true
        });

        public RouteHangerOptions ReadRouteHangerOptions() => Dispatcher.Invoke(() => new RouteHangerOptions
        {
            Scope          = ReadRadioGroup(_routeHangerScope),
            RodSize        = (_routeHangerRod?.SelectedItem as string) ?? "Auto",
            Horizontal     = _routeHangerHorz?.IsChecked     == true,
            Vertical       = _routeHangerVert?.IsChecked     == true,
            TempCorrection = _routeHangerTempCorr?.IsChecked == true
        });

        // ── STORM tab — calc inputs replace TaskDialog prompts ──
        private TextBox  _stRoofArea, _stRoofRainfall, _stRoofSafety;
        private ComboBox _stRoofType;
        private TextBox  _stSudsArea, _stSudsImperm, _stSudsGreenfield;
        private ComboBox _stSudsReturn;
        private TextBox  _stRwhArea, _stRwhRainfall, _stRwhDemand;
        private ComboBox _stRwhMaterial;
        private TextBox  _stSoakStorm, _stSoakArea, _stSoakInfilt;
        private ComboBox _stSoakGeometry;
        private TextBox  _stSepticPersons;
        private ComboBox _stSepticTertiary;

        private TabItem BuildStormTab()
        {
            var t = new TabItem { Header = "STORM" };
            var sp = NewSection();

            AddCard(sp, "Roof drainage");
            var roofGrid = NewFormGrid();
            AddFormRow(roofGrid, 0, "Catchment area (m²)", _stRoofArea     = NewTextBox(""));
            AddFormRow(roofGrid, 1, "Roof type",
                _stRoofType = NewCombo(new[] { "Flat", "Pitched 15-30°", "Pitched >30°" }, "Flat"));
            AddFormRow(roofGrid, 2, "Rainfall (l/s/m²)",   _stRoofRainfall = NewTextBox("0.021"));
            AddFormRow(roofGrid, 3, "Safety factor",       _stRoofSafety   = NewTextBox("1.50"));
            sp.Children.Add(roofGrid);
            AddBtn(sp, "Plumb_RoofDrainage", "Roof Drainage (BS EN 12056-3)",
                "Q_r = A · C_r · r · f. Computes outlet DN + count from inputs above.");

            AddCard(sp, "Surface water & SuDS");
            var sudsGrid = NewFormGrid();
            AddFormRow(sudsGrid, 0, "Site area (m²)",      _stSudsArea       = NewTextBox(""));
            AddFormRow(sudsGrid, 1, "Impermeable (0..1)",  _stSudsImperm     = NewTextBox("0.80"));
            AddFormRow(sudsGrid, 2, "Greenfield Q (l/s)",  _stSudsGreenfield = NewTextBox(""));
            AddFormRow(sudsGrid, 3, "Return period",
                _stSudsReturn = NewCombo(new[] { "1yr", "5yr", "30yr", "100yr", "100yr+CC" }, "30yr"));
            sp.Children.Add(sudsGrid);
            AddBtn(sp, "Plumb_SuDS", "SuDS Attenuation (CIRIA C753)",
                "Post-dev minus pre-dev runoff over storm duration with climate uplift.");

            AddCard(sp, "Rainwater harvesting");
            var rwhGrid = NewFormGrid();
            AddFormRow(rwhGrid, 0, "Roof area (m²)",     _stRwhArea     = NewTextBox(""));
            AddFormRow(rwhGrid, 1, "Roof material",
                _stRwhMaterial = NewCombo(new[] { "Tiles (Cv 0.75)", "Flat membrane (Cv 0.90)" }, "Tiles (Cv 0.75)"));
            AddFormRow(rwhGrid, 2, "Annual rainfall (mm)", _stRwhRainfall = NewTextBox("700"));
            AddFormRow(rwhGrid, 3, "Annual demand (L)",   _stRwhDemand   = NewTextBox(""));
            sp.Children.Add(rwhGrid);
            AddBtn(sp, "Plumb_RWH", "RWH Yield (BS 8515)",
                "Annual yield + recommended storage volume from roof area + rainfall.");
            AddBtn(sp, "Plumbing_RainwaterCalc", "RWH / SuDS / Soakaway / Septic (legacy)",
                "Pre-existing combined calculator — retained.");

            AddCard(sp, "Soakaway");
            var soakGrid = NewFormGrid();
            AddFormRow(soakGrid, 0, "Design storm (mm/hr)", _stSoakStorm  = NewTextBox(""));
            AddFormRow(soakGrid, 1, "Catchment (m²)",       _stSoakArea   = NewTextBox(""));
            AddFormRow(soakGrid, 2, "Infiltration f (m/s)", _stSoakInfilt = NewTextBox(""));
            AddFormRow(soakGrid, 3, "Geometry",
                _stSoakGeometry = NewCombo(new[] { "Square pit", "Trench", "Circular" }, "Square pit"));
            sp.Children.Add(soakGrid);
            AddBtn(sp, "Plumb_Soakaway", "Soakaway (BRE Digest 365)",
                "Catchment + infiltration → soakaway volume.");

            AddCard(sp, "Septic tank");
            var septicGrid = NewFormGrid();
            AddFormRow(septicGrid, 0, "Population P", _stSepticPersons  = NewTextBox(""));
            AddFormRow(septicGrid, 1, "Tertiary",
                _stSepticTertiary = NewCombo(new[] { "Soakaway", "Reed bed", "Polishing pond" }, "Soakaway"));
            sp.Children.Add(septicGrid);
            AddBtn(sp, "Plumb_SepticTank", "Septic Tank (BS EN 12566-1)",
                "Primary chamber sizing from population equivalent. V_tank = 1500 + 190·P L.");

            t.Content = WrapScroll(sp);
            return t;
        }

        public StormInputs ReadStormInputs() => Dispatcher.Invoke(() => new StormInputs
        {
            RoofAreaM2     = D(_stRoofArea),     RoofType       = (_stRoofType?.SelectedItem as string) ?? "Flat",
            RoofRainfall   = D(_stRoofRainfall), RoofSafety     = D(_stRoofSafety),
            SudsAreaM2     = D(_stSudsArea),     SudsImperm     = D(_stSudsImperm),
            SudsGreenfield = D(_stSudsGreenfield), SudsReturn    = (_stSudsReturn?.SelectedItem as string) ?? "30yr",
            RwhAreaM2      = D(_stRwhArea),      RwhMaterial    = (_stRwhMaterial?.SelectedItem as string) ?? "Tiles (Cv 0.75)",
            RwhRainfallMm  = D(_stRwhRainfall),  RwhDemandL     = D(_stRwhDemand),
            SoakStormMmHr  = D(_stSoakStorm),    SoakAreaM2     = D(_stSoakArea),
            SoakInfiltMs   = D(_stSoakInfilt),   SoakGeometry   = (_stSoakGeometry?.SelectedItem as string) ?? "Square pit",
            SepticPersons  = (int)D(_stSepticPersons),
            SepticTertiary = (_stSepticTertiary?.SelectedItem as string) ?? "Soakaway"
        });

        private static double D(TextBox tb) => double.TryParse(tb?.Text, out var v) ? v : 0.0;

        // ── SPECIALTY tab — backflow matrix + cross-connections + material flags ──
        private ComboBox _spcBackflowThreshold;
        private CheckBox _spcMatGalvanic, _spcMatJointing, _spcMatWras, _spcMatAll;
        public DataGrid SpecialtyFluidMatrixGrid { get; private set; }
        public DataGrid SpecialtyCrossConnGrid   { get; private set; }

        private TabItem BuildSpecialtyTab()
        {
            var t = new TabItem { Header = "SPECIALTY" };
            var sp = NewSection();

            AddCard(sp, "Backflow / Cross-connection (BS EN 1717)");
            var bfOpts = NewFormGrid();
            AddFormRow(bfOpts, 0, "Flag threshold",
                _spcBackflowThreshold = NewCombo(new[] { "Cat ≥2", "Cat ≥3", "Cat ≥4" }, "Cat ≥3"));
            sp.Children.Add(bfOpts);
            AddBtn(sp, "Plumbing_BackflowAudit", "Fluid Category Audit",
                "Classify pipes Cat 1-5 and recommend SCV/DCV/RPZ/Air-gap.");
            SpecialtyFluidMatrixGrid = NewResultGrid(("Cat", "Cat"), ("Description", "Description"), ("Required device", "RequiredDevice"), ("Found", "Found"));
            sp.Children.Add(NewExpander("Fluid category matrix", WithEmptyHint(SpecialtyFluidMatrixGrid, "(run Fluid Category Audit to populate)"), expanded: false));
            AddBtn(sp, "Plumbing_CrossConnection", "Cross-Connection Scan",
                "Graph walk potable → non-potable.");
            SpecialtyCrossConnGrid = NewResultGrid(("System A", "SystemA"), ("System B", "SystemB"), ("Separation", "Separation"), ("Risk", "Risk"));
            sp.Children.Add(NewExpander("Cross-connections", WithEmptyHint(SpecialtyCrossConnGrid, "(run Cross-Connection Scan to populate)"), expanded: false));

            AddCard(sp, "Materials & jointing");
            var matFlags = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            void addFlag(CheckBox cb, string label, bool def) { cb.Content = label; cb.IsChecked = def; cb.Margin = new Thickness(0, 0, 8, 0); cb.FontSize = 11; matFlags.Children.Add(cb); }
            addFlag(_spcMatGalvanic = new CheckBox(), "Galvanic", true);
            addFlag(_spcMatJointing = new CheckBox(), "Jointing", true);
            addFlag(_spcMatWras     = new CheckBox(), "WRAS",     true);
            addFlag(_spcMatAll      = new CheckBox(), "All",      false);
            sp.Children.Add(matFlags);
            AddBtn(sp, "Plumbing_MaterialAudit", "Material & Jointing Audit",
                "Material × jointing × service compatibility + galvanic-pair walk + WRAS check. Reads scope flags above.");

            AddCard(sp, "Med gas / lab / pool");
            sp.Children.Add(new TextBlock
            {
                Text = "Med gas (HTM 02-01) lives in the HEALTHCARE tab (Mgas suite). Lab water + pool — future enhancement.",
                Margin = new Thickness(6, 4, 6, 8),
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            });

            t.Content = WrapScroll(sp);
            return t;
        }

        public SpecialtyOptions ReadSpecialtyOptions() => Dispatcher.Invoke(() => new SpecialtyOptions
        {
            BackflowThreshold = (_spcBackflowThreshold?.SelectedItem as string) ?? "Cat ≥3",
            MatGalvanic = _spcMatGalvanic?.IsChecked == true,
            MatJointing = _spcMatJointing?.IsChecked == true,
            MatWras     = _spcMatWras?.IsChecked     == true,
            MatAll      = _spcMatAll?.IsChecked      == true
        });

        // ── AUDIT tab — 5-tile RAG dashboard + per-domain drill-down grids ──
        private readonly Dictionary<string, TextBlock> _auditPctLabels = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, ProgressBar> _auditBars     = new Dictionary<string, ProgressBar>();
        private readonly Dictionary<string, TextBlock> _auditSubLabels = new Dictionary<string, TextBlock>();
        public DataGrid AuditSupplyGrid   { get; private set; }
        public DataGrid AuditDrainageGrid { get; private set; }
        public DataGrid AuditVentsGrid    { get; private set; }
        public DataGrid AuditBackflowGrid { get; private set; }
        public DataGrid AuditHtmGrid      { get; private set; }

        private TabItem BuildAuditTab()
        {
            var t = new TabItem { Header = "AUDIT" };
            var sp = NewSection();

            AddCard(sp, "RAG dashboard");
            var ragGrid = new UniformGrid { Rows = 2, Columns = 3, Margin = new Thickness(0, 2, 0, 6) };
            string[] domains = { "Supply", "Drainage", "Vents", "Backflow", "HTM 04-01" };
            foreach (var d in domains)
            {
                var tile = NewRagTile(d, out var pct, out var bar, out var sub);
                _auditPctLabels[d] = pct;
                _auditBars[d]      = bar;
                _auditSubLabels[d] = sub;
                ragGrid.Children.Add(tile);
            }
            sp.Children.Add(ragGrid);

            AddBtn(sp, "Plumb_FullAudit", "▶ Run Full Audit",
                "Runs all five compliance domains (Supply / Drainage / Vents / Backflow / HTM 04-01) and updates the dashboard above.");

            AddCard(sp, "Per-domain drill-down");
            (string, string)[] auditCols = { ("Element", "Element"), ("Issue", "Issue"), ("Severity", "Severity") };
            AuditSupplyGrid   = NewResultGrid(auditCols);
            AuditDrainageGrid = NewResultGrid(auditCols);
            AuditVentsGrid    = NewResultGrid(auditCols);
            AuditBackflowGrid = NewResultGrid(auditCols);
            AuditHtmGrid      = NewResultGrid(auditCols);
            sp.Children.Add(NewExpander("Supply",       WithEmptyHint(AuditSupplyGrid,   "(run Full Audit)"), expanded: false));
            sp.Children.Add(NewExpander("Drainage",     WithEmptyHint(AuditDrainageGrid, "(run Full Audit)"), expanded: false));
            sp.Children.Add(NewExpander("Vents",        WithEmptyHint(AuditVentsGrid,    "(run Full Audit)"), expanded: false));
            sp.Children.Add(NewExpander("Backflow",     WithEmptyHint(AuditBackflowGrid, "(run Full Audit)"), expanded: false));
            sp.Children.Add(NewExpander("HTM 04-01",    WithEmptyHint(AuditHtmGrid,      "(run Full Audit)"), expanded: false));

            AddCard(sp, "Per-domain audits (legacy)");
            AddBtn(sp, "Plumbing_TrapVentAudit",   "Trap & Vent Audit", "");
            AddBtn(sp, "Plumbing_StackCapacity",   "Stack Capacity",    "");
            AddBtn(sp, "Plumbing_BackflowAudit",   "Fluid Category Audit", "");
            AddBtn(sp, "Plumbing_CrossConnection", "Cross-Connection Scan", "");
            AddBtn(sp, "Plumbing_DeadLegScan",     "Dead-Leg Scan",     "");
            AddBtn(sp, "Plumbing_MaterialAudit",   "Material Audit",    "");
            t.Content = WrapScroll(sp);
            return t;
        }

        // Updates one of the five RAG tiles. RAG colour follows ComplianceScan
        // convention: <50 red · 50–80 amber · ≥80 green.
        public void SetAuditRag(string domain, double pct, int warningCount)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_auditPctLabels.TryGetValue(domain, out var pctLabel)) return;
                pctLabel.Text = pct.ToString("F0") + "%";
                _auditBars[domain].Value = Math.Max(0, Math.Min(100, pct));
                _auditSubLabels[domain].Text = warningCount + (warningCount == 1 ? " warning" : " warnings");
                Color c = pct >= 80 ? Color.FromRgb(34, 139, 34)
                        : pct >= 50 ? Color.FromRgb(218, 165, 32)
                                    : Color.FromRgb(192, 64, 64);
                _auditBars[domain].Foreground = new SolidColorBrush(c);
                pctLabel.Foreground          = new SolidColorBrush(c);
            });
        }

        // ── DOCS tab — preview grids + isometric / commissioning option groups ──
        private ComboBox _docsIsoSystem;
        private CheckBox _docsIsoDims, _docsIsoInverts;
        private CheckBox _docsCmFlush, _docsCmChlor, _docsCmPress, _docsCmLegRA;
        public DataGrid DocsPipeScheduleGrid { get; private set; }
        public DataGrid DocsManholeGrid      { get; private set; }
        public DataGrid DocsBoqGrid          { get; private set; }

        private TabItem BuildDocsTab()
        {
            var t = new TabItem { Header = "DOCS" };
            var sp = NewSection();

            AddCard(sp, "Schedules");
            AddBtn(sp, "Plumb_PipeSchedule", "Pipe Schedule",
                "Group pipes by system + DN + material with totals.");
            DocsPipeScheduleGrid = NewResultGrid(("System", "System"), ("DN", "Dn"), ("Material", "Material"), ("Length (m)", "LengthM"));
            sp.Children.Add(NewExpander("Pipe schedule preview", WithEmptyHint(DocsPipeScheduleGrid, "(run Pipe Schedule to populate)"), expanded: false));

            AddBtn(sp, "Plumb_ManholeSchedule", "Manhole / Access Chamber Schedule",
                "Reads PLM_DRN_INV_* params from manholes / inspection chambers.");
            DocsManholeGrid = NewResultGrid(("Ref", "Ref"), ("Inv In", "InvInM"), ("Inv Out", "InvOutM"), ("Cover", "CoverM"), ("Depth", "DepthM"));
            sp.Children.Add(NewExpander("Manhole schedule preview", WithEmptyHint(DocsManholeGrid, "(run Manhole Schedule to populate)"), expanded: false));

            AddBtn(sp, "Plumb_TMVRegister", "TMV Register",
                "Cross-listed with the SUPPLY tab.");

            AddCard(sp, "BOQ + isometrics");
            AddBtn(sp, "Plumb_BOQ", "Plumbing BOQ",
                "Pipes (m) + fittings (nr) + accessories (nr) — full BoQ row dump.");
            DocsBoqGrid = NewResultGrid(("Item", "Item"), ("Description", "Description"), ("Qty", "Qty"), ("Unit", "Unit"));
            sp.Children.Add(NewExpander("BOQ preview", WithEmptyHint(DocsBoqGrid, "(run Plumbing BOQ to populate)"), expanded: false));

            var isoOpts = NewFormGrid();
            AddFormRow(isoOpts, 0, "System",
                _docsIsoSystem = NewCombo(new[] { "All", "DCW", "DHW", "Sanitary", "Storm", "Selection" }, "All"));
            AddFormRow(isoOpts, 1, "Dimensions", _docsIsoDims    = NewCheck(true));
            AddFormRow(isoOpts, 2, "Inverts",    _docsIsoInverts = NewCheck(true));
            sp.Children.Add(isoOpts);
            AddBtn(sp, "Plumb_Isometric", "Plumbing Isometric (drawing-type)",
                "Routes through DrawingTypeRegistry — plumb-drainage-A1-1to100 / plumb-supply-A1-1to100. Reads system + flag options above.");

            AddCard(sp, "Commissioning shell");
            var cmFlags = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            void addCm(CheckBox cb, string label, bool def) { cb.Content = label; cb.IsChecked = def; cb.Margin = new Thickness(0, 0, 8, 0); cb.FontSize = 11; cmFlags.Children.Add(cb); }
            addCm(_docsCmFlush = new CheckBox(), "Flushing",      true);
            addCm(_docsCmChlor = new CheckBox(), "Chlorination",  true);
            addCm(_docsCmPress = new CheckBox(), "Pressure test", true);
            addCm(_docsCmLegRA = new CheckBox(), "Legionella RA", true);
            sp.Children.Add(cmFlags);
            AddBtn(sp, "Plumb_CommPack", "Stage Commissioning Pack",
                "Plans the commissioning artefact folder under _BIM_COORD/plumbing/commissioning. Reads scope flags above.");

            t.Content = WrapScroll(sp);
            return t;
        }

        public DocsOptions ReadDocsOptions() => Dispatcher.Invoke(() => new DocsOptions
        {
            IsoSystem      = (_docsIsoSystem?.SelectedItem as string) ?? "All",
            IsoIncludeDims      = _docsIsoDims?.IsChecked    == true,
            IsoIncludeInverts   = _docsIsoInverts?.IsChecked == true,
            CommFlushing        = _docsCmFlush?.IsChecked    == true,
            CommChlorination    = _docsCmChlor?.IsChecked    == true,
            CommPressureTest    = _docsCmPress?.IsChecked    == true,
            CommLegionellaRA    = _docsCmLegRA?.IsChecked    == true
        });

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

        // Empty result grid with the given (header, propertyPath) columns + a
        // hint row that disappears once a command sets ItemsSource. Tuned for
        // ~260 px width: every column auto-sizes; horizontal scroll on overflow.
        // Property paths are the names commands must use on populated row DTOs
        // (see Plumbing*Row classes at end of file).
        private static DataGrid NewResultGrid(params (string Header, string Path)[] cols)
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                FontSize = 10,
                MaxHeight = 180,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            foreach (var (header, path) in cols)
                dg.Columns.Add(new DataGridTextColumn
                {
                    Header  = header,
                    Binding = new System.Windows.Data.Binding(path),
                    Width   = DataGridLength.SizeToHeader
                });
            return dg;
        }

        // Slope-fix preview grid — Apply (CheckBox, two-way) + Pipe + Δ-elev.
        // Δ-elev's header carries a non-identifier glyph + dash that aren't
        // legal in a Binding path, so the grid is built explicitly here.
        private static DataGrid NewSlopeFixGrid()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 247, 250)),
                FontSize = 10,
                MaxHeight = 180,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            dg.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Apply",
                Binding = new System.Windows.Data.Binding(nameof(DrainageSlopeRow.Apply))
                    { Mode = System.Windows.Data.BindingMode.TwoWay }
            });
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = "Pipe",
                Binding = new System.Windows.Data.Binding(nameof(DrainageSlopeRow.Pipe)),
                IsReadOnly = true,
                Width = DataGridLength.SizeToHeader
            });
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = "Δ-elev (mm)",
                Binding = new System.Windows.Data.Binding(nameof(DrainageSlopeRow.DElevMm)),
                IsReadOnly = true,
                Width = DataGridLength.SizeToHeader
            });
            return dg;
        }

        // Wraps a DataGrid with an empty-state hint shown until the grid has rows.
        private static FrameworkElement WithEmptyHint(DataGrid grid, string hint)
        {
            var sp = new StackPanel();
            var tb = new TextBlock
            {
                Text = hint,
                Margin = new Thickness(6, 4, 6, 4),
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(tb);
            sp.Children.Add(grid);
            grid.LayoutUpdated += (s, e) =>
            {
                try { tb.Visibility = (grid.Items != null && grid.Items.Count > 0) ? Visibility.Collapsed : Visibility.Visible; }
                catch { }
            };
            return sp;
        }

        // Builds a horizontal RadioButton group bound to a single-string output.
        // First option is selected by default; selected value is read via the
        // attached Tag on the parent StackPanel.
        private static StackPanel NewRadioGroup(string groupName, string[] options, string selected)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            sp.Tag = string.IsNullOrEmpty(selected) ? options.FirstOrDefault() : selected;
            foreach (var o in options)
            {
                var rb = new RadioButton
                {
                    Content = o,
                    GroupName = groupName,
                    IsChecked = string.Equals(o, (string)sp.Tag, StringComparison.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = 11,
                    FontWeight = FontWeights.Normal
                };
                rb.Checked += (s, e) => sp.Tag = ((RadioButton)s).Content?.ToString() ?? sp.Tag;
                sp.Children.Add(rb);
            }
            return sp;
        }

        private static string ReadRadioGroup(StackPanel rg) => (rg?.Tag as string) ?? "";

        // 5-tile RAG strip cell. Click handler is wired in BuildAuditTab.
        private static Border NewRagTile(string title, out TextBlock pctLabel, out ProgressBar bar, out TextBlock subLabel)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 185, 195)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                Background = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 64, 96))
            });
            pctLabel = new TextBlock { Text = "—", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(pctLabel);
            bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Value = 0 };
            sp.Children.Add(bar);
            subLabel = new TextBlock { Text = "(run audit)", FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) };
            sp.Children.Add(subLabel);
            card.Child = sp;
            return card;
        }

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

    // ── Option DTOs returned by StingPlumbingPanel.Read*Options() ──
    // Plumbing commands consume these on the Revit thread so the panel does
    // the Dispatcher.Invoke marshalling and hands back plain immutable data.

    public class RouteAutoOptions
    {
        public string Scope { get; set; }
        public double MaxRadiusMm { get; set; }
        public string Preference { get; set; }
        public bool   EnforceSlope { get; set; }
        public bool   AutoInsertPTrap { get; set; }
        public bool   EmitHangers { get; set; }
    }

    public class RoutePTrapOptions
    {
        public string Scope { get; set; }
        public string TrapFamily { get; set; }
        public bool IncludeWc { get; set; }
        public bool IncludeBasin { get; set; }
        public bool IncludeShower { get; set; }
        public bool IncludeBath { get; set; }
        public bool IncludeSink { get; set; }
        public bool IncludeGully { get; set; }
        public bool IncludeFloor { get; set; }
    }

    public class RouteSleeveOptions
    {
        public string Scope { get; set; }
        public string FireRating { get; set; }
        public double MinOdMm { get; set; }
        public bool ExportIfcPfv { get; set; }
    }

    public class RouteHangerOptions
    {
        public string Scope { get; set; }
        public string RodSize { get; set; }
        public bool Horizontal { get; set; }
        public bool Vertical { get; set; }
        public bool TempCorrection { get; set; }
    }

    public class StormInputs
    {
        public double RoofAreaM2 { get; set; }
        public string RoofType { get; set; }
        public double RoofRainfall { get; set; }
        public double RoofSafety { get; set; }
        public double SudsAreaM2 { get; set; }
        public double SudsImperm { get; set; }
        public double SudsGreenfield { get; set; }
        public string SudsReturn { get; set; }
        public double RwhAreaM2 { get; set; }
        public string RwhMaterial { get; set; }
        public double RwhRainfallMm { get; set; }
        public double RwhDemandL { get; set; }
        public double SoakStormMmHr { get; set; }
        public double SoakAreaM2 { get; set; }
        public double SoakInfiltMs { get; set; }
        public string SoakGeometry { get; set; }
        public int    SepticPersons { get; set; }
        public string SepticTertiary { get; set; }
    }

    public class SpecialtyOptions
    {
        public string BackflowThreshold { get; set; }
        public bool MatGalvanic { get; set; }
        public bool MatJointing { get; set; }
        public bool MatWras { get; set; }
        public bool MatAll { get; set; }
    }

    public class DocsOptions
    {
        public string IsoSystem { get; set; }
        public bool IsoIncludeDims { get; set; }
        public bool IsoIncludeInverts { get; set; }
        public bool CommFlushing { get; set; }
        public bool CommChlorination { get; set; }
        public bool CommPressureTest { get; set; }
        public bool CommLegionellaRA { get; set; }
    }

    // ── Result-grid row DTOs ──
    // Property names match the binding paths declared on each result grid in
    // StingPlumbingPanel; commands assign List<TRow> to the corresponding
    // public DataGrid property's ItemsSource.

    public class SupplyFixtureScanRow
    {
        public string Fixture { get; set; }
        public int    Count   { get; set; }
        public double LuCw    { get; set; }
        public double LuHw    { get; set; }
    }

    public class SupplySizingRow
    {
        public string Section { get; set; }
        public double SigmaLu { get; set; }
        public int    Dn      { get; set; }
        public double VelocityMps { get; set; }
        public string Status  { get; set; }
    }

    public class SupplyTmvRow
    {
        public string Ref      { get; set; }
        public string Location { get; set; }
        public double SetC     { get; set; }
    }

    public class DrainageDuScanRow
    {
        public string Fixture { get; set; }
        public int    Count   { get; set; }
        public double DuEach  { get; set; }
        public double SigmaDu { get; set; }
    }

    public class DrainageSizingRow
    {
        public string Pipe    { get; set; }
        public double SigmaDu { get; set; }
        public int    Dn      { get; set; }
        public double VelocityMps { get; set; }
        public double HdRatio { get; set; }
        public string Status  { get; set; }
    }

    public class DrainageSlopeRow
    {
        public bool   Apply   { get; set; }
        public string Pipe    { get; set; }
        public double DElevMm { get; set; }
    }

    public class DrainageInvertRow
    {
        public string Pipe   { get; set; }
        public double UsInvM { get; set; }
        public double DsInvM { get; set; }
        public double CoverM { get; set; }
    }

    public class DrainageVentRow
    {
        public string Drain   { get; set; }
        public double Du      { get; set; }
        public int    VentDn  { get; set; }
        public double MaxLenM { get; set; }
        public string Flag    { get; set; }
    }

    public class SpecialtyFluidMatrixRow
    {
        public int    Cat            { get; set; }
        public string Description    { get; set; }
        public string RequiredDevice { get; set; }
        public string Found          { get; set; }
    }

    public class SpecialtyCrossConnRow
    {
        public string SystemA    { get; set; }
        public string SystemB    { get; set; }
        public string Separation { get; set; }
        public string Risk       { get; set; }
    }

    public class AuditIssueRow
    {
        public string Element  { get; set; }
        public string Issue    { get; set; }
        public string Severity { get; set; }
    }

    public class DocsPipeScheduleRow
    {
        public string System   { get; set; }
        public int    Dn       { get; set; }
        public string Material { get; set; }
        public double LengthM  { get; set; }
    }

    public class DocsManholeRow
    {
        public string Ref      { get; set; }
        public double InvInM   { get; set; }
        public double InvOutM  { get; set; }
        public double CoverM   { get; set; }
        public double DepthM   { get; set; }
    }

    public class DocsBoqRow
    {
        public string Item        { get; set; }
        public string Description { get; set; }
        public double Qty         { get; set; }
        public string Unit        { get; set; }
    }
}
