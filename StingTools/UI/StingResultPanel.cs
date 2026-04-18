using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    // ══════════════════════════════════════════════════════════════════════
    //  STING RESULT PANEL — Rich WPF result display replacing TaskDialog
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reusable rich WPF result panel that replaces plain-text TaskDialog for
    /// audit reports, validation results, and command output. Supports sections
    /// with colored headers, metrics, tables, pass/fail checklists, RAG bars,
    /// and action buttons.
    /// </summary>
    internal static class StingResultPanel
    {
        // ── Theme (M-07 FIX: All brushes frozen for thread safety) ──
        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }
        private static readonly SolidColorBrush BrHeader = FZ(new(Color.FromRgb(0x1A, 0x23, 0x7E)));
        private static readonly SolidColorBrush BrAccent = FZ(new(Color.FromRgb(0xE8, 0x91, 0x2D)));
        private static readonly SolidColorBrush BrBg     = FZ(new(Color.FromRgb(0xF7, 0xF7, 0xF9)));
        private static readonly SolidColorBrush BrWhite  = Brushes.White;
        private static readonly SolidColorBrush BrGreen  = FZ(new(Color.FromRgb(0x2E, 0x7D, 0x32)));
        private static readonly SolidColorBrush BrAmber  = FZ(new(Color.FromRgb(0xF5, 0x7F, 0x17)));
        private static readonly SolidColorBrush BrRed    = FZ(new(Color.FromRgb(0xC6, 0x28, 0x28)));
        private static readonly SolidColorBrush BrBlue   = FZ(new(Color.FromRgb(0x15, 0x65, 0xC0)));
        private static readonly SolidColorBrush BrGrey   = FZ(new(Color.FromRgb(0x75, 0x75, 0x75)));
        private static readonly SolidColorBrush BrPurple = FZ(new(Color.FromRgb(0x6A, 0x1B, 0x9A)));
        private static readonly SolidColorBrush BrTeal   = FZ(new(Color.FromRgb(0x00, 0x69, 0x5C)));
        private static readonly SolidColorBrush BrLightGreen = FZ(new(Color.FromRgb(0xE8, 0xF5, 0xE9)));
        private static readonly SolidColorBrush BrLightRed   = FZ(new(Color.FromRgb(0xFF, 0xEB, 0xEE)));
        private static readonly SolidColorBrush BrLightAmber = FZ(new(Color.FromRgb(0xFF, 0xF8, 0xE1)));
        private static readonly SolidColorBrush BrLightBlue  = FZ(new(Color.FromRgb(0xE3, 0xF2, 0xFD)));
        private static readonly SolidColorBrush BrSectionBg  = FZ(new(Color.FromRgb(0xF0, 0xF0, 0xF5)));

        // ══════════════════════════════════════════════════════════════════
        //  BUILDER API
        // ══════════════════════════════════════════════════════════════════

        public class Builder
        {
            internal string Title = "STING Result";
            internal string Subtitle;
            internal SolidColorBrush SubtitleBrush;
            internal double? OverallPct;
            internal List<ResultSection> Sections = new();
            internal List<ActionDef> Actions = new();
            internal string CsvExportPath;
            internal string RawText; // Fallback plain text for clipboard
            private ResultSection _cur;

            public Builder SetTitle(string t) { Title = t; return this; }
            public Builder SetSubtitle(string s, SolidColorBrush brush = null)
            {
                Subtitle = s; SubtitleBrush = brush ?? BrHeader; return this;
            }
            public Builder SetOverallPct(double pct) { OverallPct = pct; return this; }
            public Builder SetCsvPath(string path) { CsvExportPath = path; return this; }
            public Builder SetRawText(string text) { RawText = text; return this; }

            public Builder AddSection(string title, SolidColorBrush headerBrush = null)
            {
                _cur = new ResultSection { Title = title, HeaderBrush = headerBrush ?? BrHeader };
                Sections.Add(_cur);
                return this;
            }

            public Builder Metric(string label, string value, string note = null, SolidColorBrush valueBrush = null)
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem
                {
                    Type = ItemType.Metric, Label = label, Value = value,
                    Note = note, ValueBrush = valueBrush
                });
                return this;
            }

            public Builder MetricHighlight(string label, string value, string note = null)
                => Metric(label, value, note, BrGreen);

            public Builder MetricWarn(string label, string value, string note = null)
                => Metric(label, value, note, BrAmber);

            public Builder MetricError(string label, string value, string note = null)
                => Metric(label, value, note, BrRed);

            public Builder Text(string text, SolidColorBrush brush = null)
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem { Type = ItemType.Text, Label = text, ValueBrush = brush });
                return this;
            }

            public Builder PassFail(string label, bool passed, string detail = null)
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem
                {
                    Type = ItemType.PassFail, Label = label,
                    Passed = passed, Note = detail
                });
                return this;
            }

            public Builder RAGBar(double pct, string label = null)
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem
                {
                    Type = ItemType.RAGBar, Progress = pct,
                    Label = label ?? $"{pct:F1}%"
                });
                return this;
            }

            public Builder Alert(string message, SolidColorBrush brush = null)
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem
                {
                    Type = ItemType.Alert, Label = message,
                    ValueBrush = brush ?? BrRed
                });
                return this;
            }

            public Builder Info(string message)
                => Alert(message, BrBlue);

            public Builder Table(string[] headers, List<string[]> rows)
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem
                {
                    Type = ItemType.Table, TableHeaders = headers, TableRows = rows
                });
                return this;
            }

            public Builder Separator()
            {
                EnsureSection();
                _cur.Items.Add(new ResultItem { Type = ItemType.Separator });
                return this;
            }

            /// <summary>Add an action button at the bottom of the panel.</summary>
            public Builder Action(string label, string description, Action<Window> click)
            {
                Actions.Add(new ActionDef { Label = label, Description = description, Click = click });
                return this;
            }

            /// <summary>Show the result panel and return the index of the clicked action (-1 if closed).</summary>
            public int Show()
            {
                return StingResultPanel.ShowDialog(this);
            }

            private void EnsureSection()
            {
                if (_cur == null)
                    AddSection("Results");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  DATA TYPES
        // ══════════════════════════════════════════════════════════════════

        internal class ResultSection
        {
            public string Title;
            public SolidColorBrush HeaderBrush;
            public List<ResultItem> Items = new();
        }

        internal class ResultItem
        {
            public ItemType Type;
            public string Label;
            public string Value;
            public string Note;
            public SolidColorBrush ValueBrush;
            public double? Progress;
            public bool? Passed;
            public string[] TableHeaders;
            public List<string[]> TableRows;
        }

        internal enum ItemType
        {
            Metric, Text, RAGBar, PassFail, Table, Alert, Separator
        }

        internal class ActionDef
        {
            public string Label;
            public string Description;
            public Action<Window> Click;
        }

        // ══════════════════════════════════════════════════════════════════
        //  FACTORY
        // ══════════════════════════════════════════════════════════════════

        public static Builder Create(string title) => new Builder { Title = title };

        // ══════════════════════════════════════════════════════════════════
        //  DIALOG BUILDER
        // ══════════════════════════════════════════════════════════════════

        private static int ShowDialog(Builder b)
        {
            int clickedAction = -1;

            var win = new Window
            {
                Title = $"STING Tools - {b.Title}",
                Width = 800, Height = 720,
                MinWidth = 620, MinHeight = 480,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                Background = BrBg
            };
            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"StingResultPanel owner: {ex.Message}"); }

            var root = new DockPanel { LastChildFill = true };

            // ── Header bar ──
            var headerBar = new Border
            {
                Background = BrHeader, Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = b.Title, FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            if (!string.IsNullOrEmpty(b.Subtitle))
            {
                headerStack.Children.Add(new TextBlock
                {
                    Text = b.Subtitle, FontSize = 13,
                    Foreground = b.SubtitleBrush == BrHeader ? BrAccent : b.SubtitleBrush,
                    Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
                });
            }
            // Overall percentage RAG bar in header
            if (b.OverallPct.HasValue)
            {
                var pctBar = BuildRAGBar(b.OverallPct.Value, 200, 14);
                pctBar.Margin = new Thickness(0, 6, 0, 0);
                headerStack.Children.Add(pctBar);
            }
            headerBar.Child = headerStack;
            DockPanel.SetDock(headerBar, Dock.Top);
            root.Children.Add(headerBar);

            // ── Footer with buttons ──
            var footer = new Border
            {
                Background = BrWhite, Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD))),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var footerPanel = new DockPanel();

            // Right-side buttons
            var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (!string.IsNullOrEmpty(b.CsvExportPath))
            {
                var openBtn = MakeFooterBtn("Open Export", BrBlue);
                openBtn.Click += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(b.CsvExportPath) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) { StingLog.Warn($"Open export: {ex.Message}"); }
                };
                rightButtons.Children.Add(openBtn);
            }
            var copyBtn = MakeFooterBtn("Copy Text", BrGrey);
            copyBtn.Click += (s, e) =>
            {
                try
                {
                    string text = b.RawText ?? BuildPlainText(b);
                    Clipboard.SetText(text);
                }
                catch (Exception ex) { StingLog.Warn($"Copy: {ex.Message}"); }
            };
            rightButtons.Children.Add(copyBtn);
            var closeBtn = MakeFooterBtn("Close", BrHeader);
            closeBtn.Click += (s, e) => win.Close();
            closeBtn.IsDefault = true;
            rightButtons.Children.Add(closeBtn);
            DockPanel.SetDock(rightButtons, Dock.Right);
            footerPanel.Children.Add(rightButtons);

            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // ── Action buttons bar (above footer) ──
            if (b.Actions.Count > 0)
            {
                var actionBar = new StackPanel
                {
                    Background = BrWhite, Margin = new Thickness(0)
                };
                actionBar.Children.Add(new Border
                {
                    Height = 1, Background = FZ(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)))
                });
                foreach (var act in b.Actions)
                {
                    int idx = b.Actions.IndexOf(act);
                    var actBtn = new Button
                    {
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(16, 8, 16, 8),
                        Background = BrWhite, BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand
                    };
                    var actContent = new StackPanel();
                    actContent.Children.Add(new TextBlock
                    {
                        Text = $"\u2192  {act.Label}", FontSize = 13,
                        Foreground = BrBlue, FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    });
                    if (!string.IsNullOrEmpty(act.Description))
                    {
                        actContent.Children.Add(new TextBlock
                        {
                            Text = act.Description, FontSize = 10.5,
                            Foreground = BrGrey, Margin = new Thickness(18, 2, 0, 0),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                    actBtn.Content = actContent;
                    int capturedIdx = idx;
                    actBtn.Click += (s, e) =>
                    {
                        clickedAction = capturedIdx;
                        act.Click?.Invoke(win);
                        if (act.Click == null) win.Close();
                    };
                    actionBar.Children.Add(actBtn);
                }
                DockPanel.SetDock(actionBar, Dock.Bottom);
                root.Children.Add(actionBar);
            }

            // ── Scrollable content area ──
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };
            var contentStack = new StackPanel { Margin = new Thickness(0) };

            foreach (var section in b.Sections)
            {
                contentStack.Children.Add(BuildSection(section));
            }

            scroll.Content = contentStack;
            root.Children.Add(scroll);

            win.Content = root;
            win.KeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };
            // Phase 98: owner set so the result panel stacks above BCC instead
            // of getting buried when it's dispatched from a BCC action button.
            StingWindowHelper.ApplyOwner(win);
            win.ShowDialog();
            return clickedAction;
        }

        // ══════════════════════════════════════════════════════════════════
        //  SECTION RENDERER
        // ══════════════════════════════════════════════════════════════════

        private static UIElement BuildSection(ResultSection section)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };

            // Section header
            var header = new Border
            {
                Background = FZ(new SolidColorBrush(section.HeaderBrush is SolidColorBrush hb
                    ? Color.FromArgb(0x18, hb.Color.R, hb.Color.G, hb.Color.B)
                    : Color.FromArgb(0x18, 0x1A, 0x23, 0x7E))),
                Padding = new Thickness(16, 6, 16, 6),
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            header.Child = new TextBlock
            {
                Text = section.Title, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = section.HeaderBrush
            };
            container.Children.Add(header);

            // Section content
            var contentBorder = new Border
            {
                Background = BrWhite, Padding = new Thickness(16, 6, 16, 8)
            };
            var contentPanel = new StackPanel();
            contentBorder.Child = contentPanel;

            foreach (var item in section.Items)
            {
                contentPanel.Children.Add(BuildItem(item));
            }

            container.Children.Add(contentBorder);
            return container;
        }

        private static UIElement BuildItem(ResultItem item)
        {
            switch (item.Type)
            {
                case ItemType.Metric:
                    return BuildMetric(item);
                case ItemType.Text:
                    return BuildText(item);
                case ItemType.RAGBar:
                    return BuildRAGBar(item.Progress ?? 0, 400, 12, item.Label);
                case ItemType.PassFail:
                    return BuildPassFail(item);
                case ItemType.Table:
                    return BuildTable(item);
                case ItemType.Alert:
                    return BuildAlert(item);
                case ItemType.Separator:
                    return new Border
                    {
                        Height = 1, Margin = new Thickness(0, 4, 0, 4),
                        Background = FZ(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)))
                    };
                default:
                    return new TextBlock { Text = item.Label ?? "" };
            }
        }

        private static UIElement BuildMetric(ResultItem item)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = item.Label, FontSize = 11.5, Foreground = BrGrey,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap
            };
            System.Windows.Controls.Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var value = new TextBlock
            {
                Text = item.Value ?? "", FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Foreground = item.ValueBrush ?? Brushes.Black,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap
            };
            System.Windows.Controls.Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            if (!string.IsNullOrEmpty(item.Note))
            {
                var note = new TextBlock
                {
                    Text = $"({item.Note})", FontSize = 10.5, Foreground = BrGrey,
                    FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Top,
                    TextWrapping = TextWrapping.Wrap
                };
                System.Windows.Controls.Grid.SetColumn(note, 2);
                grid.Children.Add(note);
            }

            return grid;
        }

        private static UIElement BuildText(ResultItem item)
        {
            return new TextBlock
            {
                Text = item.Label, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = item.ValueBrush ?? Brushes.Black,
                Margin = new Thickness(0, 2, 0, 2)
            };
        }

        private static UIElement BuildPassFail(ResultItem item)
        {
            bool passed = item.Passed ?? false;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            panel.Children.Add(new TextBlock
            {
                Text = passed ? "\u2714" : "\u2718", FontSize = 12,
                Foreground = passed ? BrGreen : BrRed,
                FontWeight = FontWeights.Bold, Width = 20
            });
            panel.Children.Add(new TextBlock
            {
                Text = item.Label, FontSize = 11.5,
                Foreground = passed ? Brushes.Black : BrRed,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(item.Note))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {item.Note}", FontSize = 10.5,
                    Foreground = BrGrey, FontStyle = FontStyles.Italic,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            return panel;
        }

        private static FrameworkElement BuildRAGBar(double pct, double width, double height, string label = null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            // Background track
            var track = new Border
            {
                Width = width, Height = height,
                Background = FZ(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))),
                CornerRadius = new CornerRadius(height / 2)
            };

            // Fill bar
            SolidColorBrush fillBrush = pct >= 80 ? BrGreen : pct >= 50 ? BrAmber : BrRed;
            double fillWidth = Math.Max(0, Math.Min(width, width * pct / 100.0));
            var fill = new Border
            {
                Width = fillWidth, Height = height,
                Background = fillBrush,
                CornerRadius = new CornerRadius(height / 2),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var grid = new System.Windows.Controls.Grid { Width = width, Height = height };
            grid.Children.Add(track);
            grid.Children.Add(fill);
            panel.Children.Add(grid);

            if (!string.IsNullOrEmpty(label))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {label}", FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = fillBrush, VerticalAlignment = VerticalAlignment.Center
                });
            }

            return panel;
        }

        private static UIElement BuildAlert(ResultItem item)
        {
            SolidColorBrush bg = item.ValueBrush == BrRed ? BrLightRed :
                                  item.ValueBrush == BrAmber ? BrLightAmber :
                                  item.ValueBrush == BrBlue ? BrLightBlue : BrLightGreen;
            return new Border
            {
                Background = bg, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 3, 0, 3),
                Child = new TextBlock
                {
                    Text = item.Label, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                    Foreground = item.ValueBrush ?? BrRed
                }
            };
        }

        private static UIElement BuildTable(ResultItem item)
        {
            if (item.TableHeaders == null || item.TableRows == null) return new TextBlock();

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 4, 0, 4) };
            int cols = item.TableHeaders.Length;
            for (int c = 0; c < cols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = c == 0 ? new GridLength(2, GridUnitType.Star)
                                   : new GridLength(1, GridUnitType.Star)
                });

            int row = 0;
            // Header row
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cols; c++)
            {
                var hdr = new TextBlock
                {
                    Text = item.TableHeaders[c], FontSize = 10.5, FontWeight = FontWeights.Bold,
                    Foreground = BrHeader, Margin = new Thickness(4, 2, 4, 2)
                };
                System.Windows.Controls.Grid.SetRow(hdr, row);
                System.Windows.Controls.Grid.SetColumn(hdr, c);
                grid.Children.Add(hdr);
            }
            row++;

            // Separator
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sep = new Border
            {
                Height = 1, Background = FZ(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)))
            };
            System.Windows.Controls.Grid.SetRow(sep, row);
            System.Windows.Controls.Grid.SetColumnSpan(sep, cols);
            grid.Children.Add(sep);
            row++;

            // Data rows
            foreach (var dataRow in item.TableRows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool isAlt = (row % 2 == 0);
                for (int c = 0; c < Math.Min(cols, dataRow.Length); c++)
                {
                    var cell = new TextBlock
                    {
                        Text = dataRow[c] ?? "", FontSize = 10.5,
                        Margin = new Thickness(4, 1, 4, 1),
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 300
                    };
                    // Highlight non-zero numeric values in first non-label column
                    if (c > 0 && int.TryParse(dataRow[c], out int v) && v > 0)
                        cell.Foreground = BrBlue;
                    System.Windows.Controls.Grid.SetRow(cell, row);
                    System.Windows.Controls.Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
                row++;
            }

            var border = new Border
            {
                BorderBrush = FZ(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4), Child = grid
            };
            return border;
        }

        // ══════════════════════════════════════════════════════════════════
        //  UTILITIES
        // ══════════════════════════════════════════════════════════════════

        private static Button MakeFooterBtn(string label, SolidColorBrush fg)
        {
            return new Button
            {
                Content = label, Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(4, 0, 0, 0), FontSize = 11,
                Background = BrWhite, Foreground = fg,
                BorderBrush = fg, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private static string BuildPlainText(Builder b)
        {
            var sb = new StringBuilder();
            sb.AppendLine(b.Title);
            if (!string.IsNullOrEmpty(b.Subtitle)) sb.AppendLine(b.Subtitle);
            sb.AppendLine(new string('═', 60));
            foreach (var section in b.Sections)
            {
                sb.AppendLine();
                sb.AppendLine($"── {section.Title} ──");
                foreach (var item in section.Items)
                {
                    switch (item.Type)
                    {
                        case ItemType.Metric:
                            sb.AppendLine($"  {item.Label,-30} {item.Value}" +
                                (string.IsNullOrEmpty(item.Note) ? "" : $"  ({item.Note})"));
                            break;
                        case ItemType.Text:
                            sb.AppendLine($"  {item.Label}");
                            break;
                        case ItemType.PassFail:
                            sb.AppendLine($"  [{(item.Passed == true ? "PASS" : "FAIL")}] {item.Label}" +
                                (string.IsNullOrEmpty(item.Note) ? "" : $" — {item.Note}"));
                            break;
                        case ItemType.RAGBar:
                            sb.AppendLine($"  {item.Label}");
                            break;
                        case ItemType.Alert:
                            sb.AppendLine($"  *** {item.Label}");
                            break;
                        case ItemType.Table:
                            if (item.TableHeaders != null)
                                sb.AppendLine($"  {string.Join("  ", item.TableHeaders.Select(h => h.PadRight(8)))}");
                            if (item.TableRows != null)
                                foreach (var r in item.TableRows)
                                    sb.AppendLine($"  {string.Join("  ", r.Select(c => (c ?? "").PadRight(8)))}");
                            break;
                    }
                }
            }
            return sb.ToString();
        }
    }
}
