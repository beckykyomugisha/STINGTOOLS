using System;
using Autodesk.Revit.UI;

namespace StingTools.UI.Plumbing
{
    // IDockablePaneProvider for the STING Plumbing Center panel.
    // Eight tabs: SYSTEM · SUPPLY · DRAINAGE · ROUTE · STORM · SPECIALTY ·
    // AUDIT · DOCS. The visual tree lives in StingPlumbingPanel.xaml +
    // StingPlumbingPanel.xaml.cs (Electrical-grade rebuild — DataGrids,
    // Expanders, ComboBoxes, project-context strip).
    // Tabbed behind PropertiesPalette so it sits alongside the main
    // STING panel, the Electrical Center, and the HVAC Center without
    // overlap.
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
