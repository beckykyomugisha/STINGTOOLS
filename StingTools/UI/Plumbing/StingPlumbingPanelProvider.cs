using System;
using Autodesk.Revit.UI;

namespace StingTools.UI.Plumbing
{
    // IDockablePaneProvider for the STING Plumbing Center panel.
    // Six tabs: SUPPLY · DRAINAGE · STORM · SPECIALTY · AUDIT · DOCS.
    // Tabbed behind PropertiesPalette so it sits alongside the main
    // STING panel and the Electrical Center without overlap.
    public class StingPlumbingPanelProvider : IDockablePaneProvider
    {
        private StingPlumbingPanel _panel;

        public static readonly Guid PaneGuid = new Guid("F1A2B3C4-D5E6-7890-ABCD-1234567890EF");
        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _panel = new StingPlumbingPanel();
            data.FrameworkElement = _panel;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind    = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        public StingPlumbingPanel GetPanel() => _panel;
    }
}
