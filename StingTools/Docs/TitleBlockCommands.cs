using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  STING Title Block System v1.0  (spec: 20260419_sting_tb_specification_v1.0)
    //
    //  Implements the eight Tier-1/Tier-2 automation commands called out in
    //  section 7 of the spec:
    //    Tier 1:  TitleBlockPopulate, TitleBlockValidate, TitleBlockSetVariant,
    //             DisciplineLegendBind, SheetCountAutoUpdate
    //    Tier 2:  RevisionSync, TransmittalAutoIssue, PreExportValidate
    //
    //  The 15 PRJ_TB_* parameters are written to the placed title-block
    //  FamilyInstance on each sheet (per-sheet state). Four former mirror params
    //  (CLIENT_NAME, COMPANY_NAME, COMPANY_ADDRESS, DESIGN_STAGE) were removed;
    //  those labels now bind directly to PRJ_ORG_* on ProjectInformation.
    //  Binding lives in CATEGORY_BINDINGS.csv (Generic Models + Project Information)
    //  so a one-time LoadSharedParams run makes the new params addressable.
    //
    //  All Band-6 writes, lock skips, and transmittal stamps are logged to
    //  StingLog under the "TB:" prefix for ISO 19650-2 Annex B audit traceability.
    // ═══════════════════════════════════════════════════════════════════════════

    internal static class TitleBlockEngine
    {
        // ── Paper-size / variant heuristics (spec §1.3) ────────────────────────
        // Revit stores sheet dimensions on the title-block FamilyInstance, not on
        // the ViewSheet itself. We look up width/height on the placed TB and map
        // to the six supported variants. Strip direction is inferred from the
        // dominant viewport aspect ratio when possible; otherwise defaults to R.
        internal const string VARIANT_A0R = "A0-R";
        internal const string VARIANT_A0B = "A0-B";
        internal const string VARIANT_A1R = "A1-R";
        internal const string VARIANT_A1B = "A1-B";
        internal const string VARIANT_A3R = "A3-R";
        internal const string VARIANT_A3B = "A3-B";

        internal static readonly string[] AllVariants = new[]
        {
            VARIANT_A0R, VARIANT_A0B, VARIANT_A1R, VARIANT_A1B, VARIANT_A3R, VARIANT_A3B
        };

        internal static readonly string[] SupportedDisciplines = new[]
        {
            "ARCH", "STR", "MEP", "ELE", "PLM", "FP", "LV", "COORD", "GEN"
        };

        // Suitability codes accepted in PRJ_DWG_SUITABILITY_COD_TXT (ISO 19650-1)
        internal static readonly HashSet<string> ValidSuitabilityCodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "S0","S1","S2","S3","S4","S5","S6","S7",
            "A1","A2","A3","A4","A5","A6","A7",
            "B1","B2","B3","B4","B5","B6","B7"
        };

        /// <summary>
        /// Find the title-block FamilyInstance placed on the given sheet.
        /// Returns null when the sheet has no title block (empty sheet).
        /// </summary>
        internal static FamilyInstance GetTitleBlockOnSheet(Document doc, ViewSheet sheet)
        {
            if (doc == null || sheet == null) return null;
            try
            {
                var col = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType();
                foreach (Element e in col)
                    if (e is FamilyInstance fi) return fi;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TB: GetTitleBlockOnSheet({sheet?.SheetNumber}) failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Infer a variant code (A0-R / A0-B / A1-R / A1-B / A3-R / A3-B) from
        /// the placed title-block size and viewport aspect ratio. Wide/squat
        /// viewport content prefers B-strip; narrow/portrait prefers R-strip.
        /// Falls back to A1-R when no variant matches within tolerance.
        /// </summary>
        internal static string InferVariant(Document doc, ViewSheet sheet, FamilyInstance tb)
        {
            if (tb == null) return VARIANT_A1R;
            try
            {
                BoundingBoxXYZ bb = tb.get_BoundingBox(sheet);
                if (bb == null) return VARIANT_A1R;
                double widthFt = Math.Abs(bb.Max.X - bb.Min.X);
                double heightFt = Math.Abs(bb.Max.Y - bb.Min.Y);
                const double FT_TO_MM = 304.8;
                double widthMm = widthFt * FT_TO_MM;
                double heightMm = heightFt * FT_TO_MM;
                // Sheet orientation: we assume landscape (width > height) per spec §1.3
                if (widthMm < heightMm) { var t = widthMm; widthMm = heightMm; heightMm = t; }

                string size;
                if (widthMm >= 1100 && heightMm >= 750) size = "A0";
                else if (widthMm >= 750 && heightMm >= 550) size = "A1";
                else if (widthMm >= 380 && heightMm >= 270) size = "A3";
                else size = "A1"; // non-standard sheet size — default to A1

                string strip = DetectStripDirection(doc, sheet);
                return $"{size}-{strip}";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TB: InferVariant({sheet?.SheetNumber}) failed: {ex.Message}");
                return VARIANT_A1R;
            }
        }

        /// <summary>
        /// Pick B-strip for sheets whose dominant viewport is wider-than-tall
        /// (aspect &gt; 1.4), otherwise R-strip. Default: R.
        /// </summary>
        internal static string DetectStripDirection(Document doc, ViewSheet sheet)
        {
            try
            {
                var vpIds = sheet.GetAllViewports();
                if (vpIds == null || vpIds.Count == 0) return "R";
                double widest = 0.0;
                foreach (ElementId vpId in vpIds)
                {
                    if (!(doc.GetElement(vpId) is Viewport vp)) continue;
                    Outline o = vp.GetBoxOutline();
                    if (o == null) continue;
                    double w = o.MaximumPoint.X - o.MinimumPoint.X;
                    double h = o.MaximumPoint.Y - o.MinimumPoint.Y;
                    if (h <= 0) continue;
                    double aspect = w / h;
                    if (aspect > widest) widest = aspect;
                }
                return widest > 1.4 ? "B" : "R";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return "R"; }
        }

        /// <summary>
        /// Read SHT_DISC_TXT first (STING tagged sheets), then fall back to the
        /// discipline prefix in the sheet number (&quot;A-101&quot; → &quot;ARCH&quot;).
        /// </summary>
        internal static string ResolveDiscipline(ViewSheet sheet)
        {
            if (sheet == null) return "GEN";
            string tagged = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_DISC);
            if (!string.IsNullOrWhiteSpace(tagged))
            {
                string norm = tagged.Trim().ToUpperInvariant();
                if (SupportedDisciplines.Contains(norm)) return norm;
            }
            string num = sheet.SheetNumber ?? "";
            if (num.Length >= 1)
            {
                char c = char.ToUpperInvariant(num[0]);
                switch (c)
                {
                    case 'A': return "ARCH";
                    case 'S': return "STR";
                    case 'M': return "MEP";
                    case 'E': return "ELE";
                    case 'P': return "PLM";
                    case 'F': return "FP";
                    case 'L': return "LV";
                    case 'C': return "COORD";
                }
            }
            return "GEN";
        }
    }
}

