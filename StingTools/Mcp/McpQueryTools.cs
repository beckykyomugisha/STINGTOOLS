// ════════════════════════════════════════════════════════════════════════════
// McpQueryTools — Tier 1 generic read tools (Phase 2)
//
// Every tool marshals onto the Revit API thread via McpJobBridge.Run, re-checks
// the license gate then the open document (McpSafety), opens no modal UI, and
// mutates nothing (set_selection is the sole, non-destructive state change and
// needs no transaction). Large readers (query_elements, get_schedule_data,
// get_tag_status) summarize + paginate — they never dump raw rows.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.CableSizer;
using StingTools.Core;
using StingTools.Core.Validation;

namespace StingTools.Mcp
{
    internal static class McpQueryTools
    {
        // ── size_cable_calc (pure BS 7671 / NEC calc; no Revit model) ────────────

        public static McpCallResult SizeCableCalc(JObject args)
        {
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic.ToCallResult();

            args = args ?? new JObject();
            var input = new CableSizeInput
            {
                LoadKW         = args["loadKW"]?.Value<double?>() ?? 0,
                VoltageV       = args["voltageV"]?.Value<double?>() ?? 230.0,
                PowerFactor    = args["powerFactor"]?.Value<double?>() ?? 0.85,
                LengthM        = args["lengthM"]?.Value<double?>() ?? 0,
                InstallMethod  = args["installMethod"]?.Value<string>() ?? "C",
                Material       = args["material"]?.Value<string>() ?? "Cu",
                Insulation     = args["insulation"]?.Value<string>() ?? "XLPE90",
                VDLimitPct     = args["vdLimitPct"]?.Value<double?>() ?? 3.0,
                Standard       = args["standard"]?.Value<string>() ?? "BS7671",
                Phases         = args["phases"]?.Value<int?>() ?? 1,
                AmbientTempC   = args["ambientTempC"]?.Value<double?>() ?? 30.0,
                ContinuousLoad = args["continuousLoad"]?.Value<bool?>() ?? false,
            };

            if (input.LoadKW <= 0 || input.LengthM <= 0)
                return McpJobResult.Error("bad_args",
                    "loadKW and lengthM are required and must be > 0.").ToCallResult();

            CableSizeResult r = CableSizerEngine.Calculate(input);
            var data = new Dictionary<string, object>
            {
                ["designCurrentA"]    = Math.Round(r.DesignCurrentA, 1),
                ["recommendedCsaMm2"] = r.RecommendedCsaMm2,
                ["csaLabel"]          = r.CsaLabel,
                ["actualVoltDropPct"] = Math.Round(r.ActualVoltDropPct, 2),
                ["vdCompliant"]       = r.VDCompliant,
                ["proposedBreakerA"]  = r.ProposedBreakerA,
                ["warning"]           = r.Warning ?? "",
                ["derivationNote"]    = r.DerivationNote ?? "",
            };
            string summary = string.IsNullOrEmpty(r.Warning)
                ? $"{r.CsaLabel} — Ib {r.DesignCurrentA:F1} A, VD {r.ActualVoltDropPct:F2}% ({(r.VDCompliant ? "OK" : "over")}), breaker {r.ProposedBreakerA} A."
                : $"Cable calc warning: {r.Warning}";
            return McpJobResult.Success(summary, data).ToCallResult();
        }

        // ── Pagination helper (reused by every large reader) ─────────────────────

        /// <summary>
        /// Offset-cursor pagination. Returns the total, one page of at most
        /// <paramref name="limit"/> items, the next cursor (null when exhausted),
        /// and the offset this page started at. Cursor is a simple integer offset.
        /// </summary>
        internal static (int total, List<T> page, string nextCursor, int offset) Paginate<T>(
            IReadOnlyList<T> items, int limit, string cursor)
        {
            int offset = 0;
            if (!string.IsNullOrWhiteSpace(cursor)) int.TryParse(cursor, out offset);
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 50;
            if (limit > 200) limit = 200;

            var page = items.Skip(offset).Take(limit).ToList();
            int nextOffset = offset + page.Count;
            string next = nextOffset < items.Count ? nextOffset.ToString() : null;
            return (items.Count, page, next, offset);
        }

        // ── query_elements ───────────────────────────────────────────────────────

        public static McpCallResult QueryElements(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument uidoc, out Document doc);
                if (g != null) return g;

                string category  = args["category"]?.Value<string>()?.Trim();
                string viewScope = args["viewScope"]?.Value<string>()?.Trim()?.ToLowerInvariant() ?? "project";
                int    limit     = args["limit"]?.Value<int?>() ?? 50;
                string cursor    = args["cursor"]?.Value<string>();

                FilteredElementCollector col;
                if (viewScope == "active" || viewScope == "view")
                {
                    View av = uidoc.ActiveView;
                    if (av == null) return McpJobResult.Error("bad_args", "No active view for viewScope='active'.");
                    col = new FilteredElementCollector(doc, av.Id);
                }
                else
                {
                    col = new FilteredElementCollector(doc);
                }
                col = col.WhereElementIsNotElementType();

