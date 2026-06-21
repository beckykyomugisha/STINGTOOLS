// StingMepCadStampSchema.cs — MEP-from-DWG P5 (1.1 idempotency / re-run guard).
//
// Marks every element created by Mep_CadToModel / Mep_CadWizard with the SOURCE
// IMPORT KEY (the DWG file/type name) so a re-run can detect a prior conversion
// of the same import and offer Skip / Replace / Add — instead of silently
// duplicating every fixture, run, riser and fitting (the structural CAD path is
// idempotent; this brings the MEP path to parity).
//
// One entity per created element; the ImportKey groups all elements from one
// conversion run. FindStamped uses an ExtensibleStorageFilter quick-filter to
// gather them efficiently for the Replace path.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingMepCadStampSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1247-8411-F6E5D4C3B2CE");

        private const string SchemaName        = "StingMepCadStampSchema";
        private const string FieldImportKey    = "ImportKey";
        private const string FieldConvertedTicks = "ConvertedUtcTicks";

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
                sb.AddSimpleField(FieldImportKey, typeof(string))
                    .SetDocumentation("Source DWG import key (file/type name) this element was converted from.");
                sb.AddSimpleField(FieldConvertedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the conversion run.");
                return sb.Finish();
            }
            catch (Exception ex) { StingLog.Warn($"StingMepCadStampSchema.GetOrCreate: {ex.Message}"); return null; }
        }

        /// <summary>Stamp the element with the import key. Caller must be inside a Transaction.</summary>
        public static bool Stamp(Element el, string importKey)
        {
            try
            {
                if (el == null) return false;
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldImportKey, importKey ?? "");
                entity.Set(FieldConvertedTicks, DateTime.UtcNow.Ticks);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"StingMepCadStampSchema.Stamp {el?.Id}: {ex.Message}"); return false; }
        }

        public static string ReadKey(Element el)
        {
            try
            {
                if (el == null) return null;
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return entity.Get<string>(FieldImportKey);
            }
            catch (Exception ex) { StingLog.Warn($"StingMepCadStampSchema.ReadKey {el?.Id}: {ex.Message}"); return null; }
        }

        /// <summary>All elements stamped from the given import key (a prior conversion).
        /// Uses an ExtensibleStorageFilter quick-filter, then matches the key.</summary>
        public static List<ElementId> FindStamped(Document doc, string importKey)
        {
            var ids = new List<ElementId>();
            if (doc == null) return ids;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return ids;   // schema never created → nothing stamped yet
                // ExtensibleStorageFilter returns elements carrying ANY entity of this schema.
                var coll = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ExtensibleStorageFilter(SchemaGuid));   // TODO-VERIFY-API
                foreach (var el in coll)
                    if (string.Equals(ReadKey(el), importKey, StringComparison.OrdinalIgnoreCase))
                        ids.Add(el.Id);
            }
            catch (Exception ex) { StingLog.Warn($"StingMepCadStampSchema.FindStamped: {ex.Message}"); }
            return ids;
        }
    }
}
