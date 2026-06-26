using System;
using Autodesk.Revit.UI;

namespace StingTools.UI.Sustainability
{
    // IDockablePaneProvider for the STING Sustainability Center — the 5th
    // dockable pane (Main, Electrical, Plumbing, HVAC, Sustainability). Built on
    // the StingPlumbingPanel template (Provider + CommandHandler + XAML + stable
    // PaneGuid). Tabbed behind PropertiesPalette so it sits alongside the others.
    public class StingSustainabilityPanelProvider : IDockablePaneProvider
    {
        private StingSustainabilityPanel _panel;

        // Stable GUID — generated once; never change so UIState.dat re-locates it.
        public static readonly Guid PaneGuid = new Guid("A3C5E7B9-1D2F-4A6B-8C0E-9F1A2B3C4D5E");
        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _panel = new StingSustainabilityPanel();
            data.FrameworkElement = _panel;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind    = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        public StingSustainabilityPanel GetPanel() => _panel;
    }
}
