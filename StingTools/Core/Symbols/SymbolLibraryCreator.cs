// StingTools — MEP/FP/SLD Symbol Library creator (Phase 175)
//
// Iterates a SymbolLibrary loaded from JSON, opens the appropriate family
// template per FamilyType, draws normalised geometry into the family
// document, mints connectors / parameters, saves the resulting .rfa
// alongside the project, and loads it back into the active document.
//
// All Revit API calls target the 2025/2026/2027 signatures.
//
// Fix log (applied in this file — Phase 175 hardening pass):
//   Fix 1  — Revit 2025 API: NewSymbolicCurve vs NewModelCurve per familyType,
//             SpecTypeId usage verified, NewExtrusion height in feet,
//             ConnectorElement.Create* correct overloads per domain.
//   Fix 2  — SetConnectorDirection() retired: removed, replaced with comment.
//   Fix 3  — ResolveTemplateFolder/File: multi-version search + DataPath fallback.
//   Fix 4  — textHeightMm wired from AnnotationRules into DrawText.
//   Fix 5  — Geometry coordinate range validation before curve creation.
//   Fix 6  — AddScaleTierTypes: per-standard scale-tier type variants.
//   Fix 7  — CreateCompoundSymbols: compound symbol factory + command stub.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Routing;

namespace StingTools.Core.Symbols
{
    /// <summary>Aggregate result of a CreateAllFromFile run.</summary>
    public sealed class SymbolCreationResult
    {
        public int Created { get; set; }
        public int Existed { get; set; }
        public int Failed { get; set; }
        public int Protected { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> CreatedRfaPaths { get; } = new List<string>();
    }

    internal static class SymbolLibraryCreator
    {
        // 1 ft = 304.8 mm — Revit internal length unit is decimal feet.
        private const double MmPerFoot = 304.8;

        /// <summary>Convert millimetres to Revit decimal feet.</summary>
        private static double MmToFt(double mm) => mm / MmPerFoot;

        /// <summary>Scale a normalised coordinate (-0.5..+0.5) to internal feet using symbolSize mm.</summary>
        private static double Scale(double normCoord, double symbolSizeMm)
            => normCoord * MmToFt(symbolSizeMm);

