// Phase 127-D — keyboard shortcuts for the Placement Centre.
//
// Pure RoutedCommand declarations; the centre's code-behind binds each
// to its existing click handler via Window.CommandBindings so the
// shortcuts compose with the toolbar buttons without duplication.

using System.Windows.Input;

namespace StingTools.UI.PlacementCenter
{
    public static class PlacementCentreCommands
    {
        public static readonly RoutedCommand SaveProject     = new RoutedCommand();
        public static readonly RoutedCommand AddRule         = new RoutedCommand();
        public static readonly RoutedCommand RunPlacement    = new RoutedCommand();
        public static readonly RoutedCommand Preview         = new RoutedCommand();
        public static readonly RoutedCommand Validate        = new RoutedCommand();
        public static readonly RoutedCommand UndoLast        = new RoutedCommand();
        public static readonly RoutedCommand HistoryRefresh  = new RoutedCommand();
        public static readonly RoutedCommand ClearPreview    = new RoutedCommand();
        public static readonly RoutedCommand DeleteSelected  = new RoutedCommand();

        // Phase 139 — Excel round-trip + Building Profile commands.
        public static readonly RoutedCommand ExportExcel        = new RoutedCommand();
        public static readonly RoutedCommand ImportExcel        = new RoutedCommand();
        public static readonly RoutedCommand LoadProfile        = new RoutedCommand();
        public static readonly RoutedCommand SaveProfile        = new RoutedCommand();
        public static readonly RoutedCommand RunCoverageAudit   = new RoutedCommand();
        public static readonly RoutedCommand RunUniformityAudit = new RoutedCommand();
        public static readonly RoutedCommand RunMaintenanceAudit = new RoutedCommand();
    }
}
