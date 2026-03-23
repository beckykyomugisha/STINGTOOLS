using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Multi-page WPF wizard for smart tag placement configuration.
    /// Replaces the 2-step TaskDialog workflow with rich visual controls.
    /// </summary>
    public static class SmartPlacementWizard
    {
        public static SmartPlacementSettings Show()
        {
            var wizard = new StingWizardDialog("Smart Tag Placement", 780, 560);
            wizard.AddPage(new ScopePage());
            wizard.AddPage(new PlacementOptionsPage());
            wizard.AddPage(new CollisionPage());

            bool? result = wizard.ShowDialog();
            if (result != true || !wizard.IsCompleted) return null;

            return BuildSettings(wizard.Results);
        }

        private static SmartPlacementSettings BuildSettings(Dictionary<string, object> r)
        {
            var s = new SmartPlacementSettings();
            if (r.TryGetValue("Scope", out var sc)) s.Scope = sc as string ?? "Untagged";
            if (r.TryGetValue("LeaderMode", out var lm)) s.LeaderMode = lm as string ?? "Auto";
            if (r.TryGetValue("PreferredPosition", out var pp)) s.PreferredPosition = pp as string ?? "Above";
            if (r.TryGetValue("OverlapPenalty", out var op) && op is double opd) s.OverlapPenalty = opd;
            if (r.TryGetValue("ProximityBonus", out var pb) && pb is double pbd) s.ProximityBonus = pbd;
            if (r.TryGetValue("AlignBonus", out var ab) && ab is double abd) s.AlignBonus = abd;
            if (r.TryGetValue("MaxSearchRadius", out var msr) && msr is double msrd) s.MaxSearchRadius = msrd;
            if (r.TryGetValue("ElbowStyle", out var es)) s.ElbowStyle = es as string ?? "90";
            if (r.TryGetValue("TextSize", out var ts) && ts is double tsd) s.TextSize = tsd;
            if (r.TryGetValue("BatchViews", out var bv)) s.BatchViews = bv is true;
            return s;
        }

        // ════════════════════════════════════════════════════════════
        //  Page 1: Scope & Target
        // ════════════════════════════════════════════════════════════
        private class ScopePage : WizardPage
        {
            private readonly Dictionary<string, RadioButton> _scopeRadios = new();
            private CheckBox _batchViews;

            public ScopePage()
            {
                Title = "Scope";
                Description = "Select which elements to place tags on.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Tag Placement Scope"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Choose which elements should receive visual annotation tags in the active view."));

                var scopes = new (string key, string label, string desc)[]
                {
                    ("Untagged", "Tag untagged elements only", "Place tags only on elements that don't already have visual annotations"),
                    ("All", "Tag all taggable elements", "Place tags on all elements, replacing existing tag positions"),
                    ("Selected", "Tag selected elements only", "Place tags only on currently selected elements")
                };

                bool first = true;
                foreach (var (key, label, desc) in scopes)
                {
                    var border = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 3, 0, 3),
                        Padding = new Thickness(12, 8, 12, 8),
                        Background = Brushes.White
                    };

                    var row = new DockPanel();
                    var rb = new RadioButton { GroupName = "Scope", IsChecked = first, VerticalAlignment = VerticalAlignment.Center };
                    _scopeRadios[key] = rb;
                    DockPanel.SetDock(rb, Dock.Left);
                    row.Children.Add(rb);

                    var textPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
                    textPanel.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold });
                    textPanel.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)) });
                    row.Children.Add(textPanel);

                    border.Child = row;
                    border.MouseLeftButtonUp += (s, e) => { rb.IsChecked = true; };
                    panel.Children.Add(border);
                    first = false;
                }

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Multi-View"));
                _batchViews = StingWizardDialog.MakeLabelledCheck("Apply to all views in project (batch mode)", false);
                panel.Children.Add(_batchViews);

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string scope = _scopeRadios.FirstOrDefault(kv => kv.Value.IsChecked == true).Key ?? "Untagged";
                results["Scope"] = scope;
                results["BatchViews"] = _batchViews?.IsChecked == true;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 2: Placement Options
        // ════════════════════════════════════════════════════════════
        private class PlacementOptionsPage : WizardPage
        {
            private ComboBox _leaderCombo;
            private ComboBox _positionCombo;
            private ComboBox _elbowCombo;
            private Slider _textSizeSlider;
            private TextBlock _textSizeLabel;

            public PlacementOptionsPage()
            {
                Title = "Options";
                Description = "Configure tag appearance and leader settings.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Leader Lines"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Control when leader lines are added between tags and their host elements."));

                var leaderModes = new[] { "Auto — add leaders when displaced", "Always — all tags get leaders",
                    "Never — no leader lines", "Smart — leaders only on large offsets (>2x tag width)" };
                var leaderPanel = StingWizardDialog.MakeLabelledCombo("Leader Mode:", leaderModes, 0, out _leaderCombo);
                panel.Children.Add(leaderPanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Preferred Position"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Set the preferred initial position for tag placement relative to each element."));

                var positions = new[] { "Above", "Right", "Below", "Left", "Above-Right", "Below-Right", "Below-Left", "Above-Left" };
                var posPanel = StingWizardDialog.MakeLabelledCombo("Position:", positions, 0, out _positionCombo);
                panel.Children.Add(posPanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Leader Elbow Style"));
                var elbows = new[] { "90° — orthogonal elbows", "45° — diagonal elbows", "Straight — no elbows", "Free — unconstrained" };
                var elbowPanel = StingWizardDialog.MakeLabelledCombo("Elbow:", elbows, 0, out _elbowCombo);
                panel.Children.Add(elbowPanel);

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Text Size"));
                var sizePanel = StingWizardDialog.MakeLabelledSlider("Tag Text Size (mm):", 1.5, 5, 2.5,
                    out _textSizeSlider, out _textSizeLabel);
                _textSizeSlider.TickFrequency = 0.5;
                panel.Children.Add(sizePanel);

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                string leader = _leaderCombo?.SelectedItem?.ToString() ?? "";
                results["LeaderMode"] = leader.Split(new[] { ' ' }, 2)[0].Trim();
                string pos = _positionCombo?.SelectedItem?.ToString() ?? "Above";
                results["PreferredPosition"] = pos;
                string elbow = _elbowCombo?.SelectedItem?.ToString() ?? "";
                results["ElbowStyle"] = elbow.Contains("45") ? "45" : elbow.Contains("Straight") ? "Straight" : elbow.Contains("Free") ? "Free" : "90";
                results["TextSize"] = _textSizeSlider?.Value ?? 2.5;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Page 3: Collision Avoidance Weights
        // ════════════════════════════════════════════════════════════
        private class CollisionPage : WizardPage
        {
            private Slider _overlapSlider;
            private Slider _proximitySlider;
            private Slider _alignSlider;
            private Slider _searchSlider;
            private TextBlock _overlapLabel;
            private TextBlock _proximityLabel;
            private TextBlock _alignLabel;
            private TextBlock _searchLabel;

            public CollisionPage()
            {
                Title = "Collision";
                Description = "Tune collision avoidance algorithm weights.";
            }

            public override void BuildContent()
            {
                var panel = new StackPanel { Margin = new Thickness(4) };

                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Collision Avoidance Weights"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "Adjust weights that control how the placement algorithm scores candidate positions. " +
                    "Higher values increase the influence of each factor."));

                var overlapPanel = StingWizardDialog.MakeLabelledSlider(
                    "Overlap Penalty (avoids tag-on-tag):", 0, 100, 80,
                    out _overlapSlider, out _overlapLabel);
                panel.Children.Add(overlapPanel);

                var proxPanel = StingWizardDialog.MakeLabelledSlider(
                    "Proximity Bonus (closer to element is better):", 0, 100, 60,
                    out _proximitySlider, out _proximityLabel);
                panel.Children.Add(proxPanel);

                var alignPanel = StingWizardDialog.MakeLabelledSlider(
                    "Alignment Bonus (grid-aligned tags):", 0, 100, 40,
                    out _alignSlider, out _alignLabel);
                panel.Children.Add(alignPanel);

                var searchPanel = StingWizardDialog.MakeLabelledSlider(
                    "Max Search Radius (multiples of tag width):", 1, 10, 4,
                    out _searchSlider, out _searchLabel);
                panel.Children.Add(searchPanel);

                // Visual diagram
                panel.Children.Add(StingWizardDialog.MakeSectionHeader("Placement Algorithm"));
                panel.Children.Add(StingWizardDialog.MakeDescription(
                    "The algorithm evaluates 8 candidate positions around each element (N, NE, E, SE, S, SW, W, NW), " +
                    "scores each based on the weights above, and places the tag at the highest-scoring position. " +
                    "If all positions overlap, the search radius is extended and a leader line is added."));

                Content = panel;
            }

            public override void CollectResults(Dictionary<string, object> results)
            {
                results["OverlapPenalty"] = _overlapSlider?.Value ?? 80;
                results["ProximityBonus"] = _proximitySlider?.Value ?? 60;
                results["AlignBonus"] = _alignSlider?.Value ?? 40;
                results["MaxSearchRadius"] = _searchSlider?.Value ?? 4;
            }
        }
    }

    /// <summary>Settings from the smart placement wizard.</summary>
    public class SmartPlacementSettings
    {
        public string Scope { get; set; } = "Untagged";
        public string LeaderMode { get; set; } = "Auto";
        public string PreferredPosition { get; set; } = "Above";
        public double OverlapPenalty { get; set; } = 80;
        public double ProximityBonus { get; set; } = 60;
        public double AlignBonus { get; set; } = 40;
        public double MaxSearchRadius { get; set; } = 4;
        public string ElbowStyle { get; set; } = "90";
        public double TextSize { get; set; } = 2.5;
        public bool BatchViews { get; set; }
    }
}
