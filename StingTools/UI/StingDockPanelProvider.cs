using System;
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    /// <summary>
    /// IDockablePaneProvider for the STING Tools dockable panel.
    /// Creates and hosts the WPF Page that contains all tool tabs.
    /// </summary>
    public class StingDockPanelProvider : IDockablePaneProvider
    {
        private StingDockPanel _page;

        /// <summary>
        /// The GUID used to register this dockable pane with Revit.
        /// CHANGED from B3C4D5E6-... to force Revit to discard cached floating
        /// pane state from UIState.dat and use the new Tabbed dock position.
        /// </summary>
        public static readonly Guid PaneGuid = new Guid("D4E5F6A7-8901-BCDE-F012-345678901BCD");

        /// <summary>The DockablePaneId for registration and retrieval.</summary>
        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new StingDockPanel();
            data.FrameworkElement = _page;
            data.InitialState = new DockablePaneState
            {
                // Tab behind Properties panel — most stable docking configuration.
                // DockPosition.Right caused "Only floating document is support!" on
                // every Show(), and floating panes crash Revit on WPF tab switches
                // (ANTECEDENT: editor ArrowEditor → HIDEWHENNOVALUE → crash).
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette
            };
            data.VisibleByDefault = false;
        }

        /// <summary>Get the page instance (for updating status, etc.).</summary>
        public StingDockPanel GetPage() => _page;
    }
}
