using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// 3-section project folder setup dialog. Builds and persists a ProjectSetup,
    /// then calls ProjectFolderEngine.InitializeSetup to create the folder tree.
    /// </summary>
    public class ProjectFolderSetupDialog
    {
        // ── Theme ──
        private static readonly SolidColorBrush BgDark   = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
        private static readonly SolidColorBrush BgPanel  = new(Color.FromRgb(0x25, 0x25, 0x26));
        private static readonly SolidColorBrush BgInput  = new(Color.FromRgb(0x2D, 0x2D, 0x30));
        private static readonly SolidColorBrush FgWhite  = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush FgSubtle = new(Color.FromRgb(0xAA, 0xAA, 0xAA));
        private static readonly SolidColorBrush Accent   = new(Color.FromRgb(0x00, 0x78, 0xD4));
        private static readonly SolidColorBrush BrBorder = new(Color.FromRgb(0x40, 0x40, 0x40));
        private static readonly SolidColorBrush Yellow   = new(Color.FromRgb(0xFF, 0xC1, 0x07));
        private static readonly SolidColorBrush Green    = new(Color.FromRgb(0x4C, 0xAF, 0x50));

        public ProjectSetup Result { get; private set; }

        private readonly Document _doc;
        private readonly string _docPath;
        private Window _window;

        // Form state
        private TextBox _projCodeBox;
        private TextBox _projNameBox;
        private TextBox _rootBox;
        private TextBlock _previewLabel;
        private RadioButton _radioRelative;
        private RadioButton _radioAbsolute;
        private ComboBox _templateCombo;
        private RadioButton _radioBim;
        private RadioButton _radioMini;
        private WrapPanel _disciplinePanel;
        private StackPanel _disciplineRow;
        private ComboBox _namingCombo;
        private TextBox _customNamingBox;
        private StackPanel _customNamingRow;
        private DataGrid _foldersGrid;
        private Border _migrationBanner;
        private TextBlock _migrationText;

        private List<FolderTemplate> _templates;
        private ObservableCollection<FolderRow> _folderRows;

        public class FolderRow
        {
            public bool Include { get; set; } = true;
            public string Id { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public bool DiscSubfolders { get; set; }
            public string Routes { get; set; } = "";
            public bool IsCustom { get; set; }
        }

        public ProjectFolderSetupDialog(UIApplication uiapp)
        {
            _doc = uiapp?.ActiveUIDocument?.Document;
            _docPath = _doc?.PathName ?? "";
        }

        /// <summary>Show the dialog modally. Returns true on confirm.</summary>
        public bool? ShowDialog()
        {
            BuildWindow();
            PreloadDefaults();
            return _window.ShowDialog();
        }

        // ── Layout ────────────────────────────────────────────────────────

        private void BuildWindow()
        {
            _window = new Window
            {
                Title = "Project Folder Setup",
                Width = 720,
                Height = 640,
                Background = BgDark,
                Foreground = FgWhite,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
            };

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(12) };

            // Footer
            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Migration banner (top)
            _migrationBanner = BuildMigrationBanner();
            DockPanel.SetDock(_migrationBanner, Dock.Top);
            root.Children.Add(_migrationBanner);

            // Body — scrollable stack
            var body = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = BgDark,
            };
            var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            stack.Children.Add(BuildIdentitySection());
            stack.Children.Add(BuildSeparator());
            stack.Children.Add(BuildStructureSection());
            stack.Children.Add(BuildSeparator());
            stack.Children.Add(BuildFoldersSection());
            body.Content = stack;
            root.Children.Add(body);

            _window.Content = root;
        }

        private static UIElement BuildSeparator() => new Border
        {
            BorderBrush = BrBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 12, 0, 12),
        };

        private TextBlock SectionHeader(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Accent,
            Margin = new Thickness(0, 0, 0, 8),
        };

        private TextBlock Label(string text) => new()
        {
            Text = text,
            Foreground = FgWhite,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        private TextBox MakeTextBox(string initial, double width = 280) => new()
        {
            Text = initial ?? "",
            Width = width,
            Background = BgInput,
            Foreground = FgWhite,
            BorderBrush = BrBorder,
            Padding = new Thickness(4, 3, 4, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        private Button MakeButton(string label, double width = 100)
        {
            var b = new Button
            {
                Content = label,
                Width = width,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                Background = BgInput,
                Foreground = FgWhite,
                BorderBrush = BrBorder,
                FontSize = 12,
            };
            return b;
        }

        // ── Section 1: Identity ────────────────────────────────────────────

        private UIElement BuildIdentitySection()
        {
            var box = new StackPanel();
            box.Children.Add(SectionHeader("PROJECT IDENTITY"));

            // Project code row
            var row1 = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row1.Children.Add(Label("Project Code"));
            _projCodeBox = MakeTextBox("", 200);
            _projCodeBox.TextChanged += (s, e) => UpdatePreview();
            row1.Children.Add(_projCodeBox);
            var detect = MakeButton("↺ Detect", 80);
            detect.ToolTip = "Re-read code from Revit Project Information";
            detect.Click += (s, e) =>
            {
                if (_doc != null) _projCodeBox.Text = ProjectFolderEngine.DetectProjectCode(_doc);
            };
            row1.Children.Add(detect);
            box.Children.Add(row1);

            // Project name row
            var row2 = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row2.Children.Add(Label("Project Name"));
            _projNameBox = MakeTextBox("", 380);
            _projNameBox.IsReadOnly = true;
            _projNameBox.Background = BgPanel;
            row2.Children.Add(_projNameBox);
            box.Children.Add(row2);

            // Root path row
            var row3 = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            row3.Children.Add(Label("Root Folder"));
            _rootBox = MakeTextBox("", 380);
            _rootBox.TextChanged += (s, e) => UpdatePreview();
            row3.Children.Add(_rootBox);
            var browse = MakeButton("Browse…", 80);
            browse.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Choose project root folder (save the dummy file to select the parent)",
                    FileName = "_ROOT_HERE",
                    Filter = "Folder selection|*.*",
                    OverwritePrompt = false,
                };
                if (!string.IsNullOrEmpty(_rootBox.Text) && Directory.Exists(_rootBox.Text))
                    dlg.InitialDirectory = _rootBox.Text;
                if (dlg.ShowDialog() == true)
                {
                    string chosen = Path.GetDirectoryName(dlg.FileName);
                    if (!string.IsNullOrEmpty(chosen)) _rootBox.Text = chosen;
                }
            };
            row3.Children.Add(browse);
            box.Children.Add(row3);

            // Relative/Absolute radios
            var row4 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(110, 0, 0, 4) };
            _radioRelative = new RadioButton
            {
                Content = "Relative to model file (portable)",
                Foreground = FgWhite,
                Margin = new Thickness(0, 0, 16, 0),
                IsChecked = true,
            };
            _radioRelative.Checked += (s, e) => UpdatePreview();
            _radioAbsolute = new RadioButton
            {
                Content = "Absolute path",
                Foreground = FgWhite,
            };
            _radioAbsolute.Checked += (s, e) => UpdatePreview();
            row4.Children.Add(_radioRelative);
            row4.Children.Add(_radioAbsolute);
            box.Children.Add(row4);

            // Preview label
            _previewLabel = new TextBlock
            {
                Foreground = FgSubtle,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(110, 4, 0, 0),
            };
            box.Children.Add(_previewLabel);

            return box;
        }

        // ── Section 2: Structure ───────────────────────────────────────────

        private UIElement BuildStructureSection()
        {
            var box = new StackPanel();
            box.Children.Add(SectionHeader("STRUCTURE"));

            // Template row
            var row1 = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row1.Children.Add(Label("Template"));
            _templateCombo = new ComboBox
            {
                Width = 320,
                Background = BgInput,
                Foreground = FgWhite,
                BorderBrush = BrBorder,
            };
            _templateCombo.SelectionChanged += OnTemplateChanged;
            row1.Children.Add(_templateCombo);
            var saveTpl = MakeButton("Save as…", 90);
            saveTpl.Click += OnSaveTemplate;
            row1.Children.Add(saveTpl);
            var delTpl = MakeButton("Delete", 70);
            delTpl.Click += OnDeleteTemplate;
            row1.Children.Add(delTpl);
            box.Children.Add(row1);

            // Mode radios
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(110, 0, 0, 8) };
            _radioBim = new RadioButton
            {
                Content = "BIM Project (full ISO 19650 — 16 numbered folders)",
                Foreground = FgWhite,
                Margin = new Thickness(0, 0, 16, 0),
                IsChecked = true,
            };
            _radioBim.Checked += (s, e) => OnModeChanged();
            _radioMini = new RadioButton
            {
                Content = "Mini Project (5 flat folders)",
                Foreground = FgWhite,
            };
            _radioMini.Checked += (s, e) => OnModeChanged();
            row2.Children.Add(_radioBim);
            row2.Children.Add(_radioMini);
            box.Children.Add(row2);

            // Discipline row
            _disciplineRow = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var discRowInner = new DockPanel();
            discRowInner.Children.Add(Label("Disciplines"));
            _disciplinePanel = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var d in new[] { "A_Architectural", "E_Electrical", "M_Mechanical", "P_Plumbing",
                                       "S_Structural", "FP_Fire", "LV_LowVoltage", "Z_General" })
            {
                var cb = new CheckBox
                {
                    Content = d,
                    Foreground = FgWhite,
                    Margin = new Thickness(0, 2, 12, 2),
                    Tag = d,
                    IsChecked = ProjectSetup.DefaultBimDisciplines.Contains(d),
                };
                _disciplinePanel.Children.Add(cb);
            }
            discRowInner.Children.Add(_disciplinePanel);
            _disciplineRow.Children.Add(discRowInner);
            box.Children.Add(_disciplineRow);

            // Naming convention
            var row4 = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row4.Children.Add(Label("Export Naming"));
            _namingCombo = new ComboBox
            {
                Width = 320,
                Background = BgInput,
                Foreground = FgWhite,
                BorderBrush = BrBorder,
            };
            _namingCombo.Items.Add("ISO 19650 (code-based)");
            _namingCombo.Items.Add("Timestamp (date-time suffix)");
            _namingCombo.Items.Add("Custom pattern");
            _namingCombo.SelectedIndex = 0;
            _namingCombo.SelectionChanged += (s, e) => _customNamingRow.Visibility =
                _namingCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            row4.Children.Add(_namingCombo);
            box.Children.Add(row4);

            _customNamingRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(110, 0, 0, 8),
                Visibility = Visibility.Collapsed,
            };
            _customNamingRow.Children.Add(new TextBlock
            {
                Text = "Pattern (tokens: {name} {date} {time} {code} {ext}):",
                Foreground = FgSubtle,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _customNamingBox = MakeTextBox("{name}_{date}_{code}{ext}", 280);
            _customNamingRow.Children.Add(_customNamingBox);
            box.Children.Add(_customNamingRow);

            return box;
        }

        // ── Section 3: Folders ─────────────────────────────────────────────

        private UIElement BuildFoldersSection()
        {
            var box = new StackPanel();
            box.Children.Add(SectionHeader("FOLDERS (BIM mode)"));

            _folderRows = new ObservableCollection<FolderRow>();
            _foldersGrid = new DataGrid
            {
                Background = BgInput,
                Foreground = FgWhite,
                BorderBrush = BrBorder,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                Height = 200,
                ItemsSource = _folderRows,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowBackground = BgInput,
                AlternatingRowBackground = BgPanel,
                ColumnHeaderHeight = 24,
            };
            _foldersGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Include",
                Binding = new System.Windows.Data.Binding(nameof(FolderRow.Include)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = 60,
            });
            _foldersGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "ID",
                Binding = new System.Windows.Data.Binding(nameof(FolderRow.Id)),
                IsReadOnly = true,
                Width = 110,
            });
            _foldersGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Display Name",
                Binding = new System.Windows.Data.Binding(nameof(FolderRow.DisplayName)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = 180,
            });
            _foldersGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Disc. Subs",
                Binding = new System.Windows.Data.Binding(nameof(FolderRow.DiscSubfolders)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = 80,
            });
            _foldersGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Routes",
                Binding = new System.Windows.Data.Binding(nameof(FolderRow.Routes)),
                IsReadOnly = true,
                Width = 240,
            });
            box.Children.Add(_foldersGrid);

            var addBtn = MakeButton("+ Add Custom Folder", 160);
            addBtn.Margin = new Thickness(0, 8, 0, 0);
            addBtn.Click += (s, e) =>
            {
                int n = _folderRows.Count(r => r.IsCustom) + 1;
                _folderRows.Add(new FolderRow
                {
                    Include = true,
                    Id = $"CUSTOM_{n:D2}",
                    DisplayName = "",
                    DiscSubfolders = false,
                    Routes = "(custom)",
                    IsCustom = true,
                });
            };
            box.Children.Add(addBtn);

            return box;
        }

        // ── Migration banner ───────────────────────────────────────────────

        private Border BuildMigrationBanner()
        {
            _migrationText = new TextBlock
            {
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 4, 8, 4),
            };
            var btn = MakeButton("Migrate Now", 110);
            btn.Background = Yellow;
            btn.Foreground = Brushes.Black;
            btn.Click += OnMigrate;
            var dock = new DockPanel();
            DockPanel.SetDock(btn, Dock.Right);
            dock.Children.Add(btn);
            dock.Children.Add(_migrationText);

            var border = new Border
            {
                Background = Yellow,
                BorderBrush = Yellow,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(4),
                Child = dock,
                Visibility = Visibility.Collapsed,
            };
            return border;
        }

        // ── Footer ─────────────────────────────────────────────────────────

        private UIElement BuildFooter()
        {
            var dock = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };

            var help = new TextBlock
            {
                Foreground = FgSubtle,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var helpLink = new Hyperlink(new Run("? Help"))
            {
                Foreground = Accent,
                NavigateUri = new Uri("https://planscape.com/docs/folder-setup"),
            };
            helpLink.RequestNavigate += (s, e) => StingLog.Info("Folder setup help requested.");
            help.Inlines.Add(helpLink);
            DockPanel.SetDock(help, Dock.Left);
            dock.Children.Add(help);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            var cancel = MakeButton("Cancel", 90);
            cancel.Click += (s, e) => { _window.DialogResult = false; _window.Close(); };
            var ok = MakeButton("Create Folders", 130);
            ok.Background = Accent;
            ok.Foreground = FgWhite;
            ok.FontWeight = FontWeights.Bold;
            ok.Click += OnCreateFolders;
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            dock.Children.Add(buttons);

            return dock;
        }

        // ── Defaults ───────────────────────────────────────────────────────

        private void PreloadDefaults()
        {
            // Project info
            string code = "PRJ", name = "";
            if (_doc != null)
            {
                code = ProjectFolderEngine.DetectProjectCode(_doc);
                try { name = _doc.ProjectInformation?.Name ?? ""; } catch { }
            }
            _projCodeBox.Text = code;
            _projNameBox.Text = name;

            // Default root: relative folder named after project code
            _rootBox.Text = code;

            // Templates
            _templates = LoadTemplates();
            foreach (var t in _templates) _templateCombo.Items.Add(t.Name + (t.IsBuiltIn ? "" : "  (user)"));
            _templateCombo.SelectedIndex = 0;

            // Migration check
            CheckMigrationBanner();

            UpdatePreview();
        }

        private List<FolderTemplate> LoadTemplates()
        {
            string templateDir = "";
            try
            {
                string dataDir = ProjectFolderEngine.GetDataPath(_doc);
                if (!string.IsNullOrEmpty(dataDir))
                    templateDir = Path.Combine(dataDir, "folder_templates");
            }
            catch { }
            return FolderTemplateLibrary.GetAll(templateDir);
        }

        private void CheckMigrationBanner()
        {
            if (_doc == null || string.IsNullOrEmpty(_docPath))
            {
                _migrationBanner.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                string projDir = Path.GetDirectoryName(_docPath);
                int sidecars = 0, legacy = 0;
                if (!string.IsNullOrEmpty(projDir))
                {
                    foreach (string f in Directory.GetFiles(projDir, "*.sting_*.json")) sidecars++;
                    foreach (string f in Directory.GetFiles(projDir, "*_STING_SEQ.json")) sidecars++;
                    foreach (string n in new[] { "_BIM_COORD", "STING_BIM_MANAGER", "STING_Exports", "STING_Project" })
                    {
                        string p = Path.Combine(projDir, n);
                        if (Directory.Exists(p)) legacy++;
                    }
                }
                if (sidecars > 0 || legacy > 0)
                {
                    _migrationText.Text = $"Legacy STING data detected: {legacy} folder(s) and {sidecars} sidecar JSON file(s) " +
                                          "alongside the model. Click 'Migrate Now' to consolidate them into the new structure.";
                    _migrationBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    _migrationBanner.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Migration banner: {ex.Message}"); }
        }

        // ── Handlers ───────────────────────────────────────────────────────

        private void UpdatePreview()
        {
            if (_previewLabel == null) return;
            string code = (_projCodeBox?.Text ?? "PRJ").Trim();
            string root = (_rootBox?.Text ?? "").Trim();
            string projDir = "";
            try { projDir = Path.GetDirectoryName(_docPath) ?? ""; } catch { }

            string resolved;
            if (_radioRelative?.IsChecked == true)
            {
                if (string.IsNullOrEmpty(root)) root = code;
                resolved = string.IsNullOrEmpty(projDir) ? root : Path.Combine(projDir, root);
            }
            else
            {
                resolved = string.IsNullOrEmpty(root) ? "(no path)" : root;
            }

            _previewLabel.Text = $"Folders will be created at: {resolved}";
        }

        private void OnTemplateChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = _templateCombo.SelectedIndex;
            if (idx < 0 || idx >= _templates.Count) return;
            var t = _templates[idx];

            if (t.Mode == ProjectFolderMode.Mini) _radioMini.IsChecked = true;
            else _radioBim.IsChecked = true;

            // Apply disciplines
            foreach (var child in _disciplinePanel.Children)
            {
                if (child is CheckBox cb)
                    cb.IsChecked = t.Disciplines != null && t.Disciplines.Contains((string)cb.Tag);
            }

            // Apply naming
            _namingCombo.SelectedIndex = (int)t.NamingConvention;

            // Repopulate folder rows
            RebuildFolderRowsFromTemplate(t);
        }

        private void RebuildFolderRowsFromTemplate(FolderTemplate t)
        {
            _folderRows.Clear();
            if (t.CustomFolders == null) return;
            foreach (var f in t.CustomFolders)
            {
                _folderRows.Add(new FolderRow
                {
                    Include = !t.HiddenFolders.Contains(f.Id, StringComparer.OrdinalIgnoreCase),
                    Id = f.Id,
                    DisplayName = f.DisplayName,
                    DiscSubfolders = f.HasDisciplineSubfolders,
                    Routes = SummariseRoutes(t.ExportRoutes, f.Id),
                    IsCustom = f.IsCustom,
                });
            }
        }

        private static string SummariseRoutes(Dictionary<string, string> routes, string folderId)
        {
            if (routes == null) return "";
            var keys = routes.Where(kv => string.Equals(kv.Value, folderId, StringComparison.OrdinalIgnoreCase))
                             .Select(kv => kv.Key)
                             .Take(8)
                             .ToList();
            return string.Join(", ", keys);
        }

        private void OnModeChanged()
        {
            if (_disciplineRow == null) return;
            _disciplineRow.Visibility = _radioBim?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            // Folder grid only meaningful in BIM mode
            if (_foldersGrid != null)
                _foldersGrid.IsEnabled = _radioBim?.IsChecked == true;
        }

        private void OnSaveTemplate(object sender, RoutedEventArgs e)
        {
            string name = PromptText("Save folder template", "Template name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var setup = BuildSetupFromForm();
            var template = new FolderTemplate
            {
                Name = name,
                Description = "User-saved template",
                Mode = setup.Mode,
                Disciplines = new List<string>(setup.Disciplines),
                CustomFolders = setup.CustomFolders,
                HiddenFolders = setup.HiddenFolders,
                ExportRoutes = setup.ExportRoutes,
                NamingConvention = setup.NamingConvention,
                IsBuiltIn = false,
            };
            string templateDir = "";
            try
            {
                string dataDir = ProjectFolderEngine.GetDataPath(_doc);
                if (!string.IsNullOrEmpty(dataDir)) templateDir = Path.Combine(dataDir, "folder_templates");
            }
            catch { }
            FolderTemplateLibrary.SaveUserTemplate(template, templateDir);
            // Reload combo
            _templates = LoadTemplates();
            _templateCombo.Items.Clear();
            foreach (var t in _templates) _templateCombo.Items.Add(t.Name + (t.IsBuiltIn ? "" : "  (user)"));
            _templateCombo.SelectedIndex = _templates.FindIndex(t => t.Name == name);
        }

        private void OnDeleteTemplate(object sender, RoutedEventArgs e)
        {
            int idx = _templateCombo.SelectedIndex;
            if (idx < 0 || idx >= _templates.Count) return;
            var t = _templates[idx];
            if (t.IsBuiltIn)
            {
                TaskDialog.Show("Folder Setup", "Built-in templates cannot be deleted.");
                return;
            }
            string templateDir = "";
            try
            {
                string dataDir = ProjectFolderEngine.GetDataPath(_doc);
                if (!string.IsNullOrEmpty(dataDir)) templateDir = Path.Combine(dataDir, "folder_templates");
            }
            catch { }
            FolderTemplateLibrary.DeleteUserTemplate(t.Name, templateDir);
            _templates = LoadTemplates();
            _templateCombo.Items.Clear();
            foreach (var x in _templates) _templateCombo.Items.Add(x.Name + (x.IsBuiltIn ? "" : "  (user)"));
            _templateCombo.SelectedIndex = 0;
        }

        private void OnMigrate(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            try
            {
                var rep = ProjectFolderEngine.MigrateFromLegacy(_doc);
                TaskDialog.Show("STING Migration",
                    $"Moved {rep.FilesMoved} files. Removed {rep.FoldersRemoved} legacy folders." +
                    (rep.Warnings.Count > 0 ? $"\n\nWarnings: {rep.Warnings.Count}" : ""));
                CheckMigrationBanner();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING Migration", $"Migration failed: {ex.Message}");
            }
        }

        private void OnCreateFolders(object sender, RoutedEventArgs e)
        {
            string code = (_projCodeBox?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(code))
            {
                TaskDialog.Show("Folder Setup", "Project code is required.");
                return;
            }
            string root = (_rootBox?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(root))
            {
                TaskDialog.Show("Folder Setup", "Root folder location is required.");
                return;
            }

            try
            {
                var setup = BuildSetupFromForm();
                Result = setup;
                ProjectFolderEngine.InitializeSetup(_doc, setup);
                _window.DialogResult = true;
                _window.Close();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Folder Setup", $"Failed to create folders: {ex.Message}");
            }
        }

        private ProjectSetup BuildSetupFromForm()
        {
            string code = (_projCodeBox?.Text ?? "PRJ").Trim();
            string root = (_rootBox?.Text ?? code).Trim();
            bool isRelative = _radioRelative?.IsChecked == true;

            ProjectSetup setup;
            bool isMini = _radioMini?.IsChecked == true;
            if (isMini)
            {
                setup = ProjectSetup.CreateMini(code, root);
            }
            else
            {
                var disciplines = new List<string>();
                foreach (var child in _disciplinePanel.Children)
                {
                    if (child is CheckBox cb && cb.IsChecked == true)
                        disciplines.Add((string)cb.Tag);
                }
                setup = ProjectSetup.CreateBIM(code, root, disciplines);

                // Merge folder grid edits
                if (_folderRows != null && _folderRows.Count > 0)
                {
                    var newFolders = new List<FolderDef>();
                    var hidden = new List<string>();
                    foreach (var r in _folderRows)
                    {
                        if (string.IsNullOrWhiteSpace(r.Id)) continue;
                        if (!r.Include)
                        {
                            hidden.Add(r.Id);
                            continue;
                        }
                        var existing = setup.GetFolder(r.Id);
                        newFolders.Add(new FolderDef
                        {
                            Id = r.Id,
                            DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? (existing?.DisplayName ?? r.Id) : r.DisplayName,
                            HasDisciplineSubfolders = r.DiscSubfolders,
                            SubFolders = existing?.SubFolders ?? new List<string>(),
                            IsCustom = r.IsCustom,
                        });
                    }
                    setup.CustomFolders = newFolders;
                    setup.HiddenFolders = hidden;
                }
            }

            setup.RootPath = root;
            setup.RootPathIsRelative = isRelative;
            setup.ProjectName = _projNameBox?.Text ?? "";
            setup.NamingConvention = (NamingConvention)Math.Max(0, _namingCombo.SelectedIndex);
            if (setup.NamingConvention == NamingConvention.Custom)
                setup.CustomNamingPattern = _customNamingBox?.Text ?? "";

            int idx = _templateCombo.SelectedIndex;
            if (idx >= 0 && idx < _templates.Count) setup.TemplateName = _templates[idx].Name;

            return setup;
        }

        // ── Mini text-prompt dialog ────────────────────────────────────────

        private string PromptText(string title, string prompt)
        {
            var w = new Window
            {
                Title = title,
                Width = 400,
                Height = 160,
                Background = BgDark,
                Foreground = FgWhite,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _window,
            };
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = prompt, Foreground = FgWhite, Margin = new Thickness(0, 0, 0, 8) });
            var tb = MakeTextBox("", 360);
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = MakeButton("OK", 80);
            string result = null;
            ok.Click += (s, e) => { result = tb.Text; w.DialogResult = true; w.Close(); };
            var cancel = MakeButton("Cancel", 80);
            cancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };
            row.Children.Add(cancel);
            row.Children.Add(ok);
            sp.Children.Add(row);
            w.Content = sp;
            tb.Focus();
            return w.ShowDialog() == true ? result : null;
        }
    }
}
