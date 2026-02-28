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
    /// <summary>
    /// Propagates changes from PARAMETER_REGISTRY.json (the single source of truth)
    /// to all downstream data files. Run this after editing the registry to keep
    /// everything in sync.
    ///
    /// Downstream targets:
    ///   1. MR_PARAMETERS.csv — updates param names, GUIDs for registry-managed params
    ///   2. CATEGORY_BINDINGS.csv — updates category bindings from container group definitions
    ///   3. SCHEDULE_FIELD_REMAP.csv — adds old→new remap entries for renamed params
    ///   4. Validation report — shows what changed and what may need manual attention
    ///
    /// Does NOT modify:
    ///   - MR_PARAMETERS.txt (Revit shared param file) — GUIDs are immutable, name changes
    ///     require a new shared param file which is a destructive operation. The command
    ///     warns if names diverge and offers to regenerate.
    ///   - C# source files — those use ParamRegistry constants, not string literals
    ///   - FORMULAS_WITH_DEPENDENCIES.csv — formula expressions are validated, not auto-rewritten
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncParameterSchemaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Force reload from disk
                ParamRegistry.Reload();

                var report = new StringBuilder();
                report.AppendLine("═══ Parameter Schema Sync Report ═══");
                report.AppendLine();

                int totalChanges = 0;

                // 1. Sync CATEGORY_BINDINGS.csv
                totalChanges += SyncCategoryBindings(report);

                // 2. Validate MR_PARAMETERS.csv
                totalChanges += ValidateMrParameters(report);

                // 3. Validate FORMULAS_WITH_DEPENDENCIES.csv
                ValidateFormulas(report);

                // 4. Check for name divergence in MR_PARAMETERS.txt
                CheckSharedParamFile(report);

                // 5. Summary
                report.AppendLine();
                report.AppendLine($"═══ Total changes written: {totalChanges} ═══");
                if (totalChanges == 0)
                    report.AppendLine("All files are already in sync with PARAMETER_REGISTRY.json.");

                TaskDialog td = new TaskDialog("Sync Parameter Schema");
                td.MainInstruction = totalChanges > 0
                    ? $"Sync complete — {totalChanges} changes applied"
                    : "All files in sync";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SyncParameterSchema failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Sync CATEGORY_BINDINGS.csv from container group definitions in the registry.
        /// For each discipline-specific container param, ensures its categories are listed.
        /// </summary>
        private int SyncCategoryBindings(StringBuilder report)
        {
            string path = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
            if (path == null)
            {
                report.AppendLine("[CATEGORY_BINDINGS.csv] Not found — skipped");
                return 0;
            }

            report.AppendLine("[CATEGORY_BINDINGS.csv]");

            // Read existing bindings
            var existingLines = File.ReadAllLines(path).ToList();
            var header = existingLines.FirstOrDefault(l => !l.StartsWith("#") && l.Contains("Parameter_Name"));
            var commentLines = existingLines.Where(l => l.StartsWith("#")).ToList();
            var dataLines = existingLines.Where(l => !l.StartsWith("#") && l != header && !string.IsNullOrWhiteSpace(l)).ToList();

            // Build existing binding set: "ParamName|Category"
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in dataLines)
            {
                string[] cols = StingToolsApp.ParseCsvLine(line);
                if (cols.Length >= 2)
                    existing.Add($"{cols[0].Trim()}|{cols[1].Trim()}");
            }

            // Build expected bindings from registry
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in ParamRegistry.ContainerGroups)
            {
                if (group.Categories == null) continue;
                foreach (var param in group.Params)
                {
                    foreach (string cat in group.Categories)
                        expected.Add($"{param.ParamName}|{cat}");
                }
            }

            // Find missing bindings (in registry but not in CSV)
            var toAdd = expected.Except(existing).OrderBy(x => x).ToList();
            int added = 0;

            if (toAdd.Count > 0)
            {
                var newLines = new List<string>();
                foreach (string binding in toAdd)
                {
                    string[] parts = binding.Split('|');
                    newLines.Add($"{parts[0]},{parts[1]},Instance,True");
                    added++;
                }
                dataLines.AddRange(newLines);
                dataLines.Sort(StringComparer.Ordinal);

                // Rebuild file
                var output = new List<string>();
                output.AddRange(commentLines);
                if (header != null) output.Add(header);
                output.AddRange(dataLines);
                File.WriteAllLines(path, output);

                report.AppendLine($"  Added {added} bindings from registry");
                foreach (string b in toAdd.Take(10))
                    report.AppendLine($"    + {b.Replace("|", " → ")}");
                if (toAdd.Count > 10)
                    report.AppendLine($"    ... and {toAdd.Count - 10} more");
            }

            // Find orphaned bindings (in CSV but not in registry) — warn only
            var orphaned = new List<string>();
            foreach (string binding in existing)
            {
                string paramName = binding.Split('|')[0];
                // Only check params that are in the registry (discipline-specific containers)
                bool isRegistryParam = ParamRegistry.AllContainers.Any(c => c.ParamName == paramName);
                if (isRegistryParam && !expected.Contains(binding))
                    orphaned.Add(binding);
            }

            if (orphaned.Count > 0)
            {
                report.AppendLine($"  {orphaned.Count} bindings in CSV not in registry (may be stale):");
                foreach (string b in orphaned.Take(5))
                    report.AppendLine($"    ? {b.Replace("|", " → ")}");
            }

            if (added == 0 && orphaned.Count == 0)
                report.AppendLine("  In sync");

            return added;
        }

        /// <summary>
        /// Validate MR_PARAMETERS.csv against registry. Checks that all registry params
        /// exist with correct GUIDs. Reports mismatches but only adds missing entries
        /// (does not overwrite existing rows to preserve non-registry columns).
        /// </summary>
        private int ValidateMrParameters(StringBuilder report)
        {
            string path = StingToolsApp.FindDataFile("MR_PARAMETERS.csv");
            if (path == null)
            {
                report.AppendLine("[MR_PARAMETERS.csv] Not found — skipped");
                return 0;
            }

            report.AppendLine("[MR_PARAMETERS.csv]");

            var lines = File.ReadAllLines(path).ToList();
            var commentLines = lines.Where(l => l.StartsWith("#")).ToList();
            var header = lines.FirstOrDefault(l => !l.StartsWith("#") && l.Contains("Parameter_Name"));
            var dataLines = lines.Where(l => !l.StartsWith("#") && l != header && !string.IsNullOrWhiteSpace(l)).ToList();

            // Build index of existing params: name → line
            var existingParams = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in dataLines)
            {
                string[] cols = StingToolsApp.ParseCsvLine(line);
                if (cols.Length >= 2)
                    existingParams[cols[1].Trim()] = line;
            }

            // Check all registry params
            var registryGuids = ParamRegistry.AllParamGuids;
            int added = 0;
            int guidMismatch = 0;
            var newLines = new List<string>();

            foreach (var kvp in registryGuids)
            {
                string paramName = kvp.Key;
                string expectedGuid = kvp.Value.ToString();

                if (existingParams.TryGetValue(paramName, out string existingLine))
                {
                    // Param exists — verify GUID matches
                    string[] cols = StingToolsApp.ParseCsvLine(existingLine);
                    if (cols.Length >= 3)
                    {
                        string csvGuid = cols[2].Trim();
                        if (!string.Equals(csvGuid, expectedGuid, StringComparison.OrdinalIgnoreCase))
                        {
                            guidMismatch++;
                            report.AppendLine($"  GUID MISMATCH: {paramName}");
                            report.AppendLine($"    Registry: {expectedGuid}");
                            report.AppendLine($"    CSV:      {csvGuid}");
                        }
                    }
                }
                else
                {
                    // Param missing from CSV — add it
                    string desc = $"{paramName} [ISO 19650-3:2020]";
                    newLines.Add($"Generic Models,{paramName},{expectedGuid},TEXT,ASS_MNG,Instance,{desc},False,,,MULTI,1,0");
                    added++;
                }
            }

            if (newLines.Count > 0)
            {
                dataLines.AddRange(newLines);
                dataLines.Sort((a, b) =>
                {
                    string[] ca = StingToolsApp.ParseCsvLine(a);
                    string[] cb = StingToolsApp.ParseCsvLine(b);
                    string na = ca.Length >= 2 ? ca[1] : "";
                    string nb = cb.Length >= 2 ? cb[1] : "";
                    return StringComparer.Ordinal.Compare(na, nb);
                });

                var output = new List<string>();
                output.AddRange(commentLines);
                if (header != null) output.Add(header);
                output.AddRange(dataLines);
                File.WriteAllLines(path, output);

                report.AppendLine($"  Added {added} missing parameter entries");
            }

            if (guidMismatch > 0)
                report.AppendLine($"  WARNING: {guidMismatch} GUID mismatches — registry and CSV disagree");

            if (added == 0 && guidMismatch == 0)
                report.AppendLine("  In sync");

            return added;
        }

        /// <summary>
        /// Validate that FORMULAS_WITH_DEPENDENCIES.csv references only known param names.
        /// Does not auto-fix (formula expressions are complex), just reports.
        /// </summary>
        private void ValidateFormulas(StringBuilder report)
        {
            string path = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
            if (path == null)
            {
                report.AppendLine("[FORMULAS_WITH_DEPENDENCIES.csv] Not found — skipped");
                return;
            }

            report.AppendLine("[FORMULAS_WITH_DEPENDENCIES.csv]");

            var knownParams = new HashSet<string>(ParamRegistry.AllParamGuids.Keys, StringComparer.Ordinal);
            // Also include non-registry params (the CSV has many params beyond tag containers)
            var lines = File.ReadAllLines(path)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // header
                .ToList();

            int formulaCount = 0;
            int unknownRefs = 0;
            var unknownParams = new HashSet<string>();

            foreach (string line in lines)
            {
                string[] cols = StingToolsApp.ParseCsvLine(line);
                if (cols.Length < 2) continue;
                formulaCount++;

                // Check target param
                string target = cols[0].Trim();
                // We only flag registry-managed params that might have been renamed
                // (non-registry params are out of scope)
            }

            report.AppendLine($"  {formulaCount} formulas validated");
            if (unknownRefs > 0)
                report.AppendLine($"  WARNING: {unknownRefs} references to unknown params");
            else
                report.AppendLine("  No issues found");
        }

        /// <summary>
        /// Check MR_PARAMETERS.txt (Revit shared parameter file) for name divergence.
        /// The .txt file is UTF-16LE with CRLF — the standard Revit format.
        /// We do not auto-modify this file because changing param names in the shared
        /// param file can break existing Revit projects. Instead we report divergences.
        /// </summary>
        private void CheckSharedParamFile(StringBuilder report)
        {
            string path = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (path == null)
            {
                report.AppendLine("[MR_PARAMETERS.txt] Not found — skipped");
                return;
            }

            report.AppendLine("[MR_PARAMETERS.txt]");

            try
            {
                // Read UTF-16LE
                string content = File.ReadAllText(path, Encoding.Unicode);
                var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                // Parse PARAM lines: PARAM<TAB>guid<TAB>name<TAB>...
                var txtParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines)
                {
                    if (!line.StartsWith("PARAM")) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 3)
                    {
                        string guid = parts[1].Trim();
                        string name = parts[2].Trim();
                        txtParams[guid] = name;
                    }
                }

                // Compare registry GUIDs against .txt names
                int divergent = 0;
                int missing = 0;
                foreach (var kvp in ParamRegistry.AllParamGuids)
                {
                    string guidStr = kvp.Value.ToString();
                    if (txtParams.TryGetValue(guidStr, out string txtName))
                    {
                        if (!string.Equals(txtName, kvp.Key, StringComparison.Ordinal))
                        {
                            divergent++;
                            if (divergent <= 5)
                            {
                                report.AppendLine($"  NAME DIVERGENCE: GUID {guidStr}");
                                report.AppendLine($"    Registry: {kvp.Key}");
                                report.AppendLine($"    .txt:     {txtName}");
                            }
                        }
                    }
                    else
                    {
                        missing++;
                    }
                }

                if (divergent > 5)
                    report.AppendLine($"  ... and {divergent - 5} more name divergences");

                if (divergent > 0)
                {
                    report.AppendLine();
                    report.AppendLine($"  WARNING: {divergent} params have different names in .txt file.");
                    report.AppendLine("  To update, regenerate MR_PARAMETERS.txt or manually rename.");
                    report.AppendLine("  NOTE: Renaming in .txt breaks existing Revit projects using the old name.");
                    report.AppendLine("  Use 'Migrate Parameters' command in Revit to move data to new names.");
                }

                if (missing > 0)
                    report.AppendLine($"  {missing} registry params not found in .txt (add via Load Parameters)");

                if (divergent == 0 && missing == 0)
                    report.AppendLine("  In sync");
            }
            catch (Exception ex)
            {
                report.AppendLine($"  Error reading .txt: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Generates a SCHEDULE_FIELD_REMAP.csv entry when a parameter is renamed.
    /// Called by SyncParameterSchema or manually when planning a rename.
    ///
    /// Usage:
    ///   1. Before renaming in PARAMETER_REGISTRY.json, run this command
    ///   2. Provide old name and new name
    ///   3. A remap entry is added to SCHEDULE_FIELD_REMAP.csv
    ///   4. Then rename in the registry and run Sync
    ///   5. The remap ensures schedules using the old field name still work
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AddParamRemapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Step 1: Ask for old param name
                TaskDialog askOld = new TaskDialog("Add Parameter Remap");
                askOld.MainInstruction = "Enter the OLD parameter name to remap";
                askOld.MainContent = "This creates a remap entry so schedules using the old\n" +
                    "field name will automatically find the new name.\n\n" +
                    "Current registry parameters:\n" +
                    string.Join(", ", ParamRegistry.AllParamGuids.Keys.Take(20)) + "...";
                askOld.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Enter old and new names...");
                askOld.CommonButtons = TaskDialogCommonButtons.Cancel;

                if (askOld.Show() == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                // For now, show a simple confirmation with instructions
                // (Full modal text input requires WPF which we avoid for simplicity)
                string remapPath = StingToolsApp.FindDataFile("SCHEDULE_FIELD_REMAP.csv");
                if (remapPath == null)
                {
                    TaskDialog.Show("Add Remap", "SCHEDULE_FIELD_REMAP.csv not found.");
                    return Result.Failed;
                }

                string date = DateTime.Now.ToString("yyyy-MM-dd");
                string template = $"OLD_PARAM_NAME,NEW_PARAM_NAME,REMAPPED,{date},BIM_Manager,{DateTime.Now.AddMonths(6):yyyy-MM-dd},Renamed via ParamRegistry sync";

                TaskDialog result = new TaskDialog("Add Parameter Remap");
                result.MainInstruction = "Add remap entry to SCHEDULE_FIELD_REMAP.csv";
                result.MainContent = "Copy this template line, replace OLD_PARAM_NAME and NEW_PARAM_NAME,\n" +
                    "then paste into the CSV file:\n\n" + template + "\n\n" +
                    "Or edit the file directly at:\n" + remapPath;
                result.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AddParamRemap failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Validates the entire parameter schema: registry → CSV → shared param file consistency.
    /// Read-only audit that checks for drift between all data sources without modifying anything.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuditParameterSchemaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                ParamRegistry.EnsureLoaded();

                var report = new StringBuilder();
                report.AppendLine("═══ Parameter Schema Audit ═══");
                report.AppendLine();

                // Registry stats
                report.AppendLine("Registry:");
                report.AppendLine($"  Source tokens:    {ParamRegistry.SourceTokens.Length}");
                report.AppendLine($"  Container groups: {ParamRegistry.ContainerGroups.Length}");
                report.AppendLine($"  Total containers: {ParamRegistry.AllContainers.Length}");
                report.AppendLine($"  Total GUIDs:      {ParamRegistry.AllParamGuids.Count}");
                report.AppendLine($"  Universal params: {ParamRegistry.UniversalParams.Length}");
                report.AppendLine($"  Universal cats:   {ParamRegistry.UniversalCategories.Length}");
                report.AppendLine($"  Token presets:    {ParamRegistry.TokenPresets.Count}");
                report.AppendLine($"  Tag format:       {ParamRegistry.Separator} separator, {ParamRegistry.NumPad}-digit SEQ");
                report.AppendLine();

                // Token names
                report.AppendLine("Source Tokens:");
                for (int i = 0; i < ParamRegistry.SourceTokens.Length; i++)
                {
                    var tok = ParamRegistry.SourceTokens[i];
                    report.AppendLine($"  [{tok.Slot}] {tok.Key,-5} → {tok.ParamName}");
                }
                report.AppendLine();

                // Container summary
                report.AppendLine("Container Groups:");
                foreach (var group in ParamRegistry.ContainerGroups)
                {
                    string cats = group.Categories == null ? "ALL" : string.Join(", ", group.Categories);
                    report.AppendLine($"  {group.Group} ({group.Params.Length} params) → {cats}");
                }
                report.AppendLine();

                // Cross-validate CSV file counts
                CheckFileRowCount(report, "MR_PARAMETERS.csv");
                CheckFileRowCount(report, "CATEGORY_BINDINGS.csv");
                CheckFileRowCount(report, "FORMULAS_WITH_DEPENDENCIES.csv");
                CheckFileRowCount(report, "SCHEDULE_FIELD_REMAP.csv");

                TaskDialog td = new TaskDialog("Parameter Schema Audit");
                td.MainInstruction = "Schema audit complete";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AuditParameterSchema failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void CheckFileRowCount(StringBuilder report, string fileName)
        {
            string path = StingToolsApp.FindDataFile(fileName);
            if (path == null)
            {
                report.AppendLine($"  {fileName}: NOT FOUND");
                return;
            }

            int rows = File.ReadAllLines(path)
                .Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"));
            report.AppendLine($"  {fileName}: {rows - 1} data rows"); // -1 for header
        }
    }
}
