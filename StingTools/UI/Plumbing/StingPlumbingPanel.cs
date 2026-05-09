// STING Plumbing Center — 6-tab dockable panel UI. Phase 178c.
// Programmatic WPF (no XAML) for a tight commit. Buttons dispatch via
// StingPlumbingCommandHandler on the Revit API thread.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;

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
                Text = "STING Plumbing — BS-UK · ready",
                Foreground = Brushes.White,
                Margin = new Thickness(10, 6, 10, 0),
                FontSize = 11
            };
            status.Child = _statusText;
            root.Children.Add(status);

            var tabs = new TabControl { Margin = new Thickness(2) };
            tabs.Items.Add(BuildSupplyTab());
            tabs.Items.Add(BuildDrainageTab());
            tabs.Items.Add(BuildStormTab());
            tabs.Items.Add(BuildSpecialtyTab());
            tabs.Items.Add(BuildAuditTab());
            tabs.Items.Add(BuildDocsTab());
            root.Children.Add(tabs);

            return root;
        }

        private TextBlock _statusText;

        public void SetStatus(string text)
        {
            try { _statusText.Text = text; } catch { }
        }

        private TabItem BuildSupplyTab()
        {
            var t = new TabItem { Header = "SUPPLY" };
            var sp = NewSection();
            AddCard(sp, "Loading units & sizing");
            AddBtn(sp, "Plumbing_AutoSizeDrainage", "Auto-Size DCW/DHW",
                "BS 8558 / BS EN 806 LU sizing — runs DFU/WSFU pipeline (Phase 178c v1).");
            AddCard(sp, "DHW recirculation");
            AddBtn(sp, "Plumbing_RecircBalance", "Recirc Loop + DRV Pre-Set",
                "Pipe heat-loss → pump duty + DRV kV pre-sets per branch.");
            AddCard(sp, "Pressure zoning");
            AddBtn(sp, "Plumbing_PRVSchedule", "PRV Schedule",
                "Static pressure per level + PRV recommendations per Approved Doc G (500 kPa).");
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
            AddCard(sp, "DFU sizing");
            AddBtn(sp, "Plumbing_AutoSizeDrainage", "Auto-Size Drainage",
                "DFU accumulation → BS EN 12056 / IPC 2021 sizing → slope correct → vent design.");
            AddCard(sp, "Trap & vent");
            AddBtn(sp, "Plumbing_TrapVentAudit", "Trap & Vent Audit",
                "Audit trap type + seal depth + max branch length + vent DN.");
            AddCard(sp, "Stack capacity");
            AddBtn(sp, "Plumbing_StackCapacity", "Stack Capacity (BS EN 12056-2 §6.5)",
                "Cumulative DU vs Table 11 capacity — flags > 70 % induced-siphonage risk.");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildStormTab()
        {
            var t = new TabItem { Header = "STORM" };
            var sp = NewSection();
            AddCard(sp, "Rainwater & SuDS");
            AddBtn(sp, "Plumbing_RainwaterCalc", "RWH / SuDS / Soakaway / Septic",
                "BS 8515 yield + CIRIA C753 attenuation + BRE 365 soakaway + BS EN 12566-1 septic tank.");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildSpecialtyTab()
        {
            var t = new TabItem { Header = "SPECIALTY" };
            var sp = NewSection();
            AddCard(sp, "Backflow / Cross-connection");
            AddBtn(sp, "Plumbing_BackflowAudit", "Fluid Category Audit",
                "BS EN 1717 — classify pipes Cat 1-5 and recommend SCV/DCV/RPZ/Air-gap.");
            AddBtn(sp, "Plumbing_CrossConnection", "Cross-Connection Scan",
                "Graph walk potable → non-potable; flag direct connections lacking Cat-3+ separation.");
            AddCard(sp, "Med-gas / lab / pool");
            var stub = new TextBlock
            {
                Text = "Med gas (HTM 02-01), lab water, pool — Phase 178d follow-up.",
                Margin = new Thickness(6, 4, 6, 8),
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic
            };
            sp.Children.Add(stub);
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildAuditTab()
        {
            var t = new TabItem { Header = "AUDIT" };
            var sp = NewSection();
            AddCard(sp, "Materials & jointing");
            AddBtn(sp, "Plumbing_MaterialAudit", "Material & Jointing Audit",
                "Material × jointing × service compatibility + galvanic-pair walk + WRAS check.");
            AddCard(sp, "Trap, vent, stack");
            AddBtn(sp, "Plumbing_TrapVentAudit", "Trap & Vent Audit", "");
            AddBtn(sp, "Plumbing_StackCapacity",  "Stack Capacity",   "");
            AddCard(sp, "Backflow & dead-legs");
            AddBtn(sp, "Plumbing_BackflowAudit",   "Fluid Category Audit", "");
            AddBtn(sp, "Plumbing_CrossConnection", "Cross-Connection Scan", "");
            AddBtn(sp, "Plumbing_DeadLegScan",     "Dead-Leg Scan",         "");
            t.Content = WrapScroll(sp);
            return t;
        }

        private TabItem BuildDocsTab()
        {
            var t = new TabItem { Header = "DOCS" };
            var sp = NewSection();
            var stub = new TextBlock
            {
                Text = "Schedules · BOQ · TMV register · manhole schedule · " +
                       "commissioning shell — Phase 178d follow-up.",
                Margin = new Thickness(6, 8, 6, 8),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic
            };
            sp.Children.Add(stub);
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
