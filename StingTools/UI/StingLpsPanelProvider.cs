using System;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// IDockablePaneProvider for the STING Lightning Protection Center.
    /// Seven tabs: RISK · AIR-TERM · CONDUCTORS · EARTH · SPD · ZONES · RPRT.
    /// Tabbed behind PropertiesPalette so it sits alongside HVAC / Electrical /
    /// Plumbing Centers without overlap.
    /// </summary>
    public class StingLpsPanelProvider : IDockablePaneProvider
    {
        private StingLpsPanel _page;

        /// <summary>Stable GUID — burn into UIState only once per Revit version.</summary>
        public static readonly Guid PaneGuid = new Guid("F1E2D3C4-B5A6-4978-89AB-CDEF01234567");

        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new StingLpsPanel();
            data.FrameworkElement = _page;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind    = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        public StingLpsPanel GetPage() => _page;
    }
}
