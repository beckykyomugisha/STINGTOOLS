#nullable enable
// Pack 10 — Extensible Storage schema builder.
//
// STING has about a dozen state variables stored as hidden shared parameters
// (STING_STALE_BOOL, STING_CLUSTER_COUNT, STING_CLUSTER_LABEL, STING_DISPLAY_MODE
// etc.) that shouldn't really be visible on the user's properties palette.
// They travel with the element through CopyPasteOptions and family reload,
// but they pollute schedules, auto-populate into filters, and occasionally
// get overwritten by users who don't know what they mean.
//
// Extensible Storage is the Revit-native alternative:
//   * vendor-lock (only STING can read/write the schema)
//   * typed fields (no "stored as integer 0 or 1" ambiguity)
//   * invisible to the user
//   * schema-versioned
//
// Pack 10 pilots this with STING_STALE_BOOL — the lowest-risk migration.
// The shared parameter stays in place for a transition window so old tools
// still work; new writes go to both surfaces; reads prefer the ES value.
// Future packs migrate the cluster params, display-mode params, and
// compliance cache on the same pattern.
//
// TODO-VERIFY-API: Schema.Lookup by GUID, SchemaBuilder.SetVendorId,
// Entity.Get<T>/Set<T> signatures per Revit 2025
// https://www.revitapidocs.com/2025/1a510aae-7fb2-4b21-b3e6-a15eda3a7f30.htm

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingSchemaBuilder
    {
        // Vendor ID — matches the .addin <VendorId>.
        public const string VendorId = "Planscape";

        // Deterministic, stable, never-to-be-rotated — rotating breaks every
        // project that ever wrote this schema.
        private static readonly Guid StaleSchemaGuid =
            new Guid("E1A7B2C4-1011-1235-8411-F6E5D4C3B2A1");

        private const string StaleFieldName = "StaleFlag";
        private const string StaleSchemaName = "StingStaleSchema";

        /// <summary>
        /// Resolve the stale-flag schema, creating it on first access per-document.
        /// Safe to call repeatedly — Schema.Lookup is cheap.
        /// </summary>
        public static Schema GetOrCreateStaleSchema()
        {
            try
            {
                var existing = Schema.Lookup(StaleSchemaGuid);
                if (existing != null) return existing;

                var sb = new SchemaBuilder(StaleSchemaGuid);
                sb.SetSchemaName(StaleSchemaName);
                sb.SetVendorId(VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(StaleFieldName, typeof(bool))
                    .SetDocumentation("1 = geometry has drifted since last tag write; 0 = fresh");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingSchemaBuilder.GetOrCreateStaleSchema: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write the stale flag to the element's Extensible Storage entity.
        /// Falls back silently if the schema can't be created (e.g. on a
        /// read-only document). Callers should still update the legacy
        /// STING_STALE_BOOL parameter during the transition window.
        /// </summary>
        public static bool WriteStale(Element el, bool stale)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreateStaleSchema();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(StaleFieldName, stale);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingSchemaBuilder.WriteStale {el.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read the stale flag from Extensible Storage, falling back to the
        /// legacy shared parameter when the entity is absent (pre-migration
        /// projects). Returns null only when neither surface is readable.
        /// </summary>
        public static bool? ReadStale(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(StaleSchemaGuid);
                if (schema != null)
                {
                    var entity = el.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        return entity.Get<bool>(StaleFieldName);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingSchemaBuilder.ReadStale {el.Id}: {ex.Message}");
            }

            // Fallback: legacy shared parameter
            try
            {
                var p = el.LookupParameter("STING_STALE_BOOL");
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer)
                    return p.AsInteger() != 0;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Migrate every element's STING_STALE_BOOL into Extensible Storage.
        /// Idempotent — elements that already carry the entity are skipped.
        /// Call from an explicit "Migrate to Extensible Storage" command so
        /// we never block document open for large projects.
        /// </summary>
        public static int MigrateStaleFromSharedParam(Document doc)
        {
            if (doc == null) return 0;
            var schema = GetOrCreateStaleSchema();
            if (schema == null) return 0;

            int n = 0;
            try
            {
                using (var t = new Transaction(doc, "STING ES: migrate stale flag"))
                {
                    t.Start();
                    var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    foreach (var el in col)
                    {
                        try
                        {
                            var p = el.LookupParameter("STING_STALE_BOOL");
                            if (p == null || !p.HasValue || p.StorageType != StorageType.Integer) continue;
                            bool stale = p.AsInteger() != 0;

                            var existing = el.GetEntity(schema);
                            if (existing != null && existing.IsValid()) continue; // already migrated

                            var entity = new Entity(schema);
                            entity.Set(StaleFieldName, stale);
                            el.SetEntity(entity);
                            n++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"StingSchemaBuilder.Migrate {el?.Id}: {ex.Message}");
                        }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingSchemaBuilder.MigrateStaleFromSharedParam", ex);
            }
            return n;
        }
    }
}
