// StingTools — Drawing Template Manager · QR anchor storage
//
// The QR cell is per-title-block data, and Revit will not let a project
// parameter hold it: OST_TitleBlocks answers false to
// Category.AllowsBoundParameters, so BuildCategorySet resolves 0/1 for any
// binding aimed at it and LoadSharedParams skips the parameter entirely. That
// is not a data-file bug to be fixed by adding a row to RESOLVED_BINDINGS.csv
// — it was tried, and every TB_ parameter came back skipped. Revit simply does
// not bind project parameters to that category.
//
// So the anchor lives in Extensible Storage instead, which is exactly what the
// ES layer exists for (CLAUDE.md: "removing the dependency on shared
// parameters for internal plugin state"). StingViewCropSchema is the same shape
// and the same reasoning, one feature along.
//
// Written on the title-block FAMILY SYMBOL (the type), because "where the QR
// goes on this title block" is a property of the title block, not of one sheet
// — set it once and every sheet on that type inherits it. An entity on the
// INSTANCE overrides the type for that one sheet, which is how a project
// nudges the cell on a single odd sheet without forking a type.
//
// TB_QR_ANCHOR_JSON_TXT is still read, and still wins over both, when a family
// carries it as a FAMILY parameter authored into the .rfa. That is the one
// form of this value that travels with the family into another project, so a
// project that has gone to the trouble keeps its answer.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingQrAnchorSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1248-8411-F6E5D4C3B2CF");

        private const string SchemaName        = "StingQrAnchorSchema";
        private const string FieldXMm          = "XMm";
        private const string FieldYMm          = "YMm";
        private const string FieldSizeMm       = "SizeMm";
        private const string FieldStampedTicks = "StampedUtcTicks";

        public sealed class AnchorData
        {
            public double XMm { get; set; }
            public double YMm { get; set; }
            public double SizeMm { get; set; }
            public long   StampedUtcTicks { get; set; }
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
                sb.AddSimpleField(FieldXMm, typeof(double))
                    .SetDocumentation("QR cell left edge, mm from the sheet origin (bottom-left)");
                sb.AddSimpleField(FieldYMm, typeof(double))
                    .SetDocumentation("QR cell bottom edge, mm from the sheet origin (bottom-left)");
                sb.AddSimpleField(FieldSizeMm, typeof(double))
                    .SetDocumentation("Printed QR size in mm (the cell is square)");
                sb.AddSimpleField(FieldStampedTicks, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks when the anchor was written");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingQrAnchorSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        /// <summary>Read the anchor stored on exactly this element. Returns null when
        /// there is none — callers walk instance → type themselves so the two cases
        /// stay distinguishable.</summary>
        public static AnchorData Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;

                var data = new AnchorData
                {
                    XMm             = entity.Get<double>(FieldXMm),
                    YMm             = entity.Get<double>(FieldYMm),
                    SizeMm          = entity.Get<double>(FieldSizeMm),
                    StampedUtcTicks = entity.Get<long>(FieldStampedTicks),
                };
                // A zero size is not an anchor — it is an entity that was written
                // badly or half-migrated. Say nothing rather than place a 0mm code.
                if (data.SizeMm <= 0) return null;
                return data;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingQrAnchorSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Write the anchor. Requires an active transaction.</summary>
        public static bool Write(Element el, double xMm, double yMm, double sizeMm)
        {
            if (el == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldXMm, xMm);
                entity.Set(FieldYMm, yMm);
                entity.Set(FieldSizeMm, sizeMm);
                entity.Set(FieldStampedTicks, DateTime.UtcNow.Ticks);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingQrAnchorSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Remove the anchor from this element. Requires an active
        /// transaction. Returns false when there was nothing to clear.</summary>
        public static bool Clear(Element el)
        {
            if (el == null) return false;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return false;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return false;
                el.DeleteEntity(schema);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingQrAnchorSchema.Clear {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
