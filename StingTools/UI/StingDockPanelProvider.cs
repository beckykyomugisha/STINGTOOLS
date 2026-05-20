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
        /// Static reference set when SetupDockablePane runs so StingCommandHandler
        /// can access the live WPF page without walking the visual tree or relying on
        /// System.Windows.Application.Current (which may not be initialised in Revit).
        /// </summary>
        public static StingDockPanel Instance { get; private set; }

        /// <summary>
        /// The GUID used to register this dockable pane with Revit.
        /// ROTATED again (was D4E5F6A7-8901-BCDE-F012-345678901BCD) because users
        /// on builds that registered the panel under the old GUID had it cached
        /// as hidden in UIState.dat — even after the sibling Electrical / HVAC /
        /// Plumbing panels were opened, the main panel stayed missing. Bumping
        /// the GUID makes Revit treat this as a brand-new pane and honour
        /// VisibleByDefault again.
        /// </summary>
        public static readonly Guid PaneGuid = new Guid("E5F6A7B8-9012-CDEF-0123-456789012CDE");

        /// <summary>The DockablePaneId for registration and retrieval.</summary>
        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _page = new StingDockPanel();
            Instance = _page;
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
            // Sibling panels (Electrical / HVAC / Plumbing) also default to
            // false, but the user toggles those open on demand. The main panel
            // is the catch-all UI surface for ~610 buttons across 9 tabs — if
            // it's missing, the plugin looks broken. Default it visible so a
            // fresh install (or a UIState.dat reset via the GUID bump above)
            // brings the main panel back automatically.
            data.VisibleByDefault = true;
        }

        /// <summary>Get the page instance (for updating status, etc.).</summary>
        public StingDockPanel GetPage() => _page;
    }
}
