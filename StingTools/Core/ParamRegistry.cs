using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Single source of truth for all parameter names, GUIDs, container definitions,
    /// and category bindings. Loaded once from PARAMETER_REGISTRY.json at startup.
    ///
    /// USAGE:
    ///   Instead of:  ParameterHelpers.GetString(el, "ASS_TAG_1_TXT")
    ///   Write:       ParameterHelpers.GetString(el, ParamRegistry.TAG1)
    ///
    ///   Instead of:  duplicating 36 container definitions in 4 files
    ///   Write:       ParamRegistry.ContainerGroups / ParamRegistry.ContainersForCategory(cat)
    ///
    /// To add/rename a parameter:
    ///   1. Edit PARAMETER_REGISTRY.json
    ///   2. Run "Sync Parameter Schema" command
    ///   3. All code automatically uses the new name
    /// </summary>
    public static class ParamRegistry
    {
        // ── Loaded state ────────────────────────────────────────────────
        private static bool _loaded;
        private static readonly object _lock = new object();

        // ── Tag format ──────────────────────────────────────────────────
        public static string Separator { get; private set; } = "-";
        public static int NumPad { get; private set; } = 4;
        public static string[] SegmentOrder { get; private set; } = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

        // ── Source token definitions ────────────────────────────────────
        public static TokenDef[] SourceTokens { get; private set; } = Array.Empty<TokenDef>();

        /// <summary>All 8 source token parameter names in tag segment order.</summary>
        public static string[] AllTokenParams { get; private set; } = Array.Empty<string>();

        // ── Convenience accessors: source token param names by slot ─────
        /// <summary>Discipline token parameter name (slot 0).</summary>
        public static string DISC => TokenParamName(0);
        /// <summary>Location token parameter name (slot 1).</summary>
        public static string LOC  => TokenParamName(1);
        /// <summary>Zone token parameter name (slot 2).</summary>
        public static string ZONE => TokenParamName(2);
        /// <summary>Level token parameter name (slot 3).</summary>
        public static string LVL  => TokenParamName(3);
        /// <summary>System token parameter name (slot 4).</summary>
        public static string SYS  => TokenParamName(4);
        /// <summary>Function token parameter name (slot 5).</summary>
        public static string FUNC => TokenParamName(5);
        /// <summary>Product token parameter name (slot 6).</summary>
        public static string PROD => TokenParamName(6);
        /// <summary>Sequence token parameter name (slot 7).</summary>
        public static string SEQ  => TokenParamName(7);

        // ── Support parameter names ────────────────────────────────────
        public static string STATUS { get; private set; } = "ASS_STATUS_TXT";
        public static string DETAIL_NUM { get; private set; } = "ASS_INST_DETAIL_NUM_TXT";
        public static string MNT_TYPE { get; private set; } = "MNT_TYPE_TXT";

        // ── Universal tag container names (convenience) ─────────────────
        /// <summary>Full 8-segment tag: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ</summary>
        public static string TAG1 { get; private set; } = "ASS_TAG_1_TXT";
        /// <summary>Short ID: DISC-PROD-SEQ</summary>
        public static string TAG2 { get; private set; } = "ASS_TAG_2_TXT";
        /// <summary>Location: LOC-ZONE-LVL</summary>
        public static string TAG3 { get; private set; } = "ASS_TAG_3_TXT";
        /// <summary>System: SYS-FUNC</summary>
        public static string TAG4 { get; private set; } = "ASS_TAG_4_TXT";
        /// <summary>Multi-line top: DISC-LOC-ZONE-LVL</summary>
        public static string TAG5 { get; private set; } = "ASS_TAG_5_TXT";
        /// <summary>Multi-line bottom: SYS-FUNC-PROD-SEQ</summary>
        public static string TAG6 { get; private set; } = "ASS_TAG_6_TXT";

        // ── Token presets (named token index arrays) ────────────────────
        public static Dictionary<string, int[]> TokenPresets { get; private set; } = new Dictionary<string, int[]>();

        // ── Container groups and flat container list ─────────────────────
        public static ContainerGroupDef[] ContainerGroups { get; private set; } = Array.Empty<ContainerGroupDef>();
        private static ContainerParamDef[] _allContainers;
        private static Dictionary<string, List<ContainerParamDef>> _containersByCategory;

        // ── GUID lookups ────────────────────────────────────────────────
        private static Dictionary<string, Guid> _guidByName;
        private static Dictionary<Guid, string> _nameByGuid;

        // ── Universal params (Pass 1) ───────────────────────────────────
        /// <summary>All parameter names that should be bound to all 53 categories (Pass 1).</summary>
        public static string[] UniversalParams { get; private set; } = Array.Empty<string>();

        // ── Category mappings ───────────────────────────────────────────
        /// <summary>Category display name → BuiltInCategory enum string.</summary>
        public static Dictionary<string, string> CategoryEnumMap { get; private set; } = new Dictionary<string, string>();
        /// <summary>All universal category display names.</summary>
        public static string[] UniversalCategories { get; private set; } = Array.Empty<string>();

        // ── Discipline bindings (Pass 2): param → category enums ────────
        private static Dictionary<string, string[]> _disciplineCategoryNames;

        // ════════════════════════════════════════════════════════════════
        // Public API
        // ════════════════════════════════════════════════════════════════

        /// <summary>Get token parameter name by slot index (0-7).</summary>
        public static string TokenParamName(int slot)
        {
            EnsureLoaded();
            return slot >= 0 && slot < AllTokenParams.Length ? AllTokenParams[slot] : "";
        }

        /// <summary>Get token parameter name by key ("DISC", "LOC", etc.).</summary>
        public static string TokenParamName(string key)
        {
            EnsureLoaded();
            var tok = Array.Find(SourceTokens, t => t.Key == key);
            return tok?.ParamName ?? "";
        }

        /// <summary>Get GUID for a parameter name. Returns Guid.Empty if not found.</summary>
        public static Guid GetGuid(string paramName)
        {
            EnsureLoaded();
            return _guidByName != null && _guidByName.TryGetValue(paramName, out Guid g) ? g : Guid.Empty;
        }

        /// <summary>Get parameter name for a GUID. Returns null if not found.</summary>
        public static string GetParamName(Guid guid)
        {
            EnsureLoaded();
            return _nameByGuid != null && _nameByGuid.TryGetValue(guid, out string n) ? n : null;
        }

        /// <summary>Get all parameter names that have GUIDs (tokens + support + containers).</summary>
        public static Dictionary<string, Guid> AllParamGuids
        {
            get { EnsureLoaded(); return _guidByName ?? new Dictionary<string, Guid>(); }
        }

        /// <summary>All container definitions across all groups (flat list).</summary>
        public static ContainerParamDef[] AllContainers
        {
            get
            {
                EnsureLoaded();
                if (_allContainers == null)
                    _allContainers = ContainerGroups.SelectMany(g => g.Params).ToArray();
                return _allContainers;
            }
        }

        /// <summary>Get container definitions that apply to a specific Revit category name.</summary>
        public static ContainerParamDef[] ContainersForCategory(string categoryName)
        {
            EnsureLoaded();
            if (_containersByCategory == null) BuildCategoryIndex();
            if (string.IsNullOrEmpty(categoryName)) return AllContainers.Where(c => c.Categories == null).ToArray();

            var result = new List<ContainerParamDef>();
            // Universal containers (null categories) always apply
            foreach (var c in AllContainers)
            {
                if (c.Categories == null)
                    result.Add(c);
            }
            // Plus category-specific matches
            if (_containersByCategory.TryGetValue(categoryName, out var specific))
                result.AddRange(specific);

            return result.ToArray();
        }

        /// <summary>Get category display names for a discipline-specific parameter.</summary>
        public static string[] GetCategoryNamesForParam(string paramName)
        {
            EnsureLoaded();
            if (_disciplineCategoryNames != null && _disciplineCategoryNames.TryGetValue(paramName, out string[] cats))
                return cats;
            return Array.Empty<string>();
        }

        /// <summary>Resolve token preset name to index array. Returns raw indices if not a preset name.</summary>
        public static int[] ResolveTokenPreset(string presetOrRaw)
        {
            EnsureLoaded();
            if (TokenPresets.TryGetValue(presetOrRaw, out int[] preset))
                return preset;
            return Array.Empty<int>();
        }

        /// <summary>
        /// Build tuple array matching the legacy format used by BuildTagsCommand and TokenWriterCommands.
        /// Returns (paramName, tokenIndices, separator, categoryNames) for all containers.
        /// </summary>
        public static (string param, int[] tokens, string sep, string[] categories)[] GetContainerTuples()
        {
            EnsureLoaded();
            return AllContainers.Select(c => (c.ParamName, c.TokenIndices, c.Separator, c.Categories)).ToArray();
        }

        /// <summary>
        /// Build the BuiltInCategory array for a discipline parameter, resolving
        /// category display names through the CategoryEnumMap. Used by SharedParamGuids
        /// for Pass 2 binding.
        /// </summary>
        public static BuiltInCategory[] ResolveCategoryEnums(string[] categoryNames)
        {
            if (categoryNames == null || categoryNames.Length == 0) return Array.Empty<BuiltInCategory>();

            var result = new List<BuiltInCategory>();
            foreach (string name in categoryNames)
            {
                if (CategoryEnumMap.TryGetValue(name, out string enumStr))
                {
                    if (Enum.TryParse(enumStr, out BuiltInCategory bic))
                        result.Add(bic);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Resolve the universal category list to BuiltInCategory enums.
        /// </summary>
        public static BuiltInCategory[] ResolveUniversalCategoryEnums()
        {
            EnsureLoaded();
            return ResolveCategoryEnums(UniversalCategories);
        }

        /// <summary>
        /// Build the discipline bindings dictionary in the format SharedParamGuids expects:
        /// paramName → BuiltInCategory[]. Derived from container_groups.
        /// </summary>
        public static Dictionary<string, BuiltInCategory[]> BuildDisciplineBindings()
        {
            EnsureLoaded();
            var bindings = new Dictionary<string, BuiltInCategory[]>();
            foreach (var group in ContainerGroups)
            {
                if (group.Categories == null) continue; // universal — handled by Pass 1
                var enums = ResolveCategoryEnums(group.Categories);
                foreach (var param in group.Params)
                    bindings[param.ParamName] = enums;
            }
            return bindings;
        }

        /// <summary>Force reload from disk. Call after editing PARAMETER_REGISTRY.json.</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _loaded = false;
                _allContainers = null;
                _containersByCategory = null;
            }
            EnsureLoaded();
        }

        // ════════════════════════════════════════════════════════════════
        // Loading
        // ════════════════════════════════════════════════════════════════

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                LoadFromFile();
                _loaded = true;
            }
        }

        private static void LoadFromFile()
        {
            string path = StingToolsApp.FindDataFile("PARAMETER_REGISTRY.json");
            if (path == null || !File.Exists(path))
            {
                StingLog.Warn("PARAMETER_REGISTRY.json not found — using compiled defaults");
                LoadDefaults();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                JObject root = JObject.Parse(json);

                // Tag format
                var fmt = root["tag_format"];
                if (fmt != null)
                {
                    Separator = fmt["separator"]?.ToString() ?? "-";
                    NumPad = fmt["num_pad"]?.Value<int>() ?? 4;
                    SegmentOrder = fmt["segment_order"]?.ToObject<string[]>() ?? SegmentOrder;
                }

                // Source tokens
                var tokArr = root["source_tokens"] as JArray;
                if (tokArr != null)
                {
                    var tokens = new List<TokenDef>();
                    var tokenNames = new List<string>();
                    foreach (JObject t in tokArr)
                    {
                        var def = new TokenDef
                        {
                            Slot = t["slot"]?.Value<int>() ?? 0,
                            Key = t["key"]?.ToString() ?? "",
                            ParamName = t["param_name"]?.ToString() ?? "",
                            GuidStr = t["guid"]?.ToString() ?? "",
                            Description = t["description"]?.ToString() ?? "",
                        };
                        tokens.Add(def);
                        tokenNames.Add(def.ParamName);
                    }
                    SourceTokens = tokens.OrderBy(t => t.Slot).ToArray();
                    AllTokenParams = tokenNames.ToArray();
                }

                // Support params
                var supArr = root["support_params"] as JArray;
                if (supArr != null)
                {
                    foreach (JObject s in supArr)
                    {
                        string name = s["param_name"]?.ToString() ?? "";
                        if (name.Contains("STATUS")) STATUS = name;
                        else if (name.Contains("DETAIL")) DETAIL_NUM = name;
                        else if (name.Contains("MNT")) MNT_TYPE = name;
                    }
                }

                // Token presets
                var presets = root["token_presets"] as JObject;
                TokenPresets = new Dictionary<string, int[]>();
                if (presets != null)
                {
                    foreach (var kvp in presets)
                        TokenPresets[kvp.Key] = kvp.Value.ToObject<int[]>();
                }

                // Container groups
                var groupArr = root["container_groups"] as JArray;
                if (groupArr != null)
                {
                    var groups = new List<ContainerGroupDef>();
                    foreach (JObject g in groupArr)
                    {
                        var groupDef = new ContainerGroupDef
                        {
                            Group = g["group"]?.ToString() ?? "",
                            GroupCode = g["group_code"]?.ToString() ?? "",
                            Categories = g["categories"]?.Type == JTokenType.Null ? null : g["categories"]?.ToObject<string[]>(),
                        };

                        var paramArr = g["params"] as JArray;
                        if (paramArr != null)
                        {
                            var parms = new List<ContainerParamDef>();
                            foreach (JObject p in paramArr)
                            {
                                string tokensRef = p["tokens"]?.ToString() ?? "all";
                                int[] tokenIndices = TokenPresets.TryGetValue(tokensRef, out int[] preset)
                                    ? preset
                                    : (tokensRef.StartsWith("[") ? p["tokens"].ToObject<int[]>() : new int[] { 0,1,2,3,4,5,6,7 });

                                parms.Add(new ContainerParamDef
                                {
                                    ParamName = p["param_name"]?.ToString() ?? "",
                                    GuidStr = p["guid"]?.ToString() ?? "",
                                    TokenIndices = tokenIndices,
                                    TokenPresetName = tokensRef,
                                    Separator = p["separator"]?.ToString() ?? "-",
                                    Prefix = p["prefix"]?.ToString() ?? "",
                                    Suffix = p["suffix"]?.ToString() ?? "",
                                    Description = p["description"]?.ToString() ?? "",
                                    Categories = groupDef.Categories,
                                });
                            }
                            groupDef.Params = parms.ToArray();
                        }
                        else
                        {
                            groupDef.Params = Array.Empty<ContainerParamDef>();
                        }

                        groups.Add(groupDef);
                    }
                    ContainerGroups = groups.ToArray();
                }

                // Set convenience names from first container group (Universal)
                if (ContainerGroups.Length > 0 && ContainerGroups[0].Params.Length >= 6)
                {
                    TAG1 = ContainerGroups[0].Params[0].ParamName;
                    TAG2 = ContainerGroups[0].Params[1].ParamName;
                    TAG3 = ContainerGroups[0].Params[2].ParamName;
                    TAG4 = ContainerGroups[0].Params[3].ParamName;
                    TAG5 = ContainerGroups[0].Params[4].ParamName;
                    TAG6 = ContainerGroups[0].Params[5].ParamName;
                }

                // Category enum map
                var catMap = root["category_enum_map"] as JObject;
                CategoryEnumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (catMap != null)
                {
                    foreach (var kvp in catMap)
                        CategoryEnumMap[kvp.Key] = kvp.Value.ToString();
                }

                // Universal categories
                UniversalCategories = root["universal_categories"]?.ToObject<string[]>() ?? Array.Empty<string>();

                // Build GUID lookups
                BuildGuidMaps(root);

                // Build universal params list
                BuildUniversalParams(root);

                // Build discipline category name mappings
                BuildDisciplineCategoryNames();

                StingLog.Info($"ParamRegistry loaded: {SourceTokens.Length} tokens, {ContainerGroups.Length} groups, {AllContainers.Length} containers, {_guidByName?.Count ?? 0} GUIDs");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to load PARAMETER_REGISTRY.json", ex);
                LoadDefaults();
            }
        }

        private static void BuildGuidMaps(JObject root)
        {
            _guidByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
            _nameByGuid = new Dictionary<Guid, string>();

            // Source tokens
            foreach (var tok in SourceTokens)
            {
                if (Guid.TryParse(tok.GuidStr, out Guid g))
                {
                    _guidByName[tok.ParamName] = g;
                    _nameByGuid[g] = tok.ParamName;
                }
            }

            // Support params
            var supArr = root["support_params"] as JArray;
            if (supArr != null)
            {
                foreach (JObject s in supArr)
                {
                    string name = s["param_name"]?.ToString();
                    string guidStr = s["guid"]?.ToString();
                    if (!string.IsNullOrEmpty(name) && Guid.TryParse(guidStr, out Guid g))
                    {
                        _guidByName[name] = g;
                        _nameByGuid[g] = name;
                    }
                }
            }

            // Container params
            foreach (var group in ContainerGroups)
            {
                foreach (var p in group.Params)
                {
                    if (Guid.TryParse(p.GuidStr, out Guid g))
                    {
                        _guidByName[p.ParamName] = g;
                        _nameByGuid[g] = p.ParamName;
                    }
                }
            }
        }

        private static void BuildUniversalParams(JObject root)
        {
            var list = new List<string>();
            // All source tokens are universal
            list.AddRange(AllTokenParams);
            // All universal container params
            if (ContainerGroups.Length > 0 && ContainerGroups[0].Categories == null)
            {
                foreach (var p in ContainerGroups[0].Params)
                    list.Add(p.ParamName);
            }
            // Support params
            var supArr = root["support_params"] as JArray;
            if (supArr != null)
            {
                foreach (JObject s in supArr)
                {
                    string name = s["param_name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }
            }
            UniversalParams = list.Distinct().ToArray();
        }

        private static void BuildDisciplineCategoryNames()
        {
            _disciplineCategoryNames = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var group in ContainerGroups)
            {
                if (group.Categories == null) continue;
                foreach (var param in group.Params)
                    _disciplineCategoryNames[param.ParamName] = group.Categories;
            }
        }

        private static void BuildCategoryIndex()
        {
            _containersByCategory = new Dictionary<string, List<ContainerParamDef>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in AllContainers)
            {
                if (c.Categories == null) continue;
                foreach (string cat in c.Categories)
                {
                    if (!_containersByCategory.TryGetValue(cat, out var list))
                    {
                        list = new List<ContainerParamDef>();
                        _containersByCategory[cat] = list;
                    }
                    list.Add(c);
                }
            }
        }

        /// <summary>Fallback defaults matching the original hardcoded values.</summary>
        private static void LoadDefaults()
        {
            Separator = "-";
            NumPad = 4;
            SegmentOrder = new[] { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

            AllTokenParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };

            SourceTokens = new[]
            {
                new TokenDef { Slot = 0, Key = "DISC", ParamName = "ASS_DISCIPLINE_COD_TXT", GuidStr = "8c7dcfd7-f922-52d0-b859-81cae8d17dc0" },
                new TokenDef { Slot = 1, Key = "LOC",  ParamName = "ASS_LOC_TXT",             GuidStr = "b7469c27-c80e-5b59-b999-1a99ba620cd1" },
                new TokenDef { Slot = 2, Key = "ZONE", ParamName = "ASS_ZONE_TXT",            GuidStr = "dc0d940f-e4ce-5e73-a0a7-fc7094148c84" },
                new TokenDef { Slot = 3, Key = "LVL",  ParamName = "ASS_LVL_COD_TXT",        GuidStr = "b1e51fab-fa88-50df-8b2f-bcdbe48e7c78" },
                new TokenDef { Slot = 4, Key = "SYS",  ParamName = "ASS_SYSTEM_TYPE_TXT",     GuidStr = "2b3658d9-bfc6-56db-9df5-901337fde0f5" },
                new TokenDef { Slot = 5, Key = "FUNC", ParamName = "ASS_FUNC_TXT",            GuidStr = "1ddff9a8-6e66-4a93-88fe-f3b94fbd5710" },
                new TokenDef { Slot = 6, Key = "PROD", ParamName = "ASS_PRODCT_COD_TXT",      GuidStr = "082a2a05-3387-5501-b355-51dd45e23e9f" },
                new TokenDef { Slot = 7, Key = "SEQ",  ParamName = "ASS_SEQ_NUM_TXT",         GuidStr = "bbe1cd55-247b-48bd-94ba-a08031f06d5b" },
            };

            TAG1 = "ASS_TAG_1_TXT"; TAG2 = "ASS_TAG_2_TXT"; TAG3 = "ASS_TAG_3_TXT";
            TAG4 = "ASS_TAG_4_TXT"; TAG5 = "ASS_TAG_5_TXT"; TAG6 = "ASS_TAG_6_TXT";
            STATUS = "ASS_STATUS_TXT"; DETAIL_NUM = "ASS_INST_DETAIL_NUM_TXT"; MNT_TYPE = "MNT_TYPE_TXT";

            TokenPresets = new Dictionary<string, int[]>
            {
                { "all", new[] {0,1,2,3,4,5,6,7} },
                { "short_id", new[] {0,6,7} },
                { "location", new[] {1,2,3} },
                { "system", new[] {4,5} },
                { "sys_ref", new[] {4,5,6} },
                { "line1", new[] {0,1,2,3} },
                { "line2", new[] {4,5,6,7} },
            };

            ContainerGroups = Array.Empty<ContainerGroupDef>();
            UniversalParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
                "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
                "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
                "ASS_STATUS_TXT", "ASS_INST_DETAIL_NUM_TXT", "MNT_TYPE_TXT",
            };
        }

        // ════════════════════════════════════════════════════════════════
        // Helper: assemble container value from token values
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Assemble a container tag string from token values using the container's
        /// token indices and separator. Shared logic used by all combine/build commands.
        /// </summary>
        public static string AssembleContainer(ContainerParamDef container, string[] tokenValues)
        {
            if (container.TokenIndices == null || container.TokenIndices.Length == 0)
                return "";
            var parts = new List<string>();
            bool anyValue = false;
            foreach (int idx in container.TokenIndices)
            {
                string val = idx >= 0 && idx < tokenValues.Length ? tokenValues[idx] : "";
                parts.Add(val);
                if (!string.IsNullOrEmpty(val)) anyValue = true;
            }
            if (!anyValue) return "";

            string assembled = string.Join(container.Separator, parts);
            if (!string.IsNullOrEmpty(container.Prefix)) assembled = container.Prefix + assembled;
            if (!string.IsNullOrEmpty(container.Suffix)) assembled = assembled + container.Suffix;
            return assembled;
        }

        /// <summary>
        /// Read all 8 token values from an element into an array matching AllTokenParams order.
        /// </summary>
        public static string[] ReadTokenValues(Element el)
        {
            EnsureLoaded();
            string[] values = new string[AllTokenParams.Length];
            for (int i = 0; i < AllTokenParams.Length; i++)
                values[i] = ParameterHelpers.GetString(el, AllTokenParams[i]);
            return values;
        }

        /// <summary>
        /// Write all applicable containers for an element based on its category.
        /// Returns count of containers written.
        /// </summary>
        public static int WriteContainers(Element el, string[] tokenValues, string categoryName,
            bool overwrite = true, string skipParam = null)
        {
            int written = 0;
            var containers = ContainersForCategory(categoryName);
            foreach (var c in containers)
            {
                if (c.ParamName == skipParam) continue;
                string assembled = AssembleContainer(c, tokenValues);
                if (!string.IsNullOrEmpty(assembled))
                {
                    if (ParameterHelpers.SetString(el, c.ParamName, assembled, overwrite))
                        written++;
                }
            }
            return written;
        }

        // ════════════════════════════════════════════════════════════════
        // Data types
        // ════════════════════════════════════════════════════════════════

        public class TokenDef
        {
            public int Slot { get; set; }
            public string Key { get; set; }
            public string ParamName { get; set; }
            public string GuidStr { get; set; }
            public string Description { get; set; }
        }

        public class ContainerGroupDef
        {
            public string Group { get; set; }
            public string GroupCode { get; set; }
            public string[] Categories { get; set; }
            public ContainerParamDef[] Params { get; set; }
        }

        public class ContainerParamDef
        {
            public string ParamName { get; set; }
            public string GuidStr { get; set; }
            public int[] TokenIndices { get; set; }
            public string TokenPresetName { get; set; }
            public string Separator { get; set; }
            public string Prefix { get; set; }
            public string Suffix { get; set; }
            public string Description { get; set; }
            public string[] Categories { get; set; }
        }
    }
}
