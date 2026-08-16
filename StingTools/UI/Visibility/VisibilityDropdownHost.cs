// StingTools — Visibility Center · popup host
//
// Why this goes through the ExternalEvent instead of opening straight from Cmd_Click
// (which the runner allows): the dropdown's content comes from TokenValueHarvester, and
// that is a FilteredElementCollector pass — a Revit API call, which must happen on the
// Revit API thread. So Vis_OpenDropdown takes the normal handler round-trip, harvests on
// the API thread, then marshals back to the panel's dispatcher to show the popup. The
// popup itself is still a WPF Popup anchored to the SELECT-tab button, never a modal.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Visibility;

namespace StingTools.UI.VisibilityCenter
{
    /// <summary>Owns the single visibility popup instance and the small preset prompts.</summary>
    public static class VisibilityDropdownHost
    {
        private static Popup _popup;
        private static Window _window;          // panel-less fallback host (QAT / ribbon launch)
        private static VisibilityDropdown _content;

        /// <summary>Harvest on the current (API) thread, then show the popup on the UI thread.</summary>
        /// <param name="preferFloating">
        /// True when the launch came from the ribbon Hub or the Quick Access Toolbar. Those
        /// launches must NOT anchor to the dock panel even when it happens to be open: the
        /// user clicked something at the top of the screen, so a popup that appears docked to
        /// the right-hand panel reads as the wrong window opening. Anchor to the panel only
        /// when the click came FROM the panel.
        /// </param>
        public static void ShowWindow(UIApplication app, bool preferFloating = false)
        {
            // Callers must resolve this via VisibilityCommandHelper.ResolveApp — on a panel or
            // Hub dispatch ExternalCommandData is null by design, so cmd.Application alone
            // arrives here as null and every launch dies as a bogus "No active view."
            if (app == null)
            {
                TaskDialog.Show("STING Visibility",
                    "No Revit application context — the STING command handler has not been " +
                    "initialised yet. Open the STING panel once, then try again.");
                return;
            }

            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null)
            {
                TaskDialog.Show("STING Visibility", "No active view. Open a model view and try again.");
                return;
            }

            TokenHarvest harvest;
            try
            {
                harvest = TokenValueHarvester.Harvest(uidoc.Document, uidoc.ActiveView);
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityDropdownHost.Harvest", ex);
                TaskDialog.Show("STING Visibility", $"Could not read this view: {ex.Message}");
                return;
            }

