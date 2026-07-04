using System;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// IDockablePaneProvider for the STING Copilot — a natural-language chat panel
    /// that drives StingTools through the EXISTING MCP tools (shared McpToolDispatcher).
    /// Tabbed behind PropertiesPalette so it sits alongside the main STING panel and
    /// the Electrical / Plumbing / HVAC Centers without overlap.
    /// Mirrors StingHvacPanelProvider exactly.
    /// </summary>
    public class StingCopilotPanelProvider : IDockablePaneProvider
    {
        private StingCopilotPanel _page;

        /// <summary>Stable, distinct GUID — burn into UIState only once per Revit version.</summary>
        public static readonly Guid PaneGuid = new Guid("0922FD4C-BE95-4781-AF33-A588D640E6E6");

        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new StingCopilotPanel();
            data.FrameworkElement = _page;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind    = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        public StingCopilotPanel GetPage() => _page;
    }
}
