// StingTools — Drawing Self-Test (DTW-82)
//
// Nothing merged in the drawing-types review loop had been run in Revit. The worklog's
// NEEDS REVIT CHECK list grew to some fifty items over nine rounds, and most of them ask
// a question the open model can answer on its own: is this parameter bound to Views,
// does Revit accept it on Lines, does a second production run make anything, does a
// managed template control V/G. This command asks them, one after another, and rolls
// everything back.
//
// THE CONTRACT
//   * One TransactionGroup round the whole run, ALWAYS rolled back (RollBack in a
//     finally). Whatever a check creates — views, sheets, filters, templates, detail
//     lines, match lines — is gone when the report opens. The report's first line
//     says so.
//   * Each check is self-contained: its own try/catch, so an exception is a FAIL row
//     naming the exception and the next check still runs. A check that needs something
//     the model lacks (a level, a schedule, two STING-AREA boxes) is a SKIP with the
//     reason, never a pass.
//   * INFO rows answer the open design questions (does Revit accept bindings on Lines,
//     which scale parameter a template controls, whether a view-scoped collector
//     honours the crop) — observations, not verdicts.
//   * Preset-safe: no modal dialog when PresetDialog.Quiet; the report goes to the log
//     and the step message, and a run with a FAIL returns Failed.
//
// What cannot be automated (two users, undo, Escape, how things look on screen) is in
// docs/DRAWING_REVIT_TEST_SCRIPT.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingSelfTestCommand : IExternalCommand
    {
        private const string Title = "STING — Drawing Self-Test";

        // Check names, in run order (the report groups by these).
        private const string CkBindings = "a. Bindings";
        private const string CkStamp    = "b. Stamp and re-run";
        private const string CkManaged  = "c. Managed V/G";
        private const string CkFilters  = "d. AEC filters";
        private const string CkFills    = "e. Fill patterns";
        private const string CkTitle    = "f. Title blocks";
        private const string CkNumbers  = "g. Sheet numbering";
        private const string CkWorkshar = "h. Worksharing";
        private const string CkMatch    = "i. Match lines";
        private const string CkCrop     = "j. Crop and collectors";

        /// <summary>State the checks share: the level and plan type they work on.</summary>
        private sealed class Ctx
        {
            public Document Doc;
            public Level Level;
            public DrawingType PlanType;
            public string PlanTypeSource;
            public ElementId PlanViewTypeId = ElementId.InvalidElementId;
            public bool Produced;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetDoc(data);
            if (doc == null) { message = "No document open."; return Result.Failed; }
            if (doc.IsFamilyDocument) { message = "The Drawing Self-Test runs on a project, not a family."; return Result.Failed; }
            if (doc.IsReadOnly) { message = "The model is read-only; the self-test needs to create (and then roll back) test elements."; return Result.Failed; }

            var rows = new List<SelfTestRow>();
            var ctx = new Ctx { Doc = doc };
            StingLog.Info("Drawing Self-Test: start (all changes will be rolled back).");

            using (var tg = new TransactionGroup(doc, "STING Drawing Self-Test (rolled back)"))
            {
                tg.Start();
                try
                {
                    Run(rows, "Setup", () => Setup(ctx, rows));
                    Run(rows, CkBindings, () => CheckBindings(ctx, rows));
                    Run(rows, CkFills, () => CheckFills(ctx, rows));
                    Run(rows, CkNumbers, () => CheckNumbering(ctx, rows));
                    Run(rows, CkTitle, () => CheckTitleBlocks(ctx, rows));
                    Run(rows, CkStamp, () => CheckStampAndRerun(ctx, rows));
                    Run(rows, CkWorkshar, () => CheckWorksharing(ctx, rows));
                    Run(rows, CkManaged, () => CheckManagedVg(ctx, rows));
                    Run(rows, CkFilters, () => CheckFilters(ctx, rows));
                    Run(rows, CkMatch, () => CheckMatchLines(ctx, rows));
                    Run(rows, CkCrop, () => CheckCropCollector(ctx, rows));
                }
                finally
                {
                    // The whole point: leave the model exactly as it was.
                    if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
                }
            }
            DropCaches(doc);
            StingLog.Info($"Drawing Self-Test: done, rolled back — {DrawingSelfTestModel.Summary(rows)}.");

            string csvPath = WriteCsv(doc, rows);
            var panel = BuildPanel(doc, rows, csvPath);
            PresetDialog.Show(panel, ref message);

            int fails = DrawingSelfTestModel.Count(rows, SelfTestStatus.Fail);
            if (PresetDialog.Quiet)
            {
                message = $"Drawing Self-Test (rolled back): {DrawingSelfTestModel.Summary(rows)}"
                        + (csvPath != null ? $"; CSV {csvPath}" : "") + ".";
                return fails > 0 ? Result.Failed : Result.Succeeded;
            }
            return Result.Succeeded;
        }

        // ── Runner ───────────────────────────────────────────────────────────

        /// <summary>One check: an exception is a FAIL row, never an abort.</summary>
        private static void Run(List<SelfTestRow> rows, string check, Action body)
        {
            try { body(); }
            catch (Exception ex)
            {
                StingLog.Warn($"Drawing Self-Test {check}: {ex}");
                rows.Add(new SelfTestRow(check, "(check)", SelfTestStatus.Fail, $"threw {ex.GetType().Name}: {ex.Message}"));
            }
        }

        /// <summary>
        /// Run <paramref name="body"/> in a transaction that must commit (inside the
        /// group, so it is rolled back with everything else). Warnings Revit raises are
        /// collected, not shown. Throws when the commit is refused.
        /// </summary>
        private static List<string> InTx(Document doc, string name, Action body)
        {
            using (var t = new Transaction(doc, "STING Self-Test - " + name))
            {
                t.Start();
                var pp = ProductionFailuresPreprocessor.AttachTo(t);
                body();
                var st = t.Commit();
                if (st != TransactionStatus.Committed)
                    throw new InvalidOperationException($"Revit did not commit '{name}' ({st})"
                        + (pp.Messages.Count > 0 ? ": " + string.Join("; ", pp.Messages) : ""));
                return pp.Messages;
            }
        }

        private static void Add(List<SelfTestRow> rows, string check, string item, SelfTestStatus s, string detail)
            => rows.Add(new SelfTestRow(check, item, s, detail));

        // ── Setup ────────────────────────────────────────────────────────────

        private static void Setup(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            ctx.Level = levels.FirstOrDefault(IsStorey) ?? levels.FirstOrDefault();

            ctx.PlanViewTypeId = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.FloorPlan)
                .Select(t => t.Id).FirstOrDefault() ?? ElementId.InvalidElementId;

            // A routed plan type: what Produce Per Level would make for an architectural plan.
            ctx.PlanType = DrawingDispatcher.Resolve(doc, "A", "WIP", "PLAN");
            ctx.PlanTypeSource = "routing (A / PLAN)";
            if (ctx.PlanType == null || !IsPlan(ctx.PlanType))
            {
                ctx.PlanType = DrawingTypeRegistry.Get(doc, "arch-plan-A1-1to100");
                ctx.PlanTypeSource = "catalogue (arch-plan-A1-1to100; routing gave no plan)";
            }
            if (ctx.PlanType == null || !IsPlan(ctx.PlanType))
            {
                ctx.PlanType = DrawingTypeRegistry.ListAll(doc).FirstOrDefault(IsPlan);
                ctx.PlanTypeSource = "catalogue (first plan type)";
            }

            Add(rows, "Setup", "Model", SelfTestStatus.Info,
                $"{doc.Title}; workshared: {(doc.IsWorkshared ? "yes" : "no")}; level: {ctx.Level?.Name ?? "(none)"}; "
                + $"plan type: {ctx.PlanType?.Id ?? "(none)"} from {ctx.PlanTypeSource}");
        }

        private static bool IsStorey(Level l)
        {
            try { return l.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY)?.AsInteger() != 0; }
            catch (Exception ex) { StingLog.Warn($"Self-Test storey flag on '{l?.Name}': {ex.Message}"); return true; }
        }

        private static bool IsPlan(DrawingType dt)
            => dt != null && string.Equals(dt.Purpose, DrawingPurpose.Plan, StringComparison.OrdinalIgnoreCase);

        private static ViewPlan NewPlan(Ctx ctx)
            => ViewPlan.Create(ctx.Doc, ctx.PlanViewTypeId, ctx.Level.Id);

        private static string NeedsPlan(Ctx ctx)
        {
            if (ctx.Level == null) return "the model has no level";
            if (ctx.PlanViewTypeId == ElementId.InvalidElementId) return "the model has no floor-plan view type";
            return null;
        }

        // ── a. Bindings ──────────────────────────────────────────────────────

        private static void CheckBindings(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var bound = BindingIndex(doc);

            foreach (var p in DrawingSelfTestModel.ViewParams)
                BindingRow(doc, rows, bound, p, BuiltInCategory.OST_Views, "Views");
            foreach (var p in DrawingSelfTestModel.SheetParams)
                BindingRow(doc, rows, bound, p, BuiltInCategory.OST_Sheets, "Sheets");
            BindingRow(doc, rows, bound, DrawingSelfTestModel.ProjectInfoParam, BuiltInCategory.OST_ProjectInformation, "Project Information");

            // Lines — DTW-56. Bound-in-the-map and resolvable-on-a-real-line are different
            // questions, and the second is the one match-line stamping depends on.
            var linesCat = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            bool allows = linesCat != null && linesCat.AllowsBoundParameters;
            Add(rows, CkBindings, "Lines category", SelfTestStatus.Info,
                $"Category 'Lines' AllowsBoundParameters = {(linesCat == null ? "(category not found)" : allows.ToString())}");
            foreach (var p in DrawingSelfTestModel.LineParams)
            {
                if (!bound.TryGetValue(p, out var cats))
                    Add(rows, CkBindings, p + " → Lines", SelfTestStatus.Fail, "not bound to any category (run Load Shared Parameters)");
                else if (!cats.Contains((long)BuiltInCategory.OST_Lines))
                    Add(rows, CkBindings, p + " → Lines", SelfTestStatus.Fail,
                        "REVIT REFUSED LINES: bound, but not to Lines (bound to " + CategoryNames(doc, cats)
                        + "). DTW-56: the match-line keys must move to Extensible Storage.");
                else
                    Add(rows, CkBindings, p + " → Lines", SelfTestStatus.Pass, "bound to Lines");
            }

            // The runtime half: does the parameter resolve on a detail line Revit creates?
            var drafting = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (drafting == null)
            {
                Add(rows, CkBindings, "STING_MATCH_* on a detail line", SelfTestStatus.Skip, "the model has no drafting view type");
                return;
            }
            int resolved = 0;
            var missing = new List<string>();
            InTx(doc, "detail line probe", () =>
            {
                var v = ViewDrafting.Create(doc, drafting.Id);
                var dc = doc.Create.NewDetailCurve(v, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)));
                foreach (var p in DrawingSelfTestModel.LineParams)
                {
                    if (dc.LookupParameter(p) != null) resolved++;
                    else missing.Add(p);
                }
            });
            int n = DrawingSelfTestModel.LineParams.Count;
            Add(rows, CkBindings, "STING_MATCH_* on a detail line",
                resolved == n ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                resolved == n
                    ? $"{resolved}/{n} resolve on a new detail line"
                    : $"REVIT REFUSED LINES: {resolved}/{n} resolve on a new detail line (missing {string.Join(", ", missing)}). "
                      + "DTW-56: move the match-line keys to Extensible Storage.");
        }

        private static void BindingRow(Document doc, List<SelfTestRow> rows, Dictionary<string, HashSet<long>> bound,
            string param, BuiltInCategory bic, string label)
        {
            string item = $"{param} → {label}";
            if (!bound.TryGetValue(param, out var cats))
                Add(rows, CkBindings, item, SelfTestStatus.Fail, "not bound to any category (run Load Shared Parameters)");
            else if (!cats.Contains((long)bic))
                Add(rows, CkBindings, item, SelfTestStatus.Fail, $"bound, but not to {label} (bound to {CategoryNames(doc, cats)})");
            else
                Add(rows, CkBindings, item, SelfTestStatus.Pass, $"bound to {label}");
        }

        /// <summary>Parameter name → every category id it is bound to, over all bindings.</summary>
        private static Dictionary<string, HashSet<long>> BindingIndex(Document doc)
        {
            var map = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            var it = doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                var name = it.Key?.Name;
                if (string.IsNullOrEmpty(name) || !(it.Current is ElementBinding eb) || eb.Categories == null) continue;
                if (!map.TryGetValue(name, out var set)) map[name] = set = new HashSet<long>();
                foreach (Category c in eb.Categories)
                    if (c != null) set.Add(c.Id.Value);
            }
            return map;
        }

        private static string CategoryNames(Document doc, IEnumerable<long> ids)
        {
            var names = new List<string>();
            foreach (var id in ids.Take(6))
            {
                string n = null;
                try { n = Category.GetCategory(doc, new ElementId(id))?.Name; }
                catch (Exception ex) { StingLog.Warn($"Self-Test category name {id}: {ex.Message}"); }
                names.Add(n ?? id.ToString());
            }
            int more = ids.Count() - names.Count;
            return string.Join(", ", names) + (more > 0 ? $" and {more} more" : "");
        }

        // ── e. Fill patterns ─────────────────────────────────────────────────

        private static void CheckFills(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var id = ViewStylePackApplier.ResolveFillPattern(doc, "Solid fill");
            var fpe = doc.GetElement(id) as FillPatternElement;
            if (fpe == null)
                Add(rows, CkFills, "\"Solid fill\"", SelfTestStatus.Fail, "resolved to nothing — the model has no solid fill pattern");
            else
            {
                bool solid = fpe.GetFillPattern()?.IsSolidFill == true;
                Add(rows, CkFills, "\"Solid fill\"", solid ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                    $"resolved to '{fpe.Name}' ({fpe.GetFillPattern()?.Target}); IsSolidFill = {solid}");
            }

            var cross = doc.GetElement(ViewStylePackApplier.ResolveFillPattern(doc, "Crosshatch")) as FillPatternElement;
            if (cross == null)
                Add(rows, CkFills, "\"Crosshatch\"", SelfTestStatus.Skip,
                    "the model has no crosshatch pattern under any alias; Create Fill Patterns makes 'STING - Crosshatch'");
            else
                Add(rows, CkFills, "\"Crosshatch\"", SelfTestStatus.Pass, $"resolved to '{cross.Name}'");
        }

        // ── g. Sheet numbering ───────────────────────────────────────────────

        private static void CheckNumbering(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            if (ctx.Level == null) { Add(rows, CkNumbers, "(all)", SelfTestStatus.Skip, "the model has no level"); return; }

            var codes = IsoLevelStoreys.BuildMap(doc);
            codes.TryGetValue(ctx.Level.Name, out var code);
            Add(rows, CkNumbers, $"ISO level code for '{ctx.Level.Name}'",
                DrawingSelfTestModel.IsWellFormedLevelCode(code) ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                $"'{code ?? "(none)"}'");

            var next = SheetNumbering.NextNumber(doc, "A", code);
            Add(rows, CkNumbers, "Project pattern: next 'A' number",
                DrawingSelfTestModel.IsWellFormedSheetNumber(next, out var why) ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                $"'{next}'" + (why != null ? " — " + why : ""));

            var dt = ctx.PlanType;
            if (dt == null) { Add(rows, CkNumbers, "Drawing-type numbers", SelfTestStatus.Skip, "no plan drawing type in the catalogue"); return; }
            var extras = DrawingProducer.BuildTokenDict(doc, dt, ctx.Level.Name, null, null, 1);
            foreach (var kind in new[] { SheetNumberPolicyKind.Profile, SheetNumberPolicyKind.Iso })
            {
                var pattern = SheetNumberPolicy.ResolvePattern(dt, kind, out var note);
                var number = DrawingProducer.SubstituteTokens(pattern, dt, ctx.Level.Name, null, 1, extras);
                bool ok = DrawingSelfTestModel.IsWellFormedSheetNumber(number, out var w);
                Add(rows, CkNumbers, $"{kind} policy: {dt.Id}", ok ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                    $"'{pattern}' → '{number}'" + (w != null ? " — " + w : "") + (note != null && kind == SheetNumberPolicyKind.Iso && dt.IsoNaming == null ? " (" + note + ")" : ""));
            }
        }

        // ── f. Title blocks ──────────────────────────────────────────────────

        private static void CheckTitleBlocks(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var lib = TitleBlockSpecRegistry.Load();
            if (lib?.Families == null || lib.Families.Count == 0)
                Add(rows, CkTitle, "A3 spec", SelfTestStatus.Fail, "STING_TITLE_BLOCKS.json did not load");
            else
            {
                var spec = lib.Families.FirstOrDefault(f => string.Equals(f.Id, "STING_TB_A3_BIM_v2.0", StringComparison.OrdinalIgnoreCase))
                        ?? lib.Families.FirstOrDefault(f => !f.Abstract && (f.Id ?? "").IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0);
                if (spec == null) Add(rows, CkTitle, "A3 spec", SelfTestStatus.Skip, "no A3 family in STING_TITLE_BLOCKS.json");
                else
                {
                    var resolved = TitleBlockSpecRegistry.Resolve(lib, spec);
                    var rft = resolved?.TemplateRft ?? "";
                    Add(rows, CkTitle, $"{spec.Id} template", rft.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0
                        ? SelfTestStatus.Pass : SelfTestStatus.Fail, $"resolves to '{(rft.Length > 0 ? rft : "(none)")}'");
                }
            }

            // A schedule placed in a slot: its point is the top-left (DTW-151).
            var schedule = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule);
            if (schedule == null) { Add(rows, CkTitle, "Schedule in a slot", SelfTestStatus.Skip, "the model has no schedule"); return; }

            const double mmPerFt = 304.8;
            var sp = new SlotPlacement { Center = new XYZ(400 / mmPerFt, 300 / mmPerFt, 0), WidthFt = 300 / mmPerFt, HeightFt = 200 / mmPerFt };
            double left = sp.Center.X - sp.WidthFt / 2, right = sp.Center.X + sp.WidthFt / 2;
            double bottom = sp.Center.Y - sp.HeightFt / 2, top = sp.Center.Y + sp.HeightFt / 2;
            BoundingBoxXYZ bb = null;
            var warnings = new List<string>();
            InTx(doc, "schedule in slot", () =>
            {
                var sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                var ssi = SheetPlacementBridge.PlaceScheduleInSlot(doc, sheet.Id, schedule, sp, null, warnings);
                doc.Regenerate();
                bb = ssi?.get_BoundingBox(sheet);
            });
            if (bb == null) { Add(rows, CkTitle, $"Schedule '{schedule.Name}' in a slot", SelfTestStatus.Fail, "placed, but has no extent on the sheet"); return; }
            double x = bb.Min.X, y = bb.Max.Y;
            bool inside = DrawingSelfTestModel.TopLeftInside(x, y, left, right, bottom, top, 2 / mmPerFt);
            Add(rows, CkTitle, $"Schedule '{schedule.Name}' in a slot", inside ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                $"top-left at ({x * mmPerFt:0}, {y * mmPerFt:0}) mm; slot top-left ({left * mmPerFt:0}, {top * mmPerFt:0}), "
                + $"{sp.WidthFt * mmPerFt:0} x {sp.HeightFt * mmPerFt:0} mm"
                + (warnings.Count > 0 ? "; " + string.Join("; ", warnings) : ""));
        }

        // ── b. Stamp round-trip + idempotent production ──────────────────────

        private static void CheckStampAndRerun(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var need = NeedsPlan(ctx) ?? (ctx.PlanType == null ? "no plan drawing type in the catalogue" : null);
            if (need != null) { Add(rows, CkStamp, "(all)", SelfTestStatus.Skip, need); return; }
            var dt = ctx.PlanType;

            string readBack = null, ctxBack = null;
            bool stamped = false;
            InTx(doc, "stamp round-trip", () =>
            {
                var v = NewPlan(ctx);
                stamped = DrawingTypeStamper.Stamp(v, dt.Id);
                readBack = DrawingTypeStamper.Read(v);
                var p = v.LookupParameter("STING_VIEW_CONTEXT_TAG_TXT");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) { p.Set("SELFTEST"); ctxBack = p.AsString(); }
            });
            Add(rows, CkStamp, "STING_DRAWING_TYPE_ID_TXT round-trip",
                stamped && readBack == dt.Id ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                stamped ? $"wrote '{dt.Id}', read '{readBack ?? "(nothing)"}'" : "Stamp returned false — the parameter is not on the view or not writable");
            Add(rows, CkStamp, "STING_VIEW_CONTEXT_TAG_TXT round-trip",
                ctxBack == "SELFTEST" ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                ctxBack == null ? "not on the view, or not a writable text parameter" : $"read '{ctxBack}'");

            // Production twice: the second run must make nothing.
            var opts = new ProduceOptions { RunAnnotation = false };
            var before = Counts(doc);
            var w1 = new List<string>();
            int v1 = 0, s1 = 0;
            Produce(doc, dt, ctx.Level, opts, ref v1, ref s1, w1);
            var after1 = Counts(doc);
            if (v1 == 0)
            {
                Add(rows, CkStamp, $"Produce {dt.Id} on '{ctx.Level.Name}'", SelfTestStatus.Fail,
                    "produced no view" + (w1.Count > 0 ? ": " + string.Join("; ", w1.Take(5)) : ""));
                return;
            }
            ctx.Produced = true;
            Add(rows, CkStamp, $"Produce {dt.Id} on '{ctx.Level.Name}'", SelfTestStatus.Pass,
                $"{v1} view(s) produced or reused, {s1} new sheet(s); model +{after1.Views - before.Views} view(s), +{after1.Sheets - before.Sheets} sheet(s)"
                + (w1.Count > 0 ? "; notes: " + string.Join("; ", w1.Take(3)) : ""));

            var w2 = new List<string>();
            int v2 = 0, s2 = 0;
            Produce(doc, dt, ctx.Level, opts, ref v2, ref s2, w2);
            var after2 = Counts(doc);
            bool same = after2.Views == after1.Views && after2.Sheets == after1.Sheets;
            Add(rows, CkStamp, "Re-run is idempotent", same && s2 == 0 ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                same && s2 == 0
                    ? $"second run created no view and no sheet ({v2} view(s) reused)"
                    : $"second run added {after2.Views - after1.Views} view(s) and {after2.Sheets - after1.Sheets} sheet(s)"
                      + (w2.Count > 0 ? "; notes: " + string.Join("; ", w2.Take(3)) : ""));
        }

        private static void Produce(Document doc, DrawingType dt, Level level, ProduceOptions opts,
            ref int views, ref int sheets, List<string> warnings)
        {
            DrawingTypePresentation.Prewarm(doc);
            using (DrawingProducer.PrimeBatchScope(doc))
                ProduceViewsPerLevelCommand.Produce(doc, new List<DrawingType> { dt }, new List<Level> { level },
                    opts, null, ref views, ref sheets, warnings);
        }

        private static (int Views, int Sheets) Counts(Document doc)
        {
            int views = 0, sheets = 0;
            foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(View)))
            {
                if (!(e is View v) || v.IsTemplate) continue;
                if (v is ViewSheet) sheets++; else views++;
            }
            return (views, sheets);
        }

        // ── h. Worksharing ───────────────────────────────────────────────────

        private static void CheckWorksharing(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            if (!doc.IsWorkshared) { Add(rows, CkWorkshar, "Pre-flight", SelfTestStatus.Skip, "not workshared"); return; }
            if (!ctx.Produced || ctx.PlanType == null || ctx.Level == null)
            { Add(rows, CkWorkshar, "Pre-flight", SelfTestStatus.Skip, "check b produced nothing to pre-flight"); return; }

            var pf = WorksharingPreflight.For(doc);
            var block = pf.CheckItem(new[] { ctx.PlanType }, new DrawingContext { Level = ctx.Level });
            Add(rows, CkWorkshar, $"Pre-flight {ctx.PlanType.Id} on '{ctx.Level.Name}'",
                block == null ? SelfTestStatus.Pass : SelfTestStatus.Info,
                block == null ? "every element the item would edit is editable by you" : "would be skipped: " + block);
            var counters = pf.SheetCountersNote();
            Add(rows, CkWorkshar, "Sheet-number counters (Project Information)", counters == null ? SelfTestStatus.Pass : SelfTestStatus.Info,
                counters ?? "editable by you and current with central");
        }

        // ── c. Managed V/G ───────────────────────────────────────────────────

        private static void CheckManagedVg(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var need = NeedsPlan(ctx);
            if (need != null) { Add(rows, CkManaged, "(all)", SelfTestStatus.Skip, need); return; }

            ViewStylePack pack = ctx.PlanType != null ? ViewStylePackRegistry.ResolveForDrawingType(doc, ctx.PlanType) : null;
            if (pack == null || !pack.IsManaged) pack = ViewStylePackRegistry.ListAll(doc).FirstOrDefault(p => p.IsManaged);
            if (pack == null) { Add(rows, CkManaged, "(all)", SelfTestStatus.Skip, "no style pack is in managed mode"); return; }

            var result = new PackApplyResult();
            View template = null;
            ElementId assigned = ElementId.InvalidElementId;
            InTx(doc, "managed template", () =>
            {
                var tid = ManagedTemplateSyncer.EnsureTemplate(doc, pack, ViewType.FloorPlan, result);
                template = doc.GetElement(tid) as View;
                if (template == null) return;
                var v = NewPlan(ctx);
                v.ViewTemplateId = tid;
                assigned = v.ViewTemplateId;
            });
            if (template == null)
            {
                Add(rows, CkManaged, $"Template for '{pack.Id}'", SelfTestStatus.Fail,
                    "EnsureTemplate made no template" + (result.Warnings.Count > 0 ? ": " + string.Join("; ", result.Warnings.Take(3)) : ""));
                return;
            }
            Add(rows, CkManaged, $"Template '{template.Name}' assigned", assigned == template.Id ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                assigned == template.Id ? "a new plan carries it" : "the view did not keep the template");

            var all = new HashSet<long>(template.GetTemplateParameterIds().Select(i => i.Value));
            var free = new HashSet<long>(template.GetNonControlledTemplateParameterIds().Select(i => i.Value));
            foreach (var bip in new[] { BuiltInParameter.VIS_GRAPHICS_MODEL, BuiltInParameter.VIS_GRAPHICS_FILTERS })
            {
                long id = (long)bip;
                bool controlled = all.Contains(id) && !free.Contains(id);
                Add(rows, CkManaged, bip.ToString(), controlled ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                    controlled ? "controlled by the template" : (all.Contains(id) ? "not controlled (include is off)" : "not a template parameter of this template"));
            }
            var scaleIds = new[] { BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC, BuiltInParameter.VIEW_SCALE_PULLDOWN_IMPERIAL, BuiltInParameter.VIEW_SCALE }
                .Where(b => all.Contains((long)b)).Select(b => b + (free.Contains((long)b) ? " (not controlled)" : " (controlled)")).ToList();
            Add(rows, CkManaged, "Scale parameter id (round 8 question)", SelfTestStatus.Info,
                scaleIds.Count > 0 ? "GetTemplateParameterIds has " + string.Join(", ", scaleIds) : "none of VIEW_SCALE / VIEW_SCALE_PULLDOWN_* is a template parameter");

            var onTemplate = new HashSet<string>(template.GetFilters().Select(i => doc.GetElement(i)?.Name).Where(n => n != null),
                StringComparer.OrdinalIgnoreCase);
            var wanted = (pack.Filters ?? new List<StyleFilterRule>()).Select(f => f.FilterName)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (wanted.Count == 0)
                Add(rows, CkManaged, $"'{pack.Id}' filters on the template", SelfTestStatus.Skip, "the pack names no filters");
            else
            {
                var missing = wanted.Where(n => !onTemplate.Contains(n)).ToList();
                Add(rows, CkManaged, $"'{pack.Id}' filters on the template", missing.Count == 0 ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                    $"{wanted.Count - missing.Count} of {wanted.Count} present"
                    + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing.Take(8)) + (missing.Count > 8 ? " …" : "") : "")
                    + (result.Warnings.Count > 0 && missing.Count > 0 ? "; pack warnings: " + string.Join("; ", result.Warnings.Take(3)) : ""));
            }
        }

        // ── d. AEC filters ───────────────────────────────────────────────────

        private static void CheckFilters(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            foreach (var id in DrawingSelfTestModel.RepresentativeFilters)
            {
                var def = AecFilterRegistry.Get(doc, id);
                if (def == null) { Add(rows, CkFilters, id, SelfTestStatus.Fail, "not in STING_AEC_FILTERS.json / the project override"); continue; }
                FilterFactoryResult res = null;
                try
                {
                    var revitNotes = InTx(doc, "filter " + id, () => res = AecFilterFactory.FindOrCreate(doc, def));
                    var notes = res.Warnings.Concat(revitNotes).ToList();
                    Add(rows, CkFilters, $"{id} ('{def.Name}')", res.Ok ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                        (res.Ok ? (res.Created ? "created" : res.Updated ? "rebuilt" : "already present") : "REVIT REFUSED: " + res.Error)
                        + (notes.Count > 0 ? "; " + string.Join("; ", notes.Take(3)) : ""));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Drawing Self-Test filter {id}: {ex.Message}");
                    Add(rows, CkFilters, $"{id} ('{def.Name}')", SelfTestStatus.Fail, "REVIT REFUSED: " + ex.Message);
                }
            }

            // Round 8: which built-in parameters Revit offers as filter rules.
            FilterableRow(doc, rows, BuiltInCategory.OST_Walls, "Walls", BuiltInParameter.PHASE_CREATED, BuiltInParameter.PHASE_DEMOLISHED, BuiltInParameter.STRUCTURAL_MATERIAL_TYPE);
            FilterableRow(doc, rows, BuiltInCategory.OST_Floors, "Floors", BuiltInParameter.STRUCTURAL_MATERIAL_TYPE);
        }

        private static void FilterableRow(Document doc, List<SelfTestRow> rows, BuiltInCategory bic, string label, params BuiltInParameter[] bips)
        {
            var filterable = new HashSet<long>(ParameterFilterUtilities
                .GetFilterableParametersInCommon(doc, new List<ElementId> { new ElementId(bic) }).Select(i => i.Value));
            var parts = bips.Select(b => $"{b}: {(filterable.Contains((long)b) ? "yes" : "no")}");
            Add(rows, CkFilters, $"Filterable on {label} (round 8 question)", SelfTestStatus.Info, string.Join("; ", parts));
        }

        // ── i. Match lines ───────────────────────────────────────────────────

        private static void CheckMatchLines(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            int boxes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Count(e => (e.Name ?? "").StartsWith(ScopeBoxNames.AreaPrefix, StringComparison.OrdinalIgnoreCase));
            if (boxes < 2) { Add(rows, CkMatch, "Generate twice", SelfTestStatus.Skip, $"{boxes} STING-AREA box(es); two are needed"); return; }

            var r1 = MatchLineEngine.Run(doc);
            int c1 = AnnotationCount(doc);
            if (r1.AdjacencyEdgesFound == 0)
            { Add(rows, CkMatch, "Generate twice", SelfTestStatus.Skip, $"{boxes} STING-AREA boxes, none adjacent — no match line to place"); return; }
            var r2 = MatchLineEngine.Run(doc);
            int c2 = AnnotationCount(doc);
            var errors = r1.Errors.Concat(r2.Errors).ToList();
            bool ok = c1 == c2 && r2.PairsCreated == 0 && errors.Count == 0;
            Add(rows, CkMatch, "Generate twice", ok ? SelfTestStatus.Pass : SelfTestStatus.Fail,
                $"first run: {r1.PairsCreated} pair(s) created, {r1.PairsUpdated} updated; second run: {r2.PairsCreated} created, "
                + $"{r2.PairsUpdated} updated; detail curves + notes {c1} → {c2}"
                + (errors.Count > 0 ? "; errors: " + string.Join("; ", errors.Take(3)) : ""));
        }

        private static int AnnotationCount(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).GetElementCount()
             + new FilteredElementCollector(doc).OfClass(typeof(TextNote)).GetElementCount();

        // ── j. Crop and view-scoped collectors (round 8 question) ────────────

        private static void CheckCropCollector(Ctx ctx, List<SelfTestRow> rows)
        {
            var doc = ctx.Doc;
            var need = NeedsPlan(ctx);
            if (need != null) { Add(rows, CkCrop, "View-scoped collector vs crop", SelfTestStatus.Skip, need); return; }
            int uncropped = 0, cropped = 0;
            InTx(doc, "crop probe", () =>
            {
                var v = NewPlan(ctx);
                v.CropBoxActive = false;
                doc.Regenerate();
                uncropped = new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().GetElementCount();
                var cb = v.CropBox;
                var mid = (cb.Min + cb.Max) / 2;
                var half = (cb.Max - cb.Min) * 0.01;
                v.CropBox = new BoundingBoxXYZ { Transform = cb.Transform, Min = mid - half, Max = mid + half };
                v.CropBoxActive = true;
                doc.Regenerate();
                cropped = new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().GetElementCount();
            });
            if (uncropped == 0) { Add(rows, CkCrop, "View-scoped collector vs crop", SelfTestStatus.Skip, $"nothing visible on '{ctx.Level.Name}'"); return; }
            Add(rows, CkCrop, "View-scoped collector vs crop", SelfTestStatus.Info,
                $"{uncropped} element(s) uncropped, {cropped} with a crop 2% of the view's size: a view-scoped collector "
                + (cropped < uncropped ? "DOES exclude elements outside the crop" : "does NOT exclude elements outside the crop"));
        }

        // ── Output ───────────────────────────────────────────────────────────

        /// <summary>Caches holding ids of elements the rollback removed.</summary>
        private static void DropCaches(Document doc)
        {
            try
            {
                DrawingProducer.ResetBatchCaches();
                ManagedTemplateSyncer.InvalidateCache(doc);
                ViewStylePackApplier.InvalidateCache(doc);
                ViewStylePackApplier.InvalidateMaterialClassFilterCache();
            }
            catch (Exception ex) { StingLog.Warn($"Drawing Self-Test cache reset: {ex.Message}"); }
        }

        /// <summary>The CSV path, or null when it could not be written (logged).</summary>
        private static string WriteCsv(Document doc, List<SelfTestRow> rows)
        {
            try
            {
                var path = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Validation", "STING_DrawingSelfTest", ".csv");
                File.WriteAllText(path, DrawingSelfTestModel.ToCsv(rows, doc.Title, DateTime.Now), new System.Text.UTF8Encoding(true));
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Drawing Self-Test CSV: {ex.Message}");
                rows.Add(new SelfTestRow("Output", "CSV", SelfTestStatus.Fail, "could not be written: " + ex.Message));
                return null;
            }
        }

        private static StingResultPanel.Builder BuildPanel(Document doc, List<SelfTestRow> rows, string csvPath)
        {
            int pass = DrawingSelfTestModel.Count(rows, SelfTestStatus.Pass);
            int fail = DrawingSelfTestModel.Count(rows, SelfTestStatus.Fail);
            int skip = DrawingSelfTestModel.Count(rows, SelfTestStatus.Skip);
            int info = DrawingSelfTestModel.Count(rows, SelfTestStatus.Info);

            var b = StingResultPanel.Create(Title)
                .SetSubtitle($"{doc.Title} — {DrawingSelfTestModel.Summary(rows)}")
                .Alert("ROLLED BACK — " + DrawingSelfTestModel.RollbackHeader)
                .AddSection("Summary")
                .Metric("Pass", pass.ToString())
                .Metric("Fail", fail.ToString())
                .Metric("Skip", skip.ToString(), "the model lacks what the check needs")
                .Metric("Info", info.ToString(), "answers to open design questions");
            if (csvPath != null) b.SetCsvPath(csvPath);
            if (pass + fail > 0) b.SetOverallPct(100.0 * pass / (pass + fail));

            foreach (var g in rows.GroupBy(r => r.Check))
            {
                b.AddSection(g.Key);
                foreach (var r in g)
                {
                    if (r.Status == SelfTestStatus.Pass || r.Status == SelfTestStatus.Fail)
                        b.PassFail(r.Item, r.Status == SelfTestStatus.Pass, r.Detail);
                    else
                        b.Text($"{DrawingSelfTestModel.StatusText(r.Status)}  {r.Item} — {r.Detail}");
                }
            }
            return b;
        }
    }
}
