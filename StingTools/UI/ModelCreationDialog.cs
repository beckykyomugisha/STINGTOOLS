using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Result returned by ModelCreationDialog containing the user's element type
    /// selection and dimension/option values for model creation commands.
    /// </summary>
    public class ModelCreationResult
    {
        /// <summary>True if the user clicked Create; false if cancelled.</summary>
        public bool Confirmed { get; set; }
        /// <summary>Selected element type tag (e.g. "Wall", "Floor", "BuildingShell").</summary>
        public string ElementType { get; set; }
        /// <summary>Numeric dimension values keyed by label (e.g. "Width" → 200.0 mm).</summary>
        public Dictionary<string, double> Dimensions { get; set; } = new();
        /// <summary>String option values keyed by label (e.g. "Wall Type" → "Generic - 200mm").</summary>
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>
    /// Unified WPF dialog for model creation. Presents a 2-column layout with
    /// element type selector (left) and dynamic options panel (right). Replaces
    /// per-command TaskDialog prompts with a single branded dialog.
    ///
    /// Usage:
    ///   var result = ModelCreationDialog.Show();
    ///   if (!result.Confirmed) return Result.Cancelled;
    ///   double width = result.Dimensions["Width"];
    /// </summary>
    internal static class ModelCreationDialog
    {
        // ── Element type definitions ────────────────────────────────────
        private class ElementDef
        {
            public string Tag { get; set; }
            public string Label { get; set; }
            public string Category { get; set; }
            public string Icon { get; set; }
            public string Description { get; set; }
            public List<FieldDef> Fields { get; set; } = new();
        }

        private class FieldDef
        {
            public string Label { get; set; }
            public double DefaultValue { get; set; }
            public bool IsDropdown { get; set; }
            public string[] DropdownItems { get; set; }
            public string DefaultChoice { get; set; }
        }

        // ── Theme colours (light theme) ─────────────────────────────────
        private static readonly Color BgColor = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color PanelBg = Colors.White;
        private static readonly Color HeaderBg = Color.FromRgb(0x33, 0x33, 0x38);
        private static readonly Color HeaderFg = Colors.White;
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color TextPrimary = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color TextSecondary = Color.FromRgb(0x66, 0x66, 0x66);
        private static readonly Color TextDim = Color.FromRgb(0x99, 0x99, 0x99);
        private static readonly Color BorderColor = Color.FromRgb(0xDD, 0xDD, 0xDD);
        private static readonly Color CategoryBg = Color.FromRgb(0xEE, 0xEE, 0xEE);
        private static readonly Color SelectedBg = Color.FromRgb(0xFD, 0xF0, 0xE0);
        private static readonly Color SelectedBorder = AccentOrange;
        private static readonly Color HoverBg = Color.FromRgb(0xF0, 0xEC, 0xE8);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static readonly SolidColorBrush BrBg = FZ(BgColor);
        private static readonly SolidColorBrush BrPanel = FZ(PanelBg);
        private static readonly SolidColorBrush BrHeader = FZ(HeaderBg);
        private static readonly SolidColorBrush BrHeaderFg = FZ(HeaderFg);
        private static readonly SolidColorBrush BrAccent = FZ(AccentOrange);
        private static readonly SolidColorBrush BrText = FZ(TextPrimary);
        private static readonly SolidColorBrush BrTextSec = FZ(TextSecondary);
        private static readonly SolidColorBrush BrTextDim = FZ(TextDim);
        private static readonly SolidColorBrush BrBorder = FZ(BorderColor);
        private static readonly SolidColorBrush BrCatBg = FZ(CategoryBg);
        private static readonly SolidColorBrush BrSelected = FZ(SelectedBg);
        private static readonly SolidColorBrush BrSelectedBorder = FZ(SelectedBorder);
        private static readonly SolidColorBrush BrHover = FZ(HoverBg);

        // ── Element type catalog ────────────────────────────────────────
        private static List<ElementDef> BuildCatalog()
        {
            return new List<ElementDef>
            {
                // ── Architectural ──
                new ElementDef
                {
                    Tag = "Wall", Label = "Wall", Category = "Architectural", Icon = "\u2503",
                    Description = "Create a straight wall between two picked points.",
                    Fields = new List<FieldDef>
                    {
                        new() { Label = "Width (mm)", DefaultValue = 200 },
                        new() { Label = "Height (mm)", DefaultValue = 3000 },
                        new() { Label = "Wall Type", IsDropdown = true,
                            DropdownItems = new[] { "Generic - 200mm", "Generic - 300mm", "Cavity Wall - 300mm", "Curtain Wall", "Partition - 100mm" },
                            DefaultChoice = "Generic - 200mm" }
                    }
                },
                new ElementDef
                {
                    Tag = "Floor", Label = "Floor", Category = "Architectural", Icon = "\u2584",
                    Description = "Create a floor slab from size preset or room boundary.",
                    Fields = new List<FieldDef>
                    {
                        new() { Label = "Thickness (mm)", DefaultValue = 150 },
                        new() { Label = "Floor Type", IsDropdown = true,
                            DropdownItems = new[] { "Generic - 150mm", "Generic - 200mm", "Generic - 300mm", "Composite Slab" },
                            DefaultChoice = "Generic - 150mm" }
                    }
                },
                new ElementDef
                {
                    Tag = "Ceiling", Label = "Ceiling", Category = "Architectural", Icon = "\u2580",
                    Description = "Create a rectangular ceiling slab at a specified height.",
                    Fields = new List<FieldDef>
                    {
                        new() { Label = "Height (mm)", DefaultValue = 2700 }
                    }
                },
                new ElementDef
                {
                    Tag = "Roof", Label = "Roof", Category = "Architectural", Icon = "\u25B3",
                    Description = "Create a roof element from a footprint profile.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "Room", Label = "Room", Category = "Architectural", Icon = "\u25A1",
                    Description = "Create a rectangular room enclosure (4 walls + floor + Room element).",
                    Fields = new List<FieldDef>
                    {
                        new() { Label = "Width (mm)", DefaultValue = 4000 },
                        new() { Label = "Depth (mm)", DefaultValue = 5000 }
                    }
                },
                new ElementDef
                {
                    Tag = "Door", Label = "Door", Category = "Architectural", Icon = "\u25AF",
                    Description = "Place door families at picked wall locations.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "Window", Label = "Window", Category = "Architectural", Icon = "\u25A3",
                    Description = "Place window families in walls at picked locations.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "Stairs", Label = "Stairs", Category = "Architectural", Icon = "\u2227",
                    Description = "Create a stair run between levels with configurable riser/tread dimensions.",
                    Fields = new List<FieldDef>()
                },

                // ── Structural ──
                new ElementDef
                {
                    Tag = "Column", Label = "Column", Category = "Structural", Icon = "\u2502",
                    Description = "Place structural columns at picked points.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "ColumnGrid", Label = "Column Grid", Category = "Structural", Icon = "\u2591",
                    Description = "Create an array of columns in a rectangular grid pattern.",
                    Fields = new List<FieldDef>
                    {
                        new() { Label = "Rows", DefaultValue = 3 },
                        new() { Label = "Columns", DefaultValue = 4 },
                        new() { Label = "Spacing X (mm)", DefaultValue = 6000 },
                        new() { Label = "Spacing Y (mm)", DefaultValue = 6000 }
                    }
                },
                new ElementDef
                {
                    Tag = "Beam", Label = "Beam", Category = "Structural", Icon = "\u2500",
                    Description = "Create a structural beam between two picked points.",
                    Fields = new List<FieldDef>()
                },

                // ── MEP ──
                new ElementDef
                {
                    Tag = "Duct", Label = "Duct", Category = "MEP", Icon = "\u25AD",
                    Description = "Create an HVAC duct run along a picked path.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "Pipe", Label = "Pipe", Category = "MEP", Icon = "\u25CB",
                    Description = "Create a plumbing or mechanical pipe run along a picked path.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "Fixture", Label = "Fixture", Category = "MEP", Icon = "\u25C6",
                    Description = "Place MEP fixtures (HVAC units, panels, receptacles) at picked locations.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "CableTray", Label = "Cable Tray", Category = "MEP", Icon = "\u2550",
                    Description = "Create cable tray runs for electrical distribution.",
                    Fields = new List<FieldDef>()
                },
                new ElementDef
                {
                    Tag = "Conduit", Label = "Conduit", Category = "MEP", Icon = "\u2502",
                    Description = "Create conduit runs for cable protection.",
                    Fields = new List<FieldDef>()
                },

                // ── Composite ──
                new ElementDef
                {
                    Tag = "BuildingShell", Label = "Building Shell", Category = "Composite", Icon = "\u2302",
                    Description = "One-click building enclosure: 4 walls + floor + roof at specified dimensions.",
                    Fields = new List<FieldDef>
                    {
                        new() { Label = "Width (mm)", DefaultValue = 12000 },
                        new() { Label = "Depth (mm)", DefaultValue = 8000 },
                        new() { Label = "Height (mm)", DefaultValue = 3600 },
                        new() { Label = "Stories", DefaultValue = 1 }
                    }
                },
                new ElementDef
                {
                    Tag = "DWGToModel", Label = "DWG to Model", Category = "Composite", Icon = "\u21C4",
                    Description = "Auto-convert imported DWG layers to Revit elements using 18-category pattern recognition.",
                    Fields = new List<FieldDef>()
                }
            };
        }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Show the model creation dialog and return the user's selection.
        /// Returns a <see cref="ModelCreationResult"/> with Confirmed=false if cancelled.
        /// </summary>
        public static ModelCreationResult Show()
        {
            var catalog = BuildCatalog();
            var dlg = new DialogWindow(catalog);
            // Phase 98: owner picks BCC over Revit main HWND when BCC is open.
            StingWindowHelper.ApplyOwner(dlg);
            dlg.ShowDialog();
            return dlg.Result;
        }

        // ── Inner Window class ──────────────────────────────────────────

        private class DialogWindow : Window
        {
            public ModelCreationResult Result { get; private set; } = new();

            private readonly List<ElementDef> _catalog;
            private readonly Dictionary<string, Border> _itemCards = new();
            private ElementDef _selectedDef;

            // Right panel controls
            private readonly StackPanel _optionsPanel;
            private readonly TextBlock _descriptionText;
            private readonly TextBlock _statusText;
            private readonly Button _createBtn;
            private readonly Dictionary<string, TextBox> _dimensionInputs = new();
            private readonly Dictionary<string, System.Windows.Controls.ComboBox> _dropdownInputs = new();

            public DialogWindow(List<ElementDef> catalog)
            {
                _catalog = catalog;
                Title = "Create Element";
                Width = 600;
                Height = 500;
                MinWidth = 560;
                MinHeight = 440;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = BrBg;
                FontFamily = new FontFamily("Segoe UI");
                ResizeMode = ResizeMode.NoResize;

                var root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Header
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Footer

                // ── Header ──────────────────────────────────────────────
                var header = new Border
                {
                    Background = BrHeader,
                    Padding = new Thickness(16, 10, 16, 10)
                };
                var headerStack = new StackPanel();
                headerStack.Children.Add(new TextBlock
                {
                    Text = "Create Element",
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrHeaderFg
                });
                headerStack.Children.Add(new TextBlock
                {
                    Text = "Select an element type and configure dimensions",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                header.Child = headerStack;
                Grid.SetRow(header, 0);
                root.Children.Add(header);

                // ── Body: 2-column ──────────────────────────────────────
                var body = new Grid();
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); // Left
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Right
                Grid.SetRow(body, 1);
                root.Children.Add(body);

                // Left panel — element type list
                var leftPanel = new Border
                {
                    Background = BrPanel,
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(0, 0, 1, 0)
                };
                var leftScroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(0)
                };
                var leftStack = new StackPanel();
                BuildElementList(leftStack);
                leftScroll.Content = leftStack;
                leftPanel.Child = leftScroll;
                Grid.SetColumn(leftPanel, 0);
                body.Children.Add(leftPanel);

                // Right panel — dynamic options
                var rightPanel = new Border
                {
                    Background = BrBg,
                    Padding = new Thickness(16, 12, 16, 12)
                };
                var rightScroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                var rightStack = new StackPanel();

                _descriptionText = new TextBlock
                {
                    Text = "Select an element type from the list.",
                    FontSize = 12,
                    Foreground = BrTextSec,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                rightStack.Children.Add(_descriptionText);

                _optionsPanel = new StackPanel();
                rightStack.Children.Add(_optionsPanel);

                rightScroll.Content = rightStack;
                rightPanel.Child = rightScroll;
                Grid.SetColumn(rightPanel, 1);
                body.Children.Add(rightPanel);

                // ── Footer ──────────────────────────────────────────────
                var footer = new Border
                {
                    Background = BrPanel,
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(16, 8, 16, 8)
                };
                var footerGrid = new Grid();
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _statusText = new TextBlock
                {
                    Text = "No element type selected",
                    FontSize = 11,
                    Foreground = BrTextDim,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(_statusText, 0);
                footerGrid.Children.Add(_statusText);

                var btnStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var cancelBtn = new Button
                {
                    Content = "Cancel",
                    MinWidth = 72,
                    Height = 30,
                    FontSize = 12,
                    Padding = new Thickness(14, 4, 14, 4),
                    Background = BrPanel,
                    Foreground = BrText,
                    BorderBrush = BrBorder,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                cancelBtn.Click += (s, e) => Close();
                btnStack.Children.Add(cancelBtn);

                _createBtn = new Button
                {
                    Content = "Create",
                    MinWidth = 80,
                    Height = 30,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(14, 4, 14, 4),
                    Background = BrAccent,
                    Foreground = Brushes.White,
                    BorderBrush = BrAccent,
                    Cursor = Cursors.Hand,
                    IsEnabled = false
                };
                _createBtn.Click += OnCreateClick;
                btnStack.Children.Add(_createBtn);

                Grid.SetColumn(btnStack, 1);
                footerGrid.Children.Add(btnStack);
                footer.Child = footerGrid;
                Grid.SetRow(footer, 2);
                root.Children.Add(footer);

                Content = root;

                // Keyboard shortcuts
                KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                        Close();
                    else if (e.Key == Key.Enter && _createBtn.IsEnabled)
                        OnCreateClick(null, null);
                };
            }

            // ── Build left panel element list with category headers ─────
            private void BuildElementList(StackPanel parent)
            {
                string currentCategory = null;

                foreach (var def in _catalog)
                {
                    if (def.Category != currentCategory)
                    {
                        currentCategory = def.Category;
                        var catHeader = new TextBlock
                        {
                            Text = currentCategory.ToUpperInvariant(),
                            FontSize = 9,
                            FontWeight = FontWeights.Bold,
                            Foreground = BrTextDim,
                            Padding = new Thickness(10, 8, 10, 3),
                            Background = BrCatBg
                        };
                        parent.Children.Add(catHeader);
                    }

                    var card = BuildItemCard(def);
                    _itemCards[def.Tag] = card;
                    parent.Children.Add(card);
                }
            }

            private Border BuildItemCard(ElementDef def)
            {
                var card = new Border
                {
                    Background = BrPanel,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(8, 5, 8, 5),
                    Cursor = Cursors.Hand,
                    Tag = def.Tag
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal };

                var icon = new TextBlock
                {
                    Text = def.Icon,
                    FontSize = 14,
                    Width = 20,
                    Foreground = BrTextSec,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                row.Children.Add(icon);

                var label = new TextBlock
                {
                    Text = def.Label,
                    FontSize = 12,
                    Foreground = BrText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                row.Children.Add(label);

                card.Child = row;

                card.MouseLeftButtonDown += (s, e) => SelectItem(def);
                card.MouseEnter += (s, e) =>
                {
                    if (_selectedDef?.Tag != def.Tag)
                        card.Background = BrHover;
                };
                card.MouseLeave += (s, e) =>
                {
                    if (_selectedDef?.Tag != def.Tag)
                        card.Background = BrPanel;
                };

                return card;
            }

            // ── Selection handling ──────────────────────────────────────
            private void SelectItem(ElementDef def)
            {
                // Clear previous selection
                if (_selectedDef != null && _itemCards.ContainsKey(_selectedDef.Tag))
                {
                    var prev = _itemCards[_selectedDef.Tag];
                    prev.Background = BrPanel;
                    prev.BorderBrush = Brushes.Transparent;
                }

                _selectedDef = def;

                // Highlight current selection
                if (_itemCards.ContainsKey(def.Tag))
                {
                    var card = _itemCards[def.Tag];
                    card.Background = BrSelected;
                    card.BorderBrush = BrSelectedBorder;
                }

                _createBtn.IsEnabled = true;
                _descriptionText.Text = def.Description;
                _statusText.Text = $"{def.Category}  \u2022  {def.Label}";
                _statusText.Foreground = BrText;

                BuildOptionsPanel(def);
            }

            // ── Build right panel options for selected element type ─────
            private void BuildOptionsPanel(ElementDef def)
            {
                _optionsPanel.Children.Clear();
                _dimensionInputs.Clear();
                _dropdownInputs.Clear();

                if (def.Fields.Count == 0)
                {
                    _optionsPanel.Children.Add(new TextBlock
                    {
                        Text = "No additional options required.\nPick points in the Revit view after clicking Create.",
                        FontSize = 11,
                        Foreground = BrTextDim,
                        TextWrapping = TextWrapping.Wrap,
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                    return;
                }

                // Section header
                _optionsPanel.Children.Add(new TextBlock
                {
                    Text = "PARAMETERS",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrTextDim,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                foreach (var field in def.Fields)
                {
                    var fieldPanel = new StackPanel
                    {
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    var fieldLabel = new TextBlock
                    {
                        Text = field.Label,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = BrText,
                        Margin = new Thickness(0, 0, 0, 3)
                    };
                    fieldPanel.Children.Add(fieldLabel);

                    if (field.IsDropdown)
                    {
                        var combo = new System.Windows.Controls.ComboBox
                        {
                            Height = 28,
                            FontSize = 12,
                            Background = BrPanel,
                            Foreground = BrText,
                            BorderBrush = BrBorder
                        };
                        if (field.DropdownItems != null)
                        {
                            foreach (var item in field.DropdownItems)
                                combo.Items.Add(item);
                        }
                        if (!string.IsNullOrEmpty(field.DefaultChoice))
                            combo.SelectedItem = field.DefaultChoice;
                        else if (combo.Items.Count > 0)
                            combo.SelectedIndex = 0;

                        _dropdownInputs[field.Label] = combo;
                        fieldPanel.Children.Add(combo);
                    }
                    else
                    {
                        var input = new System.Windows.Controls.TextBox
                        {
                            Text = field.DefaultValue.ToString("0.##"),
                            Height = 28,
                            FontSize = 12,
                            Padding = new Thickness(6, 4, 6, 4),
                            Background = BrPanel,
                            Foreground = BrText,
                            BorderBrush = BrBorder,
                            VerticalContentAlignment = VerticalAlignment.Center
                        };
                        // Select all text on focus for easy editing
                        input.GotFocus += (s, e) => input.SelectAll();
                        _dimensionInputs[field.Label] = input;
                        fieldPanel.Children.Add(input);
                    }

                    _optionsPanel.Children.Add(fieldPanel);
                }
            }

            // ── Create button handler ───────────────────────────────────
            private void OnCreateClick(object sender, RoutedEventArgs e)
            {
                if (_selectedDef == null) return;

                // Validate numeric inputs
                var dims = new Dictionary<string, double>();
                foreach (var kvp in _dimensionInputs)
                {
                    if (double.TryParse(kvp.Value.Text, out double val) && val > 0)
                    {
                        dims[kvp.Key] = val;
                    }
                    else
                    {
                        _statusText.Text = $"Invalid value for {kvp.Key}. Enter a positive number.";
                        _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0x40, 0x40));
                        kvp.Value.Focus();
                        kvp.Value.SelectAll();
                        return;
                    }
                }

                var opts = new Dictionary<string, string>();
                foreach (var kvp in _dropdownInputs)
                {
                    opts[kvp.Key] = kvp.Value.SelectedItem?.ToString() ?? "";
                }

                Result = new ModelCreationResult
                {
                    Confirmed = true,
                    ElementType = _selectedDef.Tag,
                    Dimensions = dims,
                    Options = opts
                };

                Close();
            }
        }
    }
}
