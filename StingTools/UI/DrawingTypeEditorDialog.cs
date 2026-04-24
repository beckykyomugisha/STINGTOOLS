// StingTools — Drawing Template Manager
//
// DrawingTypeEditorDialog is the Graitec-style corporate editor for
// the registry: left-panel list of all Drawing Types with search,
// right-panel form grouped into collapsible sections (Identity /
// Sheet / Views / Numbering / Crop / Section marker / Slots /
// Annotation / Print). Save, Save-As, Clone, Delete write to
// <project>/_BIM_COORD/drawing_types.json (project override). The
// corporate baseline on disk is never mutated — editing a corporate
// entry flips its origin to "project" automatically.
//
// No live thumbnail preview yet (that needs a Revit export pass on
// the API thread). The validator output strip at the bottom of the
// form catches most pre-generation issues.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;

// Disambiguate WPF vs Revit types
using Color     = System.Windows.Media.Color;
using Colors    = System.Windows.Media.Colors;
using Grid      = System.Windows.Controls.Grid;
using TextBox   = System.Windows.Controls.TextBox;
using ComboBox  = System.Windows.Controls.ComboBox;
using CheckBox  = System.Windows.Controls.CheckBox;

namespace StingTools.UI
{
    public sealed class DrawingTypeEditorDialog : Window
    {
        // ── palette (matches other dark dialogs in the codebase) ──
        private static readonly Color BgColor     = Color.FromRgb(0x2D, 0x2D, 0x30);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CardBg      = Color.FromRgb(0x3E, 0x3E, 0x42);
        private static readonly Color CardBorder  = Color.FromRgb(0x55, 0x55, 0x58);
        private static readonly Color FgColor     = Colors.White;
        private static readonly Color SubtleColor = Color.FromRgb(0xAA, 0xAA, 0xAA);

        // Inputs render black-on-white — readable regardless of whether
        // Revit's WPF host honours our dark Background on input chrome.
        // Labels / cards / checkboxes keep the FgColor (white) palette.
        private static readonly Color InputBg     = Colors.White;
        private static readonly Color InputFg     = Colors.Black;
        private static readonly Color InputBorder = Color.FromRgb(0xBB, 0xBB, 0xBB);

        // ── state ──
        private readonly Document _doc;
        private readonly List<DrawingType> _types;       // working copy
        private DrawingType _current;
        private ListBox _lbTypes;
        private TextBox _tbSearch;
        private StackPanel _formHost;                    // right-hand form container
        private TextBlock _validationStrip;

        // ── pack-tab state (Week 7) ──
        private readonly List<ViewStylePack> _packs;     // working copy
        private ViewStylePack _currentPack;
        private ListBox _lbPacks;
        private TextBox _tbPackSearch;
        private StackPanel _packFormHost;

        // ── document-sourced dropdown cache (one-shot at open) ──
        private readonly DocumentLookups _lookups;

        public DrawingTypeEditorDialog(Document doc)
        {
            _doc = doc;
            _lookups = DocumentLookups.Build(doc);

            // Deep-clone registry entries so Cancel reverts cleanly.
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            _types = (lib?.DrawingTypes ?? new List<DrawingType>())
                .Select(Clone).ToList();

            // Pack tab — load the raw library (pre-extends-merge) so the
            // editor shows the authored hierarchy, not the flattened view.
            var packLib = ViewStylePackRegistry.GetLibrary(doc);
            _packs = (packLib?.Packs ?? new List<ViewStylePack>())
                .Select(ClonePack).ToList();

            Title = "STING — Drawing Type Editor";
            Width = 1080; Height = 720;
            MinWidth = 900; MinHeight = 520;
            Background = new SolidColorBrush(BgColor);
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            DarkDialogTheme.ApplyComboBoxFix(this, CardBg, FgColor, CardBorder);

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { }

            Content = BuildLayout();
            if (_types.Count > 0) SelectType(_types[0]);
            if (_packs.Count > 0) SelectPack(_packs[0]);
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYOUT — TabControl wrapping two side-by-side editors:
        //  tab 1 = Drawing Types (list + form + validation)
        //  tab 2 = View Style Packs (list + form)
        //  shared footer with Save / Close runs against whichever tab
        //  is active; Save routes to drawing_types.json or
        //  view_style_packs.json depending on selected tab.
        // ═══════════════════════════════════════════════════════════════

        private TabControl _tabs;   // so Save() knows which side to persist

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _tabs = new TabControl
            {
                Background = new SolidColorBrush(BgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
            };

            var typesTab = new TabItem
            {
                Header = "Drawing Types",
                Foreground = new SolidColorBrush(FgColor),
                Background = new SolidColorBrush(CardBg),
                Content = BuildTypesTab(),
            };
            var packsTab = new TabItem
            {
                Header = "View Style Packs",
                Foreground = new SolidColorBrush(FgColor),
                Background = new SolidColorBrush(CardBg),
                Content = BuildPacksTab(),
            };
            _tabs.Items.Add(typesTab);
            _tabs.Items.Add(packsTab);
            Grid.SetRow(_tabs, 0);
            root.Children.Add(_tabs);

            var footer = BuildFooter();
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            return root;
        }

        private UIElement BuildTypesTab()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = BuildLeftPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = BuildRightPanel();
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            return grid;
        }

        private UIElement BuildPacksTab()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = BuildPacksLeftPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = BuildPacksRightPanel();
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            return grid;
        }