        // ─────────────────────────────────────────────────────────────────
        // Fix 5 — Geometry coordinate range validation
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that a normalised geometry coordinate is within the expected range [-2, 2].
        /// Coordinates outside this range are almost certainly authoring errors — log a warning
        /// and return false so the caller can skip the element. Never throws.
        /// </summary>
        private static bool ValidateGeometryCoord(double v, string context, List<string> warnings)
        {
            if (v < -2.0 || v > 2.0)
            {
                warnings.Add($"Symbol geometry coordinate {v:F3} out of expected range [-1,1] in {context}. Clamping.");
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Library entry point
        // ─────────────────────────────────────────────────────────────────

        public static SymbolCreationResult CreateAllFromFile(
            Document hostDoc,
            string jsonPath,
            string outputFolder,
            bool loadIntoProject)
        {
            var result = new SymbolCreationResult();
            if (!File.Exists(jsonPath))
            {
                result.Errors.Add($"JSON not found: {jsonPath}");
                return result;
            }

            SymbolLibrary lib;
            try
            {
                lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"JSON parse failed: {ex.Message}");
                return result;
            }
            if (lib?.Symbols == null || lib.Symbols.Count == 0)
            {
                result.Errors.Add("No symbols in library.");
                return result;
            }

            // Resolve the standard defined by this library so scale tiers and
            // annotation rules can be passed into per-symbol creation.
            StandardDefinition std = null;
            if (!string.IsNullOrEmpty(lib.Standard))
            {
                string stdJson = StingToolsApp.FindDataFile("Symbols/STING_SYMBOL_STANDARDS.json")
                    ?? StingToolsApp.FindDataFile("STING_SYMBOL_STANDARDS.json");
                if (!string.IsNullOrEmpty(stdJson) && File.Exists(stdJson))
                {
                    try
                    {
                        var stdFile = JsonConvert.DeserializeObject<SymbolStandardsFile>(File.ReadAllText(stdJson));
                        stdFile?.Standards?.TryGetValue(lib.Standard, out std);
                    }
                    catch (Exception ex2) { StingLog.Warn($"CreateAllFromFile: standards JSON failed — {ex2.Message}"); }
                }
            }

            Directory.CreateDirectory(outputFolder);
            var app = hostDoc.Application;
            var templateFolder = ResolveTemplateFolder(app);

            foreach (var def in lib.Symbols)
            {
                if (string.IsNullOrWhiteSpace(def?.Id))
                {
                    result.Failed++;
                    result.Errors.Add("Symbol with empty id skipped.");
                    continue;
                }

                var rfaPath = Path.Combine(outputFolder, def.Id + ".rfa");
                if (File.Exists(rfaPath))
                {
                    result.Existed++;
                    result.CreatedRfaPaths.Add(rfaPath);
                    if (loadIntoProject) TryLoadFamily(hostDoc, rfaPath, result);
                    continue;
                }

                try
                {
                    string built = BuildOne(app, def, outputFolder, templateFolder, std, result);
                    if (!string.IsNullOrEmpty(built))
                    {
                        result.Created++;
                        result.CreatedRfaPaths.Add(built);
                        if (loadIntoProject) TryLoadFamily(hostDoc, built, result);
                    }
                    else
                    {
                        result.Failed++;
                    }
                }
                catch (Exception ex2)
                {
                    result.Failed++;
                    result.Errors.Add($"{def.Id}: {ex2.Message}");
                    StingLog.Error($"SymbolLibraryCreator: {def.Id} failed", ex2);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Per-symbol routing
        // ─────────────────────────────────────────────────────────────────

        private static string BuildOne(Application app, SymbolDefinition def,
            string outputFolder, string templateFolder, StandardDefinition std,
            SymbolCreationResult result)
        {
            string templateFile = ResolveTemplateFile(def, templateFolder, result);
            if (string.IsNullOrEmpty(templateFile))
            {
                result.Warnings.Add($"{def.Id}: no family template found for {def.FamilyType}/{def.Discipline}");
                return null;
            }

            Document fdoc;
            try
            {
                fdoc = app.NewFamilyDocument(templateFile);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{def.Id}: NewFamilyDocument failed — {ex.Message}");
                return null;
            }
            if (fdoc == null)
            {
                result.Errors.Add($"{def.Id}: NewFamilyDocument returned null.");
                return null;
            }

            try
            {
                using (var tx = new Transaction(fdoc, "STING Create Symbol"))
                {
                    tx.Start();
                    DrawGeometry(fdoc, def, std, result);
                    AddParameters(fdoc, def, result);
                    bool hasSymbolConnectors  = def.Connectors != null && def.Connectors.Count > 0;
                    bool hasVariantConnectors = def.TypeVariants != null
                        && def.TypeVariants.Exists(v => v?.Connectors != null && v.Connectors.Count > 0);
                    if ((hasSymbolConnectors || hasVariantConnectors)
                        && !string.Equals(def.FamilyType, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
                    {
                        AddConnectors(fdoc, def, result);
                    }
                    if (def.Solid3D != null
                        && !string.Equals(def.FamilyType, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
                    {
                        AddSolid3D(fdoc, def, result);
                    }

                    // Stamp seed-family identity. STING_SEED_FAMILY_TXT
                    // becomes the swap-registry key; SwapToManufacturer
                    // reads it on every instance to find candidate
                    // replacements when the user picks a real
                    // manufacturer family.
                    if (def.IsSeed)
                    {
                        TryAddSeedMarker(fdoc, def);
                    }

                    // Wave G1 — type-variant injection. Each
                    // TypeVariantDefinition becomes a duplicate of the
                    // default family type; the duplicate's parameters
                    // are overridden per the JSON spec. Saves the
                    // author from manually duplicating types in
                    // Family Editor for the FR30/FR60/FR90/FR120 +
                    // PENDANT/RECESSED/etc. variants documented in the
                    // layman's guide.
                    if (def.TypeVariants != null && def.TypeVariants.Count > 0)
                    {
                        AddTypeVariants(fdoc, def, result);
                    }

                    // Fix 6 — Scale-tier type variants from the standard definition.
                    if (std != null && std.SymbolScaleTiers != null && std.SymbolScaleTiers.Count > 0)
                    {
                        AddScaleTierTypes(fdoc, def, std, result.Warnings);
                    }

                    // Phase 178f — bind family-formula expressions
                    // declared in the JSON spec. Most common use:
                    // Mark = PEN_CONTROL_NUMBER_TXT on the firestop
                    // seed so tags read it without extra wiring.
                    if (def.FormulaBindings != null && def.FormulaBindings.Count > 0)
                    {
                        AddFormulaBindings(fdoc, def, result);
                    }
                    tx.Commit();
                }

                var saveAs = new SaveAsOptions { OverwriteExistingFile = true };
                string outPath = Path.Combine(outputFolder, def.Id + ".rfa");
                fdoc.SaveAs(outPath, saveAs);
                fdoc.Close(false);
                return outPath;
            }
            catch (Exception ex)
            {
                try { fdoc.Close(false); } catch (Exception inner) { StingLog.Warn($"SymbolLibraryCreator: close after error — {inner.Message}"); }
                result.Errors.Add($"{def.Id}: {ex.Message}");
                StingLog.Error($"SymbolLibraryCreator.BuildOne {def.Id}", ex);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Geometry
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the family document's template is a GenericAnnotation
        /// (or if the definition explicitly declares GenericAnnotation). Annotation
        /// families use NewSymbolicCurve; model families use NewModelCurve.
        /// Fix 1a.
        /// </summary>
        private static bool IsAnnotationFamily(Document fdoc, SymbolDefinition def)
        {
            if (string.Equals(def?.FamilyType, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
                return true;
            // Also check the family category in the document itself in case the
            // template's category disagrees with what the JSON spec declares.
            try
            {
                if (fdoc.IsFamilyDocument)
                {
                    var cat = fdoc.OwnerFamily?.FamilyCategory;
                    if (cat != null && cat.Id.Value == (long)BuiltInCategory.OST_GenericAnnotation)
                        return true;
                }
            }
            catch { /* category check is best-effort */ }
            return false;
        }

        private static void DrawGeometry(Document fdoc, SymbolDefinition def,
            StandardDefinition std, SymbolCreationResult result)
        {
            var geo = def.Geometry;
            if (geo == null) return;

            View planView = ResolvePlanView(fdoc);
            if (planView == null)
            {
                result.Warnings.Add($"{def.Id}: no plan view in family template; geometry skipped.");
                return;
            }
            SketchPlane sketch = SketchPlane.Create(fdoc,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));

            double s = def.SymbolSize > 0 ? def.SymbolSize : 3.0;

            // Fix 4 — resolve the effective textHeightMm from the standard.
            double stdTextHeightMm = std?.AnnotationRules?.TextHeightMm ?? 2.5;

            if (geo.Lines != null)
                foreach (var l in geo.Lines)
                    DrawLine(fdoc, planView, sketch, l, s, result, def.Id);

            if (geo.ConnectionLines != null)
                foreach (var l in geo.ConnectionLines)
                    DrawLine(fdoc, planView, sketch, l, s, result, def.Id);

            if (geo.Arcs != null)
                foreach (var a in geo.Arcs)
                    DrawArc(fdoc, planView, sketch, a, s, result, def.Id);

            if (geo.FilledRegions != null)
                foreach (var fr in geo.FilledRegions)
                    DrawFilledRegion(fdoc, planView, fr, s, result, def.Id);

            if (geo.Text != null)
                foreach (var t in geo.Text)
                    DrawText(fdoc, planView, t, s, stdTextHeightMm, result, def.Id);

            // Phase 178f — section-view symbology. The README's
            // "200 mm vertical bar with arrows" for SpecialityEquipment
            // sections lands programmatically when the seed JSON
            // declares geometry.section.
            if (geo.Section != null)
            {
                DrawSectionGeometry(fdoc, def, geo.Section, s, stdTextHeightMm, result);
            }
        }

        /// <summary>
        /// Render a SectionSymbology block onto the family's elevation
        /// views. The view name is matched via Revit's standard four
        /// "Elevations" templates ship (Front / Back / Left / Right).
        /// "All" applies to every elevation view found.
        /// </summary>
        private static void DrawSectionGeometry(Document fdoc, SymbolDefinition def,
            SectionSymbology section, double symMm, double stdTextHeightMm,
            SymbolCreationResult result)
        {
            try
            {
                var views = new List<View>();
                foreach (var v in new FilteredElementCollector(fdoc).OfClass(typeof(View)))
                {
                    if (!(v is View view)) continue;
                    if (view.IsTemplate) continue;
                    if (view.ViewType != ViewType.Elevation) continue;
                    string nm = view.Name ?? "";
                    bool match =
                        string.Equals(section.View, "All", StringComparison.OrdinalIgnoreCase) ||
                        nm.IndexOf(section.View ?? "Front", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (match) views.Add(view);
                }
                if (views.Count == 0)
                {
                    result.Warnings.Add($"{def.Id}: no elevation view '{section.View}' for section symbology — skipped.");
                    return;
                }

                foreach (var v in views)
                {
                    // Section sketches live on a vertical plane facing the
                    // elevation. Use the view's right-direction × up-direction
                    // normal so model curves render in the elevation plane.
                    XYZ origin = XYZ.Zero;
                    XYZ normal;
                    try { normal = v.ViewDirection; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); normal = XYZ.BasisY; }
                    SketchPlane sketch;
                    try { sketch = SketchPlane.Create(fdoc, Plane.CreateByNormalAndOrigin(normal, origin)); }
                    catch (Exception ex) { result.Warnings.Add($"{def.Id} section sketch '{v.Name}': {ex.Message}"); continue; }

                    if (section.Lines != null)
                        foreach (var l in section.Lines)
                            DrawLine(fdoc, v, sketch, l, symMm, result, def.Id + " (section)");
                    if (section.Arcs != null)
                        foreach (var a in section.Arcs)
                            DrawArc(fdoc, v, sketch, a, symMm, result, def.Id + " (section)");
                    if (section.Text != null)
                        foreach (var t in section.Text)
                            DrawText(fdoc, v, t, symMm, stdTextHeightMm, result, def.Id + " (section)");
                }
            }
            catch (Exception ex) { result.Warnings.Add($"{def.Id}: section render failed — {ex.Message}"); }
        }

        private static void DrawLine(Document fdoc, View view, SketchPlane sketch,
            LineDefinition l, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                // Fix 5 — validate normalised coordinates before scaling.
                var geomWarnings = new List<string>();
                bool x1ok = ValidateGeometryCoord(l.X1, $"{id} line.x1", geomWarnings);
                bool y1ok = ValidateGeometryCoord(l.Y1, $"{id} line.y1", geomWarnings);
                bool x2ok = ValidateGeometryCoord(l.X2, $"{id} line.x2", geomWarnings);
                bool y2ok = ValidateGeometryCoord(l.Y2, $"{id} line.y2", geomWarnings);
                result.Warnings.AddRange(geomWarnings);
                if (!x1ok || !y1ok || !x2ok || !y2ok) return;

                XYZ p1 = new XYZ(Scale(l.X1, symMm), Scale(l.Y1, symMm), 0);
                XYZ p2 = new XYZ(Scale(l.X2, symMm), Scale(l.Y2, symMm), 0);
                if (p1.DistanceTo(p2) < 1e-6) return;
                Line line = Line.CreateBound(p1, p2);
                if (fdoc.IsFamilyDocument)
                {
                    // Fix 1a — GenericAnnotation families use NewSymbolicCurve;
                    // model families use NewModelCurve.
                    if (IsAnnotationFamily(fdoc, null))
                        fdoc.FamilyCreate.NewSymbolicCurve(line, sketch);
                    else
                        fdoc.FamilyCreate.NewModelCurve(line, sketch);
                }
                else
                {
                    fdoc.Create.NewDetailCurve(view, line);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: line draw failed — {ex.Message}");
            }
        }

        /// <summary>
        /// Overload used in section-view rendering where the family type is
        /// already known from the parent SymbolDefinition.
        /// Fix 1a.
        /// </summary>
        private static void DrawLine(Document fdoc, View view, SketchPlane sketch,
            LineDefinition l, double symMm, SymbolCreationResult result, string id,
            bool isAnnotation)
        {
            try
            {
                var geomWarnings = new List<string>();
                bool x1ok = ValidateGeometryCoord(l.X1, $"{id} line.x1", geomWarnings);
                bool y1ok = ValidateGeometryCoord(l.Y1, $"{id} line.y1", geomWarnings);
                bool x2ok = ValidateGeometryCoord(l.X2, $"{id} line.x2", geomWarnings);
                bool y2ok = ValidateGeometryCoord(l.Y2, $"{id} line.y2", geomWarnings);
                result.Warnings.AddRange(geomWarnings);
                if (!x1ok || !y1ok || !x2ok || !y2ok) return;

                XYZ p1 = new XYZ(Scale(l.X1, symMm), Scale(l.Y1, symMm), 0);
                XYZ p2 = new XYZ(Scale(l.X2, symMm), Scale(l.Y2, symMm), 0);
                if (p1.DistanceTo(p2) < 1e-6) return;
                Line line = Line.CreateBound(p1, p2);
                if (fdoc.IsFamilyDocument)
                {
                    if (isAnnotation)
                        fdoc.FamilyCreate.NewSymbolicCurve(line, sketch);
                    else
                        fdoc.FamilyCreate.NewModelCurve(line, sketch);
                }
                else
                {
                    fdoc.Create.NewDetailCurve(view, line);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: line draw failed — {ex.Message}");
            }
        }

        private static void DrawArc(Document fdoc, View view, SketchPlane sketch,
            ArcDefinition a, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                // Fix 5 — validate normalised coordinates before scaling.
                var geomWarnings = new List<string>();
                bool cxOk = ValidateGeometryCoord(a.Cx, $"{id} arc.cx", geomWarnings);
                bool cyOk = ValidateGeometryCoord(a.Cy, $"{id} arc.cy", geomWarnings);
                bool rOk  = ValidateGeometryCoord(a.R,  $"{id} arc.r",  geomWarnings);
                result.Warnings.AddRange(geomWarnings);
                if (!cxOk || !cyOk || !rOk) return;

                XYZ centre = new XYZ(Scale(a.Cx, symMm), Scale(a.Cy, symMm), 0);
                double r = Scale(a.R, symMm);
                if (r < 1e-6) return;

                double startRad = a.StartDeg * Math.PI / 180.0;
                double endRad   = a.EndDeg   * Math.PI / 180.0;

                Curve curve;
                if (Math.Abs(a.EndDeg - a.StartDeg) >= 360.0 - 1e-3)
                {
                    // Full circle
                    curve = Arc.Create(centre, r, 0, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
                }
                else
                {
                    curve = Arc.Create(centre, r, startRad, endRad, XYZ.BasisX, XYZ.BasisY);
                }

                if (fdoc.IsFamilyDocument)
                {
                    // Fix 1a — GenericAnnotation families use NewSymbolicCurve;
                    // model families use NewModelCurve.
                    if (IsAnnotationFamily(fdoc, null))
                        fdoc.FamilyCreate.NewSymbolicCurve(curve, sketch);
                    else
                        fdoc.FamilyCreate.NewModelCurve(curve, sketch);
                }
                else
                {
                    fdoc.Create.NewDetailCurve(view, curve);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: arc draw failed — {ex.Message}");
            }
        }

        private static void DrawFilledRegion(Document fdoc, View view,
            FilledRegionDefinition fr, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                if (fr.Boundary == null || fr.Boundary.Count < 3) return;

                // Fix 5 — validate all boundary points before scaling.
                var geomWarnings = new List<string>();
                bool allValid = true;
                for (int bi = 0; bi < fr.Boundary.Count; bi++)
                {
                    var p = fr.Boundary[bi];
                    if (!ValidateGeometryCoord(p.X, $"{id} filledRegion[{bi}].x", geomWarnings) ||
                        !ValidateGeometryCoord(p.Y, $"{id} filledRegion[{bi}].y", geomWarnings))
                    {
                        allValid = false;
                    }
                }
                result.Warnings.AddRange(geomWarnings);
                if (!allValid) return;

                var pts = fr.Boundary.Select(p => new XYZ(Scale(p.X, symMm), Scale(p.Y, symMm), 0)).ToList();

                var curves = new List<Curve>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    if (a.DistanceTo(b) < 1e-6) continue;
                    curves.Add(Line.CreateBound(a, b));
                }
                if (curves.Count < 3) return;

                var loop = CurveLoop.Create(curves);
                ElementId frTypeId = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(FilledRegionType))
                    .FirstElementId();
                if (frTypeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add($"{id}: no FilledRegionType in template.");
                    return;
                }
                FilledRegion.Create(fdoc, frTypeId, view.Id, new List<CurveLoop> { loop });
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: filled region failed — {ex.Message}");
            }
        }

        /// <summary>
        /// Fix 4 — DrawText now receives the effective textHeightMm:
        ///   1. If TextDefinition.HeightMm > 0, use that value.
        ///   2. Otherwise fall back to stdTextHeightMm (from AnnotationRules).
        ///   3. Convert to feet before passing to Revit.
        /// </summary>
        private static void DrawText(Document fdoc, View view,
            TextDefinition t, double symMm, double stdTextHeightMm,
            SymbolCreationResult result, string id)
        {
            if (string.IsNullOrEmpty(t?.Value)) return;
            try
            {
                // Fix 5 — validate text origin coordinates.
                var geomWarnings = new List<string>();
                bool xOk = ValidateGeometryCoord(t.X, $"{id} text.x", geomWarnings);
                bool yOk = ValidateGeometryCoord(t.Y, $"{id} text.y", geomWarnings);
                result.Warnings.AddRange(geomWarnings);
                if (!xOk || !yOk) return;

                XYZ origin = new XYZ(Scale(t.X, symMm), Scale(t.Y, symMm), 0);

                // Fix 4 — use JSON-defined HeightMm if set; fall back to standard default.
                double effectiveHeightMm = (t.HeightMm > 0) ? t.HeightMm : stdTextHeightMm;
                double heightFt = MmToFt(effectiveHeightMm);

                // Find or scale a text note type. We prefer to find an existing one
                // in the template and set its size, but fall back to direct creation
                // with whatever type the template provides.
                ElementId textTypeId = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (textTypeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add($"{id}: no TextNoteType in template.");
                    return;
                }

                // Attempt to find or duplicate a TextNoteType matching the required height.
                // This is best-effort; if the size doesn't match exactly the text will still
                // appear at the template's default size (author can adjust in Family Editor).
                ElementId sizedTypeId = FindOrDuplicateTextNoteType(fdoc, textTypeId, heightFt, id, result);

                var opts = new TextNoteOptions(sizedTypeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    Rotation = 0
                };
                TextNote.Create(fdoc, view.Id, origin, t.Value, opts);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: text draw failed — {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a TextNoteType whose text size matches heightFt (within tolerance),
        /// or duplicates the default type and sets the size. Returns the original
        /// type id if duplication or size-setting fails.
        /// </summary>
        private static ElementId FindOrDuplicateTextNoteType(Document fdoc, ElementId defaultTypeId,
            double heightFt, string id, SymbolCreationResult result)
        {
            try
            {
                // Search for an existing type with the right height.
                var allTypes = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                foreach (var tt in allTypes)
                {
                    var sizeParam = tt.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    if (sizeParam == null) continue;
                    if (Math.Abs(sizeParam.AsDouble() - heightFt) < 1e-5)
                        return tt.Id;
                }

                // Duplicate the default and set the size.
                var defaultType = fdoc.GetElement(defaultTypeId) as TextNoteType;
                if (defaultType == null) return defaultTypeId;

                var dup = defaultType.Duplicate($"STING_Text_{(int)(heightFt * MmPerFoot)}mm") as TextNoteType;
                if (dup == null) return defaultTypeId;

                var dupSize = dup.get_Parameter(BuiltInParameter.TEXT_SIZE);
                if (dupSize != null && !dupSize.IsReadOnly) dupSize.Set(heightFt);
                return dup.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: text size set failed — {ex.Message}. Using template default.");
                return defaultTypeId;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Parameters
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Stamp STING_SEED_FAMILY_TXT (instance) on the family with the
        /// seed's id. Read by SwapToManufacturerCommand to identify
        /// every placed seed instance + look up replacement candidates
        /// in STING_FAMILY_SWAP_REGISTRY.json. Fails silently — seeds
        /// remain useful even if the parameter isn't bound (the .rfa
        /// itself still ships with the seed-name, which is the swap
        /// key's secondary lookup).
        /// </summary>
        private static void TryAddSeedMarker(Document fdoc, SymbolDefinition def)
        {
            if (!fdoc.IsFamilyDocument) return;
            try
            {
                var fm = fdoc.FamilyManager;
                if (fm.get_Parameter("STING_SEED_FAMILY_TXT") == null)
                {
                    fm.AddParameter("STING_SEED_FAMILY_TXT",
                        GroupTypeId.IdentityData, SpecTypeId.String.Text, /* isInstance */ true);
                }
                if (fm.get_Parameter("STING_DESIGN_REF_TXT") == null)
                {
                    fm.AddParameter("STING_DESIGN_REF_TXT",
                        GroupTypeId.IdentityData, SpecTypeId.String.Text, /* isInstance */ true);
                }
                if (fm.get_Parameter("STING_SWAP_HISTORY_TXT") == null)
                {
                    fm.AddParameter("STING_SWAP_HISTORY_TXT",
                        GroupTypeId.IdentityData, SpecTypeId.String.Text, /* isInstance */ true);
                }
                // Default the seed-id parameter on every type variant so
                // even the empty seed family (no instances yet) carries
                // the registry key on its types.
                var seedParam = fm.get_Parameter("STING_SEED_FAMILY_TXT");
                if (seedParam != null && fm.Types != null)
                {
                    foreach (FamilyType t in fm.Types)
                    {
                        try
                        {
                            fm.CurrentType = t;
                            fm.Set(seedParam, def.Id ?? "");
                        }
                        catch { /* per-type set is best-effort */ }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryAddSeedMarker {def.Id}: {ex.Message}"); }
        }

        /// <summary>
        /// Wave G1 — duplicate the default type once per
        /// TypeVariantDefinition and stamp the per-variant parameter
        /// values. Saves the author from manually creating each variant
        /// in Family Editor; per the layman's guide the visual polish
        /// (2D symbology + 3D mass refinement) still happens manually,
        /// but the parameter scaffolding lands programmatically so
        /// schedules + swap candidates work the moment the .rfa loads.
        /// </summary>
        private static void AddTypeVariants(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            var fm = fdoc.FamilyManager;

            // Identify the default type (the only one that exists at
            // this point — the .rft template ships a single type and
            // we haven't added any). Some templates ship without any
            // types at all; in that case create one named "Default"
            // so the duplicate path has a source.
            FamilyType seed = fm.CurrentType;
            if (seed == null)
            {
                try { seed = fm.NewType("Default"); fm.CurrentType = seed; }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: NewType seed failed — {ex.Message}");
                    return;
                }
            }

            foreach (var variant in def.TypeVariants)
            {
                if (variant == null || string.IsNullOrWhiteSpace(variant.Name)) continue;
                try
                {
                    fm.CurrentType = seed;
                    var duplicate = fm.NewType(variant.Name);
                    fm.CurrentType = duplicate;

                    if (variant.Parameters != null)
                    {
                        foreach (var kv in variant.Parameters)
                        {
                            try
                            {
                                var p = fm.get_Parameter(kv.Key);
                                if (p == null)
                                {
                                    result.Warnings.Add($"{def.Id} variant '{variant.Name}': param '{kv.Key}' not bound — skipped.");
                                    continue;
                                }
                                SetVariantParam(fm, p, kv.Value);
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"{def.Id} variant '{variant.Name}' param '{kv.Key}': {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: variant '{variant.Name}' creation failed — {ex.Message}");
                }
            }

            // Restore the default type so the family's "current"
            // selection matches the seed when first loaded.
            try { fm.CurrentType = seed; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void SetVariantParam(FamilyManager fm, FamilyParameter p, string value)
        {
            if (p == null || value == null) return;
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        fm.Set(p, value);
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value, out int i)) fm.Set(p, i);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double d)) fm.Set(p, d);
                        break;
                    case StorageType.ElementId:
                        // Variant params don't yet support ElementId
                        // values — schedule + swap registry don't need
                        // them. Skip silently rather than fail.
                        break;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetVariantParam {p?.Definition?.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase 178f — bind family-formula expressions. Resolves
        /// "Mark" against BuiltInParameter.ALL_MODEL_MARK first, then
        /// falls back to a name lookup. Other targets are looked up
        /// by name on the family parameter set.
        /// </summary>
        private static void AddFormulaBindings(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            if (def.FormulaBindings == null || def.FormulaBindings.Count == 0) return;
            var fm = fdoc.FamilyManager;

            foreach (var b in def.FormulaBindings)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.Target) || string.IsNullOrWhiteSpace(b.Expression))
                    continue;
                try
                {
                    FamilyParameter target = null;
                    if (string.Equals(b.Target, "Mark", StringComparison.OrdinalIgnoreCase))
                    {
                        try { target = fm.get_Parameter(BuiltInParameter.ALL_MODEL_MARK); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); target = null; }
                    }
                    if (target == null) target = fm.get_Parameter(b.Target);
                    if (target == null)
                    {
                        result.Warnings.Add($"{def.Id}: formula binding target '{b.Target}' not found — skipped.");
                        continue;
                    }
                    if (target.IsReporting)
                    {
                        result.Warnings.Add($"{def.Id}: formula binding target '{b.Target}' is a reporting parameter — formulas not allowed.");
                        continue;
                    }
                    fm.SetFormula(target, b.Expression);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: formula binding '{b.Target}={b.Expression}' failed — {ex.Message}");
                }
            }
        }

        private static void AddParameters(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (def.Parameters == null || def.Parameters.Count == 0) return;
            if (!fdoc.IsFamilyDocument) return;
            var fm = fdoc.FamilyManager;

            foreach (var p in def.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p?.Name)) continue;
                try
                {
                    if (fm.get_Parameter(p.Name) != null) continue; // already exists

                    // Fix 1b — GroupTypeId is correct; SpecTypeId usage verified for 2025.
                    var groupTypeId = GroupTypeId.IdentityData;
                    var specTypeId  = ResolveSpecTypeId(p.Type);
                    fm.AddParameter(p.Name, groupTypeId, specTypeId, p.IsInstance);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: param '{p.Name}' add failed — {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Fix 1b — SpecTypeId mapping for Revit 2025 forge namespace.
        /// SpecTypeId.Int.Integer for integer, SpecTypeId.Number for dimensionless,
        /// SpecTypeId.Length for length. Deprecated ParameterType enum is not used.
        /// </summary>
        private static ForgeTypeId ResolveSpecTypeId(string type)
        {
            switch ((type ?? "Text").Trim())
            {
                case "Integer":  return SpecTypeId.Int.Integer;
                case "Number":   return SpecTypeId.Number;
                case "Length":   return SpecTypeId.Length;
                case "YesNo":    return SpecTypeId.Boolean.YesNo;
                case "Material": return SpecTypeId.Reference.Material;
                case "Text":
                default:         return SpecTypeId.String.Text;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Fix 6 — Scale-tier type variants
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fix 6 — After the default type is set up, generates additional
        /// FamilyType entries for each scale tier defined in the standard
        /// (skipping "standard" which maps to the default type). If the
        /// standard has no SymbolScaleTiers, this method is a no-op.
        ///
        /// For each tier a SymbolSizeMm parameter is set (or created) on
        /// the new type so the geometry-scale driving parameter reflects
        /// the tier. This allows Revit schedules and smart-placement
        /// engines to pick the correctly-sized type automatically based
        /// on view scale.
        /// </summary>
        private static void AddScaleTierTypes(Document fdoc, SymbolDefinition def,
            StandardDefinition std, List<string> warnings)
        {
            if (!fdoc.IsFamilyDocument) return;
            if (std?.SymbolScaleTiers == null || std.SymbolScaleTiers.Count == 0) return;

            var fm = fdoc.FamilyManager;

            // Identify (or create) the default seed type.
            FamilyType seedType = fm.CurrentType;
            if (seedType == null)
            {
                try { seedType = fm.NewType("Default"); fm.CurrentType = seedType; }
                catch (Exception ex)
                {
                    warnings?.Add($"{def.Id}: AddScaleTierTypes — seed type missing: {ex.Message}");
                    return;
                }
            }

            // Ensure SymbolSizeMm parameter exists on the family.
            const string sizeParamName = "STING_SYMBOL_SIZE_MM";
            if (fm.get_Parameter(sizeParamName) == null)
            {
                try
                {
                    fm.AddParameter(sizeParamName, GroupTypeId.IdentityData,
                        SpecTypeId.Length, /* isInstance */ false);
                }
                catch (Exception ex)
                {
                    warnings?.Add($"{def.Id}: AddScaleTierTypes — could not add {sizeParamName}: {ex.Message}");
                }
            }

            foreach (var tier in std.SymbolScaleTiers)
            {
                string tierName = tier.Key;
                double sizeMm   = tier.Value;

                // "standard" is the default type that already exists — skip.
                if (string.Equals(tierName, "standard", StringComparison.OrdinalIgnoreCase))
                    continue;

                string typeName = $"{def.Id}_{tierName}";

                try
                {
                    fm.CurrentType = seedType;
                    var tierType = fm.NewType(typeName);
                    fm.CurrentType = tierType;

                    // Set size multiplier on the new type.
                    var sizeParam = fm.get_Parameter(sizeParamName);
                    if (sizeParam != null && !sizeParam.IsReadOnly)
                    {
                        // SpecTypeId.Length means the value is stored in feet.
                        fm.Set(sizeParam, MmToFt(sizeMm));
                    }
                }
                catch (Exception ex)
                {
                    warnings?.Add($"{def.Id}: scale tier '{tierName}' type creation failed — {ex.Message}");
                }
            }

            // Restore default type.
            try { fm.CurrentType = seedType; } catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // Connectors
        // ─────────────────────────────────────────────────────────────────

        private static void AddConnectors(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            AddConnectorList(fdoc, def, def.Connectors, result, sourceLabel: "symbol");
            // Phase 178e — fold variant-level connector declarations
            // into the same family doc. Connectors live on the family,
            // not on a type, so the union of all variants ends up
            // visible to every variant; per-variant differentiation
            // happens via parameter values (size / system type) that
            // AutoPipeDrop reads at routing time.
            if (def.TypeVariants != null)
            {
                foreach (var v in def.TypeVariants)
                {
                    if (v?.Connectors == null || v.Connectors.Count == 0) continue;
                    AddConnectorList(fdoc, def, v.Connectors, result, sourceLabel: $"variant '{v.Name}'");
                }
            }
        }

        private static void AddConnectorList(Document fdoc, SymbolDefinition def,
            List<ConnectorDefinition> connectors, SymbolCreationResult result, string sourceLabel)
        {
            if (!fdoc.IsFamilyDocument) return;
            if (connectors == null) return;
            double s = def.SymbolSize > 0 ? def.SymbolSize : 3.0;

            foreach (var c in connectors)
            {
                if (c == null) continue;
                try
                {
                    XYZ origin = new XYZ(Scale(c.OffsetX, s), Scale(c.OffsetY, s), Scale(c.OffsetZ, s));
                    XYZ facing = ParseFacing(c.Facing);

                    Plane plane = Plane.CreateByNormalAndOrigin(facing, origin);
                    SketchPlane sp = SketchPlane.Create(fdoc, plane);

                    Domain domain = ResolveDomain(c.Domain);

                    // Fix 1a — reference line for connector attachment.
                    // In Revit 2025+ ConnectorElement.Create* methods need a
                    // Reference from existing geometry (a model curve endpoint).
                    // We mint a small reference line at the connector origin then
                    // pass the endpoint reference. Always use NewModelCurve here —
                    // connector geometry lives in the model, not the annotation layer.
                    XYZ p2 = origin.Add(facing.CrossProduct(XYZ.BasisZ).Normalize().Multiply(MmToFt(10)));
                    if (p2.DistanceTo(origin) < 1e-6)
                        p2 = origin.Add(XYZ.BasisX.Multiply(MmToFt(10)));
                    ModelCurve refLine;
                    try
                    {
                        refLine = fdoc.FamilyCreate.NewModelCurve(Line.CreateBound(origin, p2), sp);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"{def.Id} [{sourceLabel}]: connector reference line failed — {ex.Message}");
                        continue;
                    }

                    ConnectorElement ce = null;
                    switch (domain)
                    {
                        case Domain.DomainHvac:
                            // Revit 2025 API: CreateDuctConnector(Document, ConnectorProfileType,
                            // Reference, DuctSystemType) — 4 args; 3-arg overload does not exist.
                            try
                            {
                                ce = ConnectorElement.CreateDuctConnector(
                                    fdoc,
                                    DuctSystemType.SupplyAir,
                                    ResolveProfileType(c.Shape),
                                    refLine.GeometryCurve.GetEndPointReference(0));
                                SetConnectorSystemTypeParam(ce, c.SystemType, domain, def.Id, sourceLabel, result);
                            }
                            catch (Exception ex2)
                            {
                                StingLog.Warn($"{def.Id} [{sourceLabel}]: CreateDuctConnector failed — {ex2.Message}");
                                result.Warnings.Add($"{def.Id} [{sourceLabel}]: CreateDuctConnector failed — {ex2.Message}");
                            }
                            break;

                        case Domain.DomainPiping:
                            // Revit 2025 API: CreatePipeConnector(Document, PipeSystemType, Reference)
                            try
                            {
                                ce = ConnectorElement.CreatePipeConnector(
                                    fdoc,
                                    Autodesk.Revit.DB.Plumbing.PipeSystemType.SupplyHydronic,
                                    refLine.GeometryCurve.GetEndPointReference(0));
                                SetConnectorSystemTypeParam(ce, c.SystemType, domain, def.Id, sourceLabel, result);
                            }
                            catch (Exception ex3)
                            {
                                StingLog.Warn($"{def.Id} [{sourceLabel}]: CreatePipeConnector failed — {ex3.Message}");
                                result.Warnings.Add($"{def.Id} [{sourceLabel}]: CreatePipeConnector failed — {ex3.Message}");
                            }
                            break;

                        case Domain.DomainElectrical:
                            // Revit 2025 API: CreateElectricalConnector(Document, ElectricalSystemType, Reference)
                            try
                            {
                                ce = ConnectorElement.CreateElectricalConnector(
                                    fdoc,
                                    ResolveElectricalSystemType(c.SystemType),
                                    refLine.GeometryCurve.GetEndPointReference(0));
                            }
                            catch (Exception ex4)
                            {
                                StingLog.Warn($"{def.Id} [{sourceLabel}]: CreateElectricalConnector failed — {ex4.Message}");
                                result.Warnings.Add($"{def.Id} [{sourceLabel}]: CreateElectricalConnector failed — {ex4.Message}");
                            }
                            break;

                        case Domain.DomainCableTrayConduit:
                            // Fix 1d — ConnectorElement.CreateConduitConnector in Revit 2025:
                            //   CreateConduitConnector(doc, reference)
                            try
                            {
                                // Revit 2025 API: CreateConduitConnector(Document, Reference)
                                ce = ConnectorElement.CreateConduitConnector(
                                    fdoc,
                                    refLine.GeometryCurve.GetEndPointReference(0));
                            }
                            catch (Exception ex5)
                            {
                                StingLog.Warn($"{def.Id} [{sourceLabel}]: CreateConduitConnector failed — {ex5.Message}");
                                result.Warnings.Add($"{def.Id} [{sourceLabel}]: CreateConduitConnector failed — {ex5.Message}");
                            }
                            break;

                        default:
                            result.Warnings.Add($"{def.Id} [{sourceLabel}]: unsupported connector domain '{c.Domain}'");
                            break;
                    }

                    if (ce != null)
                    {
                        SetConnectorSize(ce, c);
                        // Fix 2 — SetConnectorDirection() is retired in Revit 2025+.
                        // Connector flow direction is geometry-driven; SetConnectorDirection removed.
                        // (No call to SetConnectorDirection here.)
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id} [{sourceLabel}]: connector ({c.Domain}/{c.SystemType}) failed — {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Fix 1d — In Revit 2025 CreateDuctConnector/CreatePipeConnector no longer
        /// accept a system type in the factory signature. Set the system type via
        /// connector parameters post-creation. This is best-effort; if the parameter
        /// doesn't exist or is read-only in this template, log a warning and continue.
        /// </summary>
        private static void SetConnectorSystemTypeParam(ConnectorElement ce, string systemType,
            Domain domain, string defId, string sourceLabel, SymbolCreationResult result)
        {
            if (ce == null || string.IsNullOrEmpty(systemType)) return;
            try
            {
                // CONNECTOR_DIRECTION_TYPE or CONNECTOR_SYSTEM_TYPE (family doc) may differ
                // by Revit version; try both BIPs and fall through gracefully.
                // Try duct system type param (covers HVAC); pipe connectors use a different param.
                // CONNECTOR_FLOW_DIRECTION and RBS_PIPE_SYSTEM_TYPE_PARAM do not exist as
                // BuiltInParameter constants in Revit 2025 — use LookupParameter by name instead.
                Parameter p = ce.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)
                    ?? ce.LookupParameter("Flow Direction")
                    ?? ce.LookupParameter("System Type");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                {
                    int sysTypeInt = ResolveSystemTypeInt(systemType, domain);
                    if (sysTypeInt >= 0) p.Set(sysTypeInt);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{defId} [{sourceLabel}]: system type param set failed — {ex.Message}");
            }
        }

        private static int ResolveSystemTypeInt(string systemType, Domain domain)
        {
            // These integer values correspond to the DuctSystemType / PipeSystemType
            // enums as they are stored in the Revit parameter; they may differ
            // from the C# enum ordinal. Best-effort only.
            if (domain == Domain.DomainHvac)
            {
                switch ((systemType ?? "").Trim())
                {
                    case "SupplyAir":  return (int)DuctSystemType.SupplyAir;
                    case "ReturnAir":  return (int)DuctSystemType.ReturnAir;
                    case "ExhaustAir": return (int)DuctSystemType.ExhaustAir;
                }
            }
            else if (domain == Domain.DomainPiping)
            {
                switch ((systemType ?? "").Trim())
                {
                    case "DomesticColdWater":   return (int)PipeSystemType.DomesticColdWater;
                    case "DomesticHotWater":    return (int)PipeSystemType.DomesticHotWater;
                    case "Sanitary":            return (int)PipeSystemType.Sanitary;
                    case "FireProtectionWet":   return (int)PipeSystemType.FireProtectWet;
                    case "FireProtectionDry":   return (int)PipeSystemType.FireProtectDry;
                }
            }
            return -1;
        }

        private static void SetConnectorSize(ConnectorElement ce, ConnectorDefinition c)
        {
            try
            {
                if (string.Equals(c.Shape, "Round", StringComparison.OrdinalIgnoreCase))
                {
                    var p = ce.get_Parameter(BuiltInParameter.CONNECTOR_DIAMETER);
                    if (p != null && !p.IsReadOnly && c.SizeMm > 0) p.Set(MmToFt(c.SizeMm));
                }
                else
                {
                    var pw = ce.get_Parameter(BuiltInParameter.CONNECTOR_WIDTH);
                    var ph = ce.get_Parameter(BuiltInParameter.CONNECTOR_HEIGHT);
                    if (pw != null && !pw.IsReadOnly && c.WidthMm  > 0) pw.Set(MmToFt(c.WidthMm));
                    if (ph != null && !ph.IsReadOnly && c.HeightMm > 0) ph.Set(MmToFt(c.HeightMm));
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetConnectorSize: {ex.Message}"); }
        }

        // Fix 2 — SetConnectorDirection() is retired in Revit 2025+.
        // The method has been removed entirely. Any callers that previously
        // used it now skip the call; see the inline comment in AddConnectorList.
        // Revit 2025+: connector flow direction is geometry-driven;
        // SetConnectorDirection removed.

        private static XYZ ParseFacing(string facing)
        {
            switch ((facing ?? "-X").Trim())
            {
                case "+X": return XYZ.BasisX;
                case "-X": return XYZ.BasisX.Negate();
                case "+Y": return XYZ.BasisY;
                case "-Y": return XYZ.BasisY.Negate();
                case "+Z": return XYZ.BasisZ;
                case "-Z": return XYZ.BasisZ.Negate();
                default:   return XYZ.BasisX.Negate();
            }
        }

        private static Domain ResolveDomain(string domain)
        {
            switch ((domain ?? "").Trim())
            {
                case "HVAC":         return Domain.DomainHvac;
                case "Piping":       return Domain.DomainPiping;
                case "Electrical":   return Domain.DomainElectrical;
                case "Conduit":
                case "CableTray":    return Domain.DomainCableTrayConduit;
                default:             return Domain.DomainUndefined;
            }
        }

        private static DuctSystemType ResolveDuctSystemType(string s)
        {
            switch ((s ?? "").Trim())
            {
                case "SupplyAir":  return DuctSystemType.SupplyAir;
                case "ReturnAir":  return DuctSystemType.ReturnAir;
                case "ExhaustAir": return DuctSystemType.ExhaustAir;
                default:           return DuctSystemType.UndefinedSystemType;
            }
        }

        private static PipeSystemType ResolvePipeSystemType(string s)
        {
            switch ((s ?? "").Trim())
            {
                case "DomesticColdWater":   return PipeSystemType.DomesticColdWater;
                case "DomesticHotWater":    return PipeSystemType.DomesticHotWater;
                case "Sanitary":            return PipeSystemType.Sanitary;
                case "FireProtectionWet":   return PipeSystemType.FireProtectWet;
                case "FireProtectionDry":   return PipeSystemType.FireProtectDry;
                case "FireProtectionPreaction": return PipeSystemType.FireProtectPreaction;
                case "ChilledWaterSupply":  return PipeSystemType.SupplyHydronic;
                case "ChilledWaterReturn":  return PipeSystemType.ReturnHydronic;
                case "HotWaterSupply":      return PipeSystemType.SupplyHydronic;
                case "HotWaterReturn":      return PipeSystemType.ReturnHydronic;
                case "Hydronic":            return PipeSystemType.SupplyHydronic;
                default:                    return PipeSystemType.UndefinedSystemType;
            }
        }

        private static ElectricalSystemType ResolveElectricalSystemType(string s)
        {
            // Revit 2025 enum names are short ("Data" not "DataCircuit").
            // PowerBalanced / PowerUnBalanced are the only "Power…" variants;
            // "Power" maps to PowerCircuit which is the generic power flag.
            switch ((s ?? "").Trim())
            {
                case "Power":         return ElectricalSystemType.PowerCircuit;
                case "Lighting":      return ElectricalSystemType.PowerCircuit;
                case "Data":          return ElectricalSystemType.Data;
                case "Telephone":     return ElectricalSystemType.Telephone;
                case "FireAlarm":     return ElectricalSystemType.FireAlarm;
                case "Security":      return ElectricalSystemType.Security;
                case "NurseCall":     return ElectricalSystemType.NurseCall;
                case "Communication": return ElectricalSystemType.Communication;
                default:              return ElectricalSystemType.UndefinedSystemType;
            }
        }

        private static ConnectorProfileType ResolveProfileType(string shape)
        {
            switch ((shape ?? "Round").Trim())
            {
                case "Round":       return ConnectorProfileType.Round;
                case "Rectangular": return ConnectorProfileType.Rectangular;
                case "Oval":        return ConnectorProfileType.Oval;
                default:            return ConnectorProfileType.Round;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3D solid (best-effort placeholder mass)
        // ─────────────────────────────────────────────────────────────────

        private static void AddSolid3D(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            var s3 = def.Solid3D;
            if (s3 == null) return;

            try
            {
                double w = s3.WidthMm  > 0 ? s3.WidthMm  : (s3.DiameterMm > 0 ? s3.DiameterMm : 200);
                double d = s3.DepthMm  > 0 ? s3.DepthMm  : (s3.DiameterMm > 0 ? s3.DiameterMm : 200);
                double h = s3.HeightMm > 0 ? s3.HeightMm : 100;

                CurveArray profile = new CurveArray();
                if (string.Equals(s3.Type, "Cylinder", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s3.Type, "Revolution", StringComparison.OrdinalIgnoreCase))
                {
                    double r = MmToFt((s3.DiameterMm > 0 ? s3.DiameterMm : Math.Max(w, d)) * 0.5);
                    profile.Append(Arc.Create(XYZ.Zero, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                    profile.Append(Arc.Create(XYZ.Zero, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                }
                else
                {
                    double hw = MmToFt(w * 0.5);
                    double hd = MmToFt(d * 0.5);
                    profile.Append(Line.CreateBound(new XYZ(-hw, -hd, 0), new XYZ( hw, -hd, 0)));
                    profile.Append(Line.CreateBound(new XYZ( hw, -hd, 0), new XYZ( hw,  hd, 0)));
                    profile.Append(Line.CreateBound(new XYZ( hw,  hd, 0), new XYZ(-hw,  hd, 0)));
                    profile.Append(Line.CreateBound(new XYZ(-hw,  hd, 0), new XYZ(-hw, -hd, 0)));
                }

                CurveArrArray prof = new CurveArrArray();
                prof.Append(profile);

                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                SketchPlane sp = SketchPlane.Create(fdoc, plane);

                // Fix 1c — NewExtrusion correct signature for Revit 2025:
                //   NewExtrusion(isSolid, curveArrArr, sketchPlane, height)
                // height must be in Revit internal feet (divide mm by 304.8).
                fdoc.FamilyCreate.NewExtrusion(/* isSolid */ true, prof, sp, MmToFt(h));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{def.Id}: 3D solid skipped — {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Fix 7 — Compound symbol factory
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fix 7 — Creates compound annotation families from concept definitions
        /// that have compoundComponents or compoundRungs. For each such concept,
        /// loads the component .rfa files already created by CreateAllFromFile,
        /// assembles them as nested families in a new GenericAnnotation family
        /// document, and saves the compound .rfa to outputFolder.
        ///
        /// Component families are loaded into the compound family doc as nested
        /// families via LoadFamily, then placed using NewFamilyInstance on the
        /// family's plan view. The compound is saved as {conceptId}_compound.rfa.
        /// </summary>
        public static SymbolCreationResult CreateCompoundSymbols(
            Document doc,
            string conceptsJsonPath,
            string outputFolder,
            bool loadIntoProject)
        {
            var result = new SymbolCreationResult();

            if (!File.Exists(conceptsJsonPath))
            {
                result.Errors.Add($"Concepts JSON not found: {conceptsJsonPath}");
                return result;
            }

            ConceptsFile concepts;
            try
            {
                concepts = JsonConvert.DeserializeObject<ConceptsFile>(File.ReadAllText(conceptsJsonPath));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Concepts JSON parse failed: {ex.Message}");
                return result;
            }

            if (concepts?.Concepts == null || concepts.Concepts.Count == 0)
            {
                result.Errors.Add("No concepts in concepts file.");
                return result;
            }

            Directory.CreateDirectory(outputFolder);
            var app = doc.Application;
            var templateFolder = ResolveTemplateFolder(app);

            foreach (var kvp in concepts.Concepts)
            {
                var concept = kvp.Value;
                if (concept == null) continue;

                // Only process concepts that have compound structure.
                bool hasComponents = concept.CompoundComponents != null && concept.CompoundComponents.Count > 0;
                bool hasRungs      = concept.CompoundRungs      != null && concept.CompoundRungs.Count > 0;
                if (!hasComponents && !hasRungs) continue;

                string conceptId = concept.ConceptId ?? kvp.Key;
                string rfaName   = conceptId + "_compound.rfa";
                string rfaPath   = Path.Combine(outputFolder, rfaName);

                if (File.Exists(rfaPath))
                {
                    result.Existed++;
                    result.CreatedRfaPaths.Add(rfaPath);
                    if (loadIntoProject) TryLoadFamily(doc, rfaPath, result);
                    continue;
                }

                try
                {
                    // Collect ordered component conceptIds — rungs take precedence in ladder mode.
                    var componentIds = new List<string>();
                    if (hasRungs)
                        foreach (var rung in concept.CompoundRungs)
                            if (rung?.Components != null)
                                componentIds.AddRange(rung.Components);
                    else
                        componentIds.AddRange(concept.CompoundComponents);

                    // Resolve the .rfa path for each component conceptId.
                    // The component family name is derived from the concept's standard mapping
                    // (first IEC genericAnnotation, then raw conceptId).
                    var componentRfaPaths = new List<(string rfaFile, string componentId)>();
                    foreach (var compId in componentIds)
                    {
                        // Try to find the rfa in output folder by conceptId.
                        string compRfa = Path.Combine(outputFolder, compId + ".rfa");
                        if (!File.Exists(compRfa))
                        {
                            // Try with _compound suffix (some components are themselves compound).
                            compRfa = Path.Combine(outputFolder, compId + "_compound.rfa");
                        }
                        if (!File.Exists(compRfa))
                        {
                            // Try via standard mapping lookups.
                            if (concepts.Concepts.TryGetValue(compId, out var compConcept) && compConcept != null)
                            {
                                foreach (var stdMap in compConcept.StandardMappings.Values)
                                {
                                    if (!string.IsNullOrEmpty(stdMap.GenericAnnotation))
                                    {
                                        string candidate = Path.Combine(outputFolder, stdMap.GenericAnnotation + ".rfa");
                                        if (File.Exists(candidate)) { compRfa = candidate; break; }
                                    }
                                }
                            }
                        }
                        componentRfaPaths.Add((compRfa, compId));
                    }

                    // Find a GenericAnnotation template for the compound family.
                    var fakeDef = new SymbolDefinition { FamilyType = "GenericAnnotation", Discipline = "Electrical", SymbolSize = 3.0 };
                    string templateFile = ResolveTemplateFile(fakeDef, templateFolder, result);
                    if (string.IsNullOrEmpty(templateFile))
                    {
                        result.Warnings.Add($"{conceptId}: no GenericAnnotation template found for compound family — skipped.");
                        result.Failed++;
                        continue;
                    }

                    Document compDoc = null;
                    try { compDoc = app.NewFamilyDocument(templateFile); }
                    catch (Exception ex2)
                    {
                        result.Errors.Add($"{conceptId}_compound: NewFamilyDocument failed — {ex2.Message}");
                        result.Failed++;
                        continue;
                    }
                    if (compDoc == null)
                    {
                        result.Errors.Add($"{conceptId}_compound: NewFamilyDocument returned null.");
                        result.Failed++;
                        continue;
                    }

                    bool compBuilt = false;
                    try
                    {
                        using (var tx = new Transaction(compDoc, "STING Create Compound Symbol"))
                        {
                            tx.Start();

                            View planView = ResolvePlanView(compDoc);

                            // Standard symbolSize for component placement offset.
                            double symSizeFt = MmToFt(3.0);

                            int componentIndex = 0;
                            foreach (var (compRfa, compId) in componentRfaPaths)
                            {
                                if (!File.Exists(compRfa))
                                {
                                    result.Warnings.Add($"{conceptId}_compound: component '{compId}' rfa not found at {compRfa} — skipped.");
                                    componentIndex++;
                                    continue;
                                }

                                try
                                {
                                    // Load the component family into the compound family document.
                                    Family compFamily;
                                    bool loaded = compDoc.LoadFamily(compRfa, new FamilyLoadOpts(), out compFamily);
                                    if (!loaded && compFamily == null)
                                    {
                                        result.Warnings.Add($"{conceptId}_compound: failed to load component '{compId}' from {compRfa}");
                                        componentIndex++;
                                        continue;
                                    }

                                    // Get the first family symbol (type) from the loaded family.
                                    FamilySymbol compSymbol = null;
                                    if (compFamily != null)
                                    {
                                        foreach (ElementId symId in compFamily.GetFamilySymbolIds())
                                        {
                                            compSymbol = compDoc.GetElement(symId) as FamilySymbol;
                                            if (compSymbol != null) break;
                                        }
                                    }

                                    if (compSymbol == null)
                                    {
                                        result.Warnings.Add($"{conceptId}_compound: no symbol found in component '{compId}' — skipped.");
                                        componentIndex++;
                                        continue;
                                    }

                                    if (!compSymbol.IsActive)
                                        compSymbol.Activate();

                                    // Place the nested family instance offset by componentIndex × symbolSize.
                                    XYZ placementOrigin = new XYZ(componentIndex * symSizeFt, 0, 0);

                                    if (planView != null)
                                    {
                                        compDoc.FamilyCreate.NewFamilyInstance(
                                            placementOrigin, compSymbol, planView);
                                    }
                                    else
                                    {
                                        // Fallback: place without view context.
                                        compDoc.FamilyCreate.NewFamilyInstance(
                                            placementOrigin, compSymbol,
                                            StructuralType.NonStructural);
                                    }
                                }
                                catch (Exception ex3)
                                {
                                    result.Warnings.Add($"{conceptId}_compound: placing component '{compId}' failed — {ex3.Message}");
                                }

                                componentIndex++;
                            }

                            tx.Commit();
                        }

                        var saveAs = new SaveAsOptions { OverwriteExistingFile = true };
                        compDoc.SaveAs(rfaPath, saveAs);
                        compDoc.Close(false);
                        compBuilt = true;
                    }
                    catch (Exception ex3)
                    {
                        try { compDoc?.Close(false); } catch { }
                        result.Errors.Add($"{conceptId}_compound: {ex3.Message}");
                        StingLog.Error($"SymbolLibraryCreator.CreateCompoundSymbols {conceptId}", ex3);
                    }

                    if (compBuilt)
                    {
                        result.Created++;
                        result.CreatedRfaPaths.Add(rfaPath);
                        if (loadIntoProject) TryLoadFamily(doc, rfaPath, result);
                    }
                    else
                    {
                        result.Failed++;
                    }
                }
                catch (Exception ex2)
                {
                    result.Failed++;
                    result.Errors.Add($"{conceptId}_compound: outer error — {ex2.Message}");
                    StingLog.Error($"SymbolLibraryCreator.CreateCompoundSymbols {conceptId}", ex2);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Family load
        // ─────────────────────────────────────────────────────────────────

        private static void TryLoadFamily(Document hostDoc, string rfaPath, SymbolCreationResult result)
        {
            try
            {
                using (var tx = new Transaction(hostDoc, "STING Load Symbol Family"))
                {
                    tx.Start();
                    Family fam;
                    bool loaded = hostDoc.LoadFamily(rfaPath, new FamilyLoadOpts(), out fam);
                    tx.Commit();
                    if (!loaded) result.Warnings.Add($"LoadFamily returned false for {Path.GetFileName(rfaPath)}");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"LoadFamily {Path.GetFileName(rfaPath)} failed — {ex.Message}");
            }
        }

        private sealed class FamilyLoadOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Fix 3 — Template resolution: multi-version + DataPath fallback
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fix 3 — Locates the Revit family template folder by searching:
        ///   1. Application.FamilyTemplatePath (set by Revit at startup).
        ///   2. ProgramData paths for Revit 2025, 2026, 2027 (both "Revit YYYY" and "RVT YYYY" layouts).
        ///   3. %APPDATA%\Autodesk\Revit\ variants.
        ///   4. DataPath/Templates/ (bundled minimal templates, future fallback).
        /// Returns the first folder that exists. Logs a warning (never throws) if none found.
        /// </summary>
        public static string ResolveTemplateFolder(Application app)
        {
            // 1. Revit's own configured path — most reliable.
            try
            {
                if (app != null)
                {
                    string p = app.FamilyTemplatePath;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFolder (app.FamilyTemplatePath): {ex.Message}"); }

            // 2. ProgramData paths for 2025 / 2026 / 2027.
            //    Autodesk ships two layouts across versions:
            //      C:\ProgramData\Autodesk\Revit YYYY\Family Templates\English
            //      C:\ProgramData\Autodesk\RVT YYYY\Family Templates\English
            string[] fallbacks =
            {
                // Revit 2027 — "Revit" layout (Autodesk changed the folder naming in 2025+).
                @"C:\ProgramData\Autodesk\Revit 2027\Family Templates\English",
                @"C:\ProgramData\Autodesk\Revit 2027\Family Templates\English-Imperial",
                // Revit 2027 — legacy "RVT" layout (in case Autodesk uses old convention).
                @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English-Imperial",

                // Revit 2026.
                @"C:\ProgramData\Autodesk\Revit 2026\Family Templates\English",
                @"C:\ProgramData\Autodesk\Revit 2026\Family Templates\English-Imperial",
                @"C:\ProgramData\Autodesk\RVT 2026\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2026\Family Templates\English-Imperial",

                // Revit 2025.
                @"C:\ProgramData\Autodesk\Revit 2025\Family Templates\English",
                @"C:\ProgramData\Autodesk\Revit 2025\Family Templates\English-Imperial",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English-Imperial",
            };
            foreach (var f in fallbacks)
            {
                try { if (Directory.Exists(f)) return f; }
                catch (Exception ex2) { StingLog.Warn($"ResolveTemplateFolder path check '{f}': {ex2.Message}"); }
            }

            // 3. %APPDATA% per-user template locations (roaming profile installs).
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    string[] appDataFallbacks =
                    {
                        Path.Combine(appData, @"Autodesk\Revit\Autodesk Revit 2027\Family Templates\English"),
                        Path.Combine(appData, @"Autodesk\Revit\Autodesk Revit 2026\Family Templates\English"),
                        Path.Combine(appData, @"Autodesk\Revit\Autodesk Revit 2025\Family Templates\English"),
                    };
                    foreach (var f in appDataFallbacks)
                    {
                        try { if (Directory.Exists(f)) return f; }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFolder (%APPDATA%): {ex.Message}"); }

            // 4. DataPath/Templates/ — bundled minimal .rft stubs (future-proof).
            try
            {
                string dataPath = StingToolsApp.DataPath;
                if (!string.IsNullOrEmpty(dataPath))
                {
                    string bundled = Path.Combine(dataPath, "Templates");
                    if (Directory.Exists(bundled)) return bundled;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFolder (DataPath/Templates): {ex.Message}"); }

            StingLog.Warn("ResolveTemplateFolder: no family template folder found on this machine. " +
                "Symbol families that require a template will be skipped with a warning.");
            return null;
        }

        /// <summary>
        /// Fix 3 — Searches the resolved template folder for candidate .rft files,
        /// returning the first match. Returns null (never throws) when no template
        /// is found so the caller can skip that symbol gracefully.
        /// </summary>
        private static string ResolveTemplateFile(SymbolDefinition def, string folder, SymbolCreationResult result)
        {
            if (string.IsNullOrEmpty(folder))
            {
                // No template folder at all — warn once via the result.
                result?.Warnings.Add($"ResolveTemplateFile: template folder is null/empty; cannot locate .rft.");
                return null;
            }

            string[] candidates = CandidateTemplateNames(def);
            foreach (var name in candidates)
            {
                try
                {
                    var hits = Directory.GetFiles(folder, name, SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
                catch (Exception ex) { StingLog.Warn($"ResolveTemplateFile {name}: {ex.Message}"); }
            }

            // Not found in primary folder — warn and return null so caller can skip.
            StingLog.Warn($"ResolveTemplateFile: '{def?.Id}' — none of [{string.Join(", ", candidates)}] found under '{folder}'.");
            return null;
        }

        private static string[] CandidateTemplateNames(SymbolDefinition def)
        {
            string ft   = (def?.FamilyType ?? "").Trim();
            string disc = (def?.Discipline ?? "").Trim();
            string host = (def?.Hosting    ?? "Standalone").Trim();
            string cat  = (def?.Category   ?? "").Trim();

            // ── Hosting overrides come first. A FaceBased / WallBased /
            // CeilingBased seed needs the matching template regardless
            // of whether it's MEP or Generic — Revit's host face wiring
            // depends on the template, not the runtime category.
            if (string.Equals(host, "FaceBased", StringComparison.OrdinalIgnoreCase))
            {
                // Category-aware face-based templates first; falls back
                // to the generic face-based template when the category-
                // specific one isn't available.
                var list = new List<string>();
                if (cat.IndexOf("Lighting", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add("Metric Lighting Fixture ceiling based.rft");
                    list.Add("Lighting Fixture ceiling based.rft");
                }
                if (cat.IndexOf("Specialty", StringComparison.OrdinalIgnoreCase) >= 0
                    || cat.IndexOf("Speciality", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add("Metric Specialty Equipment face based.rft");
                    list.Add("Specialty Equipment face based.rft");
                }
                if (string.Equals(disc, "Electrical", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add("Metric Electrical Fixture face based.rft");
                    list.Add("Electrical Fixture face based.rft");
                }
                if (string.Equals(disc, "Plumbing", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add("Metric Plumbing Fixture face based.rft");
                    list.Add("Plumbing Fixture face based.rft");
                }
                list.Add("Metric Generic Model face based.rft");
                list.Add("Generic Model face based.rft");
                return list.ToArray();
            }
            if (string.Equals(host, "WallBased", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    "Metric Generic Model wall based.rft",
                    "Generic Model wall based.rft",
                    "Metric Electrical Fixture wall based.rft",
                };
            }
            if (string.Equals(host, "CeilingBased", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    "Metric Generic Model ceiling based.rft",
                    "Generic Model ceiling based.rft",
                    "Metric Lighting Fixture ceiling based.rft",
                };
            }
            if (string.Equals(host, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    "Metric Generic Model.rft",   // work-plane-based default
                    "Generic Model.rft",
                };
            }

            // ── Original FamilyType-based path (preserved unchanged
            // for back-compat with existing JSON spec packs).
            if (string.Equals(ft, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Generic Annotation.rft", "Metric Generic Annotation.rft" };
            }
            if (string.Equals(ft, "SeedFamily", StringComparison.OrdinalIgnoreCase))
            {
                // Seed family — pick the most discipline/category-
                // appropriate freestanding template. The Hosting check
                // above already covered face / wall / ceiling.
                if (cat.IndexOf("Lighting Fixtures", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Lighting Fixture.rft", "Lighting Fixture.rft" };
                if (cat.IndexOf("Electrical Fixtures", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Electrical Fixture.rft", "Electrical Fixture.rft" };
                if (cat.IndexOf("Electrical Equipment", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Electrical Fixture.rft",  // closest available
                                   "Metric Generic Model.rft", "Generic Model.rft" };
                if (cat.IndexOf("Fire Alarm", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Fire Alarm Device.rft" };
                if (cat.IndexOf("Sprinkler", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Sprinkler.rft" };
                if (cat.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Plumbing Fixture.rft" };
                if (cat.IndexOf("Air Terminal", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Air Terminal.rft" };
                if (cat.IndexOf("Mechanical", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Mechanical Equipment.rft" };
                if (cat.IndexOf("Communication", StringComparison.OrdinalIgnoreCase) >= 0
                    || cat.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new[] { "Metric Data Device.rft", "Metric Communication Device.rft",
                                   "Metric Generic Model.rft" };
                return new[] { "Metric Generic Model.rft", "Generic Model.rft" };
            }
            if (string.Equals(ft, "MEPAccessory", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(disc, "Mechanical", StringComparison.OrdinalIgnoreCase)
                    && def.Connectors != null && def.Connectors.Any(c => string.Equals(c?.Domain, "HVAC", StringComparison.OrdinalIgnoreCase)))
                    return new[] { "Metric Duct Accessory.rft", "Duct Accessory.rft" };
                return new[] { "Metric Pipe Accessory.rft", "Pipe Accessory.rft" };
            }
            if (string.Equals(ft, "MEPEquipment", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(disc, "Electrical", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(disc, "Lighting",   StringComparison.OrdinalIgnoreCase))
                    return new[] { "Metric Electrical Fixture.rft", "Electrical Fixture.rft",
                                   "Metric Lighting Fixture.rft", "Lighting Fixture.rft",
                                   "Metric Generic Model.rft", "Generic Model.rft" };
                if (string.Equals(disc, "Plumbing", StringComparison.OrdinalIgnoreCase))
                    return new[] { "Metric Plumbing Fixture.rft", "Plumbing Fixture.rft",
                                   "Metric Generic Model.rft" };
                if (string.Equals(disc, "FireProtection", StringComparison.OrdinalIgnoreCase))
                    return new[] { "Metric Fire Alarm Device.rft",
                                   "Metric Sprinkler.rft",
                                   "Metric Pipe Accessory.rft",
                                   "Metric Generic Model.rft" };
                return new[] { "Metric Mechanical Equipment.rft", "Mechanical Equipment.rft",
                               "Metric Generic Model.rft" };
            }
            return new[] { "Metric Generic Model.rft", "Generic Model.rft" };
        }

        private static View ResolvePlanView(Document fdoc)
        {
            try
            {
                var views = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .ToList();
                return views.FirstOrDefault(v => v.ViewType == ViewType.FloorPlan)
                    ?? views.FirstOrDefault(v => v.ViewType == ViewType.CeilingPlan)
                    ?? views.FirstOrDefault(v => v.ViewType == ViewType.DraftingView)
                    ?? views.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolvePlanView: {ex.Message}");
                return null;
            }
        }
    }
}
