// FOLDER-01: Cloud sync settings dialog + manual mirror trigger.
// Exposes CloudRoot / CloudProvider / AutoMirrorOnPublish from ProjectSetup
// through a lightweight WPF dialog, and provides a one-shot manual mirror
// command for forcing a specific file to the configured cloud provider.

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Color = System.Windows.Media.Color;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cloud Sync Settings — opens a small dialog to configure CloudRoot,
    // CloudProvider, and AutoMirrorOnPublish on the active project setup.
    // ─────────────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FolderCloudSyncSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet elements)
        {
            try
            {
                var doc = data.Application.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No active document"; return Result.Failed; }

                var setup = ProjectFolderEngine.LoadOrDetectSetup(doc);
                if (setup == null)
                {
                    TaskDialog.Show("Cloud Sync", "No project setup found. Run Project Setup first.");
                    return Result.Cancelled;
                }

                var dlg = new CloudSyncSettingsDialog(setup);
                bool? ok = dlg.ShowDialog();
                if (ok != true) return Result.Cancelled;

                // Persist via Save
                string dataPath = ProjectFolderEngine.GetDataPath(doc);
                setup.Save(dataPath);
                StingLog.Info($"FolderCloudSyncSettings: saved provider={setup.CloudProvider} autoMirror={setup.AutoMirrorOnPublish}");

                TaskDialog.Show("Cloud Sync", $"Cloud sync settings saved.\nProvider: {setup.CloudProvider}\nAuto-mirror on publish: {setup.AutoMirrorOnPublish}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FolderCloudSyncSettingsCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Manual one-shot mirror — picks a file from the project folder tree and
    // pushes it to the configured cloud provider via TryMirrorToCloud.
    // ─────────────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FolderCloudMirrorNowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet elements)
        {
            try
            {
                var doc = data.Application.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No active document"; return Result.Failed; }

                var setup = ProjectFolderEngine.LoadOrDetectSetup(doc);
                if (setup == null || string.IsNullOrEmpty(setup.CloudProvider))
                {
                    TaskDialog.Show("Cloud Mirror", "No cloud provider configured. Open Cloud Sync Settings first.");
                    return Result.Cancelled;
                }

                string rootPath = ProjectFolderEngine.GetRootPath(doc);
                if (!Directory.Exists(rootPath))
                {
                    TaskDialog.Show("Cloud Mirror", $"Project folder not found:\n{rootPath}");
                    return Result.Cancelled;
                }

                // Use OpenFileDialog via System.Windows.Forms (already in Revit runtime)
                var ofd = new System.Windows.Forms.OpenFileDialog
                {
                    Title = "Select file to mirror to cloud",
                    InitialDirectory = rootPath,
                    Filter = "All files (*.*)|*.*",
                    Multiselect = false,
                };
                if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;

                string filePath = ofd.FileName;

                // Ask for target CDE state
                var stateDlg = new TaskDialog("Mirror CDE State")
                {
                    MainInstruction = "Which CDE state does this file represent?",
                    MainContent = $"File: {Path.GetFileName(filePath)}",
                };
                stateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "SHARED", "Mirror to the Shared area");
                stateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "PUBLISHED", "Mirror to the Published area");
                stateDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = stateDlg.Show();
                string cdeState = result == TaskDialogResult.CommandLink1 ? "SHARED"
                                : result == TaskDialogResult.CommandLink2 ? "PUBLISHED"
                                : null;
                if (cdeState == null) return Result.Cancelled;

                ProjectFolderEngine.TryMirrorToCloud(doc, filePath, cdeState);
                TaskDialog.Show("Cloud Mirror", $"Mirror operation complete for:\n{Path.GetFileName(filePath)}\n\nCheck StingTools.log for details.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FolderCloudMirrorNowCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WPF dialog for cloud sync settings
    // ─────────────────────────────────────────────────────────────────────────
    internal class CloudSyncSettingsDialog : Window
    {
        private readonly ProjectSetup _setup;
        private ComboBox _cbProvider;
        private TextBox _tbCloudRoot;
        private CheckBox _chkAutoMirror;

        internal CloudSyncSettingsDialog(ProjectSetup setup)
        {
            _setup = setup;
            Title = "Cloud Sync Settings";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Foreground = Brushes.White;

            Build();
        }

        private void Build()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Title
            var title = new TextBlock
            {
                Text = "Cloud Sync Settings",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = Brushes.White,
            };
            Grid.SetColumnSpan(title, 2);
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Provider row
            AddLabel(grid, "Cloud Provider:", 1);
            _cbProvider = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            };
            _cbProvider.Items.Add("");
            _cbProvider.Items.Add("ACC");
            _cbProvider.Items.Add("SharePoint");
            _cbProvider.Items.Add("Dropbox");
            _cbProvider.Items.Add("OneDrive");
            _cbProvider.SelectedItem = _setup.CloudProvider ?? "";
            _cbProvider.SelectionChanged += (s, e) => UpdateBrowseVisibility();
            Grid.SetRow(_cbProvider, 1);
            Grid.SetColumn(_cbProvider, 1);
            grid.Children.Add(_cbProvider);

            // Cloud root row
            AddLabel(grid, "Cloud Root / Folder ID:", 2);
            var rootPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            _tbCloudRoot = new TextBox
            {
                Text = _setup.CloudRoot ?? "",
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2),
            };
            var btnBrowse = new Button
            {
                Content = "…",
                Width = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                ToolTip = "Browse for local Dropbox / OneDrive sync folder",
            };
            btnBrowse.Click += BrowseRoot;
            DockPanel.SetDock(btnBrowse, Dock.Right);
            rootPanel.Children.Add(btnBrowse);
            rootPanel.Children.Add(_tbCloudRoot);
            Grid.SetRow(rootPanel, 2);
            Grid.SetColumn(rootPanel, 1);
            grid.Children.Add(rootPanel);

            // Auto-mirror checkbox
            _chkAutoMirror = new CheckBox
            {
                Content = "Auto-mirror files on SHARED / PUBLISHED transition",
                IsChecked = _setup.AutoMirrorOnPublish,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 8, 0, 4),
            };
            Grid.SetColumnSpan(_chkAutoMirror, 2);
            Grid.SetRow(_chkAutoMirror, 3);
            grid.Children.Add(_chkAutoMirror);

            // Info label
            var info = new TextBlock
            {
                Text = "• ACC: enter the ACC hub / project URL or leave blank to stage locally.\n" +
                       "• SharePoint: enter the site URL; staged files are queued for upload.\n" +
                       "• Dropbox / OneDrive: enter the local sync folder path (e.g. C:\\Users\\You\\Dropbox\\ProjectX).",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 12),
            };
            Grid.SetColumnSpan(info, 2);
            Grid.SetRow(info, 4);
            grid.Children.Add(info);

            // Buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var btnSave = new Button { Content = "Save", Width = 80, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)), Foreground = Brushes.White, BorderBrush = Brushes.Transparent };
            var btnCancel = new Button { Content = "Cancel", Width = 80, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)) };
            btnSave.Click += (s, e) =>
            {
                _setup.CloudProvider = (_cbProvider.SelectedItem as string) ?? "";
                _setup.CloudRoot = _tbCloudRoot.Text.Trim();
                _setup.AutoMirrorOnPublish = _chkAutoMirror.IsChecked == true;
                DialogResult = true;
                Close();
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnSave);
            btnPanel.Children.Add(btnCancel);
            Grid.SetColumnSpan(btnPanel, 2);
            Grid.SetRow(btnPanel, 5);
            grid.Children.Add(btnPanel);

            Content = grid;
            UpdateBrowseVisibility();
        }

        private void AddLabel(Grid grid, string text, int row)
        {
            var lbl = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4),
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);
        }

        private void BrowseRoot(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select cloud sync folder (Dropbox / OneDrive root for this project)",
                ShowNewFolderButton = false,
            };
            if (!string.IsNullOrEmpty(_tbCloudRoot.Text) && Directory.Exists(_tbCloudRoot.Text))
                fbd.SelectedPath = _tbCloudRoot.Text;
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                _tbCloudRoot.Text = fbd.SelectedPath;
        }

        private void UpdateBrowseVisibility()
        {
            // Browse button only makes sense for local sync providers
            string p = (_cbProvider.SelectedItem as string) ?? "";
            bool isFolderProvider = p == "Dropbox" || p == "OneDrive";
            // (button is always visible but label changes based on context)
            _tbCloudRoot.ToolTip = isFolderProvider
                ? "Local Dropbox / OneDrive sync folder path"
                : "Cloud hub URL or project ID";
        }
    }
}
