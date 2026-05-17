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
        // Phase 169 — Resolved-column preview state. _previewDisc drives the
        // {disc}/{discipline}/{role} tokens; _previewSeq drives {seq:Dn}; both
        // optional. Persisted across RenderForm() rebuilds for the same dialog
        // session so the operator's pick survives a re-render.
        private string _previewDisc = "";
        private int    _previewSeq  = 1;
        private ListBox _lbTypes;
        private TextBox _tbSearch;
        private StackPanel _formHost;                    // right-hand form container
        private TextBlock _validationStrip;
        private TabControl _rootTabs;                    // top-level tab host

        // Slot grid column widths — header + every data row share these
        // so the column edges line up pixel-for-pixel regardless of the
        // dialog width.
        // [Label, ViewType, NormX, NormY, NormW, NormH, Scale, ×]
        private static readonly int[] _slotColWidths = { 100, 110, 50, 50, 50, 50, 60, 75, 24 };

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
            // Phase 175 — strategy dispatcher rule types
            "GridDim", "LevelAnnotation",
            "AutoDimMEPRun", "AutoDimMEPToGrid", "AutoSpotInvert",
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
            _rootTabs.Items.Add(MakeTab("Title Block Tools", BuildTitleBlockTab()));
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
            DockPanel.SetDock(actions, Dock.Top); dock.Children.Add(actions);

            _lbPacks = new ListBox
            {
                Background = new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                ItemTemplate = BuildPackListItemTemplate(),
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

        // Phase 177 — Core.Drawing.ViewStylePack has no custom ToString, so
        // the ListBox needs an explicit template that renders the pack's
        // identity + extends-chain breadcrumb (e.g. "corp-standard-plan ← corp-base").
        private static DataTemplate BuildPackListItemTemplate()
        {
            var dt = new DataTemplate();
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.MultiBinding
            {
                Converter = new PackListItemConverter(),
                Bindings =
                {
                    new System.Windows.Data.Binding("Id"),
                    new System.Windows.Data.Binding("Extends"),
                    new System.Windows.Data.Binding("TemplateMode"),
                }
            });
            f.SetValue(TextBlock.MarginProperty, new Thickness(4, 2, 4, 2));
            dt.VisualTree = f;
            return dt;
        }

        private sealed class PackListItemConverter : System.Windows.Data.IMultiValueConverter
        {
            public object Convert(object[] values, Type t, object p, System.Globalization.CultureInfo c)
            {
                var id = values != null && values.Length > 0 ? values[0] as string : "";
                var ext = values != null && values.Length > 1 ? values[1] as string : null;
                var mode = values != null && values.Length > 2 ? values[2] as string : null;
                var s = string.IsNullOrEmpty(id) ? "(no id)" : id;
                if (!string.IsNullOrEmpty(ext)) s += "  ←  " + ext;
                if (string.Equals(mode, "managed", StringComparison.OrdinalIgnoreCase)) s += "   ⚙";
                return s;
            }
            public object[] ConvertBack(object v, Type[] t, object p, System.Globalization.CultureInfo c)
                => throw new NotSupportedException();
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
                    Text = "STING will mint a Revit view template named 'STING:{pack-id}:{ViewType}' and bind the listed fields to its " +
                           "controlled-parameter set. Edit the pack → Save → Regenerate so every view assigned to the template picks up the change.",
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

                // Phase 177 — typo-prevention dropdowns. Visual Style is a
                // closed enum; Discipline is a closed enum; Phase Filter +
                // Phase resolve against the live project. Free-typed fall
                // through (the runtime PackPhaseFilterApplier matches by name).
                tmBody.Children.Add(LabeledCombo("Discipline",
                    new[] { "", "Architectural", "Structural", "Mechanical", "Electrical", "Plumbing", "Coordination" },
                    _currentPack.Discipline, v => _currentPack.Discipline = string.IsNullOrEmpty(v) ? null : v));
                tmBody.Children.Add(LabeledCombo("Visual style",
                    new[] { "", "Wireframe", "HiddenLine", "Shaded", "ConsistentColors", "Realistic", "RayTrace" },
                    _currentPack.VisualStyle, v => _currentPack.VisualStyle = string.IsNullOrEmpty(v) ? null : v));
                tmBody.Children.Add(LabeledCombo("Phase filter name",
                    new[] { "", "Show Complete", "Show Previous + New", "Show All", "Show New", "Show Demo + New", "None" },
                    _currentPack.PhaseFilter,
                    v => _currentPack.PhaseFilter = string.IsNullOrEmpty(v) ? null : v,
                    "Standard PhaseFilter names. Free-text allowed for project-specific filters."));
                tmBody.Children.Add(LabeledProjectAssetCombo("Phase",
                    _currentPack.Phase, v => _currentPack.Phase = v,
                    Merge(ProjectAssetPicker.PhaseNames(_doc), new[] { "Existing", "New Construction", "Demolition" }),
                    "Project phases (Phase elements) in the active document."));

                // Regenerate button — closes the gap noted in the review.
                // Dispatches DrawingTypes_RegenerateTemplates which
                // re-applies every managed pack to its STING:{pack}:{vt}
                // template under a transaction.
                var regenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                regenRow.Children.Add(MakeActionBtn("⟳ Regenerate templates", "DrawingTypes_RegenerateTemplates"));
                regenRow.Children.Add(MakeActionBtn("Convert to Managed",     "DrawingTypes_ConvertToManaged"));
                regenRow.Children.Add(MakeActionBtn("Detach from Managed",    "DrawingTypes_DetachManaged"));
                tmBody.Children.Add(regenRow);
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

            // Appearance — Phase 177: flattened onto Core.Drawing.ViewStylePack
            // (no nested PackAppearance any more). Each field round-trips
            // straight to the canonical pack POCO.
            var apBody = new StackPanel();
            apBody.Children.Add(LabeledDouble("Line-weight scale", _currentPack.LineWeightScale,
                v => _currentPack.LineWeightScale = v));
            apBody.Children.Add(LabeledProjectAssetCombo("Text style name",
                _currentPack.TextStyle, v => _currentPack.TextStyle = v,
                Merge(ProjectAssetPicker.TextStyleNames(_doc), CommonStingTextStyles),
                "TextNoteType elements in the active project, plus STING corporate text styles."));
            apBody.Children.Add(LabeledProjectAssetCombo("Dimension style name",
                _currentPack.DimensionStyle, v => _currentPack.DimensionStyle = v,
                Merge(ProjectAssetPicker.DimensionStyleNames(_doc), CommonStingDimensionStyles),
                "DimensionType elements in the active project, plus STING corporate dimension styles."));
            apBody.Children.Add(LabeledCombo("Hatch palette (informational)",
                new[] { "ISO 13567 monochrome", "ISO 13567 colour", "AIA NCS", "BS 1192 mono", "Project custom" },
                _currentPack.HatchPalette, v => _currentPack.HatchPalette = v,
                tooltip: "Informational tag for the hatch family used by this pack — does not bind to a Revit asset."));
            apBody.Children.Add(LabeledProjectAssetCombo("View template name",
                _currentPack.ViewTemplate, v => _currentPack.ViewTemplate = v,
                Merge(ProjectAssetPicker.ViewTemplateNames(_doc), CommonStingViewTemplates),
                "View templates (View.IsTemplate = true) in the active project, plus STING corporate templates."));
            apBody.Children.Add(LabeledCombo("Detail level",
                Iso19650Vocabulary.DetailLevels, _currentPack.DetailLevel,
                v => _currentPack.DetailLevel = v));
            apBody.Children.Add(LabeledCombo("Scale hint",
                new[] { "1:5", "1:10", "1:20", "1:25", "1:50", "1:100", "1:200", "1:500" },
                _currentPack.ScaleHint, v => _currentPack.ScaleHint = v,
                tooltip: "Common architectural scales. Free-text allowed if your pack needs an unusual scale."));
            apBody.Children.Add(LabeledCombo("Colour scheme",
                Iso19650Vocabulary.ColorSchemes,
                _currentPack.ColorScheme, v => _currentPack.ColorScheme = v));
            _packFormHost.Children.Add(Card("Appearance", apBody));

            // Bidirectional template copy — push the pack's view template
            // name down to every DrawingType bound to this pack, or pull
            // from the first such DrawingType up onto the pack.
            _packFormHost.Children.Add(BuildPackTemplateCopyCard());

            // Filter rules — Phase 177: bound directly to Core.Drawing.StyleFilterRule.
            var frBody = new StackPanel();
            _currentPack.Filters = _currentPack.Filters ?? new List<StyleFilterRule>();
            frBody.Children.Add(new TextBlock {
                Text = "Per-filter graphic overrides. Filter names resolve against ParameterFilterElement in the active project (corporate AEC filters lazy-create on apply). Colours in #RRGGBB. " +
                       "Inherited rows (italic) come from parent packs in the extends chain — edits land on this pack only.",
                Foreground = new SolidColorBrush(SubtleColor), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4) });
            frBody.Children.Add(MakeFilterRuleHeader());

            // Show inherited filters first (italicised, from parent packs in the
            // extends chain), then this pack's own filters. The runtime
            // applier appends both at apply time, so showing both here
            // matches what the user will actually see on a view.
            var ownFilters = _currentPack.Filters ?? new List<StyleFilterRule>();
            var inherited  = ResolveInheritedFilters(_currentPack);
            if (inherited.Count > 0)
            {
                foreach (var fr in inherited)
                    frBody.Children.Add(MakeInheritedFilterRow(fr));
                frBody.Children.Add(new TextBlock {
                    Text = $"— ↑ {inherited.Count} inherited filter row(s) — ↓ this pack's own filters —",
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 10, FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 4) });
            }
            foreach (var fr in ownFilters.ToList())
                frBody.Children.Add(MakeFilterRuleRow(fr));
            frBody.Children.Add(MakeSmallBtn("＋ Add filter rule", () =>
            {
                _currentPack.Filters.Add(new StyleFilterRule { FilterName = "NewFilter", Visible = true,
                    ProjectionLineColor = "#000000", ProjectionLineWeight = 1,
                    CutLineColor = "#000000", CutLineWeight = 1 });
                MarkPackEdited(_currentPack);
                RenderPackForm();
            }));
            _packFormHost.Children.Add(Card("Filter rules", frBody));

            // VG Overrides per category — Phase 137 + 177: full Revit VG dialog
            // replica embedded inline (no popup). Backed by a bridge dict of
            // PresetCategoryOverride that mirrors the pack's StyleVgOverride
            // dict on every cell change. Pattern + detail-level fields
            // round-trip too. The bridge walks the extends chain so child
            // packs show inherited values from corp-base / mid-tier parents.
            var vgBody = new StackPanel();
            _currentPack.VgOverrides = _currentPack.VgOverrides ?? new Dictionary<string, StyleVgOverride>();
            int chainLen = CountExtendsChainLength(_currentPack);
            if (chainLen > 1)
            {
                vgBody.Children.Add(new TextBlock {
                    Text = $"Showing resolved overrides — values rolled up from {chainLen} pack(s) in the extends chain " +
                           $"({ResolveExtendsChain(_currentPack)}). Edits land on this pack only.",
                    Foreground = new SolidColorBrush(Colors.Goldenrod), FontSize = 11,
                    FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4) });
            }
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

        // Phase 177 — extended per-category Tag Appearance grid columns:
        // Category · Style preset · Token depth (1-10) · TAG7 narrative
        // sections (A-F multi-pick) · × delete.
        private static readonly GridLength[] _packCatStyleCols =
        {
            new GridLength(170),  // Category
            new GridLength(150),  // Style preset (full 128-combo dropdown)
            new GridLength(80),   // Token depth
            new GridLength(1, GridUnitType.Star), // TAG7 sections
            new GridLength(24),   // ×
        };

        // Full 128 style preset combinations: 4 sizes × 4 styles × 8 colors.
        // Mirrors TagStyleEngine's parameter matrix so the dropdown reflects
        // every TAG_{size}{style}_{color}_BOOL the family exposes.
        private static readonly string[] _allStylePresets = BuildAllStylePresets();
        private static string[] BuildAllStylePresets()
        {
            var sizes  = new[] { "2", "2.5", "3", "3.5" };
            var styles = new[] { "NOM", "BOLD", "ITALIC", "BOLDITALIC" };
            var colors = new[] { "BLACK", "BLUE", "GREEN", "RED", "ORANGE", "GREY", "PURPLE", "YELLOW" };
            var list = new List<string> { "" };
            foreach (var sz in sizes)
                foreach (var st in styles)
                    foreach (var co in colors)
                        list.Add($"{sz}{st}_{co}");
            return list.ToArray();
        }

        // TAG7 narrative section letters — "A" through "F". Stored as a
        // single string per category (e.g. "ABCD" = sections A,B,C,D
        // visible). Empty string = use pack default visibility map.
        private static readonly string[] _tag7SectionPresets =
        {
            "", "A", "AB", "ABC", "ABCD", "ABCDE", "ABCDEF",
            "AC", "AD", "AE", "AF",
            "BC", "BD", "BE", "BF",
            "ABDF", "ABCDF",
        };

        private UIElement BuildPackCategoryTagStylesGrid(ViewStylePack p)
        {
            p.CategoryTagStyles    = p.CategoryTagStyles    ?? new Dictionary<string, string>();
            p.CategoryDepths       = p.CategoryDepths       ?? new Dictionary<string, int>();
            p.CategoryTag7Sections = p.CategoryTag7Sections ?? new Dictionary<string, string>();

            var host = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            host.Children.Add(new TextBlock {
                Text = "Per-category tag appearance. Style overrides Default tag style above; depth sets TAG7 paragraph " +
                       "depth (1-10); sections control which TAG7 narrative sub-sections (A-F) render. Empty cell = inherit pack default.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2) });

            // Header
            var header = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < _packCatStyleCols.Length; i++)
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = _packCatStyleCols[i] });
            string[] hd = { "Category", "Style preset (128)", "Depth (1-10)", "TAG7 sections (A-F)", "" };
            for (int i = 0; i < hd.Length; i++)
            {
                var t = new TextBlock { Text = hd[i], FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
                Grid.SetColumn(t, i); header.Children.Add(t);
            }
            host.Children.Add(header);

            var cats = Merge(ProjectAssetPicker.TaggableCategoryNames(_doc),
                             KnownTaggableCategories).ToArray();
            string[] depths = new[] { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

            // Union of every key referenced across the three per-category
            // dicts so a row renders even if only depth or sections is set.
            var allKeys = new SortedSet<string>(p.CategoryTagStyles.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in p.CategoryDepths.Keys)       allKeys.Add(k);
            foreach (var k in p.CategoryTag7Sections.Keys) allKeys.Add(k);

            foreach (var catKey in allKeys.ToList())
            {
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                for (int i = 0; i < _packCatStyleCols.Length; i++)
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = _packCatStyleCols[i] });

                string keyCapture = catKey;
                var keyBox = SmallCombo(catKey, newKey =>
                {
                    newKey = (newKey ?? "").Trim();
                    if (string.IsNullOrEmpty(newKey) || newKey == keyCapture) return;
                    RenameCategoryKey(p, keyCapture, newKey);
                }, cats);

                var currentStyle = p.CategoryTagStyles.TryGetValue(catKey, out var stv) ? stv : "";
                var styleBox = SmallCombo(currentStyle ?? "", v =>
                {
                    var trimmed = (v ?? "").Trim();
                    if (string.IsNullOrEmpty(trimmed)) p.CategoryTagStyles.Remove(keyCapture);
                    else p.CategoryTagStyles[keyCapture] = trimmed;
                }, _allStylePresets);

                var currentDepth = p.CategoryDepths.TryGetValue(catKey, out var dv) ? dv.ToString() : "";
                var depthBox = SmallCombo(currentDepth, v =>
                {
                    var trimmed = (v ?? "").Trim();
                    if (string.IsNullOrEmpty(trimmed)) p.CategoryDepths.Remove(keyCapture);
                    else if (int.TryParse(trimmed, out var n) && n >= 1 && n <= 10)
                        p.CategoryDepths[keyCapture] = n;
                }, depths);

                var currentTag7 = p.CategoryTag7Sections.TryGetValue(catKey, out var sv) ? sv : "";
                var tag7Box = SmallCombo(currentTag7 ?? "", v =>
                {
                    // Normalise to upper-case alpha A..F characters.
                    var clean = new string((v ?? "").ToUpperInvariant().Where(c => c >= 'A' && c <= 'F').Distinct().ToArray());
                    if (string.IsNullOrEmpty(clean)) p.CategoryTag7Sections.Remove(keyCapture);
                    else p.CategoryTag7Sections[keyCapture] = clean;
                }, _tag7SectionPresets);

                var rm = MakeSmallBtn("×", () =>
                {
                    p.CategoryTagStyles.Remove(keyCapture);
                    p.CategoryDepths.Remove(keyCapture);
                    p.CategoryTag7Sections.Remove(keyCapture);
                    RenderPackForm();
                });
                rm.Width = 22;

                var ctrls = new UIElement[] { keyBox, styleBox, depthBox, tag7Box, rm };
                for (int i = 0; i < ctrls.Length; i++)
                {
                    Grid.SetColumn(ctrls[i], i);
                    if (ctrls[i] is FrameworkElement fe)
                        fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                    g.Children.Add(ctrls[i]);
                }
                host.Children.Add(g);
            }

            host.Children.Add(MakeSmallBtn("＋ Add category appearance", () =>
            {
                var key = "NewCategory" + (p.CategoryTagStyles.Count + p.CategoryDepths.Count + p.CategoryTag7Sections.Count);
                p.CategoryTagStyles[key] = "2NOM_BLACK";
                RenderPackForm();
            }));
            return host;
        }

        // Phase 177 — rename a category key across all three per-category
        // dictionaries atomically. Drops empty entries so the rename
        // doesn't leave orphan keys behind.
        private static void RenameCategoryKey(ViewStylePack p, string oldKey, string newKey)
        {
            if (p.CategoryTagStyles.TryGetValue(oldKey, out var sv))
            {
                p.CategoryTagStyles.Remove(oldKey);
                p.CategoryTagStyles[newKey] = sv;
            }
            if (p.CategoryDepths.TryGetValue(oldKey, out var dv))
            {
                p.CategoryDepths.Remove(oldKey);
                p.CategoryDepths[newKey] = dv;
            }
            if (p.CategoryTag7Sections.TryGetValue(oldKey, out var tv))
            {
                p.CategoryTag7Sections.Remove(oldKey);
                p.CategoryTag7Sections[newKey] = tv;
            }
        }

        // ── Filter rule row ──
        private UIElement MakeFilterRuleHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            string[] headers = { "Filter name (AEC + project)", "Visible", "Halftone", "Proj-Col", "Proj-Wt", "Cut-Col", "Cut-Wt", "Trans%" };
            double[] widths  = { 200, 56, 56, 70, 50, 70, 50, 56 };
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

        private UIElement MakeFilterRuleRow(StyleFilterRule fr)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            double[] widths = { 200, 56, 56, 70, 50, 70, 50, 56 };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });

            // Phase 177 — filter-name source is the union of:
            //   1. AecFilterRegistry corporate baseline (199 filters)
            //   2. ParameterFilterElement in the active project
            //   3. Common STING corporate filters
            // Pre-sorted, deduplicated. Names that resolve against the AEC
            // registry will lazy-create on apply if not yet in the project.
            var filterNames = ResolveFilterNames();
            var name = SmallCombo(fr.FilterName, v => fr.FilterName = v, filterNames);
            var vis  = MakeChk(fr.Visible,  v => fr.Visible = v);
            var ht   = MakeChk(fr.Halftone, v => fr.Halftone = v);
            // Phase 177 — swatch clicks no longer trigger RenderPackForm:
            // a full re-render on every colour pick was eating UI focus and
            // resetting the RevitVgEditor's expand / scroll state. The
            // MakeColourSwatch helper repaints its own button content from
            // the new hex value, so a re-render is unnecessary here.
            var pc   = MakeColourSwatch(fr.ProjectionLineColor, v => { fr.ProjectionLineColor = v; MarkPackEdited(_currentPack); });
            var pw   = SmallTb(fr.ProjectionLineWeight?.ToString() ?? "",
                               v => { fr.ProjectionLineWeight = string.IsNullOrEmpty(v) ? (int?)null : TryNullableInt(v); MarkPackEdited(_currentPack); });
            var cc   = MakeColourSwatch(fr.CutLineColor, v => { fr.CutLineColor = v; MarkPackEdited(_currentPack); });
            var cw   = SmallTb(fr.CutLineWeight?.ToString() ?? "",
                               v => { fr.CutLineWeight = string.IsNullOrEmpty(v) ? (int?)null : TryNullableInt(v); MarkPackEdited(_currentPack); });
            var tr   = SmallTb(fr.Transparency?.ToString() ?? "",
                               v => { fr.Transparency = string.IsNullOrEmpty(v) ? (int?)null : TryNullableInt(v); MarkPackEdited(_currentPack); });

            var ctrls = new UIElement[] { name, vis, ht, pc, pw, cc, cw, tr };
            for (int i = 0; i < ctrls.Length; i++)
            {
                Grid.SetColumn(ctrls[i], i);
                if (ctrls[i] is FrameworkElement fe) fe.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
                g.Children.Add(ctrls[i]);
            }
            return g;
        }

        // Phase 177 — collect filters declared by every parent in the
        // extends chain so the editor can render them as read-only
        // inherited rows. Returns empty when the pack has no parent
        // or no parents declare filters.
        private List<StyleFilterRule> ResolveInheritedFilters(ViewStylePack pack)
        {
            var result = new List<StyleFilterRule>();
            if (pack == null || string.IsNullOrEmpty(pack.Extends)) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pack.Id ?? "" };
            var cur = _packs?.FirstOrDefault(p => string.Equals(p.Id, pack.Extends, StringComparison.OrdinalIgnoreCase));
            int guard = 16;
            while (cur != null && guard-- > 0)
            {
                if (string.IsNullOrEmpty(cur.Id) || !seen.Add(cur.Id)) break;
                if (cur.Filters != null) result.AddRange(cur.Filters);
                if (string.IsNullOrEmpty(cur.Extends)) break;
                cur = _packs?.FirstOrDefault(p => string.Equals(p.Id, cur.Extends, StringComparison.OrdinalIgnoreCase));
            }
            return result;
        }

        // Phase 177 — render an inherited filter row read-only with an
        // italic style hint so the user knows it came from a parent pack
        // and any edit needs to either override on this pack or be made
        // on the parent.
        private UIElement MakeInheritedFilterRow(StyleFilterRule fr)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), Opacity = 0.65 };
            double[] widths = { 200, 56, 56, 70, 50, 70, 50, 56 };
            for (int i = 0; i < widths.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });
            string[] cells =
            {
                fr.FilterName ?? "",
                fr.Visible  ? "✓" : "",
                fr.Halftone ? "✓" : "",
                fr.ProjectionLineColor ?? "",
                fr.ProjectionLineWeight?.ToString() ?? "",
                fr.CutLineColor ?? "",
                fr.CutLineWeight?.ToString() ?? "",
                fr.Transparency?.ToString() ?? "",
            };
            for (int i = 0; i < cells.Length; i++)
            {
                var t = new TextBlock {
                    Text = cells[i] ?? "",
                    FontStyle = FontStyles.Italic,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(t, i); g.Children.Add(t);
            }
            g.ToolTip = "Inherited from a parent pack. Edit the parent to change, or add an override row on this pack.";
            return g;
        }

        // Phase 177 — filter-name dropdown source. Pulls from both the AEC
        // corporate-baseline registry (so filters lazy-create on apply when
        // they don't yet exist in the project) and the active project's
        // ParameterFilterElement collector. Falls back to CommonStingFilters
        // statics when neither source is available.
        private string[] ResolveFilterNames()
        {
            try
            {
                var aec = AecFilterRegistry.ListAll(_doc) ?? Array.Empty<AecFilterDefinition>();
                var aecNames = aec.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n));
                var live = ProjectAssetPicker.ParameterFilterNames(_doc) ?? Array.Empty<string>();
                return Merge(aecNames.Concat(live), CommonStingFilters).ToArray();
            }
            catch
            {
                return Merge(ProjectAssetPicker.ParameterFilterNames(_doc), CommonStingFilters).ToArray();
            }
        }

        // ── Phase 137 — VG bridge between PackCategoryOverride (the editor's
        //    on-disk JSON shape) and PresetCategoryOverride (the full-fidelity
        //    in-memory model the inline RevitVgEditor binds to). The bridge
        //    is built once when the pack form renders, lives in memory for
        //    the editor, and is synced back to the pack on every RowChanged
        //    event so the user's edits survive Save without an explicit
        //    "apply" step. ──

        // Phase 177 — bridge build now:
        //   1. Walks the extends chain (root → … → current) so child packs
        //      display the resolved/effective override set, not just their
        //      own deltas. Child entries overwrite parent entries.
        //   2. Normalises every JSON key to a BIC ("Walls" → "OST_Walls")
        //      via RevitCategoryTree.FindByDisplayName, falling back to the
        //      raw key when the lookup misses (subcategories, BIC strings
        //      already in canonical form, or unknown categories).
        // The result: opening any child pack in the editor now reveals the
        // line colours, weights, halftone, pattern overrides, etc. it will
        // actually apply at runtime — instead of a sea of empty rows.
        private Dictionary<string, StingTools.Core.Drawing.PresetCategoryOverride>
            BuildVgBridge(ViewStylePack pack)
        {
            var bridge = new Dictionary<string, StingTools.Core.Drawing.PresetCategoryOverride>(StringComparer.OrdinalIgnoreCase);
            if (pack == null) return bridge;

            // Resolve ancestors so corp-base ↑ corp-standard-plan ↑ corp-standard-rcp
            // gets walked once, root-first, with the active pack last so its
            // values win on conflicting keys.
            var chain = new List<ViewStylePack>();
            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = pack;
            int guard = 16;
            while (cur != null && guard-- > 0)
            {
                if (string.IsNullOrEmpty(cur.Id) || !seen.Add(cur.Id)) break;
                chain.Add(cur);
                if (string.IsNullOrEmpty(cur.Extends)) break;
                cur = _packs?.FirstOrDefault(p =>
                    string.Equals(p.Id, cur.Extends, StringComparison.OrdinalIgnoreCase));
            }
            chain.Reverse(); // root first; current pack overrides last

            foreach (var p in chain)
            {
                if (p.VgOverrides == null) continue;
                foreach (var kv in p.VgOverrides)
                {
                    var v = kv.Value;
                    if (v == null) continue;
                    var key = NormaliseCategoryKey(kv.Key);
                    bridge[key] = new StingTools.Core.Drawing.PresetCategoryOverride
                    {
                        Category        = kv.Key,
                        Visible         = v.Visible,
                        Halftone        = v.Halftone,
                        ProjLineColor   = v.ProjectionLineColor,
                        ProjLineWeight  = v.ProjectionLineWeight,
                        ProjLinePattern = v.ProjectionLinePattern,
                        CutLineColor    = v.CutLineColor,
                        CutLineWeight   = v.CutLineWeight,
                        CutLinePattern  = v.CutLinePattern,
                        SurfFgColor     = v.SurfaceFgColor,
                        SurfFgPattern   = v.SurfaceFgPattern,
                        SurfFgVisible   = v.SurfaceFgVisible,
                        SurfBgColor     = v.SurfaceBgColor,
                        SurfBgPattern   = v.SurfaceBgPattern,
                        SurfBgVisible   = v.SurfaceBgVisible,
                        CutFgColor      = v.CutFgColor,
                        CutFgPattern    = v.CutFgPattern,
                        CutFgVisible    = v.CutFgVisible,
                        CutBgColor      = v.CutBgColor,
                        CutBgPattern    = v.CutBgPattern,
                        Transparency    = v.Transparency,
                        DetailLevel     = v.DetailLevel,
                    };
                }
            }
            return bridge;
        }

        // Phase 177 — translate a pack JSON category key into the BIC the
        // RevitVgEditor's row builder uses. Accepts:
        //   * Already-canonical BIC strings  ("OST_Walls" → unchanged)
        //   * Localised display names        ("Walls"     → "OST_Walls")
        //   * Subcategory paths              ("Walls/Wall Surface" — first
        //     segment translated, second left alone)
        // Returns the input verbatim when no match is found so the user's
        // hand-authored key is preserved on round-trip.
        private static string NormaliseCategoryKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            // Subcategory path: "Parent/Sub"
            int slash = key.IndexOf('/');
            if (slash > 0)
            {
                var parent = key.Substring(0, slash);
                var sub    = key.Substring(slash + 1);
                var pBic = NormaliseSingle(parent);
                return pBic + "/" + sub;
            }
            return NormaliseSingle(key);
        }

        private static string NormaliseSingle(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            // Already a BIC string?
            if (key.StartsWith("OST_", StringComparison.OrdinalIgnoreCase)) return key;
            // Try the catalogue
            var cat = RevitCategoryTree.FindByDisplayName(key);
            return !string.IsNullOrEmpty(cat?.Bic) ? cat.Bic : key;
        }

        /// <summary>
        /// Phase 177 — push a single edited row from the inline VG editor
        /// back into the pack's StyleVgOverride dict. Pattern + detail-level
        /// fields round-trip too. The write-back key prefers the user-
        /// friendly display name ("Walls") over the BIC ("OST_Walls") so
        /// hand-authored JSON stays human-readable, and the editor's first
        /// edit on a corporate pack flips Origin to "project" so Save
        /// actually persists.
        /// </summary>
        private void SyncRowToPack(ViewStylePack pack, RevitVgEditor.VgRow r)
        {
            if (pack == null || r?.Data == null) return;
            pack.VgOverrides = pack.VgOverrides ?? new Dictionary<string, StyleVgOverride>();
            var v = r.Data;
            bool any =
                   v.Visible.HasValue || v.Halftone.HasValue
                || !string.IsNullOrEmpty(v.ProjLineColor)   || v.ProjLineWeight.HasValue
                || !string.IsNullOrEmpty(v.ProjLinePattern)
                || !string.IsNullOrEmpty(v.CutLineColor)    || v.CutLineWeight.HasValue
                || !string.IsNullOrEmpty(v.CutLinePattern)
                || !string.IsNullOrEmpty(v.SurfFgColor)     || !string.IsNullOrEmpty(v.SurfFgPattern) || v.SurfFgVisible.HasValue
                || !string.IsNullOrEmpty(v.SurfBgColor)     || !string.IsNullOrEmpty(v.SurfBgPattern) || v.SurfBgVisible.HasValue
                || !string.IsNullOrEmpty(v.CutFgColor)      || !string.IsNullOrEmpty(v.CutFgPattern)  || v.CutFgVisible.HasValue
                || !string.IsNullOrEmpty(v.CutBgColor)      || !string.IsNullOrEmpty(v.CutBgPattern)
                || v.Transparency.HasValue
                || !string.IsNullOrEmpty(v.DetailLevel);

            // Resolve which key form the pack already uses for this row so
            // we update the existing entry instead of creating a duplicate
            // under a different key.
            var packKey = ResolveExistingPackKey(pack, r) ?? PreferredPackKey(r);
            if (!any)
            {
                pack.VgOverrides.Remove(packKey);
                MarkPackEdited(pack);
                return;
            }
            pack.VgOverrides[packKey] = new StyleVgOverride
            {
                Visible              = v.Visible,
                Halftone             = v.Halftone,
                ProjectionLineColor  = v.ProjLineColor,
                ProjectionLineWeight = v.ProjLineWeight,
                ProjectionLinePattern= v.ProjLinePattern,
                CutLineColor         = v.CutLineColor,
                CutLineWeight        = v.CutLineWeight,
                CutLinePattern       = v.CutLinePattern,
                SurfaceFgColor       = v.SurfFgColor,
                SurfaceFgPattern     = v.SurfFgPattern,
                SurfaceFgVisible     = v.SurfFgVisible,
                SurfaceBgColor       = v.SurfBgColor,
                SurfaceBgPattern     = v.SurfBgPattern,
                SurfaceBgVisible     = v.SurfBgVisible,
                CutFgColor           = v.CutFgColor,
                CutFgPattern         = v.CutFgPattern,
                CutFgVisible         = v.CutFgVisible,
                CutBgColor           = v.CutBgColor,
                CutBgPattern         = v.CutBgPattern,
                Transparency         = v.Transparency,
                DetailLevel          = v.DetailLevel,
            };
            MarkPackEdited(pack);
        }

        // Phase 177 — find whichever key form (BIC, display name, sub-path)
        // the pack already uses for this row, so we update that entry instead
        // of creating a parallel duplicate.
        private static string ResolveExistingPackKey(ViewStylePack pack, RevitVgEditor.VgRow r)
        {
            if (pack?.VgOverrides == null || pack.VgOverrides.Count == 0) return null;
            // Try BIC, BIC/sub, name, name/sub.
            var candidates = new List<string>(4);
            if (!string.IsNullOrEmpty(r.Bic))
            {
                candidates.Add(string.IsNullOrEmpty(r.SubCategoryName) ? r.Bic : $"{r.Bic}/{r.SubCategoryName}");
            }
            if (!string.IsNullOrEmpty(r.DisplayName))
            {
                candidates.Add(string.IsNullOrEmpty(r.SubCategoryName) ? r.DisplayName : $"{r.DisplayName}/{r.SubCategoryName}");
            }
            // Display name of the parent for subcategory rows
            if (!string.IsNullOrEmpty(r.Bic) && !string.IsNullOrEmpty(r.SubCategoryName))
            {
                var parentDisplay = RevitCategoryTree.FindByBic(r.Bic)?.DisplayName;
                if (!string.IsNullOrEmpty(parentDisplay))
                    candidates.Add($"{parentDisplay}/{r.SubCategoryName}");
            }
            foreach (var c in candidates)
                if (pack.VgOverrides.ContainsKey(c)) return c;
            return null;
        }

        // Phase 177 — when the pack doesn't yet have an entry for this row,
        // pick the friendlier display name ("Walls") over the BIC
        // ("OST_Walls") so the resulting JSON stays human-readable. Falls
        // back to BIC when no display name is available.
        private static string PreferredPackKey(RevitVgEditor.VgRow r)
        {
            string head = !string.IsNullOrEmpty(r.DisplayName) ? r.DisplayName : r.Bic;
            return string.IsNullOrEmpty(r.SubCategoryName) ? head : $"{head}/{r.SubCategoryName}";
        }

        // Phase 177 — first edit on a corporate-origin pack flips Origin to
        // "project" so SavePacksToProjectOverride actually persists the edit.
        // Without this, the editor would silently discard every change a
        // user made to a corp-base pack on Save.
        private static void MarkPackEdited(ViewStylePack pack)
        {
            if (pack == null) return;
            if (string.Equals(pack.Origin, "corporate", StringComparison.OrdinalIgnoreCase))
                pack.Origin = "project";
        }

        // ── Helpers for the filter-rule grid ──
        // The legacy compact MakeVgHeader / MakeVgRow have been retired —
        // the embedded RevitVgEditor (full Revit-VG-dialog replica) is now
        // the only category-override surface in the pack form. See Phase 177.

        private CheckBox MakeChk(bool value, Action<bool> setter)
        {
            var cb = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center };
            cb.Checked   += (s, e) => setter?.Invoke(true);
            cb.Unchecked += (s, e) => setter?.Invoke(false);
            return cb;
        }

        private static int TryInt(string s) => int.TryParse(s, out var n) ? n : 0;

        // Phase 177 — strict parser that returns null on un-parseable input
        // so the user's "abc" doesn't silently become weight 0 (a meaningful
        // override) when they meant "leave blank".
        private static int? TryNullableInt(string s)
            => int.TryParse(s, out var n) ? (int?)n : null;

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

        // Phase 177 — count packs in the extends chain (current + ancestors).
        // Used to decide whether to show the "rolled up from N packs" notice
        // on the VG overrides card.
        private int CountExtendsChainLength(ViewStylePack p)
        {
            if (p == null) return 0;
            int n = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cur = p;
            while (cur != null && n < 16)
            {
                if (string.IsNullOrEmpty(cur.Id) || !seen.Add(cur.Id)) break;
                n++;
                if (string.IsNullOrEmpty(cur.Extends)) break;
                cur = _packs?.FirstOrDefault(x => string.Equals(x.Id, cur.Extends, StringComparison.OrdinalIgnoreCase));
            }
            return n;
        }

        // Phase 177 — pack-side bidirectional template copy. "↓ Push to bound
        // types" walks every DrawingType currently bound to this pack and
        // overwrites their view-template / detail level / scale / colour
        // scheme. "↑ Pull from a bound type" copies up from the first
        // matching DrawingType so the pack mirrors a known-good profile.
        private UIElement BuildPackTemplateCopyCard()
        {
            var body = new StackPanel();
            var bound = ListBoundDrawingTypeIds(_currentPack);
            body.Children.Add(new TextBlock {
                Text = bound.Count == 0
                    ? "No Drawing Types currently reference this pack id."
                    : $"Bound Drawing Types ({bound.Count}): {string.Join(", ", bound)}",
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 0, 4) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(MakeSmallBtn("↓ Push to bound types", () => PushPackTemplateToBoundTypes()));
            row.Children.Add(MakeSmallBtn("↑ Pull from bound type", () => PullPackTemplateFromBoundType()));
            body.Children.Add(row);
            return Card("Template copy (Drawing Type ↔ Pack)", body);
        }

        private List<string> ListBoundDrawingTypeIds(ViewStylePack pack)
        {
            if (pack == null || string.IsNullOrEmpty(pack.Id)) return new List<string>();
            return (_types ?? Enumerable.Empty<DrawingType>())
                .Where(t => string.Equals(t.ViewStylePackId, pack.Id, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Id)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void PushPackTemplateToBoundTypes()
        {
            if (_currentPack == null || string.IsNullOrEmpty(_currentPack.Id))
            {
                System.Windows.MessageBox.Show("Pack has no id.", "STING", MessageBoxButton.OK);
                return;
            }
            var bound = (_types ?? Enumerable.Empty<DrawingType>())
                .Where(t => string.Equals(t.ViewStylePackId, _currentPack.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bound.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "No Drawing Types reference this pack. Bind one first via the View Style Pack id combo on the Drawing Types tab.",
                    "STING — Push to bound types", MessageBoxButton.OK);
                return;
            }
            int touched = 0;
            foreach (var t in bound)
            {
                if (!string.IsNullOrEmpty(_currentPack.ViewTemplate)) { t.ViewTemplateName = _currentPack.ViewTemplate; touched++; }
                if (!string.IsNullOrEmpty(_currentPack.DetailLevel))  { t.DetailLevel       = _currentPack.DetailLevel;  touched++; }
                if (!string.IsNullOrEmpty(_currentPack.ScaleHint) && TryParseScaleHint(_currentPack.ScaleHint, out var s))
                                                                       { t.Scale             = s;                          touched++; }
                if (!string.IsNullOrEmpty(_currentPack.ColorScheme))
                {
                    t.TokenProfile = t.TokenProfile ?? new AnnotationTokenProfile();
                    t.TokenProfile.ColorScheme = _currentPack.ColorScheme;
                    touched++;
                }
                if (string.Equals(t.Origin, "corporate", StringComparison.OrdinalIgnoreCase))
                    t.Origin = "project";
            }
            System.Windows.MessageBox.Show(
                $"Wrote {touched} field(s) across {bound.Count} bound Drawing Type(s). Save the Drawing Types tab to persist.",
                "STING — Push to bound types", MessageBoxButton.OK);
        }

        private void PullPackTemplateFromBoundType()
        {
            if (_currentPack == null) return;
            var first = (_types ?? Enumerable.Empty<DrawingType>())
                .FirstOrDefault(t => string.Equals(t.ViewStylePackId, _currentPack.Id, StringComparison.OrdinalIgnoreCase));
            if (first == null)
            {
                System.Windows.MessageBox.Show("No Drawing Type references this pack.", "STING", MessageBoxButton.OK);
                return;
            }
            int n = 0;
            if (!string.IsNullOrEmpty(first.ViewTemplateName)) { _currentPack.ViewTemplate = first.ViewTemplateName; n++; }
            if (!string.IsNullOrEmpty(first.DetailLevel))      { _currentPack.DetailLevel  = first.DetailLevel;       n++; }
            if (first.Scale > 0)                                { _currentPack.ScaleHint    = $"1:{first.Scale}";       n++; }
            var cs = first.TokenProfile?.ColorScheme;
            if (!string.IsNullOrEmpty(cs))                     { _currentPack.ColorScheme  = cs;                       n++; }
            if (string.Equals(_currentPack.Origin, "corporate", StringComparison.OrdinalIgnoreCase))
                _currentPack.Origin = "project";
            RenderPackForm();
            System.Windows.MessageBox.Show(
                $"Copied {n} field(s) from Drawing Type '{first.Id}' onto pack '{_currentPack.Id}'.",
                "STING — Pull from bound type", MessageBoxButton.OK);
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
                Filters = new List<StyleFilterRule>(),
                VgOverrides = new Dictionary<string, StyleVgOverride>(),
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

        // ── JSON load ──
        // Phase 177 — load via the canonical Core.Drawing.ViewStylePackLibrary
        // so we round-trip the full schema (managed mode, filters, pattern
        // overrides, all Phase 137+ fields) instead of the truncated nested
        // POCOs that used to live here.
        // Phase 177 — load corporate baseline AND layer the project override
        // on top by id (project entries replace same-id corporate entries;
        // ids only present in project get appended). Mirrors what
        // ViewStylePackRegistry.GetLibrary does at runtime so the editor
        // shows the same union the runtime sees. The earlier version
        // returned ONLY the project override file when it existed, hiding
        // every corporate pack from the editor — a critical regression.
        private List<ViewStylePack> LoadViewStylePacks()
        {
            var list = new List<ViewStylePack>();
            try
            {
                // 1. Corporate baseline (read-only on disk, always loaded)
                var byId = new Dictionary<string, ViewStylePack>(StringComparer.OrdinalIgnoreCase);
                var path = Path.Combine(StingTools.Core.StingToolsApp.DataPath ?? "", "STING_VIEW_STYLE_PACKS.json");
                if (File.Exists(path))
                {
                    var corpLib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path));
                    if (corpLib?.Packs != null)
                    {
                        foreach (var p in corpLib.Packs)
                        {
                            if (string.IsNullOrEmpty(p?.Id)) continue;
                            if (string.IsNullOrEmpty(p.Origin)) p.Origin = "corporate";
                            byId[p.Id] = p;
                        }
                    }
                }

                // 2. Project override layered on top (same-id entries
                //    replace corporate; new ids get appended).
                string proj = null;
                try { proj = ResolveProjectPackOverridePath(); } catch { }
                if (!string.IsNullOrEmpty(proj) && File.Exists(proj))
                {
                    var pLib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(proj));
                    if (pLib?.Packs != null)
                    {
                        foreach (var p in pLib.Packs)
                        {
                            if (string.IsNullOrEmpty(p?.Id)) continue;
                            if (string.IsNullOrEmpty(p.Origin)) p.Origin = "project";
                            byId[p.Id] = p; // project wins
                        }
                    }
                }

                list.AddRange(byId.Values);
                return list;
            }
            catch (Exception ex) { StingLog.Warn("ViewStylePacks load: " + ex.Message); return list; }
        }

        // Project-scoped override file. Mirrors DrawingTypeRegistry's project
        // override path resolution so corporate baseline stays pristine.
        private string ResolveProjectPackOverridePath()
        {
            try
            {
                if (_doc == null || string.IsNullOrEmpty(_doc.PathName)) return null;
                var dir = Path.GetDirectoryName(_doc.PathName);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "_BIM_COORD", "view_style_packs.json");
            }
            catch { return null; }
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

            stack.Children.Add(TabIntro("Title Block Tools",
                "Single source of truth for every title-block action. " +
                "Authoring writes to TITLE_BLOCK.csv and PRJ_TB_* shared parameters. " +
                "Sheet identity / Revision / Transmittal stamp the live ViewSheet. " +
                "Suitability codes follow BS EN ISO 19650-1 §A (S0–S4 / A1–A5 / B1–B6 / AR)."));

            stack.Children.Add(InfoCard(
                "Per-drawing-type title block parameter bindings (titleBlockParams) " +
                "are configured on the Drawing Types tab → right-panel form → " +
                "'Title block parameters' card. Select a drawing type in the list " +
                "on the left, then scroll to the Title block parameters card to " +
                "add or edit param → value template mappings. The Resolved preview " +
                "column shows what each template will produce from the current " +
                "project's ProjectInformation."));

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
            return Card("Identity", body);
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
            body.Children.Add(LabeledTextBox("Title block symbol type (optional)",
                _current.TitleBlockSymbolType, v => _current.TitleBlockSymbolType = v,
                "Specific FamilySymbol Name within the family (e.g. 'A1 - 594x841 Landscape'). Leave empty to use the first symbol of the family."));
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
            body.Children.Add(LabeledProjectAssetCombo("View template name",
                _current.ViewTemplateName, v => _current.ViewTemplateName = v,
                ProjectAssetPicker.ViewTemplateNames(_doc),
                "View templates (View.IsTemplate = true) in the active project."));
            body.Children.Add(LabeledProjectAssetCombo("Viewport type name",
                _current.ViewportTypeName, v => _current.ViewportTypeName = v,
                ProjectAssetPicker.ViewportTypeNames(_doc),
                "ElementType where Category = OST_Viewports in the active project."));

            // Phase 177 — ViewStylePackId binding. Closes the gap noted in the
            // Phase 136 docs: editor now offers a dropdown of every pack id
            // loaded on the View Style Packs tab. Empty = no pack binding.
            var packIds = new[] { "" }.Concat(_packs?.Select(p => p.Id) ?? Enumerable.Empty<string>())
                .Where(s => s != null).ToArray();
            body.Children.Add(LabeledCombo("View Style Pack id",
                packIds,
                _current.ViewStylePackId ?? "",
                v => _current.ViewStylePackId = string.IsNullOrWhiteSpace(v) ? null : v.Trim(),
                tooltip: "Pack the runtime resolves after the profile-level template/scale, supplying shared filters, " +
                         "VG overrides, and tag appearance defaults. Empty = no pack binding."));

            // Bidirectional template copy — DrawingType ↔ Pack
            var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            copyRow.Children.Add(MakeSmallBtn("← Use pack template", () => UsePackTemplateOnDrawingType()));
            copyRow.Children.Add(MakeSmallBtn("↑ Push to pack",       () => PushDrawingTypeTemplateToPack()));
            body.Children.Add(copyRow);
            body.Children.Add(new TextBlock {
                Text = "← copies the pack's view template / detail level / scale hint / colour scheme into this Drawing Type. " +
                       "↑ does the reverse (this profile's values become the pack default).",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

            return Card("Views", body);
        }

        // Phase 177 — copy pack-level template fields onto the active
        // DrawingType. Pack ViewTemplate / DetailLevel / ScaleHint /
        // ColorScheme overwrite the profile's matching fields when set.
        private void UsePackTemplateOnDrawingType()
        {
            if (_current == null) return;
            var pack = ResolveBoundPack(_current);
            if (pack == null)
            {
                System.Windows.MessageBox.Show(
                    "This Drawing Type isn't bound to a pack. Pick a View Style Pack id first.",
                    "STING — Use pack template", MessageBoxButton.OK);
                return;
            }
            int n = 0;
            if (!string.IsNullOrEmpty(pack.ViewTemplate)) { _current.ViewTemplateName = pack.ViewTemplate; n++; }
            if (!string.IsNullOrEmpty(pack.DetailLevel))  { _current.DetailLevel       = pack.DetailLevel;  n++; }
            if (!string.IsNullOrEmpty(pack.ScaleHint) && TryParseScaleHint(pack.ScaleHint, out var s))
                                                          { _current.Scale             = s;                 n++; }
            // ColorScheme lives on AnnotationTokenProfile; create if absent.
            if (!string.IsNullOrEmpty(pack.ColorScheme))
            {
                _current.TokenProfile = _current.TokenProfile ?? new AnnotationTokenProfile();
                _current.TokenProfile.ColorScheme = pack.ColorScheme;
                n++;
            }
            RenderForm();
            System.Windows.MessageBox.Show(
                $"Copied {n} field(s) from pack '{pack.Id}' onto Drawing Type '{_current.Id}'.",
                "STING — Use pack template", MessageBoxButton.OK);
        }

        // Phase 177 — reverse direction: profile values become the pack
        // default. Only writes pack fields when the profile has a value;
        // empty profile fields leave the pack alone.
        private void PushDrawingTypeTemplateToPack()
        {
            if (_current == null) return;
            var pack = ResolveBoundPack(_current);
            if (pack == null)
            {
                System.Windows.MessageBox.Show(
                    "This Drawing Type isn't bound to a pack. Pick a View Style Pack id first.",
                    "STING — Push to pack", MessageBoxButton.OK);
                return;
            }
            int n = 0;
            if (!string.IsNullOrEmpty(_current.ViewTemplateName)) { pack.ViewTemplate = _current.ViewTemplateName; n++; }
            if (!string.IsNullOrEmpty(_current.DetailLevel))      { pack.DetailLevel  = _current.DetailLevel;       n++; }
            if (_current.Scale > 0)                                { pack.ScaleHint    = $"1:{_current.Scale}";       n++; }
            var cs = _current.TokenProfile?.ColorScheme;
            if (!string.IsNullOrEmpty(cs))                        { pack.ColorScheme  = cs;                          n++; }
            // Pushing onto a corporate-baseline pack flips it to project origin.
            if (string.Equals(pack.Origin, "corporate", StringComparison.OrdinalIgnoreCase))
                pack.Origin = "project";
            System.Windows.MessageBox.Show(
                $"Wrote {n} field(s) from Drawing Type '{_current.Id}' onto pack '{pack.Id}'. " +
                $"Save the View Style Packs tab to persist.",
                "STING — Push to pack", MessageBoxButton.OK);
        }

        private ViewStylePack ResolveBoundPack(DrawingType dt)
        {
            if (dt == null || string.IsNullOrEmpty(dt.ViewStylePackId)) return null;
            return _packs?.FirstOrDefault(p =>
                string.Equals(p.Id, dt.ViewStylePackId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseScaleHint(string hint, out int scale)
        {
            scale = 0;
            if (string.IsNullOrEmpty(hint)) return false;
            var s = hint.Trim();
            if (s.StartsWith("1:")) s = s.Substring(2);
            return int.TryParse(s, out scale) && scale > 0;
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

        // Phase 167 — Title-block parameter binding card with resolved preview.
        // Each row maps a TB FamilyInstance parameter name to a value template;
        // the third column previews what the template resolves to against the
        // active project's ProjectInformation.
        private UIElement BuildTitleBlockParamsCard()
        {
            var body = new StackPanel();
            _current.TitleBlockParams = _current.TitleBlockParams
                ?? new System.Collections.Generic.Dictionary<string, string>();

            var unbound = new System.Collections.Generic.HashSet<string>(
                StingTools.Core.Drawing.TitleBlockParamApplier
                    .FindMissingProjectInfoParams(_doc, _current),
                StringComparer.OrdinalIgnoreCase);

            // Phase 169 — preview controls row: discipline picker + seq spinner
            // drive the live Resolved column, and an Insert-token affordance
            // helps the operator discover available {tokens} + |filters.
            var previewBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
            previewBar.Children.Add(new TextBlock {
                Text = "Preview as:", VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11, Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 6, 0) });
            string[] discs = { "", "A", "S", "M", "E", "P", "FP", "L" };
            if (string.IsNullOrEmpty(_previewDisc) && !string.IsNullOrEmpty(_current.Discipline))
                _previewDisc = _current.Discipline;
            var discCombo = SmallCombo(_previewDisc ?? "",
                v => { _previewDisc = v ?? ""; RenderForm(); },
                discs);
            discCombo.Width = 90; discCombo.Margin = new Thickness(0, 0, 12, 0);
            previewBar.Children.Add(discCombo);
            previewBar.Children.Add(new TextBlock {
                Text = "{seq}:", VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11, Foreground = new SolidColorBrush(SubtleColor),
                Margin = new Thickness(0, 0, 6, 0) });
            var seqTb = SmallTb(_previewSeq.ToString(), v =>
            {
                if (int.TryParse(v, out var n) && n != _previewSeq) { _previewSeq = n; RenderForm(); }
            });
            seqTb.Width = 60; seqTb.Margin = new Thickness(0, 0, 12, 0);
            previewBar.Children.Add(seqTb);
            body.Children.Add(previewBar);

            // Build the token dict the Resolved column will use.
            var previewTokens = StingTools.Core.Drawing.DrawingTokenContext.Build(
                doc:        _doc,
                dt:         _current,
                discCode:   string.IsNullOrEmpty(_previewDisc) ? _current.Discipline : _previewDisc,
                discipline: string.IsNullOrEmpty(_previewDisc) ? _current.Discipline : _previewDisc,
                seq:        _previewSeq);

            // Header row
            var hdr = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            double[] cols = { 200, 220, 24, 160, 30 };
            for (int c = 0; c < cols.Length; c++)
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cols[c]) });
            string[] headers = { "Param name", "Value template", "+", "Resolved", "" };
            for (int i = 0; i < headers.Length; i++)
            {
                var t = new TextBlock {
                    Text = headers[i], FontFamily = new FontFamily("Consolas"),
                    FontSize = 10, Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                };
                Grid.SetColumn(t, i);
                hdr.Children.Add(t);
            }
            body.Children.Add(hdr);

            foreach (var kv in _current.TitleBlockParams.ToList())
            {
                var key = kv.Key;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                for (int c = 0; c < cols.Length; c++)
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cols[c]) });

                var keyTb = SmallTb(key, newKey =>
                {
                    if (string.IsNullOrWhiteSpace(newKey) || newKey == key) return;
                    var v = _current.TitleBlockParams[key];
                    _current.TitleBlockParams.Remove(key);
                    _current.TitleBlockParams[newKey] = v;
                    RenderForm();
                });
                Grid.SetColumn(keyTb, 0);
                row.Children.Add(keyTb);

                // Value template TextBox — Phase 169 live preview.
                var valTb = new TextBox {
                    Text = kv.Value ?? "", Height = 20, FontSize = 10 };
                DarkDialogTheme.StyleInput(valTb, CardBg, FgColor, CardBorder);
                Grid.SetColumn(valTb, 1);
                row.Children.Add(valTb);

                // Token / filter insert button — opens an autocomplete-style
                // ContextMenu listing canonical tokens + filters. Inserts at
                // current caret position so chains like "{disc|upper}" can
                // be assembled without typing the whole grammar.
                var insertBtn = new Button
                {
                    Content = "+", Height = 20, Width = 20, FontSize = 10,
                    Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0),
                    Background = new SolidColorBrush(CardBg),
                    Foreground = new SolidColorBrush(FgColor),
                    BorderBrush = new SolidColorBrush(CardBorder),
                    BorderThickness = new Thickness(1),
                    ToolTip = "Insert token or filter at the cursor position",
                };
                insertBtn.Click += (_, __) => ShowTokenInsertMenu(insertBtn, valTb);
                Grid.SetColumn(insertBtn, 2);
                row.Children.Add(insertBtn);

                // Resolved preview — captured reference so live keystrokes
                // re-resolve without rebuilding the whole form.
                var preview = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(SubtleColor),
                    Margin = new Thickness(4, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(preview, 3);
                row.Children.Add(preview);

                Action<string> updatePreview = (tpl) =>
                {
                    bool referencesProj = (tpl ?? "").IndexOf("${", StringComparison.Ordinal) >= 0;
                    bool anyUnbound = referencesProj && unbound.Any(u =>
                        (tpl ?? "").IndexOf("${" + u + "}", StringComparison.Ordinal) >= 0);
                    string txt;
                    if (anyUnbound) txt = "(param not bound)";
                    else
                    {
                        try
                        {
                            var tinyDt = new StingTools.Core.Drawing.DrawingType
                            {
                                TitleBlockParams = new Dictionary<string, string> { { "_p", tpl ?? "" } }
                            };
                            var peek = StingTools.Core.Drawing.TitleBlockParamApplier
                                .Peek(_doc, tinyDt, previewTokens);
                            txt = peek.TryGetValue("_p", out var v) ? v : "";
                        }
                        catch { txt = "(error)"; }
                    }
                    preview.Text = txt;
                    preview.ToolTip = txt;
                    preview.FontStyle = anyUnbound ? FontStyles.Italic : FontStyles.Normal;
                };
                updatePreview(kv.Value);
                valTb.TextChanged += (_, __) =>
                {
                    _current.TitleBlockParams[key] = valTb.Text ?? "";
                    updatePreview(valTb.Text);
                };

                var rm = MakeSmallBtn("×", () =>
                {
                    _current.TitleBlockParams.Remove(key);
                    RenderForm();
                });
                Grid.SetColumn(rm, 4);
                row.Children.Add(rm);

                body.Children.Add(row);
            }

            body.Children.Add(MakeSmallBtn("＋ Add parameter", () =>
            {
                int n = 1;
                string newKey = "PARAM_" + n;
                while (_current.TitleBlockParams.ContainsKey(newKey)) { n++; newKey = "PARAM_" + n; }
                _current.TitleBlockParams[newKey] = "";
                RenderForm();
            }));

            return Card("Title block parameters", body);
        }

        // Phase 169 — autocomplete-style insert menu listing every canonical
        // token and filter the resolver knows about. Inserts at the caret
        // (or replaces selection) so the operator never has to memorise the
        // grammar. Items in 4 sections so the menu is scannable.
        private void ShowTokenInsertMenu(System.Windows.Controls.Button anchor, TextBox target)
        {
            var menu = new System.Windows.Controls.ContextMenu();
            void AddItem(string label, string snippet)
            {
                var mi = new System.Windows.Controls.MenuItem { Header = label };
                mi.Click += (_, __) => InsertAtCaret(target, snippet);
                menu.Items.Add(mi);
            }
            void AddHeader(string label)
            {
                var hdr = new System.Windows.Controls.MenuItem
                {
                    Header = label,
                    IsEnabled = false,
                    FontWeight = FontWeights.SemiBold,
                };
                menu.Items.Add(hdr);
            }
            AddHeader("Project info ${X}");
            foreach (var k in new[] {
                "PRJ_PROJECT_COD_TXT", "PRJ_ORG_PROJECT_NAME_TXT",
                "PRJ_ORG_ORIGINATOR_CODE_TXT", "PRJ_ORG_COMPANY_NAME_TXT",
                "PRJ_ORG_CLIENT_NAME_TXT", "PRJ_ORG_APPOINTING_PARTY_TXT",
                "PRJ_ORG_LEAD_APPOINTED_PARTY_TXT",
            }) AddItem("${ " + k + " }", "${" + k + "}");
            menu.Items.Add(new System.Windows.Controls.Separator());
            AddHeader("Tokens {x}");
            foreach (var k in new[] {
                "{disc}", "{discipline}", "{lvl}", "{sys}", "{purpose}", "{phase}",
                "{spool}", "{mark}", "{vol}", "{type}", "{role}", "{suit}", "{rev}",
                "{project}", "{originator}", "{seq:D4}",
            }) AddItem(k, k);
            menu.Items.Add(new System.Windows.Controls.Separator());
            AddHeader("Filters | …");
            foreach (var f in new[] {
                "|upper", "|lower", "|title", "|trim",
                "|trunc:N", "|pad:N", "|padl:N",
                "|date:yyyy-MM-dd", "|default:VALUE", "|fallback:${OTHER}",
            }) AddItem(f, f);
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        private static void InsertAtCaret(TextBox tb, string snippet)
        {
            try
            {
                int start = tb.SelectionStart;
                int len   = tb.SelectionLength;
                var txt   = tb.Text ?? "";
                tb.Text   = txt.Substring(0, start) + snippet + txt.Substring(start + len);
                tb.CaretIndex = start + snippet.Length;
                tb.Focus();
            }
            catch { /* best-effort UX helper */ }
        }

        private UIElement MakeSlotHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            for (int c = 0; c < _slotColWidths.Length; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_slotColWidths[c]) });
            string[] headers = { "Label", "ViewType", "X", "Y", "W", "H", "Scale", "Detail", "" };
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
            tbSc.ToolTip = "Per-slot 1:N override (blank = use DrawingType.Scale). " +
                "Use a finer scale (e.g. 1:20) on detail slots so dimensions stay readable.";
            // Per-slot detail level — empty defers to DrawingType.DetailLevel;
            // letting authors set Fine on a 1:20 detail slot while leaving the
            // overall spool layout at Medium.
            var tbDl = SmallCombo(string.IsNullOrEmpty(slot.DetailLevel) ? "" : slot.DetailLevel,
                v => slot.DetailLevel = string.IsNullOrEmpty(v) ? null : v,
                new[] { "", "Coarse", "Medium", "Fine" });
            tbDl.ToolTip = "Per-slot detail level (blank = use DrawingType.DetailLevel).";
            var rm   = MakeSmallBtn("×", () => { _current.Slots.Remove(slot); onChange?.Invoke(); });
            rm.Width = 22;

            var ctrls = new UIElement[] { tbLbl, tbVt, tbX, tbY, tbW, tbH, tbSc, tbDl, rm };
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
            btnSave.Click  += (s, e) =>
            {
                // Phase 177 — route Save by active tab. Drawing Types tab
                // (index 0) writes drawing_types.json; View Style Packs tab
                // (index 1) writes view_style_packs.json. Other action tabs
                // dispatch their work directly so Save is a no-op there.
                int idx = _rootTabs?.SelectedIndex ?? 0;
                bool ok = idx == 1 ? SavePacksToProjectOverride() : SaveToProjectOverride();
                if (ok) { DialogResult = true; }
            };
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

        // Phase 177 — write the View Style Packs tab to its own project
        // override. Anything whose Origin isn't strictly "corporate" goes
        // out (project-origin + null/untracked + anything an edit flipped
        // off corporate); corporate baseline on disk stays pristine.
        // Reloads the runtime registry so subsequent Apply() runs see the
        // edits, and surfaces a clear empty-set warning so the user knows
        // their edits didn't reach disk.
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
                var dir = Path.Combine(Path.GetDirectoryName(_doc.PathName), "_BIM_COORD");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "view_style_packs.json");

                // Anything not strictly "corporate" wins a slot in the project
                // override. Untracked / null origins go out too — Phase 177
                // change closes the silent-discard hole where edits to a
                // corp pack vanished because Origin was still "corporate".
                var projectPacks = (_packs ?? new List<ViewStylePack>())
                    .Where(p => !string.Equals(p.Origin, "corporate", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (projectPacks.Count == 0)
                {
                    var resp = System.Windows.MessageBox.Show(
                        "No project-origin packs to write.\n\n" +
                        "Every pack still has Origin=\"corporate\" — usually that means no edits have been made, " +
                        "OR the dialog hasn't picked them up yet. Click Yes to write an empty override file " +
                        "(removes any existing project overrides), No to cancel.",
                        "STING — View Style Packs", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (resp != MessageBoxResult.Yes) return false;
                }
                var lib = new ViewStylePackLibrary
                {
                    Version = 1,
                    ViewStylePacks = projectPacks,
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(lib, Formatting.Indented));
                try { ViewStylePackRegistry.Reload(_doc); } catch { /* registry may not expose Reload(doc) */ }
                System.Windows.MessageBox.Show(
                    $"Saved {projectPacks.Count} pack(s) to\n{path}\n\n" +
                    "Corporate baseline on disk is unchanged. Reload the registry or restart Revit to reapply.",
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
    }
}
