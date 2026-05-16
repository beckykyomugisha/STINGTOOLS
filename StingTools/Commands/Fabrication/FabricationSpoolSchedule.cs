// StingTools v4 MVP — auto-create a Revit Spool Schedule (#13).
//
// After Generate Package commits, creates or refreshes a ViewSchedule
// targeting OST_Assemblies with one row per AssemblyInstance. Fields
// added when bindable: assembly name, comments, discipline, system,
// level, length total, weight, weld count.
//
// Hardening pass: mirrors the null-guard + per-field pattern already
// proven in ExLink/ISBAppsCommands.cs — null return from
// ViewSchedule.CreateSchedule(OST_Assemblies) is treated as a recovery
// path rather than an unhandled exception, and SchedulableField.GetName
// is called with (doc) exactly like every other call site in the
// codebase (SheetIndexCommand, MEPScheduleCommands, etc.). Count of
// fields actually added is returned so the workspace dialog can tell
// the user which shared params aren't bound to the Assemblies category
// yet (usually fixable by running "Sync Parameter Schema" from Tags →
// QA).

using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public static class FabricationSpoolSchedule
    {
        public const string ScheduleName = "STING v4 — Spool Schedule";

        public class CreateResult
        {
            public ElementId ScheduleId { get; set; } = ElementId.InvalidElementId;
            public int  FieldsAdded  { get; set; }
            public int  FieldsMissing { get; set; }
            public string Message    { get; set; } = "";
            public bool Ok           => ScheduleId != ElementId.InvalidElementId;
        }

        public static CreateResult CreateOrRefresh(Document doc)
        {
            var r = new CreateResult();
            if (doc == null) { r.Message = "No document."; return r; }
            try
            {
                using (var tg = new TransactionGroup(doc, "STING v4 — Spool Schedule"))
                {
                    tg.Start();
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

                    ViewSchedule vs = null;
                    using (var t2 = new Transaction(doc, "Create spool schedule"))
                    {
                        t2.Start();
                        // OST_Assemblies is a schedulable category on Revit 2025/
                        // 2026/2027; guard the null-return case per the pattern
                        // in ISBAppsCommands.CreateScheduleFromTemplate.
                        try
                        {
                            vs = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Assemblies));
                        }
                        catch (Exception ex)
                        {
                            r.Message = $"CreateSchedule(OST_Assemblies) threw: {ex.Message}";
                            StingLog.Warn(r.Message);
                            t2.RollBack();
                            tg.RollBack();
                            return r;
                        }
                        if (vs == null)
                        {
                            r.Message = "CreateSchedule returned null for OST_Assemblies.";
                            StingLog.Warn(r.Message);
                            t2.RollBack();
                            tg.RollBack();
                            return r;
                        }

                        try { vs.Name = ScheduleName; }
                        catch
                        {
                            try { vs.Name = $"{ScheduleName}_{DateTime.Now:HHmmss}"; }
                            catch (Exception ex) { StingLog.Warn($"SpoolSchedule rename fallback: {ex.Message}"); }
                        }

                        var sd = vs.Definition;
                        // BuiltInParameter fields first (always resolvable)
                        AddField(sd, BuiltInParameter.ASSEMBLY_NAME, r);
                        AddField(sd, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, r);
                        // Shared-parameter fields — only resolvable when the
                        // parameter is bound to the Assemblies category.
                        AddSharedField(sd, doc, "ASS_DISCIPLINE_COD_TXT", r);
                        AddSharedField(sd, doc, "ASS_SYSTEM_TYPE_TXT",   r);
                        AddSharedField(sd, doc, "ASS_LVL_COD_TXT",       r);
                        AddSharedField(sd, doc, "ASS_LENGTH_TOTAL_MM",   r);
                        AddSharedField(sd, doc, "ASS_WEIGHT_KG",         r);
                        AddSharedField(sd, doc, "ASS_WELD_COUNT_NR",     r);

                        r.ScheduleId = vs.Id;
                        t2.Commit();
                    }
                    tg.Assimilate();
                }
                r.Message = r.FieldsMissing == 0
                    ? $"Spool schedule refreshed ({r.FieldsAdded} fields)."
                    : $"Spool schedule refreshed ({r.FieldsAdded} fields; {r.FieldsMissing} shared-params not bound to Assemblies — run Tags → QA → Sync Parameter Schema).";
                return r;
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationSpoolSchedule.CreateOrRefresh", ex);
                r.Message = ex.Message;
                return r;
            }
        }

        private static void AddField(ScheduleDefinition sd, BuiltInParameter bip, CreateResult r)
        {
            try
            {
                var fields = sd.GetSchedulableFields();
                var f = fields.FirstOrDefault(x => x.ParameterId.Value == (long)(int)bip);
                if (f != null) { sd.AddField(f); r.FieldsAdded++; }
                else r.FieldsMissing++;
            }
            catch (Exception ex) { StingLog.Warn($"SpoolSchedule.AddField {bip}: {ex.Message}"); r.FieldsMissing++; }
        }

        private static void AddSharedField(ScheduleDefinition sd, Document doc, string paramName, CreateResult r)
        {
            try
            {
                var fields = sd.GetSchedulableFields();
                foreach (var f in fields)
                {
                    string name = "";
                    try { name = f.GetName(doc); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                    if (string.Equals(name, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        sd.AddField(f);
                        r.FieldsAdded++;
                        return;
                    }
                }
                r.FieldsMissing++;
            }
            catch (Exception ex) { StingLog.Warn($"SpoolSchedule.AddSharedField {paramName}: {ex.Message}"); r.FieldsMissing++; }
        }
    }
}
