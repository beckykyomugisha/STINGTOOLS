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
        /// <summary>Why resolution failed, when it did. Null on success.</summary>
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
            lock (_lock) { _declared = null; }
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
        {
            var res = new TagCategoryResolution
            {
                FamilyName = family?.Name ?? "",
                ActualCategory = family?.FamilyCategory?.Name ?? ""
            };

            if (doc == null || family == null)
            {
                res.Note = "null document or family";
                return res;
            }

            EnsureLoaded();

            string key = res.FamilyName.Trim().ToUpperInvariant();
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

            // Note was previously set ONLY on the three failure paths above, so a
            // successful resolution that found a MISMATCH came back with Note null —
            // and PropagateUniversalTagCommand:296 logs exactly that field, producing
            // "'STING - Air Terminal Tag' — " with nothing after the em-dash. The
            // caller had no reason for a warning it was raising. Note is now set on
            // every outcome; callers may treat it as a description, never as a
            // success/failure flag (use DeclaredTagCategory for that).
            res.Note = res.IsMismatch
                ? $"declared '{hostCat}' → tag category '{tagCat.Name}', but the family is '{res.ActualCategory}' — will recategorise"
                : $"declared '{hostCat}' → tag category '{tagCat.Name}', already matches";
            return res;
        }

        // ── declared map ────────────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_declared != null) return;
                _declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                StingLog.Info($"TagCategoryResolver: {_declared.Count} families with a declared category, from {files.Length} config file(s)");
            }
        }

        /// <summary>
        /// The config is a human-readable sheet, not a strict CSV: a family header
        /// line followed by attribute lines, one of which carries "Category: X".
        /// The category is attributed to the most recent family header.
        /// </summary>
        private static void ParseOne(string path)
        {
            string current = null;

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var fm = FamilyLine.Match(line);
                if (fm.Success)
                {
                    current = fm.Groups["name"].Value.Trim().Trim('"', ',');
                    continue;
                }

                if (current == null) continue;

                var cm = CategoryLine.Match(line);
                if (!cm.Success) continue;

                string cat = cm.Groups["cat"].Value.Trim().Trim('"', ',');
                if (cat.Length == 0) continue;

                string key = current.ToUpperInvariant();
                if (_declared.TryGetValue(key, out string existing))
                {
                    if (!string.Equals(existing, cat, StringComparison.OrdinalIgnoreCase))
                        StingLog.Warn($"TagCategoryResolver: '{current}' declared twice with different categories " +
                                      $"('{existing}' and '{cat}') — keeping the first");
                }
                else
                {
                    _declared[key] = cat;
                }

                current = null;   // one category per family header
            }
        }

        // ── host category name → tag category in this document ──────────────

        /// <summary>
        /// Revit names tag categories "&lt;singular host&gt; Tags" — "Air Terminals"
        /// becomes "Air Terminal Tags" — but the singularisation is not uniform
        /// ("Furniture Tags", "Casework Tags", "Mechanical Equipment Tags"). Rather
        /// than encode a switch that will drift from Revit, match by name against
        /// the annotation categories the document actually has, trying the plural
        /// and singular forms. Returns null rather than guessing.
        /// </summary>
        private static Category FindTagCategory(Document doc, string hostCategoryName)
        {
            string host = hostCategoryName.Trim();
            if (host.Length == 0) return null;

            var candidates = new List<string>();
            if (host.EndsWith("Tags", StringComparison.OrdinalIgnoreCase))
                candidates.Add(host);                                   // already a tag category
            candidates.Add(host + " Tags");                             // Furniture → Furniture Tags
            if (host.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                candidates.Add(host.Substring(0, host.Length - 1) + " Tags");   // Doors → Door Tags

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