        private UIElement BuildLeftPanel()
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };

            // Search
            _tbSearch = new TextBox { Height = 26 };
            DarkDialogTheme.StyleInput(_tbSearch, InputBg, InputFg, InputBorder);
            _tbSearch.TextChanged += (s, e) => RefreshList();
            DockPanel.SetDock(_tbSearch, Dock.Top);
            panel.Children.Add(new TextBlock {
                Text = "Search", Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
            DockPanel.SetDock(panel.Children[0], Dock.Top);
            panel.Children.Add(_tbSearch);

            // Actions row
            var actions = new StackPanel {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 8) };
            actions.Children.Add(MakeSmallBtn("＋ New",   () => ActionNew()));
            actions.Children.Add(MakeSmallBtn("Clone",    () => ActionClone()));
            actions.Children.Add(MakeSmallBtn("Delete",   () => ActionDelete()));
            DockPanel.SetDock(actions, Dock.Top);
            panel.Children.Add(actions);

            // List
            _lbTypes = new ListBox {
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
            };
            _lbTypes.SelectionChanged += (s, e) =>
            {
                if (_lbTypes.SelectedItem is DrawingTypeListItem it) SelectType(it.Type);
            };
            panel.Children.Add(_lbTypes);

            RefreshList();
            return panel;
        }

        private void RefreshList()
        {
            var q = (_tbSearch?.Text ?? "").Trim().ToLowerInvariant();
            _lbTypes.Items.Clear();
            foreach (var t in _types.OrderBy(t => t.Discipline).ThenBy(t => t.Purpose).ThenBy(t => t.Id))
            {
                if (!string.IsNullOrEmpty(q))
                {
                    var hay = $"{t.Id} {t.Name} {t.Discipline} {t.Purpose} {t.PaperSize}".ToLowerInvariant();
                    if (!hay.Contains(q)) continue;
                }
                _lbTypes.Items.Add(new DrawingTypeListItem { Type = t });
            }
        }

        private class DrawingTypeListItem
        {
            public DrawingType Type { get; set; }
            public override string ToString()
                => Type == null ? "" : $"[{Type.Discipline,-2}] {Type.Id} · 1:{Type.Scale}";
        }

        // ═══════════════════════════════════════════════════════════════
        //  RIGHT PANEL — collapsible form cards
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildRightPanel()
        {
            var scroll = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8, 0, 0, 0),
            };
            _formHost = new StackPanel();
            scroll.Content = _formHost;
            return scroll;
        }

        private void SelectType(DrawingType t)
        {
            _current = t;
            for (int i = 0; i < _lbTypes.Items.Count; i++)
            {
                if (_lbTypes.Items[i] is DrawingTypeListItem it && it.Type == t)
                { _lbTypes.SelectedIndex = i; break; }
            }
            RenderForm();
        }

        private void RenderForm()
        {
            _formHost.Children.Clear();
            if (_current == null)
            {
                _formHost.Children.Add(new TextBlock {
                    Text = "Select a Drawing Type on the left, or press + New.",
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(8) });
                return;
            }

            _formHost.Children.Add(BuildIdentityCard());
            _formHost.Children.Add(BuildSheetCard());
            _formHost.Children.Add(BuildViewCard());
            _formHost.Children.Add(BuildNumberingCard());
            _formHost.Children.Add(BuildCropCard());
            _formHost.Children.Add(BuildSectionMarkerCard());
            _formHost.Children.Add(BuildAnnotationCard());
            _formHost.Children.Add(BuildSlotsCard());
            _formHost.Children.Add(BuildTitleBlockParamsCard());

            // Validation strip
            var report = DrawingTypeValidator.Validate(_doc, _current);
            _validationStrip = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(4, 10, 4, 4),
                Text = report.HasErrors || report.HasWarnings
                    ? $"{report.Issues.Count(i => i.Severity == ValidationSeverity.Error)} error(s), " +
                      $"{report.Issues.Count(i => i.Severity == ValidationSeverity.Warning)} warning(s): " +
                      string.Join(" · ", report.Issues.Where(i => i.Severity != ValidationSeverity.Info)
                          .Take(3).Select(i => i.Message))
                    : "✓ All referenced assets present."
            };
            _formHost.Children.Add(_validationStrip);
        }

        // ─── Cards ───────────────────────────────────────────────────────

        private UIElement BuildIdentityCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledTextBox("Id",          _current.Id,          v => _current.Id = v));
            body.Children.Add(LabeledTextBox("Name",        _current.Name,        v => _current.Name = v));
            body.Children.Add(LabeledTextBox("Description", _current.Description, v => _current.Description = v));
            body.Children.Add(LabeledCombo("Purpose", new[] {
                DrawingPurpose.Plan, DrawingPurpose.Rcp, DrawingPurpose.Section,
                DrawingPurpose.Elevation, DrawingPurpose.Detail, DrawingPurpose.Schedule,
                DrawingPurpose.Spool, DrawingPurpose.Coordination, DrawingPurpose.Legend,
                DrawingPurpose.ThreeD },
                _current.Purpose, v => _current.Purpose = v));
            body.Children.Add(LabeledCombo("Discipline",
                _lookups.DisciplineCodes, _current.Discipline, v => _current.Discipline = v));
            body.Children.Add(LabeledTextBox("Phase (or *)",
                _current.Phase,      v => _current.Phase = v));
            body.Children.Add(LabeledTextBlock("Origin", _current.Origin ?? "project"));
            return Card("Identity", body);
        }

        private UIElement BuildSheetCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledCombo("Paper size",
                new[] { "A0", "A1", "A2", "A3", "A4" },
                _current.PaperSize, v => _current.PaperSize = v));
            body.Children.Add(LabeledCombo("Title block family",
                _lookups.TitleBlockFamilies, _current.TitleBlockFamily,
                v => _current.TitleBlockFamily = v));
            body.Children.Add(LabeledCombo("Orientation",
                new[] { "Landscape", "Portrait" },
                _current.Orientation, v => _current.Orientation = v));
            return Card("Sheet", body);
        }

        private UIElement BuildViewCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledNumber("Scale (1:N)", _current.Scale, v => _current.Scale = v));
            body.Children.Add(LabeledCombo("Detail level",
                new[] { "Coarse", "Medium", "Fine" },
                _current.DetailLevel, v => _current.DetailLevel = v));
            body.Children.Add(LabeledCombo("View template name",
                _lookups.ViewTemplates, _current.ViewTemplateName,
                v => _current.ViewTemplateName = v));
            body.Children.Add(LabeledCombo("Viewport type name",
                _lookups.ViewportTypes, _current.ViewportTypeName,
                v => _current.ViewportTypeName = v));
            return Card("Views", body);
        }

        private UIElement BuildNumberingCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledTextBox("Sheet number pattern",
                _current.SheetNumberPattern, v => _current.SheetNumberPattern = v,
                tooltip: "Tokens: {spool} {disc} {discipline} {sys} {lvl} {mark} {seq} {seq:D2..4}"));
            body.Children.Add(LabeledTextBox("Sheet name pattern",
                _current.SheetNamePattern, v => _current.SheetNamePattern = v,
                tooltip: "Same token set as sheet number."));
            return Card("Numbering", body);
        }

        private UIElement BuildCropCard()
        {
            var body = new StackPanel();
            _current.Crop = _current.Crop ?? new DrawingCropStrategy();
            body.Children.Add(LabeledCombo("Kind",
                new[] { "ScopeBox", "ScopeBoxOrBbox", "TightBbox", "RoomBoundary", "None" },
                _current.Crop.Kind, v => _current.Crop.Kind = v));
            body.Children.Add(LabeledCombo("Scope box name (when Kind=ScopeBox)",
                _lookups.ScopeBoxes, _current.Crop.ScopeBoxName,
                v => _current.Crop.ScopeBoxName = v));
            body.Children.Add(LabeledDouble("Margin (mm)",
                _current.Crop.MarginMm, v => _current.Crop.MarginMm = v));
            return Card("Crop strategy", body);
        }

        private UIElement BuildSectionMarkerCard()
        {
            var body = new StackPanel();
            _current.SectionMarker = _current.SectionMarker ?? new SectionMarkerSpec();
            body.Children.Add(LabeledCombo("Family",
                _lookups.AnnotationFamilies, _current.SectionMarker.Family,
                v => _current.SectionMarker.Family = v));
            body.Children.Add(LabeledTextBox("Mark prefix",
                _current.SectionMarker.MarkPrefix, v => _current.SectionMarker.MarkPrefix = v));
            body.Children.Add(LabeledCombo("Bubble style",
                new[] { "Filled", "Open", "Dash" },
                _current.SectionMarker.BubbleStyle, v => _current.SectionMarker.BubbleStyle = v));
            body.Children.Add(LabeledDouble("Far clip (mm)",
                _current.SectionMarker.FarClipMm, v => _current.SectionMarker.FarClipMm = v));
            return Card("Section / elevation marker", body);
        }

        private UIElement BuildAnnotationCard()
        {
            var pack = _current.Annotation = _current.Annotation ?? new AnnotationRulePack();
            var body = new StackPanel();

            body.Children.Add(CheckRow("Auto-dim grids",     pack.AutoDimGrids,     v => pack.AutoDimGrids = v));
            body.Children.Add(CheckRow("Auto-dim levels",    pack.AutoDimLevels,    v => pack.AutoDimLevels = v));
            body.Children.Add(CheckRow("Auto-tag rooms",     pack.AutoTagRooms,     v => pack.AutoTagRooms = v));
            body.Children.Add(CheckRow("Auto-tag doors",     pack.AutoTagDoors,     v => pack.AutoTagDoors = v));
            body.Children.Add(CheckRow("Auto-tag windows",   pack.AutoTagWindows,   v => pack.AutoTagWindows = v));
            body.Children.Add(CheckRow("Auto-tag equipment", pack.AutoTagEquipment, v => pack.AutoTagEquipment = v));
            body.Children.Add(CheckRow("Auto-tag welds",     pack.AutoTagWelds,     v => pack.AutoTagWelds = v));
            body.Children.Add(CheckRow("Auto-tag bends",     pack.AutoTagBends,     v => pack.AutoTagBends = v));
            body.Children.Add(CheckRow("Auto-tag supports",  pack.AutoTagSupports,  v => pack.AutoTagSupports = v));

            body.Children.Add(LabeledCombo("Dimension strategy",
                new[] { "Linear", "Ordinate", "Chain" },
                pack.DimensionStrategy, v => pack.DimensionStrategy = v));
            body.Children.Add(LabeledCombo("Dimension style name",
                _lookups.DimensionStyles, pack.DimensionStyle,
                v => pack.DimensionStyle = v));
            body.Children.Add(LabeledNullableNumber("Dense until scale (1:N)",
                pack.DenseUntilScale,  v => pack.DenseUntilScale = v,
                tooltip: "View scale ≤ this value → full annotation. Coarser → grid dims only. Empty = always full."));

            // Tag families mini-editor: "Category → Family" rows
            body.Children.Add(new TextBlock {
                Text = "Tag families (Category → Family name)",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 8, 0, 2) });
            pack.TagFamilies = pack.TagFamilies ?? new Dictionary<string, string>();
            foreach (var kv in pack.TagFamilies.ToList())
            {
                string catKey = kv.Key;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                var k = new TextBox { Text = catKey, Height = 22 };
                DarkDialogTheme.StyleInput(k, InputBg, InputFg, InputBorder);
                k.LostFocus += (s, e) =>
                {
                    pack.TagFamilies.Remove(catKey);
                    pack.TagFamilies[k.Text.Trim()] = pack.TagFamilies.ContainsKey(catKey) ? pack.TagFamilies[catKey] : kv.Value;
                };
                var v = new TextBox { Text = kv.Value, Height = 22, Margin = new Thickness(6, 0, 0, 0) };
                DarkDialogTheme.StyleInput(v, InputBg, InputFg, InputBorder);
                v.LostFocus += (s, e) => pack.TagFamilies[k.Text.Trim()] = v.Text.Trim();
                var rm = MakeSmallBtn("×", () => { pack.TagFamilies.Remove(catKey); RenderForm(); });
                rm.Width = 22;
                Grid.SetColumn(k, 0); Grid.SetColumn(v, 1); Grid.SetColumn(rm, 2);
                row.Children.Add(k); row.Children.Add(v); row.Children.Add(rm);
                body.Children.Add(row);
            }
            body.Children.Add(MakeSmallBtn("＋ Add category mapping", () =>
            {
                pack.TagFamilies["NewCategory"] = "STING_TAG_FAMILY";
                RenderForm();
            }));

            return Card("Annotation rule pack", body);
        }

        private UIElement BuildSlotsCard()
        {
            var body = new StackPanel();
            _current.Slots = _current.Slots ?? new List<DrawingSlot>();
            var hdr = new TextBlock {
                Text = "Label        ViewType       X      Y      W      H     Scale  ",
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10, Margin = new Thickness(0, 0, 0, 2) };
            body.Children.Add(hdr);

            foreach (var slot in _current.Slots.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                for (int c = 0; c < 8; c++)
                    row.ColumnDefinitions.Add(new ColumnDefinition {
                        Width = c == 0 ? new GridLength(100)
                               : c == 1 ? new GridLength(110)
                               : c == 7 ? new GridLength(24)
                               : new GridLength(60) });

                var tbLbl = SmallTb(slot.Label,    v => slot.Label    = v);
                var tbVt  = SmallTb(slot.ViewType, v => slot.ViewType = v);
                var tbX   = SmallTb(slot.NormX.ToString("F2"), v => slot.NormX = Parse(v));
                var tbY   = SmallTb(slot.NormY.ToString("F2"), v => slot.NormY = Parse(v));
                var tbW   = SmallTb(slot.NormW.ToString("F2"), v => slot.NormW = Parse(v));
                var tbH   = SmallTb(slot.NormH.ToString("F2"), v => slot.NormH = Parse(v));
                var tbSc  = SmallTb(slot.Scale?.ToString() ?? "", v =>
                    slot.Scale = int.TryParse(v, out var n) ? (int?)n : null);
                var rm    = MakeSmallBtn("×", () => { _current.Slots.Remove(slot); RenderForm(); });
                rm.Width = 22;

                var ctrls = new UIElement[] { tbLbl, tbVt, tbX, tbY, tbW, tbH, tbSc, rm };
                for (int i = 0; i < ctrls.Length; i++)
                {
                    Grid.SetColumn(ctrls[i], i);
                    if (ctrls[i] is FrameworkElement fe)
                        fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                    row.Children.Add(ctrls[i]);
                }
                body.Children.Add(row);
            }
            body.Children.Add(MakeSmallBtn("＋ Add slot", () =>
            {
                _current.Slots.Add(new DrawingSlot {
                    Label = "NewSlot", ViewType = "Plan",
                    NormX = 0.05, NormY = 0.05, NormW = 0.40, NormH = 0.40 });
                RenderForm();
            }));
            return Card("Slots (0..1 normalised)", body);
        }

        // ─── Title-block params — declarative binding ────────────────
        private UIElement BuildTitleBlockParamsCard()
        {
            _current.TitleBlockParams = _current.TitleBlockParams ?? new Dictionary<string, string>();
            var body = new StackPanel();

            body.Children.Add(new TextBlock
            {
                Text = "Parameter-name → value template. Supports ${PRJ_ORG_xxx} for Project Info, and {disc}/{lvl}/{sys}/{spool}/{mark}/{seq:Dn} tokens. Applied to the title-block FamilyInstance when a sheet is created.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 6),
            });

            foreach (var kv in _current.TitleBlockParams.ToList())
            {
                var paramName = kv.Key;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

                var k = new TextBox { Text = paramName, Height = 22 };
                DarkDialogTheme.StyleInput(k, InputBg, InputFg, InputBorder);
                k.LostFocus += (s, e) =>
                {
                    var newKey = (k.Text ?? "").Trim();
                    if (string.IsNullOrEmpty(newKey) || newKey == paramName) return;
                    _current.TitleBlockParams.Remove(paramName);
                    _current.TitleBlockParams[newKey] = kv.Value ?? "";
                    RenderForm();
                };

                var v = new TextBox { Text = kv.Value ?? "", Height = 22, Margin = new Thickness(6, 0, 0, 0) };
                DarkDialogTheme.StyleInput(v, InputBg, InputFg, InputBorder);
                v.LostFocus += (s, e) => _current.TitleBlockParams[paramName] = v.Text ?? "";

                var rm = MakeSmallBtn("×", () =>
                {
                    _current.TitleBlockParams.Remove(paramName);
                    RenderForm();
                });
                rm.Width = 22;

                Grid.SetColumn(k, 0); Grid.SetColumn(v, 1); Grid.SetColumn(rm, 2);
                row.Children.Add(k); row.Children.Add(v); row.Children.Add(rm);
                body.Children.Add(row);
            }

            body.Children.Add(MakeSmallBtn("＋ Add title-block param", () =>
            {
                var baseKey = "New Parameter"; var key = baseKey; int i = 2;
                while (_current.TitleBlockParams.ContainsKey(key)) key = $"{baseKey} {i++}";
                _current.TitleBlockParams[key] = "";
                RenderForm();
            }));
            return Card("Title-block parameter binding", body);
        }

        // ═══════════════════════════════════════════════════════════════
        //  FOOTER — Save / Save-As / Close
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildFooter()
        {
            var row = new DockPanel {
                Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };

            var hint = new TextBlock {
                Text = "Save writes the active tab to <project>/_BIM_COORD/drawing_types.json " +
                       "or view_style_packs.json — project override only. Corporate baseline " +
                       "on disk is never mutated.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(hint, Dock.Left);
            row.Children.Add(hint);

            var right = new StackPanel {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(right, Dock.Right);
            var btnClose  = MakeBigBtn("Close",          CardBg, false);
            var btnSave   = MakeBigBtn("Save",           AccentColor, true);
            btnClose.Click += (s, e) => { DialogResult = false; };
            btnSave.Click  += (s, e) =>
            {
                // Route Save to whichever tab is active — the dialog
                // edits two independent JSON libraries.
                bool ok;
                if (_tabs?.SelectedIndex == 1) ok = SavePacksToProjectOverride();
                else                           ok = SaveToProjectOverride();
                if (ok) DialogResult = true;
            };
            right.Children.Add(btnClose);
            right.Children.Add(btnSave);
            row.Children.Add(right);
            return row;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ACTIONS — New / Clone / Delete / Save
        // ═══════════════════════════════════════════════════════════════

        private void ActionNew()
        {
            var t = new DrawingType
            {
                Id = "new-drawing-type-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Name = "New Drawing Type",
                Origin = "project",
                Purpose = DrawingPurpose.Plan,
                Discipline = "A", Phase = "*",
                PaperSize = "A1", Scale = 100, DetailLevel = "Medium",
                SheetNumberPattern = "A-{lvl}-{seq:D3}",
                SheetNamePattern = "{discipline} Plan - {lvl}",
                Slots = new List<DrawingSlot>(),
            };
            _types.Add(t);
            RefreshList();
            SelectType(t);
        }

        private void ActionClone()
        {
            if (_current == null) return;
            var copy = Clone(_current);
            copy.Id = _current.Id + "-copy";
            copy.Name = (_current.Name ?? _current.Id) + " (copy)";
            copy.Origin = "project";
            copy.Checksum = null;
            _types.Add(copy);
            RefreshList();
            SelectType(copy);
        }

        private void ActionDelete()
        {
            if (_current == null) return;
            var keep = System.Windows.MessageBox.Show(
                $"Delete '{_current.Id}'?\nCorporate entries only vanish from the project override.",
                "Confirm", MessageBoxButton.YesNo);
            if (keep != MessageBoxResult.Yes) return;
            _types.Remove(_current);
            _current = _types.FirstOrDefault();
            RefreshList();
            if (_current != null) SelectType(_current); else RenderForm();
        }

        private bool SaveToProjectOverride()
        {
            if (_doc == null || string.IsNullOrEmpty(_doc.PathName))
            {
                System.Windows.MessageBox.Show(
                    "Save the Revit project first — project overrides live under the .rvt directory.",
                    "STING — Drawing Types", MessageBoxButton.OK);
                return false;
            }
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(_doc.PathName), "_BIM_COORD");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "drawing_types.json");

                // Write ONLY project-origin types + the routing table so
                // the corporate baseline on disk stays pristine; if the
                // user edited a corporate entry, ComputeChecksums already
                // flipped its origin to "project" during load.
                var lib = new DrawingTypeLibrary
                {
                    Version = 1,
                    DrawingTypes = _types.Where(t => string.Equals(t.Origin, "project", StringComparison.OrdinalIgnoreCase)).ToList(),
                    Routing = new List<DrawingRoutingRule>(DrawingTypeRegistry.ListRouting(_doc)),
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(lib, Formatting.Indented));
                DrawingTypeRegistry.Reload(_doc);
                System.Windows.MessageBox.Show(
                    $"Saved {lib.DrawingTypes.Count} project-scoped type(s) to\n{path}",
                    "STING — Drawing Types", MessageBoxButton.OK);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeEditorDialog.Save", ex);
                System.Windows.MessageBox.Show("Save failed: " + ex.Message,
                    "STING", MessageBoxButton.OK);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  UI PRIMITIVES — Card, labelled inputs, helpers
        // ═══════════════════════════════════════════════════════════════

        private UIElement Card(string title, UIElement body)
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(10, 6, 10, 10),
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock {
                Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12,
                Foreground = new SolidColorBrush(AccentColor),
                Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(body);
            outer.Child = stack;
            return outer;
        }

        private UIElement LabeledTextBox(string label, string value, Action<string> setter,
            string tooltip = null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
            var lbl = new TextBlock {
                Text = label, Width = 200,
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            var tb = new TextBox { Text = value ?? "", Height = 22, ToolTip = tooltip };
            DarkDialogTheme.StyleInput(tb, InputBg, InputFg, InputBorder);
            tb.LostFocus += (s, e) => { setter?.Invoke(tb.Text); };
            row.Children.Add(tb);
            return row;
        }

        private UIElement LabeledTextBlock(string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
            var lbl = new TextBlock {
                Text = label, Width = 200,
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            row.Children.Add(new TextBlock {
                Text = value ?? "", Foreground = new SolidColorBrush(FgColor),
                VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private UIElement LabeledCombo(string label, string[] items, string value, Action<string> setter)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
            var lbl = new TextBlock {
                Text = label, Width = 200,
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            var cb = new ComboBox { Height = 22, IsEditable = true };
            DarkDialogTheme.StyleInput(cb, InputBg, InputFg, InputBorder);
            foreach (var it in items) cb.Items.Add(it);
            cb.Text = value ?? "";
            cb.LostFocus += (s, e) => { setter?.Invoke(cb.Text?.Trim()); };
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is string ss) setter?.Invoke(ss);
            };
            row.Children.Add(cb);
            return row;
        }

        private UIElement LabeledNumber(string label, int value, Action<int> setter)
        {
            return LabeledTextBox(label, value.ToString(),
                v => { if (int.TryParse(v, out var n)) setter?.Invoke(n); });
        }

        private UIElement LabeledNullableNumber(string label, int? value, Action<int?> setter,
            string tooltip = null)
        {
            return LabeledTextBox(label, value?.ToString(), v =>
            {
                if (string.IsNullOrWhiteSpace(v)) setter?.Invoke(null);
                else if (int.TryParse(v, out var n)) setter?.Invoke(n);
            }, tooltip);
        }

        private UIElement LabeledDouble(string label, double value, Action<double> setter)
        {
            return LabeledTextBox(label, value.ToString("0.##"),
                v => { if (double.TryParse(v, out var n)) setter?.Invoke(n); });
        }

        private UIElement CheckRow(string label, bool value, Action<bool> setter)
        {
            var cb = new CheckBox
            {
                Content = label, IsChecked = value,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(0, 2, 0, 2),
            };
            cb.Checked   += (s, e) => setter?.Invoke(true);
            cb.Unchecked += (s, e) => setter?.Invoke(false);
            return cb;
        }

        private ComboBox SmallCombo(string[] items, string value, Action<string> setter)
        {
            var cb = new ComboBox
            {
                Height = 20, FontSize = 10, IsEditable = true,
            };
            DarkDialogTheme.StyleInput(cb, InputBg, InputFg, InputBorder);
            foreach (var it in items ?? new string[] { "" }) cb.Items.Add(it ?? "");
            cb.Text = value ?? "";
            cb.LostFocus += (s, e) => setter?.Invoke(cb.Text?.Trim());
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is string ss) setter?.Invoke(ss);
            };
            return cb;
        }

        private TextBox SmallTb(string text, Action<string> setter)
        {
            var tb = new TextBox { Text = text ?? "", Height = 20, FontSize = 10 };
            DarkDialogTheme.StyleInput(tb, InputBg, InputFg, InputBorder);
            tb.LostFocus += (s, e) => setter?.Invoke(tb.Text);
            return tb;
        }

        private Button MakeSmallBtn(string label, Action onClick)
        {
            var b = new Button
            {
                Content = label, Height = 22, MinWidth = 60,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11,
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        private Button MakeBigBtn(string label, Color bg, bool isDefault)
        {
            return new Button
            {
                Content = label, Width = 110, Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(bg),
                Foreground = new SolidColorBrush(FgColor),
                BorderThickness = new Thickness(0),
                FontWeight = isDefault ? FontWeights.SemiBold : FontWeights.Normal,
                IsDefault = isDefault,
                IsCancel = !isDefault,
            };
        }

        private static double Parse(string s)
            => double.TryParse(s, out var d) ? d : 0.0;

        private static ViewStylePack ClonePack(ViewStylePack src)
        {
            if (src == null) return null;
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<ViewStylePack>(json);
        }

        private static DrawingType Clone(DrawingType src)
        {
            // Deep-clone via JSON so form edits don't mutate the cached
            // registry instance until Save is pressed.
            if (src == null) return null;
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<DrawingType>(json);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PACK-TAB — LEFT PANEL (list + search + New/Clone/Delete)
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildPacksLeftPanel()
        {
            var panel = new DockPanel { Margin = new Thickness(0, 8, 8, 0), LastChildFill = true };

            panel.Children.Add(new TextBlock
            {
                Text = "Search", Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 2),
            });
            DockPanel.SetDock(panel.Children[0], Dock.Top);

            _tbPackSearch = new TextBox { Height = 26 };
            DarkDialogTheme.StyleInput(_tbPackSearch, InputBg, InputFg, InputBorder);
            _tbPackSearch.TextChanged += (s, e) => RefreshPackList();
            DockPanel.SetDock(_tbPackSearch, Dock.Top);
            panel.Children.Add(_tbPackSearch);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 8),
            };
            actions.Children.Add(MakeSmallBtn("＋ New",  () => ActionNewPack()));
            actions.Children.Add(MakeSmallBtn("Clone",   () => ActionClonePack()));
            actions.Children.Add(MakeSmallBtn("Delete",  () => ActionDeletePack()));
            DockPanel.SetDock(actions, Dock.Top);
            panel.Children.Add(actions);

            _lbPacks = new ListBox
            {
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
            };
            _lbPacks.SelectionChanged += (s, e) =>
            {
                if (_lbPacks.SelectedItem is PackListItem it) SelectPack(it.Pack);
            };
            panel.Children.Add(_lbPacks);

            RefreshPackList();
            return panel;
        }

        private void RefreshPackList()
        {
            var q = (_tbPackSearch?.Text ?? "").Trim().ToLowerInvariant();
            _lbPacks.Items.Clear();
            foreach (var p in _packs.OrderBy(p => p.Origin).ThenBy(p => p.Id))
            {
                if (!string.IsNullOrEmpty(q))
                {
                    var hay = $"{p.Id} {p.Name} {p.Extends} {p.Origin}".ToLowerInvariant();
                    if (!hay.Contains(q)) continue;
                }
                _lbPacks.Items.Add(new PackListItem { Pack = p });
            }
        }

        private class PackListItem
        {
            public ViewStylePack Pack { get; set; }
            public override string ToString()
            {
                if (Pack == null) return "";
                var originTag = string.Equals(Pack.Origin, "project", StringComparison.OrdinalIgnoreCase)
                    ? " ·project" : "";
                var extendsTag = string.IsNullOrEmpty(Pack.Extends) ? "" : $"  ⟵ {Pack.Extends}";
                return $"{Pack.Id}{originTag}{extendsTag}";
            }
        }

        private void SelectPack(ViewStylePack p)
        {
            _currentPack = p;
            for (int i = 0; i < _lbPacks.Items.Count; i++)
            {
                if (_lbPacks.Items[i] is PackListItem it && it.Pack == p)
                { _lbPacks.SelectedIndex = i; break; }
            }
            RenderPackForm();
        }

        // ═══════════════════════════════════════════════════════════════
        //  PACK-TAB — RIGHT PANEL (form cards)
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildPacksRightPanel()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(8, 8, 0, 0),
            };
            _packFormHost = new StackPanel();
            scroll.Content = _packFormHost;
            return scroll;
        }

        private void RenderPackForm()
        {
            if (_packFormHost == null) return;
            _packFormHost.Children.Clear();

            if (_currentPack == null)
            {
                _packFormHost.Children.Add(new TextBlock
                {
                    Text = "Select a View Style Pack on the left, or press ＋ New.",
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(8),
                });
                return;
            }

            _packFormHost.Children.Add(BuildPackIdentityCard());
            _packFormHost.Children.Add(BuildPackAppearanceCard());
            _packFormHost.Children.Add(BuildPackFiltersCard());
            _packFormHost.Children.Add(BuildPackVgOverridesCard());
            _packFormHost.Children.Add(BuildPackTagFamiliesCard());
        }

        // ─── Cards ───────────────────────────────────────────────────────

        private UIElement BuildPackIdentityCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledTextBox("Id",          _currentPack.Id,          v => _currentPack.Id = v));
            body.Children.Add(LabeledTextBox("Name",        _currentPack.Name,        v => _currentPack.Name = v));
            body.Children.Add(LabeledTextBox("Description", _currentPack.Description, v => _currentPack.Description = v));
            var parentIds = new List<string> { "" };
            parentIds.AddRange(_packs.Where(p => p != _currentPack).Select(p => p.Id ?? ""));
            body.Children.Add(LabeledCombo("Extends (parent pack id)",
                parentIds.ToArray(),
                _currentPack.Extends,
                v => _currentPack.Extends = string.IsNullOrWhiteSpace(v) ? null : v));
            body.Children.Add(LabeledTextBlock("Origin", _currentPack.Origin ?? "project"));
            return Card("Identity", body);
        }

        private UIElement BuildPackAppearanceCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledDouble("Line-weight scale",
                _currentPack.LineWeightScale,
                v => _currentPack.LineWeightScale = v));
            body.Children.Add(LabeledCombo("Text style name",
                _lookups.TextStyles, _currentPack.TextStyle, v => _currentPack.TextStyle = v));
            body.Children.Add(LabeledCombo("Dimension style name",
                _lookups.DimensionStyles, _currentPack.DimensionStyle,
                v => _currentPack.DimensionStyle = v));
            body.Children.Add(LabeledCombo("Hatch palette (informational)",
                _lookups.HatchPalettes, _currentPack.HatchPalette,
                v => _currentPack.HatchPalette = v));
            return Card("Appearance", body);
        }

        private UIElement BuildPackFiltersCard()
        {
            var body = new StackPanel();
            _currentPack.Filters = _currentPack.Filters ?? new List<StyleFilterRule>();

            body.Children.Add(new TextBlock
            {
                Text = "Per-filter graphic overrides. Filters must already exist in the project (ParameterFilterElement). Colours in #RRGGBB.",
                TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 0, 6),
            });

            var hdr = new TextBlock
            {
                Text = "Filter name          Visible Halftone Proj-Col Proj-Wt Cut-Col Cut-Wt Trans%",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10, Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 0, 2),
            };
            body.Children.Add(hdr);

            foreach (var rule in _currentPack.Filters.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                for (int c = 0; c < 9; c++)
                    row.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = c == 0 ? new GridLength(1, GridUnitType.Star)
                              : c == 8 ? new GridLength(24)
                              : c == 1 || c == 2 ? new GridLength(34)
                              : new GridLength(68)
                    });

                var tbName = SmallCombo(_lookups.FilterNames, rule.FilterName, v => rule.FilterName = v);
                var cbVis  = new CheckBox { IsChecked = rule.Visible,  Foreground = new SolidColorBrush(FgColor) };
                cbVis.Checked   += (s, e) => rule.Visible  = true;
                cbVis.Unchecked += (s, e) => rule.Visible  = false;
                var cbHalf = new CheckBox { IsChecked = rule.Halftone, Foreground = new SolidColorBrush(FgColor) };
                cbHalf.Checked   += (s, e) => rule.Halftone = true;
                cbHalf.Unchecked += (s, e) => rule.Halftone = false;
                var tbProjCol = SmallTb(rule.ProjectionLineColor,  v => rule.ProjectionLineColor = v);
                var tbProjWt  = SmallTb(rule.ProjectionLineWeight?.ToString(),
                                        v => rule.ProjectionLineWeight = int.TryParse(v, out var n) ? (int?)n : null);
                var tbCutCol  = SmallTb(rule.CutLineColor,         v => rule.CutLineColor = v);
                var tbCutWt   = SmallTb(rule.CutLineWeight?.ToString(),
                                        v => rule.CutLineWeight = int.TryParse(v, out var n) ? (int?)n : null);
                var tbTrans   = SmallTb(rule.Transparency?.ToString(),
                                        v => rule.Transparency = int.TryParse(v, out var n) ? (int?)n : null);
                var rm = MakeSmallBtn("×", () => { _currentPack.Filters.Remove(rule); RenderPackForm(); });
                rm.Width = 22;

                var ctrls = new UIElement[] { tbName, cbVis, cbHalf, tbProjCol, tbProjWt, tbCutCol, tbCutWt, tbTrans, rm };
                for (int i = 0; i < ctrls.Length; i++)
                {
                    Grid.SetColumn(ctrls[i], i);
                    if (ctrls[i] is FrameworkElement fe)
                        fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                    row.Children.Add(ctrls[i]);
                }
                body.Children.Add(row);
            }
            body.Children.Add(MakeSmallBtn("＋ Add filter rule", () =>
            {
                _currentPack.Filters.Add(new StyleFilterRule
                {
                    FilterName = "NewFilter", Visible = true, Halftone = false,
                });
                RenderPackForm();
            }));
            return Card("Filter rules", body);
        }

        private UIElement BuildPackVgOverridesCard()
        {
            var body = new StackPanel();
            _currentPack.VgOverrides = _currentPack.VgOverrides ?? new Dictionary<string, StyleVgOverride>();

            body.Children.Add(new TextBlock
            {
                Text = "Per-category graphic overrides. Key = category name (Walls / Grids / Rooms), BuiltInCategory enum, or <Room Separation> for subcategories. Colours in #RRGGBB.",
                TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 0, 6),
            });

            foreach (var kv in _currentPack.VgOverrides.ToList())
            {
                var catKey = kv.Key;
                var vgo = kv.Value ?? new StyleVgOverride();
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                for (int c = 0; c < 8; c++)
                    row.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = c == 0 ? new GridLength(1, GridUnitType.Star)
                              : c == 7 ? new GridLength(24)
                              : c == 1 ? new GridLength(34)
                              : new GridLength(68)
                    });

                var tbKey = SmallCombo(_lookups.CategoryNames, catKey, v =>
                {
                    var newKey = (v ?? "").Trim();
                    if (string.IsNullOrEmpty(newKey) || newKey == catKey) return;
                    _currentPack.VgOverrides.Remove(catKey);
                    _currentPack.VgOverrides[newKey] = vgo;
                    RenderPackForm();
                });
                var cbHalf = new CheckBox
                {
                    IsChecked = vgo.Halftone == true,
                    Foreground = new SolidColorBrush(FgColor),
                };
                cbHalf.Checked   += (s, e) => vgo.Halftone = true;
                cbHalf.Unchecked += (s, e) => vgo.Halftone = null;
                var tbProjCol = SmallTb(vgo.ProjectionLineColor,  v => vgo.ProjectionLineColor = v);
                var tbProjWt  = SmallTb(vgo.ProjectionLineWeight?.ToString(),
                                        v => vgo.ProjectionLineWeight = int.TryParse(v, out var n) ? (int?)n : null);
                var tbCutCol  = SmallTb(vgo.CutLineColor,         v => vgo.CutLineColor = v);
                var tbCutWt   = SmallTb(vgo.CutLineWeight?.ToString(),
                                        v => vgo.CutLineWeight = int.TryParse(v, out var n) ? (int?)n : null);
                var tbTrans   = SmallTb(vgo.Transparency?.ToString(),
                                        v => vgo.Transparency = int.TryParse(v, out var n) ? (int?)n : null);
                var rm = MakeSmallBtn("×", () =>
                {
                    _currentPack.VgOverrides.Remove(catKey);
                    RenderPackForm();
                });
                rm.Width = 22;

                var ctrls = new UIElement[] { tbKey, cbHalf, tbProjCol, tbProjWt, tbCutCol, tbCutWt, tbTrans, rm };
                for (int i = 0; i < ctrls.Length; i++)
                {
                    Grid.SetColumn(ctrls[i], i);
                    if (ctrls[i] is FrameworkElement fe)
                        fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                    row.Children.Add(ctrls[i]);
                }

                // Persist dict entry with current vgo instance (in case
                // it was defaulted above).
                _currentPack.VgOverrides[catKey] = vgo;
                body.Children.Add(row);
            }
            body.Children.Add(MakeSmallBtn("＋ Add VG override", () =>
            {
                var baseKey = "NewCategory"; var key = baseKey; int i = 2;
                while (_currentPack.VgOverrides.ContainsKey(key)) key = $"{baseKey} {i++}";
                _currentPack.VgOverrides[key] = new StyleVgOverride();
                RenderPackForm();
            }));
            return Card("VG overrides (per category)", body);
        }

        private UIElement BuildPackTagFamiliesCard()
        {
            var body = new StackPanel();
            _currentPack.TagFamilies = _currentPack.TagFamilies ?? new Dictionary<string, string>();

            body.Children.Add(new TextBlock
            {
                Text = "Category → tag family. Consumed by AnnotationRunner when a DrawingType references this pack and the rule pack does not override the mapping.",
                TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 0, 6),
            });

            foreach (var kv in _currentPack.TagFamilies.ToList())
            {
                var catKey = kv.Key;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

                var k = SmallTb(catKey, v =>
                {
                    var newKey = (v ?? "").Trim();
                    if (string.IsNullOrEmpty(newKey) || newKey == catKey) return;
                    _currentPack.TagFamilies.Remove(catKey);
                    _currentPack.TagFamilies[newKey] = kv.Value ?? "";
                    RenderPackForm();
                });
                var v = SmallTb(kv.Value, nv => _currentPack.TagFamilies[catKey] = nv ?? "");
                var rm = MakeSmallBtn("×", () =>
                {
                    _currentPack.TagFamilies.Remove(catKey);
                    RenderPackForm();
                });
                rm.Width = 22;

                Grid.SetColumn(k, 0); Grid.SetColumn(v, 1); Grid.SetColumn(rm, 2);
                row.Children.Add(k); row.Children.Add(v); row.Children.Add(rm);
                body.Children.Add(row);
            }
            body.Children.Add(MakeSmallBtn("＋ Add tag family mapping", () =>
            {
                var baseKey = "NewCategory"; var key = baseKey; int i = 2;
                while (_currentPack.TagFamilies.ContainsKey(key)) key = $"{baseKey} {i++}";
                _currentPack.TagFamilies[key] = "STING_TAG_FAMILY";
                RenderPackForm();
            }));
            return Card("Tag families (default map)", body);
        }

        // ─── Pack actions ────────────────────────────────────────────────

        private void ActionNewPack()
        {
            var p = new ViewStylePack
            {
                Id = "new-style-pack-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Name = "New Style Pack",
                Origin = "project",
                LineWeightScale = 1.0,
            };
            _packs.Add(p);
            RefreshPackList();
            SelectPack(p);
        }

        private void ActionClonePack()
        {
            if (_currentPack == null) return;
            var copy = ClonePack(_currentPack);
            copy.Id = _currentPack.Id + "-copy";
            copy.Name = (_currentPack.Name ?? _currentPack.Id) + " (copy)";
            copy.Origin = "project";
            copy.Checksum = null;
            _packs.Add(copy);
            RefreshPackList();
            SelectPack(copy);
        }

        private void ActionDeletePack()
        {
            if (_currentPack == null) return;
            var keep = System.Windows.MessageBox.Show(
                $"Delete '{_currentPack.Id}'?\nCorporate entries only vanish from the project override.",
                "Confirm", MessageBoxButton.YesNo);
            if (keep != MessageBoxResult.Yes) return;
            _packs.Remove(_currentPack);
            _currentPack = _packs.FirstOrDefault();
            RefreshPackList();
            if (_currentPack != null) SelectPack(_currentPack); else RenderPackForm();
        }

        // ─── Pack save (project override only) ───────────────────────────

        private bool SavePacksToProjectOverride()
        {
            if (_doc == null || string.IsNullOrEmpty(_doc.PathName))
            {
                System.Windows.MessageBox.Show(
                    "Save the Revit project first — project overrides live under the .rvt directory.",
                    "STING — View Style Packs", MessageBoxButton.OK);
                return false;
            }
            try
            {
                var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_doc.PathName), "_BIM_COORD");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "view_style_packs.json");

                var lib = new ViewStylePackLibrary
                {
                    Version = 1,
                    Packs = _packs.Where(p =>
                        string.Equals(p.Origin, "project", StringComparison.OrdinalIgnoreCase)).ToList(),
                };
                System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(lib, Formatting.Indented));
                ViewStylePackRegistry.Reload(_doc);
                System.Windows.MessageBox.Show(
                    $"Saved {lib.Packs.Count} project-scoped pack(s) to\n{path}",
                    "STING — View Style Packs", MessageBoxButton.OK);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeEditorDialog.SavePacks", ex);
                System.Windows.MessageBox.Show("Save failed: " + ex.Message,
                    "STING", MessageBoxButton.OK);
                return false;
            }
        }
    }
}
