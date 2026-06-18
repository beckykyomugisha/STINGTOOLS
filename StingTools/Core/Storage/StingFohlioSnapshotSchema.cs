// Phase 192 (C1) — Fohlio import snapshot ES schema.
//
// Stores the last-imported Fohlio field snapshot per element so Fohlio_Audit
// can flag rows whose model parameters have drifted from the last Fohlio import
// (the "Fohlio kept current" KPI). The snapshot is a small JSON blob of the
// imported field → value pairs plus the Fohlio item ref and import timestamp.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingFohlioSnapshotSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1245-8411-F6E5D4C3B2CD");

        private const string SchemaName        = "StingFohlioSnapshotSchema";
        private const string FieldFohlioRef    = "FohlioRef";
        private const string FieldSnapshotJson = "SnapshotJson";
        private const string FieldCapturedTicks= "CapturedUtcTicks";

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
                sb.AddSimpleField(FieldFohlioRef, typeof(string))
                    .SetDocumentation("Fohlio item ref (URL/ID) captured at the last import");
                sb.AddSimpleField(FieldSnapshotJson, typeof(string))
                    .SetDocumentation("JSON of imported field→value pairs at the last Fohlio import");
                sb.AddSimpleField(FieldCapturedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the last Fohlio import. 0 = never");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingFohlioSnapshotSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public class SnapshotData
        {
            public string FohlioRef = "";
            public string SnapshotJson = "";
            public long CapturedUtcTicks;

            public DateTime? CapturedUtc =>
                CapturedUtcTicks > 0 ? (DateTime?)new DateTime(CapturedUtcTicks, DateTimeKind.Utc) : null;
        }

        public static SnapshotData Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new SnapshotData
                {
                    FohlioRef = entity.Get<string>(FieldFohlioRef) ?? "",
                    SnapshotJson = entity.Get<string>(FieldSnapshotJson) ?? "",
                    CapturedUtcTicks = entity.Get<long>(FieldCapturedTicks),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingFohlioSnapshotSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, string fohlioRef, string snapshotJson, DateTime capturedUtc)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldFohlioRef, fohlioRef ?? "");
                entity.Set(FieldSnapshotJson, snapshotJson ?? "");
                entity.Set(FieldCapturedTicks, capturedUtc.ToUniversalTime().Ticks);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingFohlioSnapshotSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
