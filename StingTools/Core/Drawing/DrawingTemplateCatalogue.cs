// StingTools — Drawing Template Manager
//
// DrawingTemplateCatalogue — the view templates the drawing types name, and
// how to create each one. Revit-free: StingTools.Tags.Tests compiles this
// file and checks that every viewTemplateName in the shipped
// STING_DRAWING_TYPES.json is one ViewTemplatesCommand will create.
//
// Why it exists: the drawing types named templates nothing created —
// "STING - Power Layout", "STING - HVAC Duct", "STING - MEP Plan" and ~30
// more. ViewTemplatesCommand built a hand-written list that had drifted from
// the catalogue, so DrawingTypePresentation.Apply warned "not found" and fell
// back to a managed pack template on every sheet. The list is now read from
// the drawing types themselves, so a new type cannot name a template that is
// never made.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One view template a drawing type asks for.</summary>
    public sealed class DrawingTemplateSpec
    {
        public string Name { get; set; }

        /// <summary>
        /// The kind of view the template must be created from — a
        /// <see cref="DrawingViewKind"/> value. A template can only be applied
        /// to views of the type it was made from, so a schematic template has
        /// to come from a drafting view, not a plan.
        /// </summary>
        public string BaseKind { get; set; }

        /// <summary>VG scheme code ViewTemplatesCommand.ConfigureTemplateVG understands.</summary>
        public string VgCode { get; set; }

        /// <summary>
        /// Detail level every referencing type agrees on, or null when they
        /// disagree — the template then leaves detail level uncontrolled so each
        /// drawing type's own value stands.
        /// </summary>
        public string DetailLevel { get; set; }

        // No scale: the template always leaves View Scale uncontrolled. One
        // template serves types at different scales (the architectural plan
        // template is used at 1:50, 1:100 and 1:200), and a controlled scale
        // would overwrite the scale DrawingTypePresentation set a step earlier.

        public List<string> UsedBy { get; } = new List<string>();
    }

    public sealed class DrawingTemplatePlan
    {
        public List<DrawingTemplateSpec> Creatable { get; } = new List<DrawingTemplateSpec>();

        /// <summary>"name — reason" for templates that cannot be created.</summary>
        public List<string> NotCreatable { get; } = new List<string>();

        /// <summary>Managed "STING MANAGED - {pack} - {ViewType}" names (legacy "STING:…") — ManagedTemplateSyncer owns these.</summary>
        public List<string> Managed { get; } = new List<string>();
    }

    public static class DrawingTemplateCatalogue
    {
        /// <summary>Base view kinds ViewTemplatesCommand can create a template from.</summary>
        public static readonly string[] CreatableKinds =
        {
            DrawingViewKind.FloorPlan, DrawingViewKind.Rcp, DrawingViewKind.Section,
            DrawingViewKind.Elevation, DrawingViewKind.Detail, DrawingViewKind.ThreeD,
            DrawingViewKind.Drafting, DrawingViewKind.Schedule,
        };

        /// <summary>Managed templates are minted and kept in sync by ManagedTemplateSyncer.</summary>
        public static bool IsManagedName(string name)
            => ManagedTemplateNames.HasManagedPrefix(name);   // DT-R11-F: canonical or legacy form

        /// <summary>
        /// Group the drawing types' viewTemplateNames into creatable specs.
        /// Types with no template name are ignored. A name used by types whose
        /// purposes need different base views keeps the first type's kind, and
        /// the conflict is reported in <see cref="DrawingTemplatePlan.NotCreatable"/>
        /// for the later ones — a template cannot serve two view types.
        /// <para>DTW-62: a production rule's <c>viewTemplateOverride</c> is planned
        /// too, from the kind of view the RULE makes (not the type's purpose), so a
        /// mixed-kind type can name a template for each view it produces.</para>
        /// </summary>
        public static DrawingTemplatePlan Plan(IEnumerable<DrawingType> types)
        {
            var plan = new DrawingTemplatePlan();
            var byName = new Dictionary<string, DrawingTemplateSpec>(StringComparer.Ordinal);
            var levels = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            void Add(DrawingType dt, string name, string kind, string who)
            {
                if (!CreatableKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
                {
                    plan.NotCreatable.Add($"{name} — {who} is a {kind}; Revit cannot apply a view template to a {kind} view");
                    return;
                }
                if (!byName.TryGetValue(name, out var spec))
                {
                    spec = new DrawingTemplateSpec
                    {
                        Name = name,
                        BaseKind = kind,
                        VgCode = VgCodeFor(dt, kind),
                    };
                    byName[name] = spec;
                    levels[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    plan.Creatable.Add(spec);
                }
                else if (!string.Equals(spec.BaseKind, kind, StringComparison.OrdinalIgnoreCase))
                {
                    plan.NotCreatable.Add($"{name} — {who} needs a {kind} template but the name is already a " +
                                          $"{spec.BaseKind} template (used by {string.Join(", ", spec.UsedBy)})");
                    return;
                }
                if (!spec.UsedBy.Contains(dt.Id)) spec.UsedBy.Add(dt.Id);
                levels[name].Add(string.IsNullOrWhiteSpace(dt.DetailLevel) ? "Medium" : dt.DetailLevel.Trim());
            }

            foreach (var dt in types ?? Enumerable.Empty<DrawingType>())
            {
                if (dt == null) continue;
                string name = dt.ViewTemplateName?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    if (IsManagedName(name))
                    {
                        if (!plan.Managed.Contains(name)) plan.Managed.Add(name);
                    }
                    else if (!DrawingPurposeViewKind.TryResolve(dt.Purpose, out var kind))
                        plan.NotCreatable.Add($"{name} — '{dt.Id}' has purpose '{dt.Purpose ?? "(none)"}', which maps to no view kind");
                    else
                        Add(dt, name, kind, $"'{dt.Id}'");
                }

                if (dt.ProductionRules == null) continue;
                foreach (var rule in dt.ProductionRules)
                {
                    string over = rule?.ViewTemplateOverride?.Trim();
                    if (string.IsNullOrEmpty(over)) continue;
                    if (IsManagedName(over))
                    {
                        if (!plan.Managed.Contains(over)) plan.Managed.Add(over);
                        continue;
                    }
                    string ruleKind = ViewKindOf(rule.ViewType);
                    if (ruleKind == null)
                    {
                        plan.NotCreatable.Add($"{over} — '{dt.Id}' rule {rule.Idx} makes '{rule.ViewType}', which is no view kind");
                        continue;
                    }
                    Add(dt, over, ruleKind, $"'{dt.Id}' rule {rule.Idx}");
                }
            }

            foreach (var spec in plan.Creatable)
                spec.DetailLevel = levels[spec.Name].Count == 1 ? levels[spec.Name].First() : null;
            return plan;
        }

        /// <summary>
        /// DTW-62: the <see cref="DrawingViewKind"/> of a Revit ViewType name as a
        /// production rule or a live view spells it ("FloorPlan", "CeilingPlan",
        /// "ThreeD", "DraftingView" …). Null for anything a template cannot name.
        /// One table, so the catalogue and DrawingTypePresentation agree on which
        /// rule a produced view came from.
        /// </summary>
        public static string ViewKindOf(string viewType)
        {
            switch ((viewType ?? "").Trim().ToLowerInvariant())
            {
                case "floorplan": case "plan": case "areaplan": case "engineeringplan":
                    return DrawingViewKind.FloorPlan;
                case "ceilingplan": case "rcp":        return DrawingViewKind.Rcp;
                case "section":                        return DrawingViewKind.Section;
                case "elevation":                      return DrawingViewKind.Elevation;
                case "detail":                         return DrawingViewKind.Detail;
                case "threed": case "3d": case "iso":  return DrawingViewKind.ThreeD;
                case "draftingview": case "drafting":  return DrawingViewKind.Drafting;
                case "schedule": case "panelschedule": return DrawingViewKind.Schedule;
                case "legend":                         return DrawingViewKind.Legend;
                default:                               return null;
            }
        }

        /// <summary>
        /// The ConfigureTemplateVG scheme for a drawing type: view kind first
        /// (sections, details, elevations, 3D and RCPs have their own schemes),
        /// then presentation, coordination, then discipline.
        /// </summary>
        public static string VgCodeFor(DrawingType dt, string kind)
        {
            string name = dt?.ViewTemplateName ?? "";
            bool pres = name.IndexOf("Presentation", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(dt?.Phase, "PRESENTATION", StringComparison.OrdinalIgnoreCase);

            switch (kind)
            {
                case DrawingViewKind.Section:   return pres ? "SEC_P" : "SEC_W";
                case DrawingViewKind.Detail:    return "SEC_D";
                case DrawingViewKind.Elevation: return pres ? "ELEV_P" : "ELEV_W";
                case DrawingViewKind.ThreeD:    return pres ? "PRES_3D" : "MEP_3D";
                case DrawingViewKind.Rcp:
                    return string.Equals(dt?.Discipline, "E", StringComparison.OrdinalIgnoreCase) ? "RCP_LTG" : "RCP_CLG";
            }

            if (pres) return "PRES_C";
            if (string.Equals(dt?.Purpose, DrawingPurpose.Coordination, StringComparison.OrdinalIgnoreCase)) return "MEP";

            switch ((dt?.Discipline ?? "*").Trim().ToUpperInvariant())
            {
                case "M": case "MG": return "M";
                case "E":            return "E";
                case "P":            return "P";
                case "S":            return "S";
                case "FP":           return "FP";
                case "LV":           return "LV";
                case "A": case "H": case "RP": return "A";
                default:             return "ALL";
            }
        }
    }
}
