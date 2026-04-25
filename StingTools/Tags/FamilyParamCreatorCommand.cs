using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════════════════
    //  TAG FAMILY PARAMETER CREATOR ENGINE
    //
    //  Injects the STING TAG schema (tokens, ASS_TAG_* containers, visibility /
    //  paragraph gates, 128-entry style matrix, TAG_POS + 16-branch formula,
    //  position variant types) into tag family .rfa files. Scope is tag-only.
    //
    //  For regular Revit families (doors, walls, MEP equipment) the CSV-binding
    //  + formula pipeline lives in Temp › Family Parameter Processor
    //  (FamilyParameterProcessorCommand in Temp/TemplateManagerCommands.cs).
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scope of the pre-injection purge. STING identity is resolved by GUID against
    /// <see cref="ParamRegistry.AllParamGuids"/> — any shared parameter whose GUID is
    /// NOT in the registry is considered non-STING, regardless of prefix.
    /// </summary>
    public enum PurgeMode
    {
        /// <summary>No purge — additive only. Leaves every existing family parameter in place.</summary>
        None = 0,
        /// <summary>Remove shared parameters whose GUID is in the STING registry before re-injecting.
        /// Used for schema-version migrations that need a clean slate. Destroys label bindings
        /// on removed params — only use when also re-binding labels in the same run.</summary>
        StingOnly = 1,
        /// <summary>Remove shared parameters whose GUID is NOT in the STING registry. Cleans third-party
        /// / legacy / stray shared params out of a family before injecting STING's schema, so the
        /// family ends up carrying only STING-managed parameters + Revit built-ins.</summary>
        NonSting = 2,
        /// <summary>Remove every shared parameter in the family, STING or not. Most destructive —
        /// only used for full factory-reset workflows.</summary>
        All = 3,
    }

    /// <summary>
    /// Engine for processing Revit family files: detecting categories, injecting
    /// shared parameters, formulas, tag anchor planes, and position variant types.
    /// </summary>
    internal static class FamilyParamEngine
    {
        /// <summary>
        /// All 16 named position types: Ring 1 (cardinal 1x offset) + Ring 2 (far 1.5x offset).
        /// </summary>
        /// <summary>
        /// STING shared-parameter prefixes. Used by PurgeFirst to identify which family
        /// parameters belong to STING and should be removed before a fresh injection.
        /// </summary>
        private static readonly string[] StingParamPrefixes = {
            "ASS_", "BLE_", "CST_", "ELC_", "ELE_", "FLS_", "HVC_", "ICT_",
            "LTG_", "MAT_", "MEP_", "MNT_", "NCL_", "PER_", "PLM_", "RGL_",
            "SEC_", "SHT_", "SLV_", "STING_", "STR_", "TAG_", "VIEW_", "WARN_"
        };

        /// <summary>
        /// Returns true if the given parameter name starts with any STING prefix.
        /// Case-insensitive. Kept for non-shared / name-only checks — for shared
        /// parameters prefer <see cref="IsStingSharedParam"/> which matches on GUID.
        /// </summary>
        public static bool IsStingPrefix(string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return false;
            foreach (string p in StingParamPrefixes)
            {
                if (paramName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True iff the given family parameter is a shared parameter whose GUID is
        /// registered in <see cref="ParamRegistry.AllParamGuids"/>. This is the
        /// authoritative "is STING" check for purge scoping — a family parameter
        /// that happens to have a STING-looking prefix but a foreign GUID is
        /// treated as non-STING, and vice versa.
        /// </summary>
        public static bool IsStingSharedParam(FamilyParameter fp)
        {
            if (fp == null || !fp.IsShared) return false;
            try
            {
                Guid g = fp.GUID;
                if (g == Guid.Empty) return false;
                // GetParamName returns null when the GUID is not in the registry.
                return ParamRegistry.GetParamName(g) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Decide whether <paramref name="fp"/> should be removed under <paramref name="mode"/>.
        /// Assumes the caller has already filtered to <c>IsShared == true</c>.</summary>
        private static bool ShouldPurge(FamilyParameter fp, PurgeMode mode)
        {
            switch (mode)
            {
                case PurgeMode.None:      return false;
                case PurgeMode.All:       return true;
                case PurgeMode.StingOnly: return IsStingSharedParam(fp);
                case PurgeMode.NonSting:  return !IsStingSharedParam(fp);
                default:                  return false;
            }
        }

        private static readonly (string Name, int Pos)[] AllPositionTypes = {
            // Ring 1 — cardinal 1x offset
            ("1x-N",  1), ("1x-E",  2), ("1x-S",  3),  ("1x-W",  4),
            ("1x-NE", 5), ("1x-SE", 6), ("1x-SW", 7),  ("1x-NW", 8),
            // Ring 2 — far 1.5x offset
            ("1.5x-N", 9), ("1.5x-E",10), ("1.5x-S",11), ("1.5x-W",12),
            ("1.5x-NE",13),("1.5x-SE",14),("1.5x-SW",15),("1.5x-NW",16)
        };

        /// <summary>COBie type map loaded from COBIE_TYPE_MAP.csv (family name → property dict).</summary>
        private static Dictionary<string, Dictionary<string, string>> _cobieTypeMap;
        private static bool _cobieTypeMapLoaded;

        /// <summary>Options for family processing. Defaults are the safe "add only what's
        /// missing" profile — every mutation beyond injecting absent shared parameters is
        /// opt-in. Callers that explicitly want to migrate families (purge, rewrite the
        /// TAG_POS formula, add position types) must flip the relevant flags themselves.</summary>
        public class ProcessOptions
        {
            public bool PlaceAnchor { get; set; } = false;
            /// <summary>When true, inject STING position types (Left/Right/Top/Bottom variants).
            /// Default false — "add only what's missing" mode never alters family types.</summary>
            public bool CreatePositionTypes { get; set; } = false;
            /// <summary>When true, inject the 16-branch Calculated Value formula onto TAG_POS.
            /// Default false — "add only what's missing" mode never rewrites labels / formulas.</summary>
            public bool InjectFormulas { get; set; } = false;
            /// <summary>When true, inject the TAG_POS parameter itself (independent of formulas).
            /// Default false — TAG_POS is already added via ParamNames if requested.</summary>
            public bool InjectTagPos { get; set; } = false;

            /// <summary>When true, inject the automation + presentation parameter pack
            /// (clearance, fire rating, acoustic Rw, cost, CO2, manufacturer, model,
            /// datasheet URL, warranty, LOD visibility switches, workset hint, OmniClass).
            /// All family-local type parameters. See <see cref="InjectAutomationPresentationPack"/>.</summary>
            public bool InjectAutomationPack { get; set; } = false;

            /// <summary>Scope of the purge run before parameter injection. Default <see cref="PurgeMode.None"/>
            /// (pure additive). <see cref="PurgeMode.NonSting"/> strips third-party / legacy shared parameters
            /// whose GUID is not in <see cref="ParamRegistry.AllParamGuids"/> — intended for the
            /// "clean a third-party family before adopting it into STING" workflow.</summary>
            public PurgeMode Purge { get; set; } = PurgeMode.None;

            /// <summary>Deprecated alias — kept so existing callers that set PurgeFirst=true continue
            /// to behave as if they'd requested <see cref="PurgeMode.StingOnly"/>. New code should set
            /// <see cref="Purge"/> directly.</summary>
            public bool PurgeFirst
            {
                get => Purge == PurgeMode.StingOnly || Purge == PurgeMode.All;
                set { if (value && Purge == PurgeMode.None) Purge = PurgeMode.StingOnly; }
            }

            /// <summary>When true, after saving the processed .rfa, load it into the supplied
            /// <see cref="TargetProjectDoc"/>. No-op when <see cref="TargetProjectDoc"/> is null.
            /// Used by batch workflows that want one click from "folder of .rfa" to "loaded in project".</summary>
            public bool LoadAfterSave { get; set; } = false;

            /// <summary>Target project document to <see cref="Document.LoadFamily(string, IFamilyLoadOptions, out Family)"/>
            /// into when <see cref="LoadAfterSave"/> is true. When null and LoadAfterSave is true,
            /// the load step is silently skipped. The project doc must belong to the same application
            /// instance as the one opening the family docs — enforced by the caller.</summary>
            public Document TargetProjectDoc { get; set; } = null;

            /// <summary>Overwrite instance parameter values in the project when reloading.
            /// Default false (preserve whatever the user already set on placed instances).</summary>
            public bool LoadOverwriteParameterValues { get; set; } = false;

            public List<string> ParamNames { get; set; } = new List<string>();
        }

        /// <summary>Result of processing a single family file.</summary>
        public class FamilyResult
        {
            public string SourcePath { get; set; }
            public string OutputPath { get; set; }
            public string Category { get; set; }
            public string DiscCode { get; set; }
            public int ParamsAdded { get; set; }
            public int ParamsSkipped { get; set; }
            public bool TagPosInjected { get; set; }
            public int FormulasInjected { get; set; }
            public bool AnchorPlaced { get; set; }
            public int PositionTypesCreated { get; set; }
            public int TokensSeeded { get; set; }
            public int CobiePropsWritten { get; set; }
            /// <summary>Count of automation/presentation pack params added (0 when InjectAutomationPack=false).</summary>
            public int AutomationPackAdded { get; set; }
            /// <summary>Count of automation/presentation pack params skipped because already present.</summary>
            public int AutomationPackSkipped { get; set; }
            /// <summary>Count of shared parameters removed by the pre-injection purge (0 when Purge=None).</summary>
            public int ParamsPurged { get; set; }
            /// <summary>True when LoadAfterSave succeeded. False when LoadAfterSave=false, TargetProjectDoc=null, or the load call threw.</summary>
            public bool LoadedIntoProject { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Detect the family's BuiltInCategory with secondary fingerprint for GenericModel.
        /// </summary>
        public static (BuiltInCategory cat, string catName, string discCode) DetectFamilyCategory(Document famDoc)
        {
            if (famDoc == null || !famDoc.IsFamilyDocument)
                return (BuiltInCategory.INVALID, "", "A");

            try
            {
                Category famCat = famDoc.OwnerFamily.FamilyCategory;
                if (famCat == null)
                    return (BuiltInCategory.INVALID, "Unknown", "A");

                string catName = famCat.Name ?? "";
                BuiltInCategory bic = (BuiltInCategory)famCat.Id.Value;

                // Secondary fingerprint for GenericModel families
                if (bic == BuiltInCategory.OST_GenericModel)
                {
                    try
                    {
                        var paramNames = famDoc.FamilyManager.GetParameters()
                            .Select(p => p.Definition.Name.ToUpperInvariant())
                            .ToList();

                        if (paramNames.Any(n => n.Contains("HVAC") || n.Contains("AHU") ||
                            n.Contains("FCU") || n.Contains("VAV") || n.Contains("FAN")))
                        {
                            return (BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment", "M");
                        }
                        if (paramNames.Any(n => n.Contains("VOLTAGE") || n.Contains("CIRCUIT") ||
                            n.Contains("PANEL") || n.Contains("BREAKER")))
                        {
                            return (BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures", "E");
                        }
                        if (paramNames.Any(n => n.Contains("FLOW") || n.Contains("PIPE") ||
                            n.Contains("DRAIN") || n.Contains("VALVE")))
                        {
                            return (BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures", "P");
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Detect family category from parameters: {ex.Message}"); }
                }

                // Map to discipline code
                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";
                return (bic, catName, disc);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectFamilyCategory: {ex.Message}");
                return (BuiltInCategory.INVALID, "Unknown", "A");
            }
        }

        /// <summary>
        /// Inject STING shared parameters into a family document.
        /// </summary>
        public static (int added, int skipped) InjectSharedParams(
            Document famDoc, Autodesk.Revit.ApplicationServices.Application app, List<string> paramNames)
        {
            int added = 0, skipped = 0;
            if (famDoc == null || app == null || paramNames == null) return (0, 0);

            try
            {
                string spFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (string.IsNullOrEmpty(spFile))
                {
                    StingLog.Warn("InjectSharedParams: MR_PARAMETERS.txt not found");
                    return (0, 0);
                }

                string origFile = app.SharedParametersFilename;
                app.SharedParametersFilename = spFile;

                try
                {
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    if (defFile == null) return (0, 0);

                    FamilyManager fm = famDoc.FamilyManager;
                    var existingNames = new HashSet<string>(
                        fm.GetParameters().Select(p => p.Definition.Name),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (string paramName in paramNames)
                    {
                        try
                        {
                            if (existingNames.Contains(paramName))
                            {
                                skipped++;
                                continue;
                            }

                            // Find the definition in shared param file
                            ExternalDefinition extDef = null;
                            foreach (DefinitionGroup group in defFile.Groups)
                            {
                                extDef = group.Definitions
                                    .Cast<ExternalDefinition>()
                                    .FirstOrDefault(d => d.Name == paramName);
                                if (extDef != null) break;
                            }

                            if (extDef == null)
                            {
                                skipped++;
                                continue;
                            }

                            bool isInstance = paramName != ParamRegistry.TAG_POS;
                            fm.AddParameter(extDef,
                                GroupTypeId.General,
                                isInstance);
                            added++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"InjectSharedParams '{paramName}': {ex.Message}");
                            skipped++;
                        }
                    }
                }
                finally
                {
                    try { app.SharedParametersFilename = origFile; }
                    catch (Exception ex) { StingLog.Warn($"Restore shared parameters filename: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectSharedParams", ex);
            }

            return (added, skipped);
        }

        /// <summary>
        /// Inject tag position formulas into the family document.
        /// Creates Tag_Offset_Base length param if missing, then sets 16-branch
        /// nested-if formulas on Label_Offset_X and Label_Offset_Y.
        /// </summary>
        public static int InjectTagPosFormulas(Document famDoc)
        {
            int injected = 0;
            if (famDoc == null) return 0;

            try
            {
                FamilyManager fm = famDoc.FamilyManager;
                var tagPosParam = fm.GetParameters()
                    .FirstOrDefault(p => p.Definition.Name == ParamRegistry.TAG_POS);
                if (tagPosParam == null) return 0;

                // Ensure Tag_Offset_Base length param exists (default 10mm = 0.01m internal)
                const string offsetBaseParamName = "Tag_Offset_Base";
                var existingParams = fm.GetParameters();
                var paramNameSet = existingParams
                    .Select(p => p.Definition.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!paramNameSet.Contains(offsetBaseParamName))
                {
                    try
                    {
                        fm.AddParameter(
                            offsetBaseParamName,
                            GroupTypeId.General,
                            SpecTypeId.Length,
                            false); // type param
                        paramNameSet.Add(offsetBaseParamName);
                        StingLog.Info($"InjectTagPosFormulas: created '{offsetBaseParamName}' length param");

                        // Set default value (10mm = ~0.0328 ft)
                        var baseParam = fm.GetParameters()
                            .FirstOrDefault(p => p.Definition.Name == offsetBaseParamName);
                        if (baseParam != null)
                        {
                            fm.Set(baseParam, 0.0328084); // 10mm in feet
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"InjectTagPosFormulas: could not create '{offsetBaseParamName}': {ex.Message}");
                    }
                }

                // 16-branch X formula: E(+d), W(-d), far E(+1.5d), far W(-1.5d), diagonals (±0.7d / ±1.05d)
                string xFormula =
                    "if(STING_TAG_POS = 2, Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 10, 1.5 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 4, -Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 12, -1.5 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 5, Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 6, Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 13, 1.05 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 14, 1.05 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 7, -Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 8, -Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 15, -1.05 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 16, -1.05 * Tag_Offset_Base, " +
                    "0 mm))))))))))))";

                // 16-branch Y formula: N(+d), S(-d), far N(+1.5d), far S(-1.5d), diagonals (±0.7d / ±1.05d)
                string yFormula =
                    "if(STING_TAG_POS = 1, Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 9, 1.5 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 3, -Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 11, -1.5 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 5, 0.7 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 8, 0.7 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 13, 1.05 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 16, 1.05 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 6, -0.7 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 7, -0.7 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 14, -1.05 * Tag_Offset_Base, " +
                    "if(STING_TAG_POS = 15, -1.05 * Tag_Offset_Base, " +
                    "0 mm))))))))))))";

                string[] formulaPairs = {
                    "Label_Offset_X", xFormula,
                    "Label_Offset_Y", yFormula
                };

                for (int i = 0; i < formulaPairs.Length; i += 2)
                {
                    string targetParam = formulaPairs[i];
                    string formula = formulaPairs[i + 1];

                    if (!paramNameSet.Contains(targetParam)) continue;

                    try
                    {
                        var param = fm.GetParameters()
                            .FirstOrDefault(p => p.Definition.Name == targetParam);
                        if (param != null)
                        {
                            fm.SetFormula(param, formula);
                            injected++;
                            StingLog.Info($"InjectTagPosFormulas: set 16-branch formula on '{targetParam}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"InjectTagPosFormulas '{targetParam}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectTagPosFormulas", ex);
            }

            return injected;
        }

        /// <summary>
        /// Create 16 named position types in the family document (Ring 1 + Ring 2).
        /// Each type sets STING_TAG_POS to its position number (1-16).
        /// </summary>
        public static int InjectPositionTypes(Document famDoc)
        {
            int created = 0;
            if (famDoc == null) return 0;

            try
            {
                FamilyManager fm = famDoc.FamilyManager;
                var tagPosParam = fm.GetParameters()
                    .FirstOrDefault(p => p.Definition.Name == ParamRegistry.TAG_POS);
                if (tagPosParam == null) return 0;

                var existingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FamilyType ft in fm.Types)
                {
                    if (ft != null && !string.IsNullOrEmpty(ft.Name))
                        existingTypes.Add(ft.Name);
                }

                foreach (var (name, pos) in AllPositionTypes)
                {
                    string typeName = $"2.5mm - {name}";
                    if (existingTypes.Contains(typeName)) continue;

                    try
                    {
                        FamilyType newType = fm.NewType(typeName);
                        fm.CurrentType = newType;
                        fm.Set(tagPosParam, pos);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"InjectPositionTypes '{typeName}': {ex.Message}");
                    }
                }

                // Default position = 2.5mm - 1x-N (Ring 1, North, 1x offset). This is the
                // type that becomes active when the family loads into a project, so placed
                // tags start at the North cardinal position and the SwitchTagPositionCommand
                // can then move them by swapping to other types (1x-E, 1x-S, 1x-W, etc.).
                try
                {
                    FamilyType defaultType = null;
                    foreach (FamilyType ft in fm.Types)
                    {
                        if (ft != null && string.Equals(ft.Name, DefaultPositionTypeName, StringComparison.OrdinalIgnoreCase))
                        {
                            defaultType = ft;
                            break;
                        }
                    }
                    if (defaultType != null)
                    {
                        fm.CurrentType = defaultType;
                        StingLog.Info($"InjectPositionTypes: default type set to '{DefaultPositionTypeName}'");
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"InjectPositionTypes default-type: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectPositionTypes", ex);
            }

            return created;
        }

        /// <summary>
        /// Default position type name used as the active type when a family loads into a project.
        /// "1x-N" = Ring 1 (1x offset) from the cardinal North direction. SwitchTagPositionCommand
        /// rotates placed tags by swapping to other named types (1x-E, 1x-S, 1x-W, 1x-NE, etc.).
        /// </summary>
        public const string DefaultPositionTypeName = "2.5mm - 1x-N";

        /// <summary>
        /// Automation + Presentation parameter pack. Family-local (non-shared) type
        /// parameters that don't require an entry in MR_PARAMETERS.txt. They complement
        /// the shared STING schema with fields that drive scheduling, costing, carbon
        /// tracking, LOD visibility, and O&amp;M handover without GUID overhead.
        ///
        /// All Type parameters. Idempotent (skip if already present). Failures on a
        /// single parameter do not abort the pack — each Add is wrapped in its own try.
        /// </summary>
        public static (int added, int skipped) InjectAutomationPresentationPack(Document famDoc)
        {
            int added = 0, skipped = 0;
            if (famDoc == null || !famDoc.IsFamilyDocument) return (0, 0);

            try
            {
                FamilyManager fm = famDoc.FamilyManager;
                var existing = fm.GetParameters()
                    .Select(p => p.Definition.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // (name, spec, group) — all Type parameters (isInstance: false).
                // Spec choices kept conservative: Length / Integer / Number / Text / YesNo.
                // Currency, Url, Image intentionally avoided — API availability varies
                // across 2025/2026/2027 and some require shared-param GUIDs to schedule.
                // Using GroupTypeId.General uniformly — matches every other
                // AddParameter call-site in STING and avoids group-name drift
                // between Revit 2025 / 2026 / 2027 API versions.
                var pack = new (string name, ForgeTypeId spec)[]
                {
                    // Automation / design
                    ("STING_CLEARANCE_MM",         SpecTypeId.Length),
                    ("STING_FIRE_RATING_MIN",      SpecTypeId.Int.Integer),
                    ("STING_ACOUSTIC_RW_DB",       SpecTypeId.Int.Integer),
                    // 5D + sustainability (feeds SchedulingCommands + SustainabilityEngine)
                    ("STING_COST_UNIT",            SpecTypeId.Number),
                    ("STING_CO2_KG",               SpecTypeId.Number),
                    // O&M / handover (feeds COBie export)
                    ("STING_MANUFACTURER",         SpecTypeId.String.Text),
                    ("STING_MODEL_NR",             SpecTypeId.String.Text),
                    ("STING_URL_DATASHEET",        SpecTypeId.String.Text),
                    ("STING_WARRANTY_MO",          SpecTypeId.Int.Integer),
                    // LOD visibility switches (drive single-family multi-LOD presentation)
                    ("STING_LOD_COARSE_VISIBLE",   SpecTypeId.Boolean.YesNo),
                    ("STING_LOD_MEDIUM_VISIBLE",   SpecTypeId.Boolean.YesNo),
                    ("STING_LOD_FINE_VISIBLE",     SpecTypeId.Boolean.YesNo),
                    // Coordination hints
                    ("STING_WORKSET_HINT",         SpecTypeId.String.Text),
                    ("STING_OMNICLASS_23",         SpecTypeId.String.Text),

                    // ─── Pack 2 — directional clearance (ClearanceValidator already reads these) ───
                    ("STING_CLEARANCE_FRONT_MM",   SpecTypeId.Length),
                    ("STING_CLEARANCE_BACK_MM",    SpecTypeId.Length),
                    ("STING_CLEARANCE_SIDE_MM",    SpecTypeId.Length),
                    ("STING_CLEARANCE_TOP_MM",     SpecTypeId.Length),
                    // Pack 2 — maintenance / service envelope (MaintenanceClashValidator reads)
                    ("MNT_ENV_W_MM",               SpecTypeId.Length),
                    ("MNT_ENV_D_MM",               SpecTypeId.Length),
                    ("MNT_ENV_H_MM",               SpecTypeId.Length),
                    ("MNT_ACCESS_DIR_TXT",         SpecTypeId.String.Text),
                    // Pack 2 — clash-only envelope (ConnectivityValidator/MaintenanceClashValidator)
                    ("CLASH_ENV_W_MM",             SpecTypeId.Length),
                    ("CLASH_ENV_D_MM",             SpecTypeId.Length),
                    ("CLASH_ENV_H_MM",             SpecTypeId.Length),
                    ("CLASH_PRIORITY_INT",         SpecTypeId.Int.Integer),
                    ("CLASH_SOFT_TOLERANCE_MM",    SpecTypeId.Length),
                    ("EXPANSION_ALLOW_MM",         SpecTypeId.Length),
                    ("CONNECTOR_CLR_MM",           SpecTypeId.Length),
                    ("FIRE_SEP_MM",                SpecTypeId.Length),

                    // ─── Pack 3 — placement / variant (FixturePlacementEngine reads) ───
                    ("STING_FIXTURE_VARIANT_TXT",  SpecTypeId.String.Text),
                    ("STING_ROOM_TYPE_FILTER_TXT", SpecTypeId.String.Text),

                    // ─── §5.1 — remaining placement-intelligence params (PlacementParamReader) ───
                    ("PLACE_HOST_TYPE_TXT",        SpecTypeId.String.Text),
                    ("PLACE_MOUNT_HEIGHT_MM",      SpecTypeId.Length),
                    ("PLACE_SPACING_RULE_TXT",     SpecTypeId.String.Text),
                    ("PLACE_ORIENTATION_RULE_TXT", SpecTypeId.String.Text),
                    ("PLACE_LEVEL_HINT_TXT",       SpecTypeId.String.Text),
                    ("PLACE_GROUP_KEY_TXT",        SpecTypeId.String.Text),
                    ("PLACE_WEIGHT_KG",            SpecTypeId.Number),

                    // ─── §5.3 — routing / MEP hints (RoutingParamReader) ───
                    ("CONN_COUNT_INT",             SpecTypeId.Int.Integer),
                    ("CONN_TYPES_TXT",             SpecTypeId.String.Text),
                    ("PREF_DROP_DIR_TXT",          SpecTypeId.String.Text),
                    ("SLOPE_MIN_PCT",              SpecTypeId.Number),
                    ("SLOPE_MAX_PCT",              SpecTypeId.Number),
                    ("FILL_MAX_PCT",               SpecTypeId.Number),
                    ("TERM_TYPE_TXT",              SpecTypeId.String.Text),
                    ("SEGMENT_LEN_MAX_MM",         SpecTypeId.Length),
                    ("SUPPORT_PITCH_MM",           SpecTypeId.Length),

                    // ─── §5.5 — identity / classification (ClassificationReader) ───
                    ("UNICLASS_PR_TXT",            SpecTypeId.String.Text),
                    ("UNICLASS_SS_TXT",            SpecTypeId.String.Text),
                    ("UNICLASS_EF_TXT",            SpecTypeId.String.Text),
                    ("NBS_CODE_TXT",               SpecTypeId.String.Text),
                    // ASSET_RFI_URL_TXT is Instance-bound per the brief — family-local
                    // type injection suffices for now; Instance binding ships as part
                    // of the §9 MR_PARAMETERS follow-up when GUIDs are assigned.
                    ("ASSET_RFI_URL_TXT",          SpecTypeId.String.Text),

                    // ─── Pack 4 — tag anchor (TagPlacementEngine reads) ───
                    ("STING_TAG_ANCHOR_X_MM",      SpecTypeId.Length),
                    ("STING_TAG_ANCHOR_Y_MM",      SpecTypeId.Length),
                    ("TAG_LEADER_LAND_EDGE_TXT",   SpecTypeId.String.Text),
                    ("TAG_DISPLAY_SCALE_MIN_INT",  SpecTypeId.Int.Integer),
                    ("TAG_DISPLAY_SCALE_MAX_INT",  SpecTypeId.Int.Integer),
                    ("TAG_CLUSTER_KEY_TXT",        SpecTypeId.String.Text),
                    ("TAG_PRIORITY_INT",           SpecTypeId.Int.Integer),
                    ("TAG_FAMILY_HINT_TXT",        SpecTypeId.String.Text),
                };

                foreach (var (name, spec) in pack)
                {
                    if (existing.Contains(name)) { skipped++; continue; }
                    try
                    {
                        fm.AddParameter(name, GroupTypeId.General, spec, false); // type param
                        added++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"InjectAutomationPresentationPack '{name}': {ex.Message}");
                        skipped++;
                    }
                }

                // Seed sensible defaults for the LOD switches — default to all visible
                // so existing behaviour is preserved. Authors can then wire geometry
                // groups to the three booleans and flip them per LOD.
                foreach (string lodParam in new[] { "STING_LOD_COARSE_VISIBLE", "STING_LOD_MEDIUM_VISIBLE", "STING_LOD_FINE_VISIBLE" })
                {
                    try
                    {
                        var fp = fm.GetParameters().FirstOrDefault(p => p.Definition.Name == lodParam);
                        if (fp != null && fm.CurrentType != null) fm.Set(fp, 1);
                    }
                    catch (Exception ex) { StingLog.Warn($"InjectAutomationPresentationPack seed '{lodParam}': {ex.Message}"); }
                }

                // Pack 124 / Gap F — stamp pack version on the family's
                // ProjectInformation. Lets coordinators see which families
                // need re-injection when the next pack ships.
                try
                {
                    StingTools.Core.Storage.StingPackVersionSchema.Write(
                        famDoc,
                        StingTools.Core.Storage.StingPackVersionSchema.CurrentPackVersion,
                        added);
                }
                catch (Exception pvEx) { StingLog.Warn($"PackVersion stamp: {pvEx.Message}"); }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectAutomationPresentationPack", ex);
            }

            return (added, skipped);
        }

        /// <summary>
        /// Seed default DISC, PROD, SYS, FUNC, TAG_POS type parameters into the family.
        /// Returns the number of tokens written.
        /// </summary>
        public static int SeedDefaultTokens(Document famDoc, string catName, string discCode)
        {
            int seeded = 0;
            if (famDoc == null || !famDoc.IsFamilyDocument) return 0;

            try
            {
                FamilyManager fm = famDoc.FamilyManager;

                // DISC
                if (SetTypeParam(fm, ParamRegistry.DISC, discCode)) seeded++;

                // PROD from TagConfig.ProdMap
                string prodCode = "";
                if (!string.IsNullOrEmpty(catName) &&
                    TagConfig.ProdMap != null &&
                    TagConfig.ProdMap.TryGetValue(catName, out string prod))
                {
                    prodCode = prod;
                }
                if (!string.IsNullOrEmpty(prodCode) && SetTypeParam(fm, ParamRegistry.PROD, prodCode))
                    seeded++;

                // SYS from TagConfig.GetSysCode
                string sysCode = TagConfig.GetSysCode(catName ?? "");
                if (!string.IsNullOrEmpty(sysCode) && SetTypeParam(fm, ParamRegistry.SYS, sysCode))
                    seeded++;

                // FUNC from TagConfig.GetFuncCode
                string funcCode = !string.IsNullOrEmpty(sysCode)
                    ? TagConfig.GetFuncCode(sysCode)
                    : "";
                if (!string.IsNullOrEmpty(funcCode) && SetTypeParam(fm, ParamRegistry.FUNC, funcCode))
                    seeded++;

                // TAG_POS from TagPlacementEngine preferred side + 1
                try
                {
                    int preferred = TagPlacementEngine.GetPreferredSide(catName ?? "") + 1;
                    if (SetTypeParam(fm, ParamRegistry.TAG_POS, preferred)) seeded++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SeedDefaultTokens TAG_POS: {ex.Message}");
                }

                StingLog.Info($"SeedDefaultTokens: seeded {seeded} tokens for '{catName}' (DISC={discCode})");
            }
            catch (Exception ex)
            {
                StingLog.Error($"SeedDefaultTokens '{catName}'", ex);
            }

            return seeded;
        }

        /// <summary>
        /// Safely set a type parameter value on the current FamilyType.
        /// Looks up param by name, checks StorageType, and calls fm.Set() accordingly.
        /// Returns true if the value was written successfully.
        /// </summary>
        private static bool SetTypeParam(FamilyManager fm, string paramName, object value)
        {
            if (fm == null || string.IsNullOrEmpty(paramName) || value == null) return false;

            try
            {
                var param = fm.GetParameters()
                    .FirstOrDefault(p => p.Definition.Name == paramName);
                if (param == null) return false;

                switch (param.StorageType)
                {
                    case StorageType.String:
                        fm.Set(param, value.ToString());
                        return true;

                    case StorageType.Integer:
                        if (value is int intVal)
                            fm.Set(param, intVal);
                        else if (int.TryParse(value.ToString(), out int parsed))
                            fm.Set(param, parsed);
                        else
                            return false;
                        return true;

                    case StorageType.Double:
                        if (value is double dblVal)
                            fm.Set(param, dblVal);
                        else if (double.TryParse(value.ToString(), out double dParsed))
                            fm.Set(param, dParsed);
                        else
                            return false;
                        return true;

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetTypeParam '{paramName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load the COBie type map from COBIE_TYPE_MAP.csv if it exists.
        /// CSV format: FamilyName,UniclassCode,SFG20Code,AssetType,WarrantyDurationYears,...
        /// First row is header. Family name is the key (case-insensitive).
        /// </summary>
        private static void EnsureCobieTypeMapLoaded()
        {
            if (_cobieTypeMapLoaded) return;
            _cobieTypeMapLoaded = true;

            try
            {
                string csvPath = StingToolsApp.FindDataFile("COBIE_TYPE_MAP.csv");
                if (string.IsNullOrEmpty(csvPath))
                {
                    StingLog.Info("EnsureCobieTypeMapLoaded: COBIE_TYPE_MAP.csv not found, skipping");
                    return;
                }

                var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                string[] lines = File.ReadAllLines(csvPath);
                if (lines.Length < 2) return;

                string[] headers = StingToolsApp.ParseCsvLine(lines[0]);

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    string[] cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length < 2) continue;

                    string familyName = cols[0]?.Trim();
                    if (string.IsNullOrEmpty(familyName)) continue;

                    var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 1; c < cols.Length && c < headers.Length; c++)
                    {
                        string val = cols[c]?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(val))
                            props[headers[c].Trim()] = val;
                    }

                    if (props.Count > 0)
                        map[familyName] = props;
                }

                _cobieTypeMap = map;
                StingLog.Info($"EnsureCobieTypeMapLoaded: loaded {map.Count} family mappings from COBIE_TYPE_MAP.csv");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"EnsureCobieTypeMapLoaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Seed COBie type properties into the family from COBIE_TYPE_MAP.csv.
        /// Matches family name to CSV key and writes UniclassCode, SFG20Code, AssetType,
        /// WarrantyDurationYears, etc. Only writes if param exists and value is non-empty.
        /// Returns the number of properties written.
        /// </summary>
        public static int SeedCobieTypeProperties(Document famDoc, string familyName)
        {
            int written = 0;
            if (famDoc == null || string.IsNullOrEmpty(familyName)) return 0;

            try
            {
                EnsureCobieTypeMapLoaded();
                if (_cobieTypeMap == null || _cobieTypeMap.Count == 0) return 0;

                // Try exact match, then partial match
                Dictionary<string, string> props = null;
                if (!_cobieTypeMap.TryGetValue(familyName, out props))
                {
                    // Try partial match — find first key contained in family name
                    string upperFam = familyName.ToUpperInvariant();
                    foreach (var kvp in _cobieTypeMap)
                    {
                        if (upperFam.Contains(kvp.Key.ToUpperInvariant()))
                        {
                            props = kvp.Value;
                            break;
                        }
                    }
                }

                if (props == null || props.Count == 0) return 0;

                FamilyManager fm = famDoc.FamilyManager;
                var existingParams = fm.GetParameters()
                    .ToDictionary(p => p.Definition.Name, p => p, StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in props)
                {
                    string paramName = kvp.Key;
                    string value = kvp.Value;

                    if (string.IsNullOrEmpty(value)) continue;
                    if (!existingParams.TryGetValue(paramName, out FamilyParameter param)) continue;

                    try
                    {
                        if (param.StorageType == StorageType.String)
                        {
                            fm.Set(param, value);
                            written++;
                        }
                        else if (param.StorageType == StorageType.Integer && int.TryParse(value, out int intVal))
                        {
                            fm.Set(param, intVal);
                            written++;
                        }
                        else if (param.StorageType == StorageType.Double && double.TryParse(value, out double dblVal))
                        {
                            fm.Set(param, dblVal);
                            written++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SeedCobieTypeProperties '{paramName}': {ex.Message}");
                    }
                }

                if (written > 0)
                    StingLog.Info($"SeedCobieTypeProperties: wrote {written} COBie props for '{familyName}'");
            }
            catch (Exception ex)
            {
                StingLog.Error($"SeedCobieTypeProperties '{familyName}'", ex);
            }

            return written;
        }

        /// <summary>
        /// Process a single family file: detect category, inject params, seed tokens,
        /// seed COBie properties, inject formulas, and create position types.
        /// </summary>
        public static FamilyResult ProcessFamily(
            Autodesk.Revit.ApplicationServices.Application app,
            string rfaPath, string outputPath, ProcessOptions opts)
        {
            var result = new FamilyResult
            {
                SourcePath = rfaPath,
                OutputPath = outputPath
            };

            Document famDoc = null;
            try
            {
                if (!File.Exists(rfaPath))
                {
                    result.ErrorMessage = "File not found";
                    return result;
                }

                famDoc = app.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    result.ErrorMessage = "Not a valid family document";
                    if (famDoc != null) famDoc.Close(false);
                    return result;
                }

                var (cat, catName, discCode) = DetectFamilyCategory(famDoc);
                result.Category = catName;
                result.DiscCode = discCode;

                using (Transaction tx = new Transaction(famDoc, "STING Tag Family Parameter Creator"))
                {
                    tx.Start();

                    // Pre-injection purge. Scope is set by opts.Purge:
                    //   None      → skipped entirely (pure additive run).
                    //   StingOnly → remove shared params whose GUID is in the STING registry.
                    //   NonSting  → remove shared params whose GUID is NOT in the STING registry
                    //               (cleans third-party / legacy strays before adopting a family).
                    //   All       → remove every shared parameter.
                    // Only the Purge property is read here — PurgeFirst is a deprecated shim
                    // that sets Purge=StingOnly when true was passed in.
                    if (opts.Purge != PurgeMode.None)
                    {
                        try
                        {
                            FamilyManager fmPurge = famDoc.FamilyManager;
                            var toRemove = fmPurge.GetParameters()
                                .Where(p => p.IsShared)
                                .Where(p => ShouldPurge(p, opts.Purge))
                                .ToList();
                            foreach (var fp in toRemove)
                            {
                                try { fmPurge.RemoveParameter(fp); }
                                catch (Exception rpEx) { StingLog.Warn($"Purge '{fp.Definition.Name}' ({opts.Purge}): {rpEx.Message}"); }
                            }
                            result.ParamsPurged = toRemove.Count;
                            if (toRemove.Count > 0)
                                StingLog.Info($"Purge {opts.Purge}: removed {toRemove.Count} shared params from '{Path.GetFileName(rfaPath)}'");
                        }
                        catch (Exception purgeEx) { StingLog.Warn($"Purge {opts.Purge}: {purgeEx.Message}"); }
                    }

                    // Inject shared parameters
                    string familyFileName = Path.GetFileNameWithoutExtension(rfaPath);
                    var paramList = opts.ParamNames.Count > 0
                        ? opts.ParamNames
                        : GetParamsForFamily(familyFileName)
                          ?? GetDefaultParamsForCategory(catName, discCode);
                    var (added, skipped) = InjectSharedParams(famDoc, app, paramList);
                    result.ParamsAdded = added;
                    result.ParamsSkipped = skipped;

                    // Seed default tokens (DISC, PROD, SYS, FUNC, TAG_POS)
                    result.TokensSeeded = SeedDefaultTokens(famDoc, catName, discCode);

                    // Seed COBie type properties from COBIE_TYPE_MAP.csv
                    string familyName = Path.GetFileNameWithoutExtension(rfaPath);
                    result.CobiePropsWritten = SeedCobieTypeProperties(famDoc, familyName);

                    // Inject TAG_POS
                    if (opts.InjectTagPos)
                    {
                        try
                        {
                            var tagPosList = new List<string> { ParamRegistry.TAG_POS };
                            var (tpAdded, _) = InjectSharedParams(famDoc, app, tagPosList);
                            result.TagPosInjected = tpAdded > 0;
                        }
                        catch (Exception ex) { StingLog.Warn($"Inject TAG_POS shared param: {ex.Message}"); }
                    }

                    // Inject formulas
                    if (opts.InjectFormulas && result.TagPosInjected)
                    {
                        result.FormulasInjected = InjectTagPosFormulas(famDoc);
                    }

                    // Create position types (Item 13)
                    if (opts.CreatePositionTypes && result.TagPosInjected)
                    {
                        result.PositionTypesCreated = InjectPositionTypes(famDoc);
                    }

                    // Automation + Presentation pack (family-local type parameters)
                    if (opts.InjectAutomationPack)
                    {
                        var (apAdded, apSkipped) = InjectAutomationPresentationPack(famDoc);
                        result.AutomationPackAdded = apAdded;
                        result.AutomationPackSkipped = apSkipped;
                    }

                    tx.Commit();
                }

                // Save
                string savedPath = rfaPath;
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var saveOpts = new SaveAsOptions
                    {
                        OverwriteExistingFile = true,
                        MaximumBackups = 1
                    };
                    famDoc.SaveAs(outputPath, saveOpts);
                    savedPath = outputPath;
                }
                else
                {
                    famDoc.Save();
                }

                result.Success = true;

                // Close before re-loading into the target project — Revit will refuse to
                // load a family that's still open as a separate Document in the same session.
                try { famDoc.Close(false); famDoc = null; }
                catch (Exception closeEx) { StingLog.Warn($"Close before LoadAfterSave: {closeEx.Message}"); }

                // Batch-load into project (optional). No-op if TargetProjectDoc is null.
                if (opts.LoadAfterSave && opts.TargetProjectDoc != null && File.Exists(savedPath))
                {
                    try
                    {
                        var loadOpts = new StingFamilyLoadOptions(opts.LoadOverwriteParameterValues);
                        Family loadedFam;
                        bool ok = opts.TargetProjectDoc.LoadFamily(savedPath, loadOpts, out loadedFam);
                        result.LoadedIntoProject = ok;
                        if (!ok) StingLog.Warn($"LoadAfterSave '{Path.GetFileName(savedPath)}': LoadFamily returned false");
                    }
                    catch (Exception loadEx)
                    {
                        StingLog.Warn($"LoadAfterSave '{Path.GetFileName(savedPath)}': {loadEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                StingLog.Error($"ProcessFamily '{rfaPath}'", ex);
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"Close family document: {ex.Message}"); }
            }

            return result;
        }

        /// <summary>
        /// Get family-specific parameter names for tie-in and sleeve tag families.
        /// Returns null for non-matching families so the caller falls back to GetDefaultParamsForCategory.
        /// Parameter lists derived from STING_TAG_CONFIG_v5_0_MEP.csv tag family definitions.
        /// </summary>
        private static List<string> GetParamsForFamily(string familyFileName)
        {
            if (string.IsNullOrEmpty(familyFileName)) return null;
            string name = familyFileName.ToUpperInvariant();

            // Common STING base tokens (included in every family)
            var baseParams = new List<string>
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.STATUS
            };

            // Add TAG container param names (ASS_TAG_1_TXT .. ASS_TAG_7F_TXT, discipline containers, SLV_TAG, etc.)
            var containers = ParamRegistry.AllContainers;
            if (containers != null)
            {
                foreach (var c in containers)
                {
                    if (c != null && !string.IsNullOrEmpty(c.ParamName) && !baseParams.Contains(c.ParamName))
                        baseParams.Add(c.ParamName);
                }
            }

            // Full 10-tier gating booleans + warning toggle + full style matrix.
            // Keeping this in lock-step with TagFamilyConfig.VisibilityParams and
            // TagFamilyConfig.StyleParams so family-param-creator and tag-family-creator
            // produce identical parameter sets.
            foreach (string p in TagFamilyConfig.VisibilityParams)
                if (!baseParams.Contains(p)) baseParams.Add(p);
            foreach (string p in TagFamilyConfig.StyleParams)
                if (!baseParams.Contains(p)) baseParams.Add(p);

            // Common tie-in params shared by ALL tie-in families (#46-#51)
            var tieinCommon = new[]
            {
                "ASS_TIEIN_TAG_1_TXT",
                "ASS_TIEIN_STATUS_TXT",
                "ASS_TIEIN_ELEV_TXT",
                "ASS_TIEIN_PHASE_TXT",
                "ASS_TIEIN_BY_TXT",
                "ASS_TIEIN_IFC_REF_TXT",
                "ASS_TIEIN_CONNECTED_BOOL",
                "ASS_TIEIN_FLOW_DIR_TXT",
                "ASS_CST_TOTAL_UGX_NR",
                "RGL_STD_TXT"
            };

            List<string> familySpecific = null;

            // ── Tie-In Pipe (#46) ──
            if (name.Contains("TIE-IN PIPE") || name.Contains("TIE_IN_PIPE") || name.Contains("TIEIN PIPE"))
            {
                familySpecific = new List<string>(tieinCommon)
                {
                    "PLM_PPE_SZ_MM",
                    "PLM_PPE_FLW_LPS",
                    "PLM_PSR_KPA",
                    "PLM_TAG_7_PARA_TIEIN_TXT",
                    "PLM_PPE_MAT_TXT",
                    "PLM_PPE_PSR_RATING_BAR",
                    "WARN_ASS_TIEIN_OPEN_PIPE",
                    "WARN_ASS_TIEIN_DEFERRED_PIPE"
                };
            }
            // ── Tie-In Duct (#47) ──
            else if (name.Contains("TIE-IN DUCT") || name.Contains("TIE_IN_DUCT") || name.Contains("TIEIN DUCT"))
            {
                familySpecific = new List<string>(tieinCommon)
                {
                    "HVC_DCT_SZ_TXT",
                    "HVC_DCT_FLW_CFM",
                    "HVC_VEL_MPS",
                    "HVC_DCT_MAT_TXT",
                    "HVC_TAG_7_PARA_TIEIN_TXT",
                    "HVC_DUCT_CLASS_TXT",
                    "WARN_ASS_TIEIN_OPEN_DUCT",
                    "WARN_ASS_TIEIN_DEFERRED_DUCT"
                };
            }
            // ── Tie-In Conduit (#48) ──
            else if (name.Contains("TIE-IN CONDUIT") || name.Contains("TIE_IN_CONDUIT") || name.Contains("TIEIN CONDUIT"))
            {
                familySpecific = new List<string>(tieinCommon)
                {
                    "ELC_CDT_SZ_MM",
                    "ELC_CDT_MAT_TXT",
                    "ELC_TAG_7_PARA_TIEIN_TXT",
                    "ELC_CDT_CBL_FILL_PCT",
                    "WARN_ASS_TIEIN_OPEN_CONDUIT"
                };
            }
            // ── Tie-In Cable Tray (#49) ──
            else if (name.Contains("TIE-IN CABLE TRAY") || name.Contains("TIE_IN_CABLE_TRAY") || name.Contains("TIEIN CABLE"))
            {
                familySpecific = new List<string>(tieinCommon)
                {
                    "ELC_CTR_SZ_TXT",
                    "ELC_CTR_MAT_TXT",
                    "ELC_CTR_FILL_PCT",
                    "ELC_TAG_7_PARA_TIEIN_TXT",
                    "WARN_ASS_TIEIN_OPEN_CABLETRAY"
                };
            }
            // ── Tie-In Fire Protection (#50) ──
            else if (name.Contains("TIE-IN FIRE") || name.Contains("TIE_IN_FIRE") || name.Contains("TIEIN FIRE"))
            {
                familySpecific = new List<string>(tieinCommon)
                {
                    "PLM_PPE_SZ_MM",
                    "PLM_PPE_FLW_LPS",
                    "PLM_PSR_KPA",
                    "FLS_TAG_7_PARA_TIEIN_TXT",
                    "PLM_PPE_PSR_RATING_BAR",
                    "WARN_ASS_TIEIN_OPEN_FIREPIPE",
                    "WARN_ASS_TIEIN_DEFERRED_FIREPIPE"
                };
            }
            // ── Tie-In Gas (#51) ──
            else if (name.Contains("TIE-IN GAS") || name.Contains("TIE_IN_GAS") || name.Contains("TIEIN GAS"))
            {
                familySpecific = new List<string>(tieinCommon)
                {
                    "PLM_PPE_SZ_MM",
                    "PLM_PSR_KPA",
                    "PLM_PPE_MAT_TXT",
                    "PLM_TAG_7_PARA_TIEIN_TXT",
                    "PLM_PPE_PSR_RATING_BAR",
                    "WARN_ASS_TIEIN_OPEN_GAS",
                    "WARN_ASS_TIEIN_DEFERRED_GAS"
                };
            }
            // ── MEP Sleeve (#53) ──
            else if (name.Contains("SLEEVE") || name.Contains("GENERIC MODELS TAG"))
            {
                familySpecific = new List<string>
                {
                    "SLV_SZ_MM",
                    "SLV_FIRE_RATING_TXT",
                    "SLV_SERVICE_TXT",
                    "SLV_STATUS_TXT",
                    "SLV_MAT_TXT",
                    "SLV_SEAL_TYPE_TXT",
                    "SLV_WALL_TYPE_TXT",
                    "SLV_FIRESTOP_PRODUCT_TXT",
                    "SLV_TESTED_TO_TXT",
                    "SLV_INSPECTION_DATE_TXT",
                    "WARN_SLV_NO_FIRE_RATING",
                    "WARN_SLV_NO_REF",
                    "WARN_SLV_NO_SEAL"
                };
            }

            if (familySpecific == null) return null;

            // Merge: base + family-specific (dedup)
            foreach (var p in familySpecific)
            {
                if (!string.IsNullOrEmpty(p) && !baseParams.Contains(p))
                    baseParams.Add(p);
            }

            return baseParams;
        }

        /// <summary>Get default parameter names for a category/discipline.</summary>
        private static List<string> GetDefaultParamsForCategory(string catName, string discCode)
        {
            var list = new List<string>
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.STATUS
            };

            // Add TAG container param names
            var containers = ParamRegistry.AllContainers;
            if (containers != null)
            {
                foreach (var c in containers)
                {
                    if (c != null && !string.IsNullOrEmpty(c.ParamName) && !list.Contains(c.ParamName))
                        list.Add(c.ParamName);
                }
            }

            // Add the full visibility + style-appearance fleet so every family
            // carries PARA_STATE_1..10, all 128 TAG_{size}{style}_{colour}_BOOL
            // variants, box/leader colour params, and the scale/depth caches.
            foreach (string p in TagFamilyConfig.VisibilityParams)
                if (!list.Contains(p)) list.Add(p);
            foreach (string p in TagFamilyConfig.StyleParams)
                if (!list.Contains(p)) list.Add(p);

            return list;
        }

        /// <summary>
        /// Process a family document that is already open (e.g. from
        /// <see cref="Document.EditFamily(Family)"/>) without touching disk. Honors the same
        /// <see cref="ProcessOptions"/> as <see cref="ProcessFamily"/> but skips Save / SaveAs
        /// and never opens or closes the document — the caller owns the <paramref name="famDoc"/>
        /// lifecycle and is responsible for loading it back into a project via
        /// <see cref="Document.LoadFamily(IFamilyLoadOptions, out Family)"/>.
        ///
        /// <para><b>LoadAfterSave semantics:</b> in the document-overload the in-memory family is
        /// loaded back into <c>opts.TargetProjectDoc</c> via the parameter-less LoadFamily overload
        /// rather than from disk — so it works even when the family has no file path.</para>
        /// </summary>
        public static FamilyResult ProcessFamilyDocument(
            Autodesk.Revit.ApplicationServices.Application app,
            Document famDoc,
            string familyDisplayName,
            ProcessOptions opts)
        {
            var result = new FamilyResult
            {
                SourcePath = familyDisplayName ?? "<in-memory>",
                OutputPath = "",
            };

            if (famDoc == null || !famDoc.IsFamilyDocument)
            {
                result.ErrorMessage = "Not a family document";
                return result;
            }

            try
            {
                var (cat, catName, discCode) = DetectFamilyCategory(famDoc);
                result.Category = catName;
                result.DiscCode = discCode;

                using (Transaction tx = new Transaction(famDoc, "STING Family Param Inject"))
                {
                    tx.Start();

                    if (opts.Purge != PurgeMode.None)
                    {
                        try
                        {
                            FamilyManager fmPurge = famDoc.FamilyManager;
                            var toRemove = fmPurge.GetParameters()
                                .Where(p => p.IsShared)
                                .Where(p => ShouldPurge(p, opts.Purge))
                                .ToList();
                            foreach (var fp in toRemove)
                            {
                                try { fmPurge.RemoveParameter(fp); }
                                catch (Exception rpEx) { StingLog.Warn($"Purge '{fp.Definition.Name}' ({opts.Purge}): {rpEx.Message}"); }
                            }
                            result.ParamsPurged = toRemove.Count;
                        }
                        catch (Exception purgeEx) { StingLog.Warn($"Purge {opts.Purge}: {purgeEx.Message}"); }
                    }

                    string fileName = familyDisplayName ?? "";
                    var paramList = opts.ParamNames.Count > 0
                        ? opts.ParamNames
                        : GetParamsForFamily(fileName)
                          ?? GetDefaultParamsForCategory(catName, discCode);
                    var (added, skipped) = InjectSharedParams(famDoc, app, paramList);
                    result.ParamsAdded = added;
                    result.ParamsSkipped = skipped;

                    result.TokensSeeded = SeedDefaultTokens(famDoc, catName, discCode);
                    result.CobiePropsWritten = SeedCobieTypeProperties(famDoc, fileName);

                    if (opts.InjectTagPos)
                    {
                        try
                        {
                            var tagPosList = new List<string> { ParamRegistry.TAG_POS };
                            var (tpAdded, _) = InjectSharedParams(famDoc, app, tagPosList);
                            result.TagPosInjected = tpAdded > 0;
                        }
                        catch (Exception ex) { StingLog.Warn($"Inject TAG_POS shared param: {ex.Message}"); }
                    }

                    if (opts.InjectFormulas && result.TagPosInjected)
                        result.FormulasInjected = InjectTagPosFormulas(famDoc);

                    if (opts.CreatePositionTypes && result.TagPosInjected)
                        result.PositionTypesCreated = InjectPositionTypes(famDoc);

                    if (opts.InjectAutomationPack)
                    {
                        var (apAdded, apSkipped) = InjectAutomationPresentationPack(famDoc);
                        result.AutomationPackAdded = apAdded;
                        result.AutomationPackSkipped = apSkipped;
                    }

                    tx.Commit();
                }

                result.Success = true;

                // Load back into the project. Uses the parameter-less LoadFamily overload —
                // the familyDoc is already open in this app instance, so Revit can pull it
                // directly without a file round-trip.
                if (opts.LoadAfterSave && opts.TargetProjectDoc != null)
                {
                    try
                    {
                        var loadOpts = new StingFamilyLoadOptions(opts.LoadOverwriteParameterValues);
                        Family loadedFam = famDoc.LoadFamily(opts.TargetProjectDoc, loadOpts);
                        result.LoadedIntoProject = loadedFam != null;
                    }
                    catch (Exception loadEx)
                    {
                        StingLog.Warn($"LoadAfterSave (in-memory) '{familyDisplayName}': {loadEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                StingLog.Error($"ProcessFamilyDocument '{familyDisplayName}'", ex);
            }

            return result;
        }
    }

    /// <summary>
    /// <see cref="IFamilyLoadOptions"/> implementation used by <see cref="FamilyParamEngine.ProcessFamily"/>
    /// and <see cref="FamilyParamEngine.ProcessFamilyDocument"/> when LoadAfterSave is enabled.
    /// Default source is <see cref="FamilySource.Family"/> (prefer the newly-processed copy) and
    /// overwriteParameterValues is caller-controlled via the constructor argument.
    /// </summary>
    internal class StingFamilyLoadOptions : IFamilyLoadOptions
    {
        private readonly bool _overwriteParameterValues;
        public StingFamilyLoadOptions(bool overwriteParameterValues)
        {
            _overwriteParameterValues = overwriteParameterValues;
        }

        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = _overwriteParameterValues;
            return true; // always overwrite the family definition itself
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = _overwriteParameterValues;
            return true;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  TAG FAMILY PARAMETER CREATOR COMMAND
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inject the STING TAG schema into existing tag family .rfa files.
    /// This command only touches tag families (tokens, ASS_TAG_* containers,
    /// TAG_POS, visibility gates, style matrix). For regular Revit families
    /// (doors, walls, MEP equipment, etc.) use
    /// <see cref="StingTools.Temp.FamilyParameterProcessorCommand"/> which
    /// drives FAMILY_PARAMETER_BINDINGS.csv + FORMULAS_WITH_DEPENDENCIES.csv.
    ///
    /// Like the processor, this command never calls app.NewFamilyDocument
    /// and never creates new .rfa files — it only modifies families already
    /// on disk. The default mode is purely additive: absent STING parameters
    /// are appended, but labels, formulas, family types, and previously-set
    /// bindings are left exactly as they were. Optional migrate / purge
    /// modes are explicit opt-ins for schema-reset workflows.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyParamCreatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                StingLog.Warn("FamilyParamCreator: No document open — cannot access Revit Application.");
                TaskDialog.Show("STING — Tag Family Parameter Creator",
                    "A Revit document must be open to use the Tag Family Parameter Creator.\n\n" +
                    "Open any Revit project (even a blank one) to provide the\n" +
                    "Revit Application context needed for opening .rfa files.\n\n" +
                    "This command is for TAG family files only. For regular Revit\n" +
                    "families (doors, walls, MEP equipment, etc.) use\n" +
                    "Temp ▸ Family Parameter Processor.");
                return Result.Failed;
            }

            var app = ctx.App.Application;

            // Mode selection using StingListPicker for polished UI.
            // Default is Add-Only: inject any missing STING shared parameters while
            // leaving all existing labels, formulas, family types, and current bindings
            // untouched. The "Migrate" modes are destructive — they rewrite TAG_POS
            // formulas, add position family types, and (in the purge variant) delete
            // every pre-existing STING shared parameter from the family.
            var modeItems = new List<StingListPicker.ListItem>
            {
                new StingListPicker.ListItem
                    { Label = "Single Family — Add Missing Params (recommended)", Detail = "Append any missing STING params. Existing labels, formulas and types are left alone.", Tag = "add_single" },
                new StingListPicker.ListItem
                    { Label = "Batch Folder — Add Missing Params (recommended)", Detail = "Append missing params across every .rfa in the target folder.", Tag = "add_batch" },
                new StingListPicker.ListItem
                    { Label = "Single Family — Clean Non-STING Params + Inject", Detail = "Remove any shared parameters whose GUID is not in the STING registry, then inject STING's schema. Use when adopting third-party families.", Tag = "clean_single" },
                new StingListPicker.ListItem
                    { Label = "Batch Folder — Clean Non-STING Params + Inject", Detail = "Clean + inject across every .rfa in the folder. Third-party / legacy shared params are removed by GUID.", Tag = "clean_batch" },
                new StingListPicker.ListItem
                    { Label = "Single Family — Migrate (rewrite TAG_POS + position types)", Detail = "Adds TAG_POS, writes the 16-branch formula, creates position types. Modifies label bindings.", Tag = "migrate_single" },
                new StingListPicker.ListItem
                    { Label = "Batch Folder — Migrate (rewrite TAG_POS + position types)", Detail = "Migrate every .rfa in the target folder.", Tag = "migrate_batch" },
                new StingListPicker.ListItem
                    { Label = "Single Family — Purge STING + Reinject (destructive)", Detail = "Remove every STING-registered shared param then re-inject. Use only for schema migrations.", Tag = "purge_single" },
                new StingListPicker.ListItem
                    { Label = "Batch Folder — Purge STING + Reinject (destructive)", Detail = "Purge + reinject across folder. Use only for schema migrations.", Tag = "purge_batch" },
            };
            var selected = StingListPicker.Show(
                "STING — Tag Family Parameter Creator",
                "Inject STING TAG schema (tokens, ASS_TAG containers, TAG_POS, style matrix) into tag family .rfa files. " +
                "For non-tag / regular Revit families (doors, walls, MEP equipment) use Temp ▸ Family Parameter Processor.",
                modeItems);
            if (selected == null || selected.Count == 0) return Result.Cancelled;
            string mode = selected[0].Tag as string ?? "add_single";

            bool cleanNonSting = mode.StartsWith("clean");
            bool purgeSting    = mode.StartsWith("purge");
            bool migrate       = mode.StartsWith("migrate") || purgeSting;
            bool isBatch       = mode.Contains("batch");

            // Ask whether to load the processed families into the active project after saving.
            // This turns "process folder of .rfa" into a one-click flow — useful during project
            // retrofit where the user has the target project open and wants those families
            // reloaded with the new schema.
            bool loadAfterSave = false;
            if (ctx.Doc != null && !ctx.Doc.IsFamilyDocument)
            {
                var td = new TaskDialog("STING — Load into project?")
                {
                    MainInstruction = "Load processed families into the active project?",
                    MainContent = $"After each .rfa is saved, load it into '{ctx.Doc.Title}'. " +
                                  "Instance parameter values on already-placed families are preserved.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                loadAfterSave = (td.Show() == TaskDialogResult.Yes);
            }

            // Options — Add mode is purely additive. Clean mode removes non-STING shared params
            // by GUID. Migrate mode rewrites TAG_POS / formulas / position types. Purge mode
            // additionally removes STING-registered shared params before reinjection.
            var opts = new FamilyParamEngine.ProcessOptions
            {
                InjectTagPos        = migrate,
                InjectFormulas      = migrate,
                CreatePositionTypes = migrate,
                Purge               = purgeSting    ? PurgeMode.StingOnly
                                    : cleanNonSting ? PurgeMode.NonSting
                                    : PurgeMode.None,
                LoadAfterSave       = loadAfterSave,
                TargetProjectDoc    = loadAfterSave ? ctx.Doc : null,
            };

            // Get file(s) to process using file/folder browser dialogs
            List<string> rfaFiles;
            string outputDir;

            if (isBatch)
            {
                // Use OpenFileDialog multi-select to pick .rfa files from a folder.
                // User selects one or more files; we process all .rfa in that directory.
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select any .rfa file in the target folder (all .rfa will be processed)",
                    Filter = "Revit Family Files (*.rfa)|*.rfa",
                    Multiselect = true,
                    InitialDirectory = StingToolsApp.DataPath ?? ""
                };
                if (dlg.ShowDialog() != true)
                    return Result.Cancelled;
                string selectedDir = Path.GetDirectoryName(dlg.FileName);
                if (string.IsNullOrEmpty(selectedDir) || !Directory.Exists(selectedDir))
                {
                    TaskDialog.Show("STING", "Invalid folder selected.");
                    return Result.Cancelled;
                }
                rfaFiles = Directory.GetFiles(selectedDir, "*.rfa", SearchOption.AllDirectories).ToList();
                outputDir = selectedDir;
                StingLog.Info($"FamilyParamCreator: batch mode — found {rfaFiles.Count} .rfa files in {selectedDir}");
            }
            else
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select .rfa family file to process",
                    Filter = "Revit Family Files (*.rfa)|*.rfa",
                    InitialDirectory = StingToolsApp.DataPath ?? ""
                };
                if (ofd.ShowDialog() != true)
                    return Result.Cancelled;
                string selectedFile = ofd.FileName;
                if (string.IsNullOrEmpty(selectedFile) || !File.Exists(selectedFile))
                {
                    TaskDialog.Show("STING", "Selected file not found.");
                    return Result.Cancelled;
                }
                rfaFiles = new List<string> { selectedFile };
                outputDir = Path.GetDirectoryName(selectedFile);
                StingLog.Info($"FamilyParamCreator: single mode — processing {selectedFile}");
            }

            if (rfaFiles.Count == 0)
            {
                TaskDialog.Show("STING", "No .rfa family files found to process.");
                return Result.Succeeded;
            }

            // Process with progress dialog
            var results = new List<FamilyParamEngine.FamilyResult>();
            int processed = 0;
            bool cancelled = false;

            var progress = StingProgressDialog.Show("STING — Tag Family Parameter Creator", rfaFiles.Count);
            try
            {
                foreach (string rfaPath in rfaFiles)
                {
                    if (EscapeChecker.IsEscapePressed())
                    {
                        cancelled = true;
                        break;
                    }

                    string fileName = Path.GetFileName(rfaPath);
                    progress.Increment($"Processing {fileName} ({processed + 1}/{rfaFiles.Count})");

                    string outPath = rfaPath; // overwrite in-place
                    var result = FamilyParamEngine.ProcessFamily(app, rfaPath, outPath, opts);
                    results.Add(result);
                    processed++;
                }
            }
            finally
            {
                progress.Close();
            }

            if (cancelled)
            {
                StingLog.Info($"FamilyParamCreator: cancelled after {processed}/{rfaFiles.Count} files");
            }

            // Report
            int succeeded = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);
            int totalParamsAdded = results.Sum(r => r.ParamsAdded);
            int totalTokensSeeded = results.Sum(r => r.TokensSeeded);
            int totalCobieProps = results.Sum(r => r.CobiePropsWritten);
            var catBreakdown = results.Where(r => r.Success)
                .GroupBy(r => r.Category)
                .Select(g => $"  {g.Key}: {g.Count()}")
                .ToList();

            int totalParamsPurged = results.Sum(r => r.ParamsPurged);
            int totalLoadedIntoProject = results.Count(r => r.LoadedIntoProject);

            var report = new StringBuilder();
            report.AppendLine($"Files: {processed} processed, {succeeded} succeeded, {failed} failed");
            if (cancelled) report.AppendLine($"  (cancelled — {rfaFiles.Count - processed} remaining)");
            report.AppendLine($"Parameters added: {totalParamsAdded}");
            if (totalParamsPurged > 0)
                report.AppendLine($"Parameters purged: {totalParamsPurged} (mode: {opts.Purge})");
            report.AppendLine($"Position types created: {results.Sum(r => r.PositionTypesCreated)}");
            report.AppendLine($"Tokens seeded: {totalTokensSeeded}");
            if (totalCobieProps > 0) report.AppendLine($"COBie properties written: {totalCobieProps}");
            if (opts.LoadAfterSave)
                report.AppendLine($"Loaded into '{ctx.Doc?.Title}': {totalLoadedIntoProject}/{succeeded}");
            if (catBreakdown.Count > 0)
            {
                report.AppendLine("\nCategories:");
                foreach (string line in catBreakdown)
                    report.AppendLine(line);
            }

            // Write CSV log
            string logPath = null;
            try
            {
                logPath = Path.Combine(outputDir,
                    $"STING_FamilyParamCreator_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csv = new StringBuilder();
                csv.AppendLine("SourcePath,OutputPath,Category,DiscCode,ParamsAdded,ParamsSkipped,ParamsPurged," +
                    "TagPosInjected,FormulasInjected,PositionTypesCreated,TokensSeeded,CobiePropsWritten,LoadedIntoProject,Status,ErrorMessage");
                foreach (var r in results)
                {
                    csv.AppendLine($"\"{r.SourcePath}\",\"{r.OutputPath}\",\"{r.Category}\"," +
                        $"{r.DiscCode},{r.ParamsAdded},{r.ParamsSkipped},{r.ParamsPurged},{r.TagPosInjected}," +
                        $"{r.FormulasInjected},{r.PositionTypesCreated},{r.TokensSeeded},{r.CobiePropsWritten},{r.LoadedIntoProject}," +
                        $"{(r.Success ? "OK" : "FAILED")},\"{r.ErrorMessage ?? ""}\"");
                }
                File.WriteAllText(logPath, csv.ToString());
                report.AppendLine($"\nLog: {logPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FamilyParamCreator CSV log: {ex.Message}");
            }

            if (failed > 0)
            {
                report.AppendLine("\nFailed files:");
                foreach (var r in results.Where(r => !r.Success).Take(10))
                    report.AppendLine($"  {Path.GetFileName(r.SourcePath)}: {r.ErrorMessage}");
            }

            TaskDialog.Show("STING — Tag Family Parameter Creator", report.ToString());
            StingLog.Info($"FamilyParamCreator: {succeeded}/{processed} succeeded, {totalParamsAdded} params added");
            return Result.Succeeded;
        }
    }
}
