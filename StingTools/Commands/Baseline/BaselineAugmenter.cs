// ══════════════════════════════════════════════════════════════════════════
//  BaselineAugmenter.cs — layer 3: add SHARED type parameters to families
//  that are already loaded.
//
//  TWO CONSTRAINTS SHAPE THIS FILE.
//
//  1. Document.EditFamily CANNOT run inside an open transaction. So this does
//     NOT join the mint's transaction — it runs after that transaction has
//     committed, and each EditFamily/LoadFamily round trip manages its own.
//     VisibilityEngine.Reset has the same shape for the same reason: two
//     mechanisms with opposite transaction requirements sequenced by the
//     caller rather than pretended to be one.
//
//  2. The parameters must be SHARED, not family-local.
//     FamilyManager.AddParameter(name, group, spec, isInstance) creates a
//     parameter with no GUID: two families given "the same" parameter that way
//     hold two unrelated ones, which cannot be scheduled together and which
//     the material schedule cannot read across a project. Only the
//     ExternalDefinition overload produces a parameter the schedule can use.
//
//  Families that cannot be edited — in-place, some vendor families, and
//  workshared families owned by somebody else — are EXPECTED, not
//  exceptional. Each is reported by name with the reason.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Baseline;

namespace StingTools.Commands.Baseline
{
    internal static class BaselineAugmenter
    {
        /// <summary>
        /// Shared-parameter names the baseline asks for that the shared-parameter
        /// FILE does not define. Resolved during the audit so an unresolvable
        /// name blocks Apply BEFORE any family is opened — the same class of
        /// pre-flight as a layer naming an undeclared material.
        /// </summary>
        public static List<string> UnresolvableParameters(Document doc, ProjectBaseline baseline)
        {
            var missing = new List<string>();
            if (doc == null || baseline?.FamilyParameters == null) return missing;

            var wanted = baseline.FamilyParameters
                .Where(f => f != null)
                .SelectMany(f => f.Parameters ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (wanted.Count == 0) return missing;

            var defs = SharedDefinitions(doc);
            if (defs == null)
            {
                // No shared-parameter file is a single blocking fact, not one
                // complaint per parameter.
                missing.Add("no shared parameter file is set, so none of "
                          + $"{wanted.Count} parameter(s) can be added");
                return missing;
            }

            foreach (string n in wanted)
                if (!defs.ContainsKey(n)) missing.Add($"shared parameter '{n}' is not in the shared parameter file");
            return missing;
        }

        /// <summary>
        /// Add the declared parameters. Caller must have NO open transaction —
        /// see the file header.
        /// </summary>
        public static BaselineAugmentReport Apply(Document doc, ProjectBaseline baseline,
                                          BaselineAuditResult audit)
        {
            var r = new BaselineAugmentReport();
            if (doc == null || baseline?.FamilyParameters == null) return r;

            var actionable = new HashSet<string>(
                audit.Missing.Where(f => f.Group == "Family parameters").Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);
            if (actionable.Count == 0) return r;

            var defs = SharedDefinitions(doc);
            if (defs == null)
            {
                r.Failed.Add("no shared parameter file is set");
                return r;
            }

            foreach (var set in baseline.FamilyParameters)
            {
                if (set == null || string.IsNullOrWhiteSpace(set.Category)) continue;
                string cat = set.Category.Trim();
                if (!actionable.Contains(cat)) continue;

                var names = (set.Parameters ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
                if (names.Count == 0) continue;

                foreach (var fam in FamiliesIn(doc, cat))
                    AugmentOne(doc, fam, names, defs, set, r);
            }
            return r;
        }

        private static void AugmentOne(Document doc, Family fam, List<string> names,
                                       Dictionary<string, ExternalDefinition> defs,
                                       BaselineFamilyParameterSet set, BaselineAugmentReport r)
        {
            string famName = SafeName(fam);
            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null)
                {
                    r.Failed.Add($"{famName}: EditFamily returned null");
                    return;
                }

                int added = 0;
                using (var tx = new Transaction(famDoc, "STING Baseline Family Parameters"))
                {
                    tx.Start();
                    var fm = famDoc.FamilyManager;
                    foreach (string n in names)
                    {
                        // Idempotent: a family that already has it is left
                        // alone, so a re-run is free rather than duplicating.
                        if (fm.get_Parameter(n) != null) continue;
                        if (!defs.TryGetValue(n, out var def)) continue;
                        fm.AddParameter(def, GroupFor(set.Group), set.IsInstance);
                        added++;
                    }
                    if (added == 0) { tx.RollBack(); r.FamiliesAlreadyConforming++; return; }
                    tx.Commit();
                }

                famDoc.LoadFamily(doc, new ReuseLoadOptions());
                r.FamiliesAugmented++;
                r.ParametersAdded += added;
            }
            catch (Exception ex)
            {
                // In-place families, vendor families that refuse to open, and
                // workshared families owned by another user all land here. This
                // is the expected path for a slice of any real model.
                r.Failed.Add($"{famName}: {ex.Message}");
                StingLog.Warn($"BaselineAugmenter {famName}: {ex.Message}");
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"BaselineAugmenter close {famName}: {ex.Message}"); }
            }
        }

        private static IEnumerable<Family> FamiliesIn(Document doc, string category)
        {
            List<Family> found;
            try
            {
                found = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                    .Where(f => string.Equals(f.FamilyCategory?.Name, category,
                                              StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BaselineAugmenter FamiliesIn '{category}': {ex.Message}");
                yield break;
            }
            foreach (var f in found) if (f != null) yield return f;
        }

        private static Dictionary<string, ExternalDefinition> SharedDefinitions(Document doc)
        {
            try
            {
                var file = doc?.Application?.OpenSharedParameterFile();
                if (file?.Groups == null) return null;

                var map = new Dictionary<string, ExternalDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (DefinitionGroup g in file.Groups)
                    foreach (Definition d in g.Definitions)
                        if (d is ExternalDefinition ed && !map.ContainsKey(ed.Name))
                            map[ed.Name] = ed;
                return map;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BaselineAugmenter SharedDefinitions: {ex.Message}");
                return null;
            }
        }

        /// <summary>Parameter group by name, defaulting to Identity Data. An
        /// unrecognised group is a cosmetic choice, not a reason to refuse.</summary>
        private static ForgeTypeId GroupFor(string group)
        {
            switch ((group ?? "").Trim().ToLowerInvariant())
            {
                case "construction":  return GroupTypeId.Construction;
                case "dimensions":    return GroupTypeId.Geometry;
                case "materials":     return GroupTypeId.Materials;
                case "graphics":      return GroupTypeId.Graphics;
                case "data":
                case "generalxx":     return GroupTypeId.General;
                default:              return GroupTypeId.IdentityData;
            }
        }

        private static string SafeName(Family fam)
        {
            try { return fam?.Name ?? "<unnamed family>"; }
            catch (Exception ex) { StingLog.Warn($"SafeName: {ex.Message}"); return "<unnamed family>"; }
        }

        private sealed class ReuseLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = true; return true; }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family; overwriteParameterValues = true; return true;
            }
        }
    }
}
