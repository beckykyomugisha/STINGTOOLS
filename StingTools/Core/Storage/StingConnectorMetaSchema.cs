// Pack 124 / Gap H — connector metadata as structured ES.
//
// Today CONN_TYPES_TXT = "HWS,DCW,SAN" — a comma list parsed at every
// call to RoutingParamReader. Three problems:
//   * No write-time validation (typos in the family parameter pass).
//   * 5x more expensive in tight loops (string split per element per scan).
//   * Loses type fidelity for DropEngineBase + SeparationValidator.
//
// Revit ES supports IList<T>, so we store the list typed. Read API
// returns a List<string>; the legacy CONN_TYPES_TXT comma string still
// works as a fallback during the transition window via the helper
// RoutingParamReader that was added in Pack 5.3.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingConnectorMetaSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1240-8411-F6E5D4C3B2AC");

        private const string SchemaName       = "StingConnectorMetaSchema";
        private const string FieldTypes       = "Types";
        private const string FieldCount       = "Count";
        private const string FieldPrefDir     = "PreferredDropDir";

        public class ConnectorMeta
        {
            public IList<string> Types { get; set; } = new List<string>();
            public int Count;
            public string PreferredDropDir { get; set; } = "";
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
                sb.AddArrayField(FieldTypes,    typeof(string))
                    .SetDocumentation("Typed connector list — replaces CONN_TYPES_TXT comma-string");
                sb.AddSimpleField(FieldCount,   typeof(int))
                    .SetDocumentation("Connector count — replaces CONN_COUNT_INT");
                sb.AddSimpleField(FieldPrefDir, typeof(string))
                    .SetDocumentation("Up / Down / Side — replaces PREF_DROP_DIR_TXT");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingConnectorMetaSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static ConnectorMeta Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                IList<string> typesList;
                try { typesList = entity.Get<IList<string>>(FieldTypes) ?? new List<string>(); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); typesList = new List<string>(); }
                return new ConnectorMeta
                {
                    Types            = typesList,
                    Count            = entity.Get<int>(FieldCount),
                    PreferredDropDir = entity.Get<string>(FieldPrefDir) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingConnectorMetaSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, ConnectorMeta data)
        {
            if (el == null || data == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set<IList<string>>(FieldTypes, data.Types ?? new List<string>());
                entity.Set(FieldCount,   data.Count);
                entity.Set(FieldPrefDir, data.PreferredDropDir ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingConnectorMetaSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
