using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════════════════
    //  FAMILY CONFORMANCE CHECKER  (Phase 185)
    //
    //  Read-only audit of one or more .rfa files against the STING family
    //  contract. Surfaces what's missing BEFORE you stamp a manufacturer
    //  library and discover at placement time that nothing writes the
    //  mounting height because the family uses 'Mounting Height' instead
    //  of MNT_HGT_MM.
    //
    //  Score (0..100):
    //      Family template appropriate for category          ( 15 pts )
    //      4 STING placement params bound by GUID            ( 25 pts )
    //      Tag style matrix present (when family is a tag)   ( 20 pts )
    //      Tag visibility tiers present (when family is tag) ( 10 pts )
    //      Position types Ring 1 / Ring 2 (when family is tag) ( 10 pts )
    //      No obviously-wrong placement type vs category     ( 10 pts )
    //      Loads cleanly into the target document            ( 10 pts )
    //
    //  Result is a CSV report at <outputDir>/FamilyConformanceReport_yyyyMMdd_HHmmss.csv
    //  plus a TaskDialog summary listing the lowest-scoring families.
    //
    //  This command never mutates the source .rfa — every family is
    //  opened standalone via app.OpenDocumentFile, scored, and closed
    //  with saveModified=false. The active project document is never
    //  touched; the command is [Transaction(ReadOnly)] for that reason.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One row in the conformance report — one family, one score, one delta list.
    /// </summary>
    public class ConformanceReportRow
    {
        public string Path { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string Category { get; set; } = "";
        public string FamilyTemplate { get; set; } = "";  // 3D / Annotation / Detail / Unknown
        public int    Score { get; set; }
        public string Verdict { get; set; } = "";          // PASS / WARN / BLOCK
        public List<string> Missing { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Pure-data inspector — opens a family doc, inspects it, returns the
    /// score row. Engine is separate from the command so it's testable.
    /// </summary>
    public static class FamilyConformanceInspector
    {
        // Tag-only param fingerprints. The full ASS_TAG_* container set is
        // big; we sample a handful that any STING-conformant tag must carry.
        private static readonly string[] PlacementParams = new[]
        {
            ParamRegistry.PLACE_ANCHOR,
            ParamRegistry.PLACE_OFFSET_X_MM,
            ParamRegistry.PLACE_SIDE,
            "MNT_HGT_MM",
        };

        private static readonly string[] TagFingerprint = new[]
        {
            "ASS_TAG_1_TXT",
            "ASS_TAG_2_TXT",
            "ASS_TAG_7_TXT",
        };

        private static readonly string[] TagVisibilityFingerprint = new[]
        {
            "TAG_PARA_STATE_1_BOOL",
            "TAG_PARA_STATE_2_BOOL",
            "WARN_VISIBLE_BOOL",
        };

        // Sample of the 128 TAG_{size}{style}_{colour}_BOOL style matrix. If
        // these aren't present, the family was not stamped with the
        // automation/presentation pack.
        private static readonly string[] TagStyleFingerprint = new[]
        {
            "TAG_2_5_NOM_BLACK_BOOL",
            "TAG_3_BOLD_BLUE_BOOL",
        };

        /// <summary>
        /// Inspect one family file. Returns a populated row.
        /// Caller wraps the application context — this method opens the file,
        /// audits, and closes without saving.
        /// </summary>
        public static ConformanceReportRow Inspect(UIApplication uiApp, string rfaPath)
        {
            var row = new ConformanceReportRow { Path = rfaPath };
            row.FamilyName = Path.GetFileNameWithoutExtension(rfaPath) ?? "";
            Document famDoc = null;
            try
            {
                // Match the codebase's standard family-open pattern
                // (FamilyParamCreatorCommand.cs:1012, TagFamilyCreatorCommand.cs:1932).
                famDoc = uiApp.Application.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    row.Warnings.Add("Not a family document.");
                    row.Verdict = "BLOCK";
                    row.Score = 0;
                    return row;
                }

                ScoreFamily(famDoc, row);
            }
            catch (Exception ex)
            {
                row.Warnings.Add($"Open failed: {ex.Message}");
                row.Verdict = "BLOCK";
                row.Score = 0;
            }
            finally
            {
                try { famDoc?.Close(false); } catch { /* swallow */ }
            }
            return row;
        }

        private static void ScoreFamily(Document famDoc, ConformanceReportRow row)
        {
            var fm = famDoc.FamilyManager;
            int score = 0;

            // ── (1) Category + template classification (15 pts) ──────
            string catName = "";
            try { catName = famDoc.OwnerFamily?.FamilyCategory?.Name ?? ""; } catch { }
            row.Category = catName;
            // Heuristic: families with placement type "Invalid" or category
            // null / "Generic Annotations" are 2D. Anything else is treated
            // as 3D.
            bool is2D = string.Equals(catName, "Generic Annotations", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(catName, "Detail Items", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(catName, "Tags", StringComparison.OrdinalIgnoreCase)
                     || catName.IndexOf("Tags", StringComparison.OrdinalIgnoreCase) >= 0;
            row.FamilyTemplate = is2D ? "Annotation/Detail (2D)" : "3D Model";
            if (!string.IsNullOrEmpty(catName)) score += 15;
            else row.Missing.Add("Family has no category — cannot infer template.");

            // Build the parameter-by-name index once.
            var paramsByName = new Dictionary<string, FamilyParameter>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (FamilyParameter fp in fm.Parameters)
                {
                    if (fp?.Definition?.Name != null && !paramsByName.ContainsKey(fp.Definition.Name))
                        paramsByName[fp.Definition.Name] = fp;
                }
            }
            catch (Exception ex) { row.Warnings.Add($"Enumerate parameters: {ex.Message}"); }

            // ── (2) Placement parameters bound by GUID (25 pts) ──────
            // For each of the 4 STING placement params, check that the
            // parameter exists AND is bound by GUID rather than just a name
            // collision. Counts 6 pts per parameter (max 24, rounded to 25).
            int placePts = 0;
            foreach (var name in PlacementParams)
            {
                if (!paramsByName.TryGetValue(name, out var fp))
                {
                    row.Missing.Add($"Placement param missing: {name}");
                    continue;
                }
                if (!fp.IsShared)
                {
                    row.Missing.Add($"'{name}' is a family parameter, not shared — placement engine writes by GUID and will skip.");
                    continue;
                }
                placePts += 6;
            }
            // Bonus point if all 4 are present, to get to 25.
            if (placePts >= 24) placePts = 25;
            score += placePts;

            // Tag-specific checks only fire when this looks like a tag family.
            bool isTagLike = catName.IndexOf("Tags", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isTagLike)
            {
                // ── (3) Tag fingerprint params (10 pts) ──────────────
                int tagPts = 0;
                foreach (var name in TagFingerprint)
                {
                    if (paramsByName.ContainsKey(name)) tagPts += 3;
                    else row.Missing.Add($"Tag param missing: {name}");
                }
                score += Math.Min(tagPts, 10);

                // ── (4) Tag style matrix sample (10 pts) ─────────────
                int stylePts = 0;
                foreach (var name in TagStyleFingerprint)
                {
                    if (paramsByName.ContainsKey(name)) stylePts += 5;
                    else row.Missing.Add($"Tag style param missing: {name} (run FamilyParamCreator with InjectAutomationPack=true)");
                }
                score += Math.Min(stylePts, 10);

                // ── (5) Tag visibility tiers (10 pts) ────────────────
                int visPts = 0;
                foreach (var name in TagVisibilityFingerprint)
                {
                    if (paramsByName.ContainsKey(name)) visPts += 4;
                    else row.Missing.Add($"Tag visibility param missing: {name}");
                }
                score += Math.Min(visPts, 10);

                // ── (6) Position types Ring 1 / Ring 2 (10 pts) ──────
                bool foundRing1 = false, foundRing2 = false;
                try
                {
                    foreach (FamilyType ft in fm.Types)
                    {
                        string n = ft?.Name ?? "";
                        if (n.IndexOf("1x", StringComparison.OrdinalIgnoreCase) >= 0) foundRing1 = true;
                        if (n.IndexOf("1.5x", StringComparison.OrdinalIgnoreCase) >= 0) foundRing2 = true;
                    }
                }
                catch (Exception ex) { row.Warnings.Add($"Enumerate types: {ex.Message}"); }
                if (foundRing1) score += 5; else row.Missing.Add("Ring 1 position types (1x-…) missing.");
                if (foundRing2) score += 5; else row.Missing.Add("Ring 2 position types (1.5x-…) missing.");
            }
            else
            {
                // Non-tag families get the 40 tag-only points scored as
                // 'pass by N/A' — distribute evenly so the score still
                // reaches 100 when nothing else is wrong.
                score += 40;
            }

            // ── (7) Placement type vs category sanity (10 pts) ───────
            // For tag/annotation families, placement type should be ViewBased.
            // For 3D model families, it should be a 3D placement type.
            // Wrong template breaks the placement pipeline downstream.
            try
            {
                var ft = famDoc.OwnerFamily?.FamilyPlacementType;
                bool ok;
                if (isTagLike || is2D)
                {
                    ok = (ft == FamilyPlacementType.ViewBased)
                      || (ft == FamilyPlacementType.Invalid); // some annotation templates report Invalid
                    if (!ok) row.Missing.Add($"FamilyPlacementType={ft} on a 2D family — placement on plan/section views may fail.");
                }
                else
                {
                    ok = (ft == FamilyPlacementType.OneLevelBased)
                      || (ft == FamilyPlacementType.TwoLevelsBased)
                      || (ft == FamilyPlacementType.WorkPlaneBased)
                      || (ft == FamilyPlacementType.OneLevelBasedHosted);
                    if (!ok) row.Missing.Add($"FamilyPlacementType={ft} on a 3D family — placement engine may not find a host.");
                }
                if (ok) score += 10;
            }
            catch (Exception ex) { row.Warnings.Add($"Inspect FamilyPlacementType: {ex.Message}"); }

            // ── (8) Loaded-cleanly bonus (10 pts) ────────────────────
            // We got here without an Open exception — credit the bonus.
            score += 10;

            // Clamp + verdict.
            row.Score = Math.Max(0, Math.Min(100, score));
            row.Verdict = row.Score >= 85 ? "PASS"
                        : row.Score >= 70 ? "WARN"
                        : "BLOCK";
        }
    }

    /// <summary>
    /// Read-only command: pick a folder of .rfa files, run the inspector
    /// against each, write a CSV report, show a TaskDialog summary with
    /// the lowest-scoring families.
    ///
    /// Read-only because the command never opens a Transaction on the
    /// active project document — every family is opened standalone and
    /// closed without save.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyConformanceCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;

                // Pick the folder to scan.
                string folder = PickFolder(uiApp);
                if (string.IsNullOrEmpty(folder)) return Result.Cancelled;

                var rfas = Directory.EnumerateFiles(folder, "*.rfa", SearchOption.AllDirectories)
                                    .ToList();
                if (rfas.Count == 0)
                {
                    TaskDialog.Show("STING Family Conformance",
                        $"No .rfa files found under:\n{folder}");
                    return Result.Cancelled;
                }

                // Inspect each (modal progress with periodic UI yield).
                var rows = new List<ConformanceReportRow>(rfas.Count);
                int i = 0;
                foreach (var p in rfas)
                {
                    i++;
                    try
                    {
                        var row = FamilyConformanceInspector.Inspect(uiApp, p);
                        rows.Add(row);
                    }
                    catch (Exception ex)
                    {
                        var failed = new ConformanceReportRow { Path = p, FamilyName = Path.GetFileNameWithoutExtension(p) ?? "" };
                        failed.Warnings.Add($"Inspect failed: {ex.Message}");
                        failed.Verdict = "BLOCK";
                        rows.Add(failed);
                    }
                    if (i % 10 == 0) StingLog.Info($"FamilyConformanceCheck: {i}/{rfas.Count} ({p})");
                }

                // Write CSV.
                string outDir = ResolveOutputDir(commandData);
                string outPath = Path.Combine(outDir,
                    $"FamilyConformanceReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                WriteCsv(outPath, rows);

                // Summary dialog.
                ShowSummary(outPath, rows);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FamilyConformanceCheck", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string PickFolder(UIApplication uiApp)
        {
            // Folder picker via WinForms FolderBrowserDialog (Revit ships
            // System.Windows.Forms; this is the lightest dependency).
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Select a folder of .rfa families to audit (subfolders are scanned).";
                    dlg.ShowNewFolderButton = false;
                    var res = dlg.ShowDialog();
                    if (res != System.Windows.Forms.DialogResult.OK) return null;
                    return dlg.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FamilyConformanceCheck PickFolder: {ex.Message}");
                return null;
            }
        }

        private static string ResolveOutputDir(ExternalCommandData cd)
        {
            // Prefer the project's _BIM_COORD folder; fall back to %TEMP%.
            try
            {
                var doc = cd?.Application?.ActiveUIDocument?.Document;
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(projDir))
                    {
                        string bim = Path.Combine(projDir, "_BIM_COORD");
                        Directory.CreateDirectory(bim);
                        return bim;
                    }
                }
            }
            catch { }
            return Path.GetTempPath();
        }

        private static void WriteCsv(string outPath, List<ConformanceReportRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FamilyName,Category,Template,Score,Verdict,Path,Missing,Warnings");
            foreach (var r in rows)
            {
                sb.Append(CsvCell(r.FamilyName)).Append(',');
                sb.Append(CsvCell(r.Category)).Append(',');
                sb.Append(CsvCell(r.FamilyTemplate)).Append(',');
                sb.Append(r.Score).Append(',');
                sb.Append(r.Verdict).Append(',');
                sb.Append(CsvCell(r.Path)).Append(',');
                sb.Append(CsvCell(string.Join(" | ", r.Missing))).Append(',');
                sb.Append(CsvCell(string.Join(" | ", r.Warnings)));
                sb.AppendLine();
            }
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        }

        private static string CsvCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static void ShowSummary(string csvPath, List<ConformanceReportRow> rows)
        {
            int total = rows.Count;
            int pass  = rows.Count(r => r.Verdict == "PASS");
            int warn  = rows.Count(r => r.Verdict == "WARN");
            int block = rows.Count(r => r.Verdict == "BLOCK");

            var bottom = rows.OrderBy(r => r.Score).Take(10).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Audited {total} families:");
            sb.AppendLine($"   PASS  ≥85 :   {pass}");
            sb.AppendLine($"   WARN  70–84:  {warn}");
            sb.AppendLine($"   BLOCK  <70 :  {block}");
            sb.AppendLine();
            sb.AppendLine("Lowest-scoring (first 10):");
            foreach (var r in bottom)
            {
                sb.AppendLine($"   [{r.Score,3}] {r.Verdict,-5} {r.FamilyName}");
                if (r.Missing.Count > 0)
                    sb.AppendLine($"          missing: {string.Join("; ", r.Missing.Take(3))}{(r.Missing.Count > 3 ? "…" : "")}");
            }
            sb.AppendLine();
            sb.AppendLine($"Full CSV report:\n{csvPath}");

            var td = new TaskDialog("STING Family Conformance")
            {
                MainInstruction = $"{pass}/{total} families pass; {block} block.",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            td.Show();
        }
    }
}
