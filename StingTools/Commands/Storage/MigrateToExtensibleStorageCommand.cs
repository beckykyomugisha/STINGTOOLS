// Gap 2 / Phase 121 — project-wide ES migration.
//
// Walks every element and, for each of the four schema types (Stale,
// Cluster, Position, TagHistory), imports the legacy shared-parameter
// value into an Extensible Storage entity when the entity is absent.
//
// Idempotent — re-running the command on a migrated project is a fast
// no-op. The legacy shared parameters are NOT deleted; they remain as
// a safety net during the transition window. A later pack will retire
// the shared writes once every team has run this command at least once.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Storage;
using StingTools.UI;

namespace StingTools.Commands.Storage
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MigrateToExtensibleStorageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open"; return Result.Failed; }
                var doc = ctx.Doc;

                int scanned = 0, stale = 0, cluster = 0, position = 0, history = 0;
                using (var tg = new TransactionGroup(doc, "STING ES: migrate project"))
                {
                    tg.Start();
                    using (var t = new Transaction(doc, "STING ES: migrate"))
                    {
                        t.Start();
                        // Touch the schemas once up-front so Schema.Lookup returns
                        // them on every per-element read inside the loop.
                        StingSchemaBuilder.GetOrCreateStaleSchema();
                        StingClusterSchema.GetOrCreate();
                        StingPositionSchema.GetOrCreate();
                        StingTagHistorySchema.GetOrCreate();
                        StingTagLearnedSchema.GetOrCreate();

                        var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                        foreach (var el in col)
                        {
                            scanned++;
                            try
                            {
                                if (StingEsHelpers.TryImportStale(el))    stale++;
                                if (StingEsHelpers.TryImportCluster(el))  cluster++;
                                if (StingEsHelpers.TryImportPosition(el)) position++;
                                if (StingEsHelpers.TryImportHistory(el))  history++;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"MigrateToExtensibleStorageCommand element {el?.Id}: {ex.Message}");
                            }
                        }
                        t.Commit();
                    }
                    tg.Assimilate();
                }

                var panel = StingResultPanel.Create("STING — Extensible Storage migration")
                    .SetSubtitle($"Scanned {scanned} element(s) in '{doc.Title}'")
                    .AddSection("IMPORTED (first run only)")
                    .Metric("STING_STALE_BOOL → ES",    stale.ToString())
                    .Metric("Cluster metadata → ES",    cluster.ToString())
                    .Metric("Tag position → ES",        position.ToString())
                    .Metric("Tag history → ES",         history.ToString())
                    .AddSection("NOTES")
                    .Text("Legacy shared parameters remain in place — the migration is dual-surface until the transition window closes.")
                    .Text("Re-run safely: counters report only NEW imports per invocation.")
                    .Text("Click BIM ▸ ES Diagnostic to see total ES coverage.");
                panel.Show();

                StingLog.Info($"ES migration: scanned={scanned} stale+={stale} cluster+={cluster} pos+={position} history+={history}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MigrateToExtensibleStorageCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
