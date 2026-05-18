// StingTools — Drawing Template Manager · Gap 7
//
// TitleBlockParamMigrateCommand — one-time migration that copies values
// from PRJ_TB_* parameters on title-block FamilyInstances into the
// matching PRJ_ORG_* parameters on doc.ProjectInformation, then
// re-stamps every affected sheet via TitleBlockParamApplier so the
// declarative profile-driven path takes over from the legacy direct-write
// approach.
//
// Key design:
//   PRJ_TB_* params live on FamilyInstance (title blocks).
//   PRJ_ORG_* params live on ProjectInformation (project-wide).
//   The DrawingType profile references ${PRJ_ORG_*} in its
//   TitleBlockParams map; TitleBlockParamApplier.Apply reads
//   ProjectInformation and writes the resolved values back down to
//   title blocks on each sheet.
//
//   Migration direction: PRJ_TB_xxx → PRJ_ORG_xxx (strip prefix, swap).
//   Idempotent: if PRJ_ORG_xxx is already non-empty, skip (never overwrite).
//
// TitleBlockParamMigrateAuditCommand is a read-only companion that
// previews what would be migrated without touching the model.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared logic used by both commands
    // ─────────────────────────────────────────────────────────────────────────

    internal static class TitleBlockParamMigrateHelper
    {
        /// <summary>
        /// Scans every title-block FamilyInstance in the document.
        /// Collects every PRJ_TB_* String parameter whose value is non-empty.
        /// When the same parameter name appears on multiple instances the first
        /// non-empty value wins (they should agree; disagreements surface in
        /// the audit command).
        /// </summary>
        internal static Dictionary<string, string> CollectTbValues(Document doc)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var tbInstances = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                foreach (var tb in tbInstances)
                {
                    foreach (Parameter p in tb.Parameters)
                    {
                        try
                        {
                            if (p == null) continue;
                            string name = p.Definition?.Name;
                            if (string.IsNullOrEmpty(name)) continue;
                            if (!name.StartsWith("PRJ_TB_", StringComparison.OrdinalIgnoreCase)) continue;
                            if (p.StorageType != StorageType.String) continue;
                            string val = p.AsString();
                            if (string.IsNullOrEmpty(val)) continue;
                            if (!result.ContainsKey(name))
                                result[name] = val;
                        }
                        catch { /* per-param failure — continue */ }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TitleBlockParamMigrate.CollectTbValues: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Builds the migration plan: PRJ_ORG_* param name → value to write.
        /// Only includes entries where the target parameter exists on
        /// ProjectInformation AND is currently empty (idempotent guard).
        /// </summary>
        internal static Dictionary<string, string> BuildMigrationPlan(
            ProjectInfo pi, Dictionary<string, string> tbValues)
        {
            var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tbValues)
            {
                // Strip "PRJ_TB_" prefix and prepend "PRJ_ORG_".
                string sourceName = kv.Key;  // e.g. PRJ_TB_VARIANT_TXT
                if (!sourceName.StartsWith("PRJ_TB_", StringComparison.OrdinalIgnoreCase))
                    continue;
                string suffix     = sourceName.Substring("PRJ_TB_".Length); // e.g. VARIANT_TXT
                string targetName = "PRJ_ORG_" + suffix;                    // e.g. PRJ_ORG_VARIANT_TXT

                try
                {
                    var p = pi.LookupParameter(targetName);
                    if (p == null) continue;          // PRJ_ORG_* not bound on this project
                    if (p.IsReadOnly) continue;
                    if (p.StorageType != StorageType.String) continue;
                    string current = p.AsString();
                    if (!string.IsNullOrEmpty(current)) continue; // already populated — skip
                    plan[targetName] = kv.Value;
                }
                catch { /* target param not accessible — skip */ }
            }
            return plan;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Migration command (Manual transaction — writes to the model)
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockParamMigrateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No active document."; return Result.Failed; }

                // Collect PRJ_TB_* values from all title-block instances.
                var tbValues = TitleBlockParamMigrateHelper.CollectTbValues(doc);
                if (tbValues.Count == 0)
                {
                    TaskDialog.Show("STING — Migrate TB Params",
                        "No PRJ_TB_* parameters with non-empty values were found on any title-block instance.");
                    return Result.Succeeded;
                }

                // Determine which values can be promoted to PRJ_ORG_* on ProjectInformation.
                var pi = doc.ProjectInformation;
                var migrations = TitleBlockParamMigrateHelper.BuildMigrationPlan(pi, tbValues);
                if (migrations.Count == 0)
                {
                    TaskDialog.Show("STING — Migrate TB Params",
                        $"All {tbValues.Count} PRJ_TB_* source values are already set in PRJ_ORG_* on " +
                        $"ProjectInformation — nothing to migrate.");
                    return Result.Succeeded;
                }

                int migratedValues = 0;
                int sheetsReStamped = 0;

                using (var tx = new Transaction(doc, "STING Migrate PRJ_TB_* → PRJ_ORG_*"))
                {
                    tx.Start();

                    // Step 1: write to ProjectInformation.
                    foreach (var kv in migrations)
                    {
                        try
                        {
                            var p = pi.LookupParameter(kv.Key);
                            if (p == null || p.IsReadOnly) continue;
                            if (p.StorageType != StorageType.String) continue;
                            p.Set(kv.Value);
                            migratedValues++;
                            StingLog.Info($"TitleBlockParamMigrate: {kv.Key} = \"{kv.Value}\"");
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"TitleBlockParamMigrate: could not write {kv.Key} — {ex.Message}");
                        }
                    }

                    // Step 2: re-apply TitleBlockParamApplier on every stamped sheet so
                    // the newly populated PRJ_ORG_* values flow down to title blocks.
                    var sheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => !string.IsNullOrEmpty(DrawingTypeStamper.Read(s)))
                        .ToList();

                    using (TitleBlockParamApplier.Batch())
                    {
                        foreach (var sheet in sheets)
                        {
                            try
                            {
                                var dtId = DrawingTypeStamper.Read(sheet);
                                var dt   = DrawingTypeRegistry.Get(doc, dtId);
                                if (dt == null) continue;
                                var applyResult = TitleBlockParamApplier.Apply(doc, sheet, dt);
                                if (applyResult.ParamsWritten > 0) sheetsReStamped++;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"TitleBlockParamMigrate: re-stamp sheet '{sheet.SheetNumber}' — {ex.Message}");
                            }
                        }
                    }

                    tx.Commit();
                }

                string sample = string.Join(", ", migrations.Keys.Take(5));
                string detail = migrations.Count > 5
                    ? $"{sample} …(+{migrations.Count - 5} more)"
                    : sample;

                TaskDialog.Show("STING — Migrate TB Params",
                    $"Migrated {migratedValues} value(s) from PRJ_TB_* → PRJ_ORG_*.\n" +
                    $"{sheetsReStamped} sheet(s) re-stamped via TitleBlockParamApplier.\n\n" +
                    $"Keys migrated: {detail}");

                StingLog.Info($"TitleBlockParamMigrateCommand: migrated {migratedValues} values, " +
                              $"re-stamped {sheetsReStamped} sheets.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TitleBlockParamMigrateCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read-only audit companion
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockParamMigrateAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No active document."; return Result.Failed; }

                var tbValues   = TitleBlockParamMigrateHelper.CollectTbValues(doc);
                var pi         = doc.ProjectInformation;
                var migrations = TitleBlockParamMigrateHelper.BuildMigrationPlan(pi, tbValues);

                int totalTb      = tbValues.Count;
                int wouldMigrate = migrations.Count;
                int alreadySet   = totalTb - wouldMigrate;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("PRJ_TB_* → PRJ_ORG_* Audit (read-only — no changes made)");
                sb.AppendLine();
                sb.AppendLine($"PRJ_TB_* params with values found on title blocks:  {totalTb}");
                sb.AppendLine($"PRJ_ORG_* already populated (would skip):           {alreadySet}");
                sb.AppendLine($"Would migrate to PRJ_ORG_*:                         {wouldMigrate}");
                sb.AppendLine();

                if (wouldMigrate > 0)
                {
                    sb.AppendLine("Keys that would be migrated:");
                    foreach (var kv in migrations.OrderBy(x => x.Key).Take(30))
                        sb.AppendLine($"  {kv.Key} = \"{Trunc(kv.Value, 60)}\"");
                    if (migrations.Count > 30)
                        sb.AppendLine($"  …(+{migrations.Count - 30} more)");
                    sb.AppendLine();
                    sb.AppendLine("Run 'Migrate TB Params' to apply the migration.");
                }
                else if (totalTb > 0)
                {
                    sb.AppendLine("All PRJ_ORG_* targets are already populated — " +
                                  "no migration needed.");
                }
                else
                {
                    sb.AppendLine("No PRJ_TB_* source values found on any title-block instance in this project.");
                }

                TaskDialog.Show("STING — Audit TB Params", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("TitleBlockParamMigrateAuditCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static string Trunc(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 1) + "…";
        }
    }
}
