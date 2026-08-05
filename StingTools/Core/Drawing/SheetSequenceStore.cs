using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 169
//
// SheetSequenceStore persists per-bucket "next sequence" counters in
// ExtensibleStorage on ProjectInformation so the producer + renumber
// command share a single source of truth across sessions. Bucket key
// is (DrawingTypeId, packageId, discipline, vol) — matching the
// granularity at which an ISO 19650 sheet number is unique.
//
// On first read for a bucket the store falls back to scanning live
// sheets to seed the counter, so existing projects upgrade transparently.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Drawing
{
    public static class SheetSequenceStore
    {
        // Schema GUID — stable across versions. Generated for STING Phase 169.
        private static readonly Guid SchemaGuid = new Guid("a1f9b2e3-4c7d-4a08-9f5e-7d8c6b5a4e21");
        private const string SchemaName        = "STING_SheetSequenceStore";
        private const string FieldName         = "BucketsJson";

        private static Schema GetOrCreateSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;
            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName(SchemaName);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetVendorId("STING");
            sb.AddSimpleField(FieldName, typeof(string));
            return sb.Finish();
        }

        /// <summary>
        /// Get-and-increment the persisted "next" sequence for a bucket.
        /// Caller holds an active transaction. Returns the value to use for
        /// the new sheet (1-based for an empty bucket).
        /// </summary>
        public static int Next(Document doc, string drawingTypeId, string packageId, string discipline, string vol)
        {
            var key = BucketKey(drawingTypeId, packageId, discipline, vol);
            var buckets = ReadAll(doc);
            int current;
            if (!buckets.TryGetValue(key, out current))
                current = SeedFromExistingSheets(doc, drawingTypeId, packageId);
            int next = current + 1;
            buckets[key] = next;
            WriteAll(doc, buckets);
            return next;
        }

        /// <summary>Peek without incrementing (for previews and validation).</summary>
        public static int Peek(Document doc, string drawingTypeId, string packageId, string discipline, string vol)
        {
            var key = BucketKey(drawingTypeId, packageId, discipline, vol);
            var buckets = ReadAll(doc);
            if (buckets.TryGetValue(key, out var v)) return v;
            return SeedFromExistingSheets(doc, drawingTypeId, packageId);
        }

        /// <summary>
        /// Reset a bucket's counter — used by the renumber command after
        /// it compacts that bucket's gaps so the next new sheet picks up
        /// from the new high-water mark.
        /// </summary>
        public static void Set(Document doc, string drawingTypeId, string packageId, string discipline, string vol, int newValue)
        {
            var key = BucketKey(drawingTypeId, packageId, discipline, vol);
            var buckets = ReadAll(doc);
            buckets[key] = newValue;
            WriteAll(doc, buckets);
        }

        /// <summary>
        /// Read every bucket (read-only). Returns an empty dict when nothing
        /// has been stored yet.
        /// </summary>
        public static Dictionary<string, int> ReadAll(Document doc)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return result;
                var schema = GetOrCreateSchema();
                var entity = pi.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return result;
                var json = entity.Get<string>(FieldName);
                if (string.IsNullOrEmpty(json)) return result;
                // Lightweight format: "key1=val1\nkey2=val2\n…"
                foreach (var line in json.Split('\n'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = line.Substring(0, eq);
                    var v = line.Substring(eq + 1);
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        result[k] = n;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SheetSequenceStore.ReadAll: {ex.Message}"); }
            return result;
        }

        private static void WriteAll(Document doc, Dictionary<string, int> buckets)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return;
                var schema = GetOrCreateSchema();
                var entity = new Entity(schema);
                var sb = new System.Text.StringBuilder();
                foreach (var kv in buckets)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    sb.Append(kv.Key).Append('=')
                      .Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
                entity.Set(FieldName, sb.ToString());
                pi.SetEntity(entity);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SheetSequenceStore.WriteAll: {ex.Message}"); }
        }

        private static string BucketKey(string drawingTypeId, string packageId, string discipline, string vol)
            => string.Join("|",
                drawingTypeId ?? "",
                packageId     ?? "",
                discipline    ?? "",
                vol           ?? "");

        // First-run fallback. A project that's been numbering sheets for years
        // before Phase 169 has no stored counter; seed from the highest
        // existing sequence in the bucket so the next call doesn't collide.
        private static int SeedFromExistingSheets(Document doc, string drawingTypeId, string packageId)
        {
            try
            {
                int max = 0;
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .Where(s => string.Equals(
                        StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? "",
                        drawingTypeId ?? "", StringComparison.OrdinalIgnoreCase))
                    .Where(s => string.Equals(
                        StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "",
                        packageId ?? "", StringComparison.Ordinal));
                foreach (var s in sheets)
                {
                    var seq = DrawingTokenContext.ExtractSeqFromSheetNumber(s.SheetNumber);
                    if (seq.HasValue && seq.Value > max) max = seq.Value;
                }
                return max;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }
    }
}
