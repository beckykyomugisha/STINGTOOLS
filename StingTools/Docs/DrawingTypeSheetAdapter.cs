// StingTools — Drawing Template Manager · (1) SheetManager integration
//
// DrawingTypeSheetAdapter lets SheetManager.CreateFromTemplateCommand
// consume DrawingType profiles alongside the historic SheetTemplate
// library. The adapter translates a DrawingType into the existing
// SheetTemplate shape (same normalised slot coords, same sheet
// number / name patterns) so the proven SheetTemplateEngine.
// CreateSheetFromTemplate pipeline handles the actual creation —
// zero-risk integration: no new view-placement code, only a shape
// converter on the way in and a post-create stamp on the way out.
//
// Post-create steps layered on:
//   * STING_DRAWING_TYPE_ID_TXT stamp (Week 3)
//   * TitleBlockParamApplier       (Bonus 4)
//
// Result: pressing "Create From Template" and picking a DrawingType
// produces a sheet with corporate title-block cells populated,
// source profile recorded, ready for the browser organizer to group
// under the right node.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;

namespace StingTools.Docs
{
    internal static class DrawingTypeSheetAdapter
    {
        /// <summary>
        /// Convert a resolved DrawingType into a SheetTemplate. Slot
        /// normalised coords pass through verbatim — both use the
        /// 0..1-over-drawable-zone convention.
        /// </summary>
        public static SheetTemplate ToSheetTemplate(DrawingType dt)
        {
            if (dt == null) return null;
            var st = new SheetTemplate
            {
                Name                = dt.Name ?? dt.Id,
                Description         = dt.Description,
                Discipline          = dt.Discipline,
                PaperSize           = dt.PaperSize,
                TitleBlockFamily    = dt.TitleBlockFamily,
                SheetNumberPattern  = dt.SheetNumberPattern,
                SheetNamePattern    = dt.SheetNamePattern,
                Created             = DateTime.Now.ToString("yyyy-MM-dd"),
                ViewportSlots       = new List<TemplateViewSlot>(),
            };
            if (dt.Slots != null)
            {
                foreach (var s in dt.Slots)
                {
                    st.ViewportSlots.Add(new TemplateViewSlot
                    {
                        Label            = s.Label,
                        ViewType         = s.ViewType,
                        NormX            = s.NormX,
                        NormY            = s.NormY,
                        NormW            = s.NormW,
                        NormH            = s.NormH,
                        PreferredScale   = s.Scale ?? dt.Scale,
                        ViewportTypeName = s.ViewportType ?? dt.ViewportTypeName,
                        Required         = s.Required,
                    });
                }
            }
            return st;
        }

        /// <summary>
        /// Resolve the title-block ElementId declared by the profile.
        /// Returns InvalidElementId when the family is not loaded so
        /// the caller's existing picker can ask the user to choose.
        /// </summary>
        public static ElementId ResolveTitleBlock(Document doc, DrawingType dt)
        {
            if (doc == null || dt == null) return ElementId.InvalidElementId;
            var (tbFamily, tbSymbol) = DrawingDispatcher.ResolveTitleBlockVariant(dt);
            if (string.IsNullOrWhiteSpace(tbFamily)) return ElementId.InvalidElementId;
            try
            {
                var matches = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => string.Equals(fs.FamilyName, tbFamily,
                                               StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!string.IsNullOrWhiteSpace(tbSymbol))
                {
                    var picked = matches.FirstOrDefault(fs =>
                        string.Equals(fs.Name, tbSymbol,
                                      StringComparison.OrdinalIgnoreCase));
                    if (picked != null) return picked.Id;
                }
                var fallback = matches.FirstOrDefault();
                if (fallback != null) return fallback.Id;
            }
            catch { /* fall through */ }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// After SheetTemplateEngine.CreateSheetFromTemplate has built
        /// the sheet, stamp the DrawingType id and run the title-block
        /// parameter binding. Caller holds an active Transaction.
        /// </summary>
        public static void PostCreate(Document doc, ViewSheet sheet, DrawingType dt, List<string> warnings)
        {
            if (doc == null || sheet == null || dt == null) return;
            // INT-05: route through the canonical sheet-apply so future
            // callers (production-rule engine, fabrication composer) all
            // share the same stamp + title-block pipeline.
            try
            {
                var tokens = BuildDefaultTokens(doc, sheet, dt);
                var r = DrawingTypePresentation.ApplyToSheet(doc, sheet, dt, tokens);
                if (warnings != null) foreach (var w in r.Warnings) warnings.Add(w);
            }
            catch (Exception ex)
            {
                warnings?.Add("ApplyToSheet: " + ex.Message);
            }
        }

        /// <summary>
        /// INT-06 + FG-08: token dict for the sheet-manager path now
        /// goes through the canonical <see cref="DrawingTokenContext"/>
        /// builder, so the same profile produces the same title-block
        /// cell values regardless of whether the operator pressed
        /// "Create From Template" or invoked the fabrication composer.
        /// ISO 19650 codes from <c>dt.IsoNaming</c> flow through
        /// transparently — the SheetManager never had to know about
        /// them before, but the title-block cells expect them.
        /// </summary>
        private static Dictionary<string, string> BuildDefaultTokens(Document doc, ViewSheet sheet, DrawingType dt)
        {
            var seq = DrawingTokenContext.ExtractSeqFromSheetNumber(sheet?.SheetNumber);
            return DrawingTokenContext.Build(
                doc: doc,
                dt: dt,
                discCode:   dt?.Discipline,
                discipline: dt?.Discipline,
                seq:        seq);
        }
    }
}
