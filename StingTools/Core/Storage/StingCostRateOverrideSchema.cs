// ══════════════════════════════════════════════════════════════════════════
//  StingCostRateOverrideSchema — per-element cost override (Extensible Storage).
//
//  v1 (Pack 126):  RateGbp, Unit, Note, StampedUtcTicks, StampedBy
//  v2 (Phase 184): + Currency, WastePercent, OverheadPercent, ProfitPercent,
//                   DayworksCode, LockedByUser, LockedUntilUtcTicks
//  v3 (DSCH-33):   + Outcome ("Priced" / "Nil" / "Included"), IncludedIn —
//                   so an override can declare a deliberate nil or an item
//                   whose cost is carried elsewhere (RateOutcome, DSCH-26).
//
//  Schema versioning strategy
//  ─────────────────────────
//  Extensible Storage schemas are immutable — once a Schema is created with
//  a given GUID and field set, you cannot add or rename fields. To extend,
//  we mint a NEW schema with its own GUID and read all of them at lookup time:
//
//    Read():    v3 first, then v2, then v1. A v1 / v2 entity has no outcome
//               and is read as Priced, which is all it could ever mean.
//    Write():   always v3, and nothing else. Older entities on the element are
//               LEFT IN PLACE (no destructive migration): the reader prefers
//               v3, so they are shadowed, not consulted. Nothing in the plugin
//               deletes a v3 entity, so a shadowed older entity cannot resurface.
//               (Until DSCH-33, Write() deleted a v1 entity on write; the
//               explicit Cost_MigrateESEntities command now does that itself.)
//
//  The outcome encoding, the write rule and the provider answer are Revit-free
//  in BOQ/Rates/RateOverrideOutcome.cs and tested in StingTools.Boq.Tests.
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
using StingTools.BOQ.Rates;
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

        // ── v3 (DSCH-33) ──────────────────────────────────────────────
        // Never rotate: every project that wrote a v3 override is keyed on it.
        public static readonly Guid SchemaGuidV3 =
            new Guid("E1A7B2C4-1011-1243-8411-F6E5D4C3B2B3");
        private const string SchemaNameV3 = "StingCostRateOverrideSchemaV3";

        // v3 renames the rate field: "RateGbp" stopped being true when v2 added Currency.
        private const string FieldRateV3    = "Rate";
        private const string FieldOutcome   = "Outcome";
        private const string FieldIncludedIn = "IncludedIn";

        /// <summary>
        /// Per-element cost override. v3 adds the outcome (Priced / Nil / Included).
        /// v1 / v2 reads continue to work: Outcome = Priced, Currency defaults to
        /// "GBP" for v1 (its implicit assumption), percentages 0, lock fields empty.
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

            // v3 extensions
            public RateOutcome Outcome { get; set; } = RateOutcome.Priced;
            public string IncludedIn { get; set; } = "";

            /// <summary>Which schema version the override was read from (1, 2 or 3).</summary>
            public int SchemaVersion { get; set; }

            /// <summary>
            /// Set when a v3 entity's Outcome field could not be decoded. The override
            /// must not be priced from: the stored rate may belong to a Nil.
            /// </summary>
            public string UnreadableReason { get; set; } = "";

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

        /// <summary>
        /// Returns the v3 schema, creating it on first use. This is the only
        /// version written. (v2 is no longer created by STING: a document that
        /// never had a v2 override has nothing to read from it.)
        /// </summary>
        public static Schema GetOrCreateV3()
        {
            try
            {
                var existing = Schema.Lookup(SchemaGuidV3);
                if (existing != null) return existing;

                var sb = new SchemaBuilder(SchemaGuidV3);
                sb.SetSchemaName(SchemaNameV3);
                sb.SetVendorId(StingSchemaBuilder.VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(FieldRateV3,          typeof(double))
                    .SetDocumentation("Override rate in <Currency>. 0 when Outcome is Nil or Included");
                sb.AddSimpleField(FieldOutcome,         typeof(string))
                    .SetDocumentation("Priced / Nil / Included (RateOutcome name)");
                sb.AddSimpleField(FieldIncludedIn,      typeof(string))
                    .SetDocumentation("Item that carries the cost when Outcome is Included (e.g. E10/2)");
                sb.AddSimpleField(FieldUnit,            typeof(string))
                    .SetDocumentation("each / lin-m / m2 / m3 / kg");
                sb.AddSimpleField(FieldCurrency,        typeof(string))
                    .SetDocumentation("ISO 4217 currency of the rate (GBP / UGX / USD / EUR)");
                sb.AddSimpleField(FieldNote,            typeof(string))
                    .SetDocumentation("Free-text justification");
                sb.AddSimpleField(FieldStampedTicks,    typeof(long));
                sb.AddSimpleField(FieldStampedBy,       typeof(string));
                sb.AddSimpleField(FieldWastePct,        typeof(double))
                    .SetDocumentation("Waste uplift % applied to the quantity");
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
                StingLog.Warn($"StingCostRateOverrideSchema.GetOrCreate v3: {ex.Message}");
                return null;
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Read (v3, then v2, then v1)
        // ──────────────────────────────────────────────────────────────

        public static Override Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schemaV3 = Schema.Lookup(SchemaGuidV3);
                if (schemaV3 != null)
                {
                    var entityV3 = el.GetEntity(schemaV3);
                    if (entityV3 != null && entityV3.IsValid())
                        return ReadV3Entity(el, entityV3);
                }

                var schemaV2 = Schema.Lookup(SchemaGuidV2);
                if (schemaV2 != null)
                {
                    var entityV2 = el.GetEntity(schemaV2);
                    if (entityV2 != null && entityV2.IsValid())
                        return ReadV2Entity(entityV2);
                }

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

        private static Override ReadV3Entity(Element el, Entity e)
        {
            string storedOutcome = e.Get<string>(FieldOutcome) ?? "";
            string unreadable = "";
            if (!RateOverrideOutcome.TryDecode(storedOutcome, out RateOutcome outcome))
            {
                unreadable = $"v3 Outcome '{storedOutcome}' is not Priced / Nil / Included";
                StingLog.Warn($"StingCostRateOverrideSchema.Read {el?.Id}: {unreadable} - override not used.");
            }
            return new Override
            {
                SchemaVersion       = 3,
                Rate                = e.Get<double>(FieldRateV3),
                Outcome             = outcome,
                IncludedIn          = e.Get<string>(FieldIncludedIn) ?? "",
                UnreadableReason    = unreadable,
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
        }

        private static Override ReadV2Entity(Entity e) => new Override
        {
            SchemaVersion       = 2,
            Outcome             = RateOutcome.Priced,   // v2 cannot declare anything else
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
            SchemaVersion   = 1,
            Outcome         = RateOutcome.Priced,       // v1 cannot declare anything else
            Rate            = e.Get<double>(FieldRate),
            Unit            = e.Get<string>(FieldUnit) ?? "",
            Currency        = "GBP",          // v1 implicit assumption
            Note            = e.Get<string>(FieldNote) ?? "",
            StampedUtcTicks = e.Get<long>(FieldStampedTicks),
            StampedBy       = e.Get<string>(FieldStampedBy) ?? "",
            // v2 / v3 fields stay at defaults
        };

        // ──────────────────────────────────────────────────────────────
        //  Write (v3 only; older entities are left in place)
        // ──────────────────────────────────────────────────────────────

        public static bool Write(Element el, double rate, string unit, string note)
            => Write(el, rate, unit, "GBP", note, 0, 0, 0, "", "", 0);

        /// <summary>A priced override (Outcome = Priced).</summary>
        public static bool Write(Element el, double rate, string unit, string currency,
            string note, double wastePercent, double overheadPercent, double profitPercent,
            string dayworksCode, string lockedByUser, long lockedUntilUtcTicks)
            => Write(el, rate, RateOutcome.Priced, "", unit, currency, note,
                     wastePercent, overheadPercent, profitPercent,
                     dayworksCode, lockedByUser, lockedUntilUtcTicks);

        /// <summary>
        /// Full v3 write. Refused (logged, returns false) when the combination is
        /// contradictory — see <see cref="RateOverrideOutcome.CheckWrite"/>: a Nil or
        /// Included override carries rate 0. Older v1 / v2 entities on the element
        /// are not touched; v3 shadows them on read.
        /// </summary>
        public static bool Write(Element el, double rate, RateOutcome outcome, string includedIn,
            string unit, string currency, string note,
            double wastePercent, double overheadPercent, double profitPercent,
            string dayworksCode, string lockedByUser, long lockedUntilUtcTicks)
        {
            if (el == null) return false;
            string refusal = RateOverrideOutcome.CheckWrite(rate, outcome, includedIn);
            if (refusal != null)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.Write {el.Id}: refused - {refusal}.");
                return false;
            }
            try
            {
                var schema = GetOrCreateV3();
                if (schema == null) return false;

                var entity = new Entity(schema);
                entity.Set(FieldRateV3,           rate);
                entity.Set(FieldOutcome,          RateOverrideOutcome.Encode(outcome));
                entity.Set(FieldIncludedIn,       outcome == RateOutcome.Included ? (includedIn ?? "").Trim() : "");
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
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }

        private static string NonEmpty(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
