using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// N+3 — Lightweight hook called by <see cref="StingStaleMarker"/>
    /// when a material change is detected on a tagged element.
    ///
    /// Today it just emits an audit log entry so the change is
    /// traceable (via the existing Planscape audit log infrastructure
    /// the UI layer wraps in MaterialAuditLogger). Future commits can
    /// extend this hook to:
    ///   • bump a revision number on issued sheets carrying the
    ///     affected element
    ///   • enqueue a re-tag job on the StingIdlingScheduler
    ///   • push an entry into the BIM Coordination Center coord log
    ///
    /// Kept in StingTools.Core (not StingTools.UI) so the IUpdater
    /// remains free of UI-layer dependencies.
    /// </summary>
    public static class StingMaterialUpdaterStaleHook
    {
        public static void OnMaterialChanged(Document doc, ElementId elementId)
        {
            if (doc == null || elementId == null || elementId.Value <= 0) return;
            try
            {
                string elName = doc.GetElement(elementId)?.Name ?? "(unknown)";
                StingLog.Info($"MaterialChange: element {elementId.Value} '{elName}' material changed → tag + BOQ marked stale.");
                // The Planscape audit log lives in StingTools.UI to keep this
                // assembly's dependency surface small. Future wiring can route
                // through a lightweight interface; for now the IUpdater-side
                // info log is sufficient — the parameter change itself is
                // captured by Revit's own undo history.
            }
            catch (Exception ex) { StingLog.Warn($"OnMaterialChanged: {ex.Message}"); }
        }
    }
}
