using System;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// IDockablePaneProvider for the STING HVAC Center.
    /// Seven tabs: EQPT · SYS · CALCS · DUCT · LOADS · FAB · RPRT.
    /// Tabbed behind PropertiesPalette so it sits alongside the main
    /// STING panel and the Electrical / Plumbing Centers without overlap.
    /// </summary>
    public class StingHvacPanelProvider : IDockablePaneProvider
    {
        private StingHvacPanel _page;

        /// <summary>Stable GUID — burn into UIState only once per Revit version.</summary>
        public static readonly Guid PaneGuid = new Guid("D7E8F9A0-B1C2-3D4E-5F60-1A2B3C4D5E6F");

        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new StingHvacPanel();
            data.FrameworkElement = _page;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind    = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        public StingHvacPanel GetPage() => _page;
    }
}
