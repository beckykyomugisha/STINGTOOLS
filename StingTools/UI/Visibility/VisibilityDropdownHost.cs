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

            // Read what the view is ALREADY hiding, not just what it is showing. Loading rows
            // without this made every row open ticked, so the panel asserted "nothing hidden"
            // over a filtered view and the next Apply was computed from that wrong baseline.
            VisibilityStateResult state;
            try
            {
                state = VisibilityStateReader.Read(uidoc.Document, uidoc.ActiveView);
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityDropdownHost.ReadState", ex);
                TaskDialog.Show("STING Visibility", $"Could not read this view: {ex.Message}");
                return;
            }

            VisibilityBadge.Update(state.Readback);

            // The dock panel is the PREFERRED anchor, not a requirement. Refusing to open
            // without it defeats the point of pinning this to the Quick Access Toolbar —
            // the QAT is for reaching a tool WITHOUT first opening a panel. When there is
            // no panel to anchor to, show the same content in a small modeless window.
            var panel = preferFloating ? null : StingDockPanel.LastInstance;
            try
            {
                if (panel != null)
                    panel.Dispatcher.Invoke(new Action(() => ShowPopup(panel, state)));
                else
                    ShowStandaloneWindow(state, app.MainWindowHandle);
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
        private static void ShowStandaloneWindow(VisibilityStateResult state, IntPtr revitHwnd)
        {
            if (_content == null)
            {
                _content = new VisibilityDropdown();
                _content.ActionRequested += OnActionRequested;
            }
            _content.Load(state.Harvest, state.Readback);

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

        private static void ShowPopup(StingDockPanel panel, VisibilityStateResult state)
        {
            if (_content == null)
            {
                _content = new VisibilityDropdown();
                _content.ActionRequested += OnActionRequested;
            }
            _content.Load(state.Harvest, state.Readback);

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
    }
}
