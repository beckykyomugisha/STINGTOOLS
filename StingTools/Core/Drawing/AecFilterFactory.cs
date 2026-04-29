// StingTools — AEC/FM Filter Factory
//
// Converts an AecFilterDefinition into a live ParameterFilterElement.
// Walks the rule tree, resolves parameter ids (built-in / shared / phase /
// workset / level), and emits ElementParameterFilter + LogicalAndFilter /
// LogicalOrFilter trees per the Revit API constraints documented in the
// 2025 SDK View Filters page.
//
// Constraints enforced:
//   - All categories must be in ParameterFilterUtilities.GetAllFilterableCategories.
//   - Inside an ElementParameterFilter, FilterCategoryRule must be the only
//     rule (or wrapped in LogicalOrFilter alongside one other) — we don't
//     emit FilterCategoryRule today, so this is satisfied by construction.
//   - String comparison case-sensitivity is no-op since Revit 2022; we
//     pass false to be explicit.
//   - Numeric epsilon defaults to 1e-9 ("exact") for double rules.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class FilterFactoryResult
    {
        public ParameterFilterElement Filter { get; set; }
        public bool Created { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public string Error { get; set; }
        public bool Ok => Filter != null && string.IsNullOrEmpty(Error);
    }

    public static class AecFilterFactory
    {
        private const double DefaultEpsilon = 1e-9;

        /// <summary>
        /// Find or create a ParameterFilterElement matching <paramref name="def"/>.
        /// Idempotent — returns the existing filter when one already exists with
        /// the same name. Caller owns the active Transaction.
        /// </summary>
        public static FilterFactoryResult FindOrCreate(Document doc, AecFilterDefinition def)
        {
            var r = new FilterFactoryResult();
            if (doc == null || def == null || string.IsNullOrWhiteSpace(def.Name))
            { r.Error = "definition or document is null/empty"; return r; }

            // Existing match by name?
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => string.Equals(f.Name, def.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { r.Filter = existing; r.Created = false; return r; }

            // Resolve category ids (skip categories not present in this Revit version).
            var catIds = ResolveCategories(doc, def.Categories, r.Warnings);
            if (catIds.Count == 0) { r.Error = $"No filterable categories resolved for '{def.Name}'."; return r; }

            // Validate categories are filterable.
            var allFilterable = ParameterFilterUtilities.GetAllFilterableCategories();
            catIds = catIds.Where(id => allFilterable.Contains(id)).ToList();
            if (catIds.Count == 0) { r.Error = $"All requested categories are non-filterable for '{def.Name}'."; return r; }

            // Build the rule tree.
            ElementFilter elementFilter = null;
            try
            {
                elementFilter = BuildFilter(doc, def.Rule, catIds, r.Warnings);
            }
            catch (Exception ex)
            {
                r.Error = $"Rule build failed for '{def.Name}': {ex.Message}";
                return r;
            }
            if (elementFilter == null)
            {
                r.Error = $"Rule build returned null for '{def.Name}'.";
                return r;
            }

            try
            {
                r.Filter = ParameterFilterElement.Create(doc, def.Name, catIds, elementFilter);
                r.Created = true;
            }
            catch (Exception ex)
            {
                r.Error = $"ParameterFilterElement.Create failed for '{def.Name}': {ex.Message}";
            }
            return r;
        }

        // ── Rule-tree → ElementFilter ───────────────────────────────────

        private static ElementFilter BuildFilter(Document doc, AecFilterRule node,
            ICollection<ElementId> catIds, List<string> warnings)
        {
            if (node == null) return null;

            if (node.IsCompound)
            {
                var children = node.Rules
                    .Select(r => BuildFilter(doc, r, catIds, warnings))
                    .Where(f => f != null)
                    .ToList();
                if (children.Count == 0) return null;
                if (children.Count == 1) return children[0];

                if (string.Equals(node.Logic, "or", StringComparison.OrdinalIgnoreCase))
                    return new LogicalOrFilter(children);
                return new LogicalAndFilter(children);
            }

            if (node.IsLeaf)
            {
                var rule = BuildLeafRule(doc, node, catIds, warnings);
                return rule == null ? null : new ElementParameterFilter(rule);
            }
            return null;
        }

        private static FilterRule BuildLeafRule(Document doc, AecFilterRule node,
            ICollection<ElementId> catIds, List<string> warnings)
        {
            var kind  = (node.Kind ?? "builtin").Trim().ToLowerInvariant();
            var op    = (node.Op ?? "equals").Trim();
            var value = node.Value ?? string.Empty;

            ElementId paramId = ResolveParamId(doc, node.Param, kind, warnings);
            if (paramId == null || paramId == ElementId.InvalidElementId)
            {
                warnings?.Add($"Could not resolve parameter '{node.Param}' (kind={kind}).");
                return null;
            }

            // Resolve operator presence first (covers all storage types).
            switch (op.ToLowerInvariant())
            {
                case "hasvalue":   return ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId);
                case "hasnovalue": return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
            }

            // Phase / level rules compare ElementId.
            if (kind == "phase" || kind == "level")
            {
                var resolvedValueId = ResolveValueElementId(doc, kind, value);
                if (resolvedValueId == null || resolvedValueId == ElementId.InvalidElementId)
                {
                    warnings?.Add($"Could not resolve {kind} '{value}'.");
                    return null;
                }
                return BuildElementIdRule(paramId, op, resolvedValueId, warnings);
            }

            // Workset rule — ELEM_PARTITION_PARAM is integer storage; resolve
            // workset name → its int id.
            if (kind == "workset")
            {
                if (!doc.IsWorkshared)
                {
                    warnings?.Add($"Workset rule '{value}' but document not workshared — skipped.");
                    return null;
                }
                var ws = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .FirstOrDefault(w => string.Equals(w.Name, value, StringComparison.OrdinalIgnoreCase));
                if (ws == null) { warnings?.Add($"Workset '{value}' not found."); return null; }
                return BuildIntRule(paramId, op, ws.Id.IntegerValue, warnings);
            }

            // Storage type — explicit hint, then sniff the parameter.
            var typeHint = (node.Type ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(typeHint))
                typeHint = SniffStorageType(doc, paramId);

            try
            {
                switch (typeHint)
                {
                    case "int":
                    case "integer":
                    case "yesno":
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                            iv = 0;
                        return BuildIntRule(paramId, op, iv, warnings);
                    }
                    case "double":
                    case "number":
                    case "length":
                    {
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                            dv = 0;
                        return BuildDoubleRule(paramId, op, dv, warnings);
                    }
                    case "elementid":
                    {
                        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idVal))
                        {
                            warnings?.Add($"ElementId rule needs numeric value, got '{value}'.");
                            return null;
                        }
                        return BuildElementIdRule(paramId, op, new ElementId(idVal), warnings);
                    }
                    default:
                        return BuildStringRule(paramId, op, value, warnings);
                }
            }
            catch (Exception ex)
            {
                warnings?.Add($"Rule build for param '{node.Param}' op '{op}' failed: {ex.Message}");
                return null;
            }
        }

        private static FilterRule BuildStringRule(ElementId pid, string op, string val, List<string> warnings)
        {
            switch (op.ToLowerInvariant())
            {
                case "equals":         return ParameterFilterRuleFactory.CreateEqualsRule(pid, val);
                case "notequals":      return ParameterFilterRuleFactory.CreateNotEqualsRule(pid, val);
                case "contains":       return ParameterFilterRuleFactory.CreateContainsRule(pid, val);
                case "notcontains":    return ParameterFilterRuleFactory.CreateNotContainsRule(pid, val);
                case "beginswith":     return ParameterFilterRuleFactory.CreateBeginsWithRule(pid, val);
                case "notbeginswith":  return ParameterFilterRuleFactory.CreateNotBeginsWithRule(pid, val);
                case "endswith":       return ParameterFilterRuleFactory.CreateEndsWithRule(pid, val);
                case "notendswith":    return ParameterFilterRuleFactory.CreateNotEndsWithRule(pid, val);
                case "greater":
                case "greaterorequal":
                case "less":
                case "lessorequal":
                    warnings?.Add($"Numeric op '{op}' not valid on string parameter — falling back to equals.");
                    return ParameterFilterRuleFactory.CreateEqualsRule(pid, val);
                default:
                    warnings?.Add($"Unknown string op '{op}'.");
                    return null;
            }
        }

        private static FilterRule BuildIntRule(ElementId pid, string op, int val, List<string> warnings)
        {
            switch (op.ToLowerInvariant())
            {
                case "equals":         return ParameterFilterRuleFactory.CreateEqualsRule(pid, val);
                case "notequals":      return ParameterFilterRuleFactory.CreateNotEqualsRule(pid, val);
                case "greater":        return ParameterFilterRuleFactory.CreateGreaterRule(pid, val);
                case "greaterorequal": return ParameterFilterRuleFactory.CreateGreaterOrEqualRule(pid, val);
                case "less":           return ParameterFilterRuleFactory.CreateLessRule(pid, val);
                case "lessorequal":    return ParameterFilterRuleFactory.CreateLessOrEqualRule(pid, val);
                default:
                    warnings?.Add($"Unknown int op '{op}'.");
                    return null;
            }
        }

        private static FilterRule BuildDoubleRule(ElementId pid, string op, double val, List<string> warnings)
        {
            switch (op.ToLowerInvariant())
            {
                case "equals":         return ParameterFilterRuleFactory.CreateEqualsRule(pid, val, DefaultEpsilon);
                case "notequals":      return ParameterFilterRuleFactory.CreateNotEqualsRule(pid, val, DefaultEpsilon);
                case "greater":        return ParameterFilterRuleFactory.CreateGreaterRule(pid, val, DefaultEpsilon);
                case "greaterorequal": return ParameterFilterRuleFactory.CreateGreaterOrEqualRule(pid, val, DefaultEpsilon);
                case "less":           return ParameterFilterRuleFactory.CreateLessRule(pid, val, DefaultEpsilon);
                case "lessorequal":    return ParameterFilterRuleFactory.CreateLessOrEqualRule(pid, val, DefaultEpsilon);
                default:
                    warnings?.Add($"Unknown double op '{op}'.");
                    return null;
            }
        }

        private static FilterRule BuildElementIdRule(ElementId pid, string op, ElementId valId, List<string> warnings)
        {
            switch (op.ToLowerInvariant())
            {
                case "equals":         return ParameterFilterRuleFactory.CreateEqualsRule(pid, valId);
                case "notequals":      return ParameterFilterRuleFactory.CreateNotEqualsRule(pid, valId);
                case "greater":        return ParameterFilterRuleFactory.CreateGreaterRule(pid, valId);
                case "greaterorequal": return ParameterFilterRuleFactory.CreateGreaterOrEqualRule(pid, valId);
                case "less":           return ParameterFilterRuleFactory.CreateLessRule(pid, valId);
                case "lessorequal":    return ParameterFilterRuleFactory.CreateLessOrEqualRule(pid, valId);
                default:
                    warnings?.Add($"Unknown elementId op '{op}'.");
                    return null;
            }
        }

        // ── Param resolvers ─────────────────────────────────────────────

        private static ElementId ResolveParamId(Document doc, string paramName, string kind, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return ElementId.InvalidElementId;

            // Workset rule uses ELEM_PARTITION_PARAM regardless of the param string.
            if (kind == "workset")
                return new ElementId((long)BuiltInParameter.ELEM_PARTITION_PARAM);

            // Phase Created / Demolished from the param name itself.
            if (kind == "phase")
            {
                var p = paramName.ToUpperInvariant();
                if (p.Contains("DEMOLISH"))
                    return new ElementId((long)BuiltInParameter.PHASE_DEMOLISHED);
                return new ElementId((long)BuiltInParameter.PHASE_CREATED);
            }

            if (kind == "level")
                return new ElementId((long)BuiltInParameter.LEVEL_PARAM);

            // Shared parameter — look up by name first, then by GUID if the
            // string is a parseable Guid.
            if (kind == "shared")
            {
                if (Guid.TryParse(paramName, out var g))
                {
                    var sp = SharedParameterElement.Lookup(doc, g);
                    if (sp != null) return sp.Id;
                }
                // Resolve from StingTools' ParamRegistry GUID map.
                try
                {
                    var allGuids = StingTools.Core.ParamRegistry.AllParamGuids;
                    if (allGuids != null && allGuids.TryGetValue(paramName, out var sg))
                    {
                        var sp = SharedParameterElement.Lookup(doc, sg);
                        if (sp != null) return sp.Id;
                    }
                }
                catch { /* registry not loaded — fall through */ }

                // Last resort — scan project shared parameters by name.
                var byName = new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>()
                    .FirstOrDefault(s => string.Equals(s.Name, paramName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName.Id;

                warnings?.Add($"Shared parameter '{paramName}' not bound — filter skipped.");
                return ElementId.InvalidElementId;
            }

            // Built-in parameter — parse enum.
            if (Enum.TryParse<BuiltInParameter>(paramName, true, out var bip))
                return new ElementId((long)bip);

            // Fallback: also try shared lookup for legacy "kind not stated" cases.
            try
            {
                var allGuids = StingTools.Core.ParamRegistry.AllParamGuids;
                if (allGuids != null && allGuids.TryGetValue(paramName, out var sg))
                {
                    var sp = SharedParameterElement.Lookup(doc, sg);
                    if (sp != null) return sp.Id;
                }
            }
            catch { }

            warnings?.Add($"BuiltInParameter '{paramName}' not recognised.");
            return ElementId.InvalidElementId;
        }

        private static ElementId ResolveValueElementId(Document doc, string kind, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ElementId.InvalidElementId;
            switch (kind)
            {
                case "phase":
                {
                    var phase = new FilteredElementCollector(doc)
                        .OfClass(typeof(Phase))
                        .Cast<Phase>()
                        .FirstOrDefault(p => string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase));
                    return phase?.Id ?? ElementId.InvalidElementId;
                }
                case "level":
                {
                    var lvl = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault(l => string.Equals(l.Name, value, StringComparison.OrdinalIgnoreCase));
                    return lvl?.Id ?? ElementId.InvalidElementId;
                }
            }
            return ElementId.InvalidElementId;
        }

        private static List<ElementId> ResolveCategories(Document doc, IEnumerable<string> names, List<string> warnings)
        {
            var ids = new List<ElementId>();
            if (names == null) return ids;
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                ElementId id = ElementId.InvalidElementId;
                try
                {
                    if (Enum.TryParse<BuiltInCategory>(n, true, out var bic))
                    {
                        var cat = Category.GetCategory(doc, bic);
                        if (cat != null) id = cat.Id;
                    }
                    if (id == ElementId.InvalidElementId)
                    {
                        foreach (Category c in doc.Settings.Categories)
                        {
                            if (string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase))
                            { id = c.Id; break; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    warnings?.Add($"Category '{n}' lookup failed: {ex.Message}");
                }
                if (id != ElementId.InvalidElementId) ids.Add(id);
                else warnings?.Add($"Category '{n}' not present in this Revit version — skipped.");
            }
            return ids;
        }

        private static string SniffStorageType(Document doc, ElementId paramId)
        {
            // Built-in params are negative; shared params are positive Element ids.
            try
            {
                long raw = paramId.Value; // Revit 2024+ ElementId is Int64
                if (raw < 0)
                {
                    var bip = (BuiltInParameter)raw;
                    // Fall back to a common-sense lookup table for the parameters
                    // most used in the corporate filter library.
                    switch (bip)
                    {
                        case BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM:
                        case BuiltInParameter.FUNCTION_PARAM:
                        case BuiltInParameter.STRUCTURAL_MATERIAL_TYPE:
                        case BuiltInParameter.ELEM_PARTITION_PARAM:
                        case BuiltInParameter.INSTANCE_STRUCT_USAGE_PARAM:
                            return "int";
                        case BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS:
                        case BuiltInParameter.REBAR_BAR_DIAMETER:
                        case BuiltInParameter.RBS_ELEC_VOLTAGE:
                            return "double";
                        case BuiltInParameter.PHASE_CREATED:
                        case BuiltInParameter.PHASE_DEMOLISHED:
                            return "elementId";
                    }
                    return "string";
                }

                // Shared parameter — read its definition's data type
                // (Revit 2024+ ForgeTypeId; older API ParameterType is gone).
                var sp = doc.GetElement(paramId) as SharedParameterElement;
                if (sp != null)
                {
                    var def = sp.GetDefinition();
                    var ft = def?.GetDataType();
                    if (ft != null)
                    {
                        if (SpecTypeId.Boolean.YesNo.Equals(ft)) return "yesno";
                        if (SpecTypeId.Int.Integer.Equals(ft))   return "int";
                        if (SpecTypeId.Number.Equals(ft))        return "double";
                        if (UnitUtils.IsMeasurableSpec(ft))      return "double";
                        if (SpecTypeId.String.Text.Equals(ft) ||
                            SpecTypeId.String.MultilineText.Equals(ft) ||
                            SpecTypeId.String.Url.Equals(ft))    return "string";
                    }
                }
            }
            catch { }
            return "string";
        }
    }
}
