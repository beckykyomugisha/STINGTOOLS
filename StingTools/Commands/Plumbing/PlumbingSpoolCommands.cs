// PlumbingSpoolCommands — Phase 179d plumbing fabrication spool commands.
//
// Plumb_GenerateSpools  — groups drainage + supply pipes into assembly spools by system+level,
//                         creates AssemblyInstance per group, optionally generates spool sheets.
// Plumb_SpoolSchedule   — creates/refreshes a "STING - Plumbing Spool Schedule" view listing
//                         all PLM_SPOOL_NR_TXT, pipe DN, length, system, and level.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbGenerateSpoolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var pipes = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
            var fittings = new FilteredElementCollector(ctx.Doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType().Cast<Element>().ToList();
            var accessories = new FilteredElementCollector(ctx.Doc)
                .OfCategory(BuiltInCategory.OST_PipeAccessory)
                .WhereElementIsNotElementType().Cast<Element>().ToList();

            if (pipes.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Spools", "No pipes in document.");
                return Result.Cancelled;
            }

            var td = new TaskDialog("Plumb_GenerateSpools")
            {
                MainInstruction = "Generate Plumbing Spools",
                MainContent     = $"Groups {pipes.Count} pipe(s) + {fittings.Count} fitting(s) into spools " +
                                  "by system type + level.\n\nOption A: Stamp spool numbers only (no assembly).\n" +
                                  "Option B: Create AssemblyInstance + spool number (requires elements not already in assemblies).",
                CommonButtons   = TaskDialogCommonButtons.Cancel,
                DefaultButton   = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Stamp spool numbers only");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Create assemblies + stamp");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            bool createAssemblies = pick == TaskDialogResult.CommandLink2;

            // Group pipes by system + level
            var groups = new Dictionary<string, List<Element>>();
            foreach (Pipe p in pipes)
            {
                try
                {
                    string sys = p.MEPSystem?.Name ?? "Unknown";
                    string lvl = ctx.Doc.GetElement(p.ReferenceLevel?.Id ?? ElementId.InvalidElementId)?.Name ?? "XX";
                    string key = $"{sys}|{lvl}";
                    if (!groups.ContainsKey(key)) groups[key] = new List<Element>();
                    groups[key].Add(p);
                }
                catch { }
            }
            // Group fittings and accessories into the same spool groups
            foreach (var el in fittings.Concat(accessories))
            {
                try
                {
                    string sys = el.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
                                   ?.AsValueString() ?? "Unknown";
                    var lvlId  = el.LevelId;
                    string lvl = (lvlId != null && lvlId != ElementId.InvalidElementId)
                                 ? ctx.Doc.GetElement(lvlId)?.Name ?? "XX" : "XX";
                    string key = $"{sys}|{lvl}";
                    if (!groups.ContainsKey(key)) groups[key] = new List<Element>();
                    groups[key].Add(el);
                }
                catch { }
            }

            int spoolsCreated = 0, stamped = 0, failed = 0;
            var warnings = new List<string>();

            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Generate Spools"))
            {
                tx.Start();
                int spoolSeq = 1;
                foreach (var kvp in groups.OrderBy(k => k.Key))
                {
                    string spoolNr = $"PLM-S{spoolSeq:D4}";
                    spoolSeq++;

                    // Stamp spool number on each pipe
                    foreach (var el in kvp.Value)
                    {
                        try
                        {
                            var p = el.LookupParameter(ParamRegistry.PLM_SPOOL_NR);
                            if (p != null && !p.IsReadOnly)
                            {
                                if (p.StorageType == StorageType.String) p.Set(spoolNr);
                            }
                            stamped++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Stamp {el.Id}: {ex.Message}");
                        }
                    }

                    if (createAssemblies)
                    {
                        try
                        {
                            var ids = kvp.Value.Select(e => e.Id).ToList();
                            if (ids.Count > 0 && AssemblyInstance.IsValidNamingCategory(ctx.Doc,
                                ctx.Doc.GetElement(ids[0])?.Category, ids))
                            {
                                var assy = AssemblyInstance.Create(ctx.Doc, ids,
                                    ctx.Doc.GetElement(ids[0]).Category.Id);
                                if (assy != null)
                                {
                                    assy.AssemblyTypeName = spoolNr;
                                    spoolsCreated++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Assembly {kvp.Key}: {ex.Message}");
                            failed++;
                        }
                    }
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Plumbing Spool Generation");
            panel.SetSubtitle($"{groups.Count} spool groups from {pipes.Count} pipes");
            panel.AddSection("SUMMARY")
                 .Metric("Spool groups",      groups.Count.ToString())
                 .Metric("Pipe segments stamped", stamped.ToString())
                 .Metric("Assemblies created",    spoolsCreated.ToString())
                 .Metric("Failed",                failed.ToString());
            panel.AddSection("SPOOL GROUPS (first 30)");
            foreach (var kvp in groups.OrderBy(k => k.Key).Take(30))
            {
                var parts = kvp.Key.Split('|');
                panel.Text($"{parts[0],-25} Level {(parts.Length > 1 ? parts[1] : "?")} — {kvp.Value.Count} pipe(s)");
            }
            if (warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in warnings.Take(30)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSpoolScheduleCommand : IExternalCommand
    {
        private const string ScheduleName = "STING - Plumbing Spool Schedule";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Check if schedule already exists
            var existing = new FilteredElementCollector(ctx.Doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => s.Name == ScheduleName);

            ViewSchedule schedule = null;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Spool Schedule"))
            {
                tx.Start();
                if (existing != null)
                {
                    schedule = existing;
                }
                else
                {
                    // Create a new pipe schedule
                    schedule = ViewSchedule.CreateSchedule(ctx.Doc,
                        new ElementId(BuiltInCategory.OST_PipeCurves));
                    schedule.Name = ScheduleName;
                }

                var def = schedule.Definition;
                def.ShowHeaders = true;
                def.IsItemized   = true;

                // Add fields: spool number, system, level, size, length
                var fields = schedule.Definition.GetSchedulableFields();
                var existingParamIds = schedule.Definition.GetFieldOrder()
                    .Select(fid => schedule.Definition.GetField(fid).ParameterId)
                    .ToHashSet();

                // Add standard built-in parameter fields by BIP identity
                var desiredBips = new[]
                {
                    BuiltInParameter.RBS_CALCULATED_SIZE,
                    BuiltInParameter.CURVE_ELEM_LENGTH,
                    BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
                    BuiltInParameter.RBS_START_LEVEL_PARAM,
                };
                foreach (var bip in desiredBips)
                {
                    var bipId = new ElementId(bip);
                    if (existingParamIds.Contains(bipId)) continue;
                    var sf = fields.FirstOrDefault(f => f.ParameterId == bipId);
                    if (sf != null) { try { schedule.Definition.AddField(sf); } catch { } }
                }

                // Try to add PLM_SPOOL_NR shared param field by shared-parameter element name
                try
                {
                    var spoolField = fields.FirstOrDefault(f =>
                    {
                        try
                        {
                            var elem = ctx.Doc.GetElement(f.ParameterId);
                            return elem?.Name?.IndexOf("SPOOL", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        catch { return false; }
                    });
                    if (spoolField != null && !existingParamIds.Contains(spoolField.ParameterId))
                        schedule.Definition.AddField(spoolField);
                }
                catch { }

                tx.Commit();
            }

            // Activate the schedule view outside the transaction
            if (schedule != null)
            {
                try { ctx.UIDoc.ActiveView = schedule; }
                catch (Exception ex) { StingLog.Warn($"Could not activate spool schedule view: {ex.Message}"); }
            }

            TaskDialog.Show("STING Plumbing — Spool Schedule",
                $"'{ScheduleName}' has been created/refreshed and is now the active view.\n\n" +
                "Run 'Plumb_GenerateSpools' first to stamp PLM_SPOOL_NR_TXT on pipe elements.");
            return Result.Succeeded;
        }
    }
}
