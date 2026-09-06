// TypeScheduleCommands.cs — 3B. Door/window TYPE schedules.
//
// WHY THIS EXISTS
// ---------------
// STING has never produced a type schedule. Revit collapses an itemised schedule
// into one row per type with ScheduleDefinition.IsItemized = false, and that flag
// is READ in two places (ScheduleEnhancementCommands.cs:1175, :1423) and set true
// exactly once (PlumbingSpoolCommands.cs:208). It is never set false anywhere in
// the plugin, so no collapsed schedule has ever been produced.
//
// THE TWO SCHEDULES ARE NOT ALTERNATIVES
// --------------------------------------
// The itemised schedule is the REGISTER: every placed door, where it is, one row
// each. The type schedule is the SPECIFICATION: one row per product, with the
// count. On Kibale that is ~96 rows against ~12 — and it is the 12 that a
// contractor orders against and a reviewer checks. Both are kept.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTypeScheduleCommand : IExternalCommand
    {
        // Ordered: identity first, then the fields a supplier prices, then the count.
        // Each entry is (BuiltInParameter or shared-param name, heading).
        private static readonly (BuiltInParameter Bip, string Shared, string Heading)[] Fields =
        {
            (BuiltInParameter.ALL_MODEL_TYPE_MARK,  null,                    "Type Mark"),
            (BuiltInParameter.SYMBOL_NAME_PARAM,    null,                    "Type"),
            (BuiltInParameter.INVALID,              "WIDTH",                 "Width"),
            (BuiltInParameter.INVALID,              "HEIGHT",                "Height"),
            (BuiltInParameter.INVALID,              "BLE_FIRE_RATING_TXT",   "Fire rating"),
            (BuiltInParameter.INVALID,              "BLE_IRONMONGERY_TXT",   "Ironmongery set"),
            (BuiltInParameter.INVALID,              "BLE_FINISH_TXT",        "Finish"),
        };

        private static readonly (BuiltInCategory Cat, string Label)[] Targets =
        {
            (BuiltInCategory.OST_Doors,   "Door"),
            (BuiltInCategory.OST_Windows, "Window"),
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var made = new List<string>();
            var warn = new List<string>();

            using (var tx = new Transaction(doc, "STING Create Type Schedules"))
            {
                tx.Start();
                foreach (var (cat, label) in Targets)
                {
                    try
                    {
                        string name = $"STING {label} Type Schedule";
                        var existing = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                            .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) { warn.Add($"'{name}' already exists — left alone."); continue; }

                        var vs = ViewSchedule.CreateSchedule(doc, new ElementId(cat));
                        vs.Name = name;
                        var def = vs.Definition;

                        // THE POINT OF THIS COMMAND. Collapses the itemised rows into one
                        // per distinct field-set; grouping on Type Mark then yields one row
                        // per product.
                        def.IsItemized = false;

                        var available = def.GetSchedulableFields();
                        ScheduleField countField = null;

                        foreach (var f in Fields)
                        {
                            var sf = FindField(doc, available, f);
                            if (sf == null) { warn.Add($"{label}: no schedulable field for '{f.Heading}'."); continue; }
                            try
                            {
                                var added = def.AddField(sf);
                                added.ColumnHeading = f.Heading;
                            }
                            catch (Exception ex) { warn.Add($"{label} field '{f.Heading}': {ex.Message}"); }
                        }

                        // The count column — what makes a collapsed schedule a register of
                        // quantities rather than just a list of products.
                        var countSf = available.FirstOrDefault(s => s.FieldType == ScheduleFieldType.Count);
                        if (countSf != null)
                        {
                            countField = def.AddField(countSf);
                            countField.ColumnHeading = "Count";
                        }
                        else warn.Add($"{label}: no Count field available.");

                        // Sort/group on Type Mark so identical products land together.
                        var markField = def.GetFieldOrder()
                            .Select(id => def.GetField(id))
                            .FirstOrDefault(f => f.ColumnHeading == "Type Mark");
                        if (markField != null)
                        {
                            var sort = new ScheduleSortGroupField(markField.FieldId)
                            {
                                SortOrder = ScheduleSortOrder.Ascending
                            };
                            def.AddSortGroupField(sort);
                        }

                        made.Add(name);
                    }
                    catch (Exception ex)
                    {
                        warn.Add($"{label}: {ex.Message}");
                        StingLog.Error($"CreateTypeSchedule {label}", ex);
                    }
                }
                tx.Commit();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(made.Count > 0
                ? "Created:\n   " + string.Join("\n   ", made)
                : "No schedules created.");
            sb.AppendLine();
            sb.AppendLine("These are COLLAPSED schedules (IsItemized = false) — one row per type,");
            sb.AppendLine("with a count. Your existing itemised schedules are untouched: the two are");
            sb.AppendLine("the specification and the register, not alternatives.");
            if (warn.Count > 0)
            {
                sb.AppendLine();
                foreach (var w in warn.Take(12)) sb.AppendLine("! " + w);
            }
            TaskDialog.Show("Type Schedules", sb.ToString());
            return Result.Succeeded;
        }

        /// <summary>
        /// Resolve one spec to a schedulable field: by BuiltInParameter id when we have
        /// one, otherwise by the field's display name (which is how shared parameters
        /// and Revit's own Width/Height surface here).
        /// <para>
        /// Returns null when the field genuinely is not schedulable for this category —
        /// the caller reports that rather than adding nothing and claiming success.
        /// </para>
        /// </summary>
        private static SchedulableField FindField(
            Document doc, IList<SchedulableField> available,
            (BuiltInParameter Bip, string Shared, string Heading) spec)
        {
            // 1. Exact BuiltInParameter match.
            if (spec.Bip != BuiltInParameter.INVALID)
            {
                foreach (var sf in available)
                {
                    try
                    {
                        if (sf.ParameterId != null && sf.ParameterId.Value == (long)spec.Bip)
                            return sf;
                    }
                    catch (Exception ex) { StingLog.Warn($"TypeSchedule field probe: {ex.Message}"); }
                }
                return null;
            }

            // 2. Name match — exact first, then the heading as a fallback so a project
            //    whose shared parameter is absent still picks up Revit's native Width /
            //    Height rather than silently dropping the column.
            string[] candidates = string.IsNullOrEmpty(spec.Shared)
                ? new[] { spec.Heading }
                : new[] { spec.Shared, spec.Heading };

            foreach (var want in candidates)
            {
                foreach (var sf in available)
                {
                    string n;
                    try { n = sf.GetName(doc); }
                    catch { continue; }
                    if (string.Equals(n, want, StringComparison.OrdinalIgnoreCase))
                        return sf;
                }
            }
            return null;
        }
    }
}
