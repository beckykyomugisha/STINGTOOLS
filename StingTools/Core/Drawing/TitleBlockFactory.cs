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
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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
                try { originalSpFile = app.SharedParametersFilename; } catch { }
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
                try { famDoc?.Close(false); } catch { }
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
                catch { }
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
            catch { }
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
            catch { }
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
            foreach (var p in spec.Parameters)
            {
                if (string.IsNullOrEmpty(p?.Name)) continue;
                if (map.ContainsKey(p.Name)) continue;
                FamilyParameter fp = null;
                if (string.Equals(p.Kind, "internal", StringComparison.OrdinalIgnoreCase))
                {
                    fp = AddInternalParameter(fm, p.Name, p.Type, p.Group,
                        p.Instance, p.Default, p.Formula, r);
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
                var hAlign = ParseHAlign(spec.HAlign);
                var sizeFt = MmToFt(spec.Size);
                IList<FamilyParameter> labelParams = new List<FamilyParameter> { fp };
                IList<string> prefixSuffix = new List<string> { spec.Prefix ?? "", spec.Suffix ?? "" };
                var lbl = InvokeNewLabel(famDoc.FamilyCreate, view, origin, hAlign, spec.VAlign,
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

        private static void NewSlotReferencePlane(Document famDoc, View view,
            string name, XYZ p1, XYZ p2, TitleBlockBuildResult r)
        {
            try
            {
                var rp = famDoc.FamilyCreate.NewReferencePlane(p1, p2, XYZ.BasisZ, view);
                if (rp != null && !string.IsNullOrEmpty(name))
                {
                    try { rp.Name = name; }
                    catch (Exception nameEx) { r.Warnings.Add($"NewSlotReferencePlane name '{name}': {nameEx.Message}"); }
                }
            }
            catch (Exception ex) { r.Warnings.Add($"NewSlotReferencePlane '{name}': {ex.Message}"); }
        }

        // ── Resolvers ────────────────────────────────────────────────────

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

                // Ultimate fallback — if the template doesn't ship the
                // standard "Wide / Medium / Thin Lines" subcategories at
                // all (clean Revit template, custom locale, custom
                // template content), look for any USER-DEFINED subcategory
                // we can use. Skip <Bracketed> entries — those are Revit
                // built-in meta-styles like <Overhead> / <Hidden> /
                // <Sketch> / <Centerline> with specific dash/halftone
                // patterns that look wrong as a sheet border. If only
                // bracketed entries exist, leave the curve on Revit's
                // default style (no warning per-curve).
                var anyStyle = FirstUserSubcategoryStyle(doc, BuiltInCategory.OST_Lines)
                            ?? FirstUserSubcategoryStyle(doc, BuiltInCategory.OST_TitleBlocks);
                if (anyStyle != null)
                {
                    dc.LineStyle = anyStyle;
                    // Warn ONCE per unique missing style name (TryAdd
                    // returns true on first add, false on subsequent —
                    // the previous version checked Count<=3 which fired
                    // every call when Count was still <=3, hence the
                    // 60-line warning spam).
                    if (_LineStyleNotFoundOnce.TryAdd(styleName, true))
                        r.Warnings.Add($"line style '{styleName}' not found — using '{anyStyle.Name}' as fallback");
                    return;
                }
                if (_LineStyleNotFoundOnce.TryAdd(styleName, true))
                    r.Warnings.Add($"line style '{styleName}' not found (no user-defined subcategory available; left on Revit default)");
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyLineStyle '{styleName}': {ex.Message}"); }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _LineStyleNotFoundOnce
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
            catch { }
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
            catch { return ElementId.InvalidElementId; }
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
            catch { return ElementId.InvalidElementId; }
        }

        private static HorizontalAlign ParseHAlign(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "right":  return HorizontalAlign.Right;
                case "center":
                case "centre": return HorizontalAlign.Center;
                default:       return HorizontalAlign.Left;
            }
        }

        // The vertical-alignment enum was renamed/relocated between Revit
        // 2023 → 2024 → 2025 — HorizontalAlign was kept as a public enum
        // but its sibling VerticalAlign was either renamed (different
        // capitalisations, suffixes "Style" / "TextAlignment") or moved to
        // a sub-namespace. Rather than guess which, we INVERT the lookup:
        // search FamilyItemFactory for any NewLabel overload whose shape
        // matches (View, XYZ, HorizontalAlign, ?, IList<FamilyParameter>,
        // IList<string>, double) and read the actual vertical-align type
        // straight off ParameterInfo[3]. That way the code self-discovers
        // whatever the current Revit calls it.
        private static MethodInfo _newLabelMethod;
        private static Type       _verticalAlignType;
        private static bool       _newLabelDiscoveryAttempted;

        private static void EnsureNewLabelDiscovered(object factory, TitleBlockBuildResult r)
        {
            if (_newLabelMethod != null || _newLabelDiscoveryAttempted) return;
            _newLabelDiscoveryAttempted = true;
            if (factory == null) return;
            try
            {
                var factoryType = factory.GetType();
                var allMethods = factoryType.GetMethods();
                var labelLikeMethods = allMethods
                    .Where(m => m.Name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.Name.IndexOf("Create", StringComparison.OrdinalIgnoreCase) == 0
                             || m.Name.IndexOf("New",    StringComparison.OrdinalIgnoreCase) == 0)
                    .ToList();

                MethodInfo bestMatch = null;
                var diag = new System.Text.StringBuilder();
                foreach (var m in labelLikeMethods)
                {
                    var ps = m.GetParameters();
                    diag.Append(diag.Length == 0 ? "" : "; ");
                    diag.Append(m.Name).Append("(");
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) diag.Append(", ");
                        diag.Append(ps[i].ParameterType.Name);
                    }
                    diag.Append(")");

                    if (m.Name != "NewLabel") continue;
                    if (ps.Length != 7) continue;
                    if (ps[0].ParameterType != typeof(View)) continue;
                    if (ps[1].ParameterType != typeof(XYZ)) continue;
                    if (ps[2].ParameterType != typeof(HorizontalAlign)) continue;
                    if (!typeof(IList<FamilyParameter>).IsAssignableFrom(ps[4].ParameterType)) continue;
                    if (!typeof(IList<string>).IsAssignableFrom(ps[5].ParameterType)) continue;
                    if (ps[6].ParameterType != typeof(double)) continue;
                    if (!ps[3].ParameterType.IsEnum) continue;
                    bestMatch = m;
                    _verticalAlignType = ps[3].ParameterType;
                    break;
                }

                _newLabelMethod = bestMatch;

                // Always log the diagnostic — successful or not — to
                // StingTools.log so we have a permanent record of which
                // overloads existed in this Revit build. Surface it as a
                // result-dialog warning only when discovery fails.
                StingLog.Info($"InvokeNewLabel diagnostic: factory type = {factoryType.FullName} (assembly: {factoryType.Assembly.GetName().Name})");
                StingLog.Info($"InvokeNewLabel label-like overloads ({labelLikeMethods.Count}): {(diag.Length > 0 ? diag.ToString() : "(none)")}");

                if (bestMatch == null)
                {
                    // Stripped diagnostic — list method NAMES only (signatures
                    // can blow past the TaskDialog character limit when the
                    // factory has dozens of overloads).
                    var labelNames = string.Join(", ", labelLikeMethods.Select(m => m.Name).Distinct());
                    r.Warnings.Add(
                        $"InvokeNewLabel: no compatible NewLabel/CreateLabel overload found on '{factoryType.Name}'. "
                        + $"Methods checked (names): {(string.IsNullOrEmpty(labelNames) ? "(none)" : labelNames)}. "
                        + $"Full diagnostic in StingTools.log.");
                }
                else
                {
                    StingLog.Info($"InvokeNewLabel: bound to '{bestMatch.Name}' on '{factoryType.Name}', vAlign type = '{_verticalAlignType?.FullName ?? "(none)"}'");
                }
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"InvokeNewLabel discovery: {ex.Message}");
                StingLog.Error("InvokeNewLabel discovery", ex);
            }
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
                catch { }
            }
            // Last resort: pick the first declared enum value so the call
            // at least dispatches successfully.
            try
            {
                var values = Enum.GetValues(_verticalAlignType);
                if (values.Length > 0) return values.GetValue(0);
            }
            catch { }
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
        private static Element InvokeNewLabel(object factory, View view,
            XYZ origin, HorizontalAlign hAlign, string vAlignName,
            IList<FamilyParameter> labelParameters, IList<string> prefixSuffix,
            double size, TitleBlockBuildResult r)
        {
            if (factory == null) return null;
            EnsureNewLabelDiscovered(factory, r);
            if (_newLabelMethod == null) return null;
            object vAlign = ParseVAlign(vAlignName);
            try
            {
                return _newLabelMethod.Invoke(factory, new object[]
                {
                    view, origin, hAlign, vAlign, labelParameters, prefixSuffix, size,
                }) as Element;
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
    }
}
