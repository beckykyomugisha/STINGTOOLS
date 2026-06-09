using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterEngine — the export pipeline that powers StingExportCenterDialog.
    //
    //  Responsibilities:
    //    • Persist / load ExportCenterState in project_config.json
    //    • Resolve naming templates against per-sheet token contexts
    //    • Run pre-flight checks
    //    • Dispatch to per-format exporters (PDF, DWG, IFC, NWC, Image, …)
    //    • Implement combined-PDF and DWG-multi-layout pipelines
    //    • Emit ExportRunResult + per-row report rows
    //
    //  This engine is intentionally headless — no WPF or TaskDialog calls — so
    //  it can be invoked from the dialog, a workflow step, or a scheduled job.
    // ════════════════════════════════════════════════════════════════════════════

    public static class ExportCenterEngine
    {
        // ── State persistence (project_config.json: key "ExportCenter") ─────────

        private const string ConfigKey = "ExportCenter";

        public static ExportCenterState LoadState()
        {
            try
            {
                string path = TagConfig.ConfigSource;
                ExportCenterState st = null;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    if (root[ConfigKey] is JObject sub)
                        st = sub.ToObject<ExportCenterState>();
                }
                st ??= new ExportCenterState();

                // Seed built-ins on first run.
                if (st.Profiles.Count == 0)
                    st.Profiles.AddRange(ExportCenterState.BuildBuiltInProfiles());
                else
                    EnsureBuiltInProfilesPresent(st);

                if (st.SavedSets.Count == 0)
                    st.SavedSets.AddRange(ExportCenterState.BuildBuiltInSets());
                else
                    EnsureBuiltInSetsPresent(st);

                return st;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExportCenterEngine.LoadState: {ex.Message}");
                var st = new ExportCenterState();
                st.Profiles.AddRange(ExportCenterState.BuildBuiltInProfiles());
                st.SavedSets.AddRange(ExportCenterState.BuildBuiltInSets());
                return st;
            }
        }

        public static void SaveState(ExportCenterState st)
        {
            try
            {
                string path = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(path)) return;

                JObject root = File.Exists(path)
                    ? JObject.Parse(File.ReadAllText(path))
                    : new JObject();

                root[ConfigKey] = JObject.FromObject(st);
                File.WriteAllText(path, root.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExportCenterEngine.SaveState: {ex.Message}");
            }
        }

        private static void EnsureBuiltInProfilesPresent(ExportCenterState st)
        {
            var existing = new HashSet<string>(st.Profiles.Where(p => p.BuiltIn).Select(p => p.Name));
            foreach (var bi in ExportCenterState.BuildBuiltInProfiles())
                if (!existing.Contains(bi.Name)) st.Profiles.Add(bi);
        }

        private static void EnsureBuiltInSetsPresent(ExportCenterState st)
        {
            var existing = new HashSet<string>(st.SavedSets.Where(s => s.BuiltIn).Select(s => s.Name));
            foreach (var bi in ExportCenterState.BuildBuiltInSets())
                if (!existing.Contains(bi.Name)) st.SavedSets.Add(bi);
        }

        // ── Saved-set resolution ────────────────────────────────────────────────

        /// <summary>
        /// Materialise a saved set into a list of live ElementIds, following the
        /// "built-in semantic sets" rules (e.g. "All Sheets" returns every ViewSheet).
        /// Missing element ids are silently dropped; the count is returned via
        /// <paramref name="missingCount"/>.
        /// </summary>
        public static List<ElementId> ResolveSet(Document doc, ExportSavedSet set, out int missingCount)
        {
            missingCount = 0;
            if (set == null) return new List<ElementId>();

            // Built-in semantic sets bypass the persisted id list.
            if (set.BuiltIn)
                return ResolveBuiltInSet(doc, set.Name);

            var ids = new List<ElementId>();
            foreach (string idText in set.ElementIds)
            {
                if (!long.TryParse(idText, out long raw)) { missingCount++; continue; }
                var eid = new ElementId(raw);
                var el = doc.GetElement(eid);
                if (el == null) { missingCount++; continue; }
                ids.Add(eid);
            }
            return ids;
        }

        private static List<ElementId> ResolveBuiltInSet(Document doc, string name)
        {
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().Where(s => !s.IsTemplate).ToList();

            switch (name)
            {
                case "All Sheets":
                    return sheets.Select(s => s.Id).ToList();

                case "All Views":
                    return new FilteredElementCollector(doc).OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && !(v is ViewSheet))
                        .Select(v => v.Id).ToList();

                case "By Discipline: Architectural":  return FilterByDisc(sheets, "A");
                case "By Discipline: Mechanical":     return FilterByDisc(sheets, "M");
                case "By Discipline: Electrical":     return FilterByDisc(sheets, "E");
                case "By Discipline: Plumbing":       return FilterByDisc(sheets, "P");
                case "By Discipline: Structural":     return FilterByDisc(sheets, "S");

                case "Issued Sheets":
                {
                    // A sheet counts as "issued" if it appears in any revision.
                    var ids = new List<ElementId>();
                    foreach (var s in sheets)
                    {
                        var revs = s.GetAllRevisionIds();
                        if (revs != null && revs.Count > 0) ids.Add(s.Id);
                    }
                    return ids;
                }

                case "Revised This Week":
                {
                    var cutoff = DateTime.Today.AddDays(-7);
                    var ids = new List<ElementId>();
                    foreach (var s in sheets)
                    {
                        var revs = s.GetAllRevisionIds();
                        if (revs == null) continue;
                        foreach (var rid in revs)
                        {
                            if (doc.GetElement(rid) is Revision r &&
                                DateTime.TryParse(r.RevisionDate, out var d) && d >= cutoff)
                            { ids.Add(s.Id); break; }
                        }
                    }
                    return ids;
                }

                case "Changed Since Last Export":
                {
                    // Issue-driven delta: a sheet is "changed" when it has never been
                    // exported, or its current revision differs from the last-exported
                    // revision recorded in state. The single biggest automation win for
                    // an ISO 19650 re-issue — only the drawings that moved go out.
                    var state = LoadState();
                    var byUid = state.LastExports
                        .GroupBy(r => r.SheetUniqueId)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ExportedUtc).First());
                    var ids = new List<ElementId>();
                    foreach (var s in sheets)
                    {
                        var (rev, _) = GetCurrentRevision(doc, s);
                        if (!byUid.TryGetValue(s.UniqueId, out var rec) ||
                            !string.Equals(rec.Revision ?? "", rev ?? "", StringComparison.OrdinalIgnoreCase))
                            ids.Add(s.Id);
                    }
                    return ids;
                }

                case "Resume Last Failed":
                {
                    var state = LoadState();
                    var failed = new HashSet<string>(state.LastRunFailedSheetIds ?? new List<string>());
                    return sheets.Where(s => failed.Contains(s.UniqueId)).Select(s => s.Id).ToList();
                }

                case "Currently Opened":
                {
                    // We can only inspect the active UI app from the dialog layer;
                    // engine-side we approximate by returning the active view's sheet (if any).
                    var ids = new List<ElementId>();
                    var active = doc.ActiveView;
                    if (active is ViewSheet vs) ids.Add(vs.Id);
                    return ids;
                }
            }
            return sheets.Select(s => s.Id).ToList();
        }

        private static List<ElementId> FilterByDisc(List<ViewSheet> sheets, string disc)
        {
            return sheets.Where(s => GetDisciplinePrefix(s.SheetNumber).Equals(disc, StringComparison.OrdinalIgnoreCase))
                         .Select(s => s.Id).ToList();
        }

        public static string GetDisciplinePrefix(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber)) return "Other";
            int dash = sheetNumber.IndexOf('-');
            if (dash > 0) return sheetNumber.Substring(0, dash).ToUpperInvariant();
            string letters = new string(sheetNumber.TakeWhile(char.IsLetter).ToArray());
            return string.IsNullOrEmpty(letters) ? "Other" : letters.ToUpperInvariant();
        }

        /// <summary>
        /// Derive an ISO 19650 Level code from a sheet, used as a fallback when
        /// the sheet has no <c>STING_LVL_COD_TXT</c> parameter. Tries (in order):
        ///   1) Sheet's own "Level"/"STING_LVL_COD_TXT" parameter
        ///   2) Common patterns in the sheet name ("Level 01", "Ground Floor",
        ///      "Basement 2", "Roof", "L01", "GF", "B2", "RF")
        ///   3) The level of any plan view placed on the sheet (first one wins)
        /// Returns null if nothing matched — caller substitutes the ISO
        /// "unknown" code "XX".
        /// </summary>
        private static string GetLevelFromSheet(Document doc, ViewSheet sheet)
        {
            if (sheet == null) return null;
            try
            {
                string name = sheet.Name ?? "";
                // Pattern: "Level 01", "L 01", "L01", "Floor 02"
                var m = Regex.Match(name, @"\b(?:Level|Floor|L)\s*0*(\d{1,2})\b", RegexOptions.IgnoreCase);
                if (m.Success) return $"L{int.Parse(m.Groups[1].Value):D2}";
                // Basement
                m = Regex.Match(name, @"\b(?:Basement|B)\s*0*(\d)\b", RegexOptions.IgnoreCase);
                if (m.Success) return $"B{m.Groups[1].Value}";
                // Ground floor / roof
                if (Regex.IsMatch(name, @"\bground\s*floor\b|\bGF\b", RegexOptions.IgnoreCase)) return "GF";
                if (Regex.IsMatch(name, @"\broof\b|\bRF\b", RegexOptions.IgnoreCase)) return "RF";
                if (Regex.IsMatch(name, @"\bmezzanine\b|\bMEZ\b", RegexOptions.IgnoreCase)) return "MZ";

                // Fall through to a placed plan view's level
                foreach (var vpId in sheet.GetAllPlacedViews())
                {
                    if (doc.GetElement(vpId) is View v && v.GenLevel != null)
                    {
                        string lvlName = v.GenLevel.Name ?? "";
                        m = Regex.Match(lvlName, @"\b(?:Level|Floor|L)\s*0*(\d{1,2})\b", RegexOptions.IgnoreCase);
                        if (m.Success) return $"L{int.Parse(m.Groups[1].Value):D2}";
                        if (Regex.IsMatch(lvlName, @"\bground\b|\bGF\b", RegexOptions.IgnoreCase)) return "GF";
                        if (Regex.IsMatch(lvlName, @"\broof\b|\bRF\b", RegexOptions.IgnoreCase)) return "RF";
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetLevelFromSheet: {ex.Message}"); }
            return null;
        }

        // ── Token resolver ──────────────────────────────────────────────────────

        /// <summary>
        /// Resolve a naming template ({SheetNumber}, {Revision}, {Date:yyyyMMdd}, …)
        /// into a concrete filename stem (without extension). Caller is responsible
        /// for sanitising disallowed characters and appending the format extension.
        /// </summary>
        public static string ResolveNaming(Document doc, View view, string template, OutputSettings outSettings)
            => ResolveNaming(doc, view, template, outSettings, null);

        /// <summary>
        /// As <see cref="ResolveNaming(Document, View, string, OutputSettings)"/> but lets the
        /// caller override individual tokens — used by the combined-PDF path so the
        /// ISO 19650 filename carries set-level identity (e.g. the sheet-number slot
        /// holds the set label) instead of the first sheet's specifics.
        /// </summary>
        public static string ResolveNaming(Document doc, View view, string template,
            OutputSettings outSettings, Dictionary<string, string> overrides)
        {
            if (string.IsNullOrEmpty(template)) template = "{SheetNumber} - {SheetTitle}";

            var tokens = BuildTokenContext(doc, view);
            if (overrides != null)
                foreach (var kv in overrides) tokens[kv.Key] = kv.Value;
            return Regex.Replace(template, @"\{(?<key>[A-Za-z0-9_]+)(?::(?<fmt>[^}]+))?\}", m =>
            {
                string key = m.Groups["key"].Value;
                string fmt = m.Groups["fmt"].Value;

                if (key.Equals("Date", StringComparison.OrdinalIgnoreCase))
                    return DateTime.Now.ToString(string.IsNullOrEmpty(fmt) ? "yyyyMMdd" : fmt);
                if (key.Equals("Time", StringComparison.OrdinalIgnoreCase))
                    return DateTime.Now.ToString(string.IsNullOrEmpty(fmt) ? "HHmm" : fmt);

                if (tokens.TryGetValue(key, out string value))
                    return value ?? "";

                return ""; // unknown tokens vanish — keeps filenames tidy
            });
        }

        /// <summary>Build the per-view token map used by ResolveNaming + bookmark templates.</summary>
        public static Dictionary<string, string> BuildTokenContext(Document doc, View view)
        {
            var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pi = doc?.ProjectInformation;

            t["ProjectName"]   = pi?.Name ?? "";
            t["ProjectNumber"] = pi?.Number ?? "";
            t["ProjectCode"]   = ReadProjectInfo(pi, "PRJ_PROJECT_COD_TXT") ?? pi?.Number ?? "";
            t["Project"]       = t["ProjectCode"]; // ISO 19650-2 spec uses bare {Project}
            t["Originator"]    = ReadProjectInfo(pi, "PRJ_ORG_ORIGINATOR_CODE_TXT") ?? "";
            t["OriginatorCode"]= t["Originator"];
            t["CompanyName"]   = ReadProjectInfo(pi, ParamRegistry.ORG_COMPANY_NAME) ?? "";
            t["ClientName"]    = ReadProjectInfo(pi, ParamRegistry.ORG_CLIENT_NAME) ?? pi?.ClientName ?? "";

            if (view is ViewSheet sheet)
            {
                t["SheetNumber"]  = sheet.SheetNumber ?? "";
                t["SheetTitle"]   = sheet.Name ?? "";
                t["DrawingNumber"]= sheet.SheetNumber ?? "";
                t["DrawingTitle"] = sheet.Name ?? "";
                t["DrawingSet"]   = ReadParam(sheet, "Sheet Issue Date") ?? "";
                t["Discipline"]   = GetDisciplinePrefix(sheet.SheetNumber);

                var (rev, revDate) = GetCurrentRevision(doc, sheet);
                t["RevDate"] = revDate ?? "";

                // ── ISO 19650 token resolution chain ──
                // 1) Sheet-level STING_* params (per-sheet overrides)
                // 2) Stamped DrawingType.IsoNaming (Phase 113 — auto-populated
                //    when the sheet was created through the Drawing Type engine)
                // 3) Sensible ISO 19650-2 defaults
                StingTools.Core.Drawing.IsoNaming dtIso = null;
                string stampedDtId = ReadParam(sheet, StingTools.Core.Drawing.DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                if (!string.IsNullOrEmpty(stampedDtId))
                {
                    try
                    {
                        var dt = StingTools.Core.Drawing.DrawingTypeRegistry.Get(doc, stampedDtId);
                        dtIso = dt?.IsoNaming;
                    }
                    catch (Exception ex) { StingLog.Warn($"DrawingType lookup '{stampedDtId}': {ex.Message}"); }
                }

                t["Volume"]      = ReadParam(sheet, "STING_VOLUME_TXT")      ?? dtIso?.Volume      ?? "ZZ";
                t["Level"]       = ReadParam(sheet, "STING_LVL_COD_TXT")     ?? GetLevelFromSheet(doc, sheet) ?? "XX";
                t["Type"]        = ReadParam(sheet, "STING_DOC_TYPE_TXT")    ?? dtIso?.Type        ?? "DR";
                string disc      = t["Discipline"];
                t["Role"]        = ReadParam(sheet, "STING_ROLE_TXT")        ?? dtIso?.Role        ?? (string.IsNullOrEmpty(disc) ? "Z" : disc);
                t["Suitability"] = ReadParam(sheet, "STING_SUITABILITY_TXT") ?? dtIso?.Suitability ?? "S2";
                t["Revision"]    = !string.IsNullOrEmpty(rev) ? rev : (dtIso?.Revision ?? "P01");
                t["Format"]      = ""; // filled in by caller per format
            }
            else if (view != null)
            {
                t["SheetNumber"]   = "";
                t["SheetTitle"]    = view.Name ?? "";
                t["DrawingNumber"] = "";
                t["DrawingTitle"]  = view.Name ?? "";
                t["Discipline"]    = view.ViewType.ToString();
                t["Revision"]      = "";
                t["RevDate"]       = "";
            }

            // C2 — Material-class token for filename / bookmark templates.
            // Resolves to the dominant material class across elements placed
            // on the view (cheap — uses the cached title-block tokens path).
            try
            {
                t["MaterialClass"] = ResolveDominantMaterialClass(doc, view) ?? "";
            }
            catch (Exception ex) { StingLog.WarnRateLimited("ExportTokens.MatClass", $"MaterialClass token: {ex.Message}"); t["MaterialClass"] = ""; }

            return t;
        }

        /// <summary>
        /// C2 — Find the dominant Material Class across elements visible
        /// on the view. Used as a filename / bookmark token so a concrete-
        /// heavy issue can carry the class in its export name.
        /// </summary>
        private static string ResolveDominantMaterialClass(Document doc, View view)
        {
            if (doc == null || view == null) return null;
            try
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().Take(500))
                {
                    try
                    {
                        var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        ElementId mid = null;
                        if (p != null && p.StorageType == StorageType.ElementId) mid = p.AsElementId();
                        if (mid == null || mid.Value <= 0)
                        {
                            var mats = el.GetMaterialIds(false);
                            if (mats != null) foreach (var mm in mats) if (mm != null && mm.Value > 0) { mid = mm; break; }
                        }
                        if (mid == null || mid.Value <= 0) continue;
                        var mat = doc.GetElement(mid) as Material;
                        string cls = mat?.MaterialClass ?? "";
                        if (string.IsNullOrEmpty(cls)) continue;
                        counts[cls] = counts.TryGetValue(cls, out var v) ? v + 1 : 1;
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("ExportTokens.MatClassEl", $"MaterialClass el: {ex.Message}"); }
                }
                if (counts.Count == 0) return null;
                string winner = "";
                int best = 0;
                foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; winner = kv.Key; }
                return winner;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveDominantMaterialClass: {ex.Message}"); return null; }
        }

        private static string ReadProjectInfo(ProjectInfo pi, string paramName)
        {
            if (pi == null) return null;
            try
            {
                var p = pi.LookupParameter(paramName);
                if (p == null || !p.HasValue) return null;
                return p.AsString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static string ReadParam(Element el, string name)
        {
            if (el == null) return null;
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static (string rev, string date) GetCurrentRevision(Document doc, ViewSheet sheet)
        {
            try
            {
                var revIds = sheet.GetAllRevisionIds();
                if (revIds == null || revIds.Count == 0) return (null, null);
                var lastId = revIds[revIds.Count - 1];
                if (doc.GetElement(lastId) is Revision r)
                    return (r.SequenceNumber.ToString(), r.RevisionDate);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return (null, null);
        }

        // ── Filename hygiene ────────────────────────────────────────────────────

        public static string Sanitise(string filename, string replacement)
        {
            if (string.IsNullOrEmpty(filename)) return "untitled";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(filename.Length);
            foreach (char c in filename)
                sb.Append(Array.IndexOf(bad, c) >= 0 ? replacement : c.ToString());
            string clean = sb.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(clean) ? "untitled" : clean;
        }

        // ── Pre-flight ──────────────────────────────────────────────────────────

        public static List<ExportPreflightIssue> PreflightCheck(
            Document doc, ExportProfile profile, List<ElementId> selectedIds)
        {
            var issues = new List<ExportPreflightIssue>();
            if (selectedIds == null || selectedIds.Count == 0)
                issues.Add(Err("NO_SELECTION", "No sheets or views selected."));

            if (profile.Formats == ExportFormats.None)
                issues.Add(Err("NO_FORMAT", "No output format is active."));

            // Folder writability
            try
            {
                if (profile.Output.Destination != ExportDestination.PlanscapeCde)
                {
                    var folder = profile.Output.LocalFolder;
                    if (string.IsNullOrEmpty(folder))
                        issues.Add(Err("NO_OUTPUT_FOLDER", "Output folder is empty."));
                    else if (!Directory.Exists(folder))
                    {
                        if (profile.Output.CreateFolderIfMissing)
                            issues.Add(Warn("FOLDER_CREATE",
                                $"Output folder doesn't exist and will be created: {folder}"));
                        else
                            issues.Add(Err("FOLDER_MISSING", $"Output folder doesn't exist: {folder}"));
                    }
                }
            }
            catch (Exception ex) { issues.Add(Warn("FOLDER_CHECK", ex.Message)); }

            // Filename collisions across the projected set
            try
            {
                var seen = new Dictionary<string, string>();
                foreach (var eid in selectedIds)
                {
                    if (doc.GetElement(eid) is not View v) continue;
                    string stem = Sanitise(ResolveNaming(doc, v, profile.Output.NamingTemplate, profile.Output),
                                           profile.Output.IllegalCharReplacement);
                    if (seen.TryGetValue(stem, out string other) && other != v.Id.ToString())
                        issues.Add(Warn("DUP_NAME",
                            $"Two items would produce the same filename: '{stem}' (sheets {other} and {v.Id})"));
                    else
                        seen[stem] = v.Id.ToString();
                }
            }
            catch (Exception ex) { issues.Add(Warn("NAME_CHECK", ex.Message)); }

            // Compliance gate (BIM mode only)
            if (profile.Mode == ExportCenterMode.BIM)
            {
                try
                {
                    var c = ComplianceScan.Scan(doc);
                    if (c != null && c.CompliancePercent < 50)
                        issues.Add(Warn("LOW_COMPLIANCE",
                            $"Project tag compliance is {c.CompliancePercent}% — exports may show incomplete tag data."));
                }
                catch { /* non-fatal */ }
            }

            // DWG multi-layout availability — Method A (AutoCAD COM) or Method B (ODA).
            if ((profile.Formats & ExportFormats.DWG) != 0 &&
                profile.Dwg.OutputMode == DwgOutputMode.AllInOneMultiLayout)
            {
                if (ExportCenterDwgMerger.IsAvailable())
                {
                    /* Method A available — true merge */
                }
                else if (ExportCenterOdaConverter.IsAvailable())
                {
                    issues.Add(Warn("DWG_MERGE_METHOD_B",
                        "AutoCAD COM not detected. Will use ODA File Converter (Method B): " +
                        "per-sheet DWGs version-normalised + merge_manifest.json. " +
                        "True layout-tab merging requires AutoCAD or the Teigha SDK."));
                }
                else
                {
                    issues.Add(Warn("DWG_MERGE_UNAVAILABLE",
                        "DWG multi-layout merge requires AutoCAD COM or ODA File Converter — neither detected. " +
                        (profile.Dwg.FallbackOnMergeFailure
                            ? "Will fall back to individual files."
                            : "Export will fail.")));
                }
            }

            // NWC availability
            if ((profile.Formats & ExportFormats.NWC) != 0)
            {
                if (!ExportCenterNwcExporter.IsAvailable())
                    issues.Add(Warn("NWC_UNAVAILABLE",
                        "Navisworks NWC Export Utility not detected — NWC export will be skipped."));
            }

            return issues;
        }

        /// <summary>True if AutoCAD COM (Method A) is reachable. ODA (Method B) is TODO.</summary>
        public static bool IsMultiLayoutMergerAvailable() => ExportCenterDwgMerger.IsAvailable();

        private static ExportPreflightIssue Err(string code, string msg) =>
            new() { Level = ExportPreflightIssue.Severity.Error, Code = code, Message = msg };

        private static ExportPreflightIssue Warn(string code, string msg) =>
            new() { Level = ExportPreflightIssue.Severity.Warning, Code = code, Message = msg };

        // ── Run dispatcher ──────────────────────────────────────────────────────

        public delegate void ProgressReporter(int current, int total, string label);

        /// <summary>
        /// Run the export pipeline. Returns an aggregate result. Caller is expected
        /// to be on the Revit API thread (this method calls Document.Export which
        /// is not transactional but must run on the UI thread).
        /// </summary>
        public static ExportRunResult Run(
            Document doc,
            ExportProfile profile,
            List<ElementId> selectedIds,
            ProgressReporter progress = null,
            Func<bool> cancelRequested = null)
        {
            var result = new ExportRunResult { Profile = profile };

            try
            {
                EnsureFolder(profile);

                int total = CountFormatPasses(profile) * (selectedIds?.Count ?? 0);
                int done = 0;

                // PDF
                if ((profile.Formats & ExportFormats.PDF) != 0)
                {
                    RunPdf(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label),
                        cancelRequested);
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(doc, profile, result); }
                }

                // DWG
                if ((profile.Formats & ExportFormats.DWG) != 0)
                {
                    RunDwg(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label),
                        cancelRequested);
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(doc, profile, result); }
                }

                // IFC
                if ((profile.Formats & ExportFormats.IFC) != 0)
                {
                    RunIfc(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label),
                        cancelRequested);
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(doc, profile, result); }
                }

                if ((profile.Formats & ExportFormats.NWC) != 0)
                {
                    RunNwc(doc, profile, result,
                        (label) => progress?.Invoke(++done, total, label));
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(doc, profile, result); }
                }
                if ((profile.Formats & ExportFormats.Image) != 0)
                    RunImage(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label), cancelRequested);
                if ((profile.Formats & ExportFormats.DGN) != 0)
                    RunDgn(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label), cancelRequested);
                if ((profile.Formats & ExportFormats.DWF) != 0)
                    RunDwf(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label), cancelRequested);
                if ((profile.Formats & ExportFormats.XML) != 0)
                    RunXml(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label));
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportCenterEngine.Run failed", ex);
                result.Warnings.Add("Run failed: " + ex.Message);
            }
            return Finalize(doc, profile, result);
        }

        /// <summary>
        /// Common cleanup path — was the body of a finally block guarded
        /// by a "Done:" label, but C# doesn't allow goto-into-finally.
        /// </summary>
        private static ExportRunResult Finalize(Document doc, ExportProfile profile, ExportRunResult result)
        {
            result.FinishedUtc = DateTime.UtcNow;
            try { PostRun(doc, profile, result); }
            catch (Exception ex) { StingLog.Warn($"ExportCenter PostRun: {ex.Message}"); }
            if (profile.Output.GenerateReport)
                WriteReport(profile, result);
            return result;
        }

        // ── Post-run automation (delta stamping / failed tracking / manifest / hook) ──

        private static void PostRun(Document doc, ExportProfile profile, ExportRunResult result)
        {
            var ok = result.Rows.Where(r => r.Success && !string.IsNullOrEmpty(r.OutputPath)).ToList();

            // 1) Persist per-sheet last-export records + this run's failed sheets so the
            //    "Changed Since Last Export" and "Resume Last Failed" sets work next time.
            if (doc != null && profile.Output.StampLastExport)
            {
                try
                {
                    var state = LoadState();
                    var byUid = state.LastExports.ToDictionary(r => r.SheetUniqueId + "|" + r.Format, r => r);

                    foreach (var r in ok)
                    {
                        var sheet = ResolveSheet(doc, r.SheetId);
                        if (sheet == null) continue; // combined rows don't map to one sheet
                        var (rev, _) = GetCurrentRevision(doc, sheet);
                        var rec = new SheetExportRecord
                        {
                            SheetUniqueId = sheet.UniqueId, SheetNumber = sheet.SheetNumber,
                            Revision = rev, Format = r.Format, Path = r.OutputPath, ExportedUtc = DateTime.UtcNow,
                        };
                        byUid[rec.SheetUniqueId + "|" + rec.Format] = rec;
                    }
                    state.LastExports = byUid.Values.ToList();

                    state.LastRunFailedSheetIds = result.Rows
                        .Where(r => !r.Success)
                        .Select(r => ResolveSheet(doc, r.SheetId)?.UniqueId)
                        .Where(u => !string.IsNullOrEmpty(u))
                        .Distinct().ToList();

                    SaveState(state);
                }
                catch (Exception ex) { StingLog.Warn($"Last-export stamp: {ex.Message}"); }
            }

            // 2) SHA-256 manifest of every produced file (CDE integrity / ISO 19650 record).
            string manifestPath = null;
            if (profile.Output.WriteChecksumManifest && ok.Count > 0)
            {
                try { manifestPath = WriteChecksumManifest(profile, result, ok); }
                catch (Exception ex) { result.Warnings.Add("Checksum manifest failed: " + ex.Message); }
            }

            // 3) Post-export shell hook (copy to network / notify / zip). Best-effort.
            if (!string.IsNullOrWhiteSpace(profile.Output.PostExportCommand))
            {
                try { RunPostExportHook(profile, result, manifestPath); }
                catch (Exception ex) { result.Warnings.Add("Post-export hook failed: " + ex.Message); }
            }
        }

        private static ViewSheet ResolveSheet(Document doc, string sheetIdText)
        {
            if (doc == null || string.IsNullOrEmpty(sheetIdText)) return null;
            if (!long.TryParse(sheetIdText, out long raw)) return null;
            return doc.GetElement(new ElementId(raw)) as ViewSheet;
        }

        private static string WriteChecksumManifest(ExportProfile profile, ExportRunResult result, List<ExportResultRow> ok)
        {
            string folder = profile.Output.LocalFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(folder, $"STING_Export_Manifest_{stamp}.json");

            var entries = new List<object>();
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (var r in ok)
                {
                    if (!File.Exists(r.OutputPath)) continue;
                    string hash;
                    try { using var fs = File.OpenRead(r.OutputPath); hash = Convert.ToHexString(sha.ComputeHash(fs)); }
                    catch (Exception ex) { StingLog.Warn($"Hash {r.OutputPath}: {ex.Message}"); continue; }
                    entries.Add(new
                    {
                        r.Format, r.SheetNumber, r.SheetTitle,
                        File = Path.GetFileName(r.OutputPath), Bytes = r.FileSizeBytes,
                        Sha256 = hash.ToLowerInvariant(),
                    });
                }
            }

            var manifest = new
            {
                generated = DateTime.UtcNow.ToString("o"),
                profile = profile.Name,
                project = StingTools.Core.TagConfig.ConfigSource,
                fileCount = entries.Count,
                algorithm = "SHA-256",
                files = entries,
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            StingLog.Info($"Export checksum manifest written: {path}");
            return path;
        }

        private static void RunPostExportHook(ExportProfile profile, ExportRunResult result, string manifestPath)
        {
            string cmd = profile.Output.PostExportCommand
                .Replace("{manifest}", manifestPath ?? "")
                .Replace("{folder}",   profile.Output.LocalFolder ?? "")
                .Replace("{count}",    result.Success.ToString())
                .Replace("{report}",   ""); // report path is written after this; left blank by design

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + cmd,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(profile.Output.LocalFolder) ? profile.Output.LocalFolder : Environment.CurrentDirectory,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            // Don't block the UI indefinitely on a runaway hook.
            if (p != null && !p.WaitForExit(30000))
                result.Warnings.Add("Post-export hook still running after 30s — left detached.");
            StingLog.Info($"Post-export hook executed: {cmd}");
        }

        private static int CountFormatPasses(ExportProfile p)
        {
            int n = 0;
            foreach (ExportFormats f in Enum.GetValues(typeof(ExportFormats)))
                if (f != ExportFormats.None && (p.Formats & f) != 0) n++;
            return Math.Max(1, n);
        }

        private static void EnsureFolder(ExportProfile p)
        {
            if (p.Output.Destination == ExportDestination.PlanscapeCde) return;
            var f = p.Output.LocalFolder;
            if (!string.IsNullOrEmpty(f) && !Directory.Exists(f) && p.Output.CreateFolderIfMissing)
                Directory.CreateDirectory(f);
        }

        private static string SubFolderFor(ExportProfile p, string format, string discipline)
        {
            string root = p.Output.LocalFolder;
            if (p.Output.SplitByFormatSubFolder) root = Path.Combine(root, format);
            if (p.Output.SplitByDisciplineSubFolder && !string.IsNullOrEmpty(discipline))
                root = Path.Combine(root, discipline);
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        // ── PDF pipeline ────────────────────────────────────────────────────────

        private static void RunPdf(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            var sheets = ids.Select(doc.GetElement).OfType<View>().ToList();
            if (sheets.Count == 0) return;

            switch (profile.Pdf.CombineMode)
            {
                case PdfCombineMode.OnePerSheet:
                    foreach (var v in sheets)
                    {
                        if (cancel != null && cancel()) return;
                        ExportSinglePdf(doc, v, profile, result);
                        tick?.Invoke($"PDF: {GetLabel(v)}");
                    }
                    break;

                case PdfCombineMode.OnePerDiscipline:
                {
                    var groups = sheets.OfType<ViewSheet>()
                        .GroupBy(s => GetDisciplinePrefix(s.SheetNumber))
                        .ToList();
                    foreach (var g in groups)
                    {
                        if (cancel != null && cancel()) return;
                        ExportCombinedPdf(doc, g.ToList<View>(), profile, result, g.Key, PdfCombineMode.OnePerDiscipline);
                        tick?.Invoke($"PDF (combined): {g.Key}");
                    }
                    break;
                }

                case PdfCombineMode.OnePerSet:
                    ExportCombinedPdf(doc, sheets, profile, result, "All", PdfCombineMode.OnePerSet);
                    tick?.Invoke("PDF (combined): All");
                    break;

                case PdfCombineMode.CustomGroups:
                    foreach (var kv in profile.Pdf.CustomGroups)
                    {
                        var groupSheets = kv.Value
                            .Select(idText => long.TryParse(idText, out var n) ? doc.GetElement(new ElementId(n)) : null)
                            .OfType<View>().ToList();
                        if (groupSheets.Count == 0) continue;
                        ExportCombinedPdf(doc, groupSheets, profile, result, kv.Key, PdfCombineMode.CustomGroups);
                        tick?.Invoke($"PDF (custom): {kv.Key}");
                    }
                    break;
            }
        }

        private static void ExportSinglePdf(Document doc, View view, ExportProfile profile, ExportRunResult result)
        {
            var row = StartRow(view, "PDF");
            try
            {
                string disc = view is ViewSheet vs ? GetDisciplinePrefix(vs.SheetNumber) : "";
                string folder = SubFolderFor(profile, "PDF", disc);
                string stem = Sanitise(
                    ResolveNaming(doc, view, profile.Output.NamingTemplate, profile.Output),
                    profile.Output.IllegalCharReplacement);

                stem = ResolveConflict(folder, stem, "pdf", profile.Output.ConflictMode, out bool skip);
                if (skip) { row.Success = false; row.Error = "Skipped — file exists"; return; }

                // NOTE: In Revit 2022+ the PDFExportOptions.FileName property is
                // ONLY honoured when Combine == true. With Combine == false Revit
                // names every file via its own naming rule (e.g. "Sheet - <name>"),
                // so File.Exists(stem.pdf) was always false and the export was
                // reported as a failure even though a PDF had been written. Export
                // the single view with Combine == true so the requested filename is
                // produced exactly. (A single-view "combine" is just a 1-page PDF.)
                var opts = new PDFExportOptions
                {
                    FileName = stem,
                    Combine = true,
                    AlwaysUseRaster = profile.Pdf.HiddenLineMode == "Raster",
                    RasterQuality = MapRasterQuality(profile.Pdf.RasterDpi),
                    ColorDepth = MapColorDepth(profile.Pdf.ColourScheme),
                };

                var singleView = new List<View> { view };
                bool ok = RobustPdfExport(doc, new List<ElementId> { view.Id }, folder, stem, opts,
                    out string finalPath, out long bytes, out int pageCount, out string err,
                    // Post-process the verified temp file BEFORE the atomic move so the
                    // watermark / bookmark / cover land on the published PDF in one step.
                    tmp => ExportCenterPdfPostProcess.PostProcessExport(tmp, singleView, profile, result));
                row.OutputPath = finalPath;
                row.Success = ok;
                if (ok)
                {
                    row.FileSizeBytes = bytes;
                    if (pageCount != 1)
                        result.Warnings.Add($"{row.SheetNumber}: PDF has {pageCount} page(s), expected 1.");
                }
                else row.Error = err;
            }
            catch (Exception ex)
            {
                row.Success = false; row.Error = ex.Message;
                StingLog.Warn($"PDF export {row.SheetNumber}: {ex.Message}");
            }
            finally { CommitRow(row, result); }
        }

        private static void ExportCombinedPdf(Document doc, List<View> views,
            ExportProfile profile, ExportRunResult result, string groupName,
            PdfCombineMode mode)
        {
            var row = new ExportResultRow
            {
                SheetNumber = $"[combined:{groupName}]",
                SheetTitle = $"{views.Count} sheets",
                Format = "PDF",
                StartedUtc = DateTime.UtcNow,
            };
            try
            {
                string folder = SubFolderFor(profile, "PDF", profile.Output.SplitByDisciplineSubFolder ? groupName : null);

                // Combined PDFs carry SET-level identity rather than the first
                // sheet's specifics. The sheet-number slot of the ISO 19650 name
                // becomes a set label, the level becomes "XX" (spans levels), and
                // discipline/role reflect the group — so we get a clean
                // "…-XX-DR-A-ALL-S2-P01.pdf" instead of "…-A002-S2-P01_All.pdf".
                string repl = profile.Output.IllegalCharReplacement;
                bool isAllSet = mode == PdfCombineMode.OnePerSet;
                bool isDisc   = mode == PdfCombineMode.OnePerDiscipline;
                string setLabel = (isAllSet || isDisc) ? "ALL" : Sanitise(groupName, repl);
                var nameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SheetNumber"]   = setLabel,
                    ["DrawingNumber"] = setLabel,
                    ["SheetTitle"]    = isAllSet ? "All Sheets" : groupName,
                    ["DrawingTitle"]  = isAllSet ? "All Sheets" : groupName,
                    ["Level"]         = "XX",
                };
                if (isDisc) { nameOverrides["Discipline"] = groupName; nameOverrides["Role"] = groupName; }
                else if (isAllSet) { nameOverrides["Discipline"] = "Z"; nameOverrides["Role"] = "Z"; }

                string stem = Sanitise(
                    ResolveNaming(doc, views[0], profile.Output.NamingTemplate, profile.Output, nameOverrides),
                    repl);
                stem = ResolveConflict(folder, stem, "pdf", profile.Output.ConflictMode, out bool skip);
                if (skip) { row.Success = false; row.Error = "Skipped — file exists"; return; }

                var ordered = OrderForMerge(views, profile.Pdf.MergeOrderSheetIds, profile.Pdf.MergeOrder);

                var opts = new PDFExportOptions
                {
                    FileName = stem,
                    Combine = true,
                    AlwaysUseRaster = profile.Pdf.HiddenLineMode == "Raster",
                    RasterQuality = MapRasterQuality(profile.Pdf.RasterDpi),
                    ColorDepth = MapColorDepth(profile.Pdf.ColourScheme),
                };

                bool ok = RobustPdfExport(doc, ordered.Select(v => v.Id).ToList(), folder, stem, opts,
                    out string finalPath, out long bytes, out int pageCount, out string err,
                    tmp => ExportCenterPdfPostProcess.PostProcessExport(tmp, ordered, profile, result));
                row.OutputPath = finalPath;
                row.Success = ok;
                if (ok)
                {
                    row.FileSizeBytes = bytes;
                    if (pageCount != ordered.Count)
                        result.Warnings.Add($"Combined '{groupName}': PDF has {pageCount} page(s), expected {ordered.Count}.");
                }
                else row.Error = err;
            }
            catch (Exception ex)
            {
                row.Success = false; row.Error = ex.Message;
                StingLog.Warn($"PDF combined export {groupName}: {ex.Message}");
            }
            finally
            {
                row.FinishedUtc = DateTime.UtcNow;
                result.Rows.Add(row);
            }
        }

        private static List<View> OrderForMerge(List<View> views, List<string> mergeOrder, string mergeMode = "SheetNumber")
        {
            // An explicit per-sheet order (saved drag-to-reorder) always wins.
            if (mergeOrder != null && mergeOrder.Count > 0)
            {
                var index = mergeOrder.Select((id, i) => new { id, i }).ToDictionary(x => x.id, x => x.i);
                return views.OrderBy(v => index.TryGetValue(v.Id.ToString(), out int i) ? i : int.MaxValue).ToList();
            }

            string Num(View v) => v is ViewSheet s ? s.SheetNumber ?? "" : v.Name ?? "";
            switch (mergeMode)
            {
                case "IssueDate":
                    return views.OrderBy(v => v is ViewSheet s ? (ReadParam(s, "Sheet Issue Date") ?? "") : "")
                                .ThenBy(Num).ToList();
                case "Discipline":
                    return views.OrderBy(v => v is ViewSheet s ? GetDisciplinePrefix(s.SheetNumber) : "")
                                .ThenBy(Num).ToList();
                default: // SheetNumber
                    return views.OrderBy(Num).ToList();
            }
        }


        // PDFExportOptions.RasterQuality is RasterQualityType in Revit
        // 2025+; the legacy DPI-buckets enum was retired.
        private static RasterQualityType MapRasterQuality(int dpi) => dpi switch
        {
            <= 72  => RasterQualityType.Low,
            <= 150 => RasterQualityType.Medium,
            <= 300 => RasterQualityType.High,
            _      => RasterQualityType.Presentation,
        };

        private static ColorDepthType MapColorDepth(string scheme) => scheme switch
        {
            "Greyscale"     => ColorDepthType.GrayScale,
            "BlackAndWhite" => ColorDepthType.BlackLine,
            _               => ColorDepthType.Color,
        };

        // ── Robust PDF export (atomic write + verification + retry) ──────────────

        // A genuinely-rendered single-sheet PDF is several KB; anything below this
        // is an empty / failed render masquerading as a success.
        private const long MinValidPdfBytes = 600;

        /// <summary>
        /// Export PDF robustly: render into a sibling temp folder, verify the
        /// output (non-zero bytes + opens + page count), run any post-processing
        /// on the verified temp file, then atomically rename it into place. The
        /// target is checked for locks up-front and the whole render is retried
        /// with backoff on transient I/O so a sheet open in Bluebeam/Acrobat or a
        /// momentarily-busy printer doesn't fail the batch. Revit failure messages
        /// raised during the export are captured and surfaced instead of the bare
        /// "returned false".
        /// </summary>
        private static bool RobustPdfExport(Document doc, List<ElementId> ids, string folder,
            string stem, PDFExportOptions opts, out string finalPath, out long bytes,
            out int pageCount, out string error, Action<string> preMovePostProcess)
        {
            finalPath = Path.Combine(folder, stem + ".pdf");
            bytes = 0; pageCount = 0; error = null;

            // Fail fast (and clearly) if the destination is open in a viewer.
            if (IsFileLocked(finalPath))
            {
                error = "target file is open in another application — close it " +
                        "(Bluebeam / Acrobat) and re-run";
                return false;
            }

            string tempDir  = Path.Combine(folder, ".sting_export_tmp");
            string tempPath = Path.Combine(tempDir, stem + ".pdf");
            opts.FileName = stem;

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(tempDir);
                    TryDeleteFile(tempPath);

                    var captured = new List<string>();
                    void OnFail(object s, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
                    {
                        try
                        {
                            foreach (var m in e.GetFailuresAccessor().GetFailureMessages())
                                captured.Add(m.GetDescriptionText());
                        }
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }

                    bool ok;
                    doc.Application.FailuresProcessing += OnFail;
                    try { ok = doc.Export(tempDir, ids, opts); }
                    finally { doc.Application.FailuresProcessing -= OnFail; }

                    if (!ok || !File.Exists(tempPath))
                    {
                        error = "Revit PDF export returned false"
                            + (captured.Count > 0
                                ? ": " + string.Join("; ", captured.Distinct())
                                : " (no file produced)");
                        // may be transient — fall through to the backoff + retry
                    }
                    else if (!VerifyPdf(tempPath, out bytes, out pageCount, out error))
                    {
                        // corrupt / empty output is not transient — stop now
                        TryDeleteFile(tempPath); TryDeleteTempDir(tempDir);
                        return false;
                    }
                    else
                    {
                        try { preMovePostProcess?.Invoke(tempPath); }
                        catch (Exception ex) { StingLog.Warn($"PDF post-process: {ex.Message}"); }

                        File.Move(tempPath, finalPath, true); // same-volume atomic rename
                        try { bytes = new FileInfo(finalPath).Length; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        TryDeleteTempDir(tempDir);
                        return true;
                    }
                }
                catch (IOException ioex) { error = ioex.Message; }                 // transient lock → retry
                catch (UnauthorizedAccessException uaex) { error = uaex.Message; } // transient lock → retry
                catch (Autodesk.Revit.Exceptions.ApplicationException rex) { error = rex.Message; }
                catch (Exception ex)
                {
                    error = ex.Message;
                    TryDeleteFile(tempPath); TryDeleteTempDir(tempDir);
                    return false;
                }

                if (attempt < maxAttempts) System.Threading.Thread.Sleep(400 * attempt);
            }

            TryDeleteFile(tempPath); TryDeleteTempDir(tempDir);
            return false;
        }

        private static bool VerifyPdf(string path, out long bytes, out int pageCount, out string error)
        {
            bytes = 0; pageCount = 0; error = null;
            if (!File.Exists(path)) { error = "no output file produced"; return false; }
            bytes = new FileInfo(path).Length;
            if (bytes < MinValidPdfBytes)
            {
                error = $"output is only {bytes} bytes — likely empty/corrupt";
                return false;
            }
            return ExportCenterPdfPostProcess.TryReadPdfInfo(path, out pageCount, out error);
        }

        private static bool IsFileLocked(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        private static void TryDeleteFile(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void TryDeleteTempDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        // ── DWG pipeline ────────────────────────────────────────────────────────

        private static void RunDwg(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            var sheets = ids.Select(doc.GetElement).OfType<View>().ToList();
            if (sheets.Count == 0) return;

            var dwgOpts = ResolveDwgOptions(doc, profile);

            switch (profile.Dwg.OutputMode)
            {
                case DwgOutputMode.OnePerSheet:
                case DwgOutputMode.ModelSpaceOnly:
                    foreach (var v in sheets)
                    {
                        if (cancel != null && cancel()) return;
                        ExportSingleDwg(doc, v, profile, dwgOpts, result);
                        tick?.Invoke($"DWG: {GetLabel(v)}");
                    }
                    break;

                case DwgOutputMode.AllInOneMultiLayout:
                    if (IsMultiLayoutMergerAvailable())
                        ExportMultiLayoutDwg(doc, sheets, profile, dwgOpts, result, "All");
                    else if (profile.Dwg.FallbackOnMergeFailure)
                    {
                        result.Warnings.Add("DWG multi-layout merger unavailable — exporting individual files.");
                        foreach (var v in sheets) ExportSingleDwg(doc, v, profile, dwgOpts, result);
                    }
                    else
                        result.Warnings.Add("DWG multi-layout export skipped — merger unavailable.");
                    tick?.Invoke("DWG (multi-layout)");
                    break;

                case DwgOutputMode.CustomGroups:
                    foreach (var kv in profile.Dwg.CustomGroups)
                    {
                        var group = kv.Value
                            .Select(s => long.TryParse(s, out long n) ? doc.GetElement(new ElementId(n)) : null)
                            .OfType<View>().ToList();
                        if (group.Count == 0) continue;
                        if (IsMultiLayoutMergerAvailable())
                            ExportMultiLayoutDwg(doc, group, profile, dwgOpts, result, kv.Key);
                        else
                            foreach (var v in group) ExportSingleDwg(doc, v, profile, dwgOpts, result);
                        tick?.Invoke($"DWG (group): {kv.Key}");
                    }
                    break;
            }
        }

        private static DWGExportOptions ResolveDwgOptions(Document doc, ExportProfile profile)
        {
            DWGExportOptions opts = null;
            try
            {
                if (!string.IsNullOrEmpty(profile.Dwg.ExportSetupName) &&
                    profile.Dwg.ExportSetupName != "<in-session>")
                    opts = DWGExportOptions.GetPredefinedOptions(doc, profile.Dwg.ExportSetupName);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            opts ??= new DWGExportOptions();
            opts.MergedViews = profile.Dwg.OutputMode == DwgOutputMode.ModelSpaceOnly;
            opts.FileVersion = MapDwgVersion(profile.Dwg.DwgVersion);
            return opts;
        }

        // ACADVersion.R2004 was removed in Revit 2025+; oldest supported
        // is R2007. AC2004 callers fall through to Default.
        private static ACADVersion MapDwgVersion(string v) => v switch
        {
            "AC2018" => ACADVersion.R2018,
            "AC2013" => ACADVersion.R2013,
            "AC2010" => ACADVersion.R2010,
            "AC2007" => ACADVersion.R2007,
            _        => ACADVersion.Default,
        };

        private static void ExportSingleDwg(Document doc, View view, ExportProfile profile,
            DWGExportOptions opts, ExportRunResult result)
        {
            var row = StartRow(view, "DWG");
            try
            {
                string disc = view is ViewSheet vs ? GetDisciplinePrefix(vs.SheetNumber) : "";
                string folder = SubFolderFor(profile, "DWG", disc);
                string stem = Sanitise(
                    ResolveNaming(doc, view, profile.Output.NamingTemplate, profile.Output),
                    profile.Output.IllegalCharReplacement);
                stem = ResolveConflict(folder, stem, "dwg", profile.Output.ConflictMode, out bool skip);
                if (skip) { row.Success = false; row.Error = "Skipped — file exists"; return; }

                bool ok = doc.Export(folder, stem, new List<ElementId> { view.Id }, opts);
                row.OutputPath = Path.Combine(folder, stem + ".dwg");
                row.Success = ok && File.Exists(row.OutputPath);
                if (row.Success) row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                else row.Error = "DWG export returned false";
            }
            catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
            finally { CommitRow(row, result); }
        }

        private static void ExportMultiLayoutDwg(Document doc, List<View> sheets, ExportProfile profile,
            DWGExportOptions opts, ExportRunResult result, string groupName)
        {
            var row = new ExportResultRow
            {
                SheetNumber = $"[multilayout:{groupName}]",
                SheetTitle = $"{sheets.Count} sheets",
                Format = "DWG",
                StartedUtc = DateTime.UtcNow,
            };
            try
            {
                // Step 1: per-sheet temp export
                string temp = Path.Combine(Path.GetTempPath(),
                    "STING_DWG_MERGE_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(temp);
                var perSheet = new List<string>();
                var labels   = new List<string>();
                foreach (var v in sheets)
                {
                    string label = SanitiseLayoutName(ResolveNaming(doc, v, profile.Dwg.LayoutNameTemplate, profile.Output));
                    doc.Export(temp, label, new List<ElementId> { v.Id }, opts);
                    string p = Path.Combine(temp, label + ".dwg");
                    if (File.Exists(p)) { perSheet.Add(p); labels.Add(label); }
                }

                // Step 2: AutoCAD-COM merge (ExportCenterDwgMerger). Falls through
                // to the staged per-sheet output if AutoCAD isn't installed and
                // the profile permits fallback.
                string outFolder = SubFolderFor(profile, "DWG", null);
                string outName = Sanitise(
                    ResolveNaming(doc, sheets[0], profile.Output.NamingTemplate, profile.Output) + "_" + groupName,
                    profile.Output.IllegalCharReplacement);
                string outPath = Path.Combine(outFolder, outName + ".dwg");

                // Method A — AutoCAD COM
                string merged = ExportCenterDwgMerger.Merge(perSheet, labels, outPath);
                if (merged != null && File.Exists(merged))
                {
                    row.OutputPath = merged;
                    row.FileSizeBytes = new FileInfo(merged).Length;
                    row.Success = true;
                    try { Directory.Delete(temp, true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                else
                {
                    // Method B — ODA File Converter (free): version-normalise +
                    // emit merge_manifest.json. Doesn't produce a true single
                    // multi-layout DWG, but the user gets staged files at the
                    // right version + a manifest a downstream tool can use.
                    string manifest = ExportCenterDwgMerger.MergeViaOda(
                        perSheet, labels, outPath, profile.Dwg.DwgVersion);
                    if (manifest != null)
                    {
                        result.Warnings.Add(
                            $"DWG multi-layout merge for '{groupName}' used Method B (ODA). " +
                            $"True layout merge requires AutoCAD COM or the Teigha SDK — " +
                            $"the staged folder + merge_manifest.json is at: " +
                            $"{Path.GetDirectoryName(manifest)}");
                        row.OutputPath = manifest;
                        row.Success = true;
                        try { Directory.Delete(temp, true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }
                    else if (profile.Dwg.FallbackOnMergeFailure)
                    {
                        result.Warnings.Add(
                            $"DWG multi-layout merge for '{groupName}' fell back to staged " +
                            $"individual files (no AutoCAD COM, no ODA File Converter). " +
                            $"Staged at: {temp}");
                        row.OutputPath = temp;
                        row.Success = true;
                    }
                    else
                    {
                        row.Success = false;
                        row.Error = "Multi-layout merge failed and FallbackOnMergeFailure is disabled.";
                    }
                }
            }
            catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
            finally
            {
                row.FinishedUtc = DateTime.UtcNow;
                result.Rows.Add(row);
            }
        }

        private static string SanitiseLayoutName(string name)
        {
            // AutoCAD layout tab names: max 31 chars, no /\:*?"<>|
            string s = Sanitise(name ?? "Layout", "_");
            return s.Length <= 31 ? s : s.Substring(0, 31);
        }

        // ── IFC pipeline ────────────────────────────────────────────────────────

        private static void RunIfc(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            // IFC is whole-document; we ignore sheet selection for IFC and emit one file.
            var row = new ExportResultRow
            {
                SheetNumber = "[ifc]",
                SheetTitle = doc.PathName,
                Format = "IFC",
                StartedUtc = DateTime.UtcNow,
            };
            try
            {
                string folder = SubFolderFor(profile, "IFC", null);
                string stem = Sanitise(
                    ResolveNaming(doc, doc.ActiveView, profile.Output.NamingTemplate, profile.Output),
                    profile.Output.IllegalCharReplacement);

                var opts = new IFCExportOptions();
                opts.FileVersion = MapIfcSchema(profile.Ifc.Schema);
                if (!string.IsNullOrEmpty(profile.Ifc.PhaseName))
                {
                    var phase = new FilteredElementCollector(doc).OfClass(typeof(Phase))
                        .Cast<Phase>().FirstOrDefault(p => p.Name == profile.Ifc.PhaseName);
                    if (phase != null) opts.AddOption("ActivePhaseId", phase.Id.ToString());
                }

                using (var t = new Transaction(doc, "STING IFC export"))
                {
                    t.Start();
                    bool ok = doc.Export(folder, stem, opts);
                    t.RollBack(); // IFC export doesn't actually mutate the model
                    row.OutputPath = Path.Combine(folder, stem + ".ifc");
                    row.Success = ok && File.Exists(row.OutputPath);
                    if (row.Success) row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                    else row.Error = "IFC export returned false";
                }
                tick?.Invoke("IFC");
            }
            catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
            finally
            {
                row.FinishedUtc = DateTime.UtcNow;
                result.Rows.Add(row);
            }
        }

        private static IFCVersion MapIfcSchema(string s) => s switch
        {
            "IFC2x3" => IFCVersion.IFC2x3CV2,
            "IFC4"   => IFCVersion.IFC4,
            "IFC4x3" => IFCVersion.IFC4,   // 4x3 not yet exposed in many API versions
            _        => IFCVersion.IFC4,
        };

        // ── Image / DGN / DWF ───────────────────────────────────────────────────

        private static void RunImage(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            foreach (var eid in ids)
            {
                if (cancel != null && cancel()) return;
                if (doc.GetElement(eid) is not View v) continue;
                var row = StartRow(v, "Image");
                try
                {
                    string folder = SubFolderFor(profile, "Image", null);
                    string stem = Sanitise(
                        ResolveNaming(doc, v, profile.Output.NamingTemplate, profile.Output),
                        profile.Output.IllegalCharReplacement);
                    string path = Path.Combine(folder, stem + "." + profile.Image.Format.ToLowerInvariant());

                    var io = new ImageExportOptions
                    {
                        FilePath = path,
                        ZoomType = ZoomFitType.FitToPage,
                        PixelSize = 2400,
                        ImageResolution = MapImageDpi(profile.Image.Dpi),
                        ExportRange = ExportRange.SetOfViews,
                        HLRandWFViewsFileType = MapImageType(profile.Image.Format),
                        ShadowViewsFileType = MapImageType(profile.Image.Format),
                    };
                    io.SetViewsAndSheets(new List<ElementId> { v.Id });
                    doc.ExportImage(io);

                    row.OutputPath = path;
                    row.Success = File.Exists(path);
                    if (row.Success) row.FileSizeBytes = new FileInfo(path).Length;
                    tick?.Invoke($"Image: {GetLabel(v)}");
                }
                catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
                finally { CommitRow(row, result); }
            }
        }

        private static ImageResolution MapImageDpi(int dpi) => dpi switch
        {
            <= 72  => ImageResolution.DPI_72,
            <= 150 => ImageResolution.DPI_150,
            <= 300 => ImageResolution.DPI_300,
            _      => ImageResolution.DPI_600,
        };

        private static ImageFileType MapImageType(string fmt) => fmt.ToUpperInvariant() switch
        {
            "JPEG" => ImageFileType.JPEGLossless,
            "TIFF" => ImageFileType.TIFF,
            _      => ImageFileType.PNG,
        };

        private static void RunDgn(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            var opts = new DGNExportOptions();
            foreach (var eid in ids)
            {
                if (cancel != null && cancel()) return;
                if (doc.GetElement(eid) is not View v) continue;
                var row = StartRow(v, "DGN");
                try
                {
                    string folder = SubFolderFor(profile, "DGN", null);
                    string stem = Sanitise(
                        ResolveNaming(doc, v, profile.Output.NamingTemplate, profile.Output),
                        profile.Output.IllegalCharReplacement);
                    bool ok = doc.Export(folder, stem, new List<ElementId> { v.Id }, opts);
                    row.OutputPath = Path.Combine(folder, stem + ".dgn");
                    row.Success = ok && File.Exists(row.OutputPath);
                    if (row.Success) row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                    else row.Error = "DGN export returned false";
                    tick?.Invoke($"DGN: {GetLabel(v)}");
                }
                catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
                finally { CommitRow(row, result); }
            }
        }

        private static void RunDwf(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            foreach (var eid in ids)
            {
                if (cancel != null && cancel()) return;
                if (doc.GetElement(eid) is not View v) continue;
                var row = StartRow(v, profile.Dwf.DwfX ? "DWFx" : "DWF");
                try
                {
                    string folder = SubFolderFor(profile, profile.Dwf.DwfX ? "DWFx" : "DWF", null);
                    string stem = Sanitise(
                        ResolveNaming(doc, v, profile.Output.NamingTemplate, profile.Output),
                        profile.Output.IllegalCharReplacement);

                    // DWF/DWFX overloads take a ViewSet, not a List<ElementId>.
                    var viewSet = new ViewSet();
                    viewSet.Insert(v);
                    bool ok;
                    // Use a ViewSet to disambiguate the Document.Export
                    // overload — passing a List<ElementId> binds to the
                    // SAT overload in Revit 2025 because DWF overloads
                    // were realigned to take ViewSet.
                    var vs = new ViewSet();
                    vs.Insert(v);
                    if (profile.Dwf.DwfX)
                    {
                        var dx = new DWFXExportOptions();
                        ok = doc.Export(folder, stem, vs, dx);
                    }
                    else
                    {
                        var dw = new DWFExportOptions();
                        ok = doc.Export(folder, stem, vs, dw);
                    }

                    string ext = profile.Dwf.DwfX ? ".dwfx" : ".dwf";
                    row.OutputPath = Path.Combine(folder, stem + ext);
                    row.Success = ok && File.Exists(row.OutputPath);
                    if (row.Success) row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                    else row.Error = "DWF export returned false";
                    tick?.Invoke($"DWF: {GetLabel(v)}");
                }
                catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
                finally { CommitRow(row, result); }
            }
        }

        // ── NWC pipeline ────────────────────────────────────────────────────────

        private static void RunNwc(Document doc, ExportProfile profile, ExportRunResult result, Action<string> tick)
        {
            var row = new ExportResultRow
            {
                SheetNumber = "[nwc]",
                SheetTitle = doc.PathName,
                Format = "NWC",
                StartedUtc = DateTime.UtcNow,
            };
            try
            {
                if (!ExportCenterNwcExporter.IsAvailable())
                {
                    row.Success = false;
                    row.Error = "Navisworks NWC Export Utility not detected.";
                    result.Warnings.Add(row.Error +
                        " Install the free Navisworks NWC Export Utility from Autodesk and restart Revit.");
                    return;
                }

                string folder = SubFolderFor(profile, "NWC", null);
                string stem = Sanitise(
                    ResolveNaming(doc, doc.ActiveView, profile.Output.NamingTemplate, profile.Output),
                    profile.Output.IllegalCharReplacement);

                bool ok = ExportCenterNwcExporter.Export(doc, folder, stem, profile.Nwc);
                row.OutputPath = Path.Combine(folder, stem + ".nwc");
                row.Success = ok && File.Exists(row.OutputPath);
                if (row.Success) row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                else row.Error ??= "NWC export returned false";
                tick?.Invoke("NWC");
            }
            catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
            finally
            {
                row.FinishedUtc = DateTime.UtcNow;
                result.Rows.Add(row);
            }
        }

        // ── XML pipeline ────────────────────────────────────────────────────────

        private static void RunXml(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick)
        {
            var row = new ExportResultRow
            {
                SheetNumber = "[xml]",
                SheetTitle = profile.Xml.Scope == "ProjectInfoOnly" ? "(project info)" : $"{ids.Count} sheets",
                Format = "XML",
                StartedUtc = DateTime.UtcNow,
            };
            try
            {
                string folder = SubFolderFor(profile, "XML", null);
                string stem = Sanitise(
                    ResolveNaming(doc, doc.ActiveView, profile.Output.NamingTemplate, profile.Output),
                    profile.Output.IllegalCharReplacement);
                string path = Path.Combine(folder, stem + ".xml");

                bool ok = ExportCenterXmlWriter.Write(doc, ids, path, profile.Xml);
                row.OutputPath = path;
                row.Success = ok;
                if (ok) row.FileSizeBytes = new FileInfo(path).Length;
                else row.Error = "XML writer returned false";
                tick?.Invoke("XML");
            }
            catch (Exception ex) { row.Success = false; row.Error = ex.Message; }
            finally
            {
                row.FinishedUtc = DateTime.UtcNow;
                result.Rows.Add(row);
            }
        }

        // ── Helpers shared by per-format runs ───────────────────────────────────

        private static ExportResultRow StartRow(View v, string format) => new()
        {
            SheetId = v?.Id.ToString(),
            SheetNumber = v is ViewSheet s ? s.SheetNumber : "",
            SheetTitle = v?.Name ?? "",
            Format = format,
            StartedUtc = DateTime.UtcNow,
        };

        private static void CommitRow(ExportResultRow row, ExportRunResult result)
        {
            row.FinishedUtc = DateTime.UtcNow;
            result.Rows.Add(row);
        }

        private static string GetLabel(View v) =>
            v is ViewSheet s ? $"{s.SheetNumber} - {s.Name}" : v.Name;

        /// <summary>Apply the configured filename-conflict rule.</summary>
        public static string ResolveConflict(string folder, string stem, string ext,
            FilenameConflictMode mode, out bool skip)
        {
            skip = false;
            string full = Path.Combine(folder, stem + "." + ext);
            if (!File.Exists(full)) return stem;

            switch (mode)
            {
                case FilenameConflictMode.Skip:      skip = true; return stem;
                case FilenameConflictMode.Overwrite: return stem;
                case FilenameConflictMode.AutoRename:
                {
                    int i = 1;
                    while (File.Exists(Path.Combine(folder, $"{stem}_{i}.{ext}"))) i++;
                    return $"{stem}_{i}";
                }
                default:
                    // Caller would prompt in Ask mode — engine returns stem and lets the dialog override.
                    return stem;
            }
        }

        // ── Report writer ───────────────────────────────────────────────────────

        private static void WriteReport(ExportProfile profile, ExportRunResult result)
        {
            try
            {
                string folder = profile.Output.LocalFolder;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(folder, $"STING_Export_Report_{stamp}.csv");

                using var w = new StreamWriter(path);
                w.WriteLine("Format,SheetNumber,SheetTitle,OutputPath,Bytes,Success,Error,DurationMs");
                foreach (var r in result.Rows)
                {
                    w.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.Format), Csv(r.SheetNumber), Csv(r.SheetTitle),
                        Csv(r.OutputPath), r.FileSizeBytes.ToString(),
                        r.Success ? "1" : "0", Csv(r.Error),
                        ((long)r.Duration.TotalMilliseconds).ToString(),
                    }));
                }
                StingLog.Info($"Export report written: {path}");
            }
            catch (Exception ex) { StingLog.Warn($"Export report: {ex.Message}"); }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool quote = s.Contains(',') || s.Contains('"') || s.Contains('\n');
            string escaped = s.Replace("\"", "\"\"");
            return quote ? $"\"{escaped}\"" : escaped;
        }
    }
}
