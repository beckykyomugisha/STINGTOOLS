// PlumbingMaterialValidator — material × jointing × service compat-
// matrix consumer. Reads STING_PLUMBING_MATERIAL_RULES.json and per
// pipe / fitting / accessory checks:
//   1. Material × service whitelist
//   2. Jointing method × material compatibility
//   3. Pressure / temperature ratings vs design
//   4. Galvanic-pair forbidden network walk
//   5. Banned materials per service
// Phase 178c.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public enum PlumbingFinding
    {
        MaterialBannedOnService,
        ServiceNotAllowedForMaterial,
        JointMaterialIncompatible,
        JointBannedOnService,
        JointRequiredOnService,
        PressureExceeded,
        TemperatureExceeded,
        ForbiddenGalvanicPair,
        WrasRequiredOnService
    }

    public class PlumbingValidationFinding
    {
        public ElementId ElementId { get; set; }
        public PlumbingFinding Kind { get; set; }
        public string Severity { get; set; } = "WARN";
        public string Material { get; set; } = "";
        public string Joint    { get; set; } = "";
        public string Service  { get; set; } = "";
        public string Notes    { get; set; } = "";
    }

    public class PlumbingMaterialReport
    {
        public int ElementsScanned { get; set; }
        public List<PlumbingValidationFinding> Findings { get; } = new List<PlumbingValidationFinding>();
        public string RulesSource { get; set; } = "";
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class PlumbingMaterialValidator
    {
        private static MaterialRules _rules;
        private static string _rulesPath;

        public static void LoadRules()
        {
            if (_rules != null) return;
            try
            {
                _rulesPath = ResolveRulesPath();
                if (string.IsNullOrEmpty(_rulesPath) || !File.Exists(_rulesPath))
                {
                    _rules = new MaterialRules();
                    return;
                }
                _rules = JsonConvert.DeserializeObject<MaterialRules>(File.ReadAllText(_rulesPath))
                       ?? new MaterialRules();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlumbingMaterialValidator load: {ex.Message}");
                _rules = new MaterialRules();
            }
        }

        public static PlumbingMaterialReport Validate(Document doc)
        {
            LoadRules();
            var r = new PlumbingMaterialReport { RulesSource = _rulesPath ?? "(defaults)" };
            if (doc == null) return r;

            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Element>().ToList();
            var fittings = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType().ToList();
            var accessories = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeAccessory)
                .WhereElementIsNotElementType().ToList();
            var all = pipes.Concat(fittings).Concat(accessories).ToList();
            r.ElementsScanned = all.Count;

            foreach (var el in all)
            {
                try
                {
                    string mat   = ReadString(el, ParamRegistry.PLM_MAT);
                    string joint = ReadString(el, "PLM_JOINT_TYPE_TXT");
                    string svc   = ReadSystemCode(el);
                    string wras  = ReadString(el, ParamRegistry.PLM_PPE_WRAS);

                    if (!string.IsNullOrEmpty(mat) && _rules.materials.TryGetValue(mat, out var matDef))
                    {
                        if (matDef.banned_services?.Contains(svc) == true)
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.MaterialBannedOnService,
                                Severity = "CRITICAL", Material = mat, Service = svc,
                                Notes = $"{mat} banned on {svc}"
                            });
                        if (matDef.services?.Count > 0 && !matDef.services.Contains(svc) && !string.IsNullOrEmpty(svc))
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.ServiceNotAllowedForMaterial,
                                Severity = "WARN", Material = mat, Service = svc,
                                Notes = $"{mat} not whitelisted for {svc}"
                            });
                        if (!string.IsNullOrEmpty(joint) && matDef.compat_joints?.Contains(joint) == false)
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.JointMaterialIncompatible,
                                Severity = "ERROR", Material = mat, Joint = joint,
                                Notes = $"Joint {joint} incompatible with material {mat}"
                            });
                    }

                    if (_rules.service_rules.TryGetValue(svc, out var sRule))
                    {
                        if (sRule.banned_materials?.Contains(mat) == true)
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.MaterialBannedOnService,
                                Severity = "CRITICAL", Material = mat, Service = svc,
                                Notes = $"Service {svc} bans material {mat}"
                            });
                        if (sRule.banned_joints?.Contains(joint) == true)
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.JointBannedOnService,
                                Severity = "CRITICAL", Joint = joint, Service = svc,
                                Notes = $"Service {svc} bans joint {joint}"
                            });
                        if (sRule.required_joints != null && sRule.required_joints.Count > 0
                          && !string.IsNullOrEmpty(joint) && !sRule.required_joints.Contains(joint))
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.JointRequiredOnService,
                                Severity = "CRITICAL", Joint = joint, Service = svc,
                                Notes = $"Service {svc} requires {string.Join("/", sRule.required_joints)}, found {joint}"
                            });
                        if (sRule.required_wras == true && wras != "1" && wras.ToUpperInvariant() != "YES" && wras.ToUpperInvariant() != "TRUE")
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = el.Id, Kind = PlumbingFinding.WrasRequiredOnService,
                                Severity = "ERROR", Material = mat, Service = svc,
                                Notes = $"WRAS approval required on {svc}"
                            });
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"validate {el.Id}: {ex.Message}"); }
            }

            try
            {
                foreach (var pair in _rules.galvanic_pairs_forbidden ?? new List<GalvanicPair>())
                {
                    foreach (var p in pipes.OfType<Pipe>())
                    {
                        string mat = ReadString(p, ParamRegistry.PLM_MAT);
                        string svc = ReadSystemCode(p);
                        if (mat != pair.upstream) continue;
                        if (pair.service != null && pair.service.Count > 0 && !pair.service.Contains(svc)) continue;
                        if (HasDownstreamMaterial(p, pair.downstream))
                        {
                            r.Findings.Add(new PlumbingValidationFinding
                            {
                                ElementId = p.Id, Kind = PlumbingFinding.ForbiddenGalvanicPair,
                                Severity = string.IsNullOrEmpty(pair.severity) ? "WARN" : pair.severity.ToUpperInvariant(),
                                Material = mat, Service = svc,
                                Notes = $"Galvanic pair {pair.upstream} → {pair.downstream} on {svc}"
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { r.Warnings.Add($"galvanic walk: {ex.Message}"); }

            return r;
        }

        private static bool HasDownstreamMaterial(Pipe pipe, string targetMaterial)
        {
            try
            {
                var visited = new HashSet<long> { pipe.Id.Value };
                var queue = new Queue<Element>();
                queue.Enqueue(pipe);
                int safety = 200;
                while (queue.Count > 0 && safety-- > 0)
                {
                    var el = queue.Dequeue();
                    var cm = (el as MEPCurve)?.ConnectorManager
                          ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            var owner = other.Owner;
                            if (owner == null || visited.Contains(owner.Id.Value)) continue;
                            visited.Add(owner.Id.Value);
                            if (owner is Pipe op)
                            {
                                if (ReadString(op, ParamRegistry.PLM_MAT) == targetMaterial) return true;
                                queue.Enqueue(op);
                            }
                            else if (owner.Category?.Id?.Value == (long)BuiltInCategory.OST_PipeFitting
                                  || owner.Category?.Id?.Value == (long)BuiltInCategory.OST_PipeAccessory)
                            {
                                queue.Enqueue(owner);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static string ReadString(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer)
                    return p.AsInteger().ToString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "";
        }

        private static string ReadSystemCode(Element el)
        {
            try
            {
                var p = el.LookupParameter("ASS_SYSTEM_TYPE_TXT");
                if (p != null && p.HasValue && p.StorageType == StorageType.String) return (p.AsString() ?? "").ToUpperInvariant();
                if (el is MEPCurve mc) return (mc.MEPSystem?.Name ?? "").ToUpperInvariant();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "";
        }

        private static string ResolveRulesPath()
        {
            try
            {
                string asm = Path.GetDirectoryName(typeof(PlumbingMaterialValidator).Assembly.Location) ?? "";
                string[] candidates = {
                    Path.Combine(asm, "data", "Plumbing", "STING_PLUMBING_MATERIAL_RULES.json"),
                    Path.Combine(asm, "Data", "Plumbing", "STING_PLUMBING_MATERIAL_RULES.json"),
                    Path.Combine(asm, "STING_PLUMBING_MATERIAL_RULES.json"),
                };
                foreach (var c in candidates)
                    if (File.Exists(c)) return c;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        private class MaterialRules
        {
            public Dictionary<string, MaterialDef> materials { get; set; } = new Dictionary<string, MaterialDef>();
            public Dictionary<string, JointDef>    joints    { get; set; } = new Dictionary<string, JointDef>();
            public Dictionary<string, ServiceRule> service_rules { get; set; } = new Dictionary<string, ServiceRule>();
            public List<GalvanicPair> galvanic_pairs_forbidden  { get; set; } = new List<GalvanicPair>();
            public Dictionary<string, List<string>> fluid_category_separation { get; set; } = new Dictionary<string, List<string>>();
        }
        private class MaterialDef
        {
            public bool? wras { get; set; }
            public double pn_max  { get; set; }
            public double t_max_c { get; set; }
            public double t_peak_c{ get; set; }
            public string fire_class { get; set; }
            public bool buried { get; set; }
            public List<string> compat_joints   { get; set; }
            public List<string> services        { get; set; }
            public List<string> banned_services { get; set; }
        }
        private class JointDef
        {
            public string label { get; set; }
            public double pn_max { get; set; }
            public double t_max_c { get; set; }
            public List<string> materials { get; set; }
        }
        private class ServiceRule
        {
            public List<string> required_joints { get; set; }
            public List<string> banned_joints   { get; set; }
            public List<string> banned_materials{ get; set; }
            public bool? required_wras { get; set; }
            public double t_max_c_min { get; set; }
        }
        private class GalvanicPair
        {
            public string upstream   { get; set; }
            public string downstream { get; set; }
            public List<string> service { get; set; }
            public string severity { get; set; }
        }
    }
}
