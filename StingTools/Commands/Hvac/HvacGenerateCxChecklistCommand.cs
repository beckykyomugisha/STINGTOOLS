// StingTools — HVAC commissioning checklist generator.
//
// Scans the project for mechanical equipment and emits an ASHRAE
// Guideline 0 / CIBSE TM39 pre-Cx + functional Cx checklist as a CSV
// in <project>/_BIM_COORD/cx/ (created on demand). Each equipment row
// gets a class-specific task list driven by `_taskLibrary` below; the
// CSV columns are designed to drop straight into a commissioning
// agent's witnessing form.
//
// Output columns:
//   Tag, Family, Type, System, Class, Phase, Task, Method, Acceptance,
//   PassFail, Date, Signature
//
// "Phase" is one of:
//   PreInstall — drawings / data sheets / submittals
//   PreStartup — physical install + connections
//   Startup    — first energize / fill / vent
//   Functional — sequence-of-operation test under varied loads
//   Handover   — TAB report, O&M, training, warranty
//
// Tasks are conservative defaults; projects can append rows by
// dropping <project>/_BIM_COORD/cx/cx_tasks_override.json.

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
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacGenerateCxChecklistCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var equipment = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
                if (equipment.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Commissioning Checklist",
                        "No mechanical equipment found in the project.");
                    return Result.Cancelled;
                }

                // Output directory
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir))
                {
                    TaskDialog.Show("STING HVAC", "Save the project before generating the Cx checklist.");
                    return Result.Cancelled;
                }
                string cxDir = Path.Combine(projDir, "_BIM_COORD", "cx");
                Directory.CreateDirectory(cxDir);
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string csvPath = Path.Combine(cxDir, $"cx_checklist_{ts}.csv");

                // Resolve task library: corporate baseline + project override.
                // First call per Revit session caches the merged library.
                var library = LoadTaskLibrary(projDir);

                int rows = 0;
                var byClass = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var sb = new StringBuilder();
                sb.AppendLine("Tag,Family,Type,System,Class,Phase,Task,Method,Acceptance,PassFail,Date,Signature");
                foreach (var fi in equipment)
                {
                    try
                    {
                        string tag    = fi.LookupParameter("ASS_TAG_1")?.AsString() ?? $"#{fi.Id.Value}";
                        string family = fi.Symbol?.Family?.Name ?? fi.Name ?? "";
                        string type   = fi.Symbol?.Name ?? "";
                        string system = SystemNameOf(fi);
                        string cls    = ClassifyEquipment(family, type);
                        byClass[cls]  = byClass.TryGetValue(cls, out var n) ? n + 1 : 1;

                        var tasks = library.TryGetValue(cls, out var match)
                            ? match
                            : library.TryGetValue("Generic", out var fallback) ? fallback : new List<CxTask>();
                        foreach (var task in tasks)
                        {
                            sb.AppendLine(string.Join(",",
                                Esc(tag), Esc(family), Esc(type), Esc(system), Esc(cls),
                                Esc(task.Phase), Esc(task.Task), Esc(task.Method),
                                Esc(task.Acceptance), "", "", ""));
                            rows++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Cx row {fi.Id}: {ex.Message}"); }
                }
                File.WriteAllText(csvPath, sb.ToString());

                var panel = StingResultPanel.Create("HVAC — Commissioning Checklist");
                panel.SetSubtitle($"{equipment.Count} equipment / {rows} Cx tasks / {byClass.Count} classes");
                panel.AddSection("OUTPUT")
                     .Metric("CSV", csvPath)
                     .Metric("Rows", rows.ToString());

                panel.AddSection("BY EQUIPMENT CLASS");
                foreach (var kv in byClass.OrderByDescending(k => k.Value))
                {
                    int taskCount = library.TryGetValue(kv.Key, out var lst)
                        ? lst.Count
                        : (library.TryGetValue("Generic", out var fb) ? fb.Count : 0);
                    panel.Metric(kv.Key, $"{kv.Value} units × {taskCount} tasks");
                }
                panel.Text("Aligned to ASHRAE Guideline 0-2019 + CIBSE TM39 phases " +
                           "(PreInstall / PreStartup / Startup / Functional / Handover). " +
                           "Drop the CSV into the commissioning agent's witnessing form, " +
                           "sign off PassFail + Date per row. Task library: corporate " +
                           "STING_CX_TASKS.json + project override at _BIM_COORD/cx/" +
                           "cx_tasks_override.json (class entries replace, not merge).");
                panel.Show();
                try { StingHvacPanel.Instance?.PushRunRow($"Cx checklist ({rows} rows → {Path.GetFileName(csvPath)})", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacGenerateCxChecklistCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Equipment classification ────────────────────────────────

        private static string ClassifyEquipment(string family, string type)
        {
            string s = ($"{family} {type}").ToLowerInvariant();
            if (s.Contains("ahu") || s.Contains("air handl")) return "AHU";
            if (s.Contains("vrv") || s.Contains("vrf"))       return "VRF";
            if (s.Contains("chiller"))                         return "Chiller";
            if (s.Contains("boiler"))                          return "Boiler";
            if (s.Contains("pump"))                            return "Pump";
            if (s.Contains("fan") || s.Contains("blower"))     return "Fan";
            if (s.Contains("fcu") || s.Contains("fan coil"))   return "FCU";
            if (s.Contains("vav"))                             return "VAV";
            if (s.Contains("heat pump") || s.Contains("hp"))   return "HeatPump";
            if (s.Contains("cooling tower"))                   return "CoolingTower";
            if (s.Contains("heat exch") || s.Contains("hx"))   return "HeatExchanger";
            if (s.Contains("damper"))                          return "Damper";
            return "Generic";
        }

        private static string SystemNameOf(FamilyInstance fi)
        {
            try
            {
                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return "";
                foreach (Connector c in conns)
                    if (c?.MEPSystem != null) return c.MEPSystem.Name ?? "";
            }
            catch { }
            return "";
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // ── Cx task library (JSON-driven, Phase 187e) ──────────────────
        //
        // Corporate baseline: Data/STING_CX_TASKS.json (shipped pack of 13
        // equipment classes × 4-11 ASHRAE Guideline 0 / CIBSE TM39 tasks).
        // Project override: <project>/_BIM_COORD/cx/cx_tasks_override.json.
        //
        // Override semantics: classes in the override REPLACE the corporate
        // set wholesale (not row-merge). A class absent from the override
        // keeps the corporate list. Use 'Generic' as the always-applies
        // fallback for unrecognised equipment.

        private class CxTask
        {
            public string Phase      = "";
            public string Task       = "";
            public string Method     = "";
            public string Acceptance = "";
            public CxTask(string p, string t, string m, string a) { Phase = p; Task = t; Method = m; Acceptance = a; }
        }

        private static readonly
            System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, List<CxTask>>>
            _libCache = new(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, List<CxTask>> LoadTaskLibrary(string projDir)
        {
            return _libCache.GetOrAdd(projDir ?? "<no-proj>", _ =>
            {
                var lib = new Dictionary<string, List<CxTask>>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string basePath = StingTools.Core.StingToolsApp.FindDataFile("STING_CX_TASKS.json");
                    if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                        ApplyTaskJson(File.ReadAllText(basePath), lib);
                    if (!string.IsNullOrEmpty(projDir))
                    {
                        string projPath = Path.Combine(projDir, "_BIM_COORD", "cx", "cx_tasks_override.json");
                        if (File.Exists(projPath))
                            ApplyTaskJson(File.ReadAllText(projPath), lib);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"LoadTaskLibrary: {ex.Message}"); }

                if (!lib.ContainsKey("Generic"))
                    lib["Generic"] = new List<CxTask>
                    {
                        new("PreInstall", "Approved submittal received", "Document review", "Stamped"),
                        new("Startup",    "Energize + functional check", "Power on",        "No alarms"),
                        new("Handover",   "O&M + as-built submitted",    "Document review", "Complete + indexed")
                    };
                return lib;
            });
        }

        private static void ApplyTaskJson(string jsonText, Dictionary<string, List<CxTask>> lib)
        {
            try
            {
                var j = JObject.Parse(jsonText);
                var classes = j["classes"] as JObject;
                if (classes == null) return;
                foreach (var kv in classes)
                {
                    if (kv.Key.StartsWith("_")) continue;

                    // Two value shapes supported:
                    //   1. Bare array → REPLACE the corporate class wholesale.
                    //   2. Object { "_merge": "append"|"replace", "tasks": [...] }
                    //      → respect the per-class merge mode.
                    JArray arr = null;
                    string mergeMode = "replace";
                    if (kv.Value is JArray a) arr = a;
                    else if (kv.Value is JObject o)
                    {
                        mergeMode = ((string)o["_merge"] ?? "replace").ToLowerInvariant();
                        arr = o["tasks"] as JArray;
                    }
                    if (arr == null) continue;

                    var rows = new List<CxTask>();
                    foreach (var row in arr.OfType<JObject>())
                    {
                        rows.Add(new CxTask(
                            (string)row["phase"] ?? "",
                            (string)row["task"] ?? "",
                            (string)row["method"] ?? "",
                            (string)row["acceptance"] ?? ""));
                    }

                    if (mergeMode == "append" && lib.TryGetValue(kv.Key, out var existing))
                    {
                        // APPEND: corporate rows kept, override rows added below.
                        // Dedupe by (Phase + Task) to keep re-runs idempotent.
                        var seen = new HashSet<string>(existing.Select(t => $"{t.Phase}|{t.Task}"),
                                                      StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows)
                            if (seen.Add($"{r.Phase}|{r.Task}")) existing.Add(r);
                    }
                    else
                    {
                        // REPLACE (default) — override rows clobber the corporate set.
                        lib[kv.Key] = rows;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ApplyTaskJson: {ex.Message}"); }
        }

        /// <summary>Drop the cached library so the next run re-reads from disk.</summary>
        public static void InvalidateTaskCache() => _libCache.Clear();
    }
}
