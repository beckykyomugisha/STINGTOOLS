using Autodesk.Revit.DB;
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

    /// <summary>
    /// Extension methods providing safe UIApplication resolution for all commands.
    /// When invoked from the dockable panel, ExternalCommandData is null because
    /// Revit's native COM wrapper cannot be fabricated from user code. This extension
    /// falls back to StingCommandHandler.CurrentApp which is set by the IExternalEventHandler.
    /// When invoked from the ribbon, ExternalCommandData is valid and .Application works normally.
    /// </summary>
    public static class CommandContext
    {
        /// <summary>
        /// Safely resolve UIApplication from ExternalCommandData.
        /// Returns commandData.Application if available (ribbon invocation),
        /// otherwise falls back to StingCommandHandler.CurrentApp (panel invocation).
        /// </summary>
        public static UIApplication SafeApp(this ExternalCommandData commandData)
        {
            if (commandData != null)
            {
                try { return commandData.Application; }
                catch { /* corrupted ExternalCommandData — fall through to CurrentApp */ }
            }
            return UI.StingCommandHandler.CurrentApp;
        }

        /// <summary>
        /// Safely resolve Document from ExternalCommandData.
        /// </summary>
        public static Document SafeDoc(this ExternalCommandData commandData)
        {
            return commandData.SafeApp()?.ActiveUIDocument?.Document;
        }

        /// <summary>
        /// Safely resolve UIDocument from ExternalCommandData.
        /// </summary>
        public static UIDocument SafeUIDoc(this ExternalCommandData commandData)
        {
            return commandData.SafeApp()?.ActiveUIDocument;
        }
    }
}
