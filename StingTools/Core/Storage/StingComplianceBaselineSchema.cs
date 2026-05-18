// Pack 125 / Gap L — compliance baseline + 30-day ring-buffer on ES.
//
// ComplianceScan today keeps a 30-second static cache and scans on
// document open. The latest snapshot lives in memory only; closing
// Revit loses it. The control / placement centre needs real trend
// lines, which means persisting the snapshot.
//
// One Entity per ProjectInformation. Latest scalars live in their own
// fields for cheap reads (status bar / dock badge); the rolling 30-row
// ring buffer lives in a JSON blob alongside.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingComplianceBaselineSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1241-8411-F6E5D4C3B2AD");

        private const string SchemaName              = "StingComplianceBaselineSchema";
        private const string FieldLastScanUtc        = "LastScanUtcTicks";
        private const string FieldCompliancePct      = "CompliancePct";
        private const string FieldStrictPct          = "StrictPct";
        private const string FieldRevisionPct        = "RevisionPct";
        private const string FieldUntaggedCount      = "UntaggedCount";
        private const string FieldRagStatus          = "RagStatus";
        private const string FieldRingBufferJson     = "RingBufferJson"; // last 30 daily snapshots

        public class Snapshot
        {
            public long   LastScanUtcTicks;
            public double CompliancePct;
            public double StrictPct;
            public double RevisionPct;
            public int    UntaggedCount;
            public string RagStatus = "";
            public string RingBufferJson = "";

            public DateTime? LastScanUtc =>
                LastScanUtcTicks > 0 ? (DateTime?)new DateTime(LastScanUtcTicks, DateTimeKind.Utc) : null;
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
                sb.AddSimpleField(FieldLastScanUtc,    typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of last ComplianceScan.Scan()");
                sb.AddSimpleField(FieldCompliancePct,  typeof(double))
                    .SetDocumentation("Latest ComplianceResult.CompliancePercent (tagged / total * 100)");
                sb.AddSimpleField(FieldStrictPct,      typeof(double))
                    .SetDocumentation("Latest ComplianceResult.StrictPercent (fully resolved / total * 100)");
                sb.AddSimpleField(FieldRevisionPct,    typeof(double))
                    .SetDocumentation("Latest ComplianceResult.RevisionPercent");
                sb.AddSimpleField(FieldUntaggedCount,  typeof(int))
                    .SetDocumentation("Latest ComplianceResult.Untagged");
                sb.AddSimpleField(FieldRagStatus,      typeof(string))
                    .SetDocumentation("RED / AMBER / GREEN");
                sb.AddSimpleField(FieldRingBufferJson, typeof(string))
                    .SetDocumentation("Rolling 30-day daily snapshot JSON (newest first)");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingComplianceBaselineSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Snapshot Read(Document doc)
        {
            if (doc?.ProjectInformation == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Snapshot
                {
                    LastScanUtcTicks = entity.Get<long>(FieldLastScanUtc),
                    CompliancePct    = entity.Get<double>(FieldCompliancePct),
                    StrictPct        = entity.Get<double>(FieldStrictPct),
                    RevisionPct      = entity.Get<double>(FieldRevisionPct),
                    UntaggedCount    = entity.Get<int>(FieldUntaggedCount),
                    RagStatus        = entity.Get<string>(FieldRagStatus) ?? "",
                    RingBufferJson   = entity.Get<string>(FieldRingBufferJson) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingComplianceBaselineSchema.Read: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Document doc, Snapshot snap)
        {
            if (doc?.ProjectInformation == null || snap == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldLastScanUtc,    snap.LastScanUtcTicks);
                entity.Set(FieldCompliancePct,  snap.CompliancePct);
                entity.Set(FieldStrictPct,      snap.StrictPct);
                entity.Set(FieldRevisionPct,    snap.RevisionPct);
                entity.Set(FieldUntaggedCount,  snap.UntaggedCount);
                entity.Set(FieldRagStatus,      snap.RagStatus ?? "");
                entity.Set(FieldRingBufferJson, snap.RingBufferJson ?? "");
                doc.ProjectInformation.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingComplianceBaselineSchema.Write: {ex.Message}");
                return false;
            }
        }
    }
}
