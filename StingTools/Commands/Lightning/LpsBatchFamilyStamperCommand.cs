// LpsBatchFamilyStamperCommand.cs — Wave-final follow-up.
//
// One-click batch parameter-injection for the LPS family library.
// User picks a folder (default Families/LPS/) → walks every .rfa
// recursively → opens each as a family document → injects the
// ELC_LPS_* shared parameters per LPS_FAMILY_INVENTORY.json →
// saves back to source → closes.
//
// The actual injection mechanics live in FamilyParamEngine.ProcessFamily.
// This command is the orchestration shell — it picks the folder,
// resolves the param list per family category, aggregates results,
// and reports via StingResultPanel.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class LpsBatchFamilyStamperCommand : IExternalCommand, IPanelCommand
    {
        // LPS params every family should carry. Generated from
        // LPS_FAMILY_INVENTORY.json common-params section + per-family
        // shared-binding list, deduped. Acts as the fallback when the
        // inventory file isn't found at runtime.
        private static readonly string[] DefaultLpsParams =
        {
            "ELC_LPS_ELEMENT_TYPE_TXT",
            "ELC_LPS_CLASS_TXT",
            "ELC_LPS_ROLLING_SPHERE_RADIUS_M",
            "ELC_LPS_PROTECTION_ANGLE_DEG",
            "ELC_LPS_CONDUCTOR_MATERIAL_TXT",
            "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2",
            "ELC_LPS_EARTH_TYPE_TXT",
            "ELC_LPS_EARTH_RESISTANCE_OHM",
            "ELC_LPS_BOND_TYPE_TXT",
            "ELC_LPS_FROM_LPZ_TXT",
            "ELC_LPS_TO_LPZ_TXT",
            "ELC_LPS_SEPARATION_DISTANCE_MM",
            "ELC_LPS_SURGE_PROTECTION_LVL_TXT",
            "ELC_LPS_ZONE_TXT",
            "ELC_LPS_TEST_DATE_TXT",
            "ELC_LPS_CERT_REF_TXT",
            "ELC_LPS_COMPLIANCE_STATUS_TXT",
            "ELC_LPS_AIRTERM_TAG_TXT",
            "ELC_LPS_DOWNCOND_TAG_TXT",
            "ELC_LPS_EARTH_TAG_TXT",
            "ELC_LPS_BOND_TAG_TXT",
            "ELC_LPS_SPD_TAG_TXT",
            "ELC_LPS_TESTCLAMP_TAG_TXT",
            "ELC_TAG_7_PARA_LPS_TXT",
            "ASS_TAG_1",
            "ASS_DISCIPLINE_COD_TXT",
            "ASS_SYSTEM_TYPE_TXT",
            "ASS_FUNC_TXT",
            "ASS_PRODCT_COD_TXT",
            "ASS_SEQ_NUM_TXT",
            "ASS_LOC_TXT",
            "ASS_ZONE_TXT",
            "ASS_LVL_COD_TXT"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — LPS Family Stamper", "No active document.");
                return Result.Cancelled;
            }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            // ── 1. Resolve folder via file-picker (folder mode) ──────
            string folder = PickFolder(doc);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return Result.Cancelled;
            }

            // ── 2. Walk for .rfa recursively ──────────────────────────
            string[] rfas;
            try
            {
                rfas = Directory.GetFiles(folder, "*.rfa", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsBatchFamilyStamper enumerate", ex);
                TaskDialog.Show("STING — LPS Family Stamper", "Folder walk failed: " + ex.Message);
                return Result.Failed;
            }
            if (rfas.Length == 0)
            {
                TaskDialog.Show("STING — LPS Family Stamper",
                    $"No .rfa files found under:\n{folder}\n\n" +
                    "Drop vendor families (DEHN / OBO / nVent / Furse) into the right tier folder " +
                    "(see Families/LPS/AUTHORING_GUIDE.md § 13) and re-run.");
                return Result.Cancelled;
            }

            // ── 3. Confirm — large batches can take 10+ minutes ──────
            var confirm = new TaskDialog("STING — LPS Family Stamper")
            {
                MainInstruction = $"Stamp STING shared parameters into {rfas.Length} family file(s)?",
                MainContent =
                    $"Folder: {folder}\n" +
                    $"Files found: {rfas.Length}\n" +
                    $"Estimated time: ~{Math.Max(1, rfas.Length / 4)} minute(s) " +
                    $"({rfas.Length} × ~15 s per family)\n\n" +
                    "Each .rfa will be opened, parameters from LPS_FAMILY_INVENTORY.json " +
                    "will be injected, then saved back over the source file. " +
                    "Geometry is not touched.\n\n" +
                    "Caveat: Revit will appear unresponsive during the run. " +
                    "Don't switch documents or close Revit until complete.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Ok
            };
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            // ── 4. Load the param list from the inventory (fall back  ──
            //      to DefaultLpsParams if the JSON isn't accessible). ─
            var paramList = ResolveParamList();

            // ── 5. Iterate ────────────────────────────────────────────
            var opts = new FamilyParamEngine.ProcessOptions
            {
                ParamNames = paramList,
                Purge = PurgeMode.None,
                InjectFormulas = false,
                CreatePositionTypes = false,
                InjectTagPos = false,
                InjectAutomationPack = false
            };

            int ok = 0, failed = 0;
            int totalParamsAdded = 0, totalParamsSkipped = 0;
            var failures = new List<string>();
            var perFamily = new List<string[]>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < rfas.Length; i++)
            {
                string rfa = rfas[i];
                try
                {
                    var result = FamilyParamEngine.ProcessFamily(
                        app.Application, rfa, rfa, opts);

                    if (string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        ok++;
                        totalParamsAdded   += result.ParamsAdded;
                        totalParamsSkipped += result.ParamsSkipped;
                        perFamily.Add(new[]
                        {
                            Path.GetFileName(rfa),
                            result.Category ?? "",
                            result.ParamsAdded.ToString(),
                            result.ParamsSkipped.ToString(),
                            "✓"
                        });
                    }
                    else
                    {
                        failed++;
                        failures.Add($"  ▸ {Path.GetFileName(rfa)}: {result.ErrorMessage}");
                        perFamily.Add(new[]
                        {
                            Path.GetFileName(rfa),
                            result.Category ?? "",
                            "0", "0",
                            "✗ " + (result.ErrorMessage ?? "")
                        });
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"  ▸ {Path.GetFileName(rfa)}: {ex.Message}");
                    StingLog.Error($"LpsBatchFamilyStamper '{rfa}'", ex);
                }
            }
            sw.Stop();

            // ── 6. Report via StingResultPanel ────────────────────────
            var rp = StingResultPanel.Create("LPS — Batch Family Parameter Stamper");
            rp.SetSubtitle($"Folder: {folder}  •  Duration: {sw.Elapsed.TotalSeconds:F1} s");
            rp.AddSection("SUMMARY")
              .Metric("Families found", rfas.Length.ToString())
              .MetricHighlight("Stamped OK", ok.ToString())
              .MetricError("Failed", failed.ToString())
              .Metric("Params added (total)", totalParamsAdded.ToString())
              .Metric("Params skipped (already present)", totalParamsSkipped.ToString());

            if (failures.Count > 0)
            {
                var sec = rp.AddSection("FAILURES");
                foreach (var f in failures.Take(20)) sec.Text(f);
                if (failures.Count > 20)
                    sec.Text($"  …and {failures.Count - 20} more — see StingTools.log");
            }

            if (perFamily.Count > 0)
            {
                rp.AddSection("DETAIL")
                  .Table(new[] { "File", "Category", "Added", "Skipped", "Status" },
                         perFamily.Take(100).ToList());
                if (perFamily.Count > 100)
                    rp.Text($"(+{perFamily.Count - 100} more — full report in StingTools.log)");
            }

            rp.Show();
            StingLog.Info(
                $"LpsBatchFamilyStamper: {ok} ok / {failed} failed / " +
                $"{totalParamsAdded} added / {totalParamsSkipped} skipped in {sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static string PickFolder(Document doc)
        {
            // WinForms FolderBrowserDialog — Revit ships System.Windows.Forms,
            // and this matches the pattern used by FamilyConformanceCheckCommand.
            string defaultFolder = ResolveDefaultFolder(doc);
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Select the LPS family folder. Subfolders are walked recursively " +
                                      "and every .rfa gets STING shared parameters injected.";
                    dlg.ShowNewFolderButton = false;
                    if (!string.IsNullOrEmpty(defaultFolder)) dlg.SelectedPath = defaultFolder;
                    var res = dlg.ShowDialog();
                    if (res != System.Windows.Forms.DialogResult.OK) return null;
                    return dlg.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PickFolder: {ex.Message}");
                return null;
            }
        }

        private static string ResolveDefaultFolder(Document doc)
        {
            // 1) Families/LPS/ next to StingTools.dll
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
                        if (Directory.Exists(p)) return Path.GetFullPath(p);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveDefaultFolder asm: {ex.Message}"); }

            // 2) Project folder
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(projDir) && Directory.Exists(projDir))
                        return projDir;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveDefaultFolder proj: {ex.Message}"); }

            return null;
        }

        private static List<string> ResolveParamList()
        {
            // Try LPS_FAMILY_INVENTORY.json first — read the
            // commonStingTokens array + every instanceParameters[].name
            // where binding=="shared" across all 20 family entries.
            try
            {
                string path = StingToolsApp.FindDataFile("LPS_FAMILY_INVENTORY.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (root["commonStingTokens"] is JArray tokens)
                        foreach (var t in tokens)
                        {
                            string s = t.ToString();
                            if (!string.IsNullOrEmpty(s)) set.Add(s);
                        }
                    if (root["families"] is JArray fams)
                    {
                        foreach (var f in fams)
                        {
                            if (f["instanceParameters"] is JArray ips)
                                foreach (var ip in ips)
                                {
                                    string binding = ip["binding"]?.ToString();
                                    string name    = ip["name"]?.ToString();
                                    if (!string.IsNullOrEmpty(name) &&
                                        string.Equals(binding, "shared", StringComparison.OrdinalIgnoreCase))
                                        set.Add(name);
                                }
                        }
                    }
                    if (set.Count > 0) return set.ToList();
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveParamList json: {ex.Message}"); }

            // Fallback to hardcoded list
            return DefaultLpsParams.ToList();
        }
    }
}
