// StingTools — FamilySymbolAuthor.cs (Phase 175+)
//
// View-type-aware symbol authoring engine for Revit model families.
//
// Phase 175 capabilities (original):
//   1. SubcategoryScaffold    — 4 STING sub-categories on the family category.
//   2. SymbolicGeometryWirer  — plan/elevation bounding-box symbolic curves.
//   3. NestedAnnotationEmbed  — electrical/lighting 2D plan symbols.
//   4. ConnectorParametrizer  — MEP fitting connector → STING size params.
//
// Phase 175+ additions:
//   5. FamilyElementVisibility — 3D geometry IsShownInCoarse=false/Medium=true/Fine=true
//      so families present symbolic curves at coarse/medium and real geometry at fine.
//   6. Symbolic curve view-type isolation — plan curves restricted to plan/RCP via
//      FamilyElementVisibilityType.CurvesInPlanViews; elevation curves to CurvesInFrontBack;
//      side elevation curves to CurvesInLeftRight.
//   7. Side elevation symbol  — YZ-plane bounding box, wired to STING_LOD_MEDIUM_VISIBLE.
//   8. JSON-driven symbol geometry — IEC 60617 / ANSI IEEE 315 normalised shapes
//      loaded from Data/STING_SYMBOL_SHAPES.json; falls back to built-in shapes.
//   9. Missing categories     — OST_Sprinklers, OST_NurseCallDevices, OST_DataDevices,
//      OST_TelephoneDevices, OST_SpecialityEquipment now handled.
//  10. Bounding-box param link — creates STING_PLAN_HALF_W_FT / STING_PLAN_HALF_D_FT
//      and links them to Width/Depth family params via SetFormula.
//  11. Object Styles           — line weight and colour assigned per STING subcategory.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Tags;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Symbol standard enum
    // ─────────────────────────────────────────────────────────────────────────

    internal enum SymbolStandard
    {
        /// <summary>Auto-detect: use JSON shapes if available, otherwise built-in shapes.</summary>
        AutoDetect,
        /// <summary>IEC 60617 graphical symbols for diagrams.</summary>
        IEC,
        /// <summary>ANSI/IEEE 315 graphic symbols for electrical and electronics diagrams.</summary>
        ANSI,
        /// <summary>BS 1553 / BS 8888 British Standard symbols (largely mirrors IEC).</summary>
        BS,
        /// <summary>NFPA 170 Standard for Fire Safety and Emergency Symbols.</summary>
        NFPA,
        /// <summary>CIBSE Guide symbols for UK building services engineering.</summary>
        CIBSE,
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Standard switching parameter bundle (returned by InjectStandardSwitchingParams)
    // ─────────────────────────────────────────────────────────────────────────

    internal sealed class StandardSwitchingParams
    {
        public FamilyParameter StdParam   { get; set; }   // STING_SYMBOL_STD (Integer)
        public FamilyParameter IecBool    { get; set; }   // STING_SHOW_IEC_BOOL
        public FamilyParameter AnsiBool   { get; set; }   // STING_SHOW_ANSI_BOOL
        public FamilyParameter BsBool     { get; set; }   // STING_SHOW_BS_BOOL
        public FamilyParameter NfpaBool   { get; set; }   // STING_SHOW_NFPA_BOOL
        public FamilyParameter CibseBool  { get; set; }   // STING_SHOW_CIBSE_BOOL

        public FamilyParameter GetBool(SymbolStandard standard)
        {
            switch (standard)
            {
                case SymbolStandard.IEC:   return IecBool;
                case SymbolStandard.ANSI:  return AnsiBool;
                case SymbolStandard.BS:    return BsBool;
                case SymbolStandard.NFPA:  return NfpaBool;
                case SymbolStandard.CIBSE: return CibseBool;
                default:                  return IecBool;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Options / Result DTOs
    // ─────────────────────────────────────────────────────────────────────────

    internal sealed class FamilySymbolAuthorOptions
    {
        // ── Existing options ──────────────────────────────────────────────────
        public bool CreateSubcategories       { get; set; } = true;
        public bool CreatePlanSymbol          { get; set; } = true;
        public bool CreateElevationSymbol     { get; set; } = true;
        public bool CreateClearanceOutline    { get; set; } = false;
        public bool EmbedAnnotationPlanSymbol { get; set; } = true;
        public bool ParametrizeConnectors     { get; set; } = true;

        public double PlanHalfWidthFt  { get; set; } = 0.492;  // ~150 mm
        public double PlanHalfDepthFt  { get; set; } = 0.492;
        public double ElevHeightFt     { get; set; } = 0.984;  // ~300 mm
        public string AnnotationSymbolDir { get; set; } = null;

        // ── Phase 175+ options ────────────────────────────────────────────────

        /// <summary>Set FamilyElementVisibility on 3D GenericForm elements
        /// (IsShownInCoarse=false, Medium=true, Fine=true).</summary>
        public bool SetElementVisibility      { get; set; } = true;

        /// <summary>Set FamilyElementVisibilityType on symbolic curves so plan curves
        /// are isolated to plan/RCP views and elevation curves to elevation views.</summary>
        public bool SetCurveViewTypeVisibility { get; set; } = true;

        /// <summary>Create a side-elevation bounding box (YZ plane) in addition to the
        /// front-elevation one, wired to STING_LOD_MEDIUM_VISIBLE.</summary>
        public bool CreateSideElevSymbol      { get; set; } = true;

        /// <summary>Symbol geometry standard. AutoDetect tries JSON file first, then built-in.</summary>
        public SymbolStandard SymbolStandard  { get; set; } = SymbolStandard.AutoDetect;

        /// <summary>Apply line weight and colour to STING subcategories via Object Styles.</summary>
        public bool ApplyLineStyles           { get; set; } = true;

        /// <summary>Create STING_PLAN_HALF_W_FT / _D_FT params and link to Width/Depth
        /// family params via SetFormula when a matching param is found.</summary>
        public bool LinkBoundingBoxToParams   { get; set; } = true;
    }

    internal sealed class FamilySymbolAuthorResult
    {
        public int  SubcategoriesCreated          { get; set; }
        public int  SubcategoriesExisting         { get; set; }
        public int  PlanCurvesCreated             { get; set; }
        public int  ElevCurvesCreated             { get; set; }
        public int  SideElevCurvesCreated         { get; set; }
        public int  ClearanceCurvesCreated        { get; set; }
        public int  CurvesWiredToLodParam         { get; set; }
        public bool AnnotationSymbolFileEmbedded  { get; set; }
        public int  AnnotationSymbolCurvesCreated { get; set; }
        public int  ConnectorsFound               { get; set; }
        public int  ConnectorParamsCreated        { get; set; }
        public int  ConnectorParamsAssociated     { get; set; }
        public int  ElementVisibilitySet          { get; set; }
        public int  StandardParamsCreated         { get; set; }  // STING_SYMBOL_STD + STING_SHOW_*_BOOL
        public List<string> Warnings              { get; } = new List<string>();

        public override string ToString()
        {
            return $"subcats:{SubcategoriesCreated}+{SubcategoriesExisting} " +
                   $"plan:{PlanCurvesCreated} elev:{ElevCurvesCreated} " +
                   $"sideElev:{SideElevCurvesCreated} clr:{ClearanceCurvesCreated} " +
                   $"wired:{CurvesWiredToLodParam} visSet:{ElementVisibilitySet} " +
                   $"stdParams:{StandardParamsCreated} " +
                   $"annot(file:{AnnotationSymbolFileEmbedded} curves:{AnnotationSymbolCurvesCreated}) " +
                   $"conn:{ConnectorsFound}→{ConnectorParamsCreated}params/{ConnectorParamsAssociated}assoc";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main Engine
    // ─────────────────────────────────────────────────────────────────────────

    internal static class FamilySymbolAuthor
    {
        public const string ScPlanSymbol  = "STING Plan Symbol";
        public const string ScElevSymbol  = "STING Elevation Symbol";
        public const string ScClearance   = "STING Clearance";
        public const string ScHidden      = "STING Hidden";

        // ── Entry point ───────────────────────────────────────────────────────

        public static FamilySymbolAuthorResult AuthorSymbols(
            Document famDoc,
            FamilySymbolAuthorOptions opts = null)
        {
            var result = new FamilySymbolAuthorResult();
            opts = opts ?? new FamilySymbolAuthorOptions();

            if (famDoc == null || !famDoc.IsFamilyDocument)
            {
                result.Warnings.Add("AuthorSymbols: not a family document — skipped.");
                return result;
            }

            var (bic, catName, _) = FamilyParamEngine.DetectFamilyCategory(famDoc);
            FamilyManager fm = famDoc.FamilyManager;

            // ── Step 1: Subcategory scaffold ─────────────────────────────────
            if (opts.CreateSubcategories)
                CreateSubcategories(famDoc, result);

            Category scPlan = FindSubcategory(famDoc, ScPlanSymbol);
            Category scElev = FindSubcategory(famDoc, ScElevSymbol);
            Category scClr  = FindSubcategory(famDoc, ScClearance);

            FamilyParameter lodCoarse = FindFamilyParam(fm, "STING_LOD_COARSE_VISIBLE");
            FamilyParameter lodMedium = FindFamilyParam(fm, "STING_LOD_MEDIUM_VISIBLE");
            FamilyParameter lodFine   = FindFamilyParam(fm, "STING_LOD_FINE_VISIBLE");

            // ── Step 2: Plan symbol ──────────────────────────────────────────
            if (opts.CreatePlanSymbol && !HasSymbolicCurvesInSubcat(famDoc, scPlan))
            {
                int n = CreatePlanRectangle(famDoc, scPlan, lodCoarse,
                    opts.PlanHalfWidthFt, opts.PlanHalfDepthFt,
                    opts.SetCurveViewTypeVisibility, result);
                result.PlanCurvesCreated = n;
                if (lodCoarse != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 3: Front elevation symbol ───────────────────────────────
            if (opts.CreateElevationSymbol && !HasSymbolicCurvesInSubcat(famDoc, scElev))
            {
                int n = CreateElevationRectangle(famDoc, scElev, lodMedium,
                    opts.PlanHalfWidthFt, opts.ElevHeightFt,
                    opts.SetCurveViewTypeVisibility, result);
                result.ElevCurvesCreated = n;
                if (lodMedium != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 3b: Side elevation symbol ───────────────────────────────
            if (opts.CreateSideElevSymbol && opts.CreateElevationSymbol)
            {
                int n = CreateSideElevationRectangle(famDoc, scElev, lodMedium,
                    opts.PlanHalfDepthFt, opts.ElevHeightFt,
                    opts.SetCurveViewTypeVisibility, result);
                result.SideElevCurvesCreated = n;
                if (lodMedium != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 4: Clearance outline ────────────────────────────────────
            if (opts.CreateClearanceOutline && !HasSymbolicCurvesInSubcat(famDoc, scClr))
            {
                double clearFt = GetFamilyParamFt(fm, "STING_CLEARANCE_MM", 0.984);
                int n = CreatePlanRectangle(famDoc, scClr, lodFine,
                    opts.PlanHalfWidthFt + clearFt,
                    opts.PlanHalfDepthFt + clearFt,
                    opts.SetCurveViewTypeVisibility, result);
                result.ClearanceCurvesCreated = n;
                if (lodFine != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 5: Annotation plan symbol (electrical/lighting/plumbing) ─
            if (opts.EmbedAnnotationPlanSymbol && IsAnnotationCategory(bic))
            {
                // Inject STING_SYMBOL_STD + derived STING_SHOW_*_BOOL params so the
                // family can host all standards simultaneously and switch at instance level.
                var switchParams = InjectStandardSwitchingParams(famDoc, fm, lodCoarse, result);

                if (switchParams != null)
                {
                    // Multi-standard path: embed all available standard variants.
                    CreateAllStandardSymbolSets(famDoc, bic, scPlan,
                        switchParams, opts.PlanHalfWidthFt, opts.PlanHalfDepthFt,
                        opts.SetCurveViewTypeVisibility, result);

                    // Step 5b: Per-standard elevation symbols. Only authored when
                    // the JSON carries {STANDARD}_elev arrays for this category;
                    // for categories without elevation data the generic bounding
                    // box from Step 3 remains the only elevation representation.
                    if (opts.CreateElevationSymbol && scElev != null)
                        CreateAllStandardElevationSets(famDoc, bic, scElev,
                            switchParams, opts.PlanHalfWidthFt, opts.ElevHeightFt,
                            opts.SetCurveViewTypeVisibility, result);
                }
                else
                {
                    // Fallback: single standard (legacy annotation family or built-in shapes).
                    EmbedAnnotationPlanSymbol(famDoc, bic, scPlan, lodCoarse,
                        opts.AnnotationSymbolDir ?? GetDefaultAnnotationDir(),
                        opts.PlanHalfWidthFt, opts.PlanHalfDepthFt,
                        opts.SetCurveViewTypeVisibility, opts.SymbolStandard, result);
                }
            }

            // ── Step 6: Connector parametrization (MEP fittings) ────────────
            if (opts.ParametrizeConnectors && IsMepFittingCategory(bic))
                ParametrizeConnectors(famDoc, bic, fm, result);

            // ── Step 7: 3D element LOD visibility ────────────────────────────
            if (opts.SetElementVisibility)
                AutoSetLodVisibility(famDoc, result);

            // ── Step 8: Line styles per subcategory ──────────────────────────
            if (opts.ApplyLineStyles)
                ApplySubcategoryLineStyles(famDoc, result);

            // ── Step 9: Link bounding box to family dimension params ─────────
            if (opts.LinkBoundingBoxToParams)
                TryLinkBoundingBoxToFamilyDimensions(famDoc, fm, result);

            StingLog.Info($"FamilySymbolAuthor: {result}");
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  1. Sub-category scaffold
        // ─────────────────────────────────────────────────────────────────────

        private static void CreateSubcategories(Document famDoc, FamilySymbolAuthorResult result)
        {
            try
            {
                Category ownerCat = famDoc.OwnerFamily?.FamilyCategory;
                if (ownerCat == null) { result.Warnings.Add("CreateSubcategories: OwnerFamily.FamilyCategory is null"); return; }

                foreach (string name in new[] { ScPlanSymbol, ScElevSymbol, ScClearance, ScHidden })
                {
                    try
                    {
                        if (SubcategoryExists(ownerCat, name)) { result.SubcategoriesExisting++; continue; }
                        famDoc.Settings.Categories.NewSubcategory(ownerCat, name);
                        result.SubcategoriesCreated++;
                    }
                    catch (Exception ex)
                    {
                        result.SubcategoriesExisting++;
                        result.Warnings.Add($"Subcategory '{name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Error("FamilySymbolAuthor.CreateSubcategories", ex); }
        }

        private static bool SubcategoryExists(Category ownerCat, string name)
        {
            if (ownerCat.SubCategories == null) return false;
            foreach (Category sub in ownerCat.SubCategories)
                if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Category FindSubcategory(Document famDoc, string name)
        {
            try
            {
                Category ownerCat = famDoc.OwnerFamily?.FamilyCategory;
                if (ownerCat?.SubCategories == null) return null;
                foreach (Category sub in ownerCat.SubCategories)
                    if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase)) return sub;
            }
            catch (Exception ex) { StingLog.Warn($"FindSubcategory '{name}': {ex.Message}"); }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  2. Plan symbol — bounding box in XY plane
        // ─────────────────────────────────────────────────────────────────────

        private static int CreatePlanRectangle(
            Document famDoc, Category subcat, FamilyParameter lodParam,
            double halfW, double halfD, bool setViewTypeVis,
            FamilySymbolAuthorResult result)
        {
            try
            {
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisZ, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreatePlanRectangle: could not create sketch plane"); return 0; }

                // CurvesInPlanViews → only visible in plan/RCP views
                FamilyElementVisibilityType? vtVis = setViewTypeVis
                    ? FamilyElementVisibilityType.CurvesInPlanViews
                    : (FamilyElementVisibilityType?)null;

                return CreateRectangleCurves(famDoc, sp,
                    new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                    new XYZ(halfW,   halfD, 0), new XYZ(-halfW, halfD, 0),
                    subcat, lodParam, vtVis, result);
            }
            catch (Exception ex) { StingLog.Error("FamilySymbolAuthor.CreatePlanRectangle", ex); return 0; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  3. Front elevation symbol — bounding box in XZ plane
        // ─────────────────────────────────────────────────────────────────────

        private static int CreateElevationRectangle(
            Document famDoc, Category subcat, FamilyParameter lodParam,
            double halfW, double heightFt, bool setViewTypeVis,
            FamilySymbolAuthorResult result)
        {
            try
            {
                // XZ plane (front face), normal = +Y
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisY, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreateElevationRectangle: could not create sketch plane"); return 0; }

                // CurvesInFrontBack → only visible in front/back elevation views
                FamilyElementVisibilityType? vtVis = setViewTypeVis
                    ? FamilyElementVisibilityType.CurvesInFrontBack
                    : (FamilyElementVisibilityType?)null;

                return CreateRectangleCurves(famDoc, sp,
                    new XYZ(-halfW, 0, 0),       new XYZ(halfW, 0, 0),
                    new XYZ(halfW,  0, heightFt), new XYZ(-halfW, 0, heightFt),
                    subcat, lodParam, vtVis, result);
            }
            catch (Exception ex) { StingLog.Error("FamilySymbolAuthor.CreateElevationRectangle", ex); return 0; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  3b. Side elevation symbol — bounding box in YZ plane
        // ─────────────────────────────────────────────────────────────────────

        private static int CreateSideElevationRectangle(
            Document famDoc, Category subcat, FamilyParameter lodParam,
            double halfD, double heightFt, bool setViewTypeVis,
            FamilySymbolAuthorResult result)
        {
            try
            {
                // YZ plane (right-side face), normal = +X
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisX, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreateSideElevationRectangle: could not create sketch plane"); return 0; }

                // CurvesInLeftRight → only visible in left/right elevation views
                FamilyElementVisibilityType? vtVis = setViewTypeVis
                    ? FamilyElementVisibilityType.CurvesInLeftRight
                    : (FamilyElementVisibilityType?)null;

                // In YZ plane: Y axis = horizontal, Z axis = vertical; X always 0
                return CreateRectangleCurves(famDoc, sp,
                    new XYZ(0, -halfD, 0),        new XYZ(0, halfD, 0),
                    new XYZ(0,  halfD, heightFt),  new XYZ(0, -halfD, heightFt),
                    subcat, lodParam, vtVis, result);
            }
            catch (Exception ex) { StingLog.Error("FamilySymbolAuthor.CreateSideElevationRectangle", ex); return 0; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  5. Standard switching params + multi-standard symbol sets
        // ─────────────────────────────────────────────────────────────────────

        private static readonly (SymbolStandard Std, string BoolName, string StdKey, int Code)[]
            _standardDefs =
        {
            (SymbolStandard.IEC,   ParamRegistry.SHOW_IEC_BOOL,   "IEC",   ParamRegistry.STD_CODE_IEC),
            (SymbolStandard.ANSI,  ParamRegistry.SHOW_ANSI_BOOL,  "ANSI",  ParamRegistry.STD_CODE_ANSI),
            (SymbolStandard.BS,    ParamRegistry.SHOW_BS_BOOL,    "BS",    ParamRegistry.STD_CODE_BS),
            (SymbolStandard.NFPA,  ParamRegistry.SHOW_NFPA_BOOL,  "NFPA",  ParamRegistry.STD_CODE_NFPA),
            (SymbolStandard.CIBSE, ParamRegistry.SHOW_CIBSE_BOOL, "CIBSE", ParamRegistry.STD_CODE_CIBSE),
        };

        /// <summary>
        /// Creates STING_SYMBOL_STD (Integer) and five derived Yes/No formula params
        /// (STING_SHOW_IEC_BOOL … STING_SHOW_CIBSE_BOOL) inside the family.
        /// Returns null if the family manager cannot accept parameters (non-family doc).
        /// </summary>
        private static StandardSwitchingParams InjectStandardSwitchingParams(
            Document famDoc, FamilyManager fm, FamilyParameter lodCoarseParam,
            FamilySymbolAuthorResult result)
        {
            try
            {
                var existing = fm.GetParameters()
                    .ToDictionary(p => p.Definition.Name, StringComparer.OrdinalIgnoreCase);

                // ── STING_SYMBOL_STD — Integer instance param so per-instance overrides work ──
                // Instance param is required: SetElementSymbolStandardCommand writes it via
                // FamilyInstance.LookupParameter, which cannot reach type params.
                FamilyParameter stdParam = existing.ContainsKey(ParamRegistry.SYMBOL_STD_PARAM)
                    ? existing[ParamRegistry.SYMBOL_STD_PARAM]
                    : null;
                if (stdParam == null)
                {
                    try
                    {
                        stdParam = fm.AddParameter(
                            ParamRegistry.SYMBOL_STD_PARAM,
                            GroupTypeId.General,
                            SpecTypeId.Int.Integer,
                            true);   // instance param — enables per-instance standard override
                        // Seed IEC on every existing family type so the parameter has a defined value.
                        foreach (FamilyType ft in fm.Types)
                        {
                            fm.CurrentType = ft;
                            if (!stdParam.IsReadOnly)
                                fm.Set(stdParam, ParamRegistry.STD_CODE_IEC);
                        }
                        result.StandardParamsCreated++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"InjectStd — create STING_SYMBOL_STD: {ex.Message}");
                        return null;
                    }
                }

                // ── Derived Yes/No params, one per standard ────────────────────────────────
                var sp = new StandardSwitchingParams { StdParam = stdParam };

                bool hasLod = lodCoarseParam != null;
                foreach (var (std, boolName, _, code) in _standardDefs)
                {
                    FamilyParameter boolParam = existing.ContainsKey(boolName)
                        ? existing[boolName]
                        : null;

                    if (boolParam == null)
                    {
                        try
                        {
                            boolParam = fm.AddParameter(
                                boolName,
                                GroupTypeId.General,
                                SpecTypeId.Boolean.YesNo,
                                false);
                            result.StandardParamsCreated++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"InjectStd — create {boolName}: {ex.Message}");
                            continue;
                        }
                    }

                    // Wire formula: if LOD coarse param exists combine both conditions,
                    // otherwise gate on standard only (simpler but LOD-independent).
                    string formula = hasLod
                        ? $"if(STING_LOD_COARSE_VISIBLE, {ParamRegistry.SYMBOL_STD_PARAM} = {code}, false)"
                        : $"{ParamRegistry.SYMBOL_STD_PARAM} = {code}";

                    try { fm.SetFormula(boolParam, formula); }
                    catch
                    {
                        // If the compound formula fails (LOD param not yet resolved),
                        // fall back to the simpler form.
                        try { fm.SetFormula(boolParam, $"{ParamRegistry.SYMBOL_STD_PARAM} = {code}"); }
                        catch (Exception ex2)
                        {
                            result.Warnings.Add($"InjectStd — formula {boolName}: {ex2.Message}");
                        }
                    }

                    switch (std)
                    {
                        case SymbolStandard.IEC:   sp.IecBool   = boolParam; break;
                        case SymbolStandard.ANSI:  sp.AnsiBool  = boolParam; break;
                        case SymbolStandard.BS:    sp.BsBool    = boolParam; break;
                        case SymbolStandard.NFPA:  sp.NfpaBool  = boolParam; break;
                        case SymbolStandard.CIBSE: sp.CibseBool = boolParam; break;
                    }
                }

                return sp;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"InjectStandardSwitchingParams: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates one set of symbolic curves per available standard (IEC / ANSI / BS / NFPA / CIBSE).
        /// Each set is wired to its corresponding STING_SHOW_*_BOOL visibility parameter so exactly
        /// one set is visible at any given time.  Falls back to built-in shapes for standards not
        /// present in the JSON.
        /// </summary>
        private static void CreateAllStandardSymbolSets(
            Document famDoc, BuiltInCategory bic,
            Category planSubcat, StandardSwitchingParams sp,
            double halfW, double halfD, bool setViewTypeVis,
            FamilySymbolAuthorResult result)
        {
            if (HasSymbolicCurvesInSubcat(famDoc, planSubcat)) return;

            LoadSymbolShapesJson();

            SketchPlane sketchPlane = GetOrCreateSketchPlane(famDoc, XYZ.BasisZ, XYZ.Zero);
            if (sketchPlane == null)
            {
                result.Warnings.Add("CreateAllStandardSymbolSets: could not get sketch plane");
                return;
            }

            FamilyElementVisibilityType? vtVis = setViewTypeVis
                ? FamilyElementVisibilityType.CurvesInPlanViews
                : (FamilyElementVisibilityType?)null;

            string catKey = bic.ToString();

            foreach (var (std, _, stdKey, _) in _standardDefs)
            {
                FamilyParameter visParam = sp.GetBool(std);
                if (visParam == null) continue;

                // Try JSON geometry for this standard
                bool created = TryCreateStandardCurvesFromJson(famDoc, catKey, stdKey,
                    sketchPlane, planSubcat, visParam, halfW, halfD, vtVis, result);

                if (!created)
                {
                    // JSON doesn't have this standard/category combo — try the IEC JSON fallback,
                    // then the built-in schematic shapes.  Only do this for IEC to avoid duplicate
                    // curves; other standards that have no unique definition will not be authored
                    // (their bool stays false so nothing is shown for that standard).
                    if (std == SymbolStandard.IEC)
                    {
                        created = TryCreateStandardCurvesFromJson(famDoc, catKey, "IEC",
                            sketchPlane, planSubcat, visParam, halfW, halfD, vtVis, result);

                        if (!created)
                            CreateSchematicPlanSymbolWithVisParam(famDoc, bic, sketchPlane,
                                planSubcat, visParam, halfW, halfD, vtVis, result);
                    }
                }
            }
        }

        /// <summary>
        /// Renders one standard's geometry segment list onto symbolic curves wired to <paramref name="visParam"/>.
        /// Returns true when at least one curve was placed.
        /// </summary>
        private static bool TryCreateStandardCurvesFromJson(
            Document famDoc, string catKey, string stdKey,
            SketchPlane sp, Category planSubcat, FamilyParameter visParam,
            double halfW, double halfD,
            FamilyElementVisibilityType? vtVis,
            FamilySymbolAuthorResult result)
        {
            if (_symbolShapesCache == null) return false;

            JToken catNode  = _symbolShapesCache["categories"]?[catKey];
            if (catNode == null) return false;

            // Try the requested standard key; if missing fall back to "IEC"
            JToken stdNode = catNode[stdKey] ?? (stdKey == "IEC" ? null : catNode["IEC"]);
            if (stdNode == null) return false;

            int count = 0;
            foreach (JObject seg in stdNode)
            {
                string type = seg["type"]?.Value<string>() ?? "";
                switch (type)
                {
                    case "circle":
                    {
                        double r = (seg["r"]?.Value<double>() ?? 1.0) * halfW;
                        count += CreateCircle(famDoc, sp, r, planSubcat, visParam, vtVis, result);
                        break;
                    }
                    case "line":
                    {
                        double x1 = (seg["x1"]?.Value<double>() ?? 0) * halfW;
                        double y1 = (seg["y1"]?.Value<double>() ?? 0) * halfD;
                        double x2 = (seg["x2"]?.Value<double>() ?? 0) * halfW;
                        double y2 = (seg["y2"]?.Value<double>() ?? 0) * halfD;
                        count += CreateLine(famDoc, sp,
                            new XYZ(x1, y1, 0), new XYZ(x2, y2, 0),
                            planSubcat, visParam, vtVis, result);
                        break;
                    }
                    case "arc":
                    {
                        double r    = (seg["r"]?.Value<double>() ?? 1.0) * halfW;
                        double cx   = (seg["cx"]?.Value<double>() ?? 0) * halfW;
                        double cy   = (seg["cy"]?.Value<double>() ?? 0) * halfD;
                        double a1   =  seg["a1"]?.Value<double>() ?? 0;
                        double a2   =  seg["a2"]?.Value<double>() ?? Math.PI;
                        double aMid = (a1 + a2) / 2;
                        XYZ ctr   = new XYZ(cx, cy, 0);
                        XYZ start = ctr + new XYZ(r * Math.Cos(a1),   r * Math.Sin(a1),   0);
                        XYZ mid   = ctr + new XYZ(r * Math.Cos(aMid), r * Math.Sin(aMid), 0);
                        XYZ end   = ctr + new XYZ(r * Math.Cos(a2),   r * Math.Sin(a2),   0);
                        try
                        {
                            Arc arc = Arc.Create(start, end, mid);
                            count += PlaceSymbolicCurve(famDoc, sp, arc, planSubcat, visParam, vtVis, result);
                        }
                        catch (Exception ex) { result.Warnings.Add($"JSON arc [{stdKey}]: {ex.Message}"); }
                        break;
                    }
                    case "rect":
                    {
                        double rx = (seg["x"]?.Value<double>() ?? -1.0) * halfW;
                        double ry = (seg["y"]?.Value<double>() ?? -1.0) * halfD;
                        double rw = (seg["w"]?.Value<double>() ??  2.0) * halfW;
                        double rh = (seg["h"]?.Value<double>() ??  2.0) * halfD;
                        count += CreateRectangleCurves(famDoc, sp,
                            new XYZ(rx,      ry,      0),
                            new XYZ(rx + rw, ry,      0),
                            new XYZ(rx + rw, ry + rh, 0),
                            new XYZ(rx,      ry + rh, 0),
                            planSubcat, visParam, vtVis, result);
                        break;
                    }
                }
            }

            if (count > 0)
            {
                result.AnnotationSymbolCurvesCreated += count;
                return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  5b. Per-standard elevation symbol sets (JSON-driven)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Authors per-standard elevation symbol curves (gated on STING_SHOW_*_BOOL) for
        /// categories that have <c>{STANDARD}_elev</c> arrays in STING_SYMBOL_SHAPES.json.
        /// Coordinates: <c>x</c> = normalised horizontal (×halfW → XYZ.X),
        ///              <c>y</c> = normalised vertical (×halfW, origin = symbol centre = heightFt/2 → XYZ.Z).
        /// If no elevation array exists for a given standard the standard's curve set is silently
        /// omitted — the generic bounding box (Step 3) remains the fallback.
        /// </summary>
        private static void CreateAllStandardElevationSets(
            Document famDoc, BuiltInCategory bic,
            Category elevSubcat, StandardSwitchingParams sp,
            double halfW, double heightFt, bool setViewTypeVis,
            FamilySymbolAuthorResult result)
        {
            if (HasSymbolicCurvesInSubcat(famDoc, elevSubcat)) return;

            LoadSymbolShapesJson();

            // Front elevation: XZ plane, normal = +Y
            SketchPlane spFront = GetOrCreateSketchPlane(famDoc, XYZ.BasisY, XYZ.Zero);
            if (spFront == null)
            {
                result.Warnings.Add("CreateAllStandardElevationSets: could not get front-elevation sketch plane");
                return;
            }

            FamilyElementVisibilityType? vtVis = setViewTypeVis
                ? FamilyElementVisibilityType.CurvesInFrontBack
                : (FamilyElementVisibilityType?)null;

            string catKey  = bic.ToString();
            double centerZ = heightFt / 2.0;

            foreach (var (std, _, stdKey, _) in _standardDefs)
            {
                FamilyParameter visParam = sp.GetBool(std);
                if (visParam == null) continue;
                TryCreateStandardElevationCurvesFromJson(
                    famDoc, catKey, stdKey,
                    spFront, elevSubcat, visParam,
                    halfW, centerZ, vtVis, result);
            }
        }

        /// <summary>
        /// Reads <c>{stdKey}_elev</c> from the JSON category node and authors
        /// symbolic curves in the XZ elevation plane. Returns true when at least
        /// one curve was placed.
        /// </summary>
        private static bool TryCreateStandardElevationCurvesFromJson(
            Document famDoc, string catKey, string stdKey,
            SketchPlane sp, Category elevSubcat, FamilyParameter visParam,
            double halfW, double centerZ,
            FamilyElementVisibilityType? vtVis,
            FamilySymbolAuthorResult result)
        {
            if (_symbolShapesCache == null) return false;

            JToken catNode = _symbolShapesCache["categories"]?[catKey];
            if (catNode == null) return false;

            JToken stdNode = catNode[stdKey + "_elev"];
            if (stdNode == null) return false;

            int count = 0;
            foreach (JObject seg in stdNode)
            {
                string type = seg["type"]?.Value<string>() ?? "";
                switch (type)
                {
                    case "circle":
                    {
                        double r  = (seg["r"]?.Value<double>()  ?? 1.0) * halfW;
                        double cx = (seg["cx"]?.Value<double>() ?? 0)   * halfW;
                        double cz = (seg["cy"]?.Value<double>() ?? 0)   * halfW + centerZ;
                        count += CreateElevCircle(famDoc, sp, cx, cz, r,
                            elevSubcat, visParam, vtVis, result);
                        break;
                    }
                    case "line":
                    {
                        double x1 = (seg["x1"]?.Value<double>() ?? 0) * halfW;
                        double z1 = (seg["y1"]?.Value<double>() ?? 0) * halfW + centerZ;
                        double x2 = (seg["x2"]?.Value<double>() ?? 0) * halfW;
                        double z2 = (seg["y2"]?.Value<double>() ?? 0) * halfW + centerZ;
                        count += CreateLine(famDoc, sp,
                            new XYZ(x1, 0, z1), new XYZ(x2, 0, z2),
                            elevSubcat, visParam, vtVis, result);
                        break;
                    }
                    case "rect":
                    {
                        double rx = (seg["x"]?.Value<double>() ?? -1.0) * halfW;
                        double rz = (seg["y"]?.Value<double>() ?? -1.0) * halfW + centerZ;
                        double rw = (seg["w"]?.Value<double>() ??  2.0) * halfW;
                        double rh = (seg["h"]?.Value<double>() ??  2.0) * halfW;
                        count += CreateRectangleCurves(famDoc, sp,
                            new XYZ(rx,      0, rz),
                            new XYZ(rx + rw, 0, rz),
                            new XYZ(rx + rw, 0, rz + rh),
                            new XYZ(rx,      0, rz + rh),
                            elevSubcat, visParam, vtVis, result);
                        break;
                    }
                }
            }

            if (count > 0)
            {
                result.AnnotationSymbolCurvesCreated += count;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Creates a circle (4 quarter-arcs) in the XZ elevation plane,
        /// centred at <paramref name="cx"/>, <paramref name="cz"/>.
        /// </summary>
        private static int CreateElevCircle(
            Document famDoc, SketchPlane sp,
            double cx, double cz, double radius,
            Category subcat, FamilyParameter visParam,
            FamilyElementVisibilityType? viewTypeVis,
            FamilySymbolAuthorResult result)
        {
            int count = 0;
            for (int q = 0; q < 4; q++)
            {
                try
                {
                    double a1   = q * Math.PI / 2;
                    double aMid = (q + 0.5) * Math.PI / 2;
                    double a2   = (q + 1) * Math.PI / 2;
                    // XZ plane: X = horizontal, Z = vertical, Y = 0
                    XYZ start = new XYZ(cx + radius * Math.Cos(a1),   0, cz + radius * Math.Sin(a1));
                    XYZ mid   = new XYZ(cx + radius * Math.Cos(aMid), 0, cz + radius * Math.Sin(aMid));
                    XYZ end   = new XYZ(cx + radius * Math.Cos(a2),   0, cz + radius * Math.Sin(a2));
                    if (start.IsAlmostEqualTo(end)) continue;
                    Arc arc = Arc.Create(start, end, mid);
                    count += PlaceSymbolicCurve(famDoc, sp, arc, subcat, visParam, viewTypeVis, result);
                }
                catch (Exception ex) { result.Warnings.Add($"CreateElevCircle arc {q}: {ex.Message}"); }
            }
            return count;
        }

        /// <summary>
        /// Built-in schematic fallback — same geometry as <see cref="CreateSchematicPlanSymbol"/>
        /// but accepts an arbitrary <paramref name="visParam"/> instead of the LOD coarse param.
        /// </summary>
        private static void CreateSchematicPlanSymbolWithVisParam(
            Document famDoc, BuiltInCategory bic, SketchPlane sp,
            Category planSubcat, FamilyParameter visParam,
            double halfW, double halfD,
            FamilyElementVisibilityType? vtVis,
            FamilySymbolAuthorResult result)
        {
            // Delegate to the existing schematic builder but swap the lodParam for visParam.
            // We do this by temporarily creating curves directly with the correct vis param.
            try
            {
                int curves = 0;
                switch (bic)
                {
                    case BuiltInCategory.OST_ElectricalFixtures:
                    case BuiltInCategory.OST_FireAlarmDevices:
                    case BuiltInCategory.OST_SecurityDevices:
                    case BuiltInCategory.OST_CommunicationDevices:
                    case BuiltInCategory.OST_DataDevices:
                    case BuiltInCategory.OST_TelephoneDevices:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, visParam, vtVis, result);
                        curves += CreateLine(famDoc, sp,
                            new XYZ(0, -r, 0), new XYZ(0, -r * 1.4, 0),
                            planSubcat, visParam, vtVis, result);
                        break;
                    }
                    case BuiltInCategory.OST_LightingFixtures:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, visParam, vtVis, result);
                        curves += CreateLine(famDoc, sp,
                            new XYZ(-r * 0.65, 0, 0), new XYZ(r * 0.65, 0, 0),
                            planSubcat, visParam, vtVis, result);
                        curves += CreateLine(famDoc, sp,
                            new XYZ(0, -r * 0.65, 0), new XYZ(0, r * 0.65, 0),
                            planSubcat, visParam, vtVis, result);
                        break;
                    }
                    case BuiltInCategory.OST_NurseCallDevices:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r,       planSubcat, visParam, vtVis, result);
                        curves += CreateCircle(famDoc, sp, r * 0.18, planSubcat, visParam, vtVis, result);
                        break;
                    }
                    case BuiltInCategory.OST_Sprinklers:
                    {
                        double r = Math.Min(halfW, halfD) * 0.8;
                        curves += CreateCircle(famDoc, sp, r, planSubcat, visParam, vtVis, result);
                        foreach (var (dx, dy, ex2, ey2) in new[]
                        {
                            (-halfW, 0.0, -r, 0.0),
                            (halfW, 0.0, r, 0.0),
                            (0.0, -halfD, 0.0, -r),
                            (0.0, halfD, 0.0, r),
                        })
                        {
                            curves += CreateLine(famDoc, sp,
                                new XYZ(dx, dy, 0), new XYZ(ex2, ey2, 0),
                                planSubcat, visParam, vtVis, result);
                        }
                        break;
                    }
                    default:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, visParam, vtVis, result);
                        break;
                    }
                }
                if (curves > 0) result.AnnotationSymbolCurvesCreated += curves;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"CreateSchematicPlanSymbolWithVisParam: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  5b. Annotation plan symbol — single-standard (legacy path)
        // ─────────────────────────────────────────────────────────────────────

        private static void EmbedAnnotationPlanSymbol(
            Document famDoc, BuiltInCategory bic,
            Category planSubcat, FamilyParameter lodParam,
            string annotDir, double halfW, double halfD,
            bool setViewTypeVis, SymbolStandard standard,
            FamilySymbolAuthorResult result)
        {
            if (HasSymbolicCurvesInSubcat(famDoc, planSubcat)) return;

            // Try loading a pre-authored annotation family
            if (!string.IsNullOrEmpty(annotDir) && Directory.Exists(annotDir))
            {
                string file = ResolveAnnotationFile(annotDir, bic);
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    if (TryLoadAnnotationFamily(famDoc, file, result))
                        return;
                }
            }

            // Try JSON-driven shapes when standard is specified or AutoDetect finds the file
            if (standard != SymbolStandard.AutoDetect || JsonSymbolShapesExist())
            {
                if (TryCreateJsonDrivenPlanSymbol(famDoc, bic, planSubcat, lodParam,
                    halfW, halfD, setViewTypeVis, standard, result))
                    return;
            }

            // Fallback: built-in schematic symbolic curves
            CreateSchematicPlanSymbol(famDoc, bic, planSubcat, lodParam,
                halfW, halfD, setViewTypeVis, result);
        }

        private static string ResolveAnnotationFile(string dir, BuiltInCategory bic)
        {
            var map = new Dictionary<BuiltInCategory, string>
            {
                { BuiltInCategory.OST_ElectricalFixtures,   "STING Elec Outlet Plan.rfa"       },
                { BuiltInCategory.OST_LightingFixtures,     "STING Luminaire Plan.rfa"          },
                { BuiltInCategory.OST_ElectricalEquipment,  "STING Panel Plan.rfa"              },
                { BuiltInCategory.OST_PlumbingFixtures,     "STING Plumbing Fixture Plan.rfa"   },
                { BuiltInCategory.OST_MechanicalEquipment,  "STING MEP Equipment Plan.rfa"      },
                { BuiltInCategory.OST_FireAlarmDevices,     "STING Fire Alarm Plan.rfa"         },
                { BuiltInCategory.OST_SecurityDevices,      "STING Security Device Plan.rfa"    },
                { BuiltInCategory.OST_CommunicationDevices, "STING Comms Device Plan.rfa"       },
                { BuiltInCategory.OST_Sprinklers,           "STING Sprinkler Plan.rfa"          },
                { BuiltInCategory.OST_NurseCallDevices,     "STING Nurse Call Plan.rfa"         },
                { BuiltInCategory.OST_DataDevices,          "STING Data Device Plan.rfa"        },
                { BuiltInCategory.OST_TelephoneDevices,     "STING Telephone Plan.rfa"          },
                { BuiltInCategory.OST_SpecialityEquipment,  "STING Speciality Equipment Plan.rfa" },
            };
            if (map.TryGetValue(bic, out string filename))
            {
                string path = Path.Combine(dir, filename);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static bool TryLoadAnnotationFamily(Document famDoc, string annotPath, FamilySymbolAuthorResult result)
        {
            try
            {
                // TODO-VERIFY-API: Document.LoadFamily(string, IFamilyLoadOptions, out Family)
                // works in a family document context in Revit 2025/2026/2027.
                bool loaded = famDoc.LoadFamily(annotPath, new LocalFamilyLoadOptions(), out Family annotFam);
                if (!loaded || annotFam == null)
                {
                    result.Warnings.Add($"TryLoadAnnotationFamily: LoadFamily failed for '{Path.GetFileName(annotPath)}'");
                    return false;
                }

                FamilySymbol sym = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Id == annotFam.Id);

                if (sym == null)
                {
                    result.Warnings.Add($"TryLoadAnnotationFamily: no FamilySymbol in '{Path.GetFileName(annotPath)}'");
                    return false;
                }

                if (!sym.IsActive)
                    try { sym.Activate(); } catch { }

                View planView = GetPlanView(famDoc);
                if (planView == null)
                {
                    result.Warnings.Add("TryLoadAnnotationFamily: no floor-plan view in family document");
                    return false;
                }

                famDoc.FamilyCreate.NewFamilyInstance(XYZ.Zero, sym, planView);
                result.AnnotationSymbolFileEmbedded = true;
                return true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryLoadAnnotationFamily '{Path.GetFileName(annotPath)}': {ex.Message}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  5a. JSON-driven symbol geometry (IEC 60617 / ANSI IEEE 315)
        // ─────────────────────────────────────────────────────────────────────

        private static JObject _symbolShapesCache = null;
        private static bool    _symbolShapesCacheTried = false;
        private static readonly object _cacheSync = new object();

        private static bool JsonSymbolShapesExist()
        {
            LoadSymbolShapesJson();
            return _symbolShapesCache != null;
        }

        private static void LoadSymbolShapesJson()
        {
            if (_symbolShapesCacheTried) return;
            lock (_cacheSync)
            {
                if (_symbolShapesCacheTried) return;
                _symbolShapesCacheTried = true;
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_SYMBOL_SHAPES.json");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    var parsed = JObject.Parse(File.ReadAllText(path));
                    // Version guard — warn if the JSON is from an unexpected schema revision.
                    string ver = parsed["version"]?.Value<string>() ?? "unknown";
                    if (ver != "1.1")
                        StingLog.Warn($"STING_SYMBOL_SHAPES.json version '{ver}' differs from expected '1.1'.");
                    _symbolShapesCache = parsed;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"FamilySymbolAuthor: could not load STING_SYMBOL_SHAPES.json: {ex.Message}");
                }
            }
        }

        private static bool TryCreateJsonDrivenPlanSymbol(
            Document famDoc, BuiltInCategory bic,
            Category planSubcat, FamilyParameter lodParam,
            double halfW, double halfD, bool setViewTypeVis,
            SymbolStandard standard, FamilySymbolAuthorResult result)
        {
            try
            {
                if (_symbolShapesCache == null) return false;

                string catKey = bic.ToString(); // e.g. "OST_ElectricalFixtures"
                string stdKey = standard switch
                {
                    SymbolStandard.ANSI  => "ANSI",
                    SymbolStandard.BS    => "BS",
                    SymbolStandard.NFPA  => "NFPA",
                    SymbolStandard.CIBSE => "CIBSE",
                    _                   => "IEC",
                };

                var catNode = _symbolShapesCache["categories"]?[catKey];
                if (catNode == null) return false;

                var stdNode = catNode[stdKey] ?? catNode["IEC"]; // fallback to IEC
                if (stdNode == null) return false;

                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisZ, XYZ.Zero);
                if (sp == null) return false;

                FamilyElementVisibilityType? vtVis = setViewTypeVis
                    ? FamilyElementVisibilityType.CurvesInPlanViews
                    : (FamilyElementVisibilityType?)null;

                int count = 0;
                foreach (JObject seg in stdNode)
                {
                    string type = seg["type"]?.Value<string>() ?? "";
                    switch (type)
                    {
                        case "circle":
                        {
                            double r = (seg["r"]?.Value<double>() ?? 1.0) * halfW;
                            count += CreateCircle(famDoc, sp, r, planSubcat, lodParam, vtVis, result);
                            break;
                        }
                        case "line":
                        {
                            double x1 = (seg["x1"]?.Value<double>() ?? 0) * halfW;
                            double y1 = (seg["y1"]?.Value<double>() ?? 0) * halfD;
                            double x2 = (seg["x2"]?.Value<double>() ?? 0) * halfW;
                            double y2 = (seg["y2"]?.Value<double>() ?? 0) * halfD;
                            count += CreateLine(famDoc, sp,
                                new XYZ(x1, y1, 0), new XYZ(x2, y2, 0),
                                planSubcat, lodParam, vtVis, result);
                            break;
                        }
                        case "arc":
                        {
                            double r    = (seg["r"]?.Value<double>()     ?? 1.0) * halfW;
                            double cx   = (seg["cx"]?.Value<double>()    ?? 0)   * halfW;
                            double cy   = (seg["cy"]?.Value<double>()    ?? 0)   * halfD;
                            double a1   =  seg["a1"]?.Value<double>()    ?? 0;
                            double a2   =  seg["a2"]?.Value<double>()    ?? Math.PI;
                            double aMid = (a1 + a2) / 2;
                            XYZ ctr = new XYZ(cx, cy, 0);
                            XYZ start = ctr + new XYZ(r * Math.Cos(a1),   r * Math.Sin(a1),   0);
                            XYZ mid   = ctr + new XYZ(r * Math.Cos(aMid), r * Math.Sin(aMid), 0);
                            XYZ end   = ctr + new XYZ(r * Math.Cos(a2),   r * Math.Sin(a2),   0);
                            try
                            {
                                Arc arc = Arc.Create(start, end, mid);
                                count += PlaceSymbolicCurve(famDoc, sp, arc, planSubcat, lodParam, vtVis, result);
                            }
                            catch (Exception ex) { result.Warnings.Add($"JSON arc: {ex.Message}"); }
                            break;
                        }
                        case "rect":
                        {
                            double rx = (seg["x"]?.Value<double>()  ?? -1.0) * halfW;
                            double ry = (seg["y"]?.Value<double>()  ?? -1.0) * halfD;
                            double rw = (seg["w"]?.Value<double>()  ??  2.0) * halfW;
                            double rh = (seg["h"]?.Value<double>()  ??  2.0) * halfD;
                            count += CreateRectangleCurves(famDoc, sp,
                                new XYZ(rx,      ry,      0),
                                new XYZ(rx + rw, ry,      0),
                                new XYZ(rx + rw, ry + rh, 0),
                                new XYZ(rx,      ry + rh, 0),
                                planSubcat, lodParam, vtVis, result);
                            break;
                        }
                    }
                }

                if (count > 0)
                {
                    result.AnnotationSymbolCurvesCreated += count;
                    if (lodParam != null) result.CurvesWiredToLodParam += count;
                    return true;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryCreateJsonDrivenPlanSymbol: {ex.Message}");
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  5b. Built-in schematic plan symbols (fallback)
        // ─────────────────────────────────────────────────────────────────────

        private static void CreateSchematicPlanSymbol(
            Document famDoc, BuiltInCategory bic,
            Category planSubcat, FamilyParameter lodParam,
            double halfW, double halfD, bool setViewTypeVis,
            FamilySymbolAuthorResult result)
        {
            try
            {
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisZ, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreateSchematicPlanSymbol: no sketch plane"); return; }

                FamilyElementVisibilityType? vtVis = setViewTypeVis
                    ? FamilyElementVisibilityType.CurvesInPlanViews
                    : (FamilyElementVisibilityType?)null;

                int curves = 0;

                switch (bic)
                {
                    // ── Electrical outlets / fire / security / comms — circle ─
                    case BuiltInCategory.OST_ElectricalFixtures:
                    case BuiltInCategory.OST_FireAlarmDevices:
                    case BuiltInCategory.OST_SecurityDevices:
                    case BuiltInCategory.OST_CommunicationDevices:
                    case BuiltInCategory.OST_DataDevices:
                    case BuiltInCategory.OST_TelephoneDevices:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, lodParam, vtVis, result);
                        // Tick line at bottom (IEC-style outlet indicator)
                        curves += CreateLine(famDoc, sp,
                            new XYZ(0, -r, 0), new XYZ(0, -r * 1.4, 0),
                            planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Luminaires — circle + cross (IEC 60617) ───────────────
                    case BuiltInCategory.OST_LightingFixtures:
                    {
                        double r   = Math.Min(halfW, halfD);
                        double arm = r * 0.65;
                        curves += CreateCircle(famDoc, sp, r, planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(-arm, 0, 0), new XYZ(arm, 0, 0), planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(0, -arm, 0), new XYZ(0, arm, 0), planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Nurse call — circle with central dot ──────────────────
                    case BuiltInCategory.OST_NurseCallDevices:
                    {
                        double r     = Math.Min(halfW, halfD);
                        double dotR  = r * 0.18;
                        curves += CreateCircle(famDoc, sp, r,    planSubcat, lodParam, vtVis, result);
                        curves += CreateCircle(famDoc, sp, dotR, planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Sprinkler — circle + perpendicular arms (IEC/BSRIA) ───
                    case BuiltInCategory.OST_Sprinklers:
                    {
                        double r   = Math.Min(halfW, halfD) * 0.8;
                        double arm = halfW;
                        curves += CreateCircle(famDoc, sp, r, planSubcat, lodParam, vtVis, result);
                        // Four arms radiating out (standard sprinkler head plan)
                        curves += CreateLine(famDoc, sp, new XYZ(-arm, 0, 0), new XYZ(-r, 0, 0), planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(r, 0, 0),    new XYZ(arm, 0, 0),  planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(0, -arm, 0), new XYZ(0, -r, 0),  planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(0, r, 0),    new XYZ(0, arm, 0),  planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Electrical panels — rectangle + diagonal ───────────────
                    case BuiltInCategory.OST_ElectricalEquipment:
                    {
                        curves += CreateRectangleCurves(famDoc, sp,
                            new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                            new XYZ(halfW,   halfD, 0), new XYZ(-halfW,  halfD, 0),
                            planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp,
                            new XYZ(-halfW, -halfD, 0), new XYZ(halfW, halfD, 0),
                            planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Plumbing fixtures — outer + inner circle ───────────────
                    case BuiltInCategory.OST_PlumbingFixtures:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r,       planSubcat, lodParam, vtVis, result);
                        curves += CreateCircle(famDoc, sp, r * 0.6, planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Speciality equipment — rectangle with X ───────────────
                    case BuiltInCategory.OST_SpecialityEquipment:
                    {
                        curves += CreateRectangleCurves(famDoc, sp,
                            new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                            new XYZ(halfW,   halfD, 0), new XYZ(-halfW,  halfD, 0),
                            planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(-halfW, -halfD, 0), new XYZ(halfW,  halfD, 0), planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ( halfW, -halfD, 0), new XYZ(-halfW, halfD, 0), planSubcat, lodParam, vtVis, result);
                        break;
                    }

                    // ── Mechanical equipment — rectangle + centre mark ─────────
                    case BuiltInCategory.OST_MechanicalEquipment:
                    default:
                    {
                        curves += CreateRectangleCurves(famDoc, sp,
                            new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                            new XYZ(halfW,   halfD, 0), new XYZ(-halfW,  halfD, 0),
                            planSubcat, lodParam, vtVis, result);
                        double arm = Math.Min(halfW, halfD) * 0.35;
                        curves += CreateLine(famDoc, sp, new XYZ(-arm, 0, 0), new XYZ(arm, 0, 0), planSubcat, lodParam, vtVis, result);
                        curves += CreateLine(famDoc, sp, new XYZ(0, -arm, 0), new XYZ(0, arm, 0), planSubcat, lodParam, vtVis, result);
                        break;
                    }
                }

                result.AnnotationSymbolCurvesCreated += curves;
                if (lodParam != null) result.CurvesWiredToLodParam += curves;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"CreateSchematicPlanSymbol: {ex.Message}");
                StingLog.Error("FamilySymbolAuthor.CreateSchematicPlanSymbol", ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  6. Connector parametrization
        // ─────────────────────────────────────────────────────────────────────

        private static void ParametrizeConnectors(
            Document famDoc, BuiltInCategory bic, FamilyManager fm,
            FamilySymbolAuthorResult result)
        {
            try
            {
                var connectors = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(ConnectorElement))
                    .Cast<ConnectorElement>()
                    .ToList();

                result.ConnectorsFound = connectors.Count;
                if (connectors.Count == 0) return;

                bool isRound = IsRoundConnector(bic);
                var existing = fm.GetParameters()
                    .Select(p => p.Definition.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (int idx = 0; idx < connectors.Count; idx++)
                {
                    ConnectorElement conn = connectors[idx];
                    int n = idx + 1;
                    if (isRound)
                        AssociateRoundConnector(famDoc, fm, conn, n, existing, result);
                    else
                        AssociateProfileConnector(famDoc, fm, conn, n, existing, result);
                }

                if (result.ConnectorParamsAssociated == 0)
                    TryLinkPrimaryStingParam(fm, bic, result);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ParametrizeConnectors: {ex.Message}");
                StingLog.Error("FamilySymbolAuthor.ParametrizeConnectors", ex);
            }
        }

        private static void AssociateRoundConnector(
            Document famDoc, FamilyManager fm, ConnectorElement conn, int n,
            HashSet<string> existing, FamilySymbolAuthorResult result)
        {
            // TODO-VERIFY-API: CONNECTOR_RADIUS available in Revit 2025+; RBS_CONNECTOR_DIAMETER fallback.
            Parameter elemRadiusParam = conn.get_Parameter(BuiltInParameter.CONNECTOR_RADIUS)
                ?? conn.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DIAMETER);
            if (elemRadiusParam == null || elemRadiusParam.IsReadOnly) return;

            string pName = $"STING_CONN_{n}_RADIUS_MM";
            if (!existing.Contains(pName))
            {
                try
                {
                    fm.AddParameter(pName, GroupTypeId.General, SpecTypeId.Length, false);
                    existing.Add(pName);
                    result.ConnectorParamsCreated++;
                    var fp = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == pName);
                    if (fp != null && fm.CurrentType != null)
                        fm.Set(fp, 0.08202); // 25 mm in feet
                }
                catch (Exception ex) { result.Warnings.Add($"Create '{pName}': {ex.Message}"); }
            }

            var famP = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == pName);
            if (famP != null)
            {
                try
                {
                    fm.AssociateElementParameterToFamilyParameter(elemRadiusParam, famP);
                    result.ConnectorParamsAssociated++;
                }
                catch (Exception ex) { result.Warnings.Add($"Associate radius {n}: {ex.Message}"); }
            }
        }

        private static void AssociateProfileConnector(
            Document famDoc, FamilyManager fm, ConnectorElement conn, int n,
            HashSet<string> existing, FamilySymbolAuthorResult result)
        {
            // TODO-VERIFY-API: CONNECTOR_WIDTH_PARAM / CONNECTOR_HEIGHT_PARAM in Revit 2025+
            Parameter widthP  = conn.get_Parameter(BuiltInParameter.CONNECTOR_WIDTH_PARAM);
            Parameter heightP = conn.get_Parameter(BuiltInParameter.CONNECTOR_HEIGHT_PARAM);

            if (widthP != null && !widthP.IsReadOnly)
            {
                string wName = $"STING_CONN_{n}_W_MM";
                if (!existing.Contains(wName))
                {
                    try
                    {
                        fm.AddParameter(wName, GroupTypeId.General, SpecTypeId.Length, false);
                        existing.Add(wName);
                        result.ConnectorParamsCreated++;
                        var wFp = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == wName);
                        if (wFp != null && fm.CurrentType != null)
                            fm.Set(wFp, 1.3123); // 400 mm default
                    }
                    catch (Exception ex) { result.Warnings.Add($"Create '{wName}': {ex.Message}"); }
                }
                var wP = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == wName);
                if (wP != null)
                    try { fm.AssociateElementParameterToFamilyParameter(widthP, wP); result.ConnectorParamsAssociated++; }
                    catch (Exception ex) { result.Warnings.Add($"Associate width {n}: {ex.Message}"); }
            }

            if (heightP != null && !heightP.IsReadOnly)
            {
                string hName = $"STING_CONN_{n}_H_MM";
                if (!existing.Contains(hName))
                {
                    try
                    {
                        fm.AddParameter(hName, GroupTypeId.General, SpecTypeId.Length, false);
                        existing.Add(hName);
                        result.ConnectorParamsCreated++;
                        var hFp = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == hName);
                        if (hFp != null && fm.CurrentType != null)
                            fm.Set(hFp, 0.6562); // 200 mm default
                    }
                    catch (Exception ex) { result.Warnings.Add($"Create '{hName}': {ex.Message}"); }
                }
                var hP = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == hName);
                if (hP != null)
                    try { fm.AssociateElementParameterToFamilyParameter(heightP, hP); result.ConnectorParamsAssociated++; }
                    catch (Exception ex) { result.Warnings.Add($"Associate height {n}: {ex.Message}"); }
            }
        }

        private static void TryLinkPrimaryStingParam(
            FamilyManager fm, BuiltInCategory bic, FamilySymbolAuthorResult result)
        {
            string stingParamName =
                bic == BuiltInCategory.OST_PipeFitting     ? "PLM_PPE_SZ_MM" :
                bic == BuiltInCategory.OST_ConduitFittings ? "ELC_CDT_SZ_MM" :
                null;
            if (string.IsNullOrEmpty(stingParamName)) return;

            FamilyParameter stingP = FindFamilyParam(fm, stingParamName);
            FamilyParameter connR1 = FindFamilyParam(fm, "STING_CONN_1_RADIUS_MM");
            if (stingP == null || connR1 == null || connR1.IsShared) return;
            try
            {
                fm.SetFormula(connR1, $"{stingParamName} / 2");
            }
            catch (Exception ex) { result.Warnings.Add($"TryLinkPrimaryStingParam: {ex.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  7. 3D element LOD visibility (Phase 175+)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets FamilyElementVisibility on all GenericForm (solid extrusion/blend/revolve/sweep)
        /// elements so they are hidden at Coarse LOD but visible at Medium and Fine.
        /// This lets symbolic curves represent the family at Coarse while 3D geometry
        /// takes over at Medium/Fine.
        /// </summary>
        private static void AutoSetLodVisibility(Document famDoc, FamilySymbolAuthorResult result)
        {
            try
            {
                // TODO-VERIFY-API: GenericForm.SetVisibility(FamilyElementVisibility) in Revit 2025+.
                var forms = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(GenericForm))
                    .Cast<GenericForm>()
                    .ToList();

                if (forms.Count == 0) return;

                // Coarse=false so schematic plan/elevation symbols take over at Coarse LOD.
                // Medium=true so elevation symbol + 3D both render at Medium.
                // Fine=true for full 3D detail.
                var vis = new FamilyElementVisibility(FamilyElementVisibilityType.Model);
                vis.IsShownInCoarse = false;
                vis.IsShownInMedium = true;
                vis.IsShownInFine   = true;

                foreach (GenericForm form in forms)
                {
                    try
                    {
                        form.SetVisibility(vis);
                        result.ElementVisibilitySet++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"SetVisibility form {form.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"AutoSetLodVisibility: {ex.Message}");
                StingLog.Error("FamilySymbolAuthor.AutoSetLodVisibility", ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  8. Line styles per subcategory (Phase 175+)
        // ─────────────────────────────────────────────────────────────────────

        private static void ApplySubcategoryLineStyles(Document famDoc, FamilySymbolAuthorResult result)
        {
            try
            {
                // Plan Symbol: weight 2, solid, dark grey
                SetSubcategoryStyle(famDoc, ScPlanSymbol,
                    lineWeight: 2,
                    color: new Color(64, 64, 64),
                    dashPatternName: null,  // solid (default)
                    result);

                // Elevation Symbol: weight 1, solid, dark grey
                SetSubcategoryStyle(famDoc, ScElevSymbol,
                    lineWeight: 1,
                    color: new Color(64, 64, 64),
                    dashPatternName: null,
                    result);

                // Clearance: weight 1, dashed, blue-grey
                SetSubcategoryStyle(famDoc, ScClearance,
                    lineWeight: 1,
                    color: new Color(100, 120, 180),
                    dashPatternName: "Dash",
                    result);

                // Hidden: weight 1, hidden, light grey
                SetSubcategoryStyle(famDoc, ScHidden,
                    lineWeight: 1,
                    color: new Color(160, 160, 160),
                    dashPatternName: "Hidden",
                    result);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ApplySubcategoryLineStyles: {ex.Message}");
            }
        }

        private static void SetSubcategoryStyle(
            Document famDoc, string subcatName,
            int lineWeight, Color color, string dashPatternName,
            FamilySymbolAuthorResult result)
        {
            try
            {
                Category subcat = FindSubcategory(famDoc, subcatName);
                if (subcat == null) return;

                // Line weight
                try { subcat.SetLineWeight(lineWeight, GraphicsStyleType.Projection); }
                catch (Exception ex) { result.Warnings.Add($"LineWeight '{subcatName}': {ex.Message}"); }

                // Line color
                try { subcat.LineColor = color; }
                catch (Exception ex) { result.Warnings.Add($"LineColor '{subcatName}': {ex.Message}"); }

                // Line pattern (if dash pattern requested)
                if (!string.IsNullOrEmpty(dashPatternName))
                {
                    try
                    {
                        ElementId patId = FindLinePatternId(famDoc, dashPatternName);
                        if (patId != null && patId != ElementId.InvalidElementId)
                            subcat.SetLinePatternId(patId, GraphicsStyleType.Projection);
                    }
                    catch (Exception ex) { result.Warnings.Add($"LinePattern '{subcatName}': {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"SetSubcategoryStyle '{subcatName}': {ex.Message}");
            }
        }

        private static ElementId FindLinePatternId(Document famDoc, string patternName)
        {
            // TODO-VERIFY-API: LinePatternElement collector in family doc context in Revit 2025+.
            return new FilteredElementCollector(famDoc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .Where(lp => lp.Name.IndexOf(patternName, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(lp => lp.Id)
                .FirstOrDefault() ?? ElementId.InvalidElementId;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  9. Bounding-box param link (Phase 175+)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates STING_PLAN_HALF_W_FT and STING_PLAN_HALF_D_FT length parameters
        /// and wires them to existing Width/Depth family params via SetFormula.
        /// This seeds the parameters so future constraint wiring (reference planes +
        /// alignments) can drive symbolic curve size parametrically.
        /// </summary>
        private static void TryLinkBoundingBoxToFamilyDimensions(
            Document famDoc, FamilyManager fm, FamilySymbolAuthorResult result)
        {
            try
            {
                // Common names for width/depth dimension parameters in families.
                // "Length"/"Length_MM" are excluded from depth candidates for MEP pipe/duct/conduit
                // families where "Length" means run length (axial), not cross-section depth.
                bool isMepLinear = IsMepLinearCategory(famDoc);
                string[] widthCandidates = { "Width", "W", "Overall Width", "Nominal Width",
                    "STING_WIDTH_MM", "Width_MM", "b", "dim_width" };
                string[] depthCandidates = isMepLinear
                    ? new[] { "Depth", "D", "Overall Depth", "Nominal Depth",
                              "STING_DEPTH_MM", "Depth_MM", "d", "dim_depth" }
                    : new[] { "Depth", "D", "Overall Depth", "Nominal Depth",
                              "STING_DEPTH_MM", "Depth_MM", "d", "dim_depth", "Length", "Length_MM" };

                FamilyParameter widthParam = FindAnyFamilyParam(fm, widthCandidates);
                FamilyParameter depthParam = FindAnyFamilyParam(fm, depthCandidates);

                CreateHalfDimParam(fm, "STING_PLAN_HALF_W_FT", widthParam, result);
                CreateHalfDimParam(fm, "STING_PLAN_HALF_D_FT", depthParam, result);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryLinkBoundingBoxToFamilyDimensions: {ex.Message}");
            }
        }

        private static bool IsMepLinearCategory(Document famDoc)
        {
            try
            {
                var cat = famDoc.OwnerFamily?.FamilyCategory;
                if (cat == null) return false;
                var bic = (BuiltInCategory)cat.Id.IntegerValue;
                return bic == BuiltInCategory.OST_DuctCurves
                    || bic == BuiltInCategory.OST_PipeCurves
                    || bic == BuiltInCategory.OST_Conduit
                    || bic == BuiltInCategory.OST_CableTray
                    || bic == BuiltInCategory.OST_FlexDuctCurves
                    || bic == BuiltInCategory.OST_FlexPipeCurves;
            }
            catch { return false; }
        }

        private static void CreateHalfDimParam(
            FamilyManager fm, string paramName, FamilyParameter sourceParam,
            FamilySymbolAuthorResult result)
        {
            // Skip if already exists
            if (FindFamilyParam(fm, paramName) != null) return;

            try
            {
                fm.AddParameter(paramName, GroupTypeId.General, SpecTypeId.Length, false);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Create '{paramName}': {ex.Message}");
                return;
            }

            var halfP = FindFamilyParam(fm, paramName);
            if (halfP == null) return;

            // Seed with a default value (150 mm = ~0.492 ft)
            try
            {
                if (fm.CurrentType != null)
                    fm.Set(halfP, 0.492);
            }
            catch { }

            // Link via formula when source param found: STING_PLAN_HALF_W_FT = Width / 2
            if (sourceParam != null && !halfP.IsShared)
            {
                try
                {
                    fm.SetFormula(halfP, $"{sourceParam.Definition.Name} / 2");
                    StingLog.Info($"FamilySymbolAuthor: {paramName} = {sourceParam.Definition.Name} / 2");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"SetFormula '{paramName}': {ex.Message}");
                }
            }
        }

        private static FamilyParameter FindAnyFamilyParam(FamilyManager fm, string[] candidates)
        {
            var all = fm.GetParameters();
            foreach (string name in candidates)
            {
                var found = all.FirstOrDefault(p =>
                    string.Equals(p.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Curve helpers
        // ─────────────────────────────────────────────────────────────────────

        private static SketchPlane GetOrCreateSketchPlane(Document famDoc, XYZ normal, XYZ origin)
        {
            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);
                // TODO-VERIFY-API: SketchPlane.Create(Document, Plane) confirmed in Revit 2014+.
                return SketchPlane.Create(famDoc, plane);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetOrCreateSketchPlane: {ex.Message}");
                return null;
            }
        }

        private static int CreateRectangleCurves(
            Document famDoc, SketchPlane sp,
            XYZ p0, XYZ p1, XYZ p2, XYZ p3,
            Category subcat, FamilyParameter visParam,
            FamilyElementVisibilityType? viewTypeVis,
            FamilySymbolAuthorResult result)
        {
            int count = 0;
            var corners = new[] { p0, p1, p2, p3 };
            for (int i = 0; i < 4; i++)
                count += CreateLine(famDoc, sp, corners[i], corners[(i + 1) % 4],
                    subcat, visParam, viewTypeVis, result);
            return count;
        }

        private static int CreateCircle(
            Document famDoc, SketchPlane sp, double radius,
            Category subcat, FamilyParameter visParam,
            FamilyElementVisibilityType? viewTypeVis,
            FamilySymbolAuthorResult result)
        {
            int count = 0;
            for (int q = 0; q < 4; q++)
            {
                try
                {
                    double a1   = q * Math.PI / 2;
                    double aMid = (q + 0.5) * Math.PI / 2;
                    double a2   = (q + 1) * Math.PI / 2;
                    XYZ start = new XYZ(radius * Math.Cos(a1),   radius * Math.Sin(a1),   0);
                    XYZ mid   = new XYZ(radius * Math.Cos(aMid), radius * Math.Sin(aMid), 0);
                    XYZ end   = new XYZ(radius * Math.Cos(a2),   radius * Math.Sin(a2),   0);
                    if (start.IsAlmostEqualTo(end)) continue;
                    // TODO-VERIFY-API: Arc.Create(start, end, pointOnArc) 3-point arc.
                    Arc arc = Arc.Create(start, end, mid);
                    count += PlaceSymbolicCurve(famDoc, sp, arc, subcat, visParam, viewTypeVis, result);
                }
                catch (Exception ex) { result.Warnings.Add($"CreateCircle arc {q}: {ex.Message}"); }
            }
            return count;
        }

        private static int CreateLine(
            Document famDoc, SketchPlane sp, XYZ p1, XYZ p2,
            Category subcat, FamilyParameter visParam,
            FamilyElementVisibilityType? viewTypeVis,
            FamilySymbolAuthorResult result)
        {
            try
            {
                if (p1.IsAlmostEqualTo(p2)) return 0;
                return PlaceSymbolicCurve(famDoc, sp, Line.CreateBound(p1, p2),
                    subcat, visParam, viewTypeVis, result);
            }
            catch (Exception ex) { result.Warnings.Add($"CreateLine: {ex.Message}"); return 0; }
        }

        private static int PlaceSymbolicCurve(
            Document famDoc, SketchPlane sp, Curve geom,
            Category subcat, FamilyParameter visParam,
            FamilyElementVisibilityType? viewTypeVis,
            FamilySymbolAuthorResult result)
        {
            try
            {
                // TODO-VERIFY-API: FamilyCreate.NewSymbolicCurve returns ModelCurve in Revit 2014+.
                ModelCurve mc = famDoc.FamilyCreate.NewSymbolicCurve(geom, sp);
                if (mc == null) return 0;

                if (subcat != null)
                    try { mc.Subcategory = subcat; }
                    catch (Exception ex) { result.Warnings.Add($"Set Subcategory: {ex.Message}"); }

                if (visParam != null)
                    try { mc.VisibilityParam = visParam; }
                    catch (Exception ex) { result.Warnings.Add($"Set VisibilityParam: {ex.Message}"); }

                // Phase 175+: restrict which view type this curve appears in.
                // CurvesInPlanViews  → only plan/RCP cut views
                // CurvesInFrontBack  → only front/back elevation views
                // CurvesInLeftRight  → only left/right elevation views
                if (viewTypeVis.HasValue)
                {
                    try
                    {
                        // TODO-VERIFY-API: ModelCurve.SetVisibility(FamilyElementVisibility) in Revit 2025+.
                        var fev = new FamilyElementVisibility(viewTypeVis.Value);
                        mc.SetVisibility(fev);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"SetVisibility curve: {ex.Message}");
                    }
                }

                return 1;
            }
            catch (Exception ex) { result.Warnings.Add($"PlaceSymbolicCurve: {ex.Message}"); return 0; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Misc helpers
        // ─────────────────────────────────────────────────────────────────────

        private static FamilyParameter FindFamilyParam(FamilyManager fm, string name)
        {
            return fm?.GetParameters()
                .FirstOrDefault(p => string.Equals(p.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static double GetFamilyParamFt(FamilyManager fm, string name, double fallbackFt)
        {
            try
            {
                var fp = FindFamilyParam(fm, name);
                if (fp == null || fm.CurrentType == null) return fallbackFt;
                double v = fm.CurrentType.AsDouble(fp);
                return v > 0 ? v : fallbackFt;
            }
            catch { return fallbackFt; }
        }

        private static bool HasSymbolicCurvesInSubcat(Document famDoc, Category subcat)
        {
            if (subcat == null) return false;
            try
            {
                return new FilteredElementCollector(famDoc)
                    .OfClass(typeof(ModelCurve))
                    .Cast<ModelCurve>()
                    .Any(mc => mc.Subcategory != null && mc.Subcategory.Id == subcat.Id);
            }
            catch { return false; }
        }

        private static View GetPlanView(Document famDoc)
        {
            return new FilteredElementCollector(famDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan);
        }

        private static bool IsAnnotationCategory(BuiltInCategory bic)
        {
            return bic == BuiltInCategory.OST_ElectricalFixtures  ||
                   bic == BuiltInCategory.OST_LightingFixtures     ||
                   bic == BuiltInCategory.OST_ElectricalEquipment  ||
                   bic == BuiltInCategory.OST_PlumbingFixtures      ||
                   bic == BuiltInCategory.OST_MechanicalEquipment  ||
                   bic == BuiltInCategory.OST_FireAlarmDevices      ||
                   bic == BuiltInCategory.OST_SecurityDevices       ||
                   bic == BuiltInCategory.OST_CommunicationDevices  ||
                   bic == BuiltInCategory.OST_Sprinklers            ||
                   bic == BuiltInCategory.OST_NurseCallDevices      ||
                   bic == BuiltInCategory.OST_DataDevices           ||
                   bic == BuiltInCategory.OST_TelephoneDevices      ||
                   bic == BuiltInCategory.OST_SpecialityEquipment;
        }

        private static bool IsMepFittingCategory(BuiltInCategory bic)
        {
            return bic == BuiltInCategory.OST_PipeFitting     ||
                   bic == BuiltInCategory.OST_DuctFitting      ||
                   bic == BuiltInCategory.OST_CableTrayFitting  ||
                   bic == BuiltInCategory.OST_ConduitFittings   ||
                   bic == BuiltInCategory.OST_PipeCurves        ||
                   bic == BuiltInCategory.OST_DuctCurves;
        }

        private static bool IsRoundConnector(BuiltInCategory bic)
        {
            return bic == BuiltInCategory.OST_PipeFitting    ||
                   bic == BuiltInCategory.OST_ConduitFittings  ||
                   bic == BuiltInCategory.OST_PipeCurves;
        }

        private static string GetDefaultAnnotationDir()
        {
            try
            {
                string asmPath = StingToolsApp.AssemblyPath
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                string baseDir = Path.GetDirectoryName(asmPath) ?? "";
                return Path.Combine(baseDir, "Families", "AnnotationSymbols");
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Private IFamilyLoadOptions for annotation family embedding
        // ─────────────────────────────────────────────────────────────────────

        private sealed class LocalFamilyLoadOptions : IFamilyLoadOptions
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
    }
}
