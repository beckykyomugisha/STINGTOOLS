using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 170 — Title-block factory
//
// The engine that mints .rfa title-block families from a JSON spec.
// Pipeline per family:
//
//   1. Resolve template path (.rft) — checks several known Revit
//      family-template roots.
//   2. app.NewFamilyDocument(rftPath) — opens template.
//   3. Set Application.SharedParametersFilename = MR_PARAMETERS.txt
//      (with try/finally restore — same pattern as TagFamilyCreator).
//   4. In one Transaction (declarative authoring — Phase 170 revision
//      removed the BIM-toggle hybrid; each spec now produces one
//      visually-clean .rfa, BIM/NONBIM split via two specs per size):
//      a. Add every shared / internal / calculated parameter via
//         FamilyManager. Spec-supplied defaults for shared params
//         flow through TrySetSharedDefault.
//      b. Place lines — NewDetailCurve. Apply LineStyle.
//      c. Place static text — TextNote.Create.
//      d. Place labels — NewLabel bound to FamilyParameter.
//      e. Place filled regions — FilledRegion.Create.
//      f. Place slots — 4 named reference planes per slot (TOP / BOT
//         / LFT / RGT) + an optional slot-id text marker. The
//         Drawing-Type / Sheet-Manager system reads slot bounds back
//         via these reference planes.
//   5. Commit transaction.
//   6. SaveAs with overwrite.
//   7. Close(false).
//
// Returns a per-family result with counts + warnings + errors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Concurrent;

namespace StingTools.Core.Drawing
{
    public sealed class TitleBlockBuildResult
    {
        public string FamilyId { get; set; }
        public string SavedPath { get; set; }
        public bool   Ok => Errors.Count == 0;
        public int    ParametersAdded { get; set; }
        public int    LinesPlaced { get; set; }
        public int    LabelsPlaced { get; set; }
        public int    StaticTextPlaced { get; set; }
        public int    FilledRegionsPlaced { get; set; }
        public int    SlotsPlaced { get; set; }
        /// <summary>Brief slot id + bbox lines, surfaced in the report so
        /// the operator can verify slot layout without opening the .rfa.</summary>
        public List<string> SlotSummary { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors   { get; set; } = new List<string>();
    }

    public static class TitleBlockFactory
    {
        // Revit internal length unit is feet. Spec is in mm.
        private const double MmPerFoot = 304.8;
        internal static double MmToFt(double mm) => mm / MmPerFoot;

        // ── Public entry point ───────────────────────────────────────────

        public static TitleBlockBuildResult Build(UIApplication uiApp,
            TitleBlockSpec spec, string sharedParamFile)
        {
            var r = new TitleBlockBuildResult { FamilyId = spec?.Id };
            if (uiApp == null || spec == null)
            { r.Errors.Add("null uiApp or spec"); return r; }

            // Abstract specs are inheritance bases only — no .rfa minted.
            if (spec.Abstract)
            {
                r.Warnings.Add($"spec '{spec.Id}' is abstract — skipped (used as a base via extends)");
                return r;
            }

            var app = uiApp.Application;
            // Reset per-build warn-once trackers so multi-family runs
            // (TitleBlock_CreateAll) don't suppress legitimate warnings
            // on the second + subsequent families. _newLabelMethod /
            // _verticalAlignType are intentionally NOT reset between
            // families — their MethodInfo / Type cache is keyed by
            // FamilyItemFactory's type, not by document, and stays
            // valid across families.
            _LineStyleNotFoundOnce.Clear();
            _LineStyleAutoCreatedOnce.Clear();
            // Re-attempt label-API discovery on every build — the cached
            // result is type-keyed (FamilyItemFactory's type doesn't change)
            // so re-discovery is cheap and ensures a fresh diagnostic if
            // the user iterates JSON without restarting Revit.
            _newLabelDiscoveryAttempted = false;
            _newLabelMethod             = null;
            _newLabelTarget             = null;
            _verticalAlignType          = null;
            _slotRefPlanesPermittedFailCount = 0;
            _slotRefPlanesNotPermittedWarned = false;

            // 1. Resolve template
            string rftPath = ResolveTemplatePath(spec.TemplateRft);
            if (string.IsNullOrEmpty(rftPath))
            {
                r.Errors.Add($"template not found: {spec.TemplateRft}");
                return r;
            }

            // 2. Open template
            Document famDoc = null;
            string originalSpFile = null;
            try
            {
                try { originalSpFile = app.SharedParametersFilename; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                famDoc = app.NewFamilyDocument(rftPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    r.Errors.Add($"NewFamilyDocument returned null for {rftPath}");
                    return r;
                }

                // 3. Point at shared param file
                if (!string.IsNullOrEmpty(sharedParamFile)
                    && File.Exists(sharedParamFile))
                    app.SharedParametersFilename = sharedParamFile;

                DefinitionFile defFile = app.OpenSharedParameterFile();

                // 4. Build inside a transaction — declarative, monomorphic
                // pipeline. No more BIM-mode toggle, no reflow groups, no
                // label pairs (Phase 170 hybrid removed). Each family is
                // a single visually-clean .rfa; the BIM/non-BIM split is
                // handled by shipping two families per paper size.
                using (var tx = new Transaction(famDoc, $"STING Build {spec.Id}"))
                {
                    tx.Start();

                    var fm = famDoc.FamilyManager;
                    var view = ResolveTitleBlockView(famDoc);
                    if (view == null)
                    {
                        r.Errors.Add("title-block view not found in family document");
                        tx.RollBack();
                        return r;
                    }

                    // 4a. Parameters
                    var paramByName = new Dictionary<string, FamilyParameter>(
                        StringComparer.OrdinalIgnoreCase);
                    AddAllParameters(fm, defFile, spec, paramByName, r);

                    // 4b. Lines
                    foreach (var line in spec.Lines)
                        PlaceLine(famDoc, fm, view, line, paramByName, r);

                    // 4c. Static text
                    foreach (var st in spec.StaticText)
                        PlaceStaticText(famDoc, fm, view, st, paramByName, r);

                    // 4d. Labels
                    foreach (var lbl in spec.Labels)
                        PlaceLabel(famDoc, fm, view, lbl, paramByName, r);

                    // 4e. Filled regions
                    foreach (var fr in spec.FilledRegions)
                        PlaceFilledRegion(famDoc, fm, view, fr, paramByName, r);

                    // 4f. Slots — viewport zones with optional reference
                    // planes + a slot-id marker at the top-left corner.
                    // Drawing-Type / Sheet-Manager system reads slot
                    // bounds back via the named reference planes
                    // (`<id>_TOP/BOT/LFT/RGT`) and routes viewports here
                    // based on PurposeTag (see TitleBlock_AutoPlaceViewports).
                    foreach (var slot in spec.Slots)
                        PlaceSlot(famDoc, fm, view, slot, r);

                    tx.Commit();
                }

                // 5. SaveAs
                string savePath = ResolveSavePath(famDoc, spec.SaveAs);
                Directory.CreateDirectory(Path.GetDirectoryName(savePath) ?? ".");
                var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                famDoc.SaveAs(savePath, saveOpts);
                r.SavedPath = savePath;

                famDoc.Close(false);
                famDoc = null;
            }
            catch (Exception ex)
            {
                r.Errors.Add(ex.Message);
                StingLog.Error($"TitleBlockFactory.Build({spec.Id})", ex);
                try { famDoc?.Close(false); } catch (Exception exClose) { StingLog.Warn($"Suppressed: {exClose.Message}"); }
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(originalSpFile))
                        app.SharedParametersFilename = originalSpFile;
                }
                catch { /* best-effort */ }
            }
            return r;
        }

