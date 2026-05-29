// LpsCreateFamilyShellsCommand.cs
//
// Creates the 20 STING LPS family SHELLS from their Revit family templates
// (.rft) per LPS_FAMILY_INVENTORY.json: a fresh .rfa per family with every
// subcategory, type parameter (+ formula) and STING shared parameter
// pre-built — GEOMETRY LEFT EMPTY for the author to model in the Family
// Editor. Existing .rfa files are skipped (never overwritten), so authored
// or vendor families are safe.
//
// Shell creation (template → category → subcategories → type params →
// formulas → save) is done here; the STING shared-parameter injection is
// delegated to the proven FamilyParamEngine.ProcessFamily (the same engine
// the Batch Family Stamper uses) so there is one source of truth for
// shared-param binding.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.Lightning
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsCreateFamilyShellsCommand : IExternalCommand, IPanelCommand
    {
        private const double MmPerFoot = 304.8;

        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS Family Shells", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            // ── 1. Inventory ─────────────────────────────────────────────
            string invPath = StingToolsApp.FindDataFile("LPS_FAMILY_INVENTORY.json");
            if (string.IsNullOrEmpty(invPath) || !File.Exists(invPath))
            {
                TaskDialog.Show("STING — LPS Family Shells", "LPS_FAMILY_INVENTORY.json not found.");
                return Result.Cancelled;
            }
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(invPath)); }
            catch (Exception ex)
            {
                TaskDialog.Show("STING — LPS Family Shells", "Inventory parse failed: " + ex.Message);
                return Result.Failed;
            }
            var fams = root["families"] as JArray;
            if (fams == null || fams.Count == 0)
            {
                TaskDialog.Show("STING — LPS Family Shells", "No families in inventory.");
                return Result.Cancelled;
            }
            var commonTokens = (root["commonStingTokens"] as JArray)?
                .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                ?? new List<string>();

            // ── 2. Output + template folders ─────────────────────────────
            string baseFolder = ResolveLpsFolder(doc);
            string templateFolder = ResolveTemplateFolder(app.Application);
            if (string.IsNullOrEmpty(templateFolder))
            {
                TaskDialog.Show("STING — LPS Family Shells",
                    "Could not locate the Revit family-template folder on this machine.\n\n" +
                    "Set it in Revit → Options → File Locations → Family Template Files, then retry.");
                return Result.Cancelled;
            }
            try { Directory.CreateDirectory(baseFolder); }
            catch (Exception ex)
            {
                TaskDialog.Show("STING — LPS Family Shells", "Output folder could not be created:\n" + ex.Message);
                return Result.Failed;
            }

            // ── 3. Confirm ───────────────────────────────────────────────
            var confirm = new TaskDialog("STING — LPS Family Shells")
            {
                MainInstruction = $"Create up to {fams.Count} LPS family shells from their templates?",
                MainContent =
                    $"Output: {baseFolder}\n" +
                    $"Templates: {templateFolder}\n\n" +
                    "For each family STING creates a NEW .rfa from its .rft template with all subcategories, " +
                    "type parameters (+ formulas) and STING shared parameters pre-built. GEOMETRY IS LEFT " +
                    "EMPTY for you to model in the Family Editor.\n\n" +
                    "Existing .rfa files are SKIPPED — authored / vendor families are never overwritten.\n\n" +
                    $"Estimated time: ~{Math.Max(1, fams.Count / 3)} minute(s). Revit will look busy during the run.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Ok
            };
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            // ── 4. Iterate ───────────────────────────────────────────────
            int created = 0, skipped = 0, failed = 0, paramsStamped = 0;
            var rows = new List<string[]>();
            var failures = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var f in fams)
            {
                string fileName = f["fileName"]?.ToString();
                string display = f["displayName"]?.ToString() ?? fileName;
                string templateName = f["template"]?.ToString();
                string tier = f["_tier"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(templateName))
                {
                    failed++; failures.Add($"  ▸ {display}: missing fileName / template in inventory");
                    continue;
                }

                string tierFolder = Path.Combine(baseFolder, TierSubfolder(tier));
                try { Directory.CreateDirectory(tierFolder); } catch (Exception ex) { StingLog.Warn($"mkdir {tierFolder}: {ex.Message}"); }
                string outPath = Path.Combine(tierFolder, fileName);

                if (File.Exists(outPath))
                {
                    skipped++; rows.Add(new[] { fileName, "—", "skip (exists)" });
                    continue;
                }

                string templatePath = ResolveTemplateFile(templateFolder, templateName);
                if (string.IsNullOrEmpty(templatePath))
                {
                    failed++;
                    failures.Add($"  ▸ {display}: template '{templateName}' not found under {templateFolder}");
                    rows.Add(new[] { fileName, "—", "✗ no template" });
                    continue;
                }

                try
                {
                    int typeParams = CreateShell(app.Application, f, templatePath, outPath);

                    // Inject STING shared parameters via the proven engine.
                    int added = 0;
                    try
                    {
                        var opts = new FamilyParamEngine.ProcessOptions
                        {
                            ParamNames = BuildSharedParamList(f, commonTokens),
                            Purge = PurgeMode.None,
                            InjectFormulas = false,
                            CreatePositionTypes = false,
                            InjectTagPos = false,
                            InjectAutomationPack = false
                        };
                        var pr = FamilyParamEngine.ProcessFamily(app.Application, outPath, outPath, opts);
                        if (string.IsNullOrEmpty(pr.ErrorMessage)) added = pr.ParamsAdded;
                        else failures.Add($"  ▸ {display}: shared-param stamp — {pr.ErrorMessage}");
                    }
                    catch (Exception ex2) { failures.Add($"  ▸ {display}: shared-param stamp — {ex2.Message}"); }

                    paramsStamped += added;
                    created++;
                    rows.Add(new[] { fileName, $"{typeParams} type / {added} shared", "✓" });
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"  ▸ {display}: {ex.Message}");
                    rows.Add(new[] { fileName, "—", "✗ " + ex.Message });
                    StingLog.Error($"LpsCreateFamilyShells '{fileName}'", ex);
                }
            }
            sw.Stop();

            // ── 5. Report ────────────────────────────────────────────────
            var rp = StingResultPanel.Create("LPS — Create Family Shells");
            rp.SetSubtitle($"Output: {baseFolder}  •  Duration: {sw.Elapsed.TotalSeconds:F1} s");
            rp.AddSection("SUMMARY")
              .Metric("Families in inventory", fams.Count.ToString())
              .MetricHighlight("Created", created.ToString())
              .Metric("Skipped (already exist)", skipped.ToString())
              .MetricError("Failed", failed.ToString())
              .Metric("Shared params stamped", paramsStamped.ToString());
            rp.AddSection("NEXT STEP")
              .Text("Each new .rfa has the parameter + subcategory scaffolding but NO geometry. Open each in")
              .Text("the Family Editor and model the solids per Families/LPS/AUTHORING_GUIDE.md — lock the")
              .Text("dimensions to the type parameters and assign each solid to its STING_LPS_* subcategory,")
              .Text("then re-save. SPD families additionally need an electrical connector added by hand (there")
              .Text("is no geometry to host one on an empty shell). Run Family Conformance Check when done.");
            if (failures.Count > 0)
            {
                var s = rp.AddSection("ISSUES");
                foreach (var x in failures.Take(20)) s.Text(x);
                if (failures.Count > 20) s.Text($"  …and {failures.Count - 20} more — see StingTools.log");
            }
            if (rows.Count > 0)
                rp.AddSection("DETAIL").Table(new[] { "File", "Params", "Status" }, rows.Take(100).ToList());
            rp.Show();

            try { StingLpsPanel.Instance?.PushRunRow("Create Family Shells", created > 0 ? "✓" : "•"); }
            catch (Exception ex) { StingLog.Warn($"PushRunRow: {ex.Message}"); }
            StingLog.Info($"LpsCreateFamilyShells: {created} created / {skipped} skipped / {failed} failed " +
                          $"/ {paramsStamped} shared params in {sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }

        // ── Shell creation ───────────────────────────────────────────────

        /// <summary>Creates one family shell from a template and returns the
        /// number of type parameters added.</summary>
        private static int CreateShell(Application app, JToken fam, string templatePath, string outPath)
        {
            Document fdoc = app.NewFamilyDocument(templatePath);
            if (fdoc == null) throw new InvalidOperationException("NewFamilyDocument returned null");

            int typeParamCount = 0;
            try
            {
                using (var tx = new Transaction(fdoc, "STING — Build LPS Family Shell"))
                {
                    tx.Start();
                    var fm = fdoc.FamilyManager;
                    if (fm.CurrentType == null)
                    {
                        try { fm.CurrentType = fm.NewType("Standard"); }
                        catch (Exception ex) { StingLog.Warn($"NewType: {ex.Message}"); }
                    }

                    CreateSubcategories(fdoc, fam["subcategories"] as JArray);

                    // Type-parameter specs = typeParameters[] + instanceParameters[]
                    // whose binding is "type" (the computed family params such as
                    // _ProtectionRadius_m / _NominalResistance_ohm). Shared params
                    // are handled afterwards by FamilyParamEngine.
                    var specs = new List<(string name, string type, string value, string formula)>();
                    if (fam["typeParameters"] is JArray tps)
                        foreach (var tp in tps)
                            specs.Add((tp["name"]?.ToString(), tp["type"]?.ToString() ?? "Text",
                                       tp["value"]?.ToString() ?? "", tp["formula"]?.ToString() ?? ""));
                    if (fam["instanceParameters"] is JArray ips)
                        foreach (var ip in ips)
                            if (string.Equals(ip["binding"]?.ToString(), "type", StringComparison.OrdinalIgnoreCase))
                                specs.Add((ip["name"]?.ToString(), "Number", "", ip["formula"]?.ToString() ?? ""));

                    // Pass 1 — add parameters + set defaults (no formula yet).
                    foreach (var s in specs)
                    {
                        if (string.IsNullOrWhiteSpace(s.name)) continue;
                        if (fm.get_Parameter(s.name) != null) continue;
                        try
                        {
                            var fp = fm.AddParameter(s.name, GroupTypeId.General, ResolveSpec(s.type), /*isInstance*/ false);
                            typeParamCount++;
                            if (fp != null && string.IsNullOrEmpty(s.formula) && !string.IsNullOrEmpty(s.value))
                                SetDefault(fm, fp, s.type, s.value);
                        }
                        catch (Exception ex) { StingLog.Warn($"AddParameter {s.name}: {ex.Message}"); }
                    }

                    // Pass 2 — set formulas (referenced params now exist).
                    foreach (var s in specs)
                    {
                        if (string.IsNullOrWhiteSpace(s.name) || string.IsNullOrEmpty(s.formula)) continue;
                        try
                        {
                            var fp = fm.get_Parameter(s.name);
                            if (fp != null && !fp.IsReporting) fm.SetFormula(fp, s.formula);
                        }
                        catch (Exception ex) { StingLog.Warn($"SetFormula {s.name}: {ex.Message}"); }
                    }

                    tx.Commit();
                }

                var so = new SaveAsOptions { OverwriteExistingFile = true };
                fdoc.SaveAs(outPath, so);
                fdoc.Close(false);
                return typeParamCount;
            }
            catch
            {
                try { fdoc.Close(false); } catch (Exception ex) { StingLog.Warn($"shell close after error: {ex.Message}"); }
                throw;
            }
        }

        private static void CreateSubcategories(Document fdoc, JArray subcats)
        {
            if (subcats == null) return;
            Category parent = null;
            try { parent = fdoc.OwnerFamily?.FamilyCategory; } catch (Exception ex) { StingLog.Warn($"FamilyCategory: {ex.Message}"); }
            if (parent == null) return;

            foreach (var sc in subcats)
            {
                string name = sc["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                try
                {
                    Category child = null;
                    foreach (Category c in parent.SubCategories)
                        if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) { child = c; break; }
                    if (child == null) child = fdoc.Settings.Categories.NewSubcategory(parent, name);
                    if (child == null) continue;

                    int lw = 0;
                    try { lw = sc["lineWeight"]?.Value<int>() ?? 0; } catch (Exception ex) { StingLog.Warn($"lineWeight: {ex.Message}"); }
                    if (lw >= 1 && lw <= 16)
                        try { child.SetLineWeight(lw, GraphicsStyleType.Projection); }
                        catch (Exception ex) { StingLog.Warn($"SetLineWeight {name}: {ex.Message}"); }

                    var col = ParseColor(sc["color"]?.ToString());
                    if (col != null)
                        try { child.LineColor = col; } catch (Exception ex) { StingLog.Warn($"LineColor {name}: {ex.Message}"); }
                }
                catch (Exception ex) { StingLog.Warn($"Subcategory '{name}': {ex.Message}"); }
            }
        }

        private static void SetDefault(FamilyManager fm, FamilyParameter fp, string type, string value)
        {
            try
            {
                switch (fp.StorageType)
                {
                    case StorageType.Double:
                        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                        {
                            // Inventory Length values are millimetres → convert to internal feet.
                            double v = string.Equals(type, "Length", StringComparison.OrdinalIgnoreCase) ? d / MmPerFoot : d;
                            fm.Set(fp, v);
                        }
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value, out int i)) fm.Set(fp, i);
                        break;
                    case StorageType.String:
                        fm.Set(fp, value ?? "");
                        break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetDefault {fp?.Definition?.Name}: {ex.Message}"); }
        }

        private static ForgeTypeId ResolveSpec(string type)
        {
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "length":  return SpecTypeId.Length;
                case "number":  return SpecTypeId.Number;
                case "integer": return SpecTypeId.Int.Integer;
                default:        return SpecTypeId.String.Text;
            }
        }

        private static Color ParseColor(string rgb)
        {
            if (string.IsNullOrWhiteSpace(rgb)) return null;
            var parts = rgb.Split(',');
            if (parts.Length != 3) return null;
            if (byte.TryParse(parts[0].Trim(), out byte r) &&
                byte.TryParse(parts[1].Trim(), out byte g) &&
                byte.TryParse(parts[2].Trim(), out byte b))
                return new Color(r, g, b);
            return null;
        }

        // ── Param-list / folder helpers ────────────────────────────────────

        private static List<string> BuildSharedParamList(JToken fam, List<string> commonTokens)
        {
            var set = new HashSet<string>(commonTokens, StringComparer.OrdinalIgnoreCase);
            if (fam["instanceParameters"] is JArray ips)
                foreach (var ip in ips)
                    if (string.Equals(ip["binding"]?.ToString(), "shared", StringComparison.OrdinalIgnoreCase))
                    {
                        var n = ip["name"]?.ToString();
                        if (!string.IsNullOrEmpty(n)) set.Add(n);
                    }
            return set.ToList();
        }

        private static string TierSubfolder(string tier)
        {
            string t = (tier ?? "").TrimStart();
            char d = t.Length > 0 ? t[0] : '0';
            switch (d)
            {
                case '1': return "1_AirTermination";
                case '2': return "2_DownConductors";
                case '3': return "3_Earth";
                case '4': return "4_Bonding";
                case '5': return "5_SPD";
                default:  return "0_Misc";
            }
        }

        private static string ResolveLpsFolder(Document doc)
        {
            try
            {
                string asmDir = Path.GetDirectoryName(StingToolsApp.AssemblyPath ?? "");
                if (!string.IsNullOrEmpty(asmDir))
                {
                    var candidates = new[]
                    {
                        Path.Combine(asmDir, "Families", "LPS"),
                        Path.Combine(asmDir, "..", "Families", "LPS"),
                        Path.Combine(asmDir, "..", "..", "Families", "LPS")
                    };
                    foreach (var p in candidates)
                    {
                        string full = Path.GetFullPath(p);
                        if (Directory.Exists(full)) return full;
                    }
                    return Path.GetFullPath(candidates[0]);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLpsFolder asm: {ex.Message}"); }

            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(projDir)) return Path.Combine(projDir, "Families", "LPS");
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLpsFolder proj: {ex.Message}"); }

            return Path.Combine(Path.GetTempPath(), "STING_LPS_Families");
        }

        private static string ResolveTemplateFolder(Application app)
        {
            try
            {
                string p = app.FamilyTemplatePath;
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"FamilyTemplatePath: {ex.Message}"); }

            try
            {
                string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                foreach (var ver in new[] { "2027", "2026", "2025" })
                {
                    var c = Path.Combine(pd, "Autodesk", "RVT " + ver, "Family Templates");
                    if (Directory.Exists(c)) return c;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFolder fallback: {ex.Message}"); }

            return null;
        }

        private static string ResolveTemplateFile(string folder, string templateName)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(templateName)) return null;
            try
            {
                string leaf = Path.GetFileName(templateName);
                if (!leaf.EndsWith(".rft", StringComparison.OrdinalIgnoreCase)) leaf += ".rft";
                var hits = Directory.GetFiles(folder, leaf, SearchOption.AllDirectories);
                if (hits.Length > 0) return hits[0];
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFile {templateName}: {ex.Message}"); }
            return null;
        }
    }
}
