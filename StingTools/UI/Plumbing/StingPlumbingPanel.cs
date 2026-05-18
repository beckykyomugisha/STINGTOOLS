// STING Plumbing Center — 8-tab dockable panel UI. Phase 179.
// Programmatic WPF (no XAML) for a tight commit. Buttons dispatch via
// StingPlumbingCommandHandler on the Revit API thread.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Button = System.Windows.Controls.Button;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Mep;
using StingTools.Core.Routing;
using StingTools.Core.Calc;
namespace StingTools.UI.Plumbing
{
    public class StingPlumbingPanel : Page
    {
        public StingPlumbingPanel()
        {
            Title = "STING Plumbing";
            Content = BuildRoot();
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
            try { _statusText.Text = text; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private TabItem BuildSystemTab()
        {
            var t = new TabItem { Header = "SYSTEM" };
            var sp = NewSection();
            AddCard(sp, "Project & Standards");
            AddBtn(sp, "Plumb_SaveSystemConfig", "Configure System (Building / Standards / Materials)…",
                "Open the multi-section config dialog: building type → K factor, drainage / supply standard, pipe materials per service, velocity + slope limits.");
            AddBtn(sp, "Plumb_LoadSystemConfig", "Show Current System Config",
                "Read-only summary of the saved plumbing system config from _BIM_COORD/plumbing_system_config.json.");
            AddCard(sp, "Workflows");
            AddBtn(sp, "Plumb_FullAudit", "Run Full Plumbing Audit (RAG)",
                "Runs all five compliance domains (Supply / Drainage / Vents / Backflow / HTM 04-01) and produces the RAG dashboard.");
            t.Content = WrapScroll(sp);
            return t;
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

            // ── Plan-level symbol placement (STING_PLUMBING_SYMBOLS.json) ─────
            AddCard(sp, "Symbol placement — sanitary fixtures");
            AddWrapBtns(sp,
                ("PlumbSym_WC",      "WC"),
                ("PlumbSym_Urinal",  "Urinal"),
                ("PlumbSym_Bidet",   "Bidet"),
                ("PlumbSym_WHB",     "Wash-hand basin"),
                ("PlumbSym_VanityBasin", "Vanity basin"),
                ("PlumbSym_Bath",    "Bath"),
                ("PlumbSym_Shower",  "Shower tray"));

            AddCard(sp, "Symbol placement — sinks");
            AddWrapBtns(sp,
                ("PlumbSym_SingleSink",   "Single sink"),
                ("PlumbSym_DoubleSink",   "Double sink"),
                ("PlumbSym_CleanersSink", "Cleaner's sink"));

            AddCard(sp, "Symbol placement — drainage points");
            AddWrapBtns(sp,
                ("PlumbSym_FloorDrainRound",  "Floor drain (round)"),
                ("PlumbSym_FloorDrainSquare", "Floor drain (square)"),
                ("PlumbSym_Gulley",           "Yard gulley"));

            AddCard(sp, "Symbol placement — valves & accessories");
            AddWrapBtns(sp,
                ("PlumbSym_GateValve",       "Gate valve"),
                ("PlumbSym_GlobeValve",      "Globe valve"),
                ("PlumbSym_BallValve",       "Ball valve"),
                ("PlumbSym_ButterflyValve",  "Butterfly valve"),
                ("PlumbSym_CheckValve",      "Check valve"),
                ("PlumbSym_PRV",             "PRV"),
                ("PlumbSym_Strainer",        "Y-strainer"),
                ("PlumbSym_FlexConn",        "Flex connector"));

            AddCard(sp, "Symbol placement — equipment");
            AddWrapBtns(sp,
                ("PlumbSym_HWCDirect",   "HWC (direct)"),
                ("PlumbSym_HWCIndirect", "HWC (indirect)"));

            AddCard(sp, "Browse all plumbing symbols");
            AddBtn(sp, "PlumbSym_BrowseAll", "Browse & Place…",
                "Full searchable picker across all 24 plumbing symbols — search by name, subcategory, or BS EN standard.");

            t.Content = WrapScroll(sp);
            return t;
        }

        private static void AddWrapBtns(Panel host, params (string tag, string label)[] btns)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (tag, label) in btns)
            {
                var b = new Button
                {
                    Content = label,
                    Tag     = tag,
                    ToolTip = tag,
                    Margin  = new Thickness(2),
                    Padding = new Thickness(6, 4, 6, 4),
                    FontSize = 10
                };
                b.Click += (s, e) =>
                {
                    try
                    {
                        var tg = ((Button)s).Tag as string;
                        if (string.IsNullOrEmpty(tg)) return;
                        StingPlumbingCommandHandler.Instance?.SetCommand(tg);
                        StingPlumbingCommandHandler.Event?.Raise();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("STING Plumbing dispatch error: " + ex.Message);
                    }
                };
                wrap.Children.Add(b);
            }
            host.Children.Add(wrap);
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
