using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using Color = System.Windows.Media.Color;

namespace StingTools.UI
{
    /// <summary>
    /// Multi-page WPF wizard for raising ISO 19650 issues/RFIs.
    /// Replaces the 4-step TaskDialog chain with a rich interactive dialog.
    /// </summary>
    public static class IssueWizard
    {
        public static IssueWizardResult Show(Document doc, UIDocument uidoc)
        {
            var wizard = new StingWizardDialog("STING Issue Tracker", 780, 580);
            wizard.AddPage(new IssueTypePage());
            wizard.AddPage(new IssuePriorityPage());
            wizard.AddPage(new IssueDetailsPage(doc, uidoc));
            wizard.AddPage(new IssueReviewPage(doc, uidoc));

            bool? result = wizard.ShowDialog();
            if (result != true || !wizard.IsCompleted) return null;

            return BuildResult(wizard.Results);
        }

        private static IssueWizardResult BuildResult(Dictionary<string, object> r)
        {
            var res = new IssueWizardResult();
            if (r.TryGetValue("IssueType", out var it)) res.IssueType = it as string ?? "RFI";
            if (r.TryGetValue("Priority", out var p)) res.Priority = p as string ?? "MEDIUM";
            if (r.TryGetValue("AssignedTo", out var a)) res.AssignedTo = a as string ?? "";
            if (r.TryGetValue("Title", out var t)) res.Title = t as string ?? "";
            if (r.TryGetValue("Description", out var d)) res.Description = d as string ?? "";
            if (r.TryGetValue("Discipline", out var disc)) res.Discipline = disc as string ?? "Z";
            if (r.TryGetValue("DueDate", out var dd)) res.DueDate = dd as string ?? "";
            if (r.TryGetValue("Location", out var loc)) res.Location = loc as string ?? "";
            return res;
        }

        // ════════════════════════════════════════════════════════════
        //  Page 1: Issue Type
        // ════════════════════════════════════════════════════════════
        private class IssueTypePage : WizardPage
        {
            private readonly Dictionary<string, RadioButton> _typeRadios = new();

            public IssueTypePage()
            {
                Title = "Type";
                Description = "Select the type of issue to raise.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Issue Type"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Choose the category that best describes this issue. This determines tracking workflow and notification routing."));

                var types = new (string key, string label, string desc, string icon)[]
                {
                    ("RFI", "RFI — Request for Information", "Query requiring clarification from design team", "?"),
                    ("RFA", "RFA — Request for Approval", "Submittal or proposal requiring formal approval", "V"),
                    ("TQ", "TQ — Technical Query", "Technical question requiring specialist response", "T"),
                    ("CLASH", "CLASH — Coordination Clash", "Spatial conflict between building elements/disciplines", "X"),
                    ("DESIGN", "DESIGN — Design Issue/Query", "Design intent question or proposed change", "D"),
                    ("SI", "SI — Site Instruction", "Formal instruction issued to contractor on site", "I"),
                    ("NCR", "NCR — Non-Conformance Report", "Element/work not meeting specification requirements", "!"),
                    ("SNAGGING", "SNAGGING — Snagging/Defect", "Construction defect or incomplete work item", "S"),
                    ("CHANGE", "CHANGE — Change Request", "Formal request to modify design or scope", "C"),
                    ("VO", "VO — Variation Order", "Authorised change to contract scope, cost, or programme", "$"),
                    ("AI", "AI — Architect's Instruction", "Formal instruction from architect to contractor", "A"),
                    ("CVI", "CVI — Confirmation of Verbal Instruction", "Written confirmation of verbal instruction given on site", "W"),
                    ("EWN", "EWN — Early Warning Notice (NEC)", "NEC early warning of event that could affect cost/programme", "E"),
                    ("CE", "CE — Compensation Event (NEC)", "NEC event entitling contractor to time/cost compensation", "£"),
                    ("PMI", "PMI — Proposed Material Instruction", "Proposed material or product substitution for approval", "M"),
                    ("RISK", "RISK — Risk Item", "Identified risk requiring mitigation strategy", "R"),
                    ("SITE", "SITE — Site Observation", "On-site observation requiring documentation", "O"),
                    ("ACTION", "ACTION — Action Item", "Task or action requiring follow-up", "+"),
                    ("COMMENT", "COMMENT — General Comment", "General comment or note for record", "N")
                };

                bool first = true;
                foreach (var (key, label, desc, icon) in types)
                {
                    var border = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 2, 0, 2),
                        Padding = new Thickness(10, 6, 10, 6),
                        Background = Brushes.White
                    };

