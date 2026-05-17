// Gap 2 / Phase 121 — tag-history audit trail ES schema.
//
// Replaces ASS_TAG_PREV_TXT + ASS_TAG_MODIFIED_DT plus a new RevisionCode
// string so reverse-diff / audit commands can query the previous tag,
// when it changed, and under which revision. The shared-param audit is
// written by the tagging pipeline (Core/ParameterHelpers.cs:3869) every
// time a tag changes; dual-writing to ES means a future pack can drop
// the shared params without losing history.
//
// IMPORTANT — the tag pipeline is the hot path in STING. This schema is
// written alongside the existing shared-param writes so turning off the
// legacy write later is a single-line change. Do NOT move reads to
// ES-preferred yet on this one — keep shared-param reads primary until
// the dual-write has run across at least one full project cycle.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingTagHistorySchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1238-8411-F6E5D4C3B2A4");

        private const string SchemaName           = "StingTagHistorySchema";
        private const string FieldPreviousTag     = "PreviousTag";
        private const string FieldModifiedTicks   = "ModifiedUtcTicks";
        private const string FieldRevisionCode    = "RevisionCode";

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
                sb.AddSimpleField(FieldPreviousTag,   typeof(string))
                    .SetDocumentation("N-1 value of ASS_TAG_1 prior to the most recent tag write");
                sb.AddSimpleField(FieldModifiedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the most recent tag write. 0 = never written");
                sb.AddSimpleField(FieldRevisionCode,  typeof(string))
                    .SetDocumentation("Project revision code active at the time of the tag write");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTagHistorySchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public class HistoryData
        {
            public string PreviousTag = "";
            public long   ModifiedUtcTicks;
            public string RevisionCode = "";

            public DateTime? ModifiedUtc =>
                ModifiedUtcTicks > 0 ? (DateTime?)new DateTime(ModifiedUtcTicks, DateTimeKind.Utc) : null;
        }

        public static HistoryData Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new HistoryData
                {
                    PreviousTag      = entity.Get<string>(FieldPreviousTag) ?? "",
                    ModifiedUtcTicks = entity.Get<long>(FieldModifiedTicks),
                    RevisionCode     = entity.Get<string>(FieldRevisionCode) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTagHistorySchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, string previousTag, DateTime modifiedUtc, string revisionCode)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldPreviousTag,   previousTag ?? "");
                entity.Set(FieldModifiedTicks, modifiedUtc.ToUniversalTime().Ticks);
                entity.Set(FieldRevisionCode,  revisionCode ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTagHistorySchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
