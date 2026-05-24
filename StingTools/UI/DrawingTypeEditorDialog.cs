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
using ValidationSeverity = StingTools.Core.Drawing.ValidationSeverity;

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

        // Slot grid column widths — header + every data row share these
        // so the column edges line up pixel-for-pixel regardless of the
        // dialog width.
        // [Label, ViewType, NormX, NormY, NormW, NormH, Scale, ×]
        private static readonly int[] _slotColWidths = { 100, 110, 60, 60, 60, 60, 60, 24 };

        // ── Static vocabularies (Change 3 / 4 / 6) ────────────────────
        // Union with ProjectAssetPicker.* live readers at runtime so the
        // UI offers a sensible default set even on an empty model, and
        // pulls in project-specific names where the project has them.

        private static readonly string[] KnownRevitCategories = new[]
        {
            "Walls", "Curtain Panels", "Curtain Wall Mullions", "Doors", "Windows",
            "Floors", "Ceilings", "Roofs", "Stairs", "Railings", "Ramps",
            "Structural Columns", "Structural Framing", "Structural Foundations",
            "Structural Rebar", "Structural Area Reinforcement", "Structural Fabric Areas",
            "Mechanical Equipment", "Duct System", "Ducts", "Duct Fittings",
            "Duct Accessories", "Duct Insulations", "Air Terminals",
            "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulations",
            "Plumbing Fixtures",
            "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures",
            "Cable Trays", "Conduits",
            "Fire Alarm Devices", "Sprinklers",
            "Communication Devices", "Security Devices", "Nurse Call Devices",
            "Data Devices", "Fire Protection",
            "Rooms", "Areas", "Spaces", "Zones",
            "Furniture", "Furniture Systems", "Casework", "Specialty Equipment",
            "Parking", "Topography", "Site",
            "Grids", "Levels", "Scope Boxes", "Reference Planes", "Reference Lines",
            "Section Boxes", "Matchline",
            "Dimensions", "Text Notes", "Generic Annotations", "Keynote Tags",
            "Room Tags", "Area Tags", "Space Tags", "Door Tags", "Window Tags",
            "Model Groups", "Assembly Instances",
        };

        private static readonly string[] KnownTaggableCategories = new[]
        {
            "Air Terminals", "Cable Trays", "Casework", "Ceilings", "Communication Devices",
            "Conduits", "Curtain Panels", "Data Devices", "Doors", "Duct Accessories",
            "Duct Fittings", "Duct Insulations", "Ducts", "Electrical Equipment",
            "Electrical Fixtures", "Fire Alarm Devices", "Fire Protection",
            "Floors", "Furniture", "Furniture Systems", "Generic Models",
            "Lighting Devices", "Lighting Fixtures", "Mechanical Equipment",
            "Model Groups", "Nurse Call Devices", "Parking", "Pipe Accessories",
            "Pipe Fittings", "Pipe Insulations", "Pipes", "Plumbing Fixtures",
            "Railings", "Ramps", "Rooms", "Security Devices", "Site",
            "Spaces", "Specialty Equipment", "Sprinklers", "Stairs",
            "Structural Area Reinforcement", "Structural Columns",
            "Structural Fabric Areas", "Structural Foundations", "Structural Framing",
            "Structural Rebar", "Walls", "Windows", "Zones",
        };

        // Phase 136 — Native Revit categories grouped by discipline. Used by
        // the VG editor's discipline-filter combo, and as a fallback when
        // doc.Settings.Categories returns an unusually thin list (empty
        // template, family editor, no MEP loaded, etc.). Keys mirror the
        // discipline filter values; the special "All" key is reserved at
        // runtime for "no filter".
        private static readonly Dictionary<string, string[]> KnownModelCategoriesByDiscipline =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Architectural"] = new[]
            {
                "Walls", "Curtain Panels", "Curtain Wall Mullions", "Curtain Systems",
                "Doors", "Windows", "Floors", "Ceilings", "Roofs",
                "Stairs", "Railings", "Ramps", "Casework", "Furniture", "Furniture Systems",
                "Specialty Equipment", "Generic Models", "Mass",
                "Site", "Topography", "Parking", "Planting", "Entourage",
                "Roads", "Pads",
            },
            ["Structural"] = new[]
            {
                "Structural Columns", "Structural Framing", "Structural Foundations",
                "Structural Connections", "Structural Stiffeners", "Structural Trusses",
                "Structural Rebar", "Structural Area Reinforcement",
                "Structural Path Reinforcement", "Structural Fabric Areas",
                "Structural Fabric Reinforcement", "Rebar Shape", "Structural Beam Systems",
                "Structural Loads", "Analytical Spaces", "Analytical Surfaces",
            },
            ["Mechanical"] = new[]
            {
                "Mechanical Equipment", "Air Terminals", "Ducts", "Duct Fittings",
                "Duct Accessories", "Duct Insulations", "Duct Linings", "Duct Placeholders",
                "Duct Systems", "Flex Ducts", "HVAC Zones", "MEP Fabrication Ductwork",
                "MEP Fabrication Hangers",
            },
            ["Electrical"] = new[]
            {
                "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures",
                "Lighting Devices", "Cable Trays", "Cable Tray Fittings",
                "Conduits", "Conduit Fittings", "Wires", "Electrical Circuits",
                "Telephone Devices", "Communication Devices", "Data Devices",
                "Security Devices", "Nurse Call Devices", "Fire Alarm Devices",
            },
            ["Plumbing"] = new[]
            {
                "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulations",
                "Pipe Placeholders", "Flex Pipes", "Plumbing Fixtures",
                "Sprinklers", "Piping Systems", "MEP Fabrication Pipework",
            },
            ["Fire Protection"] = new[]
            {
                "Fire Alarm Devices", "Fire Protection", "Sprinklers",
            },
            ["Coordination"] = new[]
            {
                "Rooms", "Spaces", "Areas", "Zones",
                "Levels", "Grids", "Scope Boxes", "Reference Planes", "Reference Lines",
                "Section Boxes", "Matchline", "Detail Items", "Model Groups",
                "Assembly Instances", "Assemblies", "Parts", "Project Information",
            },
        };

        private static readonly Dictionary<string, string[]> KnownAnnotationCategoriesByDiscipline =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Annotation"] = new[]
            {
                "Dimensions", "Spot Coordinates", "Spot Elevations", "Spot Slopes",
                "Text Notes", "Generic Annotations", "Keynote Tags", "Multi-Category Tags",
                "Detail Items", "Filled Region", "Masking Region",
                "Revision Clouds", "Revision Cloud Tags",
                "Section Marks", "Elevation Marks", "Callout Boundary", "Callout Heads",
                "Reference View", "View Title", "Sheet Number", "Sheet Name",
                "Title Marks", "Levels (Annotation)", "Grids (Annotation)", "Matchline",
            },
            ["Tags — Architectural"] = new[]
            {
                "Door Tags", "Window Tags", "Wall Tags", "Floor Tags", "Ceiling Tags",
                "Roof Tags", "Stair Tags", "Railing Tags", "Ramp Tags", "Casework Tags",
                "Furniture Tags", "Furniture System Tags", "Curtain Panel Tags",
                "Generic Model Tags", "Specialty Equipment Tags",
                "Room Tags", "Space Tags", "Area Tags", "Zone Tags",
                "Site Tags", "Parking Tags", "Planting Tags",
            },
            ["Tags — Structural"] = new[]
            {
                "Structural Column Tags", "Structural Framing Tags",
                "Structural Foundation Tags", "Structural Connection Tags",
                "Structural Rebar Tags", "Structural Truss Tags",
                "Structural Beam System Tags", "Span Direction Symbol",
            },
            ["Tags — Mechanical"] = new[]
            {
                "Mechanical Equipment Tags", "Air Terminal Tags",
                "Duct Tags", "Duct Fitting Tags", "Duct Accessory Tags",
                "Duct Insulation Tags", "Flex Duct Tags",
                "HVAC Zone Tags",
            },
            ["Tags — Electrical"] = new[]
            {
                "Electrical Equipment Tags", "Electrical Fixture Tags",
                "Lighting Fixture Tags", "Lighting Device Tags",
                "Cable Tray Tags", "Cable Tray Fitting Tags",
                "Conduit Tags", "Conduit Fitting Tags", "Wire Tags",
                "Communication Device Tags", "Data Device Tags",
                "Security Device Tags", "Nurse Call Device Tags",
                "Fire Alarm Device Tags", "Telephone Device Tags",
            },
            ["Tags — Plumbing"] = new[]
            {
                "Pipe Tags", "Pipe Fitting Tags", "Pipe Accessory Tags",
                "Pipe Insulation Tags", "Flex Pipe Tags",
                "Plumbing Fixture Tags", "Sprinkler Tags",
            },
        };


        private static readonly string[] CommonStingFilters = new[]
        {
            "Existing - Halftone", "Demolished - Red", "New Construction",
            "Temporary Works", "Proposed - Bold", "Design Option A", "Design Option B",
            "Structural Hatch", "Fire Rating - 30min", "Fire Rating - 60min",
            "Fire Rating - 90min", "Fire Rating - 120min",
            "Mechanical Duct", "Electrical Conduit", "Plumbing Pipe",
            "Sprinkler", "Fire Alarm", "Low Voltage",
            "Out of Scope", "NTS - Not To Scale",
        };

        private static readonly string[] CommonStingTextStyles = new[]
        {
            "STING - 2.0mm", "STING - 2.5mm", "STING - 3.0mm Presentation",
            "STING - 2.0mm Shop", "STING - 3.5mm Large Format",
        };

        private static readonly string[] CommonStingDimensionStyles = new[]
        {
            "STING - Linear", "STING - Ordinate", "STING - Chain",
        };

        private static readonly string[] CommonStingViewTemplates = new[]
        {
            "STING - Architectural Plan", "STING - Mechanical Plan",
            "STING - Electrical Plan", "STING - Plumbing Plan", "STING - Structural Plan",
            "STING - Fire Protection Plan", "STING - Low Voltage Plan",
            "STING - MEP Coordination", "STING - Combined Services",
            "STING - Demolition Plan", "STING - As-Built Plan", "STING - Lighting RCP",
            "STING - Ceiling RCP", "STING - Presentation Section", "STING - Detail Section",
            "STING - Coordination 3D", "STING - Presentation 3D",
            "STING - Presentation Elevation", "STING - Working Section",
            "STING - Working Elevation",
        };

        // Auto-annotation rule type vocabulary (Change 6b). Surfaced in
        // the Annotation rules grid as a SmallCombo source.
        private static readonly string[] KnownRuleTypes = new[]
        {
            "AutoTag", "AutoDim", "AutoDimOrdinate", "AutoDimChain",
            "AutoTagWithLeader", "AutoTagHideIfEmpty", "AutoTagTypeMark",
            "AutoTagRoomName", "AutoTagRoomNumber", "AutoTagDoorNumber",
            "AutoTagWindowMark", "AutoTagEquipmentTag", "AutoTagGridBubble",
            "AutoDimWallLength", "AutoDimColumnGrid", "AutoDimOpenings",
            "AutoDimElevation", "AutoAnnotateSlope", "AutoAnnotateFlowArrow",
            "AutoAnnotateSpaceNumber", "AutoAnnotateAreaBoundary",
        };

        // Merge live + static into a deduplicated, sorted list. Used for
        // every "static + project-asset" combo source in the editor.
        private static IEnumerable<string> Merge(IEnumerable<string> live, IEnumerable<string> statics)
        {
            return (live ?? Array.Empty<string>())
                .Concat(statics ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        }

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

            // Phase 137 — initialise _packs in the constructor (was inside
            // BuildViewStylePacksTab) so Tab 0 controls that reference the
            // pack list (e.g. ViewStylePackId combo on a DrawingType card)
            // resolve cleanly on first render.
            _packs = LoadViewStylePacks();
            BuildCategoryTrees();   // Phase 136 — VG editor trees + pattern lists

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
                // Phase 137 — STING-Managed View Templates
                ("Convert to Managed",  "DrawingTypes_ConvertToManaged"),
                ("Detach Managed",      "DrawingTypes_DetachManaged"),
                ("Regenerate Templates","DrawingTypes_RegenerateTemplates"),
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

        // ── View Style Packs editor ───────────────────────────────────────
        // Left = list of packs from STING_VIEW_STYLE_PACKS.json (with
        // extends-chain shown). Right = comprehensive form with Identity,
        // Appearance, Filter rules and VG overrides cards. Mirrors the
        // Drawing Types tab so the user has one mental model.

        private List<ViewStylePack> _packs;
        private ViewStylePack _currentPack;
        private StackPanel _packFormHost;
        private ListBox _lbPacks;

        private UIElement BuildViewStylePacksTab()
        {
            if (_packs == null) _packs = LoadViewStylePacks();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left
            var dock = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };
            var hdr = new TextBlock { Text = "Style packs",
                FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(AccentColor),
                Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(hdr, Dock.Top); dock.Children.Add(hdr);

            var actions = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6) };
            actions.Children.Add(MakeSmallBtn("＋ New",  () => ActionNewPack()));
            actions.Children.Add(MakeSmallBtn("Clone",   () => ActionClonePack()));
            actions.Children.Add(MakeSmallBtn("Delete",  () => ActionDeletePack()));
            // Phase 136 — bidirectional view-template copy.
            actions.Children.Add(MakeSmallBtn("Push template → bound types",
                () => ActionPushPackTemplateToBoundTypes()));
            DockPanel.SetDock(actions, Dock.Top); dock.Children.Add(actions);

            _lbPacks = new ListBox
            {
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
            };
            foreach (var p in _packs) _lbPacks.Items.Add(p);
            _lbPacks.SelectionChanged += (s, e) =>
            {
                if (_lbPacks.SelectedItem is ViewStylePack sel) SelectPack(sel);
            };
            dock.Children.Add(_lbPacks);
            Grid.SetColumn(dock, 0); grid.Children.Add(dock);

            // Right form host
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _packFormHost = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            scroll.Content = _packFormHost;
            Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);

            if (_packs.Count > 0) SelectPack(_packs[0]);
            return grid;
        }

        private void SelectPack(ViewStylePack p)
        {
            _currentPack = p;
            for (int i = 0; i < _lbPacks.Items.Count; i++)
                if (ReferenceEquals(_lbPacks.Items[i], p)) { _lbPacks.SelectedIndex = i; break; }
            RenderPackForm();
        }

        private void RenderPackForm()
        {
            _packFormHost.Children.Clear();
            if (_currentPack == null) return;

            // Phase 137 — Template Mode card (managed vs external + regenerate hook)
            var tmBody = new StackPanel();
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var rbExt = new RadioButton { Content = "External", GroupName = "tm" + _currentPack.Id, IsChecked = !_currentPack.IsManaged, Margin = new Thickness(0,0,12,0) };
            var rbMan = new RadioButton { Content = "Managed",  GroupName = "tm" + _currentPack.Id, IsChecked =  _currentPack.IsManaged };
            rbExt.Checked += (s,e) => { _currentPack.TemplateMode = "external"; RenderPackForm(); };
            rbMan.Checked += (s,e) => { _currentPack.TemplateMode = "managed";  RenderPackForm(); };
            modeRow.Children.Add(rbExt);
            modeRow.Children.Add(rbMan);
            tmBody.Children.Add(modeRow);

            if (_currentPack.IsManaged)
            {
                var info = new TextBlock {
                    Text = "STING will mint templates named 'STING:{pack-id}:{ViewType}'. Save triggers drift; use Regenerate to re-sync.",
                    TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.Goldenrod), Margin = new Thickness(0,4,0,4)
                };
                tmBody.Children.Add(info);
                if (_currentPack.ManagedFields == null || _currentPack.ManagedFields.Count == 0)
                    _currentPack.ManagedFields = new List<string> { "scale", "detailLevel", "discipline", "visualStyle", "phaseFilter" };
                var grid = new WrapPanel { Margin = new Thickness(0,2,0,2) };
                foreach (var f in new[] { "scale","detailLevel","discipline","visualStyle","phaseFilter","phase","annotationCrop","farClip","viewRange","underlay","vgOverrides","filters","worksetVisibility" })
                {
                    var cb = new CheckBox { Content = f, Margin = new Thickness(0,0,8,0), IsChecked = _currentPack.ManagedFields.Contains(f) };
                    cb.Checked   += (s,e) => { if (!_currentPack.ManagedFields.Contains(f)) _currentPack.ManagedFields.Add(f); };
                    cb.Unchecked += (s,e) => _currentPack.ManagedFields.Remove(f);
                    grid.Children.Add(cb);
                }
                tmBody.Children.Add(grid);
                tmBody.Children.Add(LabeledTextBox("Discipline (e.g. Architectural)", _currentPack.Discipline, v => _currentPack.Discipline = v));
                tmBody.Children.Add(LabeledTextBox("Visual style (e.g. HiddenLine)",  _currentPack.VisualStyle, v => _currentPack.VisualStyle = v));
                tmBody.Children.Add(LabeledTextBox("Phase filter name",               _currentPack.PhaseFilter, v => _currentPack.PhaseFilter = v));
            }
            _packFormHost.Children.Add(Card("Template Mode (Phase 137)", tmBody));

            // Identity
            var idBody = new StackPanel();
            idBody.Children.Add(LabeledTextBox("Id",          _currentPack.Id,          v => _currentPack.Id = v));
            idBody.Children.Add(LabeledTextBox("Name",        _currentPack.Name,        v => _currentPack.Name = v));
            idBody.Children.Add(LabeledTextBox("Description", _currentPack.Description, v => _currentPack.Description = v));
            var parents = new[] { "" }.Concat(_packs.Where(x => x != _currentPack).Select(x => x.Id)).ToArray();
            idBody.Children.Add(LabeledCombo("Extends (parent pack id)", parents,
                _currentPack.Extends ?? "", v => _currentPack.Extends = string.IsNullOrEmpty(v) ? null : v));
            idBody.Children.Add(LabeledTextBlock("Origin", _currentPack.Origin ?? "project"));
            _packFormHost.Children.Add(Card("Identity", idBody));

            // Appearance
            var ap = _currentPack.Appearance = _currentPack.Appearance ?? new PackAppearance();
            var apBody = new StackPanel();

            // Phase 137 — Template mode toggle (always visible, top of card)
            apBody.Children.Add(LabeledCombo("Template mode",
                new[] {
                    "external  (point to a Revit template by name)",
                    "managed   (STING auto-generates templates)"
                },
                _currentPack.IsManaged
                    ? "managed   (STING auto-generates templates)"
                    : "external  (point to a Revit template by name)",
                v =>
                {
                    var newMode = (v ?? "").TrimStart().StartsWith("managed", StringComparison.OrdinalIgnoreCase)
                        ? "managed" : "external";
                    if (!string.Equals(newMode, _currentPack.TemplateMode ?? "external",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _currentPack.TemplateMode = newMode;
                        // Seed defaults for managed mode on first switch
                        if (newMode == "managed" && _currentPack.ManagedFields == null)
                            _currentPack.ManagedFields = new List<string>
                            { "vg", "filters", "detailLevel", "discipline", "phaseFilter" };
                        RenderPackForm();
                    }
                },
                tooltip: "external = legacy (pack.viewTemplate names a Revit template). " +
                         "managed = STING auto-generates a Revit template named STING:<pack-id>:<viewType>."));

            apBody.Children.Add(LabeledDouble("Line-weight scale", ap.LineWeightScale,
                v => ap.LineWeightScale = v));
            apBody.Children.Add(LabeledProjectAssetCombo("Text style name",
                ap.TextStyleName, v => ap.TextStyleName = v,
                Merge(ProjectAssetPicker.TextStyleNames(_doc), CommonStingTextStyles),
                "TextNoteType elements in the active project, plus STING corporate text styles."));
            apBody.Children.Add(LabeledProjectAssetCombo("Dimension style name",
                ap.DimensionStyleName, v => ap.DimensionStyleName = v,
                Merge(ProjectAssetPicker.DimensionStyleNames(_doc), CommonStingDimensionStyles),
                "DimensionType elements in the active project, plus STING corporate dimension styles."));
            apBody.Children.Add(LabeledCombo("Hatch palette (informational)",
                new[] { "ISO 13567 monochrome", "ISO 13567 colour", "AIA NCS", "BS 1192 mono", "Project custom" },
                ap.HatchPalette, v => ap.HatchPalette = v,
                tooltip: "Informational tag for the hatch family used by this pack — does not bind to a Revit asset."));
            // Phase 137 — view template field hides in managed mode because
            // the template is auto-generated by ManagedTemplateSyncer; the
            // DrawingType.ViewTemplateName field still wins when set.
            if (!_currentPack.IsManaged)
            {
                apBody.Children.Add(LabeledProjectAssetCombo("View template name (fallback)",
                    _currentPack.ViewTemplate, v => _currentPack.ViewTemplate = v,
                    Merge(ProjectAssetPicker.ViewTemplateNames(_doc), CommonStingViewTemplates),
                    "Pack-level view template — used as a FALLBACK when the DrawingType's own " +
                    "ViewTemplateName is empty. The DrawingType field always wins when both are set."));
            }
            apBody.Children.Add(LabeledCombo("Detail level (fallback)",
                Iso19650Vocabulary.DetailLevels, _currentPack.DetailLevel,
                v => _currentPack.DetailLevel = v,
                tooltip: "Pack-level detail level — fallback when the DrawingType doesn't set one."));
            apBody.Children.Add(LabeledCombo("Scale hint",
                new[] { "1:5", "1:10", "1:20", "1:25", "1:50", "1:100", "1:200", "1:500" },
                _currentPack.ScaleHint, v => _currentPack.ScaleHint = v,
                tooltip: "Informational only — the DrawingType.Scale field actually drives the produced view's scale."));
            apBody.Children.Add(LabeledCombo("Colour scheme",
                Iso19650Vocabulary.ColorSchemes,
                _currentPack.ColorScheme, v => _currentPack.ColorScheme = v));

            // Phase 136 — show how many line / fill patterns are available
            // from the active project. Confirms to the user that the VG
            // editor's Override... sub-dialogs are pulling live project
            // patterns, not a static list.
            int linePatCount = Math.Max(0, (_linePatternNames?.Length ?? 0) - 2);   // minus "(No Override)" + "Solid"
            int fillPatCount = Math.Max(0, (_fillPatternNames?.Length ?? 0) - 1);   // minus "(No Override)"
            apBody.Children.Add(new TextBlock {
                Text = $"Patterns from active project — {linePatCount} line pattern(s), {fillPatCount} fill pattern(s) loaded. " +
                       "The Override... buttons in the VG tabs below pull from these lists.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });

            // Phase 137 — managed-mode-only fields
            if (_currentPack.IsManaged)
            {
                apBody.Children.Add(BuildManagedInfoStrip());
                apBody.Children.Add(LabeledCombo("Visual style",
                    new[]{"Wireframe","HiddenLine","Shading","ShadingWithEdges",
                          "Realistic","RealisticWithEdges","RayTrace","(inherit)"},
                    _currentPack.VisualStyle ?? "(inherit)",
                    v => _currentPack.VisualStyle = (v == "(inherit)" || string.IsNullOrWhiteSpace(v)) ? null : v));
                apBody.Children.Add(LabeledCombo("View discipline",
                    new[]{"Architectural","Structural","Mechanical","Electrical",
                          "Plumbing","Coordination","(inherit)"},
                    _currentPack.Discipline ?? "(inherit)",
                    v => _currentPack.Discipline = (v == "(inherit)" || string.IsNullOrWhiteSpace(v)) ? null : v));
                apBody.Children.Add(LabeledProjectAssetCombo("Phase filter",
                    _currentPack.PhaseFilter,
                    v => _currentPack.PhaseFilter = string.IsNullOrEmpty(v) ? null : v,
                    Merge(ProjectAssetPicker.PhaseFilterNames(_doc),
                          new[] { "Show All", "Show New", "Show Previous + New", "Show Demo + New" }),
                    "PhaseFilter elements in the active project."));
                apBody.Children.Add(LabeledProjectAssetCombo("Phase",
                    _currentPack.PhaseName,
                    v => _currentPack.PhaseName = string.IsNullOrEmpty(v) ? null : v,
                    ProjectAssetPicker.PhaseNames(_doc),
                    "Phase elements in the active project."));

                apBody.Children.Add(BuildViewRangeSubCard());

                apBody.Children.Add(LabeledNumber("Far clip offset (mm)",
                    (int)(_currentPack.FarClipMm ?? 30000),
                    v => _currentPack.FarClipMm = v <= 0 ? (double?)null : v));

                apBody.Children.Add(CheckRow("Annotation crop",
                    _currentPack.AnnotationCrop ?? false,
                    v => _currentPack.AnnotationCrop = v));

                apBody.Children.Add(BuildManagedFieldsEditor());
            }

            _packFormHost.Children.Add(Card("Appearance", apBody));

            // Filter rules
            var frBody = new StackPanel();
            _currentPack.FilterRules = _currentPack.FilterRules ?? new List<PackFilterRule>();
            frBody.Children.Add(new TextBlock {
                Text = "Per-filter graphic overrides. Filters must already exist in the project (ParameterFilterElement). Colours in #RRGGBB.",
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4) });
            frBody.Children.Add(MakeFilterRuleHeader());
            foreach (var fr in _currentPack.FilterRules.ToList())
                frBody.Children.Add(MakeFilterRuleRow(fr));
            frBody.Children.Add(MakeSmallBtn("＋ Add filter rule", () =>
            {
                _currentPack.FilterRules.Add(new PackFilterRule { Name = "NewFilter", Visible = true,
                    ProjColor = "#000000", ProjWeight = 1, CutColor = "#000000", CutWeight = 1 });
                RenderPackForm();
            }));
            _packFormHost.Children.Add(Card("Filter rules", frBody));

            // VG Overrides per category — Phase 137: full Revit VG dialog
            // replica embedded inline (no popup). Backed by a bridge dict
            // of PresetCategoryOverride that mirrors the pack's
            // PackCategoryOverride dict on every cell change.
            var vgBody = new StackPanel();
            _currentPack.VgOverrides = _currentPack.VgOverrides ?? new Dictionary<string, PackCategoryOverride>();
            vgBody.Children.Add(new TextBlock {
                Text = "Full Revit Visibility/Graphics Overrides — every model + annotation category from the active project pre-populated, " +
                       "subcategories indented underneath. Cells round-trip live into the pack JSON; tab away from a cell to commit. " +
                       "No popup, no Revit-VG-dialog round-trip.",
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6) });

            var bridge = BuildVgBridge(_currentPack);
            var editor = new RevitVgEditor(_doc, bridge);
            editor.RowChanged += r => SyncRowToPack(_currentPack, r);
            // Constrain the editor to a sensible inline height; user can scroll
            // through ~80+ categories + 300+ subcategories without the card
            // ballooning the whole pack form.
            var editorHost = new Border { Height = 520, Child = editor.Build() };
            vgBody.Children.Add(editorHost);
            _packFormHost.Children.Add(Card("VG overrides (full Revit replica, with subcategories)", vgBody));

            // Phase 135 — Tag Appearance card
            _packFormHost.Children.Add(BuildPackTagAppearanceCard());

            // Validation strip
            _packFormHost.Children.Add(new TextBlock
            {
                Text = ValidatePack(_currentPack),
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(4, 10, 4, 4),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Phase 135 — pack-level tag appearance defaults. Per-DrawingType
        // AnnotationTokenProfile fields override these; the pack supplies
        // a sensible default for every profile that bound to it but didn't
        // set every tag knob explicitly.
        private UIElement BuildPackTagAppearanceCard()
        {
            var p = _currentPack;
            var body = new StackPanel();

            string[] schemes = new[] { "", "Discipline", "System", "Status", "Zone", "Level", "Location", "Function",
                                        "Warm", "Cool", "Red", "Yellow", "Blue", "Mono", "Dark" };
            body.Children.Add(LabeledCombo("Default colour scheme",
                schemes, p.TagColorScheme ?? "",
                v => p.TagColorScheme = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                tooltip: "Variable-driven scheme written to STING_VIEW_TAG_STYLE on every view this pack applies to. Profile-level scheme wins."));

            string[] commonStyles = new[] { "",
                "2NOM_BLACK", "2BOLD_BLACK", "2.5NOM_BLACK", "2.5BOLD_BLACK",
                "2NOM_BLUE", "2BOLD_BLUE", "2.5NOM_BLUE",
                "2NOM_GREEN", "2BOLD_GREEN",
                "2NOM_RED", "2BOLD_RED", "2.5BOLD_RED",
                "2NOM_ORANGE", "2BOLD_ORANGE",
                "2.5BOLDITALIC_PURPLE",
                "3NOM_BLACK", "3BOLD_BLACK", "3.5BOLD_BLACK",
            };
            body.Children.Add(LabeledCombo("Default tag style preset",
                commonStyles, p.DefaultTagStyle ?? "",
                v => p.DefaultTagStyle = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                tooltip: "Canonical name '{size}{style}_{color}' (e.g. '2.5BOLD_RED'). Profile-level Size/Style/Color triple wins."));

            body.Children.Add(BuildPackCategoryTagStylesGrid(p));
            return Card("Tag appearance (Phase 135)", body);
        }

        private static readonly GridLength[] _packCatStyleCols =
        {
            new GridLength(180),
            new GridLength(1, GridUnitType.Star),
            new GridLength(24),
        };

        private UIElement BuildPackCategoryTagStylesGrid(ViewStylePack p)
        {
            p.CategoryTagStyles = p.CategoryTagStyles ?? new Dictionary<string, string>();

            var host = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            host.Children.Add(new TextBlock {
                Text = "Per-category tag style — overrides Default tag style above for that category.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });

            // Header
            var header = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _packCatStyleCols.Length; i++)
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = _packCatStyleCols[i] });
            string[] hd = { "Category", "Style preset", "" };
            for (int i = 0; i < hd.Length; i++)
            {
                var t = new TextBlock { Text = hd[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
                Grid.SetColumn(t, i); header.Children.Add(t);
            }
            host.Children.Add(header);

            var cats = Merge(ProjectAssetPicker.TaggableCategoryNames(_doc),
                             KnownTaggableCategories).ToArray();
            string[] commonStyles = new[] {
                "2NOM_BLACK", "2BOLD_BLACK", "2.5NOM_BLACK", "2.5BOLD_BLACK",
                "2NOM_BLUE", "2BOLD_BLUE",
                "2NOM_GREEN", "2BOLD_GREEN",
                "2NOM_RED", "2BOLD_RED", "2.5BOLD_RED",
                "2NOM_ORANGE", "2BOLD_ORANGE",
                "3NOM_BLACK", "3BOLD_BLACK",
            };

            foreach (var kv in p.CategoryTagStyles.ToList())
            {
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                for (int i = 0; i < _packCatStyleCols.Length; i++)
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = _packCatStyleCols[i] });

                string catKey = kv.Key;
                var keyBox = SmallCombo(catKey, newKey =>
                {
                    newKey = (newKey ?? "").Trim();
                    if (string.IsNullOrEmpty(newKey) || newKey == catKey) return;
                    if (!p.CategoryTagStyles.ContainsKey(catKey)) return;
                    var existing = p.CategoryTagStyles[catKey];
                    p.CategoryTagStyles.Remove(catKey);
                    p.CategoryTagStyles[newKey] = existing;
                }, cats);

                var styleBox = SmallCombo(kv.Value ?? "", v =>
                {
                    if (p.CategoryTagStyles.ContainsKey(catKey))
                        p.CategoryTagStyles[catKey] = (v ?? "").Trim();
                }, commonStyles);

                var rm = MakeSmallBtn("×", () => { p.CategoryTagStyles.Remove(catKey); RenderPackForm(); });
                rm.Width = 22;

                var ctrls = new UIElement[] { keyBox, styleBox, rm };
                for (int i = 0; i < ctrls.Length; i++)
                {
                    Grid.SetColumn(ctrls[i], i);
                    if (ctrls[i] is FrameworkElement fe)
                        fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                    g.Children.Add(ctrls[i]);
                }
                host.Children.Add(g);
            }

            host.Children.Add(MakeSmallBtn("＋ Add category style", () =>
            {
                var key = "NewCategory" + p.CategoryTagStyles.Count;
                p.CategoryTagStyles[key] = "2NOM_BLACK";
                RenderPackForm();
            }));
            return host;
        }

        // ── Filter rule row ──
        private UIElement MakeFilterRuleHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            string[] headers = { "Filter name", "Visible", "Halftone", "Proj-Col", "Proj-Wt", "Cut-Col", "Cut-Wt", "Trans%" };
            double[] widths  = { 160, 56, 56, 70, 50, 70, 50, 56 };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });
            for (int i = 0; i < headers.Length; i++)
            {
                var t = new TextBlock { Text = headers[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor) };
                Grid.SetColumn(t, i); g.Children.Add(t);
            }
            return g;
        }

        private UIElement MakeFilterRuleRow(PackFilterRule fr)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            double[] widths = { 160, 56, 56, 70, 50, 70, 50, 56 };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });

            var filterNames = Merge(ProjectAssetPicker.ParameterFilterNames(_doc),
                                    CommonStingFilters).ToArray();
            var name = SmallCombo(fr.Name, v => fr.Name = v, filterNames);
            var vis  = MakeChk(fr.Visible,  v => fr.Visible = v);
            var ht   = MakeChk(fr.Halftone, v => fr.Halftone = v);
            // Phase 137 — Proj-Col / Cut-Col are now colour swatch buttons
            // that open VgColorPicker. Tooltip shows hex + R G B + decimal
            // RGB so users see the value in multiple formats at a glance.
            var pc   = MakeColourSwatch(fr.ProjColor, v => { fr.ProjColor = v; RenderPackForm(); });
            var pw   = SmallTb(fr.ProjWeight.ToString(), v => fr.ProjWeight = TryInt(v));
            var cc   = MakeColourSwatch(fr.CutColor,  v => { fr.CutColor  = v; RenderPackForm(); });
            var cw   = SmallTb(fr.CutWeight.ToString(), v => fr.CutWeight = TryInt(v));
            var tr   = SmallTb(fr.Transparency.ToString(), v => fr.Transparency = TryInt(v));

            var ctrls = new UIElement[] { name, vis, ht, pc, pw, cc, cw, tr };
            for (int i = 0; i < ctrls.Length; i++)
            {
                Grid.SetColumn(ctrls[i], i);
                if (ctrls[i] is FrameworkElement fe) fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                g.Children.Add(ctrls[i]);
            }
            return g;
        }

        // ── Phase 137 — VG bridge between PackCategoryOverride (the editor's
        //    on-disk JSON shape) and PresetCategoryOverride (the full-fidelity
        //    in-memory model the inline RevitVgEditor binds to). The bridge
        //    is built once when the pack form renders, lives in memory for
        //    the editor, and is synced back to the pack on every RowChanged
        //    event so the user's edits survive Save without an explicit
        //    "apply" step. ──

        private static Dictionary<string, StingTools.Core.Drawing.PresetCategoryOverride>
            BuildVgBridge(ViewStylePack pack)
        {
            var bridge = new Dictionary<string, StingTools.Core.Drawing.PresetCategoryOverride>(StringComparer.OrdinalIgnoreCase);
            if (pack?.VgOverrides == null) return bridge;
            foreach (var kv in pack.VgOverrides)
            {
                bridge[kv.Key] = new StingTools.Core.Drawing.PresetCategoryOverride
                {
                    Category       = kv.Key,
                    Visible        = kv.Value?.Visible,
                    Halftone       = kv.Value != null && kv.Value.Halftone ? true : (bool?)null,
                    ProjLineColor  = kv.Value?.ProjColor,
                    ProjLineWeight = kv.Value != null && kv.Value.ProjWeight  > 0 ? kv.Value.ProjWeight  : (int?)null,
                    CutLineColor   = kv.Value?.CutColor,
                    CutLineWeight  = kv.Value != null && kv.Value.CutWeight   > 0 ? kv.Value.CutWeight   : (int?)null,
                    Transparency   = kv.Value != null && kv.Value.Transparency> 0 ? kv.Value.Transparency: (int?)null
                };
            }
            return bridge;
        }

        /// <summary>
        /// Phase 137 — push a single edited row from the inline VG editor
        /// back into the pack's PackCategoryOverride dict. Called on every
        /// cell change so the pack's on-disk JSON stays current; rows
        /// without a meaningful override are dropped from the dict.
        /// </summary>
        private static void SyncRowToPack(ViewStylePack pack, RevitVgEditor.VgRow r)
        {
            if (pack == null || r?.Data == null) return;
            pack.VgOverrides = pack.VgOverrides ?? new Dictionary<string, PackCategoryOverride>();
            var v = r.Data;
            bool any = v.Visible.HasValue || v.Halftone.HasValue
                    || !string.IsNullOrEmpty(v.ProjLineColor) || v.ProjLineWeight.HasValue
                    || !string.IsNullOrEmpty(v.CutLineColor)  || v.CutLineWeight.HasValue
                    || v.Transparency.HasValue;
            if (!any)
            {
                pack.VgOverrides.Remove(r.Key);
                return;
            }
            pack.VgOverrides[r.Key] = new PackCategoryOverride
            {
                Visible      = v.Visible ?? true,
                Halftone     = v.Halftone == true,
                ProjColor    = v.ProjLineColor,
                ProjWeight   = v.ProjLineWeight ?? 0,
                CutColor     = v.CutLineColor,
                CutWeight    = v.CutLineWeight ?? 0,
                Transparency = v.Transparency ?? 0
            };
        }

        // ── Legacy compact VG row + header (still referenced by older code paths) ──

        private UIElement MakeVgHeader()
        {
            if (_currentPack.ManagedFields == null || _currentPack.ManagedFields.Count == 0)
                _currentPack.ManagedFields = new List<string>(_managedFieldsDefault);

            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = "Managed fields — STING will only write checked fields onto the template. " +
                       "Anything unchecked stays user-editable in Revit's template editor.",
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
            });

            // 4 columns × 3 rows for 12 checkboxes
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var f in _managedFieldsAll)
            {
                var key = f;
                bool isOn = _currentPack.ManagedFields.Contains(key, StringComparer.OrdinalIgnoreCase);
                var cb = new CheckBox
                {
                    Content = key,
                    IsChecked = isOn,
                    Width = 130,
                    Margin = new Thickness(0, 2, 8, 2),
                    Foreground = new SolidColorBrush(FgColor),
                };
                cb.Checked += (s, e) =>
                {
                    if (!_currentPack.ManagedFields.Contains(key, StringComparer.OrdinalIgnoreCase))
                        _currentPack.ManagedFields.Add(key);
                };
                cb.Unchecked += (s, e) =>
                {
                    var existing = _currentPack.ManagedFields
                        .FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) _currentPack.ManagedFields.Remove(existing);
                };
                wrap.Children.Add(cb);
            }
            body.Children.Add(wrap);
            return Card("Managed fields", body);
        }

        private CheckBox MakeChk(bool value, Action<bool> setter)
        {
            var cb = new CheckBox
            {
                IsChecked = value,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            cb.Checked   += (s, e) => setter?.Invoke(true);
            cb.Unchecked += (s, e) => setter?.Invoke(false);
            return cb;
        }

        private static int TryInt(string s) => int.TryParse(s, out var n) ? n : 0;

        /// <summary>
        /// Phase 137 — clickable colour swatch button used for filter-rules
        /// Proj-Col / Cut-Col cells. Opens VgColorPicker (Windows native
        /// picker) and writes back the picked hex. Tooltip shows hex +
        /// R/G/B in three formats so users can copy whichever they need.
        /// </summary>
        private System.Windows.Controls.Button MakeColourSwatch(string hex, Action<string> setter)
        {
            var btn = new System.Windows.Controls.Button { Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch };
            UpdateColourSwatch(btn, hex);
            btn.Click += (s, e) =>
            {
                var picked = StingTools.UI.VgColorPicker.Pick(hex);
                if (picked == null) return; // cancelled
                hex = picked;
                UpdateColourSwatch(btn, hex);
                setter?.Invoke(hex);
            };
            return btn;
        }

        private static void UpdateColourSwatch(System.Windows.Controls.Button btn, string hex)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new System.Windows.Controls.Border
            {
                Width = 16, Height = 14, Margin = new Thickness(0, 0, 4, 0),
                Background = StingTools.UI.VgColorPicker.HexToBrush(hex),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1)
            });
            sp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(hex) ? "—" : hex,
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center
            });
            btn.Content = sp;
            // Multi-format tooltip — hex / R G B decimal / 0.x normalised /
            // RGB() CSS string. One swatch click → user copies whichever
            // format they need without leaving the dialog.
            if (StingTools.UI.VgColorPicker.TryParseHex(hex, out byte r, out byte g, out byte b))
            {
                btn.ToolTip = $"Hex: {hex}\nR G B: {r} {g} {b}\nRGB(): rgb({r},{g},{b})\nNormalised: {(r/255.0):F3} {(g/255.0):F3} {(b/255.0):F3}";
            }
            else
            {
                btn.ToolTip = "Click to pick a colour. Currently no override.";
            }
        }

        private string ValidatePack(ViewStylePack p)
        {
            var issues = new List<string>();
            if (string.IsNullOrEmpty(p.Id)) issues.Add("id is empty");
            if (!string.IsNullOrEmpty(p.Extends) && !_packs.Any(x => x.Id == p.Extends))
                issues.Add($"extends references missing pack '{p.Extends}'");
            if (issues.Count == 0) return $"✓ Pack OK. Extends chain: {ResolveExtendsChain(p)}";
            return $"⚠ {issues.Count} issue(s): {string.Join(" · ", issues)}";
        }

        private string ResolveExtendsChain(ViewStylePack p)
        {
            var chain = new List<string> { p.Id };
            var cur = p;
            int guard = 10;
            while (cur != null && !string.IsNullOrEmpty(cur.Extends) && guard-- > 0)
            {
                cur = _packs.FirstOrDefault(x => x.Id == cur.Extends);
                if (cur != null) chain.Add(cur.Id);
            }
            return string.Join(" → ", chain);
        }

        // ── Pack list actions ──
        private void ActionNewPack()
        {
            var p = new ViewStylePack
            {
                Id = "new-pack-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Name = "New Style Pack",
                Origin = "project",
                Extends = "corp-base",
                Appearance = new PackAppearance(),
                FilterRules = new List<PackFilterRule>(),
                VgOverrides = new Dictionary<string, PackCategoryOverride>(),
            };
            _packs.Add(p);
            _lbPacks.Items.Add(p);
            SelectPack(p);
        }

        private void ActionClonePack()
        {
            if (_currentPack == null) return;
            var json = JsonConvert.SerializeObject(_currentPack);
            var copy = JsonConvert.DeserializeObject<ViewStylePack>(json);
            copy.Id = _currentPack.Id + "-copy";
            copy.Name = (_currentPack.Name ?? _currentPack.Id) + " (copy)";
            copy.Origin = "project";
            _packs.Add(copy);
            _lbPacks.Items.Add(copy);
            SelectPack(copy);
        }

        private void ActionDeletePack()
        {
            if (_currentPack == null) return;
            var resp = System.Windows.MessageBox.Show(
                $"Delete style pack '{_currentPack.Id}'?\nCorporate packs only vanish from the project override.",
                "Confirm", MessageBoxButton.YesNo);
            if (resp != MessageBoxResult.Yes) return;
            _packs.Remove(_currentPack);
            _lbPacks.Items.Remove(_currentPack);
            _currentPack = _packs.FirstOrDefault();
            if (_currentPack != null) SelectPack(_currentPack); else _packFormHost.Children.Clear();
        }

        // Phase 136 — push pack.ViewTemplate down to every DrawingType bound
        // to this pack. Useful when you've changed the pack default and want
        // every drawing using it to pick up the new template explicitly
        // (rather than relying on the runtime fallback).
        private void ActionPushPackTemplateToBoundTypes()
        {
            if (_currentPack == null) return;
            if (string.IsNullOrWhiteSpace(_currentPack.ViewTemplate))
            {
                System.Windows.MessageBox.Show(
                    "Set the pack's view template first (Appearance card).",
                    "STING — push template", MessageBoxButton.OK);
                return;
            }
            var bound = _types.Where(t => string.Equals(t.ViewStylePackId, _currentPack.Id,
                                                         StringComparison.OrdinalIgnoreCase)).ToList();
            if (bound.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    $"No Drawing Types are bound to pack '{_currentPack.Id}'. Bind some first.",
                    "STING — push template", MessageBoxButton.OK);
                return;
            }

            // Show preview list and confirm.
            var preview = string.Join("\n", bound.Take(10).Select(t => $"  • {t.Id}"
                + (string.IsNullOrEmpty(t.ViewTemplateName) ? "  (currently inherits)" : $"  (overwrites '{t.ViewTemplateName}')")));
            if (bound.Count > 10) preview += $"\n  …and {bound.Count - 10} more";
            var resp = System.Windows.MessageBox.Show(
                $"Set ViewTemplateName = '{_currentPack.ViewTemplate}' on {bound.Count} Drawing Type(s) bound to '{_currentPack.Id}'?\n\n"
                + preview + "\n\nDrawing Types that already have an explicit template will be OVERWRITTEN.",
                "STING — push template to bound types", MessageBoxButton.YesNo);
            if (resp != MessageBoxResult.Yes) return;

            int n = 0;
            foreach (var t in bound)
            {
                t.ViewTemplateName = _currentPack.ViewTemplate;
                if (!string.Equals(t.Origin, "project", StringComparison.OrdinalIgnoreCase))
                    t.Origin = "project";   // edits land in project override on save
                n++;
            }
            // Refresh the Drawing Types tab list display.
            RefreshList();
            System.Windows.MessageBox.Show(
                $"Updated {n} Drawing Type(s).\nSave to persist to <project>/_BIM_COORD/drawing_types.json.",
                "STING — push template", MessageBoxButton.OK);
        }

        // ── JSON load ──
        private List<ViewStylePack> LoadViewStylePacks()
        {
            var list = new List<ViewStylePack>();
            try
            {
                var path = Path.Combine(StingTools.Core.StingToolsApp.DataPath ?? "", "STING_VIEW_STYLE_PACKS.json");
                if (!File.Exists(path)) return list;
                var doc = JsonConvert.DeserializeObject<ViewStylePackDoc>(File.ReadAllText(path));
                return doc?.StylePacks ?? list;
            }
            catch (Exception ex) { StingLog.Warn("ViewStylePacks load: " + ex.Message); return list; }
        }

        // ── POCO models for view style packs ──
        private class ViewStylePackDoc
        {
            [JsonProperty("stylePacks")] public List<ViewStylePack> StylePacks { get; set; }
        }

        private class ViewStylePack
        {
            [JsonProperty("id")]          public string Id { get; set; }
            [JsonProperty("name")]        public string Name { get; set; }
            [JsonProperty("description")] public string Description { get; set; }
            [JsonProperty("extends")]     public string Extends { get; set; }
            [JsonProperty("origin")]      public string Origin { get; set; }
            [JsonProperty("viewTemplate")] public string ViewTemplate { get; set; }
            [JsonProperty("detailLevel")] public string DetailLevel { get; set; }
            [JsonProperty("scaleHint")]   public string ScaleHint { get; set; }
            [JsonProperty("colorScheme")] public string ColorScheme { get; set; }
            [JsonProperty("appearance")]  public PackAppearance Appearance { get; set; }
            [JsonProperty("filterRules")] public List<PackFilterRule> FilterRules { get; set; }
            [JsonProperty("vgOverrides")] public Dictionary<string, PackCategoryOverride> VgOverrides { get; set; }

            // Phase 135 — Tag Appearance pack-level defaults
            [JsonProperty("tagColorScheme",   NullValueHandling = NullValueHandling.Ignore)]
            public string TagColorScheme { get; set; }
            [JsonProperty("defaultTagStyle",  NullValueHandling = NullValueHandling.Ignore)]
            public string DefaultTagStyle { get; set; }
            [JsonProperty("categoryTagStyles", NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, string> CategoryTagStyles { get; set; }

            // Phase 137 — Managed view-template mode (nested-class mirror of
            // StingTools.Core.Drawing.ViewStylePack so the editor's pack
            // form can author the same fields the runtime reads).
            [JsonProperty("templateMode",  NullValueHandling = NullValueHandling.Ignore)]
            public string TemplateMode { get; set; }
            [JsonProperty("managedFields", NullValueHandling = NullValueHandling.Ignore)]
            public List<string> ManagedFields { get; set; }
            [JsonProperty("discipline",    NullValueHandling = NullValueHandling.Ignore)]
            public string Discipline { get; set; }
            [JsonProperty("visualStyle",   NullValueHandling = NullValueHandling.Ignore)]
            public string VisualStyle { get; set; }
            [JsonProperty("phaseFilter",   NullValueHandling = NullValueHandling.Ignore)]
            public string PhaseFilter { get; set; }

            [JsonIgnore]
            public bool IsManaged =>
                string.Equals(TemplateMode, "managed", StringComparison.OrdinalIgnoreCase);

            public override string ToString()
            {
                var ext = string.IsNullOrEmpty(Extends) ? "" : $"  ←  {Extends}";
                var managed = IsManaged ? "● " : "";
                return $"{managed}{Id}{ext}";
            }
        }

        private class PackViewRangeUi
        {
            [JsonProperty("topMm")]       public double TopMm       { get; set; } = 2300;
            [JsonProperty("cutPlaneMm")] public double CutPlaneMm  { get; set; } = 1200;
            [JsonProperty("bottomMm")]   public double BottomMm    { get; set; } = 0;
            [JsonProperty("viewDepthMm")]public double ViewDepthMm { get; set; } = -300;
        }

        private class PackDisplayOptionsUi
        {
            [JsonProperty("shadows")]        public bool Shadows        { get; set; }
            [JsonProperty("sketchyLines")]   public bool SketchyLines   { get; set; }
            [JsonProperty("ambientShadows")] public bool AmbientShadows { get; set; }
        }

        private class PackAppearance
        {
            [JsonProperty("lineWeightScale")] public double LineWeightScale { get; set; } = 1.0;
            [JsonProperty("textStyleName")]   public string TextStyleName { get; set; }
            [JsonProperty("dimensionStyleName")] public string DimensionStyleName { get; set; }
            [JsonProperty("hatchPalette")]    public string HatchPalette { get; set; }
        }

        // Phase 136 — PackFilterRule mirrors StyleFilterRule's full Revit-style
        // override surface: line override (color + weight + pattern) and
        // surface fg/bg pattern (name + color + visibility) for both
        // projection and cut. Legacy projColor/projWeight/cutColor/cutWeight
        // kept for backward compat with existing JSON files.
        private class PackFilterRule
        {
            [JsonProperty("name")]         public string Name { get; set; }
            [JsonProperty("visible")]      public bool Visible { get; set; } = true;
            [JsonProperty("halftone")]     public bool Halftone { get; set; }

            // ── legacy short field names (kept for back-compat) ──
            [JsonProperty("projColor",  NullValueHandling = NullValueHandling.Ignore)] public string ProjColor { get; set; }
            [JsonProperty("projWeight", NullValueHandling = NullValueHandling.Ignore)] public int    ProjWeight { get; set; }
            [JsonProperty("cutColor",   NullValueHandling = NullValueHandling.Ignore)] public string CutColor { get; set; }
            [JsonProperty("cutWeight",  NullValueHandling = NullValueHandling.Ignore)] public int    CutWeight { get; set; }
            [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)] public int  Transparency { get; set; }

            // ── new full Revit-style fields ──
            [JsonProperty("projLinePattern", NullValueHandling = NullValueHandling.Ignore)] public string ProjLinePattern { get; set; }

            [JsonProperty("surfaceFgPatternName",    NullValueHandling = NullValueHandling.Ignore)] public string SurfaceFgPatternName { get; set; }
            [JsonProperty("surfaceFgPatternColor",   NullValueHandling = NullValueHandling.Ignore)] public string SurfaceFgPatternColor { get; set; }
            [JsonProperty("surfaceFgPatternVisible", NullValueHandling = NullValueHandling.Ignore)] public bool?  SurfaceFgPatternVisible { get; set; }

            [JsonProperty("surfaceBgPatternName",    NullValueHandling = NullValueHandling.Ignore)] public string SurfaceBgPatternName { get; set; }
            [JsonProperty("surfaceBgPatternColor",   NullValueHandling = NullValueHandling.Ignore)] public string SurfaceBgPatternColor { get; set; }
            [JsonProperty("surfaceBgPatternVisible", NullValueHandling = NullValueHandling.Ignore)] public bool?  SurfaceBgPatternVisible { get; set; }

            [JsonProperty("cutLinePattern", NullValueHandling = NullValueHandling.Ignore)] public string CutLinePattern { get; set; }

            [JsonProperty("cutFgPatternName",    NullValueHandling = NullValueHandling.Ignore)] public string CutFgPatternName { get; set; }
            [JsonProperty("cutFgPatternColor",   NullValueHandling = NullValueHandling.Ignore)] public string CutFgPatternColor { get; set; }
            [JsonProperty("cutFgPatternVisible", NullValueHandling = NullValueHandling.Ignore)] public bool?  CutFgPatternVisible { get; set; }

            [JsonProperty("cutBgPatternName",    NullValueHandling = NullValueHandling.Ignore)] public string CutBgPatternName { get; set; }
            [JsonProperty("cutBgPatternColor",   NullValueHandling = NullValueHandling.Ignore)] public string CutBgPatternColor { get; set; }
            [JsonProperty("cutBgPatternVisible", NullValueHandling = NullValueHandling.Ignore)] public bool?  CutBgPatternVisible { get; set; }
        }

        // Phase 136 — PackCategoryOverride mirrors StyleVgOverride's full
        // Revit-style override surface. Visibility / Halftone now nullable
        // (null = inherited / not set). Legacy projColor/projWeight/cutColor/
        // cutWeight kept for backward compat with existing JSON files.
        private class PackCategoryOverride
        {
            [JsonProperty("visible")]      public bool Visible { get; set; } = true;
            [JsonProperty("halftone")]     public bool Halftone { get; set; }
            [JsonProperty("projColor")]    public string ProjColor { get; set; }
            [JsonProperty("projWeight")]   public int ProjWeight { get; set; }
            [JsonProperty("cutColor")]     public string CutColor { get; set; }
            [JsonProperty("cutWeight")]    public int CutWeight { get; set; }
            [JsonProperty("transparency")] public int Transparency { get; set; }
        }

        private UIElement BuildViewportToolsTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(TabIntro("Viewport Tools",
                "Operate on viewports placed on sheets — align, renumber, distribute. " +
                "Every button dispatches to the same Revit external-event handler the dock panel uses. " +
                "Run on the active sheet's viewports, or on a multi-select."));

            stack.Children.Add(SectionCardRich("Alignment",
                "Snap selected viewports to a common edge or centre line.",
                new (string,string,string)[]
                {
                    ("↑ Top",  "VPAlignTop",   "Align to topmost edge"),
                    ("↕ MidY", "VPAlignMidY",  "Align horizontal centre lines"),
                    ("↓ Bot",  "VPAlignBot",   "Align to bottom edge"),
                    ("← Left", "VPAlignLeft",  "Align to leftmost edge"),
                    ("↔ MidX", "VPAlignMidX",  "Align vertical centre lines"),
                    ("→ Right","VPAlignRight", "Align to rightmost edge"),
                }));
            stack.Children.Add(SectionCardRich("Numbering",
                "Auto-renumber viewports on the active sheet (left-to-right or top-to-bottom).",
                new (string,string,string)[]
                {
                    ("L→R",      "VPNumLR",            "Renumber left-to-right, top-to-bottom"),
                    ("T→B",      "VPNumTB",            "Renumber top-to-bottom, left-to-right"),
                    ("+1",       "VPNumPlus",          "Increment all viewport numbers by 1"),
                    ("-1",       "VPNumMinus",         "Decrement all viewport numbers by 1"),
                    ("Prefix",   "VPPrefix",           "Add a prefix to selected viewport numbers"),
                    ("Suffix",   "VPSuffix",           "Add a suffix to selected viewport numbers"),
                    ("Renum VP", "RenumberViewports",  "Generic renumber dialog (handed-off to engine)"),
                }));
            stack.Children.Add(SectionCardRich("Spacing",
                "Auto-position, evenly distribute or move viewports between sheets.",
                new (string,string,string)[]
                {
                    ("Auto-Place",     "AutoPlaceViewports",  "Pack viewports onto sheet using shelf-packing"),
                    ("Batch Align",    "BatchAlignViewports", "Align viewports across multiple sheets"),
                    ("Align VP",       "AlignViewports",      "Single-sheet alignment dialog"),
                    ("Crop to Content","CropToContent",       "Tight crop region per viewport"),
                    ("Move VP",        "MoveViewport",        "Move a viewport between sheets"),
                }));
            host.Content = stack;
            return host;
        }

        private UIElement BuildSheetToolsTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(TabIntro("Sheet Tools",
                "Project-wide sheet operations — organise, index, transmittal, naming, automation. " +
                "Read-only commands open dialogs; Manual commands run a Revit transaction."));

            stack.Children.Add(SectionCardRich("Sheet operations",
                "Catalogue, organise and audit sheets. Most produce a CSV or schedule.",
                new (string,string,string)[]
                {
                    ("Organizer",      "SheetOrganizer",    "Group sheets by discipline prefix"),
                    ("Index",          "SheetIndex",        "Create the STING - Sheet Index schedule"),
                    ("Transmittal",    "Transmittal",       "Generate ISO 19650 transmittal report"),
                    ("Journal",        "JournalParser",     "Parse Revit journal for errors / commands / memory"),
                    ("Naming",         "SheetNamingCheck",  "ISO 19650 sheet-naming compliance audit"),
                    ("Auto-Num",       "AutoNumberSheets",  "Sequentially renumber sheets within discipline"),
                    ("Map Sheets",     "MapSheets",         "Build sheet-to-element ownership map"),
                    ("Tag Sheets",     "TagSheets",         "Bulk-write title-block tags onto sheets"),
                    ("View Organizer", "ViewOrganizer",     "Organise non-placed views by type / level"),
                    ("Delete Unused",  "DeleteUnusedViews", "Remove views not placed on any sheet (with protection)"),
                }));
            stack.Children.Add(SectionCardRich("Sheet number editor",
                "Bulk-edit sheet numbers. All operate on the active selection or all sheets when nothing is selected.",
                new (string,string,string)[]
                {
                    ("Reset Title",   "SheetResetTitle",     "Reset title-block instance position to (0,0)"),
                    ("+1",            "SheetNumPlus",        "Increment numeric portion of sheet numbers"),
                    ("-1",            "SheetNumMinus",       "Decrement numeric portion of sheet numbers"),
                    ("Prefix",        "SheetPrefix",         "Add prefix to sheet numbers (e.g. A-)"),
                    ("Suffix",        "SheetSuffix",         "Add suffix to sheet numbers (e.g. -R1)"),
                    ("Rem Pre",       "SheetRemovePrefix",   "Strip prefix from sheet numbers"),
                    ("Rem Suf",       "SheetRemoveSuffix",   "Strip suffix from sheet numbers"),
                    ("Find&Replace",  "SheetFindReplace",    "Open the Batch Rename dialog (sheets scope)"),
                }));
            stack.Children.Add(SectionCardRich("View automation",
                "Duplicate, rename, retemplate views in bulk.",
                new (string,string,string)[]
                {
                    ("Duplicate View",     "DuplicateView",     "Duplicate with Detailing / View-only / Dependent"),
                    ("Batch Rename",       "BatchRenameViews",  "Single-step dialog with 7 rename ops + preview"),
                    ("Copy View Settings", "CopyViewSettings",  "Copy filters, overrides and template from source view"),
                    ("Magic Rename",       "MagicRename",       "Auto-rename from title-block + level + discipline"),
                    ("View Tab Colour",    "ViewTabColour",     "Colour the Project Browser tab per discipline"),
                    ("Text Case",          "TextCase",          "Convert text notes to UPPER / lower / Title case"),
                    ("Sum Areas",          "SumAreas",          "Total area of selected / all rooms"),
                }));
            host.Content = stack;
            return host;
        }

        // Single source of truth for every title-block action surfaced
        // anywhere in the plugin. Consolidates Phase-97 commands, repair
        // utilities, sheet-context revision tools, transmittal stamps and
        // pre-export gating — so the user never has to leave this dialog
        // to operate on title blocks.
        private UIElement BuildTitleBlockTab()
        {
            var host = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(TabIntro("Title Block",
                "Single source of truth for every title-block action. " +
                "Authoring writes to TITLE_BLOCK.csv and PRJ_TB_* shared parameters. " +
                "Sheet identity / Revision / Transmittal stamp the live ViewSheet. " +
                "Suitability codes follow BS EN ISO 19650-1 §A (S0–S4 / A1–A5 / B1–B6 / AR)."));

            // ── Quick-pick reference for ISO 19650 codes ──
            var codeBody = new StackPanel();
            codeBody.Children.Add(new TextBlock {
                Text = "ISO 19650 reference (read-only) — useful for the Authoring dialogs below.",
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            codeBody.Children.Add(LabeledCombo("Discipline / Role (ISO 19650-2 §A.5)",
                Iso19650Vocabulary.DisciplineCodes, "*", _ => { },
                tooltip: "ISO single-letter codes — A/B/C/D/E/F/G/H/I/K/L/M/P/Q/S/T/W/X/Y/Z. Use * for any."));
            codeBody.Children.Add(LabeledCombo("Information-Container Type (ISO 19650-2 §A.6)",
                Iso19650Vocabulary.DocTypes, "DR", _ => { },
                tooltip: "DR = Drawing · M3 = 3D Model · SP = Specification · SC = Schedule · RP = Report · etc."));
            codeBody.Children.Add(LabeledCombo("Suitability code (BS EN ISO 19650-1 §A)",
                Iso19650Vocabulary.SuitabilityCodes, "S2", _ => { },
                tooltip: "WIP S0 · Shared S1–S7 · Published A1–A5 · Partial B1–B6 · Archive AR."));
            codeBody.Children.Add(LabeledCombo("Revision prefix",
                Iso19650Vocabulary.RevisionPrefixes, "P", _ => { },
                tooltip: "P = Preliminary · C = Construction · T = Tender · I = Information · R = Revision · A = As-built."));
            codeBody.Children.Add(LabeledCombo("RIBA stage",
                Iso19650Vocabulary.RibaStages, "*", _ => { },
                tooltip: "RIBA Plan of Work 2020 stages 0–7."));
            codeBody.Children.Add(LabeledCombo("Authority",
                Iso19650Vocabulary.AuthorityCodes, "", _ => { },
                tooltip: "KCCA / ERA / NEMA (Uganda) · BC / PA / EA (UK). Empty = non-submission."));
            stack.Children.Add(Card("ISO 19650 code legend", codeBody));

            // ── Authoring (Phase 97 STING TB System v1.0) ──
            stack.Children.Add(SectionCardRich("Authoring (Phase 97 — TITLE_BLOCK.csv)",
                "Edit per-discipline defaults, populate every sheet, validate completeness, swap variants, bind legends, and gate pre-export.",
                new (string,string,string)[]
                {
                    ("Edit CSV…",   "TitleBlockEditCsv",   "Open the in-Revit DataGrid for TITLE_BLOCK.csv (no external editor needed)"),
                    ("Populate",    "TitleBlockPopulate",  "Bulk-write PRJ_TB_* values from TITLE_BLOCK.csv to every sheet"),
                    ("Validate",    "TitleBlockValidate",  "Audit completeness — missing fields, invalid suitability codes, stale syncs"),
                    ("Set Variant", "TitleBlockSetVariant","Auto-swap STING_TB_* family by paper size + viewport aspect"),
                    ("Legend Bind", "DisciplineLegendBind","Place LGD-{DISC}-NOTES legend view into title-block notes region"),
                    ("Pre-Export",  "PreExportValidate",   "Pre-export completeness gate — blocks PDF/DWF when critical fields are empty"),
                }));

            // ── Sheet identity (counts, transmittals, swap) ──
            stack.Children.Add(SectionCard("Sheet identity & transmittal", new (string,string)[]
            {
                ("Count Sheets",   "SheetCountAutoUpdate"),
                ("Stamp TX",       "TransmittalAutoIssue"),
                ("Transmittal",    "Transmittal"),
                ("Swap Title Block","SwapTitleBlock"),
                ("Set Variant",    "TitleBlockSetVariant"),
            }));

            // ── Revision tools that stamp the title block ──
            stack.Children.Add(SectionCard("Revision (writes to PRJ_TB_REVISION_*)", new (string,string)[]
            {
                ("Revision Sync",        "RevisionSync"),
                ("Auto Rev Cloud",       "RevisionCloudAuto"),
                ("Show Clouds",          "RevShowClouds"),
                ("Show Tags",            "RevShowTags"),
                ("Rev Naming Check",     "RevisionNamingEnforce"),
                ("Rev Tag Integration",  "RevisionTagIntegration"),
                ("Rev Schedule",         "RevisionSchedule"),
                ("Rev Dashboard",        "RevisionDashboard"),
                ("Rev Compare",          "RevisionCompare"),
                ("Rev Export",           "RevisionExport"),
            }));

            // ── Repair / first-aid ──
            stack.Children.Add(SectionCard("Repair & first-aid", new (string,string)[]
            {
                ("Reset Position",   "TitleBlockReset"),
                ("Rescue",           "TitleBlockRescue"),
                ("Transmittal Gate", "TransmittalGateCheck"),
            }));

            // ── Author kit (file map + nested-family pointers) ──
            stack.Children.Add(InfoCard(
                "Author kit (15 .rfa families): COVER_A3 · STARTUP_A3 · ASSEMBLY_PIPE · " +
                "ASSEMBLY_DUCT · ASSEMBLY_COND · ASSEMBLY_HANGER · TECHNICAL_A1 · CLIENT_A1 · " +
                "IFC_A1 · IFT_A1 · AS_BUILT_A1 · SUBMISSION_KCCA · SUBMISSION_ERA · " +
                "SUBMISSION_NEMA · MARKETING_A2.\n\n" +
                "Each family carries 37 TB_ family parameters (drawable zone, slots, reserved " +
                "regions, nested-family pointers, visibility toggles, authority code, version " +
                "stamp). The project carries 37 PRJ_TB_ project-info parameters that the kit " +
                "auto-fills.\n\n" +
                "Step-by-step layman guide → docs/guides/TITLE_BLOCK_CREATION_GUIDE.md\n" +
                "Per-discipline default values → StingTools/Data/TITLE_BLOCK.csv\n" +
                "Visual style packs (corp-base, fabrication-shop, technical-presentation, " +
                "client-presentation, construction-issue, tender-issue, as-built, " +
                "authority-submission, marketing-render) → StingTools/Data/STING_VIEW_STYLE_PACKS.json"));

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

        // Rich variant — adds a one-line description below the title
        // and per-button tooltips.
        private UIElement SectionCardRich(string title, string description,
            (string label, string tag, string tip)[] buttons)
        {
            var body = new StackPanel();
            if (!string.IsNullOrEmpty(description))
                body.Children.Add(new TextBlock {
                    Text = description, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(SubtleColor), FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4) });
            var wrap = new WrapPanel();
            foreach (var (label, tag, tip) in buttons)
            {
                var b = MakeActionBtn(label, tag);
                if (!string.IsNullOrEmpty(tip)) b.ToolTip = tip;
                wrap.Children.Add(b);
            }
            body.Children.Add(wrap);
            return Card(title, body);
        }

        // Tab intro banner — accent-coloured title + subtle description.
        private UIElement TabIntro(string title, string body)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            stack.Children.Add(new TextBlock {
                Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14,
                Foreground = new SolidColorBrush(AccentColor),
                Margin = new Thickness(0, 0, 0, 2) });
            stack.Children.Add(new TextBlock {
                Text = body, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11 });
            return stack;
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
                try
                {
                    // Phase 137 — surface dispatch failures so users aren't left
                    // wondering why a button does nothing. The most common cause
                    // (the editor was opened modally, blocking ExternalEvent) is
                    // fixed by launching modeless in DrawingTypeEditorCommand,
                    // but if the dock panel isn't initialised yet we still
                    // explain it.
                    bool ok = StingDockPanel.DispatchCommand(tag);
                    if (!ok)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("STING — Drawing Type Editor",
                            $"Could not dispatch command '{tag}'.\n\n" +
                            "Open the STING dock panel once before launching the editor — " +
                            "the dock panel registers the external event handler that runs " +
                            "tagged commands. If the panel is already open, your Revit session " +
                            "may have a pending modal dialog blocking the event queue; close " +
                            "it and try again.");
                    }
                }
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
            _formHost.Children.Add(BuildTokenProfileCard());   // Phase 135
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
            body.Children.Add(LabeledCombo("Purpose",
                Iso19650Vocabulary.DrawingPurposes,
                _current.Purpose, v => _current.Purpose = v));
            body.Children.Add(LabeledCombo("Discipline (ISO 19650-2 §A.5)",
                Iso19650Vocabulary.DisciplineCodes,
                _current.Discipline, v => _current.Discipline = v,
                tooltip: "ISO 19650-2 / BS 1192 single-letter role codes — A/B/C/D/E/F/G/H/I/K/L/M/P/Q/S/T/W/X/Y/Z. Use * for any."));
            body.Children.Add(LabeledCombo("Phase / RIBA stage",
                Iso19650Vocabulary.RibaStages,
                _current.Phase, v => _current.Phase = v,
                tooltip: "RIBA Plan of Work 2020 stage 0–7. Use * for any stage."));
            body.Children.Add(LabeledTextBlock("Origin", _current.Origin ?? "project"));

            // Phase 136 — surface View style pack as a top-level identity field
            // (a Drawing Type's most important visual binding). A dedicated
            // BuildViewStylePackPicker() returns label + dropdown + edit link
            // as one row group so it's discoverable here AND callable from the
            // Views card if needed.
            body.Children.Add(BuildViewStylePackPicker());
            return Card("Identity", body);
        }

        // Phase 136 — small accent-coloured hyperlink used inside the pack
        // picker hint row. ToolTip optional; clicks fire onClick.
        private TextBlock MakePackInlineLink(string text, string tooltip, Action onClick)
        {
            var t = new TextBlock
            {
                Text = "  " + text,
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 11,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = tooltip,
            };
            t.MouseLeftButtonUp += (s, e) => onClick?.Invoke();
            return t;
        }

        // Phase 136 — push the active DrawingType's ViewTemplateName up to
        // its bound pack (sets pack.ViewTemplate). Useful when the user
        // has authored a template they want adopted as the pack default.
        private void ActionPushTypeTemplateToPack()
        {
            if (_current == null) return;
            if (string.IsNullOrWhiteSpace(_current.ViewStylePackId))
            {
                System.Windows.MessageBox.Show(
                    "Bind this Drawing Type to a View Style Pack first.",
                    "STING — push template", MessageBoxButton.OK);
                return;
            }
            if (string.IsNullOrWhiteSpace(_current.ViewTemplateName))
            {
                System.Windows.MessageBox.Show(
                    "This Drawing Type has no ViewTemplateName set — nothing to push.",
                    "STING — push template", MessageBoxButton.OK);
                return;
            }
            var pack = _packs.FirstOrDefault(p =>
                string.Equals(p.Id, _current.ViewStylePackId, StringComparison.OrdinalIgnoreCase));
            if (pack == null)
            {
                System.Windows.MessageBox.Show(
                    $"Bound pack '{_current.ViewStylePackId}' not found in the editor's pack list.",
                    "STING — push template", MessageBoxButton.OK);
                return;
            }
            var prev = pack.ViewTemplate ?? "(unset)";
            var resp = System.Windows.MessageBox.Show(
                $"Set pack '{pack.Id}'.ViewTemplate = '{_current.ViewTemplateName}'?\n\n" +
                $"Previous pack value: {prev}\n\n" +
                "Every Drawing Type bound to this pack that doesn't override its own template " +
                "will fall through to the new pack value.",
                "STING — push template to pack", MessageBoxButton.YesNo);
            if (resp != MessageBoxResult.Yes) return;

            pack.ViewTemplate = _current.ViewTemplateName;
            if (!string.Equals(pack.Origin, "project", StringComparison.OrdinalIgnoreCase))
                pack.Origin = "project";
            System.Windows.MessageBox.Show(
                $"Updated pack '{pack.Id}'.\nSave to persist to <project>/_BIM_COORD/view_style_packs.json.",
                "STING — push template", MessageBoxButton.OK);
        }

        // Dedicated, prominent View style pack chooser. Non-editable
        // dropdown listing every loaded pack ("(none)" + every entry from
        // STING_VIEW_STYLE_PACKS.json — corporate baseline + project
        // override + new packs created in the same session). Includes a
        // "→ Edit selected pack" link that jumps to the View Style Packs
        // tab and highlights the active pack.
        private UIElement BuildViewStylePackPicker()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

            // Row 1 — label + ComboBox (non-editable for unmistakable UX).
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0), LastChildFill = true };
            var lbl = new TextBlock {
                Text = "View style pack",
                Width = 200,
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Picks a ViewStylePack from STING_VIEW_STYLE_PACKS.json. " +
                          "The pack supplies VG overrides, filter rules, text/dim style and tag defaults.",
            };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);

            var combo = new ComboBox { Height = 22, IsEditable = false };
            DarkDialogTheme.StyleInput(combo, CardBg, FgColor, CardBorder);
            combo.Items.Add("(none)");
            foreach (var p in _packs ?? new List<ViewStylePack>())
                combo.Items.Add($"{p.Id}  —  {p.Name ?? p.Id}");

            // Initial selection — match by id prefix; fall back to (none).
            string initial = "(none)";
            if (!string.IsNullOrEmpty(_current.ViewStylePackId))
            {
                foreach (var item in combo.Items)
                {
                    if (item is string s &&
                        s.StartsWith(_current.ViewStylePackId + "  —  ", StringComparison.OrdinalIgnoreCase))
                    { initial = s; break; }
                }
                if (initial == "(none)") initial = _current.ViewStylePackId;
            }
            combo.SelectedItem = initial;
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is string ss)
                {
                    if (ss == "(none)") { _current.ViewStylePackId = null; return; }
                    var idx = ss.IndexOf("  —  ", StringComparison.Ordinal);
                    _current.ViewStylePackId = idx > 0 ? ss.Substring(0, idx).Trim() : ss.Trim();
                }
            };
            row.Children.Add(combo);
            stack.Children.Add(row);

            // Row 2 — count strip + "Edit selected pack" hyperlink.
            var hint = new DockPanel { Margin = new Thickness(204, 2, 4, 4), LastChildFill = true };
            var count = new TextBlock {
                Text = $"{(_packs?.Count ?? 0)} pack(s) loaded · select one to bind",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(count, Dock.Left);
            hint.Children.Add(count);

            // Right-side link group — three actions, each space-separated.
            var linkRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            linkRow.Children.Add(MakePackInlineLink("Use pack template",
                "Clear my ViewTemplateName so I inherit from the pack's fallback.",
                () =>
                {
                    if (string.IsNullOrEmpty(_current.ViewStylePackId)) return;
                    _current.ViewTemplateName = null;
                    if (!string.Equals(_current.Origin, "project", StringComparison.OrdinalIgnoreCase))
                        _current.Origin = "project";
                    RenderForm();   // refresh to show the cleared field
                }));
            linkRow.Children.Add(MakePackInlineLink("↑ Push to pack",
                "Set the bound pack's ViewTemplate = this DrawingType's ViewTemplateName.",
                () => ActionPushTypeTemplateToPack()));
            linkRow.Children.Add(MakePackInlineLink("→ Edit pack", null,
                () =>
                {
                    if (string.IsNullOrEmpty(_current.ViewStylePackId)) return;
                    var target = (_packs ?? new List<ViewStylePack>()).FirstOrDefault(p =>
                        string.Equals(p.Id, _current.ViewStylePackId, StringComparison.OrdinalIgnoreCase));
                    if (target == null) return;
                    _rootTabs.SelectedIndex = 1;
                    SelectPack(target);
                }));
            DockPanel.SetDock(linkRow, Dock.Right);
            hint.Children.Add(linkRow);
            stack.Children.Add(hint);

            return stack;
        }

        private UIElement BuildSheetCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledCombo("Paper size (ISO 216)",
                Iso19650Vocabulary.PaperSizes,
                _current.PaperSize, v => _current.PaperSize = v,
                tooltip: "A0 = 841×1189 · A1 = 594×841 · A2 = 420×594 · A3 = 297×420 · A4 = 210×297 mm."));
            // Title-block family — pulled from the active project so users
            // pick from what is actually loaded, eliminating typo errors.
            body.Children.Add(LabeledProjectAssetCombo("Title block family",
                _current.TitleBlockFamily, v => _current.TitleBlockFamily = v,
                ProjectAssetPicker.TitleBlockFamilyTypes(_doc),
                "Family Symbols of category OST_TitleBlocks loaded in the active project."));
            body.Children.Add(LabeledCombo("Orientation",
                Iso19650Vocabulary.Orientations,
                _current.Orientation, v => _current.Orientation = v));
            return Card("Sheet", body);
        }

        private UIElement BuildViewCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledNumber("Scale (1:N)", _current.Scale, v => _current.Scale = v));
            body.Children.Add(LabeledCombo("Detail level",
                Iso19650Vocabulary.DetailLevels,
                _current.DetailLevel, v => _current.DetailLevel = v));
            body.Children.Add(LabeledProjectAssetCombo("View template name (overrides pack)",
                _current.ViewTemplateName, v => _current.ViewTemplateName = v,
                ProjectAssetPicker.ViewTemplateNames(_doc),
                "View templates (View.IsTemplate = true) in the active project. " +
                "When set, this OVERRIDES the View Style Pack's template fallback. " +
                "Leave blank to inherit from the pack."));
            body.Children.Add(LabeledProjectAssetCombo("Viewport type name",
                _current.ViewportTypeName, v => _current.ViewportTypeName = v,
                ProjectAssetPicker.ViewportTypeNames(_doc),
                "ElementType where Category = OST_Viewports in the active project."));
            return Card("Views", body);
        }

        private UIElement BuildNumberingCard()
        {
            var body = new StackPanel();
            body.Children.Add(LabeledCombo("Sheet number pattern",
                Iso19650Vocabulary.SheetNumberPatterns,
                _current.SheetNumberPattern, v => _current.SheetNumberPattern = v,
                tooltip: "Standard ISO 19650 / BS 1192 patterns. Tokens: {prj} {orig} {vol} {lvl} {role} " +
                         "{spool} {disc} {discipline} {sys} {mark} {seq} {seq:D2..4}."));
            body.Children.Add(LabeledCombo("Sheet name pattern",
                Iso19650Vocabulary.SheetNamePatterns,
                _current.SheetNamePattern, v => _current.SheetNamePattern = v,
                tooltip: "Standard sheet name templates. Same token set as sheet number."));
            return Card("Numbering (ISO 19650-2 §A.6)", body);
        }

        private UIElement BuildCropCard()
        {
            var body = new StackPanel();
            _current.Crop = _current.Crop ?? new DrawingCropStrategy();
            body.Children.Add(LabeledCombo("Kind",
                Iso19650Vocabulary.CropKinds,
                _current.Crop.Kind, v => _current.Crop.Kind = v));
            body.Children.Add(LabeledProjectAssetCombo("Scope box name (when Kind=ScopeBox)",
                _current.Crop.ScopeBoxName, v => _current.Crop.ScopeBoxName = v,
                ProjectAssetPicker.ScopeBoxNames(_doc),
                "Scope boxes (OST_VolumeOfInterest) in the active project."));
            body.Children.Add(LabeledDouble("Margin (mm)",
                _current.Crop.MarginMm, v => _current.Crop.MarginMm = v));
            return Card("Crop strategy", body);
        }

        private UIElement BuildSectionMarkerCard()
        {
            var body = new StackPanel();
            _current.SectionMarker = _current.SectionMarker ?? new SectionMarkerSpec();
            body.Children.Add(LabeledProjectAssetCombo("Family",
                _current.SectionMarker.Family, v => _current.SectionMarker.Family = v,
                ProjectAssetPicker.SectionMarkerFamilies(_doc),
                "Section / elevation / callout marker families loaded in the project."));
            body.Children.Add(LabeledTextBox("Mark prefix",
                _current.SectionMarker.MarkPrefix, v => _current.SectionMarker.MarkPrefix = v));
            body.Children.Add(LabeledCombo("Bubble style",
                Iso19650Vocabulary.BubbleStyles,
                _current.SectionMarker.BubbleStyle, v => _current.SectionMarker.BubbleStyle = v));
            body.Children.Add(LabeledDouble("Far clip (mm)",
                _current.SectionMarker.FarClipMm, v => _current.SectionMarker.FarClipMm = v));
            return Card("Section / elevation marker", body);
        }

        private UIElement BuildAnnotationCard()
        {
            var pack = _current.Annotation = _current.Annotation ?? new AnnotationRulePack();
            // Fold legacy autoDim/autoTag booleans into the new Rules
            // collection on first edit so any legacy save persists as
            // Rules entries rather than the old flat flags.
            pack.MigrateFromLegacy();

            var body = new StackPanel();

            // ── Automation rule pack ──
            body.Children.Add(BuildAnnotationRulesGrid(pack));

            body.Children.Add(LabeledCombo("Dimension strategy",
                Iso19650Vocabulary.DimensionStrategies,
                pack.DimensionStrategy, v => pack.DimensionStrategy = v));
            body.Children.Add(LabeledProjectAssetCombo("Dimension style name",
                pack.DimensionStyle, v => pack.DimensionStyle = v,
                Merge(ProjectAssetPicker.DimensionStyleNames(_doc), CommonStingDimensionStyles),
                "DimensionType elements in the active project, plus STING corporate dimension styles."));
            body.Children.Add(LabeledNullableNumber("Dense until scale (1:N)",
                pack.DenseUntilScale,  v => pack.DenseUntilScale = v,
                tooltip: "View scale ≤ this value → full annotation. Coarser → grid dims only. Empty = always full."));

            // ── Tag families + per-category Depth (Change 4 + 5) ──
            body.Children.Add(BuildTagFamiliesGrid(pack));

            return Card("Annotation rule pack", body);
        }

        // Phase 135 — Token Depth & Style card.
        // Drives the AnnotationTokenProfile fields: presentation mode,
        // global + per-category paragraph depth, TAG7 section visibility,
        // tag size/style/colour preset, view-level colour scheme, segment
        // mask, display mode. Empty ⇒ inherit from ViewStylePack pack-level
        // defaults (defaultTagStyle / categoryTagStyles / tagColorScheme).
        private UIElement BuildTokenProfileCard()
        {
            var tp = _current.TokenProfile = _current.TokenProfile ?? new AnnotationTokenProfile();
            var body = new StackPanel();

            string[] presetModes = new[] { "", "Compact", "Technical", "FullSpec", "Presentation", "BOQ" };
            body.Children.Add(LabeledCombo("Presentation mode preset",
                presetModes, tp.PresentationMode ?? "",
                v => tp.PresentationMode = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                tooltip: "Sets PARA_STATE_1/2/3 + WARN_VISIBLE in one shot. Empty = use Para-depth slider below."));

            // Phase 137 — typo-prevention: Global paragraph depth is now a
            // closed dropdown 1..10 (was a free-form NullableNumber).
            string[] depths = new[] { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
            body.Children.Add(LabeledCombo("Global paragraph depth (1-10)",
                depths, tp.ParaDepth?.ToString() ?? "",
                v => tp.ParaDepth = int.TryParse(v, out var n) ? (int?)n : null,
                tooltip: "Tier 1 = compact, 10 = full audit. Empty = inherit from preset / leave alone."));

            string[] sizes  = new[] { "", "2", "2.5", "3", "3.5" };
            string[] styles = new[] { "", "NOM", "BOLD", "ITALIC", "BOLDITALIC" };
            string[] colors = new[] { "", "BLACK", "BLUE", "GREEN", "RED", "ORANGE", "GREY", "PURPLE", "YELLOW" };

            body.Children.Add(LabeledCombo("Tag size",
                sizes, tp.TagSize ?? "",
                v => tp.TagSize = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                tooltip: "Empty = inherit from pack DefaultTagStyle. mm text height."));
            body.Children.Add(LabeledCombo("Tag style",
                styles, tp.TagStyle ?? "",
                v => tp.TagStyle = string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant()));
            body.Children.Add(LabeledCombo("Tag colour",
                colors, tp.TagColor ?? "",
                v => tp.TagColor = string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant()));

            string[] schemes = new[] { "", "Discipline", "System", "Status", "Zone", "Level", "Location", "Function",
                                        "Warm", "Cool", "Red", "Yellow", "Blue", "Mono", "Dark" };
            body.Children.Add(LabeledCombo("View colour scheme (STING_VIEW_TAG_STYLE)",
                schemes, tp.ColorScheme ?? "",
                v => tp.ColorScheme = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                tooltip: "Variable-driven colour map written to STING_VIEW_TAG_STYLE on the view."));

            // Phase 137 — Segment mask: 8 individual checkboxes (one per
            // DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ token). Replaces the
            // previous free-form TextBox so users can't type "0010000A"
            // or transpose digits.
            body.Children.Add(BuildSegmentMaskRow(tp));

            // Phase 137 — Display mode: closed dropdown of the five named
            // modes so users can't type "6" or non-numeric text.
            string[] displayModes = new[]
            {
                "",                              // empty = no override
                "1 — SEQ",
                "2 — PROD-SEQ",
                "3 — DISC-SYS-SEQ",
                "4 — DISC-PROD-SEQ",
                "5 — Full 8-segment",
                "6 — TAG7 narrative (client prose)",
            };
            body.Children.Add(LabeledCombo("Display mode",
                displayModes,
                tp.DisplayMode.HasValue ? displayModes.FirstOrDefault(s => s.StartsWith(tp.DisplayMode.Value + " ")) ?? "" : "",
                v => tp.DisplayMode = (!string.IsNullOrEmpty(v) && int.TryParse(v.Split(' ')[0], out var n)) ? (int?)n : null,
                tooltip: "1=SEQ, 2=PROD-SEQ, 3=DISC-SYS-SEQ, 4=DISC-PROD-SEQ,\n" +
                         "5=Full 8-segment, 6=TAG7 plain-language narrative.\n\n" +
                         "Mode 6 reads the rich TAG7 narrative composed by WriteTag7All —\n" +
                         "ideal for client-facing presentation drawings where prose reads\n" +
                         "better than the technical 8-segment tag."));

            // Phase 165 — pattern mode (HANDOVER / DC / CUSTOM) for T4-T10 payload.
            // Empty = inherit project / type defaults (which are DC unless overridden).
            string[] patternModes = new[] { "", "DC", "HANDOVER", "CUSTOM" };
            body.Children.Add(LabeledCombo("T4-T10 pattern mode",
                patternModes,
                tp.PatternMode ?? "",
                v => tp.PatternMode = string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant(),
                tooltip: "Selects which T4-T10 payload pack renders.\n" +
                         "  DC       — design & construction payload (default project mode)\n" +
                         "  HANDOVER — post-construction handover payload\n" +
                         "  CUSTOM   — project-specific payload\n" +
                         "Empty = leave whatever the type already has set."));

            body.Children.Add(BuildSectionVisibilityGrid(tp));
            body.Children.Add(BuildTierVisibilityGrid(tp));
            body.Children.Add(BuildCategoryDepthsGrid(tp));

            // Summary / collapse line
            var summary = new TextBlock {
                Text = SummariseTokenProfile(tp),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 6, 0, 0),
            };
            body.Children.Add(summary);

            return Card("Token Depth & Style (Phase 135)", body);
        }

        /// <summary>
        /// Phase 137 — segment-mask author UX: eight checkboxes labelled
        /// DISC / LOC / ZONE / LVL / SYS / FUNC / PROD / SEQ instead of a
        /// free-form 8-char TextBox. Mask string is materialised on every
        /// change so the on-disk JSON stays the same shape; no chance of
        /// typos like "11I00001" or transposed digits.
        /// </summary>
        private UIElement BuildSegmentMaskRow(AnnotationTokenProfile tp)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock {
                Text = "Segment mask (DISC LOC ZONE LVL SYS FUNC PROD SEQ)",
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11,
                Margin = new Thickness(0, 6, 0, 2) });
            string mask = (tp.SegmentMask ?? "").PadRight(8, '1').Substring(0, 8);
            string[] tokens = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var cbs = new System.Windows.Controls.CheckBox[8];
            for (int i = 0; i < 8; i++)
            {
                int idx = i;
                cbs[i] = new System.Windows.Controls.CheckBox
                {
                    Content = tokens[i],
                    IsChecked = mask[i] == '1',
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = $"Token {idx + 1} ({tokens[idx]}) on/off in the displayed tag.",
                };
                cbs[idx].Checked   += (s, e) => UpdateMask(cbs, tp);
                cbs[idx].Unchecked += (s, e) => UpdateMask(cbs, tp);
                row.Children.Add(cbs[i]);
            }
            sp.Children.Add(row);
            return sp;
        }

        private static void UpdateMask(System.Windows.Controls.CheckBox[] cbs, AnnotationTokenProfile tp)
        {
            var sb = new System.Text.StringBuilder(8);
            foreach (var cb in cbs) sb.Append(cb.IsChecked == true ? '1' : '0');
            var s = sb.ToString();
            tp.SegmentMask = s == "11111111" ? null : s;   // null = no override
        }

        private UIElement BuildSectionVisibilityGrid(AnnotationTokenProfile tp)
        {
            tp.SectionVisibility = tp.SectionVisibility ?? new Dictionary<string, bool>();
            var host = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            host.Children.Add(new TextBlock {
                Text = "TAG7 section visibility — A: Identity · B: System · C: Spatial · D: Lifecycle · E: Technical · F: Classification",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4) });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            string[] keys = { "A", "B", "C", "D", "E", "F" };
            foreach (var k in keys)
            {
                bool cur = tp.SectionVisibility.TryGetValue(k, out var b) && b;
                var cb = new CheckBox {
                    Content = k, IsChecked = cur, Margin = new Thickness(0, 0, 12, 0),
                    Foreground = new SolidColorBrush(FgColor),
                };
                cb.Checked   += (s, e) => tp.SectionVisibility[k] = true;
                cb.Unchecked += (s, e) => tp.SectionVisibility[k] = false;
                row.Children.Add(cb);
            }
            host.Children.Add(row);
            return host;
        }

        // Phase 165 — T4-T10 tier visibility checkboxes. Reuses the same
        // SectionVisibility dictionary as A-F but with keys "T4".."T10".
        // TokenProfileApplier writes these to TAG_PARA_STATE_4..10_BOOL on
        // the element types referenced by the view, which gates the T4-T10
        // payload appends in WriteTag7All. Pair this card with the
        // "T4-T10 pattern mode" dropdown above to choose which payload set
        // (HANDOVER / DC / CUSTOM) is active for the enabled tiers.
        private UIElement BuildTierVisibilityGrid(AnnotationTokenProfile tp)
        {
            tp.SectionVisibility = tp.SectionVisibility ?? new Dictionary<string, bool>();
            var host = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            host.Children.Add(new TextBlock {
                Text = "TAG7 tier 4-10 visibility — T4: Commissioning · T5: Cost · T6: Carbon · " +
                       "T7: Fabrication · T8: Clash · T9: As-built · T10: Compliance",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4) });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            string[] tierKeys = { "T4", "T5", "T6", "T7", "T8", "T9", "T10" };
            foreach (var k in tierKeys)
            {
                bool cur = tp.SectionVisibility.TryGetValue(k, out var b) && b;
                var cb = new CheckBox {
                    Content = k, IsChecked = cur, Margin = new Thickness(0, 0, 10, 0),
                    Foreground = new SolidColorBrush(FgColor),
                    ToolTip = $"Enable {k} payload row. Requires PatternMode (above) to choose which T4-T10 pack populates the value."
                };
                cb.Checked   += (s, e) => tp.SectionVisibility[k] = true;
                cb.Unchecked += (s, e) => tp.SectionVisibility[k] = false;
                row.Children.Add(cb);
            }
            host.Children.Add(row);
            return host;
        }

        private static readonly GridLength[] _catDepthCols =
        {
            new GridLength(220),
            new GridLength(60),
            new GridLength(24),
        };

        private UIElement BuildCategoryDepthsGrid(AnnotationTokenProfile tp)
        {
            tp.CategoryDepths = tp.CategoryDepths ?? new Dictionary<string, int>();
            var host = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            host.Children.Add(new TextBlock {
                Text = "Per-category paragraph depth (overrides global ParaDepth above for that category only).",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4) });

            // Header
            var header = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _catDepthCols.Length; i++)
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = _catDepthCols[i] });
            string[] hd = { "Category", "Depth", "" };
            for (int i = 0; i < hd.Length; i++)
            {
                var t = new TextBlock { Text = hd[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
                Grid.SetColumn(t, i); header.Children.Add(t);
            }
            host.Children.Add(header);

            var cats = Merge(ProjectAssetPicker.TaggableCategoryNames(_doc),
                             KnownTaggableCategories).ToArray();

            foreach (var kv in tp.CategoryDepths.ToList())
            {
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                for (int i = 0; i < _catDepthCols.Length; i++)
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = _catDepthCols[i] });

                string catKey = kv.Key;
                var keyBox = SmallCombo(catKey, newKey =>
                {
                    newKey = (newKey ?? "").Trim();
                    if (string.IsNullOrEmpty(newKey) || newKey == catKey) return;
                    if (!tp.CategoryDepths.ContainsKey(catKey)) return;
                    var existing = tp.CategoryDepths[catKey];
                    tp.CategoryDepths.Remove(catKey);
                    tp.CategoryDepths[newKey] = existing;
                }, cats);

                int curDepth = kv.Value;
                var depth = SmallCombo(curDepth.ToString(), v =>
                {
                    if (int.TryParse(v, out var n) && tp.CategoryDepths.ContainsKey(catKey))
                        tp.CategoryDepths[catKey] = n;
                }, new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" });
                depth.IsEditable = false;

                var rm = MakeSmallBtn("×", () => { tp.CategoryDepths.Remove(catKey); RenderForm(); });
                rm.Width = 22;

                var ctrls = new UIElement[] { keyBox, depth, rm };
                for (int i = 0; i < ctrls.Length; i++)
                {
                    Grid.SetColumn(ctrls[i], i);
                    if (ctrls[i] is FrameworkElement fe)
                        fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                    g.Children.Add(ctrls[i]);
                }
                host.Children.Add(g);
            }

            host.Children.Add(MakeSmallBtn("＋ Add category depth", () =>
            {
                var key = "NewCategory" + tp.CategoryDepths.Count;
                tp.CategoryDepths[key] = 5;
                RenderForm();
            }));
            return host;
        }

        private static string SummariseTokenProfile(AnnotationTokenProfile tp)
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(tp.PresentationMode)) bits.Add($"mode:{tp.PresentationMode}");
            if (tp.ParaDepth.HasValue) bits.Add($"depth:{tp.ParaDepth.Value}");
            if (!string.IsNullOrWhiteSpace(tp.TagSize) || !string.IsNullOrWhiteSpace(tp.TagStyle) || !string.IsNullOrWhiteSpace(tp.TagColor))
                bits.Add($"tag:{tp.TagSize ?? "·"}{tp.TagStyle ?? "·"}_{tp.TagColor ?? "·"}");
            if (!string.IsNullOrWhiteSpace(tp.ColorScheme)) bits.Add($"scheme:{tp.ColorScheme}");
            if (!string.IsNullOrWhiteSpace(tp.SegmentMask)) bits.Add($"mask:{tp.SegmentMask}");
            if (tp.DisplayMode.HasValue) bits.Add($"disp:{tp.DisplayMode.Value}");
            if (!string.IsNullOrWhiteSpace(tp.PatternMode)) bits.Add($"pattern:{tp.PatternMode}");
            if (tp.SectionVisibility != null && tp.SectionVisibility.Count > 0)
            {
                // A-F (single-letter) and T4..T10 (two-three char) summarised separately.
                var letters = tp.SectionVisibility.Where(k => k.Key.Length == 1).ToList();
                var tiers   = tp.SectionVisibility
                    .Where(k => k.Key.StartsWith("T", StringComparison.OrdinalIgnoreCase) && k.Key.Length > 1)
                    .ToList();
                if (letters.Count > 0)
                    bits.Add("sect:" + string.Join("",
                        letters.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                               .Select(k => k.Value ? k.Key.ToUpperInvariant() : k.Key.ToLowerInvariant())));
                if (tiers.Count > 0)
                    bits.Add("tiers:" + string.Join(",",
                        tiers.Where(k => k.Value)
                             .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                             .Select(k => k.Key.ToUpperInvariant())));
            }
            if (tp.CategoryDepths != null && tp.CategoryDepths.Count > 0)
                bits.Add($"cats:{tp.CategoryDepths.Count}");
            return bits.Count == 0
                ? "Inherits all values from ViewStylePack pack-level defaults."
                : "Active: " + string.Join(" · ", bits);
        }

        // Compact grid editor for AnnotationRulePack.Rules (Change 6c).
        // Header: ✓ Enabled · Category · Rule type · Tag family · Depth · ×
        private UIElement BuildAnnotationRulesGrid(AnnotationRulePack pack)
        {
            pack.Rules = pack.Rules ?? new List<AutoAnnotationRule>();
            var host = new StackPanel();

            host.Children.Add(new TextBlock {
                Text = "Automation rules — one row per (category, rule type) pair the annotation pass should fire.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4) });

            host.Children.Add(MakeRuleHeader());
            foreach (var rule in pack.Rules.ToList())
                host.Children.Add(MakeRuleRow(pack, rule));

            host.Children.Add(MakeSmallBtn("＋ Add rule", () =>
            {
                pack.Rules.Add(new AutoAnnotationRule {
                    Category = "Rooms", RuleType = "AutoTag", Enabled = true });
                RenderForm();
            }));
            return host;
        }

        // Column geometry shared by the rules header + each row so cells
        // align: Enabled 40 · Category 170 · RuleType 160 · TagFamily * · Depth 50 · × 24.
        private static readonly GridLength[] _ruleCols =
        {
            new GridLength(40),
            new GridLength(170),
            new GridLength(160),
            new GridLength(1, GridUnitType.Star),
            new GridLength(50),
            new GridLength(24),
        };

        private UIElement MakeRuleHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _ruleCols.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = _ruleCols[i] });
            string[] headers = { "✓", "Category", "Rule type", "Tag family", "Depth", "" };
            for (int i = 0; i < headers.Length; i++)
            {
                var t = new TextBlock {
                    Text = headers[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                };
                Grid.SetColumn(t, i); g.Children.Add(t);
            }
            return g;
        }

        private UIElement MakeRuleRow(AnnotationRulePack pack, AutoAnnotationRule rule)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            for (int i = 0; i < _ruleCols.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = _ruleCols[i] });

            var cats   = Merge(ProjectAssetPicker.TaggableCategoryNames(_doc),
                               KnownTaggableCategories).ToArray();
            var fams   = Merge(ProjectAssetPicker.TagFamilyNames(_doc),
                               Iso19650Vocabulary.CommonTagFamilies).ToArray();

            var enabled = MakeChk(rule.Enabled, b => rule.Enabled = b);
            var cat     = SmallCombo(rule.Category, v => rule.Category = v, cats);
            var rt      = SmallCombo(rule.RuleType, v => rule.RuleType = v, KnownRuleTypes);
            var fam     = SmallCombo(rule.TagFamily ?? "", v =>
                rule.TagFamily = string.IsNullOrWhiteSpace(v) ? null : v.Trim(), fams);
            var depth   = SmallCombo(rule.Depth?.ToString() ?? "", v =>
            {
                if (string.IsNullOrWhiteSpace(v)) rule.Depth = null;
                else if (int.TryParse(v, out var n)) rule.Depth = n;
            }, new[] { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" });
            depth.IsEditable = false;
            var rm = MakeSmallBtn("×", () => { pack.Rules.Remove(rule); RenderForm(); });
            rm.Width = 22;

            var ctrls = new UIElement[] { enabled, cat, rt, fam, depth, rm };
            for (int i = 0; i < ctrls.Length; i++)
            {
                Grid.SetColumn(ctrls[i], i);
                if (ctrls[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                g.Children.Add(ctrls[i]);
            }
            return g;
        }

        // Tag families grid with Category dropdown + Family combo + Depth.
        // Columns: Category 180 · Family 1* · Depth 50 · × 24.
        private static readonly GridLength[] _tagFamCols =
        {
            new GridLength(180),
            new GridLength(1, GridUnitType.Star),
            new GridLength(50),
            new GridLength(24),
        };

        private UIElement BuildTagFamiliesGrid(AnnotationRulePack pack)
        {
            pack.TagFamilies = pack.TagFamilies ?? new Dictionary<string, string>();
            pack.TagDepths   = pack.TagDepths   ?? new Dictionary<string, int>();

            var host = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            host.Children.Add(new TextBlock {
                Text = "Tag families (Category → Family name → Depth)",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });

            host.Children.Add(MakeTagFamHeader());
            foreach (var kv in pack.TagFamilies.ToList())
                host.Children.Add(MakeTagFamRow(pack, kv.Key, kv.Value));

            host.Children.Add(MakeSmallBtn("＋ Add category mapping", () =>
            {
                var key = "NewCategory" + pack.TagFamilies.Count;
                pack.TagFamilies[key] = "STING_TAG_FAMILY";
                RenderForm();
            }));
            return host;
        }

        private UIElement MakeTagFamHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _tagFamCols.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = _tagFamCols[i] });
            string[] headers = { "Category", "Family", "Depth", "" };
            string[] tooltips = {
                null, null,
                "TAG7 paragraph depth (1=compact … 10=full audit). Overrides drawing type default for this category only.",
                null,
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var t = new TextBlock {
                    Text = headers[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                };
                if (!string.IsNullOrEmpty(tooltips[i])) t.ToolTip = tooltips[i];
                Grid.SetColumn(t, i); g.Children.Add(t);
            }
            return g;
        }

        private UIElement MakeTagFamRow(AnnotationRulePack pack, string catKey, string famValue)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            for (int i = 0; i < _tagFamCols.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = _tagFamCols[i] });

            var cats = Merge(ProjectAssetPicker.TaggableCategoryNames(_doc),
                             KnownTaggableCategories).ToArray();
            var fams = Merge(ProjectAssetPicker.TagFamilyNames(_doc),
                             Iso19650Vocabulary.CommonTagFamilies).ToArray();

            // Category combo — rename-key-preserves-value semantics.
            var k = SmallCombo(catKey, newKey =>
            {
                newKey = (newKey ?? "").Trim();
                if (string.IsNullOrEmpty(newKey) || newKey == catKey) return;
                if (!pack.TagFamilies.ContainsKey(catKey)) return;
                var existing = pack.TagFamilies[catKey];
                pack.TagFamilies.Remove(catKey);
                pack.TagFamilies[newKey] = existing;
                if (pack.TagDepths.TryGetValue(catKey, out var d))
                {
                    pack.TagDepths.Remove(catKey);
                    pack.TagDepths[newKey] = d;
                }
            }, cats);

            var v = SmallCombo(famValue ?? "", val =>
            {
                if (pack.TagFamilies.ContainsKey(catKey))
                    pack.TagFamilies[catKey] = (val ?? "").Trim();
            }, fams);

            int currentDepth = pack.TagDepths.TryGetValue(catKey, out var dv) ? dv : 2;
            var depth = SmallCombo(currentDepth.ToString(), dv2 =>
            {
                if (int.TryParse(dv2, out var n))
                    pack.TagDepths[catKey] = n;
            }, new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" });
            depth.IsEditable = false;
            depth.ToolTip = "TAG7 paragraph depth (1=compact … 10=full audit). " +
                            "Overrides drawing type default for this category only.";

            var rm = MakeSmallBtn("×", () =>
            {
                pack.TagFamilies.Remove(catKey);
                pack.TagDepths.Remove(catKey);
                RenderForm();
            });
            rm.Width = 22;

            var ctrls = new UIElement[] { k, v, depth, rm };
            for (int i = 0; i < ctrls.Length; i++)
            {
                Grid.SetColumn(ctrls[i], i);
                if (ctrls[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                g.Children.Add(ctrls[i]);
            }
            return g;
        }

        private UIElement BuildSlotsCard()
        {
            var body = new StackPanel();
            _current.Slots = _current.Slots ?? new List<DrawingSlot>();

            // Header row uses the same column definitions as every data
            // row so cells stay aligned regardless of dialog width.
            body.Children.Add(MakeSlotHeader());

            foreach (var slot in _current.Slots.ToList())
                body.Children.Add(MakeSlotRow(slot, () => RenderForm()));

            body.Children.Add(MakeSmallBtn("＋ Add slot", () =>
            {
                _current.Slots.Add(new DrawingSlot {
                    Label = "NewSlot", ViewType = "Plan",
                    NormX = 0.05, NormY = 0.05, NormW = 0.40, NormH = 0.40 });
                RenderForm();
            }));
            return Card("Slots (0..1 normalised)", body);
        }

        private UIElement MakeSlotHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            for (int c = 0; c < _slotColWidths.Length; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_slotColWidths[c]) });
            string[] headers = { "Label", "ViewType", "X", "Y", "W", "H", "Scale", "" };
            for (int i = 0; i < headers.Length; i++)
            {
                var t = new TextBlock {
                    Text = headers[i],
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                };
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            return g;
        }

        private UIElement MakeSlotRow(DrawingSlot slot, Action onChange)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            for (int c = 0; c < _slotColWidths.Length; c++)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_slotColWidths[c]) });

            // Phase 137 — slot label as a ComboBox sourced from any STING-aware
            // title-block's TB_VIEWPORT_SLOTS_JSON_TXT parameter (so the user
            // picks a real slot name authored on the title-block family),
            // with the canonical default catalogue appended as a fallback.
            // Selecting a label that maps to a known slot also auto-fills
            // normX / normY / normW / normH from that slot's geometry.
            var slotLabels = TitleBlockSlotLoader.GetLabels(_doc, _current.TitleBlockFamily);
            var tbLbl = SmallCombo(slot.Label, v =>
            {
                slot.Label = v;
                var match = TitleBlockSlotLoader.FindByLabel(_doc, v, _current.TitleBlockFamily);
                if (match != null)
                {
                    slot.NormX = match.NormX;
                    slot.NormY = match.NormY;
                    slot.NormW = match.NormW;
                    slot.NormH = match.NormH;
                    if (!string.IsNullOrEmpty(match.ViewType)) slot.ViewType = match.ViewType;
                    if (match.Scale.HasValue) slot.Scale = match.Scale;
                    onChange?.Invoke();
                }
            }, slotLabels.ToArray());
            // Closed list of Revit ViewType enum values — eliminates typos.
            var tbVt  = SmallCombo(slot.ViewType, v => slot.ViewType = v,
                new[] { "FloorPlan", "CeilingPlan", "Elevation", "Section", "ThreeD",
                        "Detail", "DraftingView", "Legend", "Schedule", "AreaPlan",
                        "EngineeringPlan", "Walkthrough", "Rendering", "ProjectBrowser",
                        "SystemBrowser", "Plan", "Sheet", "Internal", "Undefined" });
            var tbX  = SmallTb(slot.NormX.ToString("F2"), v => slot.NormX = Parse(v));
            var tbY  = SmallTb(slot.NormY.ToString("F2"), v => slot.NormY = Parse(v));
            var tbW  = SmallTb(slot.NormW.ToString("F2"), v => slot.NormW = Parse(v));
            var tbH  = SmallTb(slot.NormH.ToString("F2"), v => slot.NormH = Parse(v));
            var tbSc = SmallTb(slot.Scale?.ToString() ?? "", v =>
                slot.Scale = int.TryParse(v, out var n) ? (int?)n : null);
            var rm   = MakeSmallBtn("×", () => { _current.Slots.Remove(slot); onChange?.Invoke(); });
            rm.Width = 22;

            var ctrls = new UIElement[] { tbLbl, tbVt, tbX, tbY, tbW, tbH, tbSc, rm };
            for (int i = 0; i < ctrls.Length; i++)
            {
                Grid.SetColumn(ctrls[i], i);
                if (ctrls[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                row.Children.Add(ctrls[i]);
            }
            return row;
        }

        // ═══════════════════════════════════════════════════════════════
        //  FOOTER — Save / Save-As / Close
        // ═══════════════════════════════════════════════════════════════

        private UIElement BuildFooter()
        {
            // DockPanel processes children in declaration order — adding
            // the buttons FIRST with Dock.Right reserves their pixels
            // before the hint is added, so a narrow window can never
            // clip the buttons. The hint then takes whatever room
            // remains and ellipsises gracefully.
            var row = new DockPanel {
                Margin = new Thickness(0, 10, 0, 0),
                LastChildFill = true,
                MinHeight = 36,
            };

            var right = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 230,
            };
            DockPanel.SetDock(right, Dock.Right);
            var btnClose = MakeBigBtn("Close", CardBg, false);
            var btnSave  = MakeBigBtn("Save",  AccentColor, true);
            btnClose.Click += (s, e) => { DialogResult = false; };
            btnSave.Click  += (s, e) => { if (SaveToProjectOverride()) { DialogResult = true; } };
            right.Children.Add(btnClose);
            right.Children.Add(btnSave);
            row.Children.Add(right);

            var hint = new TextBlock {
                Text = "Save writes the active tab to <project>/_BIM_COORD/drawing_types.json or view_style_packs.json — " +
                       "project override only. Corporate baseline on disk is never mutated. " +
                       "Action tabs dispatch directly via the dock-panel external-event queue.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0),
            };
            row.Children.Add(hint);

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

        private UIElement LabeledCombo(string label, string[] items, string value, Action<string> setter,
            string tooltip = null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
            var lbl = new TextBlock {
                Text = label, Width = 200,
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            var cb = new ComboBox { Height = 22, IsEditable = true, ToolTip = tooltip };
            DarkDialogTheme.StyleInput(cb, CardBg, FgColor, CardBorder);
            foreach (var it in items ?? Array.Empty<string>()) cb.Items.Add(it);
            cb.Text = value ?? "";
            cb.LostFocus += (s, e) => { setter?.Invoke(cb.Text?.Trim()); };
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is string ss) setter?.Invoke(ss);
            };
            row.Children.Add(cb);
            return row;
        }

        // Project-asset combo — like LabeledCombo but the items are
        // queried live from the active Revit document via
        // ProjectAssetPicker, and a small ⟳ button refreshes the list.
        private UIElement LabeledProjectAssetCombo(string label, string value,
            Action<string> setter, IEnumerable<string> items, string tooltip = null)
        {
            return LabeledCombo(label,
                (items ?? Array.Empty<string>()).OrderBy(x => x).ToArray(),
                value, setter, tooltip);
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

        // Compact editable combo for inline grids (Slots / VG rows etc.).
        private ComboBox SmallCombo(string text, Action<string> setter, string[] items)
        {
            var cb = new ComboBox { Height = 20, FontSize = 10, IsEditable = true };
            DarkDialogTheme.StyleInput(cb, CardBg, FgColor, CardBorder);
            foreach (var it in items ?? Array.Empty<string>()) cb.Items.Add(it);
            cb.Text = text ?? "";
            cb.LostFocus += (s, e) => setter?.Invoke(cb.Text?.Trim());
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is string ss) setter?.Invoke(ss);
            };
            return cb;
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

        // ───────────────────────────────────────────────────────────────
        //  PHASE 136 — FULL REVIT-STYLE VG EDITOR
        //  4-tab layout (Model / Annotation / Imported / Filters) with
        //  category tree, Override... sub-dialogs (Lines + Patterns),
        //  per-category Halftone + DetailLevel, All/None/Invert/Expand bulk.
        // ───────────────────────────────────────────────────────────────

        // ── VgCategoryNode — internal model for the editor tree ──
        private sealed class VgCategoryNode
        {
            public string CategoryName { get; set; }       // display name
            public string CategoryKey  { get; set; }       // key into VgOverrides dict
            public string Discipline   { get; set; }       // "Architectural"/"Mechanical"/... for filter combo
            public bool IsSubcategory  { get; set; }
            public List<VgCategoryNode> Children { get; set; } = new List<VgCategoryNode>();
            public bool IsExpanded { get; set; }            // UI state for [+]/[-]
        }

        // Cached tree state (built once in the constructor, reused).
        private List<VgCategoryNode> _modelCatNodes      = new List<VgCategoryNode>();
        private List<VgCategoryNode> _annotationCatNodes = new List<VgCategoryNode>();
        private List<VgCategoryNode> _importedCatNodes   = new List<VgCategoryNode>();
        private string[] _linePatternNames = new[] { "(No Override)", "Solid" };
        private string[] _fillPatternNames = new[] { "(No Override)" };

        // Discipline label assigned to nodes created from the static
        // fallback list (the live doc loop assigns "Project" so users see
        // which entries are currently bound to a category in this model).
        private const string DISC_PROJECT = "Project";

        // Build the three category trees + line/fill pattern lists.
        // Unions doc.Settings.Categories with the static
        // KnownModelCategoriesByDiscipline / KnownAnnotationCategoriesByDiscipline
        // tables so the editor always shows a complete native-Revit catalogue,
        // even on minimal projects (template-only documents, family editor,
        // empty new project) and is filterable by discipline.
        private void BuildCategoryTrees()
        {
            // Per-step try/catch so a failure in one step (e.g. a hostile
            // doc.Settings.Categories iteration) doesn't suppress the static
            // fallback that follows.
            var modelByKey = new Dictionary<string, VgCategoryNode>(StringComparer.OrdinalIgnoreCase);
            var annoByKey  = new Dictionary<string, VgCategoryNode>(StringComparer.OrdinalIgnoreCase);

            // 1. Live doc categories ----------------------------------------
            try
            {
                if (_doc != null)
                {
                    foreach (Category cat in _doc.Settings.Categories)
                    {
                        if (cat == null) continue;
                        Dictionary<string, VgCategoryNode> bucket = null;
                        if (cat.CategoryType == CategoryType.Model) bucket = modelByKey;
                        else if (cat.CategoryType == CategoryType.Annotation) bucket = annoByKey;
                        if (bucket == null) continue;
                        var node = new VgCategoryNode {
                            CategoryName = cat.Name, CategoryKey = cat.Name,
                            Discipline = DISC_PROJECT };
                        foreach (Category sub in cat.SubCategories)
                        {
                            if (sub == null) continue;
                            node.Children.Add(new VgCategoryNode {
                                CategoryName = sub.Name, CategoryKey = "<" + sub.Name + ">",
                                Discipline = DISC_PROJECT, IsSubcategory = true });
                        }
                        bucket[cat.Name] = node;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("BuildCategoryTrees.live: " + ex.Message); }

            // 2. Static fallback (runs unconditionally — never throws) -----
            foreach (var kv in KnownModelCategoriesByDiscipline)
                foreach (var name in kv.Value)
                {
                    if (modelByKey.ContainsKey(name)) continue;
                    modelByKey[name] = new VgCategoryNode {
                        CategoryName = name, CategoryKey = name, Discipline = kv.Key };
                }
            foreach (var kv in KnownAnnotationCategoriesByDiscipline)
                foreach (var name in kv.Value)
                {
                    if (annoByKey.ContainsKey(name)) continue;
                    annoByKey[name] = new VgCategoryNode {
                        CategoryName = name, CategoryKey = name, Discipline = kv.Key };
                }

            _modelCatNodes      = modelByKey.Values.OrderBy(n => n.CategoryName).ToList();
            _annotationCatNodes = annoByKey.Values .OrderBy(n => n.CategoryName).ToList();

            // 3. Imported (DWG / DXF) categories — live doc only ----------
            try
            {
                if (_doc != null)
                {
                    var imports = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ImportInstance))
                        .Cast<ImportInstance>()
                        .Where(i => i?.Category != null)
                        .ToList();
                    var byFile = new Dictionary<string, VgCategoryNode>(StringComparer.OrdinalIgnoreCase);
                    foreach (var imp in imports)
                    {
                        var c = imp.Category;
                        if (c == null) continue;
                        if (!byFile.TryGetValue(c.Name, out var parent))
                        {
                            parent = new VgCategoryNode {
                                CategoryName = c.Name, CategoryKey = c.Name, Discipline = DISC_PROJECT };
                            byFile[c.Name] = parent;
                            foreach (Category sub in c.SubCategories)
                            {
                                if (sub == null) continue;
                                parent.Children.Add(new VgCategoryNode {
                                    CategoryName = sub.Name, CategoryKey = "<" + sub.Name + ">",
                                    Discipline = DISC_PROJECT, IsSubcategory = true });
                            }
                        }
                    }
                    _importedCatNodes = byFile.Values.OrderBy(n => n.CategoryName).ToList();
                }
            }
            catch (Exception ex) { StingLog.Warn("BuildCategoryTrees.imports: " + ex.Message); }

            // 4. Line + fill patterns — live doc only ---------------------
            try
            {
                if (_doc != null)
                {
                    _linePatternNames = new[] { "(No Override)", "Solid" }
                        .Concat(new FilteredElementCollector(_doc)
                            .OfClass(typeof(LinePatternElement))
                            .Cast<LinePatternElement>()
                            .Select(lp => lp.Name).OrderBy(n => n))
                        .Distinct().ToArray();

                    _fillPatternNames = new[] { "(No Override)" }
                        .Concat(new FilteredElementCollector(_doc)
                            .OfClass(typeof(FillPatternElement))
                            .Cast<FillPatternElement>()
                            .Select(fp => fp.Name).OrderBy(n => n))
                        .Distinct().ToArray();
                }
            }
            catch (Exception ex) { StingLog.Warn("BuildCategoryTrees.patterns: " + ex.Message); }
        }

        // Returns the discipline filter options for a given tree type.
        // "All" + "Project" (live-doc-tagged) + each static-table key.
        private string[] DisciplineFilterOptions(IEnumerable<VgCategoryNode> nodes,
            Dictionary<string, string[]> staticTable)
        {
            var live = nodes.Select(n => n.Discipline ?? "")
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Order: "All" first, then "Project" if any live entries, then
            // static-table keys in their declared order.
            var ordered = new List<string> { "All" };
            if (live.Any(d => d.Equals(DISC_PROJECT, StringComparison.OrdinalIgnoreCase)))
                ordered.Add(DISC_PROJECT);
            foreach (var k in staticTable.Keys)
                if (live.Any(d => d.Equals(k, StringComparison.OrdinalIgnoreCase))
                    && !ordered.Contains(k, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(k);
            return ordered.ToArray();
        }


        // ── BuildFullVgEditor — replaces the old VG/Filter row-grids ──
        // 4 tabs: Model Categories / Annotation Categories / Imported / Filters
        private UIElement BuildFullVgEditor()
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(CardBorder),
                Background  = new SolidColorBrush(CardBg),
                Margin = new Thickness(0, 6, 0, 6),
            };

            var outer = new DockPanel();
            border.Child = outer;

            var title = new TextBlock
            {
                Text = "VG overrides (per category)",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentColor),
                Margin = new Thickness(8, 6, 8, 2),
            };
            DockPanel.SetDock(title, Dock.Top);
            outer.Children.Add(title);

            var note = new TextBlock
            {
                Text = "Categories not overridden are drawn according to Object Styles settings. " +
                       "Colours in #RRGGBB. Patterns resolved by name from the active project.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 8, 4),
            };
            DockPanel.SetDock(note, Dock.Top);
            outer.Children.Add(note);

            var tabs = new TabControl
            {
                Background  = new SolidColorBrush(BgColor),
                Foreground  = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                Margin      = new Thickness(4),
            };
            tabs.Items.Add(MakeVgTab("Model Categories",      BuildVgTreeTab(_modelCatNodes)));
            tabs.Items.Add(MakeVgTab("Annotation Categories", BuildVgTreeTab(_annotationCatNodes)));
            tabs.Items.Add(MakeVgTab("Imported Categories",   BuildVgTreeTab(_importedCatNodes)));
            tabs.Items.Add(MakeVgTab("Filters",               BuildFiltersTab()));
            DockPanel.SetDock(tabs, Dock.Top);
            outer.Children.Add(tabs);

            return border;
        }

        private TabItem MakeVgTab(string header, UIElement content)
        {
            return new TabItem
            {
                Header = header, Content = content,
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
            };
        }


        // Column widths shared by the header row + every category row so
        // the columns line up regardless of dialog width. Order:
        // [vis-chk, name, lines, patterns, transparency, cut-lines, cut-patterns, halftone, detail-lvl]
        private static readonly double[] _vgColWidths =
            { 22, 180, 90, 90, 50, 90, 90, 60, 80 };

        // Per-tab discipline filter state — keyed by tree-list reference so
        // each tab keeps its own filter independently.
        private readonly Dictionary<List<VgCategoryNode>, string> _disciplineFilter
            = new Dictionary<List<VgCategoryNode>, string>();

        private UIElement BuildVgTreeTab(List<VgCategoryNode> nodes)
        {
            var dock = new DockPanel { Margin = new Thickness(2), LastChildFill = true };

            // Resolve the static-table for this tree (used for filter options).
            var staticTable = ReferenceEquals(nodes, _annotationCatNodes)
                ? KnownAnnotationCategoriesByDiscipline
                : KnownModelCategoriesByDiscipline;

            // Discipline filter row (top) ---------------------------------
            if (!_disciplineFilter.ContainsKey(nodes)) _disciplineFilter[nodes] = "All";
            var filterRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = true };
            var filterLbl = new TextBlock {
                Text = "Filter by discipline:",
                Foreground = new SolidColorBrush(SubtleColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(filterLbl, Dock.Left);
            filterRow.Children.Add(filterLbl);

            var filterCombo = new ComboBox { Height = 22, IsEditable = false, MinWidth = 160 };
            DarkDialogTheme.StyleInput(filterCombo, CardBg, FgColor, CardBorder);
            foreach (var d in DisciplineFilterOptions(nodes, staticTable))
                filterCombo.Items.Add(d);
            filterCombo.SelectedItem = _disciplineFilter[nodes];
            DockPanel.SetDock(filterRow, Dock.Top);
            dock.Children.Add(filterRow);

            // Column header row
            var hdrRow = MakeVgHeaderRow();
            DockPanel.SetDock(hdrRow, Dock.Top);
            dock.Children.Add(hdrRow);

            // Action button row at bottom: All / None / Invert / Expand All
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 2),
            };
            actions.Children.Add(MakeSmallBtn("All",        () => BulkSetVisibility(FilteredNodes(nodes), true)));
            actions.Children.Add(MakeSmallBtn("None",       () => BulkSetVisibility(FilteredNodes(nodes), false)));
            actions.Children.Add(MakeSmallBtn("Invert",     () => BulkInvertVisibility(FilteredNodes(nodes))));
            actions.Children.Add(MakeSmallBtn("Expand All", () => BulkExpand(FilteredNodes(nodes), true)));
            actions.Children.Add(MakeSmallBtn("Collapse All", () => BulkExpand(FilteredNodes(nodes), false)));
            DockPanel.SetDock(actions, Dock.Bottom);
            dock.Children.Add(actions);

            // Category rows — direct StackPanel inside the DockPanel's
            // remaining (LastChildFill) area. The form-host already has its
            // own outer ScrollViewer, so we don't nest one here (which could
            // collapse to zero height when the parent has Auto sizing).
            var rowsHost = new StackPanel { MinHeight = 220 };
            dock.Children.Add(rowsHost);

            // Wire the filter combo *after* rowsHost exists so the handler
            // can re-render against the current filter selection.
            filterCombo.SelectionChanged += (s, e) =>
            {
                if (filterCombo.SelectedItem is string ss)
                {
                    _disciplineFilter[nodes] = ss;
                    RenderVgRows(rowsHost, nodes);
                }
            };
            filterRow.Children.Add(filterCombo);

            RenderVgRows(rowsHost, nodes);
            return dock;
        }

        // Filter `nodes` by the active discipline selection ("All" → no filter).
        private List<VgCategoryNode> FilteredNodes(List<VgCategoryNode> nodes)
        {
            if (!_disciplineFilter.TryGetValue(nodes, out var disc) || string.IsNullOrEmpty(disc) || disc == "All")
                return nodes;
            return nodes.Where(n => string.Equals(n.Discipline, disc, StringComparison.OrdinalIgnoreCase))
                        .ToList();
        }

        // Render the rows for the given node list, honouring the per-tab
        // discipline filter. Subcat rows render only when the parent is
        // expanded. Re-called whenever filter / override state changes.
        private void RenderVgRows(StackPanel host, List<VgCategoryNode> nodes)
        {
            host.Children.Clear();
            var filtered = FilteredNodes(nodes);

            if (filtered.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = "(no categories match this filter — switch to All or load some elements)",
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontStyle = FontStyles.Italic,
                    FontSize = 11,
                    Margin = new Thickness(8, 8, 8, 8),
                });
                return;
            }

            foreach (var n in filtered)
            {
                host.Children.Add(MakeVgCategoryRow(n, host, nodes, indent: 0));
                if (n.IsExpanded)
                {
                    foreach (var sub in n.Children)
                        host.Children.Add(MakeVgCategoryRow(sub, host, nodes, indent: 16));
                }
            }
        }

        private UIElement MakeVgHeaderRow()
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _vgColWidths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_vgColWidths[i]) });

            // Header labels (matching Revit's VG dialog headings)
            string[] headers = { "", "Visibility", "Lines", "Patterns", "Trans%", "Cut Lines", "Cut Patterns", "Halftone", "Detail Level" };
            for (int i = 0; i < headers.Length; i++)
            {
                var t = new TextBlock
                {
                    Text = headers[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                };
                Grid.SetColumn(t, i); g.Children.Add(t);
            }
            return g;
        }


        // Build the row for one category (model / annotation / imported).
        // Has: visibility-checkbox, name (with [+]/[-] for parents), Lines
        // Override... button, Patterns Override... button, Transparency tb,
        // Cut Lines Override... button, Cut Patterns Override... button,
        // Halftone checkbox, DetailLevel combo.
        private UIElement MakeVgCategoryRow(VgCategoryNode node, StackPanel host,
            List<VgCategoryNode> rootList, int indent)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            for (int i = 0; i < _vgColWidths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_vgColWidths[i]) });

            var ovr = GetOrCreateOverride(node.CategoryKey);

            // Col 0 — visibility checkbox
            var visCb = new CheckBox
            {
                IsChecked = ovr.Visible ?? true,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            visCb.Checked   += (s, e) => { ovr.Visible = true;  CleanupIfEmpty(node.CategoryKey); };
            visCb.Unchecked += (s, e) => { ovr.Visible = false; CleanupIfEmpty(node.CategoryKey); };
            Grid.SetColumn(visCb, 0); g.Children.Add(visCb);

            // Col 1 — category name with optional expand/collapse triangle
            var namePanel = new DockPanel { Margin = new Thickness(indent + 2, 0, 0, 0) };
            if (!node.IsSubcategory && node.Children.Count > 0)
            {
                var tri = new TextBlock
                {
                    Text = node.IsExpanded ? "▼" : "▶",
                    Width = 12, FontSize = 10,
                    Foreground = new SolidColorBrush(AccentColor),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                tri.MouseLeftButtonUp += (s, e) =>
                {
                    node.IsExpanded = !node.IsExpanded;
                    RenderVgRows(host, rootList);
                };
                DockPanel.SetDock(tri, Dock.Left);
                namePanel.Children.Add(tri);
            }
            else
            {
                var spacer = new TextBlock { Width = 12 };
                DockPanel.SetDock(spacer, Dock.Left);
                namePanel.Children.Add(spacer);
            }
            namePanel.Children.Add(new TextBlock
            {
                Text = node.CategoryName, FontSize = 11,
                Foreground = new SolidColorBrush(FgColor),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(namePanel, 1); g.Children.Add(namePanel);

            // Col 2 — Projection Lines override button
            var btnLines = MakeOverrideBtn(SummariseLineOverride(
                ovr.ProjColor, ovr.ProjWeight, ovr.ProjLinePattern), () =>
            {
                if (LineOverrideSubDialog.ShowEditor(this, "Line Graphics",
                    ovr.ProjColor, ovr.ProjWeight, ovr.ProjLinePattern,
                    _linePatternNames,
                    out var newColor, out var newWeight, out var newPattern))
                {
                    ovr.ProjColor       = newColor;
                    ovr.ProjWeight      = newWeight;
                    ovr.ProjLinePattern = newPattern;
                    CleanupIfEmpty(node.CategoryKey);
                    RenderVgRows(host, rootList);
                }
            });
            Grid.SetColumn(btnLines, 2); g.Children.Add(btnLines);

            // Col 3 — Surface Patterns override button (fg + bg)
            var btnPats = MakeOverrideBtn(SummarisePatternOverride(
                ovr.SurfaceFgPatternName, ovr.SurfaceFgPatternColor, ovr.SurfaceFgPatternVisible,
                ovr.SurfaceBgPatternName, ovr.SurfaceBgPatternColor, ovr.SurfaceBgPatternVisible), () =>
            {
                var fg = (ovr.SurfaceFgPatternName, ovr.SurfaceFgPatternColor, ovr.SurfaceFgPatternVisible);
                var bg = (ovr.SurfaceBgPatternName, ovr.SurfaceBgPatternColor, ovr.SurfaceBgPatternVisible);
                if (PatternOverrideSubDialog.ShowEditor(this, "Surface Patterns",
                    fg, bg, _fillPatternNames, out var newFg, out var newBg))
                {
                    ovr.SurfaceFgPatternName    = newFg.name;
                    ovr.SurfaceFgPatternColor   = newFg.color;
                    ovr.SurfaceFgPatternVisible = newFg.visible;
                    ovr.SurfaceBgPatternName    = newBg.name;
                    ovr.SurfaceBgPatternColor   = newBg.color;
                    ovr.SurfaceBgPatternVisible = newBg.visible;
                    CleanupIfEmpty(node.CategoryKey);
                    RenderVgRows(host, rootList);
                }
            });
            Grid.SetColumn(btnPats, 3); g.Children.Add(btnPats);

            // Col 4 — transparency text box
            var transTb = new TextBox
            {
                Text = ovr.Transparency == 0 ? "" : ovr.Transparency.ToString(),
                FontSize = 10, Height = 20,
                ToolTip = "0–100. Empty = no override.",
            };
            DarkDialogTheme.StyleInput(transTb, CardBg, FgColor, CardBorder);
            transTb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(transTb.Text)) { ovr.Transparency = 0; }
                else if (int.TryParse(transTb.Text, out var n)) ovr.Transparency = Math.Max(0, Math.Min(100, n));
                CleanupIfEmpty(node.CategoryKey);
            };
            Grid.SetColumn(transTb, 4); g.Children.Add(transTb);

            // Col 5 — Cut Lines override button
            var btnCutLines = MakeOverrideBtn(SummariseLineOverride(
                ovr.CutColor, ovr.CutWeight, ovr.CutLinePattern), () =>
            {
                if (LineOverrideSubDialog.ShowEditor(this, "Cut Line Graphics",
                    ovr.CutColor, ovr.CutWeight, ovr.CutLinePattern,
                    _linePatternNames,
                    out var newColor, out var newWeight, out var newPattern))
                {
                    ovr.CutColor       = newColor;
                    ovr.CutWeight      = newWeight;
                    ovr.CutLinePattern = newPattern;
                    CleanupIfEmpty(node.CategoryKey);
                    RenderVgRows(host, rootList);
                }
            });
            Grid.SetColumn(btnCutLines, 5); g.Children.Add(btnCutLines);

            // Col 6 — Cut Patterns override button (fg + bg)
            var btnCutPats = MakeOverrideBtn(SummarisePatternOverride(
                ovr.CutFgPatternName, ovr.CutFgPatternColor, ovr.CutFgPatternVisible,
                ovr.CutBgPatternName, ovr.CutBgPatternColor, ovr.CutBgPatternVisible), () =>
            {
                var fg = (ovr.CutFgPatternName, ovr.CutFgPatternColor, ovr.CutFgPatternVisible);
                var bg = (ovr.CutBgPatternName, ovr.CutBgPatternColor, ovr.CutBgPatternVisible);
                if (PatternOverrideSubDialog.ShowEditor(this, "Cut Patterns",
                    fg, bg, _fillPatternNames, out var newFg, out var newBg))
                {
                    ovr.CutFgPatternName    = newFg.name;
                    ovr.CutFgPatternColor   = newFg.color;
                    ovr.CutFgPatternVisible = newFg.visible;
                    ovr.CutBgPatternName    = newBg.name;
                    ovr.CutBgPatternColor   = newBg.color;
                    ovr.CutBgPatternVisible = newBg.visible;
                    CleanupIfEmpty(node.CategoryKey);
                    RenderVgRows(host, rootList);
                }
            });
            Grid.SetColumn(btnCutPats, 6); g.Children.Add(btnCutPats);

            // Col 7 — Halftone checkbox (3-state via context: null = not overridden)
            var htCb = new CheckBox
            {
                IsChecked = ovr.Halftone,
                IsThreeState = true,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = "Indeterminate = no override; checked = halftone; unchecked = no halftone.",
            };
            htCb.Checked   += (s, e) => { ovr.Halftone = true;  CleanupIfEmpty(node.CategoryKey); };
            htCb.Unchecked += (s, e) => { ovr.Halftone = false; CleanupIfEmpty(node.CategoryKey); };
            htCb.Indeterminate += (s, e) => { ovr.Halftone = null; CleanupIfEmpty(node.CategoryKey); };
            Grid.SetColumn(htCb, 7); g.Children.Add(htCb);

            // Col 8 — Detail Level combo
            var dlItems = new[] { "(By View)", "Coarse", "Medium", "Fine" };
            var dlCb = new ComboBox { Height = 20, FontSize = 10, IsEditable = false };
            DarkDialogTheme.StyleInput(dlCb, CardBg, FgColor, CardBorder);
            foreach (var it in dlItems) dlCb.Items.Add(it);
            dlCb.SelectedItem = string.IsNullOrEmpty(ovr.DetailLevel)
                ? "(By View)"
                : (dlItems.FirstOrDefault(d => d.Equals(ovr.DetailLevel, StringComparison.OrdinalIgnoreCase)) ?? "(By View)");
            dlCb.SelectionChanged += (s, e) =>
            {
                if (dlCb.SelectedItem is string ss)
                {
                    ovr.DetailLevel = ss == "(By View)" ? null : ss;
                    CleanupIfEmpty(node.CategoryKey);
                }
            };
            Grid.SetColumn(dlCb, 8); g.Children.Add(dlCb);

            return g;
        }


        // ── Override-state helpers ──
        private PackCategoryOverride GetOrCreateOverride(string key)
        {
            if (_currentPack.VgOverrides == null)
                _currentPack.VgOverrides = new Dictionary<string, PackCategoryOverride>();
            if (!_currentPack.VgOverrides.TryGetValue(key, out var ovr) || ovr == null)
            {
                ovr = new PackCategoryOverride();
                _currentPack.VgOverrides[key] = ovr;
            }
            return ovr;
        }

        // If the override has no fields set, remove it from the dict so the
        // serialised JSON stays sparse — mirrors Revit's behaviour.
        private void CleanupIfEmpty(string key)
        {
            if (_currentPack.VgOverrides == null) return;
            if (!_currentPack.VgOverrides.TryGetValue(key, out var ovr) || ovr == null) return;
            if (ovr.IsEmpty()) _currentPack.VgOverrides.Remove(key);
        }

        private void BulkSetVisibility(List<VgCategoryNode> nodes, bool visible)
        {
            foreach (var n in nodes)
            {
                var ovr = GetOrCreateOverride(n.CategoryKey);
                ovr.Visible = visible;
                CleanupIfEmpty(n.CategoryKey);
                foreach (var sub in n.Children)
                {
                    var so = GetOrCreateOverride(sub.CategoryKey);
                    so.Visible = visible;
                    CleanupIfEmpty(sub.CategoryKey);
                }
            }
            RenderPackForm();
        }

        private void BulkInvertVisibility(List<VgCategoryNode> nodes)
        {
            foreach (var n in nodes)
            {
                var ovr = GetOrCreateOverride(n.CategoryKey);
                ovr.Visible = !(ovr.Visible ?? true);
                CleanupIfEmpty(n.CategoryKey);
            }
            RenderPackForm();
        }

        private void BulkExpand(List<VgCategoryNode> nodes, bool expand)
        {
            foreach (var n in nodes) n.IsExpanded = expand;
            RenderPackForm();
        }

        // ── UI factory helpers ──
        private Button MakeOverrideBtn(string label, Action onClick)
        {
            var b = new Button
            {
                Content = label, Height = 20, FontSize = 10,
                Padding = new Thickness(4, 0, 4, 0),
                Background  = new SolidColorBrush(CardBg),
                Foreground  = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        private static string SummariseLineOverride(string color, int weight, string pattern)
        {
            bool any = !string.IsNullOrEmpty(color) || weight > 0 || !string.IsNullOrEmpty(pattern);
            if (!any) return "Override...";
            var parts = new List<string>();
            if (weight > 0) parts.Add($"Wt:{weight}");
            if (!string.IsNullOrEmpty(color)) parts.Add(color);
            if (!string.IsNullOrEmpty(pattern)) parts.Add(pattern);
            return string.Join(" ", parts);
        }

        private static string SummarisePatternOverride(
            string fgName, string fgColor, bool? fgVisible,
            string bgName, string bgColor, bool? bgVisible)
        {
            bool anyFg = !string.IsNullOrEmpty(fgName) || !string.IsNullOrEmpty(fgColor) || fgVisible.HasValue;
            bool anyBg = !string.IsNullOrEmpty(bgName) || !string.IsNullOrEmpty(bgColor) || bgVisible.HasValue;
            if (!anyFg && !anyBg) return "Override...";
            var bits = new List<string>();
            if (anyFg) bits.Add($"fg:{fgName ?? ""}");
            if (anyBg) bits.Add($"bg:{bgName ?? ""}");
            return string.Join(" ", bits);
        }


        // Filters tab — VG-style override surface for ParameterFilterElements.
        // Same column layout as VG categories (without the category-tree
        // expand/collapse) so the editor reads consistently across tabs.
        private static readonly double[] _filterColWidths =
            { 200, 22, 90, 90, 50, 60 };
        // [Filter name, visible-chk, lines, patterns, trans%, halftone]

        private UIElement BuildFiltersTab()
        {
            var dock = new DockPanel { Margin = new Thickness(2), LastChildFill = true };

            // Header
            var hdr = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _filterColWidths.Length; i++)
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_filterColWidths[i]) });
            string[] hd = { "Filter Name", "", "Lines", "Patterns", "Trans%", "Halftone" };
            for (int i = 0; i < hd.Length; i++)
            {
                var t = new TextBlock
                {
                    Text = hd[i], FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                };
                Grid.SetColumn(t, i); hdr.Children.Add(t);
            }
            DockPanel.SetDock(hdr, Dock.Top);
            dock.Children.Add(hdr);

            // "Add filter rule" + Remove-all bottom row
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 2),
            };
            actions.Children.Add(MakeSmallBtn("＋ Add filter rule", () =>
            {
                _currentPack.FilterRules.Add(new PackFilterRule
                {
                    Name = "NewFilter", Visible = true, Halftone = false,
                });
                RenderPackForm();
            }));
            DockPanel.SetDock(actions, Dock.Bottom);
            dock.Children.Add(actions);

            // Scrollable filter rows
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 380,
            };
            var rows = new StackPanel();
            scroll.Content = rows;
            dock.Children.Add(scroll);

            var filterNames = Merge(ProjectAssetPicker.ParameterFilterNames(_doc),
                                    CommonStingFilters).ToArray();
            foreach (var fr in _currentPack.FilterRules.ToList())
                rows.Children.Add(MakeFilterRow(fr, filterNames));

            return dock;
        }

        private UIElement MakeFilterRow(PackFilterRule fr, string[] filterNames)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            for (int i = 0; i < _filterColWidths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_filterColWidths[i]) });

            // Col 0 — filter name combo
            var nameBox = SmallCombo(fr.Name ?? "", v => fr.Name = v, filterNames);
            Grid.SetColumn(nameBox, 0); g.Children.Add(nameBox);

            // Col 1 — visibility checkbox
            var visCb = new CheckBox
            {
                IsChecked = fr.Visible,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            visCb.Checked   += (s, e) => fr.Visible = true;
            visCb.Unchecked += (s, e) => fr.Visible = false;
            Grid.SetColumn(visCb, 1); g.Children.Add(visCb);

            // Col 2 — Projection Lines override button
            var btnLines = MakeOverrideBtn(
                SummariseLineOverride(fr.ProjColor, fr.ProjWeight, fr.ProjLinePattern), () =>
                {
                    if (LineOverrideSubDialog.ShowEditor(this, "Filter Line Graphics",
                        fr.ProjColor, fr.ProjWeight, fr.ProjLinePattern,
                        _linePatternNames,
                        out var nc, out var nw, out var np))
                    {
                        fr.ProjColor = nc; fr.ProjWeight = nw; fr.ProjLinePattern = np;
                        RenderPackForm();
                    }
                });
            Grid.SetColumn(btnLines, 2); g.Children.Add(btnLines);

            // Col 3 — Surface Patterns override button (fg + bg)
            var btnPats = MakeOverrideBtn(SummarisePatternOverride(
                fr.SurfaceFgPatternName, fr.SurfaceFgPatternColor, fr.SurfaceFgPatternVisible,
                fr.SurfaceBgPatternName, fr.SurfaceBgPatternColor, fr.SurfaceBgPatternVisible), () =>
            {
                var fg = (fr.SurfaceFgPatternName, fr.SurfaceFgPatternColor, fr.SurfaceFgPatternVisible);
                var bg = (fr.SurfaceBgPatternName, fr.SurfaceBgPatternColor, fr.SurfaceBgPatternVisible);
                if (PatternOverrideSubDialog.ShowEditor(this, "Filter Surface Patterns",
                    fg, bg, _fillPatternNames, out var newFg, out var newBg))
                {
                    fr.SurfaceFgPatternName    = newFg.name;
                    fr.SurfaceFgPatternColor   = newFg.color;
                    fr.SurfaceFgPatternVisible = newFg.visible;
                    fr.SurfaceBgPatternName    = newBg.name;
                    fr.SurfaceBgPatternColor   = newBg.color;
                    fr.SurfaceBgPatternVisible = newBg.visible;
                    RenderPackForm();
                }
            });
            Grid.SetColumn(btnPats, 3); g.Children.Add(btnPats);

            // Col 4 — transparency
            var transTb = new TextBox
            {
                Text = fr.Transparency == 0 ? "" : fr.Transparency.ToString(),
                FontSize = 10, Height = 20,
            };
            DarkDialogTheme.StyleInput(transTb, CardBg, FgColor, CardBorder);
            transTb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(transTb.Text)) fr.Transparency = 0;
                else if (int.TryParse(transTb.Text, out var n)) fr.Transparency = Math.Max(0, Math.Min(100, n));
            };
            Grid.SetColumn(transTb, 4); g.Children.Add(transTb);

            // Col 5 — halftone
            var htCb = new CheckBox
            {
                IsChecked = fr.Halftone,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            htCb.Checked   += (s, e) => fr.Halftone = true;
            htCb.Unchecked += (s, e) => fr.Halftone = false;
            Grid.SetColumn(htCb, 5); g.Children.Add(htCb);

            return g;
        }


        // ── LineOverrideSubDialog — modal "Override Lines" editor ──
        // Color (square swatch + #RRGGBB textbox + native ColorDialog),
        // Weight (combo: (No Override) | 1..16),
        // Pattern (combo from doc's LinePatternElements; Solid + No Override).
        // Static ShowEditor() takes existing values, returns user-chosen values.
        private sealed class LineOverrideSubDialog : Window
        {
            private string _color;
            private int    _weight;
            private string _pattern;
            private TextBox _hexBox;
            private Border  _swatch;
            private ComboBox _weightCb;
            private ComboBox _patternCb;
            private bool _ok;

            public static bool ShowEditor(Window owner, string title,
                string currentColor, int currentWeight, string currentPattern,
                string[] linePatternNames,
                out string newColor, out int newWeight, out string newPattern)
            {
                var dlg = new LineOverrideSubDialog(owner, title,
                    currentColor, currentWeight, currentPattern, linePatternNames);
                dlg.ShowDialog();
                newColor   = dlg._ok ? dlg._color   : currentColor;
                newWeight  = dlg._ok ? dlg._weight  : currentWeight;
                newPattern = dlg._ok ? dlg._pattern : currentPattern;
                return dlg._ok;
            }

            private LineOverrideSubDialog(Window owner, string title,
                string color, int weight, string pattern, string[] linePatternNames)
            {
                _color = color ?? "";
                _weight = weight;
                _pattern = pattern ?? "";

                Title = title;
                Width = 360; Height = 220;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Owner = owner;
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
                FontFamily = new FontFamily("Segoe UI");
                ResizeMode = ResizeMode.NoResize;

                var grid = new Grid { Margin = new Thickness(12) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Row 0 — color
                var lblColor = new TextBlock { Text = "Color:", VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(lblColor, 0); Grid.SetColumn(lblColor, 0); grid.Children.Add(lblColor);

                var colorPanel = new DockPanel { LastChildFill = true };
                _swatch = new Border
                {
                    Width = 24, Height = 24,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)),
                    BorderThickness = new Thickness(1),
                    Background = HexToBrush(_color),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                };
                _swatch.MouseLeftButtonUp += (s, e) => OpenColorPicker();
                DockPanel.SetDock(_swatch, Dock.Left);
                colorPanel.Children.Add(_swatch);

                _hexBox = new TextBox { Text = _color, Height = 22 };
                _hexBox.LostFocus += (s, e) =>
                {
                    _color = (_hexBox.Text ?? "").Trim();
                    _swatch.Background = HexToBrush(_color);
                };
                colorPanel.Children.Add(_hexBox);
                Grid.SetRow(colorPanel, 0); Grid.SetColumn(colorPanel, 1); grid.Children.Add(colorPanel);

                var lnkClearColor = new TextBlock
                {
                    Text = "(No Override)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)),
                    Cursor = Cursors.Hand, FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                };
                lnkClearColor.MouseLeftButtonUp += (s, e) =>
                {
                    _color = ""; _hexBox.Text = ""; _swatch.Background = Brushes.Transparent;
                };
                Grid.SetRow(lnkClearColor, 0); Grid.SetColumn(lnkClearColor, 2); grid.Children.Add(lnkClearColor);

                // Row 1 — weight
                var lblWeight = new TextBlock { Text = "Weight:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                Grid.SetRow(lblWeight, 1); Grid.SetColumn(lblWeight, 0); grid.Children.Add(lblWeight);

                _weightCb = new ComboBox { Height = 22, Margin = new Thickness(0, 8, 0, 0) };
                _weightCb.Items.Add("(No Override)");
                for (int i = 1; i <= 16; i++) _weightCb.Items.Add(i.ToString());
                _weightCb.SelectedItem = _weight > 0 ? _weight.ToString() : "(No Override)";
                _weightCb.SelectionChanged += (s, e) =>
                {
                    if (_weightCb.SelectedItem is string ss)
                        _weight = ss == "(No Override)" ? 0 : int.Parse(ss);
                };
                Grid.SetRow(_weightCb, 1); Grid.SetColumn(_weightCb, 1); Grid.SetColumnSpan(_weightCb, 2);
                grid.Children.Add(_weightCb);

                // Row 2 — pattern
                var lblPattern = new TextBlock { Text = "Pattern:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                Grid.SetRow(lblPattern, 2); Grid.SetColumn(lblPattern, 0); grid.Children.Add(lblPattern);

                _patternCb = new ComboBox { Height = 22, Margin = new Thickness(0, 8, 0, 0) };
                foreach (var n in linePatternNames ?? new[] { "(No Override)", "Solid" })
                    _patternCb.Items.Add(n);
                _patternCb.SelectedItem = string.IsNullOrEmpty(_pattern) ? "(No Override)" : _pattern;
                _patternCb.SelectionChanged += (s, e) =>
                {
                    if (_patternCb.SelectedItem is string ss)
                        _pattern = ss == "(No Override)" ? "" : ss;
                };
                Grid.SetRow(_patternCb, 2); Grid.SetColumn(_patternCb, 1); Grid.SetColumnSpan(_patternCb, 2);
                grid.Children.Add(_patternCb);

                // Row 4 — OK / Cancel
                var btns = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                var ok = new Button { Content = "OK", Width = 80, Height = 26, IsDefault = true,
                    Margin = new Thickness(0, 0, 6, 0) };
                ok.Click += (s, e) => { _ok = true; Close(); };
                var cancel = new Button { Content = "Cancel", Width = 80, Height = 26, IsCancel = true };
                cancel.Click += (s, e) => { _ok = false; Close(); };
                btns.Children.Add(ok); btns.Children.Add(cancel);
                Grid.SetRow(btns, 4); Grid.SetColumn(btns, 0); Grid.SetColumnSpan(btns, 3);
                grid.Children.Add(btns);

                Content = grid;
            }

            private static SolidColorBrush HexToBrush(string hex)
            {
                if (string.IsNullOrWhiteSpace(hex)) return new SolidColorBrush(Colors.Transparent);
                var s = hex.TrimStart('#');
                if (s.Length != 6) return new SolidColorBrush(Colors.Transparent);
                try
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
                catch { return new SolidColorBrush(Colors.Transparent); }
            }

            private void OpenColorPicker()
            {
                using (var cd = new System.Windows.Forms.ColorDialog())
                {
                    if (!string.IsNullOrEmpty(_color))
                    {
                        var s = _color.TrimStart('#');
                        if (s.Length == 6)
                        {
                            try
                            {
                                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                                cd.Color = System.Drawing.Color.FromArgb(r, g, b);
                            }
                            catch { }
                        }
                    }
                    if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _color = $"#{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                        _hexBox.Text = _color;
                        _swatch.Background = HexToBrush(_color);
                    }
                }
            }
        }


        // ── PatternOverrideSubDialog — modal "Override Patterns" editor ──
        // Two stacked sections (Foreground / Background); each section has
        // Visible CheckBox + Color row (swatch + hex + native ColorDialog) +
        // Pattern combo. ShowEditor takes/returns (name, color, visible).
        private sealed class PatternOverrideSubDialog : Window
        {
            private string _fgName, _fgColor; private bool? _fgVisible;
            private string _bgName, _bgColor; private bool? _bgVisible;
            private bool _ok;

            // Foreground UI
            private CheckBox _fgVisCb; private TextBox _fgHex;
            private Border _fgSwatch; private ComboBox _fgPatCb;
            // Background UI
            private CheckBox _bgVisCb; private TextBox _bgHex;
            private Border _bgSwatch; private ComboBox _bgPatCb;

            public static bool ShowEditor(Window owner, string title,
                (string name, string color, bool? visible) fg,
                (string name, string color, bool? visible) bg,
                string[] fillPatternNames,
                out (string name, string color, bool? visible) newFg,
                out (string name, string color, bool? visible) newBg)
            {
                var dlg = new PatternOverrideSubDialog(owner, title, fg, bg, fillPatternNames);
                dlg.ShowDialog();
                newFg = dlg._ok ? (dlg._fgName, dlg._fgColor, dlg._fgVisible) : fg;
                newBg = dlg._ok ? (dlg._bgName, dlg._bgColor, dlg._bgVisible) : bg;
                return dlg._ok;
            }

            private PatternOverrideSubDialog(Window owner, string title,
                (string name, string color, bool? visible) fg,
                (string name, string color, bool? visible) bg,
                string[] fillPatternNames)
            {
                _fgName = fg.name ?? ""; _fgColor = fg.color ?? ""; _fgVisible = fg.visible;
                _bgName = bg.name ?? ""; _bgColor = bg.color ?? ""; _bgVisible = bg.visible;

                Title = title;
                Width = 420; Height = 320;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Owner = owner;
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
                FontFamily = new FontFamily("Segoe UI");
                ResizeMode = ResizeMode.NoResize;

                var stack = new StackPanel { Margin = new Thickness(12) };
                stack.Children.Add(BuildSection("Foreground pattern", true,  fillPatternNames));
                stack.Children.Add(BuildSection("Background pattern", false, fillPatternNames));

                var btns = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                var ok = new Button { Content = "OK", Width = 80, Height = 26, IsDefault = true,
                    Margin = new Thickness(0, 0, 6, 0) };
                ok.Click += (s, e) => { _ok = true; Close(); };
                var cancel = new Button { Content = "Cancel", Width = 80, Height = 26, IsCancel = true };
                cancel.Click += (s, e) => { _ok = false; Close(); };
                btns.Children.Add(ok); btns.Children.Add(cancel);
                stack.Children.Add(btns);

                Content = stack;
            }

            private UIElement BuildSection(string title, bool isFg, string[] fillPatternNames)
            {
                var border = new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 6),
                };
                var inner = new StackPanel();
                inner.Children.Add(new TextBlock
                {
                    Text = title, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                });

                // Visible row
                var visCb = new CheckBox
                {
                    Content = "Visible", IsThreeState = true,
                    IsChecked = isFg ? _fgVisible : _bgVisible,
                    Margin = new Thickness(0, 0, 0, 4),
                    ToolTip = "Indeterminate = no override.",
                };
                visCb.Checked      += (s, e) => SetVisible(isFg, true);
                visCb.Unchecked    += (s, e) => SetVisible(isFg, false);
                visCb.Indeterminate += (s, e) => SetVisible(isFg, null);
                if (isFg) _fgVisCb = visCb; else _bgVisCb = visCb;
                inner.Children.Add(visCb);

                // Color row
                var colorRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
                var lbl = new TextBlock { Text = "Color:", Width = 50, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(lbl, Dock.Left);
                colorRow.Children.Add(lbl);

                var swatch = new Border
                {
                    Width = 24, Height = 24,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)),
                    BorderThickness = new Thickness(1),
                    Background = LineOverrideSubDialog_HexToBrush(isFg ? _fgColor : _bgColor),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                DockPanel.SetDock(swatch, Dock.Left);
                colorRow.Children.Add(swatch);
                if (isFg) _fgSwatch = swatch; else _bgSwatch = swatch;

                var hex = new TextBox { Text = isFg ? _fgColor : _bgColor, Height = 22 };
                hex.LostFocus += (s, e) =>
                {
                    var v = (hex.Text ?? "").Trim();
                    if (isFg) { _fgColor = v; _fgSwatch.Background = LineOverrideSubDialog_HexToBrush(v); }
                    else      { _bgColor = v; _bgSwatch.Background = LineOverrideSubDialog_HexToBrush(v); }
                };
                colorRow.Children.Add(hex);
                if (isFg) _fgHex = hex; else _bgHex = hex;

                swatch.MouseLeftButtonUp += (s, e) => OpenColorPicker(isFg);
                inner.Children.Add(colorRow);

                // Pattern row
                var patRow = new DockPanel { LastChildFill = true };
                var lbl2 = new TextBlock { Text = "Pattern:", Width = 50, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(lbl2, Dock.Left);
                patRow.Children.Add(lbl2);

                var patCb = new ComboBox { Height = 22 };
                foreach (var n in fillPatternNames ?? new[] { "(No Override)" })
                    patCb.Items.Add(n);
                patCb.SelectedItem = string.IsNullOrEmpty(isFg ? _fgName : _bgName)
                    ? "(No Override)"
                    : (isFg ? _fgName : _bgName);
                patCb.SelectionChanged += (s, e) =>
                {
                    if (patCb.SelectedItem is string ss)
                    {
                        var v = ss == "(No Override)" ? "" : ss;
                        if (isFg) _fgName = v; else _bgName = v;
                    }
                };
                if (isFg) _fgPatCb = patCb; else _bgPatCb = patCb;
                patRow.Children.Add(patCb);
                inner.Children.Add(patRow);

                border.Child = inner;
                return border;
            }

            private void SetVisible(bool isFg, bool? v)
            {
                if (isFg) _fgVisible = v;
                else      _bgVisible = v;
            }

            private void OpenColorPicker(bool isFg)
            {
                var current = isFg ? _fgColor : _bgColor;
                using (var cd = new System.Windows.Forms.ColorDialog())
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        var s = current.TrimStart('#');
                        if (s.Length == 6)
                        {
                            try
                            {
                                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                                cd.Color = System.Drawing.Color.FromArgb(r, g, b);
                            }
                            catch { }
                        }
                    }
                    if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        var hex = $"#{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                        if (isFg) { _fgColor = hex; _fgHex.Text = hex; _fgSwatch.Background = LineOverrideSubDialog_HexToBrush(hex); }
                        else      { _bgColor = hex; _bgHex.Text = hex; _bgSwatch.Background = LineOverrideSubDialog_HexToBrush(hex); }
                    }
                }
            }

            // Reusable hex→brush converter (kept inside the dialog so the
            // sub-dialog stays self-contained).
            private static SolidColorBrush LineOverrideSubDialog_HexToBrush(string hex)
            {
                if (string.IsNullOrWhiteSpace(hex)) return new SolidColorBrush(Colors.Transparent);
                var s = hex.TrimStart('#');
                if (s.Length != 6) return new SolidColorBrush(Colors.Transparent);
                try
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
                catch { return new SolidColorBrush(Colors.Transparent); }
            }
        }

    }
}
