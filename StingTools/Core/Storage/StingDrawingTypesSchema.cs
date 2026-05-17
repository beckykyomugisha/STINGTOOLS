// Pack 122 / Gap C — Drawing Type project overrides on Extensible Storage.
//
// Phase 113 stores per-project drawing-type overrides at
// <project>/_BIM_COORD/drawing_types.json. That breaks when projects are
// renamed, moved, or cloned — the file stays behind. Promoting the JSON
// blob onto ProjectInformation gives:
//   * atomic save/load with the .rvt
//   * survives "Save As" duplications
//   * SHA-256 checksum-drift detection moves from filesystem timestamps
//     to entity content hashing
//
// Single string field on ProjectInformation. The DrawingTypeRegistry
// reads ES first; falls back to the on-disk JSON for pre-migration
// projects. The migration command in Pack 122 imports the on-disk file
// once when present.

using System;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingDrawingTypesSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-123B-8411-F6E5D4C3B2A7");

        private const string SchemaName       = "StingDrawingTypesSchema";
        private const string FieldOverridesJson = "OverridesJson";
        private const string FieldUpdatedTicks  = "UpdatedUtcTicks";
        private const string FieldChecksumSha256 = "ChecksumSha256";

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
                sb.AddSimpleField(FieldOverridesJson,   typeof(string))
                    .SetDocumentation("Project DrawingTypeLibrary override JSON (drawingTypes[] + routing[])");
                sb.AddSimpleField(FieldUpdatedTicks,    typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the most recent override update");
                sb.AddSimpleField(FieldChecksumSha256,  typeof(string))
                    .SetDocumentation("Hex SHA-256 of OverridesJson — drift detection vs corporate baseline");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDrawingTypesSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public class Overrides
        {
            public string OverridesJson = "";
            public long   UpdatedUtcTicks;
            public string ChecksumSha256 = "";

            public bool IsEmpty => string.IsNullOrEmpty(OverridesJson);
        }

        public static Overrides Read(Document doc)
        {
            if (doc?.ProjectInformation == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Overrides
                {
                    OverridesJson    = entity.Get<string>(FieldOverridesJson) ?? "",
                    UpdatedUtcTicks  = entity.Get<long>(FieldUpdatedTicks),
                    ChecksumSha256   = entity.Get<string>(FieldChecksumSha256) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDrawingTypesSchema.Read: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Document doc, string overridesJson)
        {
            if (doc?.ProjectInformation == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                string json = overridesJson ?? "";
                entity.Set(FieldOverridesJson,   json);
                entity.Set(FieldUpdatedTicks,    DateTime.UtcNow.Ticks);
                entity.Set(FieldChecksumSha256,  ComputeSha256(json));
                doc.ProjectInformation.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingDrawingTypesSchema.Write: {ex.Message}");
                return false;
            }
        }

        private static string ComputeSha256(string s)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch { return ""; }
        }
    }
}
