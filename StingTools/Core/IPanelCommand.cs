using Autodesk.Revit.UI;

namespace StingTools.Core
{
    /// <summary>
    /// Interface for commands invoked from the WPF dockable panel.
    /// Bypasses ExternalCommandData fabrication (which uses reflection hacks
    /// on native COM wrappers and can crash Revit with access violations).
    /// Commands implementing this interface receive the real UIApplication
    /// directly from the IExternalEventHandler.Execute context.
    /// </summary>
    public interface IPanelCommand
    {
        /// <summary>
        /// Execute the command from the dockable panel context.
        /// </summary>
        /// <param name="app">The UIApplication provided by the IExternalEventHandler.</param>
        /// <returns>Revit Result indicating success, failure, or cancellation.</returns>
        Result Execute(UIApplication app);
    }
}
