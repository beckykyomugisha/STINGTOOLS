// ══════════════════════════════════════════════════════════════════════════
//  RegisterAuditCommand.cs — Materials_RegisterAudit. READ-ONLY.
//
//  Compares every layered host type whose NAME is a register MAT_NAME against
//  the build-up that register row declares, and reports the mismatches.
//
//  It exists because of 87 floor types. On 2026-09-09 Baseline_RenameTypes
//  proposed the identical name PLNS_SLB_RC100 for all 87; 86 of them are
//  register MAT_NAMEs verbatim, and every one is modelled as a single 100 mm
//  layer of "Concrete, Cast-in-Place gray" while the register declares its own
//  materials and thicknesses. The rename collision was the symptom; this reports
//  the cause, per type, so it can be seen before anything is decided.
//
//  IT WRITES NOTHING TO THE MODEL, and that is a design decision rather than a
//  first increment. Rebuilding a compound structure moves every element hosted
//  on that type; doing it for 87 types unattended, from a CSV, on a delivered
//  model, is a larger and less reversible act than the rename that started this.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Materials;

namespace StingTools.Commands.Materials
{
    [Transaction(TransactionMode.ReadOnly)]
    public class RegisterAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var registry = LoadRegistry(out string loadNote);
                if (registry.Count == 0)
                {
                    // A register that loaded nothing must say so, not report "0 mismatches".
                    TaskDialog.Show("Register Audit",
                        "The material register did not load, so there is nothing to compare "
                      + "against:\n\n" + loadNote);
                    return Result.Failed;
                }

                var types = ReadHostTypes(doc);
                var rows = HostTypeRegisterAudit.Audit(types, registry);

                string path = WriteCsv(doc, HostTypeRegisterAudit.ToCsv(rows));

                var findings = rows.Where(r => r.IsFinding)
                                   .OrderByDescending(r => r.InstanceCount)
                                   .ThenBy(r => r.TypeName).ToList();

                var sb = new StringBuilder();
                sb.AppendLine(HostTypeRegisterAudit.Summary(rows));
                if (!string.IsNullOrEmpty(loadNote)) { sb.AppendLine(loadNote); sb.AppendLine(); }
                foreach (var r in findings.Take(12))
                    sb.AppendLine($"  {r.InstanceCount,4} x  {r.TypeName}\n           {r.Detail}");
                if (findings.Count > 12)
                    sb.AppendLine($"  … and {findings.Count - 12} more, in the CSV");
                if (path != null) { sb.AppendLine(); sb.AppendLine("Report: " + path); }

                TaskDialog.Show("Register Audit", sb.ToString());
                StingLog.Info($"Materials_RegisterAudit: {rows.Count} types, {findings.Count} findings -> {path}");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("Materials_RegisterAudit", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// The corporate register from the deployed data folder. Returns an EMPTY registry
        /// and a note when a file is missing — never null, and never a silent zero.
        /// </summary>
        internal static MaterialRegistry LoadRegistry(out string note)
        {
            string ble = Read("BLE_MATERIALS.csv");
            string mep = Read("MEP_MATERIALS.csv");
            var reg = MaterialRegistry.Parse(ble, mep);
            note = reg.Warnings.Count == 0
                ? null
                : "Register load notes:\n  " + string.Join("\n  ", reg.Warnings.Take(6));
            return reg;
        }

        private static string Read(string fileName)
        {
            try
            {
                string p = StingToolsApp.FindDataFile(fileName);
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Register audit: reading {fileName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Every layered host type, reduced to what the audit compares.</summary>
        private static List<ModelledHostType> ReadHostTypes(Document doc)
        {
            var counts = new Dictionary<long, int>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType()
                                      .OfClass(typeof(HostObject)))
            {
                var tid = el.GetTypeId();
                if (tid == null || tid.Value <= 0) continue;
                counts.TryGetValue(tid.Value, out int n);
                counts[tid.Value] = n + 1;
            }

            var outList = new List<ModelledHostType>();
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(HostObjAttributes))
                                 .Cast<HostObjAttributes>())
            {
                string cat = t.Category?.Name;
                if (string.IsNullOrEmpty(cat)) continue;

                var mt = new ModelledHostType { Category = cat, TypeName = t.Name };
                counts.TryGetValue(t.Id.Value, out int used);
                mt.InstanceCount = used;

                // W3 — what the type RECORDS it was built from. Preferred over the name
                // match, and only ever set by CompoundTypeCreator, so a type from any
                // other source keeps auditing by name exactly as before.
                try
                {
                    var prov = StingTools.Core.Storage.StingProvenanceSchema.Read(t);
                    if (prov != null
                        && string.Equals(prov.Engine, "CompoundTypeCreator", StringComparison.OrdinalIgnoreCase))
                        mt.RecordedCode = prov.RuleId ?? "";
                }
                catch (Exception ex)
                { StingLog.WarnRateLimited("RegAudit.Prov", $"provenance {t.Id}: {ex.Message}"); }

                CompoundStructure cs = null;
                try { cs = t.GetCompoundStructure(); }
                catch (Exception ex)
                { StingLog.WarnRateLimited("RegAudit.CS", $"GetCompoundStructure {t.Id}: {ex.Message}"); }

                if (cs != null)
                {
                    var layers = cs.GetLayers();
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var l = layers[i];
                        string name = null;
                        if (l.MaterialId != null && l.MaterialId.Value > 0)
                            name = doc.GetElement(l.MaterialId)?.Name;
                        mt.Layers.Add(new ModelledLayer
                        {
                            MaterialName = name ?? "",
                            ThicknessMm = l.Width * 304.8,
                            Function = l.Function.ToString(),
                        });
                    }
                }
                outList.Add(mt);
            }
            return outList;
        }

        private static string WriteCsv(Document doc, IEnumerable<string> rows)
        {
            try
            {
                string dir = StingPaths.Meta(doc, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"register_audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("register_audit CSV: " + ex.Message); return null; }
        }
    }
}
