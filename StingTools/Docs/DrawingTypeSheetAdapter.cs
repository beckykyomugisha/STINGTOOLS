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
            if (doc == null || dt == null || string.IsNullOrWhiteSpace(dt.TitleBlockFamily))
                return ElementId.InvalidElementId;
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                    if (el is FamilySymbol fs
                        && string.Equals(fs.FamilyName, dt.TitleBlockFamily,
                                         StringComparison.OrdinalIgnoreCase))
                        return fs.Id;
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
            try
            {
                DrawingTypeStamper.Stamp(sheet, dt.Id);
            }
            catch (Exception ex)
            {
                warnings?.Add("Stamp: " + ex.Message);
            }

            try
            {
                var tokens = BuildDefaultTokens(sheet, dt);
                var r = TitleBlockParamApplier.Apply(doc, sheet, dt, tokens);
                if (warnings != null) foreach (var w in r.Warnings) warnings.Add("TitleBlockParams: " + w);
            }
            catch (Exception ex)
            {
                warnings?.Add("TitleBlockParams: " + ex.Message);
            }
        }

        /// <summary>
        /// Best-effort token dict for the sheet-manager path: disc /
        /// discipline pulled from the profile, lvl / mark left empty
        /// (the sheet-manager command does not know which level or
        /// section mark this produces), seq formatted from the sheet
        /// number's trailing digit run when present.
        /// </summary>
        private static Dictionary<string, string> BuildDefaultTokens(ViewSheet sheet, DrawingType dt)
        {
            var disc       = dt.Discipline ?? "";
            var discipline = dt.Discipline ?? "";
            var num = sheet.SheetNumber ?? "";
            var seq = "";
            for (int i = num.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(num[i])) seq = num[i] + seq;
                else if (seq.Length > 0) break;
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "disc",       disc },
                { "discipline", discipline },
                { "seq",        seq },
                { "spool",      "" },
                { "sys",        "" },
                { "lvl",        "" },
                { "mark",       "" },
            };
        }
    }
}
