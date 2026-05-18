#nullable enable
// Gap 2 / Phase 121 — Extensible Storage facade.
//
// One stop for every STING read-site that wants to honour ES-first,
// shared-parameter-fallback. Keeps the migration invisible to callers:
// a command uses StingEsHelpers.ReadTagPos(el) without caring whether
// the value lives in an ES entity or a legacy STING_TAG_POS int.
//
// Three patterns encoded here:
//
//   ReadFoo(element)   — ES-preferred with shared-param fallback.
//                        Returns null/0/empty when nothing is set.
//   WriteFoo(element)  — writes to ES. Dual-write to the legacy shared
//                        parameter happens at the call-site for safety
//                        until the transition window closes.
//   TryImportFoo(el)   — idempotent: if ES is empty but shared param is
//                        set, copy shared → ES. The migration command
//                        calls this for every element.

using System;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingEsHelpers
    {
        // ─── Cluster ─────────────────────────────────────────────────

        public static StingClusterSchema.ClusterData ReadCluster(Element el)
        {
            if (el == null) return null;
            var fromEs = StingClusterSchema.Read(el);
            if (fromEs != null && !fromEs.IsEmpty) return fromEs;

            // Legacy shared-parameter fallback — unchanged for pre-migration projects
            try
            {
                int count = ParameterHelpers.GetInt(el, ParamRegistry.CLUSTER_COUNT, 0);
                string label = ParameterHelpers.GetString(el, ParamRegistry.CLUSTER_LABEL);
                if (count > 0 || !string.IsNullOrEmpty(label))
                    return new StingClusterSchema.ClusterData { Count = count, Label = label };
            }
            catch { }
            return null;
        }

        public static bool TryImportCluster(Element el)
        {
            if (el == null) return false;
            var existing = StingClusterSchema.Read(el);
            if (existing != null && !existing.IsEmpty) return false; // idempotent
            int count = ParameterHelpers.GetInt(el, ParamRegistry.CLUSTER_COUNT, 0);
            string label = ParameterHelpers.GetString(el, ParamRegistry.CLUSTER_LABEL);
            if (count == 0 && string.IsNullOrEmpty(label)) return false;
            return StingClusterSchema.Write(el, new StingClusterSchema.ClusterData
            {
                Count    = count,
                Label    = label ?? "",
                GroupKey = "",
            });
        }

        // ─── Position ────────────────────────────────────────────────

        public static int ReadTagPos(Element el)
        {
            if (el == null) return 0;
            var fromEs = StingPositionSchema.Read(el);
            if (fromEs != null && fromEs.TagPos != 0) return fromEs.TagPos;
            return ParameterHelpers.GetInt(el, ParamRegistry.TAG_POS, 0);
        }

        public static int ReadTokenPresenceMask(Element el)
        {
            if (el == null) return 0;
            var fromEs = StingPositionSchema.Read(el);
            if (fromEs != null && fromEs.TokenPresence != 0) return fromEs.TokenPresence;

            // First-read derives from shared-parameter values so the migration
            // command can write the computed mask back to ES.
            int mask = 0;
            void Bit(string paramName, int bit)
            {
                if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, paramName))) mask |= (1 << bit);
            }
            Bit(ParamRegistry.DISC, 0);
            Bit(ParamRegistry.LOC,  1);
            Bit(ParamRegistry.ZONE, 2);
            Bit(ParamRegistry.LVL,  3);
            Bit(ParamRegistry.SYS,  4);
            Bit(ParamRegistry.FUNC, 5);
            Bit(ParamRegistry.PROD, 6);
            Bit(ParamRegistry.SEQ,  7);
            return mask;
        }

        public static bool TryImportPosition(Element el)
        {
            if (el == null) return false;
            var existing = StingPositionSchema.Read(el);
            if (existing != null && !existing.IsEmpty) return false;
            int tagPos = ParameterHelpers.GetInt(el, ParamRegistry.TAG_POS, 0);
            int mask   = ReadTokenPresenceMask(el);  // derives from shared params when ES empty
            if (tagPos == 0 && mask == 0) return false;
            return StingPositionSchema.Write(el, new StingPositionSchema.PositionData
            {
                TagPos        = tagPos,
                TokenPresence = mask,
            });
        }

        // ─── Tag history ─────────────────────────────────────────────

        public static StingTagHistorySchema.HistoryData ReadHistory(Element el)
        {
            if (el == null) return null;
            var fromEs = StingTagHistorySchema.Read(el);
            if (fromEs != null && (fromEs.ModifiedUtcTicks > 0 || !string.IsNullOrEmpty(fromEs.PreviousTag)))
                return fromEs;

            // Legacy fallback — parse the old string timestamp as best effort.
            string prev = ParameterHelpers.GetString(el, "ASS_TAG_PREV_TXT");
            string stamp = ParameterHelpers.GetString(el, "ASS_TAG_MODIFIED_DT");
            long ticks = 0;
            if (!string.IsNullOrEmpty(stamp) &&
                DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var dt))
                ticks = dt.Ticks;
            if (ticks == 0 && string.IsNullOrEmpty(prev)) return null;
            return new StingTagHistorySchema.HistoryData
            {
                PreviousTag = prev ?? "",
                ModifiedUtcTicks = ticks,
                RevisionCode = "",
            };
        }

        public static bool TryImportHistory(Element el)
        {
            if (el == null) return false;
            var existing = StingTagHistorySchema.Read(el);
            if (existing != null && existing.ModifiedUtcTicks > 0) return false;
            var legacy = ReadHistory(el);
            if (legacy == null) return false;
            return StingTagHistorySchema.Write(el, legacy.PreviousTag,
                legacy.ModifiedUtcTicks > 0
                    ? new DateTime(legacy.ModifiedUtcTicks, DateTimeKind.Utc)
                    : DateTime.UtcNow,
                legacy.RevisionCode);
        }

        // ─── Stale (delegates to Pack 10 pilot) ──────────────────────

        public static bool? ReadStale(Element el) => StingSchemaBuilder.ReadStale(el);

        public static bool TryImportStale(Element el)
        {
            if (el == null) return false;
            var p = el.LookupParameter("STING_STALE_BOOL");
            if (p == null || !p.HasValue || p.StorageType != StorageType.Integer) return false;
            // StingSchemaBuilder.WriteStale is idempotent enough — it re-writes
            // the same value — but we re-do the absence check here so the
            // migration counter increments only on first import.
            var schema = StingSchemaBuilder.GetOrCreateStaleSchema();
            if (schema == null) return false;
            try
            {
                var entity = el.GetEntity(schema);
                if (entity != null && entity.IsValid()) return false;
            }
            catch { }
            return StingSchemaBuilder.WriteStale(el, p.AsInteger() != 0);
        }
    }
}
