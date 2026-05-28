using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// A user-defined sequence of Template Manager ops. Stored as JSON under
    /// <project>/_BIM_COORD/recipes/*.json and shown on the v2 dashboard's
    /// Recipes tab. Mirror of the WorkflowEngine pattern but scoped to
    /// Template Manager so the dependency graph + readiness snapshot can
    /// be respected without dragging in the full workflow infrastructure.
    /// </summary>
    public sealed class TemplateRecipe
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsCorporate { get; set; }   // shipped baseline vs project-authored
        public List<RecipeStep> Steps { get; set; } = new();
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>One step in a recipe — references an op-tag from OperationRegistry.</summary>
    public sealed class RecipeStep
    {
        public string OpTag { get; set; } = "";
        public bool SkipIfDone { get; set; } = true;        // honour readiness snapshot
        public bool StopOnFailure { get; set; } = false;
        public string MinReadinessStatus { get; set; } = "";  // "Green" — only run if dependencies green
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>One step's outcome inside a RecipeRunRecord.</summary>
    public sealed class RecipeStepResult
    {
        public string OpTag { get; set; } = "";
        public bool Skipped { get; set; }
        public bool Succeeded { get; set; }
        public string Note { get; set; } = "";
        public double DurationMs { get; set; }
    }

    /// <summary>Aggregate outcome of a recipe run.</summary>
    public sealed class RecipeRunRecord
    {
        public string RecipeId { get; set; } = "";
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public double TotalDurationMs { get; set; }
        public List<RecipeStepResult> Steps { get; set; } = new();
        public bool Cancelled { get; set; }
        public string FinalStatus { get; set; } = "";
    }

    /// <summary>
    /// Loads recipes from the corporate baseline + project overlay folder
    /// and persists project-authored recipes. Pure data-layer — running a
    /// recipe is the dashboard's responsibility (needs ExternalEvent).
    /// </summary>
    public static class RecipeRegistry
    {
        private const string ProjectRecipeDir = "recipes";

        public static List<TemplateRecipe> LoadAll(Document doc)
        {
            var list = new List<TemplateRecipe>();
            // 1) Corporate baseline recipes (shipped JSON in data/)
            try
            {
                string corpFile = StingTools.Core.StingToolsApp.FindDataFile("STING_TEMPLATE_RECIPES.json");
                if (!string.IsNullOrEmpty(corpFile) && File.Exists(corpFile))
                {
                    var json = File.ReadAllText(corpFile);
                    var corpList = JsonConvert.DeserializeObject<List<TemplateRecipe>>(json);
                    if (corpList != null) foreach (var r in corpList) { r.IsCorporate = true; list.Add(r); }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"RecipeRegistry corp: {ex.Message}"); }

            // 2) Built-in baseline recipes (always present)
            foreach (var r in BuiltInRecipes())
                if (!list.Any(x => x.Id == r.Id)) list.Add(r);

            // 3) Project recipes
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string dir = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD", ProjectRecipeDir);
                    if (Directory.Exists(dir))
                    {
                        foreach (var path in Directory.GetFiles(dir, "*.json"))
                        {
                            try
                            {
                                var json = File.ReadAllText(path);
                                var r = JsonConvert.DeserializeObject<TemplateRecipe>(json);
                                if (r != null) { r.IsCorporate = false; list.Add(r); }
                            }
                            catch (Exception ex) { StingTools.Core.StingLog.Warn($"RecipeRegistry project {path}: {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"RecipeRegistry project dir: {ex.Message}"); }

            return list;
        }

        public static void SaveProject(Document doc, TemplateRecipe recipe)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName))
                throw new InvalidOperationException("Document has no save path — save the project first.");
            if (string.IsNullOrWhiteSpace(recipe.Id))
                recipe.Id = $"recipe-{DateTime.UtcNow:yyyyMMddHHmmss}";
            string dir = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD", ProjectRecipeDir);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, recipe.Id + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(recipe, Formatting.Indented));
        }

        public static void DeleteProject(Document doc, string recipeId)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName) || string.IsNullOrEmpty(recipeId)) return;
            string path = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD", ProjectRecipeDir, recipeId + ".json");
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"RecipeRegistry delete: {ex.Message}"); }
        }

        /// <summary>Recipes that always ship with the plugin (no JSON needed).</summary>
        public static IEnumerable<TemplateRecipe> BuiltInRecipes()
        {
            yield return new TemplateRecipe
            {
                Id = "stinglite-setup",
                Name = "★ Setup (lite)",
                Description = "Minimum viable setup: parameters → filters → templates.",
                IsCorporate = true,
                Steps = new List<RecipeStep>
                {
                    new() { OpTag = "CreateParameters" },
                    new() { OpTag = "CreateFilters" },
                    new() { OpTag = "ViewTemplates" }
                }
            };
            yield return new TemplateRecipe
            {
                Id = "sting-master-setup",
                Name = "★ Master Setup",
                Description = "Full setup pipeline (~20 steps).",
                IsCorporate = true,
                Steps = new List<RecipeStep>
                {
                    new() { OpTag = "CreateParameters", StopOnFailure = true },
                    new() { OpTag = "CreateFilters" },
                    new() { OpTag = "CreateWorksets" },
                    new() { OpTag = "CreateLinePatterns" },
                    new() { OpTag = "CreatePhases" },
                    new() { OpTag = "CreateFillPatterns" },
                    new() { OpTag = "CreateLineStyles" },
                    new() { OpTag = "CreateObjectStyles" },
                    new() { OpTag = "CreateTextStyles" },
                    new() { OpTag = "CreateDimensionStyles" },
                    new() { OpTag = "CreateVGOverrides" },
                    new() { OpTag = "ViewTemplates" },
                    new() { OpTag = "ApplyFilters" },
                    new() { OpTag = "AutoAssignTemplates" },
                    new() { OpTag = "CreateTemplateSchedules" },
                    new() { OpTag = "TemplateAudit" }
                }
            };
            yield return new TemplateRecipe
            {
                Id = "sting-style-refresh",
                Name = "Style refresh",
                Description = "Re-apply all STING styles without touching templates.",
                IsCorporate = true,
                Steps = new List<RecipeStep>
                {
                    new() { OpTag = "CreateFillPatterns" },
                    new() { OpTag = "CreateLineStyles" },
                    new() { OpTag = "CreateObjectStyles" },
                    new() { OpTag = "CreateTextStyles" },
                    new() { OpTag = "CreateDimensionStyles" },
                    new() { OpTag = "SyncTemplateOverrides" }
                }
            };
            yield return new TemplateRecipe
            {
                Id = "sting-audit-only",
                Name = "Audit (read-only)",
                Description = "Run every audit / inspection op; safe on locked projects.",
                IsCorporate = true,
                Steps = new List<RecipeStep>
                {
                    new() { OpTag = "TemplateAudit" },
                    new() { OpTag = "TemplateComplianceScore" },
                    new() { OpTag = "TemplateVGAudit" },
                    new() { OpTag = "ValidateTemplate" },
                    new() { OpTag = "SchemaValidate" }
                }
            };
            yield return new TemplateRecipe
            {
                Id = "sting-healthcare-pack",
                Name = "Healthcare project setup",
                Description = "Setup pipeline with healthcare-specific defaults.",
                IsCorporate = true,
                Steps = new List<RecipeStep>
                {
                    new() { OpTag = "CreateParameters", StopOnFailure = true },
                    new() { OpTag = "CreateFilters" },
                    new() { OpTag = "ViewTemplates" },
                    new() { OpTag = "CreateFillPatterns" },
                    new() { OpTag = "CreateLineStyles" },
                    new() { OpTag = "ApplyFilters" },
                    new() { OpTag = "AutoAssignTemplates" },
                    new() { OpTag = "TemplateComplianceScore",
                            Options = new Dictionary<string, string> { ["profile"] = "healthcare" } }
                }
            };
        }
    }
}
