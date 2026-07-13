// StingTools — Work Item D · Annotation family builder
//
// Builds the 3 minimal parametric Generic-Annotation families the slot
// graphics placers (Work Item C) load via TitleBlockGraphicsRegistry:
//
//   STING_TB_NorthArrow   — arrow shaft + head; instance "Rotation Angle".
//   STING_TB_ScaleBar     — divided graphic bar; instance "Scale" (int) +
//                           formula-linked "Bar Length".
//   STING_TB_KeyPlanBase  — outline rectangle + a highlight filled region.
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

            var report = new List<string>();
            report.Add(BuildOne(app, doc, template, outDir, "STING_TB_NorthArrow",  BuildNorthArrow));
            report.Add(BuildOne(app, doc, template, outDir, "STING_TB_ScaleBar",    BuildScaleBar));
            report.Add(BuildOne(app, doc, template, outDir, "STING_TB_KeyPlanBase", BuildKeyPlanBase));

            TaskDialog.Show("STING — Build Graphics Families",
                $"Output: {outDir}\n\n" + string.Join("\n", report)
                + "\n\nNow run TitleBlock_PlaceNorthArrow / PlaceScaleBar / PlaceKeyPlan.");
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

                using (var tx = new Transaction(fam, "STING author annotation"))
                {
                    tx.Start();
                    try { author(fam); }
                    catch (Exception ex) { StingLog.Warn($"{familyName} geometry: {ex.Message}"); }
                    tx.Commit();
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
                return $"[OK]   {familyName} -> {Path.GetFileName(path)}";
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
            AddInstanceParam(fam, "Rotation Angle", SpecTypeId.Angle);
            AddText(fam, v, new XYZ(0, -h / 2 - Mm(3), 0), "N");
        }

        private void BuildScaleBar(Document fam)
        {
            var v = fam.ActiveView;
            double len = Mm(50), ht = Mm(3);
            // frame
            AddLine(fam, v, new XYZ(0, 0, 0), new XYZ(len, 0, 0));
            AddLine(fam, v, new XYZ(0, ht, 0), new XYZ(len, ht, 0));
            AddLine(fam, v, new XYZ(0, 0, 0), new XYZ(0, ht, 0));
            AddLine(fam, v, new XYZ(len, 0, 0), new XYZ(len, ht, 0));
            // 5 divisions
            for (int i = 1; i < 5; i++)
                AddLine(fam, v, new XYZ(len * i / 5.0, 0, 0), new XYZ(len * i / 5.0, ht, 0));

            var scale  = AddInstanceParam(fam, "Scale", SpecTypeId.Int.Integer);
            var barLen = AddInstanceParam(fam, "Bar Length", SpecTypeId.Length);
            // formula: 50 mm bar represents (Scale * 50 mm) at real world; expose
            // the represented length so a designer can dimension-drive ticks.
            try { if (scale != null && barLen != null) fam.FamilyManager.SetFormula(barLen, "Scale * 1 mm"); }
            catch (Exception ex) { StingLog.Warn($"ScaleBar formula: {ex.Message}"); }
            AddText(fam, v, new XYZ(0, ht + Mm(1), 0), "SCALE");
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

        private static void AddLine(Document fam, View v, XYZ a, XYZ b)
        {
            try { fam.FamilyCreate.NewDetailCurve(v, Autodesk.Revit.DB.Line.CreateBound(a, b)); }
            catch (Exception ex) { StingLog.Warn($"detail line: {ex.Message}"); }
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

        private static FamilyParameter AddInstanceParam(Document fam, string name, ForgeTypeId spec)
        {
            try
            {
                var existing = fam.FamilyManager.get_Parameter(name);
                if (existing != null) return existing;
                return fam.FamilyManager.AddParameter(name, GroupTypeId.Graphics, spec, true);
            }
            catch (Exception ex) { StingLog.Warn($"AddParameter {name}: {ex.Message}"); return null; }
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
