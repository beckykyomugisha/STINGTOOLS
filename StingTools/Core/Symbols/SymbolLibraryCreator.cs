// StingTools — MEP/FP/SLD Symbol Library creator (Phase 175)
//
// Iterates a SymbolLibrary loaded from JSON, opens the appropriate family
// template per FamilyType, draws normalised geometry into the family
// document, mints connectors / parameters, saves the resulting .rfa
// alongside the project, and loads it back into the active document.
//
// All Revit API calls target the 2025/2026/2027 signatures. The build
// environment has no Revit assemblies, so spots that depend on overload
// resolution are tagged // TODO-VERIFY-API for in-Revit verification.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Symbols
{
    /// <summary>
    /// Controls which seeds the builder will (re-)create when
    /// CreateAllFromFile is called.
    /// </summary>
    public enum SeedRebuildMode
    {
        /// <summary>
        /// Default safe mode: skip any seed whose .rfa already exists on
        /// disk or whose .sting-finalized sidecar is present. This protects
        /// hand-polished families from accidental regeneration.
        /// </summary>
        MissingOnly,

        /// <summary>
        /// Regenerate every seed whose .sting-finalized sidecar is absent,
        /// regardless of whether the .rfa file exists. Use after editing a
        /// JSON spec to pick up parameter or variant changes without
        /// destroying finalised families.
        /// </summary>
        RebuildUnfinalized,

        /// <summary>
        /// Regenerate ALL seeds including those marked as finalised. Only
        /// use when intentionally discarding manual polish (e.g. the JSON
        /// spec has a breaking change that requires a full rebuild). The
        /// command prompts the user to confirm before entering this mode.
        /// </summary>
        RebuildAll,
    }

    /// <summary>Aggregate result of a CreateAllFromFile run.</summary>
    public sealed class SymbolCreationResult
    {
        public int Created   { get; set; }
        public int Existed   { get; set; }
        public int Failed    { get; set; }
        public int Protected { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors   { get; } = new List<string>();
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
        // Library entry point
        // ─────────────────────────────────────────────────────────────────

        public static SymbolCreationResult CreateAllFromFile(
            Document hostDoc,
            string jsonPath,
            string outputFolder,
            bool loadIntoProject,
            SeedRebuildMode rebuildMode = SeedRebuildMode.MissingOnly)
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

                var rfaPath   = Path.Combine(outputFolder, def.Id + ".rfa");
                bool exists   = File.Exists(rfaPath);
                bool finalized = IsFinalized(rfaPath);

                // Protection decision ────────────────────────────────────────
                // RebuildAll: skip only if the JSON spec itself says protect.
                // RebuildUnfinalized: skip if finalised sidecar is present.
                // MissingOnly (default): skip if .rfa exists (original behaviour).
                bool skip = false;
                if (rebuildMode == SeedRebuildMode.RebuildAll)
                {
                    if (def.ProtectExisting && exists)
                    {
                        result.Protected++;
                        result.Warnings.Add($"{def.Id}: protectExisting=true — skipped even in RebuildAll mode.");
                        skip = true;
                    }
                }
                else if (rebuildMode == SeedRebuildMode.RebuildUnfinalized)
                {
                    if (finalized)
                    {
                        result.Protected++;
                        result.Warnings.Add($"{def.Id}: .sting-finalized sidecar present — skipped.");
                        skip = true;
                    }
                    else if (def.ProtectExisting && exists)
                    {
                        result.Protected++;
                        result.Warnings.Add($"{def.Id}: protectExisting=true in JSON spec — skipped even in RebuildUnfinalized mode. Remove the flag or use RebuildAll to force.");
                        skip = true;
                    }
                }
                else // MissingOnly
                {
                    if (exists)
                    {
                        result.Existed++;
                        result.CreatedRfaPaths.Add(rfaPath);
                        if (loadIntoProject) TryLoadFamily(hostDoc, rfaPath, result);
                        continue;
                    }
                }

                if (skip)
                {
                    if (exists)
                    {
                        result.CreatedRfaPaths.Add(rfaPath);
                        if (loadIntoProject) TryLoadFamily(hostDoc, rfaPath, result);
                    }
                    continue;
                }

                // Build ───────────────────────────────────────────────────────
                try
                {
                    // Option A: augment a pre-built source family when the
                    // JSON spec points at one and the file resolves.
                    string sourcePath = ResolveSourceFamilyPath(def.SourceFamilyPath, jsonPath);
                    string built = !string.IsNullOrEmpty(sourcePath)
                        ? BuildFromSourceFamily(app, hostDoc, def, sourcePath, outputFolder, result)
                        : BuildOne(app, def, outputFolder, templateFolder, result);

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
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{def.Id}: {ex.Message}");
                    StingLog.Error($"SymbolLibraryCreator: {def.Id} failed", ex);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Sidecar helpers — finalization protection
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the path of the .sting-finalized sidecar file that sits
        /// alongside an .rfa. The sidecar's presence signals that the family
        /// has been hand-polished and must not be regenerated.
        /// </summary>
        public static string GetSidecarPath(string rfaPath)
            => rfaPath + ".sting-finalized";

        /// <summary>Returns true if the .sting-finalized sidecar exists.</summary>
        public static bool IsFinalized(string rfaPath)
        {
            try { return File.Exists(GetSidecarPath(rfaPath)); }
            catch { return false; }
        }

        /// <summary>
        /// Writes the .sting-finalized sidecar so future rebuild runs skip
        /// this seed. Records the timestamp and a human note in the file
        /// content for auditability.
        /// </summary>
        public static void MarkFinalized(string rfaPath, string note = null)
        {
            try
            {
                string content = $"Finalized: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n" +
                                 $"Family: {Path.GetFileName(rfaPath)}\n" +
                                 (string.IsNullOrEmpty(note) ? "" : $"Note: {note}\n");
                File.WriteAllText(GetSidecarPath(rfaPath), content);
            }
            catch (Exception ex) { StingLog.Warn($"MarkFinalized {rfaPath}: {ex.Message}"); }
        }

        /// <summary>Removes the .sting-finalized sidecar, allowing future rebuilds.</summary>
        public static void ClearFinalized(string rfaPath)
        {
            try
            {
                string sidecar = GetSidecarPath(rfaPath);
                if (File.Exists(sidecar)) File.Delete(sidecar);
            }
            catch (Exception ex) { StingLog.Warn($"ClearFinalized {rfaPath}: {ex.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────
        // Source-family augmentation path (Option A)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a sourceFamilyPath value from a JSON spec against the
        /// spec file's directory and the Data/Seeds/Families/ subfolder.
        /// Returns null when the path is empty or no file is found.
        /// </summary>
        private static string ResolveSourceFamilyPath(string declared, string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(declared)) return null;
            try
            {
                // 1. Absolute path
                if (Path.IsPathRooted(declared) && File.Exists(declared)) return declared;
                // 2. Relative to the JSON spec's directory
                string specDir = Path.GetDirectoryName(jsonPath) ?? "";
                string rel1 = Path.Combine(specDir, declared);
                if (File.Exists(rel1)) return rel1;
                // 3. Relative to Data/Seeds/ via StingToolsApp
                string dataPath = StingTools.Core.StingToolsApp.DataPath ?? "";
                string rel2 = Path.Combine(dataPath, declared);
                if (File.Exists(rel2)) return rel2;
                string rel3 = Path.Combine(dataPath, "Seeds", declared);
                if (File.Exists(rel3)) return rel3;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSourceFamilyPath: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Opens an existing finished .rfa, injects the STING parameter
        /// scheme declared in <paramref name="def"/>, stamps the seed
        /// identity, mints any missing type variants, and saves to the
        /// seed output folder. Geometry generation is skipped — the
        /// imported family supplies its own 2D/3D content.
        /// </summary>
        private static string BuildFromSourceFamily(Application app, Document hostDoc,
            SymbolDefinition def, string sourcePath, string outputFolder, SymbolCreationResult result)
        {
            string outPath = Path.Combine(outputFolder, def.Id + ".rfa");
            Document fdoc = null;
            try
            {
                // Open the source .rfa in the family editor.
                // Application.OpenDocumentFile opens any file including .rfa.
                fdoc = app.OpenDocumentFile(sourcePath);
                if (fdoc == null || !fdoc.IsFamilyDocument)
                {
                    result.Warnings.Add($"{def.Id}: sourceFamilyPath '{sourcePath}' did not open as a family document — falling back to generate-from-scratch.");
                    return null;
                }

                // Validate category compatibility (warn, don't abort).
                try
                {
                    string srcCat = fdoc.OwnerFamily?.FamilyCategory?.Name ?? "";
                    if (!string.IsNullOrEmpty(def.Category) && !string.IsNullOrEmpty(srcCat)
                        && !string.Equals(srcCat, def.Category, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"{def.Id}: source family category '{srcCat}' differs from spec '{def.Category}' — parameters injected, verify in Family Editor.");
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"{def.Id}: category check failed — {ex.Message}"); }

                using (var tx = new Transaction(fdoc, "STING Augment Source Family"))
                {
                    tx.Start();

                    // Inject STING shared parameters declared in the spec.
                    // AddParameters is idempotent — skips params already present.
                    AddParameters(fdoc, def, result);

                    // Connector injection: add any connectors declared in the
                    // spec that don't already exist in the source family.
                    bool hasSpecConnectors = (def.Connectors != null && def.Connectors.Count > 0)
                        || (def.TypeVariants != null && def.TypeVariants.Exists(v => v?.Connectors?.Count > 0));
                    if (hasSpecConnectors
                        && !string.Equals(def.FamilyType, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only add connectors that aren't already in the family.
                        int existingCount = new FilteredElementCollector(fdoc)
                            .OfClass(typeof(ConnectorElement))
                            .GetElementCount();
                        if (existingCount == 0)
                            AddConnectors(fdoc, def, result);
                        else
                            result.Warnings.Add($"{def.Id}: source family already has {existingCount} connector(s) — spec connectors not added. Verify in Family Editor.");
                    }

                    // Seed stamp + type variant injection.
                    if (def.IsSeed) TryAddSeedMarker(fdoc, def);
                    if (def.TypeVariants != null && def.TypeVariants.Count > 0)
                        AddTypeVariants(fdoc, def, result);
                    if (def.FormulaBindings != null && def.FormulaBindings.Count > 0)
                        AddFormulaBindings(fdoc, def, result);

                    tx.Commit();
                }

                var saveAs = new SaveAsOptions { OverwriteExistingFile = true };
                fdoc.SaveAs(outPath, saveAs);
                result.Warnings.Add($"{def.Id}: built from source family '{Path.GetFileName(sourcePath)}'.");
                return outPath;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{def.Id}: BuildFromSourceFamily failed ({ex.Message}) — falling back to generate-from-scratch.");
                return null;
            }
            finally
            {
                try { fdoc?.Close(false); } catch (Exception ex) { StingLog.Warn($"BuildFromSourceFamily close {def.Id}: {ex.Message}"); }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Per-symbol routing
        // ─────────────────────────────────────────────────────────────────

        private static string BuildOne(Application app, SymbolDefinition def,
            string outputFolder, string templateFolder, SymbolCreationResult result)
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
                    DrawGeometry(fdoc, def, result);
                    if (!string.IsNullOrWhiteSpace(def.Subcategory))
                        ApplySubcategory(fdoc, def, result);
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

        private static void DrawGeometry(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
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
                    DrawText(fdoc, planView, t, s, result, def.Id);

            // Phase 178f — section-view symbology. The README's
            // "200 mm vertical bar with arrows" for SpecialityEquipment
            // sections lands programmatically when the seed JSON
            // declares geometry.section.
            if (geo.Section != null)
            {
                DrawSectionGeometry(fdoc, def, geo.Section, s, result);
            }
        }

        /// <summary>
        /// Render a SectionSymbology block onto the family's elevation
        /// views. The view name is matched via Revit's standard four
        /// "Elevations" templates ship (Front / Back / Left / Right).
        /// "All" applies to every elevation view found.
        /// </summary>
        private static void DrawSectionGeometry(Document fdoc, SymbolDefinition def,
            SectionSymbology section, double symMm, SymbolCreationResult result)
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
                    try { normal = v.ViewDirection; } catch { normal = XYZ.BasisY; }
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
                            DrawText(fdoc, v, t, symMm, result, def.Id + " (section)");
                }
            }
            catch (Exception ex) { result.Warnings.Add($"{def.Id}: section render failed — {ex.Message}"); }
        }

        private static void DrawLine(Document fdoc, View view, SketchPlane sketch,
            LineDefinition l, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                XYZ p1 = new XYZ(Scale(l.X1, symMm), Scale(l.Y1, symMm), 0);
                XYZ p2 = new XYZ(Scale(l.X2, symMm), Scale(l.Y2, symMm), 0);
                if (p1.DistanceTo(p2) < 1e-6) return;
                Line line = Line.CreateBound(p1, p2);
                // TODO-VERIFY-API: NewSymbolicCurve for family annotation; falls back to NewDetailCurve.
                DetailCurve dc = fdoc.IsFamilyDocument
                    ? fdoc.FamilyCreate.NewDetailCurve(view, line)
                    : fdoc.Create.NewDetailCurve(view, line);
                // Apply the declared line style when the JSON spec names one.
                if (dc != null && !string.IsNullOrWhiteSpace(l.Style))
                {
                    var gs = ResolveGraphicsStyle(fdoc, l.Style);
                    if (gs != null) try { dc.LineStyle = gs; } catch { }
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

                DetailCurve dc = fdoc.IsFamilyDocument
                    ? fdoc.FamilyCreate.NewDetailCurve(view, curve)
                    : fdoc.Create.NewDetailCurve(view, curve);
                // Apply the declared line style when the JSON spec names one.
                if (dc != null && !string.IsNullOrWhiteSpace(a.Style))
                {
                    var gs = ResolveGraphicsStyle(fdoc, a.Style);
                    if (gs != null) try { dc.LineStyle = gs; } catch { }
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
                ElementId frTypeId = ResolveFilledRegionType(fdoc, fr.FillType);
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

        private static void DrawText(Document fdoc, View view,
            TextDefinition t, double symMm, SymbolCreationResult result, string id)
        {
            if (string.IsNullOrEmpty(t?.Value)) return;
            try
            {
                XYZ origin = new XYZ(Scale(t.X, symMm), Scale(t.Y, symMm), 0);
                // Resolve a TextNoteType that matches the requested height; create one
                // by duplicating the template default when no exact match exists.
                ElementId textTypeId = ResolveTextNoteType(fdoc, t.HeightMm);
                if (textTypeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add($"{id}: no TextNoteType in template.");
                    return;
                }
                var opts = new TextNoteOptions(textTypeId)
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
            try { fm.CurrentType = seed; } catch { }
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
                        try { target = fm.get_Parameter(BuiltInParameter.ALL_MODEL_MARK); } catch { target = null; }
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

            // Pass 1 — add any parameters not already present.
            foreach (var p in def.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p?.Name)) continue;
                try
                {
                    if (fm.get_Parameter(p.Name) != null) continue; // already exists

                    var groupTypeId = GroupTypeId.IdentityData; // TODO-VERIFY-API
                    var specTypeId  = ResolveSpecTypeId(p.Type);

                    // When the JSON spec marks a parameter as shared, look it up
                    // in MR_PARAMETERS.txt and add it as an ExternallyDefinedParameter
                    // so the instance matches the shared-parameter GUID required by
                    // tag families and the COBie/ISO 19650 schedule filters.
                    // Falls back to a plain project parameter when not found in the file.
                    if (p.IsShared && TryAddSharedParameter(fdoc, fm, p, groupTypeId, result, def.Id))
                        continue;

                    fm.AddParameter(p.Name, groupTypeId, specTypeId, p.IsInstance);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: param '{p.Name}' add failed — {ex.Message}");
                }
            }

            // Pass 2 — apply "default" values declared in the JSON on the seed
            // (template) type. AddTypeVariants duplicates from this type, so
            // defaults propagate automatically; per-variant overrides applied
            // later in SetVariantParam win over these seeds.
            foreach (var p in def.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p?.Name) || p.Default == null) continue;
                try
                {
                    var fp = fm.get_Parameter(p.Name);
                    if (fp != null && !fp.IsReadOnly && fm.CurrentType != null)
                        SetVariantParam(fm, fp, p.Default);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: param '{p.Name}' default failed — {ex.Message}");
                }
            }
        }

        private static ForgeTypeId ResolveSpecTypeId(string type)
        {
            // TODO-VERIFY-API: Spec type IDs in Revit 2025 forge namespace.
            switch ((type ?? "Text").Trim())
            {
                case "Integer": return SpecTypeId.Int.Integer;
                case "Number":  return SpecTypeId.Number;
                case "Length":  return SpecTypeId.Length;
                case "YesNo":   return SpecTypeId.Boolean.YesNo;
                case "Material": return SpecTypeId.Reference.Material;
                case "Text":
                default:        return SpecTypeId.String.Text;
            }
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
                    // Pre-flight: reject bad domain/systemType pairings before the API
                    // call produces a cryptic exception at runtime.
                    if (!ValidateDomainSystemType(domain, c.SystemType, out string pairError))
                    {
                        result.Warnings.Add($"{def.Id} [{sourceLabel}]: connector skipped — {pairError}");
                        continue;
                    }
                    // TODO-VERIFY-API: Connector creation requires reference geometry in 2025.
                    // We create a small reference line at the connector origin perpendicular
                    // to the facing direction, then mint the connector against its endpoint.
                    XYZ p2 = origin.Add(facing.CrossProduct(XYZ.BasisZ).Normalize().Multiply(MmToFt(10)));
                    if (p2.DistanceTo(origin) < 1e-6)
                        p2 = origin.Add(XYZ.BasisX.Multiply(MmToFt(10)));
                    var refLine = fdoc.FamilyCreate.NewModelCurve(Line.CreateBound(origin, p2), sp);

                    ConnectorElement ce = null;
                    switch (domain)
                    {
                        case Domain.DomainHvac:
                            ce = ConnectorElement.CreateDuctConnector(
                                fdoc,
                                ResolveDuctSystemType(c.SystemType),
                                ResolveProfileType(c.Shape),
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        case Domain.DomainPiping:
                            ce = ConnectorElement.CreatePipeConnector(
                                fdoc,
                                ResolvePipeSystemType(c.SystemType),
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        case Domain.DomainElectrical:
                            ce = ConnectorElement.CreateElectricalConnector(
                                fdoc,
                                ResolveElectricalSystemType(c.SystemType),
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        case Domain.DomainCableTrayConduit:
                            ce = ConnectorElement.CreateConduitConnector(
                                fdoc,
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        default:
                            result.Warnings.Add($"{def.Id} [{sourceLabel}]: unsupported connector domain '{c.Domain}'");
                            break;
                    }

                    if (ce != null)
                    {
                        SetConnectorSize(ce, c);
                        SetConnectorDirection(ce, c.Direction);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id} [{sourceLabel}]: connector ({c.Domain}/{c.SystemType}) failed — {ex.Message}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Geometry helpers — style / fill-type / text-height resolution
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds a projection GraphicsStyle by name (case-insensitive).
        /// Returns null when the style doesn't exist in the document.
        /// </summary>
        private static GraphicsStyle ResolveGraphicsStyle(Document fdoc, string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName)) return null;
            return new FilteredElementCollector(fdoc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .FirstOrDefault(gs => gs.GraphicsStyleType == GraphicsStyleType.Projection
                    && string.Equals(gs.Name, styleName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds a FilledRegionType by name; falls back to the first available
        /// type when no name is specified or the name is not found.
        /// </summary>
        private static ElementId ResolveFilledRegionType(Document fdoc, string fillTypeName)
        {
            if (!string.IsNullOrWhiteSpace(fillTypeName))
            {
                var namedType = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(FilledRegionType))
                    .Cast<FilledRegionType>()
                    .FirstOrDefault(t => string.Equals(t.Name, fillTypeName, StringComparison.OrdinalIgnoreCase));
                if (namedType != null) return namedType.Id;
            }
            return new FilteredElementCollector(fdoc).OfClass(typeof(FilledRegionType)).FirstElementId();
        }

        /// <summary>
        /// Finds a TextNoteType whose TEXT_SIZE is within 0.01 mm of the
        /// requested height; when no exact match exists, duplicates the first
        /// type and sets its height. Falls back to the first type when
        /// heightMm ≤ 0 or duplication fails.
        /// </summary>
        private static ElementId ResolveTextNoteType(Document fdoc, double heightMm)
        {
            if (heightMm > 0)
            {
                double targetFt = MmToFt(heightMm);
                var types = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                var match = types.FirstOrDefault(tnt =>
                {
                    var p = tnt.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    return p != null && Math.Abs(p.AsDouble() - targetFt) < MmToFt(0.01);
                });
                if (match != null) return match.Id;

                // Duplicate the first type and stamp the target height.
                var first = types.FirstOrDefault();
                if (first != null)
                {
                    try
                    {
                        var dup = first.Duplicate($"STING Text {heightMm}mm") as TextNoteType;
                        if (dup != null)
                        {
                            dup.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(targetFt);
                            return dup.Id;
                        }
                    }
                    catch { /* fall through to first-type fallback */ }
                }
            }
            return new FilteredElementCollector(fdoc).OfClass(typeof(TextNoteType)).FirstElementId();
        }

        // ─────────────────────────────────────────────────────────────────
        // Subcategory
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gets or creates the subcategory declared in the JSON spec and
        /// assigns it to every CurveElement and FilledRegion in the family
        /// document. The STING_SEED subcategory lets VG overrides in host
        /// projects target all seed geometry independently of the host
        /// element category.
        /// </summary>
        private static void ApplySubcategory(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            try
            {
                Category ownerCat = fdoc.OwnerFamily?.FamilyCategory;
                if (ownerCat == null)
                {
                    result.Warnings.Add($"{def.Id}: subcategory '{def.Subcategory}' — could not resolve family category.");
                    return;
                }

                Category subCat = ownerCat.SubCategories.Contains(def.Subcategory)
                    ? ownerCat.SubCategories.get_Item(def.Subcategory)
                    : fdoc.Settings.Categories.NewSubcategory(ownerCat, def.Subcategory);
                if (subCat == null) return;

                int assigned = 0;
                foreach (var ce in new FilteredElementCollector(fdoc)
                    .OfClass(typeof(CurveElement)).Cast<CurveElement>())
                {
                    try
                    {
                        var p = ce.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                        if (p != null && !p.IsReadOnly) { p.Set(subCat.Id); assigned++; }
                    }
                    catch { }
                }
                foreach (var fr in new FilteredElementCollector(fdoc)
                    .OfClass(typeof(FilledRegion)).Cast<FilledRegion>())
                {
                    try
                    {
                        var p = fr.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                        if (p != null && !p.IsReadOnly) { p.Set(subCat.Id); assigned++; }
                    }
                    catch { }
                }
                if (assigned > 0)
                    result.Warnings.Add($"{def.Id}: subcategory '{def.Subcategory}' assigned to {assigned} elements.");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{def.Id}: subcategory '{def.Subcategory}' failed — {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Shared parameters
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to add a parameter as an ExternallyDefinedParameter by
        /// looking it up in the STING shared-parameter file (MR_PARAMETERS.txt).
        /// The application's SharedParametersFilename is saved and restored
        /// around the call so other open family documents aren't affected.
        /// Returns true and adds the parameter when found; returns false so
        /// the caller can fall back to a plain project parameter.
        /// </summary>
        private static bool TryAddSharedParameter(Document fdoc, FamilyManager fm,
            ParameterDefinition p, GroupTypeId groupTypeId,
            SymbolCreationResult result, string defId)
        {
            try
            {
                var app = fdoc.Application;
                string stingFile = null;
                try { stingFile = Path.Combine(StingToolsApp.DataPath ?? "", "MR_PARAMETERS.txt"); }
                catch { }
                if (string.IsNullOrEmpty(stingFile) || !File.Exists(stingFile)) return false;

                string saved = app.SharedParametersFilename;
                try
                {
                    app.SharedParametersFilename = stingFile;
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    if (defFile == null) return false;

                    foreach (DefinitionGroup grp in defFile.Groups)
                    {
                        var extDef = grp.Definitions.get_Item(p.Name) as ExternalDefinition;
                        if (extDef != null)
                        {
                            fm.AddExternallyDefinedParameter(extDef, groupTypeId, p.IsInstance);
                            return true;
                        }
                    }
                }
                finally
                {
                    try { app.SharedParametersFilename = saved; } catch { }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{defId}: shared param '{p.Name}' lookup failed ({ex.Message}); falling back to project param.");
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Connector validation
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns false with a descriptive error when the systemType string
        /// is not compatible with the resolved domain. Called before
        /// ConnectorElement.Create* to surface mismatches as human-readable
        /// warnings rather than cryptic Revit API exceptions.
        /// </summary>
        private static bool ValidateDomainSystemType(Domain domain, string systemType, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(systemType)) return true; // undefined is always accepted

            var hvac = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "SupplyAir", "ReturnAir", "ExhaustAir", "OutsideAir", "UndefinedSystemType" };
            var piping = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "DomesticColdWater", "DomesticHotWater", "Sanitary", "FireProtectionWet",
                  "FireProtectionDry", "FireProtectionPreaction", "ChilledWaterSupply",
                  "ChilledWaterReturn", "HotWaterSupply", "HotWaterReturn", "Hydronic",
                  "UndefinedSystemType" };
            var electrical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "PowerCircuit", "PowerBalanced", "PowerUnBalanced", "Data", "FireAlarm",
                  "Controls", "Communication", "Nurse", "Security", "Telephone",
                  "UndefinedSystemType" };

            switch (domain)
            {
                case Domain.DomainHvac:
                    if (!hvac.Contains(systemType))
                    { error = $"'{systemType}' is not valid for HVAC domain. Expected one of: {string.Join(", ", hvac)}"; return false; }
                    break;
                case Domain.DomainPiping:
                    if (!piping.Contains(systemType))
                    { error = $"'{systemType}' is not valid for Piping domain. Expected one of: {string.Join(", ", piping)}"; return false; }
                    break;
                case Domain.DomainElectrical:
                    if (!electrical.Contains(systemType))
                    { error = $"'{systemType}' is not valid for Electrical domain. Expected one of: {string.Join(", ", electrical)}"; return false; }
                    break;
            }
            return true;
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

        private static void SetConnectorDirection(ConnectorElement ce, string direction)
        {
            // ConnectorElement (family edit) doesn't expose flow direction
            // directly in Revit 2025 — the runtime Connector.Direction is
            // a get-only mirror of the connector's intrinsic type, and
            // BuiltInParameter.CONNECTOR_FLOW_DIRECTION was retired.
            // Symbol families default to Bidirectional which is fine for
            // schematic / tag content; users can override in the family
            // editor when fabrication-grade direction is required.
            if (string.IsNullOrEmpty(direction)
                || direction.Equals("Bidirectional", StringComparison.OrdinalIgnoreCase))
                return;
            StingLog.Info($"SetConnectorDirection: '{direction}' requested but ConnectorElement "
                + "doesn't expose flow direction in 2025 API — left at family default.");
        }

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

        // TODO-VERIFY-API: System type names mapped to MEPSystemType / DuctSystemType / PipeSystemType
        // enums in Revit 2025. Default fallbacks chosen for safety.
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
                // TODO-VERIFY-API: NewExtrusion signature in 2025.
                fdoc.FamilyCreate.NewExtrusion(true, prof, sp, MmToFt(h));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{def.Id}: 3D solid skipped — {ex.Message}");
            }
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
        // Template resolution
        // ─────────────────────────────────────────────────────────────────

        public static string ResolveTemplateFolder(Application app)
        {
            try
            {
                if (app != null)
                {
                    string p = app.FamilyTemplatePath;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFolder: {ex.Message}"); }

            string[] fallbacks =
            {
                @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2026\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English-Imperial",
            };
            foreach (var f in fallbacks)
                if (Directory.Exists(f)) return f;
            return null;
        }

        private static string ResolveTemplateFile(SymbolDefinition def, string folder, SymbolCreationResult result)
        {
            if (string.IsNullOrEmpty(folder)) return null;
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
            return null;
        }

        private static string[] CandidateTemplateNames(SymbolDefinition def)
        {
            string ft   = (def.FamilyType ?? "").Trim();
            string disc = (def.Discipline ?? "").Trim();
            string host = (def.Hosting    ?? "Standalone").Trim();
            string cat  = (def.Category   ?? "").Trim();

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
