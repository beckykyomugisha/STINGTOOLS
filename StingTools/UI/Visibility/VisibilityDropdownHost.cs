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
        private static VisibilityDropdown _content;

        /// <summary>Harvest on the current (API) thread, then show the popup on the UI thread.</summary>
        public static void ShowWindow(UIApplication app)
        {
            var uidoc = app?.ActiveUIDocument;
            if (uidoc?.ActiveView == null)
            {
                TaskDialog.Show("STING Visibility", "No active view.");
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

            var panel = StingDockPanel.LastInstance;
            if (panel == null)
            {
                TaskDialog.Show("STING Visibility",
                    "Open the STING panel first — the visibility dropdown anchors to its SELECT tab.");
                return;
            }

            try
            {
                panel.Dispatcher.Invoke(new Action(() => ShowPopup(panel, harvest)));
            }
            catch (Exception ex)
            {
                StingLog.Error("VisibilityDropdownHost.ShowPopup", ex);
                TaskDialog.Show("STING Visibility", $"Could not open the dropdown: {ex.Message}");
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

        /// <summary>Pick a preset by name, reusing the shared list picker. Null when cancelled.</summary>
        public static VisibilitySet PromptForPreset(List<VisibilitySet> presets)
        {
            if (presets == null || presets.Count == 0) return null;

            var labels = presets
                .Select(p => string.Equals(p.Origin, "project", StringComparison.OrdinalIgnoreCase)
                    ? $"{p.Name}  (project)"
                    : p.Name)
                .ToList();

            string picked = StingTools.Select.StingListPicker.Show(
                "Visibility presets",
                "Corporate baseline plus this project's saved presets.",
                labels);
            if (string.IsNullOrWhiteSpace(picked)) return null;

            int idx = labels.IndexOf(picked);
            return idx >= 0 && idx < presets.Count ? presets[idx] : null;
        }
    }
}
