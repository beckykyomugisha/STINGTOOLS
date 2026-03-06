using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════════
    //  Tag Style Engine — rule-based tag family type switching
    //
    //  Maps element conditions (discipline, status, zone, any parameter)
    //  to tag family type names (e.g. 2BOLD_RED, 3.5ITALIC_GREEN).
    //  Rules are loaded from TAG_STYLE_RULES.json and evaluated
    //  top-down (first match wins) per IndependentTag.
    //
    //  This automates what is otherwise manual: selecting a tag type in
    //  the Type Properties dialog for each annotation tag. The tag family
    //  types encode text size, weight (NOM/BOLD/ITALIC), and color
    //  directly in the family definition.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Core engine for rule-based tag style resolution.
    /// Loads presets from TAG_STYLE_RULES.json, evaluates conditions against
    /// host element parameters, and resolves to tag family type names.
    /// </summary>
    internal static class TagStyleEngine
    {
        // ── Data model ──────────────────────────────────────────────────

        public class StyleRule
        {
            public Dictionary<string, string> Conditions { get; set; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string TagType { get; set; }
        }

        public class StylePreset
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string DefaultType { get; set; }
            public List<StyleRule> Rules { get; set; } = new List<StyleRule>();
        }

        // ── Special condition keys ──────────────────────────────────────

        private const string COND_TAG_COMPLETE = "_tag_complete";

        // ── Loading ─────────────────────────────────────────────────────

        /// <summary>Load all presets from TAG_STYLE_RULES.json.</summary>
        public static Dictionary<string, StylePreset> LoadPresets()
        {
            var result = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase);

            string path = StingToolsApp.FindDataFile("TAG_STYLE_RULES.json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn("TagStyleEngine: TAG_STYLE_RULES.json not found");
                return result;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                JObject presets = root["presets"] as JObject;
                if (presets == null) return result;

                foreach (var prop in presets.Properties())
                {
                    var preset = new StylePreset
                    {
                        Name = prop.Name,
                        Description = (string)prop.Value["description"] ?? "",
                        DefaultType = (string)prop.Value["default_type"] ?? "2NOM_BLACK"
                    };

                    JArray rules = prop.Value["rules"] as JArray;
                    if (rules != null)
                    {
                        foreach (JObject ruleObj in rules)
                        {
                            var rule = new StyleRule
                            {
                                TagType = (string)ruleObj["tag_type"] ?? preset.DefaultType
                            };
                            JObject conds = ruleObj["conditions"] as JObject;
                            if (conds != null)
                            {
                                foreach (var c in conds.Properties())
                                    rule.Conditions[c.Name] = (string)c.Value ?? "";
                            }
                            preset.Rules.Add(rule);
                        }
                    }

                    result[prop.Name] = preset;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("TagStyleEngine: failed to load presets", ex);
            }

            return result;
        }

        /// <summary>Save a user-created preset back to TAG_STYLE_RULES.json.</summary>
        public static bool SavePreset(StylePreset preset)
        {
            string path = StingToolsApp.FindDataFile("TAG_STYLE_RULES.json");
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                JObject presets = root["presets"] as JObject;
                if (presets == null)
                {
                    presets = new JObject();
                    root["presets"] = presets;
                }

                var presetObj = new JObject
                {
                    ["description"] = preset.Description,
                    ["default_type"] = preset.DefaultType
                };

                var rulesArr = new JArray();
                foreach (var rule in preset.Rules)
                {
                    var ruleObj = new JObject
                    {
                        ["tag_type"] = rule.TagType
                    };
                    var condsObj = new JObject();
                    foreach (var kvp in rule.Conditions)
                        condsObj[kvp.Key] = kvp.Value;
                    ruleObj["conditions"] = condsObj;
                    rulesArr.Add(ruleObj);
                }
                presetObj["rules"] = rulesArr;
                presets[preset.Name] = presetObj;

                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                StingLog.Info($"TagStyleEngine: saved preset '{preset.Name}'");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("TagStyleEngine: failed to save preset", ex);
                return false;
            }
        }

        // ── Rule evaluation ─────────────────────────────────────────────

        /// <summary>
        /// Resolve which tag type name to use for a given host element.
        /// Evaluates rules top-down; first match wins.
        /// </summary>
        public static string ResolveTagType(Document doc, Element host, StylePreset preset)
        {
            if (host == null || preset == null) return preset?.DefaultType ?? "2NOM_BLACK";

            foreach (var rule in preset.Rules)
            {
                if (EvaluateConditions(doc, host, rule.Conditions))
                    return rule.TagType;
            }

            return preset.DefaultType;
        }

        /// <summary>Check if all conditions in a rule match the host element.</summary>
        private static bool EvaluateConditions(Document doc, Element host,
            Dictionary<string, string> conditions)
        {
            foreach (var kvp in conditions)
            {
                string condKey = kvp.Key;
                string expected = kvp.Value;

                if (string.Equals(condKey, COND_TAG_COMPLETE, StringComparison.OrdinalIgnoreCase))
                {
                    // Special: evaluate tag completeness
                    string tag1 = ParameterHelpers.GetString(host, ParamRegistry.TAG1);
                    bool complete = TagConfig.TagIsComplete(tag1);
                    bool partial = !complete && !string.IsNullOrWhiteSpace(tag1);

                    if (expected == "true" && !complete) return false;
                    if (expected == "partial" && !partial) return false;
                    if (expected == "false" && (complete || partial)) return false;
                }
                else
                {
                    // Standard: read parameter value and compare
                    string actual = ParameterHelpers.GetString(host, condKey);
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        }

        // ── Tag type index ──────────────────────────────────────────────

        /// <summary>
        /// Build a lookup from tag type name → FamilySymbol ElementId
        /// for all annotation tag types loaded in the project.
        /// </summary>
        public static Dictionary<string, ElementId> BuildTagTypeIndex(Document doc)
        {
            var index = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

            var tagTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    try { return fs.Family.FamilyCategory?.CategoryType == CategoryType.Annotation; }
                    catch { return false; }
                });

            foreach (var fs in tagTypes)
            {
                // Index by type name (e.g. "2BOLD_RED") and by "Family: Type"
                if (!index.ContainsKey(fs.Name))
                    index[fs.Name] = fs.Id;

                string fullName = $"{fs.Family.Name}: {fs.Name}";
                if (!index.ContainsKey(fullName))
                    index[fullName] = fs.Id;
            }

            return index;
        }

        /// <summary>
        /// Get the host element for an IndependentTag.
        /// Returns null if the tag has no valid host.
        /// </summary>
        public static Element GetTagHost(IndependentTag tag, Document doc)
        {
            try
            {
                var hostIds = tag.GetTaggedLocalElementIds();
                if (hostIds.Count == 0) return null;
                return doc.GetElement(hostIds.First());
            }
            catch { return null; }
        }

        // ── Preview / dry-run ───────────────────────────────────────────

        public class StylePreview
        {
            public int TotalTags { get; set; }
            public int WouldChange { get; set; }
            public int TypeNotFound { get; set; }
            public int NoHost { get; set; }
            public Dictionary<string, int> TypeDistribution { get; set; }
                = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Dry-run: preview what would change without modifying anything.
        /// </summary>
        public static StylePreview Preview(Document doc, List<IndependentTag> tags,
            StylePreset preset, Dictionary<string, ElementId> typeIndex)
        {
            var result = new StylePreview { TotalTags = tags.Count };

            foreach (var tag in tags)
            {
                Element host = GetTagHost(tag, doc);
                if (host == null)
                {
                    result.NoHost++;
                    continue;
                }

                string targetTypeName = ResolveTagType(doc, host, preset);

                if (!result.TypeDistribution.ContainsKey(targetTypeName))
                    result.TypeDistribution[targetTypeName] = 0;
                result.TypeDistribution[targetTypeName]++;

                if (!typeIndex.TryGetValue(targetTypeName, out ElementId targetTypeId))
                {
                    result.TypeNotFound++;
                    continue;
                }

                if (tag.GetTypeId() != targetTypeId)
                    result.WouldChange++;
            }

            return result;
        }

        /// <summary>Format preview results for display.</summary>
        public static string FormatPreview(StylePreview preview, string presetName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Preset: {presetName}");
            sb.AppendLine($"Total tags: {preview.TotalTags:N0}");
            sb.AppendLine($"Would change: {preview.WouldChange:N0}");
            if (preview.NoHost > 0)
                sb.AppendLine($"No host element: {preview.NoHost}");
            if (preview.TypeNotFound > 0)
                sb.AppendLine($"Type not loaded: {preview.TypeNotFound} (load tag families first)");
            sb.AppendLine();
            sb.AppendLine("Target distribution:");
            foreach (var kvp in preview.TypeDistribution.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");
            return sb.ToString();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  1. ApplyTagStyles — evaluate rules, switch all tags to matching type
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply tag style rules from a preset to all annotation tags in the active view
    /// (or selection). Evaluates each tag's host element against the preset rules
    /// and switches the IndependentTag to the resolved family type via ChangeTypeId.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyTagStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Load presets
            var presets = TagStyleEngine.LoadPresets();
            if (presets.Count == 0)
            {
                TaskDialog.Show("Apply Tag Styles",
                    "No presets found in TAG_STYLE_RULES.json.\n" +
                    "Use 'Save Style Preset' to create one.");
                return Result.Succeeded;
            }

            // Get target tags
            var (tags, fromSel) = Organise.AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Apply Tag Styles", "No annotation tags found in view.");
                return Result.Succeeded;
            }

            // Pick preset (top 4)
            var presetList = presets.Values.ToList();
            TaskDialog dlg = new TaskDialog("Apply Tag Styles");
            dlg.MainInstruction = $"Apply style preset to {tags.Count:N0} tags";
            dlg.MainContent = fromSel ? "(from selection)" : "(all tags in active view)";

            int maxLinks = Math.Min(presetList.Count, 4);
            for (int i = 0; i < maxLinks; i++)
            {
                dlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    presetList[i].Name, presetList[i].Description);
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int picked = -1;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: picked = 0; break;
                case TaskDialogResult.CommandLink2: picked = 1; break;
                case TaskDialogResult.CommandLink3: picked = 2; break;
                case TaskDialogResult.CommandLink4: picked = 3; break;
                default: return Result.Cancelled;
            }

            var preset = presetList[picked];
            var typeIndex = TagStyleEngine.BuildTagTypeIndex(doc);

            // Apply
            int changed = 0;
            int skipped = 0;
            int noHost = 0;
            int typeNotFound = 0;
            var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tx = new Transaction(doc, "STING Apply Tag Styles"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        Element host = TagStyleEngine.GetTagHost(tag, doc);
                        if (host == null) { noHost++; continue; }

                        string targetTypeName = TagStyleEngine.ResolveTagType(doc, host, preset);

                        if (!typeCounts.ContainsKey(targetTypeName))
                            typeCounts[targetTypeName] = 0;
                        typeCounts[targetTypeName]++;

                        if (!typeIndex.TryGetValue(targetTypeName, out ElementId targetTypeId))
                        {
                            typeNotFound++;
                            continue;
                        }

                        if (tag.GetTypeId() != targetTypeId && tag.IsValidType(targetTypeId))
                        {
                            tag.ChangeTypeId(targetTypeId);
                            changed++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"TagStyleEngine: skip tag {tag.Id} — {ex.Message}");
                        skipped++;
                    }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Preset: {preset.Name}");
            report.AppendLine($"Changed: {changed:N0}  |  Already correct: {skipped:N0}");
            if (noHost > 0) report.AppendLine($"No host element: {noHost}");
            if (typeNotFound > 0)
                report.AppendLine($"Type not loaded: {typeNotFound} — load tag families first");
            report.AppendLine();
            report.AppendLine("Distribution:");
            foreach (var kvp in typeCounts.OrderByDescending(x => x.Value))
                report.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");

            TaskDialog.Show("Apply Tag Styles", report.ToString());
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. PreviewTagStyles — dry-run showing what would change
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dry-run preview of tag style application. Shows what would change
    /// without modifying any tags. Useful for verifying rules before committing.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PreviewTagStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var presets = TagStyleEngine.LoadPresets();
            if (presets.Count == 0)
            {
                TaskDialog.Show("Preview Tag Styles", "No presets found.");
                return Result.Succeeded;
            }

            var (tags, _) = Organise.AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Preview Tag Styles", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Pick preset
            var presetList = presets.Values.ToList();
            TaskDialog dlg = new TaskDialog("Preview Tag Styles");
            dlg.MainInstruction = "Select preset to preview";
            int maxLinks = Math.Min(presetList.Count, 4);
            for (int i = 0; i < maxLinks; i++)
            {
                dlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    presetList[i].Name, presetList[i].Description);
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int picked = -1;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: picked = 0; break;
                case TaskDialogResult.CommandLink2: picked = 1; break;
                case TaskDialogResult.CommandLink3: picked = 2; break;
                case TaskDialogResult.CommandLink4: picked = 3; break;
                default: return Result.Cancelled;
            }

            var preset = presetList[picked];
            var typeIndex = TagStyleEngine.BuildTagTypeIndex(doc);
            var preview = TagStyleEngine.Preview(doc, tags, preset, typeIndex);

            TaskDialog.Show("Preview Tag Styles",
                TagStyleEngine.FormatPreview(preview, preset.Name));
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. SetTagStyleRule — add/edit a single rule via dialog
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Interactive rule editor: pick a parameter, value, and target tag type
    /// to add a new rule to an existing or new preset.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetTagStyleRuleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Step 1: Pick condition parameter
            TaskDialog step1 = new TaskDialog("Set Style Rule — Step 1/3");
            step1.MainInstruction = "What parameter should this rule match?";
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Discipline (ASS_DISCIPLINE_COD_TXT)", "M, E, P, A, S, FP, LV, G");
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Status (ASS_STATUS_TXT)", "NEW, EXISTING, DEMOLISHED, TEMPORARY");
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "System (ASS_SYSTEM_TYPE_TXT)", "HVAC, DCW, SAN, LV, FP, etc.");
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Zone (ASS_ZONE_TXT)", "Z01, Z02, Z03, Z04");
            step1.CommonButtons = TaskDialogCommonButtons.Cancel;

            string paramName;
            switch (step1.Show())
            {
                case TaskDialogResult.CommandLink1: paramName = ParamRegistry.DISC; break;
                case TaskDialogResult.CommandLink2: paramName = "ASS_STATUS_TXT"; break;
                case TaskDialogResult.CommandLink3: paramName = ParamRegistry.SYS; break;
                case TaskDialogResult.CommandLink4: paramName = ParamRegistry.ZONE; break;
                default: return Result.Cancelled;
            }

            // Step 2: Pick value — scan project for actual values
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            foreach (Element el in collector)
            {
                string val = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrWhiteSpace(val)) values.Add(val);
            }

            if (values.Count == 0)
            {
                TaskDialog.Show("Set Style Rule",
                    $"No elements have values for '{paramName}'.\nTag elements first.");
                return Result.Succeeded;
            }

            var sortedValues = values.OrderBy(v => v).ToList();
            TaskDialog step2 = new TaskDialog("Set Style Rule — Step 2/3");
            step2.MainInstruction = $"Pick value for {paramName}";
            int maxVals = Math.Min(sortedValues.Count, 4);
            for (int i = 0; i < maxVals; i++)
            {
                step2.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    sortedValues[i]);
            }
            step2.CommonButtons = TaskDialogCommonButtons.Cancel;

            int valPicked = -1;
            switch (step2.Show())
            {
                case TaskDialogResult.CommandLink1: valPicked = 0; break;
                case TaskDialogResult.CommandLink2: valPicked = 1; break;
                case TaskDialogResult.CommandLink3: valPicked = 2; break;
                case TaskDialogResult.CommandLink4: valPicked = 3; break;
                default: return Result.Cancelled;
            }
            string condValue = sortedValues[valPicked];

            // Step 3: Pick target tag type — show loaded types
            var typeIndex = TagStyleEngine.BuildTagTypeIndex(doc);
            var typeNames = typeIndex.Keys
                .Where(k => !k.Contains(":")) // type names only, not "Family: Type"
                .OrderBy(n => n)
                .ToList();

            if (typeNames.Count == 0)
            {
                TaskDialog.Show("Set Style Rule",
                    "No annotation tag types found.\nLoad tag families first.");
                return Result.Succeeded;
            }

            // Group by size for cleaner display
            TaskDialog step3 = new TaskDialog("Set Style Rule — Step 3/3");
            step3.MainInstruction = "Pick target tag type";
            step3.MainContent = $"Rule: when {paramName} = {condValue}\n\n" +
                "Available types:\n" +
                string.Join(", ", typeNames.Take(20)) +
                (typeNames.Count > 20 ? $"\n  ... and {typeNames.Count - 20} more" : "");

            // Show 4 most common/useful options
            var preferredTypes = new[] { "2BOLD_BLUE", "2BOLD_RED", "2BOLD_GREEN", "2NOM_BLACK",
                "2BOLD_ORANGE", "2BOLD_PURPLE", "2NOM_GREY", "2BOLDITALIC_RED" };
            var available = preferredTypes.Where(t => typeIndex.ContainsKey(t)).ToList();
            if (available.Count < 4)
            {
                // Fill with whatever is loaded
                foreach (var t in typeNames)
                {
                    if (available.Count >= 4) break;
                    if (!available.Contains(t)) available.Add(t);
                }
            }

            for (int i = 0; i < Math.Min(available.Count, 4); i++)
            {
                step3.AddCommandLink((TaskDialogCommandLinkId)(i + 1001), available[i]);
            }
            step3.CommonButtons = TaskDialogCommonButtons.Cancel;

            int typePicked = -1;
            switch (step3.Show())
            {
                case TaskDialogResult.CommandLink1: typePicked = 0; break;
                case TaskDialogResult.CommandLink2: typePicked = 1; break;
                case TaskDialogResult.CommandLink3: typePicked = 2; break;
                case TaskDialogResult.CommandLink4: typePicked = 3; break;
                default: return Result.Cancelled;
            }
            string targetType = available[typePicked];

            // Save to preset — add to "Custom" preset or create it
            var presets = TagStyleEngine.LoadPresets();
            TagStyleEngine.StylePreset custom;
            if (!presets.TryGetValue("Custom", out custom))
            {
                custom = new TagStyleEngine.StylePreset
                {
                    Name = "Custom",
                    Description = "User-created rules",
                    DefaultType = "2NOM_BLACK"
                };
            }

            // Remove existing rule with same condition if present
            custom.Rules.RemoveAll(r =>
                r.Conditions.Count == 1 &&
                r.Conditions.ContainsKey(paramName) &&
                string.Equals(r.Conditions[paramName], condValue, StringComparison.OrdinalIgnoreCase));

            // Add new rule
            var newRule = new TagStyleEngine.StyleRule { TagType = targetType };
            newRule.Conditions[paramName] = condValue;
            custom.Rules.Add(newRule);

            if (TagStyleEngine.SavePreset(custom))
            {
                TaskDialog.Show("Set Style Rule",
                    $"Rule saved to 'Custom' preset:\n\n" +
                    $"  When {paramName} = {condValue}\n" +
                    $"  → Use tag type: {targetType}\n\n" +
                    $"Custom preset now has {custom.Rules.Count} rule(s).\n" +
                    "Use 'Apply Tag Styles' to apply.");
            }
            else
            {
                TaskDialog.Show("Set Style Rule", "Failed to save rule — check log.");
            }

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  4. SaveTagStylePreset — save current tag type distribution as preset
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Captures the current tag type assignments in the view and saves them
    /// as a named preset. This "learns" from the current state — reverse
    /// engineering rules from what the user has already set up manually.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveTagStylePresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var (tags, _) = Organise.AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Save Style Preset", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Analyse current distribution: discipline → tag type
            var discToTypes = new Dictionary<string, Dictionary<string, int>>(
                StringComparer.OrdinalIgnoreCase);
            int noHost = 0;

            foreach (var tag in tags)
            {
                Element host = TagStyleEngine.GetTagHost(tag, doc);
                if (host == null) { noHost++; continue; }

                string disc = ParameterHelpers.GetString(host, ParamRegistry.DISC);
                if (string.IsNullOrWhiteSpace(disc)) disc = "_NONE";

                Element typeEl = doc.GetElement(tag.GetTypeId());
                string typeName = typeEl?.Name ?? "Unknown";

                if (!discToTypes.ContainsKey(disc))
                    discToTypes[disc] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (!discToTypes[disc].ContainsKey(typeName))
                    discToTypes[disc][typeName] = 0;
                discToTypes[disc][typeName]++;
            }

            // Find the dominant tag type per discipline
            var rules = new List<TagStyleEngine.StyleRule>();
            string overallDefault = "2NOM_BLACK";
            int maxCount = 0;

            foreach (var kvp in discToTypes)
            {
                string disc = kvp.Key;
                var dominant = kvp.Value.OrderByDescending(x => x.Value).First();

                if (disc == "_NONE")
                {
                    if (dominant.Value > maxCount)
                    {
                        overallDefault = dominant.Key;
                        maxCount = dominant.Value;
                    }
                    continue;
                }

                var rule = new TagStyleEngine.StyleRule { TagType = dominant.Key };
                rule.Conditions[ParamRegistry.DISC] = disc;
                rules.Add(rule);

                if (dominant.Value > maxCount)
                {
                    overallDefault = dominant.Key;
                    maxCount = dominant.Value;
                }
            }

            // Pick a name
            TaskDialog nameDlg = new TaskDialog("Save Style Preset");
            nameDlg.MainInstruction = "Name this preset";
            nameDlg.MainContent =
                $"Learned {rules.Count} discipline rules from {tags.Count:N0} tags.\n\n" +
                "Distribution:\n" +
                string.Join("\n", discToTypes.Select(kvp =>
                    $"  {kvp.Key}: {kvp.Value.OrderByDescending(x => x.Value).First().Key} " +
                    $"({kvp.Value.Values.Sum()})"));

            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Learned from View",
                $"Default: {overallDefault}, {rules.Count} rules");
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "My Standard",
                "Project standard tag styling");
            nameDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Phase-specific",
                "For this project phase");
            nameDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string presetName;
            switch (nameDlg.Show())
            {
                case TaskDialogResult.CommandLink1: presetName = "Learned from View"; break;
                case TaskDialogResult.CommandLink2: presetName = "My Standard"; break;
                case TaskDialogResult.CommandLink3: presetName = "Phase-specific"; break;
                default: return Result.Cancelled;
            }

            var preset = new TagStyleEngine.StylePreset
            {
                Name = presetName,
                Description = $"Auto-learned from {tags.Count} tags in {doc.ActiveView.Name}",
                DefaultType = overallDefault,
                Rules = rules
            };

            if (TagStyleEngine.SavePreset(preset))
            {
                TaskDialog.Show("Save Style Preset",
                    $"Saved preset '{presetName}' with {rules.Count} rules.\n" +
                    $"Default type: {overallDefault}\n\n" +
                    "Use 'Apply Tag Styles' to apply to other views.");
            }
            else
            {
                TaskDialog.Show("Save Style Preset", "Failed to save — check log.");
            }

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  5. LoadTagStylePreset — load and immediately apply a saved preset
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lists all saved presets with their descriptions and rule counts,
    /// lets the user pick one, shows a preview, then applies.
    /// Combines load + preview + apply in one smooth workflow.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadTagStylePresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var presets = TagStyleEngine.LoadPresets();
            if (presets.Count == 0)
            {
                TaskDialog.Show("Load Style Preset", "No presets found.");
                return Result.Succeeded;
            }

            var (tags, fromSel) = Organise.AnnotationColorHelper.GetTargetTags(uidoc);
            if (tags.Count == 0)
            {
                TaskDialog.Show("Load Style Preset", "No annotation tags found.");
                return Result.Succeeded;
            }

            // Pick preset — show all (up to 4)
            var presetList = presets.Values.ToList();
            TaskDialog dlg = new TaskDialog("Load Style Preset");
            dlg.MainInstruction = "Select preset to load and apply";
            dlg.MainContent = $"{tags.Count:N0} tags in scope" +
                (fromSel ? " (from selection)" : " (active view)");

            int maxLinks = Math.Min(presetList.Count, 4);
            for (int i = 0; i < maxLinks; i++)
            {
                dlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    $"{presetList[i].Name} ({presetList[i].Rules.Count} rules)",
                    presetList[i].Description);
            }
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int picked = -1;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: picked = 0; break;
                case TaskDialogResult.CommandLink2: picked = 1; break;
                case TaskDialogResult.CommandLink3: picked = 2; break;
                case TaskDialogResult.CommandLink4: picked = 3; break;
                default: return Result.Cancelled;
            }

            var preset = presetList[picked];
            var typeIndex = TagStyleEngine.BuildTagTypeIndex(doc);

            // Preview first
            var preview = TagStyleEngine.Preview(doc, tags, preset, typeIndex);

            TaskDialog confirmDlg = new TaskDialog("Confirm Apply");
            confirmDlg.MainInstruction = $"Apply '{preset.Name}'?";
            confirmDlg.MainContent = TagStyleEngine.FormatPreview(preview, preset.Name);
            confirmDlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

            if (confirmDlg.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Apply
            int changed = 0;
            using (Transaction tx = new Transaction(doc, "STING Load Tag Style Preset"))
            {
                tx.Start();
                foreach (var tag in tags)
                {
                    try
                    {
                        Element host = TagStyleEngine.GetTagHost(tag, doc);
                        if (host == null) continue;

                        string targetTypeName = TagStyleEngine.ResolveTagType(doc, host, preset);
                        if (!typeIndex.TryGetValue(targetTypeName, out ElementId targetTypeId))
                            continue;

                        if (tag.GetTypeId() != targetTypeId && tag.IsValidType(targetTypeId))
                        {
                            tag.ChangeTypeId(targetTypeId);
                            changed++;
                        }
                    }
                    catch { }
                }
                tx.Commit();
            }

            TaskDialog.Show("Load Style Preset",
                $"Applied '{preset.Name}': {changed:N0} tags changed.");
            return Result.Succeeded;
        }
    }
}
