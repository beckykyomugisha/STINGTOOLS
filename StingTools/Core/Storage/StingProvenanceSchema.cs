// Pack 123 / Gap E — element-creation provenance.
//
// AutoConduitDrop, AutoPipeDrop, AutoDuctDrop, PlaceFixturesCommand,
// BuildingShell, fabrication spool, and a dozen other STING engines
// create elements directly in the document. Today nothing on the new
// element records that it was auto-created, which engine, or under
// which rule. That makes three things impossible:
//
//   * "delete every auto-created element not yet validated"
//   * BOQ separating auto-from-manual quantity (typical mark-up)
//   * "why did this element get created?" audit trail
//
// One small ES schema closes the gap. Engines call ProvenanceWriter
// after their own model mutation; consumers (cleanup, BOQ, validator
// reports) read through StingProvenanceSchema.Read.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingProvenanceSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-123D-8411-F6E5D4C3B2A9");

        private const string SchemaName        = "StingProvenanceSchema";
        private const string FieldEngine       = "Engine";
        private const string FieldRuleId       = "RuleId";
        private const string FieldCreatedTicks = "CreatedUtcTicks";
        private const string FieldOperator     = "Operator";

        public class Provenance
        {
            public string Engine { get; set; } = "";   // "AutoConduitDrop", "PlaceFixtures", "BuildingShell" …
            public string RuleId { get; set; } = "";   // STING_PLACEMENT_RULES.json rule key, etc.
            public long   CreatedUtcTicks;
            public string Operator { get; set; } = ""; // Environment.UserName at creation time

            public DateTime? CreatedUtc =>
                CreatedUtcTicks > 0 ? (DateTime?)new DateTime(CreatedUtcTicks, DateTimeKind.Utc) : null;
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
                sb.AddSimpleField(FieldEngine,       typeof(string))
                    .SetDocumentation("STING engine that created this element");
                sb.AddSimpleField(FieldRuleId,       typeof(string))
                    .SetDocumentation("Rule library key (e.g. STING_PLACEMENT_RULES.json::lighting-corridor)");
                sb.AddSimpleField(FieldCreatedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of creation");
                sb.AddSimpleField(FieldOperator,     typeof(string))
                    .SetDocumentation("Environment.UserName at the time of creation");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingProvenanceSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Provenance Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Provenance
                {
                    Engine          = entity.Get<string>(FieldEngine) ?? "",
                    RuleId          = entity.Get<string>(FieldRuleId) ?? "",
                    CreatedUtcTicks = entity.Get<long>(FieldCreatedTicks),
                    Operator        = entity.Get<string>(FieldOperator) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingProvenanceSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write provenance. Caller owns the transaction. Idempotent —
        /// subsequent writes overwrite the existing entity, which is what
        /// the BOQ / cleanup commands expect (last engine wins).
        /// </summary>
        public static bool Stamp(Element el, string engine, string ruleId)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldEngine,       engine ?? "");
                entity.Set(FieldRuleId,       ruleId ?? "");
                entity.Set(FieldCreatedTicks, DateTime.UtcNow.Ticks);
                entity.Set(FieldOperator,     Environment.UserName ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingProvenanceSchema.Stamp {el?.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>True when the element was created by a STING engine.</summary>
        public static bool IsAutoCreated(Element el) => Read(el) != null;
    }
}