        // ── Template + view resolution ───────────────────────────────────

        private static readonly string[] TemplateSearchRoots =
        {
            @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English",
            @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English-Imperial",
            @"C:\ProgramData\Autodesk\RVT 2026\Family Templates\English",
            @"C:\ProgramData\Autodesk\RVT 2026\Family Templates\English-Imperial",
            @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English",
            @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English-Imperial",
            @"C:\ProgramData\Autodesk\RAC 2025\Family Templates\English",
        };

        private static string ResolveTemplatePath(string templateRft)
        {
            if (string.IsNullOrEmpty(templateRft)) return null;
            // Absolute path?
            if (Path.IsPathRooted(templateRft) && File.Exists(templateRft))
                return templateRft;
            // Search known roots.
            foreach (var root in TemplateSearchRoots)
            {
                if (!Directory.Exists(root)) continue;
                var candidate = Path.Combine(root, templateRft);
                if (File.Exists(candidate)) return candidate;
            }
            // Fall back: glob across known roots for a file with a matching
            // basename — handles locale variants like "A1 metric.rft" vs
            // "A1 metric_FRA.rft".
            string baseName = Path.GetFileName(templateRft);
            foreach (var root in TemplateSearchRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    var hit = Directory.EnumerateFiles(root, baseName,
                        SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(hit)) return hit;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return null;
        }

        private static View ResolveTitleBlockView(Document famDoc)
        {
            // Title-block templates ship with a single drafting view.
            try
            {
                foreach (var el in new FilteredElementCollector(famDoc)
                    .OfClass(typeof(View)))
                {
                    if (el is View v && !v.IsTemplate
                        && v.ViewType == ViewType.DraftingView)
                        return v;
                }
                // Fallback — first non-template view.
                foreach (var el in new FilteredElementCollector(famDoc)
                    .OfClass(typeof(View)))
                {
                    if (el is View v && !v.IsTemplate) return v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTitleBlockView: {ex.Message}"); }
            return null;
        }

        private static string ResolveSavePath(Document famDoc, string specPath)
        {
            if (Path.IsPathRooted(specPath)) return specPath;
            // Relative to the project directory if open; else to the
            // active assembly's directory.
            try
            {
                if (famDoc.Application?.Documents != null)
                {
                    foreach (Document d in famDoc.Application.Documents)
                    {
                        if (!d.IsFamilyDocument && !string.IsNullOrEmpty(d.PathName))
                        {
                            var dir = Path.GetDirectoryName(d.PathName);
                            if (!string.IsNullOrEmpty(dir))
                                return Path.Combine(dir, specPath);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            // Final fallback: alongside the addin DLL. AssemblyPath is the
            // FILE path (ends in StingTools.dll), so we have to take the
            // directory — Path.Combine(asmFilePath, "Families/…") would
            // produce "…\StingTools.dll\Families\…" which Revit then tries
            // to create and fails because StingTools.dll is a file.
            var asmDir = ".";
            try
            {
                var asm = StingToolsApp.AssemblyPath;
                if (!string.IsNullOrEmpty(asm))
                    asmDir = Path.GetDirectoryName(asm) ?? ".";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return Path.Combine(asmDir, specPath);
        }

        // ── Parameter creation ───────────────────────────────────────────

        private static void AddAllParameters(FamilyManager fm, DefinitionFile defFile,
            TitleBlockSpec spec, Dictionary<string, FamilyParameter> map,
            TitleBlockBuildResult r)
        {
            // Two-family architecture (Phase 170 revision): no BIM_MODE_BOOL
            // is minted by the factory itself. Each spec carries its own
            // declared parameter list (typically including a shared
            // STING_SHEET_BIM_MODE_TXT with default "BIM" or "NONBIM" so
            // each sheet records the variant in use). Specs that need
            // family-internal calculated parameters declare them via the
            // standard ParamSpec.Kind = "internal" + Formula path.
            var pendingFormulas = new List<(string Name, string Formula)>();
            foreach (var p in spec.Parameters)
            {
                if (string.IsNullOrEmpty(p?.Name)) continue;
                if (map.ContainsKey(p.Name)) continue;
                FamilyParameter fp = null;
                if (string.Equals(p.Kind, "internal", StringComparison.OrdinalIgnoreCase))
                {
                    fp = AddInternalParameter(fm, p.Name, p.Type, p.Group,
                        p.Instance, p.Default, formula: null, r);
                    if (!string.IsNullOrEmpty(p.Formula))
                        pendingFormulas.Add((p.Name, p.Formula));
                }
                else
                {
                    fp = AddSharedParameter(fm, defFile, p.Name, p.Group,
                        p.Instance, r);
                    // For shared params, write the spec-supplied default
                    // value if any (e.g. STING_SHEET_BIM_MODE_TXT default
                    // "BIM" / "NONBIM" — the marker every sheet inherits
                    // from the loaded title-block family).
                    if (fp != null && !string.IsNullOrEmpty(p.Default))
                        TrySetSharedDefault(fm, fp, p.Default, r);
                }
                if (fp != null) map[p.Name] = fp;
            }

            // 4. Pass 2 — every formula now sees a fully-populated map.
            foreach (var (name, formula) in pendingFormulas)
            {
                if (!map.TryGetValue(name, out var fp)) continue;
                try { fm.SetFormula(fp, formula); }
                catch (Exception ex) { r.Warnings.Add($"SetFormula '{name}': {ex.Message}"); }
            }

            r.ParametersAdded = map.Count;
        }

        /// <summary>Set the default value of a shared text/numeric/yesno
        /// parameter on the current family type. Mirrors the type-aware
        /// branching in AddInternalParameter — text-default literals like
        /// "0001" must not be parsed as numeric just because they happen
        /// to look like a number.</summary>
        private static void TrySetSharedDefault(FamilyManager fm,
            FamilyParameter fp, string defaultValue, TitleBlockBuildResult r)
        {
            try
            {
                var current = fm.CurrentType ?? fm.NewType("Default");
                var dt = fp.Definition?.GetDataType();
                if (dt != null && dt.Equals(SpecTypeId.Boolean.YesNo))
                {
                    fm.Set(fp, (defaultValue == "1"
                        || string.Equals(defaultValue, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(defaultValue, "yes",  StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
                }
                else if (dt != null && dt.Equals(SpecTypeId.Int.Integer))
                {
                    if (int.TryParse(defaultValue, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var i))
                        fm.Set(fp, i);
                }
                else if (dt != null && (dt.Equals(SpecTypeId.Length) || dt.Equals(SpecTypeId.Number)))
                {
                    if (double.TryParse(defaultValue, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var d))
                        fm.Set(fp, d);
                }
                else
                {
                    fm.Set(fp, defaultValue);
                }
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"TrySetSharedDefault '{fp.Definition?.Name}': {ex.Message}");
            }
        }

        private static FamilyParameter AddSharedParameter(FamilyManager fm,
            DefinitionFile defFile, string paramName, string group,
            bool isInstance, TitleBlockBuildResult r)
        {
            if (defFile == null)
            {
                r.Warnings.Add($"shared param '{paramName}' skipped: no shared param file open");
                return null;
            }
            try
            {
                ExternalDefinition extDef = null;
                foreach (DefinitionGroup g in defFile.Groups)
                {
                    foreach (Definition d in g.Definitions)
                    {
                        if (string.Equals(d.Name, paramName, StringComparison.OrdinalIgnoreCase))
                        { extDef = d as ExternalDefinition; break; }
                    }
                    if (extDef != null) break;
                }
                if (extDef == null)
                {
                    r.Warnings.Add($"shared param '{paramName}' not in shared file");
                    return null;
                }
                var groupId = ResolveGroupTypeId(group);
                return fm.AddParameter(extDef, groupId, isInstance);
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"AddSharedParameter '{paramName}': {ex.Message}");
                return null;
            }
        }

        private static FamilyParameter AddInternalParameter(FamilyManager fm,
            string name, string type, string group, bool isInstance,
            string defaultValue, string formula, TitleBlockBuildResult r)
        {
            try
            {
                var groupId = ResolveGroupTypeId(group);
                var specId  = ResolveSpecTypeId(type);
                var fp = fm.AddParameter(name, groupId, specId, isInstance);
                if (!string.IsNullOrEmpty(formula))
                {
                    try { fm.SetFormula(fp, formula); }
                    catch (Exception ex) { r.Warnings.Add($"SetFormula '{name}': {ex.Message}"); }
                }
                else if (!string.IsNullOrEmpty(defaultValue))
                {
                    // Set the current type's value. Family must have at
                    // least one type — Revit pre-creates "Default" in
                    // every title-block template.
                    var current = fm.CurrentType ?? fm.NewType("Default");
                    try
                    {
                        // Branch on the SPEC TYPE (what kind of param we
                        // just added), not on whether the literal happens
                        // to look like a number — text defaults like "01"
                        // or "0001" parse as doubles but must still be
                        // written via the string overload.
                        if (specId == SpecTypeId.Boolean.YesNo)
                        {
                            fm.Set(fp, (defaultValue == "1"
                                || string.Equals(defaultValue, "true", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(defaultValue, "yes",  StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
                        }
                        else if (specId == SpecTypeId.Int.Integer)
                        {
                            if (int.TryParse(defaultValue, System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture, out var i))
                                fm.Set(fp, i);
                            else
                                r.Warnings.Add($"Set default '{name}': '{defaultValue}' is not a valid integer");
                        }
                        else if (specId == SpecTypeId.Length
                              || specId == SpecTypeId.Number)
                        {
                            if (double.TryParse(defaultValue, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                                fm.Set(fp, d);
                            else
                                r.Warnings.Add($"Set default '{name}': '{defaultValue}' is not a valid number");
                        }
                        else
                        {
                            // Text (and any other string-storage spec)
                            fm.Set(fp, defaultValue);
                        }
                    }
                    catch (Exception ex) { r.Warnings.Add($"Set default '{name}': {ex.Message}"); }
                }
                return fp;
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"AddInternalParameter '{name}': {ex.Message}");
                return null;
            }
        }

        private static ForgeTypeId ResolveGroupTypeId(string group)
        {
            switch ((group ?? "").ToLowerInvariant())
            {
                case "constraints":  return GroupTypeId.Constraints;
                case "geometry":     return GroupTypeId.Geometry;
                case "general":      return GroupTypeId.General;
                case "graphics":     return GroupTypeId.Graphics;
                case "text":         return GroupTypeId.Text;
                case "data":
                case "identitydata":
                default:             return GroupTypeId.IdentityData;
            }
        }

        private static ForgeTypeId ResolveSpecTypeId(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "yesno":        return SpecTypeId.Boolean.YesNo;
                case "length":       return SpecTypeId.Length;
                case "number":       return SpecTypeId.Number;
                case "integer":      return SpecTypeId.Int.Integer;
                case "text":
                default:             return SpecTypeId.String.Text;
            }
        }

        // ── Geometry placement ───────────────────────────────────────────
        //
        // Phase 170 revision (Two-Family BIM Architecture):
        // No more BIM-mode visibility gating on individual elements —
        // each family is a single visually-clean variant (BIM or NONBIM)
        // declared as its own JSON spec. The legacy reflow-group +
        // label-pair + visibility-gate code paths were removed; the
        // placement helpers below are now pure declarative authoring.

        private static void PlaceLine(Document famDoc, FamilyManager fm,
            View view, LineSpec spec, Dictionary<string, FamilyParameter> map,
            TitleBlockBuildResult r)
        {
            if (spec?.From == null || spec.To == null
                || spec.From.Length < 2 || spec.To.Length < 2) return;
            try
            {
                var p1 = new XYZ(MmToFt(spec.From[0]), MmToFt(spec.From[1]), 0);
                var p2 = new XYZ(MmToFt(spec.To[0]),   MmToFt(spec.To[1]),   0);
                var line = Line.CreateBound(p1, p2);
                var dc = famDoc.FamilyCreate.NewDetailCurve(view, line);
                ApplyLineStyle(famDoc, dc, spec.Style, r);
                r.LinesPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceLine: {ex.Message}"); }
        }

        private static void PlaceStaticText(Document famDoc, FamilyManager fm,
            View view, StaticTextSpec spec, Dictionary<string, FamilyParameter> map,
            TitleBlockBuildResult r)
        {
            if (spec?.Anchor == null || spec.Anchor.Length < 2
                || string.IsNullOrEmpty(spec.Text)) return;
            try
            {
                var pos = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var typeId = ResolveTextNoteTypeId(famDoc, spec.TextTypeName);
                if (typeId == ElementId.InvalidElementId)
                { r.Warnings.Add($"PlaceStaticText: no text type for '{spec.TextTypeName}'"); return; }
                TextNote.Create(famDoc, view.Id, pos, spec.Text, typeId);
                r.StaticTextPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceStaticText '{spec.Text}': {ex.Message}"); }
        }

        private static void PlaceLabel(Document famDoc, FamilyManager fm,
            View view, LabelSpec spec, Dictionary<string, FamilyParameter> map,
            TitleBlockBuildResult r)
        {
            if (spec?.Anchor == null || spec.Anchor.Length < 2
                || string.IsNullOrEmpty(spec.Param)) return;
            if (!map.TryGetValue(spec.Param, out var fp))
            { r.Warnings.Add($"PlaceLabel: param '{spec.Param}' not added — skipped"); return; }
            try
            {
                var origin = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var hAlign = (HorizontalAlign)ParseHAlign(spec.HAlign);
                var sizeFt = MmToFt(spec.Size);
                IList<FamilyParameter> labelParams = new List<FamilyParameter> { fp };
                IList<string> prefixSuffix = new List<string> { spec.Prefix ?? "", spec.Suffix ?? "" };
                var lbl = InvokeNewLabel(famDoc, view, origin, hAlign, spec.VAlign,
                    labelParams, prefixSuffix, sizeFt, r);
                if (lbl != null) r.LabelsPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceLabel '{spec.Param}': {ex.Message}"); }
        }

        private static void PlaceFilledRegion(Document famDoc, FamilyManager fm,
            View view, FilledRegionSpec spec, Dictionary<string, FamilyParameter> map,
            TitleBlockBuildResult r)
        {
            if (spec?.TopLeft == null || spec.BottomRight == null
                || spec.TopLeft.Length < 2 || spec.BottomRight.Length < 2) return;
            try
            {
                var x1 = MmToFt(Math.Min(spec.TopLeft[0], spec.BottomRight[0]));
                var x2 = MmToFt(Math.Max(spec.TopLeft[0], spec.BottomRight[0]));
                var y1 = MmToFt(Math.Min(spec.TopLeft[1], spec.BottomRight[1]));
                var y2 = MmToFt(Math.Max(spec.TopLeft[1], spec.BottomRight[1]));
                var loop = CurveLoop.Create(new List<Curve>
                {
                    Line.CreateBound(new XYZ(x1, y1, 0), new XYZ(x2, y1, 0)),
                    Line.CreateBound(new XYZ(x2, y1, 0), new XYZ(x2, y2, 0)),
                    Line.CreateBound(new XYZ(x2, y2, 0), new XYZ(x1, y2, 0)),
                    Line.CreateBound(new XYZ(x1, y2, 0), new XYZ(x1, y1, 0)),
                });
                var typeId = ResolveFilledRegionTypeId(famDoc, spec.FillTypeName);
                if (typeId == ElementId.InvalidElementId)
                { r.Warnings.Add($"PlaceFilledRegion: no fill type '{spec.FillTypeName}'"); return; }
                FilledRegion.Create(famDoc, typeId, view.Id, new List<CurveLoop> { loop });
                r.FilledRegionsPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceFilledRegion: {ex.Message}"); }
        }

        /// <summary>Authors a viewport slot: 4 reference planes (top /
        /// bottom / left / right) at the slot bounds plus a small slot-id
        /// marker at the top-left corner. Slots are invisible at sheet-
        /// placement time but the Drawing-Type / Sheet-Manager system
        /// can introspect the .rfa to read slot bounds back, and the
        /// operator can dimension viewports off the reference planes.</summary>
        private static void PlaceSlot(Document famDoc, FamilyManager fm,
            View view, SlotSpec spec, TitleBlockBuildResult r)
        {
            if (spec == null) return;
            if (string.IsNullOrEmpty(spec.Id))
            { r.Warnings.Add("PlaceSlot: slot has no id — skipped"); return; }
            if (spec.Anchor == null || spec.Anchor.Length < 2
                || spec.Size == null || spec.Size.Length < 2)
            { r.Warnings.Add($"PlaceSlot '{spec.Id}': missing anchor or size"); return; }
            try
            {
                double xMm = spec.Anchor[0];
                double yMm = spec.Anchor[1];
                double wMm = spec.Size[0];
                double hMm = spec.Size[1];
                // Top-left in screen-space terms = (xMm, yMm + hMm) since
                // the spec is bottom-left-anchored.
                double topYMm    = yMm + hMm;
                double bottomYMm = yMm;
                double leftXMm   = xMm;
                double rightXMm  = xMm + wMm;

                if (spec.CreateReferencePlanes)
                {
                    NewSlotReferencePlane(famDoc, view, $"{spec.Id}_TOP",
                        new XYZ(MmToFt(leftXMm),  MmToFt(topYMm),    0),
                        new XYZ(MmToFt(rightXMm), MmToFt(topYMm),    0), r);
                    NewSlotReferencePlane(famDoc, view, $"{spec.Id}_BOT",
                        new XYZ(MmToFt(leftXMm),  MmToFt(bottomYMm), 0),
                        new XYZ(MmToFt(rightXMm), MmToFt(bottomYMm), 0), r);
                    NewSlotReferencePlane(famDoc, view, $"{spec.Id}_LFT",
                        new XYZ(MmToFt(leftXMm),  MmToFt(bottomYMm), 0),
                        new XYZ(MmToFt(leftXMm),  MmToFt(topYMm),    0), r);
                    NewSlotReferencePlane(famDoc, view, $"{spec.Id}_RGT",
                        new XYZ(MmToFt(rightXMm), MmToFt(bottomYMm), 0),
                        new XYZ(MmToFt(rightXMm), MmToFt(topYMm),    0), r);
                }

                if (spec.ShowCornerMarker)
                {
                    // Place the marker just inside the top-left corner so
                    // it's visible but doesn't overlap the slot bounds. 5
                    // mm inset, 2 mm text height.
                    var cornerInsetMm = 5.0;
                    var textTypeId = ResolveTextNoteTypeId(famDoc, null);
                    if (textTypeId != ElementId.InvalidElementId)
                    {
                        var pos = new XYZ(MmToFt(leftXMm + cornerInsetMm),
                                          MmToFt(topYMm  - cornerInsetMm), 0);
                        TextNote.Create(famDoc, view.Id, pos, spec.Id, textTypeId);
                    }
                }

                r.SlotsPlaced++;
                r.SlotSummary.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-6} ({1,7:0.0}, {2,7:0.0}) → ({3,7:0.0}, {4,7:0.0}) mm   {5}{6}",
                    spec.Id, leftXMm, bottomYMm, rightXMm, topYMm,
                    spec.CreateReferencePlanes ? "[ref-planes]" : "[no ref-planes]",
                    string.IsNullOrEmpty(spec.Description) ? "" : "  — " + spec.Description));
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceSlot '{spec.Id}': {ex.Message}"); }
        }

        // Best-effort reference-plane authoring. Title-block family documents
        // in Revit 2025 reject `FamilyCreate.NewReferencePlane` with
        // "The attempted operation is not permitted in this type of family."
        // Slot ref planes are nice-to-have (the operator can dimension off
        // them in Family Editor) but not load-bearing — slot bounds for
        // TitleBlock_AutoPlaceViewports are read from the JSON spec at
        // runtime, not from the .rfa. So when ref-plane creation fails we
        // surface ONE summary warning per build and move on.
        private static int _slotRefPlanesPermittedFailCount = 0;
        private static bool _slotRefPlanesNotPermittedWarned = false;

        private static void NewSlotReferencePlane(Document famDoc, View view,
            string name, XYZ p1, XYZ p2, TitleBlockBuildResult r)
        {
            try
            {
                var rp = famDoc.FamilyCreate.NewReferencePlane(p1, p2, XYZ.BasisZ, view);
                if (rp != null && !string.IsNullOrEmpty(name))
                {
                    try { rp.Name = name; } catch { /* best-effort */ }
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { LogRefPlaneFailureOnce(r); }
            catch (Exception ex)
            {
                // Some Revit versions throw a generic Exception with the
                // "not permitted in this type of family" message rather than
                // InvalidOperationException — pattern-match to suppress those
                // too instead of polluting the warning list 12× per build.
                if (ex.Message != null
                    && ex.Message.IndexOf("not permitted", StringComparison.OrdinalIgnoreCase) >= 0)
                    LogRefPlaneFailureOnce(r);
                else
                    r.Warnings.Add($"NewSlotReferencePlane '{name}': {ex.Message}");
            }
        }

        private static void LogRefPlaneFailureOnce(TitleBlockBuildResult r)
        {
            _slotRefPlanesPermittedFailCount++;
            if (!_slotRefPlanesNotPermittedWarned)
            {
                _slotRefPlanesNotPermittedWarned = true;
                r.Warnings.Add(
                    "Slot reference planes not permitted in this title-block family — "
                    + "skipped silently. Slot bounds are still readable from STING_TITLE_BLOCKS.json "
                    + "at AutoPlaceViewports time, so this only affects the visual cue in Family Editor "
                    + "(operator can dimension off the slot-id text markers instead).");
            }
        }

        // ── Resolvers ────────────────────────────────────────────────────

        // Phase 171 — title-block .rft templates only ship a small
        // subset of generic-line subcategories. "Wide Lines" / "Heavy
        // Lines" / "Thick Lines" all describe the same intent across
        // English / English-Imperial / locale variants. Try the
        // requested name first, then walk a fallback chain, then drop
        // back to whatever style the family's first projection
        // subcategory exposes. Lines remain drawn either way.
        private static readonly Dictionary<string, string[]> LineStyleFallbacks =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Wide Lines"]   = new[] { "Heavy Lines", "Thick Lines", "Medium Lines", "Thin Lines" },
                ["Heavy Lines"]  = new[] { "Wide Lines", "Thick Lines", "Medium Lines", "Thin Lines" },
                ["Thick Lines"]  = new[] { "Heavy Lines", "Wide Lines", "Medium Lines", "Thin Lines" },
                ["Medium Lines"] = new[] { "Thin Lines" },
                ["Thin Lines"]   = new[] { "Medium Lines" },
            };

        private static void ApplyLineStyle(Document doc, DetailCurve dc,
            string styleName, TitleBlockBuildResult r)
        {
            if (string.IsNullOrEmpty(styleName) || dc == null) return;
            try
            {
                // Title-block line styles like "Wide Lines" / "Medium Lines" /
                // "Thin Lines" can live as subcategories of OST_TitleBlocks
                // (most title-block templates) OR OST_Lines (when the user
                // has migrated them to the generic-lines category). Search
                // both — first hit wins.
                GraphicsStyle hit = LookupLineStyle(doc, BuiltInCategory.OST_TitleBlocks, styleName)
                                 ?? LookupLineStyle(doc, BuiltInCategory.OST_Lines,        styleName);

                // If the template doesn't ship the requested style, AUTO-
                // CREATE it as a subcategory under OST_Lines with a
                // canonical line weight + colour (BS 1192 Annex A — black
                // weight 5 / 3 / 1 for Wide / Medium / Thin). This used to
                // be a manual "open the .rfa, Manage → Object Styles → New
                // Subcategory" step the operator had to do once per
                // template. The factory now does it on first use so the
                // generated .rfa is self-contained.
                if (hit == null && IsCanonicalLineStyle(styleName))
                {
                    hit = AutoCreateLineStyle(doc, styleName, r);
                }

                // Fall-back chain: try the next-thinner style so a line still
                // gets *some* weight rather than landing on the default
                // <Invisible lines> some templates put first.
                if (hit == null)
                {
                    string[] fallbacks =
                        styleName.Equals("Wide Lines",   StringComparison.OrdinalIgnoreCase) ? new[] { "Medium Lines", "Thin Lines" } :
                        styleName.Equals("Medium Lines", StringComparison.OrdinalIgnoreCase) ? new[] { "Wide Lines",   "Thin Lines" } :
                        styleName.Equals("Thin Lines",   StringComparison.OrdinalIgnoreCase) ? new[] { "Medium Lines", "Wide Lines" } :
                                                                                               new[] { "Thin Lines", "Medium Lines", "Wide Lines" };
                    foreach (var fb in fallbacks)
                    {
                        hit = LookupLineStyle(doc, BuiltInCategory.OST_TitleBlocks, fb)
                           ?? LookupLineStyle(doc, BuiltInCategory.OST_Lines,        fb);
                        if (hit != null) break;
                    }
                }

                if (hit != null)
                {
                    dc.LineStyle = hit;
                    return;
                }

                // Ultimate fallback — pick any user-defined subcategory
                // (skipping Revit built-in <Overhead> / <Hidden> / <Sketch>
                // / <Centerline> meta-styles whose patterns look wrong as
                // a generic line-weight fallback).
                var anyStyle = FirstUserSubcategoryStyle(doc, BuiltInCategory.OST_Lines)
                            ?? FirstUserSubcategoryStyle(doc, BuiltInCategory.OST_TitleBlocks);
                if (anyStyle != null)
                {
                    dc.LineStyle = anyStyle;
                    if (_LineStyleNotFoundOnce.TryAdd(styleName, true))
                        r.Warnings.Add($"line style '{styleName}' not found — using '{anyStyle.Name}' as fallback");
                    return;
                }
                if (_LineStyleNotFoundOnce.TryAdd(styleName, true))
                    r.Warnings.Add($"line style '{styleName}' not found (no user-defined subcategory available; left on Revit default)");
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyLineStyle '{styleName}': {ex.Message}"); }
        }

        private static bool IsCanonicalLineStyle(string s) =>
               string.Equals(s, "Wide Lines",   StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Medium Lines", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Thin Lines",   StringComparison.OrdinalIgnoreCase);

        /// <summary>Mints a new subcategory under OST_Lines with the canonical
        /// BS 1192 Annex A weight for Wide / Medium / Thin Lines. Called the
        /// first time the factory needs the style and finds it absent in the
        /// template — caches the resulting GraphicsStyle so subsequent curves
        /// in the same .rfa reuse the same subcategory.</summary>
        private static GraphicsStyle AutoCreateLineStyle(Document doc, string styleName, TitleBlockBuildResult r)
        {
            try
            {
                var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (lines == null) return null;

                // Canonical weight per BS 1192 Annex A.
                int weight = styleName.Equals("Wide Lines",   StringComparison.OrdinalIgnoreCase) ? 5 :
                             styleName.Equals("Medium Lines", StringComparison.OrdinalIgnoreCase) ? 3 :
                                                                                                    1;

                Category sub = doc.Settings.Categories.NewSubcategory(lines, styleName);
                sub.LineColor = new Color(0, 0, 0);
                sub.SetLineWeight(weight, GraphicsStyleType.Projection);
                if (_LineStyleAutoCreatedOnce.TryAdd(styleName, true))
                    r.Warnings.Add($"line style '{styleName}' auto-created under OST_Lines (weight {weight}, BS 1192 Annex A canonical)");
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"AutoCreateLineStyle '{styleName}': {ex.Message}");
                return null;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _LineStyleNotFoundOnce
            = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _LineStyleAutoCreatedOnce
            = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();

        private static GraphicsStyle FirstUserSubcategoryStyle(Document doc, BuiltInCategory parentCat)
        {
            try
            {
                var parent = doc.Settings.Categories.get_Item(parentCat);
                if (parent?.SubCategories == null) return null;
                foreach (Category sub in parent.SubCategories)
                {
                    // Skip Revit built-in meta-styles like <Overhead> /
                    // <Hidden> / <Sketch> / <Centerline> — they have
                    // specific dash/halftone patterns that look wrong
                    // as a fallback for solid sheet borders.
                    if (!string.IsNullOrEmpty(sub.Name) && sub.Name.StartsWith("<"))
                        continue;
                    var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (gs != null) return gs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        private static GraphicsStyle LookupLineStyle(Document doc,
            BuiltInCategory parentCat, string styleName)
        {
            try
            {
                var parent = doc.Settings.Categories.get_Item(parentCat);
                if (parent?.SubCategories == null) return null;
                foreach (Category sub in parent.SubCategories)
                {
                    if (string.Equals(sub.Name, styleName, StringComparison.OrdinalIgnoreCase))
                        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                }
            }
            catch { /* swallowed — caller logs */ }
            return null;
        }

        private static ElementId ResolveTextNoteTypeId(Document doc, string typeName)
        {
            try
            {
                TextNoteType first = null;
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)))
                {
                    if (!(el is TextNoteType tnt)) continue;
                    if (first == null) first = tnt;
                    if (!string.IsNullOrEmpty(typeName)
                        && string.Equals(tnt.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        return tnt.Id;
                }
                return first?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ElementId.InvalidElementId; }
        }

        private static ElementId ResolveFilledRegionTypeId(Document doc, string typeName)
        {
            try
            {
                FilledRegionType first = null;
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType)))
                {
                    if (!(el is FilledRegionType frt)) continue;
                    if (first == null) first = frt;
                    if (!string.IsNullOrEmpty(typeName)
                        && string.Equals(frt.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        return frt.Id;
                }
                return first?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ElementId.InvalidElementId; }
        }

        // Phase 171b — Revit's HorizontalAlign / VerticalAlign enums
        // were renamed (or otherwise mangled) between 2024 and 2025 such
        // that VerticalAlign no longer resolves at compile time on stock
        // installs. To stay portable across 2025 / 2026 / 2027 we parse
        // to integers (matching the enum's underlying values) and let
        // CreateLabelViaReflection convert them to whatever type the
        // loaded RevitAPI's NewLabel signature actually demands.
        //
        // Underlying values match the historic Revit enum:
        //   HorizontalAlign: Left = 0, Center = 1, Right = 2
        //   VerticalAlign:   Top  = 0, Middle = 1, Bottom = 2
        private const int H_LEFT = 0, H_CENTER = 1, H_RIGHT = 2;
        private const int V_TOP  = 0, V_MIDDLE = 1, V_BOTTOM = 2;

        private static int ParseHAlign(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "right":  return H_RIGHT;
                case "center":
                case "centre": return H_CENTER;
                default:       return H_LEFT;
            }
        }

        // The vertical-alignment enum was renamed/relocated between Revit
        // 2023 → 2024 → 2025 — HorizontalAlign was kept as a public enum
        // but its sibling VerticalAlign was either renamed (different
        // capitalisations, suffixes "Style" / "TextAlignment") or moved to
        // a sub-namespace. Rather than guess which, we INVERT the lookup:
        // search for any NewLabel overload whose shape matches
        // (View, XYZ, HorizontalAlign, ?, IList<FamilyParameter>,
        // IList<string>, double) and read the actual vertical-align type
        // straight off ParameterInfo[3]. That way the code self-discovers
        // whatever the current Revit calls it.
        //
        // Two factories are checked in priority order:
        //   1. famDoc.FamilyCreate (FamilyItemFactory) — works for tag /
        //      annotation-symbol families.
        //   2. famDoc.Application.Create (Application creator) — has an
        //      8-arg overload that takes the Document as the first
        //      parameter, which is the title-block-family-compatible
        //      surface (FamilyCreate.NewLabel has been observed missing
        //      from the title-block FamilyItemFactory in Revit 2025).
        private static MethodInfo _newLabelMethod;
        private static object     _newLabelTarget;            // factory the method is invoked on
        private static bool       _newLabelTakesDocFirstArg;  // true for Application.Create.NewLabel
        private static Type       _verticalAlignType;
        private static bool       _newLabelDiscoveryAttempted;

        private static void EnsureNewLabelDiscovered(object famFactory, object appFactory,
            TitleBlockBuildResult r)
        {
            if (_newLabelMethod != null || _newLabelDiscoveryAttempted) return;
            _newLabelDiscoveryAttempted = true;
            try
            {
                var diag = new System.Text.StringBuilder();
                MethodInfo bestMatch = null;
                bool bestTakesDoc   = false;
                object bestTarget   = null;

                // Plan A: 7-arg form on FamilyItemFactory (familyCreate).
                if (famFactory != null)
                {
                    EnumerateLabelLikeMethods(famFactory.GetType(), "FamilyCreate", diag);
                    foreach (var m in famFactory.GetType().GetMethods())
                    {
                        if (m.Name != "NewLabel") continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 7) continue;
                        if (ps[0].ParameterType != typeof(View)) continue;
                        if (ps[1].ParameterType != typeof(XYZ))  continue;
                        if (ps[2].ParameterType != typeof(HorizontalAlign)) continue;
                        if (!typeof(IList<FamilyParameter>).IsAssignableFrom(ps[4].ParameterType)) continue;
                        if (!typeof(IList<string>).IsAssignableFrom(ps[5].ParameterType)) continue;
                        if (ps[6].ParameterType != typeof(double)) continue;
                        if (!ps[3].ParameterType.IsEnum) continue;
                        bestMatch         = m;
                        _verticalAlignType = ps[3].ParameterType;
                        bestTakesDoc      = false;
                        bestTarget        = famFactory;
                        break;
                    }
                }

                // Plan B: 8-arg form on Application.Create with leading Document.
                if (bestMatch == null && appFactory != null)
                {
                    EnumerateLabelLikeMethods(appFactory.GetType(), "Application.Create", diag);
                    foreach (var m in appFactory.GetType().GetMethods())
                    {
                        if (m.Name != "NewLabel") continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 8) continue;
                        if (!typeof(Document).IsAssignableFrom(ps[0].ParameterType)) continue;
                        if (ps[1].ParameterType != typeof(View)) continue;
                        if (ps[2].ParameterType != typeof(XYZ))  continue;
                        if (ps[3].ParameterType != typeof(HorizontalAlign)) continue;
                        if (!typeof(IList<FamilyParameter>).IsAssignableFrom(ps[5].ParameterType)) continue;
                        if (!typeof(IList<string>).IsAssignableFrom(ps[6].ParameterType)) continue;
                        if (ps[7].ParameterType != typeof(double)) continue;
                        if (!ps[4].ParameterType.IsEnum) continue;
                        bestMatch          = m;
                        _verticalAlignType = ps[4].ParameterType;
                        bestTakesDoc       = true;
                        bestTarget         = appFactory;
                        break;
                    }
                }

                _newLabelMethod           = bestMatch;
                _newLabelTakesDocFirstArg = bestTakesDoc;
                _newLabelTarget           = bestTarget;

                // Dual-factory diagnostic — record whether each surface had NewLabel.
                StingLog.Info($"InvokeNewLabel diagnostic — {diag}");
                if (bestMatch == null)
                {
                    r.Warnings.Add(
                        "InvokeNewLabel: no compatible NewLabel overload found on either "
                        + "FamilyCreate (FamilyItemFactory) or Application.Create. Labels skipped — "
                        + "operator must add labels via Family Editor's Label tool. Full method "
                        + "diagnostic in StingTools.log.");
                }
                else
                {
                    StingLog.Info($"InvokeNewLabel: bound to '{bestMatch.DeclaringType?.Name}.{bestMatch.Name}' (vAlign type '{_verticalAlignType?.FullName}', docFirstArg={bestTakesDoc})");
                }
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"InvokeNewLabel discovery: {ex.Message}");
                StingLog.Error("InvokeNewLabel discovery", ex);
            }
        }

        private static void EnumerateLabelLikeMethods(Type factoryType,
            string surfaceLabel, System.Text.StringBuilder diag)
        {
            try
            {
                var methods = factoryType.GetMethods()
                    .Where(m => m.Name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.Name.IndexOf("Create", StringComparison.OrdinalIgnoreCase) == 0
                             || m.Name.IndexOf("New",    StringComparison.OrdinalIgnoreCase) == 0)
                    .OrderBy(m => m.Name)
                    .ToList();
                if (diag.Length > 0) diag.Append("\n");
                diag.Append($"{surfaceLabel} ({factoryType.FullName}): ");
                if (methods.Count == 0)
                {
                    diag.Append("(no Label/Create/New methods)");
                    return;
                }
                bool first = true;
                foreach (var m in methods)
                {
                    if (!first) diag.Append("; ");
                    first = false;
                    var ps = m.GetParameters();
                    diag.Append(m.Name).Append('(');
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) diag.Append(", ");
                        diag.Append(ps[i].ParameterType.Name);
                    }
                    diag.Append(')');
                }
            }
            catch { /* swallow — diagnostic only */ }
        }

        private static object ParseVAlign(string s)
        {
            if (_verticalAlignType == null) return null;
            string memberName = (s ?? "").ToLowerInvariant() switch
            {
                "top"    => "Top",
                "bottom" => "Bottom",
                _        => "Middle",
            };
            // Try the requested member, then common middle-value aliases.
            string[] candidates = memberName == "Middle"
                ? new[] { "Middle", "Center", "Centre" }
                : new[] { memberName };
            foreach (var c in candidates)
            {
                try { return Enum.Parse(_verticalAlignType, c, ignoreCase: true); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            // Last resort: pick the first declared enum value so the call
            // at least dispatches successfully.
            try
            {
                var values = Enum.GetValues(_verticalAlignType);
                if (values.Length > 0) return values.GetValue(0);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        // factory is an Autodesk.Revit.Creation.FamilyItemFactory (the type
        // returned by Document.FamilyCreate). Typed as `object` to avoid
        // having to import the Autodesk.Revit.Creation namespace, which
        // pulls in a `Document` class that collides with the DB.Document
        // already used throughout this file. vAlign is taken as a STRING
        // (e.g. "Top" / "Middle" / "Bottom") rather than a parsed enum
        // because the actual enum type isn't known until EnsureNewLabel-
        // Discovered runs — the parse happens after discovery.
        private static Element InvokeNewLabel(Document famDoc, View view,
            XYZ origin, HorizontalAlign hAlign, string vAlignName,
            IList<FamilyParameter> labelParameters, IList<string> prefixSuffix,
            double size, TitleBlockBuildResult r)
        {
            if (famDoc == null) return null;
            object famFactory = null;
            object appFactory = null;
            try { famFactory = famDoc.FamilyCreate; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { appFactory = famDoc.Application?.Create; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            EnsureNewLabelDiscovered(famFactory, appFactory, r);
            if (_newLabelMethod == null || _newLabelTarget == null) return null;
            object vAlign = ParseVAlign(vAlignName);
            try
            {
                object[] args = _newLabelTakesDocFirstArg
                    ? new object[] { famDoc, view, origin, hAlign, vAlign, labelParameters, prefixSuffix, size }
                    : new object[] {           view, origin, hAlign, vAlign, labelParameters, prefixSuffix, size };
                return _newLabelMethod.Invoke(_newLabelTarget, args) as Element;
            }
            catch (TargetInvocationException tie)
            {
                r.Warnings.Add($"InvokeNewLabel: {(tie.InnerException ?? tie).Message}");
                return null;
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"InvokeNewLabel: {ex.Message}");
                return null;
            }
        }

        // Reflection-based NewLabel invocation. Caches the MethodInfo
        // on first use. Throws on missing/unmatched signature so the
        // caller's try/catch wraps it into a per-element warning.
        private static System.Reflection.MethodInfo _newLabelMi;

        private static Element CreateLabelViaReflection(Document famDoc, View view,
            XYZ origin, int hAlignInt, int vAlignInt,
            IList<FamilyParameter> labelParams, IList<string> prefixSuffix,
            double sizeFt)
        {
            var fc = famDoc.FamilyCreate;
            if (_newLabelMi == null)
            {
                // The NewLabel overload we want has 7 params and the
                // 3rd / 4th are the alignment enums. Narrow by name
                // and arity, then trust the converted enum values.
                foreach (var m in fc.GetType().GetMethods())
                {
                    if (m.Name != "NewLabel") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 7) continue;
                    if (!ps[2].ParameterType.IsEnum) continue;
                    if (!ps[3].ParameterType.IsEnum) continue;
                    _newLabelMi = m;
                    break;
                }
                if (_newLabelMi == null)
                    throw new InvalidOperationException("FamilyCreate.NewLabel(7-arg, enum-h, enum-v) overload not found in this Revit version");
            }
            var ps2 = _newLabelMi.GetParameters();
            var hAlign = Enum.ToObject(ps2[2].ParameterType, hAlignInt);
            var vAlign = Enum.ToObject(ps2[3].ParameterType, vAlignInt);
            return (Element)_newLabelMi.Invoke(fc, new object[]
            { view, origin, hAlign, vAlign, labelParams, prefixSuffix, sizeFt });
        }

        // Reflection-based NewLabel invocation. Caches the MethodInfo
        // on first use. Throws on missing/unmatched signature so the
        // caller's try/catch wraps it into a per-element warning.
        private static System.Reflection.MethodInfo _newLabelMi;

        private static Element CreateLabelViaReflection(Document famDoc, View view,
            XYZ origin, int hAlignInt, int vAlignInt,
            IList<FamilyParameter> labelParams, IList<string> prefixSuffix,
            double sizeFt)
        {
            var fc = famDoc.FamilyCreate;
            if (_newLabelMi == null)
            {
                // The NewLabel overload we want has 7 params and the
                // 3rd / 4th are the alignment enums. Narrow by name
                // and arity, then trust the converted enum values.
                foreach (var m in fc.GetType().GetMethods())
                {
                    if (m.Name != "NewLabel") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 7) continue;
                    if (!ps[2].ParameterType.IsEnum) continue;
                    if (!ps[3].ParameterType.IsEnum) continue;
                    _newLabelMi = m;
                    break;
                }
                if (_newLabelMi == null)
                    throw new InvalidOperationException("FamilyCreate.NewLabel(7-arg, enum-h, enum-v) overload not found in this Revit version");
            }
            var ps2 = _newLabelMi.GetParameters();
            var hAlign = Enum.ToObject(ps2[2].ParameterType, hAlignInt);
            var vAlign = Enum.ToObject(ps2[3].ParameterType, vAlignInt);
            return (Element)_newLabelMi.Invoke(fc, new object[]
            { view, origin, hAlign, vAlign, labelParams, prefixSuffix, sizeFt });
        }
    }
}
