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

        /// <summary>
        /// Paper size (mm) of the main slot of the drawing type <paramref name="req"/> routes
        /// to, and that type's scale (0 when it has none) — what a schematic generator fits
        /// its drawing into before it chooses a view scale. The slot is a fraction of the
        /// title block family's drawable rect (STING_TITLE_BLOCKS.json), as placement uses;
        /// a family with no drawable rect falls back to the ISO paper size less 25 mm
        /// margins, as placement does. False, with <paramref name="note"/>, when no size can
        /// be had — the caller then draws without a fit check and says so.
        /// </summary>
        internal static bool TryGetSlotPaperSize(Document doc, DrawingRouteRequest req,
            out double widthMm, out double heightMm, out int typeScale, out string note)
        {
            widthMm = heightMm = 0; typeScale = 0; note = null;
            try
            {
                var dt = req == null ? null : DrawingRouteResolver.Resolve(doc, req);
                if (dt == null) { note = "no drawing type is routed for this schematic"; return false; }
                typeScale = dt.Scale;

                double drawW = 0, drawH = 0;
                try
                {
                    var lib = TitleBlockSpecRegistry.Load();
                    var spec = lib?.Families?.FirstOrDefault(f => !f.Abstract
                        && string.Equals(f.Id, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase));
                    var drawable = spec == null ? null : TitleBlockSpecRegistry.Resolve(lib, spec)?.Drawable;
                    if (drawable != null && drawable.W > 0 && drawable.H > 0) { drawW = drawable.W; drawH = drawable.H; }
                }
                catch (Exception ex) { StingLog.Warn($"SchematicViewFactory: drawable of '{dt.TitleBlockFamily}': {ex.Message}"); }

                if (drawW <= 0 || drawH <= 0)
                {
                    double pw, ph;
                    switch ((dt.PaperSize ?? "").Trim().ToUpperInvariant())
                    {
                        case "A0": pw = 1189; ph = 841; break;
                        case "A1": pw = 841;  ph = 594; break;
                        case "A2": pw = 594;  ph = 420; break;
                        case "A3": pw = 420;  ph = 297; break;
                        case "A4": pw = 297;  ph = 210; break;
                        default:
                            note = $"drawing type '{dt.Id}' has no drawable rect and no ISO paper size ('{dt.PaperSize}')";
                            return false;
                    }
                    if (string.Equals((dt.Orientation ?? "").Trim(), "Portrait", StringComparison.OrdinalIgnoreCase))
                    { var t = pw; pw = ph; ph = t; }
                    drawW = pw - 50; drawH = ph - 50;
                }

                var slot = dt.Slots?.FirstOrDefault();
                double fw = slot != null && slot.NormW > 0 ? slot.NormW : 1.0;
                double fh = slot != null && slot.NormH > 0 ? slot.NormH : 1.0;
                widthMm = drawW * fw;
                heightMm = drawH * fh;
                return widthMm > 0 && heightMm > 0;
            }
            catch (Exception ex)
            {
                note = ex.Message;
                StingLog.Warn($"SchematicViewFactory.TryGetSlotPaperSize: {ex.Message}");
                return false;
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
