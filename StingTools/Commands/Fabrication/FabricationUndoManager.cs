// StingTools v4 MVP — FabricationUndoManager.
//
// Session-scoped undo stack for Generate Fabrication Package runs.
// FabricationEngine returns a FabricationResult carrying the ElementIds
// of every AssemblyInstance, view, and ViewSheet it created. Recording
// that result here lets users roll the package back in one click
// without leaving orphan sheets / views behind.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;

namespace StingTools.Commands.Fabrication
{
    /// <summary>
    /// In-process LIFO history of FabricationResult instances. Survives
    /// command invocations but not Revit restarts — matches the scope
    /// of Revit's built-in undo buffer.
    /// </summary>
    public static class FabricationUndoManager
    {
        private static readonly Stack<FabricationResult> _history
            = new Stack<FabricationResult>();

        /// <summary>Number of undo entries currently available.</summary>
        public static int Depth => _history.Count;

        /// <summary>True when Undo() would have something to delete.</summary>
        public static bool HasHistory => _history.Count > 0;

        /// <summary>
        /// Push a FabricationResult onto the undo stack. Called by
        /// GenerateFabPackageCommand immediately after the engine returns.
        /// </summary>
        public static void Record(FabricationResult result)
        {
            if (result == null) return;
            if (result.AssemblyIds.Count == 0 && result.SheetIds.Count == 0) return;
            _history.Push(result);
        }

        /// <summary>
        /// Peek the most recent FabricationResult without popping.
        /// </summary>
        public static FabricationResult Peek()
        {
            return _history.Count == 0 ? null : _history.Peek();
        }

        /// <summary>Clear the entire history without deleting any elements.</summary>
        public static void Clear()
        {
            _history.Clear();
        }

        /// <summary>
        /// Pop the last FabricationResult and delete its sheets + assemblies
        /// inside a single transaction. Returns the number of elements
        /// actually removed (0 when nothing to undo).
        /// </summary>
        public static int Undo(Document doc)
        {
            if (doc == null || _history.Count == 0) return 0;
            FabricationResult last = _history.Pop();

            // Sheets first so viewports disappear cleanly, then assemblies.
            var ids = new List<ElementId>();
            ids.AddRange(last.SheetIds);
            ids.AddRange(last.AssemblyIds);
            if (ids.Count == 0) return 0;

            int deleted = 0;
            using (var t = new Transaction(doc, "STING Undo Fabrication Package"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    try
                    {
                        if (id == null || id == ElementId.InvalidElementId) continue;
                        if (doc.GetElement(id) == null) continue;
                        var removed = doc.Delete(id);
                        if (removed != null) deleted += removed.Count;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"FabricationUndoManager.Undo({id?.Value}): {ex.Message}");
                    }
                }
                t.Commit();
            }
            return deleted;
        }
    }

    /// <summary>
    /// Roll back the most recent fabrication package created in this session.
    /// Bound to the Fabrication sub-tab's "Undo Package" button.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UndoFabPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!FabricationUndoManager.HasHistory)
            {
                TaskDialog.Show("STING v4 — Undo Fabrication Package",
                    "No fabrication package on the session undo stack.\n\n" +
                    "Run Generate Fabrication Package first — the result is\n" +
                    "recorded automatically on success.");
                return Result.Cancelled;
            }

            FabricationResult target = FabricationUndoManager.Peek();
            int expected = (target?.AssemblyIds.Count ?? 0) + (target?.SheetIds.Count ?? 0);

            int deleted = FabricationUndoManager.Undo(ctx.Doc);
            TaskDialog.Show("STING v4 — Undo Fabrication Package",
                $"Removed {deleted} element(s) (of {expected} tracked).\n" +
                $"History depth now: {FabricationUndoManager.Depth}.");
            return Result.Succeeded;
        }
    }
}
