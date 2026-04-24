// StingTools v4 MVP — ShopDrawingComposer.
//
// Creates a ViewSheet using the discipline-specific title block,
// places the 5 views from AssemblyViewSet at fixed slot positions
// and the BOM schedule via ScheduleSheetInstance.Create. Title-block
// parameters are populated from the assembly's spool metadata.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Fabrication
{
    public static class ShopDrawingComposer
    {
        // Title-block family names per discipline. Stub families live
        // under Families/AssemblyTitleBlocks/ — see S5.15 for the
        // parameter list each family must expose.
        private static readonly Dictionary<string, string> TitleBlockByDiscipline =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pipe",       "STING_TB_ASSEMBLY_PIPE" },
            { "Plumbing",   "STING_TB_ASSEMBLY_PIPE" },
            { "Duct",       "STING_TB_ASSEMBLY_DUCT" },
            { "HVAC",       "STING_TB_ASSEMBLY_DUCT" },
            { "Electrical", "STING_TB_ASSEMBLY_COND" },
            { "Hanger",     "STING_TB_ASSEMBLY_HANGER" },
            { "Generic",    "STING_TB_ASSEMBLY_PIPE" }
        };

        // Discipline code used when assembling the SP-{disc}-{sys}-{lvl}-{seq}
        // sheet number. Matches ISO 19650 discipline single-letter codes.
        private static readonly Dictionary<string, string> DisciplineCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Plumbing",   "P"  },
            { "Pipe",       "P"  },
            { "HVAC",       "M"  },
            { "Duct",       "M"  },
            { "Electrical", "E"  },
            { "Hanger",     "HG" },
            { "Generic",    "G"  }
        };

        // Session-scoped sequence per (discipline, level) bucket — ensures
        // unique SP-M-HVAC-L02-0003 even when the assembly has no spool
        // number yet (Phase A fallback; Phase B will hydrate from a
        // persistent doc_sequences.json keyed per project).
        private static readonly Dictionary<string, int> _sequenceByBucket
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Slot positions on a 1:50 A1 sheet (Revit feet, sheet origin).
        // Plan TL, ISO TR, Elev0 BL, Elev90 ML, 3D BR, BOM RIGHT-PANEL.
        private static readonly Dictionary<string, XYZ> SlotPositions =
            new Dictionary<string, XYZ>
        {
            { "PLAN",       new XYZ( 0.20, 1.95, 0) },
            { "ISO",        new XYZ( 1.55, 1.95, 0) },
            { "ELEV0",      new XYZ( 0.20, 1.20, 0) },
            { "ELEV90",     new XYZ( 1.05, 1.20, 0) },
            { "3D",         new XYZ( 1.55, 1.20, 0) },
            { "BOM",        new XYZ( 2.20, 1.95, 0) }
        };

        public static ElementId ComposeSheet(
            Document doc,
            string discipline,
            ElementId assemblyId,
            AssemblyViewSet views,
            FabricationResult result)
        {
            if (doc == null || assemblyId == null || views == null) return null;

            // Phase I options: user-picked title block and view template
            // flow through via FabricationOptions.ShopDrawing. When Auto
            // (ElementId.InvalidElementId), fall back to the per-
            // discipline STING_TB_ASSEMBLY_* lookup.
            var options = StingTools.Commands.Fabrication.FabricationOptions.ShopDrawing;
            ElementId tbId = (options != null && options.TitleBlockSymbolId != ElementId.InvalidElementId)
                ? options.TitleBlockSymbolId
                : ResolveTitleBlock(doc, discipline);

            ViewSheet sheet = null;
            try
            {
                sheet = ViewSheet.Create(doc, tbId);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ShopDrawingComposer: ViewSheet.Create failed: {ex.Message}");
                return null;
            }
            if (sheet == null) return null;

            try { ApplySheetMetadata(doc, sheet, assemblyId, discipline, result); }
            catch (Exception ex) { result.Warnings.Add($"Sheet metadata: {ex.Message}"); }

            // Apply user-selected view template to every non-schedule
            // view the sheet will host, so the shop drawing inherits
            // the company's graphic standards (line weights, filled
            // regions, VG overrides, cropping rules).
            if (options != null && options.ViewTemplateId != ElementId.InvalidElementId)
            {
                foreach (var viewId in new[] {
                    views.View3D, views.ViewPlan, views.ViewIso6412,
                    views.Elevation0, views.Elevation90, views.ElevationTop })
                {
                    ApplyViewTemplate(doc, viewId, options.ViewTemplateId, result);
                }
            }

            // Place views at fixed slots. Elev0/Elev90 receive the new
            // ElevationFront / ElevationLeft views from AssemblyViewUtils
            // (fixed by the Phase A swap). ElevationTop reuses the plan
            // slot when no native plan/part-list was created.
            PlaceView(doc, sheet,
                      views.ViewPlan != ElementId.InvalidElementId
                          ? views.ViewPlan
                          : views.ElevationTop,
                      SlotPositions["PLAN"],   result);
            PlaceView(doc, sheet, views.ViewIso6412, SlotPositions["ISO"],    result);
            PlaceView(doc, sheet, views.Elevation0,  SlotPositions["ELEV0"],  result);
            PlaceView(doc, sheet, views.Elevation90, SlotPositions["ELEV90"], result);
            PlaceView(doc, sheet, views.View3D,      SlotPositions["3D"],     result);

            // Place BOM schedule
            try
            {
                if (views.BomSchedule != null && views.BomSchedule != ElementId.InvalidElementId)
                {
                    ScheduleSheetInstance.Create(doc, sheet.Id, views.BomSchedule,
                        SlotPositions["BOM"]);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"BOM placement: {ex.Message}"); }

            return sheet.Id;
        }

        private static void PlaceView(Document doc, ViewSheet sheet, ElementId viewId, XYZ pos,
            FabricationResult result)
        {
            if (viewId == null || viewId == ElementId.InvalidElementId) return;
            try
            {
                if (Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                    Viewport.Create(doc, sheet.Id, viewId, pos);
                else
                    result.Warnings.Add($"View {viewId.Value} cannot be placed on sheet {sheet.SheetNumber}");
            }
            catch (Exception ex) { result.Warnings.Add($"PlaceView {viewId.Value}: {ex.Message}"); }
        }

        private static ElementId ResolveTitleBlock(Document doc, string discipline)
        {
            string familyName = TitleBlockByDiscipline.TryGetValue(discipline ?? "", out var n)
                ? n : TitleBlockByDiscipline["Generic"];
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                {
                    if (el is FamilySymbol fs && string.Equals(fs.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                        return fs.Id;
                }
                // Fallback: first available title block
                foreach (var el in col)
                    if (el is FamilySymbol fs) return fs.Id;
            }
            catch (Exception ex) { StingLog.Warn($"ShopDrawingComposer: title block resolve: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static void ApplySheetMetadata(Document doc, ViewSheet sheet, ElementId assemblyId,
            string discipline, FabricationResult result)
        {
            var ai = doc.GetElement(assemblyId) as AssemblyInstance;
            if (ai == null) return;

            // Sheet number follows the SP-{disc}-{sys}-{lvl}-{seq} pattern
            // so every shop drawing has a unique, parseable identifier.
            // If the assembly carries a spool number (AssyParams.SPOOL_NR_TXT)
            // we use that verbatim — it was minted by AssemblyBuilder and
            // already respects the same convention. Otherwise we compose
            // one here and bump a session-scoped counter.
            string spool = ReadString(ai, AssyParams.SPOOL_NR_TXT);
            string systemCode = ReadString(ai, "ASS_SYSTEM_TYPE_TXT");
            string levelCode  = ReadString(ai, "ASS_LVL_COD_TXT");
            if (string.IsNullOrEmpty(levelCode)) levelCode = "XX";

            // Resolve sequence up-front (used by pattern substitution).
            string discCode = DisciplineCode.TryGetValue(discipline ?? "", out var dc) ? dc : "G";
            string sysCode  = string.IsNullOrEmpty(systemCode) ? "GEN" : Sanitise(systemCode);
            string bucket   = $"{discCode}:{sysCode}:{levelCode}";
            int seq;
            lock (_sequenceByBucket)
            {
                _sequenceByBucket.TryGetValue(bucket, out seq);
                seq += 1;
                _sequenceByBucket[bucket] = seq;
            }

            // Honour the user pattern when the ShopDrawingOptionsDialog
            // captured one, otherwise fall back to the spool number
            // (if minted by AssemblyBuilder) or the default
            // SP-{disc}-{sys}-{lvl}-{seq} pattern.
            var options = StingTools.Commands.Fabrication.FabricationOptions.ShopDrawing;
            string sheetNumber = !string.IsNullOrEmpty(options?.SheetNumberPattern)
                ? SubstituteTokens(options.SheetNumberPattern, spool, discCode, sysCode, levelCode, seq)
                : (!string.IsNullOrEmpty(spool)
                    ? spool
                    : $"SP-{discCode}-{sysCode}-{levelCode}-{seq:D4}");

            // Revit throws when two sheets share a number. Uniquify with
            // a monotonic suffix as a last resort.
            string unique = EnsureUniqueSheetNumber(doc, sheetNumber);
            try { sheet.SheetNumber = unique; }
            catch (Exception ex)
            { result.Warnings.Add($"SheetNumber assign ('{unique}'): {ex.Message}"); }

            string sheetName = !string.IsNullOrEmpty(options?.SheetNamePattern)
                ? SubstituteTokens(options.SheetNamePattern, spool, discCode, sysCode, levelCode, seq)
                : (!string.IsNullOrEmpty(spool)
                    ? $"Spool {spool}"
                    : $"{discipline} spool {unique}");
            try { sheet.Name = sheetName; } catch { }

            // Title-block instance parameters live on the title block
            // FamilyInstance, accessible via collector. We set the
            // common ones if present.
            try
            {
                var tbInst = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
                if (tbInst != null)
                {
                    TrySetString(tbInst, AssyParams.SPOOL_NR_TXT,    spool);
                    TrySetString(tbInst, AssyParams.WEIGHT_KG,       ReadString(ai, AssyParams.WEIGHT_KG));
                    TrySetString(tbInst, AssyParams.FAB_LOC_TXT,     ReadString(ai, AssyParams.FAB_LOC_TXT));
                    TrySetString(tbInst, AssyParams.FAB_STATUS_TXT,  ReadString(ai, AssyParams.FAB_STATUS_TXT));
                    TrySetString(tbInst, AssyParams.BOM_REV_TXT,     ReadString(ai, AssyParams.BOM_REV_TXT));
                    TrySetString(tbInst, "DISCIPLINE",               discipline);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Title block populate: {ex.Message}"); }
        }

        private static string ReadString(Element el, string param)
        {
            try { return el?.LookupParameter(param)?.AsString() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// Substitute {spool}/{disc}/{sys}/{lvl}/{seq} tokens in the
        /// user-supplied sheet-number or sheet-name pattern.
        /// Unrecognised tokens are left as literal text so shops can
        /// mix in extra markers like "REV {revision}" by hand later.
        /// </summary>
        private static string SubstituteTokens(string pattern, string spool,
            string disc, string sys, string lvl, int seq)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            return pattern
                .Replace("{spool}", spool ?? "")
                .Replace("{disc}",  disc  ?? "")
                .Replace("{sys}",   sys   ?? "")
                .Replace("{lvl}",   lvl   ?? "")
                .Replace("{seq}",   seq.ToString("D4"));
        }

        /// <summary>
        /// Apply the user-picked view template to the view if the
        /// template permits it for that view type. View3D / section /
        /// plan all accept a template via View.ViewTemplateId.
        /// </summary>
        private static void ApplyViewTemplate(Document doc, ElementId viewId,
            ElementId templateId, FabricationResult result)
        {
            if (viewId == null || viewId == ElementId.InvalidElementId) return;
            if (templateId == null || templateId == ElementId.InvalidElementId) return;
            try
            {
                var v = doc.GetElement(viewId) as View;
                if (v == null || v.IsTemplate) return;
                v.ViewTemplateId = templateId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ApplyViewTemplate {viewId.Value}: {ex.Message}");
            }
        }

        /// <summary>
        /// Strip characters Revit refuses in sheet numbers (\ / : * ?
        /// " &lt; &gt; | {} [] ;) and collapse whitespace to a single
        /// underscore. Preserves A-Z / 0-9 / - / _.
        /// </summary>
        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var chars = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') chars.Append(ch);
                else if (char.IsWhiteSpace(ch)) chars.Append('_');
            }
            return chars.ToString();
        }

        /// <summary>
        /// Probe the document for an existing sheet with the proposed
        /// number and, if found, append -A, -B, … until unique.
        /// </summary>
        private static string EnsureUniqueSheetNumber(Document doc, string baseNumber)
        {
            if (string.IsNullOrEmpty(baseNumber)) return baseNumber;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)))
                {
                    if (el is ViewSheet vs && !string.IsNullOrEmpty(vs.SheetNumber))
                        existing.Add(vs.SheetNumber);
                }
            }
            catch { return baseNumber; }
            if (!existing.Contains(baseNumber)) return baseNumber;
            for (char c = 'A'; c <= 'Z'; c++)
            {
                var candidate = baseNumber + "-" + c;
                if (!existing.Contains(candidate)) return candidate;
            }
            // Final fallback: long random suffix.
            return baseNumber + "-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        }
        private static void TrySetString(Element el, string param, string val)
        {
            try
            {
                var p = el.LookupParameter(param);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(val);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ShopDrawingComposer.TrySetString({param}) on {el?.Id}: {ex.Message}");
            }
        }
    }
}
