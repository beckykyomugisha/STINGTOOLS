// Phase 108m — LOD validation against RIBA stage + category minimum LOD.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.UI;

namespace StingTools.Core
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LODValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                string path = Path.Combine(StingToolsApp.DataPath ?? "", "LOD_REQUIREMENTS.json");
                if (!File.Exists(path))
                {
                    TaskDialog.Show("LOD Validation", "LOD_REQUIREMENTS.json not found.");
                    return Result.Failed;
                }

                var root = JObject.Parse(File.ReadAllText(path));
                string stage = TagConfig.GetConfigValue("BOQ_TENDER_WORK_STAGE")
                             ?? "RIBA Stage 4 — Technical Design";
                int targetLod = 300;
                if (root["stage_to_target_lod"]?[stage] != null)
                    int.TryParse(root["stage_to_target_lod"][stage].ToString(), out targetLod);
                var catMinMap = root["category_minimum_lod"] as JObject;

                int failed = 0, passed = 0, unknown = 0;
                var perCategoryFail = new Dictionary<string, int>();
                var failList = new List<string>();

                // Pack 1 — LOD-switch consumer state (STING_LOD_*_VISIBLE).
                int switchBearingTypes = 0;
                int switchMismatchTypes = 0;
                int switchAllOff = 0;
                var switchIssues = new List<string>();

                // Heuristic LOD scoring per element — parameter-presence based.
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (var el in collector)
                {
                    var cat = el.Category?.Name ?? "";
                    if (string.IsNullOrEmpty(cat)) continue;
                    JObject rule = catMinMap?[cat] as JObject;
                    if (rule == null) { unknown++; continue; }
                    int minLod = rule["any"]?.Value<int?>() ?? rule["shell"]?.Value<int?>() ?? rule["structural"]?.Value<int?>() ?? 300;
                    int effectiveMin = Math.Min(minLod, targetLod);

                    int score = ScoreElementLOD(el);
                    if (score >= effectiveMin) passed++;
                    else
                    {
                        failed++;
                        if (!perCategoryFail.ContainsKey(cat)) perCategoryFail[cat] = 0;
                        perCategoryFail[cat]++;
                        if (failList.Count < 20)
                            failList.Add($"• {cat} [{el.Id}] scored LOD {score} vs required {effectiveMin} — {MissingFor(el)}");
                    }
                }

                // Pack 1 — separate scan over family types to audit the LOD
                // visibility switches the Automation Presentation Pack injects.
                // Done as a type-level pass so we report once per family type
                // instead of once per instance (InjectAutomationPresentationPack
                // writes the three params at the type level).
                var typePass = new FilteredElementCollector(doc).WhereElementIsElementType();
                foreach (var t in typePass)
                {
                    int? c = ReadLodSwitch(t, "STING_LOD_COARSE_VISIBLE");
                    int? m = ReadLodSwitch(t, "STING_LOD_MEDIUM_VISIBLE");
                    int? f = ReadLodSwitch(t, "STING_LOD_FINE_VISIBLE");
                    if (c == null && m == null && f == null) continue;
                    switchBearingTypes++;

                    int c0 = c ?? 0, m0 = m ?? 0, f0 = f ?? 0;
                    if (c0 == 0 && m0 == 0 && f0 == 0)
                    {
                        switchAllOff++;
                        if (switchIssues.Count < 10)
                            switchIssues.Add($"• {t.Category?.Name} type '{t.Name}' [{t.Id}] — all LOD switches OFF, type is invisible at every detail level");
                    }
                    else if (c == null || m == null || f == null)
                    {
                        switchMismatchTypes++;
                        if (switchIssues.Count < 10)
                            switchIssues.Add($"• {t.Category?.Name} type '{t.Name}' [{t.Id}] — partial LOD-switch set (coarse={FmtBool(c)} medium={FmtBool(m)} fine={FmtBool(f)})");
                    }
                }

                var rp = StingResultPanel.Create("LOD Validation")
                    .SetSubtitle($"Stage: {stage} → target LOD {targetLod}")
                    .AddSection("COVERAGE")
                    .Metric("Passed",  passed.ToString())
                    .Metric("Failed",  failed.ToString())
                    .Metric("Uncategorised", unknown.ToString())
                    .Metric("Pass rate", $"{(passed + failed > 0 ? 100.0 * passed / (passed + failed) : 0):F1}%");
                if (perCategoryFail.Count > 0)
                {
                    rp.AddSection("FAILURES BY CATEGORY");
                    foreach (var kv in perCategoryFail.OrderByDescending(x => x.Value))
                        rp.Metric(kv.Key, kv.Value.ToString());
                }
                if (failList.Count > 0)
                {
                    rp.AddSection("FAILURES (first 20)");
                    foreach (var f in failList) rp.Text(f);
                }

                // Pack 1 — LOD-switch audit (STING_LOD_*_VISIBLE consumer).
                if (switchBearingTypes > 0)
                {
                    rp.AddSection("LOD SWITCHES (STING_LOD_*_VISIBLE)")
                      .Metric("Types carrying switches",  switchBearingTypes.ToString())
                      .Metric("All-off (invisible)",      switchAllOff.ToString())
                      .Metric("Partial (incomplete set)", switchMismatchTypes.ToString());
                    foreach (var msg in switchIssues) rp.Text(msg);
                }
                rp.Show();
                return failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("LODValidationCommand", ex); message = ex.Message; return Result.Failed; }
        }

        // Simple LOD scoring — presence of parameters drives the band.
        private static int ScoreElementLOD(Element el)
        {
            int s = 100;
            try
            {
                if (!string.IsNullOrEmpty(el.Name)) s = 200;
                if (el.LevelId != null && el.LevelId != ElementId.InvalidElementId) s = Math.Max(s, 250);
                try
                {
                    var matIds = el.GetMaterialIds(false);
                    if (matIds != null && matIds.Count > 0) s = Math.Max(s, 300);
                }
                catch { /* not all categories have materials */ }
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string loc  = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                if (!string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(loc)) s = Math.Max(s, 350);
                string mfr  = ParameterHelpers.GetString(el, "Manufacturer");
                string model = ParameterHelpers.GetString(el, "Model");
                if (!string.IsNullOrEmpty(mfr) && !string.IsNullOrEmpty(model)) s = Math.Max(s, 400);
                string serial = ParameterHelpers.GetString(el, "Serial Number");
                if (!string.IsNullOrEmpty(serial)) s = Math.Max(s, 500);
            }
            catch (Exception ex) { StingLog.Warn($"ScoreElementLOD: {ex.Message}"); }
            return s;
        }

        private static string MissingFor(Element el)
        {
            var missing = new List<string>();
            try
            {
                var matIds = el.GetMaterialIds(false);
                if (matIds == null || matIds.Count == 0) missing.Add("material");
            }
            catch (Exception ex) { StingLog.Warn($"LOD.MissingFor.GetMaterialIds {el?.Id}: {ex.Message}"); }
            if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.DISC))) missing.Add("DISC");
            if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.LOC))) missing.Add("LOC");
            return missing.Count == 0 ? "(unknown)" : "missing " + string.Join(", ", missing);
        }

        /// <summary>
        /// Pack 1 — reads one of the STING_LOD_*_VISIBLE YesNo type parameters.
        /// Returns null when the parameter is absent (family was never processed
        /// by InjectAutomationPresentationPack), 0/1 otherwise.
        /// </summary>
        private static int? ReadLodSwitch(Element type, string paramName)
        {
            if (type == null) return null;
            try
            {
                var p = type.LookupParameter(paramName);
                if (p == null) return null;
                if (p.StorageType == StorageType.Integer) return p.AsInteger() == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LODValidationCommand.ReadLodSwitch({paramName}) {type?.Id}: {ex.Message}");
            }
            return null;
        }

        private static string FmtBool(int? v) => v == null ? "—" : (v.Value == 0 ? "off" : "on");
    }
}
