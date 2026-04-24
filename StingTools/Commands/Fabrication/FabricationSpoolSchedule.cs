// StingTools v4 MVP — auto-create a Revit Spool Schedule (#13).
//
// After Generate Package commits, this creates or refreshes a
// ViewSchedule targeting BuiltInCategory.OST_Assemblies with one row
// per AssemblyInstance. Fields include: assembly name, element
// count, total length, total weight, weld count. The schedule lives
// under "STING v4 — Spool Schedule" in the project browser, and is
// recreated (deleted + rebuilt) on every call so the field layout
// stays canonical.

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public static class FabricationSpoolSchedule
    {
        public const string ScheduleName = "STING v4 — Spool Schedule";

        public static ElementId CreateOrRefresh(Document doc)
        {
            if (doc == null) return ElementId.InvalidElementId;
            try
            {
                using (var tg = new TransactionGroup(doc, "STING v4 — Spool Schedule"))
                {
                    tg.Start();
                    // Delete any prior schedule with this name so the
                    // field layout stays canonical.
                    using (var t1 = new Transaction(doc, "Delete prior spool schedule"))
                    {
                        t1.Start();
                        var prior = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSchedule))
                            .Cast<ViewSchedule>()
                            .FirstOrDefault(s => string.Equals(s.Name, ScheduleName, StringComparison.OrdinalIgnoreCase));
                        if (prior != null) doc.Delete(prior.Id);
                        t1.Commit();
                    }

                    ElementId scheduleId;
                    using (var t2 = new Transaction(doc, "Create spool schedule"))
                    {
                        t2.Start();
                        var vs = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Assemblies));
                        vs.Name = ScheduleName;
                        var sd = vs.Definition;

                        // Add fields by BuiltInParameter + shared params.
                        AddField(sd, BuiltInParameter.ASSEMBLY_NAME);
                        AddField(sd, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        AddSharedField(sd, doc, "ASS_DISCIPLINE_COD_TXT");
                        AddSharedField(sd, doc, "ASS_SYSTEM_TYPE_TXT");
                        AddSharedField(sd, doc, "ASS_LVL_COD_TXT");
                        AddSharedField(sd, doc, "ASS_LENGTH_TOTAL_MM");
                        AddSharedField(sd, doc, "ASS_WEIGHT_KG");
                        AddSharedField(sd, doc, "ASS_WELD_COUNT_NR");

                        scheduleId = vs.Id;
                        t2.Commit();
                    }
                    tg.Assimilate();
                    return scheduleId;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationSpoolSchedule.CreateOrRefresh", ex);
                return ElementId.InvalidElementId;
            }
        }

        private static void AddField(ScheduleDefinition sd, BuiltInParameter bip)
        {
            try
            {
                var fields = sd.GetSchedulableFields();
                var f = fields.FirstOrDefault(x => x.ParameterId.Value == (long)(int)bip);
                if (f != null) sd.AddField(f);
            }
            catch (Exception ex) { StingLog.Warn($"SpoolSchedule.AddField {bip}: {ex.Message}"); }
        }

        private static void AddSharedField(ScheduleDefinition sd, Document doc, string paramName)
        {
            try
            {
                var fields = sd.GetSchedulableFields();
                foreach (var f in fields)
                {
                    string name = "";
                    try { name = f.GetName(doc); } catch { }
                    if (string.Equals(name, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        sd.AddField(f);
                        return;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpoolSchedule.AddSharedField {paramName}: {ex.Message}"); }
        }
    }
}
