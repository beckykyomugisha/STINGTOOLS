// ═══════════════════════════════════════════════════════════════════════
//  ExLinkPropertyDiscovery.cs — Universal Property Discovery Engine
//
//  Discovers ALL available parameters on ANY element in ANY Revit project
//  from three sources:
//    1. STING Shared Parameters (from ParamRegistry.AllParamGuids — 1,447+)
//    2. Revit Built-in Parameters (from BuiltInParameter enum)
//    3. Project/Family Live Parameters (from sample element iteration)
//
//  Zero hardcoding — every parameter discovery is runtime-reflective.
// ═══════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.ExLink
{
    /// <summary>
    /// Represents a discovered property available for export/import.
    /// Richer than PropertyDef — carries metadata for UI grouping and filtering.
    /// </summary>
    internal class AvailableProperty
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DataType { get; set; } = "";          // TEXT, INTEGER, DOUBLE, YESNO, ELEMENTID
        public string SourceType { get; set; } = "";         // STING_SHARED, REVIT_BUILTIN, PROJECT_PARAM, FAMILY_PARAM, CALCULATED
        public string ParameterGroup { get; set; } = "";     // e.g. "Identity", "HVAC", "Structural", etc.
        public bool IsReadOnly { get; set; }
        public bool IsHidden { get; set; }
        public string BuiltInName { get; set; } = "";
        public string SharedParamGuid { get; set; } = "";
        public List<string> ValidationList { get; set; }     // For STING tokens (DISC codes, SYS codes, etc.)

        /// <summary>Convert to PropertyDef for use with ExLinkEngine.</summary>
        public PropertyDef ToPropertyDef()
        {
            var pd = new PropertyDef
            {
                Name = Name,
                DisplayName = DisplayName,
                DataType = DataType,
                SourceType = SourceType,
                ParameterGroup = ParameterGroup,
                IsReadOnly = IsReadOnly,
                IsHidden = IsHidden
            };

            if (!string.IsNullOrEmpty(SharedParamGuid))
            {
                pd.PropertyType = "SHARED_PARAMETER";
                pd.SharedParamGuid = SharedParamGuid;
            }
            else if (!string.IsNullOrEmpty(BuiltInName))
            {
                pd.PropertyType = "BUILT_IN_PARAMETER";
                pd.LookupType = "BUILT_IN_PARAMETER";
                pd.BuiltInName = BuiltInName;
            }
            else if (SourceType == "CALCULATED")
            {
                pd.PropertyType = "CALCULATED_PROPERTY";
                pd.LookupType = "CALCULATED_PROPERTY";
                pd.IsReadOnly = true;
            }

            if (ValidationList != null && ValidationList.Count > 0)
                pd.ValidationList = new List<string>(ValidationList);

            return pd;
        }
    }

    /// <summary>
    /// Universal property discovery engine — finds ANY parameter on ANY element.
    /// </summary>
    internal static class ExLinkPropertyDiscovery
    {
        // ── Cached results per (document, elementType) to avoid repeated scans ──
        private static string _cachedDocKey;
        private static string _cachedElementType;
        private static List<AvailableProperty> _cachedProperties;

        /// <summary>
        /// Discover all available properties for a given element type in the document.
        /// Returns a merged, deduplicated, grouped list from all three sources.
        /// </summary>
        public static List<AvailableProperty> DiscoverProperties(Document doc, string elementType)
        {
            if (doc == null) return new List<AvailableProperty>();

            var docKey = doc.PathName ?? doc.Title ?? "Untitled";
            if (docKey == _cachedDocKey && elementType == _cachedElementType && _cachedProperties != null)
                return _cachedProperties;

            var allProps = new Dictionary<string, AvailableProperty>(StringComparer.OrdinalIgnoreCase);

            // Source 1: Calculated properties (always available)
            AddCalculatedProperties(allProps);

            // Source 2: STING shared parameters from ParamRegistry
            AddStingSharedParameters(allProps);

            // Source 3: Revit built-in parameters from a sample element
            var sampleElement = GetSampleElement(doc, elementType);
            if (sampleElement != null)
            {
                // Source 3a: Live parameters from sample element (instance + type)
                AddLiveParameters(doc, sampleElement, allProps);

                // Source 3b: Built-in parameters that exist on this element
                AddBuiltInParameters(sampleElement, allProps);
            }

            var result = allProps.Values
                .OrderBy(p => GetSourceSortOrder(p.SourceType))
                .ThenBy(p => p.ParameterGroup)
                .ThenBy(p => p.DisplayName)
                .ToList();

            _cachedDocKey = docKey;
            _cachedElementType = elementType;
            _cachedProperties = result;

            return result;
        }

        /// <summary>Clear the discovery cache (call on document switch).</summary>
        public static void InvalidateCache()
        {
            _cachedDocKey = null;
            _cachedElementType = null;
            _cachedProperties = null;
        }

        /// <summary>
        /// Get distinct source groups present in a property list, for UI filtering.
        /// </summary>
        public static List<string> GetSourceGroups(List<AvailableProperty> properties)
        {
            return properties
                .Select(p => p.SourceType)
                .Distinct()
                .OrderBy(s => GetSourceSortOrder(s))
                .ToList();
        }

        /// <summary>
        /// Get distinct parameter groups present in a property list, for UI filtering.
        /// </summary>
        public static List<string> GetParameterGroups(List<AvailableProperty> properties)
        {
            return properties
                .Where(p => !string.IsNullOrEmpty(p.ParameterGroup))
                .Select(p => p.ParameterGroup)
                .Distinct()
                .OrderBy(g => g)
                .ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Source 1: Calculated (synthetic) properties
        // ════════════════════════════════════════════════════════════════════

        private static void AddCalculatedProperties(Dictionary<string, AvailableProperty> props)
        {
            var calcProps = new[]
            {
                ("Element ID",      "Element_ID",       "Identity"),
                ("Unique ID",       "Unique_ID",        "Identity"),
                ("Category",        "Category",         "Identity"),
                ("Family",          "Family_Name",      "Identity"),
                ("Type",            "Type_Name",        "Identity"),
                ("Family and Type", "Family_And_Type",  "Identity"),
                ("Level",           "Level",            "Spatial"),
                ("Phase Created",   "Phase_Created",    "Lifecycle"),
                ("Phase Demolished","Phase_Demolished",  "Lifecycle"),
                ("Design Option",   "Design_Option",    "Project"),
                ("Workset",         "Workset",          "Project"),
            };

            foreach (var (display, name, group) in calcProps)
            {
                var key = $"[{display}]";
                props[key] = new AvailableProperty
                {
                    Name = name,
                    DisplayName = display,
                    DataType = "TEXT",
                    SourceType = "CALCULATED",
                    ParameterGroup = group,
                    IsReadOnly = true
                };
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Source 2: STING shared parameters from ParamRegistry
        // ════════════════════════════════════════════════════════════════════

        private static void AddStingSharedParameters(Dictionary<string, AvailableProperty> props)
        {
            try
            {
                var allGuids = ParamRegistry.AllParamGuids;
                if (allGuids == null) return;

                foreach (var kvp in allGuids)
                {
                    var paramName = kvp.Key;
                    var guid = kvp.Value;

                    if (props.ContainsKey(paramName)) continue;

                    var group = ClassifyStingParameterGroup(paramName);
                    var validationList = GetStingTokenValidationList(paramName);

                    props[paramName] = new AvailableProperty
                    {
                        Name = paramName,
                        DisplayName = FormatDisplayName(paramName),
                        DataType = InferStingDataType(paramName),
                        SourceType = "STING_SHARED",
                        ParameterGroup = group,
                        IsReadOnly = false,
                        IsHidden = paramName.EndsWith("_BOOL") && !IsStingTokenParam(paramName),
                        SharedParamGuid = guid.ToString(),
                        ValidationList = validationList
                    };
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExLinkPropertyDiscovery.AddStingSharedParameters: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Source 3a: Live parameters from sample element
        // ════════════════════════════════════════════════════════════════════

        private static void AddLiveParameters(Document doc, Element sampleElement, Dictionary<string, AvailableProperty> props)
        {
            try
            {
                // Instance parameters
                AddParametersFromParameterSet(sampleElement.Parameters, props, false);

                // Type parameters (if element has a type)
                var typeId = sampleElement.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var typeElem = doc.GetElement(typeId);
                    if (typeElem != null)
                        AddParametersFromParameterSet(typeElem.Parameters, props, true);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExLinkPropertyDiscovery.AddLiveParameters: {ex.Message}");
            }
        }

        private static void AddParametersFromParameterSet(ParameterSet parameters, Dictionary<string, AvailableProperty> props, bool isTypeParam)
        {
            if (parameters == null) return;

            foreach (Parameter p in parameters)
            {
                try
                {
                    if (p.Definition == null) continue;
                    var name = p.Definition.Name;
                    if (string.IsNullOrEmpty(name)) continue;

                    // Skip if already discovered via STING or calculated source
                    if (props.ContainsKey(name)) continue;

                    string sourceType;
                    string guid = "";
                    string builtInName = "";

                    if (p.Definition is InternalDefinition intDef &&
                        intDef.BuiltInParameter != BuiltInParameter.INVALID)
                    {
                        sourceType = "REVIT_BUILTIN";
                        builtInName = intDef.BuiltInParameter.ToString();
                    }
                    else if (p.IsShared)
                    {
                        sourceType = "PROJECT_PARAM";
                        try { guid = p.GUID.ToString(); } catch { /* non-shared */ }
                    }
                    else
                    {
                        sourceType = isTypeParam ? "FAMILY_PARAM" : "PROJECT_PARAM";
                    }

                    var dataType = StorageTypeToString(p.StorageType);
                    var group = GetRevitParameterGroupName(p);

                    props[name] = new AvailableProperty
                    {
                        Name = name,
                        DisplayName = name,
                        DataType = dataType,
                        SourceType = sourceType,
                        ParameterGroup = group,
                        IsReadOnly = p.IsReadOnly,
                        BuiltInName = builtInName,
                        SharedParamGuid = guid
                    };
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExLinkPropertyDiscovery.AddParam: {ex.Message}");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Source 3b: Additional built-in parameters from the element
        // ════════════════════════════════════════════════════════════════════

        private static void AddBuiltInParameters(Element sampleElement, Dictionary<string, AvailableProperty> props)
        {
            // Common built-in parameters that may not appear in the ParameterSet
            // but are accessible via el.get_Parameter(BuiltInParameter.XXX)
            var commonBIPs = new[]
            {
                BuiltInParameter.ALL_MODEL_MARK,
                BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
                BuiltInParameter.ALL_MODEL_DESCRIPTION,
                BuiltInParameter.ALL_MODEL_URL,
                BuiltInParameter.ALL_MODEL_IMAGE,
                BuiltInParameter.ALL_MODEL_MANUFACTURER,
                BuiltInParameter.ALL_MODEL_MODEL,
                BuiltInParameter.ALL_MODEL_COST,
                BuiltInParameter.UNIFORMAT_CODE,
                BuiltInParameter.OMNICLASS_CODE,
                BuiltInParameter.KEYNOTE_PARAM,
                BuiltInParameter.ELEM_FAMILY_PARAM,
                BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
                BuiltInParameter.ELEM_TYPE_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                BuiltInParameter.PHASE_CREATED,
                BuiltInParameter.PHASE_DEMOLISHED,
                BuiltInParameter.DESIGN_OPTION_ID,
                BuiltInParameter.ELEM_PARTITION_PARAM,
                BuiltInParameter.SYMBOL_NAME_PARAM,
                BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM,
            };

            foreach (var bip in commonBIPs)
            {
                try
                {
                    var p = sampleElement.get_Parameter(bip);
                    if (p == null) continue;

                    var name = p.Definition?.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (props.ContainsKey(name)) continue;

                    props[name] = new AvailableProperty
                    {
                        Name = name,
                        DisplayName = name,
                        DataType = StorageTypeToString(p.StorageType),
                        SourceType = "REVIT_BUILTIN",
                        ParameterGroup = GetRevitParameterGroupName(p),
                        IsReadOnly = p.IsReadOnly,
                        BuiltInName = bip.ToString()
                    };
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExLinkPropertyDiscovery.AddBIP {bip}: {ex.Message}");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Sample element retrieval
        // ════════════════════════════════════════════════════════════════════

        private static Element GetSampleElement(Document doc, string elementType)
        {
            try
            {
                // Reuse ExLinkEngine's element type filter to get a sample
                var tempDef = new LinkDefinition { ElementType = elementType };
                var elements = ExLinkEngine.CollectElements(doc, tempDef);
                return elements.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExLinkPropertyDiscovery.GetSampleElement: {ex.Message}");
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Classification and formatting helpers
        // ════════════════════════════════════════════════════════════════════

        private static string ClassifyStingParameterGroup(string paramName)
        {
            if (paramName.StartsWith("ASS_TAG_")) return "Tags";
            if (paramName.StartsWith("ASS_DISCIPLINE") || paramName.StartsWith("ASS_LOC") ||
                paramName.StartsWith("ASS_ZONE") || paramName.StartsWith("ASS_LVL") ||
                paramName.StartsWith("ASS_SYSTEM") || paramName.StartsWith("ASS_FUNC") ||
                paramName.StartsWith("ASS_PRODCT") || paramName.StartsWith("ASS_SEQ")) return "ISO 19650 Tokens";
            if (paramName.StartsWith("ASS_STATUS") || paramName.StartsWith("ASS_REV") ||
                paramName.StartsWith("ASS_ORIGIN")) return "Lifecycle";
            if (paramName.StartsWith("ASS_")) return "STING Identity";
            if (paramName.StartsWith("HVC_")) return "HVAC";
            if (paramName.StartsWith("ELC_") || paramName.StartsWith("ELE_") || paramName.StartsWith("LTG_")) return "Electrical";
            if (paramName.StartsWith("PLM_")) return "Plumbing";
            if (paramName.StartsWith("FLS_")) return "Fire Safety";
            if (paramName.StartsWith("COM_") || paramName.StartsWith("SEC_") ||
                paramName.StartsWith("NCL_") || paramName.StartsWith("ICT_")) return "Communications";
            if (paramName.StartsWith("BLE_")) return "Building Elements";
            if (paramName.StartsWith("MAT_")) return "Materials";
            if (paramName.StartsWith("STR_")) return "Structural";
            if (paramName.StartsWith("MEP_")) return "MEP General";
            if (paramName.StartsWith("MNT_")) return "Maintenance";
            if (paramName.StartsWith("PER_")) return "Performance";
            if (paramName.StartsWith("RGL_")) return "Regulatory";
            if (paramName.StartsWith("CST_")) return "Cost";
            if (paramName.StartsWith("TAG_")) return "Tag Style";
            if (paramName.StartsWith("STING_")) return "STING System";
            if (paramName.StartsWith("VIEW_")) return "View";
            if (paramName.StartsWith("WARN_")) return "Warnings";
            return "Other";
        }

        private static string FormatDisplayName(string paramName)
        {
            // Convert ASS_SYSTEM_TYPE_TXT → "System Type"
            // Remove prefix and suffix patterns
            var name = paramName;

            // Remove known prefixes
            var prefixes = new[] { "ASS_", "HVC_", "ELC_", "ELE_", "LTG_", "PLM_", "FLS_",
                                   "COM_", "SEC_", "NCL_", "ICT_", "BLE_", "MAT_", "STR_",
                                   "MEP_", "MNT_", "PER_", "RGL_", "CST_", "TAG_", "STING_",
                                   "VIEW_", "WARN_" };
            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }
            }

            // Remove known suffixes
            var suffixes = new[] { "_TXT", "_NR", "_BOOL", "_MM", "_M", "_SQ_M", "_CU_M",
                                   "_KPA", "_PCT", "_DB", "_INT" };
            foreach (var suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    break;
                }
            }

            // Convert underscores to spaces and title-case
            return string.Join(" ", name.Split('_')
                .Where(w => !string.IsNullOrEmpty(w))
                .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1).ToLower() : "")));
        }

        private static string InferStingDataType(string paramName)
        {
            if (paramName.EndsWith("_BOOL")) return "YESNO";
            if (paramName.EndsWith("_INT")) return "INTEGER";
            if (paramName.EndsWith("_NR") || paramName.EndsWith("_MM") ||
                paramName.EndsWith("_M") || paramName.EndsWith("_SQ_M") ||
                paramName.EndsWith("_CU_M") || paramName.EndsWith("_KPA") ||
                paramName.EndsWith("_PCT") || paramName.EndsWith("_DB")) return "DOUBLE";
            return "TEXT";
        }

        private static bool IsStingTokenParam(string paramName)
        {
            return paramName == ParamRegistry.DISC ||
                   paramName == ParamRegistry.LOC ||
                   paramName == ParamRegistry.ZONE ||
                   paramName == ParamRegistry.LVL ||
                   paramName == ParamRegistry.SYS ||
                   paramName == ParamRegistry.FUNC ||
                   paramName == ParamRegistry.PROD ||
                   paramName == ParamRegistry.SEQ ||
                   paramName == ParamRegistry.STATUS ||
                   paramName == ParamRegistry.REV;
        }

        private static List<string> GetStingTokenValidationList(string paramName)
        {
            try
            {
                if (paramName == ParamRegistry.DISC)
                    return TagConfig.DiscMap?.Values.Distinct().OrderBy(v => v).ToList();
                if (paramName == ParamRegistry.SYS)
                    return TagConfig.SysMap?.Keys.OrderBy(v => v).ToList();
                if (paramName == ParamRegistry.FUNC)
                    return TagConfig.FuncMap?.Values.Distinct().OrderBy(v => v).ToList();
                if (paramName == ParamRegistry.LOC)
                    return TagConfig.LocCodes?.ToList();
                if (paramName == ParamRegistry.ZONE)
                    return TagConfig.ZoneCodes?.ToList();
                if (paramName == ParamRegistry.STATUS)
                    return new List<string> { "NEW", "EXISTING", "DEMOLISHED", "TEMPORARY" };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetStingTokenValidationList({paramName}): {ex.Message}");
            }
            return null;
        }

        private static string StorageTypeToString(StorageType st)
        {
            switch (st)
            {
                case StorageType.String: return "TEXT";
                case StorageType.Integer: return "INTEGER";
                case StorageType.Double: return "DOUBLE";
                case StorageType.ElementId: return "ELEMENTID";
                default: return "TEXT";
            }
        }

        private static string GetRevitParameterGroupName(Parameter p)
        {
            try
            {
                // Revit 2025+ uses ForgeTypeId for group
                var groupId = p.Definition.GetGroupTypeId();
                if (groupId != null)
                {
                    var name = groupId.TypeId;
                    if (!string.IsNullOrEmpty(name))
                    {
                        // Extract readable portion: "autodesk.parameter.group:identityData-2.0.0" → "Identity Data"
                        var colonIdx = name.LastIndexOf(':');
                        if (colonIdx >= 0)
                        {
                            var tail = name.Substring(colonIdx + 1);
                            var dashIdx = tail.IndexOf('-');
                            if (dashIdx >= 0) tail = tail.Substring(0, dashIdx);
                            // camelCase → "Title Case"
                            return FormatCamelCase(tail);
                        }
                    }
                }
            }
            catch { /* pre-2025 or missing group */ }

            return "Other";
        }

        private static string FormatCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var result = new System.Text.StringBuilder();
            result.Append(char.ToUpper(input[0]));
            for (int i = 1; i < input.Length; i++)
            {
                if (char.IsUpper(input[i]) && i > 0 && char.IsLower(input[i - 1]))
                    result.Append(' ');
                result.Append(input[i]);
            }
            return result.ToString();
        }

        private static int GetSourceSortOrder(string sourceType)
        {
            switch (sourceType)
            {
                case "CALCULATED": return 0;
                case "STING_SHARED": return 1;
                case "REVIT_BUILTIN": return 2;
                case "PROJECT_PARAM": return 3;
                case "FAMILY_PARAM": return 4;
                default: return 5;
            }
        }
    }
}
