// ============================================================================
// TagCategoryResolver.cs — the declared tag category for a STING tag family.
//
// WHY THIS EXISTS
//
// PropagateUniversalTagCommand.PropagateOne resolved its target category from
// the target family itself:
//
//     ElementId targetCatId = target.FamilyCategory?.Id;      // ← the defect
//
// That preserves whatever category the family already carries. A family
// authored against the wrong template stays wrong through every future
// propagation, and re-propagating can never correct it. Observed live on
// KNP26: "STING - Air Terminal Tag" is categorised as Generic Model Tags, so
// Revit never offers it for Air Terminals and "Tag All Not Tagged" lists it
// against the wrong row.
//
// The intended category is already declared. STING_TAG_CONFIG_v5_0_*.csv
// carries, per family:
//
//     Tag Family #1: STING - Air Terminal Tag
//     TAG7: HVC_TAG_7_PARA_AT_TXT  •  Category: Air Terminals
//
// Declared "Air Terminals", actual "Generic Model Tags", and nothing reconciled
// them — the same declared-vs-actual shape as G-8 and K-16.
//
// WHAT THIS DOES
//
// Resolves family name → declared HOST category → the matching tag category in
// this document. Propagation then ENFORCES the declared standard instead of
// preserving whatever it finds, and one run corrects all 206 families rather
// than an operator opening each one.
//
// WHAT IT DELIBERATELY DOES NOT DO
//
// It never guesses. An unresolved family returns null and the caller falls back
// to the family's existing category — the pre-fix behaviour — and RECORDS the
// fact. A silent guess here would recategorise a family wrongly and, because
// recategorising rewrites the .rfa, would be expensive to undo.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>Outcome of resolving one family's tag category.</summary>
    public class TagCategoryResolution
    {
        /// <summary>Family name as loaded in the project.</summary>
        public string FamilyName { get; set; }
        /// <summary>Host category declared in the tag config, e.g. "Air Terminals". Null if undeclared.</summary>
        public string DeclaredHostCategory { get; set; }
        /// <summary>The tag category resolved from the declaration. Null if it could not be resolved.</summary>
        public Category DeclaredTagCategory { get; set; }
        /// <summary>The category the family currently carries.</summary>
        public string ActualCategory { get; set; }
        /// <summary>True when the family's current category differs from the declared one.</summary>
        public bool IsMismatch { get; set; }
        /// <summary>
        /// Which master this family takes its label from, e.g. "universal" or
        /// "LPS". Declared as "LabelMaster: LPS" in the tag config.
        ///
        /// <para>Defaults to the universal group, including for an undeclared
        /// family, because the two mistakes are not symmetric: a family wrongly
        /// INCLUDED gets the universal label and is recoverable from git, while
        /// a family wrongly EXCLUDED is silently passed over on every run
        /// forever.</para>
        /// </summary>
        public string LabelMaster { get; set; } = TagConfigDeclarations.UniversalGroup;

        /// <summary>True when this family belongs to the universal group.</summary>
        public bool Universal
            => string.Equals(LabelMaster, TagConfigDeclarations.UniversalGroup,
                             StringComparison.OrdinalIgnoreCase);
        /// <summary>
        /// Why resolution failed, or — when it succeeded and
        /// <see cref="IsMismatch"/> is true — what disagrees with what. Null only
        /// when resolution succeeded and the family already carries the declared
        /// category, i.e. when there is nothing to say.
        /// </summary>
        public string Note { get; set; }
    }

    /// <summary>
    /// Maps a STING tag family name to the tag category it is declared to serve,
    /// from the shipped STING_TAG_CONFIG_v5_0_*.csv files.
    /// </summary>
    public static class TagCategoryResolver
    {
        // family name (upper, trimmed) → declared host category name
        private static Dictionary<string, string> _declared;
        // key -> label-master group, for families that declared one. Absent
        // means the universal group; a map rather than a flag on _declared so an
        // UNDECLARED family cannot land in it by accident.
        private static Dictionary<string, string> _labelMaster;
        private static readonly object _lock = new object();

        // Anchored at both ends: unanchored, "Tag Family" could match mid-line in a
        // description cell and capture rubbish as a family name.
        private static readonly Regex FamilyLine =
            new Regex(@"^Tag\s+Family\s*#\d+\s*:\s*(?<name>.+?)\s*$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CategoryLine =
            new Regex(@"Category\s*:\s*(?<cat>[^,•|]+)",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Drops the cache so an edited tag-config CSV is picked up without restarting Revit.</summary>
        public static void Reload()
        {
            lock (_lock) { _declared = null; _labelMaster = null; }
        }

        /// <summary>Number of families with a declared category. Zero means the config was not found.</summary>
        public static int DeclaredCount
        {
            get { EnsureLoaded(); return _declared.Count; }
        }

        /// <summary>
        /// Resolve one family. Never throws; an unresolvable family comes back with
        /// <see cref="TagCategoryResolution.DeclaredTagCategory"/> null and a Note
        /// explaining why, so the caller can fall back and report rather than guess.
        /// </summary>
        public static TagCategoryResolution Resolve(Document doc, Family family)
            => Resolve(doc, family?.Name, family?.FamilyCategory, family == null ? "null family" : null);

        /// <summary>
        /// Resolve by NAME. Needed because a family document opened standalone does not
        /// reliably report the file's name through <c>OwnerFamily.Name</c>, and the
        /// declarations in STING_TAG_CONFIG_v5_0_*.csv are keyed on the name the family
        /// has as a FILE — which is the same thing Revit uses for a loaded family
        /// ("a loaded family's project name IS its .rfa FILE name").
        ///
        /// <para>Measured 2026-09-18: FixTagFamilyCategories called the Family overload
        /// for each of 212 standalone-opened .rfa files and got "no Category declared"
        /// for every one, while 137 of those file names match a declaration exactly. The
        /// audit therefore reported "0 families would change category" — a clean bill of
        /// health for a library where most families are mis-categorised. Resolving by
        /// file name fixed it: the same library now reports 103 to change, 14 correct,
        /// 69 undeclared and 20 unresolvable.</para>
        ///
        /// <para>What is NOT established is WHY the Family overload failed. The obvious
        /// explanation — that <c>OwnerFamily.Name</c> returns something other than the
        /// file name — is contradicted by the evidence: the command records both names
        /// whenever they differ, and across 206 families on 2026-09-21 it recorded
        /// ZERO differences. So the file-name route is correct and proven, and the
        /// reason the other one was not is still open. It is written here rather than
        /// left as a confident-sounding comment, because a wrong mechanism in a comment
        /// is how the next person debugs the wrong thing.</para>
        /// </summary>
        public static TagCategoryResolution Resolve(
            Document doc, string familyName, Category actualCategory, string nullNote = null)
        {
            var res = new TagCategoryResolution
            {
                FamilyName = familyName ?? "",
                ActualCategory = actualCategory?.Name ?? ""
            };

            if (doc == null || string.IsNullOrWhiteSpace(familyName))
            {
                res.Note = nullNote ?? (doc == null ? "null document" : "no family name");
                return res;
            }

            EnsureLoaded();

            // Normalised on BOTH sides: a declaration is a human name, the family
            // is a FILE, and Windows forbids characters a human name may contain.
            string key = TagCategoryNameForms.NormaliseKey(res.FamilyName);
            res.LabelMaster = LookupGroup(key);
            if (!_declared.TryGetValue(key, out string hostCat) || string.IsNullOrWhiteSpace(hostCat))
            {
                res.Note = "no Category declared in STING_TAG_CONFIG_v5_0_*.csv";
                return res;
            }

            res.DeclaredHostCategory = hostCat;

            Category tagCat = FindTagCategory(doc, hostCat);
            if (tagCat == null)
            {
                res.Note = $"declared host category '{hostCat}' has no matching tag category in this document";
                return res;
            }

            res.DeclaredTagCategory = tagCat;
            res.IsMismatch = !string.Equals(res.ActualCategory, tagCat.Name, StringComparison.OrdinalIgnoreCase);
            if (res.IsMismatch)
            {
                // Note was only written on the FAILURE paths above, so a resolved
                // mismatch — the case callers log — came back with Note null and
                // printed as "PropagateUniversalTag: 'STING - Duct Tag' — " with
                // nothing after the dash. Observed 2026-09-17: a warning that
                // names the family and then says nothing about it.
                res.Note = $"declared '{tagCat.Name}' (host '{hostCat}') but family carries " +
                           $"'{(string.IsNullOrEmpty(res.ActualCategory) ? "(none)" : res.ActualCategory)}' " +
                           "— will be recategorised to the declared category";
            }
            return res;
        }

        // ── declared map ────────────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_declared != null) return;
                _declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _labelMaster = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                string dataDir = StingToolsApp.DataPath;
                if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir))
                {
                    StingLog.Warn("TagCategoryResolver: data directory not found; no declared categories loaded");
                    return;
                }

                var files = Directory.GetFiles(dataDir, "STING_TAG_CONFIG_v5_0_*.csv");
                if (files.Length == 0)
                    StingLog.Warn("TagCategoryResolver: no STING_TAG_CONFIG_v5_0_*.csv found in " + dataDir);

                foreach (string path in files)
                {
                    try { ParseOne(path); }
                    catch (Exception ex)
                    {
                        // A malformed config must not take the command down, but it
                        // must not pass silently either — an empty map degrades
                        // propagation to its old preserve-whatever-you-find behaviour.
                        StingLog.Warn($"TagCategoryResolver: failed to parse {Path.GetFileName(path)}: {ex.Message}");
                    }
                }

                StingLog.Info($"TagCategoryResolver: {_declared.Count} families with a declared category, from {files.Length} config file(s); {_labelMaster.Count} declared a non-universal LabelMaster group");
            }
        }

        /// <summary>
        /// Reads one config file through <see cref="TagConfigDeclarations"/>,
        /// which understands BOTH dialects the config grew - the prose
        /// "Tag Family #N:" form and the healthcare "TAG_FAMILY," row form.
        /// Declarations are keyed by <see cref="TagCategoryNameForms.NormaliseKey"/>
        /// so a name written with a slash matches the file that cannot contain one.
        /// </summary>
        private static void ParseOne(string path)
        {
            foreach (var d in TagConfigDeclarations.Parse(File.ReadLines(path)))
            {
                string key = TagCategoryNameForms.NormaliseKey(d.FamilyName);
                if (key.Length == 0) continue;

                // Recorded BEFORE the duplicate-category short-circuit below. A
                // family declared in several files (every one also has a
                // _DesignConstruction twin) would otherwise have only its first
                // file consulted, so a "Universal: No" in the second would be
                // dropped without a word.
                //
                // Any single NO wins, because a family that keeps a bespoke label
                // in one config and takes the universal one in another is not a
                // preference to average - it is a mistake, and it is warned about
                // rather than silently resolved.
                if (!d.Universal)
                {
                    string existingGroup;
                    if (!_labelMaster.TryGetValue(key, out existingGroup))
                    {
                        _labelMaster[key] = d.LabelMaster;
                        StingLog.Info($"TagCategoryResolver: '{d.FamilyName}' takes its label from the " +
                                      $"'{d.LabelMaster}' master, not the universal one");
                    }
                    else if (!string.Equals(existingGroup, d.LabelMaster, StringComparison.OrdinalIgnoreCase))
                    {
                        // Two files, two different masters. Keeping the first is
                        // arbitrary, so say so - a family served by whichever
                        // config happened to be read first is a bug waiting for
                        // a directory listing to change.
                        StingLog.Warn($"TagCategoryResolver: '{d.FamilyName}' is declared under TWO label-master " +
                                      $"groups ('{existingGroup}' and '{d.LabelMaster}'). Keeping the first - " +
                                      "fix the config so every declaration of this family agrees.");
                    }
                }
                else if (_labelMaster.ContainsKey(key))
                {
                    StingLog.Warn($"TagCategoryResolver: '{d.FamilyName}' declares a LabelMaster group in one " +
                                  "config file and not in another. Keeping the group - fix the config so " +
                                  "every declaration of this family agrees.");
                }

                if (_declared.TryGetValue(key, out string existing))
                {
                    if (!string.Equals(existing, d.HostCategory, StringComparison.OrdinalIgnoreCase))
                        StingLog.Warn($"TagCategoryResolver: '{d.FamilyName}' declared twice with different " +
                                      $"categories ('{existing}' and '{d.HostCategory}') — keeping the first. " +
                                      $"Both normalise to the key '{key}'; if the two names differ only by a " +
                                      "slash, a dash or a trailing \"Tag\", rename one so the clash is visible " +
                                      "in the config.");
                    continue;
                }

                _declared[key] = d.HostCategory;
            }
        }

        /// <summary>
        /// Whether a family declares "Universal: No", without resolving its
        /// category. EnsureLoaded plus one hash lookup.
        ///
        /// <para>Exists because asking <see cref="Resolve(Document, Family)"/>
        /// for this costs a full category resolution, and
        /// <c>FindTagCategory</c> enumerates every category in the document on
        /// every call. Asking it 206 times to populate a confirmation dialog
        /// froze Revit before the dialog could appear - measured 2026-09-22,
        /// and the command never logged a line because it never got that far.
        /// Callers that only need the flag must use this.</para>
        /// </summary>
        public static bool IsNonUniversal(string familyName)
            => !string.Equals(LabelMasterGroup(familyName), TagConfigDeclarations.UniversalGroup,
                              StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Which master this family takes its label from. EnsureLoaded plus one
        /// hash lookup - no category resolution.
        ///
        /// <para>An undeclared family belongs to the universal group. That is
        /// the lenient direction on purpose: wrongly included is recoverable
        /// from git, wrongly excluded is silently passed over forever.</para>
        /// </summary>
        public static string LabelMasterGroup(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return TagConfigDeclarations.UniversalGroup;
            EnsureLoaded();
            return LookupGroup(TagCategoryNameForms.NormaliseKey(familyName));
        }

        private static string LookupGroup(string key)
        {
            string g;
            return _labelMaster.TryGetValue(key, out g) ? g : TagConfigDeclarations.UniversalGroup;
        }

        private static Category FindTagCategory(Document doc, string hostCategoryName)
        {
            string host = hostCategoryName.Trim();
            if (host.Length == 0) return null;

            var candidates = TagCategoryNameForms.Candidates(host);

            var annotation = new List<Category>();
            foreach (Category c in doc.Settings.Categories)
            {
                if (c != null && c.CategoryType == CategoryType.Annotation)
                    annotation.Add(c);
            }

            foreach (string want in candidates)
            {
                var hit = annotation.FirstOrDefault(c =>
                    string.Equals(c.Name, want, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }

            return null;
        }
    }
}
