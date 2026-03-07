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

        /// <summary>The GUID used to register this dockable pane with Revit.</summary>
        public static readonly Guid PaneGuid = new Guid("B3C4D5E6-7890-ABCD-EF01-234567890ABC");

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
