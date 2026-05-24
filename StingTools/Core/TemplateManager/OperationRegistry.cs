using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// One Template Manager operation as known to the v2 dashboard. Carries the
    /// command tag (already dispatched via StingCommandHandler), description,
    /// group, dependencies, and optional preview provider.
    /// </summary>
    public sealed class OpDefinition
    {
        public string Tag { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;   // SETUP | TEMPLATES | STYLES | SCHEDULES & DATA | AUTOMATION
        public string Description { get; set; } = string.Empty;
        public bool IsHighlighted { get; set; }              // ★ star
        public bool IsDestructive { get; set; }              // requires snapshot warning
        public bool IsReadOnly { get; set; }                 // no transaction → safe to re-run
        public string[] RequiresOps { get; set; } = Array.Empty<string>();
        public string HelpDocPath { get; set; } = string.Empty;
        public string IconKey { get; set; } = string.Empty;
        public PreviewProvider Preview { get; set; }         // null when no preview is available
    }

    /// <summary>
    /// Single source of truth for the v2 dashboard's operation catalogue.
    /// Replaces the BuildSetupOps/BuildTemplateOps/... methods inside the
    /// old dashboard with declarative metadata.
    /// </summary>
    public static class OperationRegistry
    {
        public const string GroupSetup = "SETUP";
        public const string GroupTemplates = "TEMPLATES";
        public const string GroupStyles = "STYLES";
        public const string GroupSchedules = "SCHEDULES & DATA";
        public const string GroupAutomation = "AUTOMATION";

        private static readonly List<OpDefinition> _ops = Build();

        public static IReadOnlyList<OpDefinition> All => _ops;

        public static IEnumerable<OpDefinition> ByGroup(string group) =>
            _ops.Where(o => string.Equals(o.Group, group, StringComparison.OrdinalIgnoreCase));

        public static OpDefinition Get(string tag) =>
            _ops.FirstOrDefault(o => string.Equals(o.Tag, tag, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<string> Groups => new[]
        {
            GroupSetup, GroupTemplates, GroupStyles, GroupSchedules, GroupAutomation
        };

        /// <summary>Register a preview provider for an op (called by the dashboard wiring).</summary>
        public static void RegisterPreview(string tag, PreviewProvider provider)
        {
            var op = Get(tag);
            if (op != null) op.Preview = provider;
        }

        /// <summary>Free-text search across title + description + tag.</summary>
        public static IEnumerable<OpDefinition> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return _ops;
            query = query.Trim();
            return _ops.Where(o =>
                (o.Title?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (o.Description?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (o.Tag?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        }

        // ── The catalogue ────────────────────────────────────────────────
        private static List<OpDefinition> Build()
        {
            return new List<OpDefinition>
            {
                // SETUP ─────────────────────────────────────────────────
                new OpDefinition {
                    Tag = "CreateParameters", Group = GroupSetup,
                    Title = "Create Shared Parameters",
                    Description = "Bind STING shared parameters to project categories (2-pass binding from MR_PARAMETERS.txt)",
                },
                new OpDefinition {
                    Tag = "CreateFilters", Group = GroupSetup,
                    Title = "Create Filters",
                    Description = "28 multi-category view filters (Mechanical, Electrical, Plumbing, etc.)",
                    RequiresOps = new[] { "CreateParameters" }
                },
                new OpDefinition {
                    Tag = "CreateWorksets", Group = GroupSetup,
                    Title = "Create Worksets",
                    Description = "35 ISO 19650-compliant worksets",
                },
                new OpDefinition {
                    Tag = "CreateLinePatterns", Group = GroupSetup,
                    Title = "Create Line Patterns",
                    Description = "10 ISO 128-2:2020 line patterns",
                },
                new OpDefinition {
                    Tag = "CreatePhases", Group = GroupSetup,
                    Title = "Create Phases",
                    Description = "Report phase status",
                    IsReadOnly = true
                },
                new OpDefinition {
                    Tag = "MasterSetup", Group = GroupSetup,
                    Title = "★ Master Setup",
                    Description = "Run all setup steps in sequence (20 steps)",
                    IsHighlighted = true,
                    RequiresOps = Array.Empty<string>()
                },

                // TEMPLATES ─────────────────────────────────────────────
                new OpDefinition {
                    Tag = "ViewTemplates", Group = GroupTemplates,
                    Title = "Create View Templates",
                    Description = "23 STING discipline view templates with VG",
                    RequiresOps = new[] { "CreateFilters" }
                },
                new OpDefinition {
                    Tag = "AutoAssignTemplates", Group = GroupTemplates,
                    Title = "Auto-Assign Templates",
                    Description = "5-layer intelligent matching (name → level → phase → scope → type)",
                    RequiresOps = new[] { "ViewTemplates" }
                },
                new OpDefinition {
                    Tag = "CloneTemplate", Group = GroupTemplates,
                    Title = "Clone Template",
                    Description = "Deep clone with VG, filters, and overrides",
                },
                new OpDefinition {
                    Tag = "ApplyFilters", Group = GroupTemplates,
                    Title = "Apply Filters to Templates",
                    Description = "Apply STING filters to all STING templates",
                    RequiresOps = new[] { "CreateFilters", "ViewTemplates" }
                },
                new OpDefinition {
                    Tag = "SyncTemplateOverrides", Group = GroupTemplates,
                    Title = "Sync VG Overrides",
                    Description = "Re-apply VG overrides to restore discipline colours",
                    IsDestructive = true
                },
                new OpDefinition {
                    Tag = "AutoFixTemplate", Group = GroupTemplates,
                    Title = "Auto-Fix Templates",
                    Description = "One-click template health repair",
                    IsDestructive = true
                },
                new OpDefinition {
                    Tag = "BatchVGReset", Group = GroupTemplates,
                    Title = "Batch VG Reset",
                    Description = "Reset VG settings across multiple views",
                    IsDestructive = true
                },
                new OpDefinition {
                    Tag = "TemplateAudit", Group = GroupTemplates,
                    Title = "Template Audit",
                    Description = "Deep compliance audit with scoring",
                    IsReadOnly = true
                },
                new OpDefinition {
                    Tag = "TemplateDiff", Group = GroupTemplates,
                    Title = "Template Diff",
                    Description = "Compare VG settings between two templates",
                    IsReadOnly = true
                },
                new OpDefinition {
                    Tag = "TemplateComplianceScore", Group = GroupTemplates,
                    Title = "Compliance Score",
                    Description = "Weighted 10-point scoring per view",
                    IsReadOnly = true
                },

                // STYLES ────────────────────────────────────────────────
                new OpDefinition {
                    Tag = "CreateFillPatterns", Group = GroupStyles,
                    Title = "Fill Patterns",
                    Description = "12 ISO 128-2:2020 fill patterns",
                },
                new OpDefinition {
                    Tag = "CreateLineStyles", Group = GroupStyles,
                    Title = "Line Styles",
                    Description = "16 ISO line styles (from CSV with hardcoded fallback)",
                },
                new OpDefinition {
                    Tag = "CreateObjectStyles", Group = GroupStyles,
                    Title = "Object Styles",
                    Description = "40+ ISO category line weights/colours",
                },
                new OpDefinition {
                    Tag = "CreateTextStyles", Group = GroupStyles,
                    Title = "Text Styles",
                    Description = "12 ISO 3098 text note types",
                },
                new OpDefinition {
                    Tag = "CreateDimensionStyles", Group = GroupStyles,
                    Title = "Dimension Styles",
                    Description = "7 ISO dimension types",
                },
                new OpDefinition {
                    Tag = "CreateVGOverrides", Group = GroupStyles,
                    Title = "VG Overrides",
                    Description = "6-layer VG override intelligence",
                },

                // SCHEDULES & DATA ──────────────────────────────────────
                new OpDefinition {
                    Tag = "CreateTemplateSchedules", Group = GroupSchedules,
                    Title = "Create Template Schedules",
                    Description = "Standard schedule templates from CSV",
                },
                new OpDefinition {
                    Tag = "MaterialSchedules", Group = GroupSchedules,
                    Title = "Material Schedules",
                    Description = "Material takeoff schedules (8 categories)",
                },
                new OpDefinition {
                    Tag = "CreateCableTrays", Group = GroupSchedules,
                    Title = "Cable Trays",
                    Description = "Cable tray types from MEP_MATERIALS.csv",
                },
                new OpDefinition {
                    Tag = "CreateConduits", Group = GroupSchedules,
                    Title = "Conduits",
                    Description = "Conduit types from MEP_MATERIALS.csv",
                },
                new OpDefinition {
                    Tag = "BatchFamilyParams", Group = GroupSchedules,
                    Title = "Batch Family Parameters",
                    Description = "Add shared parameters to families",
                },
                new OpDefinition {
                    Tag = "FamilyParameterProcessor", Group = GroupSchedules,
                    Title = "Family Parameter Processor",
                    Description = "Batch .rfa parameter processing",
                },

                // AUTOMATION ────────────────────────────────────────────
                new OpDefinition {
                    Tag = "TemplateSetupWizard", Group = GroupAutomation,
                    Title = "★ Template Setup Wizard",
                    Description = "15-step complete automation pipeline",
                    IsHighlighted = true
                },
                new OpDefinition {
                    Tag = "ProjectSetup", Group = GroupAutomation,
                    Title = "★ Project Setup Wizard",
                    Description = "7-page comprehensive project wizard",
                    IsHighlighted = true
                },
                new OpDefinition {
                    Tag = "ValidateTemplate", Group = GroupAutomation,
                    Title = "Validate Template",
                    Description = "45 validation checks",
                    IsReadOnly = true
                },
                new OpDefinition {
                    Tag = "DynamicBindings", Group = GroupAutomation,
                    Title = "Dynamic Bindings",
                    Description = "Load bindings from BINDING_COVERAGE_MATRIX.csv",
                },
                new OpDefinition {
                    Tag = "SchemaValidate", Group = GroupAutomation,
                    Title = "Schema Validate",
                    Description = "Validate CSV columns match MATERIAL_SCHEMA.json",
                    IsReadOnly = true
                },
                new OpDefinition {
                    Tag = "TemplateVGAudit", Group = GroupAutomation,
                    Title = "Template VG Audit",
                    Description = "Visual Graphics override analysis",
                    IsReadOnly = true
                },
            };
        }
    }
}
