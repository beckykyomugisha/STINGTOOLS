// StingTools — the Revit-free half of the seed type-rename migration (MG-2).
// Unit-tested by StingTools.Tags.Tests; SeedTypeMigrator (Revit-bound) executes it.
//
// A seed variant that was renamed declares its old name(s) in "renamedFrom". For a
// project that already holds the family, the old type is either renamed (the new
// name is free) or merged (both exist: instances move to the new type, the old one
// is deleted). A name the spec still declares is never migrated away.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Symbols
{
    public enum SeedTypeMigrationAction { None, Rename, Merge }

    public sealed class SeedTypeMigrationStep
    {
        public string OldName { get; set; }
        public string NewName { get; set; }
        public SeedTypeMigrationAction Action { get; set; }
        /// <summary>The variant the step migrates onto (its params are re-applied on a rename).</summary>
        public TypeVariantDefinition Variant { get; set; }
    }

    public static class SeedTypeRenames
    {
        /// <summary>What to do with one old name, given which of the two types the family holds.</summary>
        public static SeedTypeMigrationAction Decide(bool oldExists, bool newExists)
        {
            if (!oldExists) return SeedTypeMigrationAction.None;
            return newExists ? SeedTypeMigrationAction.Merge : SeedTypeMigrationAction.Rename;
        }

        /// <summary>
        /// The ordered steps for a family whose types are <paramref name="existingTypeNames"/>.
        /// After a rename the new name exists, so a second old name for the same variant merges.
        /// </summary>
        public static List<SeedTypeMigrationStep> Plan(
            IEnumerable<TypeVariantDefinition> variants, IEnumerable<string> existingTypeNames)
        {
            var steps = new List<SeedTypeMigrationStep>();
            var list = (variants ?? Enumerable.Empty<TypeVariantDefinition>()).Where(v => v != null).ToList();
            var present = new HashSet<string>(existingTypeNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var declared = new HashSet<string>(
                list.Select(v => v.Name).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

            foreach (var v in list)
            {
                if (string.IsNullOrWhiteSpace(v.Name) || v.RenamedFrom == null) continue;
                foreach (var raw in v.RenamedFrom)
                {
                    string old = (raw ?? "").Trim();
                    if (old.Length == 0 || old == v.Name || declared.Contains(old)) continue;
                    var action = Decide(present.Contains(old), present.Contains(v.Name));
                    if (action == SeedTypeMigrationAction.None) continue;
                    steps.Add(new SeedTypeMigrationStep { OldName = old, NewName = v.Name, Action = action, Variant = v });
                    present.Remove(old);
                    present.Add(v.Name);
                }
            }
            return steps;
        }

        /// <summary>
        /// A migrated instance takes the new type's value of an instance parameter (the
        /// product code): the old value was the old type's default, not a user choice.
        /// </summary>
        public static bool ShouldRestamp(string current, string target)
            => !string.IsNullOrEmpty(target) && !string.Equals(current ?? "", target, StringComparison.Ordinal);

        /// <summary>Problems with the renamedFrom declarations of one seed; empty when sound.</summary>
        public static List<string> Validate(string seedLabel, IEnumerable<TypeVariantDefinition> variants)
        {
            var problems = new List<string>();
            var list = (variants ?? Enumerable.Empty<TypeVariantDefinition>()).Where(v => v != null).ToList();
            var declared = new HashSet<string>(
                list.Select(v => v.Name).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var v in list)
            {
                if (v.RenamedFrom == null) continue;
                foreach (var raw in v.RenamedFrom)
                {
                    string old = (raw ?? "").Trim();
                    if (old.Length == 0)
                    { problems.Add($"{seedLabel}: {v.Name} has a blank renamedFrom."); continue; }
                    if (old == v.Name)
                        problems.Add($"{seedLabel}: {v.Name} names itself in renamedFrom.");
                    else if (declared.Contains(old))
                        problems.Add($"{seedLabel}: {v.Name} renamedFrom '{old}', which the seed still declares — the migration would delete a live type.");
                    if (owner.TryGetValue(old, out var other) && other != v.Name)
                        problems.Add($"{seedLabel}: '{old}' is claimed by both {other} and {v.Name}.");
                    else owner[old] = v.Name;
                }
            }
            return problems;
        }
    }
}
