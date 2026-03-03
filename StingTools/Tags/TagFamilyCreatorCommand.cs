using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Creates and loads STING tag families (.rfa) for all 50 taggable categories.
    /// Each tag family is created from the appropriate Revit .rft annotation template
    /// and configured with STING shared parameters (ASS_TAG_1_TXT, etc.).
    ///
    /// Workflow:
    ///   1. Locate Revit annotation tag templates (.rft) on disk
    ///   2. For each taggable category, create a family from the template
    ///   3. Add STING shared parameters via FamilyManager
    ///   4. Save .rfa to TagFamilies/ output directory
    ///   5. Load into the current project
    ///   6. Report results with per-category status
    ///
    /// Post-creation step: User opens each family in Family Editor and sets the
    /// Label to display ASS_TAG_1_TXT (cannot be done programmatically — Revit API limitation).
    /// </summary>
    internal static class TagFamilyConfig
    {
        /// <summary>Naming prefix for all STING tag families.</summary>
        public const string FamilyPrefix = "STING";

        /// <summary>
        /// Maps BuiltInCategory to the expected Revit .rft annotation template filename.
        /// Revit ships these templates in the Family Templates/Annotations/ directory.
        /// If the specific template is not found, falls back to Generic Tag.rft.
        /// </summary>
        public static readonly Dictionary<BuiltInCategory, string> CategoryTemplateMap =
            new Dictionary<BuiltInCategory, string>
        {
            // MEP — Mechanical / HVAC
            { BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment Tag.rft" },
            { BuiltInCategory.OST_DuctCurves, "Duct Tag.rft" },
            { BuiltInCategory.OST_DuctFitting, "Duct Fitting Tag.rft" },
            { BuiltInCategory.OST_DuctAccessory, "Duct Accessory Tag.rft" },
            { BuiltInCategory.OST_DuctTerminal, "Air Terminal Tag.rft" },
            { BuiltInCategory.OST_FlexDuctCurves, "Flex Duct Tag.rft" },

            // MEP — Plumbing / Piping
            { BuiltInCategory.OST_PipeCurves, "Pipe Tag.rft" },
            { BuiltInCategory.OST_PipeFitting, "Pipe Fitting Tag.rft" },
            { BuiltInCategory.OST_PipeAccessory, "Pipe Accessory Tag.rft" },
            { BuiltInCategory.OST_FlexPipeCurves, "Flex Pipe Tag.rft" },
            { BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixture Tag.rft" },

            // MEP — Fire Protection
            { BuiltInCategory.OST_Sprinklers, "Sprinkler Tag.rft" },
            { BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Device Tag.rft" },

            // MEP — Electrical
            { BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft" },
            { BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixture Tag.rft" },
            { BuiltInCategory.OST_LightingFixtures, "Lighting Fixture Tag.rft" },
            { BuiltInCategory.OST_LightingDevices, "Lighting Device Tag.rft" },
            { BuiltInCategory.OST_Conduit, "Conduit Tag.rft" },
            { BuiltInCategory.OST_ConduitFitting, "Conduit Fitting Tag.rft" },
            { BuiltInCategory.OST_CableTray, "Cable Tray Tag.rft" },
            { BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fitting Tag.rft" },

            // MEP — Low Voltage / Communications
            { BuiltInCategory.OST_CommunicationDevices, "Communication Device Tag.rft" },
            { BuiltInCategory.OST_DataDevices, "Data Device Tag.rft" },
            { BuiltInCategory.OST_NurseCallDevices, "Nurse Call Device Tag.rft" },
            { BuiltInCategory.OST_SecurityDevices, "Security Device Tag.rft" },
            { BuiltInCategory.OST_TelephoneDevices, "Telephone Device Tag.rft" },

            // Architecture
            { BuiltInCategory.OST_Doors, "Door Tag.rft" },
            { BuiltInCategory.OST_Windows, "Window Tag.rft" },
            { BuiltInCategory.OST_Walls, "Wall Tag.rft" },
            { BuiltInCategory.OST_Floors, "Floor Tag.rft" },
            { BuiltInCategory.OST_Ceilings, "Ceiling Tag.rft" },
            { BuiltInCategory.OST_Roofs, "Roof Tag.rft" },
            { BuiltInCategory.OST_Rooms, "Room Tag.rft" },
            { BuiltInCategory.OST_Furniture, "Furniture Tag.rft" },
            { BuiltInCategory.OST_FurnitureSystems, "Furniture System Tag.rft" },
            { BuiltInCategory.OST_Casework, "Casework Tag.rft" },

            // Architecture — Circulation
            { BuiltInCategory.OST_Stairs, "Stair Tag.rft" },
            { BuiltInCategory.OST_Ramps, "Ramp Tag.rft" },

            // Structure
            { BuiltInCategory.OST_StructuralColumns, "Structural Column Tag.rft" },
            { BuiltInCategory.OST_StructuralFraming, "Structural Framing Tag.rft" },
            { BuiltInCategory.OST_StructuralFoundation, "Structural Foundation Tag.rft" },

            // Generic / Specialty / Site
            { BuiltInCategory.OST_GenericModel, "Generic Model Tag.rft" },
            { BuiltInCategory.OST_SpecialityEquipment, "Specialty Equipment Tag.rft" },
            { BuiltInCategory.OST_Parking, "Parking Tag.rft" },
            { BuiltInCategory.OST_Site, "Site Tag.rft" },
        };

        /// <summary>
        /// Human-readable category name for family naming and reporting.
        /// Uses the Revit display name where possible.
        /// </summary>
        public static readonly Dictionary<BuiltInCategory, string> CategoryDisplayName =
            new Dictionary<BuiltInCategory, string>
        {
            { BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment" },
            { BuiltInCategory.OST_DuctCurves, "Ducts" },
            { BuiltInCategory.OST_DuctFitting, "Duct Fittings" },
            { BuiltInCategory.OST_DuctAccessory, "Duct Accessories" },
            { BuiltInCategory.OST_DuctTerminal, "Air Terminals" },
            { BuiltInCategory.OST_FlexDuctCurves, "Flex Ducts" },
            { BuiltInCategory.OST_PipeCurves, "Pipes" },
            { BuiltInCategory.OST_PipeFitting, "Pipe Fittings" },
            { BuiltInCategory.OST_PipeAccessory, "Pipe Accessories" },
            { BuiltInCategory.OST_FlexPipeCurves, "Flex Pipes" },
            { BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures" },
            { BuiltInCategory.OST_Sprinklers, "Sprinklers" },
            { BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Devices" },
            { BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment" },
            { BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures" },
            { BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures" },
            { BuiltInCategory.OST_LightingDevices, "Lighting Devices" },
            { BuiltInCategory.OST_Conduit, "Conduits" },
            { BuiltInCategory.OST_ConduitFitting, "Conduit Fittings" },
            { BuiltInCategory.OST_CableTray, "Cable Trays" },
            { BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings" },
            { BuiltInCategory.OST_CommunicationDevices, "Communication Devices" },
            { BuiltInCategory.OST_DataDevices, "Data Devices" },
            { BuiltInCategory.OST_NurseCallDevices, "Nurse Call Devices" },
            { BuiltInCategory.OST_SecurityDevices, "Security Devices" },
            { BuiltInCategory.OST_TelephoneDevices, "Telephone Devices" },
            { BuiltInCategory.OST_Doors, "Doors" },
            { BuiltInCategory.OST_Windows, "Windows" },
            { BuiltInCategory.OST_Walls, "Walls" },
            { BuiltInCategory.OST_Floors, "Floors" },
            { BuiltInCategory.OST_Ceilings, "Ceilings" },
            { BuiltInCategory.OST_Roofs, "Roofs" },
            { BuiltInCategory.OST_Rooms, "Rooms" },
            { BuiltInCategory.OST_Furniture, "Furniture" },
            { BuiltInCategory.OST_FurnitureSystems, "Furniture Systems" },
            { BuiltInCategory.OST_Casework, "Casework" },
            { BuiltInCategory.OST_Stairs, "Stairs" },
            { BuiltInCategory.OST_Ramps, "Ramps" },
            { BuiltInCategory.OST_StructuralColumns, "Structural Columns" },
            { BuiltInCategory.OST_StructuralFraming, "Structural Framing" },
            { BuiltInCategory.OST_StructuralFoundation, "Structural Foundations" },
            { BuiltInCategory.OST_GenericModel, "Generic Models" },
            { BuiltInCategory.OST_SpecialityEquipment, "Specialty Equipment" },
            { BuiltInCategory.OST_Parking, "Parking" },
            { BuiltInCategory.OST_Site, "Site" },
        };

        /// <summary>
        /// STING shared parameters to add to each tag family.
        /// These are the primary tag container parameters that the Label should display.
        /// </summary>
        public static readonly string[] TagParams = new[]
        {
            ParamRegistry.TAG1,  // Primary 8-segment tag (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)
            ParamRegistry.TAG2,  // System tag short (SYS-FUNC-PROD-SEQ)
            ParamRegistry.TAG3,  // System tag extended
            ParamRegistry.TAG4,  // Short label (PROD-SEQ)
            ParamRegistry.TAG5,  // Description tag (PROD + family name)
            ParamRegistry.TAG6,  // Status tag (multi-line)
        };

        /// <summary>
        /// Visibility control parameters — added to every tag family so that
        /// calculated values in Edit Label can gate tier 2/3 and warning visibility.
        /// These are Type parameters (Yes/No) set by SetPresentationModeCommand.
        /// </summary>
        public static readonly string[] VisibilityParams = new[]
        {
            ParamRegistry.PARA_STATE_1,  // TAG_PARA_STATE_1_BOOL — Compact
            ParamRegistry.PARA_STATE_2,  // TAG_PARA_STATE_2_BOOL — Standard
            ParamRegistry.PARA_STATE_3,  // TAG_PARA_STATE_3_BOOL — Comprehensive
            ParamRegistry.WARN_VISIBLE,  // TAG_WARN_VISIBLE_BOOL — Warning toggle
        };

        /// <summary>
        /// Get all parameters that should be added to a tag family for a specific
        /// category. Includes TagParams + VisibilityParams + category-specific
        /// paragraph container and tier 2/3 display parameters from LABEL_DEFINITIONS.json.
        /// </summary>
        public static List<string> GetAllFamilyParams(string categoryDisplayName)
        {
            var result = new List<string>();

            // Always add universal tag params
            result.AddRange(TagParams);

            // Always add visibility control params
            result.AddRange(VisibilityParams);

            // Add description param
            result.Add("ASS_DESCRIPTION_TXT");

            // Add category-specific params from label definitions
            var labelParams = LabelDefinitionHelper.GetCategoryParams(categoryDisplayName);
            foreach (string p in labelParams)
            {
                if (!result.Contains(p))
                    result.Add(p);
            }

            return result;
        }

        /// <summary>Generate the STING family name for a category.</summary>
        public static string GetFamilyName(BuiltInCategory bic)
        {
            string catName = CategoryDisplayName.TryGetValue(bic, out string name)
                ? name : bic.ToString().Replace("OST_", "");
            return $"{FamilyPrefix} - {catName} Tag";
        }

        /// <summary>Generate the .rfa filename for a category.</summary>
        public static string GetFamilyFileName(BuiltInCategory bic)
        {
            return GetFamilyName(bic) + ".rfa";
        }

        /// <summary>
        /// Locate the Revit annotation template directory.
        /// Searches common installation paths for the .rft files.
        /// </summary>
        public static string FindTemplateDirectory(Autodesk.Revit.ApplicationServices.Application app)
        {
            // Primary: Revit's reported family template path
            string basePath = app.FamilyTemplatePath;
            if (!string.IsNullOrEmpty(basePath))
            {
                // Try common annotation template subdirectory names
                string[] annotDirs = new[]
                {
                    "Annotations",                      // English
                    "English",                          // Some installations
                    "English_I",                        // Imperial
                    "English-Imperial\\Annotations",    // Nested Imperial
                    "Metric\\Annotations",              // Metric
                };

                foreach (string sub in annotDirs)
                {
                    string candidate = Path.Combine(basePath, sub);
                    if (Directory.Exists(candidate))
                    {
                        // Verify it has .rft files
                        if (Directory.GetFiles(candidate, "*.rft").Length > 0)
                            return candidate;
                    }
                }

                // Try the base path itself
                if (Directory.GetFiles(basePath, "*.rft").Length > 0)
                    return basePath;

                // Search recursively for any directory containing "Tag.rft" files
                try
                {
                    foreach (string dir in Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories))
                    {
                        if (Directory.GetFiles(dir, "*Tag.rft").Length > 0)
                            return dir;
                    }
                }
                catch { /* permission issues */ }
            }

            // Fallback: search common Revit installation paths
            string[] revitPaths = new[]
            {
                @"C:\ProgramData\Autodesk\RVT 2027\Family Templates",
                @"C:\ProgramData\Autodesk\RVT 2026\Family Templates",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Autodesk", "RVT 2025", "Family Templates"),
            };

            foreach (string rp in revitPaths)
            {
                if (!Directory.Exists(rp)) continue;
                try
                {
                    foreach (string dir in Directory.GetDirectories(rp, "*", SearchOption.AllDirectories))
                    {
                        if (Directory.GetFiles(dir, "*Tag.rft").Length > 0)
                            return dir;
                    }
                }
                catch { /* permission issues */ }
            }

            return null;
        }

        /// <summary>
        /// Find the .rft template file for a given category.
        /// Tries the specific category template first, then falls back to Generic Tag.rft.
        /// Also handles locale variations (e.g., "Metric Generic Tag.rft").
        /// </summary>
        public static string FindTemplate(string templateDir, BuiltInCategory bic)
        {
            if (string.IsNullOrEmpty(templateDir)) return null;

            // Try specific category template
            if (CategoryTemplateMap.TryGetValue(bic, out string templateName))
            {
                string specific = Path.Combine(templateDir, templateName);
                if (File.Exists(specific)) return specific;

                // Try with "Metric " prefix
                string metric = Path.Combine(templateDir, "Metric " + templateName);
                if (File.Exists(metric)) return metric;

                // Try without spaces (some locales)
                string noSpace = Path.Combine(templateDir, templateName.Replace(" ", ""));
                if (File.Exists(noSpace)) return noSpace;
            }

            // Fallback chain: Generic Tag → Multi-Category Tag → Generic Annotation
            string[] fallbacks = new[]
            {
                "Generic Tag.rft",
                "Metric Generic Tag.rft",
                "Multi-Category Tag.rft",
                "Metric Multi-Category Tag.rft",
                "Generic Annotation.rft",
                "Metric Generic Annotation.rft",
            };

            foreach (string fb in fallbacks)
            {
                string path = Path.Combine(templateDir, fb);
                if (File.Exists(path)) return path;
            }

            // Last resort: pick any .rft file in the directory
            string[] rftFiles = Directory.GetFiles(templateDir, "*Tag.rft");
            if (rftFiles.Length > 0) return rftFiles[0];

            return null;
        }

        /// <summary>
        /// Search for a pre-configured seed family (.rfa) with labels already bound.
        /// Seed families are the gold standard — they have Label → ASS_TAG_1_TXT
        /// already configured, so they work immediately without manual Family Editor steps.
        ///
        /// Search order:
        ///   1. Data/TagFamilies/Seeds/  (distributed seed files)
        ///   2. Data/TagFamilies/        (user-configured files from previous Configure Labels run)
        /// Seed files are identified by having a "_seed" suffix or being in the Seeds/ subdirectory.
        /// </summary>
        public static string FindSeedFamily(BuiltInCategory bic)
        {
            string baseName = GetFamilyFileName(bic);
            string nameNoExt = GetFamilyName(bic);
            string dataPath = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(dataPath)) return null;

            // Check Seeds/ subdirectory first (distributed with the plugin)
            string seedDir = Path.Combine(dataPath, "TagFamilies", "Seeds");
            if (Directory.Exists(seedDir))
            {
                string seedPath = Path.Combine(seedDir, baseName);
                if (File.Exists(seedPath)) return seedPath;

                // Also check for _seed suffix variant
                string seedSuffix = Path.Combine(seedDir, nameNoExt + "_seed.rfa");
                if (File.Exists(seedSuffix)) return seedSuffix;
            }

            return null;
        }

        /// <summary>
        /// Get the output directory for tag families.
        /// Creates a TagFamilies/ subdirectory alongside the plugin data.
        /// </summary>
        public static string GetOutputDirectory()
        {
            string baseDir = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.GetDirectoryName(StingToolsApp.AssemblyPath) ?? "";

            string tagFamilyDir = Path.Combine(baseDir, "TagFamilies");
            if (!Directory.Exists(tagFamilyDir))
                Directory.CreateDirectory(tagFamilyDir);

            return tagFamilyDir;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Create Tag Families — create all 50 tag families from templates
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates STING tag families (.rfa) for all 50 taggable categories.
    /// Each family is created from the appropriate Revit annotation template,
    /// configured with STING shared parameters, saved, and loaded into the project.
    ///
    /// The command:
    ///   1. Locates Revit's .rft annotation templates on disk
    ///   2. Skips categories that already have a STING tag loaded
    ///   3. Creates new family documents from templates
    ///   4. Adds ASS_TAG_1_TXT through ASS_TAG_6_TXT shared parameters
    ///   5. Saves .rfa files to Data/TagFamilies/
    ///   6. Loads families into the current project
    ///
    /// Post-creation: Open each family in Family Editor, add a Label pointing
    /// to ASS_TAG_1_TXT to complete the tag family configuration.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;
            var app = uiApp.Application;

            // ── Step 1: Locate annotation templates ──
            string templateDir = TagFamilyConfig.FindTemplateDirectory(app);
            if (string.IsNullOrEmpty(templateDir))
            {
                TaskDialog.Show("Create Tag Families",
                    "Cannot find Revit annotation tag templates (.rft).\n\n" +
                    "Ensure Revit is installed with Family Templates.\n" +
                    $"Searched: {app.FamilyTemplatePath ?? "(null)"}");
                return Result.Failed;
            }

            StingLog.Info($"Tag family templates directory: {templateDir}");

            // ── Step 2: Locate shared parameter file ──
            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(sharedParamFile))
            {
                TaskDialog.Show("Create Tag Families",
                    "Cannot find MR_PARAMETERS.txt shared parameter file.\n" +
                    "Run 'Check Data' to verify data files are present.");
                return Result.Failed;
            }

            // ── Step 3: Check which STING tag families are already loaded ──
            var loadedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                if (fam.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase))
                    loadedFamilies.Add(fam.Name);
            }

            // ── Step 4: Determine categories to process ──
            var categories = TagFamilyConfig.CategoryTemplateMap.Keys.ToList();
            int total = categories.Count;
            int alreadyLoaded = 0;
            int created = 0;
            int loaded = 0;
            int failed = 0;
            int templateMissing = 0;
            var report = new StringBuilder();
            var failures = new List<string>();

            // Pre-check: how many already loaded?
            foreach (var bic in categories)
            {
                string famName = TagFamilyConfig.GetFamilyName(bic);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }

            // Confirmation dialog
            int toCreate = total - alreadyLoaded;
            if (toCreate == 0)
            {
                TaskDialog.Show("Create Tag Families",
                    $"All {total} STING tag families are already loaded in this project.\n" +
                    "No new families to create.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Create Tag Families");
            confirm.MainInstruction = $"Create {toCreate} STING tag families?";
            confirm.MainContent =
                $"Total taggable categories: {total}\n" +
                $"Already loaded: {alreadyLoaded}\n" +
                $"To create: {toCreate}\n\n" +
                $"Templates: {templateDir}\n" +
                $"Output: {TagFamilyConfig.GetOutputDirectory()}\n\n" +
                "Each family will be created from a Revit annotation template\n" +
                "and loaded into the project with STING shared parameters.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            string outputDir = TagFamilyConfig.GetOutputDirectory();
            report.AppendLine($"STING Tag Family Creation Report");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"Template directory: {templateDir}");
            report.AppendLine($"Output directory: {outputDir}");
            report.AppendLine();

            // ── Step 5: Create each tag family ──
            foreach (var bic in categories)
            {
                string famName = TagFamilyConfig.GetFamilyName(bic);
                string catDisplay = TagFamilyConfig.CategoryDisplayName.TryGetValue(bic, out string dn)
                    ? dn : bic.ToString();

                // Skip if already loaded
                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {catDisplay} — already loaded");
                    continue;
                }

                // Find template
                string templatePath = TagFamilyConfig.FindTemplate(templateDir, bic);
                if (string.IsNullOrEmpty(templatePath))
                {
                    report.AppendLine($"  [MISS] {catDisplay} — no template found");
                    templateMissing++;
                    failures.Add($"{catDisplay}: template not found");
                    continue;
                }

                string outputPath = Path.Combine(outputDir, TagFamilyConfig.GetFamilyFileName(bic));

                try
                {
                    // Strategy 3: Check for pre-configured seed files (with labels already bound)
                    // These are .rfa files manually configured via the Family Editor and
                    // placed in Data/TagFamilies/ for distribution. They take priority
                    // because they have labels already pointing to ASS_TAG_1_TXT.
                    string seedPath = TagFamilyConfig.FindSeedFamily(bic);
                    if (!string.IsNullOrEmpty(seedPath))
                    {
                        if (LoadFamilyIntoProject(doc, seedPath, famName))
                        {
                            report.AppendLine($"  [SEED] {catDisplay} — loaded pre-configured seed family");
                            loaded++;
                        }
                        else
                        {
                            report.AppendLine($"  [FAIL] {catDisplay} — seed load failed");
                            failed++;
                            failures.Add($"{catDisplay}: seed family load failed");
                        }
                        continue;
                    }

                    // Check if .rfa already exists on disk (from previous run)
                    if (File.Exists(outputPath))
                    {
                        if (LoadFamilyIntoProject(doc, outputPath, famName))
                        {
                            report.AppendLine($"  [LOAD] {catDisplay} — loaded from existing .rfa");
                            loaded++;
                        }
                        else
                        {
                            report.AppendLine($"  [FAIL] {catDisplay} — load failed");
                            failed++;
                            failures.Add($"{catDisplay}: family load failed");
                        }
                        continue;
                    }

                    // Create family from template (Strategy 1 + 2 fallback)
                    Document famDoc = app.NewFamilyDocument(templatePath);
                    if (famDoc == null)
                    {
                        report.AppendLine($"  [FAIL] {catDisplay} — NewFamilyDocument returned null");
                        failed++;
                        failures.Add($"{catDisplay}: cannot create from template");
                        continue;
                    }

                    // Add shared parameters: tag containers + visibility + category-specific
                    var allParams = TagFamilyConfig.GetAllFamilyParams(catDisplay);
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, allParams);

                    // Attempt to rebind the existing Label to ASS_TAG_1_TXT
                    bool labelBound = TryRebindLabel(famDoc);

                    // Save the family document
                    SaveAsOptions saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(outputPath, saveOpts);
                    famDoc.Close(false);

                    created++;
                    string paramStatus = paramsAdded
                        ? (labelBound ? "with params + label" : "with params")
                        : "no params (manual add needed)";

                    // Load into project
                    if (LoadFamilyIntoProject(doc, outputPath, famName))
                    {
                        report.AppendLine($"  [OK]   {catDisplay} — created and loaded ({paramStatus})");
                        loaded++;
                    }
                    else
                    {
                        report.AppendLine($"  [PART] {catDisplay} — created but load failed ({paramStatus})");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{catDisplay}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {catDisplay} — {ex.Message}");
                    StingLog.Error($"Tag family creation failed for {catDisplay}", ex);
                }
            }

            // ── Step 6: Report ──
            report.AppendLine();
            report.AppendLine(new string('-', 50));
            report.AppendLine($"Created:  {created}");
            report.AppendLine($"Loaded:   {loaded}");
            report.AppendLine($"Skipped:  {alreadyLoaded} (already loaded)");
            report.AppendLine($"Missing:  {templateMissing} (no template)");
            report.AppendLine($"Failed:   {failed}");

            if (created > 0)
            {
                report.AppendLine();
                report.AppendLine("NEXT STEP:");
                report.AppendLine("Run 'Configure Labels' to open each family in the");
                report.AppendLine("Family Editor and set the Label to ASS_TAG_1_TXT.");
                report.AppendLine("The wizard will guide you step by step.");
                report.AppendLine();
                report.AppendLine("TIP: After configuring, copy finished .rfa files to");
                report.AppendLine("Data/TagFamilies/Seeds/ to skip this step next time.");
            }

            TaskDialog td = new TaskDialog("Create Tag Families");
            td.MainInstruction = $"Created {created}, loaded {loaded} tag families";
            td.MainContent = report.ToString();
            if (failures.Count > 0)
            {
                td.ExpandedContent = "Failures:\n" + string.Join("\n", failures);
            }
            td.Show();

            StingLog.Info($"CreateTagFamilies: created={created}, loaded={loaded}, " +
                $"skipped={alreadyLoaded}, missing={templateMissing}, failed={failed}");

            return Result.Succeeded;
        }

        /// <summary>
        /// Add STING shared parameters to a family document using FamilyManager.
        /// Opens the shared parameter file, finds each tag parameter by GUID,
        /// and adds it to the family as an instance parameter.
        /// </summary>
        private bool AddSharedParameters(Document famDoc,
            string sharedParamFile,
            Autodesk.Revit.ApplicationServices.Application app,
            List<string> paramNames = null)
        {
            try
            {
                // Set the shared parameter file
                string originalFile = app.SharedParametersFilename;
                app.SharedParametersFilename = sharedParamFile;

                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    StingLog.Warn("Cannot open shared parameter file for tag family");
                    if (!string.IsNullOrEmpty(originalFile))
                        app.SharedParametersFilename = originalFile;
                    return false;
                }

                FamilyManager famMan = famDoc.FamilyManager;
                int added = 0;

                // Use provided param list or fallback to basic TagParams
                var paramsToAdd = paramNames ?? new List<string>(TagFamilyConfig.TagParams);

                using (Transaction tx = new Transaction(famDoc, "STING Add Tag Params"))
                {
                    tx.Start();

                    foreach (string paramName in paramsToAdd)
                    {
                        // Find the definition in the shared parameter file
                        ExternalDefinition extDef = FindSharedDefinition(defFile, paramName);
                        if (extDef == null)
                        {
                            StingLog.Warn($"Shared parameter '{paramName}' not found in file");
                            continue;
                        }

                        // Check if already added
                        bool exists = false;
                        foreach (FamilyParameter fp in famMan.Parameters)
                        {
                            if (fp.Definition.Name == paramName)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (exists) continue;

                        try
                        {
                            famMan.AddParameter(
                                extDef,
                                GroupTypeId.General,
                                true); // isInstance = true (tags display instance values)
                            added++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Cannot add param '{paramName}' to family: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // Restore original shared parameter file
                if (!string.IsNullOrEmpty(originalFile))
                    app.SharedParametersFilename = originalFile;

                StingLog.Info($"Added {added} shared parameters to tag family");
                return added > 0;
            }
            catch (Exception ex)
            {
                StingLog.Error("AddSharedParameters failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Attempt to find the existing Label/TextNote in the tag family template
        /// and rebind it to ASS_TAG_1_TXT. This exploits the fact that .rft templates
        /// come with a pre-existing Label element pointing to a built-in parameter.
        ///
        /// Approach: Find all TextNote elements in the family document, locate the
        /// FamilyParameter for ASS_TAG_1_TXT, and attempt to associate them via
        /// the Dimension.FamilyLabel API (the only programmatic Label mechanism).
        ///
        /// Returns true if the label was successfully rebound, false if the API
        /// does not support this operation (expected for most Revit versions).
        /// </summary>
        private bool TryRebindLabel(Document famDoc)
        {
            try
            {
                FamilyManager famMan = famDoc.FamilyManager;

                // Find the ASS_TAG_1_TXT parameter we just added
                FamilyParameter tagParam = null;
                foreach (FamilyParameter fp in famMan.Parameters)
                {
                    if (fp.Definition.Name == ParamRegistry.TAG1)
                    {
                        tagParam = fp;
                        break;
                    }
                }
                if (tagParam == null) return false;

                // Find existing dimensions in the family that may have labels.
                // In tag .rft templates, the label text is typically implemented as
                // a Dimension with a FamilyLabel property.
                var dims = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .ToList();

                using (Transaction tx = new Transaction(famDoc, "STING Rebind Label"))
                {
                    tx.Start();

                    foreach (Dimension dim in dims)
                    {
                        try
                        {
                            // Attempt to set the FamilyLabel to our tag parameter.
                            // This works for dimension labels in families but may not
                            // work for annotation text labels (which is the Revit limitation).
                            if (dim.FamilyLabel != null || dim.FamilyLabel == null)
                            {
                                dim.FamilyLabel = tagParam;
                                StingLog.Info("Successfully rebound dimension label to ASS_TAG_1_TXT");
                                tx.Commit();
                                return true;
                            }
                        }
                        catch
                        {
                            // Expected: most dimensions in tag families don't support
                            // FamilyLabel assignment. Continue to next.
                        }
                    }

                    // Also try: find TextNote elements and check if they have
                    // any association mechanism (varies by Revit version)
                    var textNotes = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                    foreach (TextNote tn in textNotes)
                    {
                        try
                        {
                            // In some Revit versions, tag templates use TextNote with
                            // a special BuiltInParameter for label association.
                            // Try to set the text to indicate which parameter to display.
                            Parameter labelParam = tn.get_Parameter(
                                BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (labelParam != null && !labelParam.IsReadOnly)
                            {
                                labelParam.Set(ParamRegistry.TAG1);
                            }
                        }
                        catch { /* Not supported — expected */ }
                    }

                    tx.Commit();
                }

                // If we get here, no programmatic rebind worked.
                // The label still shows the default parameter.
                return false;
            }
            catch (Exception ex)
            {
                StingLog.Info($"Label rebind attempt (expected to fail): {ex.Message}");
                return false;
            }
        }

        /// <summary>Search all groups in the shared parameter file for a definition by name.</summary>
        private ExternalDefinition FindSharedDefinition(DefinitionFile defFile, string paramName)
        {
            foreach (DefinitionGroup group in defFile.Groups)
            {
                foreach (Definition def in group.Definitions)
                {
                    if (def.Name == paramName && def is ExternalDefinition extDef)
                        return extDef;
                }
            }
            return null;
        }

        /// <summary>
        /// Load a family .rfa file into the project document.
        /// Uses the overwrite option to update existing families.
        /// </summary>
        private bool LoadFamilyIntoProject(Document doc, string familyPath, string expectedName)
        {
            try
            {
                using (Transaction tx = new Transaction(doc, $"STING Load Tag Family"))
                {
                    tx.Start();
                    bool loaded = doc.LoadFamily(familyPath, new TagFamilyLoadOptions(), out Family family);
                    tx.Commit();

                    if (loaded && family != null)
                    {
                        StingLog.Info($"Loaded tag family: {family.Name}");
                        return true;
                    }
                    else if (family != null)
                    {
                        StingLog.Info($"Tag family already loaded: {expectedName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to load tag family '{expectedName}'", ex);
            }
            return false;
        }
    }

    /// <summary>
    /// Family load options that allow overwriting existing families with updated versions.
    /// </summary>
    internal class TagFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true; // Always load/overwrite
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Load Tag Families — load pre-existing .rfa files from disk
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads STING tag family .rfa files from the Data/TagFamilies/ directory
    /// into the current project. Use this after tag families have been created
    /// and their Labels configured in the Family Editor.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            string tagFamilyDir = TagFamilyConfig.GetOutputDirectory();
            if (!Directory.Exists(tagFamilyDir))
            {
                TaskDialog.Show("Load Tag Families",
                    "Tag families directory not found.\n" +
                    "Run 'Create Tag Families' first to generate the .rfa files.");
                return Result.Failed;
            }

            string[] rfaFiles = Directory.GetFiles(tagFamilyDir, "STING - *.rfa");
            if (rfaFiles.Length == 0)
            {
                TaskDialog.Show("Load Tag Families",
                    $"No STING tag family .rfa files found in:\n{tagFamilyDir}\n\n" +
                    "Run 'Create Tag Families' first.");
                return Result.Failed;
            }

            // Check which are already loaded
            var loadedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                loadedFamilies.Add(fam.Name);
            }

            int loaded = 0;
            int skipped = 0;
            int failed = 0;
            var report = new StringBuilder();

            foreach (string rfaPath in rfaFiles.OrderBy(f => f))
            {
                string famName = Path.GetFileNameWithoutExtension(rfaPath);

                if (loadedFamilies.Contains(famName))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    using (Transaction tx = new Transaction(doc, $"STING Load {famName}"))
                    {
                        tx.Start();
                        bool success = doc.LoadFamily(rfaPath, new TagFamilyLoadOptions(), out Family fam);
                        tx.Commit();

                        if (success)
                        {
                            loaded++;
                            report.AppendLine($"  [OK] {famName}");
                        }
                        else
                        {
                            failed++;
                            report.AppendLine($"  [FAIL] {famName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    report.AppendLine($"  [FAIL] {famName} — {ex.Message}");
                    StingLog.Error($"Load tag family failed: {famName}", ex);
                }
            }

            TaskDialog td = new TaskDialog("Load Tag Families");
            td.MainInstruction = $"Loaded {loaded} tag families";
            td.MainContent =
                $"Found: {rfaFiles.Length} .rfa files\n" +
                $"Loaded: {loaded}\n" +
                $"Skipped: {skipped} (already loaded)\n" +
                $"Failed: {failed}\n\n" +
                (report.Length > 0 ? report.ToString() : "");
            td.Show();

            StingLog.Info($"LoadTagFamilies: loaded={loaded}, skipped={skipped}, failed={failed}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Configure Tag Labels — guided wizard to set Labels in tag families
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Guided wizard that opens each STING tag family in the Family Editor
    /// for Label configuration. Walks the user through setting the Label
    /// parameter to ASS_TAG_1_TXT for each family, one at a time.
    ///
    /// This command overcomes the Revit API limitation (no NewLabel API) by:
    ///   1. Finding all loaded STING tag families in the project
    ///   2. Opening each in the Family Editor via Document.EditFamily()
    ///   3. Showing step-by-step instructions for configuring the Label
    ///   4. Automatically reloading the family after the user saves
    ///   5. Tracking progress and allowing skip/stop at any point
    ///
    /// Workflow: Run after CreateTagFamiliesCommand to complete tag family setup.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigureTagLabelsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            // Find all loaded STING tag families
            var stingFamilies = new List<Family>();
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                if (fam.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase)
                    && fam.Name.Contains("Tag"))
                {
                    stingFamilies.Add(fam);
                }
            }

            if (stingFamilies.Count == 0)
            {
                TaskDialog.Show("Configure Tag Labels",
                    "No STING tag families loaded in this project.\n\n" +
                    "Run 'Create Tag Families' first to generate and load them.");
                return Result.Failed;
            }

            // Sort alphabetically for consistent order
            stingFamilies = stingFamilies.OrderBy(f => f.Name).ToList();

            // Introduction dialog
            TaskDialog intro = new TaskDialog("Configure Tag Labels");
            intro.MainInstruction = $"Configure Labels for {stingFamilies.Count} STING tag families";
            intro.MainContent =
                "This wizard will open each tag family in the Family Editor.\n\n" +
                "For each family, you need to:\n" +
                "  1. Select the existing Label text (usually shows 'Type Mark')\n" +
                "  2. Click 'Edit Label' in the Properties panel\n" +
                "  3. Remove the current parameter\n" +
                "  4. Add 'ASS_TAG_1_TXT' from the list\n" +
                "  5. Click OK, then Save (Ctrl+S)\n" +
                "  6. Click 'Load into Project and Close'\n\n" +
                "The wizard will guide you through each family.";
            intro.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (intro.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int configured = 0;
            int skipped = 0;
            int remaining = stingFamilies.Count;

            foreach (Family fam in stingFamilies)
            {
                remaining--;

                // Show instructions for this family
                TaskDialog step = new TaskDialog("Configure Tag Label");
                step.MainInstruction = $"Family: {fam.Name}";
                step.MainContent =
                    $"[{configured + skipped + 1}/{stingFamilies.Count}] " +
                    $"({remaining} remaining after this)\n\n" +
                    "Steps:\n" +
                    "  1. Select the Label text in the family\n" +
                    "  2. Edit Label → remove default → add ASS_TAG_1_TXT\n" +
                    "  3. Save and Load into Project\n\n" +
                    "Click 'Open' to open this family in the Editor,\n" +
                    "or 'Skip' to move to the next one.";
                step.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Open in Family Editor",
                    "Opens this tag family for Label configuration");
                step.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Skip this family",
                    "Move to the next tag family");
                step.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Stop — done for now",
                    $"Exit wizard ({configured} configured so far)");
                step.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult stepResult = step.Show();

                if (stepResult == TaskDialogResult.CommandLink3 ||
                    stepResult == TaskDialogResult.Cancel)
                {
                    break;
                }

                if (stepResult == TaskDialogResult.CommandLink2)
                {
                    skipped++;
                    continue;
                }

                // Open the family in the Family Editor
                try
                {
                    Document famDoc = doc.EditFamily(fam);
                    if (famDoc != null)
                    {
                        // Family is now open in the editor.
                        // Show a reminder dialog (non-blocking since the family editor is active)
                        TaskDialog reminder = new TaskDialog("Label Configuration");
                        reminder.MainInstruction = $"Now editing: {fam.Name}";
                        reminder.MainContent =
                            "The family is open in the Family Editor.\n\n" +
                            "Configure the Label:\n" +
                            "  1. Click the Label text element\n" +
                            "  2. In Properties → Edit Label\n" +
                            "  3. Remove the existing parameter\n" +
                            "  4. Add 'ASS_TAG_1_TXT'\n" +
                            "  5. Click OK\n" +
                            "  6. Save (Ctrl+S) → Load into Project and Close\n\n" +
                            "Click OK when you've finished configuring this family.";
                        reminder.CommonButtons = TaskDialogCommonButtons.Ok;
                        reminder.Show();

                        configured++;
                    }
                    else
                    {
                        StingLog.Warn($"EditFamily returned null for {fam.Name}");
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Error($"Failed to open family: {fam.Name}", ex);
                    TaskDialog.Show("Error",
                        $"Could not open {fam.Name}:\n{ex.Message}");
                    skipped++;
                }
            }

            // Final summary
            TaskDialog summary = new TaskDialog("Configure Tag Labels");
            summary.MainInstruction = $"Label configuration complete";
            summary.MainContent =
                $"Families opened for editing: {configured}\n" +
                $"Skipped: {skipped}\n" +
                $"Total STING tag families: {stingFamilies.Count}\n\n" +
                (configured < stingFamilies.Count
                    ? "Run this command again to configure remaining families."
                    : "All tag families have been opened for configuration.");
            summary.Show();

            StingLog.Info($"ConfigureTagLabels: configured={configured}, skipped={skipped}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Audit Tag Families — check which categories have tag families loaded
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audits tag family coverage: reports which of the 50 taggable categories
    /// have STING tag families loaded and which are missing.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuditTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Collect all loaded families
            var loadedFamilies = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                loadedFamilies[fam.Name] = fam;
            }

            // Also collect all annotation family symbols for the FindTagType approach
            var annotationTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                    fs.Category.CategoryType == CategoryType.Annotation)
                .ToList();

            var report = new StringBuilder();
            report.AppendLine("STING Tag Family Audit");
            report.AppendLine(new string('=', 50));

            int stingLoaded = 0;
            int otherTag = 0;
            int missing = 0;

            foreach (var bic in TagFamilyConfig.CategoryTemplateMap.Keys)
            {
                string famName = TagFamilyConfig.GetFamilyName(bic);
                string catDisplay = TagFamilyConfig.CategoryDisplayName.TryGetValue(bic, out string dn)
                    ? dn : bic.ToString();

                if (loadedFamilies.ContainsKey(famName))
                {
                    report.AppendLine($"  [STING] {catDisplay}");
                    stingLoaded++;
                }
                else
                {
                    // Check if ANY tag type exists for this category
                    Category cat = doc.Settings.Categories.get_Item(bic);
                    FamilySymbol anyTag = (cat != null)
                        ? TagPlacementEngine.FindTagType(doc, cat)
                        : null;

                    if (anyTag != null)
                    {
                        report.AppendLine($"  [OTHER] {catDisplay} — using '{anyTag.Family.Name}'");
                        otherTag++;
                    }
                    else
                    {
                        report.AppendLine($"  [NONE]  {catDisplay} — NO tag family loaded");
                        missing++;
                    }
                }
            }

            report.AppendLine();
            report.AppendLine(new string('-', 50));
            report.AppendLine($"STING tags: {stingLoaded}");
            report.AppendLine($"Other tags: {otherTag}");
            report.AppendLine($"Missing:    {missing}");
            report.AppendLine($"Coverage:   {(stingLoaded + otherTag) * 100 / TagFamilyConfig.CategoryTemplateMap.Count}%");

            // Check for .rfa files on disk
            string outputDir = TagFamilyConfig.GetOutputDirectory();
            int onDisk = Directory.Exists(outputDir)
                ? Directory.GetFiles(outputDir, "STING - *.rfa").Length
                : 0;
            if (onDisk > 0 && stingLoaded < onDisk)
            {
                report.AppendLine();
                report.AppendLine($"NOTE: {onDisk} .rfa files exist on disk but only {stingLoaded} loaded.");
                report.AppendLine("Run 'Load Tag Families' to load them.");
            }

            TaskDialog td = new TaskDialog("Tag Family Audit");
            td.MainInstruction = $"Tag coverage: {stingLoaded + otherTag}/{TagFamilyConfig.CategoryTemplateMap.Count} categories";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagFamilyAudit: sting={stingLoaded}, other={otherTag}, missing={missing}");
            return Result.Succeeded;
        }
    }
}
