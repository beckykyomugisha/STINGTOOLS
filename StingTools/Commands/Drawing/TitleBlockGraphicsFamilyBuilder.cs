// StingTools — Work Item D · Annotation family builder
//
// Builds the 2 minimal parametric Generic-Annotation families the slot
// graphics placers (Work Item C) load via TitleBlockGraphicsRegistry:
//
//   STING_TB_NorthArrow   — arrow shaft + head; instance "Rotation Angle".
//   STING_TB_KeyPlanBase  — outline rectangle + a highlight filled region.
//
// (G3-b: the scale bar is no longer a nested family — it is drawn as
// auto-scaling in-view detail lines by TitleBlock_PlaceScaleBar, so
// STING_TB_ScaleBar is not built here.)
//
// The families are authored programmatically (Application.NewFamilyDocument
// from a Generic Annotation .rft + famDoc.FamilyCreate detail curves +
// FamilyManager params) and saved to <project|addin>/Families/Annotations/,
// which is where TitleBlockGraphicsRegistry searches. Run once in Revit;
// the placement commands then find the .rfa. Until then those commands skip
// cleanly (no synthetic geometry) — no runtime dependency is broken.
//
// NOTE: geometry is intentionally minimal — a designer can refine each
// family (arrowhead style, tick spacing, dimension-locked flexing) in the
// Family Editor without breaking the family names / params the registry and
// placers rely on.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockBuildGraphicsFamiliesCommand : IExternalCommand
    {
        private const double MmPerFoot = 304.8;
        private static double Mm(double mm) => mm / MmPerFoot;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = ParameterHelpers.GetApp(data);
            if (uiApp == null) { msg = "No Revit application."; return Result.Failed; }
            var app = uiApp.Application;
            var doc = uiApp.ActiveUIDocument?.Document;

            string template = ResolveAnnotationTemplate(app);
            if (template == null)
            {
                TaskDialog.Show("STING — Build Graphics Families",
                    "Could not locate a 'Generic Annotation.rft' template under Revit's family "
                    + "template path:\n\n" + (app.FamilyTemplatePath ?? "(null)")
                    + "\n\nInstall the Revit content libraries, or drop the 3 .rfa files into "
                    + "Families/Annotations/ by hand (see the README there).");
                return Result.Failed;
            }

            string outDir = ResolveOutputDir(doc);
            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex) { msg = $"Cannot create {outDir}: {ex.Message}"; return Result.Failed; }

            // G3-b: the scale bar is no longer a family (it is drawn as auto-scaling
            // in-view detail lines), so only the north arrow + key-plan base build here.
            var report = new List<string>();
            report.Add(BuildOne(app, doc, template, outDir, "STING_TB_NorthArrow",  BuildNorthArrow));
            report.Add(BuildOne(app, doc, template, outDir, "STING_TB_KeyPlanBase", BuildKeyPlanBase));

            TaskDialog.Show("STING — Build Graphics Families",
                $"Output: {outDir}\n\n" + string.Join("\n", report)
                + "\n\nNow run TitleBlock_PlaceNorthArrow / PlaceKeyPlan. (The scale bar is drawn\n"
                + "directly in-view by TitleBlock_PlaceScaleBar — no family needed.)");
            return Result.Succeeded;
        }

        private string BuildOne(Application app, Document projDoc, string template, string outDir,
            string familyName, Action<Document> author)
        {
            Document fam = null;
            try
            {
                fam = app.NewFamilyDocument(template);
                if (fam == null) return $"[FAIL] {familyName} — NewFamilyDocument returned null";

                int curveCount;
                using (var tx = new Transaction(fam, "STING author annotation"))
                {
                    tx.Start();
                    try { author(fam); }
                    catch (Exception ex) { StingLog.Warn($"{familyName} geometry: {ex.Message}"); }
                    curveCount = CountGeometry(fam);
                    tx.Commit();
                }

                // G3-c: don't report success when the annotation geometry factory
                // silently produced nothing — a family with zero authored curves is
                // a FAILURE, not [OK].
                if (curveCount == 0)
                {
                    try { fam.Close(false); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                    fam = null;
                    return $"[FAIL] {familyName} — 0 geometry curves authored (check the annotation geometry factory)";
                }

                string path = Path.Combine(outDir, familyName + ".rfa");
                fam.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                fam.Close(false); fam = null;

                if (projDoc != null)
                {
                    using (var tx = new Transaction(projDoc, $"STING Load {familyName}"))
                    {
                        tx.Start();
                        projDoc.LoadFamily(path, new TitleBlockFamilyLoadOptions(), out _);
                        tx.Commit();
                    }
                }
                return $"[OK]   {familyName} ({curveCount} curves) -> {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StingLog.Error($"Build {familyName}: {ex.Message}", ex);
                try { fam?.Close(false); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                return $"[FAIL] {familyName} — {ex.Message}";
            }
        }

        // --- geometry authors (run inside the family doc's transaction) ---

        private void BuildNorthArrow(Document fam)
        {
            var v = fam.ActiveView;
            double h = Mm(12), w = Mm(4);
            // shaft
            AddLine(fam, v, new XYZ(0, -h / 2, 0), new XYZ(0, h / 2, 0));
            // arrow head (two strokes)
            AddLine(fam, v, new XYZ(0, h / 2, 0), new XYZ(-w / 2, h / 2 - w, 0));
            AddLine(fam, v, new XYZ(0, h / 2, 0), new XYZ(w / 2, h / 2 - w, 0));
            // G3-d: the placer orients the arrow via the plan view's north (in-view
            // path) or an ElementTransformUtils rotation (sheet fallback), so the
            // old "Rotation Angle" family param drove nothing and was removed.
            AddText(fam, v, new XYZ(0, -h / 2 - Mm(3), 0), "N");
        }

        private void BuildKeyPlanBase(Document fam)
        {
            var v = fam.ActiveView;
            double w = Mm(40), h = Mm(30);
            // outline
            AddLine(fam, v, new XYZ(0, 0, 0), new XYZ(w, 0, 0));
            AddLine(fam, v, new XYZ(w, 0, 0), new XYZ(w, h, 0));
            AddLine(fam, v, new XYZ(w, h, 0), new XYZ(0, h, 0));
            AddLine(fam, v, new XYZ(0, h, 0), new XYZ(0, 0, 0));
            // highlight region (a quadrant) — filled region if a type exists
            try
            {
                var frType = new FilteredElementCollector(fam).OfClass(typeof(FilledRegionType))
                    .OfType<FilledRegionType>().Select(t => t.Id).FirstOrDefault();
                if (frType != null && frType != ElementId.InvalidElementId)
                {
                    var loop = new CurveLoop();
                    var p0 = new XYZ(w / 2, h / 2, 0); var p1 = new XYZ(w, h / 2, 0);
                    var p2 = new XYZ(w, h, 0);         var p3 = new XYZ(w / 2, h, 0);
                    loop.Append(Line.CreateBound(p0, p1)); loop.Append(Line.CreateBound(p1, p2));
                    loop.Append(Line.CreateBound(p2, p3)); loop.Append(Line.CreateBound(p3, p0));
                    FilledRegion.Create(fam, frType, v.Id, new List<CurveLoop> { loop });
                }
            }
            catch (Exception ex) { StingLog.Warn($"KeyPlan highlight: {ex.Message}"); }
        }

        // --- helpers ---

        /// <summary>G3-c: draw one line into the annotation family. Generic
        /// Annotation geometry is authored with FamilyItemFactory.NewSymbolicCurve
        /// (2D symbolic line) — the correct factory for annotation symbols; falls
        /// back to NewDetailCurve. Failures are logged and later surfaced by the
        /// zero-geometry FAIL check.</summary>
        private static void AddLine(Document fam, View v, XYZ a, XYZ b)
        {
            var line = Autodesk.Revit.DB.Line.CreateBound(a, b);
            // Preferred: symbolic curve on the family's active-view sketch plane.
            try
            {
                var sp = v.SketchPlane ?? SketchPlane.Create(fam, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                fam.FamilyCreate.NewSymbolicCurve(line, sp);
                return;
            }
            catch (Exception ex) { StingLog.Warn($"symbolic line: {ex.Message}"); }
            // Fallback: detail curve in the view.
            try { fam.FamilyCreate.NewDetailCurve(v, line); }
            catch (Exception ex) { StingLog.Warn($"detail line: {ex.Message}"); }
        }

        /// <summary>Count the visible 2D geometry authored in the family
        /// (symbolic/detail curves + filled regions). Zero ⇒ the factory rejected
        /// everything ⇒ the build should report FAILURE, not [OK].</summary>
        private static int CountGeometry(Document fam)
        {
            try
            {
                int curves = new FilteredElementCollector(fam).OfClass(typeof(CurveElement)).GetElementCount();
                int regions = new FilteredElementCollector(fam).OfClass(typeof(FilledRegion)).GetElementCount();
                return curves + regions;
            }
            catch (Exception ex) { StingLog.Warn($"CountGeometry: {ex.Message}"); return 0; }
        }

        private static void AddText(Document fam, View v, XYZ p, string s)
        {
            try
            {
                var tnt = new FilteredElementCollector(fam).OfClass(typeof(TextNoteType))
                    .OfType<TextNoteType>().Select(t => t.Id).FirstOrDefault();
                if (tnt != null && tnt != ElementId.InvalidElementId)
                    TextNote.Create(fam, v.Id, p, s, tnt);
            }
            catch (Exception ex) { StingLog.Warn($"text note: {ex.Message}"); }
        }

        private static string ResolveAnnotationTemplate(Application app)
        {
            string basePath = app.FamilyTemplatePath;
            var names = new[]
            {
                "Generic Annotation.rft", "Metric Generic Annotation.rft",
                "Generic Annotation (Imperial).rft",
            };
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(basePath)) roots.Add(basePath);
            foreach (var root in roots)
            {
                foreach (var n in names)
                {
                    try
                    {
                        var direct = Path.Combine(root, n);
                        if (File.Exists(direct)) return direct;
                        var hit = Directory.GetFiles(root, n, SearchOption.AllDirectories).FirstOrDefault();
                        if (hit != null) return hit;
                    }
                    catch (Exception ex) { StingLog.Warn($"template search: {ex.Message}"); }
                }
            }
            return null;
        }

        private static string ResolveOutputDir(Document doc)
        {
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    var d = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(d)) return Path.Combine(d, "Families", "Annotations");
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveOutputDir prj: {ex.Message}"); }
            try
            {
                var asm = Path.GetDirectoryName(StingToolsApp.AssemblyPath);
                if (!string.IsNullOrEmpty(asm)) return Path.Combine(asm, "Families", "Annotations");
            }
            catch (Exception ex) { StingLog.Warn($"ResolveOutputDir asm: {ex.Message}"); }
            return Path.Combine(Path.GetTempPath(), "STING", "Families", "Annotations");
        }
    }
}
