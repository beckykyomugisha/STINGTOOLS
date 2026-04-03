using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned from the TemplateManagerDashboard.
    /// </summary>
    public class TemplateManagerResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } = string.Empty;
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>
    /// Unified Template Manager Dashboard consolidating ALL template, style, filter,
    /// and VG override operations into a single 5-tab WPF dialog organized by
    /// execution order: SETUP, TEMPLATES, STYLES, SCHEDULES &amp; DATA, AUTOMATION.
    /// Replaces the multi-step wizard with a comprehensive single-dialog interface.
    /// Each operation is presented as a card with title, description, and Run button.
    /// </summary>
    internal static class TemplateManagerDashboard
    {
        // ── Theme colours (light corporate) ─────────────────────────────
        private static readonly Color BgColor      = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color PanelBg      = Colors.White;
        private static readonly Color HeaderBg     = Color.FromRgb(0x1A, 0x23, 0x7E);
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgDark       = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgDim        = Color.FromRgb(0x88, 0x88, 0x88);
        private static readonly Color BorderClr    = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color GreenAccent  = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color HoverBg      = Color.FromRgb(0xFD, 0xF0, 0xDD);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static readonly SolidColorBrush BrBg      = FZ(BgColor);
        private static readonly SolidColorBrush BrPanel    = FZ(PanelBg);
        private static readonly SolidColorBrush BrHeader   = FZ(HeaderBg);
        private static readonly SolidColorBrush BrAccent   = FZ(AccentOrange);
        private static readonly SolidColorBrush BrFg       = FZ(FgDark);
        private static readonly SolidColorBrush BrFgDim    = FZ(FgDim);
        private static readonly SolidColorBrush BrBorder   = FZ(BorderClr);
        private static readonly SolidColorBrush BrWhite    = FZ(PanelBg);
        private static readonly SolidColorBrush BrGreen    = FZ(GreenAccent);
        private static readonly SolidColorBrush BrHover    = FZ(HoverBg);
        private static readonly SolidColorBrush BrHeaderFg = FZ(Color.FromRgb(0xBB, 0xBB, 0xBB));

        // ── Operation definition ────────────────────────────────────────
        private class OpDef
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string OperationTag { get; set; } = string.Empty;
            public bool IsHighlighted { get; set; }
        }

        /// <summary>
        /// Show the Template Manager Dashboard and return the user's selection.
        /// </summary>
        public static TemplateManagerResult Show()
        {
            var result = new TemplateManagerResult();

            var win = new Window
            {
                Title = "STING Template Manager Dashboard",
                Width = 820,
                Height = 600,
                MinWidth = 720,
                MinHeight = 500,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBg,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = handle;
            }
            catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboard: Could not set owner — {ex.Message}"); }

            // ── Root layout: header + body + footer ─────────────────────
            var root = new DockPanel { LastChildFill = true };

            // ── Header ──────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Template Manager Dashboard",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrWhite
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Templates, Styles, Filters, VG Overrides & Automation",
                FontSize = 11,
                Foreground = BrHeaderFg,
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Footer ──────────────────────────────────────────────────
            var footer = new Border { Padding = new Thickness(12, 8, 12, 10), Background = BrPanel };
            var footerPanel = new DockPanel { LastChildFill = false };

            var btnClose = new Button
            {
                Content = "Close",
                Width = 80,
                Height = 30,
                FontSize = 12,
                Background = BrWhite,
                BorderBrush = BrBorder,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (_, __) => { win.DialogResult = false; };
            DockPanel.SetDock(btnClose, Dock.Right);
            footerPanel.Children.Add(btnClose);

            var statusText = new TextBlock
            {
                Text = "Select an operation to run.",
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center
            };
            footerPanel.Children.Add(statusText);
            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Body: TabControl ────────────────────────────────────────
            var tabs = new TabControl
            {
                Background = BrBg,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0)
            };

            // Handler: when user clicks Run on any operation card
            void OnRun(string operationTag)
            {
                result.Confirmed = true;
                result.Operation = operationTag;
                result.Options = new Dictionary<string, string>();
                win.DialogResult = true;
            }

            // ── Tab 1: SETUP ────────────────────────────────────────────
            tabs.Items.Add(BuildTab("SETUP", BuildSetupOps(), OnRun));

            // ── Tab 2: TEMPLATES ────────────────────────────────────────
            tabs.Items.Add(BuildTab("TEMPLATES", BuildTemplateOps(), OnRun));

            // ── Tab 3: STYLES ───────────────────────────────────────────
            tabs.Items.Add(BuildTab("STYLES", BuildStyleOps(), OnRun));

            // ── Tab 4: SCHEDULES & DATA ─────────────────────────────────
            tabs.Items.Add(BuildTab("SCHEDULES & DATA", BuildScheduleDataOps(), OnRun));

            // ── Tab 5: AUTOMATION ───────────────────────────────────────
            tabs.Items.Add(BuildTab("AUTOMATION", BuildAutomationOps(), OnRun));

            root.Children.Add(tabs);
            win.Content = root;

            // ── Keyboard shortcuts ──────────────────────────────────────
            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    win.DialogResult = false;
            };

            bool? dlgResult = false;
            try { dlgResult = win.ShowDialog(); }
            catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboard: ShowDialog failed — {ex.Message}"); }

            if (dlgResult != true)
                result.Confirmed = false;

            return result;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB BUILDER
        // ════════════════════════════════════════════════════════════════

        private static TabItem BuildTab(string header, List<OpDef> ops, Action<string> onRun)
        {
            var tab = new TabItem
            {
                Header = new TextBlock
                {
                    Text = header,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(10, 4, 10, 4)
                },
                Background = BrBg
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 12, 16, 12)
            };

            var stack = new StackPanel();

            foreach (var op in ops)
            {
                stack.Children.Add(BuildOperationCard(op, onRun));
            }

            scroll.Content = stack;
            tab.Content = scroll;
            return tab;
        }

        // ════════════════════════════════════════════════════════════════
        // OPERATION CARD
        // ════════════════════════════════════════════════════════════════

        private static UIElement BuildOperationCard(OpDef op, Action<string> onRun)
        {
            var accentBrush = op.IsHighlighted ? BrGreen : BrAccent;

            var card = new Border
            {
                Background = BrWhite,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(4, 1, 1, 1),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(2),
                Cursor = Cursors.Hand
            };
            // Left accent border via the 4px left BorderThickness
            card.BorderBrush = new SolidColorBrush(BorderClr);

            // Use a Grid to overlay the left accent colour
            var outerGrid = new Grid();

            // Left accent strip
            var accentStrip = new Border
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = accentBrush,
                Margin = new Thickness(-10, -8, 0, -8),
                CornerRadius = new CornerRadius(2, 0, 0, 2)
            };
            outerGrid.Children.Add(accentStrip);

            // Content row: title + description + button
            var contentGrid = new Grid { Margin = new Thickness(4, 0, 0, 0) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left side: title and description stacked
            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = op.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFg,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textStack.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(op.Description))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = op.Description,
                    FontSize = 11,
                    Foreground = BrFgDim,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 12, 0)
                });
            }

            Grid.SetColumn(textStack, 0);
            contentGrid.Children.Add(textStack);

            // Right side: Run button
            var btnRun = new Button
            {
                Content = "\u25B6  Run",
                Width = 70,
                Height = 28,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = accentBrush,
                Foreground = BrWhite,
                BorderBrush = accentBrush,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            string tag = op.OperationTag;
            btnRun.Click += (_, __) =>
            {
                try { onRun(tag); }
                catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboard: Run '{tag}' failed — {ex.Message}"); }
            };

            Grid.SetColumn(btnRun, 1);
            contentGrid.Children.Add(btnRun);

            outerGrid.Children.Add(contentGrid);
            card.Child = outerGrid;

            // Hover effect
            card.MouseEnter += (_, __) => { card.Background = BrHover; };
            card.MouseLeave += (_, __) => { card.Background = BrWhite; };

            // Click anywhere on the card also triggers run
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is Button) return; // button handles its own click
                try { onRun(tag); }
                catch (Exception ex) { StingLog.Warn($"TemplateManagerDashboard: Card click '{tag}' failed — {ex.Message}"); }
            };

            return card;
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 1: SETUP — Foundation infrastructure
        // ════════════════════════════════════════════════════════════════

        private static List<OpDef> BuildSetupOps()
        {
            return new List<OpDef>
            {
                new OpDef
                {
                    Title = "Create Shared Parameters",
                    Description = "Bind STING shared parameters to project categories (2-pass binding from MR_PARAMETERS.txt)",
                    OperationTag = "CreateParameters"
                },
                new OpDef
                {
                    Title = "Create Filters",
                    Description = "28 multi-category view filters (Mechanical, Electrical, Plumbing, etc.)",
                    OperationTag = "CreateFilters"
                },
                new OpDef
                {
                    Title = "Create Worksets",
                    Description = "35 ISO 19650-compliant worksets",
                    OperationTag = "CreateWorksets"
                },
                new OpDef
                {
                    Title = "Create Line Patterns",
                    Description = "10 ISO 128-2:2020 line patterns",
                    OperationTag = "CreateLinePatterns"
                },
                new OpDef
                {
                    Title = "Create Phases",
                    Description = "Report phase status",
                    OperationTag = "CreatePhases"
                },
                new OpDef
                {
                    Title = "\u2605 Master Setup",
                    Description = "Run all setup steps in sequence (15 steps)",
                    OperationTag = "MasterSetup",
                    IsHighlighted = true
                }
            };
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 2: TEMPLATES — View template management
        // ════════════════════════════════════════════════════════════════

        private static List<OpDef> BuildTemplateOps()
        {
            return new List<OpDef>
            {
                new OpDef
                {
                    Title = "Create View Templates",
                    Description = "23 STING discipline view templates with VG",
                    OperationTag = "ViewTemplates"
                },
                new OpDef
                {
                    Title = "Auto-Assign Templates",
                    Description = "5-layer intelligent matching (name \u2192 level \u2192 phase \u2192 scope \u2192 type)",
                    OperationTag = "AutoAssignTemplates"
                },
                new OpDef
                {
                    Title = "Clone Template",
                    Description = "Deep clone with VG, filters, and overrides",
                    OperationTag = "CloneTemplate"
                },
                new OpDef
                {
                    Title = "Apply Filters to Templates",
                    Description = "Apply STING filters to all STING templates",
                    OperationTag = "ApplyFilters"
                },
                new OpDef
                {
                    Title = "Sync VG Overrides",
                    Description = "Re-apply VG overrides to restore discipline colours",
                    OperationTag = "SyncTemplateOverrides"
                },
                new OpDef
                {
                    Title = "Auto-Fix Templates",
                    Description = "One-click template health repair",
                    OperationTag = "AutoFixTemplate"
                },
                new OpDef
                {
                    Title = "Batch VG Reset",
                    Description = "Reset VG settings across multiple views",
                    OperationTag = "BatchVGReset"
                },
                new OpDef
                {
                    Title = "Template Audit",
                    Description = "Deep compliance audit with scoring",
                    OperationTag = "TemplateAudit"
                },
                new OpDef
                {
                    Title = "Template Diff",
                    Description = "Compare VG settings between two templates",
                    OperationTag = "TemplateDiff"
                },
                new OpDef
                {
                    Title = "Compliance Score",
                    Description = "Weighted 10-point scoring per view",
                    OperationTag = "TemplateComplianceScore"
                }
            };
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 3: STYLES — ISO standard styles
        // ════════════════════════════════════════════════════════════════

        private static List<OpDef> BuildStyleOps()
        {
            return new List<OpDef>
            {
                new OpDef
                {
                    Title = "Fill Patterns",
                    Description = "12 ISO 128-2:2020 fill patterns",
                    OperationTag = "CreateFillPatterns"
                },
                new OpDef
                {
                    Title = "Line Styles",
                    Description = "16 ISO line styles from CSV",
                    OperationTag = "CreateLineStyles"
                },
                new OpDef
                {
                    Title = "Object Styles",
                    Description = "40+ ISO category line weights/colours",
                    OperationTag = "CreateObjectStyles"
                },
                new OpDef
                {
                    Title = "Text Styles",
                    Description = "12 ISO 3098 text note types",
                    OperationTag = "CreateTextStyles"
                },
                new OpDef
                {
                    Title = "Dimension Styles",
                    Description = "7 ISO dimension types",
                    OperationTag = "CreateDimensionStyles"
                },
                new OpDef
                {
                    Title = "VG Overrides",
                    Description = "6-layer VG override intelligence",
                    OperationTag = "CreateVGOverrides"
                }
            };
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 4: SCHEDULES & DATA
        // ════════════════════════════════════════════════════════════════

        private static List<OpDef> BuildScheduleDataOps()
        {
            return new List<OpDef>
            {
                new OpDef
                {
                    Title = "Create Template Schedules",
                    Description = "Standard schedule templates from CSV",
                    OperationTag = "CreateTemplateSchedules"
                },
                new OpDef
                {
                    Title = "Material Schedules",
                    Description = "Material takeoff schedules (8 categories)",
                    OperationTag = "MaterialSchedules"
                },
                new OpDef
                {
                    Title = "Cable Trays",
                    Description = "Cable tray types from MEP_MATERIALS.csv",
                    OperationTag = "CreateCableTrays"
                },
                new OpDef
                {
                    Title = "Conduits",
                    Description = "Conduit types from MEP_MATERIALS.csv",
                    OperationTag = "CreateConduits"
                },
                new OpDef
                {
                    Title = "Batch Family Parameters",
                    Description = "Add shared parameters to families",
                    OperationTag = "BatchFamilyParams"
                },
                new OpDef
                {
                    Title = "Family Parameter Processor",
                    Description = "Batch .rfa parameter processing",
                    OperationTag = "FamilyParameterProcessor"
                }
            };
        }

        // ════════════════════════════════════════════════════════════════
        // TAB 5: AUTOMATION
        // ════════════════════════════════════════════════════════════════

        private static List<OpDef> BuildAutomationOps()
        {
            return new List<OpDef>
            {
                new OpDef
                {
                    Title = "\u2605 Template Setup Wizard",
                    Description = "15-step complete automation pipeline",
                    OperationTag = "TemplateSetupWizard",
                    IsHighlighted = true
                },
                new OpDef
                {
                    Title = "\u2605 Project Setup Wizard",
                    Description = "7-page comprehensive project wizard",
                    OperationTag = "ProjectSetup",
                    IsHighlighted = true
                },
                new OpDef
                {
                    Title = "Validate Template",
                    Description = "45 validation checks",
                    OperationTag = "ValidateTemplate"
                },
                new OpDef
                {
                    Title = "Dynamic Bindings",
                    Description = "Load bindings from BINDING_COVERAGE_MATRIX.csv",
                    OperationTag = "DynamicBindings"
                },
                new OpDef
                {
                    Title = "Schema Validate",
                    Description = "Validate CSV columns match MATERIAL_SCHEMA.json",
                    OperationTag = "SchemaValidate"
                },
                new OpDef
                {
                    Title = "Template VG Audit",
                    Description = "Visual Graphics override analysis",
                    OperationTag = "TemplateVGAudit"
                }
            };
        }
    }
}