                    var row = new DockPanel();
                    var iconBlock = new TextBlock
                    {
                        Text = icon, FontSize = 16, FontWeight = FontWeights.Bold,
                        Width = 28, TextAlignment = TextAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(88, 44, 131)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    DockPanel.SetDock(iconBlock, Dock.Left);
                    row.Children.Add(iconBlock);

                    var rb = new RadioButton
                    {
                        GroupName = "IssueType",
                        IsChecked = first,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _typeRadios[key] = rb;
                    DockPanel.SetDock(rb, Dock.Left);
                    row.Children.Add(rb);

                    var textPanel = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 60))
                    });
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = desc, FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120))
                    });
                    row.Children.Add(textPanel);

                    border.Child = row;
                    border.MouseLeftButtonUp += (s, e) => { rb.IsChecked = true; };
                    panel.Children.Add(border);
                    first = false;
                }

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string selected = _typeRadios.FirstOrDefault(kv => kv.Value.IsChecked == true).Key ?? "RFI";
                results["IssueType"] = selected;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 2: Priority & Assignment
        // ════════════════════════════════════════════════════════════
        private class IssuePriorityPage : WizardPage
        {
            private readonly Dictionary<string, RadioButton> _priorityRadios = new();
            private System.Windows.Controls.ComboBox _assignCombo;
            private System.Windows.Controls.TextBox _customAssignee;

            public IssuePriorityPage()
            {
                Title = "Priority";
                Description = "Set priority level and assign responsibility.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Priority Level"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Priority determines response time and escalation path per ISO 19650 information management."));

                var priorities = new (string key, string label, string desc, Color color)[]
                {
                    ("CRITICAL", "CRITICAL", "Blocks progress — immediate action required", Color.FromRgb(211, 47, 47)),
                    ("HIGH", "HIGH", "Significant impact — action within 24 hours", Color.FromRgb(245, 124, 0)),
                    ("MEDIUM", "MEDIUM", "Moderate impact — action within 1 week", Color.FromRgb(255, 193, 7)),
                    ("LOW", "LOW", "Minor impact — action at convenience", Color.FromRgb(76, 175, 80))
                };

                foreach (var (key, label, desc, color) in priorities)
                {
                    var border = new Border
                    {
                        BorderBrush = new SolidColorBrush(color),
                        BorderThickness = new Thickness(2, 2, 2, 2),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 3, 0, 3),
                        Padding = new Thickness(12, 8, 12, 8),
                        Background = Brushes.White
                    };

                    var row = new DockPanel();
                    var colorDot = new Border
                    {
                        Width = 14, Height = 14,
                        CornerRadius = new CornerRadius(7),
                        Background = new SolidColorBrush(color),
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    DockPanel.SetDock(colorDot, Dock.Left);
                    row.Children.Add(colorDot);

                    var rb = new RadioButton
                    {
                        GroupName = "Priority",
                        IsChecked = key == "MEDIUM",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _priorityRadios[key] = rb;
                    DockPanel.SetDock(rb, Dock.Left);
                    row.Children.Add(rb);

                    var textPanel = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = label, FontSize = 13, FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(color)
                    });
                    textPanel.Children.Add(new TextBlock
                    {
                        Text = desc, FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120))
                    });
                    row.Children.Add(textPanel);

                    border.Child = row;
                    border.MouseLeftButtonUp += (s, e) => { rb.IsChecked = true; };
                    panel.Children.Add(border);
                }

                // Assignment
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Assign To"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Select who is responsible for resolving this issue."));

                var assignees = new[]
                {
                    Environment.UserName + " (Self)",
                    "BIM Coordinator",
                    "Design Lead",
                    "Project Manager",
                    "Contractor",
                    "Specialist Consultant",
                    "Unassigned",
                    "Custom..."
                };
                var assignPanel = StingWizardDialog.MakeLabelledCombo("Assignee:", assignees, 0, out _assignCombo);
                panel.Children.Add(assignPanel);

                var customPanel = StingWizardDialog.MakeLabelledText("Custom Assignee:", "", out _customAssignee);
                _customAssignee.IsEnabled = false;
                _assignCombo.SelectionChanged += (s, e) =>
                {
                    _customAssignee.IsEnabled = _assignCombo.SelectedItem?.ToString()?.Contains("Custom") == true;
                };
                panel.Children.Add(customPanel);

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string priority = _priorityRadios.FirstOrDefault(kv => kv.Value.IsChecked == true).Key ?? "MEDIUM";
                results["Priority"] = priority;

                string assignee = _assignCombo?.SelectedItem?.ToString() ?? "";
                if (assignee.Contains("Custom") && !string.IsNullOrWhiteSpace(_customAssignee?.Text))
                    assignee = _customAssignee.Text;
                else if (assignee.Contains("(Self)"))
                    assignee = Environment.UserName;
                else if (assignee == "Unassigned")
                    assignee = "";
                results["AssignedTo"] = assignee;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 3: Issue Details
        // ════════════════════════════════════════════════════════════
        private class IssueDetailsPage : WizardPage
        {
            private readonly Document _doc;
            private readonly UIDocument _uidoc;
            private System.Windows.Controls.TextBox _titleBox;
            private System.Windows.Controls.TextBox _descBox;
            private System.Windows.Controls.ComboBox _discCombo;
            private System.Windows.Controls.TextBox _locationBox;
            private System.Windows.Controls.TextBox _dueDateBox;

            public IssueDetailsPage(Document doc, UIDocument uidoc)
            {
                _doc = doc;
                _uidoc = uidoc;
                Title = "Details";
                Description = "Provide issue title, description, and context.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Issue Title"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Title is auto-generated from selected elements but can be edited."));

                // Auto-generate title from selection
                string autoTitle = GenerateAutoTitle();
                var titlePanel = StingWizardDialog.MakeLabelledText("Title:", autoTitle, out _titleBox);
                panel.Children.Add(titlePanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Description"));
                var descPanel = StingWizardDialog.MakeLabelledText("Description:", GenerateAutoDescription(), out _descBox);
                _descBox.AcceptsReturn = true;
                _descBox.TextWrapping = TextWrapping.Wrap;
                _descBox.Height = 80;
                _descBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                panel.Children.Add(descPanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Context"));

                var disciplines = new[] { "M — Mechanical", "E — Electrical", "P — Plumbing",
                    "A — Architectural", "S — Structural", "FP — Fire Protection",
                    "LV — Low Voltage", "G — General", "Z — Unspecified" };
                string detectedDisc = DetectDiscipline();
                int discIdx = Array.FindIndex(disciplines, d => d.StartsWith(detectedDisc));
                var discPanel = StingWizardDialog.MakeLabelledCombo("Discipline:", disciplines,
                    discIdx >= 0 ? discIdx : 8, out _discCombo);
                panel.Children.Add(discPanel);

                var locationPanel = StingWizardDialog.MakeLabelledText("Location/Level:", DetectLocation(), out _locationBox);
                panel.Children.Add(locationPanel);

                string defaultDue = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd");
                var duePanel = StingWizardDialog.MakeLabelledText("Due Date (yyyy-MM-dd):", defaultDue, out _dueDateBox);
                panel.Children.Add(duePanel);

                // Element context
                var selectedIds = _uidoc?.Selection?.GetElementIds();
                if (selectedIds != null && selectedIds.Count > 0)
                {
                    panel.Children.Add(StingWizardDialog.MakeSectionHeader("Linked Elements"));
                    var elemInfo = new TextBlock
                    {
                        Text = $"{selectedIds.Count} element(s) selected and will be linked to this issue.",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    panel.Children.Add(elemInfo);
                }

                Content = panel;
            }

            private string GenerateAutoTitle()
            {
                try
                {
                    var selectedIds = _uidoc?.Selection?.GetElementIds();
                    string issueType = Wizard?.Results?.ContainsKey("IssueType") == true
                        ? Wizard.Results["IssueType"] as string ?? "RFI" : "RFI";

                    if (selectedIds != null && selectedIds.Count > 0)
                    {
                        var firstEl = _doc.GetElement(selectedIds.First());
                        if (firstEl == null) return $"{issueType}: (element not found)";
                        string cat = ParameterHelpers.GetCategoryName(firstEl);
                        string tag = ParameterHelpers.GetString(firstEl, ParamRegistry.TAG1);
                        string title = $"{issueType}: {cat}";
                        if (!string.IsNullOrEmpty(tag)) title += $" [{tag}]";
                        if (selectedIds.Count > 1) title += $" (+{selectedIds.Count - 1} more)";
                        return title;
                    }
                    return $"{issueType}: {_uidoc?.ActiveView?.Name ?? "General"}";
                }
                catch (Exception ex) { StingLog.Warn($"Issue auto-title: {ex.Message}"); }
                return "New Issue";
            }

            private string GenerateAutoDescription()
            {
                try
                {
                    var selectedIds = _uidoc?.Selection?.GetElementIds();
                    if (selectedIds != null && selectedIds.Count > 0)
                    {
                        var firstEl = _doc.GetElement(selectedIds.First());
                        if (firstEl == null) return "Issue raised against (element not found)";
                        string cat = ParameterHelpers.GetCategoryName(firstEl);
                        string lvl = ParameterHelpers.GetString(firstEl, ParamRegistry.LVL);
                        string desc = $"Issue raised against {cat} on {(string.IsNullOrEmpty(lvl) ? "unknown level" : lvl)}";
                        if (selectedIds.Count > 1) desc += $" and {selectedIds.Count - 1} other element(s)";
                        return desc;
                    }
                    return $"Issue observed in view '{_uidoc?.ActiveView?.Name ?? "unknown"}'";
                }
                catch (Exception ex) { StingLog.Warn($"Issue auto-desc: {ex.Message}"); }
                return "";
            }

            private string DetectDiscipline()
            {
                try
                {
                    var selectedIds = _uidoc?.Selection?.GetElementIds();
                    if (selectedIds != null && selectedIds.Count > 0)
                    {
                        var firstEl = _doc.GetElement(selectedIds.First());
                        if (firstEl == null) return "Z";
                        string disc = ParameterHelpers.GetString(firstEl, ParamRegistry.DISC);
                        if (!string.IsNullOrEmpty(disc)) return disc;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Issue disc detect: {ex.Message}"); }
                return "Z";
            }

            private string DetectLocation()
            {
                try
                {
                    var selectedIds = _uidoc?.Selection?.GetElementIds();
                    if (selectedIds != null && selectedIds.Count > 0)
                    {
                        var firstEl = _doc.GetElement(selectedIds.First());
                        if (firstEl == null) return "";
                        string lvl = ParameterHelpers.GetString(firstEl, ParamRegistry.LVL);
                        string loc = ParameterHelpers.GetString(firstEl, ParamRegistry.LOC);
                        if (!string.IsNullOrEmpty(lvl) || !string.IsNullOrEmpty(loc))
                            return $"{loc} {lvl}".Trim();
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Issue location detect: {ex.Message}"); }
                return "";
            }

            public override string Validate()
            {
                if (string.IsNullOrWhiteSpace(_titleBox?.Text))
                    return "Please enter an issue title.";
                return null;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                results["Title"] = _titleBox?.Text ?? "";
                results["Description"] = _descBox?.Text ?? "";
                string discText = _discCombo?.SelectedItem?.ToString() ?? "Z";
                results["Discipline"] = discText.Split(new[] { ' ' }, 2)[0].Trim();
                results["Location"] = _locationBox?.Text ?? "";
                results["DueDate"] = _dueDateBox?.Text ?? "";
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 4: Review & Confirm
        // ════════════════════════════════════════════════════════════
        private class IssueReviewPage : WizardPage
        {
            private readonly Document _doc;
            private readonly UIDocument _uidoc;
            private TextBlock _summaryBlock;

            public IssueReviewPage(Document doc, UIDocument uidoc)
            {
                _doc = doc;
                _uidoc = uidoc;
                Title = "Review";
                Description = "Review issue details before submission.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Issue Summary"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Review the details below. Click 'Finish' to raise the issue."));

                _summaryBlock = new TextBlock
                {
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 60)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = Brushes.White,
                    Padding = new Thickness(12)
                };

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Child = _summaryBlock
                };
                panel.Children.Add(border);

                Content = panel;
            }

            public override void OnNavigatedTo()
            {
                if (_summaryBlock == null || Wizard?.Results == null) return;

                var r = Wizard.Results;
                var sb = new StringBuilder();
                sb.AppendLine($"  Type:        {GetVal(r, "IssueType")}");
                sb.AppendLine($"  Priority:    {GetVal(r, "Priority")}");
                sb.AppendLine($"  Title:       {GetVal(r, "Title")}");
                sb.AppendLine($"  Assigned To: {GetVal(r, "AssignedTo", "Unassigned")}");
                sb.AppendLine($"  Discipline:  {GetVal(r, "Discipline")}");
                sb.AppendLine($"  Location:    {GetVal(r, "Location", "N/A")}");
                sb.AppendLine($"  Due Date:    {GetVal(r, "DueDate")}");
                sb.AppendLine();
                sb.AppendLine($"  Description:");
                sb.AppendLine($"    {GetVal(r, "Description")}");
                sb.AppendLine();

                var selectedIds = _uidoc?.Selection?.GetElementIds();
                sb.AppendLine($"  Linked Elements: {selectedIds?.Count ?? 0}");
                sb.AppendLine($"  Raised By:       {Environment.UserName}");
                sb.AppendLine($"  View:            {_uidoc?.ActiveView?.Name ?? "N/A"}");

                _summaryBlock.Text = sb.ToString();
            }

            private static string GetVal(Dictionary<string, object> r, string key, string fallback = "")
            {
                if (r.TryGetValue(key, out var val) && val != null)
                {
                    string s = val.ToString();
                    return string.IsNullOrEmpty(s) ? fallback : s;
                }
                return fallback;
            }
        }
    }

    /// <summary>Result from the issue wizard.</summary>
    public class IssueWizardResult
    {
        public string IssueType { get; set; } = "RFI";
        public string Priority { get; set; } = "MEDIUM";
        public string AssignedTo { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Discipline { get; set; } = "Z";
        public string DueDate { get; set; } = "";
        public string Location { get; set; } = "";
    }
}
