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

        /// <summary>
        /// True when <paramref name="familyName"/> carries the strip-direction
        /// variant token for <paramref name="variant"/> (e.g. "A1-B" matches
        /// STING_TB_A1_B_v1.0).
        /// <para>
        /// Phase 244 — this used to be a bare <c>IndexOf("A1_B")</c>, which also
        /// matched <c>STING_TB_A1_BIM_v2.0</c> because "A1_B" is a prefix of
        /// "A1_BIM". Every v2.0 BIM family was therefore silently registered as the
        /// B-strip variant of its size, so Set Variant reported sheets as "already
        /// matching" and swapped nothing. The token must be delimited on both sides
        /// by a non-alphanumeric character (or a string boundary) to count.
        /// </para>
        /// </summary>
        internal static bool FamilyMatchesVariant(string familyName, string variant)
        {
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(variant))
                return false;

            // Accept both "A1_B" and "A1-B" spellings in the family name.
            foreach (string token in new[] { variant.Replace("-", "_"), variant.Replace("_", "-") })
            {
                int from = 0;
                while (from <= familyName.Length - token.Length)
                {
                    int i = familyName.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) break;

                    int before = i - 1;
                    int after = i + token.Length;
                    bool leftOk = before < 0 || !char.IsLetterOrDigit(familyName[before]);
                    bool rightOk = after >= familyName.Length || !char.IsLetterOrDigit(familyName[after]);
                    if (leftOk && rightOk) return true;

                    from = i + 1;
                }
            }
            return false;
        }

        /// <summary>True for the post-May-2026 naming scheme
        /// (STING_TB_&lt;SIZE&gt;_BIM_v2.0 / _NONBIM_v2.0), which has no R/B strip
        /// variant and is therefore outside Set Variant's remit.</summary>
        internal static bool IsBimSchemeFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return false;
            return familyName.IndexOf("_NONBIM", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("_BIM", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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
        /// <summary>"Yes"/"1"/"true"/"y" -> 1, anything else 0. One copy, on the
        /// engine, because both the CSV populate path and the dual-home writer need
        /// it and two copies of a yes/no rule is how they come to disagree.</summary>
        internal static int ParseYesNo(string v)
        {
            if (string.IsNullOrEmpty(v)) return 0;
            string s = v.Trim().ToLowerInvariant();
            return (s == "1" || s == "yes" || s == "true" || s == "y") ? 1 : 0;
        }

        /// <summary>Write a title-block value to EVERY home the name has — the sheet's
        /// parameter and the title block's — because nothing in the API says which one
        /// the family's label is bound to.
        ///
        /// Measured on a live model: 29 of 29 title-block parameter names existed BOTH
        /// on the sheet (as project parameters) and on the title-block family, and the
        /// labels were bound to the SHEET's copy. Every STING command wrote to the
        /// title block's. So Populate reported "8 fields updated", Count Sheets
        /// reported "3 pagination cells written", both were telling the truth, and the
        /// drawing showed "?" in those cells for as long as anyone cared to look.
        ///
        /// Writing one and hoping is what produced that. Writing both cannot produce
        /// it: whichever copy the label reads, it now has the value, and the two can
        /// no longer disagree with each other on the same drawing.
        ///
        /// Returns true if ANY home took the value. `homes` reports how many did, so a
        /// caller can tell "written once" from "written to both" without guessing.</summary>
        /// <summary>Which command writes an audit field Populate refuses to.
        /// Named rather than left to the reader: "Populate skipped it" is only half an
        /// answer, and the other half is what to run instead.</summary>
        internal static string WhoWrites(string paramName)
        {
            if (paramName == ParamRegistry.TB_LAST_TRANSMITTAL
                || paramName == ParamRegistry.TB_LAST_TRANSMITTAL_DATE)
                return "   <- written by 'Stamp TX' when a transmittal is issued";
            if (paramName == ParamRegistry.TB_LAST_SYNC
                || paramName == ParamRegistry.TB_LAST_SYNC_BY)
                return "   <- stamped by Populate itself, after the CSV pass";
            if (paramName == ParamRegistry.TB_NOTES_LEGEND_REF)
                return "   <- written by 'Legend Bind'";
            if (paramName == ParamRegistry.TB_LOCK)
                return "   <- set by hand; cleared with 'Unlock TBs'";
            return "";
        }

        /// <summary>Title-block fields that Revit ALREADY has a built-in sheet
        /// parameter for. Writing the STING name alone is not enough for these.
        ///
        /// A title block showed DRAWN BY "Author", CHECKED BY "Checker" and
        /// APPROVED BY "Approver" -- Revit's stock defaults for SHEET_DRAWN_BY /
        /// SHEET_CHECKED_BY / SHEET_APPROVED_BY. Those labels were bound to the
        /// BUILT-IN parameters, which is the normal thing for a title block to do
        /// and what every stock Autodesk template does. So the CSV was edited,
        /// Populate wrote PRJ_TB_DRAWN_BY_TXT to both of its homes, reported
        /// success -- and the cell kept printing "Author", because nothing had
        /// written the parameter the label actually draws.
        ///
        /// This is the THIRD home, and it is the one a title block authored
        /// outside STING will use. Eleven places in this codebase already READ
        /// these built-ins; nothing wrote them.</summary>
        private static readonly Dictionary<string, BuiltInParameter> BuiltInSheetHomes =
            new Dictionary<string, BuiltInParameter>(StringComparer.OrdinalIgnoreCase)
            {
                { "PRJ_TB_DRAWN_BY_TXT",   BuiltInParameter.SHEET_DRAWN_BY },
                { "PRJ_TB_CHECKED_BY_TXT", BuiltInParameter.SHEET_CHECKED_BY },
                { "PRJ_TB_APVD_BY_TXT",    BuiltInParameter.SHEET_APPROVED_BY },
                // Only names that actually exist. PRJ_TB_DESIGNED_BY_TXT and
                // PRJ_TB_ISSUE_DATE_TXT are declared nowhere, so rows for them would
                // be two entries that can never match anything -- the same
                // list-restating-nothing problem check_title_block_surfaces.py exists
                // to catch. Add them here the day they are added to the CSV.
            };

        internal static bool SetOnSheetAndTitleBlock(
            ViewSheet sheet, Element tb, string paramName, string value, bool isBool, out int homes)
        {
            homes = 0;
            foreach (Element target in new Element[] { sheet, tb })
            {
                if (target == null) continue;
                try
                {
                    bool ok = isBool
                        ? ParameterHelpers.SetInt(target, paramName, ParseYesNo(value), overwrite: true)
                        : ParameterHelpers.SetString(target, paramName, value, overwrite: true);
                    if (ok) homes++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TB: writing '{paramName}' to {target.Id}: {ex.Message}");
                }
            }

            // The built-in home, when this field has one. Written LAST and never
            // instead of the others: a STING-authored title block binds the shared
            // parameter, a stock one binds the built-in, and which of the two is in
            // front of you is not knowable from here. Writing both is the only
            // answer that does not depend on knowing.
            if (!isBool && sheet != null && BuiltInSheetHomes.TryGetValue(paramName, out BuiltInParameter bip))
            {
                try
                {
                    Parameter p = sheet.get_Parameter(bip);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && p.Set(value ?? ""))
                        homes++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TB: writing built-in for '{paramName}' on "
                        + $"'{sheet.SheetNumber}': {ex.Message}");
                }
            }

            return homes > 0;
        }

        /// <summary>True when Revit has a built-in sheet parameter for this field,
        /// so a report can say the label may be bound to it rather than to the
        /// shared parameter.</summary>
        internal static bool HasBuiltInSheetHome(string paramName)
            => paramName != null && BuiltInSheetHomes.ContainsKey(paramName);

        /// <summary>What Revit's own built-in sheet parameter holds for this field,
        /// or null. Read so a report can show it beside the shared parameter's value:
        /// the two disagreeing is the whole explanation for a cell that will not
        /// change no matter what is written to it.</summary>
        internal static string ReadBuiltInSheetHome(ViewSheet sheet, string paramName)
        {
            if (sheet == null || paramName == null) return null;
            if (!BuiltInSheetHomes.TryGetValue(paramName, out BuiltInParameter bip)) return null;
            try { return sheet.get_Parameter(bip)?.AsString(); }
            catch (Exception ex)
            {
                StingLog.Warn($"TB: built-in read '{paramName}' on '{sheet.SheetNumber}': {ex.Message}");
                return null;
            }
        }

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

        /// <summary>Every parameter the CSV has a row for, in file order.
        ///
        /// Populate used to iterate ParamRegistry.AllTitleBlockParams instead -- a
        /// hard-coded array of 22 names -- while the CSV shipped 38 rows. The 16 it
        /// did not name were edited in the CSV editor, saved, and then silently
        /// ignored: PRJ_TB_DRAWN_BY_TXT and PRJ_TB_CHECKED_BY_TXT among them, which
        /// is why "I set them in the CSV and ran Populate" changed nothing and the
        /// only route left was typing onto one sheet at a time.
        ///
        /// A list restating a data file is right until the data file moves. The CSV
        /// is the data; this is read from it.</summary>
        public List<string> ParamNames { get; } = new List<string>();

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
                    if (!csv.DefaultValues.ContainsKey(pname)) csv.ParamNames.Add(pname);
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
            // Per-parameter failure tally. A bare paramFails count cannot
            // distinguish "one odd sheet" from "this parameter is absent from
            // every title-block family in the project" — the second is what
            // silently hid the missing PRJ_TB_SHOW_* / LOCK / LAST_SYNC params.
            var failsByParam = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalSheetListed = 0;
            var updatedSheets = new List<string>();
            var skippedSheets = new List<string>();
            string stampUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture);
            string stampUser = Environment.UserName ?? "unknown";

            var multiTbSheets = new List<string>();
            int bothHomes = 0;
            int derived = 0;
            var csvDrivenNames = new List<string>(csv.ParamNames);
            {
                var seen = new HashSet<string>(csv.ParamNames, StringComparer.OrdinalIgnoreCase);
                foreach (string n in ParamRegistry.AllTitleBlockParams)
                    if (seen.Add(n)) csvDrivenNames.Add(n);
            }

            var auditFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lodSeen = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var replaced = new List<string>();
            // One read, project-wide. The CSV's DefaultValue column carries it;
            // anything unrecognised falls back to CONTAINER rather than producing a
            // silently different cell.
            string cdeFormat = (csv.ValueFor(ParamRegistry.TB_CDE_REF_FORMAT, "") ?? "")
                .Trim().ToUpperInvariant();
            if (cdeFormat != "FULL" && cdeFormat != "SUFFIX") cdeFormat = "CONTAINER";

            var cdeRefNoId = new List<string>();
            var cdeRefNoRev = new List<string>();
            int cdeRefWritten = 0;
            var suitUnknown = new List<string>();
            var descMismatch = new List<string>();
            var noValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                    // WHICH title block was written to. "the" title block is whichever
                    // the collector yields first, and that order is not a documented
                    // guarantee -- so on a sheet carrying two, this can write to one
                    // while the drawing displays the other. Every write then reports
                    // success and the cell stays blank. Recording the id makes that
                    // answerable from the log instead of by guesswork.
                    int tbCount = 0;
                    try
                    {
                        tbCount = new FilteredElementCollector(doc, sheet.Id)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .WhereElementIsNotElementType()
                            .GetElementCount();
                    }
                    catch (Exception ex) { StingLog.Warn($"TB Populate: counting title blocks on '{sheet.SheetNumber}': {ex.Message}"); }

                    if (tbCount > 1)
                    {
                        string warn = $"{sheet.SheetNumber}: {tbCount} title blocks on this sheet — "
                            + $"wrote to id {tb.Id}; the drawing may be showing another one";
                        StingLog.Warn("TB Populate: " + warn);
                        multiTbSheets.Add(warn);
                    }
                    else
                    {
                        StingLog.Info($"TB Populate: '{sheet.SheetNumber}' -> title block id {tb.Id}");
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

                    // Every row the CSV has, plus the registry's own names for the
                    // audit fields below (which have no CSV row and must still be
                    // reported rather than passed over in silence). Union, in CSV
                    // order first, so an operator reading the report sees it in the
                    // same order as the editor they just used.
                    foreach (string paramName in csvDrivenNames)
                    {
                        // Never let the CSV overwrite sync/transmittal audit fields.
                        //
                        // These were skipped SILENTLY -- before the value check, so they
                        // appeared in neither the failures nor the "no CSV value" list.
                        // A label bound to one therefore printed "?" forever with nothing
                        // anywhere saying why, which is exactly what happened when CDE REF
                        // was pointed at PRJ_TB_LAST_TRANSMITTAL_TXT. Record them.
                        if (paramName == ParamRegistry.TB_LAST_SYNC
                            || paramName == ParamRegistry.TB_LAST_SYNC_BY
                            || paramName == ParamRegistry.TB_LAST_TRANSMITTAL
                            || paramName == ParamRegistry.TB_LAST_TRANSMITTAL_DATE
                            || paramName == ParamRegistry.TB_NOTES_LEGEND_REF
                            || paramName == ParamRegistry.TB_LOCK)
                        {
                            auditFields.Add(paramName);
                            continue;
                        }

                        string val = csv.ValueFor(paramName, disc);
                        if (string.IsNullOrEmpty(val))
                        {
                            // NOT a failure -- the CSV simply has nothing to say for
                            // this parameter and discipline. But it is not a success
                            // either, and reporting only "8 fields written" left the
                            // operator unable to tell "no value in the CSV" from
                            // "the write failed", which are opposite problems.
                            noValue.Add(paramName);
                            continue;
                        }

                        // The registry's set names the six it knows; the _BOOL suffix
                        // covers a row the CSV grows before anyone adds a constant.
                        // Writing "Yes" into a YESNO parameter as text fails silently,
                        // so guessing wrong here is another blank cell with no message.
                        bool isBool = ParamRegistry.TitleBlockBoolParams.Contains(paramName)
                            || paramName.EndsWith("_BOOL", StringComparison.OrdinalIgnoreCase);

                        // What is there BEFORE the write. Populate uses overwrite:true,
                        // which is right for a bulk fill from the CSV and wrong for a
                        // value someone set deliberately on one sheet — and from inside
                        // this loop the two look identical. It cannot decide, so it
                        // reports: a per-sheet name replaced by a project default is
                        // exactly the kind of loss nobody notices until an issue.
                        string before = isBool ? null : ParameterHelpers.GetString(tb, paramName);
                        if (!isBool
                            && !string.IsNullOrWhiteSpace(before)
                            && !string.Equals(before.Trim(), val.Trim(), StringComparison.Ordinal))
                        {
                            replaced.Add($"{sheet.SheetNumber}  {paramName}: "
                                + $"\"{before.Trim()}\" -> \"{val.Trim()}\"");
                        }

                        bool ok = TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, paramName, val, isBool, out int homes);
                        if (homes == 2) bothHomes++;

                        if (ok) paramsWrittenThisSheet++;
                        else
                        {
                            paramFails++;
                            failsByParam.TryGetValue(paramName, out int n);
                            failsByParam[paramName] = n + 1;
                        }
                    }

                    // Stamp audit fields on every successfully-populated sheet
                    // What this sheet ends up showing for the two fields a drawing set
                    // is most often inconsistent about. Collected AFTER the CSV pass so
                    // it reflects the value that will actually print.
                    string lodNow = ParameterHelpers.GetString(tb, ParamRegistry.DWG_LOIN_LOD);
                    if (string.IsNullOrWhiteSpace(lodNow))
                        lodNow = ParameterHelpers.GetString(sheet, ParamRegistry.DWG_LOIN_LOD);
                    string lodKey = string.IsNullOrWhiteSpace(lodNow) ? "(empty)" : lodNow.Trim();
                    if (!lodSeen.TryGetValue(lodKey, out var lodSheets))
                        lodSeen[lodKey] = lodSheets = new List<string>();
                    lodSheets.Add(sheet.SheetNumber);

                    // ── Derive what follows from the suitability code ────────────
                    //
                    // A title block printed STATUS "S2 / WIP", SUITABILITY
                    // "S4 - FOR APROVAL" and CDE REF "WIP" at the same time: four
                    // cells, two facts, and a contradiction, because each was typed
                    // independently. ISO 19650 has ONE input here -- the suitability
                    // code -- and the description and the CDE container FOLLOW from
                    // it. Derived, they cannot disagree.
                    //
                    // The code is read from either cell and may arrive as
                    // "S4 - FOR APROVAL", so it is extracted rather than compared.
                    string suitRaw = ParameterHelpers.GetString(sheet, "PRJ_DWG_SUITABILITY_COD_TXT");
                    if (string.IsNullOrWhiteSpace(suitRaw))
                        suitRaw = ParameterHelpers.GetString(tb, "PRJ_DWG_SUITABILITY_COD_TXT");
                    if (string.IsNullOrWhiteSpace(suitRaw))
                        suitRaw = ParameterHelpers.GetString(tb, ParamRegistry.PRJ_STATUS_COD);

                    string suitCode = StingTools.Core.Drawing.Iso19650Suitability.ExtractCode(suitRaw);
                    if (!string.IsNullOrEmpty(suitCode))
                    {
                        // The code itself, normalised, in both of its homes -- so a
                        // label bound to either cannot show a different code.
                        TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, "PRJ_DWG_SUITABILITY_COD_TXT", suitCode, false, out _);
                        TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, ParamRegistry.PRJ_STATUS_COD, suitCode, false, out _);

                        // The CDE container. This is what the STATUS cell should show:
                        // SHARED / PUBLISHED / WIP -- information the suitability cell
                        // does NOT already carry.
                        string state = StingTools.Core.Drawing.Iso19650Suitability.CdeStateFor(suitCode);
                        if (state != null)
                        {
                            TitleBlockEngine.SetOnSheetAndTitleBlock(
                                sheet, tb, ParamRegistry.TB_DELIVERABLE_CDE, state, false, out _);
                            derived++;
                        }
                        else
                        {
                            // An unrecognised code must not file the drawing anywhere.
                            // PUBLISHED is a contractual statement, not a default.
                            suitUnknown.Add($"{sheet.SheetNumber}: suitability '{suitRaw}' is not an "
                                + "ISO 19650 code, so the CDE state was left alone");
                        }

                        // The description, in the standard's wording -- but only when
                        // the cell is EMPTY. A project that has written its own wording
                        // keeps it; overwriting would be presumptuous. A mismatch is
                        // reported instead so "FOR APROVAL" is visible as a typo rather
                        // than silently corrected or silently kept.
                        string isoDesc = StingTools.Core.Drawing.Iso19650Suitability.DescriptionFor(suitCode);
                        string curDesc = ParameterHelpers.GetString(tb, "PRJ_DWG_SUITABILITY_DESC_TXT");
                        if (string.IsNullOrWhiteSpace(curDesc))
                        {
                            if (isoDesc != null)
                                TitleBlockEngine.SetOnSheetAndTitleBlock(
                                    sheet, tb, "PRJ_DWG_SUITABILITY_DESC_TXT", isoDesc, false, out _);
                        }
                        else if (isoDesc != null
                                 && !string.Equals(curDesc.Trim(), isoDesc, StringComparison.OrdinalIgnoreCase))
                        {
                            descMismatch.Add($"{sheet.SheetNumber}: {suitCode} reads \"{curDesc.Trim()}\" "
                                + $"but ISO 19650 says \"{isoDesc}\"");
                        }
                    }

                    // ── The CDE REFERENCE cell ───────────────────────────────────
                    //
                    // The one string that lets someone holding the paper find the file
                    // in the CDE: document identifier + suitability + revision, which is
                    // exactly the tail of the published filename. Derived here rather
                    // than read from the CSV because it is per-sheet by definition.
                    //
                    // The identifier is SHT_TAG_1_TXT (assembled by Tag Sheets), or the
                    // sheet number itself once that number IS the identifier. With
                    // neither, nothing is written -- a partial reference points at a
                    // file that does not exist, which is worse than a blank cell -- and
                    // the sheet is named so the fix is Tag Sheets, not a mystery.
                    string cdeDocId = ParameterHelpers.GetString(sheet, ParamRegistry.SHT_TAG_1);
                    if (string.IsNullOrWhiteSpace(cdeDocId)
                        && StingTools.Core.Drawing.Iso19650DocumentCode.LooksAssembled(sheet.SheetNumber))
                        cdeDocId = sheet.SheetNumber;

                    if (string.IsNullOrWhiteSpace(cdeDocId))
                    {
                        cdeRefNoId.Add(sheet.SheetNumber);
                    }
                    else
                    {
                        var refParts = new List<string>();
                        if (cdeFormat == "FULL") refParts.Add(cdeDocId.Trim());
                        if (!string.IsNullOrEmpty(suitCode) && cdeFormat != "CONTAINER")
                            refParts.Add(suitCode);

                        // Revision, from whichever home holds one.
                        //
                        // SHEET_CURRENT_REVISION is the honest first choice -- it is
                        // what the revision box prints, so a CDE reference built from
                        // it cannot contradict the drawing. But it is EMPTY until a
                        // Revit revision is created and assigned to the sheet, and a
                        // set issued at P01 without Revit revisions in play is normal.
                        // The reference then read "...-0002-S4" with the revision
                        // silently missing -- and a CDE reference without a revision
                        // does not identify an issue, which is the one thing it is for.
                        string sheetRev = null;
                        try
                        {
                            sheetRev = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION)?.AsString();
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"TB Populate: revision read on '{sheet.SheetNumber}': {ex.Message}");
                        }
                        if (string.IsNullOrWhiteSpace(sheetRev))
                            sheetRev = ParameterHelpers.GetString(tb, "PRJ_TB_REVISION_NR_TXT");
                        if (string.IsNullOrWhiteSpace(sheetRev))
                            sheetRev = ParameterHelpers.GetString(sheet, "PRJ_TB_REVISION_NR_TXT");
                        if (string.IsNullOrWhiteSpace(sheetRev))
                        {
                            if (cdeFormat != "CONTAINER") cdeRefNoRev.Add(sheet.SheetNumber);
                        }
                        else if (cdeFormat != "CONTAINER")
                        {
                            refParts.Add(sheetRev.Trim());
                        }

                        // CONTAINER: the CDE folder this deliverable lives in, which is
                        // the one thing the sheet does not already print. State comes
                        // from the suitability code, so it cannot contradict the STATUS
                        // cell; discipline and type come from the identifier's own
                        // segments, so it cannot contradict DRG NO. either.
                        string cdeRefValue;
                        if (cdeFormat == "CONTAINER")
                        {
                            string state = StingTools.Core.Drawing.Iso19650Suitability
                                .CdeStateFor(suitCode) ?? "WIP";
                            var segs = cdeDocId.Trim().Split('-');
                            // Project-Originator-Volume-Level-Type-Role-Number:
                            // Type is index 4, Role index 5.
                            string type = segs.Length > 4 ? segs[4] : null;
                            string role = segs.Length > 5 ? segs[5] : null;
                            var path = new List<string> { state };
                            if (!string.IsNullOrWhiteSpace(role)) path.Add(role);
                            if (!string.IsNullOrWhiteSpace(type)) path.Add(type);
                            cdeRefValue = string.Join("/", path);
                        }
                        else
                        {
                            cdeRefValue = string.Join("-", refParts);
                        }

                        if (TitleBlockEngine.SetOnSheetAndTitleBlock(
                                sheet, tb, ParamRegistry.TB_CDE_REF,
                                cdeRefValue, false, out _))
                        {
                            cdeRefWritten++;
                            paramsWrittenThisSheet++;
                        }
                    }

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
                .Metric("Parameters with no CSV value (skipped)", noValue.Count.ToString())
                .Metric("Writes that reached BOTH sheet and title block", bothHomes.ToString())
                .Metric("CDE state derived from the suitability code", derived.ToString())
                .Metric("CDE reference written", cdeRefWritten.ToString())
                .Metric("Per-sheet values replaced by the CSV", replaced.Count.ToString())
                .Metric("Sheets with MORE THAN ONE title block", multiTbSheets.Count.ToString())
                .Metric("Sheets on sheet list (auto-counted)", totalSheetListed.ToString())
                .Metric("CSV path", csvPath ?? "<default>")
                .AddSection("Updated Sheets")
                .Text(updatedSheets.Count == 0 ? "(none)" : string.Join("\n", updatedSheets))
                .AddSection("Skipped Sheets")
                .Text(skippedSheets.Count == 0 ? "(none)" : string.Join("\n", skippedSheets))
                // A sheet with two title blocks explains the whole "it says it wrote and
                // the cell is blank" class of report, so it goes ABOVE the failures.
                // A drawing set showing 200 on some sheets and 350 on others is a
                // real inconsistency, and the only way anyone found it before was by
                // flipping through the set. One value = fine; more than one = say so.
                .AddSection("Per-Sheet Values Replaced By The CSV")
                .Text(replaced.Count == 0
                    ? "(none — nothing that differed from the CSV was overwritten)"
                    : string.Join("\n", replaced.Take(25).Select(r => "  " + r))
                      + (replaced.Count > 25 ? $"\n  … and {replaced.Count - 25} more" : "")
                      + "\n\nThese sheets held a DIFFERENT value and now hold the CSV's. That is "
                      + "what Populate is for when the CSV is the source of truth — but a name "
                      + "typed onto one sheet on purpose is gone. Revit's Undo reverses the whole "
                      + "run. To keep a per-sheet value, leave that parameter's CSV cell EMPTY: "
                      + "Populate skips empty cells and will not touch the sheet.")
                .AddSection("CDE Reference")
                .Text($"Format: {cdeFormat}   (PRJ_TB_CDE_REF_FORMAT_TXT in TITLE_BLOCK.csv)\n\n"
                    + "  CONTAINER  SHARED/Z/DR   — where in the CDE the file sits. The default, "
                    + "because it is the one fact the sheet does not already print.\n"
                    + "  SUFFIX     S4-P01        — suitability and revision only.\n"
                    + "  FULL       SAH-PLNS-ZZ-01-DR-Z-0002-S4-P01 — the whole published "
                    + "filename, searchable in the CDE, and a near-repeat of the DRG NO. cell.\n\n"
                    + $"Written on {cdeRefWritten} sheet(s). Bind the CDE REF label to "
                    + "PRJ_TB_CDE_REF_TXT.\n\n"
                    + (cdeRefNoRev.Count == 0
                        ? ""
                        : $"{cdeRefNoRev.Count} sheet(s) have NO revision, in either Revit's "
                          + "revision schedule or PRJ_TB_REVISION_NR_TXT, so their reference ends "
                          + "at the suitability code. Set PRJ_TB_REVISION_NR_TXT in TITLE_BLOCK.csv "
                          + "(\"P01\") or assign a Revit revision:\n"
                          + string.Join(", ", cdeRefNoRev.Take(20))
                          + (cdeRefNoRev.Count > 20 ? $", \u2026 and {cdeRefNoRev.Count - 20} more" : "")
                          + "\n\n")
                    + (cdeRefNoId.Count == 0
                        ? "Every sheet scanned carried an ISO 19650 identifier."
                        : $"{cdeRefNoId.Count} sheet(s) carry NO ISO 19650 identifier, so nothing "
                          + "was written for them — a reference missing its document id points at "
                          + "no file at all. Run Tag Sheets on:\n"
                          + string.Join(", ", cdeRefNoId.Take(20))
                          + (cdeRefNoId.Count > 20 ? $", \u2026 and {cdeRefNoId.Count - 20} more" : ""))
                    + "\n\nIf the cell still prints \"?\" after this, the family does not yet "
                    + "have the parameter: load STING_TITLE_BLOCK_PARAMETERS.txt (Manage > Shared "
                    + "Parameters), add PRJ_TB_CDE_REF_TXT to the title-block family as an "
                    + "INSTANCE parameter, and point the label at it.")
                .AddSection("LOD / LOIN Across This Set")
                .Text(lodSeen.Count <= 1
                    ? (lodSeen.Count == 0
                        ? "(no sheets scanned)"
                        : "All sheets show " + lodSeen.Keys.First() + ".")
                    : "THESE SHEETS DISAGREE:\n"
                      + string.Join("\n", lodSeen.OrderByDescending(kv => kv.Value.Count)
                          .Select(kv => $"  {kv.Key,-10} {kv.Value.Count} sheet(s): "
                                        + string.Join(", ", kv.Value.Take(8))
                                        + (kv.Value.Count > 8 ? ", …" : "")))
                      + "\n\nNothing wrote this field until now — it was in neither "
                      + "TITLE_BLOCK.csv nor the list Populate iterates, so each sheet kept "
                      + "whatever was typed into it or inherited from its family. Set "
                      + "PRJ_DWG_LOIN_LOD_TXT per discipline in TITLE_BLOCK.csv (Edit CSV...) "
                      + "and re-run Populate to make the set agree.")
                .AddSection("Written By Another Command, Not Populate")
                .Text(auditFields.Count == 0
                    ? "(none)"
                    : string.Join("\n", auditFields.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Select(n => "  " + n + TitleBlockEngine.WhoWrites(n)))
                      + "\n\nPopulate deliberately does not touch these -- they are audit "
                      + "fields, and letting a CSV default overwrite them would erase a record "
                      + "of what actually happened. If a title-block label is bound to one and "
                      + "prints \"?\", it is waiting on that command, not on Populate.")
                .AddSection("Suitability")
                .Text(suitUnknown.Count == 0 && descMismatch.Count == 0
                    ? "Every suitability code is an ISO 19650 code, and every description "
                      + "matches it.\n\nSUITABILITY shows the code and its meaning; STATUS shows "
                      + "the CDE container that code puts the drawing in (WIP / SHARED / "
                      + "PUBLISHED). They are derived from one value, so they cannot disagree."
                    : string.Join("\n", suitUnknown.Concat(descMismatch))
                      + "\n\nAn unrecognised code leaves the CDE state alone rather than filing "
                      + "the drawing somewhere plausible. A description that differs from the "
                      + "standard's wording is KEPT — this only reports it.")
                .AddSection("Sheets With More Than One Title Block")
                .Text(multiTbSheets.Count == 0
                    ? "(none — every sheet has exactly one)"
                    : string.Join("\n", multiTbSheets)
                      + "\n\nEvery command writes to whichever title block the collector yields"
                      + "\nfirst, and that order is not guaranteed. A value written to one will"
                      + "\nnot appear on a drawing that displays the other. Delete the spare and"
                      + "\nre-run Populate.")
                .AddSection("Parameters With No CSV Value (not a failure)")
                .Text(noValue.Count == 0
                    ? "(none — every title-block parameter had a value to write)"
                    : string.Join("\n", noValue.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Select(n => "  " + n))
                      + "\n\nThese were SKIPPED, not failed: TITLE_BLOCK.csv has no value for them"
                      + "\nunder this sheet's discipline and no DefaultValue. Fill the cell in"
                      + "\nTITLE_BLOCK.csv (Edit CSV...) if the sheet should show one.")
                .AddSection("Parameter Write Failures")
                .Text(failsByParam.Count == 0
                    ? "(none)"
                    : string.Join("\n", failsByParam
                        .OrderByDescending(kv => kv.Value)
                        .Select(kv => $"  {kv.Key}: {kv.Value} sheet(s)"
                            + (kv.Value == written
                               ? "  — failed on every updated sheet; the parameter is "
                                 + "probably absent from the title-block family"
                               : ""))))
                .Show();

            return Result.Succeeded;
        }

        internal static string ResolveCsvPath(Document doc, string name)
        {
            // 1. STING_BIM_MANAGER dir alongside project
            try
            {
                string projDir = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName);
                if (!string.IsNullOrEmpty(projDir))
                {
                    string p = Path.Combine(ProjectFolderEngine.GetMetaPath(doc, "STING_BIM_MANAGER"), name);
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
            var bimSchemeFamilies = new List<string>();
            foreach (var sym in loadedTbs)
            {
                string fn = sym.FamilyName ?? "";

                // v2.0 BIM/NONBIM families carry no R/B strip variant — record them
                // for the diagnostic below rather than mis-binding them to a strip.
                if (TitleBlockEngine.IsBimSchemeFamily(fn))
                {
                    if (!bimSchemeFamilies.Contains(fn)) bimSchemeFamilies.Add(fn);
                    continue;
                }

                foreach (string v in TitleBlockEngine.AllVariants)
                {
                    // Match STING_TB_A1_R_v1.0 / A1-R / A1_R in family name.
                    // Delimiter-aware — see TitleBlockEngine.FamilyMatchesVariant.
                    if (TitleBlockEngine.FamilyMatchesVariant(fn, v))
                    {
                        if (!byVariant.ContainsKey(v)) byVariant[v] = sym;
                        break;
                    }
                }
            }

            if (byVariant.Count == 0)
            {
                if (bimSchemeFamilies.Count > 0)
                {
                    // The common case now: the project is on the v2.0 scheme, where
                    // the variant axis is BIM/NONBIM, not R/B strip. Set Variant has
                    // nothing to do here and saying "no families loaded" would be a
                    // lie — name the families and point at the right command.
                    TaskDialog.Show("STING Set Variant",
                        "This project uses the v2.0 title-block naming scheme " +
                        "(BIM / NONBIM), which has no R/B strip variant — so " +
                        "Set Variant has nothing to swap.\n\n" +
                        "Loaded:\n  " + string.Join("\n  ", bimSchemeFamilies) + "\n\n" +
                        "Use instead:\n" +
                        "  \u2022 Swap TBs \u2014 pick any loaded title block and apply it to " +
                        "the active sheet, selected sheets, or every sheet.\n" +
                        "  \u2022 BIM Mode \u2014 toggle the active sheet between its BIM and " +
                        "NONBIM variant.\n" +
                        "  \u2022 Migrate Legacy \u2014 move v1.0 sheets onto their v2.0 BIM " +
                        "variant in bulk.\n\n" +
                        "Set Variant only applies to the six v1.0 strip families " +
                        "(A0-R/A0-B/A1-R/A1-B/A3-R/A3-B).");
                    return Result.Cancelled;
                }

                TaskDialog.Show("STING Set Variant",
                    "No STING_TB_* strip-variant title block families loaded in this project.\n\n" +
                    "Load the six STING_TB_*_v1.0.rfa families (A0-R/A0-B/A1-R/A1-B/A3-R/A3-B) " +
                    "then re-run this command \u2014 or use 'Swap TBs' to apply any loaded " +
                    "title block to your sheets.");
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
                        // Activate must be followed by a regeneration before the
                        // symbol can be assigned (Phase 244 — the sibling swaps in
                        // TitleBlockMigrationCommands / TitleBlockSlotCommands
                        // already did this; this one did not).
                        if (!targetSym.IsActive) { targetSym.Activate(); doc.Regenerate(); }
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
    //
    //  Also stamps PRJ_SHEET_OF_TOTAL_TXT on each counted sheet's placed
    //  title block with the compact "NN / MM" pagination string (this sheet's
    //  ordinal position in SheetNumber order, over the total) — the short
    //  cell used where the full 7-segment PRJ_SHEET_FULL_REF_TXT ISO 19650
    //  sheet ID has no room (e.g. the fabrication assembly title blocks' BOM
    //  strip). Locked title blocks (PRJ_TB_LOCK_BOOL) are skipped, matching
    //  TitleBlockPopulate's lock gate.
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

            var counted = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder
                    && s.get_Parameter(BuiltInParameter.SHEET_SCHEDULED)?.AsInteger() != 0)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int total = counted.Count;
            int width = Math.Max(2, total.ToString(CultureInfo.InvariantCulture).Length);

            int paginationWritten = 0, lockedSkipped = 0, noTbSkipped = 0;
            using (var tx = new Transaction(doc, "STING Sheet Count Auto-Update"))
            {
                tx.Start();
                ParameterHelpers.SetString(doc.ProjectInformation,
                    "PRJ_TB_TOTAL_NO_SHEETS_TXT",
                    total.ToString(CultureInfo.InvariantCulture),
                    overwrite: true);

                for (int i = 0; i < counted.Count; i++)
                {
                    var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, counted[i]);
                    if (tb == null) { noTbSkipped++; continue; }
                    if (ParameterHelpers.GetInt(tb, ParamRegistry.TB_LOCK, 0) != 0)
                    { lockedSkipped++; continue; }

                    string seq = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
                    string tot = total.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
                    // Both homes — the label is bound to one of them and the API does
                    // not say which. Writing only the title block is what made this
                    // report "3 pagination cells written" onto a drawing showing "?".
                    if (TitleBlockEngine.SetOnSheetAndTitleBlock(
                            counted[i], tb, "PRJ_SHEET_OF_TOTAL_TXT",
                            $"{seq} / {tot}", isBool: false, out _))
                        paginationWritten++;
                }

                tx.Commit();
            }

            StingLog.Info($"TB SheetCount: {total} sheets on sheet list, "
                + $"{paginationWritten} pagination cell(s) written, {lockedSkipped} locked, {noTbSkipped} no TB");
            TaskDialog.Show("STING Sheet Count",
                $"Sheets appearing in sheet list: {total}\n" +
                $"Pagination cells written (PRJ_SHEET_OF_TOTAL_TXT): {paginationWritten}\n" +
                $"Skipped — locked: {lockedSkipped}, no title block: {noTbSkipped}\n\n" +
                "Total written to PRJ_TB_TOTAL_NO_SHEETS_TXT on Project Information.");
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

            // Phase 195: this command is now a thin front-end over the single
            // revision-sync engine, StingTools.Core.Drawing.TitleBlockRevisionSyncer.
            // The tag "RevisionSync" and this class are retained for back-compat
            // so both dock buttons ("Rev Sync" and "Sync Rev") do the same
            // correct thing.
            var result = StingTools.Core.Drawing.TitleBlockRevisionSyncer.SyncAll(doc);

            string warn = result.Warnings.Count > 0
                ? $"\n\nWarnings ({result.Warnings.Count}):\n  " +
                  string.Join("\n  ", result.Warnings.Take(10))
                : "";

            TaskDialog.Show("STING Revision Sync",
                $"Synced {result.SheetsProcessed} sheet(s), {result.ParamsWritten} parameter(s) written.\n" +
                $"Skipped: {result.SheetsSkipped}.\n\n" +
                "Wrote SHT_REV_TXT / SHT_REV_DATE_TXT on sheets and\n" +
                "PRJ_TB_REVISION_NR_TXT / _DATE_TXT / _DESCRIPTION_TXT\n" +
                "on title blocks." + warn);
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

                    // BOTH homes. Nearly every title-block parameter name exists twice
                    // -- once on the sheet as a project parameter, once on the family --
                    // and the label binds to one of them without saying which. Writing
                    // only the title block is why CDE REF printed "?" on a sheet whose
                    // title block held the value: Populate and Count Sheets were fixed
                    // for this and the transmittal stamp was not.
                    TitleBlockEngine.SetOnSheetAndTitleBlock(
                        sheet, tb, ParamRegistry.TB_LAST_TRANSMITTAL, txId, false, out _);
                    TitleBlockEngine.SetOnSheetAndTitleBlock(
                        sheet, tb, ParamRegistry.TB_LAST_TRANSMITTAL_DATE, txDate, false, out _);
                    if (!string.IsNullOrEmpty(deliverableDataDrop))
                        TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, ParamRegistry.TB_DELIVERABLE_DATADROP, deliverableDataDrop, false, out _);
                    if (!string.IsNullOrEmpty(deliverableStatus))
                        TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, ParamRegistry.TB_DELIVERABLE_STATUS, deliverableStatus, false, out _);
                    if (!string.IsNullOrEmpty(deliverableDue))
                        TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, ParamRegistry.TB_DELIVERABLE_DUE, deliverableDue, false, out _);
                    if (!string.IsNullOrEmpty(deliverableCde))
                        TitleBlockEngine.SetOnSheetAndTitleBlock(
                            sheet, tb, ParamRegistry.TB_DELIVERABLE_CDE, deliverableCde, false, out _);

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
