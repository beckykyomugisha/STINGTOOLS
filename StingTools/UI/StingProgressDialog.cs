using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StingTools.UI
{
    /// <summary>
    /// ENH-001: Reusable modeless WPF progress window for batch operations.
    /// Shows progress bar, element count, estimated time remaining, and cancel button.
    /// Thread-safe updates via Dispatcher. Uses Win32 GetAsyncKeyState for Escape key.
    ///
    /// Usage:
    ///   var progress = StingProgressDialog.Show("Batch Tag", totalElements);
    ///   foreach (var el in elements)
    ///   {
    ///       if (progress.IsCancelled) break;
    ///       // ... process element ...
    ///       progress.Increment($"Processing {el.Name}");
    ///   }
    ///   progress.Close();
    /// </summary>
    public class StingProgressDialog
    {
        private readonly Window _window;
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _statusText;
        private readonly TextBlock _countText;
        private readonly TextBlock _etaText;
        private readonly Stopwatch _stopwatch;
        private readonly int _total;
        private int _current;
        private volatile bool _cancelled;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_ESCAPE = 0x1B;

        /// <summary>True if user clicked Cancel or pressed Escape.</summary>
        public bool IsCancelled
        {
            get
            {
                // Check Escape key (Win32 — works even when Revit has focus)
                if (!_cancelled && (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
                    _cancelled = true;
                return _cancelled;
            }
        }

        /// <summary>Current progress count.</summary>
        public int Current => _current;

        private StingProgressDialog(string title, int total)
        {
            _total = Math.Max(total, 1);
            _current = 0;
            _cancelled = false;
            // UX-01: Always create a fresh stopwatch so ETA doesn't carry over
            _stopwatch = new Stopwatch();
            _stopwatch.Start();

            // Build WPF window programmatically (no XAML needed)
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = _total,
                Value = 0,
                Height = 24,
                Margin = new Thickness(0, 0, 0, 8),
            };

            _statusText = new TextBlock
            {
                Text = "Starting...",
                FontSize = 12,
                Foreground = Brushes.DarkGray,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 4),
            };

            _countText = new TextBlock
            {
                Text = $"0 / {_total}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            };

            _etaText = new TextBlock
            {
                Text = "Estimating...",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 8),
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            cancelButton.Click += (s, e) => { _cancelled = true; };

            var panel = new StackPanel
            {
                Margin = new Thickness(16),
            };
            panel.Children.Add(_countText);
            panel.Children.Add(_progressBar);
            panel.Children.Add(_statusText);
            panel.Children.Add(_etaText);
            panel.Children.Add(cancelButton);

            _window = new Window
            {
                Title = $"STING — {title}",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Content = panel,
            };
        }

        /// <summary>
        /// Show a new progress dialog. Call from Revit API thread.
        /// </summary>
        public static StingProgressDialog Show(string title, int total)
        {
            var dialog = new StingProgressDialog(title, total);
            dialog._window.Show();
            return dialog;
        }

        /// <summary>
        /// Increment progress by 1 and update status text.
        /// Only updates UI every 50 elements for performance.
        /// </summary>
        public void Increment(string statusMessage = null)
        {
            int val = Interlocked.Increment(ref _current);

            // Update UI every 50 elements or at completion
            if (val % 50 == 0 || val >= _total || val == 1)
            {
                try
                {
                    _window.Dispatcher.Invoke(() =>
                    {
                        _progressBar.Value = val;
                        _countText.Text = $"{val:N0} / {_total:N0}";

                        if (!string.IsNullOrEmpty(statusMessage))
                            _statusText.Text = statusMessage;

                        // ETA calculation
                        double elapsed = _stopwatch.Elapsed.TotalSeconds;
                        if (val > 0 && elapsed > 0.5)
                        {
                            double rate = val / elapsed;
                            int remaining = _total - val;
                            double etaSeconds = remaining / rate;

                            if (etaSeconds < 60)
                                _etaText.Text = $"~{etaSeconds:F0}s remaining ({rate:F0} elem/s)";
                            else
                                _etaText.Text = $"~{etaSeconds / 60:F1}min remaining ({rate:F0} elem/s)";
                        }
                    });
                }
                catch { /* Window may have been closed */ }
            }
        }

        /// <summary>
        /// Close the progress dialog.
        /// </summary>
        public void Close()
        {
            _stopwatch.Stop();
            try
            {
                _window.Dispatcher.Invoke(() =>
                {
                    try { _window.Close(); } catch { }
                });
            }
            catch { }
        }

        /// <summary>
        /// Update status text without incrementing.
        /// </summary>
        public void SetStatus(string statusMessage)
        {
            try
            {
                _window.Dispatcher.Invoke(() =>
                {
                    _statusText.Text = statusMessage ?? "";
                });
            }
            catch { }
        }
    }
}
