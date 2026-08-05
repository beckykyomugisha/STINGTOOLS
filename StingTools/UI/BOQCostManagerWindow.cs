// ══════════════════════════════════════════════════════════════════════════
//  BOQCostManagerWindow.cs — Phase 7 (companion).
//  Standalone Window that hosts BOQCostManagerPanel. Opened via the
//  "BOQCostManager" dispatch tag from both the dock panel button and the
//  BIM Coordination Center. Non-modal so a user can continue working in
//  Revit (eg. click-selecting elements) while the BOQ is open.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Windows;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    internal class BOQCostManagerWindow : Window
    {
        private static BOQCostManagerWindow _current;
        public BOQCostManagerPanel Panel { get; }

        public BOQCostManagerWindow(Document doc)
        {
            Title = "STING — BOQ & Cost Manager";
            Width = 1260; Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Panel = new BOQCostManagerPanel(doc);
            Content = Panel;
            Closed += (s, e) => { if (ReferenceEquals(_current, this)) _current = null; };
        }

        /// <summary>
        /// Show the BOQ window. If it's already open for the same document the
        /// existing window is activated; otherwise a fresh one is created.
        /// </summary>
        public static void ShowFor(Document doc)
        {
            if (doc == null) return;
            try
            {
                if (_current != null && ReferenceEquals(_current.Panel?.Doc, doc))
                {
                    _current.Activate();
                    _current.Panel?.RefreshAsync();
                    return;
                }
                if (_current != null)
                {
                    try { _current.Close(); } catch (Exception ex) { StingLog.Warn($"BOQ window close: {ex.Message}"); }
                }
                var w = new BOQCostManagerWindow(doc);
                _current = w;
                var helper = new System.Windows.Interop.WindowInteropHelper(w);
                try { helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
                catch (Exception ex) { StingLog.Warn($"BOQ window owner: {ex.Message}"); }
                w.Show();
            }
            catch (Exception ex) { StingLog.Error("BOQCostManagerWindow.ShowFor", ex); }
        }
    }
}
