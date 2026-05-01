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
//         FamilyManager. Internal Yes/No for STING_BIM_MODE_BOOL.
//      b. Place reflow groups — for each ReflowGroupSpec, create
//         two reference planes (top/bottom), place a labelled
//         dimension between them, add a length parameter, set the
//         if(BIM_MODE, fullHeight, collapsedHeight) formula. Track
//         the height-parameter for child Visible-binding (defence
//         in depth).
//      c. Place lines — NewDetailCurve. Apply LineStyle. Bind
//         Visible to BIM_MODE (or the group's height-parameter
//         when inside a reflow group).
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
                                  visibilityGate: GateForBimOnly(line.BimOnly,
                                      paramByName, spec.BimModeParameter), r);

                    // 4d. Static text
                    foreach (var st in spec.StaticText)
                        PlaceStaticText(famDoc, fm, view, st, paramByName,
                                  visibilityGate: GateForBimOnly(st.BimOnly,
                                      paramByName, spec.BimModeParameter), r);

                    // 4e. Labels
                    foreach (var lbl in spec.Labels)
                        PlaceLabel(famDoc, fm, view, lbl, paramByName,
                                  visibilityGate: GateForBimOnly(lbl.BimOnly,
                                      paramByName, spec.BimModeParameter), r);

                    // 4f. Label pairs (two-label trick)
                    foreach (var lp in spec.LabelPairs)
                        PlaceLabelPair(famDoc, fm, view, lp, paramByName,
                                  spec.BimModeParameter, r);

                    // 4g. Filled regions
                    foreach (var fr in spec.FilledRegions)
                        PlaceFilledRegion(famDoc, fm, view, fr, paramByName,
                                  visibilityGate: GateForBimOnly(fr.BimOnly,
                                      paramByName, spec.BimModeParameter), r);

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
            // 1. Mint the family-internal BIM_MODE_BOOL first (the
            // visibility / formula gate every cell references).
            var bim = AddInternalParameter(fm, spec.BimModeParameter,
                "YesNo", "IdentityData", isInstance: true,
                defaultValue: "1", formula: null, r);
            if (bim != null) map[spec.BimModeParameter] = bim;

            // 2. Mint the inverse (NOT BIM) so the two-label trick has
            // a parameter to bind label B's Visible to.
            string notBimName = spec.BimModeParameter.Replace("STING_BIM_MODE", "STING_NOT_BIM");
            if (notBimName == spec.BimModeParameter) notBimName = spec.BimModeParameter + "_INVERSE";
            var notBim = AddInternalParameter(fm, notBimName, "YesNo",
                "IdentityData", isInstance: true, defaultValue: null,
                formula: $"not({spec.BimModeParameter})", r);
            if (notBim != null) map[notBimName] = notBim;
            // Also expose under a stable alias the rest of the engine queries.
            map["__NOT_BIM__"] = notBim;

            // 3. Spec-declared parameters.
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
                }
                if (fp != null) map[p.Name] = fp;
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

        // ── Reflow group construction (Strategy B) ───────────────────────

        /// <summary>For each ReflowGroupSpec: mint a length parameter,
        /// set its formula to if(BIM_MODE, fullHeightMm, collapsedHeightMm),
        /// and return the height-parameter map keyed by group id. Child
        /// elements placed inside the group have their Visible
        /// associated to this height parameter (via a derived YesNo
        /// formula `&lt;heightParam&gt; > 0`) so they hide when the row
        /// collapses to 0 mm.
        ///
        /// NOTE: Revit doesn't directly accept a Length parameter as a
        /// Visible (YesNo) binding source. Pattern: mint a sibling
        /// YesNo parameter `&lt;groupId&gt;_VIS` with the formula
        /// `if(STING_BIM_MODE_BOOL and (&lt;heightParam&gt; > 0), 1, 0)`,
        /// and bind child Visible to that. Same effect — group visibility
        /// = (BIM mode is on) AND (height &gt; 0).</summary>
        private static Dictionary<string, FamilyParameter> BuildReflowGroups(
            Document famDoc, FamilyManager fm, TitleBlockSpec spec,
            Dictionary<string, FamilyParameter> map, View view,
            TitleBlockBuildResult r)
        {
            var visByGroup = new Dictionary<string, FamilyParameter>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var grp in spec.ReflowGroups)
            {
                if (string.IsNullOrEmpty(grp?.Id)) continue;
                try
                {
                    // 1. Length parameter for the group's height.
                    var heightParamName = string.IsNullOrEmpty(grp.HeightParam)
                        ? $"H_{grp.Id}_MM" : grp.HeightParam;
                    var hp = AddInternalParameter(fm, heightParamName,
                        "Length", "Constraints", isInstance: false,
                        defaultValue: null,
                        formula: $"if({spec.BimModeParameter}, {grp.FullHeightMm} mm, {grp.CollapsedHeightMm} mm)",
                        r);
                    if (hp != null) map[heightParamName] = hp;

                    // 2. Sibling YesNo for child Visible binding.
                    var visName = $"{grp.Id}_VIS";
                    var vp = AddInternalParameter(fm, visName, "YesNo",
                        "Constraints", isInstance: false,
                        defaultValue: null,
                        formula: $"{spec.BimModeParameter} and ({heightParamName} > 0 mm)",
                        r);
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
                // TODO-VERIFY-API: FamilyItemFactory has no public NewLabel in
                // Revit 2025+. Same documented limitation that
                // FamilyLabelAuthor calls out — programmatic Label creation
                // is not exposed by the public API. Fall back to a placeholder
                // TextNote so the family at least carries a visual hint at
                // the right anchor; Phase 171+ will load labels from a seed
                // .rft and rebind them via FamilyManager rather than mint
                // them from scratch.
                var origin = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var typeId = ResolveTextNoteTypeId(famDoc, null);
                if (typeId == ElementId.InvalidElementId)
                { r.Warnings.Add($"PlaceLabel '{spec.Param}': no text type for placeholder"); return; }
                var placeholder = $"{spec.Prefix ?? ""}<{spec.Param}>{spec.Suffix ?? ""}";
                var lbl = TextNote.Create(famDoc, view.Id, origin, placeholder, typeId);
                BindVisibility(fm, lbl, visibilityGate, r);
                r.Warnings.Add($"PlaceLabel '{spec.Param}': created placeholder TextNote (NewLabel API not available — bind manually in Family Editor)");
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
                // TODO-VERIFY-API: see PlaceLabel — no public NewLabel API.
                // Both legs of the pair fall back to placeholder TextNotes.
                var origin = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var typeId = ResolveTextNoteTypeId(famDoc, null);
                if (typeId == ElementId.InvalidElementId)
                { r.Warnings.Add($"PlaceLabelPair: no text type for placeholder"); return; }

                // Label A — visible when BIM = 1
                if (map.TryGetValue(spec.ParamBim, out var fpA))
                {
                    var lblA = TextNote.Create(famDoc, view.Id, origin,
                        $"<{spec.ParamBim}>", typeId);
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
                    var lblB = TextNote.Create(famDoc, view.Id, origin,
                        $"<{spec.ParamNonBim}>", typeId);
                    map.TryGetValue("__NOT_BIM__", out var notBim);
                    BindVisibility(fm, lblB, notBim, r);
                }
                r.Warnings.Add($"PlaceLabelPair: created placeholder TextNotes (NewLabel API not available — bind manually in Family Editor)");
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

        private static void ApplyLineStyle(Document doc, DetailCurve dc,
            string styleName, TitleBlockBuildResult r)
        {
            if (string.IsNullOrEmpty(styleName) || dc == null) return;
            try
            {
                var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (lines == null) return;
                foreach (Category sub in lines.SubCategories)
                {
                    if (string.Equals(sub.Name, styleName, StringComparison.OrdinalIgnoreCase))
                    {
                        var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                        if (gs != null) dc.LineStyle = gs;
                        return;
                    }
                }
                r.Warnings.Add($"line style '{styleName}' not found");
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

    }
}
