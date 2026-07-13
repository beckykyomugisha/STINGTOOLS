// StingTools — Work Item C · Slot-driven title-block graphics
//
// Generalises the W4 QR pattern (TitleBlockStampQRCommand) into four
// toggle-gated, idempotent, remove-when-off graphic placers that land on
// the reserved title-block slots declared in STING_TITLE_BLOCKS.json:
//
//   TitleBlock_PlaceNorthArrow — north-arrow annotation at the north-arrow
//       slot, rotated to project north (PRJ_ORG_PROJECT_NORTH_TXT).
//   TitleBlock_PlaceScaleBar   — graphic scale-bar annotation at the
//       scale-bar slot, keyed to the primary plan viewport's View.Scale.
//   TitleBlock_PlaceKeyPlan    — a per-sheet key-plan drafting view placed
//       as a viewport at the key-plan slot, labelled with the sheet zone.
//   TitleBlock_PlaceLegend     — the discipline symbol legend (reusing
//       DisciplineLegendEngine) placed as a viewport at the discipline-
//       legend (fallback: notes) slot.
//
// Each is gated by its PRJ_TB_SHOW_*_BOOL toggle on the title-block
// instance: toggle off => the placed graphic is removed and the sheet
// skipped. Re-runs replace (never duplicate). Slot bounds + placement
// helpers live in TitleBlockSlotUtils.
//
// The annotation families are resolved through TitleBlockGraphicsRegistry
// (Work Item D families under Families/Annotations/). When a family is not
// available the sheet is skipped cleanly (no synthetic geometry) — the
// same "silently skip when the symbol is missing" contract the fixture
// placement engine uses.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    /// <summary>Resolves the minimal annotation symbol families the graphics
    /// placers load (Work Item D). Kind → family name + on-disk relative path.
    /// <see cref="ResolveSymbol"/> returns an activated <see cref="FamilySymbol"/>
    /// or null when the family is neither loaded nor found on disk.</summary>
    internal static class TitleBlockGraphicsRegistry
    {
        // kind → (family name authored in the .rfa, relative path under a
        // Families/Annotations search root).
        private static readonly Dictionary<string, (string Family, string File)> Map =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["north-arrow"] = ("STING_TB_NorthArrow",  "Annotations/STING_TB_NorthArrow.rfa"),
            ["scale-bar"]   = ("STING_TB_ScaleBar",    "Annotations/STING_TB_ScaleBar.rfa"),
            ["key-plan"]    = ("STING_TB_KeyPlanBase", "Annotations/STING_TB_KeyPlanBase.rfa"),
        };

        public static string FamilyName(string kind)
            => Map.TryGetValue(kind, out var m) ? m.Family : null;

        /// <summary>Find (or load from disk) the family for <paramref name="kind"/>
        /// and return its first, activated symbol. Caller must have an open
        /// transaction (LoadFamily / Activate mutate the document). Null when the
        /// family cannot be found on disk.</summary>
        public static FamilySymbol ResolveSymbol(Document doc, string kind)
        {
            if (!Map.TryGetValue(kind, out var m)) return null;
            try
            {
                var fam = new FilteredElementCollector(doc).OfClass(typeof(Family)).OfType<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, m.Family, StringComparison.OrdinalIgnoreCase));
                if (fam == null)
                {
                    var path = FindOnDisk(doc, m.File);
                    if (path == null) return null;
                    if (!doc.LoadFamily(path, new TitleBlockFamilyLoadOptions(), out fam) && fam == null)
                        return null;
                }
                var symId = fam?.GetFamilySymbolIds().FirstOrDefault() ?? ElementId.InvalidElementId;
                if (symId == ElementId.InvalidElementId) return null;
                var sym = doc.GetElement(symId) as FamilySymbol;
                if (sym != null && !sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                return sym;
            }
            catch (Exception ex) { StingLog.Warn($"GraphicsRegistry.ResolveSymbol '{kind}': {ex.Message}"); return null; }
        }

        private static string FindOnDisk(Document doc, string relative)
        {
            foreach (var root in SearchRoots(doc))
            {
                if (string.IsNullOrEmpty(root)) continue;
                var p = Path.Combine(root, "Families", relative);
                if (File.Exists(p)) return p;
                var p2 = Path.Combine(root, relative);   // root already ends in Families/
                if (File.Exists(p2)) return p2;
            }
            return null;
        }

        private static IEnumerable<string> SearchRoots(Document doc)
        {
            // 1. project folder, 2. addin DLL folder, 3. repo root guess.
            string prj = null;
            try { if (!string.IsNullOrEmpty(doc.PathName)) prj = Path.GetDirectoryName(doc.PathName); }
            catch (Exception ex) { StingLog.Warn($"SearchRoots prj: {ex.Message}"); }
            if (prj != null) yield return prj;
            string asm = null;
            try { asm = Path.GetDirectoryName(StingToolsApp.AssemblyPath); }
            catch (Exception ex) { StingLog.Warn($"SearchRoots asm: {ex.Message}"); }
            if (asm != null) { yield return asm; yield return Path.Combine(asm, "data"); }
        }
    }

    /// <summary>Per-graphic placement outcome, aggregated by the commands +
    /// the StampSheetGraphics orchestrator (Work Item E).</summary>
    internal enum GraphicOutcome { Placed, SkippedToggleOff, SkippedNoSlot, SkippedNoFamily, SkippedNoData, Failed }

    /// <summary>North-arrow stamper. Places the north-arrow annotation at the
    /// title-block north-arrow slot and rotates it to project north.</summary>
    internal static class NorthArrowStamper
    {
        public const string ToggleParam = "PRJ_TB_SHOW_NORTH_ARROW_BOOL";

        public static GraphicOutcome Stamp(Document doc, ViewSheet sheet, List<string> log)
        {
            try
            {
                var tb = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                var famPrefix = TitleBlockGraphicsRegistry.FamilyName("north-arrow");
                if (TitleBlockSlotUtils.IsShowToggleOff(tb, ToggleParam))
                {
                    TitleBlockSlotUtils.RemoveStingFamilyInstancesOnSheet(doc, sheet, famPrefix);
                    return GraphicOutcome.SkippedToggleOff;
                }
                if (!TitleBlockSlotUtils.TryResolveSlotCentre(doc, tb, "north-arrow", out var centre, out _, out _))
                    return GraphicOutcome.SkippedNoSlot;

                var sym = TitleBlockGraphicsRegistry.ResolveSymbol(doc, "north-arrow");
                if (sym == null) return GraphicOutcome.SkippedNoFamily;

                TitleBlockSlotUtils.RemoveStingFamilyInstancesOnSheet(doc, sheet, famPrefix);
                var fi = doc.Create.NewFamilyInstance(centre, sym, sheet);
                double rad = ResolveProjectNorthRadians(doc);
                if (Math.Abs(rad) > 1e-6 && fi != null)
                {
                    var axis = Line.CreateBound(centre, centre + XYZ.BasisZ);
                    try { ElementTransformUtils.RotateElement(doc, fi.Id, axis, rad); }
                    catch (Exception ex) { StingLog.Warn($"North-arrow rotate {sheet.SheetNumber}: {ex.Message}"); }
                }
                return GraphicOutcome.Placed;
            }
            catch (Exception ex)
            {
                StingLog.Error($"North-arrow stamp {sheet?.SheetNumber}: {ex.Message}", ex);
                log?.Add($"{sheet?.SheetNumber}: north arrow failed — {ex.Message}");
                return GraphicOutcome.Failed;
            }
        }

        /// <summary>Project-north angle (radians) parsed from
        /// PRJ_ORG_PROJECT_NORTH_TXT on ProjectInformation (e.g. "12.5deg E").
        /// 0 when absent / unparsable.</summary>
        private static double ResolveProjectNorthRadians(Document doc)
        {
            try
            {
                var pi = doc.ProjectInformation;
                var p = pi?.LookupParameter("PRJ_ORG_PROJECT_NORTH_TXT");
                var s = p?.AsString();
                if (string.IsNullOrWhiteSpace(s)) return 0;
                var m = Regex.Match(s, @"-?\d+(\.\d+)?");
                if (!m.Success) return 0;
                double deg = double.Parse(m.Value, CultureInfo.InvariantCulture);
                bool west = s.IndexOf(" W", StringComparison.OrdinalIgnoreCase) >= 0
                            || s.IndexOf("W of", StringComparison.OrdinalIgnoreCase) >= 0;
                return (west ? deg : -deg) * Math.PI / 180.0;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveProjectNorthRadians: {ex.Message}"); return 0; }
        }
    }

    /// <summary>Scale-bar stamper. Places the scale-bar annotation at the
    /// scale-bar slot and sets its "Scale" param to the primary plan
    /// viewport's View.Scale.</summary>
    internal static class ScaleBarStamper
    {
        public const string ToggleParam = "PRJ_TB_SHOW_SCALE_BAR_BOOL";

        public static GraphicOutcome Stamp(Document doc, ViewSheet sheet, List<string> log)
        {
            try
            {
                var tb = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                var famPrefix = TitleBlockGraphicsRegistry.FamilyName("scale-bar");
                if (TitleBlockSlotUtils.IsShowToggleOff(tb, ToggleParam))
                {
                    TitleBlockSlotUtils.RemoveStingFamilyInstancesOnSheet(doc, sheet, famPrefix);
                    return GraphicOutcome.SkippedToggleOff;
                }
                if (!TitleBlockSlotUtils.TryResolveSlotCentre(doc, tb, "scale-bar", out var centre, out _, out _))
                    return GraphicOutcome.SkippedNoSlot;

                var plan = TitleBlockSlotUtils.FindPrimaryPlanViewport(doc, sheet);
                int scale = 0;
                if (plan != null && doc.GetElement(plan.ViewId) is View pv) scale = pv.Scale;
                if (scale <= 0) return GraphicOutcome.SkippedNoData;  // no plan viewport / no scale

                var sym = TitleBlockGraphicsRegistry.ResolveSymbol(doc, "scale-bar");
                if (sym == null) return GraphicOutcome.SkippedNoFamily;

                TitleBlockSlotUtils.RemoveStingFamilyInstancesOnSheet(doc, sheet, famPrefix);
                var fi = doc.Create.NewFamilyInstance(centre, sym, sheet);
                var sp = fi?.LookupParameter("Scale");
                if (sp != null && !sp.IsReadOnly)
                {
                    try
                    {
                        if (sp.StorageType == StorageType.Integer) sp.Set(scale);
                        else if (sp.StorageType == StorageType.Double) sp.Set((double)scale);
                        else if (sp.StorageType == StorageType.String) sp.Set($"1:{scale}");
                    }
                    catch (Exception ex) { StingLog.Warn($"Scale-bar scale set {sheet.SheetNumber}: {ex.Message}"); }
                }
                return GraphicOutcome.Placed;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Scale-bar stamp {sheet?.SheetNumber}: {ex.Message}", ex);
                log?.Add($"{sheet?.SheetNumber}: scale bar failed — {ex.Message}");
                return GraphicOutcome.Failed;
            }
        }
    }

    /// <summary>Key-plan stamper. Ensures a per-sheet key-plan drafting view
    /// (with the key-plan base symbol + zone label) and places it as a
    /// viewport at the key-plan slot.</summary>
    internal static class KeyPlanStamper
    {
        public const string ToggleParam = "PRJ_TB_SHOW_KEY_PLAN_BOOL";

        public static GraphicOutcome Stamp(Document doc, ViewSheet sheet, List<string> log)
        {
            try
            {
                var tb = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                string viewName = $"STING KeyPlan - {sheet.SheetNumber}";

                if (TitleBlockSlotUtils.IsShowToggleOff(tb, ToggleParam))
                {
                    // G2-c: the key-plan drafting view is per-sheet, so on
                    // toggle-off remove its viewport AND the now-orphaned view.
                    var existingV = TitleBlockSlotUtils.FindDraftingViewByName(doc, viewName);
                    if (existingV != null)
                    {
                        var vp = TitleBlockSlotUtils.FindViewportForView(doc, sheet, existingV.Id);
                        if (vp != null) { try { doc.Delete(vp.Id); } catch (Exception ex) { StingLog.Warn($"KeyPlan vp del: {ex.Message}"); } }
                        try { doc.Delete(existingV.Id); } catch (Exception ex) { StingLog.Warn($"KeyPlan view del: {ex.Message}"); }
                    }
                    return GraphicOutcome.SkippedToggleOff;
                }
                if (!TitleBlockSlotUtils.TryResolveSlotCentre(doc, tb, "key-plan", out var centre, out _, out _))
                    return GraphicOutcome.SkippedNoSlot;

                var view = TitleBlockSlotUtils.FindDraftingViewByName(doc, viewName)
                           ?? CreateKeyPlanView(doc, sheet, tb, viewName);
                if (view == null) return GraphicOutcome.Failed;

                var placed = TitleBlockSlotUtils.PlaceOrMoveViewport(doc, sheet, view, centre);
                return placed != null ? GraphicOutcome.Placed : GraphicOutcome.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Key-plan stamp {sheet?.SheetNumber}: {ex.Message}", ex);
                log?.Add($"{sheet?.SheetNumber}: key plan failed — {ex.Message}");
                return GraphicOutcome.Failed;
            }
        }

        private static ViewDrafting CreateKeyPlanView(Document doc, ViewSheet sheet, Element tb, string viewName)
        {
            var vftId = TitleBlockSlotUtils.ResolveDraftingViewFamilyType(doc);
            if (vftId == ElementId.InvalidElementId) return null;
            var view = ViewDrafting.Create(doc, vftId);
            try { view.Name = viewName; } catch (Exception ex) { StingLog.Warn($"KeyPlan name: {ex.Message}"); }

            // Base outline symbol (optional — skipped cleanly when absent).
            var sym = TitleBlockGraphicsRegistry.ResolveSymbol(doc, "key-plan");
            if (sym != null)
            {
                try { doc.Create.NewFamilyInstance(XYZ.Zero, sym, view); }
                catch (Exception ex) { StingLog.Warn($"KeyPlan base symbol: {ex.Message}"); }
            }

            // Zone label — ASS_ZONE_TXT on the sheet, else scope-box name.
            string zone = ResolveZone(doc, sheet, tb);
            if (!string.IsNullOrWhiteSpace(zone))
            {
                try
                {
                    var tnt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                        .OfType<TextNoteType>().Select(t => t.Id).FirstOrDefault();
                    if (tnt != null && tnt != ElementId.InvalidElementId)
                        TextNote.Create(doc, view.Id, new XYZ(0, -0.05, 0), $"ZONE: {zone}", tnt);
                }
                catch (Exception ex) { StingLog.Warn($"KeyPlan zone label: {ex.Message}"); }
            }
            return view;
        }

        private static string ResolveZone(Document doc, ViewSheet sheet, Element tb)
        {
            try
            {
                var z = sheet.LookupParameter("ASS_ZONE_TXT")?.AsString();
                if (!string.IsNullOrWhiteSpace(z)) return z;
                z = tb?.LookupParameter("PRJ_SHEET_VOLUME_TXT")?.AsString();
                if (!string.IsNullOrWhiteSpace(z)) return z;
                // scope box on the sheet's primary plan view
                var plan = TitleBlockSlotUtils.FindPrimaryPlanViewport(doc, sheet);
                if (plan != null && doc.GetElement(plan.ViewId) is View pv)
                {
                    var sb = pv.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    var sbId = sb?.AsElementId();
                    if (sbId != null && sbId != ElementId.InvalidElementId)
                        return doc.GetElement(sbId)?.Name;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveZone: {ex.Message}"); }
            return null;
        }
    }

    /// <summary>Legend stamper. Reuses DisciplineLegendEngine to get/create the
    /// sheet-discipline symbol legend and places it as a viewport at the
    /// discipline-legend (fallback notes) slot.</summary>
    internal static class LegendStamper
    {
        // The discipline-legend / notes slots carry no PRJ_TB_SHOW_* toggle
        // (per TitleBlockSpec: notes are always visible), so the legend is
        // always eligible — it self-skips only when the slot or discipline
        // can't be resolved.

        public static GraphicOutcome Stamp(Document doc, ViewSheet sheet, List<string> log)
        {
            try
            {
                var tb = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                string disc = ResolveDiscipline(doc, sheet, tb);
                if (string.IsNullOrEmpty(disc) || !Core.Drawing.DisciplineLegendEngine.SupportsDiscipline(disc))
                    return GraphicOutcome.SkippedNoData;

                // G2-a: the engine (LegendBuilder.CreateLegendView) names the view
                // "STING Legend - {config.Title}" and it may be a native Legend OR a
                // Drafting fallback. The old finder searched "{DiscName} Symbols
                // Legend" + ViewType.Legend only, always missed, and minted a fresh
                // view + viewport every run. Match the REAL name (any view type) so
                // the view is reused and its viewport is MOVED, not recreated.
                string legendName = $"STING Legend - {Core.Drawing.DisciplineLegendEngine.DisciplineName(disc)} Symbols Legend";
                var legend = FindLegendByName(doc, legendName);

                // Resolve slot first so a no-slot short-circuits before we mint a view.
                if (!TitleBlockSlotUtils.TryResolveSlotCentre(doc, tb, "discipline-legend", out var centre, out _, out _)
                    && !TitleBlockSlotUtils.TryResolveSlotCentre(doc, tb, "notes", out centre, out _, out _))
                {
                    return GraphicOutcome.SkippedNoSlot;
                }

                if (legend == null)
                {
                    legend = Core.Drawing.DisciplineLegendEngine.CreateDisciplineLegend(doc, disc, out _);
                    if (legend == null) return GraphicOutcome.Failed;
                }

                var placed = TitleBlockSlotUtils.PlaceOrMoveViewport(doc, sheet, legend, centre);
                return placed != null ? GraphicOutcome.Placed : GraphicOutcome.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Legend stamp {sheet?.SheetNumber}: {ex.Message}", ex);
                log?.Add($"{sheet?.SheetNumber}: legend failed — {ex.Message}");
                return GraphicOutcome.Failed;
            }
        }

        /// <summary>Find the engine-created legend view by its stable name,
        /// accepting either a native Legend or the Drafting fallback (the engine
        /// uses whichever the project supports, same name for both).</summary>
        private static View FindLegendByName(Document doc, string name)
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(View)).OfType<View>()
                    .FirstOrDefault(v => !v.IsTemplate
                                         && (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView)
                                         && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { StingLog.Warn($"FindLegendByName: {ex.Message}"); return null; }
        }

        internal static string ResolveDiscipline(Document doc, ViewSheet sheet, Element tb)
        {
            try
            {
                var d = tb?.LookupParameter("PRJ_TB_DISCIPLINE_TXT")?.AsString();
                if (!string.IsNullOrWhiteSpace(d))
                {
                    var code = d.Trim();
                    if (Core.Drawing.DisciplineLegendEngine.SupportsDiscipline(code)) return code;
                    if (code.Length > 0 && Core.Drawing.DisciplineLegendEngine.SupportsDiscipline(code.Substring(0, 1)))
                        return code.Substring(0, 1);
                }
                var sn = sheet.SheetNumber ?? "";
                var m = Regex.Match(sn, @"^([A-Za-z])");
                if (m.Success && Core.Drawing.DisciplineLegendEngine.SupportsDiscipline(m.Groups[1].Value))
                    return m.Groups[1].Value.ToUpperInvariant();
            }
            catch (Exception ex) { StingLog.Warn($"ResolveDiscipline: {ex.Message}"); }
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Commands — each loops every non-placeholder sheet, one TransactionGroup.
    // ---------------------------------------------------------------------

    internal static class GraphicsCommandRunner
    {
        public delegate GraphicOutcome StampFn(Document doc, ViewSheet sheet, List<string> log);

        public static Result RunAllSheets(ExternalCommandData data, ref string msg,
            string title, string txName, StampFn stamp)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().Where(s => !s.IsPlaceholder).ToList();
            if (sheets.Count == 0)
            { TaskDialog.Show(title, "No sheets found in this project."); return Result.Cancelled; }

            int placed = 0, skipped = 0, failed = 0;
            var log = new List<string>();
            // G2-b: resolve slot bounds (incl. the EditFamily ref-plane override)
            // BEFORE the transaction; served from cache during placement.
            TitleBlockSlotUtils.WarmSlotBounds(doc, sheets);
            using (var tg = new TransactionGroup(doc, txName))
            {
                tg.Start();
                using (var tx = new Transaction(doc, txName))
                {
                    tx.Start();
                    foreach (var s in sheets)
                    {
                        var o = stamp(doc, s, log);
                        if (o == GraphicOutcome.Placed) placed++;
                        else if (o == GraphicOutcome.Failed) failed++;
                        else skipped++;
                    }
                    tx.Commit();
                }
                tg.Assimilate();
            }
            TitleBlockSlotUtils.ClearSlotBoundsCache();

            var report = $"{title}\n\nPlaced: {placed}\nSkipped: {skipped} (toggle off / no slot / no family / no data)\nFailed: {failed}.";
            if (log.Count > 0) report += "\n\n" + string.Join("\n", log.Take(15));
            TaskDialog.Show(title, report);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockPlaceNorthArrowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => GraphicsCommandRunner.RunAllSheets(data, ref msg, "STING — Place North Arrow",
                "STING Place North Arrow", NorthArrowStamper.Stamp);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockPlaceScaleBarCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => GraphicsCommandRunner.RunAllSheets(data, ref msg, "STING — Place Scale Bar",
                "STING Place Scale Bar", ScaleBarStamper.Stamp);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockPlaceKeyPlanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => GraphicsCommandRunner.RunAllSheets(data, ref msg, "STING — Place Key Plan",
                "STING Place Key Plan", KeyPlanStamper.Stamp);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockPlaceLegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
            => GraphicsCommandRunner.RunAllSheets(data, ref msg, "STING — Place Discipline Legend",
                "STING Place Discipline Legend", LegendStamper.Stamp);
    }
}
