// StingLpsSldStampSchema.cs — Wave A item #1.
//
// Marks Detail Lines / Text Notes / Filled Regions placed by
// LpsSldEngine so a rebuild can purge ONLY engine-placed content,
// not user annotations on the same view. The previous implementation
// deleted every Detail Line + Text Note + Filled Region in the SLD
// view which silently clobbered annotations a coordinator might have
// added (call-outs, region tags, notes).
//
// One zero-value entity per element; presence = "owned by SLD engine".

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingLpsSldStampSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("C2E3F4A5-B6C7-4D8E-9F01-23456789ABCE");

        private const string SchemaName   = "StingLpsSldStampSchema";
        private const string FieldOwnedBy = "OwnedBySldEngine";

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
                sb.AddSimpleField(FieldOwnedBy, typeof(int))
                    .SetDocumentation("Always 1 — presence marks the element as owned by LpsSldEngine.Build, eligible for purge on rebuild.");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingLpsSldStampSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        /// <summary>Stamp the element as owned by the LPS SLD engine. Caller must be inside a Transaction.</summary>
        public static bool Stamp(Element el)
        {
            try
            {
                if (el == null) return false;
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldOwnedBy, 1);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"StingLpsSldStampSchema.Stamp: {ex.Message}"); return false; }
        }

        public static bool IsOwned(Element el)
        {
            try
            {
                if (el == null) return false;
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return false;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return false;
                return entity.Get<int>(FieldOwnedBy) == 1;
            }
            catch (Exception ex) { StingLog.Warn($"StingLpsSldStampSchema.IsOwned: {ex.Message}"); return false; }
        }
    }
}
