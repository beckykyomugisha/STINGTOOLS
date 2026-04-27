// StingTools — Drawing Template Manager · Phase 137
//
// DrawingProducer is the engine that turns a (DrawingType, Context)
// pair into one or more views and (optionally) a sheet hosting them.
// Per-type ProductionRules drive multi-view production: one rule per
// produced view, each with optional per-rule overrides.
//
// Caller responsibility: open a Transaction (or TransactionGroup)
// before invoking ProduceAllViews. The producer does not open
// transactions itself.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class DrawingContext
    {
        public Level Level { get; set; }
        public Element Room { get; set; }
        public Element ScopeBox { get; set; }
        public BoundingBoxXYZ CustomBounds { get; set; }
        public string Tag { get; set; }
        public string PackageId { get; set; }
    }

    public sealed class ProduceOptions
    {
        public bool CreateSheet { get; set; } = true;
        public bool PlaceOnSheet { get; set; } = true;
        public bool RunAnnotation { get; set; } = true;
        public bool DuplicateFromTemplate { get; set; } = false;
        public ViewDuplicateOption DuplicateOption { get; set; } = ViewDuplicateOption.Duplicate;
        public bool Idempotent { get; set; } = true;
        public string OverrideSheetNumber { get; set; }
        public string OverrideSheetName { get; set; }
        public DrawingProductionPreset Preset { get; set; }
    }

    public sealed class ProduceResult
    {
        public List<ElementId> ViewIds { get; } = new List<ElementId>();
        public ElementId SheetId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> ViewportIds { get; } = new List<ElementId>();
        public bool WasIdempotent { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class DrawingProducer
    {
        public static ProduceResult ProduceView(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts)
            => ProduceAllViews(doc, dt, ctx, opts);

        public static ProduceResult ProduceAllViews(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts)
        {
            var result = new ProduceResult();
            if (doc == null || dt == null || ctx == null) return result;
            opts = opts ?? new ProduceOptions();

            var rules = (dt.ProductionRules != null && dt.ProductionRules.Count > 0)
                ? dt.ProductionRules.OrderBy(r => r.Idx).ToList()
                : new List<ProductionRule> { SynthesizeSingleRule(dt) };

            if (opts.CreateSheet)
                result.SheetId = CreateOrFindSheet(doc, dt, ctx, opts, result);

            foreach (var rule in rules)
            {
                if (rule == null) continue;
                if (opts.Preset?.General?.GenerateOnlyDefault == true && rule.Idx > 0 && !rule.Required) continue;
                var viewId = ProduceSingleView(doc, dt, rule, ctx, opts, result);
                if (viewId == ElementId.InvalidElementId) continue;
                result.ViewIds.Add(viewId);
                StampViewParameters(doc, viewId, dt, rule, ctx);

                if (opts.PlaceOnSheet && result.SheetId != ElementId.InvalidElementId)
                {
                    var vpId = PlaceViewOnSheet(doc, result.SheetId, viewId, dt, rule, result);
                    if (vpId != ElementId.InvalidElementId)
                    {
                        result.ViewportIds.Add(vpId);
                        StampAutoPlaced(doc, vpId);
                    }
                }
            }

            return result;
        }

        private static ProductionRule SynthesizeSingleRule(DrawingType dt)
        {
            string vt;
            switch ((dt.Purpose ?? "").Trim())
            {
                case DrawingPurpose.Plan:         vt = "FloorPlan"; break;
                case DrawingPurpose.Rcp:          vt = "RCP"; break;
                case DrawingPurpose.Section:      vt = "Section"; break;
                case DrawingPurpose.Elevation:    vt = "Elevation"; break;
                case DrawingPurpose.Detail:       vt = "Detail"; break;
                case DrawingPurpose.ThreeD:       vt = "ThreeD"; break;
                case DrawingPurpose.Schedule:     vt = "Schedule"; break;
                default:                          vt = "FloorPlan"; break;
            }
            return new ProductionRule { Idx = 0, ViewType = vt, Required = true, SlotIndex = 0 };
        }

        private static ElementId ProduceSingleView(Document doc, DrawingType dt, ProductionRule rule, DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            try
            {
                if (opts.Idempotent)
                {
                    var existing = FindExistingView(doc, dt.Id, ctx, rule.Idx);
                    if (existing != null)
                    {
                        result.WasIdempotent = true;
                        return existing.Id;
                    }
                }

                var vft = ResolveViewFamilyType(doc, rule, result);
                if (vft == null) return ElementId.InvalidElementId;

                var viewId = CreateViewByType(doc, rule, ctx, dt, vft, result);
                if (viewId == ElementId.InvalidElementId) return viewId;

                var view = doc.GetElement(viewId) as View;
                if (view == null) return ElementId.InvalidElementId;

                try { view.Name = MakeUniqueViewName(doc, BuildViewName(dt, rule, ctx)); } catch { }
                if (rule.ScaleOverride.HasValue) try { view.Scale = rule.ScaleOverride.Value; } catch { }

                var applyOpts = new DrawingTypePresentation.ApplyOptions
                {
                    AnnotationOptions = opts.RunAnnotation
                        ? new AnnotationRunOptions { ViewScale = view.Scale }
                        : new AnnotationRunOptions { SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true }
                };
                var presResult = DrawingTypePresentation.Apply(doc, view, dt, applyOpts);
                result.Warnings.AddRange(presResult.Warnings);

                if (opts.Preset?.VgOverrides != null &&
                    opts.Preset.VgOverrides.TryGetValue(dt.Id, out var presetVg) &&
                    presetVg != null && presetVg.Count > 0)
                {
                    var packResult = new PackApplyResult();
                    ViewStylePackApplier.ApplyPresetOverrides(doc, view, presetVg, packResult);
                    result.Warnings.AddRange(packResult.Warnings);
                }

                return viewId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ProduceSingleView({rule?.ViewType}): {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static ViewFamilyType ResolveViewFamilyType(Document doc, ProductionRule rule, ProduceResult result)
        {
            ViewFamily targetFamily;
            switch ((rule.ViewType ?? "").Trim())
            {
                case "FloorPlan":    targetFamily = ViewFamily.FloorPlan; break;
                case "RCP":
                case "CeilingPlan":  targetFamily = ViewFamily.CeilingPlan; break;
                case "Section":      targetFamily = ViewFamily.Section; break;
                case "Detail":       targetFamily = ViewFamily.Detail; break;
                case "Elevation":    targetFamily = ViewFamily.Elevation; break;
                case "ThreeD":       targetFamily = ViewFamily.ThreeDimensional; break;
                case "DraftingView": targetFamily = ViewFamily.Drafting; break;
                case "Schedule":     targetFamily = ViewFamily.Schedule; break;
                default:
                    result.Warnings.Add($"Unknown rule.ViewType '{rule.ViewType}'.");
                    return null;
            }
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == targetFamily);
            if (vft == null)
                result.Warnings.Add($"No ViewFamilyType found for '{rule.ViewType}'.");
            return vft;
        }

        private static ElementId CreateViewByType(Document doc, ProductionRule rule, DrawingContext ctx, DrawingType dt, ViewFamilyType vft, ProduceResult result)
        {
            try
            {
                switch ((rule.ViewType ?? "").Trim())
                {
                    case "FloorPlan":
                    case "RCP":
                    case "CeilingPlan":
                        if (ctx.Level == null)
                        {
                            result.Warnings.Add($"{rule.ViewType} requires a Level — none in context.");
                            return ElementId.InvalidElementId;
                        }
                        return ViewPlan.Create(doc, vft.Id, ctx.Level.Id).Id;

                    case "Section":
                        var sectionBox = ctx.CustomBounds ?? BuildDefaultSectionBbox(ctx);
                        return ViewSection.CreateSection(doc, vft.Id, sectionBox).Id;

                    case "Detail":
                        var detailBox = ctx.CustomBounds ?? BuildDefaultDetailBbox(ctx);
                        return ViewSection.CreateDetail(doc, vft.Id, detailBox).Id;

                    case "Elevation":
                        if (ctx.Level == null && ctx.Room == null)
                        {
                            result.Warnings.Add("Elevation requires Level or Room context.");
                            return ElementId.InvalidElementId;
                        }
                        var origin = ResolveElevationOrigin(ctx);
                        var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, dt.Scale > 0 ? dt.Scale : 100);
                        var ownerPlan = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewPlan))
                            .Cast<ViewPlan>()
                            .FirstOrDefault(v => !v.IsTemplate);
                        if (ownerPlan == null) { result.Warnings.Add("Elevation requires an owner FloorPlan view."); return ElementId.InvalidElementId; }
                        return marker.CreateElevation(doc, ownerPlan.Id, 0).Id;

                    case "ThreeD":
                        return View3D.CreateIsometric(doc, vft.Id).Id;

                    case "DraftingView":
                        return ViewDrafting.Create(doc, vft.Id).Id;

                    case "Schedule":
                        result.Warnings.Add("Schedule production rule requires scheduleCategory field — not yet implemented.");
                        return ElementId.InvalidElementId;

                    default:
                        result.Warnings.Add($"CreateViewByType: unsupported '{rule.ViewType}'.");
                        return ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"CreateViewByType('{rule.ViewType}'): {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static BoundingBoxXYZ BuildDefaultSectionBbox(DrawingContext ctx)
        {
            // 10m × 5m × 10m default box at level elevation (or origin).
            double elevFt = ctx?.Level?.Elevation ?? 0;
            var bb = new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = new XYZ(-5.0 * 3.281, elevFt - 1.0 * 3.281, -5.0 * 3.281),
                Max = new XYZ( 5.0 * 3.281, elevFt + 4.0 * 3.281,  5.0 * 3.281)
            };
            return bb;
        }

        private static BoundingBoxXYZ BuildDefaultDetailBbox(DrawingContext ctx)
        {
            double elevFt = ctx?.Level?.Elevation ?? 0;
            return new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = new XYZ(-1.0 * 3.281, elevFt - 0.5 * 3.281, -1.0 * 3.281),
                Max = new XYZ( 1.0 * 3.281, elevFt + 1.5 * 3.281,  1.0 * 3.281)
            };
        }

        private static XYZ ResolveElevationOrigin(DrawingContext ctx)
        {
            try
            {
                if (ctx.Room is FamilyInstance fi)
                {
                    var lp = fi.Location as LocationPoint;
                    if (lp != null) return lp.Point;
                }
                if (ctx.Room?.Location is LocationPoint lpr) return lpr.Point;
                if (ctx.Level != null) return new XYZ(0, 0, ctx.Level.Elevation);
            }
            catch { }
            return XYZ.Zero;
        }

        private static ElementId CreateOrFindSheet(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            string effectivePackage = ctx.PackageId ?? dt.PackageId ?? "";
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s =>
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), dt.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal));
                if (existing != null) return existing.Id;
            }
            catch { }

            ElementId titleBlockId = ElementId.InvalidElementId;
            try
            {
                titleBlockId = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .Where(s => string.IsNullOrEmpty(dt.TitleBlockFamily) ||
                                string.Equals(s.FamilyName, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
                if (titleBlockId == ElementId.InvalidElementId)
                    titleBlockId = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }
            catch { }

            ViewSheet sheet;
            try { sheet = ViewSheet.Create(doc, titleBlockId); }
            catch (Exception ex) { result.Warnings.Add($"CreateSheet: {ex.Message}"); return ElementId.InvalidElementId; }

            try
            {
                sheet.SheetNumber = opts.OverrideSheetNumber ?? SubstituteTokens(dt.SheetNumberPattern, dt, ctx, doc);
            }
            catch (Exception ex) { result.Warnings.Add($"SheetNumber: {ex.Message}"); }
            try
            {
                sheet.Name = opts.OverrideSheetName ?? SubstituteTokens(dt.SheetNamePattern, dt, ctx, doc);
            }
            catch (Exception ex) { result.Warnings.Add($"SheetName: {ex.Message}"); }

            DrawingTypeStamper.Stamp(sheet, dt.Id);
            DrawingTypeStamper.StampPackage(sheet, effectivePackage);

            try
            {
                int seq = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Count(s => string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal));
                DrawingTypeStamper.StampSheetSequence(sheet, seq);
            }
            catch { }

            if (dt.TitleBlockParams != null && dt.TitleBlockParams.Count > 0)
            {
                try
                {
                    var tokens = BuildTokenDict(dt, ctx);
                    TitleBlockParamApplier.Apply(doc, sheet, dt, tokens);
                }
                catch (Exception ex) { result.Warnings.Add($"TitleBlockParams: {ex.Message}"); }
            }

            return sheet.Id;
        }

        private static ElementId PlaceViewOnSheet(Document doc, ElementId sheetId, ElementId viewId, DrawingType dt, ProductionRule rule, ProduceResult result)
        {
            try
            {
                var pt = SheetPlacementBridge.GetSlotPosition(doc, sheetId, dt,
                    rule.SlotIndex >= 0 ? rule.SlotIndex : 0, result);
                if (pt == null)
                {
                    var sheet = doc.GetElement(sheetId) as ViewSheet;
                    var bb = sheet?.Outline;
                    pt = bb != null ? new XYZ((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0, 0) : XYZ.Zero;
                }
                var vp = Viewport.Create(doc, sheetId, viewId, pt);
                return vp?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"PlaceViewOnSheet: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static void StampViewParameters(Document doc, ElementId viewId, DrawingType dt, ProductionRule rule, DrawingContext ctx)
        {
            var view = doc.GetElement(viewId);
            if (view == null) return;
            try { StingTools.Core.ParameterHelpers.SetString(view, ParamRegistry.STING_VIEW_CONTEXT_TAG, BuildContextTag(ctx), overwrite: true); } catch { }
            try { StingTools.Core.ParameterHelpers.SetString(view, ParamRegistry.STING_DRAWING_PACKAGE_ID, ctx.PackageId ?? dt.PackageId ?? "", overwrite: true); } catch { }
            try { StingTools.Core.ParameterHelpers.SetInt(view, ParamRegistry.STING_PRODUCTION_RULE_IDX, rule.Idx, overwrite: true); } catch { }
        }

        private static void StampAutoPlaced(Document doc, ElementId vpId)
        {
            var el = doc.GetElement(vpId);
            if (el == null) return;
            try { StingTools.Core.ParameterHelpers.SetInt(el, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); } catch { }
        }

        private static string BuildContextTag(DrawingContext ctx)
        {
            string lvl = ctx?.Level?.Name ?? "";
            string room = "";
            try { room = ctx?.Room?.Id?.ToString() ?? ""; } catch { }
            return $"{lvl}::{room}::{ctx?.Tag ?? ""}";
        }

        private static View FindExistingView(Document doc, string dtId, DrawingContext ctx, int ruleIdx)
        {
            try
            {
                var ctxTag = BuildContextTag(ctx);
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(v, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), dtId, StringComparison.OrdinalIgnoreCase) &&
                        StingTools.Core.ParameterHelpers.GetInt(v, ParamRegistry.STING_PRODUCTION_RULE_IDX, -1) == ruleIdx &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(v, ParamRegistry.STING_VIEW_CONTEXT_TAG), ctxTag, StringComparison.Ordinal));
            }
            catch { return null; }
        }

        private static string BuildViewName(DrawingType dt, ProductionRule rule, DrawingContext ctx)
        {
            string ctxLabel = ctx?.Level?.Name
                ?? (ctx?.Room != null ? StingTools.Core.ParameterHelpers.GetString(ctx.Room, "Number") : null)
                ?? ctx?.Tag
                ?? "";
            string raw = $"{dt.Name} - {ctxLabel}{rule.NameSuffix ?? ""}".Trim();
            return SanitizeViewName(raw);
        }

        private static string SanitizeViewName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var bad = new[] { '{', '}', '[', ']', '|', ':', ';', '<', '>', '?', '\\', '/' };
            foreach (var c in bad) raw = raw.Replace(c, '-');
            return raw.Trim();
        }

        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int n = 2;
            while (NameExists(doc, name) && n < 100) name = $"{baseName}_({n++})";
            return name;
        }

        private static bool NameExists(Document doc, string name)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.Ordinal));
            }
            catch { return false; }
        }

        private static string SubstituteTokens(string pattern, DrawingType dt, DrawingContext ctx, Document doc)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            string disc  = dt?.Discipline ?? "";
            string lvl   = ctx?.Level?.Name ?? "";
            string sys   = "";
            string mark  = ctx?.Tag ?? "";
            string spool = ctx?.Tag ?? "";

            string Replace(string p)
            {
                p = p.Replace("{disc}", SafeShort(disc));
                p = p.Replace("{discipline}", disc);
                p = p.Replace("{lvl}", SafeShort(lvl));
                p = p.Replace("{sys}", SafeShort(sys));
                p = p.Replace("{mark}", SafeShort(mark));
                p = p.Replace("{spool}", SafeShort(spool));
                p = p.Replace("{purpose}", dt?.Purpose ?? "");
                // {seq:Dn}
                int seq = (ctx?.Tag != null && int.TryParse(ctx.Tag, out var s)) ? s : 1;
                for (int width = 1; width <= 6; width++)
                    p = p.Replace($"{{seq:D{width}}}", seq.ToString("D" + width));
                p = p.Replace("{seq}", seq.ToString("D4"));
                return p;
            }
            return Replace(pattern);
        }

        private static string SafeShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "XX";
            return new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(8).ToArray());
        }

        private static Dictionary<string, string> BuildTokenDict(DrawingType dt, DrawingContext ctx)
        {
            // INT-06: route through the canonical builder so SheetManager,
            // ShopDrawingComposer and the production engine all feed the
            // exact same token set into TitleBlockParamApplier.
            var d = DrawingTokenContext.Build(
                doc:        null,        // producer is invoked without a doc handle here
                dt:         dt,
                discCode:   dt?.Discipline,
                discipline: dt?.Discipline,
                levelCode:  ctx?.Level?.Name,
                spool:      ctx?.Tag,
                mark:       ctx?.Tag);
            d["package"] = ctx?.PackageId ?? dt?.PackageId ?? string.Empty;
            return d;
        }
    }
}
