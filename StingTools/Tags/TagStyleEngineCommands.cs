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

    // ════════════════════════════════════════════════════════════════════
    //  Parameter-Driven Style Engine
    //
    //  Controls tag appearance via 2 shared INTEGER parameters on host
    //  elements + OverrideGraphicSettings for color. This decouples
    //  style from the tag family type — one tag type handles all
    //  128 visual combinations.
    //
    //  Parameters:
    //    TAG_STYLE_SIZE_INT    1=1.5mm  2=2mm  3=2.5mm  4=3.5mm
    //    TAG_STYLE_WEIGHT_INT  1=NOM  2=BOLD  3=ITALIC  4=BOLDITALIC
    //    Color                 via view.SetElementOverrides (per-view)
    //
    //  In the tag family: 16 labels (4 sizes × 4 weights) controlled
    //  by calculated visibility formulas referencing these integer params.
    //  Color is applied via OverrideGraphicSettings so it can differ
    //  per view without touching element data.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Engine for parameter-driven tag styling. Resolves size, weight,
    /// and color from element conditions without changing the tag family type.
    /// </summary>
    internal static class ParamDrivenStyleEngine
    {
        // ── Size / weight enums ──────────────────────────────────────────

        public static readonly Dictionary<int, string> SizeLabels = new Dictionary<int, string>
        {
            { 1, "1.5mm" }, { 2, "2mm" }, { 3, "2.5mm" }, { 4, "3.5mm" }
        };

        public static readonly Dictionary<int, string> WeightLabels = new Dictionary<int, string>
        {
            { 1, "Normal" }, { 2, "Bold" }, { 3, "Italic" }, { 4, "BoldItalic" }
        };

        // ── Named color constants ────────────────────────────────────────

        public static readonly Dictionary<string, Color> NamedColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            // Core palette (1-8)
            { "BLACK",      new Color(0, 0, 0) },
            { "BLUE",       new Color(0, 80, 200) },
            { "GREEN",      new Color(0, 140, 0) },
            { "RED",        new Color(200, 0, 0) },
            { "ORANGE",     new Color(220, 120, 0) },
            { "PURPLE",     new Color(128, 0, 180) },
            { "GREY",       new Color(128, 128, 128) },
            { "WHITE",      new Color(255, 255, 255) },
            // Extended palette (9-16)
            { "CYAN",       new Color(0, 160, 190) },
            { "MAGENTA",    new Color(180, 0, 120) },
            { "TEAL",       new Color(0, 128, 128) },
            { "BROWN",      new Color(140, 80, 20) },
            { "DARK_GREEN", new Color(0, 100, 50) },
            { "DARK_BLUE",  new Color(0, 40, 130) },
            { "CORAL",      new Color(210, 90, 70) },
            { "GOLD",       new Color(180, 150, 0) },
            // Presentation palette (17-20)
            { "CHARCOAL",   new Color(50, 50, 50) },
            { "SLATE",      new Color(80, 100, 120) },
            { "OLIVE",      new Color(100, 110, 50) },
            { "NAVY",       new Color(0, 20, 80) },
        };

        public static readonly Dictionary<int, string> ColorIndex = new Dictionary<int, string>
        {
            { 1, "BLACK" },   { 2, "BLUE" },      { 3, "GREEN" },     { 4, "RED" },
            { 5, "ORANGE" },  { 6, "PURPLE" },     { 7, "GREY" },      { 8, "WHITE" },
            { 9, "CYAN" },    { 10, "MAGENTA" },   { 11, "TEAL" },     { 12, "BROWN" },
            { 13, "DARK_GREEN" }, { 14, "DARK_BLUE" }, { 15, "CORAL" }, { 16, "GOLD" },
            { 17, "CHARCOAL" }, { 18, "SLATE" },   { 19, "OLIVE" },    { 20, "NAVY" },
        };

        // ── Style presets (condition → size/weight/colorIndex) ───────────

        public class ParamStyleRule
        {
            public Dictionary<string, string> Conditions { get; set; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int Size { get; set; } = 2;
            public int Weight { get; set; } = 1;
            public int ColorIdx { get; set; } = 1;
        }

        public class ParamStylePreset
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public int DefaultSize { get; set; } = 2;
            public int DefaultWeight { get; set; } = 1;
            public int DefaultColorIdx { get; set; } = 1;
            public List<ParamStyleRule> Rules { get; set; } = new List<ParamStyleRule>();
        }

        // ── Built-in presets ─────────────────────────────────────────────

        /// <summary>All built-in presets. Each preset encodes a design intent
        /// (discipline separation, QA checking, presentation, etc.).</summary>
        public static List<ParamStylePreset> GetBuiltInPresets()
        {
            return new List<ParamStylePreset>
            {
                BuildDisciplinePreset(),
                BuildSystemPreset(),
                BuildStatusPhasePreset(),
                BuildZonePreset(),
                BuildQACheckPreset(),
                BuildPresentationPreset(),
                BuildPrintPreset(),
                BuildLargeFormatPreset(),
                BuildCoordinationPreset(),
                BuildMonochromePreset(),
            };
        }

        // ── Discipline — visual discipline separation on coordinated drawings ──

        private static ParamStylePreset BuildDisciplinePreset()
        {
            var p = new ParamStylePreset
            {
                Name = "Discipline",
                Description = "Color by discipline — instant visual separation on coordinated drawings",
                DefaultSize = 2,
                DefaultWeight = 1,
                DefaultColorIdx = 17 // Charcoal for unknown
            };

            p.Rules.Add(MakeRule(ParamRegistry.DISC, "M",  2, 1, 2));   // Blue - Mechanical
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "E",  2, 1, 16));  // Gold - Electrical
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "P",  2, 1, 3));   // Green - Plumbing
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "A",  2, 1, 18));  // Slate - Architecture
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "S",  2, 1, 4));   // Red - Structural
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "FP", 2, 2, 15));  // Coral bold - Fire Protection
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "LV", 2, 1, 6));   // Purple - Low Voltage
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "G",  2, 1, 7));   // Grey - General
            return p;
        }

        // ── System — MEP system identification for coordination ──

        private static ParamStylePreset BuildSystemPreset()
        {
            var p = new ParamStylePreset
            {
                Name = "System",
                Description = "Color by MEP system (HVAC=Blue DCW=Cyan SAN=Brown) — for coordination views",
                DefaultSize = 2,
                DefaultWeight = 1,
                DefaultColorIdx = 7 // Grey for unknown
            };

            p.Rules.Add(MakeRule(ParamRegistry.SYS, "HVAC", 2, 1, 2));   // Blue
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "DCW",  2, 1, 9));   // Cyan
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "DHW",  2, 1, 15));  // Coral
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "HWS",  2, 1, 4));   // Red
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "SAN",  2, 1, 12));  // Brown
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "RWD",  2, 1, 11));  // Teal
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "GAS",  2, 2, 5));   // Orange bold
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "FP",   2, 2, 15));  // Coral bold
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "LV",   2, 1, 6));   // Purple
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "FLS",  2, 2, 4));   // Red bold
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "COM",  2, 1, 10));  // Magenta
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "ICT",  2, 1, 14));  // Dark Blue
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "NCL",  2, 1, 19));  // Olive
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "SEC",  2, 1, 20));  // Navy
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "ARC",  2, 1, 18));  // Slate
            p.Rules.Add(MakeRule(ParamRegistry.SYS, "STR",  2, 2, 12));  // Brown bold
            return p;
        }

        // ── Status/Phase — phasing and renovation project views ──

        private static ParamStylePreset BuildStatusPhasePreset()
        {
            var p = new ParamStylePreset
            {
                Name = "Status / Phase",
                Description = "Color by phase status — NEW=green, EXISTING=grey, DEMOLISHED=red, TEMPORARY=orange",
                DefaultSize = 2,
                DefaultWeight = 1,
                DefaultColorIdx = 7
            };

            p.Rules.Add(MakeRule("ASS_STATUS_TXT", "NEW",        2, 2, 3));   // Green bold
            p.Rules.Add(MakeRule("ASS_STATUS_TXT", "EXISTING",   2, 1, 7));   // Grey normal
            p.Rules.Add(MakeRule("ASS_STATUS_TXT", "DEMOLISHED", 2, 3, 4));   // Red italic
            p.Rules.Add(MakeRule("ASS_STATUS_TXT", "TEMPORARY",  2, 3, 5));   // Orange italic
            return p;
        }

        // ── Zone — spatial coordination views ──

        private static ParamStylePreset BuildZonePreset()
        {
            var p = new ParamStylePreset
            {
                Name = "Zone",
                Description = "Color by zone code — spatial isolation for coordination and clash review",
                DefaultSize = 2,
                DefaultWeight = 1,
                DefaultColorIdx = 7
            };

            p.Rules.Add(MakeRule(ParamRegistry.ZONE, "Z01", 2, 1, 2));   // Blue
            p.Rules.Add(MakeRule(ParamRegistry.ZONE, "Z02", 2, 1, 3));   // Green
            p.Rules.Add(MakeRule(ParamRegistry.ZONE, "Z03", 2, 1, 5));   // Orange
            p.Rules.Add(MakeRule(ParamRegistry.ZONE, "Z04", 2, 1, 6));   // Purple
            p.Rules.Add(MakeRule(ParamRegistry.ZONE, "ZZ",  2, 3, 9));   // Cyan italic
            p.Rules.Add(MakeRule(ParamRegistry.ZONE, "XX",  2, 3, 7));   // Grey italic (unassigned)
            return p;
        }

        // ── QA Check — compliance auditing with visual severity ──

        private static ParamStylePreset BuildQACheckPreset()
        {
            var p = new ParamStylePreset
            {
                Name = "QA Check",
                Description = "Compliance audit — complete=green, partial=bold orange, missing=3.5mm bold red",
                DefaultSize = 4,
                DefaultWeight = 2,
                DefaultColorIdx = 4 // Big bold red for untagged
            };

            p.Rules.Add(new ParamStyleRule
            {
                Conditions = { { "_tag_complete", "true" } },
                Size = 2, Weight = 1, ColorIdx = 13  // Dark green
            });
            p.Rules.Add(new ParamStyleRule
            {
                Conditions = { { "_tag_complete", "partial" } },
                Size = 3, Weight = 2, ColorIdx = 5  // Bold orange 2.5mm
            });
            // Default: 3.5mm bold red for missing
            return p;
        }

        // ── Presentation — clean client-facing views ──

        private static ParamStylePreset BuildPresentationPreset()
        {
            return new ParamStylePreset
            {
                Name = "Presentation",
                Description = "Clean uniform 2.5mm charcoal — client-facing presentation views and exports",
                DefaultSize = 3,
                DefaultWeight = 1,
                DefaultColorIdx = 17 // Charcoal — softer than black
            };
        }

        // ── Print — monochrome for standard document printing ──

        private static ParamStylePreset BuildPrintPreset()
        {
            return new ParamStylePreset
            {
                Name = "Print",
                Description = "Monochrome 2mm black — print-ready documentation (1:100 scale)",
                DefaultSize = 2,
                DefaultWeight = 1,
                DefaultColorIdx = 1 // Pure black
            };
        }

        // ── Large Format — A0/A1 sheets at 1:50 ──

        private static ParamStylePreset BuildLargeFormatPreset()
        {
            var p = new ParamStylePreset
            {
                Name = "Large Format",
                Description = "3.5mm discipline colors — detailed A0/A1 drawings at 1:50 scale",
                DefaultSize = 4,
                DefaultWeight = 1,
                DefaultColorIdx = 17
            };

            p.Rules.Add(MakeRule(ParamRegistry.DISC, "M",  4, 1, 2));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "E",  4, 1, 16));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "P",  4, 1, 3));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "A",  4, 1, 18));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "S",  4, 1, 4));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "FP", 4, 2, 15));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "LV", 4, 1, 6));
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "G",  4, 1, 7));
            return p;
        }

        // ── Coordination — bold discipline colors for multi-trade review ──

        private static ParamStylePreset BuildCoordinationPreset()
        {
            var p = new ParamStylePreset
            {
                Name = "Coordination",
                Description = "Bold 2.5mm discipline colors — for multi-trade review and clash meetings",
                DefaultSize = 3,
                DefaultWeight = 2,
                DefaultColorIdx = 17
            };

            p.Rules.Add(MakeRule(ParamRegistry.DISC, "M",  3, 2, 2));   // Bold blue
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "E",  3, 2, 16));  // Bold gold
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "P",  3, 2, 3));   // Bold green
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "A",  3, 2, 18));  // Bold slate
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "S",  3, 2, 4));   // Bold red
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "FP", 3, 2, 15));  // Bold coral
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "LV", 3, 2, 6));   // Bold purple
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "G",  3, 2, 7));   // Bold grey
            return p;
        }

        // ── Monochrome — greyscale for review/markup ──

        private static ParamStylePreset BuildMonochromePreset()
        {
            var p = new ParamStylePreset
            {
                Name = "Monochrome",
                Description = "Greyscale gradient by discipline — for halftone underlay and markup views",
                DefaultSize = 2,
                DefaultWeight = 1,
                DefaultColorIdx = 7
            };

            p.Rules.Add(MakeRule(ParamRegistry.DISC, "M",  2, 1, 1));   // Black
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "E",  2, 1, 17));  // Charcoal
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "P",  2, 1, 18));  // Slate
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "A",  2, 1, 7));   // Grey
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "S",  2, 2, 1));   // Black bold
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "FP", 2, 2, 17));  // Charcoal bold
            p.Rules.Add(MakeRule(ParamRegistry.DISC, "LV", 2, 1, 18));  // Slate
            return p;
        }

        private static ParamStyleRule MakeRule(string param, string value, int size, int weight, int color)
        {
            var r = new ParamStyleRule { Size = size, Weight = weight, ColorIdx = color };
            r.Conditions[param] = value;
            return r;
        }

        // ── Rule evaluation ──────────────────────────────────────────────

        /// <summary>
        /// Resolve the style tuple (size, weight, colorIdx) for a host element.
        /// Evaluates rules top-down; first match wins.
        /// </summary>
        public static (int size, int weight, int colorIdx) ResolveStyle(
            Document doc, Element host, ParamStylePreset preset)
        {
            if (host == null || preset == null)
                return (preset?.DefaultSize ?? 2, preset?.DefaultWeight ?? 1, preset?.DefaultColorIdx ?? 1);

            foreach (var rule in preset.Rules)
            {
                if (EvaluateConditions(doc, host, rule.Conditions))
                    return (rule.Size, rule.Weight, rule.ColorIdx);
            }

            return (preset.DefaultSize, preset.DefaultWeight, preset.DefaultColorIdx);
        }

        private static bool EvaluateConditions(Document doc, Element host,
            Dictionary<string, string> conditions)
        {
            foreach (var kvp in conditions)
            {
                string condKey = kvp.Key;
                string expected = kvp.Value;

                if (string.Equals(condKey, "_tag_complete", StringComparison.OrdinalIgnoreCase))
                {
                    string tag1 = ParameterHelpers.GetString(host, ParamRegistry.TAG1);
                    bool complete = TagConfig.TagIsComplete(tag1);
                    bool partial = !complete && !string.IsNullOrWhiteSpace(tag1);

                    if (expected == "true" && !complete) return false;
                    if (expected == "partial" && !partial) return false;
                    if (expected == "false" && (complete || partial)) return false;
                }
                else
                {
                    string actual = ParameterHelpers.GetString(host, condKey);
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        }

        // ── Apply to elements ────────────────────────────────────────────

        /// <summary>
        /// Write TAG_STYLE_SIZE_INT and TAG_STYLE_WEIGHT_INT to element.
        /// Returns true if at least one value was written.
        /// </summary>
        public static bool WriteStyleParams(Element el, int size, int weight)
        {
            bool a = ParameterHelpers.SetInt(el, ParamRegistry.STYLE_SIZE, size);
            bool b = ParameterHelpers.SetInt(el, ParamRegistry.STYLE_WEIGHT, weight);
            return a || b;
        }

        /// <summary>
        /// Apply color override to a tag annotation in the view.
        /// Uses projection line color and surface foreground for full coverage.
        /// </summary>
        public static void ApplyColorOverride(Document doc, View view,
            ElementId tagId, int colorIdx)
        {
            if (!ColorIndex.TryGetValue(colorIdx, out string colorName))
                colorName = "BLACK";
            if (!NamedColors.TryGetValue(colorName, out Color color))
                color = new Color(0, 0, 0);

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);

            // Try to set surface pattern color if solid fill is available
            FillPatternElement solidFill = ParameterHelpers.GetSolidFillPattern(doc);
            if (solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(color);
            }

            view.SetElementOverrides(tagId, ogs);
        }

        /// <summary>
        /// Clear color override from a tag in the view.
        /// </summary>
        public static void ClearColorOverride(View view, ElementId tagId)
        {
            view.SetElementOverrides(tagId, new OverrideGraphicSettings());
        }

        // ── Preset picker (2-step: category → preset) ─────────────────────

        /// <summary>
        /// Two-step preset picker for commands. Groups presets into categories
        /// so all 10+ presets are accessible via TaskDialog's 4-link limit.
        /// </summary>
        public static ParamStylePreset PickPreset(List<ParamStylePreset> presets)
        {
            // Group: Design = Discipline/System/Status/Zone
            //        QA     = QA Check
            //        Output = Presentation/Print/Large Format/Coordination/Monochrome
            TaskDialog step1 = new TaskDialog("Select Style Category");
            step1.MainInstruction = "What type of styling?";
            step1.MainContent = "No tag type switching — styles controlled by element parameters + view color overrides.";
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Design Intent", "Discipline, System, Status/Phase, Zone — indicate meaning in the design");
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Quality Assurance", "QA Check — highlight completeness and compliance issues");
            step1.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Output / Presentation", "Presentation, Print, Large Format, Coordination, Monochrome");
            step1.CommonButtons = TaskDialogCommonButtons.Cancel;

            int cat;
            switch (step1.Show())
            {
                case TaskDialogResult.CommandLink1: cat = 0; break;
                case TaskDialogResult.CommandLink2: cat = 1; break;
                case TaskDialogResult.CommandLink3: cat = 2; break;
                default: return null;
            }

            List<ParamStylePreset> filtered;
            switch (cat)
            {
                case 0:
                    filtered = presets.Where(p =>
                        p.Name == "Discipline" || p.Name == "System" ||
                        p.Name == "Status / Phase" || p.Name == "Zone").ToList();
                    break;
                case 1:
                    filtered = presets.Where(p => p.Name == "QA Check").ToList();
                    break;
                default:
                    filtered = presets.Where(p =>
                        p.Name == "Presentation" || p.Name == "Print" ||
                        p.Name == "Large Format" || p.Name == "Coordination" ||
                        p.Name == "Monochrome").ToList();
                    break;
            }

            if (filtered.Count == 0) return null;
            if (filtered.Count == 1) return filtered[0];

            TaskDialog step2 = new TaskDialog("Select Preset");
            step2.MainInstruction = "Choose preset";
            int max = Math.Min(filtered.Count, 4);
            for (int i = 0; i < max; i++)
            {
                step2.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    filtered[i].Name, filtered[i].Description);
            }
            step2.CommonButtons = TaskDialogCommonButtons.Cancel;

            int picked;
            switch (step2.Show())
            {
                case TaskDialogResult.CommandLink1: picked = 0; break;
                case TaskDialogResult.CommandLink2: picked = 1; break;
                case TaskDialogResult.CommandLink3: picked = 2; break;
                case TaskDialogResult.CommandLink4: picked = 3; break;
                default: return null;
            }

            return filtered[picked];
        }

        // ── Report formatting ────────────────────────────────────────────

        public static string FormatStyleReport(
            int total, int paramsWritten, int colorsApplied, int skipped,
            Dictionary<string, int> distribution, string presetName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Preset: {presetName}");
            sb.AppendLine($"Elements processed: {total:N0}");
            sb.AppendLine($"Style params written: {paramsWritten:N0}");
            sb.AppendLine($"Color overrides applied: {colorsApplied:N0}");
            if (skipped > 0) sb.AppendLine($"Skipped (no host): {skipped}");
            if (distribution.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Style distribution:");
                foreach (var kvp in distribution.OrderByDescending(x => x.Value))
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");
            }
            return sb.ToString();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  6. ApplyParamDrivenStyles — write size/weight integers + color
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply parameter-driven styles to elements in the active view or selection.
    /// Writes TAG_STYLE_SIZE_INT and TAG_STYLE_WEIGHT_INT to host elements,
    /// then applies OverrideGraphicSettings color to their annotation tags.
    /// Unlike type-switching, this requires only 1 tag family type.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyParamDrivenStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Pick preset category first, then specific preset
            var presets = ParamDrivenStyleEngine.GetBuiltInPresets();
            var preset = PickPreset(presets);
            if (preset == null) return Result.Cancelled;

            // Get tags and their hosts
            var (tags, fromSel) = Organise.AnnotationColorHelper.GetTargetTags(uidoc);

            // Also collect elements directly (for writing params to untagged elements)
            var hostElements = new Dictionary<long, Element>();
            var tagsByHost = new Dictionary<long, List<IndependentTag>>();

            foreach (var tag in tags)
            {
                Element host = TagStyleEngine.GetTagHost(tag, doc);
                if (host == null) continue;
                long hid = host.Id.Value;
                hostElements[hid] = host;
                if (!tagsByHost.ContainsKey(hid))
                    tagsByHost[hid] = new List<IndependentTag>();
                tagsByHost[hid].Add(tag);
            }

            // If no tags but selection has elements, apply params directly
            if (hostElements.Count == 0 && !fromSel)
            {
                // Collect all taggable elements in view
                var collector = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();
                foreach (Element el in collector)
                {
                    if (el.Category == null) continue;
                    hostElements[el.Id.Value] = el;
                }
            }
            else if (hostElements.Count == 0)
            {
                // Try selection elements directly
                foreach (ElementId id in uidoc.Selection.GetElementIds())
                {
                    Element el = doc.GetElement(id);
                    if (el != null && el.Category != null && !(el is IndependentTag))
                        hostElements[el.Id.Value] = el;
                }
            }

            if (hostElements.Count == 0)
            {
                TaskDialog.Show("Apply Param-Driven Styles",
                    "No elements found. Select elements or ensure the view has taggable content.");
                return Result.Succeeded;
            }

            int paramsWritten = 0;
            int colorsApplied = 0;
            int skipped = 0;
            var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tx = new Transaction(doc, "STING Apply Param-Driven Styles"))
            {
                tx.Start();

                foreach (var kvp in hostElements)
                {
                    Element host = kvp.Value;
                    try
                    {
                        var (size, weight, colorIdx) = ParamDrivenStyleEngine.ResolveStyle(doc, host, preset);

                        // Write size + weight to element
                        if (ParamDrivenStyleEngine.WriteStyleParams(host, size, weight))
                            paramsWritten++;

                        // Apply color to annotation tags
                        if (tagsByHost.TryGetValue(kvp.Key, out var hostTags))
                        {
                            foreach (var tag in hostTags)
                            {
                                ParamDrivenStyleEngine.ApplyColorOverride(doc, view, tag.Id, colorIdx);
                                colorsApplied++;
                            }
                        }

                        // Track distribution
                        string sizeLabel = ParamDrivenStyleEngine.SizeLabels.TryGetValue(size, out string sl) ? sl : $"{size}";
                        string weightLabel = ParamDrivenStyleEngine.WeightLabels.TryGetValue(weight, out string wl) ? wl : $"{weight}";
                        string colorLabel = ParamDrivenStyleEngine.ColorIndex.TryGetValue(colorIdx, out string cl) ? cl : $"{colorIdx}";
                        string key = $"{sizeLabel} {weightLabel} {colorLabel}";
                        if (!distribution.ContainsKey(key)) distribution[key] = 0;
                        distribution[key]++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ParamDrivenStyle: skip element {host.Id} — {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Apply Param-Driven Styles",
                ParamDrivenStyleEngine.FormatStyleReport(
                    hostElements.Count, paramsWritten, colorsApplied, skipped,
                    distribution, preset.Name));
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  7. PreviewParamDrivenStyles — dry-run preview
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Preview parameter-driven style application without modifying any elements.
    /// Shows the distribution of size/weight/color assignments.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PreviewParamDrivenStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Pick preset
            var presets = ParamDrivenStyleEngine.GetBuiltInPresets();
            var preset = ParamDrivenStyleEngine.PickPreset(presets);
            if (preset == null) return Result.Cancelled;

            // Collect elements
            var viewElements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            int wouldChange = 0;

            foreach (var el in viewElements)
            {
                total++;
                var (size, weight, colorIdx) = ParamDrivenStyleEngine.ResolveStyle(doc, el, preset);

                int curSize = ParameterHelpers.GetInt(el, ParamRegistry.STYLE_SIZE);
                int curWeight = ParameterHelpers.GetInt(el, ParamRegistry.STYLE_WEIGHT);
                if (curSize != size || curWeight != weight)
                    wouldChange++;

                string sizeLabel = ParamDrivenStyleEngine.SizeLabels.TryGetValue(size, out string sl) ? sl : $"{size}";
                string weightLabel = ParamDrivenStyleEngine.WeightLabels.TryGetValue(weight, out string wl) ? wl : $"{weight}";
                string colorLabel = ParamDrivenStyleEngine.ColorIndex.TryGetValue(colorIdx, out string cl) ? cl : $"{colorIdx}";
                string key = $"{sizeLabel} {weightLabel} {colorLabel}";
                if (!distribution.ContainsKey(key)) distribution[key] = 0;
                distribution[key]++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Preset: {preset.Name}");
            sb.AppendLine($"Total elements: {total:N0}");
            sb.AppendLine($"Would change: {wouldChange:N0}");
            sb.AppendLine();
            sb.AppendLine("Projected distribution:");
            foreach (var kvp in distribution.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");

            TaskDialog.Show("Preview Param-Driven Styles", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  8. ClearParamDrivenStyles — reset params + remove color overrides
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reset TAG_STYLE_SIZE_INT and TAG_STYLE_WEIGHT_INT to 0 on all
    /// elements in view/selection and remove any color overrides from tags.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearParamDrivenStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Get tags for color clearing
            var (tags, fromSel) = Organise.AnnotationColorHelper.GetTargetTags(uidoc);

            // Get host elements for param clearing
            var hostIds = new HashSet<long>();
            foreach (var tag in tags)
            {
                Element host = TagStyleEngine.GetTagHost(tag, doc);
                if (host != null) hostIds.Add(host.Id.Value);
            }

            // Also include directly selected non-tag elements
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element el = doc.GetElement(id);
                if (el != null && !(el is IndependentTag))
                    hostIds.Add(id.Value);
            }

            // If nothing selected, clear entire view
            if (hostIds.Count == 0 && tags.Count == 0)
            {
                var collector = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();
                foreach (Element el in collector)
                {
                    if (el.Category != null)
                        hostIds.Add(el.Id.Value);
                }
                // Collect all tags in view
                tags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();
            }

            int paramsCleared = 0;
            int colorsCleared = 0;

            using (Transaction tx = new Transaction(doc, "STING Clear Param-Driven Styles"))
            {
                tx.Start();

                // Clear integer params on elements
                foreach (long id in hostIds)
                {
                    Element el = doc.GetElement(new ElementId(id));
                    if (el == null) continue;

                    bool a = ParameterHelpers.SetInt(el, ParamRegistry.STYLE_SIZE, 0);
                    bool b = ParameterHelpers.SetInt(el, ParamRegistry.STYLE_WEIGHT, 0);
                    if (a || b) paramsCleared++;
                }

                // Clear color overrides on tags
                foreach (var tag in tags)
                {
                    ParamDrivenStyleEngine.ClearColorOverride(view, tag.Id);
                    colorsCleared++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Clear Param-Driven Styles",
                $"Cleared {paramsCleared:N0} element style params\n" +
                $"Cleared {colorsCleared:N0} tag color overrides");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  9. BatchApplyParamDrivenStyles — project-wide application
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply parameter-driven styles to ALL elements in the entire project.
    /// Writes size/weight integers to every taggable element. Color overrides
    /// are only applied to tags in the active view (view-scoped by design).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchApplyParamDrivenStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Pick preset
            var presets = ParamDrivenStyleEngine.GetBuiltInPresets();
            var preset = ParamDrivenStyleEngine.PickPreset(presets);
            if (preset == null) return Result.Cancelled;

            // Confirm
            TaskDialog confirm = new TaskDialog("Confirm Batch Apply");
            confirm.MainInstruction = $"Apply '{preset.Name}' to entire project?";
            confirm.MainContent = "This will write TAG_STYLE_SIZE_INT and TAG_STYLE_WEIGHT_INT\n" +
                "to every taggable element in the model.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Collect all taggable elements
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            // Collect tags in active view for color
            var viewTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var tagsByHost = new Dictionary<long, List<IndependentTag>>();
            foreach (var tag in viewTags)
            {
                Element host = TagStyleEngine.GetTagHost(tag, doc);
                if (host == null) continue;
                long hid = host.Id.Value;
                if (!tagsByHost.ContainsKey(hid))
                    tagsByHost[hid] = new List<IndependentTag>();
                tagsByHost[hid].Add(tag);
            }

            int paramsWritten = 0;
            int colorsApplied = 0;
            var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tx = new Transaction(doc, "STING Batch Param-Driven Styles"))
            {
                tx.Start();

                foreach (var el in allElements)
                {
                    try
                    {
                        var (size, weight, colorIdx) = ParamDrivenStyleEngine.ResolveStyle(doc, el, preset);

                        if (ParamDrivenStyleEngine.WriteStyleParams(el, size, weight))
                            paramsWritten++;

                        // Color overrides for tags in active view
                        if (tagsByHost.TryGetValue(el.Id.Value, out var hostTags))
                        {
                            foreach (var tag in hostTags)
                            {
                                ParamDrivenStyleEngine.ApplyColorOverride(doc, view, tag.Id, colorIdx);
                                colorsApplied++;
                            }
                        }

                        string sizeLabel = ParamDrivenStyleEngine.SizeLabels.TryGetValue(size, out string sl) ? sl : $"{size}";
                        string weightLabel = ParamDrivenStyleEngine.WeightLabels.TryGetValue(weight, out string wl) ? wl : $"{weight}";
                        string colorLabel = ParamDrivenStyleEngine.ColorIndex.TryGetValue(colorIdx, out string cl) ? cl : $"{colorIdx}";
                        string key = $"{sizeLabel} {weightLabel} {colorLabel}";
                        if (!distribution.ContainsKey(key)) distribution[key] = 0;
                        distribution[key]++;
                    }
                    catch { }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Apply Param-Driven Styles",
                ParamDrivenStyleEngine.FormatStyleReport(
                    allElements.Count, paramsWritten, colorsApplied, 0,
                    distribution, preset.Name));
            return Result.Succeeded;
        }
    }
}
