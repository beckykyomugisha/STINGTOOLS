using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Reusable multi-page WPF wizard dialog with corporate styling.
    /// Provides a step indicator, page navigation, and consistent look for
    /// complex workflows like COBie export, BEP creation, issue management.
    /// </summary>
    public class StingWizardDialog : Window
    {
        // ── Brand colours ──
        private static readonly Color BrandPurple = Color.FromRgb(88, 44, 131);
        private static readonly Color BrandGold = Color.FromRgb(255, 193, 7);
        private static readonly Color ActiveStep = Color.FromRgb(88, 44, 131);
        private static readonly Color CompletedStep = Color.FromRgb(76, 175, 80);
        private static readonly Color PendingStep = Color.FromRgb(189, 189, 189);
        private static readonly Color PanelBg = Color.FromRgb(250, 250, 252);
        private static readonly Color BorderGrey = Color.FromRgb(220, 220, 230);

        private readonly List<WizardPage> _pages = new();
        private int _currentPage = 0;
        private readonly StackPanel _stepIndicator;
        private readonly ContentControl _pageHost;
        private readonly Button _backBtn;
        private readonly Button _nextBtn;
        private readonly Button _finishBtn;
        private readonly TextBlock _statusText;

        /// <summary>True if the user completed the wizard (clicked Finish).</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>Collected results from all pages.</summary>
        public Dictionary<string, object> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        public StingWizardDialog(string title, int width = 780, int height = 560)
        {
            Title = title;
            Width = width; Height = height;
            MinWidth = 600; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(PanelBg);
            FontFamily = new FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResizeWithGrip;

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"StingWizardDialog owner: {ex.Message}"); }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Step indicator
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Page
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Footer

            // ── Header ──
            var header = new Border
            {
                Background = new SolidColorBrush(BrandPurple),
                Padding = new Thickness(20, 14, 20, 14)
            };
            header.Child = new TextBlock
            {
                Text = title,
                FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Step indicator ──
            _stepIndicator = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 12, 20, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(_stepIndicator, 1);
            root.Children.Add(_stepIndicator);

            // ── Page host ──
            var pageScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20, 4, 20, 4)
            };
            _pageHost = new ContentControl();
            pageScroll.Content = _pageHost;
            Grid.SetRow(pageScroll, 2);
            root.Children.Add(pageScroll);

            // ── Footer ──
            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 248)),
                BorderBrush = new SolidColorBrush(BorderGrey),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 10, 20, 10)
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var cancelBtn = MakeButton("Cancel", false);
            cancelBtn.Click += (s, e) => { IsCompleted = false; DialogResult = false; };
            btnPanel.Children.Add(cancelBtn);

            _backBtn = MakeButton("← Back", false);
            _backBtn.Margin = new Thickness(8, 0, 0, 0);
            _backBtn.Click += (s, e) => NavigateTo(_currentPage - 1);
            btnPanel.Children.Add(_backBtn);

            _nextBtn = MakeButton("Next →", true);
            _nextBtn.Margin = new Thickness(8, 0, 0, 0);
            _nextBtn.Click += (s, e) =>
            {
                if (ValidateCurrentPage())
                    NavigateTo(_currentPage + 1);
            };
            btnPanel.Children.Add(_nextBtn);

            _finishBtn = MakeButton("✓ Finish", true);
            _finishBtn.Margin = new Thickness(8, 0, 0, 0);
            _finishBtn.Background = new SolidColorBrush(CompletedStep);
            _finishBtn.Click += (s, e) =>
            {
                if (ValidateCurrentPage())
                {
                    CollectAllResults();
                    IsCompleted = true;
                    DialogResult = true;
                }
            };
            btnPanel.Children.Add(_finishBtn);

            Grid.SetColumn(btnPanel, 1);
            footerGrid.Children.Add(btnPanel);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
        }

        /// <summary>Add a page to the wizard.</summary>
        public void AddPage(WizardPage page)
        {
            _pages.Add(page);
            page.Wizard = this;
        }

        /// <summary>Show the wizard after all pages are added.</summary>
        public new bool? ShowDialog()
        {
            if (_pages.Count == 0) return false;
            RebuildStepIndicator();
            NavigateTo(0);
            // Phase 98: wizard stacks above BCC (when open) or above Revit's
            // main HWND otherwise, so users can't lose it behind other windows.
            StingWindowHelper.ApplyOwner(this);
            return base.ShowDialog();
        }

        private void NavigateTo(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            _currentPage = index;

            var page = _pages[_currentPage];
            if (page.Content == null)
                page.BuildContent();
            _pageHost.Content = page.Content;

            // Update navigation buttons
            _backBtn.Visibility = _currentPage > 0 ? Visibility.Visible : Visibility.Collapsed;
            _nextBtn.Visibility = _currentPage < _pages.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
            _finishBtn.Visibility = _currentPage == _pages.Count - 1 ? Visibility.Visible : Visibility.Collapsed;

            _statusText.Text = $"Step {_currentPage + 1} of {_pages.Count}: {page.Title}";
            UpdateStepIndicator();

            page.OnNavigatedTo();
        }

        private bool ValidateCurrentPage()
        {
            string error = _pages[_currentPage].Validate();
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(error, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _pages[_currentPage].CollectResults(Results);
            return true;
        }

        private void CollectAllResults()
        {
            foreach (var page in _pages)
                page.CollectResults(Results);
        }

        private void RebuildStepIndicator()
        {
            _stepIndicator.Children.Clear();
            for (int i = 0; i < _pages.Count; i++)
            {
                if (i > 0)
                {
                    _stepIndicator.Children.Add(new TextBlock
                    {
                        Text = "───", Foreground = new SolidColorBrush(PendingStep),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0), FontSize = 10
                    });
                }

                var circle = new Border
                {
                    Width = 28, Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(PendingStep),
                    VerticalAlignment = VerticalAlignment.Center
                };
                circle.Child = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                circle.Tag = i;

                var stepPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(4, 0, 4, 0)
                };
                stepPanel.Children.Add(circle);
                stepPanel.Children.Add(new TextBlock
                {
                    Text = _pages[i].Title, FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                    MaxWidth = 80, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
                });
                _stepIndicator.Children.Add(stepPanel);
            }
        }

        private void UpdateStepIndicator()
        {
            int stepIdx = 0;
            foreach (UIElement child in _stepIndicator.Children)
            {
                if (child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Border circle)
                {
                    int idx = stepIdx;
                    Color color = idx < _currentPage ? CompletedStep :
                                  idx == _currentPage ? ActiveStep : PendingStep;
                    circle.Background = new SolidColorBrush(color);
                    stepIdx++;
                }
            }
        }

        // ── Helpers ──

        /// <summary>Create a labelled combo box for wizard pages.</summary>
        public static StackPanel MakeLabelledCombo(string label, string[] items, int selectedIndex, out ComboBox combo)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            panel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            combo = new ComboBox
            {
                FontSize = 12, Padding = new Thickness(8, 6, 8, 6),
                Background = Brushes.White
            };
            foreach (string item in items)
                combo.Items.Add(item);
            if (selectedIndex >= 0 && selectedIndex < items.Length)
                combo.SelectedIndex = selectedIndex;
            panel.Children.Add(combo);
            return panel;
        }

        /// <summary>Create a labelled text box for wizard pages.</summary>
        public static StackPanel MakeLabelledText(string label, string defaultValue, out TextBox textBox)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            panel.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            textBox = new TextBox
            {
                Text = defaultValue, FontSize = 12,
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brushes.White
            };
            panel.Children.Add(textBox);
            return panel;
        }

        /// <summary>Create a labelled slider for wizard pages.</summary>
        public static StackPanel MakeLabelledSlider(string label, double min, double max, double value,
            out Slider slider, out TextBlock valueLabel)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            var headerRow = new DockPanel();
            headerRow.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 80))
            });
            valueLabel = new TextBlock
            {
                Text = value.ToString("F0"), FontSize = 12,
                Foreground = new SolidColorBrush(BrandPurple),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(valueLabel, Dock.Right);
            headerRow.Children.Add(valueLabel);
            panel.Children.Add(headerRow);

            slider = new Slider
            {
                Minimum = min, Maximum = max, Value = value,
                TickFrequency = 1, IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var lbl = valueLabel; // capture for closure
            slider.ValueChanged += (s, e) => lbl.Text = e.NewValue.ToString("F0");
            panel.Children.Add(slider);
            return panel;
        }

        /// <summary>Create a labelled checkbox for wizard pages.</summary>
        public static CheckBox MakeLabelledCheck(string label, bool isChecked)
        {
            return new CheckBox
            {
                Content = label, FontSize = 12, IsChecked = isChecked,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 80))
            };
        }

        /// <summary>Create a section header for wizard pages.</summary>
        public static TextBlock MakeSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(BrandPurple),
                Margin = new Thickness(0, 12, 0, 6)
            };
        }

        /// <summary>Create a description label.</summary>
        public static TextBlock MakeDescription(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static Button MakeButton(string text, bool primary)
        {
            var btn = new Button
            {
                Content = text, MinWidth = 80, Height = 32, FontSize = 12,
                Padding = new Thickness(16, 4, 16, 4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(primary ? BrandPurple : BorderGrey)
            };
            if (primary)
            {
                btn.Background = new SolidColorBrush(BrandPurple);
                btn.Foreground = Brushes.White;
            }
            else
            {
                btn.Background = Brushes.White;
                btn.Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 80));
            }
            return btn;
        }
    }

    /// <summary>
    /// Base class for wizard pages. Subclass to create specific wizard steps.
    /// </summary>
    public abstract class WizardPage
    {
        /// <summary>Short title displayed in the step indicator.</summary>
        public string Title { get; set; }

        /// <summary>Description shown at top of page.</summary>
        public string Description { get; set; }

        /// <summary>The visual content of this page (built lazily).</summary>
        public UIElement Content { get; protected set; }

        /// <summary>Reference to the parent wizard.</summary>
        public StingWizardDialog Wizard { get; internal set; }

        /// <summary>Build the WPF content for this page. Called once when first navigated to.</summary>
        public abstract void BuildContent();

        /// <summary>Validate the page. Return null/empty if valid, or an error message.</summary>
        public virtual string Validate() => null;

        /// <summary>Collect results from this page into the wizard results dictionary.</summary>
        public virtual void CollectResults(Dictionary<string, object> results) { }

        /// <summary>Called each time the page is navigated to (for refresh/update).</summary>
        public virtual void OnNavigatedTo() { }
    }
}
