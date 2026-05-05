#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

// Disambiguate WPF controls from the Revit UI / DB shims of the same
// name. 'TextBox' / 'ComboBox' exist in both System.Windows.Controls
// and Autodesk.Revit.UI; 'Color' exists in both System.Windows.Media
// and Autodesk.Revit.DB. We only ever want the WPF/Media variants here.
using TextBox  = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color    = System.Windows.Media.Color;

namespace StingTools.BIMManager
{
    /// <summary>
    /// Inline 'mint a new Planscape project' helper. Opens a small modal WPF
    /// prompt for Name + Code + Phase, calls
    /// <see cref="PlanscapeServerClient.CreateProjectAsync"/>, and confirms
    /// the new project id. Used by the BCC PLATFORM tab so coordinators can
    /// create a project without leaving Revit when the tenant has none yet.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlanscapeCreateProjectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var client = PlanscapeServerClient.Instance;
            if (string.IsNullOrEmpty(client.ConnectedUser))
            {
                TaskDialog.Show(
                    "Create Planscape Project",
                    "You're not signed in to Planscape. Use 'Connect' on the BCC PLATFORM tab first.");
                return Result.Cancelled;
            }

            // Prefill Name + Code from the active document if present.
            var doc = commandData.Application.ActiveUIDocument?.Document;
            string defaultName = doc?.Title ?? "";
            string defaultCode = SuggestCode(defaultName);

            var dlg = new CreateProjectInputDialog(defaultName, defaultCode);
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            var (ok, id, error) = Task.Run(() => client.CreateProjectAsync(
                dlg.ProjectName, dlg.ProjectCode, dlg.ProjectPhase, dlg.ProjectDescription))
                .GetAwaiter().GetResult();

            if (!ok)
            {
                TaskDialog.Show("Create Planscape Project", $"Could not create the project:\n\n{error}");
                StingLog.Warn($"Planscape: create project failed — {error}");
                return Result.Failed;
            }

            TaskDialog.Show(
                "Create Planscape Project",
                $"Project created.\n\n" +
                $"Name: {dlg.ProjectName}\n" +
                $"Code: {dlg.ProjectCode}\n" +
                $"Id:   {id}\n\n" +
                "It will appear in the BCC project picker the next time the list is fetched.");
            StingLog.Info($"Planscape: created project {id} ({dlg.ProjectName} / {dlg.ProjectCode})");
            return Result.Succeeded;
        }

        /// <summary>Suggest a 6-char project code from the document title.</summary>
        private static string SuggestCode(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var ch in title.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                if (sb.Length >= 6) break;
            }
            return sb.ToString();
        }
    }

    /// <summary>Tiny modal WPF input dialog: Name / Code / Phase / Description.</summary>
    internal class CreateProjectInputDialog : Window
    {
        public string ProjectName { get; private set; } = "";
        public string ProjectCode { get; private set; } = "";
        public string ProjectPhase { get; private set; } = "Design";
        public string ProjectDescription { get; private set; } = "";

        private readonly TextBox _nameBox;
        private readonly TextBox _codeBox;
        private readonly ComboBox _phaseBox;
        private readonly TextBox _descBox;

        public CreateProjectInputDialog(string defaultName, string defaultCode)
        {
            Title = "Create Planscape Project";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFB));

            // Anchor to Revit / BCC z-order if available.
            try { StingTools.UI.StingWindowHelper.ApplyOwner(this); } catch { /* non-fatal */ }

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = "Create a new Planscape project",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x7E)),
                Margin = new Thickness(0, 0, 0, 4),
            });
            root.Children.Add(new TextBlock
            {
                Text = "Project Code is used in ISO 19650 file naming. It must be unique within your tenant.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            _nameBox = AddField(root, "Name *", defaultName);
            _codeBox = AddField(root, "Code *", defaultCode);
            _phaseBox = AddPhase(root);
            _descBox = AddField(root, "Description", "");
            _descBox.AcceptsReturn = true;
            _descBox.Height = 60;
            _descBox.TextWrapping = TextWrapping.Wrap;
            _descBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            // Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var ok = new Button
            {
                Content = "Create",
                Width = 110,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                IsDefault = true,
            };
            ok.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text) || string.IsNullOrWhiteSpace(_codeBox.Text))
                {
                    MessageBox.Show("Name and Code are required.", "Create Planscape Project",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ProjectName = _nameBox.Text.Trim();
                ProjectCode = _codeBox.Text.Trim();
                ProjectPhase = (_phaseBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Design";
                ProjectDescription = _descBox.Text.Trim();
                DialogResult = true;
                Close();
            };
            btnRow.Children.Add(cancel);
            btnRow.Children.Add(ok);
            root.Children.Add(btnRow);

            Content = root;
        }

        private static TextBox AddField(StackPanel parent, string label, string value)
        {
            parent.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 2),
            });
            var tb = new TextBox
            {
                Text = value,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
            };
            parent.Children.Add(tb);
            return tb;
        }

        private static ComboBox AddPhase(StackPanel parent)
        {
            parent.Children.Add(new TextBlock
            {
                Text = "Phase",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 2),
            });
            var cb = new ComboBox { FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
            foreach (var p in new[] { "Design", "Construction", "Handover", "Operation" })
                cb.Items.Add(new ComboBoxItem { Content = p });
            cb.SelectedIndex = 0;
            parent.Children.Add(cb);
            return cb;
        }
    }
}
