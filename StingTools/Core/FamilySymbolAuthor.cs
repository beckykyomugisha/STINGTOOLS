// StingTools — FamilySymbolAuthor.cs
//
// View-type-aware symbol authoring engine for Revit model families.
// Implements four capabilities not covered by FamilyParamCreatorCommand:
//
//   1. StingSubcategoryScaffold   — creates four STING sub-categories on the
//      family's own category so plan/elevation/clearance/hidden geometry can
//      be independently controlled via project VG overrides.
//
//   2. SymbolicGeometryWirer      — creates 2D plan and elevation bounding-box
//      symbolic curves and wires each set's visibility to the corresponding
//      STING_LOD_*_VISIBLE Yes/No family parameter so the family presents a
//      schematic plan symbol at coarse detail and 3D geometry at fine detail.
//
//   3. NestedAnnotationEmbedder   — for electrical / lighting / plumbing
//      categories: attempts to load a pre-authored annotation-symbol .rfa from
//      Families/AnnotationSymbols/, falling back to creating category-specific
//      schematic plan symbols (circle for outlets/luminaires, rectangle + cross
//      for panels) using symbolic curves.
//
//   4. ConnectorParametrizer      — scans ConnectorElement objects in MEP
//      fitting families (pipe, duct, conduit, cable-tray), creates STING family
//      parameters (STING_CONN_{n}_RADIUS_MM / STING_CONN_{n}_W_MM / H_MM),
//      and calls FamilyManager.AssociateElementParameterToFamilyParameter to
//      drive connector size from the STING parameter.  For pipe and conduit
//      fittings the primary STING size param (PLM_PPE_SZ_MM / ELC_CDT_SZ_MM)
//      is also linked via a half-diameter formula when direct association fails.
//
// All methods are idempotent (skip when geometry or params already present),
// try/catch-wrapped per STING convention, and marked TODO-VERIFY-API where
// the Revit 2025/2026/2027 API surface is uncertain.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Tags;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Options / Result DTOs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Controls which authoring steps <see cref="FamilySymbolAuthor.AuthorSymbols"/> executes.</summary>
    internal sealed class FamilySymbolAuthorOptions
    {
        /// <summary>Create the four STING sub-categories if they are absent.</summary>
        public bool CreateSubcategories       { get; set; } = true;
        /// <summary>Create a plan-view bounding-box symbolic rectangle and wire to STING_LOD_COARSE_VISIBLE.</summary>
        public bool CreatePlanSymbol          { get; set; } = true;
        /// <summary>Create a front-elevation bounding-box symbolic rectangle and wire to STING_LOD_MEDIUM_VISIBLE.</summary>
        public bool CreateElevationSymbol     { get; set; } = true;
        /// <summary>Create a dashed clearance outline in plan, offset by STING_CLEARANCE_MM, wired to STING_LOD_FINE_VISIBLE.</summary>
        public bool CreateClearanceOutline    { get; set; } = false;
        /// <summary>For electrical / lighting / plumbing categories: embed a 2D plan-symbol annotation
        /// family (if found on disk) or generate schematic plan curves.</summary>
        public bool EmbedAnnotationPlanSymbol { get; set; } = true;
        /// <summary>For MEP fitting categories: scan connectors, create STING size params, associate.</summary>
        public bool ParametrizeConnectors     { get; set; } = true;

        /// <summary>Plan symbol half-width in Revit internal feet (default 150 mm ≈ 0.492 ft).</summary>
        public double PlanHalfWidthFt  { get; set; } = 0.492;
        /// <summary>Plan symbol half-depth in Revit internal feet (default 150 mm ≈ 0.492 ft).</summary>
        public double PlanHalfDepthFt  { get; set; } = 0.492;
        /// <summary>Elevation symbol full height in Revit internal feet (default 300 mm ≈ 0.984 ft).</summary>
        public double ElevHeightFt     { get; set; } = 0.984;

        /// <summary>Directory to search for annotation-symbol .rfa files.
        /// Defaults to &lt;plugin&gt;/Families/AnnotationSymbols/.</summary>
        public string AnnotationSymbolDir { get; set; } = null;
    }

    /// <summary>Accumulates the results of a single <see cref="FamilySymbolAuthor.AuthorSymbols"/> run.</summary>
    internal sealed class FamilySymbolAuthorResult
    {
        public int  SubcategoriesCreated          { get; set; }
        public int  SubcategoriesExisting         { get; set; }
        public int  PlanCurvesCreated             { get; set; }
        public int  ElevCurvesCreated             { get; set; }
        public int  ClearanceCurvesCreated        { get; set; }
        public int  CurvesWiredToLodParam         { get; set; }
        public bool AnnotationSymbolFileEmbedded  { get; set; }
        public int  AnnotationSymbolCurvesCreated { get; set; }
        public int  ConnectorsFound               { get; set; }
        public int  ConnectorParamsCreated        { get; set; }
        public int  ConnectorParamsAssociated     { get; set; }
        public List<string> Warnings              { get; } = new List<string>();

        public override string ToString()
        {
            return $"subcats:{SubcategoriesCreated}+{SubcategoriesExisting} " +
                   $"plan:{PlanCurvesCreated} elev:{ElevCurvesCreated} " +
                   $"clr:{ClearanceCurvesCreated} wired:{CurvesWiredToLodParam} " +
                   $"annot(file:{AnnotationSymbolFileEmbedded} curves:{AnnotationSymbolCurvesCreated}) " +
                   $"conn:{ConnectorsFound}→{ConnectorParamsCreated}params/{ConnectorParamsAssociated}assoc";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main Engine
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// View-type-aware symbol authoring engine. Call <see cref="AuthorSymbols"/> inside
    /// an open <see cref="Transaction"/> on a family document.
    /// </summary>
    internal static class FamilySymbolAuthor
    {
        // Sub-category display names — kept as constants so every call-site is consistent.
        public const string ScPlanSymbol  = "STING Plan Symbol";
        public const string ScElevSymbol  = "STING Elevation Symbol";
        public const string ScClearance   = "STING Clearance";
        public const string ScHidden      = "STING Hidden";

        // ── Entry point ───────────────────────────────────────────────────────

        /// <summary>
        /// Execute all requested authoring steps on <paramref name="famDoc"/>.
        /// Must be called inside an open <see cref="Transaction"/>.
        /// </summary>
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

            // Resolve subcategory objects (may be null if creation failed)
            Category scPlan = FindSubcategory(famDoc, ScPlanSymbol);
            Category scElev = FindSubcategory(famDoc, ScElevSymbol);
            Category scClr  = FindSubcategory(famDoc, ScClearance);

            // Resolve LOD visibility family parameters (injected by InjectAutomationPresentationPack)
            FamilyParameter lodCoarse = FindFamilyParam(fm, "STING_LOD_COARSE_VISIBLE");
            FamilyParameter lodMedium = FindFamilyParam(fm, "STING_LOD_MEDIUM_VISIBLE");
            FamilyParameter lodFine   = FindFamilyParam(fm, "STING_LOD_FINE_VISIBLE");

            // ── Step 2: Plan symbol ──────────────────────────────────────────
            if (opts.CreatePlanSymbol && !HasSymbolicCurvesInSubcat(famDoc, scPlan))
            {
                int n = CreatePlanRectangle(famDoc, scPlan, lodCoarse,
                    opts.PlanHalfWidthFt, opts.PlanHalfDepthFt, result);
                result.PlanCurvesCreated = n;
                if (lodCoarse != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 3: Elevation symbol ─────────────────────────────────────
            if (opts.CreateElevationSymbol && !HasSymbolicCurvesInSubcat(famDoc, scElev))
            {
                int n = CreateElevationRectangle(famDoc, scElev, lodMedium,
                    opts.PlanHalfWidthFt, opts.ElevHeightFt, result);
                result.ElevCurvesCreated = n;
                if (lodMedium != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 4: Clearance outline ────────────────────────────────────
            if (opts.CreateClearanceOutline && !HasSymbolicCurvesInSubcat(famDoc, scClr))
            {
                double clearFt = GetFamilyParamFt(fm, "STING_CLEARANCE_MM", 0.984); // default 300 mm
                int n = CreatePlanRectangle(famDoc, scClr, lodFine,
                    opts.PlanHalfWidthFt + clearFt,
                    opts.PlanHalfDepthFt + clearFt,
                    result);
                result.ClearanceCurvesCreated = n;
                if (lodFine != null) result.CurvesWiredToLodParam += n;
            }

            // ── Step 5: Annotation plan symbol (electrical / lighting / plumbing) ──
            if (opts.EmbedAnnotationPlanSymbol && IsAnnotationCategory(bic))
            {
                EmbedAnnotationPlanSymbol(famDoc, bic, scPlan, lodCoarse,
                    opts.AnnotationSymbolDir ?? GetDefaultAnnotationDir(),
                    opts.PlanHalfWidthFt, opts.PlanHalfDepthFt, result);
            }

            // ── Step 6: Connector parametrization (MEP fittings) ────────────
            if (opts.ParametrizeConnectors && IsMepFittingCategory(bic))
                ParametrizeConnectors(famDoc, bic, fm, result);

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
                        // TODO-VERIFY-API: NewSubcategory signature unchanged in Revit 2025+
                        famDoc.Settings.Categories.NewSubcategory(ownerCat, name);
                        result.SubcategoriesCreated++;
                        StingLog.Info($"FamilySymbolAuthor: created subcategory '{name}'");
                    }
                    catch (Exception ex)
                    {
                        // Creation may fail if the name already exists under a different casing,
                        // or if the category doesn't support subcategories.
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
            double halfW, double halfD, FamilySymbolAuthorResult result)
        {
            try
            {
                // Z = 0, normal = +Z  (horizontal / plan sketch plane)
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisZ, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreatePlanRectangle: could not create sketch plane"); return 0; }
                return CreateRectangleCurves(famDoc, sp,
                    new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                    new XYZ(halfW,   halfD, 0), new XYZ(-halfW, halfD, 0),
                    subcat, lodParam, result);
            }
            catch (Exception ex) { StingLog.Error("FamilySymbolAuthor.CreatePlanRectangle", ex); return 0; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  3. Elevation symbol — bounding box in front-elevation (XZ) plane
        // ─────────────────────────────────────────────────────────────────────

        private static int CreateElevationRectangle(
            Document famDoc, Category subcat, FamilyParameter lodParam,
            double halfW, double heightFt, FamilySymbolAuthorResult result)
        {
            try
            {
                // XZ plane (front face), normal = +Y
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisY, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreateElevationRectangle: could not create sketch plane"); return 0; }

                // Rectangle base at Z=0, top at Z=heightFt, width ±halfW in X; Y always 0
                return CreateRectangleCurves(famDoc, sp,
                    new XYZ(-halfW, 0, 0),       new XYZ(halfW, 0, 0),
                    new XYZ(halfW,  0, heightFt), new XYZ(-halfW, 0, heightFt),
                    subcat, lodParam, result);
            }
            catch (Exception ex) { StingLog.Error("FamilySymbolAuthor.CreateElevationRectangle", ex); return 0; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  5. Annotation plan symbol — electrical / lighting / plumbing
        // ─────────────────────────────────────────────────────────────────────

        private static void EmbedAnnotationPlanSymbol(
            Document famDoc, BuiltInCategory bic,
            Category planSubcat, FamilyParameter lodParam,
            string annotDir, double halfW, double halfD,
            FamilySymbolAuthorResult result)
        {
            // Skip if plan-symbol curves already exist (subcategory-based check)
            if (HasSymbolicCurvesInSubcat(famDoc, planSubcat)) return;

            // Try loading a pre-authored annotation family from disk first
            if (!string.IsNullOrEmpty(annotDir) && Directory.Exists(annotDir))
            {
                string file = ResolveAnnotationFile(annotDir, bic);
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    if (TryLoadAnnotationFamily(famDoc, file, result))
                        return;
                }
            }

            // Fallback: generate schematic symbolic curves per category
            CreateSchematicPlanSymbol(famDoc, bic, planSubcat, lodParam, halfW, halfD, result);
        }

        private static string ResolveAnnotationFile(string dir, BuiltInCategory bic)
        {
            var map = new Dictionary<BuiltInCategory, string>
            {
                { BuiltInCategory.OST_ElectricalFixtures,  "STING Elec Outlet Plan.rfa"      },
                { BuiltInCategory.OST_LightingFixtures,    "STING Luminaire Plan.rfa"         },
                { BuiltInCategory.OST_ElectricalEquipment, "STING Panel Plan.rfa"             },
                { BuiltInCategory.OST_PlumbingFixtures,    "STING Plumbing Fixture Plan.rfa"  },
                { BuiltInCategory.OST_MechanicalEquipment, "STING MEP Equipment Plan.rfa"     },
                { BuiltInCategory.OST_FireAlarmDevices,    "STING Fire Alarm Plan.rfa"        },
                { BuiltInCategory.OST_SecurityDevices,     "STING Security Device Plan.rfa"   },
                { BuiltInCategory.OST_CommunicationDevices,"STING Comms Device Plan.rfa"      },
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
                // Load the annotation symbol family into this family document context.
                // TODO-VERIFY-API: Document.LoadFamily(string, IFamilyLoadOptions, out Family)
                // works in a family document context in Revit 2025/2026/2027.
                Family annotFam;
                bool loaded = famDoc.LoadFamily(annotPath, new LocalFamilyLoadOptions(), out annotFam);
                if (!loaded || annotFam == null)
                {
                    result.Warnings.Add($"TryLoadAnnotationFamily: LoadFamily failed for '{Path.GetFileName(annotPath)}'");
                    return false;
                }

                // Find the first symbol belonging to this family
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
                {
                    try { sym.Activate(); } catch { }
                }

                // Place the annotation symbol at the family origin using the plan view.
                // TODO-VERIFY-API: FamilyCreate.NewFamilyInstance with annotation symbol requires
                // a View that accepts annotation placement (floor plan or ceiling plan).
                View planView = GetPlanView(famDoc);
                if (planView == null)
                {
                    result.Warnings.Add("TryLoadAnnotationFamily: no floor-plan view found in family document");
                    return false;
                }

                famDoc.FamilyCreate.NewFamilyInstance(XYZ.Zero, sym, planView);
                result.AnnotationSymbolFileEmbedded = true;
                StingLog.Info($"FamilySymbolAuthor: embedded '{Path.GetFileName(annotPath)}'");
                return true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryLoadAnnotationFamily '{Path.GetFileName(annotPath)}': {ex.Message}");
                StingLog.Warn($"TryLoadAnnotationFamily: {ex.Message}");
                return false;
            }
        }

        private static void CreateSchematicPlanSymbol(
            Document famDoc, BuiltInCategory bic,
            Category planSubcat, FamilyParameter lodParam,
            double halfW, double halfD,
            FamilySymbolAuthorResult result)
        {
            try
            {
                SketchPlane sp = GetOrCreateSketchPlane(famDoc, XYZ.BasisZ, XYZ.Zero);
                if (sp == null) { result.Warnings.Add("CreateSchematicPlanSymbol: no sketch plane"); return; }

                int curves = 0;

                switch (bic)
                {
                    // Electrical outlets, fire alarm, security, comms — circle symbol
                    case BuiltInCategory.OST_ElectricalFixtures:
                    case BuiltInCategory.OST_FireAlarmDevices:
                    case BuiltInCategory.OST_SecurityDevices:
                    case BuiltInCategory.OST_CommunicationDevices:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, lodParam, result);
                        break;
                    }

                    // Luminaires — circle + cross (standard IEC luminaire symbol)
                    case BuiltInCategory.OST_LightingFixtures:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, lodParam, result);
                        double arm = r * 0.65;
                        curves += CreateLine(famDoc, sp, new XYZ(-arm, 0, 0), new XYZ(arm, 0, 0), planSubcat, lodParam, result);
                        curves += CreateLine(famDoc, sp, new XYZ(0, -arm, 0), new XYZ(0, arm, 0), planSubcat, lodParam, result);
                        break;
                    }

                    // Electrical panels / switchboards — rectangle + diagonal (standard schematic)
                    case BuiltInCategory.OST_ElectricalEquipment:
                    {
                        curves += CreateRectangleCurves(famDoc, sp,
                            new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                            new XYZ(halfW,   halfD, 0), new XYZ(-halfW,  halfD, 0),
                            planSubcat, lodParam, result);
                        curves += CreateLine(famDoc, sp, new XYZ(-halfW, -halfD, 0), new XYZ(halfW, halfD, 0), planSubcat, lodParam, result);
                        break;
                    }

                    // Plumbing fixtures — circle + two inner arcs (standard sanitary symbol)
                    case BuiltInCategory.OST_PlumbingFixtures:
                    {
                        double r = Math.Min(halfW, halfD);
                        curves += CreateCircle(famDoc, sp, r, planSubcat, lodParam, result);
                        // Inner circle at 60% radius
                        curves += CreateCircle(famDoc, sp, r * 0.6, planSubcat, lodParam, result);
                        break;
                    }

                    // Mechanical equipment — rectangle with centre-mark cross
                    case BuiltInCategory.OST_MechanicalEquipment:
                    default:
                    {
                        curves += CreateRectangleCurves(famDoc, sp,
                            new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0),
                            new XYZ(halfW,   halfD, 0), new XYZ(-halfW,  halfD, 0),
                            planSubcat, lodParam, result);
                        double arm = Math.Min(halfW, halfD) * 0.35;
                        curves += CreateLine(famDoc, sp, new XYZ(-arm, 0, 0), new XYZ(arm, 0, 0), planSubcat, lodParam, result);
                        curves += CreateLine(famDoc, sp, new XYZ(0, -arm, 0), new XYZ(0, arm, 0), planSubcat, lodParam, result);
                        break;
                    }
                }

                result.AnnotationSymbolCurvesCreated = curves;
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
                // Collect ConnectorElement objects from the family document
                // TODO-VERIFY-API: FilteredElementCollector<ConnectorElement> works identically
                // in family documents in Revit 2025/2026/2027.
                var connectors = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(ConnectorElement))
                    .Cast<ConnectorElement>()
                    .ToList();

                result.ConnectorsFound = connectors.Count;
                if (connectors.Count == 0)
                {
                    StingLog.Info($"FamilySymbolAuthor.ParametrizeConnectors: 0 connectors found ({bic})");
                    return;
                }

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

                // For pipe/conduit: wire STING_CONN_1_RADIUS_MM = PLM_PPE_SZ_MM / 2
                // only when the direct element-parameter association above succeeded for
                // connector 1 (i.e., the family param no longer needs SetFormula).
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
            // TODO-VERIFY-API: BuiltInParameter.CONNECTOR_RADIUS available in Revit 2025+.
            // In some Revit versions RBS_CONNECTOR_DIAMETER is preferred.
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

                    // Seed: 25 mm nominal radius (50 mm nominal pipe), in feet
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
                    StingLog.Info($"FamilySymbolAuthor: connector {n} radius → {pName}");
                }
                catch (Exception ex) { result.Warnings.Add($"Associate radius {n}: {ex.Message}"); }
            }
        }

        private static void AssociateProfileConnector(
            Document famDoc, FamilyManager fm, ConnectorElement conn, int n,
            HashSet<string> existing, FamilySymbolAuthorResult result)
        {
            // TODO-VERIFY-API: CONNECTOR_WIDTH_PARAM / CONNECTOR_HEIGHT_PARAM names in Revit 2025+
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

        /// <summary>
        /// When direct connector association failed (e.g. the family has no connector elements),
        /// fall back to a formula-based link: STING_CONN_1_RADIUS_MM = &lt;stingSize&gt; / 2.
        /// Only applies to pipe (PLM_PPE_SZ_MM) and conduit (ELC_CDT_SZ_MM).
        /// SetFormula on a non-shared family-local param is safe to call after AddParameter.
        /// </summary>
        private static void TryLinkPrimaryStingParam(
            FamilyManager fm, BuiltInCategory bic, FamilySymbolAuthorResult result)
        {
            string stingParamName =
                bic == BuiltInCategory.OST_PipeFitting      ? "PLM_PPE_SZ_MM" :
                bic == BuiltInCategory.OST_ConduitFittings  ? "ELC_CDT_SZ_MM" :
                null;

            if (string.IsNullOrEmpty(stingParamName)) return;

            FamilyParameter stingP  = FindFamilyParam(fm, stingParamName);
            FamilyParameter connR1  = FindFamilyParam(fm, "STING_CONN_1_RADIUS_MM");
            if (stingP == null || connR1 == null) return;
            if (connR1.IsShared) return; // SetFormula not allowed on shared params

            try
            {
                fm.SetFormula(connR1, $"{stingParamName} / 2");
                StingLog.Info($"FamilySymbolAuthor: STING_CONN_1_RADIUS_MM = {stingParamName} / 2");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryLinkPrimaryStingParam: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Curve helpers
        // ─────────────────────────────────────────────────────────────────────

        private static SketchPlane GetOrCreateSketchPlane(Document famDoc, XYZ normal, XYZ origin)
        {
            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);
                // TODO-VERIFY-API: SketchPlane.Create(Document, Plane) available in Revit 2014+;
                // confirmed supported in 2025/2026/2027.
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
            FamilySymbolAuthorResult result)
        {
            int count = 0;
            var corners = new[] { p0, p1, p2, p3 };
            for (int i = 0; i < 4; i++)
                count += CreateLine(famDoc, sp, corners[i], corners[(i + 1) % 4], subcat, visParam, result);
            return count;
        }

        private static int CreateCircle(
            Document famDoc, SketchPlane sp, double radius,
            Category subcat, FamilyParameter visParam,
            FamilySymbolAuthorResult result)
        {
            int count = 0;
            // Four arc quadrants → full circle
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
                    // TODO-VERIFY-API: Arc.Create(start, end, pointOnArc) — 3-point arc.
                    Arc arc = Arc.Create(start, end, mid);
                    count += PlaceSymbolicCurve(famDoc, sp, arc, subcat, visParam, result);
                }
                catch (Exception ex) { result.Warnings.Add($"CreateCircle arc {q}: {ex.Message}"); }
            }
            return count;
        }

        private static int CreateLine(
            Document famDoc, SketchPlane sp, XYZ p1, XYZ p2,
            Category subcat, FamilyParameter visParam,
            FamilySymbolAuthorResult result)
        {
            try
            {
                if (p1.IsAlmostEqualTo(p2)) return 0;
                return PlaceSymbolicCurve(famDoc, sp, Line.CreateBound(p1, p2), subcat, visParam, result);
            }
            catch (Exception ex) { result.Warnings.Add($"CreateLine: {ex.Message}"); return 0; }
        }

        private static int PlaceSymbolicCurve(
            Document famDoc, SketchPlane sp, Curve geom,
            Category subcat, FamilyParameter visParam,
            FamilySymbolAuthorResult result)
        {
            try
            {
                // TODO-VERIFY-API: FamilyCreate.NewSymbolicCurve returns ModelCurve in Revit 2014+.
                // ModelCurve.Subcategory and ModelCurve.VisibilityParam confirmed in Revit 2025 API docs.
                ModelCurve mc = famDoc.FamilyCreate.NewSymbolicCurve(geom, sp);
                if (mc == null) return 0;

                if (subcat != null)
                    try { mc.Subcategory = subcat; }
                    catch (Exception ex) { result.Warnings.Add($"Set Subcategory: {ex.Message}"); }

                if (visParam != null)
                    try { mc.VisibilityParam = visParam; }
                    catch (Exception ex) { result.Warnings.Add($"Set VisibilityParam: {ex.Message}"); }

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
                   bic == BuiltInCategory.OST_CommunicationDevices;
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
            return bic == BuiltInCategory.OST_PipeFitting   ||
                   bic == BuiltInCategory.OST_ConduitFittings ||
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
        //  Private IFamilyLoadOptions used for annotation family embedding
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
