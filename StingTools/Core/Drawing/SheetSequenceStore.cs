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
        ///
        /// Throws rather than returning an unpersisted number: a sequence the
        /// store could not save is worse than no sequence, because the caller
        /// stamps it on a sheet and the next session hands out the same one.
        /// DrawingProducer.ResolveSheetSequence catches this and falls back to
        /// its legacy counter, so production still proceeds — loudly.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Counters could not be read, or could not be persisted.
        /// </exception>
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

        /// <summary>
        /// The value <see cref="Next"/> would return, without consuming it.
        ///
        /// E-13: this used to return the STORED value, which is the last
        /// number already used — Next stores what it hands out — so a preview
        /// showed the number of the previous sheet. No caller existed to
        /// notice; fixed before one appears.
        ///
        /// Read failures degrade to the live-sheet seed rather than throwing:
        /// a preview must not break the caller, and unlike Next it writes
        /// nothing, so a wrong guess here cannot corrupt stored state.
        /// </summary>
        public static int Peek(Document doc, string drawingTypeId, string packageId, string discipline, string vol)
        {
            var key = BucketKey(drawingTypeId, packageId, discipline, vol);
            int lastUsed;
            try
            {
                var buckets = ReadAll(doc);
                if (!buckets.TryGetValue(key, out lastUsed))
                    lastUsed = SeedFromExistingSheets(doc, drawingTypeId, packageId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetSequenceStore.Peek: read failed ({ex.Message}); seeding from live sheets.");
                lastUsed = SeedFromExistingSheets(doc, drawingTypeId, packageId);
            }
            return lastUsed + 1;
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
        /// Read every bucket.
        ///
        /// E-5(a): this used to funnel ANY failure into an empty dictionary.
        /// Next then wrote that dictionary back with a single bucket in it,
        /// destroying every other counter in the project — a transient read
        /// error silently reset the whole numbering state.
        ///
        /// "Nothing stored yet" and "could not read what is stored" are now
        /// different outcomes. The first returns empty, which is correct and
        /// safe to write back. The second throws, so Next aborts into its
        /// caller's catch and the legacy fallback path rather than persisting
        /// a dictionary that is missing everything it failed to read.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The stored payload exists but could not be read or parsed.
        /// </exception>
        public static Dictionary<string, int> ReadAll(Document doc)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);

            var pi = doc?.ProjectInformation;
            if (pi == null) return result;               // no document — legitimately empty

            Schema schema;
            try { schema = GetOrCreateSchema(); }
            catch (Exception ex)
            {
                StingLog.Error("SheetSequenceStore.ReadAll: schema unavailable", ex);
                throw new InvalidOperationException(
                    $"Sheet-sequence schema unavailable: {ex.Message}", ex);
            }

            Entity entity;
            try { entity = pi.GetEntity(schema); }
            catch (Exception ex)
            {
                StingLog.Error("SheetSequenceStore.ReadAll: GetEntity failed", ex);
                throw new InvalidOperationException(
                    $"Sheet-sequence counters could not be read: {ex.Message}", ex);
            }

            // Never stored on this project — genuinely empty, safe to write over.
            if (entity == null || !entity.IsValid()) return result;

            string payload;
            try { payload = entity.Get<string>(FieldName); }
            catch (Exception ex)
            {
                StingLog.Error("SheetSequenceStore.ReadAll: field read failed", ex);
                throw new InvalidOperationException(
                    $"Sheet-sequence payload could not be read: {ex.Message}", ex);
            }
            if (string.IsNullOrEmpty(payload)) return result;

            var parsed = ParseBuckets(payload, out int candidateLines, out int badLines);

            // A payload that held data but yielded nothing is corrupt, not
            // empty. Returning empty here is precisely what would let the
            // caller overwrite good state with one bucket.
            if (parsed.Count == 0 && candidateLines > 0)
            {
                StingLog.Error(
                    $"SheetSequenceStore.ReadAll: payload had {candidateLines} line(s) but none parsed — refusing to " +
                    "treat as empty (would wipe all counters on the next write).", null);
                throw new InvalidOperationException(
                    "Sheet-sequence payload is unreadable; counters not overwritten.");
            }
            if (badLines > 0)
                StingLog.Warn($"SheetSequenceStore.ReadAll: skipped {badLines} malformed counter line(s).");

            return parsed;
        }

        /// <summary>
        /// Parse the stored payload. Pure string work, no Revit types, so the
        /// wipe-guard above is testable outside Revit.
        /// Format: "key=value" per line.
        /// </summary>
        /// <param name="candidateLines">Non-empty lines seen.</param>
        /// <param name="badLines">Lines that looked like data but did not parse.</param>
        internal static Dictionary<string, int> ParseBuckets(string payload, out int candidateLines, out int badLines)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            candidateLines = 0;
            badLines = 0;
            if (string.IsNullOrEmpty(payload)) return result;

            foreach (var raw in payload.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (string.IsNullOrEmpty(line)) continue;
                candidateLines++;
                int eq = line.IndexOf('=');
                if (eq <= 0) { badLines++; continue; }
                var k = line.Substring(0, eq);
                var v = line.Substring(eq + 1);
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    result[k] = n;
                else
                    badLines++;
            }
            return result;
        }

        /// <summary>Serialise buckets to the stored payload format. Pure, testable.</summary>
        internal static string FormatBuckets(Dictionary<string, int> buckets)
        {
            var sb = new StringBuilder();
            if (buckets == null) return sb.ToString();
            foreach (var kv in buckets)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                sb.Append(kv.Key).Append('=')
                  .Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Persist every bucket.
        ///
        /// E-5(b)/(c): this used to swallow every failure. Two of them are
        /// silent corruption rather than noise —
        ///   * no active transaction: SetEntity throws, the caller still
        ///     receives a sequence number, and nothing is stored, so the same
        ///     number is handed out again next session;
        ///   * workshared and ProjectInformation is owned by another user:
        ///     the write fails for that user only, so two people produce
        ///     sheets with identical numbers and neither is told.
        /// Both are now detected before the write, named in the message, and
        /// raised so the number is never treated as persisted.
        /// </summary>
        /// <exception cref="InvalidOperationException">Counters were not persisted.</exception>
        private static void WriteAll(Document doc, Dictionary<string, int> buckets)
        {
            var pi = doc?.ProjectInformation;
            if (pi == null) return;

            // Pre-flight 1 — a write outside a transaction cannot persist.
            if (!doc.IsModifiable)
            {
                const string msg = "Sheet-sequence counters not persisted: no active transaction. " +
                                   "The number handed out will be reissued next session.";
                StingLog.Error("SheetSequenceStore.WriteAll: " + msg, null);
                throw new InvalidOperationException(msg);
            }

            // Pre-flight 2 — worksharing ownership of ProjectInformation.
            if (doc.IsWorkshared)
            {
                try
                {
                    var status = WorksharingUtils.GetCheckoutStatus(doc, pi.Id, out string owner);
                    if (status == CheckoutStatus.OwnedByOtherUser)
                    {
                        var msg = "Sheet-sequence counters not persisted — Project Information is owned by " +
                                  $"'{owner}'. Sheet numbers issued now may collide with theirs. " +
                                  "Ask them to synchronise and relinquish, then re-run.";
                        StingLog.Error("SheetSequenceStore.WriteAll: " + msg, null);
                        throw new InvalidOperationException(msg);
                    }
                }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex)
                {
                    // Ownership could not be determined — proceed and let the
                    // write itself be the arbiter, but say so.
                    StingLog.Warn($"SheetSequenceStore.WriteAll: checkout status unavailable ({ex.Message}); attempting write.");
                }
            }

            try
            {
                var schema = GetOrCreateSchema();
                var entity = new Entity(schema);
                entity.Set(FieldName, FormatBuckets(buckets));
                pi.SetEntity(entity);
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetSequenceStore.WriteAll: SetEntity failed", ex);
                throw new InvalidOperationException(
                    $"Sheet-sequence counters not persisted: {ex.Message}", ex);
            }
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