                if (!string.IsNullOrEmpty(category))
                {
                    ElementId catId = ResolveCategoryId(doc, category);
                    if (catId == null)
                        return McpJobResult.Error("not_found",
                            $"Category '{category}' not found. Use a friendly name (e.g. 'Ducts') or a BuiltInCategory (e.g. 'OST_DuctCurves').");
                    col = col.OfCategoryId(catId);
                }

                var filters = ParseFilters(args["paramFilters"] as JArray, out string ferr);
                if (ferr != null) return McpJobResult.Error("bad_args", ferr);

                var matched = new List<Element>();
                foreach (Element el in col)
                    if (PassesFilters(el, filters)) matched.Add(el);

                var byCat = new Dictionary<string, int>();
                var byLevel = new Dictionary<string, int>();
                foreach (Element el in matched)
                {
                    string cn = el.Category?.Name ?? "(none)";
                    byCat[cn] = byCat.TryGetValue(cn, out int c1) ? c1 + 1 : 1;
                    string lv = SafeLevel(doc, el);
                    byLevel[lv] = byLevel.TryGetValue(lv, out int c2) ? c2 + 1 : 1;
                }

                var (total, page, nextCursor, offset) = Paginate(matched, limit, cursor);

                var rows = page.Select(el => (object)new Dictionary<string, object>
                {
                    ["id"]        = el.Id.Value,
                    ["category"]  = el.Category?.Name ?? "",
                    ["family"]    = SafeFamily(el),
                    ["type"]      = SafeType(el),
                    ["keyParams"] = BuildKeyParams(doc, el, filters),
                }).ToList();

