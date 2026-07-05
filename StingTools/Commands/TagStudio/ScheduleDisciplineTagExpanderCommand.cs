// ============================================================================
// ScheduleDisciplineTagExpanderCommand.cs — per-category tag-expander schedules.
//
//   *** Phase 195 — Universal Tag pivot, Task 3 ***
//
// The universal tag is discipline-agnostic (Task 1), so the discipline engineering
// data that used to live on bespoke tag tiers moves OFF the tag and INTO schedules.
// This command auto-builds one ViewSchedule per model category from
// SCHEDULE_SPEC_all_disciplines.json (207 entries — one per tag-family category
// across ARCH/GEN/HEALTH/MEP/STR):
//
//   • Column 1 = ASS_TAG_1_TXT  (the tag — links drawing ↔ schedule)
//   • then the entry's sheet_columns (the dropped discipline params)
//   • then the built-in Comments column
//
// A reader sees the compact universal tag in the drawing and the expanded
// properties in the schedule placed alongside. A "Full as-built" variant uses
// full_columns instead of sheet_columns.
//
// Category derivation is RUNTIME: the spec's family name is joined to
// LABEL_DEFINITIONS.json's category_labels (family_name / csv_family_alias /
// plural Revit key) to get the Revit model-category DISPLAY NAME, then resolved
// to a live Category in the open document. Entries whose category can't be
// resolved (or isn't schedulable) are skipped and reported — never fatal.
// Entries with empty sheet_columns are skipped (the universal tag already
// covers them). Families that share a model category (e.g. the 16 healthcare
// "Medical Equipment" families) collapse to ONE schedule with the de-duplicated
// union of their columns.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleDisciplineTagExpanderCommand : IExternalCommand
    {
        private const string SchedulePrefix = "STING Tag Expander - ";
        // Keep schedules SIMPLE — cap the union of discipline columns per category.
        private const int MaxColumns = 24;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // ── Load the spec + the family→category map ──
            JObject spec = LoadJson("SCHEDULE_SPEC_all_disciplines.json");
            if (spec == null)
            {
                TaskDialog.Show("Schedule Tag Expander",
                    "SCHEDULE_SPEC_all_disciplines.json not found in the data directory.");
                return Result.Failed;
            }
            Dictionary<string, string> familyToCategory = BuildFamilyCategoryMap();
            if (familyToCategory.Count == 0)
            {
                TaskDialog.Show("Schedule Tag Expander",
                    "Could not read LABEL_DEFINITIONS.json category_labels — cannot map families to Revit categories.");
                return Result.Failed;
            }

            // ── Mode: sheet columns / full as-built / both ──
            var modeDlg = new TaskDialog("Schedule Tag Expander");
            modeDlg.MainInstruction = "Which schedules to build?";
            modeDlg.MainContent =
                "One ViewSchedule per model category. Column 1 = ASS_TAG_1_TXT (the tag), " +
                "then the discipline columns, then Comments.";
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Sheet columns (compact — recommended)",
                "The curated discipline columns dropped from the universal tag (sheet_columns).");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Full as-built columns",
                "The wider full_columns set (all discipline + fabrication params).");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Both (sheet + full)",
                "Two schedules per category: a compact sheet schedule and a wide as-built schedule.");
            var mode = modeDlg.Show();
            if (mode == TaskDialogResult.Cancel) return Result.Cancelled;
            bool doSheet = mode == TaskDialogResult.CommandLink1 || mode == TaskDialogResult.CommandLink3;
            bool doFull  = mode == TaskDialogResult.CommandLink2 || mode == TaskDialogResult.CommandLink3;

            // ── Group entries by resolved category display name ──
            // categoryDisplay → (sheetCols union, fullCols union)
            var byCategory = new Dictionary<string, CategoryPlan>(StringComparer.OrdinalIgnoreCase);
            var unmatchedFamilies = new List<string>();
            int emptySkipped = 0;

            foreach (var prop in spec.Properties())
            {
                JObject e = prop.Value as JObject;
                if (e == null) continue;
                string family = e.Value<string>("family") ?? "";
                var sheetCols = ReadStrArray(e, "sheet_columns");
                var fullCols  = ReadStrArray(e, "full_columns");

                if (sheetCols.Count == 0 && fullCols.Count == 0) { emptySkipped++; continue; }

                string catDisplay = ResolveCategoryName(family, familyToCategory);
                if (catDisplay == null) { unmatchedFamilies.Add(family); continue; }

                if (!byCategory.TryGetValue(catDisplay, out var plan))
                {
                    plan = new CategoryPlan { CategoryDisplay = catDisplay };
                    byCategory[catDisplay] = plan;
                }
                foreach (var c in sheetCols) plan.AddSheet(c);
                foreach (var c in fullCols) plan.AddFull(c);
            }

            if (byCategory.Count == 0)
            {
                TaskDialog.Show("Schedule Tag Expander",
                    $"No schedulable categories resolved from the spec.\n" +
                    $"Skipped empty entries: {emptySkipped}, unmatched families: {unmatchedFamilies.Count}.");
                return Result.Cancelled;
            }

            // ── Resolve category display names to live categories ──
            var catByName = BuildDocCategoryIndex(doc);
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            int created = 0, skippedExisting = 0, unresolvedCat = 0, notSchedulable = 0;
            int truncatedCols = 0;
            var unresolvedNames = new List<string>();

            var progress = StingProgressDialog.Show("Schedule Tag Expander", byCategory.Count);
            try
            {
                using (var tx = new Transaction(doc, "STING Build Tag Expander Schedules"))
                {
                    tx.Start();
                    foreach (var plan in byCategory.Values.OrderBy(p => p.CategoryDisplay))
                    {
                        progress.Increment($"Schedule: {plan.CategoryDisplay}");

                        if (!catByName.TryGetValue(plan.CategoryDisplay, out Category cat) || cat == null)
                        {
                            unresolvedCat++;
                            unresolvedNames.Add(plan.CategoryDisplay);
                            continue;
                        }

                        if (doSheet)
                        {
                            var r = BuildSchedule(doc, cat, plan.CategoryDisplay, plan.SheetColumns,
                                existing, isFull: false);
                            Tally(r, ref created, ref skippedExisting, ref notSchedulable, ref truncatedCols);
                        }
                        if (doFull)
                        {
                            var r = BuildSchedule(doc, cat, plan.CategoryDisplay, plan.FullColumns,
                                existing, isFull: true);
                            Tally(r, ref created, ref skippedExisting, ref notSchedulable, ref truncatedCols);
                        }
                    }
                    tx.Commit();
                }
            }
            finally { progress.Close(); }

            var td = new TaskDialog("Schedule Tag Expander — done");
            td.MainInstruction = $"Created {created} schedule(s)";
            td.MainContent =
                $"Categories planned:      {byCategory.Count}\n" +
                $"Schedules created:       {created}\n" +
                $"Skipped (already exist): {skippedExisting}\n" +
                $"Category not in project: {unresolvedCat}\n" +
                $"Not schedulable:         {notSchedulable}\n" +
                $"Empty entries skipped:   {emptySkipped}\n" +
                $"Unmatched families:      {unmatchedFamilies.Count}\n" +
                (truncatedCols > 0 ? $"Column-capped categories: {truncatedCols} (>{MaxColumns} cols)\n" : "") +
                (unresolvedNames.Count > 0
                    ? "\nNot in project: " + string.Join(", ", unresolvedNames.Take(12)) +
                      (unresolvedNames.Count > 12 ? " …" : "")
                    : "");
            td.Show();

            StingLog.Info($"ScheduleTagExpander: created={created}, skippedExisting={skippedExisting}, " +
                $"unresolvedCat={unresolvedCat}, notSchedulable={notSchedulable}, emptySkipped={emptySkipped}, " +
                $"unmatched={unmatchedFamilies.Count}, truncated={truncatedCols}");
            if (unmatchedFamilies.Count > 0)
                StingLog.Info("ScheduleTagExpander unmatched families: " + string.Join(" | ", unmatchedFamilies));
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Schedule construction
        // ──────────────────────────────────────────────────────────────────

        private class BuildOutcome { public bool Created; public bool SkippedExisting; public bool NotSchedulable; public bool Truncated; }

        private static void Tally(BuildOutcome r, ref int created, ref int skippedExisting,
            ref int notSchedulable, ref int truncated)
        {
            if (r == null) return;
            if (r.Created) created++;
            if (r.SkippedExisting) skippedExisting++;
            if (r.NotSchedulable) notSchedulable++;
            if (r.Truncated) truncated++;
        }

        private BuildOutcome BuildSchedule(Document doc, Category cat, string catDisplay,
            List<string> columns, HashSet<string> existingNames, bool isFull)
        {
            var outcome = new BuildOutcome();
            string name = SchedulePrefix + catDisplay + (isFull ? " (Full)" : "");
            if (existingNames.Contains(name)) { outcome.SkippedExisting = true; return outcome; }

            ViewSchedule sched;
            try
            {
                sched = ViewSchedule.CreateSchedule(doc, cat.Id);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ScheduleTagExpander: CreateSchedule '{catDisplay}': {ex.Message}");
                outcome.NotSchedulable = true;
                return outcome;
            }

            try { sched.Name = name; }
            catch (Exception ex) { StingLog.Warn($"ScheduleTagExpander: name '{name}': {ex.Message}"); }

            ScheduleDefinition sdef = sched.Definition;

            // Build a name → SchedulableField index for this category.
            var byName = new Dictionary<string, SchedulableField>(StringComparer.OrdinalIgnoreCase);
            foreach (SchedulableField sf in sdef.GetSchedulableFields())
            {
                string n;
                try { n = sf.GetName(doc); } catch { continue; }
                if (!string.IsNullOrEmpty(n) && !byName.ContainsKey(n)) byName[n] = sf;
            }

            // Ordered, de-duplicated column list: TAG_1, then columns (capped), then Comments.
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Want(string p) { if (!string.IsNullOrEmpty(p) && seen.Add(p)) ordered.Add(p); }

            Want(ParamRegistry.TAG1); // ASS_TAG_1_TXT
            int capBudget = MaxColumns;
            foreach (var c in columns)
            {
                if (capBudget <= 0) { outcome.Truncated = true; break; }
                if (seen.Contains(c)) continue;
                Want(c);
                capBudget--;
            }

            int added = 0;
            foreach (var pname in ordered)
            {
                if (byName.TryGetValue(pname, out var sf))
                {
                    try { sdef.AddField(sf); added++; }
                    catch (Exception ex) { StingLog.Warn($"ScheduleTagExpander AddField '{pname}' → {catDisplay}: {ex.Message}"); }
                }
                // params not bound to this category simply won't be in byName → silently omitted
            }

            // Built-in Comments column last.
            SchedulableField comments = FindBuiltIn(sdef, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comments");
            if (comments != null)
            {
                try { sdef.AddField(comments); added++; }
                catch (Exception ex) { StingLog.Warn($"ScheduleTagExpander AddField Comments → {catDisplay}: {ex.Message}"); }
            }

            existingNames.Add(name);
            outcome.Created = true;
            StingLog.Info($"ScheduleTagExpander: '{name}' created with {added} fields ({cat.Name})");
            return outcome;
        }

        private static SchedulableField FindBuiltIn(ScheduleDefinition sdef, Document doc,
            BuiltInParameter bip, string fallbackName)
        {
            var wantId = new ElementId((long)bip);
            foreach (SchedulableField sf in sdef.GetSchedulableFields())
            {
                if (sf.ParameterId == wantId) return sf;
            }
            // Fallback by name
            foreach (SchedulableField sf in sdef.GetSchedulableFields())
            {
                string n; try { n = sf.GetName(doc); } catch { continue; }
                if (string.Equals(n, fallbackName, StringComparison.OrdinalIgnoreCase)) return sf;
            }
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Family → category derivation
        // ──────────────────────────────────────────────────────────────────

        private sealed class CategoryPlan
        {
            public string CategoryDisplay;
            public List<string> SheetColumns = new List<string>();
            public List<string> FullColumns = new List<string>();
            private readonly HashSet<string> _sheetSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _fullSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public void AddSheet(string c) { if (!string.IsNullOrEmpty(c) && _sheetSeen.Add(c)) SheetColumns.Add(c); }
            public void AddFull(string c) { if (!string.IsNullOrEmpty(c) && _fullSeen.Add(c)) FullColumns.Add(c); }
        }

        /// <summary>
        /// Build spec-family → Revit-category-display-name from LABEL_DEFINITIONS.json
        /// category_labels. Keys are the plural Revit display names; each value carries
        /// family_name ("STING - X Tag") and optionally csv_family_alias. We index by
        /// the singular family suffix, the alias(es), and the plural key itself.
        /// </summary>
        private static Dictionary<string, string> BuildFamilyCategoryMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JObject root = LoadJson("LABEL_DEFINITIONS.json");
            var labels = root?["category_labels"] as JObject;
            if (labels == null) return map;

            foreach (var prop in labels.Properties())
            {
                string categoryDisplay = prop.Name; // plural Revit category name
                var v = prop.Value as JObject;
                if (v == null) continue;

                void Index(string key)
                {
                    if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
                        map[key] = categoryDisplay;
                }

                Index(categoryDisplay);
                string famName = v.Value<string>("family_name");
                Index(StripFamily(famName));

                var alias = v["csv_family_alias"];
                if (alias is JArray arr) foreach (var a in arr) Index(a?.ToString());
                else if (alias != null) Index(alias.ToString());
            }
            return map;
        }

        /// <summary>Resolve a spec family to a category display name: exact match first,
        /// then a prefix match (the spec truncates a handful of long family names).</summary>
        private static string ResolveCategoryName(string family, Dictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(family)) return null;
            if (map.TryGetValue(family, out string cat)) return cat;

            // Truncated spec names (e.g. "Anti-Ligature (Lighting Fixt") are a prefix of
            // a known key. Only accept an UNAMBIGUOUS prefix hit.
            string hit = null;
            foreach (var kv in map)
            {
                if (kv.Key.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                {
                    if (hit == null) hit = kv.Value;
                    else if (!string.Equals(hit, kv.Value, StringComparison.OrdinalIgnoreCase)) return null; // ambiguous
                }
            }
            return hit;
        }

        private static string StripFamily(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            string s = familyName;
            if (s.StartsWith("STING - ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(8);
            if (s.EndsWith(" Tag", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Trim();
        }

        /// <summary>Index the document's top-level model categories by display name.</summary>
        private static Dictionary<string, Category> BuildDocCategoryIndex(Document doc)
        {
            var d = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            foreach (Category c in doc.Settings.Categories)
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                if (!d.ContainsKey(c.Name)) d[c.Name] = c;
            }
            return d;
        }

        // ──────────────────────────────────────────────────────────────────

        private static List<string> ReadStrArray(JObject e, string key)
        {
            var list = new List<string>();
            if (e[key] is JArray arr)
                foreach (var t in arr)
                {
                    string s = t?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
            return list;
        }

        private static JObject LoadJson(string fileName)
        {
            try
            {
                string path = StingToolsApp.FindDataFile(fileName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) { StingLog.Warn($"ScheduleTagExpander LoadJson '{fileName}': {ex.Message}"); return null; }
        }
    }
}
