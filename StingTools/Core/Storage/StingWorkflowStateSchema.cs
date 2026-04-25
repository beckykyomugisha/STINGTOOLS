// Pack 122 / Gap B — workflow state on Extensible Storage.
//
// WorkflowEngine writes WorkflowRunRecord rows to STING_WORKFLOW_LOG.json
// alongside the .rvt. Two problems:
//   1. JSONL on disk loses atomic save/load with the project; "Save As"
//      duplicates the .rvt but the workflow log stays behind.
//   2. SLA state can't be queried from outside the running engine — the
//      morning briefing, dock panel, and Pack-8 idling scheduler all
//      have to parse JSONL to surface SLA breaches.
//
// This schema mirrors the WorkflowRunRecord shape onto ProjectInformation:
// last-run summary scalars + a full RunsJson blob holding the rolling
// 100-record buffer. Single Entity per document.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingWorkflowStateSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-123A-8411-F6E5D4C3B2A6");

        private const string SchemaName            = "StingWorkflowStateSchema";
        private const string FieldLastRunUtc       = "LastRunUtcTicks";
        private const string FieldLastRunStatus    = "LastRunStatus";
        private const string FieldLastRunPreset    = "LastRunPreset";
        private const string FieldOpenSlaCount     = "OpenSlaCount";
        private const string FieldBreachedSlaCount = "BreachedSlaCount";
        private const string FieldRunsJson         = "RunsJson";

        public class State
        {
            public long   LastRunUtcTicks;
            public string LastRunStatus = "";
            public string LastRunPreset = "";
            public int    OpenSlaCount;
            public int    BreachedSlaCount;
            public string RunsJson = "";

            public DateTime? LastRunUtc =>
                LastRunUtcTicks > 0 ? (DateTime?)new DateTime(LastRunUtcTicks, DateTimeKind.Utc) : null;
        }

        public static Schema GetOrCreate()
        {
            try
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null) return existing;

                var sb = new SchemaBuilder(SchemaGuid);
                sb.SetSchemaName(SchemaName);
                sb.SetVendorId(StingSchemaBuilder.VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(FieldLastRunUtc,       typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the most recent workflow run");
                sb.AddSimpleField(FieldLastRunStatus,    typeof(string))
                    .SetDocumentation("Most recent run status — Succeeded / Failed / Cancelled");
                sb.AddSimpleField(FieldLastRunPreset,    typeof(string))
                    .SetDocumentation("Most recent preset name (DailyQA, MorningHealthCheck, …)");
                sb.AddSimpleField(FieldOpenSlaCount,     typeof(int))
                    .SetDocumentation("Open workflow instances awaiting transition");
                sb.AddSimpleField(FieldBreachedSlaCount, typeof(int))
                    .SetDocumentation("Workflow instances past their SLA without transition");
                sb.AddSimpleField(FieldRunsJson,         typeof(string))
                    .SetDocumentation("Rolling 100-record JSON buffer of WorkflowRunRecord rows");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingWorkflowStateSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static State Read(Document doc)
        {
            if (doc?.ProjectInformation == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new State
                {
                    LastRunUtcTicks  = entity.Get<long>(FieldLastRunUtc),
                    LastRunStatus    = entity.Get<string>(FieldLastRunStatus) ?? "",
                    LastRunPreset    = entity.Get<string>(FieldLastRunPreset) ?? "",
                    OpenSlaCount     = entity.Get<int>(FieldOpenSlaCount),
                    BreachedSlaCount = entity.Get<int>(FieldBreachedSlaCount),
                    RunsJson         = entity.Get<string>(FieldRunsJson) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingWorkflowStateSchema.Read: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Document doc, State data)
        {
            if (doc?.ProjectInformation == null || data == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldLastRunUtc,       data.LastRunUtcTicks);
                entity.Set(FieldLastRunStatus,    data.LastRunStatus ?? "");
                entity.Set(FieldLastRunPreset,    data.LastRunPreset ?? "");
                entity.Set(FieldOpenSlaCount,     data.OpenSlaCount);
                entity.Set(FieldBreachedSlaCount, data.BreachedSlaCount);
                entity.Set(FieldRunsJson,         data.RunsJson ?? "");
                doc.ProjectInformation.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingWorkflowStateSchema.Write: {ex.Message}");
                return false;
            }
        }

        /// <summary>Convenience: stamp the last-run scalar without touching the JSON buffer.</summary>
        public static bool StampLastRun(Document doc, string preset, string status)
        {
            var existing = Read(doc) ?? new State();
            existing.LastRunUtcTicks = DateTime.UtcNow.Ticks;
            existing.LastRunPreset   = preset ?? "";
            existing.LastRunStatus   = status ?? "";
            return Write(doc, existing);
        }
    }
}
