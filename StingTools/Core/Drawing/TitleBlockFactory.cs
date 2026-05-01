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
//   4. In one Transaction:
//      a. Add every shared / internal / calculated parameter via
//         FamilyManager in two passes (Phase 171): pass 1 mints
//         every parameter without a formula; pass 2 sets formulas
//         once the full map is populated, so any formula can
//         reference any param regardless of declaration order.
//      b. Build reflow groups — Phase 171 simplifies to pure
//         Strategy A: per group, mint a YesNo parameter
//         <groupId>_VIS = STING_BIM_MODE_BOOL. Children bind
//         their Visible to it. Strip outer auto-shrink is achieved
//         in the spec via Strategy A line pairing
//         (visibility:"bimOnly" + visibility:"nonBimOnly").
//      c. Place lines — NewDetailCurve. Apply LineStyle (with
//         fallback chain "Wide" → "Heavy" → "Thick" → "Medium"
//         → "Thin"). Bind Visible via ResolveGate(visibility,
//         bimOnly).
//      d. Place static text — TextNote.Create. Bind Visible.
//      e. Place labels — NewLabel bound to FamilyParameter. Bind
//         Visible.
//      f. Place label pairs — two NewLabel calls at the same anchor,
//         label A bound to paramBim, label B bound to paramNonBim
//         (or Sheet Number built-in). Reciprocal Visible flags.
//      g. Place filled regions — FilledRegion.Create. Bind Visible.
//   5. Commit transaction.
//   6. SaveAs with overwrite.
//   7. Close(false).
//
// Returns a per-family result with counts + warnings + errors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public int    LabelPairsPlaced { get; set; }
        public int    StaticTextPlaced { get; set; }
        public int    FilledRegionsPlaced { get; set; }
        public int    ReflowGroupsBuilt { get; set; }
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
            var app = uiApp.Application;

            // 1. Resolve template
            string rftPath = ResolveTemplatePath(spec.TemplateRft);
            if (string.IsNullOrEmpty(rftPath))
            {
                r.Errors.Add($"template not found: {spec.TemplateRft}");
                return r;
            }

            // 2. Open template
            Document famDoc = null;
            string originalSpFile = app.SharedParametersFilename;
            try
            {
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

                // 4. Build inside a transaction
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

                    // 4b. Reflow groups (must come before lines/labels so
                    // we know which group each child belongs to)
                    var heightParamByGroup = BuildReflowGroups(famDoc, fm,
                        spec, paramByName, view, r);

                    // 4c. Lines (family-level + group children)
                    foreach (var line in spec.Lines)
                        PlaceLine(famDoc, fm, view, line, paramByName,
                                  visibilityGate: ResolveGate(line.Visibility,
                                      line.BimOnly, paramByName, spec.BimModeParameter), r);

                    // 4d. Static text
                    foreach (var st in spec.StaticText)
                        PlaceStaticText(famDoc, fm, view, st, paramByName,
                                  visibilityGate: ResolveGate(st.Visibility,
                                      st.BimOnly, paramByName, spec.BimModeParameter), r);

                    // 4e. Labels
                    foreach (var lbl in spec.Labels)
                        PlaceLabel(famDoc, fm, view, lbl, paramByName,
                                  visibilityGate: ResolveGate(lbl.Visibility,
                                      lbl.BimOnly, paramByName, spec.BimModeParameter), r);

                    // 4f. Label pairs (two-label trick)
                    foreach (var lp in spec.LabelPairs)
                        PlaceLabelPair(famDoc, fm, view, lp, paramByName,
                                  spec.BimModeParameter, r);

                    // 4g. Filled regions
                    foreach (var fr in spec.FilledRegions)
                        PlaceFilledRegion(famDoc, fm, view, fr, paramByName,
                                  visibilityGate: ResolveGate(fr.Visibility,
                                      fr.BimOnly, paramByName, spec.BimModeParameter), r);

                    // 4h. Reflow group children
                    foreach (var grp in spec.ReflowGroups)
                    {
                        if (!heightParamByGroup.TryGetValue(grp.Id, out var hp))
                            continue;
                        foreach (var line in grp.Lines)
                            PlaceLine(famDoc, fm, view, line, paramByName, hp, r);
                        foreach (var st in grp.StaticText)
                            PlaceStaticText(famDoc, fm, view, st, paramByName, hp, r);
                        foreach (var lbl in grp.Labels)
                            PlaceLabel(famDoc, fm, view, lbl, paramByName, hp, r);
                        foreach (var fill in grp.FilledRegions)
                            PlaceFilledRegion(famDoc, fm, view, fill, paramByName, hp, r);
                    }

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
            // Final fallback: alongside the addin DLL.
            return Path.Combine(StingToolsApp.AssemblyPath ?? ".", specPath);
        }

        // ── Parameter creation ───────────────────────────────────────────

        private static void AddAllParameters(FamilyManager fm, DefinitionFile defFile,
            TitleBlockSpec spec, Dictionary<string, FamilyParameter> map,
            TitleBlockBuildResult r)
        {
            // Phase 171 — two-pass creation. Pass 1 mints every
            // parameter (internal + shared) WITHOUT formulas, so a
            // formula in pass 2 can reference any other parameter
            // regardless of declaration order. Defaults are still
            // applied in pass 1 (they don't need other params).
            var pendingFormulas = new List<(string Name, string Formula)>();

            // 1. BIM_MODE_BOOL — the gate every cell references.
            var bim = AddInternalParameter(fm, spec.BimModeParameter,
                "YesNo", "IdentityData", isInstance: true,
                defaultValue: "1", formula: null, r);
            if (bim != null) map[spec.BimModeParameter] = bim;

            // 2. NOT_BIM inverse. Formula deferred to pass 2.
            string notBimName = spec.BimModeParameter.Replace("STING_BIM_MODE", "STING_NOT_BIM");
            if (notBimName == spec.BimModeParameter) notBimName = spec.BimModeParameter + "_INVERSE";
            var notBim = AddInternalParameter(fm, notBimName, "YesNo",
                "IdentityData", isInstance: true, defaultValue: null,
                formula: null, r);
            if (notBim != null)
            {
                map[notBimName] = notBim;
                map["__NOT_BIM__"] = notBim;
                pendingFormulas.Add((notBimName, $"not({spec.BimModeParameter})"));
            }

            // 3. Spec-declared parameters — pass 1 (no formulas yet).
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
                        if (specId == SpecTypeId.Boolean.YesNo)
                            fm.Set(fp, defaultValue == "1" || string.Equals(defaultValue, "true", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                        else if (double.TryParse(defaultValue, out var d))
                            fm.Set(fp, d);
                        else
                            fm.Set(fp, defaultValue);
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

        // ── Reflow group construction (Strategy A — Phase 171) ──────────

        /// <summary>Phase 170 originally minted a Length parameter per
        /// group with formula `if(BIM, fullH mm, collapsedH mm)` and a
        /// sibling YesNo `BIM and (H > 0 mm)`. Revit's family-formula
        /// parser rejected the metric-literal expressions in practice
        /// (`0.0 mm` and `> 0 mm` both produce "invalid formula
        /// string"), so Phase 171 simplifies to pure Strategy A:
        ///
        ///   Per group, mint one YesNo parameter `&lt;groupId&gt;_VIS`
        ///   with formula = STING_BIM_MODE_BOOL. Children bind their
        ///   Visible to it. When BIM toggles to 0, every child of every
        ///   group hides simultaneously.
        ///
        /// The strip outer top edge is still parametric — but via
        /// Strategy A line pairing in the spec: a `bimOnly` line at
        /// y = full-strip-top + a `nonBimOnly` line at y = collapsed-
        /// strip-top. See LineSpec.Visibility ("nonBimOnly") added in
        /// Phase 171.
        ///
        /// CollapsedHeightMm in the spec is now informational only;
        /// the engine no longer creates Length parameters.</summary>
        private static Dictionary<string, FamilyParameter> BuildReflowGroups(
            Document famDoc, FamilyManager fm, TitleBlockSpec spec,
            Dictionary<string, FamilyParameter> map, View view,
            TitleBlockBuildResult r)
        {
            var visByGroup = new Dictionary<string, FamilyParameter>(
                StringComparer.OrdinalIgnoreCase);
            // Defer formula assignment until the BIM_MODE param exists in
            // the family, which it always does at this point — but match
            // the post-pass-2 ordering of AddAllParameters by setting it
            // directly here (the formula references only one param so no
            // ordering risk).
            foreach (var grp in spec.ReflowGroups)
            {
                if (string.IsNullOrEmpty(grp?.Id)) continue;
                try
                {
                    var visName = $"{grp.Id}_VIS";
                    var vp = AddInternalParameter(fm, visName, "YesNo",
                        "Constraints", isInstance: false,
                        defaultValue: null,
                        formula: spec.BimModeParameter, r);
                    if (vp != null)
                    {
                        map[visName] = vp;
                        visByGroup[grp.Id] = vp;
                    }
                    r.ReflowGroupsBuilt++;
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"BuildReflowGroup '{grp.Id}': {ex.Message}");
                }
            }
            return visByGroup;
        }

        // ── Visibility gating ────────────────────────────────────────────

        /// <summary>Returns the FamilyParameter the element should bind
        /// its Visible to: either the BIM_MODE parameter directly (for
        /// bimOnly cells outside a group) or null (always visible).</summary>
        private static FamilyParameter GateForBimOnly(bool bimOnly,
            Dictionary<string, FamilyParameter> map, string bimParamName)
        {
            if (!bimOnly) return null;
            return map.TryGetValue(bimParamName, out var fp) ? fp : null;
        }

        /// <summary>Phase 171 — three-way visibility resolver. The
        /// per-element `visibility` field takes precedence over the
        /// legacy `bimOnly` boolean. Recognised values:
        ///   "always"      → null gate (element always visible)
        ///   "bimOnly"     → BIM_MODE gate (visible when BIM = 1)
        ///   "nonBimOnly"  → NOT_BIM gate (visible when BIM = 0; new
        ///                   in Phase 171 — enables Strategy A strip
        ///                   auto-shrink: pair a `bimOnly` line at the
        ///                   full strip top with a `nonBimOnly` line at
        ///                   the collapsed strip top).
        /// When `visibility` is null/empty/unrecognised, falls through
        /// to the legacy bimOnly behaviour.</summary>
        private static FamilyParameter ResolveGate(string visibility,
            bool bimOnlyLegacy, Dictionary<string, FamilyParameter> map,
            string bimParamName)
        {
            if (!string.IsNullOrEmpty(visibility))
            {
                switch (visibility.Trim().ToLowerInvariant())
                {
                    case "always":
                        return null;
                    case "bimonly":
                        return map.TryGetValue(bimParamName, out var bim) ? bim : null;
                    case "nonbimonly":
                    case "nonbim":
                        return map.TryGetValue("__NOT_BIM__", out var notBim) ? notBim : null;
                }
            }
            return GateForBimOnly(bimOnlyLegacy, map, bimParamName);
        }

        /// <summary>Associate an element's Visible parameter to a
        /// family parameter so flipping the family parameter hides the
        /// element. Skips when gate is null (element is always
        /// visible).</summary>
        private static void BindVisibility(FamilyManager fm, Element el,
            FamilyParameter gate, TitleBlockBuildResult r)
        {
            if (el == null || gate == null) return;
            try
            {
                var vis = el.LookupParameter("Visible");
                if (vis == null) return;
                if (fm.CanElementParameterBeAssociated(vis))
                    fm.AssociateElementParameterToFamilyParameter(vis, gate);
                else
                    r.Warnings.Add($"Element {el.Id} 'Visible' not associatable to {gate.Definition?.Name}");
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"BindVisibility: {ex.Message}");
            }
        }

        // ── Geometry placement ───────────────────────────────────────────

        private static void PlaceLine(Document famDoc, FamilyManager fm,
            View view, LineSpec spec, Dictionary<string, FamilyParameter> map,
            FamilyParameter visibilityGate, TitleBlockBuildResult r)
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
                BindVisibility(fm, dc, visibilityGate, r);
                r.LinesPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceLine: {ex.Message}"); }
        }

        private static void PlaceStaticText(Document famDoc, FamilyManager fm,
            View view, StaticTextSpec spec, Dictionary<string, FamilyParameter> map,
            FamilyParameter visibilityGate, TitleBlockBuildResult r)
        {
            if (spec?.Anchor == null || spec.Anchor.Length < 2
                || string.IsNullOrEmpty(spec.Text)) return;
            try
            {
                var pos = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var typeId = ResolveTextNoteTypeId(famDoc, spec.TextTypeName);
                if (typeId == ElementId.InvalidElementId)
                { r.Warnings.Add($"PlaceStaticText: no text type for '{spec.TextTypeName}'"); return; }
                var tn = TextNote.Create(famDoc, view.Id, pos, spec.Text, typeId);
                BindVisibility(fm, tn, visibilityGate, r);
                r.StaticTextPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceStaticText '{spec.Text}': {ex.Message}"); }
        }

        private static void PlaceLabel(Document famDoc, FamilyManager fm,
            View view, LabelSpec spec, Dictionary<string, FamilyParameter> map,
            FamilyParameter visibilityGate, TitleBlockBuildResult r)
        {
            if (spec?.Anchor == null || spec.Anchor.Length < 2
                || string.IsNullOrEmpty(spec.Param)) return;
            if (!map.TryGetValue(spec.Param, out var fp))
            { r.Warnings.Add($"PlaceLabel: param '{spec.Param}' not added — skipped"); return; }
            try
            {
                var origin = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var hAlign = ParseHAlign(spec.HAlign);
                var vAlign = ParseVAlign(spec.VAlign);
                var sizeFt = MmToFt(spec.Size);
                IList<FamilyParameter> labelParams = new List<FamilyParameter> { fp };
                IList<string> prefixSuffix = new List<string> { spec.Prefix ?? "", spec.Suffix ?? "" };
                var lbl = CreateLabelViaReflection(famDoc, view, origin, hAlign, vAlign,
                    labelParams, prefixSuffix, sizeFt);
                BindVisibility(fm, lbl, visibilityGate, r);
                r.LabelsPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceLabel '{spec.Param}': {ex.Message}"); }
        }

        private static void PlaceLabelPair(Document famDoc, FamilyManager fm,
            View view, LabelPairSpec spec, Dictionary<string, FamilyParameter> map,
            string bimParamName, TitleBlockBuildResult r)
        {
            if (spec?.Anchor == null || spec.Anchor.Length < 2) return;
            try
            {
                var origin = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var hAlign = ParseHAlign(spec.HAlign);
                var vAlign = ParseVAlign(spec.VAlign);
                var sizeFt = MmToFt(spec.Size);
                IList<string> prefixSuffix = new List<string> { "", "" };

                // Label A — visible when BIM = 1
                if (map.TryGetValue(spec.ParamBim, out var fpA))
                {
                    var lblA = CreateLabelViaReflection(famDoc, view, origin, hAlign,
                        vAlign, new List<FamilyParameter> { fpA }, prefixSuffix, sizeFt);
                    map.TryGetValue(bimParamName, out var bimFp);
                    BindVisibility(fm, lblA, bimFp, r);
                }

                // Label B — visible when BIM = 0 (paramNonBim, or built-in
                // Sheet Number when paramNonBimIsBuiltIn = true)
                FamilyParameter fpB = null;
                if (spec.ParamNonBimIsBuiltIn)
                {
                    // Built-in sheet number doesn't appear in FamilyManager —
                    // the workable substitute is to bind to a calculated
                    // family parameter with a fixed-text fallback. Phase II
                    // upgrade: load a label that reads SHEET_NUMBER built-in
                    // via a Sheet shared param. For now, fall back to the
                    // declared paramNonBim if present, else skip.
                    if (!string.IsNullOrEmpty(spec.ParamNonBim)
                        && map.TryGetValue(spec.ParamNonBim, out fpB)) { /* ok */ }
                    else
                    {
                        r.Warnings.Add($"LabelPair: built-in Sheet Number binding not yet supported (Phase II) — non-BIM label skipped at ({spec.Anchor[0]}, {spec.Anchor[1]})");
                    }
                }
                else if (!string.IsNullOrEmpty(spec.ParamNonBim))
                {
                    map.TryGetValue(spec.ParamNonBim, out fpB);
                }

                if (fpB != null)
                {
                    var lblB = CreateLabelViaReflection(famDoc, view, origin, hAlign,
                        vAlign, new List<FamilyParameter> { fpB }, prefixSuffix, sizeFt);
                    map.TryGetValue("__NOT_BIM__", out var notBim);
                    BindVisibility(fm, lblB, notBim, r);
                }
                r.LabelPairsPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceLabelPair: {ex.Message}"); }
        }

        private static void PlaceFilledRegion(Document famDoc, FamilyManager fm,
            View view, FilledRegionSpec spec, Dictionary<string, FamilyParameter> map,
            FamilyParameter visibilityGate, TitleBlockBuildResult r)
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
                var fr = FilledRegion.Create(famDoc, typeId, view.Id,
                    new List<CurveLoop> { loop });
                BindVisibility(fm, fr, visibilityGate, r);
                r.FilledRegionsPlaced++;
            }
            catch (Exception ex) { r.Warnings.Add($"PlaceFilledRegion: {ex.Message}"); }
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
                var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (lines == null) return;

                // Build the candidate name list: requested → fallbacks.
                var names = new List<string> { styleName };
                if (LineStyleFallbacks.TryGetValue(styleName, out var fb))
                    names.AddRange(fb);

                foreach (var name in names)
                {
                    foreach (Category sub in lines.SubCategories)
                    {
                        if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                            if (gs != null) dc.LineStyle = gs;
                            return;
                        }
                    }
                }

                // No match found in the chain — line keeps its default
                // style (still drawn). Warn once with the chain tried.
                r.Warnings.Add($"line style '{styleName}' not found (no fallback available)");
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyLineStyle '{styleName}': {ex.Message}"); }
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

        private static int ParseVAlign(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "top":    return V_TOP;
                case "bottom": return V_BOTTOM;
                default:       return V_MIDDLE;
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
    }
}
