// StingTools v4 MVP — Fabrication Result dialog (light-themed).
//
// Replaces the TaskDialog summary that fired after Generate Package.
// Matches the Fabrication Workspace's visual language: same light
// palette, same card pattern, same orange accent. Surfaces the
// headline counts plus action buttons (Open last sheet, View log,
// Open workspace, Close) so users can take the next step without
// hunting through the dock panel.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Fabrication;

// Disambiguate WPF vs Revit types.
using Color    = System.Windows.Media.Color;
using TextBox  = System.Windows.Controls.TextBox;
using Grid     = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    public sealed class FabricationResultDialog : Window
    {
        // ── light palette (matches FabricationWorkspaceDialog) ──
        private static readonly Color BgColor     = Color.FromRgb(0xFA, 0xFA, 0xFA);
        private static readonly Color CardBg      = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CardBorder  = Color.FromRgb(0xCF, 0xD8, 0xDC);
        private static readonly Color FgColor     = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color SubtleColor = Color.FromRgb(0x66, 0x66, 0x66);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D); // STING orange
        private static readonly Color GreenColor  = Color.FromRgb(0x2E, 0x7D, 0x32); // success
        private static readonly Color RedColor    = Color.FromRgb(0xC6, 0x28, 0x28); // failures
        private static readonly Color AmberColor  = Color.FromRgb(0xE6, 0x5A, 0x00); // warnings
        private static readonly Color NeutralBtn  = Color.FromRgb(0xE8, 0xE8, 0xE8);

        private readonly Document _doc;
        private readonly FabricationResult _res;

        public FabricationResultDialog(Document doc, FabricationResult res)
        {
            _doc = doc;
            _res = res ?? new FabricationResult();

            Title = "STING v4 — Fabrication Package";
            Width = 720; Height = 560;
            MinWidth = 600; MinHeight = 420;
            Background = new SolidColorBrush(BgColor);
            Foreground = new SolidColorBrush(FgColor);
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Force-reset inherited theme resources so the dialog is
            // always light, even when the host dock panel runs a dark
            // or non-default theme.
            Resources["PrimaryBg"]   = new SolidColorBrush(BgColor);
            Resources["SecondaryBg"] = new SolidColorBrush(CardBg);
            Resources["AccentBrush"] = new SolidColorBrush(AccentColor);
            Resources["BorderColor"] = new SolidColorBrush(CardBorder);
            Resources["ButtonBg"]    = new SolidColorBrush(NeutralBtn);
            Resources["ButtonFg"]    = new SolidColorBrush(FgColor);
            DarkDialogTheme.ApplyComboBoxFix(this, CardBg, FgColor, CardBorder);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { }

            Content = BuildLayout();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYOUT — header banner + summary cards + footer
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12), LastChildFill = true };

            var header = BuildHeader();   DockPanel.SetDock(header, Dock.Top);    root.Children.Add(header);
            var footer = BuildFooter();   DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            root.Children.Add(BuildBody()); // last child fills remainder

            return root;
        }

        // ─── Header banner ──────────────────────────────────────────────

        private UIElement BuildHeader()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 8),
            };
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = "Fabrication package generated.",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(StatusColor()),
            });
            stack.Children.Add(new TextBlock
            {
                Text = _res.FormatSummary(),
                FontSize = 12,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 4, 0, 0),
            });

            border.Child = stack;
            return border;
        }

        private Color StatusColor()
        {
            if (_res.FailedCount > 0) return RedColor;
            if (_res.Warnings.Count > 0) return AmberColor;
            return GreenColor;
        }

        // ─── Body — assemblies / sheets / warnings cards ────────────────

        private UIElement BuildBody()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var stack = new StackPanel();

            stack.Children.Add(BuildAssembliesCard());
            stack.Children.Add(BuildSheetsCard());
            stack.Children.Add(BuildSymbolsCard());
            if (_res.TitleBlockFallbacks.Count > 0) stack.Children.Add(BuildTitleBlockFallbacksCard());
            if (_res.Warnings.Count > 0) stack.Children.Add(BuildWarningsCard());

            scroll.Content = stack;
            return scroll;
        }

        private UIElement BuildAssembliesCard()
        {
            var body = new StackPanel();
            if (_res.AssembliesByDiscipline.Count == 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = "No assemblies created.",
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 12,
                });
            }
            else
            {
                foreach (var kv in _res.AssembliesByDiscipline.OrderByDescending(kv => kv.Value))
                {
                    body.Children.Add(MetricRow(kv.Key, kv.Value.ToString()));
                }
                body.Children.Add(MetricRow("Total",
                    _res.AssembliesByDiscipline.Values.Sum().ToString(),
                    bold: true));
            }
            return Card("Assemblies by discipline", body);
        }

        private UIElement BuildSheetsCard()
        {
            var body = new StackPanel();
            body.Children.Add(MetricRow("Generated", _res.SheetIds.Count.ToString()));
            body.Children.Add(MetricRow("Failed", _res.FailedCount.ToString(),
                accent: _res.FailedCount > 0 ? RedColor : (Color?)null));
            return Card("Sheets", body);
        }

        private UIElement BuildSymbolsCard()
        {
            var body = new StackPanel();
            // FabricationOptions is static — read directly rather than
            // aliasing the type to a local (CS0723 / CS0176).
            bool optionOn = StingTools.Commands.Fabrication.FabricationOptions.PlaceISO6412Symbols;
            body.Children.Add(MetricRow("Placement option", optionOn ? "ON" : "OFF",
                accent: optionOn ? GreenColor : (Color?)SubtleColor));
            body.Children.Add(MetricRow("Mode",
                StingTools.Commands.Fabrication.FabricationOptions.SymbolPlacementMode.ToString()));
            body.Children.Add(MetricRow("Symbols placed", _res.SymbolsPlaced.ToString(),
                accent: _res.SymbolsPlaced > 0 ? GreenColor : (Color?)null,
                bold: _res.SymbolsPlaced > 0));
            if (_res.SymbolsReplaced > 0)
                body.Children.Add(MetricRow("Symbols replaced", _res.SymbolsReplaced.ToString(),
                    accent: AmberColor));
            if (_res.UnmatchedMembers > 0)
                body.Children.Add(MetricRow("Unmatched members", _res.UnmatchedMembers.ToString(),
                    accent: AmberColor));
            body.Children.Add(MetricRow("Missing families", _res.MissingFamilies.Count.ToString(),
                accent: _res.MissingFamilies.Count > 0 ? AmberColor : (Color?)null));

            // Per-discipline breakdown when there's more than one.
            if (_res.SymbolsByDiscipline.Count > 1)
            {
                body.Children.Add(new TextBlock
                {
                    Text = "By discipline:",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(0, 6, 0, 4),
                });
                foreach (var kv in _res.SymbolsByDiscipline.OrderByDescending(kv => kv.Value))
                    body.Children.Add(MetricRow("  " + kv.Key, kv.Value.ToString()));
            }

            if (_res.MissingFamilies.Count > 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"Missing .rfa files (first {Math.Min(6, _res.MissingFamilies.Count)} of {_res.MissingFamilies.Count}):",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(0, 6, 0, 4),
                });
                foreach (var fam in _res.MissingFamilies.OrderBy(f => f).Take(6))
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = "• " + fam,
                        Foreground = new SolidColorBrush(FgColor),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1, 0, 1),
                    });
                }
                body.Children.Add(new TextBlock
                {
                    Text = "Drop the .rfa files into Families/ISO6412/ next to the plugin and re-run, " +
                           "or use the standalone Place ISO 6412 Symbols command.",
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                });
            }
            else if (optionOn && _res.AssemblyIds.Count > 0 && _res.SymbolsPlaced == 0)
            {
                string hint = _res.UnmatchedMembers > 0
                    ? "Option was ON but every member was unmatched — extend STING_ISO_SYMBOLS_INDEX.csv " +
                      "with codes that match your fitting names, or check the family keyword tokens."
                    : "Option was ON but nothing placed — check that STING_ISO_SYMBOLS_INDEX.csv " +
                      "is in the data folder and the symbol_code keywords match member names.";
                body.Children.Add(new TextBlock
                {
                    Text = hint,
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                });
            }

            // Sample of unmatched member names so users can see WHY
            // symbols weren't found and update the catalogue.
            if (_res.UnmatchedSamples.Count > 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"Unmatched member samples (first {Math.Min(5, _res.UnmatchedSamples.Count)}):",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(0, 6, 0, 4),
                });
                foreach (var s in _res.UnmatchedSamples.Take(5))
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = "• " + s,
                        Foreground = new SolidColorBrush(FgColor),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1, 0, 1),
                    });
                }
            }
            return Card("ISO 6412 symbols", body);
        }

        // Gap-6: surfaces which spools used a non-profile title-block family.
        private UIElement BuildTitleBlockFallbacksCard()
        {
            var body = new StackPanel();
            body.Children.Add(MetricRow("Total fallbacks", _res.TitleBlockFallbacks.Count.ToString(),
                accent: AmberColor, bold: true));
            body.Children.Add(new TextBlock
            {
                Text = $"Title-block fallbacks (first {Math.Min(10, _res.TitleBlockFallbacks.Count)}):",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 6, 0, 4),
            });
            foreach (var fb in _res.TitleBlockFallbacks.Take(10))
            {
                string sheetLabel = fb.SheetId >= 0 ? $"Sheet {fb.SheetId}" : "Sheet (pending)";
                body.Children.Add(new TextBlock
                {
                    Text = $"• {sheetLabel}: wanted '{fb.ExpectedFamily}', used '{fb.UsedFamily}'",
                    Foreground = new SolidColorBrush(FgColor),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }
            if (_res.TitleBlockFallbacks.Count > 10)
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"(+{_res.TitleBlockFallbacks.Count - 10} more — see StingTools.log for the full list)",
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            body.Children.Add(new TextBlock
            {
                Text = "Load the expected title-block families or update the DrawingType profile to match the loaded family.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
            return Card("Title-block fallbacks (Gap-6)", body);
        }

        private UIElement BuildWarningsCard()
        {
            var body = new StackPanel();
            body.Children.Add(MetricRow("Total warnings", _res.Warnings.Count.ToString(),
                accent: AmberColor, bold: true));
            body.Children.Add(new TextBlock
            {
                Text = "Top warnings (first 8):",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 6, 0, 4),
            });
            foreach (var w in _res.Warnings.Take(8))
            {
                body.Children.Add(new TextBlock
                {
                    Text = "• " + w,
                    Foreground = new SolidColorBrush(FgColor),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }
            if (_res.Warnings.Count > 8)
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"(+{_res.Warnings.Count - 8} more — click View log for the full list)",
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            return Card("Warnings", body);
        }

        // ─── Footer — Open last sheet / View log / Open workspace / Close ─

        private UIElement BuildFooter()
        {
            var dock = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };

            var hint = new TextBlock
            {
                Text = "Every warning is logged to StingTools.log — open it for the full audit trail.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
            };
            DockPanel.SetDock(hint, Dock.Left);
            dock.Children.Add(hint);

            var actions = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(actions, Dock.Right);

            actions.Children.Add(MakeBtn("Open last sheet", NeutralBtn, false, OpenLastSheet,
                enabled: _res.SheetIds.Count > 0));
            actions.Children.Add(MakeBtn("View log",        NeutralBtn, false, OpenStingLog));
            actions.Children.Add(MakeBtn("Open Workspace",  NeutralBtn, false, OpenWorkspace));
            actions.Children.Add(MakeBtn("Close",           AccentColor, true, () => { DialogResult = true; }));
            dock.Children.Add(actions);

            return dock;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Actions
        // ═══════════════════════════════════════════════════════════════

        private void OpenLastSheet()
        {
            try
            {
                if (_res.SheetIds == null || _res.SheetIds.Count == 0) return;
                var app = StingTools.UI.StingCommandHandler.CurrentApp;
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null || _doc == null) return;
                var sheet = _doc.GetElement(_res.SheetIds[0]) as View;
                if (sheet == null) return;
                uidoc.ActiveView = sheet;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationResultDialog.OpenLastSheet", ex);
            }
        }

        private void OpenStingLog()
        {
            try
            {
                string assyDir = StingToolsApp.AssemblyPath;
                if (string.IsNullOrEmpty(assyDir)) return;
                string logPath = Path.Combine(assyDir, "StingTools.log");
                if (!File.Exists(logPath))
                {
                    MessageBox.Show($"Log file not found: {logPath}",
                        Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationResultDialog.OpenStingLog", ex);
            }
        }

        private void OpenWorkspace()
        {
            try
            {
                DialogResult = true;
                var dlg = new FabricationWorkspaceDialog(_doc);
                try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationResultDialog.OpenWorkspace", ex);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  UI primitives
        // ═══════════════════════════════════════════════════════════════

        private UIElement Card(string title, UIElement body)
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 8, 12, 10),
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(AccentColor),
                Margin = new Thickness(0, 0, 0, 6),
            });
            stack.Children.Add(body);
            outer.Child = stack;
            return outer;
        }

        private UIElement MetricRow(string label, string value, Color? accent = null, bool bold = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            var l = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 12,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(l, 0); grid.Children.Add(l);

            var v = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(accent ?? FgColor),
                FontSize = 12,
                FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(v, 1); grid.Children.Add(v);

            return grid;
        }

        private Button MakeBtn(string label, Color bg, bool isDefault, Action onClick, bool enabled = true)
        {
            var fg = bg == NeutralBtn ? FgColor : Color.FromRgb(0xFF, 0xFF, 0xFF);
            var b = new Button
            {
                Content = label,
                MinWidth = 110,
                Height = 30,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(fg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                FontWeight = isDefault ? FontWeights.Bold : FontWeights.Normal,
                IsDefault = isDefault,
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                IsEnabled = enabled,
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }
    }
}
