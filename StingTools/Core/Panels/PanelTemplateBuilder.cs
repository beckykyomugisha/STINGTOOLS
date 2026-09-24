// PanelTemplateBuilder — builds / rebuilds a Revit PanelScheduleTemplate from a
// PanelTemplateSpec (STING_PANEL_SCHEDULE_SPECS.json).
//
// The Revit API has created templates since PanelScheduleTemplate.Create and
// edits them through PanelScheduleTemplate.GetTableData / SetTableData and
// TableSectionData (insert/remove rows and columns, bind a cell to a
// parameter, set text and widths). The internal row/column layout of a fresh
// template is not documented, so the builder ADAPTS to what it finds and then
// READS BACK every cell it wrote from a fresh GetTableData() after SetTableData.
// Anything that did not land is reported by name — a template that looks
// finished but silently lost its columns is the failure this avoids.
//
// Run PanelTemplateInspect on any template to see its exact structure.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Panels
{
    public sealed class PanelTemplateBuildResult
    {
        public string Name = "";
        public bool Created;
        public bool Failed;
        public string FailReason = "";
        public int CellsRequested;
        public int CellsVerified;
        public List<string> Problems = new List<string>();
        public List<string> Notes = new List<string>();
    }

    public static class PanelTemplateBuilder
    {
        public const string SpecFileName = "STING_PANEL_SCHEDULE_SPECS.json";
        public const string ProjectOverrideFileName = "panel_schedule_specs.json";

        /// <summary>Corporate specs with the project override (by template name) merged over them.</summary>
        public static PanelTemplateSpecSet LoadSpecs(Document doc, List<string> warnings)
        {
            PanelTemplateSpecSet corporate = null, project = null;
            try
            {
                string path = StingToolsApp.FindDataFile(SpecFileName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    warnings.Add($"{SpecFileName} not found in the data folder.");
                else corporate = PanelTemplateSpecSet.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) { warnings.Add($"{SpecFileName}: {ex.Message}"); StingLog.Warn($"Panel specs: {ex.Message}"); }

            try
            {
                string over = doc != null ? StingPaths.MetaFile(doc, "_BIM_COORD", ProjectOverrideFileName) : null;
                if (!string.IsNullOrEmpty(over) && File.Exists(over))
                {
                    project = PanelTemplateSpecSet.Parse(File.ReadAllText(over));
                    warnings.Add($"Project override applied: {over}");
                }
            }
            catch (Exception ex) { warnings.Add($"Project override {ProjectOverrideFileName}: {ex.Message}"); StingLog.Warn($"Panel spec override: {ex.Message}"); }

            var merged = PanelTemplateSpecSet.Merge(corporate, project);
            foreach (var p in merged.Validate()) warnings.Add("Spec: " + p);
            return merged;
        }

        public static PanelScheduleTemplate FindTemplate(Document doc, string name)
            => new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleTemplate))
                   .Cast<PanelScheduleTemplate>()
                   .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Create the template if missing, then (re)write its header, circuit table and
        /// footer from the spec. Must run inside an open Transaction; throws on a hard
        /// failure so the caller can roll back just this template.
        /// </summary>
        public static PanelTemplateBuildResult BuildOrUpdate(Document doc, PanelTemplateSpec spec)
        {
            var res = new PanelTemplateBuildResult { Name = spec.Name };

            if (!Enum.TryParse(spec.ScheduleType, out PanelScheduleType type) || type == PanelScheduleType.Unknown)
            { res.Failed = true; res.FailReason = $"unknown scheduleType '{spec.ScheduleType}'"; return res; }
            if (!Enum.TryParse(spec.Configuration, out PanelConfiguration config))
            { res.Failed = true; res.FailReason = $"unknown configuration '{spec.Configuration}'"; return res; }

            var template = FindTemplate(doc, spec.Name);
            if (template == null)
            {
                if (!PanelScheduleTemplate.IsValidPanelConfiguration(type, config))
                { res.Failed = true; res.FailReason = $"Revit does not allow {config} for a {type} schedule"; return res; }
                template = PanelScheduleTemplate.Create(doc, type, config, spec.Name);
                res.Created = true;
            }
            else if (template.GetPanelScheduleType() != type)
            {
                res.Failed = true;
                res.FailReason = $"a template named '{spec.Name}' already exists as a {template.GetPanelScheduleType()} schedule — rename or delete it";
                return res;
            }

            var ctx = new Ctx(doc, res);
            PanelScheduleData data = template.GetTableData();
            var expected = new List<(SectionType sec, int row, int col, ElementId pid, string text)>();

            BuildHeader(ctx, data.GetSectionData(SectionType.Header), spec, expected);
            BuildBody(ctx, data.GetSectionData(SectionType.Body), spec, expected);
            BuildFooter(ctx, data.GetSectionData(SectionType.Footer), spec, expected);

            template.SetTableData(data);

            // Verify against a FRESH read: what SetTableData actually kept.
            var fresh = template.GetTableData();
            res.CellsRequested = expected.Count;
            foreach (var e in expected)
            {
                try
                {
                    var s = fresh.GetSectionData(e.sec);
                    bool ok = e.pid != null
                        ? s.GetCellParamId(e.row, e.col) == e.pid
                        : string.Equals(s.GetCellText(e.row, e.col) ?? "", e.text ?? "", StringComparison.Ordinal);
                    if (ok) res.CellsVerified++;
                    else res.Problems.Add($"{e.sec} r{e.row} c{e.col}: '{(e.pid != null ? ctx.NameOf(e.pid) : e.text)}' did not persist");
                }
                catch (Exception ex) { res.Problems.Add($"{e.sec} r{e.row} c{e.col}: read-back failed ({ex.Message})"); }
            }
            return res;
        }

        // ── Sections ─────────────────────────────────────────────────────

        // Header: title row, then label/value pairs two per row (4 columns).
        private static void BuildHeader(Ctx ctx, TableSectionData s, PanelTemplateSpec spec,
            List<(SectionType, int, int, ElementId, string)> expected)
        {
            if (s == null) { ctx.Res.Problems.Add("template has no header section"); return; }
            int pairs = spec.Header.Count;
            int rowsNeeded = 1 + (pairs + 1) / 2;
            ResizeGrid(ctx, s, rowsNeeded, 4, "header");
            ClearAll(s);
            int r0 = s.FirstRowNumber, c0 = s.FirstColumnNumber;

            if (!string.IsNullOrWhiteSpace(spec.Title) && WriteText(ctx, s, r0, c0, spec.Title, "header title"))
                expected.Add((SectionType.Header, r0, c0, null, spec.Title));

            for (int i = 0; i < pairs; i++)
            {
                var f = spec.Header[i];
                int r = r0 + 1 + i / 2, cl = c0 + (i % 2) * 2;
                if (!Fits(s, r, cl + 1)) { ctx.Res.Problems.Add($"header: no room for '{f.Label}'"); continue; }
                if (WriteText(ctx, s, r, cl, f.Label, "header label"))
                    expected.Add((SectionType.Header, r, cl, null, f.Label));
                var cat = ctx.CategoryFor(f, BuiltInCategory.OST_ElectricalEquipment);
                var pid = ctx.Resolve(f, "header");
                if (pid != null && WriteParam(ctx, s, r, cl + 1, pid, cat, $"header '{f.Label}'"))
                    expected.Add((SectionType.Header, r, cl + 1, pid, null));
            }
        }

        // Circuit table: one column per spec field. The row that already carries
        // parameter bindings is the circuit row; the row above it, if any, holds
        // the column headings. Every other circuit-template row is bound the same.
        private static void BuildBody(Ctx ctx, TableSectionData s, PanelTemplateSpec spec,
            List<(SectionType, int, int, ElementId, string)> expected)
        {
            if (s == null) { ctx.Res.Problems.Add("template has no circuit table (body) section"); return; }
            int want = spec.Body.Count;
            ResizeColumns(ctx, s, want, "circuit table");

            var paramRows = new List<int>();
            for (int r = s.FirstRowNumber; r <= s.LastRowNumber; r++)
                for (int c = s.FirstColumnNumber; c <= s.LastColumnNumber; c++)
                {
                    ElementId id = null;
                    try { id = s.GetCellParamId(r, c); } catch (Exception ex) { StingLog.Warn($"Body scan: {ex.Message}"); }
                    if (id != null && id != ElementId.InvalidElementId) { paramRows.Add(r); break; }
                }
            if (paramRows.Count == 0) paramRows.Add(s.FirstRowNumber);
            int headingRow = paramRows[0] > s.FirstRowNumber ? paramRows[0] - 1 : -1;
            if (headingRow < 0)
                ctx.Res.Notes.Add("Circuit table has no heading row above the circuit row — column headings could not be written; check with Inspect.");

            int cols = Math.Min(want, s.NumberOfColumns);
            if (cols < want)
                ctx.Res.Problems.Add($"circuit table: only {cols} of {want} columns available — {string.Join(", ", spec.Body.Skip(cols).Select(f => f.Heading))} not placed");

            for (int i = 0; i < cols; i++)
            {
                var f = spec.Body[i];
                int c = s.FirstColumnNumber + i;
                var cat = ctx.CategoryFor(f, BuiltInCategory.OST_ElectricalCircuit);
                var pid = ctx.Resolve(f, "circuit table");
                if (pid != null)
                    foreach (int r in paramRows)
                        if (WriteParam(ctx, s, r, c, pid, cat, $"column '{f.Heading}'"))
                            expected.Add((SectionType.Body, r, c, pid, null));
                if (headingRow >= 0 && WriteText(ctx, s, headingRow, c, f.Heading, "column heading"))
                    expected.Add((SectionType.Body, headingRow, c, null, f.Heading));
                if (f.WidthMm > 0)
                {
                    try { s.SetColumnWidth(c, f.WidthMm / 304.8); }
                    catch (Exception ex) { ctx.Res.Notes.Add($"width of '{f.Heading}' not set: {ex.Message}"); }
                }
            }
        }

        // Footer: totals as label/value pairs, then one row per note.
        private static void BuildFooter(Ctx ctx, TableSectionData s, PanelTemplateSpec spec,
            List<(SectionType, int, int, ElementId, string)> expected)
        {
            if (s == null)
            {
                if (spec.Footer.Count + spec.Notes.Count > 0) ctx.Res.Problems.Add("template has no footer section");
                return;
            }
            int pairRows = (spec.Footer.Count + 1) / 2;
            int rowsNeeded = Math.Max(1, pairRows + spec.Notes.Count);
            ResizeGrid(ctx, s, rowsNeeded, 4, "footer");
            ClearAll(s);
            int r0 = s.FirstRowNumber, c0 = s.FirstColumnNumber;

            for (int i = 0; i < spec.Footer.Count; i++)
            {
                var f = spec.Footer[i];
                int r = r0 + i / 2, cl = c0 + (i % 2) * 2;
                if (!Fits(s, r, cl + 1)) { ctx.Res.Problems.Add($"footer: no room for '{f.Label}'"); continue; }
                if (WriteText(ctx, s, r, cl, f.Label, "footer label"))
                    expected.Add((SectionType.Footer, r, cl, null, f.Label));
                var cat = ctx.CategoryFor(f, BuiltInCategory.OST_ElectricalEquipment);
                var pid = ctx.Resolve(f, "footer");
                if (pid != null && WriteParam(ctx, s, r, cl + 1, pid, cat, $"footer '{f.Label}'"))
                    expected.Add((SectionType.Footer, r, cl + 1, pid, null));
            }
            for (int n = 0; n < spec.Notes.Count; n++)
            {
                int r = r0 + pairRows + n;
                if (!Fits(s, r, c0)) { ctx.Res.Problems.Add("footer: no room for notes"); break; }
                if (WriteText(ctx, s, r, c0, spec.Notes[n], "footer note"))
                    expected.Add((SectionType.Footer, r, c0, null, spec.Notes[n]));
            }
        }

        // ── Grid helpers ─────────────────────────────────────────────────

        private static bool Fits(TableSectionData s, int r, int c)
            => r >= s.FirstRowNumber && r <= s.LastRowNumber && c >= s.FirstColumnNumber && c <= s.LastColumnNumber;

        private static void ResizeGrid(Ctx ctx, TableSectionData s, int rows, int cols, string where)
        {
            ResizeColumns(ctx, s, cols, where);
            int guard = 0;
            while (s.NumberOfRows < rows && guard++ < 200)
            {
                int at = s.LastRowNumber + 1;
                if (s.CanInsertRow(at)) s.InsertRow(at);
                else if (s.CanInsertRow(s.LastRowNumber)) s.InsertRow(s.LastRowNumber);
                else { ctx.Res.Problems.Add($"{where}: cannot add rows (have {s.NumberOfRows}, need {rows})"); break; }
            }
            guard = 0;
            while (s.NumberOfRows > rows && guard++ < 200 && s.CanRemoveRow(s.LastRowNumber))
                s.RemoveRow(s.LastRowNumber);
        }

        private static void ResizeColumns(Ctx ctx, TableSectionData s, int cols, string where)
        {
            int guard = 0;
            while (s.NumberOfColumns < cols && guard++ < 200)
            {
                int at = s.LastColumnNumber + 1;
                if (s.CanInsertColumn(at)) s.InsertColumn(at);
                else if (s.CanInsertColumn(s.LastColumnNumber)) s.InsertColumn(s.LastColumnNumber);
                else { ctx.Res.Problems.Add($"{where}: cannot add columns (have {s.NumberOfColumns}, need {cols})"); break; }
            }
            guard = 0;
            while (s.NumberOfColumns > cols && guard++ < 200 && s.CanRemoveColumn(s.LastColumnNumber))
                s.RemoveColumn(s.LastColumnNumber);
            if (s.NumberOfColumns > cols)
                ctx.Res.Notes.Add($"{where}: {s.NumberOfColumns - cols} extra column(s) Revit would not remove were left as they are.");
        }

        private static void ClearAll(TableSectionData s)
        {
            for (int r = s.FirstRowNumber; r <= s.LastRowNumber; r++)
                for (int c = s.FirstColumnNumber; c <= s.LastColumnNumber; c++)
                {
                    try { s.ClearCell(r, c); }
                    catch (Exception ex) { StingLog.Info($"ClearCell r{r} c{c}: {ex.Message}"); }
                }
        }

        private static bool WriteText(Ctx ctx, TableSectionData s, int r, int c, string text, string what)
        {
            try
            {
                // Change the cell type only when it differs; a failure here fails the
                // write (reported) rather than being swallowed.
                if (s.GetCellType(r, c) != CellType.Text) s.SetCellType(r, c, CellType.Text);
                s.SetCellText(r, c, text ?? "");
                return true;
            }
            catch (Exception ex) { ctx.Res.Problems.Add($"{what} '{text}': {ex.Message}"); return false; }
        }

        private static bool WriteParam(Ctx ctx, TableSectionData s, int r, int c, ElementId pid, ElementId cat, string what)
        {
            try
            {
                if (!s.IsAcceptableParamIdAndCategoryId(pid, cat))
                {
                    ctx.Res.Problems.Add($"{what}: Revit does not accept '{ctx.NameOf(pid)}' here");
                    return false;
                }
                if (s.GetCellType(r, c) != CellType.Parameter) s.SetCellType(r, c, CellType.Parameter);
                s.SetCellParamIdAndCategoryId(r, c, pid, cat);
                return true;
            }
            catch (Exception ex) { ctx.Res.Problems.Add($"{what} '{ctx.NameOf(pid)}': {ex.Message}"); return false; }
        }

        // ── Inspect ──────────────────────────────────────────────────────

        /// <summary>Every cell of every section: section, row, column, type, text, parameter.</summary>
        public static List<string[]> Dump(Document doc, PanelScheduleTemplate template)
        {
            var rows = new List<string[]>();
            var data = template.GetTableData();
            foreach (SectionType st in new[] { SectionType.Header, SectionType.Body, SectionType.Summary, SectionType.Footer })
            {
                TableSectionData s = null;
                try { s = data.GetSectionData(st); } catch (Exception ex) { StingLog.Info($"Dump {st}: {ex.Message}"); }
                if (s == null) { rows.Add(new[] { st.ToString(), "", "", "(no section)", "", "" }); continue; }
                for (int r = s.FirstRowNumber; r <= s.LastRowNumber; r++)
                    for (int c = s.FirstColumnNumber; c <= s.LastColumnNumber; c++)
                    {
                        string type = "", text = "", param = "";
                        try { type = s.GetCellType(r, c).ToString(); } catch (Exception ex) { type = "?" + ex.Message; }
                        try { text = s.GetCellText(r, c) ?? ""; } catch (Exception ex) { StingLog.Info($"Dump text: {ex.Message}"); }
                        try
                        {
                            var id = s.GetCellParamId(r, c);
                            if (id != null && id != ElementId.InvalidElementId) param = ParamName(doc, id);
                        }
                        catch (Exception ex) { StingLog.Info($"Dump param: {ex.Message}"); }
                        rows.Add(new[] { st.ToString(), r.ToString(), c.ToString(), type, text, param });
                    }
            }
            return rows;
        }

        internal static string ParamName(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return "";
            if (id.Value < 0)
            {
                try { return "bip:" + ((BuiltInParameter)id.Value); }
                catch (Exception ex) { StingLog.Info($"ParamName: {ex.Message}"); return id.Value.ToString(); }
            }
            return doc.GetElement(id)?.Name ?? id.Value.ToString();
        }

        // ── Resolution context ───────────────────────────────────────────

        private sealed class Ctx
        {
            public readonly Document Doc;
            public readonly PanelTemplateBuildResult Res;
            private Dictionary<string, ElementId> _shared;

            public Ctx(Document doc, PanelTemplateBuildResult res) { Doc = doc; Res = res; }

            public ElementId Resolve(PanelTemplateField f, string where)
            {
                string name = f.ParamName;
                if (f.IsBuiltIn)
                {
                    if (Enum.TryParse(name, out BuiltInParameter bip) && bip != BuiltInParameter.INVALID)
                        return new ElementId(bip);
                    Res.Problems.Add($"{where}: '{name}' is not a Revit built-in parameter");
                    return null;
                }
                _shared ??= new FilteredElementCollector(Doc).OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>()
                    .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
                if (_shared.TryGetValue(name, out var id)) return id;
                Res.Problems.Add($"{where}: shared parameter '{name}' is not in this project — run Load Params, then re-run");
                return null;
            }

            public ElementId CategoryFor(PanelTemplateField f, BuiltInCategory fallback)
            {
                if (string.Equals(f.Category, "ProjectInformation", StringComparison.OrdinalIgnoreCase))
                    return new ElementId(BuiltInCategory.OST_ProjectInformation);
                return new ElementId(fallback);
            }

            public string NameOf(ElementId id) => ParamName(Doc, id);
        }
    }
}
