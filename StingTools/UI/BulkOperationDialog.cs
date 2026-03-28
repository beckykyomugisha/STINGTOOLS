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
    /// Operation type for bulk parameter writes.
    /// </summary>
    public enum BulkOperation
    {
        SetToken,
        AutoPopulate,
        ClearTags,
        Retag
    }

    /// <summary>
    /// Result returned from the BulkOperationDialog.
    /// </summary>
    public class BulkOperationResult
    {
        public BulkOperation Operation { get; set; }
        public string TokenName { get; set; }
        public string TokenValue { get; set; }
        public bool AutoDetectStatus { get; set; }
        public bool Cancelled { get; set; } = true;
    }

    /// <summary>
    /// Unified WPF dialog for bulk parameter operations on selected elements.
    /// Replaces the multi-step TaskDialog chain in BulkParamWriteCommand with a
    /// single-window interface showing operation selector, dynamic options, and preview.
    /// </summary>
    public class BulkOperationDialog : Window
    {
        // ── Theme colours ─────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromRgb(0x2D, 0x2D, 0x30);
        private static readonly Color BgMedium = Color.FromRgb(0x3E, 0x3E, 0x42);
        private static readonly Color BgLight = Color.FromRgb(0x4A, 0x4A, 0x4E);
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgWhite = Color.FromRgb(0xF0, 0xF0, 0xF0);
        private static readonly Color FgDim = Color.FromRgb(0xA0, 0xA0, 0xA0);
        private static readonly Color WarningRed = Color.FromRgb(0xE0, 0x50, 0x50);
        private static readonly Color SuccessGreen = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color BorderDark = Color.FromRgb(0x55, 0x55, 0x58);

        private static SolidColorBrush FZ(SolidColorBrush b) { b.Freeze(); return b; }
        private static SolidColorBrush FZA(byte a, byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromArgb(a, r, g, b)); br.Freeze(); return br; }
        private static readonly SolidColorBrush BrBgDark = FZ(new(BgDark));
        private static readonly SolidColorBrush BrBgMedium = FZ(new(BgMedium));
        private static readonly SolidColorBrush BrBgLight = FZ(new(BgLight));
        private static readonly SolidColorBrush BrAccent = FZ(new(AccentOrange));
        private static readonly SolidColorBrush BrFgWhite = FZ(new(FgWhite));
        private static readonly SolidColorBrush BrFgDim = FZ(new(FgDim));
        private static readonly SolidColorBrush BrWarning = FZ(new(WarningRed));
        private static readonly SolidColorBrush BrSuccess = FZ(new(SuccessGreen));
        private static readonly SolidColorBrush BrBorder = FZ(new(BorderDark));
        private static readonly SolidColorBrush BrDark25 = FZ(new(Color.FromRgb(0x25, 0x25, 0x28)));
        private static readonly SolidColorBrush BrWarnBg = FZA(0x30, 0xE0, 0x50, 0x50);
        private static readonly SolidColorBrush BrRetagBg = FZA(0x30, 0xE8, 0x91, 0x2D);
        private static readonly SolidColorBrush BrWhite = FZ(new SolidColorBrush(Colors.White));

        // ── State ─────────────────────────────────────────────────────
        private readonly int _elementCount;
        private readonly List<PreviewEntry> _previewData;
        private BulkOperationResult _result;

        // ── Operation radios ──────────────────────────────────────────
        private RadioButton _rbSetToken;
        private RadioButton _rbAutoPopulate;
        private RadioButton _rbClearTags;
        private RadioButton _rbRetag;

        // ── Center panel dynamic content ──────────────────────────────
        private readonly StackPanel _centerContent;

        // ── Token sub-options (visible when SetToken selected) ────────
        private RadioButton _rbLoc;
        private RadioButton _rbZone;
        private RadioButton _rbStatus;
        private RadioButton _rbAutoDetect;
        private StackPanel _valuePanel;
        private string _selectedTokenValue;

        // ── Preview panel ─────────────────────────────────────────────
        private TextBlock _previewHeader;
        private StackPanel _previewList;
        private TextBlock _previewEstimate;

        // ── Status bar ────────────────────────────────────────────────
        private TextBlock _statusText;

        /// <summary>
        /// Data for preview entries showing before/after for the first few elements.
        /// </summary>
        public class PreviewEntry
        {
            public string CategoryName { get; set; }
            public string CurrentTag { get; set; }
            public string CurrentLoc { get; set; }
            public string CurrentZone { get; set; }
            public string CurrentStatus { get; set; }
        }

        private BulkOperationDialog(int elementCount, List<PreviewEntry> previewData)
        {
            _elementCount = elementCount;
            _previewData = previewData ?? new List<PreviewEntry>();

            Title = "STING Bulk Operation";
            Width = 750;
            Height = 530;
            MinWidth = 700;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BrBgDark;
            Foreground = BrFgWhite;
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            // Owner = Revit main window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"BulkOperationDialog set owner: {ex.Message}"); }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });              // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });              // Bottom bar

            // ── Header ────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrDark25,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Bulk Parameter Operation",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrAccent
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"{_elementCount} elements selected",
                FontSize = 11,
                Foreground = BrFgDim,
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Body (3-column grid) ──────────────────────────────────
            var body = new Grid { Margin = new Thickness(0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });       // Left: ops
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Center: options
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });       // Right: preview

            // ─── LEFT PANEL: Operation selector ───────────────────────
            var leftPanel = new Border
            {
                Background = BrBgMedium,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(12, 12, 12, 12)
            };
            var leftStack = new StackPanel();
            leftStack.Children.Add(MakeSectionLabel("OPERATION"));

            _rbSetToken = MakeOperationRadio("Set Location / Zone / Status", "Set LOC, ZONE, or STATUS tokens", true);
            _rbAutoPopulate = MakeOperationRadio("Auto-populate all tokens", "Derive DISC, LOC, ZONE, LVL, SYS, FUNC, PROD");
            _rbClearTags = MakeOperationRadio("Clear all tags", "Remove all tag and token values");
            _rbRetag = MakeOperationRadio("Re-tag with overwrite", "Force re-derive and regenerate all tags");

            _rbSetToken.Checked += (s, e) => OnOperationChanged();
            _rbAutoPopulate.Checked += (s, e) => OnOperationChanged();
            _rbClearTags.Checked += (s, e) => OnOperationChanged();
            _rbRetag.Checked += (s, e) => OnOperationChanged();

            leftStack.Children.Add(_rbSetToken);
            leftStack.Children.Add(_rbAutoPopulate);
            leftStack.Children.Add(_rbClearTags);
            leftStack.Children.Add(_rbRetag);

            leftPanel.Child = leftStack;
            Grid.SetColumn(leftPanel, 0);
            body.Children.Add(leftPanel);

            // ─── CENTER PANEL: Dynamic options ────────────────────────
            var centerBorder = new Border
            {
                Padding = new Thickness(16, 12, 16, 12)
            };
            _centerContent = new StackPanel();
            centerBorder.Child = new ScrollViewer
            {
                Content = _centerContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(centerBorder, 1);
            body.Children.Add(centerBorder);

            // ─── RIGHT PANEL: Preview ─────────────────────────────────
            var rightPanel = new Border
            {
                Background = BrBgMedium,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(12, 12, 12, 12)
            };
            var rightStack = new StackPanel();
            rightStack.Children.Add(MakeSectionLabel("PREVIEW"));

            _previewHeader = new TextBlock
            {
                Text = $"Affected: {_elementCount} elements",
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = BrFgWhite,
                Margin = new Thickness(0, 4, 0, 8)
            };
            rightStack.Children.Add(_previewHeader);

            _previewList = new StackPanel();
            rightStack.Children.Add(_previewList);

            _previewEstimate = new TextBlock
            {
                FontSize = 11,
                Foreground = BrFgDim,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            rightStack.Children.Add(_previewEstimate);

            rightPanel.Child = new ScrollViewer
            {
                Content = rightStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(rightPanel, 2);
            body.Children.Add(rightPanel);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            // ── Bottom bar ────────────────────────────────────────────
            var bottomBar = new Border
            {
                Background = BrDark25,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };
            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Select an operation and configure options.",
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            bottomGrid.Children.Add(_statusText);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };
            var cancelBtn = MakeButton("Cancel", false);
            cancelBtn.Click += (s, e) => { _result = new BulkOperationResult { Cancelled = true }; Close(); };
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            btnStack.Children.Add(cancelBtn);

            var okBtn = MakeButton("OK", true);
            okBtn.Click += (s, e) => AcceptAndClose();
            btnStack.Children.Add(okBtn);

            Grid.SetColumn(btnStack, 1);
            bottomGrid.Children.Add(btnStack);

            bottomBar.Child = bottomGrid;
            Grid.SetRow(bottomBar, 2);
            root.Children.Add(bottomBar);

            Content = root;

            // Keyboard shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _result = new BulkOperationResult { Cancelled = true }; Close(); }
                else if (e.Key == Key.Enter) AcceptAndClose();
            };

            // Initialize center panel for default selection
            OnOperationChanged();
        }

        // ── Operation radio factory ───────────────────────────────────
        private RadioButton MakeOperationRadio(string label, string description, bool isChecked = false)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = BrFgWhite
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 10,
                Foreground = BrFgDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

            return new RadioButton
            {
                Content = stack,
                GroupName = "Operation",
                IsChecked = isChecked,
                Foreground = BrFgWhite,
                Margin = new Thickness(0, 6, 0, 6),
                Padding = new Thickness(4, 4, 4, 4)
            };
        }

        // ── Dynamic center panel update ───────────────────────────────
        private void OnOperationChanged()
        {
            _centerContent.Children.Clear();
            _selectedTokenValue = null;

            if (_rbSetToken.IsChecked == true)
                BuildSetTokenPanel();
            else if (_rbAutoPopulate.IsChecked == true)
                BuildAutoPopulatePanel();
            else if (_rbClearTags.IsChecked == true)
                BuildClearTagsPanel();
            else if (_rbRetag.IsChecked == true)
                BuildRetagPanel();

            UpdatePreview();
        }

        // ── SetToken panel ────────────────────────────────────────────
        private void BuildSetTokenPanel()
        {
            _centerContent.Children.Add(MakeSectionLabel("TOKEN TYPE"));

            _rbLoc = new RadioButton
            {
                Content = "LOC (Location)",
                GroupName = "TokenType",
                IsChecked = true,
                Foreground = BrFgWhite,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 2)
            };
            _rbZone = new RadioButton
            {
                Content = "ZONE",
                GroupName = "TokenType",
                Foreground = BrFgWhite,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _rbStatus = new RadioButton
            {
                Content = "STATUS",
                GroupName = "TokenType",
                Foreground = BrFgWhite,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _rbAutoDetect = new RadioButton
            {
                Content = "Auto-detect STATUS from phases",
                GroupName = "TokenType",
                Foreground = BrFgWhite,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 8)
            };

            _rbLoc.Checked += (s, e) => { BuildValuePicker("LOC"); UpdatePreview(); };
            _rbZone.Checked += (s, e) => { BuildValuePicker("ZONE"); UpdatePreview(); };
            _rbStatus.Checked += (s, e) => { BuildValuePicker("STATUS"); UpdatePreview(); };
            _rbAutoDetect.Checked += (s, e) => { BuildValuePicker("AUTODETECT"); UpdatePreview(); };

            _centerContent.Children.Add(_rbLoc);
            _centerContent.Children.Add(_rbZone);
            _centerContent.Children.Add(_rbStatus);
            _centerContent.Children.Add(_rbAutoDetect);

            _centerContent.Children.Add(MakeSectionLabel("VALUE"));

            _valuePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _centerContent.Children.Add(_valuePanel);

            BuildValuePicker("LOC");
        }

        private void BuildValuePicker(string tokenType)
        {
            if (_valuePanel == null) return;
            _valuePanel.Children.Clear();
            _selectedTokenValue = null;

            if (tokenType == "AUTODETECT")
            {
                _valuePanel.Children.Add(new TextBlock
                {
                    Text = "STATUS will be derived from Revit phase data for each element:\n" +
                           "  New Construction \u2192 NEW\n" +
                           "  Existing \u2192 EXISTING\n" +
                           "  Demolished \u2192 DEMOLISHED\n" +
                           "  Temporary \u2192 TEMPORARY",
                    FontSize = 11,
                    Foreground = BrFgDim,
                    TextWrapping = TextWrapping.Wrap
                });
                _statusText.Text = "Auto-detect: STATUS derived from Revit phases.";
                return;
            }

            var values = tokenType switch
            {
                "LOC" => new[]
                {
                    ("BLD1", "Building 1 (primary)"),
                    ("BLD2", "Building 2"),
                    ("BLD3", "Building 3"),
                    ("EXT", "External / Site")
                },
                "ZONE" => new[]
                {
                    ("Z01", "Zone 01"),
                    ("Z02", "Zone 02"),
                    ("Z03", "Zone 03"),
                    ("Z04", "Zone 04")
                },
                "STATUS" => new[]
                {
                    ("NEW", "New construction \u2014 element to be built"),
                    ("EXISTING", "Existing \u2014 element already in place"),
                    ("DEMOLISHED", "Demolished \u2014 element to be removed"),
                    ("TEMPORARY", "Temporary \u2014 hoarding, propping")
                },
                _ => Array.Empty<(string, string)>()
            };

            bool first = true;
            foreach (var (code, desc) in values)
            {
                var tile = MakeValueTile(code, desc, first);
                if (first)
                {
                    _selectedTokenValue = code;
                    first = false;
                }
                _valuePanel.Children.Add(tile);
            }
        }

        private RadioButton MakeValueTile(string code, string description, bool isChecked)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var codeBlock = new TextBlock
            {
                Text = code,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(codeBlock, 0);
            grid.Children.Add(codeBlock);

            var descBlock = new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(descBlock, 1);
            grid.Children.Add(descBlock);

            var rb = new RadioButton
            {
                Content = grid,
                GroupName = "TokenValue",
                IsChecked = isChecked,
                Foreground = BrFgWhite,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(6, 6, 6, 6)
            };

            string capturedCode = code;
            rb.Checked += (s, e) =>
            {
                _selectedTokenValue = capturedCode;
                UpdatePreview();
            };

            return rb;
        }

        // ── AutoPopulate panel ────────────────────────────────────────
        private void BuildAutoPopulatePanel()
        {
            _centerContent.Children.Add(MakeSectionLabel("AUTO-POPULATE"));
            _centerContent.Children.Add(new TextBlock
            {
                Text = "All 9 tokens will be auto-derived from category, spatial, and phase data:",
                FontSize = 12,
                Foreground = BrFgWhite,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8)
            });

            var tokens = new[]
            {
                ("DISC", "Discipline code from category"),
                ("LOC", "Location from room/project data"),
                ("ZONE", "Zone from room department"),
                ("LVL", "Level code from element level"),
                ("SYS", "System type from MEP system/category"),
                ("FUNC", "Function code from system type"),
                ("PROD", "Product code from family name"),
                ("STATUS", "Construction status from Revit phase"),
                ("REV", "Revision from project information")
            };

            foreach (var (token, desc) in tokens)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new TextBlock
                {
                    Text = token,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrAccent,
                    Width = 56
                });
                row.Children.Add(new TextBlock
                {
                    Text = desc,
                    FontSize = 11,
                    Foreground = BrFgDim
                });
                _centerContent.Children.Add(row);
            }

            _centerContent.Children.Add(new TextBlock
            {
                Text = "\nNative Revit parameters (dimensions, MEP data) will also be mapped and formulas evaluated.",
                FontSize = 11,
                Foreground = BrFgDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            _statusText.Text = $"Auto-populate: derive all tokens on {_elementCount} elements.";
        }

        // ── ClearTags panel ───────────────────────────────────────────
        private void BuildClearTagsPanel()
        {
            _centerContent.Children.Add(MakeSectionLabel("CLEAR ALL TAGS"));

            // Warning block
            var warningBorder = new Border
            {
                Background = BrWarnBg,
                BorderBrush = BrWarning,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 12)
            };
            var warningStack = new StackPanel();
            warningStack.Children.Add(new TextBlock
            {
                Text = "\u26A0  WARNING: Destructive Operation",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = BrWarning
            });
            warningStack.Children.Add(new TextBlock
            {
                Text = $"This will clear ALL tag and token values from {_elementCount} elements. This action cannot be undone.",
                FontSize = 11,
                Foreground = BrFgWhite,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            warningBorder.Child = warningStack;
            _centerContent.Children.Add(warningBorder);

            _centerContent.Children.Add(new TextBlock
            {
                Text = "Parameters that will be cleared:",
                FontSize = 12,
                Foreground = BrFgWhite,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var paramNames = new[]
            {
                "ASS_TAG_1 (full tag)", "ASS_TAG_2 through ASS_TAG_6",
                "DISC, LOC, ZONE, LVL", "SYS, FUNC, PROD, SEQ", "STATUS"
            };
            foreach (string p in paramNames)
            {
                _centerContent.Children.Add(new TextBlock
                {
                    Text = "\u2022  " + p,
                    FontSize = 11,
                    Foreground = BrFgDim,
                    Margin = new Thickness(8, 1, 0, 1)
                });
            }

            _statusText.Text = $"Clear: will remove all tags from {_elementCount} elements.";
        }

        // ── Retag panel ───────────────────────────────────────────────
        private void BuildRetagPanel()
        {
            _centerContent.Children.Add(MakeSectionLabel("RE-TAG WITH OVERWRITE"));

            // Warning block
            var warningBorder = new Border
            {
                Background = BrRetagBg,
                BorderBrush = BrAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 12)
            };
            var warningStack = new StackPanel();
            warningStack.Children.Add(new TextBlock
            {
                Text = "\u26A0  Overwrite Mode",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent
            });
            warningStack.Children.Add(new TextBlock
            {
                Text = "All existing token values will be re-derived and overwritten. Existing tags will be replaced with newly generated values.",
                FontSize = 11,
                Foreground = BrFgWhite,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            warningBorder.Child = warningStack;
            _centerContent.Children.Add(warningBorder);

            _centerContent.Children.Add(new TextBlock
            {
                Text = "The full tagging pipeline will run on each element:",
                FontSize = 12,
                Foreground = BrFgWhite,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var steps = new[]
            {
                "1. Type token inheritance",
                "2. Auto-populate all tokens (overwrite)",
                "3. Native parameter mapping",
                "4. Formula evaluation",
                "5. Build ISO 19650 tag with collision detection",
                "6. Write to all discipline containers",
                "7. Build TAG7 rich narrative",
                "8. Grid reference auto-detect"
            };
            foreach (string step in steps)
            {
                _centerContent.Children.Add(new TextBlock
                {
                    Text = step,
                    FontSize = 11,
                    Foreground = BrFgDim,
                    Margin = new Thickness(8, 1, 0, 1)
                });
            }

            _statusText.Text = $"Re-tag: full pipeline on {_elementCount} elements.";
        }

        // ── Preview update ────────────────────────────────────────────
        private void UpdatePreview()
        {
            _previewList.Children.Clear();
            _previewHeader.Text = $"Affected: {_elementCount} elements";

            if (_rbSetToken.IsChecked == true)
            {
                string tokenLabel = "LOC";
                if (_rbZone.IsChecked == true) tokenLabel = "ZONE";
                else if (_rbStatus.IsChecked == true) tokenLabel = "STATUS";
                else if (_rbAutoDetect.IsChecked == true) tokenLabel = "STATUS (auto)";

                int shown = 0;
                foreach (var entry in _previewData)
                {
                    if (shown >= 5) break;
                    string current = tokenLabel switch
                    {
                        "LOC" => entry.CurrentLoc,
                        "ZONE" => entry.CurrentZone,
                        _ => entry.CurrentStatus
                    };
                    string arrow = _rbAutoDetect.IsChecked == true ? "(auto)" : (_selectedTokenValue ?? "?");

                    var row = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                    row.Children.Add(new TextBlock
                    {
                        Text = entry.CategoryName ?? "Element",
                        FontSize = 10,
                        Foreground = BrFgDim
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{tokenLabel}: {(string.IsNullOrEmpty(current) ? "(empty)" : current)} \u2192 {arrow}",
                        FontSize = 11,
                        Foreground = BrFgWhite
                    });
                    _previewList.Children.Add(row);
                    shown++;
                }

                if (_previewData.Count > 5)
                    _previewEstimate.Text = $"... and {_previewData.Count - 5} more";
                else
                    _previewEstimate.Text = "";

                _statusText.Text = _rbAutoDetect.IsChecked == true
                    ? $"Auto-detect STATUS on {_elementCount} elements."
                    : $"Set {tokenLabel} = {_selectedTokenValue ?? "?"} on {_elementCount} elements.";
            }
            else if (_rbAutoPopulate.IsChecked == true)
            {
                _previewEstimate.Text = $"Estimated: up to {_elementCount * 9} token writes\n(9 tokens per element)";
            }
            else if (_rbClearTags.IsChecked == true)
            {
                int taggedCount = _previewData.Count(p => !string.IsNullOrEmpty(p.CurrentTag));
                _previewEstimate.Text = $"Currently tagged: {taggedCount} of {_elementCount}\nAll values will be cleared.";
            }
            else if (_rbRetag.IsChecked == true)
            {
                int taggedCount = _previewData.Count(p => !string.IsNullOrEmpty(p.CurrentTag));
                _previewEstimate.Text = $"Currently tagged: {taggedCount} of {_elementCount}\n" +
                                        $"All {_elementCount} elements will be re-tagged.";
            }
        }

        // ── Accept ────────────────────────────────────────────────────
        private void AcceptAndClose()
        {
            _result = new BulkOperationResult { Cancelled = false };

            if (_rbSetToken.IsChecked == true)
            {
                _result.Operation = BulkOperation.SetToken;
                if (_rbAutoDetect.IsChecked == true)
                {
                    _result.AutoDetectStatus = true;
                    _result.TokenName = ParamRegistry.STATUS;
                }
                else if (_rbLoc.IsChecked == true)
                {
                    _result.TokenName = ParamRegistry.LOC;
                    _result.TokenValue = _selectedTokenValue;
                }
                else if (_rbZone.IsChecked == true)
                {
                    _result.TokenName = ParamRegistry.ZONE;
                    _result.TokenValue = _selectedTokenValue;
                }
                else if (_rbStatus.IsChecked == true)
                {
                    _result.TokenName = ParamRegistry.STATUS;
                    _result.TokenValue = _selectedTokenValue;
                }

                // Validate a value was picked (unless auto-detect)
                if (!_result.AutoDetectStatus && string.IsNullOrEmpty(_result.TokenValue))
                {
                    _statusText.Text = "Please select a value.";
                    _statusText.Foreground = BrWarning;
                    _result = null;
                    return;
                }
            }
            else if (_rbAutoPopulate.IsChecked == true)
            {
                _result.Operation = BulkOperation.AutoPopulate;
            }
            else if (_rbClearTags.IsChecked == true)
            {
                _result.Operation = BulkOperation.ClearTags;
            }
            else if (_rbRetag.IsChecked == true)
            {
                _result.Operation = BulkOperation.Retag;
            }

            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────
        private static TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = BrAccent,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static Button MakeButton(string text, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 80,
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(14, 4, 14, 4),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };

            if (isPrimary)
            {
                btn.Background = BrAccent;
                btn.Foreground = BrWhite;
                btn.BorderBrush = BrAccent;
            }
            else
            {
                btn.Background = BrBgLight;
                btn.Foreground = BrFgWhite;
                btn.BorderBrush = BrBorder;
            }

            return btn;
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Show the bulk operation dialog and return the user's selection.
        /// Returns a result with Cancelled=true if the user closed/cancelled.
        /// </summary>
        /// <param name="elementCount">Number of selected elements.</param>
        /// <param name="previewData">Optional preview data for the first few elements.</param>
        public static BulkOperationResult Show(int elementCount, List<PreviewEntry> previewData = null)
        {
            var dlg = new BulkOperationDialog(elementCount, previewData);
            dlg.ShowDialog();
            return dlg._result ?? new BulkOperationResult { Cancelled = true };
        }
    }
}
