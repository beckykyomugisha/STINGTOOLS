// StingTools — migrate renamed seed types in a project that already holds the seed (MG-2).
//
// Build Seeds reloads a seed family, and a family reload never deletes a type the
// project holds. So when a seed variant is renamed (MAP_THEATRE_PANEL ->
// THEATRE_GAS_PANEL), every project built before the rename keeps the old type and
// its instances. A variant's "renamedFrom" declares the old name(s); this executes
// the plan SeedTypeRenames (Revit-free, unit-tested) makes for each family:
//
//   old only      -> rename the old type, re-apply the variant's type parameters
//   old and new   -> move every old instance to the new type, delete the old type
//
// Migrated instances also take the new type's product code, because the seed
// declares ASS_PRODCT_COD_TXT as an INSTANCE parameter: the old type's default stays
// on each instance after a rename or a type change, and MgasNetwork reads it.
// It runs in every Build Seeds mode, including Missing Only, since that is the default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public sealed class SeedTypeMigrationReport
    {
        public int Types { get; set; }
        public int Instances { get; set; }
        public List<string> Messages { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SeedTypeMigrator
    {
        private const string ProductCodeParam = "ASS_PRODCT_COD_TXT";

        /// <summary>Migrates every renamed variant in the seed spec at <paramref name="jsonPath"/>.</summary>
        public static SeedTypeMigrationReport MigrateFromFile(Document doc, string jsonPath, SeedTypeMigrationReport report = null)
        {
            report = report ?? new SeedTypeMigrationReport();
            if (doc == null || string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) return report;

            SymbolLibrary lib;
            try { lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(jsonPath)); }
            catch (Exception ex)
            {
                report.Warnings.Add($"Type migration: '{Path.GetFileName(jsonPath)}' unreadable — {ex.Message}");
                return report;
            }

            foreach (var def in lib?.Symbols ?? new List<SymbolDefinition>())
            {
                if (def?.TypeVariants == null || string.IsNullOrWhiteSpace(def.Id)) continue;
                if (!def.TypeVariants.Any(v => v?.RenamedFrom != null && v.RenamedFrom.Count > 0)) continue;
                try { MigrateFamily(doc, def, report); }
                catch (Exception ex)
                {
                    report.Warnings.Add($"Type migration: {def.Id} failed — {ex.Message}");
                    StingLog.Error($"SeedTypeMigrator: {def.Id}", ex);
                }
            }
            return report;
        }

        private static void MigrateFamily(Document doc, SymbolDefinition def, SeedTypeMigrationReport report)
        {
            var fam = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, def.Id, StringComparison.OrdinalIgnoreCase));
            if (fam == null) return;   // not in this project — nothing was ever built from the old name

            var byName = new Dictionary<string, FamilySymbol>(StringComparer.Ordinal);
            foreach (var id in fam.GetFamilySymbolIds())
                if (doc.GetElement(id) is FamilySymbol s && !byName.ContainsKey(s.Name)) byName[s.Name] = s;

            var steps = SeedTypeRenames.Plan(def.TypeVariants, byName.Keys);
            if (steps.Count == 0) return;

            // A throw rolls the whole family back, so the counts must roll back with it.
            int typesBefore = report.Types, instancesBefore = report.Instances;
            int messagesBefore = report.Messages.Count;
            try
            {
                MigrateSteps(doc, def, steps, byName, report);
            }
            catch
            {
                report.Types = typesBefore;
                report.Instances = instancesBefore;
                report.Messages.RemoveRange(messagesBefore, report.Messages.Count - messagesBefore);
                throw;
            }
        }

        private static void MigrateSteps(Document doc, SymbolDefinition def, List<SeedTypeMigrationStep> steps,
            Dictionary<string, FamilySymbol> byName, SeedTypeMigrationReport report)
        {
            using (var tx = new Transaction(doc, "STING Migrate Renamed Seed Types"))
            {
                tx.Start();
                foreach (var step in steps)
                {
                    var oldSym = byName[step.OldName];
                    string newCode = step.Variant?.Parameters != null
                        && step.Variant.Parameters.TryGetValue(ProductCodeParam, out var c) ? c : null;

                    if (step.Action == SeedTypeMigrationAction.Rename)
                    {
                        oldSym.Name = step.NewName;
                        ApplyTypeParams(oldSym, step.Variant, report);
                        byName.Remove(step.OldName);
                        byName[step.NewName] = oldSym;
                        int n = 0;
                        foreach (var inst in InstancesOf(doc, oldSym)) { Restamp(inst, newCode, report); n++; }
                        report.Types++;
                        report.Instances += n;
                        report.Messages.Add($"{def.Id}: renamed type {step.OldName} -> {step.NewName} ({n} instance(s)).");
                    }
                    else if (step.Action == SeedTypeMigrationAction.Merge)
                    {
                        var newSym = byName[step.NewName];
                        if (!newSym.IsActive) newSym.Activate();
                        int moved = 0, failed = 0;
                        foreach (var inst in InstancesOf(doc, oldSym))
                        {
                            try
                            {
                                inst.ChangeTypeId(newSym.Id);
                                Restamp(inst, newCode, report);
                                moved++;
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                report.Warnings.Add($"{def.Id}: instance {inst.Id} could not move {step.OldName} -> {step.NewName} — {ex.Message}");
                            }
                        }
                        report.Instances += moved;
                        if (failed == 0)
                        {
                            doc.Delete(oldSym.Id);
                            byName.Remove(step.OldName);
                            report.Types++;
                            report.Messages.Add($"{def.Id}: moved {moved} instance(s) {step.OldName} -> {step.NewName}; deleted {step.OldName}.");
                        }
                        else
                        {
                            report.Warnings.Add($"{def.Id}: kept type {step.OldName} — {failed} instance(s) still on it; swap them to {step.NewName} by hand, then purge it.");
                        }
                    }
                }
                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException($"transaction ended {status}; nothing was migrated");
            }
        }

        private static List<FamilyInstance> InstancesOf(Document doc, FamilySymbol sym)
            => new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                .WherePasses(new FamilyInstanceFilter(doc, sym.Id))
                .Cast<FamilyInstance>().ToList();

        /// <summary>
        /// Writes the variant's values onto the renamed type. Only TYPE parameters are
        /// reachable from a FamilySymbol; instance parameters are handled per instance.
        /// </summary>
        private static void ApplyTypeParams(FamilySymbol sym, TypeVariantDefinition variant, SeedTypeMigrationReport report)
        {
            if (variant?.Parameters == null) return;
            foreach (var kv in variant.Parameters)
            {
                try
                {
                    var p = sym.LookupParameter(kv.Key);
                    if (p == null || p.IsReadOnly) continue;
                    if (p.StorageType == StorageType.String) p.Set(kv.Value ?? "");
                    else p.SetValueString(kv.Value ?? "");
                }
                catch (Exception ex)
                {
                    report.Warnings.Add($"{sym.Name}: type parameter {kv.Key} not set — {ex.Message}");
                }
            }
        }

        private static void Restamp(FamilyInstance inst, string newCode, SeedTypeMigrationReport report)
        {
            if (string.IsNullOrEmpty(newCode)) return;
            try
            {
                var p = inst.LookupParameter(ProductCodeParam);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return;
                if (SeedTypeRenames.ShouldRestamp(p.AsString(), newCode)) p.Set(newCode);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"instance {inst.Id}: {ProductCodeParam} not restamped — {ex.Message}");
            }
        }
    }
}
