// Gap 2 / Phase 121 — cluster metadata Extensible Storage schema.
//
// Replaces the shared parameters STING_CLUSTER_COUNT (int) +
// STING_CLUSTER_LABEL (text) + a new GroupKey string for future
// clustering policies. Consumed today by DeclusterTagsCommand; the
// shared-param read path falls back when ES is empty, so pre-migration
// projects keep working.
//
// Stable GUID — never rotated. Adding a field later requires a NEW
// GUID (Revit forbids field additions to existing schemas).

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingClusterSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1236-8411-F6E5D4C3B2A2");

        private const string SchemaName = "StingClusterSchema";
        private const string FieldCount    = "Count";
        private const string FieldLabel    = "Label";
        private const string FieldGroupKey = "GroupKey";

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
                sb.AddSimpleField(FieldCount,    typeof(int))
                    .SetDocumentation("Cluster member count — identical-value tags collapsed into one");
                sb.AddSimpleField(FieldLabel,    typeof(string))
                    .SetDocumentation("Human-readable cluster label shown on the single leader tag");
                sb.AddSimpleField(FieldGroupKey, typeof(string))
                    .SetDocumentation("Policy-specific grouping key (e.g. '{DISC}-{SYS}' template)");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingClusterSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public class ClusterData
        {
            public int Count;
            public string Label = "";
            public string GroupKey = "";

            public bool IsEmpty => Count == 0 && string.IsNullOrEmpty(Label) && string.IsNullOrEmpty(GroupKey);
        }

        public static ClusterData Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new ClusterData
                {
                    Count    = entity.Get<int>(FieldCount),
                    Label    = entity.Get<string>(FieldLabel) ?? "",
                    GroupKey = entity.Get<string>(FieldGroupKey) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingClusterSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, ClusterData data)
        {
            if (el == null || data == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldCount,    data.Count);
                entity.Set(FieldLabel,    data.Label ?? "");
                entity.Set(FieldGroupKey, data.GroupKey ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingClusterSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