            // The dock panel is the PREFERRED anchor, not a requirement. Refusing to open
            // without it defeats the point of pinning this to the Quick Access Toolbar —
            // the QAT is for reaching a tool WITHOUT first opening a panel. When there is
            // no panel to anchor to, show the same content in a small modeless window.
            var panel = preferFloating ? null : StingDockPanel.LastInstance;
            try
            {
                if (panel != null)
                    panel.Dispatcher.Invoke(new Action(() => ShowPopup(panel, harvest)));
                else
                    ShowStandaloneWindow(harvest, app.MainWindowHandle);
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityDropdownHost.ShowPopup", ex);
                TaskDialog.Show("STING Visibility", $"Could not open the dropdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Panel-less fallback: the dropdown as its own modeless window, owned by the Revit
        /// main window so it floats above it and does not block the API thread.
        /// </summary>
        private static void ShowStandaloneWindow(TokenHarvest harvest, IntPtr revitHwnd)
        {
            if (_content == null)
            {
                _content = new VisibilityDropdown();
                _content.ActionRequested += OnActionRequested;
            }
            _content.Load(harvest);

            if (_window == null || !_window.IsLoaded)
            {
                _window = new Window
                {
                    Title = "STING Visibility",
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    // Manual, not CenterOwner: this stands in for a dropdown hanging off the
                    // button the user just clicked, so it belongs under the cursor at the top
                    // of the screen — centring it on Revit reads as an unrelated dialog.
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                // Own it to Revit's main window via the SUPPORTED handle
                // (UIApplication.MainWindowHandle), not Autodesk.Windows —
                // AdWindows is undocumented and version-fragile, and this project
                // deliberately carries no dependency on it.
                try
                {
                    if (revitHwnd != IntPtr.Zero)
                        new System.Windows.Interop.WindowInteropHelper(_window).Owner = revitHwnd;
                }
                catch (Exception ex) { StingLog.Info($"Visibility window owner: {ex.Message}"); }
                _window.Closed += (s, e) => { _window = null; };
            }

            // The content may still be parented by the popup from an earlier panel launch.
            if (_popup != null && _popup.Child == _content) _popup.Child = null;
            _window.Content = _content;
            _window.Show();
            PositionUnderCursor(_window);
            _window.Activate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        /// <summary>
        /// Drop the window just below-left of the cursor, then pull it back on-screen if that
        /// would push it off. Positioned AFTER Show() because SizeToContent means the real
        /// width/height are unknown until the first layout pass, and clamping needs them.
        /// </summary>
        private static void PositionUnderCursor(Window win)
        {
            try
            {
                POINT p;
                if (!GetCursorPos(out p)) return;

                // GetCursorPos is in physical pixels; WPF Left/Top are device-independent.
                var src = System.Windows.Interop.HwndSource.FromVisual(win) as System.Windows.Interop.HwndSource;
                double sx = 1.0, sy = 1.0;
                if (src?.CompositionTarget != null)
                {
                    var m = src.CompositionTarget.TransformFromDevice;
                    sx = m.M11; sy = m.M22;
                }

                double left = (p.X * sx) - 40;   // slight left bias so the cursor sits over the window
                double top  = (p.Y * sy) + 14;   // clear of the ribbon button itself

                double maxL = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth  - win.ActualWidth  - 8;
                double maxT = SystemParameters.VirtualScreenTop  + SystemParameters.VirtualScreenHeight - win.ActualHeight - 8;

                win.Left = Math.Max(SystemParameters.VirtualScreenLeft + 8, Math.Min(left, maxL));
                win.Top  = Math.Max(SystemParameters.VirtualScreenTop  + 8, Math.Min(top,  maxT));
            }
            catch (Exception ex)
            {
                StingLog.Info($"PositionUnderCursor: {ex.Message}");
            }
        }

        private static void ShowPopup(StingDockPanel panel, TokenHarvest harvest)
        {
            if (_content == null)
            {
                _content = new VisibilityDropdown();
                _content.ActionRequested += OnActionRequested;
            }
            _content.Load(harvest);

            if (_popup == null)
            {
                _popup = new Popup
                {
                    StaysOpen = false,
                    AllowsTransparency = true,
                    Placement = PlacementMode.Bottom,
                    PopupAnimation = PopupAnimation.Fade,
                    Child = _content
                };
            }

            // The content may still be hosted by the standalone window from a panel-less
            // launch. A UIElement has one parent — reclaim it before re-parenting to the
            // popup, or WPF throws "already the logical child of another element".
            if (_window != null && ReferenceEquals(_window.Content, _content))
            {
                _window.Content = null;
                _window.Hide();
            }
            if (_popup != null && _popup.Child == null) _popup.Child = _content;

            // Anchor to the SELECT-tab button when we can find it; fall back to the panel.
            var anchor = panel.FindName("btnVisDropdown") as UIElement ?? panel as UIElement;
            _popup.PlacementTarget = anchor;
            _popup.IsOpen = false;
            _popup.IsOpen = true;
        }

        private static void OnActionRequested(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            try
            {
                if (_popup != null) _popup.IsOpen = false;
                StingDockPanel.DispatchCommand(tag);
            }
            catch (Exception ex)
            {
                StingLog.Error($"VisibilityDropdownHost.OnActionRequested({tag})", ex);
            }
        }

        /// <summary>Close the popup if it is showing. Safe from any thread.</summary>
        public static void Close()
        {
            try
            {
                if (_popup == null) return;
                if (_popup.Dispatcher.CheckAccess()) _popup.IsOpen = false;
                else _popup.Dispatcher.Invoke(new Action(() => _popup.IsOpen = false));
            }
            catch (Exception ex) { StingLog.Warn($"VisibilityDropdownHost.Close: {ex.Message}"); }
        }

        // ── Small prompts ───────────────────────────────────────────────

        /// <summary>Modal name prompt for Save preset. Returns null when cancelled.</summary>
        public static string PromptForPresetName()
        {
            var win = new Window
            {
                Title = "Save visibility preset",
                Width = 340,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock
            {
                Text = "Preset name",
                Margin = new Thickness(0, 0, 0, 4)
            });

            var box = new System.Windows.Controls.TextBox { Padding = new Thickness(4, 3, 4, 3) };
            stack.Children.Add(box);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var ok = new Button { Content = "Save", Width = 74, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 74, IsCancel = true };
            row.Children.Add(ok);
            row.Children.Add(cancel);
            stack.Children.Add(row);

            win.Content = stack;

            string chosen = null;
            ok.Click += (s, e) => { chosen = box.Text; win.DialogResult = true; };

            box.Focus();
            return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(chosen) ? chosen.Trim() : null;
        }

        /// <summary>
        /// Tick one or more presets and get back a single combined set. Null when cancelled.
        /// <para>Combining is the point: "Level solo" + "Hide all MEP" is a real request, and
        /// forcing one-at-a-time made the user re-pick constantly. Rules concatenate, and the
        /// engine's existing semantics do the rest — values within a rule OR, rules across
        /// kinds AND.</para>
        /// <para>Hide and Show-only cannot be combined: they are opposite instructions and the
        /// engine rejects a mixed set. Rather than let the user build one and fail at Apply,
        /// ticking a preset of one action disables the presets of the other, live.</para>
        /// </summary>
        public static VisibilitySet PromptForPresets(List<VisibilitySet> presets)
        {
            if (presets == null || presets.Count == 0) return null;

            var win = new Window
            {
                Title = "Visibility presets",
                Width = 420,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new TextBlock
            {
                Text = "Tick any number — they combine. Corporate baseline plus this project's saved presets.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Opacity = 0.8
            };
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            var list = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.85
            };
            Grid.SetRow(status, 2);
            root.Children.Add(status);

            var boxes = new List<Tuple<CheckBox, VisibilitySet>>();

            foreach (var p in presets)
            {
                bool isShowOnly = p.Rules != null &&
                                  p.Rules.Any(r => r != null && r.Action == VisibilityAction.ShowOnly);
                string origin = string.Equals(p.Origin, "project", StringComparison.OrdinalIgnoreCase)
                    ? "  (project)" : "";

                var cb = new CheckBox
                {
                    Margin = new Thickness(0, 3, 0, 3),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"{p.Name}{origin}   ·   {(isShowOnly ? "Show-only" : "Hide")}"
                                       + (p.Mode == VisibilityMode.ViewFilter ? "   ·   saved to view" : ""),
                                FontWeight = FontWeights.SemiBold
                            },
                            new TextBlock
                            {
                                Text = p.Description ?? "",
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.75,
                                Margin = new Thickness(0, 1, 0, 0)
                            }
                        }
                    }
                };
                list.Children.Add(cb);
                boxes.Add(Tuple.Create(cb, p));
            }

            // Live conflict lock-out + running count.
            Action refresh = () =>
            {
                var ticked = boxes.Where(b => b.Item1.IsChecked == true).Select(b => b.Item2).ToList();
                bool anyShowOnly = ticked.Any(p => p.Rules.Any(r => r.Action == VisibilityAction.ShowOnly));
                bool anyHide     = ticked.Any(p => p.Rules.Any(r => r.Action != VisibilityAction.ShowOnly));

                foreach (var b in boxes)
                {
                    if (b.Item1.IsChecked == true) { b.Item1.IsEnabled = true; continue; }
                    bool showOnly = b.Item2.Rules.Any(r => r.Action == VisibilityAction.ShowOnly);
                    b.Item1.IsEnabled = !((anyShowOnly && !showOnly) || (anyHide && showOnly));
                }

                int rules = ticked.Sum(p => p.Rules.Count);
                status.Text = ticked.Count == 0
                    ? "Nothing ticked."
                    : $"{ticked.Count} preset(s), {rules} rule(s) combined: {string.Join(" + ", ticked.Select(p => p.Name))}"
                      + (anyShowOnly
                            ? "\nShow-only is active, so Hide presets are locked out until you untick these."
                            : anyHide
                                ? "\nHide is active, so Show-only presets are locked out until you untick these."
                                : "");
            };
            foreach (var b in boxes)
            {
                b.Item1.Checked   += (s, e) => refresh();
                b.Item1.Unchecked += (s, e) => refresh();
            }
            refresh();

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var clear  = new Button { Content = "Clear", Width = 74, Margin = new Thickness(0, 0, 6, 0) };
            var ok     = new Button { Content = "Load",  Width = 74, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 74, IsCancel = true };
            row.Children.Add(clear);
            row.Children.Add(ok);
            row.Children.Add(cancel);
            Grid.SetRow(row, 3);
            root.Children.Add(row);

            clear.Click += (s, e) => { foreach (var b in boxes) b.Item1.IsChecked = false; refresh(); };

            VisibilitySet combined = null;
            ok.Click += (s, e) =>
            {
                var ticked = boxes.Where(b => b.Item1.IsChecked == true).Select(b => b.Item2).ToList();
                if (ticked.Count == 0) { status.Text = "Tick at least one preset, or press Cancel."; return; }
                combined = Combine(ticked);
                win.DialogResult = true;
            };

            win.Content = root;
            return win.ShowDialog() == true ? combined : null;
        }

        /// <summary>
        /// Merge ticked presets into one set. Mode escalates to ViewFilter if any ticked preset
        /// is saved-to-view — the persistent intent is the stronger one, and silently
        /// downgrading it to Temporary would lose work the user asked to keep.
        /// </summary>
        private static VisibilitySet Combine(List<VisibilitySet> ticked)
        {
            if (ticked.Count == 1) return ticked[0];

            return new VisibilitySet
            {
                Name   = string.Join(" + ", ticked.Select(p => p.Name)),
                Mode   = ticked.Any(p => p.Mode == VisibilityMode.ViewFilter)
                            ? VisibilityMode.ViewFilter : VisibilityMode.Temporary,
                Target = ticked[0].Target,
                Origin = "combined",
                Description = "Combined preset: " + string.Join(" + ", ticked.Select(p => p.Name)),
                Rules  = ticked.SelectMany(p => p.Rules).ToList()
            };
        }
    }
}
