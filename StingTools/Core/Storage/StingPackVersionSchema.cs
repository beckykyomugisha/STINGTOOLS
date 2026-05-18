// Pack 124 / Gap F — family pack-version stamp.
//
// InjectAutomationPresentationPack adds 45+ shared parameters to a
// family. Today there's no record on the family of which pack version
// it ran. When Phase 122 adds the next batch of params, coordinators
// can't tell which families need re-injection short of opening every
// .rfa.
//
// Stamp the pack version + UTC timestamp + count onto the family doc's
// ProjectInformation (each family is its own Document with its own
// ProjectInformation). For non-family docs we stamp on the active
// project's ProjectInformation so per-project rollups are also possible.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingPackVersionSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-123E-8411-F6E5D4C3B2AA");

        /// <summary>
        /// Increment when InjectAutomationPresentationPack adds, removes, or
        /// renames any parameter. Coordinators compare this against the
        /// stamp on each family to identify out-of-date families.
        /// </summary>
        public const int CurrentPackVersion = 4;

        private const string SchemaName       = "StingPackVersionSchema";
        private const string FieldVersion     = "PackVersion";
        private const string FieldInjectedUtc = "InjectedUtcTicks";
        private const string FieldParamCount  = "ParamCount";

        public class Stamp
        {
            public int  Version;
            public long InjectedUtcTicks;
            public int  ParamCount;

            public DateTime? InjectedUtc =>
                InjectedUtcTicks > 0 ? (DateTime?)new DateTime(InjectedUtcTicks, DateTimeKind.Utc) : null;
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
                sb.AddSimpleField(FieldVersion,     typeof(int))
                    .SetDocumentation("Pack version that ran InjectAutomationPresentationPack on this document");
                sb.AddSimpleField(FieldInjectedUtc, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks at injection");
                sb.AddSimpleField(FieldParamCount,  typeof(int))
                    .SetDocumentation("Number of parameters added in this run (excludes parameters that already existed)");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPackVersionSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Stamp Read(Document doc)
        {
            if (doc?.ProjectInformation == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Stamp
                {
                    Version          = entity.Get<int>(FieldVersion),
                    InjectedUtcTicks = entity.Get<long>(FieldInjectedUtc),
                    ParamCount       = entity.Get<int>(FieldParamCount),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPackVersionSchema.Read: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Document doc, int version, int paramCount)
        {
            if (doc?.ProjectInformation == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldVersion,     version);
                entity.Set(FieldInjectedUtc, DateTime.UtcNow.Ticks);
                entity.Set(FieldParamCount,  paramCount);
                doc.ProjectInformation.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPackVersionSchema.Write: {ex.Message}");
                return false;
            }
        }

        /// <summary>True when the document needs re-injection.</summary>
        public static bool IsStale(Document doc)
        {
            var stamp = Read(doc);
            return stamp == null || stamp.Version < CurrentPackVersion;
        }
    }
}
