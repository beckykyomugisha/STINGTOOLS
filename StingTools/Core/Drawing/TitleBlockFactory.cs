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
        public int    LabelPairsPlaced { get; set; }
        public int    StaticTextPlaced { get; set; }
        public int    FilledRegionsPlaced { get; set; }
        public int    ReflowGroupsBuilt { get; set; }
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

                    // 4i. Slots — viewport zones with optional reference
                    // planes + a slot-number marker at the top-left
                    // corner. These don't appear at sheet placement time
                    // (reference planes are always invisible on sheets);
                    // they exist so the operator can dimension viewports
                    // off them and so the Drawing Type / Sheet Manager
                    // system can read slot bounds back from the family.
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
                    // Past attempts at `if(BIM, full, collapsed)` formulas
                    // were rejected by Revit's family-formula parser across
                    // every length-literal form tried (mm suffix, feet
                    // suffix, bare numbers, IF/if). For Phase 170 we keep
                    // the param as a plain Length with a fixed default
                    // (the full strip height); Phase 171 will revisit when
                    // we have hands-on Revit access to discover what
                    // formula form the parser actually accepts.
                    //
                    // Important: instance-level (isInstance: true) so any
                    // future formula here can reference the instance-level
                    // STING_BIM_MODE_BOOL — type-level params can't
                    // reference instance-level params.
                    var heightParamName = string.IsNullOrEmpty(grp.HeightParam)
                        ? $"H_{grp.Id}_MM" : grp.HeightParam;
                    var fullFt = MmToFt(grp.FullHeightMm).ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
                    var hp = AddInternalParameter(fm, heightParamName,
                        "Length", "Constraints", isInstance: true,
                        defaultValue: fullFt, formula: null, r);
                    if (hp != null) map[heightParamName] = hp;

                    // 2. Sibling YesNo for child Visible binding. The
                    // simplest possible formula — direct assignment from
                    // BIM_MODE — is the safest form Revit's parser will
                    // accept across versions. The defence-in-depth `AND
                    // (height > 0)` clause is dropped: BIM_MODE alone is
                    // the gate.
                    //
                    // Instance-level so the formula can reference the
                    // instance-level STING_BIM_MODE_BOOL — this was the
                    // root cause of the 3 row*_VIS "Formula setting
                    // failed" warnings on the previous run (type-level
                    // VIS trying to read instance-level BIM_MODE).
                    var visName = $"{grp.Id}_VIS";
                    var vp = AddInternalParameter(fm, visName, "YesNo",
                        "Constraints", isInstance: true,
                        defaultValue: null,
                        formula: spec.BimModeParameter,
                        r);
                    if (vp != null)
                    {
                        map[visName] = vp;
                        visByGroup[grp.Id] = vp;
                    }
                    else
                    {
                        // Last-resort fallback: bind reflow-group children
                        // directly to BIM_MODE_BOOL so they still
                        // toggle correctly even if the sibling-VIS-formula
                        // path fails. Without this, BuildReflowGroups
                        // returning empty visByGroup makes the loop in
                        // Build() skip every reflow-group child entirely.
                        if (map.TryGetValue(spec.BimModeParameter, out var bimFp))
                            visByGroup[grp.Id] = bimFp;
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
                var origin = new XYZ(MmToFt(spec.Anchor[0]), MmToFt(spec.Anchor[1]), 0);
                var hAlign = ParseHAlign(spec.HAlign);
                var sizeFt = MmToFt(spec.Size);
                IList<FamilyParameter> labelParams = new List<FamilyParameter> { fp };
                IList<string> prefixSuffix = new List<string> { spec.Prefix ?? "", spec.Suffix ?? "" };
                var lbl = InvokeNewLabel(famDoc.FamilyCreate, view, origin, hAlign, spec.VAlign,
                    labelParams, prefixSuffix, sizeFt, r);
                if (lbl != null)
                {
                    BindVisibility(fm, lbl, visibilityGate, r);
                    r.LabelsPlaced++;
                }
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
                var sizeFt = MmToFt(spec.Size);
                IList<string> prefixSuffix = new List<string> { "", "" };

                // Label A — visible when BIM = 1
                if (map.TryGetValue(spec.ParamBim, out var fpA))
                {
                    var lblA = InvokeNewLabel(famDoc.FamilyCreate, view, origin, hAlign,
                        spec.VAlign, new List<FamilyParameter> { fpA }, prefixSuffix, sizeFt, r);
                    if (lblA != null)
                    {
                        map.TryGetValue(bimParamName, out var bimFp);
                        BindVisibility(fm, lblA, bimFp, r);
                    }
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
                    var lblB = InvokeNewLabel(famDoc.FamilyCreate, view, origin, hAlign,
                        spec.VAlign, new List<FamilyParameter> { fpB }, prefixSuffix, sizeFt, r);
                    if (lblB != null)
                    {
                        map.TryGetValue("__NOT_BIM__", out var notBim);
                        BindVisibility(fm, lblB, notBim, r);
                    }
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
