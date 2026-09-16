using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// BINDSCOPE-2 — the migration that `CATEGORY_BINDINGS.csv` could not perform.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// On 2026-09-16 a newly placed door tagged `A-BLD1-Z01-L01-ARC-FIT-DR-` — seven tokens
    /// and a trailing separator where the sequence number belongs. `ASS_SEQ_NUM_TXT` on that
    /// door read `-215` and was GREYED in the Properties palette, which is how Revit renders
    /// a TYPE-bound parameter on an instance. A type-bound parameter is not reachable through
    /// `element.get_Parameter()`, so
    ///
    ///     ParameterHelpers.SetString(el, ParamRegistry.SEQ, seq, overwrite: true);
    ///
    /// returned false in silence and the tag assembled with an empty final segment.
    ///
    /// It is also wrong on its own terms: a sequence number identifies ONE element, and bound
    /// to the type every door of `750 x 2000mm` shares a single value.
    ///
    /// WHY THE CSV FIX WAS NOT ENOUGH — measured, after claiming otherwise
    /// ------------------------------------------------------------------
    /// 1,198 rows of `CATEGORY_BINDINGS.csv` were flipped Type → Instance. That change is
    /// DOCUMENTATION ONLY. `SharedParamGuids.LoadPerParamCategoryBindings` reads `cols[0]`
    /// (parameter) and `cols[1]` (category) and never looks at `cols[2]`, and
    /// `LoadSharedParamsCommand` calls `NewInstanceBinding` unconditionally. The scope column
    /// is not an input to anything.
    ///
    /// So the type binding in an affected model did not come from that file and cannot be
    /// repaired by editing it — it came from a template, a manually loaded shared-parameter
    /// file, or an earlier binder. The only thing that can fix it is a re-bind inside Revit,
    /// which is this command.
    ///
    /// WHAT IT DOES, AND THE DATA IT COSTS
    /// -----------------------------------
    /// Walks `Document.ParameterBindings`, finds STING parameters held by a `TypeBinding`,
    /// and re-inserts them as an `InstanceBinding` over the same categories.
    ///
    /// ⚠️ RE-BINDING TYPE → INSTANCE DISCARDS THE VALUES HELD ON TYPES. Revit stores the two
    /// scopes separately; there is no API that migrates values across the change. For the
    /// tag tokens that is what you want — a per-type SEQ is meaningless and a per-type LOC is
    /// wrong — but it is a real loss and the user is told the count and must confirm before
    /// anything is written. Audit first; the read-only pass writes nothing.
    /// </summary>
    internal static class BindingScopeMigration
    {
        /// <summary>A parameter that the tagging pipeline writes per element.</summary>
        internal static bool ShouldBeInstance(string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return false;

            // The eight ISO tokens plus the assembled containers and the audit trail. These
            // are per-occurrence by definition: two instances of one type stand in different
            // rooms, on different levels, and carry different sequence numbers.
            if (ParamRegistry.AllTokenParams != null
                && ParamRegistry.AllTokenParams.Contains(paramName, StringComparer.Ordinal))
                return true;

            foreach (string p in new[]
            {
                ParamRegistry.TAG1, ParamRegistry.TAG_PREV, ParamRegistry.STATUS,
            })
            {
                if (string.Equals(p, paramName, StringComparison.Ordinal)) return true;
            }

            // Containers (ASS_TAG_2_TXT .. ASS_TAG_7x_TXT) and the per-element audit stamps.
            if (paramName.StartsWith("ASS_TAG_", StringComparison.Ordinal)) return true;
            if (paramName.StartsWith("ASS_ROOM_", StringComparison.Ordinal)) return true;
            if (string.Equals(paramName, "ASS_SERIAL_NR_TXT", StringComparison.Ordinal)) return true;
            if (string.Equals(paramName, "PRJ_GRID_REF_TXT", StringComparison.Ordinal)) return true;

            return false;
        }

        internal sealed class Finding
        {
            public string ParamName;
            public Definition Definition;
            public CategorySet Categories;
            public int CategoryCount;
        }

        internal static List<Finding> Audit(Document doc)
        {
            var found = new List<Finding>();
            if (doc == null) return found;

            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition def = null;
                try { def = it.Key; } catch { }
                if (def == null) continue;

                // Only TypeBinding is a problem. InstanceBinding is already correct.
                var tb = it.Current as TypeBinding;
                if (tb == null) continue;

                string name = def.Name;
                if (!ShouldBeInstance(name)) continue;

                int n = 0;
                try { foreach (Category c in tb.Categories) { if (c != null) n++; } } catch { }

                found.Add(new Finding
                {
                    ParamName = name,
                    Definition = def,
                    Categories = tb.Categories,
                    CategoryCount = n,
                });
            }
            return found;
        }
    }

    /// <summary>Read-only: report STING parameters bound to the TYPE that must be per-instance.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuditBindingScopeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            Document doc = cd?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            try
            {
                var findings = BindingScopeMigration.Audit(doc);
                var sb = new StringBuilder();

                if (findings.Count == 0)
                {
                    sb.AppendLine("No STING per-element parameter is bound to the TYPE.");
                    sb.AppendLine();
                    sb.AppendLine("This is the healthy state: the tag tokens, the assembled");
                    sb.AppendLine("containers and the audit stamps are all per-instance, so a");
                    sb.AppendLine("sequence number can identify one element rather than a type.");
                }
                else
                {
                    sb.AppendLine($"{findings.Count} STING parameter(s) are bound to the TYPE but");
                    sb.AppendLine("are written per element. Every such write returns false in");
                    sb.AppendLine("silence, which is why a tag can lose its SEQ and end in a");
                    sb.AppendLine("trailing separator (A-BLD1-Z01-L01-ARC-FIT-DR-).");
                    sb.AppendLine();
                    foreach (var f in findings.OrderBy(x => x.ParamName, StringComparer.Ordinal))
                        sb.AppendLine($"  {f.ParamName}   ({f.CategoryCount} categor(y/ies))");
                    sb.AppendLine();
                    sb.AppendLine("Run 'Migrate binding scope' to re-bind them as Instance.");
                    sb.AppendLine("Re-binding DISCARDS values currently held on types — for");
                    sb.AppendLine("these parameters that is intended, but it is a real loss.");
                }

                StingLog.Info($"AuditBindingScope: {findings.Count} type-bound per-element param(s).");
                TaskDialog.Show("STING — Binding scope", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AuditBindingScopeCommand failed", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Re-bind STING per-element parameters from TypeBinding to InstanceBinding.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MigrateBindingScopeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            UIApplication uiapp = cd?.Application;
            Document doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            try
            {
                var findings = BindingScopeMigration.Audit(doc);
                if (findings.Count == 0)
                {
                    TaskDialog.Show("STING — Binding scope",
                        "Nothing to migrate: no STING per-element parameter is bound to the TYPE.");
                    return Result.Succeeded;
                }

                var confirm = new TaskDialog("STING — Migrate binding scope")
                {
                    MainInstruction = $"Re-bind {findings.Count} parameter(s) from Type to Instance?",
                    MainContent =
                        "These parameters are written per element, so a Type binding makes the "
                        + "write fail silently — this is what leaves a tag ending in a separator "
                        + "with no sequence number.\n\n"
                        + "Revit stores Type and Instance values separately and provides no way "
                        + "to carry them across, so ANY VALUES CURRENTLY HELD ON TYPES FOR THESE "
                        + "PARAMETERS WILL BE LOST. For a sequence number or a location token "
                        + "that is the point — a per-type value was never meaningful — but it is "
                        + "a real loss and it cannot be undone except with Revit's own undo.\n\n"
                        + "Re-tag after migrating so the per-element values are written.",
                    AllowCancellation = true,
                };
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Re-bind {findings.Count} parameter(s)");
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel");
                if (confirm.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                int ok = 0, failed = 0;
                var problems = new List<string>();

                using (var tx = new Transaction(doc, "STING Migrate Binding Scope"))
                {
                    tx.Start();
                    foreach (var f in findings)
                    {
                        try
                        {
                            InstanceBinding ib =
                                uiapp.Application.Create.NewInstanceBinding(f.Categories);
                            if (doc.ParameterBindings.ReInsert(f.Definition, ib))
                                ok++;
                            else
                            {
                                failed++;
                                problems.Add($"{f.ParamName}: ReInsert returned false");
                                StingLog.Warn($"MigrateBindingScope: ReInsert false for {f.ParamName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            problems.Add($"{f.ParamName}: {ex.Message}");
                            StingLog.Warn($"MigrateBindingScope: {f.ParamName} failed: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                SharedParamGuids.InvalidateCache();
                ParameterHelpers.InvalidateSourceTokenSet();

                var sb = new StringBuilder();
                sb.AppendLine($"Re-bound: {ok}");
                sb.AppendLine($"Failed  : {failed}");
                if (problems.Count > 0)
                {
                    sb.AppendLine();
                    foreach (string p in problems.Take(12)) sb.AppendLine("  " + p);
                }
                sb.AppendLine();
                sb.AppendLine("NEXT: re-tag. The per-element values do not exist yet — the");
                sb.AppendLine("migration only made it possible to write them.");

                StingLog.Info($"MigrateBindingScope: re-bound {ok}, failed {failed}.");
                TaskDialog.Show("STING — Binding scope migrated", sb.ToString());
                return failed == 0 ? Result.Succeeded : Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MigrateBindingScopeCommand failed", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
