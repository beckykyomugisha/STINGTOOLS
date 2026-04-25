// Gap 2 / Phase 121 — tag position + token-presence ES schema.
//
// Replaces STING_TAG_POS (int 1..4: Above/Right/Below/Left) plus a new
// TokenPresenceMask bit-field that lets the compliance scanner cache the
// per-element "which tokens are populated" state without recomputing
// from eight LookupParameter calls on every scan.
//
// Bit layout for TokenPresenceMask:
//   bit 0  DISC, bit 1 LOC, bit 2 ZONE, bit 3 LVL,
//   bit 4  SYS,  bit 5 FUNC, bit 6 PROD, bit 7 SEQ
// All bits set (0xFF) = fully tagged.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace StingTools.Core.Storage
{
    public static class StingPositionSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1237-8411-F6E5D4C3B2A3");

        private const string SchemaName = "StingPositionSchema";
        private const string FieldTagPos      = "TagPos";
        private const string FieldTokenMask   = "TokenPresenceMask";

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
                sb.AddSimpleField(FieldTagPos,    typeof(int))
                    .SetDocumentation("1=Above, 2=Right, 3=Below, 4=Left — preferred tag position");
                sb.AddSimpleField(FieldTokenMask, typeof(int))
                    .SetDocumentation("Bitmask: bit N = token N populated. DISC=0, LOC=1, ZONE=2, LVL=3, SYS=4, FUNC=5, PROD=6, SEQ=7");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPositionSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public class PositionData
        {
            public int TagPos;        // 1..4
            public int TokenPresence; // 0..0xFF

            public bool IsEmpty => TagPos == 0 && TokenPresence == 0;
            public bool IsFullyTagged => TokenPresence == 0xFF;
        }

        public static PositionData Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new PositionData
                {
                    TagPos        = entity.Get<int>(FieldTagPos),
                    TokenPresence = entity.Get<int>(FieldTokenMask),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPositionSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, PositionData data)
        {
            if (el == null || data == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldTagPos,    data.TagPos);
                entity.Set(FieldTokenMask, data.TokenPresence);
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPositionSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
