using System;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// IDockablePaneProvider for the STING Electrical Center panel.
    /// Hosts the WPF Page used for panel scheduling, circuit work,
    /// voltage-drop / breaker sizing, SLD viewer, lighting and reports.
    /// Docks tabbed behind the Properties palette so it sits alongside
    /// the main STING panel without overlap (Phase 177).
    /// </summary>
    public class StingElectricalPanelProvider : IDockablePaneProvider
    {
        private StingElectricalPanel _page;

        /// <summary>Stable GUID — burn this into UIState only once per Revit version.</summary>
        public static readonly Guid PaneGuid = new Guid("E5F6A7B8-9012-CDEF-0123-456789012CDE");

        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new StingElectricalPanel();
            data.FrameworkElement = _page;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        public StingElectricalPanel GetPage() => _page;
    }
}