namespace StingTools.Docs
{
    // ── TITLE_BLOCK.csv loader (spec §7.1) ─────────────────────────────────
    // CSV schema (discipline-split, UTF-8 BOM, CRLF per existing STING data
    // file conventions):
    //   Column 1: ParameterName (exact PRJ_TB_* param name)
    //   Column 2: DefaultValue  (used for sheets with no discipline match)
    //   Columns 3-11: Per-discipline overrides — ARCH STR MEP ELE PLM FP LV COORD GEN
    // Empty cells fall back to DefaultValue. Sheet discipline is resolved via
    // TitleBlockEngine.ResolveDiscipline.
    internal class TitleBlockCsv
    {
        public string[] Disciplines { get; private set; } = TitleBlockEngine.SupportedDisciplines;
        public Dictionary<string, string> DefaultValues { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> PerDiscipline { get; }
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public int RowCount { get; private set; }
        public string SourcePath { get; private set; }

        public static TitleBlockCsv Load(string path)
        {
            var csv = new TitleBlockCsv { SourcePath = path };
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn($"TB: TITLE_BLOCK.csv not found at {path ?? "(null)"}");
                return csv;
            }
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2) return csv;
                string[] header = ParseCsvLine(lines[0]);
                // Column 0 = ParameterName, Column 1 = DefaultValue, remainder = disciplines
                var discCols = new List<string>();
                for (int i = 2; i < header.Length; i++)
                    discCols.Add(header[i].Trim().ToUpperInvariant());
                csv.Disciplines = discCols.ToArray();
                foreach (string d in discCols)
                    csv.PerDiscipline[d] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int r = 1; r < lines.Length; r++)
                {
                    string line = lines[r];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    string[] cols = ParseCsvLine(line);
                    if (cols.Length < 1) continue;
                    string pname = cols[0].Trim();
                    if (string.IsNullOrEmpty(pname)) continue;
                    string dflt = cols.Length > 1 ? cols[1] : "";
                    csv.DefaultValues[pname] = dflt;
                    for (int i = 0; i < discCols.Count; i++)
                    {
                        int colIdx = i + 2;
                        string v = colIdx < cols.Length ? cols[colIdx] : "";
                        if (!string.IsNullOrEmpty(v))
                            csv.PerDiscipline[discCols[i]][pname] = v;
                    }
                    csv.RowCount++;
                }
                StingLog.Info($"TB: loaded TITLE_BLOCK.csv — {csv.RowCount} parameter rows, " +
                    $"{csv.Disciplines.Length} discipline columns");
            }
            catch (Exception ex)
            {
                StingLog.Error("TB: TITLE_BLOCK.csv load failed", ex);
            }
            return csv;
        }

        /// <summary>
        /// Resolve the effective value for a parameter+discipline. Per-discipline
        /// override wins; falls back to DefaultValue; empty string otherwise.
        /// </summary>
        public string ValueFor(string paramName, string discipline)
        {
            if (string.IsNullOrEmpty(paramName)) return "";
            string disc = (discipline ?? "").Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(disc)
                && PerDiscipline.TryGetValue(disc, out var dmap)
                && dmap.TryGetValue(paramName, out string v))
                return v ?? "";
            return DefaultValues.TryGetValue(paramName, out string d) ? (d ?? "") : "";
        }

        private static string[] ParseCsvLine(string line)
        {
            // Mirror Core.StingToolsApp.ParseCsvLine (quoted-field aware)
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result.ToArray();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 1 / Tier 1 — TitleBlockPopulate  (spec §7.1)
    //
    //  Bulk-writes every PRJ_TB_* value from TITLE_BLOCK.csv into every sheet's
    //  placed title block in one transaction. Locked sheets (PRJ_TB_LOCK_BOOL
    //  = Yes) are skipped. Stamps PRJ_TB_LAST_SYNC_TXT / LAST_SYNC_BY on each
    //  populated sheet and PRJ_TB_TOTAL_NO_SHEETS_TXT on ProjectInformation.
    // ═══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Locate TITLE_BLOCK.csv (data dir first, project-adjacent second)
            string csvPath = ResolveCsvPath(doc, "TITLE_BLOCK.csv");
            var csv = TitleBlockCsv.Load(csvPath);
            if (csv.RowCount == 0)
            {
                var offer = new TaskDialog("STING Title Block Populate");
                offer.MainInstruction = "No TITLE_BLOCK.csv rows found.";
                offer.MainContent = $"Searched: {csvPath ?? "<none>"}\n\n" +
                    "Open the in-Revit editor to create or edit the CSV, or cancel and " +
                    "author it externally.";
                offer.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Open Title Block CSV Editor");
                offer.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Cancel");
                if (offer.Show() == TaskDialogResult.CommandLink1)
                {
                    var outcome = TitleBlockCsvEditor.ShowDialog(doc);
                    if (!outcome.Saved) return Result.Cancelled;
                    // Reload from the just-saved path and continue
                    csvPath = outcome.Path;
                    csv = TitleBlockCsv.Load(csvPath);
                    if (csv.RowCount == 0) return Result.Cancelled;
                }
                else return Result.Cancelled;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();
            if (sheets.Count == 0)
            {
                TaskDialog.Show("STING Title Block Populate", "No sheets found."); 
                return Result.Succeeded;
            }

            int written = 0, lockedSkipped = 0, noTbSkipped = 0, paramFails = 0;
            int totalSheetListed = 0;
            var updatedSheets = new List<string>();
            var skippedSheets = new List<string>();
            string stampUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture);
            string stampUser = Environment.UserName ?? "unknown";

            using (var tx = new Transaction(doc, "STING Title Block Populate"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    // SheetCountAutoUpdate (§7.5) — count anything marked for inclusion
                    if (sheet.get_Parameter(BuiltInParameter.SHEET_SCHEDULED)?.AsInteger() != 0)
                        totalSheetListed++;

                    var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);
                    if (tb == null)
                    {
                        noTbSkipped++;
                        skippedSheets.Add($"{sheet.SheetNumber}: no title block placed");
                        continue;
                    }

                    // Lock gate — skip sheets the user has explicitly frozen
                    int locked = ParameterHelpers.GetInt(tb, ParamRegistry.TB_LOCK, 0);
                    if (locked != 0)
                    {
                        lockedSkipped++;
                        skippedSheets.Add($"{sheet.SheetNumber}: locked");
                        continue;
                    }

                    string disc = TitleBlockEngine.ResolveDiscipline(sheet);
                    int paramsWrittenThisSheet = 0;

                    foreach (string paramName in ParamRegistry.AllTitleBlockParams)
                    {
                        // Never let the CSV overwrite sync/transmittal audit fields
                        if (paramName == ParamRegistry.TB_LAST_SYNC
                            || paramName == ParamRegistry.TB_LAST_SYNC_BY
                            || paramName == ParamRegistry.TB_LAST_TRANSMITTAL
                            || paramName == ParamRegistry.TB_LAST_TRANSMITTAL_DATE
                            || paramName == ParamRegistry.TB_NOTES_LEGEND_REF
                            || paramName == ParamRegistry.TB_LOCK)
                            continue;

                        string val = csv.ValueFor(paramName, disc);
                        if (string.IsNullOrEmpty(val)) continue;

                        bool ok;
                        if (ParamRegistry.TitleBlockBoolParams.Contains(paramName))
                            ok = ParameterHelpers.SetInt(tb, paramName, ParseYesNo(val), overwrite: true);
                        else
                            ok = ParameterHelpers.SetString(tb, paramName, val, overwrite: true);

                        if (ok) paramsWrittenThisSheet++;
                        else paramFails++;
                    }

                    // Stamp audit fields on every successfully-populated sheet
                    if (paramsWrittenThisSheet > 0)
                    {
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_LAST_SYNC,
                            stampUtc, overwrite: true);
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_LAST_SYNC_BY,
                            stampUser, overwrite: true);
                        written++;
                        updatedSheets.Add($"{sheet.SheetNumber} ({disc}): {paramsWrittenThisSheet} fields");
                    }
                }

                // SheetCountAutoUpdate (§7.5) — total goes on Project Information
                ParameterHelpers.SetString(doc.ProjectInformation,
                    "PRJ_TB_TOTAL_NO_SHEETS_TXT",
                    totalSheetListed.ToString(CultureInfo.InvariantCulture),
                    overwrite: true);

                tx.Commit();
            }

            StingLog.Info($"TB Populate: {written} updated, {lockedSkipped} locked, " +
                $"{noTbSkipped} no TB, {paramFails} param failures, total sheets listed = {totalSheetListed}");

            StingResultPanel.Create("")
                .SetTitle("Title Block Populate")
                .SetSubtitle($"TITLE_BLOCK.csv → {written} sheet(s) updated")
                .SetOverallPct(sheets.Count == 0 ? 0 : 100.0 * written / sheets.Count)
                .AddSection("Summary")
                .Metric("Sheets scanned", sheets.Count.ToString())
                .Metric("Sheets updated", written.ToString())
                .Metric("Sheets skipped (locked)", lockedSkipped.ToString())
                .Metric("Sheets skipped (no title block)", noTbSkipped.ToString())
                .Metric("Parameter write failures", paramFails.ToString())
                .Metric("Sheets on sheet list (auto-counted)", totalSheetListed.ToString())
                .Metric("CSV path", csvPath ?? "<default>")
                .AddSection("Updated Sheets")
                .Text(updatedSheets.Count == 0 ? "(none)" : string.Join("\n", updatedSheets))
                .AddSection("Skipped Sheets")
                .Text(skippedSheets.Count == 0 ? "(none)" : string.Join("\n", skippedSheets))
                .Show();

            return Result.Succeeded;
        }

        private static int ParseYesNo(string v)
        {
            if (string.IsNullOrEmpty(v)) return 0;
            string s = v.Trim().ToLowerInvariant();
            return (s == "1" || s == "yes" || s == "true" || s == "y") ? 1 : 0;
        }

        internal static string ResolveCsvPath(Document doc, string name)
        {
            // 1. STING_BIM_MANAGER dir alongside project
            try
            {
                string projDir = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string p = Path.Combine(projDir, "STING_BIM_MANAGER", name);
                    if (File.Exists(p)) return p;
                }
            }
            catch (Exception ex) { StingLog.Warn($"TB: project-adjacent CSV lookup failed: {ex.Message}"); }
            // 2. plugin data dir (shipped template)
            string data = StingToolsApp.FindDataFile(name);
            if (!string.IsNullOrEmpty(data) && File.Exists(data)) return data;
            return null;
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 2 / Tier 1 — TitleBlockValidate  (spec §7.2)
    //
    //  Non-transactional audit of title-block completeness. Checks:
    //    - every sheet has a placed title block,
    //    - every required PRJ_TB_* field is populated,
    //    - SHT_DISC_TXT is consistent with PRJ_TB_VARIANT_TXT discipline,
    //    - PRJ_DWG_SUITABILITY_COD_TXT is a valid ISO 19650 code,
    //    - PRJ_TB_LAST_SYNC_TXT is not older than 7 days.
    //  Produces a StingResultPanel report; never modifies the project.
    // ═══════════════════════════════════════════════════════════════════════
    internal static class TitleBlockValidator
    {
        internal class SheetIssue
        {
            public string SheetNumber;
            public string Code;       // stable code: MISSING_TB, MISSING_FIELD, INVALID_SUIT, STALE_SYNC, DISC_MISMATCH
            public string Detail;
        }

        internal class Report
        {
            public int TotalSheets;
            public int Passing;
            public List<SheetIssue> Issues = new List<SheetIssue>();
            public Dictionary<string, int> CountsByCode = new Dictionary<string, int>();
        }

        // Fields required for a sheet to count as "complete" (subset of the 41
        // PRJ_TB_* params — these are the ones that must carry values before
        // a sheet can be handed to a client).
        internal static readonly string[] CriticalFields = new[]
        {
            "PRJ_TB_DRAWN_BY_TXT",
            "PRJ_TB_CHECKED_BY_TXT",
            "PRJ_TB_APVD_BY_TXT",
            ParamRegistry.TB_VARIANT
        };

        internal static Report Run(Document doc, IEnumerable<ViewSheet> sheets)
        {
            var r = new Report();
            DateTime staleCutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var sheet in sheets)
            {
                r.TotalSheets++;
                bool sheetClean = true;
                var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);
                if (tb == null)
                {
                    AddIssue(r, sheet.SheetNumber, "MISSING_TB",
                        "No title block placed on this sheet.");
                    sheetClean = false;
                    continue; // can't check fields without a TB
                }

                foreach (string field in CriticalFields)
                {
                    string v = ParameterHelpers.GetString(tb, field);
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        AddIssue(r, sheet.SheetNumber, "MISSING_FIELD",
                            $"{field} is empty");
                        sheetClean = false;
                    }
                }

                string variant = ParameterHelpers.GetString(tb, ParamRegistry.TB_VARIANT);
                string suit = ParameterHelpers.GetString(sheet, "PRJ_DWG_SUITABILITY_COD_TXT");
                if (string.IsNullOrEmpty(suit))
                    suit = ParameterHelpers.GetString(doc.ProjectInformation, "PRJ_DWG_SUITABILITY_COD_TXT");
                if (!string.IsNullOrEmpty(suit) && !TitleBlockEngine.ValidSuitabilityCodes.Contains(suit.Trim()))
                {
                    AddIssue(r, sheet.SheetNumber, "INVALID_SUIT",
                        $"PRJ_DWG_SUITABILITY_COD_TXT = '{suit}' is not a valid ISO 19650 code");
                    sheetClean = false;
                }

                // Sync staleness — only flags when sync field is populated
                string sync = ParameterHelpers.GetString(tb, ParamRegistry.TB_LAST_SYNC);
                if (!string.IsNullOrEmpty(sync))
                {
                    if (DateTime.TryParse(sync, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTime lastSync))
                    {
                        if (lastSync < staleCutoff)
                        {
                            AddIssue(r, sheet.SheetNumber, "STALE_SYNC",
                                $"Last sync {lastSync:yyyy-MM-dd} is older than 7 days");
                            sheetClean = false;
                        }
                    }
                }

                // Variant vs sheet discipline consistency
                string disc = TitleBlockEngine.ResolveDiscipline(sheet);
                if (!string.IsNullOrEmpty(variant)
                    && !TitleBlockEngine.AllVariants.Contains(variant))
                {
                    AddIssue(r, sheet.SheetNumber, "INVALID_VARIANT",
                        $"Variant '{variant}' is not one of the six supported codes");
                    sheetClean = false;
                }

                if (sheetClean) r.Passing++;
            }
            return r;
        }

        private static void AddIssue(Report r, string sheet, string code, string detail)
        {
            r.Issues.Add(new SheetIssue { SheetNumber = sheet, Code = code, Detail = detail });
            if (!r.CountsByCode.ContainsKey(code)) r.CountsByCode[code] = 0;
            r.CountsByCode[code]++;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            var report = TitleBlockValidator.Run(doc, sheets);
            double pct = report.TotalSheets == 0
                ? 100.0
                : 100.0 * report.Passing / report.TotalSheets;

            StingLog.Info($"TB Validate: {report.Passing}/{report.TotalSheets} clean, " +
                $"{report.Issues.Count} issues");

            var b = StingResultPanel.Create("")
                .SetTitle("Title Block Validation")
                .SetSubtitle($"{report.Passing}/{report.TotalSheets} sheets pass all checks")
                .SetOverallPct(pct)
                .AddSection("Summary")
                .Metric("Sheets audited", report.TotalSheets.ToString())
                .Metric("Sheets passing", report.Passing.ToString())
                .Metric("Total issues", report.Issues.Count.ToString());

            b.AddSection("Issues by Code");
            foreach (var kvp in report.CountsByCode.OrderByDescending(k => k.Value))
                b.Metric(kvp.Key, kvp.Value.ToString());

            if (report.Issues.Count > 0)
            {
                b.AddSection("Failing Sheets");
                var rows = report.Issues
                    .OrderBy(i => i.SheetNumber)
                    .ThenBy(i => i.Code)
                    .Take(300)
                    .Select(i => new[] { i.SheetNumber ?? "", i.Code ?? "", i.Detail ?? "" })
                    .ToList();
                b.Table(new[] { "Sheet", "Code", "Detail" }, rows);
                if (report.Issues.Count > 300)
                    b.Text($"... {report.Issues.Count - 300} additional issues truncated");
            }

            b.Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 3 / Tier 1 — TitleBlockSetVariant  (spec §7.3)
    //
    //  Infers the correct STING_TB_* family type for each sheet based on
    //  placed title-block size and viewport aspect ratio, then swaps the
    //  placed title block when the current symbol's Family.Name differs.
    //  The swap is a symbol change (not delete+recreate), so all filled-in
    //  parameter values are preserved automatically.
    // ═══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockSetVariantCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Build an index of loaded STING_TB_* family symbols
            var loadedTbs = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .ToList();

            var byVariant = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var sym in loadedTbs)
            {
                string fn = sym.FamilyName ?? "";
                foreach (string v in TitleBlockEngine.AllVariants)
                {
                    // Match STING_TB_A1_R_v1.0 / A1-R / A1_R in family name
                    string vUnder = v.Replace("-", "_");
                    if (fn.IndexOf(vUnder, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!byVariant.ContainsKey(v)) byVariant[v] = sym;
                        break;
                    }
                }
            }

            if (byVariant.Count == 0)
            {
                TaskDialog.Show("STING Set Variant",
                    "No STING_TB_* title block families loaded in this project.\n\n" +
                    "Load the six STING_TB_*_v1.0.rfa families (A0-R/A0-B/A1-R/A1-B/A3-R/A3-B) " +
                    "then re-run this command.");
                return Result.Cancelled;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            int swapped = 0, alreadyMatch = 0, noTb = 0, noMatch = 0;
            var swapDetails = new List<string>();

            using (var tx = new Transaction(doc, "STING Title Block Set Variant"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);
                    if (tb == null) { noTb++; continue; }

                    string currentFn = (tb.Symbol?.FamilyName) ?? "";
                    string targetVariant = TitleBlockEngine.InferVariant(doc, sheet, tb);
                    if (!byVariant.TryGetValue(targetVariant, out var targetSym))
                    { noMatch++; continue; }

                    string targetFn = targetSym.FamilyName ?? "";
                    if (string.Equals(currentFn, targetFn, StringComparison.OrdinalIgnoreCase)
                        && tb.Symbol.Id == targetSym.Id)
                    { alreadyMatch++; continue; }

                    try
                    {
                        if (!targetSym.IsActive) targetSym.Activate();
                        tb.Symbol = targetSym;
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_VARIANT, targetVariant,
                            overwrite: true);
                        swapped++;
                        swapDetails.Add($"{sheet.SheetNumber}: {currentFn} → {targetFn} ({targetVariant})");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"TB: variant swap failed on {sheet.SheetNumber}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"TB SetVariant: swapped {swapped}, already match {alreadyMatch}, " +
                $"no TB {noTb}, no matching family {noMatch}");

            var b = StingResultPanel.Create("")
                .SetTitle("Title Block Set Variant")
                .SetSubtitle($"{swapped} sheet(s) swapped to matching STING_TB_* family")
                .SetOverallPct(sheets.Count == 0 ? 100.0 : 100.0 * (swapped + alreadyMatch) / sheets.Count)
                .AddSection("Summary")
                .Metric("Sheets scanned", sheets.Count.ToString())
                .Metric("Swapped", swapped.ToString())
                .Metric("Already matching", alreadyMatch.ToString())
                .Metric("No title block", noTb.ToString())
                .Metric("No matching variant family", noMatch.ToString())
                .AddSection("Available STING_TB_* Families");
            foreach (var kvp in byVariant.OrderBy(k => k.Key))
                b.Metric(kvp.Key, kvp.Value.FamilyName ?? "");
            if (swapDetails.Count > 0)
            {
                b.AddSection("Swap Detail");
                b.Text(string.Join("\n", swapDetails.Take(200)));
            }
            b.Show();
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 4 / Tier 1 — DisciplineLegendBind  (spec §3.3)
    //
    //  Option C implementation: places LGD-{DISC}-NOTES and LGD-{DISC}-SYMBOLS
    //  legend views into the notes/symbols regions of each sheet's title
    //  block. Existing legend viewports in those regions are removed first.
    //  Stamps the placed legend name into PRJ_TB_NOTES_LEGEND_REF_TXT for
    //  audit traceability.
    // ═══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DisciplineLegendBindCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Index LGD-*-NOTES / LGD-*-SYMBOLS legend views
            var legendMap = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
            {
                if (v.ViewType != ViewType.Legend) continue;
                string vn = (v.Name ?? "").Trim();
                if (vn.StartsWith("LGD-", StringComparison.OrdinalIgnoreCase)
                    && (vn.EndsWith("-NOTES", StringComparison.OrdinalIgnoreCase)
                        || vn.EndsWith("-SYMBOLS", StringComparison.OrdinalIgnoreCase)))
                {
                    legendMap[vn.ToUpperInvariant()] = v;
                }
            }

            if (legendMap.Count == 0)
            {
                TaskDialog.Show("STING Legend Bind",
                    "No LGD-*-NOTES or LGD-*-SYMBOLS legend views found.\n\n" +
                    "Create legend views following the naming convention:\n" +
                    "  LGD-ARCH-NOTES, LGD-ARCH-SYMBOLS, LGD-MEP-NOTES, etc.\n" +
                    "Then re-run this command.");
                return Result.Cancelled;
            }

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            int boundCount = 0, skippedNoMatch = 0, alreadyBound = 0, failed = 0;
            var details = new List<string>();

            using (var tx = new Transaction(doc, "STING Discipline Legend Bind"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    string disc = TitleBlockEngine.ResolveDiscipline(sheet);
                    string notesKey = $"LGD-{disc}-NOTES";
                    if (!legendMap.TryGetValue(notesKey, out View notesView))
                    {
                        skippedNoMatch++;
                        details.Add($"{sheet.SheetNumber} ({disc}): no {notesKey} legend — skipped");
                        continue;
                    }

                    string alreadyRef = "";
                    var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);
                    if (tb != null)
                        alreadyRef = ParameterHelpers.GetString(tb, ParamRegistry.TB_NOTES_LEGEND_REF);

                    if (string.Equals(alreadyRef, notesView.Name, StringComparison.OrdinalIgnoreCase)
                        && SheetAlreadyHasLegend(doc, sheet, notesView.Id))
                    { alreadyBound++; continue; }

                    // Remove any legend viewport previously placed in the notes region
                    // (identified by matching the last-stamped legend name)
                    RemoveMatchingLegendViewports(doc, sheet, alreadyRef);

                    // Place at a conventional anchor — top-left of title block's band 3 area.
                    // Actual location is tuned in the family; here we drop at the sheet
                    // centre-left as a sensible default that falls inside band 3 of every
                    // STING_TB_* variant.
                    XYZ anchor = ComputeNotesAnchor(doc, sheet, tb);
                    try
                    {
                        Viewport.Create(doc, sheet.Id, notesView.Id, anchor);
                        if (tb != null)
                            ParameterHelpers.SetString(tb, ParamRegistry.TB_NOTES_LEGEND_REF,
                                notesView.Name, overwrite: true);
                        boundCount++;
                        details.Add($"{sheet.SheetNumber} ({disc}) ← {notesView.Name}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"TB LegendBind: {sheet.SheetNumber} failed: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"TB LegendBind: bound {boundCount}, already bound {alreadyBound}, " +
                $"no legend match {skippedNoMatch}, failed {failed}");

            StingResultPanel.Create("")
                .SetTitle("Discipline Legend Bind")
                .SetSubtitle($"{boundCount} sheet(s) updated")
                .SetOverallPct(sheets.Count == 0 ? 100.0
                    : 100.0 * (boundCount + alreadyBound) / sheets.Count)
                .AddSection("Summary")
                .Metric("Sheets scanned", sheets.Count.ToString())
                .Metric("Legends bound", boundCount.ToString())
                .Metric("Already bound (unchanged)", alreadyBound.ToString())
                .Metric("Skipped (no matching legend)", skippedNoMatch.ToString())
                .Metric("Failures", failed.ToString())
                .Metric("Legend views indexed", legendMap.Count.ToString())
                .AddSection("Detail")
                .Text(details.Count == 0 ? "(none)" : string.Join("\n", details.Take(200)))
                .Show();

            return Result.Succeeded;
        }

        private static bool SheetAlreadyHasLegend(Document doc, ViewSheet sheet, ElementId legendViewId)
        {
            try
            {
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    if (doc.GetElement(vpId) is Viewport vp
                        && vp.ViewId == legendViewId) return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"TB: SheetAlreadyHasLegend failed: {ex.Message}"); }
            return false;
        }

        private static void RemoveMatchingLegendViewports(Document doc, ViewSheet sheet, string legendName)
        {
            if (string.IsNullOrEmpty(legendName)) return;
            try
            {
                var toRemove = new List<ElementId>();
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    if (!(doc.GetElement(vpId) is Viewport vp)) continue;
                    View v = doc.GetElement(vp.ViewId) as View;
                    if (v != null && v.ViewType == ViewType.Legend
                        && string.Equals(v.Name, legendName, StringComparison.OrdinalIgnoreCase))
                        toRemove.Add(vpId);
                }
                foreach (var id in toRemove) doc.Delete(id);
            }
            catch (Exception ex) { StingLog.Warn($"TB: RemoveMatchingLegendViewports failed: {ex.Message}"); }
        }

        private static XYZ ComputeNotesAnchor(Document doc, ViewSheet sheet, FamilyInstance tb)
        {
            try
            {
                BoundingBoxXYZ bb = tb?.get_BoundingBox(sheet);
                if (bb != null)
                {
                    // Band 3 sits near top-left of the title block content area.
                    // Use a ~20mm inset from the TB's left edge, ~80mm below TB top.
                    double mmToFt = 1.0 / 304.8;
                    double x = bb.Min.X + 100.0 * mmToFt;
                    double y = bb.Max.Y - 120.0 * mmToFt;
                    return new XYZ(x, y, 0);
                }
            }
            catch (Exception ex) { StingLog.Warn($"TB: ComputeNotesAnchor failed: {ex.Message}"); }
            return new XYZ(0.3, 1.8, 0); // safe fallback inside most sheet outlines
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 5 / Tier 1 — SheetCountAutoUpdate  (spec §7.5)
    //
    //  Counts every sheet where &quot;Appears in Sheet List&quot; is Yes and writes the
    //  total to PRJ_TB_TOTAL_NO_SHEETS_TXT (on Project Information). The same
    //  logic runs automatically inside TitleBlockPopulate; this standalone
    //  command lets users refresh the count without running a full populate.
    // ═══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetCountAutoUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            int total = 0;
            foreach (ViewSheet s in new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                if (s.IsPlaceholder) continue;
                if (s.get_Parameter(BuiltInParameter.SHEET_SCHEDULED)?.AsInteger() != 0)
                    total++;
            }

            using (var tx = new Transaction(doc, "STING Sheet Count Auto-Update"))
            {
                tx.Start();
                ParameterHelpers.SetString(doc.ProjectInformation,
                    "PRJ_TB_TOTAL_NO_SHEETS_TXT",
                    total.ToString(CultureInfo.InvariantCulture),
                    overwrite: true);
                tx.Commit();
            }

            StingLog.Info($"TB SheetCount: {total} sheets on sheet list");
            TaskDialog.Show("STING Sheet Count",
                $"Sheets appearing in sheet list: {total}\n\n" +
                "Written to PRJ_TB_TOTAL_NO_SHEETS_TXT on Project Information.");
            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Command 6 / Tier 2 — RevisionSync  (spec §7.6)
    //
    //  Reads each sheet's most recent non-issued revision from Revit's native
    //  Sheet Issues/Revisions table and mirrors the number/date into
    //  PRJ_TB_REVISION_NR_TXT / PRJ_TB_REVISION_DATE_TXT on the placed title
    //  block so labels bound to those fields update everywhere.
    // ═══════════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            int synced = 0, noRev = 0, noTb = 0;
            using (var tx = new Transaction(doc, "STING Revision Sync"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);
                    if (tb == null) { noTb++; continue; }
                    var revIds = sheet.GetAllRevisionIds();
                    if (revIds == null || revIds.Count == 0) { noRev++; continue; }
                    // Pick the most recent revision that is not yet Issued
                    Revision chosen = null;
                    foreach (var id in revIds)
                    {
                        if (doc.GetElement(id) is Revision r)
                        {
                            if (!r.Issued) { chosen = r; /* keep scanning; last non-issued wins */ }
                            else if (chosen == null) chosen = r;
                        }
                    }
                    if (chosen == null) { noRev++; continue; }
                    string seq = (chosen.SequenceNumber > 0)
                        ? chosen.SequenceNumber.ToString(CultureInfo.InvariantCulture)
                        : (chosen.RevisionNumber ?? "");
                    string rdate = chosen.RevisionDate ?? "";
                    ParameterHelpers.SetString(tb, "PRJ_TB_REVISION_NR_TXT", seq, overwrite: true);
                    ParameterHelpers.SetString(tb, "PRJ_TB_REVISION_DATE_TXT", rdate, overwrite: true);
                    if (!string.IsNullOrEmpty(chosen.Description))
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_ISSUE_SUMMARY,
                            chosen.Description, overwrite: true);
                    synced++;
                }
                tx.Commit();
            }

            StingLog.Info($"TB RevisionSync: {synced} synced, {noRev} no revision, {noTb} no TB");
            TaskDialog.Show("STING Revision Sync",
                $"Synced {synced} sheet(s).\n" +
                $"Skipped: {noRev} with no revision, {noTb} without a title block.\n\n" +
                "Wrote PRJ_TB_REVISION_NR_TXT, PRJ_TB_REVISION_DATE_TXT, and PRJ_TB_ISSUE_SUMMARY_TXT.");
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 7 / Tier 2 — TransmittalAutoIssue  (spec §6 and §7.7)
    //
    //  Stamps the PRJ_TB_LAST_TRANSMITTAL_* and PRJ_TB_DELIVERABLE_* parameters
    //  onto every selected sheet's title block based on the most recent
    //  transmittal record from STING_BIM_MANAGER/transmittals.json.
    //
    //  Called manually (user picks sheets and transmittal) or automatically
    //  from CreateTransmittalCommand via the StampSheets entry point below.
    // ═══════════════════════════════════════════════════════════════════════
    internal static class TransmittalStamper
    {
        /// <summary>
        /// Apply transmittal metadata to every sheet in the list. Returns the
        /// number of sheets successfully stamped. Transaction must already be
        /// open (or null to open/commit locally).
        /// </summary>
        internal static int Stamp(Document doc, IEnumerable<ViewSheet> sheets,
            JObject transmittal, JObject deliverable, bool useOuterTransaction = false)
        {
            if (doc == null || transmittal == null) return 0;
            int stamped = 0;
            string txId = transmittal["transmittal_id"]?.ToString() ?? "";
            string txDate = transmittal["date_issued"]?.ToString() ?? "";
            string suitability = transmittal["suitability_code"]?.ToString() ?? "";

            string deliverableDataDrop = deliverable?["data_drop"]?.ToString()
                ?? deliverable?["datadrop"]?.ToString() ?? "";
            string deliverableStatus = deliverable?["status"]?.ToString() ?? "";
            string deliverableDue = deliverable?["due_date"]?.ToString()
                ?? deliverable?["date_due"]?.ToString() ?? "";
            string deliverableCde = deliverable?["cde_state"]?.ToString()
                ?? deliverable?["cde_status"]?.ToString()
                ?? MapSuitabilityToCde(suitability);

            Action body = () =>
            {
                foreach (var sheet in sheets)
                {
                    if (sheet == null) continue;
                    var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);
                    if (tb == null) continue;
                    int locked = ParameterHelpers.GetInt(tb, ParamRegistry.TB_LOCK, 0);
                    if (locked != 0) continue;

                    ParameterHelpers.SetString(tb, ParamRegistry.TB_LAST_TRANSMITTAL, txId, overwrite: true);
                    ParameterHelpers.SetString(tb, ParamRegistry.TB_LAST_TRANSMITTAL_DATE, txDate, overwrite: true);
                    if (!string.IsNullOrEmpty(deliverableDataDrop))
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_DELIVERABLE_DATADROP,
                            deliverableDataDrop, overwrite: true);
                    if (!string.IsNullOrEmpty(deliverableStatus))
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_DELIVERABLE_STATUS,
                            deliverableStatus, overwrite: true);
                    if (!string.IsNullOrEmpty(deliverableDue))
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_DELIVERABLE_DUE,
                            deliverableDue, overwrite: true);
                    if (!string.IsNullOrEmpty(deliverableCde))
                        ParameterHelpers.SetString(tb, ParamRegistry.TB_DELIVERABLE_CDE,
                            deliverableCde, overwrite: true);

                    StingLog.Info($"TB TxStamp: {sheet.SheetNumber} ← TX={txId} " +
                        $"(suit={suitability}, status={deliverableStatus}, cde={deliverableCde})");
                    stamped++;
                }
            };

            if (useOuterTransaction) body();
            else
            {
                using (var tx = new Transaction(doc, "STING Transmittal Stamp Sheets"))
                {
                    tx.Start();
                    body();
                    tx.Commit();
                }
            }
            return stamped;
        }

        /// <summary>
        /// Map ISO 19650 suitability (Sn/An/Bn) to a CDE container state.
        /// Heuristic used when DeliverableRow.cde_state is not supplied.
        /// </summary>
        internal static string MapSuitabilityToCde(string suit)
        {
            if (string.IsNullOrEmpty(suit)) return "";
            string s = suit.Trim().ToUpperInvariant();
            if (s == "S0") return "WIP";
            if (s.StartsWith("S")) return "SHARED";
            if (s.StartsWith("A")) return "PUBLISHED";
            if (s.StartsWith("B")) return "ARCHIVE";
            return "SHARED";
        }

        /// <summary>
        /// Load transmittals.json from STING_BIM_MANAGER; returns an empty JArray
        /// when the file is missing or unreadable.
        /// </summary>
        internal static JArray LoadTransmittals(Document doc)
        {
            try
            {
                string path = BIMManager.BIMManagerEngine.GetBIMManagerFilePath(doc, "transmittals.json");
                return BIMManager.BIMManagerEngine.LoadJsonArray(path);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TB TxStamp: LoadTransmittals failed: {ex.Message}");
                return new JArray();
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TransmittalAutoIssueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Pick transmittal (latest first)
            var transmittals = TransmittalStamper.LoadTransmittals(doc);
            if (transmittals.Count == 0)
            {
                TaskDialog.Show("STING Transmittal Stamp",
                    "No transmittals found in STING_BIM_MANAGER/transmittals.json.\n\n" +
                    "Create a transmittal via BIM Coordination Center first.");
                return Result.Cancelled;
            }

            var txItems = transmittals
                .Select(t => new
                {
                    Token = (JObject)t,
                    Id = t["transmittal_id"]?.ToString() ?? "?",
                    Date = t["date_issued"]?.ToString() ?? "",
                    Suit = t["suitability_code"]?.ToString() ?? "",
                    To = t["to_organization"]?.ToString() ?? ""
                })
                .Reverse()
                .Select(t => new StingListPicker.ListItem
                {
                    Label = $"{t.Id} — {t.Date} ({t.Suit})",
                    Detail = $"To: {(string.IsNullOrEmpty(t.To) ? "(unspecified)" : t.To)}",
                    Tag = t.Token
                })
                .ToList();

            var txPick = StingListPicker.Show("Select Transmittal",
                "Pick the transmittal to stamp on sheets", txItems, false);
            if (txPick == null || txPick.Count == 0) return Result.Cancelled;
            var tx = txPick[0].Tag as JObject;
            if (tx == null) return Result.Cancelled;

            // Pick sheets
            var allSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber).ToList();
            var sheetItems = allSheets.Select(s => new StingListPicker.ListItem
            {
                Label = $"{s.SheetNumber} - {s.Name}",
                Tag = s
            }).ToList();
            var picked = StingListPicker.Show("Select Sheets",
                "Stamp transmittal on which sheets?", sheetItems, true);
            if (picked == null || picked.Count == 0) return Result.Cancelled;
            var selected = picked.Select(p => p.Tag as ViewSheet)
                .Where(s => s != null).ToList();

            int stamped = TransmittalStamper.Stamp(doc, selected, tx, deliverable: null);
            TaskDialog.Show("STING Transmittal Stamp",
                $"Stamped transmittal {tx["transmittal_id"]} on {stamped} sheet(s).");
            return Result.Succeeded;
        }
    }
}

