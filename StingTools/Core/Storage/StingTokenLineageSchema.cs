// Pack 124 / Gap G — inheritance lineage on auto-derived tokens.
//
// TokenAutoPopulator silently swallows the source when it derives
// ASS_LOC from a Room, ASS_ZONE from a Room department, ASS_SYS from
// a connected MEP system, etc. "Why is this fixture in BLD2 not BLD3?"
// has no answer in the model — only in StingLog.log.
//
// One ES schema per element captures the source per token. Three
// fields cover the auto-derived three (LOC, ZONE, SYS) — the others
// (DISC, LVL, FUNC, PROD, SEQ) come from family / category / counter
// and are deterministic, not lineage-worthy.
//
// Source values are stable strings like:
//   "room:1234"          — derived from room with element id 1234
//   "connector:abc"      — derived from connected MEP system
//   "project-info"       — derived from Project Information
//   "family-default"     — fell through to family-level default
//   "manual"             — user-set, not derived

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingTokenLineageSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-123F-8411-F6E5D4C3B2AB");

        private const string SchemaName     = "StingTokenLineageSchema";
        private const string FieldLocSource = "LocSource";
        private const string FieldZoneSource = "ZoneSource";
        private const string FieldSysSource = "SysSource";
        private const string FieldStampedTicks = "StampedUtcTicks";

        public class Lineage
        {
            public string LocSource { get; set; } = "";
            public string ZoneSource { get; set; } = "";
            public string SysSource { get; set; } = "";
            public long   StampedUtcTicks;
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
                sb.AddSimpleField(FieldLocSource,     typeof(string))
                    .SetDocumentation("Source for ASS_LOC_TXT (room:N | project-info | manual | family-default)");
                sb.AddSimpleField(FieldZoneSource,    typeof(string))
                    .SetDocumentation("Source for ASS_ZONE_TXT");
                sb.AddSimpleField(FieldSysSource,     typeof(string))
                    .SetDocumentation("Source for ASS_SYSTEM_TYPE_TXT (connector:N | category | manual)");
                sb.AddSimpleField(FieldStampedTicks,  typeof(long))
                    .SetDocumentation("Stamp time — DateTime.UtcNow.Ticks");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTokenLineageSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Lineage Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Lineage
                {
                    LocSource       = entity.Get<string>(FieldLocSource) ?? "",
                    ZoneSource      = entity.Get<string>(FieldZoneSource) ?? "",
                    SysSource       = entity.Get<string>(FieldSysSource) ?? "",
                    StampedUtcTicks = entity.Get<long>(FieldStampedTicks),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTokenLineageSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Stamp(Element el, string locSrc, string zoneSrc, string sysSrc)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var existing = el.GetEntity(schema);
                Entity entity = (existing != null && existing.IsValid()) ? existing : new Entity(schema);
                if (locSrc != null)  entity.Set(FieldLocSource,  locSrc);
                if (zoneSrc != null) entity.Set(FieldZoneSource, zoneSrc);
                if (sysSrc != null)  entity.Set(FieldSysSource,  sysSrc);
                entity.Set(FieldStampedTicks, DateTime.UtcNow.Ticks);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTokenLineageSchema.Stamp {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
