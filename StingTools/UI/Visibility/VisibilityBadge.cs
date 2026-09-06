// StingTools — Visibility Center · hidden-count badge
//
// "Why can't I see my ducts" is the support ticket this exists to prevent. The SELECT-tab
// button and the ribbon Hub button carry the count, so a filtered view announces itself
// without the user opening anything.
//
// The count is a BY-PRODUCT of a read that already happened — opening the dropdown, or an
// Apply / Isolate / Reset — never a background poll. Between those it shows the last thing
// that was actually read, which is why the label always names a moment ("3 hidden") rather
// than implying it is live.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Visibility;

namespace StingTools.UI.VisibilityCenter
{
    internal static class VisibilityBadge
    {
        private const string BaseLabel = "👁 Show / Hide";

        /// <summary>The ribbon Hub button, captured at startup so its tooltip can be updated.</summary>
        private static PushButton _hubButton;

        private static string _label = BaseLabel + " ▾";
        private static string _tooltip =
            "Show/hide by category or ISO tag token (zone, location, level, discipline…)";

        /// <summary>Called once from the ribbon build so the Hub tooltip can carry the count.</summary>
        internal static void RegisterHubButton(PushButton button)
        {
            _hubButton = button;
            Push();
        }

        /// <summary>
        /// Update from a read-back that was just computed. Cheap — no Revit calls.
        /// </summary>
        internal static void Update(VisibilityReadback readback)
        {
            if (readback == null || !readback.AnythingHidden)
            {
                _label = BaseLabel + " ▾";
                _tooltip = "Show/hide by category or ISO tag token (zone, location, level, discipline…)\n" +
                           "Nothing is currently hidden in this view.";
            }
            else
            {
                _label = $"{BaseLabel} ({readback.HiddenCount:N0} hidden) ▾";
                _tooltip = readback.BadgeTooltip();
            }
            Push();
        }

        /// <summary>
        /// Re-read the active view and update. Used after Apply / Isolate / Reset, where the
        /// harvest cache is stale by definition — the model just changed.
        /// </summary>
        internal static void Refresh(Document doc, View view)
        {
            if (doc == null || view == null) return;
            try
            {
                TokenValueHarvester.InvalidateCache();
                Update(VisibilityStateReader.Read(doc, view).Readback);
            }
            catch (Exception ex)
            {
                // A badge is not worth failing a command over, but a swallowed refresh would
                // leave a stale count on screen — so it is logged, not ignored.
                StingLog.Warn($"VisibilityBadge.Refresh: {ex.Message}");
            }
        }

        private static void Push()
        {
            try
            {
                if (_hubButton != null) _hubButton.ToolTip = _tooltip;
            }
            catch (Exception ex) { StingLog.Warn($"VisibilityBadge: hub tooltip: {ex.Message}"); }

            try
            {
                var panel = StingDockPanel.LastInstance;
                if (panel == null) return;
                string label = _label, tip = _tooltip;
                panel.Dispatcher.BeginInvoke(new Action(() => panel.UpdateVisibilityBadge(label, tip)));
            }
            catch (Exception ex) { StingLog.Warn($"VisibilityBadge: panel badge: {ex.Message}"); }
        }
    }
}
