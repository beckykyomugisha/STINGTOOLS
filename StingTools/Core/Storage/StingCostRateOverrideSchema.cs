// Pack 126 / Gap N — per-element cost-rate provenance.
//
// cost_rates_5d.csv ships category-level rates that every element
// inherits. Project managers need to override those per-element
// without editing the CSV ("this AHU comes in at £8500 not the £6200
// catalogue rate"). Today they have nowhere to put the override.
//
// Per-element ES schema captures the override rate, the unit (each /
// linear-m / m² / m³), and the manager's note explaining why.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingCostRateOverrideSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1243-8411-F6E5D4C3B2AF");

        private const string SchemaName        = "StingCostRateOverrideSchema";
        private const string FieldRate         = "RateGbp";
        private const string FieldUnit         = "Unit";
        private const string FieldNote         = "Note";
        private const string FieldStampedTicks = "StampedUtcTicks";
        private const string FieldStampedBy    = "StampedBy";

        public class Override
        {
            public double RateGbp;
            public string Unit { get; set; } = "";   // "each", "lin-m", "m2", "m3"
            public string Note { get; set; } = "";
            public long   StampedUtcTicks;
            public string StampedBy { get; set; } = "";
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
                sb.AddSimpleField(FieldRate,         typeof(double))
                    .SetDocumentation("Override rate in GBP — overrides the cost_rates_5d.csv category default");
                sb.AddSimpleField(FieldUnit,         typeof(string))
                    .SetDocumentation("each / lin-m / m2 / m3");
                sb.AddSimpleField(FieldNote,         typeof(string))
                    .SetDocumentation("Free-text justification surfaced in the cost report");
                sb.AddSimpleField(FieldStampedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks at the time of override");
                sb.AddSimpleField(FieldStampedBy,    typeof(string))
                    .SetDocumentation("Environment.UserName at the time of override");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Override Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Override
                {
                    RateGbp         = entity.Get<double>(FieldRate),
                    Unit            = entity.Get<string>(FieldUnit) ?? "",
                    Note            = entity.Get<string>(FieldNote) ?? "",
                    StampedUtcTicks = entity.Get<long>(FieldStampedTicks),
                    StampedBy       = entity.Get<string>(FieldStampedBy) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, double rate, string unit, string note)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldRate,         rate);
                entity.Set(FieldUnit,         unit ?? "each");
                entity.Set(FieldNote,         note ?? "");
                entity.Set(FieldStampedTicks, DateTime.UtcNow.Ticks);
                entity.Set(FieldStampedBy,    Environment.UserName ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostRateOverrideSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
