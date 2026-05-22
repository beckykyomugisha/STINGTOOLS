using System;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// IDockablePaneProvider for the STING Material Hub — modeless,
    /// floats independently from the main STING panel. Three-pane
    /// layout (Nav / Grid / Inspector) with sticky KPI strip + action
    /// bar. Tabbed behind PropertiesPalette by default but free to
    /// undock / float / pin anywhere.
    /// </summary>
    public class MaterialHubProvider : IDockablePaneProvider
    {
        private MaterialHubPanel _page;

        public static readonly Guid PaneGuid = new Guid("F1A2B3C4-D5E6-4A7B-8C9D-0E1F2A3B4C5D");
        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new MaterialHubPanel();
            data.FrameworkElement = _page;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette,
            };
            data.VisibleByDefault = false;
        }

        public MaterialHubPanel GetPage() => _page;
    }
}
