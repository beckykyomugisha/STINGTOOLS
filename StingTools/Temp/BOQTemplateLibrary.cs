using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using WpfColor    = System.Windows.Media.Color;
using WpfBrushes  = System.Windows.Media.Brushes;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid     = System.Windows.Controls.Grid;

namespace StingTools.Temp
{
    // ══════════════════════════════════════════════════════════════════════
    //  BOQTemplate — one NRM2 paragraph template with provenance metadata.
    // ══════════════════════════════════════════════════════════════════════

    internal class BOQTemplate
    {
        public string Id;               // stable GUID (used to match edits/deletes across sources)
        public string Category;         // e.g. "Walls", "Doors", "Air Terminals"
        public string Nrm2Section;      // e.g. "14", "21", "34"
        public string Paragraph;        // NRM2 prose with [placeholder] tokens
        public string[] Placeholders;   // parsed from the paragraph itself
        public string Source;           // "builtin" | "company" | "project"
        public string UpdatedBy;
        public DateTime UpdatedDate;
        public int Version;
        public string Notes;

        /// <summary>
        /// Optional match criteria (Gap 3 — variant selection). When populated,
        /// the engine prefers this template over the plain category match for
        /// elements whose family/type/system/disc contains the substring.
        /// All fields are case-insensitive. Empty = no constraint.
        /// Specificity (non-empty count) breaks ties so variants beat category-only.
        /// </summary>
        public string FamilyContains;   // e.g. "curtain" → curtain walls
        public string TypeContains;     // e.g. "automatic" → automatic sliding doors
        public string SystemContains;   // e.g. "HVAC"
        public string DiscContains;     // e.g. "M" → mechanical-only

        public int Specificity
        {
            get
            {
                int s = 0;
                if (!string.IsNullOrEmpty(FamilyContains)) s++;
                if (!string.IsNullOrEmpty(TypeContains)) s++;
                if (!string.IsNullOrEmpty(SystemContains)) s++;
                if (!string.IsNullOrEmpty(DiscContains)) s++;
                return s;
            }
        }

        public string DisplayLabel
        {
            get
            {
                string prefix = Source switch
                {
                    "builtin" => "",
                    "company" => "★ ",    // company library = reusable across projects
                    "project" => "◆ ",    // project-specific custom
                    _ => ""
                };
                var bits = new List<string>();
                if (!string.IsNullOrEmpty(FamilyContains)) bits.Add("family~" + FamilyContains);
                if (!string.IsNullOrEmpty(TypeContains)) bits.Add("type~" + TypeContains);
                if (!string.IsNullOrEmpty(SystemContains)) bits.Add("sys~" + SystemContains);
                if (!string.IsNullOrEmpty(DiscContains)) bits.Add("disc~" + DiscContains);
                string variant = bits.Count > 0 ? "  [" + string.Join(", ", bits) + "]" : "";
                string preview = Paragraph?.Length > 50 ? Paragraph.Substring(0, 50) + "…" : (Paragraph ?? "");
                return $"{prefix}§{Nrm2Section,-3} {Category,-22}{variant}  {preview}";
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQTemplateLibrary — 3-layer template catalogue with persistence.
    //    builtin   → Data/BOQ_DESCRIPTIONS.json (ships with plugin, read-only)
    //    company   → %APPDATA%/STING/boq_templates_library.json  (reusable)
    //    project   → <project>/_bim_manager/boq_custom_templates.json
    // ══════════════════════════════════════════════════════════════════════

    internal static class BOQTemplateLibrary
    {
        /// <summary>
        /// Placeholder tokens the paragraph resolver recognises. Editing the
        /// paragraph is free-form — unknown tokens just don't resolve — but the
        /// editor will warn before the user saves an unknown token.
        /// </summary>
        public static readonly HashSet<string> KnownPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Identity
            "material", "element_type", "family", "type",
            "manufacturer", "model", "model_ref", "manufacturer_ref",
            // Spatial
            "location", "level",
            // Dimensions (mm unless noted)
            "width", "height", "thickness", "depth", "sill_height",
            "size", "diameter", "length", "dimensions", "spacing",
            // Performance / specs
            "airflow", "rating", "voltage", "phases",
            "fire_rating", "finish", "insulation", "substrate",
            "fixings", "frame_material", "hardware", "glass_spec",
            "concrete_spec", "reinforcement", "section_size",
            "worktop_material", "edge_trim",
            // Typed variants (fallback to element type)
            "door_type", "window_type", "foundation_type",
            "terminal_type", "equipment_type", "furniture_type", "casework_type",
            // Reference / standards
            "standard",
            // User text
            "description", "notes"
        };

        /// <summary>
        /// Tokens that must NEVER appear in a BOQ description paragraph.
        /// Costs and quantities belong in their own columns.
        /// </summary>
        public static readonly HashSet<string> ForbiddenPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "qty", "quantity", "rate", "unit_rate", "amount", "total",
            "cost", "price", "unit_price", "subtotal"
        };

        // ── Path resolution ─────────────────────────────────────────────

        /// <summary>
        /// Path to the company template library.
        /// Resolution order (first non-empty wins):
        ///   1. BOQ_COMPANY_LIBRARY_PATH in <see cref="StingToolsApp.DataPath"/>'s sibling project_config.json
        ///      (so a project can pin its library to a network share)
        ///   2. BOQ_COMPANY_LIBRARY_PATH in %APPDATA%/STING/boq_config.json
        ///      (per-machine default for all projects on this workstation)
        ///   3. %APPDATA%/STING/boq_templates_library.json (legacy default)
        /// The resolved path may point at a shared drive so a team's templates
        /// stay in sync without manual import/export.
        /// </summary>
        public static string CompanyLibraryPath
        {
            get
            {
                string configured = TryReadConfiguredLibraryPath();
                if (!string.IsNullOrEmpty(configured))
                {
                    try
                    {
                        string parent = Path.GetDirectoryName(configured);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    }
                    catch (Exception ex) { StingLog.Warn($"CompanyLibraryPath parent: {ex.Message}"); }
                    return configured;
                }
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData ?? Path.GetTempPath(), "STING");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "boq_templates_library.json");
            }
        }

