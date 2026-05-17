// StingTools v4 MVP — ShopDrawingComposer.
//
// Creates a ViewSheet using the discipline-specific title block,
// places the 5 views from AssemblyViewSet at fixed slot positions
// and the BOM schedule via ScheduleSheetInstance.Create. Title-block
// parameters are populated from the assembly's spool metadata.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Drawing;
using System.Text.RegularExpressions;

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
        // GAP-B: per-document sequence bucket. Previously a single static
        // dict was shared across every open document, so sheet numbers
        // could collide / overshoot on the second project opened in the
        // same session. Outer key is the document path; inner is the
        // disc:sys:lvl bucket the original code already used.
        private static readonly Dictionary<string, Dictionary<string, int>> _sequenceByBucket
            = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        private static string DocBucketKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        private static Dictionary<string, int> GetDocBucket(Document doc)
        {
            string k = DocBucketKey(doc);
            lock (_sequenceByBucket)
            {
                if (!_sequenceByBucket.TryGetValue(k, out var inner))
                    _sequenceByBucket[k] = inner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                return inner;
            }
        }

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
            // (ElementId.InvalidElementId), consult the Drawing Type
            // registry next (disc-specific "pipe-spool-A1-1to50" /
            // "duct-spool-A1-1to50" profile) — if it names a title block
            // family loaded in the project we use that; otherwise fall
            // back to the per-discipline STING_TB_ASSEMBLY_* lookup.
            // The three-tier resolution keeps today's behaviour as a
            // last resort, so landing this wiring is zero-regression.
            var options = StingTools.Commands.Fabrication.FabricationOptions.ShopDrawing;
            var drawingType = ResolveDrawingType(doc, discipline);
            ElementId tbId;
            if (options != null && options.TitleBlockSymbolId != ElementId.InvalidElementId)
            {
                tbId = options.TitleBlockSymbolId;
            }
            else
            {
                // 1) DrawingType profile (corporate spool A1 etc.)
                tbId = ResolveTitleBlockFromDrawingType(doc, drawingType);
                // 2) Project Setup Wizard router — single source of truth
                //    populated by ProjectSetupCommand. Honoured here so the
                //    fabrication path obeys the same per-discipline policy
                //    as DocAutomation / SheetManager / BatchCreateSheets.
                if (tbId == ElementId.InvalidElementId)
                {
                    string discCode = DisciplineCode.TryGetValue(discipline ?? "", out var dc)
                        ? dc : "G";
                    var routed = StingTools.Core.TitleBlockRouter.Resolve(doc, discCode);
                    if (routed != null) tbId = routed.Id;
                }
                // 3) Per-discipline STING_TB_ASSEMBLY_* hard-code as last resort.
                // Gap-6: when we reach this tier, the profile family (if any) was
                // not found — the discipline-dict name is the "expected" value and
                // whatever ResolveTitleBlock picks is the "used" value.
                if (tbId == ElementId.InvalidElementId)
                    tbId = ResolveTitleBlock(doc, discipline, result);
            }

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

            try { ApplySheetMetadata(doc, sheet, assemblyId, discipline, drawingType, result); }
            catch (Exception ex) { result.Warnings.Add($"Sheet metadata: {ex.Message}"); }

            // Apply the resolved DrawingType profile to every non-schedule
            // view so spools render at the slot's intended scale. Without
            // this, AssemblyViewUtils-created views keep the
            // ViewFamilyType default (~1:100) and print half-size on a
            // layout sized for 1:50. Schedules / material takeoffs have
            // no Scale property and are intentionally excluded.
            //
            // runAnnotation: false — fabrication views handle their own
            // annotation via IsoSymbolPlacer; we only want the
            // presentation pass (scale, detail level, view template,
            // crop, style pack).
            // Map each fabrication view to the DrawingSlot that hosts it
            // so per-slot Scale / DetailLevel / ViewTemplate overrides
            // declared in the editor can land on the right view. Detail
                // callout slots (e.g. 1:20) can sit on the same sheet as the
                // 1:50 spool overview without the author having to clone the
                // whole DrawingType.
            var viewSlotMap = new (ElementId ViewId, string SlotLabel)[]
            {
                (views.ViewPlan,    "Plan"),
                (views.View3D,      "3D"),
                (views.ViewIso6412, "ISO"),
                (views.Elevation0,  "Elev0"),
                (views.Elevation90, "Elev90"),
                (views.ElevationTop,"ElevTop"),
            };
            var viewIdsToPresent = viewSlotMap.Select(t => t.ViewId).ToArray();

            if (drawingType != null)
            {
                foreach (var (viewId, slotLabel) in viewSlotMap)
                {
                    if (viewId == null || viewId == ElementId.InvalidElementId) continue;
                    try
                    {
                        if (doc.GetElement(viewId) is View v && !v.IsTemplate)
                        {
                            var apply = StingTools.Core.Drawing.DrawingTypePresentation
                                .Apply(doc, v, drawingType, runAnnotation: false);
                            foreach (var w in apply.Warnings)
                                result.Warnings.Add($"Apply view {viewId.Value}: {w}");

                            // Layer per-slot overrides on top of DrawingType
                            // defaults — finer scale on detail slots, fine
                            // detail level on the ISO callout, etc.
                            var slot = ResolveSlot(drawingType, slotLabel);
                            if (slot != null)
                            {
                                StingTools.Core.Drawing.DrawingTypePresentation
                                    .ApplySlotOverrides(doc, v, slot, apply);
                            }

                            // Phase 175 — auto-dim pass. Annotation was
                            // skipped above so fabrication keeps its own
                            // tag pipeline; here we explicitly run only
                            // dim + spot rules so the profile's
                            // dimensionStrategy (Ordinate on spools) lands
                            // on every dimensionable view. 3D views are
                            // skipped by every dimensioner.
                            RunFabricationDimPass(doc, v, drawingType, result);
                        }
                    }
                    catch (Exception ex2)
                    {
                        result.Warnings.Add($"DrawingType view apply {viewId.Value}: {ex.Message}");
                    }
                }
            }
            else
            {
                // No profile resolved — still guarantee a readable scale
                // so the slot layout (sized for 1:50 A1) isn't fed
                // default-scaled views.
                foreach (var viewId in viewIdsToPresent)
                    TrySetViewScale(doc, viewId, 50, result);
            }

            // User-picked view template (if any) wins over the profile —
            // applied last so corporate graphic standards override the
            // DrawingType.viewTemplateName fallback.
            if (options != null && options.ViewTemplateId != ElementId.InvalidElementId)
            {
                foreach (var viewId in viewIdsToPresent)
                    ApplyViewTemplate(doc, viewId, options.ViewTemplateId, result);
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

        private static ElementId ResolveTitleBlock(Document doc, string discipline, FabricationResult result)
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
                // Last-resort fallback: first available title block. Warn
                // loudly — populated cells (spool / weight / FAB_LOC / BOM
                // rev) may land on parameters this family doesn't carry.
                foreach (var el in col)
                {
                    if (el is FamilySymbol fs)
                    {
                        string actualFamily = fs.FamilyName;
                        string msg = $"Title block '{familyName}' not loaded; falling back to '{actualFamily}'. Populated fab cells may be silently dropped.";
                        StingLog.Warn("ShopDrawingComposer: " + msg);
                        result?.Warnings.Add(msg);
                        // Gap-6: record the discipline-dict fallback (sheet not yet created).
                        result?.TitleBlockFallbacks.Add((-1L, "(from discipline dict) " + familyName, actualFamily));
                        return fs.Id;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ShopDrawingComposer: title block resolve: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static void ApplySheetMetadata(Document doc, ViewSheet sheet, ElementId assemblyId,
            string discipline, StingTools.Core.Drawing.DrawingType drawingType, FabricationResult result)
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
            // GAP-B: per-document bucket — the outer lock prevents two
            // composer calls on the same doc from racing the increment.
            var docBucket = GetDocBucket(doc);
            lock (docBucket)
            {
                docBucket.TryGetValue(bucket, out seq);
                seq += 1;
                docBucket[bucket] = seq;
            }

            // Honour the user pattern when the ShopDrawingOptionsDialog
            // captured one; next, consult the resolved Drawing Type for
            // a corporate pattern; otherwise fall back to the spool
            // number (if minted by AssemblyBuilder) or the engine
            // default SP-{disc}-{sys}-{lvl}-{seq}.
            var options = StingTools.Commands.Fabrication.FabricationOptions.ShopDrawing;
            string registryNumPattern = drawingType?.SheetNumberPattern;
            var extraTokens = BuildTokenDict(doc, drawingType, spool, discCode, discipline, sysCode, levelCode, seq);
            string sheetNumber = !string.IsNullOrEmpty(options?.SheetNumberPattern)
                ? SubstituteTokens(options.SheetNumberPattern, spool, discCode, sysCode, levelCode, seq, discipline, extras: extraTokens)
                : !string.IsNullOrEmpty(registryNumPattern)
                    ? SubstituteTokens(registryNumPattern, spool, discCode, sysCode, levelCode, seq, discipline, extras: extraTokens)
                    : (!string.IsNullOrEmpty(spool)
                        ? spool
                        : $"SP-{discCode}-{sysCode}-{levelCode}-{seq:D4}");

            // Revit throws when two sheets share a number. Uniquify with
            // a monotonic suffix as a last resort.
            string unique = EnsureUniqueSheetNumber(doc, sheetNumber);
            try { sheet.SheetNumber = unique; }
            catch (Exception ex)
            { result.Warnings.Add($"SheetNumber assign ('{unique}'): {ex.Message}"); }

            string registryNamePattern = drawingType?.SheetNamePattern;
            string sheetName = !string.IsNullOrEmpty(options?.SheetNamePattern)
                ? SubstituteTokens(options.SheetNamePattern, spool, discCode, sysCode, levelCode, seq, discipline, extras: extraTokens)
                : !string.IsNullOrEmpty(registryNamePattern)
                    ? SubstituteTokens(registryNamePattern, spool, discCode, sysCode, levelCode, seq, discipline, extras: extraTokens)
                    : (!string.IsNullOrEmpty(spool)
                        ? $"Spool {spool}"
                        : $"{discipline} spool {unique}");
            try { sheet.Name = sheetName; } catch { }

            // Title-block instance parameters live on the title block
            // FamilyInstance, accessible via collector. We set the
            // fabrication-specific ones here — spool, weight, fab
            // location, status, BOM rev, discipline — then let the
            // Drawing Type's TitleBlockParams payload layer on top so
            // corporate profiles can declaratively bind more cells
            // (client name, suitability, drawn-by, project code) via
            // ${PRJ_ORG_*} and {disc}/{lvl}/{sys}/{seq:Dn} templates.
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

            // FIX-9: route through DrawingTypePresentation.ApplyToSheet so
            // the canonical lock check + DrawingType stamp + Package stamp +
            // TitleBlockParamApplier sequence applies — keeps fabrication in
            // step with the SheetManager / scope-box paths.
            try
            {
                if (drawingType != null)
                {
                    var apply = StingTools.Core.Drawing.DrawingTypePresentation
                        .ApplyToSheet(doc, sheet, drawingType, extraTokens);
                    foreach (var w in apply.Warnings)
                        result.Warnings.Add("ApplyToSheet: " + w);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"ApplyToSheet: {ex.Message}"); }
        }

        private static string ReadString(Element el, string param)
        {
            try { return el?.LookupParameter(param)?.AsString() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// Substitute tokens in a sheet-number or sheet-name pattern —
        /// {spool}/{disc}/{discipline}/{sys}/{lvl}/{mark} literal, plus
        /// {seq} (defaults to D4 padding) and {seq:D2}/{seq:D3}/{seq:D4}
        /// with explicit width. ISO 19650 tokens {project}/{originator}/
        /// {vol}/{type}/{role}/{suit}/{rev} resolve from the optional
        /// <paramref name="extras"/> dict when supplied. Unrecognised
        /// tokens are left as literal text.
        /// </summary>
        private static string SubstituteTokens(string pattern, string spool,
            string disc, string sys, string lvl, int seq,
            string disciplineFull = null, string mark = null,
            System.Collections.Generic.IDictionary<string, string> extras = null)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            var s = pattern
                .Replace("{spool}",      spool ?? "")
                .Replace("{disc}",       disc  ?? "")
                .Replace("{discipline}", disciplineFull ?? disc ?? "")
                .Replace("{sys}",        sys   ?? "")
                .Replace("{lvl}",        lvl   ?? "")
                .Replace("{mark}",       mark  ?? "");

            // ISO 19650 tokens (and anything else the caller feeds).
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    s = s.Replace("{" + kv.Key + "}", kv.Value ?? "");
                }
            }

            // {seq:Dn} with explicit padding width — honour whatever the
            // pattern asks for so A-{seq:D3} yields A-001 and
            // SP-{seq:D4} yields SP-0001.
            s = System.Text.RegularExpressions.Regex.Replace(
                s, @"\{seq:D(\d+)\}",
                m => seq.ToString("D" + m.Groups[1].Value));
            // Bare {seq} keeps the historical default of 4 digits.
            s = s.Replace("{seq}", seq.ToString("D4"));
            return s;
        }

        /// <summary>
        /// Build the standard token dict for a resolved DrawingType —
        /// ISO tokens sourced from dt.IsoNaming, project + originator
        /// pulled from ProjectInformation so every sheet in a project
        /// shares those two codes automatically.
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, string>
            BuildTokenDict(Document doc, StingTools.Core.Drawing.DrawingType dt,
                string spool, string discCode, string discipline, string sysCode, string levelCode, int seq)
        {
            // INT-06: route through the canonical builder so the SheetManager
            // and fabrication paths feed identical tokens to
            // TitleBlockParamApplier. Adding new fields (mark, purpose, phase,
            // …) only needs to happen in one place.
            return StingTools.Core.Drawing.DrawingTokenContext.Build(
                doc:        doc,
                dt:         dt,
                discCode:   discCode,
                discipline: discipline,
                sysCode:    sysCode,
                levelCode:  levelCode,
                seq:        seq,
                seqWidth:   4,
                spool:      spool);
        }

        private static string ReadProjectInfo(Document doc, string param)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                var p = pi?.LookupParameter(param);
                return p?.StorageType == StorageType.String ? (p.AsString() ?? "") : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Phase 175 — run the AnnotationRunner with tag passes disabled
        /// against a fabrication view, so the profile's dim rules
        /// (Ordinate spool chains, MEP-run chains, drainage spot
        /// elevations) actually fire. Tagging is owned by IsoSymbolPlacer
        /// + the discipline-specific fabricator; this pass exists purely
        /// to land annotation primitives the spool drawer expects.
        /// </summary>
        private static void RunFabricationDimPass(Document doc, View view,
            StingTools.Core.Drawing.DrawingType dt, FabricationResult result)
        {
            if (dt?.Annotation == null) return;
            try
            {
                var pack = dt.Annotation;
                try { pack.MigrateFromLegacy(); } catch { }
                var opts = new StingTools.Core.Drawing.AnnotationRunOptions
                {
                    ViewScale       = view.Scale,
                    SkipAutoTag     = true,
                    SkipDecorative  = true,
                    SkipAutoDim     = false,
                    SkipSpots       = false,
                };
                var r = StingTools.Core.Drawing.AnnotationRunner.Run(doc, view, pack, opts);
                foreach (var w in r.Warnings)
                    result.Warnings.Add($"FabDim view {view.Id.Value}: {w}");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"RunFabricationDimPass: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve the DrawingSlot whose Label matches the supplied
        /// fabrication-view tag (Plan / 3D / ISO / Elev0 / Elev90 /
        /// ElevTop). Match is case-insensitive and tolerates the legacy
        /// uppercase forms ("PLAN" / "ELEV0") used in the slot
        /// catalogues. Returns null when the profile authors didn't
        /// declare a corresponding slot.
        /// </summary>
        private static StingTools.Core.Drawing.DrawingSlot ResolveSlot(
            StingTools.Core.Drawing.DrawingType dt, string slotLabel)
        {
            if (dt?.Slots == null || dt.Slots.Count == 0) return null;
            if (string.IsNullOrEmpty(slotLabel)) return null;
            foreach (var s in dt.Slots)
            {
                if (s == null || string.IsNullOrEmpty(s.Label)) continue;
                if (string.Equals(s.Label, slotLabel, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        /// <summary>
        /// Force a view's scale (1:N) — used as the no-DrawingType
        /// fallback so spools never end up at the Revit default scale,
        /// which would print half-size on the 1:50 slot layout.
        /// Views without a settable Scale (schedules, legends) throw
        /// and are silently skipped.
        /// </summary>
        private static void TrySetViewScale(Document doc, ElementId viewId, int scale,
            FabricationResult result)
        {
            if (viewId == null || viewId == ElementId.InvalidElementId || scale <= 0) return;
            try
            {
                if (doc.GetElement(viewId) is View v && !v.IsTemplate)
                    v.Scale = scale;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Scale 1:{scale} on {viewId.Value}: {ex.Message}");
            }
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

        // ────────────────────────────────────────────────────────────────
        //  Drawing Template Manager integration
        //
        //  Phase I keeps fabrication wired to its historic hard-coded
        //  behaviour but layers the Drawing Type registry on top as an
        //  additional fallback ahead of the per-discipline dict. Null
        //  return = no profile found, caller keeps today's defaults —
        //  so landing this is zero-regression by construction.
        // ────────────────────────────────────────────────────────────────

        private static StingTools.Core.Drawing.DrawingType ResolveDrawingType(Document doc, string discipline)
        {
            try
            {
                string discCode = DisciplineCode.TryGetValue(discipline ?? "", out var dc) ? dc : "G";
                return StingTools.Core.Drawing.DrawingDispatcher.Resolve(
                    doc, discCode, "*", StingTools.Core.Drawing.DrawingPurpose.Spool);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ShopDrawingComposer.ResolveDrawingType: {ex.Message}");
                return null;
            }
        }

        private static ElementId ResolveTitleBlockFromDrawingType(
            Document doc, StingTools.Core.Drawing.DrawingType dt)
        {
            if (dt == null || string.IsNullOrWhiteSpace(dt.TitleBlockFamily))
                return ElementId.InvalidElementId;
            try
            {
                // Mirror DrawingTypeSheetAdapter.ResolveTitleBlock so the
                // optional TitleBlockSymbolType variant (Tender / Construction
                // / As-Built etc.) is honoured on the fabrication path too.
                var matches = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => string.Equals(
                        fs.FamilyName, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!string.IsNullOrWhiteSpace(dt.TitleBlockSymbolType))
                {
                    var picked = matches.FirstOrDefault(fs => string.Equals(
                        fs.Name, dt.TitleBlockSymbolType, StringComparison.OrdinalIgnoreCase));
                    if (picked != null) return picked.Id;
                }
                var fallback = matches.FirstOrDefault();
                if (fallback != null) return fallback.Id;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveTitleBlockFromDrawingType('{dt.TitleBlockFamily}'): {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }
    }
}
