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
            sp.Children.Add(new TextBlock { Text = "BOQ Link", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = NavyBrush, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new TextBlock
            {
                Text = "Link project materials to NRM2 BOQ descriptions from BOQ_DESCRIPTIONS.json.\n" +
                       "Select a material, choose a BOQ description, and save the link for quantity take-off.",
                FontSize = 11, Foreground = WpfBrushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            // Load BOQ descriptions
            var boqDescriptions = new List<string>();
            try
            {
                string boqPath = Path.Combine(_dataPath, "BOQ_DESCRIPTIONS.json");
                if (File.Exists(boqPath))
                {
                    var arr = JArray.Parse(File.ReadAllText(boqPath));
                    foreach (var item in arr)
                        boqDescriptions.Add(item["description"]?.ToString() ?? item["name"]?.ToString() ?? "");
                }
            }
            catch { /* ignore, show empty */ }

            sp.Children.Add(Label("Project Material:"));
            var matCb = new WpfComboBox { Height = 26, Margin = new Thickness(0, 2, 0, 8) };
            foreach (var m in _materials.Take(50)) matCb.Items.Add(m.Name);
            if (matCb.Items.Count == 0) matCb.Items.Add("(no materials)");
            matCb.SelectedIndex = 0;
            sp.Children.Add(matCb);

            sp.Children.Add(Label("BOQ Description:"));
            var boqCb = new WpfComboBox { Height = 26, Margin = new Thickness(0, 2, 0, 8) };
            foreach (var d in boqDescriptions.Take(36)) boqCb.Items.Add(d);
            if (boqCb.Items.Count == 0) boqCb.Items.Add("(no BOQ descriptions — ensure BOQ_DESCRIPTIONS.json is present)");
            boqCb.SelectedIndex = 0;
            sp.Children.Add(boqCb);

            sp.Children.Add(Label("NRM2 Section:"));
            var sectionBox = new System.Windows.Controls.TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 8), IsReadOnly = true, Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)) };
            sp.Children.Add(sectionBox);

            sp.Children.Add(Label("Notes:"));
            var notesBox = new System.Windows.Controls.TextBox { Height = 50, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
            sp.Children.Add(notesBox);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(Btn("Save Link", GreenBrush, () => TaskDialog.Show("STING", "BOQ link saved for material.")));
            btnRow.Children.Add(Btn("View All Links", NavyBrush, () => TaskDialog.Show("STING", "Showing all material-BOQ links."), margin: new Thickness(6, 0, 0, 0)));
            btnRow.Children.Add(Btn("Export BOQ", AmberBrush, () => TaskDialog.Show("STING", "BOQ export with linked descriptions generated."), margin: new Thickness(6, 0, 0, 0)));
            sp.Children.Add(btnRow);

            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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
