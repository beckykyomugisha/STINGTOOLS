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
        // ── palette (light, contrast-safe) ──
        // Flipped from the old dark #2D2D30 palette so text is always
        // readable even when a WPF control falls back to default chrome.
        private static readonly Color BgColor     = Color.FromRgb(0xFA, 0xFA, 0xFA);
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color CardBg      = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CardBorder  = Color.FromRgb(0xCF, 0xD8, 0xDC);
        private static readonly Color FgColor     = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color SubtleColor = Color.FromRgb(0x66, 0x66, 0x66);

        // ── state ──
        private readonly Document _doc;
        private readonly List<DrawingType> _types;       // working copy
        private DrawingType _current;
        private ListBox _lbTypes;
        private TextBox _tbSearch;
        private StackPanel _formHost;                    // right-hand form container
        private TextBlock _validationStrip;
        private TabControl _rootTabs;                    // top-level tab host

        public DrawingTypeEditorDialog(Document doc)
        {
            _doc = doc;
            // Deep-clone the registry entries so cancel reverts cleanly.
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            _types = (lib?.DrawingTypes ?? new List<DrawingType>())
                .Select(Clone).ToList();

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
        }

        // ═══════════════════════════════════════════════════════════════
        //  LAYOUT — 2-column: left = list, right = form
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _rootTabs = new TabControl
            {
                Background = new SolidColorBrush(BgColor),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                Padding = new Thickness(6),
            };
            _rootTabs.Items.Add(MakeTab("Drawing Types",   BuildDrawingTypesTab()));
            _rootTabs.Items.Add(MakeTab("View Style Packs", BuildViewStylePacksTab()));
            _rootTabs.Items.Add(MakeTab("Viewport Tools",   BuildViewportToolsTab()));
            _rootTabs.Items.Add(MakeTab("Sheet Tools",      BuildSheetToolsTab()));
            _rootTabs.Items.Add(MakeTab("Title Block",      BuildTitleBlockTab()));
            _rootTabs.Items.Add(MakeTab("Sheet Manager",    BuildSheetManagerTab()));
            Grid.SetRow(_rootTabs, 0);
            root.Children.Add(_rootTabs);

            var footer = BuildFooter();
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);

            return root;
        }

        private TabItem MakeTab(string header, UIElement content)
        {
            return new TabItem
            {
                Header = header,
                Content = content,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
            };
        }

        // The original 2-column Drawing Types layout, now hosted inside the
        // first tab. The toolbar above the search row exposes the docs-panel
        // DRAWING TYPES section (Inspect / Reload JSON / Group Browser /
        // Sync Styles / From Scope Boxes / Edit Types alias).
        private UIElement BuildDrawingTypesTab()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Toolbar — corp ribbon style
            var toolbar = BuildSectionToolbar(new (string label, string tag)[]
            {
                ("Inspect",         "DrawingTypes_Inspect"),
                ("Reload JSON",     "DrawingTypes_Reload"),
                ("Group Browser",   "DrawingTypes_GroupBrowser"),
                ("Sync Styles",     "DrawingTypes_SyncStyles"),
                ("From Scope Boxes","DrawingTypes_FromScopeBoxes"),
            });
            Grid.SetRow(toolbar, 0); Grid.SetColumnSpan(toolbar, 2);
            grid.Children.Add(toolbar);

            var left = BuildLeftPanel();
            Grid.SetRow(left, 1); Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = BuildRightPanel();
            Grid.SetRow(right, 1); Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            return grid;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ACTION TABS — mirror the dock-panel DOCS sections so users can
        //  drive the entire docs surface without leaving this dialog.
        //  Every button dispatches via StingDockPanel.DispatchCommand,
        //  which raises the same IExternalEventHandler the dock panel uses.
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildViewStylePacksTab()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left list — read STING_VIEW_STYLE_PACKS.json from data folder
            var dock = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };
            var hdr = new TextBlock { Text = "Style packs", FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentColor), Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(hdr, Dock.Top); dock.Children.Add(hdr);
            var lb = new ListBox
            {
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
            };
            var detail = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            try
            {
                var path = Path.Combine(StingTools.Core.StingToolsApp.DataPath ?? "", "STING_VIEW_STYLE_PACKS.json");
                if (File.Exists(path))
                {
                    var json = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(path));
                    var packs = json?.stylePacks;
                    if (packs != null)
                        foreach (var p in packs)
                            lb.Items.Add(new ViewStylePackItem { Id = (string)p.id, Name = (string)p.name, Json = p.ToString() });
                }
            }
            catch (Exception ex) { StingLog.Warn("ViewStylePacks load: " + ex.Message); }
            lb.SelectionChanged += (s, e) =>
            {
                detail.Children.Clear();
                if (!(lb.SelectedItem is ViewStylePackItem it)) return;
                detail.Children.Add(new TextBlock { Text = it.Name, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(AccentColor), FontSize = 13, Margin = new Thickness(0,0,0,6) });
                var tb = new TextBox
                {
                    Text = it.Json, IsReadOnly = true, AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    FontFamily = new FontFamily("Consolas"),
                    Height = 540,
                };
                DarkDialogTheme.StyleInput(tb, CardBg, FgColor, CardBorder);
                detail.Children.Add(tb);
            };
            dock.Children.Add(lb);

            Grid.SetColumn(dock, 0); grid.Children.Add(dock);
            var scroll = new ScrollViewer { Content = detail, Margin = new Thickness(8, 0, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
            return grid;
        }

        private class ViewStylePackItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Json { get; set; }
            public override string ToString() => $"{Id}  ·  {Name}";
        }

        private UIElement BuildViewportToolsTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(SectionCard("Alignment", new (string,string)[]
            {
                ("↑ Top",     "VPAlignTop"),    ("↕ MidY",   "VPAlignMidY"),
                ("↓ Bot",     "VPAlignBot"),    ("← Left",   "VPAlignLeft"),
                ("↔ MidX",    "VPAlignMidX"),   ("→ Right",  "VPAlignRight"),
            }));
            stack.Children.Add(SectionCard("Numbering", new (string,string)[]
            {
                ("L→R",     "VPNumLR"),     ("T→B",      "VPNumTB"),
                ("+1",      "VPNumPlus"),   ("-1",       "VPNumMinus"),
                ("Prefix",  "VPPrefix"),    ("Suffix",   "VPSuffix"),
                ("Renum VP","RenumberViewports"),
            }));
            stack.Children.Add(SectionCard("Spacing", new (string,string)[]
            {
                ("Auto-Place",    "AutoPlaceViewports"),
                ("Batch Align",   "BatchAlignViewports"),
                ("Align VP",      "AlignViewports"),
                ("Crop to Content","CropToContent"),
                ("Move VP",       "MoveViewport"),
            }));
            host.Content = stack;
            return host;
        }

        private UIElement BuildSheetToolsTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(SectionCard("Sheet Tools", new (string,string)[]
            {
                ("Organizer",   "SheetOrganizer"),
                ("Index",       "SheetIndex"),
                ("Transmittal", "Transmittal"),
                ("Journal",     "JournalParser"),
                ("Naming",      "SheetNamingCheck"),
                ("Auto-Num",    "AutoNumberSheets"),
                ("Map Sheets",  "MapSheets"),
                ("Tag Sheets",  "TagSheets"),
                ("View Organizer","ViewOrganizer"),
                ("Delete Unused","DeleteUnusedViews"),
            }));
            stack.Children.Add(SectionCard("Sheet Number", new (string,string)[]
            {
                ("Reset Title",    "SheetResetTitle"),
                ("+1",             "SheetNumPlus"),
                ("-1",             "SheetNumMinus"),
                ("Prefix",         "SheetPrefix"),
                ("Suffix",         "SheetSuffix"),
                ("Rem Pre",        "SheetRemovePrefix"),
                ("Rem Suf",        "SheetRemoveSuffix"),
                ("Find&Replace",   "SheetFindReplace"),
            }));
            stack.Children.Add(SectionCard("View Automation", new (string,string)[]
            {
                ("Duplicate View",   "DuplicateView"),
                ("Batch Rename",     "BatchRenameViews"),
                ("Copy View Settings","CopyViewSettings"),
                ("Magic Rename",     "MagicRename"),
                ("View Tab Colour",  "ViewTabColour"),
                ("Text Case",        "TextCase"),
                ("Sum Areas",        "SumAreas"),
            }));
            host.Content = stack;
            return host;
        }

        private UIElement BuildTitleBlockTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(SectionCard("Title Block (Phase 97)", new (string,string)[]
            {
                ("Edit CSV…",      "TitleBlockEditCsv"),
                ("Populate",       "TitleBlockPopulate"),
                ("Validate",       "TitleBlockValidate"),
                ("Set Variant",    "TitleBlockSetVariant"),
                ("Legend Bind",    "DisciplineLegendBind"),
                ("Count Sheets",   "SheetCountAutoUpdate"),
                ("Revision Sync",  "RevisionSync"),
                ("Stamp TX",       "TransmittalAutoIssue"),
                ("Pre-Export",     "PreExportValidate"),
            }));
            stack.Children.Add(SectionCard("Repair", new (string,string)[]
            {
                ("Reset Position", "TitleBlockReset"),
                ("Rescue",         "TitleBlockRescue"),
            }));
            stack.Children.Add(InfoCard(
                "Tip — for the layered author guide (cover page, start-up page, fabrication, " +
                "technical / client / IFC / IFT / as-built / authority / marketing variants, plus " +
                "every TB_/PRJ_TB_ shared parameter explained) see " +
                "docs/guides/TITLE_BLOCK_CREATION_GUIDE.md."));
            host.Content = stack;
            return host;
        }

        private UIElement BuildSheetManagerTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(SectionCard("Sheet Manager", new (string,string)[]
            {
                ("Sheet Manager",  "SheetManager"),
                ("Auto-Layout",    "AutoLayout"),
                ("Clone Sheet",    "CloneSheet"),
                ("Place Unplaced", "PlaceUnplacedViews"),
                ("Optimal Scale",  "OptimalScale"),
                ("Sheet Audit",    "SheetAudit"),
                ("Batch Arrange",  "BatchArrange"),
                ("Move VP",        "MoveViewport"),
            }));
            stack.Children.Add(SectionCard("Advanced", new (string,string)[]
            {
                ("MaxRects",       "MaxRectsLayout"),
                ("Save Layout",    "SaveLayoutPreset"),
                ("Apply Layout",   "ApplyLayoutPreset"),
                ("Overflow",       "PlaceWithOverflow"),
                ("Batch Clone",    "BatchCloneSheets"),
                ("Renumber",       "BatchRenumberSheets"),
                ("VP Types",       "AutoAssignVPTypes"),
                ("Export CSV",     "ExportSheetSet"),
            }));
            stack.Children.Add(SectionCard("Templates & Compliance", new (string,string)[]
            {
                ("From Template",  "CreateFromTemplate"),
                ("Save Template",  "SaveSheetTemplate"),
                ("ISO Check",      "SheetComplianceCheck"),
                ("Tag Sheets",     "TagSheets"),
                ("Sheet Register", "ExportSheetRegister"),
                ("Grid Align",     "GridAlignViewports"),
                ("Align Edges",    "AlignViewportEdges"),
                ("Distribute",     "DistributeViewports"),
                ("Batch Print",    "BatchPrintSheets"),
            }));
            host.Content = stack;
            return host;
        }

        // ── Section card helpers shared by every action tab ──

        private UIElement SectionCard(string title, (string label, string tag)[] buttons)
        {
            var body = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (label, tag) in buttons)
                body.Children.Add(MakeActionBtn(label, tag));
            return Card(title, body);
        }

        private UIElement InfoCard(string text)
        {
            var body = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
            };
            return Card("Reference", body);
        }

        // Toolbar row for the Drawing Types tab — same dispatcher.
        private UIElement BuildSectionToolbar((string label, string tag)[] buttons)
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (label, tag) in buttons)
                bar.Children.Add(MakeActionBtn(label, tag));
            return bar;
        }

        // Action button — dispatches via the dock panel's external-event
        // handler so the same Revit API thread runs the work, exactly as
        // the dock panel does. Works while this dialog is modal because
        // WPF runs its own message pump.
        private Button MakeActionBtn(string label, string tag)
        {
            var b = new Button
            {
                Content = label, MinWidth = 92, Height = 26,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                ToolTip = $"Dispatch tag: {tag}",
            };
            b.Click += (s, e) =>
            {
                try { StingDockPanel.DispatchCommand(tag); }
                catch (Exception ex) { StingLog.Error("Dispatch:" + tag, ex); }
            };
            return b;
        }

        private UIElement BuildLeftPanel()
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };

            // Search
            _tbSearch = new TextBox { Height = 26 };
            DarkDialogTheme.StyleInput(_tbSearch, CardBg, FgColor, CardBorder);
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
            body.Children.Add(LabeledTextBox("Discipline (A/S/M/E/P or *)",
                _current.Discipline, v => _current.Discipline = v));
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
            body.Children.Add(LabeledTextBox("Title block family",
                _current.TitleBlockFamily, v => _current.TitleBlockFamily = v));
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
            body.Children.Add(LabeledTextBox("View template name",
                _current.ViewTemplateName, v => _current.ViewTemplateName = v));
            body.Children.Add(LabeledTextBox("Viewport type name",
                _current.ViewportTypeName, v => _current.ViewportTypeName = v));
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
            body.Children.Add(LabeledTextBox("Scope box name (when Kind=ScopeBox)",
                _current.Crop.ScopeBoxName, v => _current.Crop.ScopeBoxName = v));
            body.Children.Add(LabeledDouble("Margin (mm)",
                _current.Crop.MarginMm, v => _current.Crop.MarginMm = v));
            return Card("Crop strategy", body);
        }

        private UIElement BuildSectionMarkerCard()
        {
            var body = new StackPanel();
            _current.SectionMarker = _current.SectionMarker ?? new SectionMarkerSpec();
            body.Children.Add(LabeledTextBox("Family",
                _current.SectionMarker.Family, v => _current.SectionMarker.Family = v));
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
            body.Children.Add(LabeledTextBox("Dimension style name",
                pack.DimensionStyle, v => pack.DimensionStyle = v));
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
                DarkDialogTheme.StyleInput(k, CardBg, FgColor, CardBorder);
                k.LostFocus += (s, e) =>
                {
                    pack.TagFamilies.Remove(catKey);
                    pack.TagFamilies[k.Text.Trim()] = pack.TagFamilies.ContainsKey(catKey) ? pack.TagFamilies[catKey] : kv.Value;
                };
                var v = new TextBox { Text = kv.Value, Height = 22, Margin = new Thickness(6, 0, 0, 0) };
                DarkDialogTheme.StyleInput(v, CardBg, FgColor, CardBorder);
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

        // ═══════════════════════════════════════════════════════════════
        //  FOOTER — Save / Save-As / Close
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildFooter()
        {
            var row = new DockPanel {
                Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };

            var hint = new TextBlock {
                Text = "Save writes the active tab to <project>/_BIM_COORD/drawing_types.json or view_style_packs.json — " +
                       "project override only. Corporate baseline on disk is never mutated. " +
                       "Action tabs dispatch directly via the dock-panel external-event queue.",
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
            btnSave.Click  += (s, e) => { if (SaveToProjectOverride()) { DialogResult = true; } };
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
            DarkDialogTheme.StyleInput(tb, CardBg, FgColor, CardBorder);
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
            DarkDialogTheme.StyleInput(cb, CardBg, FgColor, CardBorder);
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

        private TextBox SmallTb(string text, Action<string> setter)
        {
            var tb = new TextBox { Text = text ?? "", Height = 20, FontSize = 10 };
            DarkDialogTheme.StyleInput(tb, CardBg, FgColor, CardBorder);
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

        private static DrawingType Clone(DrawingType src)
        {
            // Deep-clone via JSON so form edits don't mutate the cached
            // registry instance until Save is pressed.
            if (src == null) return null;
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<DrawingType>(json);
        }
    }
}
