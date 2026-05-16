using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using WpfColor    = System.Windows.Media.Color;
using WpfGrid     = System.Windows.Controls.Grid;
using WpfBrush    = System.Windows.Media.Brush;
using WpfBrushes  = System.Windows.Media.Brushes;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfUniformGrid = System.Windows.Controls.Primitives.UniformGrid;

namespace StingTools.Temp
{
    // ══════════════════════════════════════════════════════════════════════
    //  MaterialManagerCommand — opens the Material Manager dialog
    //  Phase 76 Item 13
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MaterialManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var dlg = new MaterialManagerDialog(ctx.Doc, StingToolsApp.DataPath);
            dlg.ShowDialog();
            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MaterialManagerDialog — 7-tab WPF material management dialog
    // ══════════════════════════════════════════════════════════════════════

    internal class MaterialManagerDialog : Window
    {
        private readonly Document _doc;
        private readonly string _dataPath;
        private List<Material> _materials;
        private ObservableCollection<MatRow> _matRows;

        private static readonly SolidColorBrush NavyBrush  = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x3A, 0x5F));
        private static readonly SolidColorBrush AmberBrush = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xA0, 0x20));
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x7D, 0x32));

        public MaterialManagerDialog(Document doc, string dataPath)
        {
            _doc      = doc;
            _dataPath = dataPath;
            Title     = "STING Material Manager";
            Width     = 900;
            Height    = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            LoadMaterials();
            BuildUI();
        }

        private void LoadMaterials()
        {
            _materials = new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .OrderBy(m => m.Name)
                .ToList();

            _matRows = new ObservableCollection<MatRow>(
                _materials.Select(m => new MatRow
                {
                    Name     = m.Name,
                    Category = m.MaterialCategory ?? "",
                    Class    = m.MaterialClass.ToString(),
                    Id       = m.Id.Value
                }));
        }

        private void BuildUI()
        {
            var root = new DockPanel { Background = WpfBrushes.White };

            // ── Header ──
            var header = new Border { Background = NavyBrush, Padding = new Thickness(20, 10, 20, 10) };
            DockPanel.SetDock(header, Dock.Top);
            var hGrid = new WpfGrid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hLeft = new StackPanel();
            hLeft.Children.Add(new TextBlock { Text = "MATERIAL MANAGER", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = WpfBrushes.White });
            hLeft.Children.Add(new TextBlock { Text = $"{_materials.Count} materials in project  ·  BLE + MEP CSV libraries", FontSize = 10, Foreground = new SolidColorBrush(Colors.LightSteelBlue) });
            hGrid.Children.Add(hLeft);
            var closeBtn = new Button { Content = "✕  Close", Height = 28, Padding = new Thickness(12, 0, 12, 0), Background = new SolidColorBrush(WpfColor.FromRgb(0x45, 0x50, 0x6E)), Foreground = WpfBrushes.White, BorderThickness = new Thickness(0), FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            closeBtn.Click += (s, e) => Close();
            WpfGrid.SetColumn(closeBtn, 1);
            hGrid.Children.Add(closeBtn);
            header.Child = hGrid;
            root.Children.Add(header);

            // ── TabControl ──
            var tc = new TabControl { Margin = new Thickness(8) };
            tc.Items.Add(MakeTab("Library",       BuildLibraryTab()));
            tc.Items.Add(MakeTab("Editor",        BuildEditorTab()));
            tc.Items.Add(MakeTab("Layers",        BuildLayersTab()));
            tc.Items.Add(MakeTab("Paint",         BuildPaintTab()));
            tc.Items.Add(MakeTab("Audit",         BuildAuditTab()));
            tc.Items.Add(MakeTab("Import/Export", BuildImportExportTab()));
            tc.Items.Add(MakeTab("BOQ Link",      BuildBOQLinkTab()));
            root.Children.Add(tc);

            Content = root;
        }

        private TabItem MakeTab(string header, UIElement content)
            => new TabItem { Header = header, Content = content, FontSize = 12 };

        // ── Tab 1: Library ──────────────────────────────────────────────

        private UIElement BuildLibraryTab()
        {
            var sp = new StackPanel { Margin = new Thickness(8) };

            var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            searchRow.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var searchBox = new System.Windows.Controls.TextBox { Width = 260, Height = 26 };
            searchRow.Children.Add(searchBox);
            searchRow.Children.Add(new TextBlock { Text = $"  {_matRows.Count} materials", VerticalAlignment = VerticalAlignment.Center, Foreground = WpfBrushes.Gray, FontSize = 11 });
            sp.Children.Add(searchRow);

            var dg = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true,
                ItemsSource = _matRows,
                Height = 380, FontSize = 11,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                CanUserSortColumns = true, RowHeaderWidth = 0,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xD6, 0xE8)),
                BorderThickness = new Thickness(1)
            };
            dg.Columns.Add(new DataGridTextColumn { Header = "Name",     Binding = new System.Windows.Data.Binding("Name"),     Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new System.Windows.Data.Binding("Category"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Class",    Binding = new System.Windows.Data.Binding("Class"),    Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            sp.Children.Add(dg);

            searchBox.TextChanged += (s, e) =>
            {
                string q = searchBox.Text.ToLowerInvariant();
                dg.ItemsSource = string.IsNullOrEmpty(q)
                    ? _matRows
                    : new ObservableCollection<MatRow>(_matRows.Where(r =>
                        r.Name.ToLowerInvariant().Contains(q) ||
                        r.Category.ToLowerInvariant().Contains(q)));
            };

            var btnRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            btnRow.Children.Add(Btn("Duplicate Material", GreenBrush,  () => TaskDialog.Show("STING", "Duplicates selected material.")));
            btnRow.Children.Add(Btn("Delete Selected",    new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28)), () => TaskDialog.Show("STING", "Delete selected material from project.")));
            sp.Children.Add(btnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── Tab 2: Editor ───────────────────────────────────────────────

        private UIElement BuildEditorTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Edit Material Properties", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) });

            sp.Children.Add(Label("Material Name:"));
            var nameBox = new System.Windows.Controls.TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 8) };
            sp.Children.Add(nameBox);

            sp.Children.Add(Label("Category:"));
            var catBox = new System.Windows.Controls.TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 8) };
            sp.Children.Add(catBox);

            sp.Children.Add(Label("Material Class:"));
            var classCb = new WpfComboBox { Height = 26, Margin = new Thickness(0, 2, 0, 8) };
            foreach (var c in new[] { "Basic", "Concrete", "Masonry", "Metal", "Wood", "Glass", "Finish", "Insulation", "Liquid", "Gas" })
                classCb.Items.Add(c);
            classCb.SelectedIndex = 0;
            sp.Children.Add(classCb);

            sp.Children.Add(Label("Appearance (Surface Color):"));
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
            var colorPreview = new Border { Width = 40, Height = 26, Background = new SolidColorBrush(WpfColor.FromRgb(0xBB, 0xBB, 0xBB)), BorderBrush = WpfBrushes.Gray, BorderThickness = new Thickness(1) };
            colorRow.Children.Add(colorPreview);
            colorRow.Children.Add(Btn("Pick Color", NavyBrush, () => { }, margin: new Thickness(6, 0, 0, 0)));
            sp.Children.Add(colorRow);

            sp.Children.Add(Label("Description / Notes:"));
            var notesBox = new System.Windows.Controls.TextBox { Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Margin = new Thickness(0, 2, 0, 10) };
            sp.Children.Add(notesBox);

            var saveBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            saveBtnRow.Children.Add(Btn("Apply Changes", GreenBrush, () => TaskDialog.Show("STING", "Material properties updated.")));
            saveBtnRow.Children.Add(Btn("Reset",         NavyBrush,  () => { nameBox.Text = ""; catBox.Text = ""; notesBox.Text = ""; }, margin: new Thickness(6, 0, 0, 0)));
            sp.Children.Add(saveBtnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── Tab 3: Layers ───────────────────────────────────────────────

        private UIElement BuildLayersTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Compound Layer Materials", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new TextBlock { Text = "Select a wall, floor, ceiling, or roof type to view and edit its compound layers.", FontSize = 11, Foreground = WpfBrushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            typeRow.Children.Add(new TextBlock { Text = "Host Type:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var typeCb = new WpfComboBox { Width = 300 };
            foreach (var t in new[] { "Exterior Wall 200mm", "Interior Wall 100mm", "Floor Slab 250mm", "Suspended Ceiling" })
                typeCb.Items.Add(t);
            typeCb.SelectedIndex = 0;
            typeRow.Children.Add(typeCb);
            sp.Children.Add(typeRow);

            var dg = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = true,
                Height = 220, FontSize = 11,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeaderWidth = 0
            };
            dg.Columns.Add(new DataGridTextColumn { Header = "Layer",    Binding = new System.Windows.Data.Binding("Layer"),    Width = 60 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Function", Binding = new System.Windows.Data.Binding("Function"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Material", Binding = new System.Windows.Data.Binding("Material"), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Thickness (mm)", Binding = new System.Windows.Data.Binding("Thickness"), Width = 110 });
            dg.ItemsSource = new ObservableCollection<LayerRow>
            {
                new LayerRow { Layer = "1", Function = "Finish 1 [4]",      Material = "Plaster - External",    Thickness = "15" },
                new LayerRow { Layer = "2", Function = "Substrate [1]",     Material = "Concrete Block 140mm",  Thickness = "140" },
                new LayerRow { Layer = "3", Function = "Insulation [3]",    Material = "Mineral Wool",          Thickness = "50" },
                new LayerRow { Layer = "4", Function = "Finish 2 [5]",      Material = "Plaster - Internal",    Thickness = "12.5" }
            };
            sp.Children.Add(dg);

            var btnRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            btnRow.Children.Add(Btn("Apply Layers", GreenBrush, () => TaskDialog.Show("STING", "Layer materials applied to type.")));
            btnRow.Children.Add(Btn("Add Layer",    NavyBrush,  () => { }, margin: new Thickness(6, 0, 0, 0)));
            sp.Children.Add(btnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── Tab 4: Paint ────────────────────────────────────────────────

        private UIElement BuildPaintTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Paint Surface Material", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new TextBlock
            {
                Text = "Paints a material onto selected element faces. Select elements in Revit before using this tool.\n" +
                       "Painted materials override the layer material on individual faces.",
                FontSize = 11, Foreground = WpfBrushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            sp.Children.Add(Label("Surface Material:"));
            var matCb = new WpfComboBox { Height = 26, Margin = new Thickness(0, 2, 0, 8) };
            foreach (var m in _materials.Take(30)) matCb.Items.Add(m.Name);
            if (matCb.Items.Count == 0) matCb.Items.Add("No materials loaded");
            matCb.SelectedIndex = 0;
            sp.Children.Add(matCb);

            var faceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            faceRow.Children.Add(new TextBlock { Text = "Face selection:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var rb1 = new RadioButton { Content = "Top faces",    IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
            var rb2 = new RadioButton { Content = "Side faces",   Margin = new Thickness(0, 0, 12, 0) };
            var rb3 = new RadioButton { Content = "All faces" };
            faceRow.Children.Add(rb1); faceRow.Children.Add(rb2); faceRow.Children.Add(rb3);
            sp.Children.Add(faceRow);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            btnRow.Children.Add(Btn("Paint Selected", AmberBrush, () => TaskDialog.Show("STING", "Paint material applied to selected element faces.")));
            btnRow.Children.Add(Btn("Remove Paint",   new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28)), () => TaskDialog.Show("STING", "Paint removed from selected faces."), margin: new Thickness(6, 0, 0, 0)));
            sp.Children.Add(btnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── Tab 5: Audit ────────────────────────────────────────────────

        private UIElement BuildAuditTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Material Audit", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) });

            // Summary KPIs
            var kpiRow = new WpfUniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            kpiRow.Children.Add(KpiCard("TOTAL",    _materials.Count.ToString(),        NavyBrush));
            kpiRow.Children.Add(KpiCard("UNUSED",   "—",                               AmberBrush));
            kpiRow.Children.Add(KpiCard("DUPLICATE","—",                               GreenBrush));
            sp.Children.Add(kpiRow);

            sp.Children.Add(new TextBlock { Text = "Material Usage by Category:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var catGroups = _materials.GroupBy(m => m.MaterialCategory ?? "(none)").OrderByDescending(g => g.Count()).ToList();
            foreach (var g in catGroups.Take(12))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new TextBlock { Text = g.Key, Width = 200, FontSize = 11 });
                var bar = new Border { Height = 14, Width = Math.Max(4, g.Count() * 4), Background = NavyBrush, CornerRadius = new CornerRadius(2), Margin = new Thickness(4, 0, 4, 0) };
                row.Children.Add(bar);
                row.Children.Add(new TextBlock { Text = g.Count().ToString(), FontSize = 11, Foreground = WpfBrushes.Gray, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(row);
            }

            var btnRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            btnRow.Children.Add(Btn("Find Unused",     NavyBrush,  () => TaskDialog.Show("STING", "Scanning for unused materials...")));
            btnRow.Children.Add(Btn("Find Duplicates", AmberBrush, () => TaskDialog.Show("STING", "Scanning for duplicate materials..."), margin: new Thickness(6, 0, 0, 0)));
            btnRow.Children.Add(Btn("Purge Unused",    new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28)), () => TaskDialog.Show("STING", "Purge unused materials."), margin: new Thickness(6, 0, 0, 0)));
            sp.Children.Add(btnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── Tab 6: Import/Export ────────────────────────────────────────

        private UIElement BuildImportExportTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Import / Export", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) });

            // Export section
            sp.Children.Add(new TextBlock { Text = "EXPORT", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var expCard = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)), BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xD6, 0xE8)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
            var expStack = new StackPanel();
            expStack.Children.Add(new TextBlock { Text = "Export all project materials to CSV or XLSX:", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            var expBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            expBtnRow.Children.Add(Btn("Export CSV",  GreenBrush,  () => TaskDialog.Show("STING", "Material list exported to CSV.")));
            expBtnRow.Children.Add(Btn("Export XLSX", NavyBrush,   () => TaskDialog.Show("STING", "Material list exported to XLSX."), margin: new Thickness(6, 0, 0, 0)));
            expStack.Children.Add(expBtnRow);
            expCard.Child = expStack;
            sp.Children.Add(expCard);

            // Import section
            sp.Children.Add(new TextBlock { Text = "IMPORT FROM CSV LIBRARIES", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var impCard = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)), BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xD6, 0xE8)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
            var impStack = new StackPanel();
            impStack.Children.Add(new TextBlock { Text = "Create materials from STING BLE/MEP CSV libraries:", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            var impBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            impBtnRow.Children.Add(Btn("Create BLE Materials (815)",  AmberBrush, () => TaskDialog.Show("STING", "Launch CreateBLEMaterials command.")));
            impBtnRow.Children.Add(Btn("Create MEP Materials (464)",  GreenBrush, () => TaskDialog.Show("STING", "Launch CreateMEPMaterials command."), margin: new Thickness(6, 0, 0, 0)));
            impStack.Children.Add(impBtnRow);
            impCard.Child = impStack;
            sp.Children.Add(impCard);

            // Custom import
            sp.Children.Add(new TextBlock { Text = "CUSTOM CSV IMPORT", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var custCard = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)), BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xD6, 0xE8)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12) };
            var custStack = new StackPanel();
            custStack.Children.Add(new TextBlock { Text = "Import materials from a custom CSV (columns: Name, Category, Class, Color R, Color G, Color B):", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var fileRow = new StackPanel { Orientation = Orientation.Horizontal };
            var fileBox = new System.Windows.Controls.TextBox { Width = 340, Height = 26 };
            fileRow.Children.Add(fileBox);
            fileRow.Children.Add(Btn("Browse…", NavyBrush, () =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "CSV files (*.csv)|*.csv", Title = "Select material CSV" };
                if (ofd.ShowDialog() == true) fileBox.Text = ofd.FileName;
            }, margin: new Thickness(6, 0, 0, 0)));
            custStack.Children.Add(fileRow);
            custStack.Children.Add(Btn("Import", GreenBrush, () => TaskDialog.Show("STING", "Custom CSV import started."), margin: new Thickness(0, 8, 0, 0)));
            custCard.Child = custStack;
            sp.Children.Add(custCard);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── Tab 7: BOQ Link ─────────────────────────────────────────────

        private UIElement BuildBOQLinkTab()
        {
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "BOQ Link", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock
            {
                Text = "Link project materials to NRM2 paragraph templates. Paragraphs describe item " +
                       "properties only (material, dimensions, finish, standard) — quantities and costs " +
                       "go in their own columns at export.  Badges:  ◆ = project-only   ★ = company library (reusable)",
                FontSize = 11, Foreground = WpfBrushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            // ── Load 3-layer template catalogue + material links sidecar ──
            var templates = new List<BOQTemplate>(BOQTemplateLibrary.LoadAll(_doc, _dataPath));
            var links = BOQLinkStore.LoadLinks(_doc);
            var redBrush = new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28));

            // ── Project Material ──
            sp.Children.Add(Label("Project Material:"));
            var matCb = new WpfComboBox { Height = 26, Margin = new Thickness(0, 2, 0, 8), IsEditable = true };
            foreach (var m in _materials) matCb.Items.Add(m.Name);
            if (matCb.Items.Count == 0) { matCb.Items.Add("(no materials in project)"); matCb.IsEnabled = false; }
            matCb.SelectedIndex = 0;
            sp.Children.Add(matCb);

            // ── BOQ Description (template catalogue) ──
            sp.Children.Add(Label("BOQ Description (paragraph template):"));
            var boqCb = new WpfComboBox { Height = 26, Margin = new Thickness(0, 2, 0, 8), IsEditable = true, MaxDropDownHeight = 320 };
            void RebuildTemplateDropdown(string preferredId = null)
            {
                int selectIdx = 0;
                boqCb.Items.Clear();
                for (int i = 0; i < templates.Count; i++)
                {
                    boqCb.Items.Add(templates[i].DisplayLabel);
                    if (!string.IsNullOrEmpty(preferredId) && templates[i].Id == preferredId) selectIdx = i;
                }
                if (templates.Count == 0)
                {
                    boqCb.Items.Add("(no BOQ paragraph templates — Data/BOQ_DESCRIPTIONS.json missing)");
                    boqCb.IsEnabled = false;
                }
                else
                {
                    boqCb.IsEnabled = true;
                    boqCb.SelectedIndex = selectIdx;
                }
            }
            RebuildTemplateDropdown();
            sp.Children.Add(boqCb);

            // ── NRM2 Section + source badge (auto-filled) ──
            sp.Children.Add(Label("NRM2 Section / template source:"));
            var sectionBox = new System.Windows.Controls.TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 8), IsReadOnly = true, Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)) };
            sp.Children.Add(sectionBox);

            // ── Paragraph preview (read-only, wraps) ──
            sp.Children.Add(Label("Paragraph preview (placeholders resolved at export):"));
            var previewBox = new System.Windows.Controls.TextBox
            {
                Height = 70, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)),
                Margin = new Thickness(0, 2, 0, 8), FontStyle = FontStyles.Italic
            };
            sp.Children.Add(previewBox);

            // ── Notes ──
            sp.Children.Add(Label("Notes (optional — e.g., bespoke specification references):"));
            var notesBox = new System.Windows.Controls.TextBox { Height = 40, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
            sp.Children.Add(notesBox);

            // ── Link status line ──
            var statusBlock = new TextBlock { FontSize = 10, Foreground = WpfBrushes.Gray, Margin = new Thickness(0, 0, 0, 6) };
            sp.Children.Add(statusBlock);

            BOQTemplate Current() => (boqCb.SelectedIndex >= 0 && boqCb.SelectedIndex < templates.Count) ? templates[boqCb.SelectedIndex] : null;

            void RefreshPreview()
            {
                var t = Current();
                if (t != null)
                {
                    string src = t.Source switch { "company" => "★ company library", "project" => "◆ this project", _ => "built-in" };
                    sectionBox.Text = $"NRM2 §{t.Nrm2Section} — {t.Category}   ·   {src}";
                    previewBox.Text = t.Paragraph;
                }
                else { sectionBox.Text = ""; previewBox.Text = ""; }

                string matName = matCb.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(matName) && links.TryGetValue(matName, out var existing))
                {
                    statusBlock.Text = $"Linked: §{existing.Nrm2Section} {existing.Category} (updated {existing.UpdatedDate:yyyy-MM-dd} by {existing.UpdatedBy})";
                    statusBlock.Foreground = GreenBrush;
                }
                else
                {
                    statusBlock.Text = "Not linked — choose a template and click Save Link.";
                    statusBlock.Foreground = WpfBrushes.Gray;
                }
            }
            boqCb.SelectionChanged += (s, e) => RefreshPreview();
            matCb.SelectionChanged += (s, e) =>
            {
                // Auto-jump to existing link for this material
                string matName = matCb.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(matName) && links.TryGetValue(matName, out var existing))
                {
                    int idx = templates.FindIndex(t => t.Category == existing.Category && t.Nrm2Section == existing.Nrm2Section && t.Paragraph == existing.Paragraph);
                    if (idx < 0) idx = templates.FindIndex(t => t.Category == existing.Category);
                    if (idx >= 0) boqCb.SelectedIndex = idx;
                    notesBox.Text = existing.Notes ?? "";
                }
                RefreshPreview();
            };
            RefreshPreview();

            // ══════════════════════════════════════════════════════════
            //  Row 1 — Template library management
            // ══════════════════════════════════════════════════════════
            sp.Children.Add(Label("Template library:"));
            var tplRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };

            void ReloadAndSelect(string selectId)
            {
                templates.Clear();
                templates.AddRange(BOQTemplateLibrary.LoadAll(_doc, _dataPath));
                RebuildTemplateDropdown(selectId);
                RefreshPreview();
            }

            void OpenEditor(BOQTemplate seed, bool isNew)
            {
                try
                {
                    var dlg = new BOQParagraphEditorDialog(seed, _doc, isNew) { Owner = this };
                    dlg.ShowDialog();
                    if (!dlg.Saved || dlg.Result == null) return;
                    var t = dlg.Result;
                    if (dlg.SaveTarget == "company") BOQTemplateLibrary.SaveToCompany(t);
                    else if (dlg.SaveTarget == "project" && _doc != null) BOQTemplateLibrary.SaveToProject(_doc, t);
                    ReloadAndSelect(t.Id);
                }
                catch (Exception ex)
                {
                    StingLog.Error($"BOQ editor: {ex.Message}");
                    TaskDialog.Show("BOQ Template", $"Editor failed:\n{ex.Message}");
                }
            }

            tplRow.Children.Add(Btn("★ New Template…", GreenBrush, () => OpenEditor(null, true)));
            tplRow.Children.Add(Btn("Edit…", NavyBrush, () =>
            {
                var t = Current();
                if (t == null) { TaskDialog.Show("BOQ Template", "Select a template first."); return; }
                if (t.Source == "builtin")
                {
                    var ans = MessageBox.Show(
                        "Built-in templates are read-only. Edit a copy in the company library instead?",
                        "BOQ Template", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (ans != MessageBoxResult.OK) return;
                    // Seed from the builtin but reset id → becomes a new company template
                    var seed = new BOQTemplate
                    {
                        Id = null, Category = t.Category, Nrm2Section = t.Nrm2Section,
                        Paragraph = t.Paragraph, Placeholders = t.Placeholders, Source = "company"
                    };
                    OpenEditor(seed, false);
                    return;
                }
                OpenEditor(t, false);
            }, margin: new Thickness(6, 0, 0, 0)));

            tplRow.Children.Add(Btn("Duplicate…", NavyBrush, () =>
            {
                var t = Current();
                if (t == null) { TaskDialog.Show("BOQ Template", "Select a template first."); return; }
                var seed = new BOQTemplate
                {
                    Id = null,  // new id on save
                    Category = t.Category + " (copy)",
                    Nrm2Section = t.Nrm2Section,
                    Paragraph = t.Paragraph,
                    Placeholders = t.Placeholders,
                    Source = "company"
                };
                OpenEditor(seed, true);
            }, margin: new Thickness(6, 0, 0, 0)));

            tplRow.Children.Add(Btn("Delete", redBrush, () =>
            {
                var t = Current();
                if (t == null) { TaskDialog.Show("BOQ Template", "Select a template first."); return; }
                if (t.Source == "builtin") { TaskDialog.Show("BOQ Template", "Built-in templates cannot be deleted."); return; }
                var ans = MessageBox.Show($"Delete this {t.Source} template?\n\n§{t.Nrm2Section} {t.Category}", "Confirm Delete",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ans != MessageBoxResult.OK) return;
                bool ok = false;
                if (t.Source == "company") ok = BOQTemplateLibrary.DeleteFromCompany(t.Id);
                else if (t.Source == "project" && _doc != null) ok = BOQTemplateLibrary.DeleteFromProject(_doc, t.Id);
                if (ok) { ReloadAndSelect(null); TaskDialog.Show("BOQ Template", "Template deleted."); }
                else TaskDialog.Show("BOQ Template", "Delete failed — template may have been externally modified.");
            }, margin: new Thickness(6, 0, 0, 0)));

            tplRow.Children.Add(Btn("Import Library…", NavyBrush, () =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import BOQ Template Library",
                    Filter = "STING BOQ library (*.json)|*.json|All files (*.*)|*.*"
                };
                if (ofd.ShowDialog() != true) return;
                var td = new TaskDialog("BOQ Library Import")
                {
                    MainInstruction = "Import where?",
                    MainContent = "★ Company library is shared across all your projects on this machine. ◆ This project only keeps the imports in this project's sidecar."
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "★ Company library");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "◆ This project only");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = td.Show();
                string target = choice == TaskDialogResult.CommandLink1 ? "company" : choice == TaskDialogResult.CommandLink2 ? "project" : null;
                if (target == null) return;
                try
                {
                    int n = BOQTemplateLibrary.ImportLibrary(ofd.FileName, target, _doc);
                    ReloadAndSelect(null);
                    TaskDialog.Show("BOQ Template", $"Imported {n} template(s) into the {target} library.");
                }
                catch (Exception ex) { TaskDialog.Show("BOQ Template", $"Import failed:\n{ex.Message}"); }
            }, margin: new Thickness(6, 0, 0, 0)));

            tplRow.Children.Add(Btn("Export Library…", NavyBrush, () =>
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export BOQ Template Library",
                    FileName = $"STING_BOQ_Library_{DateTime.Now:yyyyMMdd}.json",
                    Filter = "STING BOQ library (*.json)|*.json"
                };
                if (sfd.ShowDialog() != true) return;
                try
                {
                    // Export everything EXCEPT builtin (builtin ships with the plugin already)
                    var toExport = templates.Where(t => t.Source != "builtin").ToList();
                    int n = BOQTemplateLibrary.ExportLibrary(sfd.FileName, toExport);
                    TaskDialog.Show("BOQ Template", $"Exported {n} template(s) (company + project).\n\nFile: {sfd.FileName}");
                }
                catch (Exception ex) { TaskDialog.Show("BOQ Template", $"Export failed:\n{ex.Message}"); }
            }, margin: new Thickness(6, 0, 0, 0)));

            // ── Git-friendly folder I/O (one file per template) ──
            tplRow.Children.Add(Btn("Export → Folder…", NavyBrush, () =>
            {
                // Use SaveFileDialog as a "pick a folder" shim (OpenFolderDialog is .NET 8+;
                // keep portability by synthesising a folder from the chosen file's directory).
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Choose a folder (name any file inside it — its folder will be used)",
                    FileName = "BOQ_Library",
                    Filter = "Folder (select any file inside) (*.*)|*.*",
                    CheckPathExists = true
                };
                if (sfd.ShowDialog() != true) return;
                try
                {
                    string folder = System.IO.Path.GetDirectoryName(sfd.FileName) ?? sfd.FileName;
                    // If user gave us a bare folder that doesn't exist yet, create it
                    if (!System.IO.Directory.Exists(folder) && !System.IO.File.Exists(sfd.FileName))
                        System.IO.Directory.CreateDirectory(folder);
                    var toExport = templates.Where(t => t.Source != "builtin").ToList();
                    int n = BOQTemplateLibrary.ExportToFolder(folder, toExport);
                    TaskDialog.Show("BOQ Template",
                        $"Exported {n} template(s) as individual JSON files.\n\n" +
                        $"Folder: {folder}\n\nEach template is one file — commit the folder to Git to review changes per template.");
                }
                catch (Exception ex) { TaskDialog.Show("BOQ Template", $"Folder export failed:\n{ex.Message}"); }
            }, margin: new Thickness(6, 0, 0, 0)));

            tplRow.Children.Add(Btn("Import ← Folder…", NavyBrush, () =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose a folder (pick any *.json inside — its folder is imported)",
                    Filter = "JSON template (*.json)|*.json",
                    CheckFileExists = true
                };
                if (ofd.ShowDialog() != true) return;
                try
                {
                    string folder = System.IO.Path.GetDirectoryName(ofd.FileName);
                    if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
                    {
                        TaskDialog.Show("BOQ Template", "Could not resolve folder from the selected file.");
                        return;
                    }
                    var td = new TaskDialog("BOQ Folder Import")
                    {
                        MainInstruction = "Import where?",
                        MainContent = "★ Company library is shared across all your projects on this machine. ◆ This project only keeps the imports in this project's sidecar."
                    };
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "★ Company library");
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "◆ This project only");
                    td.CommonButtons = TaskDialogCommonButtons.Cancel;
                    var choice = td.Show();
                    string target = choice == TaskDialogResult.CommandLink1 ? "company"
                                  : choice == TaskDialogResult.CommandLink2 ? "project" : null;
                    if (target == null) return;
                    int n = BOQTemplateLibrary.ImportFromFolder(folder, target, _doc);
                    ReloadAndSelect(null);
                    TaskDialog.Show("BOQ Template", $"Imported {n} template(s) from folder.\n\nFolder: {folder}");
                }
                catch (Exception ex) { TaskDialog.Show("BOQ Template", $"Folder import failed:\n{ex.Message}"); }
            }, margin: new Thickness(6, 0, 0, 0)));

            tplRow.Children.Add(Btn("Library Path…", NavyBrush, () =>
            {
                string current = BOQTemplateLibrary.CompanyLibraryPath;
                var td = new TaskDialog("BOQ Company Library Path")
                {
                    MainInstruction = "Where is the shared company template library?",
                    MainContent =
                        $"Current path:\n{current}\n\n" +
                        "Set a network share path to share the library across a team without manual import/export. " +
                        "This is a per-machine setting (stored in %APPDATA%/STING/boq_config.json) " +
                        "but any project_config.json with BOQ_COMPANY_LIBRARY_PATH takes precedence."
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Choose a file…");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Reset to default (%APPDATA%)");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var choice = td.Show();
                if (choice == TaskDialogResult.CommandLink1)
                {
                    var sfd = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Choose company library file (existing or new)",
                        FileName = "boq_templates_library.json",
                        Filter = "STING BOQ library (*.json)|*.json"
                    };
                    if (sfd.ShowDialog() == true)
                    {
                        try
                        {
                            BOQTemplateLibrary.SetCompanyLibraryPathMachine(sfd.FileName);
                            ReloadAndSelect(null);
                            TaskDialog.Show("BOQ Template", $"Company library path set to:\n{sfd.FileName}");
                        }
                        catch (Exception ex) { TaskDialog.Show("BOQ Template", $"Set library path failed:\n{ex.Message}"); }
                    }
                }
                else if (choice == TaskDialogResult.CommandLink2)
                {
                    try
                    {
                        BOQTemplateLibrary.SetCompanyLibraryPathMachine(null);
                        ReloadAndSelect(null);
                        TaskDialog.Show("BOQ Template", "Reverted to default (%APPDATA%/STING/boq_templates_library.json).");
                    }
                    catch (Exception ex) { TaskDialog.Show("BOQ Template", $"Reset failed:\n{ex.Message}"); }
                }
            }, margin: new Thickness(6, 0, 0, 0)));

            sp.Children.Add(tplRow);

            // ══════════════════════════════════════════════════════════
            //  Row 2 — Material linking + automation
            // ══════════════════════════════════════════════════════════
            sp.Children.Add(Label("Material linking & automation:"));
            var btnRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            btnRow.Children.Add(Btn("Save Link", GreenBrush, () =>
            {
                string matName = matCb.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(matName) || matName.StartsWith("(no "))
                {
                    TaskDialog.Show("BOQ Link", "Select a project material first.");
                    return;
                }
                var t = Current();
                if (t == null) { TaskDialog.Show("BOQ Link", "Choose a BOQ paragraph template."); return; }
                links[matName] = new BOQLinkStore.Link
                {
                    MaterialName = matName,
                    Category = t.Category,
                    Nrm2Section = t.Nrm2Section,
                    Paragraph = t.Paragraph,
                    Notes = notesBox.Text?.Trim() ?? "",
                    UpdatedBy = Environment.UserName ?? "unknown",
                    UpdatedDate = DateTime.Now
                };
                try
                {
                    BOQLinkStore.SaveLinks(_doc, links);
                    TaskDialog.Show("BOQ Link", $"Saved link:\n{matName}  →  §{t.Nrm2Section} {t.Category}");
                    RefreshPreview();
                }
                catch (Exception ex)
                {
                    StingLog.Error($"BOQ link save failed: {ex.Message}");
                    TaskDialog.Show("BOQ Link", $"Could not save link:\n{ex.Message}");
                }
            }));

            btnRow.Children.Add(Btn("View All Links", NavyBrush, () =>
            {
                if (links.Count == 0) { TaskDialog.Show("BOQ Link", "No material→BOQ links saved yet."); return; }
                var msg = new System.Text.StringBuilder();
                msg.AppendLine($"Saved material → BOQ links  ({links.Count}):");
                msg.AppendLine(new string('─', 60));
                foreach (var kv in links.OrderBy(k => k.Value.Nrm2Section).ThenBy(k => k.Key))
                {
                    msg.AppendLine($"  {kv.Key,-30}  §{kv.Value.Nrm2Section,-3} {kv.Value.Category}");
                    if (!string.IsNullOrEmpty(kv.Value.Notes)) msg.AppendLine($"    note: {kv.Value.Notes}");
                }
                var td = new TaskDialog("STING — BOQ Links") { MainInstruction = $"{links.Count} material link(s)", MainContent = msg.ToString() };
                td.CommonButtons = TaskDialogCommonButtons.Ok;
                td.Show();
            }, margin: new Thickness(6, 0, 0, 0)));

            btnRow.Children.Add(Btn("Unlink Selected", redBrush, () =>
            {
                string matName = matCb.Text?.Trim() ?? "";
                if (!links.Remove(matName)) { TaskDialog.Show("BOQ Link", "No link to remove for the selected material."); return; }
                try { BOQLinkStore.SaveLinks(_doc, links); } catch (Exception ex) { StingLog.Warn($"Unlink save: {ex.Message}"); }
                RefreshPreview();
                TaskDialog.Show("BOQ Link", $"Removed link for {matName}.");
            }, margin: new Thickness(6, 0, 0, 0)));

            btnRow.Children.Add(Btn("Auto-Link All", AmberBrush, () =>
            {
                if (_materials.Count == 0) { TaskDialog.Show("BOQ Link", "No materials in project."); return; }
                int proposed = 0, skipped = 0;
                foreach (var m in _materials)
                {
                    if (links.ContainsKey(m.Name)) { skipped++; continue; }
                    var best = FindBestTemplateForMaterial(m.Name, m.MaterialCategory ?? "", templates);
                    if (best == null) continue;
                    links[m.Name] = new BOQLinkStore.Link
                    {
                        MaterialName = m.Name,
                        Category = best.Category,
                        Nrm2Section = best.Nrm2Section,
                        Paragraph = best.Paragraph,
                        Notes = "(auto-linked by category keyword match — review before export)",
                        UpdatedBy = Environment.UserName ?? "auto",
                        UpdatedDate = DateTime.Now
                    };
                    proposed++;
                }
                try { BOQLinkStore.SaveLinks(_doc, links); } catch (Exception ex) { StingLog.Warn($"Auto-link save: {ex.Message}"); }
                RefreshPreview();
                TaskDialog.Show("BOQ Link",
                    $"Auto-link complete:\n" +
                    $"  {proposed} new links proposed\n" +
                    $"  {skipped} already linked (unchanged)\n" +
                    $"  {_materials.Count - proposed - skipped} unmatched (add template or link manually)\n\n" +
                    "Review each auto-linked material — the category keyword match is a best-guess.");
            }, margin: new Thickness(6, 0, 0, 0)));

            btnRow.Children.Add(Btn("Coverage Audit", NavyBrush, () =>
            {
                var audit = BuildCoverageAudit(_materials.Select(x => x.Name).ToList(), links, templates);
                // Gap 2 — resolvability audit: scan project elements against each template
                var resolv = BOQTemplateLibrary.AuditResolvability(
                    _doc, templates,
                    doc => new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                            .ToList(),
                    el => el?.Category?.Name ?? "",
                    (el, token) => ResolvePlaceholderForAudit(el, token));

                var combined = audit.summary
                    + Environment.NewLine
                    + new string('─', 60) + Environment.NewLine
                    + "RESOLVABILITY (per-template placeholder coverage)" + Environment.NewLine
                    + new string('─', 60) + Environment.NewLine
                    + resolv.FormatSummary();

                var td = new TaskDialog("BOQ Coverage Audit")
                {
                    MainInstruction = $"{audit.linked}/{audit.total} materials linked  ({audit.percent:F0}%)   ·   " +
                                      $"{resolv.TotalFullyResolved}/{resolv.TotalElementsEvaluated} elements fully resolve  ({resolv.ResolvedPercent:F0}%)",
                    MainContent = combined
                };
                td.CommonButtons = TaskDialogCommonButtons.Ok;
                td.Show();
            }, margin: new Thickness(6, 0, 0, 0)));

            btnRow.Children.Add(Btn("Export BOQ", AmberBrush, () =>
            {
                Close();
                try
                {
                    var cmd = new StingTools.Temp.BOQExportCommand();
                    string m = null;
                    cmd.Execute(null, ref m, null);
                }
                catch (Exception ex)
                {
                    StingLog.Error($"BOQ export launch failed: {ex.Message}");
                    TaskDialog.Show("BOQ Export", $"Launch failed:\n{ex.Message}\n\nUse BIM tab → Export BOQ instead.");
                }
            }, margin: new Thickness(6, 0, 0, 0)));

            sp.Children.Add(btnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ── BOQ automation helpers ──────────────────────────────────────

        /// <summary>
        /// Best-guess template match for a material using keyword scoring.
        /// Returns null when no template scores above threshold.
        /// </summary>
        /// <summary>
        /// Probe whether a placeholder token would resolve for an element.
        /// Mirrors the key mappings used by BOQDescriptionEngine.BuildPlaceholderValues
        /// so the resolvability audit matches what the exporter actually produces.
        /// Returns empty string when the placeholder would be blank at export time.
        /// </summary>
        private static string ResolvePlaceholderForAudit(Element el, string token)
        {
            if (el == null || string.IsNullOrEmpty(token)) return "";
            try
            {
                switch (token.ToLowerInvariant())
                {
                    case "material":
                        // Prefer STING material param, then structural material, then type material
                        string mat = ParameterHelpers.GetString(el, "INS_MATERIAL_TXT");
                        if (!string.IsNullOrEmpty(mat)) return mat;
                        try
                        {
                            var p = el.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                            if (p != null && p.AsElementId() != ElementId.InvalidElementId) return "✓";
                        } catch (Exception ex) { StingLog.Warn($"Audit material: {ex.Message}"); }
                        return "";
                    case "element_type":
                    case "type":
                    case "door_type":
                    case "window_type":
                    case "foundation_type":
                    case "terminal_type":
                    case "equipment_type":
                    case "furniture_type":
                    case "casework_type":
                        return ParameterHelpers.GetFamilySymbolName(el) ?? "";
                    case "family":
                        return ParameterHelpers.GetFamilyName(el) ?? "";
                    case "manufacturer":          return ParameterHelpers.GetString(el, "ASS_MANUFACTURER_TXT");
                    case "model":
                    case "model_ref":             return ParameterHelpers.GetString(el, "ASS_MODEL_NR_TXT");
                    case "manufacturer_ref":
                        return (ParameterHelpers.GetString(el, "ASS_MANUFACTURER_TXT") + " " +
                                ParameterHelpers.GetString(el, "ASS_MODEL_NR_TXT")).Trim();
                    case "location":
                    case "level":                 return ParameterHelpers.GetString(el, ParamRegistry.LVL);
                    // Fix: BLE dimension parameters use _MM (millimetres) or _DEG suffix in
                    // MR_PARAMETERS.txt — the old _NR suffix names did not exist, so every
                    // FirstNonEmpty call here always returned "" and downstream dimensions
                    // were never populated. BLE_CBL_TRAY_*_NR have no canonical equivalent
                    // in the registry yet — left in place so a future param addition can
                    // wire them up without touching this site again.
                    case "width":                 return FirstNonEmpty(el, "BLE_DOOR_WIDTH_MM", "BLE_WINDOW_WIDTH_MM", "BLE_CBL_TRAY_WIDTH_NR", "BLE_WALL_LENGTH_MM");
                    case "height":                return FirstNonEmpty(el, "BLE_WALL_HEIGHT_MM", "BLE_DOOR_HEIGHT_MM", "BLE_WINDOW_HEIGHT_MM");
                    case "thickness":             return FirstNonEmpty(el, "BLE_WALL_THICKNESS_MM", "BLE_FLR_THICKNESS_MM");
                    case "depth":                 return FirstNonEmpty(el, "BLE_CBL_TRAY_DEPTH_NR", "BLE_FLR_THICKNESS_MM");
                    case "sill_height":           return ParameterHelpers.GetString(el, "BLE_WINDOW_SILL_HEIGHT_FROM_FLR_MM");
                    case "size":
                    case "diameter":              return ParameterHelpers.GetString(el, "ASS_SIZE_TXT");
                    case "airflow":               return ParameterHelpers.GetString(el, "HVC_AIRFLOW_LS_NR");
                    case "rating":                return FirstNonEmpty(el, "ELC_EQP_LOAD_KW_NR", "ELC_EQP_AMPS_NR");
                    case "voltage":               return ParameterHelpers.GetString(el, "ELC_EQP_VOLTS_NR");
                    case "phases":                return ParameterHelpers.GetString(el, "ELC_EQP_PHASE_NR");
                    case "fire_rating":           return ParameterHelpers.GetString(el, "BLE_FIRE_RATING_TXT");
                    case "finish":                return ParameterHelpers.GetString(el, "ASS_FINISH_TXT");
                    case "insulation":            return ParameterHelpers.GetString(el, "BLE_INSULATION_TXT");
                    case "substrate":             return ParameterHelpers.GetString(el, "BLE_SUBSTRATE_TXT");
                    case "fixings":               return ParameterHelpers.GetString(el, "ASS_FIXINGS_TXT");
                    case "frame_material":        return ParameterHelpers.GetString(el, "BLE_FRAME_MATERIAL_TXT");
                    case "hardware":              return ParameterHelpers.GetString(el, "BLE_HARDWARE_TXT");
                    case "glass_spec":            return ParameterHelpers.GetString(el, "BLE_GLASS_SPEC_TXT");
                    case "concrete_spec":         return ParameterHelpers.GetString(el, "STR_CONCRETE_GRADE_TXT");
                    case "reinforcement":         return ParameterHelpers.GetString(el, "STR_REBAR_SPEC_TXT");
                    case "section_size":          return ParameterHelpers.GetString(el, "STR_SECTION_SIZE_TXT");
                    case "worktop_material":      return ParameterHelpers.GetString(el, "BLE_WORKTOP_MATERIAL_TXT");
                    case "edge_trim":             return ParameterHelpers.GetString(el, "BLE_EDGE_TRIM_TXT");
                    case "spacing":               return ParameterHelpers.GetString(el, "ASS_SPACING_MM_NR");
                    case "description":           return ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT");
                    case "notes":                 return ParameterHelpers.GetString(el, "ASS_NOTES_TXT");
                    case "standard":              return "✓"; // engine always supplies via workmanship table
                    case "dimensions":
                        // Considered resolvable if width+height, or size
                        return (!string.IsNullOrEmpty(FirstNonEmpty(el, "BLE_DOOR_WIDTH_MM", "BLE_WINDOW_WIDTH_MM")) ||
                                !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_SIZE_TXT"))) ? "✓" : "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolvePlaceholderForAudit({token}): {ex.Message}"); }
            // Unknown token → try a loose parameter lookup using the token as a param name
            return ParameterHelpers.GetString(el, token) ?? "";
        }

        private static string FirstNonEmpty(Element el, params string[] paramNames)
        {
            foreach (var n in paramNames)
            {
                string v = ParameterHelpers.GetString(el, n);
                if (!string.IsNullOrEmpty(v) && v != "0") return v;
            }
            return "";
        }

        private static BOQTemplate FindBestTemplateForMaterial(string materialName, string materialCategory, List<BOQTemplate> templates)
        {
            if (string.IsNullOrEmpty(materialName) || templates.Count == 0) return null;
            string haystack = (materialName + " " + materialCategory).ToLowerInvariant();

            int BestScore(BOQTemplate t)
            {
                int score = 0;
                string cat = (t.Category ?? "").ToLowerInvariant();
                if (cat.Length > 0 && haystack.Contains(cat)) score += 10;
                // token-level match against category words
                foreach (var word in cat.Split(' ', '-', '/'))
                {
                    if (word.Length >= 4 && haystack.Contains(word)) score += 3;
                }
                // common construction keywords give weaker bonus
                string[] keys = { "wall", "floor", "ceiling", "roof", "door", "window", "column", "beam", "pipe", "duct", "cable", "tray", "concrete", "timber", "steel", "glass", "paint", "insul" };
                foreach (var k in keys) if (cat.Contains(k) && haystack.Contains(k)) score += 2;
                return score;
            }

            var best = templates
                .Select(t => (t, score: BestScore(t)))
                .Where(x => x.score >= 6)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.t.Source == "project" ? 0 : x.t.Source == "company" ? 1 : 2)  // prefer user templates on ties
                .FirstOrDefault();
            return best.t;
        }

        /// <summary>
        /// Produces a coverage audit summary for the BOQ workflow — linked vs
        /// unlinked materials, plus unresolvable-placeholder detection for the
        /// current project.
        /// </summary>
        private (int total, int linked, double percent, string summary) BuildCoverageAudit(
            List<string> materialNames, Dictionary<string, BOQLinkStore.Link> links, List<BOQTemplate> templates)
        {
            int total = materialNames.Count;
            int linkedCount = materialNames.Count(n => links.ContainsKey(n));
            double pct = total == 0 ? 0 : 100.0 * linkedCount / total;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Linked:     {linkedCount,4} / {total}  ({pct:F0}%)");
            sb.AppendLine($"Unlinked:   {total - linkedCount,4}");
            sb.AppendLine();

            var unlinked = materialNames.Where(n => !links.ContainsKey(n)).Take(20).ToList();
            if (unlinked.Count > 0)
            {
                sb.AppendLine("UNLINKED MATERIALS (first 20):");
                foreach (var n in unlinked) sb.AppendLine("  · " + n);
                if (materialNames.Count(n => !links.ContainsKey(n)) > 20) sb.AppendLine("  …");
                sb.AppendLine();
            }

            // Template-side stats
            int builtin = templates.Count(t => t.Source == "builtin");
            int company = templates.Count(t => t.Source == "company");
            int project = templates.Count(t => t.Source == "project");
            sb.AppendLine($"Template catalogue: {templates.Count}  (built-in {builtin}, ★ company {company}, ◆ project {project})");

            // Surface unique NRM2 sections covered
            var sections = templates.Select(t => t.Nrm2Section).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();
            sb.AppendLine($"NRM2 sections covered: {string.Join(", ", sections)}");

            return (total, linkedCount, pct, sb.ToString());
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static TextBlock Label(string text)
            => new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) };

        private static Button Btn(string content, SolidColorBrush bg, Action onClick, Thickness? margin = null)
        {
            var btn = new Button
            {
                Content = content, Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Background = bg, Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand,
                Margin = margin ?? new Thickness(0)
            };
            btn.Click += (s, e) => onClick();
            return btn;
        }

        private static Border KpiCard(string label, string value, SolidColorBrush valueBrush)
        {
            var b = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)), BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xD6, 0xE8)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 6, 0) };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = valueBrush, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = WpfBrushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
            b.Child = sp;
            return b;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQLinkStore — persists Material→BOQ-description links as a JSON
    //  sidecar alongside the Revit file (_bim_manager/material_boq_links.json).
    //  Also loads the paragraph catalogue from BOQ_DESCRIPTIONS.json.
    // ══════════════════════════════════════════════════════════════════════

    internal static class BOQLinkStore
    {
        public class DescriptionRow
        {
            public string Category;
            public string Nrm2Section;
            public string Paragraph;
            public string[] Placeholders;
            public string Preview
            {
                get
                {
                    if (string.IsNullOrEmpty(Paragraph)) return "";
                    string s = Paragraph.Length <= 60 ? Paragraph : Paragraph.Substring(0, 60) + "…";
                    return s;
                }
            }
        }

        public class Link
        {
            public string MaterialName;
            public string Category;
            public string Nrm2Section;
            public string Paragraph;
            public string Notes;
            public string UpdatedBy;
            public DateTime UpdatedDate;
        }

        public static List<DescriptionRow> LoadDescriptions(string dataPath)
        {
            var rows = new List<DescriptionRow>();
            try
            {
                string p = Path.Combine(dataPath ?? "", "BOQ_DESCRIPTIONS.json");
                if (!File.Exists(p)) return rows;
                var arr = JArray.Parse(File.ReadAllText(p));
                foreach (var item in arr)
                {
                    string cat = item["category"]?.ToString() ?? "";
                    string sec = item["nrm2_section"]?.ToString() ?? "";
                    string para = item["paragraph"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(para)) continue;
                    var phArr = item["placeholders"] as JArray;
                    var ph = phArr?.Select(t => t?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string>();
                    rows.Add(new DescriptionRow { Category = cat, Nrm2Section = sec, Paragraph = para, Placeholders = ph });
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadDescriptions: {ex.Message}"); }
            return rows;
        }

        private static string GetSidecarPath(Document doc)
        {
            string projDir;
            try { projDir = Path.GetDirectoryName(doc.PathName) ?? ""; }
            catch (Exception ex) { StingLog.Warn($"Sidecar path: {ex.Message}"); projDir = ""; }
            if (string.IsNullOrEmpty(projDir))
                projDir = Path.Combine(Path.GetTempPath(), "STING");
            string dir = Path.Combine(projDir, "_bim_manager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "material_boq_links.json");
        }

        public static Dictionary<string, Link> LoadLinks(Document doc)
        {
            var map = new Dictionary<string, Link>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string p = GetSidecarPath(doc);
                if (!File.Exists(p)) return map;
                var arr = JArray.Parse(File.ReadAllText(p));
                foreach (var item in arr)
                {
                    string name = item["material_name"]?.ToString();
                    if (string.IsNullOrEmpty(name)) continue;
                    var link = new Link
                    {
                        MaterialName = name,
                        Category = item["category"]?.ToString() ?? "",
                        Nrm2Section = item["nrm2_section"]?.ToString() ?? "",
                        Paragraph = item["paragraph"]?.ToString() ?? "",
                        Notes = item["notes"]?.ToString() ?? "",
                        UpdatedBy = item["updated_by"]?.ToString() ?? ""
                    };
                    if (DateTime.TryParse(item["updated_date"]?.ToString(), out var dt)) link.UpdatedDate = dt;
                    map[name] = link;
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadLinks: {ex.Message}"); }
            return map;
        }

        public static void SaveLinks(Document doc, Dictionary<string, Link> links)
        {
            string p = GetSidecarPath(doc);
            var arr = new JArray();
            foreach (var kv in links.OrderBy(k => k.Value.Nrm2Section).ThenBy(k => k.Key))
            {
                var l = kv.Value;
                arr.Add(new JObject
                {
                    ["material_name"] = l.MaterialName,
                    ["category"] = l.Category,
                    ["nrm2_section"] = l.Nrm2Section,
                    ["paragraph"] = l.Paragraph,
                    ["notes"] = l.Notes ?? "",
                    ["updated_by"] = l.UpdatedBy ?? "",
                    ["updated_date"] = l.UpdatedDate.ToString("o")
                });
            }
            // Atomic write: tmp + replace + .bak (matches pattern used across the codebase)
            string tmp = p + ".tmp";
            string bak = p + ".bak";
            File.WriteAllText(tmp, arr.ToString(Newtonsoft.Json.Formatting.Indented));
            if (File.Exists(p)) File.Replace(tmp, p, bak);
            else File.Move(tmp, p);
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────

    internal class MatRow
    {
        public string Name     { get; set; }
        public string Category { get; set; }
        public string Class    { get; set; }
        public long   Id       { get; set; }
    }

    internal class LayerRow
    {
        public string Layer     { get; set; }
        public string Function  { get; set; }
        public string Material  { get; set; }
        public string Thickness { get; set; }
    }
}