        /// <summary>
        /// Sets (and persists) a company library path override at per-machine scope.
        /// The plugin now resolves to this path for all projects on this workstation
        /// until another override is set. Pass null/empty to revert to the default.
        /// </summary>
        public static void SetCompanyLibraryPathMachine(string newPath)
        {
            string cfg = GetMachineConfigPath();
            JObject obj = new JObject();
            try
            {
                if (File.Exists(cfg)) obj = JObject.Parse(File.ReadAllText(cfg));
            }
            catch (Exception ex) { StingLog.Warn($"SetCompanyLibraryPath read: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(newPath)) obj.Remove("BOQ_COMPANY_LIBRARY_PATH");
            else obj["BOQ_COMPANY_LIBRARY_PATH"] = newPath.Trim();
            string tmp = cfg + ".tmp";
            File.WriteAllText(tmp, obj.ToString(Newtonsoft.Json.Formatting.Indented));
            if (File.Exists(cfg)) File.Replace(tmp, cfg, cfg + ".bak"); else File.Move(tmp, cfg);
        }

        private static string GetMachineConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData ?? Path.GetTempPath(), "STING");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "boq_config.json");
        }

        private static string TryReadConfiguredLibraryPath()
        {
            // 1. project_config.json next to the plugin data (TagConfig loads this on doc open)
            try
            {
                string projCfg = Path.Combine(StingToolsApp.DataPath ?? "", "project_config.json");
                if (File.Exists(projCfg))
                {
                    var j = JObject.Parse(File.ReadAllText(projCfg));
                    string v = j["BOQ_COMPANY_LIBRARY_PATH"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryReadConfiguredLibraryPath project: {ex.Message}"); }
            // 2. per-machine config (%APPDATA%/STING/boq_config.json)
            try
            {
                string cfg = GetMachineConfigPath();
                if (File.Exists(cfg))
                {
                    var j = JObject.Parse(File.ReadAllText(cfg));
                    string v = j["BOQ_COMPANY_LIBRARY_PATH"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryReadConfiguredLibraryPath machine: {ex.Message}"); }
            return null;
        }

        public static string ProjectCustomPath(Document doc)
        {
            string projDir = "";
            try { projDir = Path.GetDirectoryName(doc.PathName) ?? ""; }
            catch (Exception ex) { StingLog.Warn($"BOQTemplateLibrary project path: {ex.Message}"); }
            if (string.IsNullOrEmpty(projDir))
                projDir = Path.Combine(Path.GetTempPath(), "STING");
            string dir = Path.Combine(projDir, "_bim_manager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "boq_custom_templates.json");
        }

        // ── Load / merge ────────────────────────────────────────────────

        /// <summary>
        /// Loads all three layers and merges them.
        /// Precedence on (Id) match: project > company > builtin.
        /// When ID is not set (legacy builtin entries), entries are treated as
        /// independent templates and all appear in the catalogue.
        /// </summary>
        public static List<BOQTemplate> LoadAll(Document doc, string pluginDataPath)
        {
            var byId = new Dictionary<string, BOQTemplate>(StringComparer.OrdinalIgnoreCase);
            var anonymous = new List<BOQTemplate>();

            void Accept(BOQTemplate t)
            {
                if (t == null || string.IsNullOrEmpty(t.Paragraph)) return;
                if (string.IsNullOrEmpty(t.Id))
                {
                    anonymous.Add(t);
                    return;
                }
                // Higher-precedence source overrides lower — merge order handles this
                byId[t.Id] = t;
            }

            // Built-in (lowest priority)
            foreach (var t in LoadBuiltin(pluginDataPath)) Accept(t);
            // Company library (mid priority)
            foreach (var t in LoadCompany()) Accept(t);
            // Project custom (highest priority)
            if (doc != null)
                foreach (var t in LoadProject(doc)) Accept(t);

            var all = anonymous.Concat(byId.Values)
                .OrderBy(t => t.Nrm2Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return all;
        }

        public static List<BOQTemplate> LoadBuiltin(string pluginDataPath)
        {
            var list = new List<BOQTemplate>();
            try
            {
                string p = Path.Combine(pluginDataPath ?? "", "BOQ_DESCRIPTIONS.json");
                if (string.IsNullOrEmpty(pluginDataPath) || !File.Exists(p))
                    p = StingToolsApp.FindDataFile("BOQ_DESCRIPTIONS.json") ?? "";
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return list;
                var arr = JArray.Parse(File.ReadAllText(p));
                foreach (var item in arr)
                    list.Add(FromJson(item, "builtin"));
            }
            catch (Exception ex) { StingLog.Warn($"LoadBuiltin BOQ templates: {ex.Message}"); }
            return list.Where(t => t != null).ToList();
        }

        public static List<BOQTemplate> LoadCompany() => LoadFromFile(CompanyLibraryPath, "company");

        public static List<BOQTemplate> LoadProject(Document doc) => LoadFromFile(ProjectCustomPath(doc), "project");

        private static List<BOQTemplate> LoadFromFile(string path, string source)
        {
            var list = new List<BOQTemplate>();
            try
            {
                if (!File.Exists(path)) return list;
                var arr = JArray.Parse(File.ReadAllText(path));
                foreach (var item in arr)
                    list.Add(FromJson(item, source));
            }
            catch (Exception ex) { StingLog.Warn($"BOQ template load {source}: {ex.Message}"); }
            return list.Where(t => t != null).ToList();
        }

        private static BOQTemplate FromJson(JToken j, string source)
        {
            try
            {
                string para = j["paragraph"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(para)) return null;
                var phArr = j["placeholders"] as JArray;
                var explicitPh = phArr?.Select(t => t?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                var parsedPh = ExtractPlaceholders(para);
                // Use whichever is richer
                var placeholders = (explicitPh != null && explicitPh.Length >= parsedPh.Length) ? explicitPh : parsedPh;

                DateTime upd = DateTime.MinValue;
                DateTime.TryParse(j["updated_date"]?.ToString(), out upd);
                int ver = 0;
                int.TryParse(j["version"]?.ToString(), out ver);

                return new BOQTemplate
                {
                    Id = j["id"]?.ToString() ?? "",
                    Category = j["category"]?.ToString() ?? "",
                    Nrm2Section = j["nrm2_section"]?.ToString() ?? "",
                    Paragraph = para,
                    Placeholders = placeholders,
                    Source = source,
                    UpdatedBy = j["updated_by"]?.ToString() ?? "",
                    UpdatedDate = upd,
                    Version = ver,
                    Notes = j["notes"]?.ToString() ?? "",
                    // Variant match criteria (Gap 3) — optional
                    FamilyContains = j["family_contains"]?.ToString() ?? "",
                    TypeContains = j["type_contains"]?.ToString() ?? "",
                    SystemContains = j["system_contains"]?.ToString() ?? "",
                    DiscContains = j["disc_contains"]?.ToString() ?? ""
                };
            }
            catch (Exception ex) { StingLog.Warn($"BOQ template parse: {ex.Message}"); return null; }
        }

        // ── Save / delete ───────────────────────────────────────────────

        public static void SaveToCompany(BOQTemplate tpl) => UpsertInFile(CompanyLibraryPath, tpl);
        public static void SaveToProject(Document doc, BOQTemplate tpl) => UpsertInFile(ProjectCustomPath(doc), tpl);

        public static bool DeleteFromCompany(string id) => DeleteFromFile(CompanyLibraryPath, id);
        public static bool DeleteFromProject(Document doc, string id) => DeleteFromFile(ProjectCustomPath(doc), id);

        private static void UpsertInFile(string path, BOQTemplate tpl)
        {
            if (string.IsNullOrEmpty(tpl.Id)) tpl.Id = Guid.NewGuid().ToString("N");
            tpl.UpdatedBy = Environment.UserName ?? "unknown";
            tpl.UpdatedDate = DateTime.Now;
            tpl.Version = Math.Max(1, tpl.Version + 1);
            // Re-extract placeholders from the (potentially edited) paragraph
            tpl.Placeholders = ExtractPlaceholders(tpl.Paragraph);

            var arr = new JArray();
            if (File.Exists(path))
            {
                try { arr = JArray.Parse(File.ReadAllText(path)); } catch (Exception ex) { StingLog.Warn($"UpsertInFile parse: {ex.Message}"); }
            }
            // Remove existing with same id
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (string.Equals(arr[i]["id"]?.ToString(), tpl.Id, StringComparison.OrdinalIgnoreCase))
                    arr.RemoveAt(i);
            }
            arr.Add(ToJson(tpl));
            WriteAtomic(path, arr);
        }

        private static bool DeleteFromFile(string path, string id)
        {
            if (!File.Exists(path) || string.IsNullOrEmpty(id)) return false;
            JArray arr;
            try { arr = JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"DeleteFromFile parse: {ex.Message}"); return false; }
            bool removed = false;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (string.Equals(arr[i]["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase))
                {
                    arr.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed) WriteAtomic(path, arr);
            return removed;
        }

        private static JObject ToJson(BOQTemplate t)
        {
            var o = new JObject
            {
                ["id"] = t.Id,
                ["category"] = t.Category ?? "",
                ["nrm2_section"] = t.Nrm2Section ?? "",
                ["paragraph"] = t.Paragraph ?? "",
                ["placeholders"] = new JArray(t.Placeholders ?? Array.Empty<string>()),
                ["updated_by"] = t.UpdatedBy ?? "",
                ["updated_date"] = t.UpdatedDate.ToString("o"),
                ["version"] = t.Version,
                ["notes"] = t.Notes ?? ""
            };
            // Variant match criteria — only write when non-empty to keep JSON clean
            if (!string.IsNullOrEmpty(t.FamilyContains)) o["family_contains"] = t.FamilyContains;
            if (!string.IsNullOrEmpty(t.TypeContains)) o["type_contains"] = t.TypeContains;
            if (!string.IsNullOrEmpty(t.SystemContains)) o["system_contains"] = t.SystemContains;
            if (!string.IsNullOrEmpty(t.DiscContains)) o["disc_contains"] = t.DiscContains;
            return o;
        }

        private static void WriteAtomic(string path, JArray arr)
        {
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, arr.ToString(Newtonsoft.Json.Formatting.Indented));
            if (File.Exists(path)) File.Replace(tmp, path, bak);
            else File.Move(tmp, path);
        }

        // ── Import / export ─────────────────────────────────────────────

        public static int ExportLibrary(string path, IEnumerable<BOQTemplate> templates)
        {
            var arr = new JArray();
            int n = 0;
            foreach (var t in templates)
            {
                if (t == null || string.IsNullOrEmpty(t.Paragraph)) continue;
                arr.Add(ToJson(t));
                n++;
            }
            WriteAtomic(path, arr);
            return n;
        }

        /// <summary>
        /// Imports templates into the chosen target. Preserves ids so re-import
        /// updates existing entries rather than creating duplicates.
        /// </summary>
        public static int ImportLibrary(string sourcePath, string targetSource, Document doc)
        {
            if (!File.Exists(sourcePath)) return 0;
            var arr = JArray.Parse(File.ReadAllText(sourcePath));
            int n = 0;
            foreach (var item in arr)
            {
                var t = FromJson(item, targetSource);
                if (t == null) continue;
                if (string.IsNullOrEmpty(t.Id)) t.Id = Guid.NewGuid().ToString("N");
                if (targetSource == "company") SaveToCompany(t);
                else if (targetSource == "project" && doc != null) SaveToProject(doc, t);
                n++;
            }
            return n;
        }

        // ── Placeholder extraction & validation ─────────────────────────

        private static readonly Regex _placeholderRx = new Regex(@"\[([a-zA-Z0-9_]+)\]", RegexOptions.Compiled);

        public static string[] ExtractPlaceholders(string paragraph)
        {
            if (string.IsNullOrEmpty(paragraph)) return Array.Empty<string>();
            return _placeholderRx.Matches(paragraph)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Validates a paragraph: reports forbidden tokens (blocked) and unknown
        /// tokens (warned). Returns (errors, warnings) — empty errors == OK to save.
        /// </summary>
        public static (List<string> errors, List<string> warnings) ValidateParagraph(string paragraph)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                errors.Add("Paragraph is empty.");
                return (errors, warnings);
            }
            if (paragraph.Length < 20)
                warnings.Add("Paragraph is very short — NRM2 descriptions typically specify material, location and standard.");
            var tokens = ExtractPlaceholders(paragraph);
            foreach (var t in tokens)
            {
                if (ForbiddenPlaceholders.Contains(t))
                    errors.Add($"Forbidden token [{t}] — costs/quantities belong in their own columns, not in the description.");
                else if (!KnownPlaceholders.Contains(t))
                    warnings.Add($"Unknown token [{t}] — it will be left blank at export unless you populate it on elements.");
            }
            if (!paragraph.Contains("["))
                warnings.Add("No placeholders found — every item will read identically. Add [material], [location], etc. to vary by element.");
            return (errors, warnings);
        }

        // ── Folder-format I/O (Git-friendly — Gap 5) ────────────────────

        /// <summary>
        /// Exports a template catalogue to a folder, one JSON file per template.
        /// Filenames are deterministic (category + section + short id) so the
        /// tree is diff-friendly for engineering teams using Git. Existing .json
        /// files in the folder for templates not in the set are preserved.
        /// </summary>
        public static int ExportToFolder(string folderPath, IEnumerable<BOQTemplate> templates)
        {
            Directory.CreateDirectory(folderPath);
            int n = 0;
            foreach (var t in templates)
            {
                if (t == null || string.IsNullOrEmpty(t.Paragraph)) continue;
                string safeCat = MakeSafeFileName(t.Category ?? "uncategorised");
                string idFrag = string.IsNullOrEmpty(t.Id) ? Guid.NewGuid().ToString("N").Substring(0, 6) : t.Id.Substring(0, Math.Min(6, t.Id.Length));
                string fname = $"§{t.Nrm2Section}_{safeCat}_{idFrag}.json";
                string path = Path.Combine(folderPath, fname);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, ToJson(t).ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                n++;
            }
            return n;
        }

        /// <summary>
        /// Imports every *.json file in a folder into the chosen target.
        /// Matches the folder export format (one template per file) but also
        /// accepts JSON arrays for backward compatibility.
        /// </summary>
        public static int ImportFromFolder(string folderPath, string targetSource, Document doc)
        {
            if (!Directory.Exists(folderPath)) return 0;
            int n = 0;
            foreach (string f in Directory.EnumerateFiles(folderPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string text = File.ReadAllText(f);
                    JToken root;
                    try { root = JToken.Parse(text); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                    IEnumerable<JToken> items = root is JArray arr ? arr : new[] { root };
                    foreach (var item in items)
                    {
                        var t = FromJson(item, targetSource);
                        if (t == null) continue;
                        if (string.IsNullOrEmpty(t.Id)) t.Id = Guid.NewGuid().ToString("N");
                        if (targetSource == "company") SaveToCompany(t);
                        else if (targetSource == "project" && doc != null) SaveToProject(doc, t);
                        n++;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ImportFromFolder({f}): {ex.Message}"); }
            }
            return n;
        }

        private static string MakeSafeFileName(string s)
            => StingTools.Core.OutputLocationHelper.MakeSafeFileName(
                s, replacement: '-', extraInvalid: new[] { ' ', '/', '\\' });

        // ── Per-project resolvability audit (Gap 2) ─────────────────────

        /// <summary>
        /// Shape of the per-project resolvability report. Lists, per template,
        /// which placeholder tokens could not be resolved for the project's
        /// current elements — so a QS can see "this template uses [fire_rating]
        /// but 23 of 45 walls have it blank" BEFORE exporting the BOQ.
        /// </summary>
        public class ResolvabilityReport
        {
            public class TemplateResult
            {
                public BOQTemplate Template;
                public int ElementsInCategory;
                public int ElementsFullyResolved;  // all placeholders populated on the element
                public Dictionary<string, int> UnresolvedByToken = new Dictionary<string, int>();
            }
            public List<TemplateResult> Templates = new List<TemplateResult>();
            public int TotalElementsEvaluated;
            public int TotalFullyResolved;
            public double ResolvedPercent => TotalElementsEvaluated == 0 ? 0 : 100.0 * TotalFullyResolved / TotalElementsEvaluated;

            public string FormatSummary(int topN = 15)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Templates scanned: {Templates.Count}");
                sb.AppendLine($"Elements evaluated: {TotalElementsEvaluated}");
                sb.AppendLine($"Fully resolved:     {TotalFullyResolved}  ({ResolvedPercent:F0}%)");
                sb.AppendLine();
                int shown = 0;
                foreach (var r in Templates.Where(t => t.UnresolvedByToken.Count > 0)
                    .OrderByDescending(t => t.UnresolvedByToken.Sum(x => x.Value)).Take(topN))
                {
                    double pct = r.ElementsInCategory == 0 ? 0 : 100.0 * r.ElementsFullyResolved / r.ElementsInCategory;
                    sb.AppendLine($"§{r.Template.Nrm2Section} {r.Template.Category} — {r.ElementsFullyResolved}/{r.ElementsInCategory} fully resolve ({pct:F0}%)");
                    foreach (var kv in r.UnresolvedByToken.OrderByDescending(x => x.Value).Take(6))
                        sb.AppendLine($"    [{kv.Key}] unresolved on {kv.Value} element(s)");
                    sb.AppendLine();
                    shown++;
                }
                if (shown == 0) sb.AppendLine("All templates resolve cleanly on the current model.");
                return sb.ToString();
            }
        }

        /// <summary>
        /// Scans the project's elements against the template catalogue and
        /// reports which placeholders won't resolve per template. Uses an
        /// element predicate supplied by the caller so we don't have to import
        /// the ParameterHelpers coupling into the library.
        /// </summary>
        public static ResolvabilityReport AuditResolvability(
            Document doc, List<BOQTemplate> templates,
            Func<Document, List<Element>> collectElements,
            Func<Element, string> getCategory,
            Func<Element, string, string> getPlaceholderValue)
        {
            var report = new ResolvabilityReport();
            if (doc == null || templates == null || templates.Count == 0) return report;
            var elems = collectElements(doc) ?? new List<Element>();

            // Bucket elements by category (lowercased) for fast lookup per template
            var byCat = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in elems)
            {
                string cat = (getCategory(el) ?? "").ToLowerInvariant();
                if (string.IsNullOrEmpty(cat)) continue;
                if (!byCat.TryGetValue(cat, out var list)) byCat[cat] = list = new List<Element>();
                list.Add(el);
            }

            var seenElementsFullyResolved = new HashSet<long>();
            foreach (var tpl in templates)
            {
                if (string.IsNullOrEmpty(tpl.Category) || string.IsNullOrEmpty(tpl.Paragraph)) continue;
                string tplCat = tpl.Category.ToLowerInvariant();
                // Loose category match — a template's category is contained in, or contains, the element category
                var matchedLists = byCat.Where(kv => kv.Key.Contains(tplCat) || tplCat.Contains(kv.Key)).ToList();
                var matched = matchedLists.SelectMany(kv => kv.Value).Distinct().ToList();
                if (matched.Count == 0) continue;

                var tokens = ExtractPlaceholders(tpl.Paragraph)
                    .Where(t => !ForbiddenPlaceholders.Contains(t)).ToArray();

                var tplResult = new ResolvabilityReport.TemplateResult
                {
                    Template = tpl,
                    ElementsInCategory = matched.Count
                };

                foreach (var el in matched)
                {
                    bool allResolved = tokens.Length > 0;
                    foreach (var tok in tokens)
                    {
                        string val = getPlaceholderValue?.Invoke(el, tok);
                        if (string.IsNullOrEmpty(val))
                        {
                            allResolved = false;
                            if (!tplResult.UnresolvedByToken.ContainsKey(tok)) tplResult.UnresolvedByToken[tok] = 0;
                            tplResult.UnresolvedByToken[tok]++;
                        }
                    }
                    if (allResolved)
                    {
                        tplResult.ElementsFullyResolved++;
                        seenElementsFullyResolved.Add(el.Id.Value);
                    }
                }
                report.Templates.Add(tplResult);
            }

            report.TotalElementsEvaluated = elems.Count;
            report.TotalFullyResolved = seenElementsFullyResolved.Count;
            return report;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BOQParagraphEditorDialog — modal editor for creating/editing templates.
    // ══════════════════════════════════════════════════════════════════════

    internal class BOQParagraphEditorDialog : Window
    {
        public BOQTemplate Result { get; private set; }
        public string SaveTarget { get; private set; } // "company" | "project"
        public bool Saved { get; private set; }

        private readonly BOQTemplate _seed;
        private readonly bool _isNew;
        private readonly Document _doc;
        private TextBox _categoryBox;
        private TextBox _sectionBox;
        private TextBox _paragraphBox;
        private TextBox _familyBox;
        private TextBox _typeBox;
        private TextBox _systemBox;
        private TextBox _discBox;
        private TextBlock _validationBlock;
        private TextBlock _previewBlock;
        private RadioButton _targetCompany;
        private RadioButton _targetProject;

        private static readonly SolidColorBrush NavyBrush = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x3A, 0x5F));
        private static readonly SolidColorBrush AmberBrush = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xA0, 0x20));
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28));

        public BOQParagraphEditorDialog(BOQTemplate seed, Document doc, bool isNew)
        {
            _seed = seed ?? new BOQTemplate();
            _isNew = isNew;
            _doc = doc;
            Title = isNew ? "STING — New BOQ Paragraph Template" : "STING — Edit BOQ Paragraph Template";
            Width = 780;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Build();
        }

        private void Build()
        {
            var root = new DockPanel { Margin = new Thickness(12) };

            // ── Header strip ─────────────────────────────────────────
            var header = new Border { Background = NavyBrush, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10) };
            var hsp = new StackPanel();
            hsp.Children.Add(new TextBlock { Text = _isNew ? "NEW BOQ PARAGRAPH" : $"EDIT BOQ PARAGRAPH  ·  source: {_seed.Source}", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = WpfBrushes.White });
            hsp.Children.Add(new TextBlock { Text = "Write prose that describes the item's properties only. Quantities, units, rates and amounts are inserted in separate columns at export — never inside the paragraph.", FontSize = 10, Foreground = new SolidColorBrush(Colors.LightSteelBlue), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            header.Child = hsp;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Buttons strip (docked bottom) ────────────────────────
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            btnRow.Children.Add(Btn("Cancel", WpfBrushes.Gray, () => { Saved = false; Close(); }));
            btnRow.Children.Add(Btn("Validate", AmberBrush, RunValidation, new Thickness(8, 0, 0, 0)));
            btnRow.Children.Add(Btn("Save", GreenBrush, OnSave, new Thickness(8, 0, 0, 0)));
            DockPanel.SetDock(btnRow, Dock.Bottom);
            root.Children.Add(btnRow);

            // ── Body: 2-column grid (fields left, placeholder help right) ─
            var grid = new WpfGrid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column — fields
            var left = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            left.Children.Add(Label("Category:"));
            _categoryBox = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 8), Text = _seed.Category ?? "" };
            left.Children.Add(_categoryBox);

            left.Children.Add(Label("NRM2 Section (number only, e.g. 14):"));
            _sectionBox = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 8), Text = _seed.Nrm2Section ?? "" };
            left.Children.Add(_sectionBox);

            // ── Optional variant match (Gap 3): narrows this template to a subset of the category ──
            var variantExpander = new Expander
            {
                Header = "Variant matchers (optional — narrow this template to specific elements)",
                Margin = new Thickness(0, 0, 0, 8),
                IsExpanded = _seed != null && _seed.Specificity > 0
            };
            var variantPanel = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };
            variantPanel.Children.Add(new TextBlock
            {
                Text = "Leave blank for a plain category template. Fill any combination to make this a variant. " +
                       "The engine prefers the most specific template at export. Matches are case-insensitive substring.",
                FontSize = 10, Foreground = WpfBrushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
            });
            var variantGrid = new WpfGrid();
            variantGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            variantGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) variantGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void AddField(string label, TextBox tb, int row, int col)
            {
                var p = new StackPanel { Margin = new Thickness(0, 0, 4, 4) };
                p.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold });
                p.Children.Add(tb);
                WpfGrid.SetRow(p, row); WpfGrid.SetColumn(p, col);
                variantGrid.Children.Add(p);
            }
            _familyBox = new TextBox { Height = 24, FontSize = 11, Text = _seed?.FamilyContains ?? "" };
            _typeBox = new TextBox { Height = 24, FontSize = 11, Text = _seed?.TypeContains ?? "" };
            _systemBox = new TextBox { Height = 24, FontSize = 11, Text = _seed?.SystemContains ?? "" };
            _discBox = new TextBox { Height = 24, FontSize = 11, Text = _seed?.DiscContains ?? "" };
            AddField("Family name contains:", _familyBox, 0, 0);
            AddField("Type name contains:", _typeBox, 0, 1);
            AddField("System (SYS token) contains:", _systemBox, 1, 0);
            AddField("Discipline (DISC) equals:", _discBox, 1, 1);
            variantPanel.Children.Add(variantGrid);
            variantExpander.Content = variantPanel;
            left.Children.Add(variantExpander);

            left.Children.Add(Label("Paragraph (use [token] placeholders for element-specific data):"));
            _paragraphBox = new TextBox
            {
                Height = 200,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 2, 0, 8),
                Text = _seed.Paragraph ?? "",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            };
            _paragraphBox.TextChanged += (s, e) => RefreshPreview();
            left.Children.Add(_paragraphBox);

            // Live preview (what the paragraph looks like with sample tokens)
            left.Children.Add(Label("Live preview (tokens shown with sample values):"));
            _previewBlock = new TextBlock
            {
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8),
                MinHeight = 60,
                Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xFF)),
                Padding = new Thickness(8, 6, 8, 6)
            };
            left.Children.Add(_previewBlock);

            // Validation feedback
            _validationBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), MinHeight = 20 };
            left.Children.Add(_validationBlock);

            // Save target
            left.Children.Add(Label("Save to:"));
            var targetPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _targetCompany = new RadioButton { Content = "★ Company library (reusable on later projects)", GroupName = "tgt", Margin = new Thickness(0, 0, 20, 0), IsChecked = _seed.Source != "project" };
            _targetProject = new RadioButton { Content = "◆ This project only", GroupName = "tgt", IsChecked = _seed.Source == "project" || _doc == null ? (_doc != null) : false };
            // If no document is open we can't save to project
            if (_doc == null) _targetProject.IsEnabled = false;
            targetPanel.Children.Add(_targetCompany);
            targetPanel.Children.Add(_targetProject);
            left.Children.Add(targetPanel);

            WpfGrid.SetColumn(left, 0);
            grid.Children.Add(left);

            // Right column — placeholder helper
            var right = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            right.Children.Add(Label("Insert placeholder (double-click):"));
            var phList = new ListBox
            {
                Height = 420,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 8)
            };
            foreach (var ph in BOQTemplateLibrary.KnownPlaceholders.OrderBy(x => x))
                phList.Items.Add(ph);
            phList.MouseDoubleClick += (s, e) =>
            {
                if (phList.SelectedItem is string ph)
                    InsertAtCaret("[" + ph + "]");
            };
            right.Children.Add(phList);
            right.Children.Add(new TextBlock
            {
                Text = "Tokens resolve from element parameters and Revit geometry at export. Unknown tokens are stripped; cost/qty tokens are blocked.",
                TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = WpfBrushes.Gray
            });
            WpfGrid.SetColumn(right, 1);
            grid.Children.Add(right);

            var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroll);

            Content = root;
            RefreshPreview();
        }

        private void InsertAtCaret(string text)
        {
            int caret = _paragraphBox.SelectionStart;
            int selLen = _paragraphBox.SelectionLength;
            _paragraphBox.Text = _paragraphBox.Text.Substring(0, caret) + text + _paragraphBox.Text.Substring(caret + selLen);
            _paragraphBox.SelectionStart = caret + text.Length;
            _paragraphBox.Focus();
        }

        private void RefreshPreview()
        {
            string paragraph = _paragraphBox?.Text ?? "";
            if (string.IsNullOrEmpty(paragraph)) { _previewBlock.Text = ""; return; }
            // Substitute sample values for known tokens so the user sees how the prose reads
            string s = paragraph;
            s = s.Replace("[material]", "150mm blockwork")
                 .Replace("[element_type]", "partition")
                 .Replace("[location]", "Level 01 — Corridor")
                 .Replace("[level]", "Level 01")
                 .Replace("[width]", "1200").Replace("[height]", "2100").Replace("[thickness]", "100")
                 .Replace("[standard]", "BS EN 1996 workmanship")
                 .Replace("[fire_rating]", "60 minutes")
                 .Replace("[finish]", "fair-faced")
                 .Replace("[manufacturer]", "(Approved Manufacturer)")
                 .Replace("[model]", "(Model Ref)");
            // Strip any remaining unresolved [token]
            s = Regex.Replace(s, @"\[[a-zA-Z0-9_]+\]", "");
            // Tidy
            s = Regex.Replace(s, @"\s+,", ",");
            s = Regex.Replace(s, @",\s*,", ",");
            s = Regex.Replace(s, @"\s{2,}", " ");
            s = s.Trim();
            if (s.Length > 0 && !s.EndsWith(".")) s += ".";
            _previewBlock.Text = s;
        }

        private void RunValidation()
        {
            var (errors, warnings) = BOQTemplateLibrary.ValidateParagraph(_paragraphBox.Text ?? "");
            var sb = new System.Text.StringBuilder();
            if (errors.Count == 0 && warnings.Count == 0)
            {
                _validationBlock.Text = "✓ No issues.";
                _validationBlock.Foreground = GreenBrush;
                return;
            }
            foreach (var e in errors) sb.AppendLine("✘ " + e);
            foreach (var w in warnings) sb.AppendLine("⚠ " + w);
            _validationBlock.Text = sb.ToString().TrimEnd();
            _validationBlock.Foreground = errors.Count > 0 ? RedBrush : AmberBrush;
        }

        private void OnSave()
        {
            string cat = _categoryBox.Text?.Trim() ?? "";
            string sec = _sectionBox.Text?.Trim() ?? "";
            string para = _paragraphBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(cat)) { MessageBox.Show("Category is required.", "BOQ Template"); return; }
            if (string.IsNullOrEmpty(sec)) { MessageBox.Show("NRM2 section is required.", "BOQ Template"); return; }

            var (errors, warnings) = BOQTemplateLibrary.ValidateParagraph(para);
            if (errors.Count > 0)
            {
                RunValidation();
                MessageBox.Show("Please fix the errors before saving:\n\n" + string.Join("\n", errors), "BOQ Template");
                return;
            }
            if (warnings.Count > 0)
            {
                var ok = MessageBox.Show("Save with warnings?\n\n" + string.Join("\n", warnings), "BOQ Template", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
            }

            SaveTarget = (_targetCompany.IsChecked == true) ? "company" : "project";

            // Preserve existing id when editing
            Result = new BOQTemplate
            {
                Id = _seed?.Id,
                Category = cat,
                Nrm2Section = sec,
                Paragraph = para,
                Placeholders = BOQTemplateLibrary.ExtractPlaceholders(para),
                Source = SaveTarget,
                Version = _seed?.Version ?? 0,
                Notes = _seed?.Notes ?? "",
                FamilyContains = _familyBox?.Text?.Trim() ?? "",
                TypeContains = _typeBox?.Text?.Trim() ?? "",
                SystemContains = _systemBox?.Text?.Trim() ?? "",
                DiscContains = _discBox?.Text?.Trim() ?? ""
            };
            Saved = true;
            Close();
        }

        private static TextBlock Label(string text)
            => new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) };

        private static Button Btn(string content, Brush bg, Action onClick, Thickness? margin = null)
        {
            var btn = new Button
            {
                Content = content, Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Background = bg, Foreground = WpfBrushes.White, BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand,
                Margin = margin ?? new Thickness(0)
            };
            btn.Click += (s, e) => onClick();
            return btn;
        }
    }
}