                var data = new Dictionary<string, object>
                {
                    ["total"]      = total,
                    ["returned"]   = page.Count,
                    ["offset"]     = offset,
                    ["nextCursor"] = nextCursor,
                    ["byCategory"] = byCat.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => k.Value),
                    ["byLevel"]    = byLevel.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => k.Value),
                    ["elements"]   = rows,
                };

                string summary =
                    $"{total} element(s)" +
                    (string.IsNullOrEmpty(category) ? "" : $" in '{category}'") +
                    (filters.Count > 0 ? $" matching {filters.Count} filter(s)" : "") +
                    $"; showing {page.Count} from offset {offset}" +
                    (nextCursor != null ? $" (more available — cursor '{nextCursor}')" : "") + ".";
                return McpJobResult.Success(summary, data);
            }).ToCallResult();
        }

        // ── get_element ──────────────────────────────────────────────────────────

        public static McpCallResult GetElement(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                if (!TryGetId(args, "id", out long idVal, out McpJobResult idErr)) return idErr;
                Element el = doc.GetElement(new ElementId(idVal));
                if (el == null) return McpJobResult.Error("not_found", $"No element with id {idVal}.");

                var allParams = new Dictionary<string, string>();
                foreach (Parameter p in el.Parameters)
                {
                    try
                    {
                        string pn = p.Definition?.Name;
                        if (string.IsNullOrEmpty(pn) || allParams.ContainsKey(pn)) continue;
                        allParams[pn] = ParamValueString(p);
                    }
                    catch (Exception ex) { StingLog.Warn($"get_element param read: {ex.Message}"); }
                }

                var data = new Dictionary<string, object>
                {
                    ["id"]         = el.Id.Value,
                    ["category"]   = el.Category?.Name ?? "",
                    ["family"]     = SafeFamily(el),
                    ["type"]       = SafeType(el),
                    ["name"]       = SafeName(el),
                    ["level"]      = SafeLevel(doc, el),
                    ["location"]   = ReadLocation(el),
                    ["boundingBox"] = ReadBbox(el),
                    ["parameterCount"] = allParams.Count,
                    ["parameters"] = allParams,
                };
                return McpJobResult.Success(
                    $"Element {el.Id.Value} — {el.Category?.Name}: {SafeFamily(el)} / {SafeType(el)} ({allParams.Count} params).",
                    data);
            }).ToCallResult();
        }

        // ── get_parameter ────────────────────────────────────────────────────────

        public static McpCallResult GetParameter(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                if (!TryGetId(args, "id", out long idVal, out McpJobResult idErr)) return idErr;
                string name = args["name"]?.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(name)) return McpJobResult.Error("bad_args", "Missing required argument: name.");

                Element el = doc.GetElement(new ElementId(idVal));
                if (el == null) return McpJobResult.Error("not_found", $"No element with id {idVal}.");

                Parameter p = el.LookupParameter(name);
                if (p == null) return McpJobResult.Error("not_found", $"Element {idVal} has no parameter named '{name}'.");

                bool isBuiltIn = p.Definition is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID;
                var data = new Dictionary<string, object>
                {
                    ["id"]          = el.Id.Value,
                    ["name"]        = name,
                    ["value"]       = ParamValueString(p),
                    ["storageType"] = p.StorageType.ToString(),
                    ["hasValue"]    = p.HasValue,
                    ["isShared"]    = p.IsShared,
                    ["isBuiltIn"]   = isBuiltIn,
                    ["isReadOnly"]  = p.IsReadOnly,
                };
                return McpJobResult.Success($"{name} = {ParamValueString(p)} ({p.StorageType}).", data);
            }).ToCallResult();
        }

        // ── get_selection ────────────────────────────────────────────────────────

        public static McpCallResult GetSelection()
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument uidoc, out Document doc);
                if (g != null) return g;

                var ids = uidoc.Selection.GetElementIds().ToList();
                var byCat = new Dictionary<string, int>();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    string cn = el?.Category?.Name ?? "(none)";
                    byCat[cn] = byCat.TryGetValue(cn, out int c) ? c + 1 : 1;
                }
                var data = new Dictionary<string, object>
                {
                    ["count"]      = ids.Count,
                    ["ids"]        = ids.Select(i => i.Value).ToList(),
                    ["byCategory"] = byCat.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => k.Value),
                };
                return McpJobResult.Success($"{ids.Count} element(s) selected.", data);
            }).ToCallResult();
        }

        // ── set_selection ────────────────────────────────────────────────────────

        public static McpCallResult SetSelection(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument uidoc, out Document doc);
                if (g != null) return g;

                if (!(args["ids"] is JArray arr))
                    return McpJobResult.Error("bad_args", "Missing required argument: ids (array of element ids).");

                var valid = new List<ElementId>();
                var missing = new List<long>();
                foreach (var t in arr)
                {
                    long v = t?.Value<long?>() ?? -1;
                    if (v < 0) continue;
                    var eid = new ElementId(v);
                    if (doc.GetElement(eid) != null) valid.Add(eid);
                    else missing.Add(v);
                }

                uidoc.Selection.SetElementIds(valid);   // non-destructive; no transaction needed
                var data = new Dictionary<string, object>
                {
                    ["set"]     = valid.Count,
                    ["missing"] = missing,
                };
                return McpJobResult.Success(
                    $"Selection set to {valid.Count} element(s)" +
                    (missing.Count > 0 ? $"; {missing.Count} id(s) not found and skipped." : "."), data);
            }).ToCallResult();
        }

        // ── list_views ───────────────────────────────────────────────────────────

        public static McpCallResult ListViews(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                string filter = args["filter"]?.Value<string>()?.Trim();
                string type   = args["type"]?.Value<string>()?.Trim();

                var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Where(v => string.IsNullOrEmpty(filter) ||
                                (v.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Where(v => string.IsNullOrEmpty(type) ||
                                v.ViewType.ToString().Equals(type, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name)
                    .ToList();

                var byType = views.GroupBy(v => v.ViewType.ToString())
                    .ToDictionary(gr => gr.Key, gr => gr.Count());

                const int cap = 300;
                var list = views.Take(cap).Select(v => (object)new Dictionary<string, object>
                {
                    ["id"]    = v.Id.Value,
                    ["name"]  = v.Name,
                    ["type"]  = v.ViewType.ToString(),
                    ["scale"] = SafeScale(v),
                }).ToList();

                var data = new Dictionary<string, object>
                {
                    ["total"]     = views.Count,
                    ["returned"]  = list.Count,
                    ["truncated"] = views.Count > cap,
                    ["byType"]    = byType,
                    ["views"]     = list,
                };
                return McpJobResult.Success($"{views.Count} view(s)" +
                    (views.Count > cap ? $" (showing first {cap})" : "") + ".", data);
            }).ToCallResult();
        }

        // ── list_sheets ──────────────────────────────────────────────────────────

        public static McpCallResult ListSheets(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                string filter = args["filter"]?.Value<string>()?.Trim();

                var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(s => string.IsNullOrEmpty(filter) ||
                                (s.SheetNumber?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (s.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                const int cap = 500;
                var list = sheets.Take(cap).Select(s => (object)new Dictionary<string, object>
                {
                    ["id"]     = s.Id.Value,
                    ["number"] = s.SheetNumber,
                    ["name"]   = s.Name,
                }).ToList();

                var data = new Dictionary<string, object>
                {
                    ["total"]     = sheets.Count,
                    ["returned"]  = list.Count,
                    ["truncated"] = sheets.Count > cap,
                    ["sheets"]    = list,
                };
                return McpJobResult.Success($"{sheets.Count} sheet(s)" +
                    (sheets.Count > cap ? $" (showing first {cap})" : "") + ".", data);
            }).ToCallResult();
        }

        // ── get_schedule_data ────────────────────────────────────────────────────

        public static McpCallResult GetScheduleData(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                string name = args["name"]?.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(name)) return McpJobResult.Error("bad_args", "Missing required argument: name (schedule name).");
                int    limit  = args["limit"]?.Value<int?>() ?? 50;
                string cursor = args["cursor"]?.Value<string>();

                var sched = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (sched == null) return McpJobResult.Error("not_found", $"No schedule named '{name}'.");

                var fields = new List<string>();
                var def = sched.Definition;
                for (int i = 0; i < def.GetFieldCount(); i++)
                {
                    try { fields.Add(def.GetField(i).GetName()); } catch { fields.Add($"col{i}"); }
                }

                TableSectionData body = sched.GetTableData().GetSectionData(SectionType.Body);
                int rowCount = body.NumberOfRows;
                int colCount = body.NumberOfColumns;

                if (limit <= 0) limit = 50;
                if (limit > 200) limit = 200;
                int offset = 0;
                if (!string.IsNullOrWhiteSpace(cursor)) int.TryParse(cursor, out offset);
                if (offset < 0) offset = 0;
                int end = Math.Min(rowCount, offset + limit);

                var rows = new List<List<string>>();
                for (int r = offset; r < end; r++)
                {
                    var row = new List<string>(colCount);
                    for (int c = 0; c < colCount; c++)
                    {
                        try { row.Add(sched.GetCellText(SectionType.Body, r, c)); }
                        catch { row.Add(""); }
                    }
                    rows.Add(row);
                }
                string nextCursor = end < rowCount ? end.ToString() : null;

                var data = new Dictionary<string, object>
                {
                    ["schedule"]   = sched.Name,
                    ["fields"]     = fields,
                    ["totalRows"]  = rowCount,
                    ["returned"]   = rows.Count,
                    ["offset"]     = offset,
                    ["nextCursor"] = nextCursor,
                    ["rows"]       = rows,
                };
                return McpJobResult.Success(
                    $"Schedule '{sched.Name}' — {rowCount} row(s); showing {rows.Count} from offset {offset}" +
                    (nextCursor != null ? $" (more — cursor '{nextCursor}')" : "") + ".", data);
            }).ToCallResult();
        }

        // ── get_compliance ───────────────────────────────────────────────────────

        public static McpCallResult GetCompliance(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                bool byDisc = args["byDiscipline"]?.Value<bool?>() ?? false;

                var scan = ComplianceScan.GetCached() ?? ComplianceScan.Scan(doc);
                if (scan == null || scan.TotalElements < 0)
                    return McpJobResult.Success(
                        "Compliance scan is pending (model still loading or not yet scanned). Retry shortly.",
                        new Dictionary<string, object> { ["pending"] = true });

                var data = new Dictionary<string, object>
                {
                    ["rag"]               = scan.RAGStatus,
                    ["totalElements"]     = scan.TotalElements,
                    ["taggedComplete"]    = scan.TaggedComplete,
                    ["taggedIncomplete"]  = scan.TaggedIncomplete,
                    ["untagged"]          = scan.Untagged,
                    ["compliancePercent"] = Math.Round(scan.CompliancePercent, 1),
                    ["strictPercent"]     = Math.Round(scan.StrictPercent, 1),
                    ["revisionPercent"]   = Math.Round(scan.RevisionPercent, 1),
                    ["staleCount"]        = scan.StaleCount,
                    ["topIssues"]         = scan.IssuesByType?.OrderByDescending(k => k.Value)
                                              .Take(8).ToDictionary(k => k.Key, k => k.Value)
                                              ?? new Dictionary<string, int>(),
                };

                if (byDisc && scan.ByDisc != null)
                {
                    data["byDiscipline"] = scan.ByDisc.ToDictionary(
                        kv => kv.Key,
                        kv => (object)new Dictionary<string, object>
                        {
                            ["total"]    = kv.Value.Total,
                            ["tagged"]   = kv.Value.Tagged,
                            ["untagged"] = kv.Value.Untagged,
                            ["percent"]  = Math.Round(kv.Value.CompliancePct, 1),
                        });
                }

                return McpJobResult.Success(
                    $"{scan.RAGStatus} — {scan.CompliancePercent:F0}% tagged " +
                    $"({scan.TaggedComplete}/{scan.TotalElements}); {scan.Untagged} untagged.", data);
            }).ToCallResult();
        }

        // ── get_tag_status ───────────────────────────────────────────────────────

        public static McpCallResult GetTagStatus(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                string discFilter = args["discipline"]?.Value<string>()?.Trim();
                const int idCap = 200;   // cap element-id lists per bucket to protect agent context

                var buckets = new Dictionary<string, DiscBucket>(StringComparer.OrdinalIgnoreCase);

                foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    Parameter tagParam = el.LookupParameter(ParamRegistry.TAG1);
                    if (tagParam == null) continue;   // not a STING-taggable element

                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    if (string.IsNullOrEmpty(disc)) disc = "(none)";
                    if (!string.IsNullOrEmpty(discFilter) &&
                        !string.Equals(disc, discFilter, StringComparison.OrdinalIgnoreCase)) continue;

                    if (!buckets.TryGetValue(disc, out DiscBucket b)) { b = new DiscBucket(); buckets[disc] = b; }
                    b.Total++;

                    string tag = tagParam.AsString() ?? "";
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        b.Untagged++;
                        if (b.UntaggedIds.Count < idCap) b.UntaggedIds.Add(el.Id.Value);
                    }
                    else if (TagConfig.TagIsComplete(tag))
                    {
                        b.Complete++;
                    }
                    else
                    {
                        b.Incomplete++;
                        if (b.IncompleteIds.Count < idCap) b.IncompleteIds.Add(el.Id.Value);
                    }
                }

                int totalTaggable = buckets.Values.Sum(b => b.Total);
                int totalUntagged = buckets.Values.Sum(b => b.Untagged);
                int totalIncomplete = buckets.Values.Sum(b => b.Incomplete);

                var byDisc = buckets.OrderBy(k => k.Key).ToDictionary(
                    kv => kv.Key,
                    kv => (object)new Dictionary<string, object>
                    {
                        ["total"]         = kv.Value.Total,
                        ["complete"]      = kv.Value.Complete,
                        ["incomplete"]    = kv.Value.Incomplete,
                        ["untagged"]      = kv.Value.Untagged,
                        ["untaggedIds"]   = kv.Value.UntaggedIds,
                        ["incompleteIds"] = kv.Value.IncompleteIds,
                        ["idsCapped"]     = kv.Value.Untagged > idCap || kv.Value.Incomplete > idCap,
                    });

                var data = new Dictionary<string, object>
                {
                    ["totalTaggable"]   = totalTaggable,
                    ["totalUntagged"]   = totalUntagged,
                    ["totalIncomplete"] = totalIncomplete,
                    ["idCapPerBucket"]  = idCap,
                    ["byDiscipline"]    = byDisc,
                };
                return McpJobResult.Success(
                    $"{totalTaggable} taggable element(s): {totalUntagged} untagged, {totalIncomplete} incomplete-tag" +
                    (string.IsNullOrEmpty(discFilter) ? "" : $" (discipline '{discFilter}')") + ".", data);
            }).ToCallResult();
        }

        // ── run_validator ────────────────────────────────────────────────────────

        public static McpCallResult RunValidator(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                string name = args["name"]?.Value<string>()?.Trim();
                var runners = ValidatorRunners();
                string available = string.Join(", ", runners.Keys.OrderBy(k => k));

                if (string.IsNullOrEmpty(name))
                    return McpJobResult.Error("bad_args", $"Missing required argument: name. Available validators: {available}.");
                if (!runners.TryGetValue(name, out var run))
                    return McpJobResult.Error("not_found", $"Unknown validator '{name}'. Available: {available}.");

                List<ValidationResult> findings;
                try { findings = run(doc) ?? new List<ValidationResult>(); }
                catch (Exception ex)
                {
                    StingLog.Warn($"Validator '{name}' threw: {ex.Message}");
                    return McpJobResult.Error("exception", $"Validator '{name}' failed: {ex.Message}");
                }

                int errors   = findings.Count(f => f.Severity == ValidationSeverity.Error);
                int warnings = findings.Count(f => f.Severity == ValidationSeverity.Warning);
                int infos    = findings.Count(f => f.Severity == ValidationSeverity.Info);
                string verdict = errors > 0 ? "FAIL" : warnings > 0 ? "WARN" : "PASS";

                const int findingCap = 100;
                var top = findings
                    .OrderByDescending(f => (int)f.Severity)
                    .Take(findingCap)
                    .Select(f => (object)new Dictionary<string, object>
                    {
                        ["severity"]  = f.Severity.ToString(),
                        ["code"]      = f.Code,
                        ["message"]   = f.Message,
                        ["elementId"] = f.ElementId?.Value ?? -1,
                    }).ToList();

                var byCode = findings.GroupBy(f => f.Code)
                    .OrderByDescending(gr => gr.Count())
                    .Take(15)
                    .ToDictionary(gr => string.IsNullOrEmpty(gr.Key) ? "(none)" : gr.Key, gr => gr.Count());

                var data = new Dictionary<string, object>
                {
                    ["validator"]    = name,
                    ["verdict"]      = verdict,
                    ["total"]        = findings.Count,
                    ["errors"]       = errors,
                    ["warnings"]     = warnings,
                    ["infos"]        = infos,
                    ["byCode"]       = byCode,
                    ["findings"]     = top,
                    ["findingsCapped"] = findings.Count > findingCap,
                };
                return McpJobResult.Success(
                    $"Validator '{name}': {verdict} — {errors} error(s), {warnings} warning(s), {infos} info.", data);
            }).ToCallResult();
        }

        private static Dictionary<string, Func<Document, List<ValidationResult>>> ValidatorRunners() =>
            new Dictionary<string, Func<Document, List<ValidationResult>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["connectivity"] = d => new ConnectivityValidator().Validate(d),
                ["fill"]         = d => new FillValidator().Validate(d),
                ["spec"]         = d => new SpecValidator().Validate(d),
                ["slope"]        = d => new SlopeValidator().Validate(d),
                ["termination"]  = d => new TerminationValidator().Validate(d),
                ["clearance"]    = d => new ClearanceValidator().Validate(d),
                ["separation"]   = d => SeparationValidator.Validate(d),
            };

        private sealed class DiscBucket
        {
            public int Total, Complete, Incomplete, Untagged;
            public readonly List<long> UntaggedIds = new List<long>();
            public readonly List<long> IncompleteIds = new List<long>();
        }

        // ── get_rooms ────────────────────────────────────────────────────────────

        public static McpCallResult GetRooms(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                string filter = args["filter"]?.Value<string>()?.Trim();
                int    limit  = args["limit"]?.Value<int?>() ?? 50;
                string cursor = args["cursor"]?.Value<string>();

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => string.IsNullOrEmpty(filter) ||
                                (r.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (SafeRoomNumber(r).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(SafeRoomNumber)
                    .ToList();

                int placed = rooms.Count(r => { try { return r.Area > 1e-6; } catch { return false; } });
                var (total, page, nextCursor, offset) = Paginate(rooms, limit, cursor);

                var list = page.Select(r => (object)new Dictionary<string, object>
                {
                    ["id"]     = r.Id.Value,
                    ["name"]   = SafeName(r),
                    ["number"] = SafeRoomNumber(r),
                    ["areaM2"] = RoomAreaM2(r),
                    ["level"]  = SafeLevel(doc, r),
                }).ToList();

                var data = new Dictionary<string, object>
                {
                    ["total"]      = total,
                    ["placed"]     = placed,
                    ["returned"]   = page.Count,
                    ["offset"]     = offset,
                    ["nextCursor"] = nextCursor,
                    ["rooms"]      = list,
                };
                return McpJobResult.Success(
                    $"{total} room(s) ({placed} placed); showing {page.Count} from offset {offset}" +
                    (nextCursor != null ? $" (more — cursor '{nextCursor}')" : "") + ".", data);
            }).ToCallResult();
        }

        // ── get_levels ───────────────────────────────────────────────────────────

        public static McpCallResult GetLevels()
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();

                double Mm(double ft) => Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters), 1);
                var list = levels.Select(l => (object)new Dictionary<string, object>
                {
                    ["id"]          = l.Id.Value,
                    ["name"]        = l.Name,
                    ["elevationMm"] = Mm(l.Elevation),
                }).ToList();

                var data = new Dictionary<string, object> { ["total"] = levels.Count, ["levels"] = list };
                return McpJobResult.Success($"{levels.Count} level(s).", data);
            }).ToCallResult();
        }

        // ── get_grids ────────────────────────────────────────────────────────────

        public static McpCallResult GetGrids()
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                    .OrderBy(gr => gr.Name).ToList();

                var list = grids.Select(gr =>
                {
                    var d = new Dictionary<string, object> { ["id"] = gr.Id.Value, ["name"] = gr.Name };
                    try
                    {
                        Curve c = gr.Curve;
                        if (c != null && c.IsBound)
                        {
                            d["start"] = XyzMm(c.GetEndPoint(0));
                            d["end"]   = XyzMm(c.GetEndPoint(1));
                            d["shape"] = c is Arc ? "arc" : "line";
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"get_grids curve: {ex.Message}"); }
                    return (object)d;
                }).ToList();

                var data = new Dictionary<string, object> { ["total"] = grids.Count, ["grids"] = list };
                return McpJobResult.Success($"{grids.Count} grid(s).", data);
            }).ToCallResult();
        }

        // ── get_warnings ─────────────────────────────────────────────────────────

        public static McpCallResult GetWarnings(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out UIDocument _, out Document doc);
                if (g != null) return g;

                int limit = args["limit"]?.Value<int?>() ?? 100;
                if (limit <= 0) limit = 100;
                if (limit > 500) limit = 500;

                IList<FailureMessage> warnings;
                try { warnings = doc.GetWarnings(); }
                catch (Exception ex)
                {
                    StingLog.Warn($"get_warnings: {ex.Message}");
                    return McpJobResult.Error("exception", $"Could not read warnings: {ex.Message}");
                }

                int total = warnings.Count;
                var byDesc = new Dictionary<string, int>();
                foreach (var w in warnings)
                {
                    string d = SafeDesc(w);
                    byDesc[d] = byDesc.TryGetValue(d, out int c) ? c + 1 : 1;
                }

                var list = warnings.Take(limit).Select(w => (object)new Dictionary<string, object>
                {
                    ["description"] = SafeDesc(w),
                    ["severity"]    = SafeSeverity(w),
                    ["elementIds"]  = SafeFailingIds(w),
                }).ToList();

                var data = new Dictionary<string, object>
                {
                    ["total"]     = total,
                    ["returned"]  = list.Count,
                    ["truncated"] = total > limit,
                    ["topTypes"]  = byDesc.OrderByDescending(k => k.Value).Take(15).ToDictionary(k => k.Key, k => k.Value),
                    ["warnings"]  = list,
                };
                return McpJobResult.Success(
                    $"{total} model warning(s)" + (total > limit ? $" (showing first {limit})" : "") + ".", data);
            }).ToCallResult();
        }

        private static string SafeRoomNumber(Autodesk.Revit.DB.Architecture.Room r)
        {
            try { return r.Number ?? ""; } catch { return ""; }
        }
        private static double RoomAreaM2(Autodesk.Revit.DB.Architecture.Room r)
        {
            try { return Math.Round(UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters), 2); }
            catch { return 0; }
        }
        private static string SafeDesc(FailureMessage w)
        {
            try { return w.GetDescriptionText() ?? ""; } catch { return ""; }
        }
        private static string SafeSeverity(FailureMessage w)
        {
            try { return w.GetSeverity().ToString(); } catch { return ""; }
        }
        private static List<long> SafeFailingIds(FailureMessage w)
        {
            try { return w.GetFailingElements().Select(i => i.Value).Take(25).ToList(); }
            catch { return new List<long>(); }
        }

        // ── shared guard + readers ───────────────────────────────────────────────

        /// <summary>License + document guard used by every job. Returns a typed error
        /// (and leaves out-params null) on failure, else null with uidoc/doc set.</summary>
        private static McpJobResult Guard(UIApplication uiApp, out UIDocument uidoc, out Document doc)
        {
            uidoc = null; doc = null;
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic;
            var de = McpSafety.RequireDocument(uiApp);
            if (de != null) return de;
            uidoc = uiApp.ActiveUIDocument;
            doc = uidoc.Document;
            return null;
        }

        private static bool TryGetId(JObject args, string key, out long id, out McpJobResult err)
        {
            id = 0; err = null;
            var tok = args[key];
            if (tok == null) { err = McpJobResult.Error("bad_args", $"Missing required argument: {key}."); return false; }
            long? v = tok.Type == JTokenType.String ? (long.TryParse(tok.Value<string>(), out long pv) ? pv : (long?)null)
                                                    : tok.Value<long?>();
            if (v == null) { err = McpJobResult.Error("bad_args", $"Argument '{key}' must be an element id (integer)."); return false; }
            id = v.Value;
            return true;
        }

        private static ElementId ResolveCategoryId(Document doc, string category)
        {
            if (category.StartsWith("OST_", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse(category, true, out BuiltInCategory bic))
                return new ElementId(bic);

            foreach (Category c in doc.Settings.Categories)
                if (string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase))
                    return c.Id;

            string guess = "OST_" + category.Replace(" ", "");
            if (Enum.TryParse(guess, true, out BuiltInCategory bic2))
                return new ElementId(bic2);

            return null;
        }

        private static List<(string name, string op, string val)> ParseFilters(JArray arr, out string err)
        {
            err = null;
            var list = new List<(string, string, string)>();
            if (arr == null) return list;

            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "eq", "ne", "gt", "lt", "contains", "empty", "notempty" };

            foreach (var f in arr)
            {
                string name = f["name"]?.Value<string>();
                string op = f["op"]?.Value<string>()?.Trim();
                string val = f["value"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(op))
                { err = "Each paramFilter needs 'name' and 'op'."; return list; }
                if (!valid.Contains(op))
                { err = $"Unknown filter op '{op}'. Use: eq, ne, gt, lt, contains, empty, notEmpty."; return list; }
                list.Add((name, op.ToLowerInvariant(), val ?? ""));
            }
            return list;
        }

        private static bool PassesFilters(Element el, List<(string name, string op, string val)> filters)
        {
            foreach (var (name, op, val) in filters)
            {
                var (str, num, has) = GetComparable(el, name);
                switch (op)
                {
                    case "empty":    if (has && !string.IsNullOrEmpty(str)) return false; break;
                    case "notempty": if (!has || string.IsNullOrEmpty(str)) return false; break;
                    case "eq":       if (!EqCompare(str, num, val)) return false; break;
                    case "ne":       if (EqCompare(str, num, val)) return false; break;
                    case "contains": if (str == null || str.IndexOf(val, StringComparison.OrdinalIgnoreCase) < 0) return false; break;
                    case "gt":       if (!(num.HasValue && double.TryParse(val, out double gv) && num.Value > gv)) return false; break;
                    case "lt":       if (!(num.HasValue && double.TryParse(val, out double lv) && num.Value < lv)) return false; break;
                }
            }
            return true;
        }

        private static bool EqCompare(string str, double? num, string val)
        {
            if (num.HasValue && double.TryParse(val, out double v)) return Math.Abs(num.Value - v) < 1e-9;
            return string.Equals(str ?? "", val ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Read a parameter as (displayString, numeric?, hasParam). Numeric comparisons
        /// use the parameter's DISPLAYED value in project units (via AsValueString) so a
        /// filter like Diameter&lt;100 reads in mm, not internal feet.
        /// </summary>
        private static (string str, double? num, bool has) GetComparable(Element el, string name)
        {
            Parameter p = el.LookupParameter(name);
            if (p == null)
            {
                string s = ParameterHelpers.GetString(el, name);
                if (!string.IsNullOrEmpty(s)) return (s, TryNum(s), true);
                return (null, null, false);
            }
            if (!p.HasValue) return ("", null, true);

            switch (p.StorageType)
            {
                case StorageType.String:
                {
                    string s = p.AsString() ?? "";
                    return (s, TryNum(s), true);
                }
                case StorageType.Integer:
                {
                    int i = p.AsInteger();
                    return (i.ToString(), i, true);
                }
                case StorageType.Double:
                {
                    string vs = null; try { vs = p.AsValueString(); } catch { }
                    double? disp = TryNum(vs);
                    if (!disp.HasValue) { double d = p.AsDouble(); return (vs ?? d.ToString("G"), d, true); }
                    return (vs, disp, true);
                }
                case StorageType.ElementId:
                {
                    ElementId id = p.AsElementId();
                    long v = id?.Value ?? -1;
                    return (v.ToString(), v, true);
                }
                default:
                {
                    string vs = null; try { vs = p.AsValueString(); } catch { }
                    return (vs ?? "", null, true);
                }
            }
        }

        private static Dictionary<string, string> BuildKeyParams(
            Document doc, Element el, List<(string name, string op, string val)> filters)
        {
            var d = new Dictionary<string, string>();
            try
            {
                string mark = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                if (!string.IsNullOrEmpty(mark)) d["Mark"] = mark;
            }
            catch { }
            string lvl = SafeLevel(doc, el);
            if (!string.IsNullOrEmpty(lvl) && lvl != "(none)") d["Level"] = lvl;
            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            if (!string.IsNullOrEmpty(tag)) d["Tag"] = tag;

            foreach (var (name, op, val) in filters)
            {
                if (d.ContainsKey(name)) continue;
                var (s, num, has) = GetComparable(el, name);
                if (has) d[name] = s ?? "";
            }
            return d;
        }

        private static string ParamValueString(Parameter p)
        {
            try
            {
                if (p == null || !p.HasValue) return "";
                switch (p.StorageType)
                {
                    case StorageType.String:    return p.AsString() ?? "";
                    case StorageType.Integer:   return p.AsInteger().ToString();
                    case StorageType.Double:    return p.AsValueString() ?? p.AsDouble().ToString("G");
                    case StorageType.ElementId:
                    {
                        var id = p.AsElementId();
                        return (id != null && id.Value >= 0) ? id.Value.ToString() : "";
                    }
                    default: return p.AsValueString() ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"ParamValueString: {ex.Message}"); return ""; }
        }

        private static object ReadLocation(Element el)
        {
            try
            {
                switch (el.Location)
                {
                    case LocationPoint lp:
                        return new Dictionary<string, object> { ["type"] = "point", ["point"] = XyzMm(lp.Point) };
                    case LocationCurve lc:
                        return new Dictionary<string, object>
                        {
                            ["type"]  = "curve",
                            ["start"] = XyzMm(lc.Curve.GetEndPoint(0)),
                            ["end"]   = XyzMm(lc.Curve.GetEndPoint(1)),
                        };
                    default: return null;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadLocation: {ex.Message}"); return null; }
        }

        private static object ReadBbox(Element el)
        {
            try
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb == null) return null;
                return new Dictionary<string, object> { ["min"] = XyzMm(bb.Min), ["max"] = XyzMm(bb.Max) };
            }
            catch (Exception ex) { StingLog.Warn($"ReadBbox: {ex.Message}"); return null; }
        }

        private static Dictionary<string, double> XyzMm(XYZ p)
        {
            double Mm(double ft) => Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters), 1);
            return new Dictionary<string, double> { ["x"] = Mm(p.X), ["y"] = Mm(p.Y), ["z"] = Mm(p.Z) };
        }

        private static double? TryNum(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Regex.Match(s, @"-?\d+(\.\d+)?");
            return m.Success && double.TryParse(m.Value, out double d) ? d : (double?)null;
        }

        private static string SafeLevel(Document doc, Element el)
        {
            try { string s = ParameterHelpers.GetLevelCode(doc, el); return string.IsNullOrEmpty(s) ? "(none)" : s; }
            catch { return "(none)"; }
        }
        private static string SafeFamily(Element el)
        {
            try { return ParameterHelpers.GetFamilyName(el) ?? ""; } catch { return ""; }
        }
        private static string SafeType(Element el)
        {
            try { return ParameterHelpers.GetFamilySymbolName(el) ?? ""; } catch { return ""; }
        }
        private static string SafeName(Element el)
        {
            try { return el.Name ?? ""; } catch { return ""; }
        }
        private static int SafeScale(View v)
        {
            try { return v.Scale; } catch { return 0; }
        }
    }
}
