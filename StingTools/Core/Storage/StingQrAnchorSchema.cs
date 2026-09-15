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
        // GUID bumped once, from ...1248..., after the first layout proved
        // unconstructible: it declared XMm/YMm/SizeMm as `double`, and Revit
        // answered "Units are required for field XMm" — every floating-point ES
        // field needs a SetSpec. GetOrCreate therefore returned null and no
        // document ever registered the old layout, so nothing has to be migrated;
        // the bump only guarantees a half-registered schema can never be found
        // with field names that no longer exist.
        //
        // The replacement stores ONE STRING rather than three specced doubles.
        // A Length-specced field would hold Revit internal units (feet), putting a
        // mm->ft conversion on every read and write of a value whose whole point is
        // to be millimetres — a fresh bug surface, and one the unit tests could not
        // see because they are Revit-free. The string is the same {x,y,size} JSON
        // TB_QR_ANCHOR_JSON_TXT carries, parsed by the same SheetQrConfig.ParseAnchor
        // that is already under test. One format, one parser, no units.
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1249-8411-F6E5D4C3B2D0");

        private const string SchemaName        = "StingQrAnchorSchema";
        private const string FieldAnchorJson   = "AnchorJson";
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
                // string, NOT double — see the GUID note above. A double field
                // without SetSpec makes Finish() throw and the schema unusable.
                sb.AddSimpleField(FieldAnchorJson, typeof(string))
                    .SetDocumentation("QR cell as {\"x\":mm,\"y\":mm,\"size\":mm} from the sheet " +
                        "origin (bottom-left) — the same shape TB_QR_ANCHOR_JSON_TXT carries");
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

                string json = entity.Get<string>(FieldAnchorJson);
                // Parsed by the SAME Revit-free parser the family parameter uses, so
                // the stored form and the authored form cannot drift apart.
                var a = Drawing.SheetQrConfig.ParseAnchor(json, 0.0);
                if (a == null) return null;
                // A zero size is not an anchor — it is an entity written badly.
                // Say nothing rather than place a 0 mm code.
                if (a.SizeMm <= 0) return null;
                return new AnchorData
                {
                    XMm             = a.XMm,
                    YMm             = a.YMm,
                    SizeMm          = a.SizeMm,
                    StampedUtcTicks = entity.Get<long>(FieldStampedTicks),
                };
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
                entity.Set(FieldAnchorJson, Drawing.SheetQrConfig.FormatAnchor(xMm, yMm, sizeMm));
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
