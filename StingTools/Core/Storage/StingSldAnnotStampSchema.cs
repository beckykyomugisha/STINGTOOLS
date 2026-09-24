// StingSldAnnotStampSchema.cs — ELEC-10.
//
// Marks a TextNote as an SLD inline annotation and records what it annotates.
// The SLD annotate commands used to "stamp" notes by writing
// STING_SLD_ANNOT_KIND_TXT / _ELEM_ID_TXT / _FORMAT_TXT — shared parameters
// that exist in no data file, so the write fell into an empty catch and
// Update / Toggle / Clear / Audit found zero annotations every time.
// Extensible Storage needs no binding and cannot be missing.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingSldAnnotStampSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-124A-8411-F6E5D4C3B2D1");

        private const string SchemaName  = "StingSldAnnotStampSchema";
        private const string FieldKind   = "Kind";
        private const string FieldAnchor = "AnchorUniqueId";
        private const string FieldFormat = "Format";

        public sealed class StampData
        {
            public string Kind = "";
            /// <summary>UniqueId of the element the note annotates (the SLD symbol, or
            /// the model element when annotating a model view).</summary>
            public string AnchorUniqueId = "";
            public string Format = "";
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
                    .SetDocumentation("Annotation kind (Voltage / Current / Fault / Cable / Phase / Load / Reference / Impedance / Diversity / All).");
                sb.AddSimpleField(FieldAnchor, typeof(string))
                    .SetDocumentation("UniqueId of the annotated element.");
                sb.AddSimpleField(FieldFormat, typeof(string))
                    .SetDocumentation("Format the text was built in (Compact / Full / Reference).");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingSldAnnotStampSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        /// <summary>Stamp a note. Caller must be inside a Transaction.</summary>
        public static bool Write(Element el, StampData d)
        {
            try
            {
                if (el == null || d == null) return false;
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldKind,   d.Kind ?? "");
                entity.Set(FieldAnchor, d.AnchorUniqueId ?? "");
                entity.Set(FieldFormat, d.Format ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"StingSldAnnotStampSchema.Write {el?.Id}: {ex.Message}"); return false; }
        }

        /// <summary>The stamp, or null when the element is not an SLD annotation.</summary>
        public static StampData Read(Element el)
        {
            try
            {
                if (el == null) return null;
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;   // never stamped in this session's documents
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                var d = new StampData
                {
                    Kind           = entity.Get<string>(FieldKind) ?? "",
                    AnchorUniqueId = entity.Get<string>(FieldAnchor) ?? "",
                    Format         = entity.Get<string>(FieldFormat) ?? "",
                };
                return string.IsNullOrEmpty(d.AnchorUniqueId) ? null : d;
            }
            catch (Exception ex) { StingLog.Warn($"StingSldAnnotStampSchema.Read {el?.Id}: {ex.Message}"); return null; }
        }
    }
}
