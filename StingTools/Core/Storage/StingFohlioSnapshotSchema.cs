// Phase 192 (C1) — Fohlio import snapshot ES schema.
//
// Stores the last-imported Fohlio field snapshot per element so Fohlio_Audit
// can flag rows whose model parameters have drifted from the last Fohlio import
// (the "Fohlio kept current" KPI). The snapshot is a small JSON blob of the
// imported field → value pairs plus the Fohlio item ref and import timestamp.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingFohlioSnapshotSchema
    {
        // v1 (Phase 192 C1) — FohlioRef + SnapshotJson + CapturedUtcTicks.
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1245-8411-F6E5D4C3B2CD");
        // v2 (Phase C — KUT lifecycle) — adds the procurement cost fields the
        // FohlioRateProvider pulls into the BOQ. A new GUID is required because
        // ExtensibleStorage schemas are immutable once registered in a document;
        // Read falls back to v1 for elements snapshotted before this phase.
        public static readonly Guid SchemaGuidV2 =
            new Guid("E1A7B2C4-1011-1246-8411-F6E5D4C3B2CD");

        private const string SchemaName        = "StingFohlioSnapshotSchema";
        private const string SchemaNameV2      = "StingFohlioSnapshotSchemaV2";
        private const string FieldFohlioRef    = "FohlioRef";
        private const string FieldSnapshotJson = "SnapshotJson";
        private const string FieldCapturedTicks= "CapturedUtcTicks";
        private const string FieldUnitCost     = "UnitCost";
        private const string FieldCurrency     = "Currency";
        private const string FieldQtyFromFohlio= "QtyFromFohlio";
        private const string FieldLeadTimeDays = "LeadTimeDays";

        private static Schema GetOrCreateV2()
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
                sb.AddSimpleField(FieldFohlioRef, typeof(string))
                    .SetDocumentation("Fohlio item ref (URL/ID) captured at the last import");
                sb.AddSimpleField(FieldSnapshotJson, typeof(string))
                    .SetDocumentation("JSON of imported field→value pairs at the last Fohlio import");
                sb.AddSimpleField(FieldCapturedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of the last Fohlio import. 0 = never");
                sb.AddSimpleField(FieldUnitCost, typeof(double))
                    .SetDocumentation("Fohlio procurement unit cost in the quote currency");
                sb.AddSimpleField(FieldCurrency, typeof(string))
                    .SetDocumentation("Fohlio quote currency (ISO 4217)");
                sb.AddSimpleField(FieldQtyFromFohlio, typeof(double))
                    .SetDocumentation("Quantity Fohlio holds for this item (for variance, not bill qty)");
                sb.AddSimpleField(FieldLeadTimeDays, typeof(int))
                    .SetDocumentation("Procurement lead time in days, from Fohlio");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingFohlioSnapshotSchema.GetOrCreateV2: {ex.Message}");
                return null;
            }
        }

        public class SnapshotData
        {
            public string FohlioRef = "";
            public string SnapshotJson = "";
            public long CapturedUtcTicks;
            public double UnitCost;          // Phase C — 0 when never costed
            public string Currency = "";
            public double QtyFromFohlio;
            public int LeadTimeDays;

            public DateTime? CapturedUtc =>
                CapturedUtcTicks > 0 ? (DateTime?)new DateTime(CapturedUtcTicks, DateTimeKind.Utc) : null;
        }

        public static SnapshotData Read(Element el)
        {
            if (el == null) return null;
            try
            {
                // Prefer the v2 (cost-bearing) entity, fall back to v1.
                var schemaV2 = Schema.Lookup(SchemaGuidV2);
                if (schemaV2 != null)
                {
                    var e2 = el.GetEntity(schemaV2);
                    if (e2 != null && e2.IsValid())
                        return new SnapshotData
                        {
                            FohlioRef = e2.Get<string>(FieldFohlioRef) ?? "",
                            SnapshotJson = e2.Get<string>(FieldSnapshotJson) ?? "",
                            CapturedUtcTicks = e2.Get<long>(FieldCapturedTicks),
                            UnitCost = e2.Get<double>(FieldUnitCost),
                            Currency = e2.Get<string>(FieldCurrency) ?? "",
                            QtyFromFohlio = e2.Get<double>(FieldQtyFromFohlio),
                            LeadTimeDays = e2.Get<int>(FieldLeadTimeDays),
                        };
                }

                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new SnapshotData
                {
                    FohlioRef = entity.Get<string>(FieldFohlioRef) ?? "",
                    SnapshotJson = entity.Get<string>(FieldSnapshotJson) ?? "",
                    CapturedUtcTicks = entity.Get<long>(FieldCapturedTicks),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingFohlioSnapshotSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Write the snapshot. The optional cost fields default to 0/empty,
        /// so the existing Fohlio_Import callers stay source-compatible.</summary>
        public static bool Write(Element el, string fohlioRef, string snapshotJson, DateTime capturedUtc,
            double unitCost = 0, string currency = "", double qtyFromFohlio = 0, int leadTimeDays = 0)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreateV2();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldFohlioRef, fohlioRef ?? "");
                entity.Set(FieldSnapshotJson, snapshotJson ?? "");
                entity.Set(FieldCapturedTicks, capturedUtc.ToUniversalTime().Ticks);
                entity.Set(FieldUnitCost, unitCost);
                entity.Set(FieldCurrency, currency ?? "");
                entity.Set(FieldQtyFromFohlio, qtyFromFohlio);
                entity.Set(FieldLeadTimeDays, leadTimeDays);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingFohlioSnapshotSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
