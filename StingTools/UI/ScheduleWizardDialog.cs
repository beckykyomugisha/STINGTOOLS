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
    /// Result returned from the ScheduleWizardDialog.
    /// </summary>
    public class ScheduleWizardResult
    {
        public bool Confirmed { get; set; }
        public string Operation { get; set; } = string.Empty;
        public List<string> SelectedSchedules { get; set; } = new();
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>
    /// Unified WPF dialog for schedule management operations.
    /// 3-section layout: operation selector, schedule list, dynamic options.
    /// Replaces multi-step TaskDialog chains with a single-window interface.
    /// </summary>
    internal static class ScheduleWizardDialog
    {
        // ── Operations ──────────────────────────────────────────────────
        private const string OpCreateBatch = "CreateBatch";
        private const string OpAutoPopulate = "AutoPopulate";
        private const string OpFullAuto = "FullAuto";
        private const string OpAudit = "Audit";
        private const string OpExportCsv = "ExportCSV";
        private const string OpManage = "Manage";

        // ── Theme colours (light theme) ─────────────────────────────────
        private static readonly Color BgColor = Color.FromRgb(0xF5, 0xF5, 0xF5);
        private static readonly Color PanelBg = Colors.White;
        private static readonly Color HeaderBg = Color.FromRgb(0x33, 0x33, 0x33);
        private static readonly Color AccentOrange = Color.FromRgb(0xE8, 0x91, 0x2D);
        private static readonly Color FgDark = Color.FromRgb(0x22, 0x22, 0x22);
        private static readonly Color FgDim = Color.FromRgb(0x88, 0x88, 0x88);
        private static readonly Color BorderClr = Color.FromRgb(0xD0, 0xD0, 0xD0);
        private static readonly Color SelectedBg = Color.FromRgb(0xFD, 0xF0, 0xDD);
        private static readonly Color HoverBg = Color.FromRgb(0xF0, 0xF0, 0xF0);

        private static SolidColorBrush FZ(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static readonly SolidColorBrush BrBg = FZ(BgColor);
        private static readonly SolidColorBrush BrPanel = FZ(PanelBg);
        private static readonly SolidColorBrush BrHeader = FZ(HeaderBg);
        private static readonly SolidColorBrush BrAccent = FZ(AccentOrange);
        private static readonly SolidColorBrush BrFg = FZ(FgDark);
        private static readonly SolidColorBrush BrFgDim = FZ(FgDim);
        private static readonly SolidColorBrush BrBorder = FZ(BorderClr);
        private static readonly SolidColorBrush BrSelected = FZ(SelectedBg);
        private static readonly SolidColorBrush BrWhite = FZ(Colors.White);
        private static readonly SolidColorBrush BrTransparent = Brushes.Transparent;

        /// <summary>
        /// Show the schedule wizard dialog.
        /// </summary>
        /// <param name="csvDefinitions">Schedule names from CSV definitions.</param>
        /// <param name="existingSchedules">Schedule names already in the project.</param>
        public static ScheduleWizardResult Show(
            List<string> csvDefinitions = null,
            List<string> existingSchedules = null)
        {
            csvDefinitions ??= new List<string>();
            existingSchedules ??= new List<string>();

            var result = new ScheduleWizardResult();
            var allItems = BuildScheduleItems(csvDefinitions, existingSchedules);

            var win = new Window
            {
                Title = "STING Schedule Wizard",
                Width = 650,
                Height = 500,
                MinWidth = 550,
                MinHeight = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = BrBg,
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            // Set Revit as owner
            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = handle;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScheduleWizardDialog: Could not set owner — {ex.Message}");
            }

            // ── Root layout ─────────────────────────────────────────────
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // 0: header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // 1: operation selector
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: schedule list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // 3: options panel
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // 4: buttons

            // ── Row 0: Header ───────────────────────────────────────────
            var header = new Border
            {
                Background = BrHeader,
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "Schedule Wizard",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrWhite
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = $"{csvDefinitions.Count} definitions available | {existingSchedules.Count} existing",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Row 1: Operation selector ───────────────────────────────
            string selectedOp = OpCreateBatch;
            var opPanel = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var opLabel = new TextBlock
            {
                Text = "OPERATION",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDim,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var opWrap = new WrapPanel { Orientation = Orientation.Horizontal };

            var opButtons = new Dictionary<string, Border>();
            var opDefs = new[]
            {
                (OpCreateBatch,   "Create Batch",   "Create schedules from CSV definitions"),
                (OpAutoPopulate,  "Auto-Populate",   "Fill schedule fields with token data"),
                (OpFullAuto,      "Full Auto",       "Zero-input: populate + create + formulas"),
                (OpAudit,         "Audit",           "Compare existing vs CSV definitions"),
                (OpExportCsv,     "Export CSV",       "Export schedule data to file"),
                (OpManage,        "Manage",          "Duplicate, delete, refresh, compare")
            };

            // Panels that need the schedule list
            var listOps = new HashSet<string> { OpCreateBatch, OpAudit, OpExportCsv, OpManage };

            // Controls we need to reference across closures
            Border scheduleSection = null;
            StackPanel optionsContainer = null;
            ListBox scheduleListBox = null;
            TextBox searchBox = null;
            TextBlock matchCountText = null;
            var scheduleItems = new List<ScheduleItem>(allItems);

            // Dynamic options controls
            // Create
            CheckBox chkDiscM = null, chkDiscE = null, chkDiscP = null, chkDiscA = null, chkDiscS = null, chkDiscFP = null;
            // AutoPopulate
            CheckBox chkOverwrite = null, chkFormulas = null;
            // Export
            TextBox txtExportPath = null;
            ComboBox cmbFormat = null;
            // Manage
            ComboBox cmbManageOp = null;

            void UpdateOptionsPanel()
            {
                if (optionsContainer == null) return;
                optionsContainer.Children.Clear();

                switch (selectedOp)
                {
                    case OpCreateBatch:
                        BuildCreateOptions(optionsContainer, ref chkDiscM, ref chkDiscE, ref chkDiscP, ref chkDiscA, ref chkDiscS, ref chkDiscFP);
                        break;
                    case OpAutoPopulate:
                        BuildAutoPopulateOptions(optionsContainer, ref chkOverwrite, ref chkFormulas);
                        break;
                    case OpExportCsv:
                        BuildExportOptions(optionsContainer, ref txtExportPath, ref cmbFormat);
                        break;
                    case OpManage:
                        BuildManageOptions(optionsContainer, ref cmbManageOp);
                        break;
                    case OpFullAuto:
                        optionsContainer.Children.Add(new TextBlock
                        {
                            Text = "Full Auto will populate tokens, create all schedules, and evaluate formulas in one step. No additional options required.",
                            Foreground = BrFgDim,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(4)
                        });
                        break;
                    case OpAudit:
                        optionsContainer.Children.Add(new TextBlock
                        {
                            Text = "Audit compares existing project schedules against CSV definitions and reports missing, extra, and mismatched fields.",
                            Foreground = BrFgDim,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(4)
                        });
                        break;
                }
            }

            void SelectOperation(string op)
            {
                selectedOp = op;
                foreach (var kvp in opButtons)
                {
                    bool isSel = kvp.Key == op;
                    kvp.Value.BorderBrush = isSel ? BrAccent : BrBorder;
                    kvp.Value.BorderThickness = new Thickness(isSel ? 2 : 1);
                    kvp.Value.Background = isSel ? BrSelected : BrPanel;
                }
                // Show/hide schedule list
                if (scheduleSection != null)
                    scheduleSection.Visibility = listOps.Contains(op) ? Visibility.Visible : Visibility.Collapsed;

                UpdateOptionsPanel();
            }

            foreach (var (code, label, tooltip) in opDefs)
            {
                var btn = new Border
                {
                    BorderBrush = BrBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Background = BrPanel,
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip
                };
                var btnText = new TextBlock
                {
                    Text = label,
                    FontSize = 11.5,
                    Foreground = BrFg,
                    FontWeight = FontWeights.Medium
                };
                btn.Child = btnText;
                opButtons[code] = btn;

                string capturedCode = code;
                btn.MouseLeftButtonDown += (_, __) => SelectOperation(capturedCode);
                opWrap.Children.Add(btn);
            }

            var opStack = new StackPanel();
            opStack.Children.Add(opLabel);
            opStack.Children.Add(opWrap);
            opPanel.Child = opStack;
            Grid.SetRow(opPanel, 1);
            root.Children.Add(opPanel);

            // ── Row 2: Schedule list ────────────────────────────────────
            scheduleSection = new Border
            {
                Padding = new Thickness(12, 8, 12, 4),
                Visibility = Visibility.Visible
            };
            var listStack = new StackPanel();

            // Search row
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            searchBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Background = BrWhite
            };

            matchCountText = new TextBlock
            {
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Text = $"{allItems.Count} items"
            };
            DockPanel.SetDock(matchCountText, Dock.Right);
            searchRow.Children.Add(matchCountText);
            searchRow.Children.Add(searchBox);
            listStack.Children.Add(searchRow);

            // Select All / Clear All
            var selRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var btnSelectAll = CreateLinkButton("Select All");
            var btnClearAll = CreateLinkButton("Clear All");
            selRow.Children.Add(btnSelectAll);
            selRow.Children.Add(new TextBlock
            {
                Text = " | ",
                Foreground = BrFgDim,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            selRow.Children.Add(btnClearAll);
            listStack.Children.Add(selRow);

            // ListBox
            scheduleListBox = new ListBox
            {
                Height = 300,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Background = BrWhite,
                SelectionMode = SelectionMode.Multiple,
                FontSize = 12
            };

            void RefreshList(string filter)
            {
                scheduleListBox.Items.Clear();
                var filtered = string.IsNullOrWhiteSpace(filter)
                    ? scheduleItems
                    : scheduleItems.Where(s => s.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                foreach (var item in filtered)
                {
                    var cb = new CheckBox
                    {
                        Content = FormatScheduleItem(item),
                        Tag = item,
                        IsChecked = item.IsSelected,
                        FontSize = 12,
                        Foreground = BrFg,
                        Margin = new Thickness(2)
                    };
                    cb.Checked += (_, __) => { item.IsSelected = true; UpdateMatchCount(); };
                    cb.Unchecked += (_, __) => { item.IsSelected = false; UpdateMatchCount(); };
                    scheduleListBox.Items.Add(cb);
                }
                UpdateMatchCount();
            }

            void UpdateMatchCount()
            {
                int sel = scheduleItems.Count(s => s.IsSelected);
                int vis = scheduleListBox.Items.Count;
                matchCountText.Text = $"{sel} selected | {vis} shown";
            }

            searchBox.TextChanged += (_, __) => RefreshList(searchBox.Text);

            btnSelectAll.MouseLeftButtonDown += (_, __) =>
            {
                foreach (var item in scheduleItems) item.IsSelected = true;
                RefreshList(searchBox.Text);
            };
            btnClearAll.MouseLeftButtonDown += (_, __) =>
            {
                foreach (var item in scheduleItems) item.IsSelected = false;
                RefreshList(searchBox.Text);
            };

            listStack.Children.Add(scheduleListBox);
            scheduleSection.Child = listStack;
            Grid.SetRow(scheduleSection, 2);
            root.Children.Add(scheduleSection);

            // ── Row 3: Options panel ────────────────────────────────────
            var optionsBorder = new Border
            {
                Background = BrPanel,
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var optionsOuter = new StackPanel();
            optionsOuter.Children.Add(new TextBlock
            {
                Text = "OPTIONS",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrFgDim,
                Margin = new Thickness(0, 0, 0, 6)
            });
            optionsContainer = new StackPanel();
            optionsOuter.Children.Add(optionsContainer);
            optionsBorder.Child = optionsOuter;
            Grid.SetRow(optionsBorder, 3);
            root.Children.Add(optionsBorder);

            // ── Row 4: Action buttons ───────────────────────────────────
            var btnRow = new Border
            {
                Padding = new Thickness(12, 8, 12, 10)
            };
            var btnPanel = new DockPanel { LastChildFill = false };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                FontSize = 12,
                Background = BrWhite,
                BorderBrush = BrBorder,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (_, __) => { win.DialogResult = false; };
            DockPanel.SetDock(btnCancel, Dock.Right);
            btnPanel.Children.Add(btnCancel);

            var btnExecute = new Button
            {
                Content = "Execute",
                Width = 100,
                Height = 30,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = BrAccent,
                Foreground = BrWhite,
                BorderBrush = BrAccent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnExecute.Click += (_, __) =>
            {
                result.Confirmed = true;
                result.Operation = selectedOp;
                result.SelectedSchedules = scheduleItems.Where(s => s.IsSelected).Select(s => s.Name).ToList();
                result.Options = CollectOptions(selectedOp,
                    chkDiscM, chkDiscE, chkDiscP, chkDiscA, chkDiscS, chkDiscFP,
                    chkOverwrite, chkFormulas,
                    txtExportPath, cmbFormat,
                    cmbManageOp);
                win.DialogResult = true;
            };
            DockPanel.SetDock(btnExecute, Dock.Right);
            btnPanel.Children.Add(btnExecute);

            // Progress area (hidden, reserved for future use)
            var progressText = new TextBlock
            {
                Text = string.Empty,
                FontSize = 11,
                Foreground = BrFgDim,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnPanel.Children.Add(progressText);

            btnRow.Child = btnPanel;
            Grid.SetRow(btnRow, 4);
            root.Children.Add(btnRow);

            win.Content = root;

            // Initialise
            SelectOperation(OpCreateBatch);
            RefreshList(null);

            // Keyboard shortcut
            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    win.DialogResult = false;
            };

            bool? dlgResult = false;
            try
            {
                dlgResult = win.ShowDialog();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScheduleWizardDialog: ShowDialog failed — {ex.Message}");
            }

            if (dlgResult != true)
            {
                result.Confirmed = false;
            }

            return result;
        }

        // ── Schedule item model ─────────────────────────────────────────
        private class ScheduleItem
        {
            public string Name { get; set; } = string.Empty;
            public bool ExistsInProject { get; set; }
            public bool ExistsInCsv { get; set; }
            public bool IsSelected { get; set; }
        }

        private static List<ScheduleItem> BuildScheduleItems(List<string> csvDefs, List<string> existing)
        {
            var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            var csvSet = new HashSet<string>(csvDefs, StringComparer.OrdinalIgnoreCase);
            var allNames = new HashSet<string>(csvDefs, StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing) allNames.Add(e);

            return allNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => new ScheduleItem
                {
                    Name = n,
                    ExistsInProject = existingSet.Contains(n),
                    ExistsInCsv = csvSet.Contains(n),
                    IsSelected = false
                })
                .ToList();
        }

        private static object FormatScheduleItem(ScheduleItem item)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontSize = 12,
                Foreground = BrFg,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (item.ExistsInProject && item.ExistsInCsv)
            {
                sp.Children.Add(MakeTag("CSV + Project", Color.FromRgb(0x4C, 0xAF, 0x50)));
            }
            else if (item.ExistsInProject)
            {
                sp.Children.Add(MakeTag("Project", Color.FromRgb(0x42, 0x9E, 0xE6)));
            }
            else if (item.ExistsInCsv)
            {
                sp.Children.Add(MakeTag("CSV", Color.FromRgb(0xFF, 0x98, 0x00)));
            }

            return sp;
        }

        private static TextBlock MakeTag(string text, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 9,
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
        }

        // ── Options builders ────────────────────────────────────────────

        private static void BuildCreateOptions(StackPanel container,
            ref CheckBox m, ref CheckBox e, ref CheckBox p, ref CheckBox a, ref CheckBox s, ref CheckBox fp)
        {
            container.Children.Add(new TextBlock
            {
                Text = "Discipline filter:",
                FontSize = 11,
                Foreground = BrFg,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            m = MakeFilterCheckBox("M - Mechanical", true);
            e = MakeFilterCheckBox("E - Electrical", true);
            p = MakeFilterCheckBox("P - Plumbing", true);
            a = MakeFilterCheckBox("A - Architectural", true);
            s = MakeFilterCheckBox("S - Structural", true);
            fp = MakeFilterCheckBox("FP - Fire Protection", true);

            wrap.Children.Add(m);
            wrap.Children.Add(e);
            wrap.Children.Add(p);
            wrap.Children.Add(a);
            wrap.Children.Add(s);
            wrap.Children.Add(fp);
            container.Children.Add(wrap);
        }

        private static void BuildAutoPopulateOptions(StackPanel container,
            ref CheckBox overwrite, ref CheckBox formulas)
        {
            overwrite = new CheckBox
            {
                Content = "Overwrite existing values",
                FontSize = 11,
                Foreground = BrFg,
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 4)
            };
            formulas = new CheckBox
            {
                Content = "Include formula evaluation",
                FontSize = 11,
                Foreground = BrFg,
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 4)
            };
            container.Children.Add(overwrite);
            container.Children.Add(formulas);
        }

        private static void BuildExportOptions(StackPanel container,
            ref TextBox pathBox, ref ComboBox formatCombo)
        {
            // Output path
            container.Children.Add(new TextBlock
            {
                Text = "Output path:",
                FontSize = 11,
                Foreground = BrFg,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var browseBtn = new Button
            {
                Content = "Browse...",
                Width = 70,
                Height = 24,
                FontSize = 11,
                Background = BrWhite,
                BorderBrush = BrBorder,
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0)
            };
            DockPanel.SetDock(browseBtn, Dock.Right);
            pathRow.Children.Add(browseBtn);

            pathBox = new TextBox
            {
                FontSize = 11,
                Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = BrBorder,
                BorderThickness = new Thickness(1),
                Background = BrWhite,
                Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            pathRow.Children.Add(pathBox);
            container.Children.Add(pathRow);

            // Capture for closure
            var capturedPathBox = pathBox;
            browseBtn.Click += (_, __) =>
            {
                try
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Export Schedule Data",
                        InitialDirectory = capturedPathBox.Text,
                        Filter = "CSV Files (*.csv)|*.csv|Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                        FileName = "STING_Schedules"
                    };
                    if (dlg.ShowDialog() == true)
                        capturedPathBox.Text = dlg.FileName;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ScheduleWizardDialog: Browse failed — {ex.Message}");
                }
            };

            // Format
            var fmtRow = new StackPanel { Orientation = Orientation.Horizontal };
            fmtRow.Children.Add(new TextBlock
            {
                Text = "Format:",
                FontSize = 11,
                Foreground = BrFg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            formatCombo = new ComboBox
            {
                Width = 100,
                FontSize = 11,
                SelectedIndex = 0
            };
            formatCombo.Items.Add("CSV");
            formatCombo.Items.Add("XLSX");
            fmtRow.Children.Add(formatCombo);
            container.Children.Add(fmtRow);
        }

        private static void BuildManageOptions(StackPanel container, ref ComboBox manageCombo)
        {
            container.Children.Add(new TextBlock
            {
                Text = "Sub-operation:",
                FontSize = 11,
                Foreground = BrFg,
                Margin = new Thickness(0, 0, 0, 4)
            });
            manageCombo = new ComboBox
            {
                Width = 200,
                FontSize = 11,
                SelectedIndex = 0
            };
            manageCombo.Items.Add("Duplicate");
            manageCombo.Items.Add("Delete");
            manageCombo.Items.Add("Refresh");
            manageCombo.Items.Add("Compare");
            container.Children.Add(manageCombo);
        }

        // ── Collect options from controls ────────────────────────────────

        private static Dictionary<string, string> CollectOptions(string operation,
            CheckBox discM, CheckBox discE, CheckBox discP, CheckBox discA, CheckBox discS, CheckBox discFP,
            CheckBox overwrite, CheckBox formulas,
            TextBox exportPath, ComboBox formatCombo,
            ComboBox manageCombo)
        {
            var opts = new Dictionary<string, string>();

            switch (operation)
            {
                case OpCreateBatch:
                    var discs = new List<string>();
                    if (discM?.IsChecked == true) discs.Add("M");
                    if (discE?.IsChecked == true) discs.Add("E");
                    if (discP?.IsChecked == true) discs.Add("P");
                    if (discA?.IsChecked == true) discs.Add("A");
                    if (discS?.IsChecked == true) discs.Add("S");
                    if (discFP?.IsChecked == true) discs.Add("FP");
                    opts["DisciplineFilter"] = string.Join(",", discs);
                    break;

                case OpAutoPopulate:
                    opts["Overwrite"] = (overwrite?.IsChecked == true).ToString();
                    opts["IncludeFormulas"] = (formulas?.IsChecked == true).ToString();
                    break;

                case OpExportCsv:
                    opts["OutputPath"] = exportPath?.Text ?? string.Empty;
                    opts["Format"] = (formatCombo?.SelectedItem as string) ?? "CSV";
                    break;

                case OpManage:
                    opts["SubOperation"] = (manageCombo?.SelectedItem as string) ?? "Duplicate";
                    break;
            }

            return opts;
        }

        // ── Helper controls ─────────────────────────────────────────────

        private static CheckBox MakeFilterCheckBox(string label, bool isChecked)
        {
            return new CheckBox
            {
                Content = label,
                FontSize = 11,
                Foreground = BrFg,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 14, 4)
            };
        }

        private static TextBlock CreateLinkButton(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = BrAccent,
                Cursor = Cursors.Hand,
                TextDecorations = TextDecorations.Underline,
                Margin = new Thickness(0, 0, 4, 0)
            };
            return tb;
        }
    }
}
