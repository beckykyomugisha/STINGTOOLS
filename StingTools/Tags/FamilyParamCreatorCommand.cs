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
    //  FAMILY PARAMETER CREATOR ENGINE
    //
    //  Injects STING shared parameters into .rfa family files, detects category,
    //  adds tag position parameter with formulas, and creates position variant types.
    // ════════════════════════════════════════════════════════════════════════════

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
        /// Case-insensitive. Used by PurgeFirst to scope deletions to STING params only,
        /// leaving Revit built-ins and third-party params untouched.
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

        /// <summary>Options for family processing.</summary>
        public class ProcessOptions
        {
            public bool InjectTagPos { get; set; } = true;
            public bool InjectFormulas { get; set; } = true;
            public bool PlaceAnchor { get; set; } = false;
            public bool CreatePositionTypes { get; set; } = true;
            /// <summary>FIX-9.1: When true, remove ALL pre-existing shared parameters from the family
            /// (any STING prefix — ASS_, BLE_, CST_, ELC_, ELE_, FLS_, HVC_, ICT_, LTG_, MAT_,
            /// MEP_, NCL_, PER_, PLM_, RGL_, SEC_, SLV_, STING_, STR_, TAG_, WARN_) before injecting
            /// a fresh set. Default true so families cannot accumulate stale/duplicate bindings
            /// across successive runs.</summary>
            public bool PurgeFirst { get; set; } = true;
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

                using (Transaction tx = new Transaction(famDoc, "STING Family Param Creator"))
                {
                    tx.Start();

                    // FIX-9.2: Purge all pre-existing STING shared parameters before injection.
                    // Default: true. Scope covers all STING prefixes so families cannot carry
                    // duplicates or stale bindings from earlier runs / prior schema versions.
                    if (opts.PurgeFirst)
                    {
                        try
                        {
                            FamilyManager fmPurge = famDoc.FamilyManager;
                            var toRemove = fmPurge.GetParameters()
                                .Where(p => p.IsShared && IsStingPrefix(p.Definition.Name))
                                .ToList();
                            foreach (var fp in toRemove)
                            {
                                try { fmPurge.RemoveParameter(fp); }
                                catch (Exception rpEx) { StingLog.Warn($"PurgeFirst '{fp.Definition.Name}': {rpEx.Message}"); }
                            }
                            if (toRemove.Count > 0)
                                StingLog.Info($"PurgeFirst: removed {toRemove.Count} shared params from '{Path.GetFileName(rfaPath)}'");
                        }
                        catch (Exception purgeEx) { StingLog.Warn($"PurgeFirst: {purgeEx.Message}"); }
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

                    tx.Commit();
                }

                // Save
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var saveOpts = new SaveAsOptions
                    {
                        OverwriteExistingFile = true,
                        MaximumBackups = 1
                    };
                    famDoc.SaveAs(outputPath, saveOpts);
                }
                else
                {
                    famDoc.Save();
                }

                result.Success = true;
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

            // 3-tier gating booleans (progressive disclosure)
            var gatingBools = new[]
            {
                "TAG_PARA_STATE_2_BOOL",
                "TAG_PARA_STATE_3_BOOL",
                "TAG_WARN_VISIBLE_BOOL"
            };
            foreach (var g in gatingBools)
                if (!baseParams.Contains(g)) baseParams.Add(g);

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

            return list;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  FAMILY PARAMETER CREATOR COMMAND
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inject STING shared parameters into .rfa family files with category detection,
    /// tag position formulas, and named position variant types.
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
                TaskDialog.Show("STING — Family Param Creator",
                    "A Revit document must be open to use Family Param Creator.\n\n" +
                    "Open any Revit project (even a blank one) to provide the\n" +
                    "Revit Application context needed for opening .rfa files.");
                return Result.Failed;
            }

            var app = ctx.App.Application;

            // Mode selection using StingListPicker for polished UI.
            // Default workflow is Purge + Inject — families accumulate stale/duplicate
            // bindings otherwise. "Add without purging" remains for edge cases.
            var modeItems = new List<StingListPicker.ListItem>
            {
                new StingListPicker.ListItem
                    { Label = "Single Family — Purge + Inject (recommended)", Detail = "Remove all existing STING shared params, then inject a fresh set", Tag = "purge_single" },
                new StingListPicker.ListItem
                    { Label = "Batch Folder — Purge + Inject (recommended)", Detail = "Purge + re-inject every .rfa in the target folder", Tag = "purge_batch" },
                new StingListPicker.ListItem
                    { Label = "Single Family — Add Only (no purge)", Detail = "Append missing params without removing existing ones", Tag = "single" },
                new StingListPicker.ListItem
                    { Label = "Batch Folder — Add Only (no purge)", Detail = "Append missing params across folder without removing existing", Tag = "batch" },
            };
            var selected = StingListPicker.Show(
                "STING — Family Param Creator",
                "Inject STING shared parameters into .rfa family files",
                modeItems);
            if (selected == null || selected.Count == 0) return Result.Cancelled;
            string mode = selected[0].Tag as string ?? "single";

            bool purgeFirst = mode.StartsWith("purge");
            bool isBatch = mode.Contains("batch");

            // Options
            var opts = new FamilyParamEngine.ProcessOptions
            {
                InjectTagPos = true,
                InjectFormulas = true,
                CreatePositionTypes = true,
                PurgeFirst = purgeFirst
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

            var progress = StingProgressDialog.Show("STING — Family Param Creator", rfaFiles.Count);
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

            var report = new StringBuilder();
            report.AppendLine($"Files: {processed} processed, {succeeded} succeeded, {failed} failed");
            if (cancelled) report.AppendLine($"  (cancelled — {rfaFiles.Count - processed} remaining)");
            report.AppendLine($"Parameters added: {totalParamsAdded}");
            report.AppendLine($"Position types created: {results.Sum(r => r.PositionTypesCreated)}");
            report.AppendLine($"Tokens seeded: {totalTokensSeeded}");
            if (totalCobieProps > 0) report.AppendLine($"COBie properties written: {totalCobieProps}");
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
                csv.AppendLine("SourcePath,OutputPath,Category,DiscCode,ParamsAdded,ParamsSkipped," +
                    "TagPosInjected,FormulasInjected,PositionTypesCreated,TokensSeeded,CobiePropsWritten,Status,ErrorMessage");
                foreach (var r in results)
                {
                    csv.AppendLine($"\"{r.SourcePath}\",\"{r.OutputPath}\",\"{r.Category}\"," +
                        $"{r.DiscCode},{r.ParamsAdded},{r.ParamsSkipped},{r.TagPosInjected}," +
                        $"{r.FormulasInjected},{r.PositionTypesCreated},{r.TokensSeeded},{r.CobiePropsWritten}," +
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

            TaskDialog.Show("STING — Family Param Creator", report.ToString());
            StingLog.Info($"FamilyParamCreator: {succeeded}/{processed} succeeded, {totalParamsAdded} params added");
            return Result.Succeeded;
        }
    }
}
