// Gap 2 / Phase 121 — Smart Tag Placement learned-offsets ES schema.
//
// LearnTagPlacementCommand analyses existing placed tags in a view to
// derive per-category X/Y offset defaults. Today it emits a JSON file
// in _BIM_COORD; that works but isn't atomic with the project, doesn't
// survive renaming the .rvt, and can go stale vs the model.
//
// Moving the learned offsets onto ProjectInformation (the document-wide
// anchor for document-scoped data) gives:
//   * atomic save/load with the .rvt
//   * typed fields (no manual JSON parse)
//   * vendor-locked writes
//
// Storage model: one Entity per ProjectInformation, keyed by a per-
// category sub-schema. We collapse multi-category storage into a single
// JSON blob inside one string field to avoid the per-category GUID
// explosion; the JSON shape is stable.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace StingTools.Core.Storage
{
    public static class StingTagLearnedSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1239-8411-F6E5D4C3B2A5");

        private const string SchemaName       = "StingTagLearnedSchema";
        private const string FieldOffsetsJson = "OffsetsJson";
        private const string FieldUpdatedTicks = "UpdatedUtcTicks";

        public class CategoryOffset
        {
            public double OffsetXMm;
            public double OffsetYMm;
            public int    SampleCount;   // how many tags informed this offset
        }

        public class LearnedOffsets
        {
            public Dictionary<string, CategoryOffset> ByCategory { get; set; } =
                new Dictionary<string, CategoryOffset>(StringComparer.OrdinalIgnoreCase);
            public long UpdatedUtcTicks;
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
                sb.AddSimpleField(FieldOffsetsJson,  typeof(string))
                    .SetDocumentation("JSON dictionary: category-name → { OffsetXMm, OffsetYMm, SampleCount }");
                sb.AddSimpleField(FieldUpdatedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the last LearnTagPlacement run");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTagLearnedSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        /// <summary>Read learned offsets from the document's ProjectInformation.</summary>
        public static LearnedOffsets Read(Document doc)
        {
            if (doc?.ProjectInformation == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;

                string json = entity.Get<string>(FieldOffsetsJson) ?? "";
                long ticks  = entity.Get<long>(FieldUpdatedTicks);
                var offsets = string.IsNullOrEmpty(json)
                    ? new LearnedOffsets()
                    : JsonConvert.DeserializeObject<LearnedOffsets>(json) ?? new LearnedOffsets();
                offsets.UpdatedUtcTicks = ticks;
                return offsets;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTagLearnedSchema.Read: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write learned offsets to the document's ProjectInformation.
        /// Caller must own the transaction — the Entity API requires one.
        /// </summary>
        public static bool Write(Document doc, LearnedOffsets data)
        {
            if (doc?.ProjectInformation == null || data == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                data.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                string json = JsonConvert.SerializeObject(data, Formatting.None);
                entity.Set(FieldOffsetsJson,  json);
                entity.Set(FieldUpdatedTicks, data.UpdatedUtcTicks);
                doc.ProjectInformation.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTagLearnedSchema.Write: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Lookup a single category's learned offset, or null when never
        /// learned. Smart Tag Placement reads this during candidate scoring.
        /// </summary>
        public static CategoryOffset LookupOffset(Document doc, string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;
            var all = Read(doc);
            if (all?.ByCategory == null) return null;
            return all.ByCategory.TryGetValue(categoryName, out var off) ? off : null;
        }
    }
}
