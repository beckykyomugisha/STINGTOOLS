// StingTools — Drawing Template Manager · Phase 184
//
// StingViewCropSchema persists the Phase 183 crop stamp (kind +
// marginMm) in Extensible Storage on the View element so the drift
// detector can spot bbox-derived crops that have fallen behind the
// profile WITHOUT requiring the project to have run LoadSharedParams.
//
// The legacy shared parameters (STING_CROP_KIND_TXT,
// STING_CROP_MARGIN_MM_TXT) stay in MR_PARAMETERS.txt for tooling
// that wants to read the value via schedules / filters / Dynamo —
// when the params are bound, StampCrop writes to both surfaces and
// ReadCrop prefers the ES value. Pre-migration projects (params
// unbound) get full crop-drift support via ES alone.

using System;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingViewCropSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1244-8411-F6E5D4C3B2CC");

        private const string SchemaName     = "StingViewCropSchema";
        private const string FieldKind      = "Kind";       // string
        private const string FieldMarginMm  = "MarginMm";   // double
        private const string FieldStampedTicks = "StampedUtcTicks"; // long

        public sealed class CropStamp
        {
            public string Kind { get; set; } = "";
            public double MarginMm { get; set; }
            public long   StampedUtcTicks;
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
                sb.AddSimpleField(FieldKind, typeof(string))
                    .SetDocumentation("DrawingType.Crop.Kind as of last apply (ScopeBox / TightBbox / RoomBoundary / ScopeBoxOrBbox / None)");
                sb.AddSimpleField(FieldMarginMm, typeof(double))
                    .SetDocumentation("DrawingType.Crop.MarginMm as of last apply");
                sb.AddSimpleField(FieldStampedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks when the stamp was written");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingViewCropSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static CropStamp Read(View view)
        {
            if (view == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = view.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new CropStamp
                {
                    Kind            = entity.Get<string>(FieldKind) ?? "",
                    MarginMm        = entity.Get<double>(FieldMarginMm),
                    StampedUtcTicks = entity.Get<long>(FieldStampedTicks),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingViewCropSchema.Read {view?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Write the stamp. Requires an active transaction.</summary>
        public static bool Write(View view, string kind, double marginMm)
        {
            if (view == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldKind, kind ?? "");
                entity.Set(FieldMarginMm, marginMm);
                entity.Set(FieldStampedTicks, DateTime.UtcNow.Ticks);
                view.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingViewCropSchema.Write {view?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
