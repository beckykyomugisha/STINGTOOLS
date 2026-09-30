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
        // ── State persistence ───────────────────────────────────────────────────
        //
        // Two homes (DOCX-2). USER-level — profiles, the last profile, ODA path, recent
        // folders — stays in project_config.json under "ExportCenter", because a profile
        // is a reusable recipe. PROJECT-level — saved sets (ElementIds), last-export
        // records, scheduled jobs and the save-trigger opt-in — lives in
        // <project>/_data/coord/export_center.json. All of it used to be in the one
        // shared file, so a saved set from project A resolved to arbitrary elements in
        // project B and "Changed Since Last Export" mixed two projects' history.
        //
        // An unsaved model has no project folder and keeps the old single-file behaviour.

        private const string ConfigKey = "ExportCenter";
        private const string ProjectStateFile = "export_center.json";

        /// <summary>The per-project half of <see cref="ExportCenterState"/>.</summary>
        private sealed class ProjectExportState
        {
            public int SchemaVersion { get; set; } = 1;
            public List<ExportSavedSet> SavedSets { get; set; } = new();
            public List<ScheduledExport> ScheduledExports { get; set; } = new();
            public bool EnableSaveTriggeredSchedules { get; set; }
            public List<SheetExportRecord> LastExports { get; set; } = new();
            public string LastOutputFolder { get; set; }
        }

        private static string ProjectStatePath(Document doc)
        {
            try { return doc == null ? null : StingPaths.MetaFile(doc, "_BIM_COORD", ProjectStateFile); }
            catch (Exception ex) { StingLog.Warn($"Export Centre project state path: {ex.Message}"); return null; }
        }

        /// <summary>State for <paramref name="doc"/>: user-level settings plus this
        /// project's own sets, records and schedules. With no document (or an unsaved
        /// one), the single shared file as before.</summary>
        public static ExportCenterState LoadState(Document doc)
        {
            var st = LoadState();
            string path = ProjectStatePath(doc);
            if (string.IsNullOrEmpty(path)) return st;
            try
            {
                ProjectExportState proj;
                if (File.Exists(path))
                    proj = JsonConvert.DeserializeObject<ProjectExportState>(File.ReadAllText(path)) ?? new ProjectExportState();
                else
                {
                    proj = MigrateProjectState(doc, st);
                    WriteProjectState(path, proj);
                }
                var builtIns = st.SavedSets.Where(x => x.BuiltIn).ToList();
                st.SavedSets = builtIns.Concat(proj.SavedSets ?? new List<ExportSavedSet>()).ToList();
                st.ScheduledExports = proj.ScheduledExports ?? new List<ScheduledExport>();
                st.EnableSaveTriggeredSchedules = proj.EnableSaveTriggeredSchedules;
                st.LastExports = proj.LastExports ?? new List<SheetExportRecord>();
                st.LastOutputFolder = proj.LastOutputFolder;
            }
            catch (Exception ex) { StingLog.Warn($"Export Centre project state load: {ex.Message}"); }
            return st;
        }

        /// <summary>First load in a project: keep only what provably belongs to it — sets
        /// whose every id is a view in this model, last-export records for sheets that
        /// exist here, schedules whose set survived, and a last folder inside this
        /// project. The shared file is left untouched for other projects to migrate from.</summary>
        private static ProjectExportState MigrateProjectState(Document doc, ExportCenterState legacy)
        {
            var proj = new ProjectExportState();
            foreach (var set in legacy.SavedSets.Where(x => !x.BuiltIn))
            {
                bool all = set.ElementIds.Count > 0 && set.ElementIds.All(id =>
                    long.TryParse(id, out long raw) && doc.GetElement(new ElementId(raw)) is View);
                if (all) proj.SavedSets.Add(set);
                else StingLog.Info($"Export Centre migration: saved set '{set.Name}' does not belong to this model — not carried over.");
            }
            foreach (var r in legacy.LastExports ?? new List<SheetExportRecord>())
                if (!string.IsNullOrEmpty(r.SheetUniqueId) && doc.GetElement(r.SheetUniqueId) is ViewSheet)
                    proj.LastExports.Add(r);
            var setNames = new HashSet<string>(proj.SavedSets.Select(x => x.Name)
                .Concat(legacy.SavedSets.Where(x => x.BuiltIn).Select(x => x.Name)), StringComparer.OrdinalIgnoreCase);
            foreach (var sch in legacy.ScheduledExports ?? new List<ScheduledExport>())
                if (string.IsNullOrEmpty(sch.SetName) || setNames.Contains(sch.SetName)) proj.ScheduledExports.Add(sch);
            try
            {
                string root = ProjectFolderEngine.GetRootPath(doc);
                if (!string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(legacy.LastOutputFolder) &&
                    Path.GetFullPath(legacy.LastOutputFolder).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                    proj.LastOutputFolder = legacy.LastOutputFolder;
            }
            catch (Exception ex) { StingLog.Warn($"Export Centre migration folder: {ex.Message}"); }
            StingLog.Info($"Export Centre state migrated for this project: {proj.SavedSets.Count} set(s), " +
                          $"{proj.LastExports.Count} last-export record(s), {proj.ScheduledExports.Count} schedule(s).");
            return proj;
        }

        private static void WriteProjectState(string path, ProjectExportState proj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            OutputLocationHelper.WriteAllTextAtomic(path, JsonConvert.SerializeObject(proj, Formatting.Indented));
        }

        /// <summary>Save for <paramref name="doc"/>: the project half to the project file,
        /// the user half to project_config.json — whose legacy project fields are left as
        /// they were so unmigrated projects can still carry theirs over.</summary>
        public static void SaveState(ExportCenterState st, Document doc)
        {
            string path = ProjectStatePath(doc);
            if (string.IsNullOrEmpty(path)) { SaveState(st); return; }
            try
            {
                WriteProjectState(path, new ProjectExportState
                {
                    SavedSets = st.SavedSets.Where(x => !x.BuiltIn).ToList(),
                    ScheduledExports = st.ScheduledExports ?? new List<ScheduledExport>(),
                    EnableSaveTriggeredSchedules = st.EnableSaveTriggeredSchedules,
                    LastExports = st.LastExports ?? new List<SheetExportRecord>(),
                    LastOutputFolder = st.LastOutputFolder,
                });

                string cfg = TagConfig.ConfigSource;
                if (string.IsNullOrEmpty(cfg)) return;
                JObject root = File.Exists(cfg) ? JObject.Parse(File.ReadAllText(cfg)) : new JObject();
                var prev = root[ConfigKey] as JObject;
                var user = JObject.FromObject(st);
                foreach (var key in new[] { "SavedSets", "ScheduledExports", "EnableSaveTriggeredSchedules", "LastExports", "LastOutputFolder" })
                {
                    if (prev != null && prev[key] != null) user[key] = prev[key];
                    else user.Remove(key);
                }
                root[ConfigKey] = user;
                File.WriteAllText(cfg, root.ToString(Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"Export Centre state save: {ex.Message}"); }
        }

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
                    // revision recorded in state. The biggest automation win for an
                    // ISO 19650 re-issue — only the drawings that moved go out. Records
                    // are stamped post-run when the profile's Output.StampLastExport is
                    // on (default). Salvaged from claude/dreamy-maxwell-prlg44.
                    var state = LoadState(doc);
                    var byUid = (state.LastExports ?? new List<SheetExportRecord>())
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
            return sheets.Where(s => SheetDiscipline(s).Equals(disc, StringComparison.OrdinalIgnoreCase))
                         .Select(s => s.Id).ToList();
        }

        public static string GetDisciplinePrefix(string sheetNumber) => SheetDiscipline(sheetNumber, null);

        /// <summary>The discipline a sheet declares, via the shared
        /// <see cref="Core.Drawing.SheetDisciplineResolver.ForSheet"/> (number, ISO role
        /// segment, then title). An unconfigured prefix is kept as written so such sheets
        /// still group together; "Other" when there is nothing to read.</summary>
        public static string SheetDiscipline(string sheetNumber, string sheetName)
        {
            string d = Core.Drawing.SheetDisciplineResolver.ForSheet(sheetNumber, sheetName);
            if (!string.IsNullOrEmpty(d)) return d;
            if (string.IsNullOrWhiteSpace(sheetNumber)) return "Other";
            string letters = new string(sheetNumber.Trim().TakeWhile(char.IsLetter).ToArray());
            return string.IsNullOrEmpty(letters) ? "Other" : letters.ToUpperInvariant();
        }

        private static string SheetDiscipline(ViewSheet s) => SheetDiscipline(s?.SheetNumber, s?.Name);

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
        {
            if (string.IsNullOrEmpty(template)) template = "{SheetNumber} - {SheetTitle}";
            template = EffectiveNamingTemplate(doc, template);

            var tokens = BuildTokenContext(doc, view);
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

        private static readonly object _sevenGate = new object();
        private static readonly Dictionary<string, (DateTime at, bool seven)> _sevenCache =
            new Dictionary<string, (DateTime, bool)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Does this project's ACC settings file apply the 7-field ISO 19650 name
        /// (AccOperatingPolicy.SevenFieldNaming)? Cached per document for 30 s — ResolveNaming
        /// runs once per sheet per format.</summary>
        internal static bool SevenFieldNamingApplies(Document doc)
        {
            if (doc == null) return false;
            string key = doc.PathName ?? doc.Title ?? "";
            lock (_sevenGate)
                if (_sevenCache.TryGetValue(key, out var c) && (DateTime.UtcNow - c.at).TotalSeconds < 30) return c.seven;
            bool seven = false;
            try { seven = V6.AccOperatingPolicy.Load(Core.Clash.AccProjectSettingsFile.PathFor(doc)).SevenFieldNaming; }
            catch (Exception ex) { StingLog.Warn("Export naming: ACC settings: " + ex.Message); }
            lock (_sevenGate) _sevenCache[key] = (DateTime.UtcNow, seven);
            return seven;
        }

        /// <summary>The naming template actually used: the built-in 9-field ISO default becomes
        /// the 7-field ISO name when the project's ACC settings apply it — suitability and
        /// revision then travel as ACC attributes, and an ACC item keeps one name across
        /// revisions (ACC matches items by name). A template the user wrote is never changed.</summary>
        internal static string EffectiveNamingTemplate(Document doc, string template)
            => string.Equals(template, ExportNamingPresets.Iso19650Full, StringComparison.Ordinal) && SevenFieldNamingApplies(doc)
                ? ExportNamingPresets.Iso19650SevenField
                : template;

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
                t["Discipline"]   = SheetDiscipline(sheet);

                var (_, revDate) = GetCurrentRevision(doc, sheet);
                t["RevDate"] = revDate ?? "";

                // ── ISO 19650 token resolution chain ──
                // 1) Sheet-level STING_* params (per-sheet overrides a project may
                //    have added by hand — no STING command writes or binds them)
                // 2) The sheet's own ISO identifier (SHT_TAG_1_TXT from Tag Sheets,
                //    or the sheet number once it IS the identifier), decomposed.
                //    Iso19650DocumentCode: "the identifier is the SOURCE and these
                //    are its decomposition" — so a filename built from it cannot
                //    contradict the DRG NO. printed on the drawing.
                // 3) The suitability code Title Block Populate writes onto the sheet
                // 4) Stamped DrawingType.IsoNaming (Phase 113 — auto-populated
                //    when the sheet was created through the Drawing Type engine)
                // 5) Sensible ISO 19650-2 defaults
                //
                // Before this, step 1 was the ONLY per-sheet source and none of its
                // five parameters exists in MR_PARAMETERS, so every file fell through
                // to the defaults: an S4 drawing exported as "...-S2-...", a level
                // code "L01" where ISO uses "01", and a revision "3" (the Revit
                // sequence number) where the revision box prints "P03".
                var idSegs = DecomposeSheetIdentifier(sheet, out string docId);
                t["DocumentId"] = docId ?? "";
                t["Number"]     = idSegs?.Number ?? "";
                if (idSegs != null)
                {
                    t["ProjectCode"]    = idSegs.Project;
                    t["Project"]        = idSegs.Project;
                    t["Originator"]     = idSegs.Originator;
                    t["OriginatorCode"] = idSegs.Originator;
                }
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

                string derivedLevel = GetLevelFromSheet(doc, sheet);
                t["Volume"]      = ReadParam(sheet, "STING_VOLUME_TXT")      ?? idSegs?.Volume ?? dtIso?.Volume ?? "ZZ";
                t["Level"]       = ReadParam(sheet, "STING_LVL_COD_TXT")     ?? idSegs?.Level
                                   ?? (derivedLevel != null ? Core.Drawing.Iso19650DocumentCode.NormaliseLevel(derivedLevel) : null)
                                   ?? "XX";
                t["Type"]        = ReadParam(sheet, "STING_DOC_TYPE_TXT")    ?? idSegs?.Type   ?? dtIso?.Type   ?? "DR";
                string disc      = t["Discipline"];
                t["Role"]        = ReadParam(sheet, "STING_ROLE_TXT")        ?? idSegs?.Role   ?? dtIso?.Role
                                   ?? Core.Drawing.Iso19650DocumentCode.NormaliseRole(disc);
                // Suitability and revision are what the SHEET carries, or an explicit
                // not-set marker (XX / NOREV) — never the drawing type's template default
                // ("S2"/"P01" on 91 of 93 corporate types) and never an invented code. The
                // same resolver feeds the export row, the register row and the ACC upload,
                // so the file name and the register cannot disagree (ExportIsoFields).
                // {IsoName}: the 7-field ISO 19650 name — the sheet's own assembled identifier
                // when it has one (so the file name cannot contradict the DRG NO.), else the
                // fields composed. No suitability, no revision: those are metadata.
                t["IsoName"] = !string.IsNullOrEmpty(docId) ? docId
                    : string.Join("-", t["ProjectCode"], t["Originator"], t["Volume"], t["Level"], t["Type"], t["Role"], t["SheetNumber"]);
                var iso = ResolveIsoFields(doc, sheet);
                t["Suitability"] = iso.Suitability;
                t["CdeState"]    = iso.CdeState ?? "";
                t["Revision"]    = iso.Revision;
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

        /// <summary>The sheet's revision as the revision box prints it ("P03", "C01"),
        /// plus that revision's date.
        ///
        /// Was <c>Revision.SequenceNumber</c> of the last id in GetAllRevisionIds —
        /// Revit's internal ordering counter, so a sheet at P03 exported as "...-3"
        /// and its filename disagreed with the drawing. Same chain Title Block
        /// Populate uses for the CDE REF cell: SHEET_CURRENT_REVISION first, then
        /// the title-block revision parameter for a set issued without Revit
        /// revisions in play.
        ///
        /// The value is also the key "Changed Since Last Export" compares, so the
        /// first run after this change reports every previously exported sheet as
        /// changed once — the safe direction for a re-issue.</summary>
        /// <remarks>Now the latest ISSUED revision, numbered on the sheet (SheetRevisionResolver —
        /// the same answer the title block prints). SHEET_CURRENT_REVISION is Revit's
        /// newest revision of any kind, so the first cloud of an un-issued draft renamed
        /// every export to a revision nobody had issued.</remarks>
        private static (string rev, string date) GetCurrentRevision(Document doc, ViewSheet sheet)
        {
            string label = null, date = null;
            try
            {
                var state = Core.Drawing.SheetRevisionReader.Read(doc, sheet);
                if (state.Issued != null)
                {
                    label = state.Issued.NumberOnSheet;
                    date = state.Issued.Date;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Export revision read on '{sheet?.SheetNumber}': {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(label))
                label = ReadParam(sheet, "PRJ_TB_REVISION_NR_TXT");
            return (string.IsNullOrWhiteSpace(label) ? null : label.Trim(), date);
        }

        /// <summary>The sheet's suitability and revision as the export file name, the
        /// register row and the ACC upload all use them: the sheet's own suitability code
        /// and current revision, or the explicit not-set markers. One chain, so the three
        /// cannot disagree about the same file.</summary>
        internal static Core.Drawing.ExportIsoFieldValues ResolveIsoFields(Document doc, ViewSheet sheet)
        {
            if (sheet == null) return Core.Drawing.ExportIsoFields.Resolve(null, null);
            return Core.Drawing.ExportIsoFields.Resolve(SheetSuitabilityCode(sheet), GetCurrentRevision(doc, sheet).rev);
        }

        /// <summary>The sheet's ISO 19650 identifier, decomposed — SHT_TAG_1_TXT when
        /// Tag Sheets has assembled one, else the sheet number when it already IS one.
        /// Null when neither parses; a half-parsed identifier would make every segment
        /// look populated while several were wrong.</summary>
        internal static Core.Drawing.Iso19650DocumentCode.Segments DecomposeSheetIdentifier(
            ViewSheet sheet, out string identifier)
        {
            identifier = null;
            if (sheet == null) return null;
            string tagged = ReadParam(sheet, ParamRegistry.SHT_TAG_1);
            if (Core.Drawing.Iso19650DocumentCode.LooksAssembled(tagged)) identifier = tagged.Trim();
            else if (Core.Drawing.Iso19650DocumentCode.LooksAssembled(sheet.SheetNumber)) identifier = sheet.SheetNumber.Trim();
            return identifier == null ? null : Core.Drawing.Iso19650DocumentCode.Decompose(identifier);
        }

        /// <summary>The suitability CODE Title Block Populate normalises onto the sheet
        /// (PRJ_DWG_SUITABILITY_COD_TXT, then the STATUS cell). ExtractCode tolerates a
        /// hand-typed "S4 - FOR APPROVAL"; null when neither holds a known code.</summary>
        private static string ReadSuitabilityCode(ViewSheet sheet)
        {
            foreach (string p in new[] { ParamRegistry.DWG_SUITABILITY_COD, ParamRegistry.PRJ_STATUS_COD })
            {
                string code = Core.Drawing.Iso19650Suitability.ExtractCode(ReadParam(sheet, p));
                if (!string.IsNullOrEmpty(code)) return code;
            }
            return null;
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

            // Destination. No code uploads to the Planscape CDE server: with
            // "Planscape CDE" selected the local-folder checks were skipped, the
            // folder resolved to "", and every sheet then failed on an empty path
            // — or, with a stale folder in the profile, the files quietly went
            // there and nothing was uploaded. Say so before anything runs.
            if (profile.Output.Destination == ExportDestination.PlanscapeCde)
                issues.Add(Err("CDE_UPLOAD_UNAVAILABLE",
                    "Uploading to the Planscape CDE server is not implemented in the Export Centre. " +
                    "Choose 'Local / Network folder' and press Auto to target this project's CDE folder."));
            else if (profile.Output.Destination == ExportDestination.Both)
                issues.Add(Warn("CDE_UPLOAD_UNAVAILABLE",
                    "Files will be written to the local folder only — the Planscape CDE upload is not implemented."));

            // Folder writability
            try
            {
                if (profile.Output.RouteByProjectStructure)
                {
                    if (string.IsNullOrEmpty(doc?.PathName))
                        issues.Add(Err("ROUTE_UNSAVED",
                            "Routing into the project structure needs a saved project — save the model first."));
                }
                else if (profile.Output.Destination != ExportDestination.PlanscapeCde)
                {
                    var folder = profile.Output.LocalFolder;
                    if (string.IsNullOrEmpty(folder))
                        issues.Add(Err("NO_OUTPUT_FOLDER", "Output folder is empty."));
                    else if (!Path.IsPathRooted(folder))
                        issues.Add(Err("RELATIVE_PATH",
                            $"Output folder must be an absolute path (e.g. C:\\Exports). Got: '{folder}'"));
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

            // Profiles are shared across projects (DOCX-2) and carry an absolute folder,
            // so a profile last used on another job points into THAT job's folders.
            try
            {
                string root = doc != null ? ProjectFolderEngine.GetRootPath(doc) : null;
                string folder = profile.Output.LocalFolder;
                if (!profile.Output.RouteByProjectStructure && !string.IsNullOrEmpty(root) &&
                    !string.IsNullOrEmpty(folder) && Path.IsPathRooted(folder) &&
                    !Path.GetFullPath(folder).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                    issues.Add(Warn("OUTSIDE_PROJECT",
                        $"Output folder is outside this project ({root}): {folder}. " +
                        "Press Auto, or tick 'File into the project structure', to export into this project."));
            }
            catch (Exception ex) { StingLog.Warn($"Preflight project-folder check: {ex.Message}"); }

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
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(profile, result); }
                }

                // DWG
                if ((profile.Formats & ExportFormats.DWG) != 0)
                {
                    RunDwg(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label),
                        cancelRequested);
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(profile, result); }
                }

                // IFC
                if ((profile.Formats & ExportFormats.IFC) != 0)
                {
                    RunIfc(doc, profile, selectedIds, result,
                        (label) => progress?.Invoke(++done, total, label),
                        cancelRequested);
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(profile, result); }
                }

                if ((profile.Formats & ExportFormats.NWC) != 0)
                {
                    RunNwc(doc, profile, result,
                        (label) => progress?.Invoke(++done, total, label));
                    if (cancelRequested != null && cancelRequested()) { result.Cancelled = true; return Finalize(profile, result); }
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
            if (profile?.Output != null && !result.Cancelled &&
                !string.Equals(EffectiveNamingTemplate(doc, profile.Output.NamingTemplate), profile.Output.NamingTemplate, StringComparison.Ordinal))
                result.Warnings.Add("File names use the 7-field ISO 19650 name (no suitability or revision) because this " +
                                    "project's ACC settings apply it (acc_settings.json \"fileNamingFields\"; absent = 7 when ACC " +
                                    "is configured). Suitability and revision are recorded in the register and sent to ACC as attributes.");
            AnnotateIsoFields(doc, result);
            StampLastExports(doc, profile, result);
            RegisterExports(doc, profile, result);
            UploadExportsToAcc(doc, profile, result);
            return Finalize(profile, result);
        }

        /// <summary>
        /// Stamp each single-sheet row with the suitability, revision and document number its
        /// file name was built from (ResolveIsoFields — the same chain BuildTokenContext used),
        /// flag the rows where either was not set, and add ONE summary warning naming them.
        /// The register and the ACC upload read these fields; neither re-derives them.
        /// </summary>
        private static void AnnotateIsoFields(Document doc, ExportRunResult result)
        {
            if (doc == null || result == null) return;
            var flagged = new List<string>();
            foreach (var r in result.Rows.Where(x => x.Success))
            {
                try
                {
                    var sheet = ResolveSheet(doc, r.SheetId);
                    if (sheet == null) continue;
                    var iso = ResolveIsoFields(doc, sheet);
                    r.Suitability = iso.Suitability;
                    r.Revision = iso.Revision;
                    DecomposeSheetIdentifier(sheet, out string id);
                    r.DocumentNumber = id ?? sheet.SheetNumber;
                    r.IsoFieldsUnset = iso.Unset;
                    if (!iso.Complete)
                        flagged.Add($"{sheet.SheetNumber} ({r.Format}: {Core.Drawing.ExportIsoFields.DescribeUnset(iso.Unset)})");
                }
                catch (Exception ex) { StingLog.Warn($"Export ISO fields {r.SheetNumber}/{r.Format}: {ex.Message}"); }
            }
            if (flagged.Count == 0) return;
            result.Warnings.Add(
                $"{flagged.Count} file(s) exported from sheets with no suitability and/or no revision. Their names carry " +
                $"'{Core.Drawing.ExportIsoFields.NotSetSuitability}' / '{Core.Drawing.ExportIsoFields.NotSetRevision}' in place " +
                "of the missing value, the register records them as not set, and they will not be uploaded to ACC. " +
                "Set the suitability (Title Block Populate / PRJ_DWG_SUITABILITY_COD_TXT) and a revision on: " +
                string.Join(", ", flagged.Take(12)) + (flagged.Count > 12 ? $" … and {flagged.Count - 12} more" : "") + ".");
            StingLog.Warn($"Export Centre: {flagged.Count} file(s) exported without suitability/revision: " + string.Join(", ", flagged));
        }

        /// <summary>
        /// Optional (Output.UploadToAcc): send each exported sheet file to ACC after the register
        /// is written. Per file: a file whose suitability or revision is not set is refused; a revision that
        /// contradicts its suitability (Iso19650RevisionRules, via AccUploadGate) is refused; the
        /// ledger skips an identical file already sent and refuses a CHANGED file under a document
        /// number + revision already sent (a re-issue without a revision change) unless the
        /// profile allows it; everything else goes through AccModelUpload with the project's CDE
        /// folders and ISO 19650 attributes. Every outcome is recorded on the row and summarised
        /// in the warnings. Nothing here undoes the export.
        /// </summary>
        private static void UploadExportsToAcc(Document doc, ExportProfile profile, ExportRunResult result)
        {
            if (doc == null || profile?.Output == null || !profile.Output.UploadToAcc) return;
            if (result == null || result.Cancelled) return;
            var rows = result.Rows.Where(x => x.Success && File.Exists(x.OutputPath ?? "")).ToList();
            var tally = new ExportAccUploadTally { Requested = true };
            result.Acc = tally;
            if (rows.Count == 0) return;
            // A whole-run block: recorded on the tally (which fails a scheduled step) and put
            // first in the warnings, where the result dialog cannot cut it off.
            void Block(string why, string rowNote)
            {
                tally.BlockedReason = why;
                result.Warnings.Insert(0, tally.Line());
                foreach (var r in rows) r.AccUpload = rowNote;
            }

            const string who = "Export Centre ACC upload";
            V6.AccCredentials creds;
            V6.AccOperatingPolicy policy;
            try
            {
                policy = Core.Clash.AccProjectSettingsFile.LoadFor(doc, who);
                creds = Core.Clash.AccProjectSettingsFile.LoadCredentials(doc, who);
            }
            catch (Exception ex)
            {
                StingLog.Warn("Export Centre ACC upload: settings: " + ex.Message);
                Block("the project's ACC settings could not be read: " + ex.Message, "not uploaded: ACC settings unreadable");
                return;
            }
            if (creds == null || string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                Block("ACC is not set up for this project on this machine " +
                      "(BIM Coordination Center > ACC: sign in, then Discover the project).", "not uploaded: ACC not set up");
                return;
            }

            string ledgerPath;
            V6.AccUploadLedger ledger;
            try
            {
                // The same ledger ACC_UploadModel / ACC_UploadLastBundle read and write.
                ledgerPath = Core.Clash.AccUploadCommandBase.LedgerPath(doc);
                ledger = V6.AccUploadLedger.Load(ledgerPath, out string ledgerErr);
                if (ledger == null)
                {
                    // An unreadable ledger is not an empty one: treating it as empty would re-send
                    // every file as "never sent" and stack duplicate versions in ACC.
                    Block("the upload ledger could not be read (" + ledgerErr + "): " + ledgerPath, "not uploaded: ledger unreadable");
                    return;
                }
            }
            catch (Exception ex)
            {
                Block("the upload ledger path could not be resolved: " + ex.Message, "not uploaded: ledger unavailable");
                return;
            }

            string originator = "";
            try { originator = ParameterHelpers.GetString(doc.ProjectInformation, ParamRegistry.ORG_ORIGINATOR_CODE); }
            catch (Exception ex) { StingLog.Warn("Export Centre ACC upload: originator code: " + ex.Message); }

            int sent = 0, identical = 0, refused = 0, failed = 0;
            var problems = new List<string>();
            foreach (var r in rows)
            {
                string name = Path.GetFileName(r.OutputPath);
                try
                {
                    if (string.IsNullOrEmpty(r.Suitability))
                    {
                        r.AccUpload = "not uploaded: not a single sheet, so there is no suitability or revision to file it by";
                        tally.NotEligible++; continue;
                    }
                    if (r.IsoFieldsUnset != null && r.IsoFieldsUnset.Count > 0)
                    {
                        r.AccUpload = "not uploaded: " + Core.Drawing.ExportIsoFields.DescribeUnset(r.IsoFieldsUnset);
                        refused++; problems.Add($"{name}: {Core.Drawing.ExportIsoFields.DescribeUnset(r.IsoFieldsUnset)}"); continue;
                    }
                    // The one pre-upload discipline ACC_UploadModel also runs: ISO 19650
                    // revision/suitability pairing, then the ledger (identical -> skip; changed
                    // under a revision already sent -> refused unless the profile allows it).
                    var gate = V6.AccUploadGate.Check(ledger, r.OutputPath, r.DocumentNumber, r.Revision, r.Suitability,
                        profile.Output.AccAllowReissueWithoutRevisionChange);
                    if (gate.Decision == V6.AccUploadGateDecision.SkipIdentical)
                    {
                        r.AccUpload = "skipped: " + gate.Reason;
                        identical++; continue;
                    }
                    if (!gate.ShouldUpload)
                    {
                        r.AccUpload = "not uploaded: " + gate.Reason;
                        if (gate.HeldAsReissue) { tally.Held++; continue; }
                        refused++; problems.Add($"{name}: {gate.Reason}"); continue;
                    }

                    var options = new V6.AccUploadOptions
                    {
                        Suitability = r.Suitability,
                        CdeFolders = policy.CdeFolders,
                        CreateMissingAttributes = policy.DocsAttributesCreateMissing,
                        AttributeNames = policy.DocsAttributeNames,
                        SevenFieldNaming = policy.SevenFieldNaming,
                        CheckNamingStandard = true,
                    };
                    if (policy.DocsAttributes)
                        options.Metadata = new V6.AccDocMetadataInput
                        {
                            DocumentNumber = r.DocumentNumber,
                            Suitability = r.Suitability,
                            Revision = r.Revision,
                            Originator = originator ?? "",
                        };
                    var up = V6.AccModelUpload.UploadAsync(creds, r.OutputPath, options).GetAwaiter().GetResult();
                    if (up == null || !up.Ok)
                    {
                        string why = up?.Message ?? "the upload returned no result";
                        r.AccUpload = $"FAILED ({up?.Status.ToString() ?? "TransportFailed"}): {why}";
                        failed++; problems.Add($"{name}: FAILED — {why}");
                        StingLog.Warn($"Export Centre ACC upload FAILED for '{r.OutputPath}': {why}");
                        continue;
                    }
                    V6.AccUploadGate.Record(ledger, gate, r.OutputPath, r.DocumentNumber, r.Revision, r.Suitability,
                        up.ItemUrn, up.VersionUrn, DateTime.UtcNow, up.FolderUrn);
                    // Save after every upload: a crash half-way must not forget what already went.
                    if (!ledger.TrySave(ledgerPath, out string saveErr))
                        problems.Add($"{name}: uploaded, but the ledger could not be saved ({saveErr}) — a re-run may send it again");
                    r.AccUpload = "uploaded" +
                                  (gate.ReissueAllowed ? " (re-issue allowed by the profile)" : "") +
                                  (up.MetadataComplete ? "" : " — " + up.MetadataNote);
                    // A11: the file is in ACC without the attributes asked for; that belongs in
                    // the run's problem list, not only in one row's note.
                    if (up.MetadataIncomplete)
                        problems.Add($"{name}: uploaded, but its ISO 19650 attributes are incomplete — {up.MetadataNote}");
                    sent++;
                }
                catch (Exception ex)
                {
                    StingLog.Error($"Export Centre ACC upload '{r.OutputPath}'", ex);
                    r.AccUpload = "FAILED: " + ex.Message;
                    failed++; problems.Add($"{name}: FAILED — {ex.Message}");
                }
            }

            tally.Uploaded = sent; tally.Identical = identical; tally.Refused = refused; tally.Failed = failed;
            // First, not last: the result dialog shows only the first warnings, and this is the
            // line that says whether the issue set reached the CDE.
            var accLines = new List<string> { tally.Line() + " The export itself is complete either way." };
            accLines.AddRange(problems.Take(10).Select(p => "ACC: " + p));
            if (problems.Count > 10) accLines.Add($"ACC: … and {problems.Count - 10} more (see the export report).");
            result.Warnings.InsertRange(0, accLines);
            StingLog.Info($"Export Centre ACC upload: {sent} sent, {identical} identical, {refused} refused, {failed} failed.");
        }

        /// <summary>
        /// Delta automation (salvaged from claude/dreamy-maxwell-prlg44, scoped to the
        /// "Changed Since Last Export" feature). After a completed run, persist a
        /// per-sheet last-export record (revision + path) for every successful,
        /// single-sheet row so the delta set can tell next time which drawings moved.
        /// Gated on Output.StampLastExport (default true); skipped for cancelled runs.
        /// Combined-PDF rows don't map to one sheet and are ignored.
        /// </summary>
        private static void StampLastExports(Document doc, ExportProfile profile, ExportRunResult result)
        {
            if (doc == null || profile?.Output == null || !profile.Output.StampLastExport) return;
            if (result == null || result.Cancelled) return;
            try
            {
                var state = LoadState(doc);
                var byKey = (state.LastExports ?? new List<SheetExportRecord>())
                    .Where(r => r != null)
                    .GroupBy(r => r.SheetUniqueId + "|" + r.Format)
                    .ToDictionary(g => g.Key, g => g.Last());   // a hand-edited file with a repeat must not stop stamping

                foreach (var r in result.Rows.Where(x => x.Success && !string.IsNullOrEmpty(x.OutputPath)))
                {
                    var sheet = ResolveSheet(doc, r.SheetId);
                    if (sheet == null) continue; // combined rows don't map to one sheet
                    var (rev, _) = GetCurrentRevision(doc, sheet);
                    var rec = new SheetExportRecord
                    {
                        SheetUniqueId = sheet.UniqueId,
                        SheetNumber   = sheet.SheetNumber,
                        Revision      = rev,
                        Format        = r.Format,
                        Path          = r.OutputPath,
                        ExportedUtc   = DateTime.UtcNow,
                    };
                    byKey[rec.SheetUniqueId + "|" + rec.Format] = rec;
                }

                state.LastExports = byKey.Values.ToList();
                SaveState(state, doc);
            }
            catch (Exception ex) { StingLog.Warn($"Last-export stamp: {ex.Message}"); }
        }

        /// <summary>
        /// DOCX-6: record each exported file in the project's document register
        /// (BIMManagerEngine.AutoRegisterExport — the same register the Document Manager
        /// and the unified register read), when Output.CdeAutoRegister is on. The setting
        /// defaulted to true and nothing read it, so Export Centre output reached the
        /// Document Manager only as loose files with no suitability or revision.
        ///
        /// A sheet's PDF is registered under its ISO document number, so it lines up with
        /// the deliverable of the same number; every other rendition (DWG, image …) is
        /// matched by file name, so it gets its own row instead of overwriting the PDF's.
        /// Suitability, revision and CDE state come from the sheet.
        /// </summary>
        private static void RegisterExports(Document doc, ExportProfile profile, ExportRunResult result)
        {
            if (doc == null || profile?.Output == null || !profile.Output.CdeAutoRegister) return;
            if (result == null || result.Cancelled) return;
            // Collected, then recorded with one register load and one save (DOCX-13):
            // per-file calls re-read and re-wrote the whole register for every file.
            var files = result.Rows.Where(x => x.Success && File.Exists(x.OutputPath ?? ""))
                .Select(r => new ExportedFile
                {
                    Sheet = ResolveSheet(doc, r.SheetId),
                    Path = r.OutputPath,
                    Format = r.Format,
                    Title = r.SheetTitle,
                    SheetNumber = r.SheetNumber,
                    Suitability = r.Suitability,
                    Revision = r.Revision,
                    DocumentNumber = r.DocumentNumber,
                    IsoFieldsUnset = r.IsoFieldsUnset,
                });
            int n = RegisterExportedFiles(doc, files);
            if (n > 0) StingLog.Info($"Export Centre: {n} file(s) recorded in the document register.");
        }

        /// <summary>One exported file, for <see cref="RegisterExportedFiles"/>.</summary>
        internal sealed class ExportedFile
        {
            public ViewSheet Sheet;
            public string Path;
            public string Format;
            public string Title;
            public string SheetNumber;
            // The ISO fields the file name printed, when the caller has them (Export Centre
            // rows). Unset = resolved from the sheet through ResolveIsoFields.
            public string Suitability;
            public string Revision;
            public string DocumentNumber;
            public System.Collections.Generic.IReadOnlyList<string> IsoFieldsUnset;
        }

        /// <summary>
        /// Record exported files in the project's document register, the way the Export
        /// Centre does: suitability, revision and CDE state from the sheet, a sheet's PDF
        /// under its ISO document number. Shared by the Export Centre and Produce &amp;
        /// Export so every exported drawing reaches the Document Manager the same way.
        /// Returns the number of rows recorded.
        /// </summary>
        internal static int RegisterExportedFiles(Document doc, IEnumerable<ExportedFile> files)
        {
            if (doc == null || files == null) return 0;
            // Collected, then recorded with one register load and one save (DOCX-13):
            // per-file calls re-read and re-wrote the whole register for every file.
            var batch = new List<BIMManager.ExportRegistration>();
            foreach (var r in files)
            {
                try
                {
                    var sheet = r.Sheet;
                    string docNumber = null, title = r.Title;
                    // Suitability / revision are the values the FILE NAME printed — a real code,
                    // or the XX / NOREV not-set marker. The Export Centre passes the row's own
                    // (AnnotateIsoFields); Produce & Export resolves them through the same chain
                    // (ResolveIsoFields). The register used to re-derive them with a different
                    // fallback (S0), so one file said S2 in its name and S0 in the register.
                    string suit = r.Suitability, revLabel = r.Revision;
                    System.Collections.Generic.IReadOnlyList<string> isoUnset = r.IsoFieldsUnset;
                    if (sheet != null && string.IsNullOrEmpty(suit))
                    {
                        var iso = ResolveIsoFields(doc, sheet);
                        suit = iso.Suitability; revLabel = iso.Revision; isoUnset = iso.Unset;
                    }
                    bool isSheetRow = sheet != null && !string.IsNullOrEmpty(suit);
                    if (sheet != null)
                    {
                        if (string.Equals(r.Format, "PDF", StringComparison.OrdinalIgnoreCase))
                        {
                            docNumber = r.DocumentNumber;
                            if (string.IsNullOrEmpty(docNumber))
                            {
                                DecomposeSheetIdentifier(sheet, out string id);
                                docNumber = id ?? sheet.SheetNumber;
                            }
                        }
                        title = $"{sheet.SheetNumber} - {sheet.Name}";
                    }
                    string state = isSheetRow ? Core.Drawing.Iso19650Suitability.CdeStateFor(suit) : null;
                    string type = r.Format is "IFC" or "NWC" ? "M3" : "DR";
                    batch.Add(new BIMManager.ExportRegistration
                    {
                        FilePath = r.Path,
                        DocType = type,
                        Description = $"{title} ({r.Format})",
                        // A non-sheet row (model export, combined PDF) keeps the register's
                        // documented WIP/S0 convention; it carries no ISO fields to flag.
                        Suitability = isSheetRow ? suit : null,
                        Revision = isSheetRow ? revLabel : null,
                        CdeStatus = state ?? "WIP",
                        DocNumber = docNumber,
                        IsoUnset = isSheetRow ? isoUnset : null,
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Export register {r.SheetNumber ?? r.Sheet?.SheetNumber}/{r.Format}: {ex.Message}"); }
            }
            return batch.Count == 0 ? 0 : BIMManager.BIMManagerEngine.AutoRegisterExports(doc, batch);
        }

        /// <summary>Resolve a ViewSheet from an ExportResultRow.SheetId string
        /// (mirrors the long→ElementId parse used by ResolveSet). Returns null for
        /// combined rows or ids that no longer resolve to a sheet.</summary>
        private static ViewSheet ResolveSheet(Document doc, string sheetId)
        {
            if (string.IsNullOrEmpty(sheetId) || !long.TryParse(sheetId, out long raw)) return null;
            return doc.GetElement(new ElementId(raw)) as ViewSheet;
        }

        /// <summary>
        /// Common cleanup path — was the body of a finally block guarded
        /// by a "Done:" label, but C# doesn't allow goto-into-finally.
        /// </summary>
        private static ExportRunResult Finalize(ExportProfile profile, ExportRunResult result)
        {
            result.FinishedUtc = DateTime.UtcNow;
            if (profile.Output.GenerateReport)
                WriteReport(profile, result);
            return result;
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
            if (string.IsNullOrEmpty(f)) return;
            if (!Path.IsPathRooted(f)) f = Path.GetFullPath(f);
            if (!Directory.Exists(f) && p.Output.CreateFolderIfMissing)
                Directory.CreateDirectory(f);
        }

        /// <summary>
        /// The folder one exported file goes to.
        ///
        /// <b>Routed</b> (Output.RouteByProjectStructure): the project's own structure —
        /// CDE state from the sheet's suitability, then the discipline's sub-folder
        /// (ProjectFolderEngine.GetDeliverableFolder). An S3 architectural sheet lands
        /// in 02_SHARED/A_Architectural (BIM layout) or 01_SHARED/Drawings/A_Architectural
        /// (CDE-first); an A1 structural sheet in the PUBLISHED equivalent.
        ///
        /// <b>Local</b>: the chosen folder, optionally split by format and discipline.
        /// The discipline split now uses the project's discipline folder name
        /// ("A_Architectural") when the project has one. It used to use the raw code,
        /// so exporting into a CDE folder created "A" beside "A_Architectural". Image,
        /// DGN and DWF exports were never split at all because they passed no
        /// discipline.
        /// </summary>
        private static string SubFolderFor(Document doc, ExportProfile p, string format,
            View view, string groupDiscipline = null)
        {
            string disc = groupDiscipline
                ?? (view is ViewSheet vs ? SheetDiscipline(vs) : null);
            if (string.Equals(disc, "Other", StringComparison.OrdinalIgnoreCase)) disc = null;

            if (p.Output.RouteByProjectStructure && doc != null)
            {
                // A sheet with no suitability code is not asserted to be anywhere in
                // particular, so it follows the project's export route (06_DRAWINGS /
                // 00_WIP|Drawings) rather than being filed as SHARED by a default.
                string state = view is ViewSheet sheet ? SheetCdeState(sheet) : null;
                string routed = ProjectFolderEngine.GetDeliverableFolder(doc, RouteKeyFor(format), disc, state);
                if (!string.IsNullOrEmpty(routed))
                {
                    if (p.Output.SplitByFormatSubFolder) routed = Path.Combine(routed, format);
                    Directory.CreateDirectory(routed);
                    return routed;
                }
                StingLog.Warn($"Export routing: no project folder for {format}/{disc}/{state} — using the local folder.");
            }

            string root = p.Output.LocalFolder;
            if (!string.IsNullOrEmpty(root) && !Path.IsPathRooted(root))
                root = Path.GetFullPath(root);
            if (p.Output.SplitByFormatSubFolder) root = Path.Combine(root, format);
            if (p.Output.SplitByDisciplineSubFolder && !string.IsNullOrEmpty(disc))
                root = Path.Combine(root, ProjectFolderEngine.ResolveDisciplineFolder(doc, disc) ?? disc);
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>Folder for one file made from several sheets (combined PDF,
        /// multi-layout DWG).
        ///
        /// The group's name is a label, not a discipline: "All" and custom group names
        /// used to be passed as the discipline, so a combined set landed in an "All"
        /// folder, and a group was filed under the first sheet's CDE state whatever
        /// the others said. The file goes in a discipline folder only when every sheet
        /// in it shares that discipline, and in a CDE state only when every sheet
        /// shares that state; otherwise it stays one level up, where it is visible.</summary>
        private static string SubFolderForGroup(Document doc, ExportProfile p, string format, List<View> views)
        {
            var sheets = (views ?? new List<View>()).OfType<ViewSheet>().ToList();
            string disc = null;
            var discs = sheets.Select(s => SheetDiscipline(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (discs.Count == 1) disc = discs[0];

            View stateFrom = null;
            var states = sheets.Select(SheetCdeState).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sheets.Count > 0 && states.Count == 1) stateFrom = sheets[0];

            // SubFolderFor reads the discipline from the view when none is passed; a
            // mixed group must not inherit the first sheet's, so pass "Other" (which
            // it treats as none) rather than null.
            return SubFolderFor(doc, p, format, stateFrom, disc ?? "Other");
        }

        /// <summary>CDE state a sheet's own suitability code puts it in, or null when
        /// the sheet carries no recognised code.</summary>
        public static string SheetCdeState(ViewSheet sheet)
            => Core.Drawing.Iso19650Suitability.CdeStateFor(SheetSuitabilityCode(sheet));

        /// <summary>The suitability code the sheet itself carries, or null.</summary>
        public static string SheetSuitabilityCode(ViewSheet sheet)
        {
            if (sheet == null) return null;
            string raw = ReadParam(sheet, "STING_SUITABILITY_TXT");
            string code = Core.Drawing.Iso19650Suitability.ExtractCode(raw);
            return !string.IsNullOrEmpty(code) ? code : ReadSuitabilityCode(sheet);
        }

        /// <summary>The project folder an exported sheet belongs in — the single rule
        /// every sheet exporter should use so the same drawing cannot land in two
        /// places depending on which button produced it: CDE state from the sheet's
        /// suitability (or the project's export route when it has none), then the
        /// discipline folder from the sheet number / ISO identifier. Null when the
        /// project has no folder structure (unsaved model).</summary>
        public static string DeliverableFolderForSheet(Document doc, ViewSheet sheet, string routeKey = "PDF")
        {
            if (doc == null || sheet == null) return null;
            string disc = SheetDiscipline(sheet);
            if (string.Equals(disc, "Other", StringComparison.OrdinalIgnoreCase)) disc = null;
            return ProjectFolderEngine.GetDeliverableFolder(doc, routeKey, disc, SheetCdeState(sheet));
        }

        /// <summary>
        /// The discipline of a whole MODEL, for IFC / NWC / gbXML exports, which have no
        /// sheet number to read one from. In order:
        ///   1. the model's ISO 19650 file name — its Role segment (the central model's
        ///      name when workshared, so a local copy's "_user" suffix does not matter);
        ///   2. PRJ_TB_DISCIPLINE_TXT on Project Information (already bound there);
        ///   3. null — the export stays in the models folder root.
        /// A multi-discipline model therefore lands in the root, as it should; nothing is
        /// guessed from its content.
        /// </summary>
        public static string ModelDiscipline(Document doc)
        {
            if (doc == null) return null;
            try
            {
                string path = doc.PathName;
                if (doc.IsWorkshared)
                {
                    var central = doc.GetWorksharingCentralModelPath();
                    if (central != null)
                        path = ModelPathUtils.ConvertModelPathToUserVisiblePath(central) ?? path;
                }
                string role = DisciplineFolderMatcher.RoleFromModelFileName(path)
                              ?? DisciplineFolderMatcher.RoleFromModelFileName(doc.Title);
                if (!string.IsNullOrEmpty(role)) return role;
            }
            catch (Exception ex) { StingLog.Warn($"ModelDiscipline file name: {ex.Message}"); }

            string declared = ReadProjectInfo(doc.ProjectInformation, ParamRegistry.TB_DISCIPLINE);
            return string.IsNullOrWhiteSpace(declared) ? null : declared.Trim();
        }

        /// <summary><see cref="DisciplineSubFolder(Document, string, ViewSheet)"/> for a
        /// code rather than a sheet — used for model exports into a chosen folder.</summary>
        public static string DisciplineSubFolder(Document doc, string baseDir, string disciplineCode)
        {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrWhiteSpace(disciplineCode)) return baseDir;
            string name = ProjectFolderEngine.ResolveDisciplineFolder(doc, disciplineCode);
            if (name == null) return baseDir;   // unknown code: never mint a raw-code folder for a model
            string leaf = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(leaf, name, StringComparison.OrdinalIgnoreCase)) return baseDir;
            string dir = Path.Combine(baseDir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>The discipline sub-folder of a folder the user chose, for one sheet —
        /// "&lt;chosen&gt;/A_Architectural" when the project has that discipline folder,
        /// else "&lt;chosen&gt;/&lt;code&gt;". Not nested again when the chosen folder already
        /// IS that discipline's folder. Sheets whose discipline cannot be read stay in
        /// the chosen folder.</summary>
        public static string DisciplineSubFolder(Document doc, string baseDir, ViewSheet sheet)
        {
            if (string.IsNullOrEmpty(baseDir) || sheet == null) return baseDir;
            string disc = SheetDiscipline(sheet);
            if (string.IsNullOrEmpty(disc) || string.Equals(disc, "Other", StringComparison.OrdinalIgnoreCase))
                return baseDir;
            string name = ProjectFolderEngine.ResolveDisciplineFolder(doc, disc) ?? disc;
            string leaf = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(leaf, name, StringComparison.OrdinalIgnoreCase)) return baseDir;
            string dir = Path.Combine(baseDir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Export-route key for a format. Sheet-based output is drawing
        /// content whatever its file type, so DWG / image / DGN / DWF sheets route
        /// with the PDFs (the project's DWG route points at MODELS, which is right for
        /// a model DWG and wrong for a sheet set). Models and data keep their own.</summary>
        private static string RouteKeyFor(string format)
        {
            switch ((format ?? "").ToUpperInvariant())
            {
                case "IFC": return "IFC";
                case "NWC": return "NWC";
                case "XML": return "SCHEDULE";
                default:    return "PDF";
            }
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
                {
                    // Track output paths already emitted this run so two sheets whose
                    // sanitised names collide can't silently overwrite one another while
                    // both report success.
                    var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var v in sheets)
                    {
                        if (cancel != null && cancel()) return;
                        ExportSinglePdf(doc, v, profile, result, emitted);
                        tick?.Invoke($"PDF: {GetLabel(v)}");
                    }
                    break;
                }

                case PdfCombineMode.OnePerDiscipline:
                {
                    var groups = sheets.OfType<ViewSheet>()
                        .GroupBy(s => SheetDiscipline(s))
                        .ToList();
                    foreach (var g in groups)
                    {
                        if (cancel != null && cancel()) return;
                        ExportCombinedPdf(doc, g.ToList<View>(), profile, result, g.Key);
                        tick?.Invoke($"PDF (combined): {g.Key}");
                    }
                    break;
                }

                case PdfCombineMode.OnePerSet:
                    ExportCombinedPdf(doc, sheets, profile, result, "All");
                    tick?.Invoke("PDF (combined): All");
                    break;

                case PdfCombineMode.CustomGroups:
                    foreach (var kv in profile.Pdf.CustomGroups)
                    {
                        var groupSheets = kv.Value
                            .Select(idText => long.TryParse(idText, out var n) ? doc.GetElement(new ElementId(n)) : null)
                            .OfType<View>().ToList();
                        if (groupSheets.Count == 0) continue;
                        ExportCombinedPdf(doc, groupSheets, profile, result, kv.Key);
                        tick?.Invoke($"PDF (custom): {kv.Key}");
                    }
                    break;
            }
        }

        private static void ExportSinglePdf(Document doc, View view, ExportProfile profile,
            ExportRunResult result, HashSet<string> emittedPaths = null)
        {
            var row = StartRow(view, "PDF");
            try
            {
                string disc = view is ViewSheet vs ? GetDisciplinePrefix(vs.SheetNumber) : "";
                string folder = SubFolderFor(doc, profile, "PDF", view);
                string stem = Sanitise(
                    ResolveNaming(doc, view, profile.Output.NamingTemplate, profile.Output),
                    profile.Output.IllegalCharReplacement);

                stem = ResolveConflict(folder, stem, "pdf", profile.Output.ConflictMode, out bool skip);
                if (skip) { row.Success = false; row.Error = "Skipped — file exists"; return; }

                string outputPath = Path.Combine(folder, stem + ".pdf");
                row.OutputPath = outputPath;

                // Within-run collision guard: two sheets whose sanitised names resolve to
                // the same file would otherwise overwrite one another and both report
                // success. Skip the duplicate and flag it rather than double-reporting.
                if (emittedPaths != null && !emittedPaths.Add(outputPath))
                {
                    row.Success = false;
                    row.Error = $"Skipped — duplicate output name '{stem}.pdf' already written in this run";
                    StingLog.Warn($"PDF export {row.SheetNumber}: duplicate output name '{stem}.pdf' within run — skipped to avoid overwrite");
                    return;
                }

                // Snapshot the folder's PDFs before export so we can locate whatever
                // Revit actually writes. PDFExportOptions.FileName honouring is version-
                // dependent: with Combine == true it is normally authoritative, but some
                // Revit builds still write a single-sheet export under a default name
                // (e.g. "<Sheet Number> - <Sheet Name>.pdf" or "Sheet-Unnamed.pdf").
                // Verifying File.Exists(<stem>.pdf) alone therefore reported perfectly
                // good exports as failures and left the file under the wrong name.
                var before = SnapshotFiles(folder, "pdf");

                var opts = new PDFExportOptions
                {
                    FileName = stem,
                    Combine = true,
                    AlwaysUseRaster = profile.Pdf.HiddenLineMode == "Raster",
                    RasterQuality = MapRasterQuality(profile.Pdf.RasterDpi),
                    ColorDepth = MapColorDepth(profile.Pdf.ColourScheme),
                };
                ApplyPdfLayout(opts, profile.Pdf);

                bool ok = doc.Export(folder, new List<ElementId> { view.Id }, opts);

                // Resolve the file Revit actually produced and move it to <stem>.pdf when
                // it landed under a different name, so the output matches the naming
                // template the user configured and the success verify is reliable.
                string produced = ResolveProducedFile(folder, outputPath, before, "pdf");
                if (produced != null && !PathsEqual(produced, outputPath))
                {
                    try
                    {
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                        File.Move(produced, outputPath);
                    }
                    catch (Exception mv)
                    {
                        StingLog.Warn($"PDF export {row.SheetNumber}: could not rename " +
                                      $"'{Path.GetFileName(produced)}' to '{stem}.pdf': {mv.Message}");
                        outputPath = produced;          // report the real path we ended up with
                        row.OutputPath = produced;
                    }
                }

                row.Success = File.Exists(row.OutputPath);
                if (row.Success)
                {
                    row.FileSizeBytes = new FileInfo(row.OutputPath).Length;

                    // Stamp the watermark on the single-sheet output too. Previously only
                    // the combined-PDF path injected it, so OnePerSheet exports came out
                    // blank even with "Apply watermark" ticked.
                    if (profile.Pdf.ApplyWatermark)
                        TryInjectWatermark(row.OutputPath, profile.Pdf, result);
                }
                else
                {
                    row.Error = ok
                        ? $"PDF export reported success but no output PDF was found in '{folder}' (expected '{stem}.pdf')"
                        : $"PDF export returned false (folder: '{folder}', file: '{stem}.pdf')";
                }
            }
            catch (Exception ex)
            {
                row.Success = false; row.Error = ex.Message;
                StingLog.Warn($"PDF export {row.SheetNumber}: {ex.Message}");
            }
            finally { CommitRow(row, result); }
        }

        /// <summary>Snapshot files of the given extension(s) in a folder (full path →
        /// last-write UTC) so a post-export diff can identify exactly what Revit
        /// produced. Extensions are given without the dot, e.g. "pdf" or "png","jpg".</summary>
        private static Dictionary<string, DateTime> SnapshotFiles(string folder, params string[] exts)
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(folder))
                {
                    var set = new HashSet<string>(exts.Select(e => "." + e.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
                    foreach (var f in Directory.EnumerateFiles(folder))
                        if (set.Contains(Path.GetExtension(f)))
                            map[f] = File.GetLastWriteTimeUtc(f);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SnapshotFiles '{folder}': {ex.Message}"); }
            return map;
        }

        /// <summary>Identify the file a single-item export produced. Prefers the
        /// expected path; otherwise returns the newest file (of the given
        /// extension(s)) that is new — or whose timestamp advanced — versus
        /// <paramref name="before"/>. Null when nothing was written.</summary>
        private static string ResolveProducedFile(string folder, string expectedPath,
            Dictionary<string, DateTime> before, params string[] exts)
        {
            try
            {
                if (File.Exists(expectedPath) &&
                    (before == null || !before.TryGetValue(expectedPath, out var prevExp) ||
                     File.GetLastWriteTimeUtc(expectedPath) > prevExp))
                    return expectedPath;

                var set = new HashSet<string>(exts.Select(e => "." + e.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
                string best = null;
                DateTime bestTime = DateTime.MinValue;
                if (Directory.Exists(folder))
                {
                    foreach (var f in Directory.EnumerateFiles(folder))
                    {
                        if (!set.Contains(Path.GetExtension(f))) continue;
                        var t = File.GetLastWriteTimeUtc(f);
                        bool changed = before == null || !before.TryGetValue(f, out var prev) || t > prev;
                        if (!changed) continue;
                        if (t >= bestTime) { bestTime = t; best = f; }
                    }
                }
                // Overwrite mode may reuse an existing file whose timestamp didn't move —
                // fall back to the expected path if it is present.
                if (best == null && File.Exists(expectedPath)) return expectedPath;
                return best;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveProducedFile '{folder}': {ex.Message}");
                return File.Exists(expectedPath) ? expectedPath : null;
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private static void ExportCombinedPdf(Document doc, List<View> views,
            ExportProfile profile, ExportRunResult result, string groupName)
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
                string folder = SubFolderForGroup(doc, profile, "PDF", views);
                string stem = Sanitise(
                    ResolveNaming(doc, views[0], profile.Output.NamingTemplate, profile.Output) + "_" + groupName,
                    profile.Output.IllegalCharReplacement);
                stem = ResolveConflict(folder, stem, "pdf", profile.Output.ConflictMode, out bool skip);
                if (skip) { row.Success = false; row.Error = "Skipped — file exists"; return; }

                var ordered = OrderForMerge(views, profile.Pdf.MergeOrderSheetIds);

                var opts = new PDFExportOptions
                {
                    FileName = stem,
                    Combine = true,
                    AlwaysUseRaster = profile.Pdf.HiddenLineMode == "Raster",
                    RasterQuality = MapRasterQuality(profile.Pdf.RasterDpi),
                    ColorDepth = MapColorDepth(profile.Pdf.ColourScheme),
                };
                ApplyPdfLayout(opts, profile.Pdf);

                bool ok = doc.Export(folder, ordered.Select(v => v.Id).ToList(), opts);
                row.OutputPath = Path.Combine(folder, stem + ".pdf");
                row.Success = ok && File.Exists(row.OutputPath);
                if (row.Success)
                {
                    row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                    if (profile.Pdf.AddBookmarks)
                        TryInjectBookmarks(row.OutputPath, ordered, profile, result);
                    if (profile.Pdf.ApplyWatermark)
                        TryInjectWatermark(row.OutputPath, profile.Pdf, result);
                }
                else row.Error = "Combined PDF export returned false";
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

        private static List<View> OrderForMerge(List<View> views, List<string> mergeOrder)
        {
            if (mergeOrder == null || mergeOrder.Count == 0)
                return views.OrderBy(v => v is ViewSheet s ? s.SheetNumber : v.Name).ToList();

            var index = mergeOrder.Select((id, i) => new { id, i }).ToDictionary(x => x.id, x => x.i);
            return views.OrderBy(v => index.TryGetValue(v.Id.ToString(), out int i) ? i : int.MaxValue).ToList();
        }

        private static void TryInjectBookmarks(string pdfPath, List<View> views,
            ExportProfile profile, ExportRunResult result)
        {
            // Backed by PDFsharp 6.x (added as a NuGet package). Best-effort —
            // a bookmark failure must not abort the export.
            try { ExportCenterPdfPostProcess.InjectBookmarks(pdfPath, views, profile); }
            catch (Exception ex)
            {
                result.Warnings.Add($"Bookmark injection failed for '{Path.GetFileName(pdfPath)}': {ex.Message}");
            }
        }

        private static void TryInjectWatermark(string pdfPath, PdfExportSettings pdf, ExportRunResult result)
        {
            try { ExportCenterPdfPostProcess.InjectWatermark(pdfPath, pdf); }
            catch (Exception ex)
            {
                result.Warnings.Add($"Watermark injection failed for '{Path.GetFileName(pdfPath)}': {ex.Message}");
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

        /// <summary>Paper placement and zoom from the profile (DOCX-4 — both were saved
        /// and ignored, so every PDF came out centred at fit-to-page).</summary>
        private static void ApplyPdfLayout(PDFExportOptions opts, PdfExportSettings pdf)
        {
            try
            {
                if (string.Equals(pdf.PaperPlacement, "Offset", StringComparison.OrdinalIgnoreCase))
                {
                    opts.PaperPlacement = PaperPlacementType.LowerLeft;
                    opts.OriginOffsetX = pdf.OffsetXmm / 304.8;   // API stores feet
                    opts.OriginOffsetY = pdf.OffsetYmm / 304.8;
                }
                else opts.PaperPlacement = PaperPlacementType.Center;

                if (string.Equals(pdf.Zoom, "Percent", StringComparison.OrdinalIgnoreCase))
                {
                    opts.ZoomType = ZoomType.Zoom;
                    opts.ZoomPercentage = Math.Max(10, Math.Min(500, pdf.ZoomPercent));
                }
                else opts.ZoomType = ZoomType.FitToPage;
            }
            catch (Exception ex) { StingLog.Warn($"PDF layout options: {ex.Message}"); }
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

            // MergedViews = true makes each exported sheet a SINGLE self-contained DWG with
            // every view merged into its model space. Left at Revit's default of false, each
            // view placed on a sheet is written out as its OWN extra sidecar .dwg next to the
            // real one and referenced as an XREF — so exporting 2 sheets could litter the
            // output folder with a dozen files like
            // "<sheet>-Drafting View - SITE LOCATION PLAN.dwg", which reads as the exporter
            // ignoring the selection and dumping every view. It also silently broke the
            // multi-layout merge, whose per-sheet sources live in a temp staging folder: the
            // merged output carried unresolvable xref pointers back into it, so model space
            // showed only "Xref <temp path>" placeholder text with X1..Xn listed "Not Found".
            // BUT leaving it false is what makes Revit's own sheets correct, and that matters
            // more. With MergedViews = false Revit writes one xref per view, all sitting at the
            // model origin, and isolates them with per-viewport layer freezes — which is why
            // every exported viewport's Target is (0,0,0) and why the sheets look right.
            // Setting it true merges the views out across model space while the viewports still
            // target the origin, so the framing no longer matches the geometry. The sidecar
            // files are the price of Revit-accurate sheets, so OnePerSheet keeps the default.
            // Only ModelSpaceOnly — which explicitly wants geometry and no paper space — merges.
            opts.MergedViews = profile.Dwg.OutputMode == DwgOutputMode.ModelSpaceOnly;
            opts.FileVersion = MapDwgVersion(profile.Dwg.DwgVersion);

            // Coordinates and layers (DOCX-4: both were saved and ignored).
            opts.SharedCoords = string.Equals(profile.Dwg.CoordinateSystem, "Shared", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(profile.Dwg.CoordinateSystem, "Survey", StringComparison.OrdinalIgnoreCase);
            try
            {
                switch ((profile.Dwg.LayerMappingMode ?? "").Trim().ToUpperInvariant())
                {
                    case "STANDARD":
                        if (!string.IsNullOrWhiteSpace(profile.Dwg.LayerStandard))
                            opts.LayerMapping = profile.Dwg.LayerStandard.Trim();
                        break;
                    case "CUSTOM":
                        if (File.Exists(profile.Dwg.LayerCustomMappingFile ?? ""))
                            opts.LayerMapping = profile.Dwg.LayerCustomMappingFile;
                        else
                            StingLog.Warn($"DWG layer mapping file not found: '{profile.Dwg.LayerCustomMappingFile}' — using the export setup's layers.");
                        break;
                    // ByCategory: keep the export setup's own mapping.
                }
            }
            catch (Exception ex) { StingLog.Warn($"DWG layer mapping: {ex.Message}"); }
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
                string folder = SubFolderFor(doc, profile, "DWG", view);
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
                string outFolder = SubFolderForGroup(doc, profile, "DWG", sheets);
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
                    // Merge() can succeed while having skipped one or more sheets whose page
                    // setup/plot device it couldn't resolve — surface that instead of reporting
                    // a silent full success (this is how sheets used to come out at the wrong
                    // paper size, or missing entirely, without any visible warning).
                    if (ExportCenterDwgMerger.LastWarning != null)
                        result.Warnings.Add($"DWG multi-layout merge for '{groupName}': {ExportCenterDwgMerger.LastWarning}");

                    // Only delete the staging folder once every xref is confirmed bound —
                    // an unbound xref is still a LIVE external reference into this folder, and
                    // deleting it out from under the saved file is exactly how sheets came back
                    // showing as "Not Found" when reopened.
                    if (ExportCenterDwgMerger.AllXrefsBound)
                        try { Directory.Delete(temp, true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    else
                        result.Warnings.Add($"DWG multi-layout merge for '{groupName}': kept the staging folder at '{temp}' " +
                                            "because not every xref bound successfully — deleting it would have broken those " +
                                            "references in the saved file.");
                }
                else
                {
                    if (ExportCenterDwgMerger.LastWarning != null)
                        result.Warnings.Add($"DWG multi-layout merge for '{groupName}': {ExportCenterDwgMerger.LastWarning}");

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
                string folder = SubFolderFor(doc, profile, "IFC", null, ModelDiscipline(doc));
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
                // Coordinate base (DOCX-4). The IFC exporter's SitePlacement option:
                // 0 shared (survey), 2 project base point, 3 internal origin.
                string site = (profile.Ifc.CoordinateOrigin ?? "Project").Trim().ToUpperInvariant() switch
                {
                    "SURVEY" or "SHARED" => "0",
                    "INTERNAL" => "3",
                    _ => "2",
                };
                opts.AddOption("SitePlacement", site);

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
                    string folder = SubFolderFor(doc, profile, "Image", v);
                    string stem = Sanitise(
                        ResolveNaming(doc, v, profile.Output.NamingTemplate, profile.Output),
                        profile.Output.IllegalCharReplacement);
                    string path = Path.Combine(folder, stem + "." + profile.Image.Format.ToLowerInvariant());
                    row.OutputPath = path;

                    // Revit's ExportImage appends the view/sheet name to FilePath for a
                    // SetOfViews export and writes .jpg for JPEG, so the image rarely lands
                    // at <stem>.<ext> — the same FileName-not-honoured class as single-sheet
                    // PDF. Snapshot the folder, export, then move the produced image into
                    // place so the name matches the template and success is reliable.
                    string[] imgExts = { "png", "jpg", "jpeg", "tif", "tiff", "bmp" };
                    var before = SnapshotFiles(folder, imgExts);

                    var io = new ImageExportOptions
                    {
                        FilePath = path,
                        ZoomType = ZoomFitType.FitToPage,
                        PixelSize = 2400,
                        ImageResolution = MapImageDpi(profile.Image.Dpi),
                        ExportRange = ExportRange.SetOfViews,
                        HLRandWFViewsFileType = MapImageType(profile.Image.Format, profile.Image.JpegQuality),
                        ShadowViewsFileType = MapImageType(profile.Image.Format, profile.Image.JpegQuality),
                    };
                    io.SetViewsAndSheets(new List<ElementId> { v.Id });
                    doc.ExportImage(io);

                    string produced = ResolveProducedFile(folder, path, before, imgExts);
                    if (produced != null && !PathsEqual(produced, path))
                    {
                        try
                        {
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(produced, path);
                        }
                        catch (Exception mv)
                        {
                            StingLog.Warn($"Image export {GetLabel(v)}: could not rename " +
                                          $"'{Path.GetFileName(produced)}' to '{Path.GetFileName(path)}': {mv.Message}");
                            row.OutputPath = produced;
                        }
                    }

                    row.Success = File.Exists(row.OutputPath);
                    if (row.Success) row.FileSizeBytes = new FileInfo(row.OutputPath).Length;
                    else row.Error = "Image export produced no file";
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

        private static ImageFileType MapImageType(string fmt, int jpegQuality = 100) => fmt.ToUpperInvariant() switch
        {
            "JPEG" => jpegQuality >= 90 ? ImageFileType.JPEGLossless
                    : jpegQuality >= 60 ? ImageFileType.JPEGMedium
                    : ImageFileType.JPEGSmallest,
            "TIFF" => ImageFileType.TIFF,
            _      => ImageFileType.PNG,
        };

        private static void RunDgn(Document doc, ExportProfile profile, List<ElementId> ids,
            ExportRunResult result, Action<string> tick, Func<bool> cancel)
        {
            var opts = new DGNExportOptions();
            opts.FileVersion = string.Equals(profile.Dgn?.Version, "V7", StringComparison.OrdinalIgnoreCase)
                ? DGNFileFormat.DGNVersion7 : DGNFileFormat.DGNVersion8;
            foreach (var eid in ids)
            {
                if (cancel != null && cancel()) return;
                if (doc.GetElement(eid) is not View v) continue;
                var row = StartRow(v, "DGN");
                try
                {
                    string folder = SubFolderFor(doc, profile, "DGN", v);
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
                    string folder = SubFolderFor(doc, profile, profile.Dwf.DwfX ? "DWFx" : "DWF", v);
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
                        var dx = new DWFXExportOptions { ExportingAreas = profile.Dwf.IncludeRoomBoundaries };
                        ok = doc.Export(folder, stem, vs, dx);
                    }
                    else
                    {
                        var dw = new DWFExportOptions { ExportingAreas = profile.Dwf.IncludeRoomBoundaries };
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

                string folder = SubFolderFor(doc, profile, "NWC", null, ModelDiscipline(doc));
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
                string folder = SubFolderFor(doc, profile, "XML", null);
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
                    // Ask. Nothing ever prompted: the engine is headless and the dialog
                    // never overrode the stem, so "Ask" silently OVERWROTE the existing
                    // file — the one outcome a user choosing "Ask" had ruled out. Until a
                    // prompt exists, keep both files, which loses nothing.
                    goto case FilenameConflictMode.AutoRename;
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
                string[] header = { "Format", "SheetNumber", "SheetTitle", "OutputPath", "Bytes", "Success", "Error", "DurationMs",
                                    "Suitability", "Revision", "IsoFieldsNotSet", "AccUpload" };
                bool xlsx = string.Equals(profile.Output.ReportFormat, "XLSX", StringComparison.OrdinalIgnoreCase);
                string path = Path.Combine(folder, $"STING_Export_Report_{stamp}.{(xlsx ? "xlsx" : "csv")}");

                if (xlsx)
                {
                    // ReportFormat defaulted to XLSX and was ignored — every report was CSV.
                    using var wb = new ClosedXML.Excel.XLWorkbook();
                    var ws = wb.Worksheets.Add("Export");
                    for (int c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];
                    int rowIx = 2;
                    foreach (var r in result.Rows)
                    {
                        ws.Cell(rowIx, 1).Value = r.Format ?? "";
                        ws.Cell(rowIx, 2).Value = r.SheetNumber ?? "";
                        ws.Cell(rowIx, 3).Value = r.SheetTitle ?? "";
                        ws.Cell(rowIx, 4).Value = r.OutputPath ?? "";
                        ws.Cell(rowIx, 5).Value = r.FileSizeBytes;
                        ws.Cell(rowIx, 6).Value = r.Success ? "OK" : "FAILED";
                        ws.Cell(rowIx, 7).Value = r.Error ?? "";
                        ws.Cell(rowIx, 8).Value = (long)r.Duration.TotalMilliseconds;
                        ws.Cell(rowIx, 9).Value = r.Suitability ?? "";
                        ws.Cell(rowIx, 10).Value = r.Revision ?? "";
                        ws.Cell(rowIx, 11).Value = string.Join(" + ", r.IsoFieldsUnset ?? new List<string>());
                        ws.Cell(rowIx, 12).Value = r.AccUpload ?? "";
                        rowIx++;
                    }
                    ws.Row(1).Style.Font.Bold = true;
                    ws.SheetView.FreezeRows(1);
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(path);
                }
                else
                {
                    using var w = new StreamWriter(path);
                    w.WriteLine(string.Join(",", header));
                    foreach (var r in result.Rows)
                    {
                        w.WriteLine(string.Join(",", new[]
                        {
                            Csv(r.Format), Csv(r.SheetNumber), Csv(r.SheetTitle),
                            Csv(r.OutputPath), r.FileSizeBytes.ToString(),
                            r.Success ? "1" : "0", Csv(r.Error),
                            ((long)r.Duration.TotalMilliseconds).ToString(),
                            Csv(r.Suitability), Csv(r.Revision),
                            Csv(string.Join(" + ", r.IsoFieldsUnset ?? new List<string>())), Csv(r.AccUpload),
                        }));
                    }
                }
                result.ReportPath = path;
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
