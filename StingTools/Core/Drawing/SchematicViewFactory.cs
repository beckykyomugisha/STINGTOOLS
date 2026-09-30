// StingTools — Drawing Template Manager
//
// SchematicViewFactory — the one way a schematic generator gets its drafting view.
//
// Five generators (fire alarm, earthing, LPS, MGPS, panel door) each had their own
// CreateDraftingView: ViewDrafting.Create, then `try { v.Name = name; } catch { Warn }`.
// A second run hit the name the first run had taken, so the rename threw, the view
// stayed "Drafting 1", and the first run's view was never touched — every re-run
// left another orphan view in the project, and the report named a view nobody
// could find. Here, as SLD_RiserDiagram already did: a view of the same name is
// reused and cleared (so its sheet placement survives), and a new view that cannot
// take its name is an error the caller reports — never a silently default-named view.
//
// Also: BoardNames — one reading of a board's name (Panel Name, else element name)
// for every command that names, filters or keys a sheet by board.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    internal static class SchematicViewFactory
    {
        /// <summary>
        /// The drafting view named <paramref name="name"/>, ready to draw into: an existing
        /// one is emptied and reused, else a new one is made and named. Scale is set to 1:1
        /// (the diagrams are drawn in paper millimetres with paper-sized text). Returns null
        /// with <paramref name="error"/> set when no view can be had under that name.
        /// Requires an open transaction.
        /// </summary>
        internal static ViewDrafting CreateOrReplace(Document doc, string name, out string error, int scale = 1)
        {
            error = null;
            if (doc == null) { error = "No document."; return null; }
            if (string.IsNullOrWhiteSpace(name)) { error = "No view name was given."; return null; }
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                    .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // Reuse, cleared: drawing into it as-is would stack a second copy of the
                    // diagram on the first. Its viewport (if any) stays on its sheet.
                    Clear(doc, existing, name);
                    SetScale(existing, scale, name);
                    return existing;
                }

                // Any other view already holding the name (a drafting view is not the only
                // kind) blocks the rename; say so rather than leave "Drafting 1".
                var clash = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
                if (clash != null)
                {
                    error = $"A {clash.ViewType} view is already named '{name}' — rename or delete it and run again.";
                    return null;
                }

                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
                if (vft == null) { error = "No Drafting view family type in the project."; return null; }

                var v = ViewDrafting.Create(doc, vft.Id);
                try { v.Name = name; }
                catch (Exception ex)
                {
                    // An unnamed view is one nobody can find and the next run cannot reuse.
                    error = $"The new drafting view could not be named '{name}': {ex.Message}";
                    StingLog.Warn($"SchematicViewFactory: {error}");
                    try { doc.Delete(v.Id); }
                    catch (Exception exDel) { StingLog.Warn($"SchematicViewFactory: remove unnamed view: {exDel.Message}"); }
                    return null;
                }
                SetScale(v, scale, name);
                return v;
            }
            catch (Exception ex)
            {
                error = $"Could not make the drafting view '{name}': {ex.Message}";
                StingLog.Error("SchematicViewFactory.CreateOrReplace", ex);
                return null;
            }
        }

        private static void Clear(Document doc, ViewDrafting view, string name)
        {
            try
            {
                var owned = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
                    .ToElementIds().Where(id => id != view.Id).ToList();
                if (owned.Count > 0) doc.Delete(owned);
            }
            catch (Exception ex) { StingLog.Warn($"SchematicViewFactory: clear '{name}': {ex.Message}"); }
        }

        private static void SetScale(View v, int scale, string name)
        {
            if (scale <= 0) return;
            try { if (v.Scale != scale) v.Scale = scale; }
            catch (Exception ex) { StingLog.Warn($"SchematicViewFactory: scale of '{name}': {ex.Message}"); }
        }
    }

    /// <summary>A board's name as every panel command reads it.</summary>
    internal static class BoardNames
    {
        /// <summary>
        /// The board's Panel Name (RBS_ELEC_PANEL_NAME — what a circuit's "Panel" field
        /// reads), else its element name. Not the family type name: filtering a circuit
        /// schedule on that gave an empty schedule.
        /// </summary>
        internal static string Of(Element board)
        {
            if (board == null) return "";
            string panelName = null;
            try { panelName = board.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString(); }
            catch (Exception ex) { StingLog.Warn($"Board name {board.Id}: {ex.Message}"); }
            string elementName = null;
            try { elementName = board.Name; }
            catch (Exception ex) { StingLog.Warn($"Board element name {board.Id}: {ex.Message}"); }
            return BoardNaming.Resolve(panelName, elementName, board.Id.Value);
        }
    }
}