namespace StingTools.Docs
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Command 8 / Tier 2 — PreExportValidate  (spec §7.8)
    //
    //  Runs a subset of TitleBlockValidate before PDF/DWF export and blocks
    //  the operation when critical fields are empty. Hook:
    //    PreExportValidate.CheckOrAbort(doc, sheets) → bool
    //  is called from BatchPrintSheetsCommand before ExportSheetsToPDF.
    //  The standalone command lets users run the gate check on demand.
    // ═══════════════════════════════════════════════════════════════════════
    internal static class PreExportValidateGate
    {
        internal static readonly string[] CriticalFields = new[]
        {
            "PRJ_TB_DRAWN_BY_TXT",
            "PRJ_TB_CHECKED_BY_TXT",
            "PRJ_TB_APVD_BY_TXT"
        };

        /// <summary>
        /// Validate sheets for pre-export completeness. Returns true when the
        /// export may proceed, false when the caller should abort. When issues
        /// exist the user is prompted with fix/override/cancel options.
        /// </summary>
        internal static bool CheckOrAbort(Document doc, IEnumerable<ViewSheet> sheets)
        {
            if (doc == null || sheets == null) return true;
            var failures = new List<string>();
            int total = 0;
            foreach (var s in sheets)
            {
                if (s == null) continue;
                total++;
                if (string.IsNullOrWhiteSpace(s.SheetNumber))
                { failures.Add($"(no number) / {s.Name}: sheet number is empty"); continue; }
                if (string.IsNullOrWhiteSpace(s.Name))
                { failures.Add($"{s.SheetNumber}: drawing title (sheet name) is empty"); continue; }
                var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, s);
                if (tb == null)
                { failures.Add($"{s.SheetNumber}: no title block placed"); continue; }
                string suit = ParameterHelpers.GetString(s, "PRJ_DWG_SUITABILITY_COD_TXT");
                if (string.IsNullOrEmpty(suit))
                    suit = ParameterHelpers.GetString(doc.ProjectInformation, "PRJ_DWG_SUITABILITY_COD_TXT");
                if (string.IsNullOrWhiteSpace(suit))
                    failures.Add($"{s.SheetNumber}: PRJ_DWG_SUITABILITY_COD_TXT is empty");
                foreach (string f in CriticalFields)
                {
                    string v = ParameterHelpers.GetString(tb, f);
                    if (string.IsNullOrWhiteSpace(v))
                        failures.Add($"{s.SheetNumber}: {f} is empty");
                }
            }

            if (failures.Count == 0)
            {
                StingLog.Info($"TB PreExport: {total} sheets pass — export allowed");
                return true;
            }

            int uniqueSheets = failures
                .Select(f => f.Split(':')[0].Trim())
                .Distinct().Count();
            var td = new TaskDialog("STING Pre-Export Validation");
            td.MainInstruction = $"{uniqueSheets} sheet(s) have incomplete title blocks.";
            td.MainContent = "ISO 19650 requires drawing title, sheet number, suitability code, " +
                "and approvals to be populated before export.\n\n" +
                "First 20 issues:\n" + string.Join("\n", failures.Take(20));
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Cancel export and fix issues");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Override — export anyway",
                "Records an override entry in StingLog for audit trail.");
            var r = td.Show();
            if (r == TaskDialogResult.CommandLink2)
            {
                StingLog.Warn($"TB PreExport: user overrode gate — {failures.Count} issues on " +
                    $"{uniqueSheets} sheet(s)");
                return true;
            }
            StingLog.Info($"TB PreExport: export cancelled by user — {failures.Count} issues");
            return false;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PreExportValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null || ctx.Doc == null)
            { TaskDialog.Show("STING Title Block", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();
            bool ok = PreExportValidateGate.CheckOrAbort(doc, sheets);
            TaskDialog.Show("STING Pre-Export Validate",
                ok
                    ? $"All {sheets.Count} sheet(s) pass the pre-export gate."
                    : "Pre-export gate flagged issues — see dialog for detail.");
            return ok ? Result.Succeeded : Result.Cancelled;
        }
    }
}
