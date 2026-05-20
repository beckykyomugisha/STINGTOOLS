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
        /// ROTATED again — the previous bump to E5F6A7B8-… collided with the
        /// Electrical panel's GUID (same value), causing Revit to drop one of
        /// the two registrations silently. The new value below was checked
        /// against every other STING pane (Electrical / HVAC / Plumbing).
        /// </summary>
        public static readonly Guid PaneGuid = new Guid("C8D9EA1B-2C3D-4E5F-6071-8A9BACBDCEDF");

        /// <summary>The DockablePaneId for registration and retrieval.</summary>
        public static DockablePaneId PaneId => new DockablePaneId(PaneGuid);

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            // Wrap construction. If the WPF Page throws (XAML parse error,
            // missing resource, theme-init failure), Revit silently swallows
            // the exception and leaves the pane registered but with a null
            // FrameworkElement — the pane then never renders and the user
            // sees "main panel missing" with no log entry. Substitute a
            // visible fallback so the failure is diagnosable.
            try
            {
                _page = new StingDockPanel();
                data.FrameworkElement = _page;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("StingDockPanel construction failed", ex);
                data.FrameworkElement = new System.Windows.Controls.TextBlock
                {
                    Text = "STING Tools panel failed to load.\n\n" +
                           "Cause: " + ex.Message + "\n\n" +
                           "Check StingTools.log for the full stack trace.",
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(16),
                    Foreground = System.Windows.Media.Brushes.IndianRed
                };
            }
            Instance = _page;
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
