// ══════════════════════════════════════════════════════════════════════════
//  StingCostRateOverrideSchema — per-element cost override (Extensible Storage).
//
//  v1 (Pack 126):  RateGbp, Unit, Note, StampedUtcTicks, StampedBy
//  v2 (Phase 184): + Currency, WastePercent, OverheadPercent, ProfitPercent,
//                   DayworksCode, LockedByUser, LockedUntilUtcTicks
//
//  Schema versioning strategy
//  ─────────────────────────
//  Extensible Storage schemas are immutable — once a Schema is created with
//  a given GUID and field set, you cannot add or rename fields. To extend,
//  we mint a SECOND schema with its own GUID and read both at lookup time:
//
//    Read():    try v2 first, then fall back to v1 — back-compat for any
//               project that had v1 entities stamped before this commit.
//    Write():   always v2. We also delete the v1 entity on write so the
//               element doesn't carry stale data in two places.
//
//  Lock semantics
//  ──────────────
//  LockedByUser + LockedUntilUtcTicks support pessimistic locking — the
//  field is informational at this layer (the v1 plugin had no concept of
//  locks). Future P5.1 / P5.2 work uses these fields to prevent edits to
//  rows tied to issued payment certificates and approved variations.
//
//  P0.1 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingCostRateOverrideSchema
    {
        // ── v1 (legacy) ───────────────────────────────────────────────
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1243-8411-F6E5D4C3B2AF");
        private const string SchemaNameV1 = "StingCostRateOverrideSchema";

        private const string FieldRate         = "RateGbp";
        private const string FieldUnit         = "Unit";
        private const string FieldNote         = "Note";
        private const string FieldStampedTicks = "StampedUtcTicks";
        private const string FieldStampedBy    = "StampedBy";

        // ── v2 (Phase 184) ────────────────────────────────────────────
        public static readonly Guid SchemaGuidV2 =
            new Guid("E1A7B2C4-1011-1243-8411-F6E5D4C3B2B2");
        private const string SchemaNameV2 = "StingCostRateOverrideSchemaV2";

        private const string FieldCurrency        = "Currency";
        private const string FieldWastePct        = "WastePercent";
        private const string FieldOverheadPct     = "OverheadPercent";
        private const string FieldProfitPct       = "ProfitPercent";
        private const string FieldDayworksCode    = "DayworksCode";
        private const string FieldLockedByUser    = "LockedByUser";
        private const string FieldLockedUntilTicks = "LockedUntilUtcTicks";

        /// <summary>
        /// Per-element cost override. v2 carries the extended QS fields
        /// (waste / overhead / profit / dayworks / lock). v1 reads
        /// continue to work — Currency defaults to "GBP" (the v1
        /// implicit assumption), all percentage fields default to 0,
        /// lock fields stay empty.
        /// </summary>
        public class Override
        {
            public double Rate;                  // legacy alias: RateGbp
            public string Unit { get; set; } = "";   // "each", "lin-m", "m2", "m3"
            public string Currency { get; set; } = "GBP";  // ISO 4217
            public string Note { get; set; } = "";
            public long   StampedUtcTicks;
            public string StampedBy { get; set; } = "";

            // v2 extensions
            public double WastePercent { get; set; } = 0;
            public double OverheadPercent { get; set; } = 0;
            public double ProfitPercent { get; set; } = 0;
            public string DayworksCode { get; set; } = "";
            public string LockedByUser { get; set; } = "";
            public long   LockedUntilUtcTicks = 0;

            /// <summary>Back-compat alias — old callers read .RateGbp.</summary>
            public double RateGbp
            {
                get => string.Equals(Currency, "GBP", StringComparison.OrdinalIgnoreCase) ? Rate : 0;
                set { Rate = value; Currency = "GBP"; }
            }

            /// <summary>True when LockedUntilUtcTicks is in the future.</summary>
            public bool IsLocked => LockedUntilUtcTicks > DateTime.UtcNow.Ticks;
        }

        // ──────────────────────────────────────────────────────────────
        //  Schema creation
        // ──────────────────────────────────────────────────────────────

        /// <summary>Returns the v1 schema (used for reading legacy entities).</summary>
        public static Schema GetOrCreate()
        {
            try
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null) return existing;

                var sb = new SchemaBuilder(SchemaGuid);
                sb.SetSchemaName(SchemaNameV1);
                sb.SetVendorId(StingSchemaBuilder.VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(FieldRate,         typeof(double));
                sb.AddSimpleField(FieldUnit,         typeof(string));
                sb.AddSimpleField(FieldNote,         typeof(string));
                sb.AddSimpleField(FieldStampedTicks, typeof(long));
                sb.AddSimpleField(FieldStampedBy,    typeof(string));
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.GetOrCreate v1: {ex.Message}");
                return null;
            }
        }

        public static Schema GetOrCreateV2()
        {
            try
            {
                var existing = Schema.Lookup(SchemaGuidV2);
                if (existing != null) return existing;

                var sb = new SchemaBuilder(SchemaGuidV2);
                sb.SetSchemaName(SchemaNameV2);
                sb.SetVendorId(StingSchemaBuilder.VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(FieldRate,            typeof(double))
                    .SetDocumentation("Override rate in <Currency> — overrides cost_rates_5d.csv defaults");
                sb.AddSimpleField(FieldUnit,            typeof(string))
                    .SetDocumentation("each / lin-m / m2 / m3 / kg");
                sb.AddSimpleField(FieldCurrency,        typeof(string))
                    .SetDocumentation("ISO 4217 currency of the rate (GBP / UGX / USD / EUR)");
                sb.AddSimpleField(FieldNote,            typeof(string))
                    .SetDocumentation("Free-text justification");
                sb.AddSimpleField(FieldStampedTicks,    typeof(long));
                sb.AddSimpleField(FieldStampedBy,       typeof(string));
                sb.AddSimpleField(FieldWastePct,        typeof(double))
                    .SetDocumentation("Waste uplift % applied per item");
                sb.AddSimpleField(FieldOverheadPct,     typeof(double))
                    .SetDocumentation("Overhead % applied per item (separate from global PrelimPct)");
                sb.AddSimpleField(FieldProfitPct,       typeof(double))
                    .SetDocumentation("Profit margin %");
                sb.AddSimpleField(FieldDayworksCode,    typeof(string))
                    .SetDocumentation("Cross-ref to dayworks schedule entry");
                sb.AddSimpleField(FieldLockedByUser,    typeof(string))
                    .SetDocumentation("User who locked this override (e.g. cert issuer)");
                sb.AddSimpleField(FieldLockedUntilTicks, typeof(long))
                    .SetDocumentation("DateTime.Ticks until which the override is locked");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.GetOrCreate v2: {ex.Message}");
                return null;
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Read (v2-preferred with v1 fallback)
        // ──────────────────────────────────────────────────────────────

        public static Override Read(Element el)
        {
            if (el == null) return null;
            try
            {
                // Try v2 first.
                var schemaV2 = Schema.Lookup(SchemaGuidV2);
                if (schemaV2 != null)
                {
                    var entityV2 = el.GetEntity(schemaV2);
                    if (entityV2 != null && entityV2.IsValid())
                        return ReadV2Entity(entityV2);
                }

                // Fall back to v1.
                var schemaV1 = Schema.Lookup(SchemaGuid);
                if (schemaV1 != null)
                {
                    var entityV1 = el.GetEntity(schemaV1);
                    if (entityV1 != null && entityV1.IsValid())
                        return ReadV1Entity(entityV1);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.Read {el?.Id}: {ex.Message}");
            }
            return null;
        }

        private static Override ReadV2Entity(Entity e) => new Override
        {
            Rate                = e.Get<double>(FieldRate),
            Unit                = e.Get<string>(FieldUnit) ?? "",
            Currency            = NonEmpty(e.Get<string>(FieldCurrency), "GBP"),
            Note                = e.Get<string>(FieldNote) ?? "",
            StampedUtcTicks     = e.Get<long>(FieldStampedTicks),
            StampedBy           = e.Get<string>(FieldStampedBy) ?? "",
            WastePercent        = e.Get<double>(FieldWastePct),
            OverheadPercent     = e.Get<double>(FieldOverheadPct),
            ProfitPercent       = e.Get<double>(FieldProfitPct),
            DayworksCode        = e.Get<string>(FieldDayworksCode) ?? "",
            LockedByUser        = e.Get<string>(FieldLockedByUser) ?? "",
            LockedUntilUtcTicks = e.Get<long>(FieldLockedUntilTicks)
        };

        private static Override ReadV1Entity(Entity e) => new Override
        {
            Rate            = e.Get<double>(FieldRate),
            Unit            = e.Get<string>(FieldUnit) ?? "",
            Currency        = "GBP",          // v1 implicit assumption
            Note            = e.Get<string>(FieldNote) ?? "",
            StampedUtcTicks = e.Get<long>(FieldStampedTicks),
            StampedBy       = e.Get<string>(FieldStampedBy) ?? "",
            // v2-only fields stay at defaults
        };

        // ──────────────────────────────────────────────────────────────
        //  Write (v2; deletes v1 entity if present)
        // ──────────────────────────────────────────────────────────────

        public static bool Write(Element el, double rate, string unit, string note)
            => Write(el, rate, unit, "GBP", note, 0, 0, 0, "", "", 0);

        /// <summary>
        /// Full v2 write. Existing v1 entity (if any) is deleted so the
        /// element doesn't carry stale data in two schemas.
        /// </summary>
        public static bool Write(Element el, double rate, string unit, string currency,
            string note, double wastePercent, double overheadPercent, double profitPercent,
            string dayworksCode, string lockedByUser, long lockedUntilUtcTicks)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreateV2();
                if (schema == null) return false;

                var entity = new Entity(schema);
                entity.Set(FieldRate,             rate);
                entity.Set(FieldUnit,             unit ?? "each");
                entity.Set(FieldCurrency,         NonEmpty(currency, "GBP"));
                entity.Set(FieldNote,             note ?? "");
                entity.Set(FieldStampedTicks,     DateTime.UtcNow.Ticks);
                entity.Set(FieldStampedBy,        Environment.UserName ?? "");
                entity.Set(FieldWastePct,         wastePercent);
                entity.Set(FieldOverheadPct,      overheadPercent);
                entity.Set(FieldProfitPct,        profitPercent);
                entity.Set(FieldDayworksCode,     dayworksCode ?? "");
                entity.Set(FieldLockedByUser,     lockedByUser ?? "");
                entity.Set(FieldLockedUntilTicks, lockedUntilUtcTicks);
                el.SetEntity(entity);

                // Clean up any orphan v1 entity so subsequent reads
                // don't return stale data.
                TryDeleteV1Entity(el);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }

        private static void TryDeleteV1Entity(Element el)
        {
            try
            {
                var schemaV1 = Schema.Lookup(SchemaGuid);
                if (schemaV1 == null) return;
                var existing = el.GetEntity(schemaV1);
                if (existing != null && existing.IsValid())
                    el.DeleteEntity(schemaV1);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.TryDeleteV1Entity {el?.Id}: {ex.Message}");
            }
        }

        private static string NonEmpty(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
