// StingTools — annotation provenance storage.
//
// Stamps an annotation STING created (dimension, text note) with WHO made it
// and WHAT it annotates, so a re-run identifies its own work exactly instead
// of inferring it from references, text shape and position. The why, and the
// key format, are in Core/Drawing/AnnotationProvenance.cs.
//
// Extensible Storage rather than a shared parameter: Revit will not bind a
// project parameter to Dimensions or Text Notes in any useful way, and this is
// internal plugin state — exactly what the ES layer is for (CLAUDE.md).
// Strings only, so no field needs a unit spec (see StingQrAnchorSchema's note
// on why a double field without SetSpec makes Finish() throw).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingAnnotationProvenanceSchema
    {
        public static readonly Guid SchemaGuid = new Guid("E1A7B2C4-1011-1252-8411-F6E5D4C3B2D3");

        private const string SchemaName    = "StingAnnotationProvenance";
        private const string FieldProducer = "Producer";
        private const string FieldKey      = "Key";

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
                sb.AddSimpleField(FieldProducer, typeof(string))
                    .SetDocumentation("Which STING pass created this annotation (AnnotationProvenance.*)");
                sb.AddSimpleField(FieldKey, typeof(string))
                    .SetDocumentation("What it annotates: host UniqueId, optionally '|part'");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingAnnotationProvenanceSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Stamp <paramref name="annotation"/>. Needs an open transaction. Returns
        /// false (and logs) when it cannot — the annotation stays, and the next run
        /// falls back to the old heuristic for it, which is the pre-stamp behaviour.
        /// </summary>
        public static bool Stamp(Element annotation, string producer, string key)
        {
            if (annotation == null || string.IsNullOrEmpty(producer) || string.IsNullOrEmpty(key)) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var e = new Entity(schema);
                e.Set(FieldProducer, producer);
                e.Set(FieldKey, key);
                annotation.SetEntity(e);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Annotation provenance stamp on {annotation.Id} ({producer}): {ex.Message}");
                return false;
            }
        }

        /// <summary>(producer, key) stamped on <paramref name="el"/>, or null.</summary>
        public static (string Producer, string Key)? Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var e = el.GetEntity(schema);
                if (e == null || !e.IsValid()) return null;
                var p = e.Get<string>(FieldProducer);
                var k = e.Get<string>(FieldKey);
                if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(k)) return null;
                return (p, k);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Annotation provenance read on {el.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Every annotation of <paramref name="annotationClass"/> in
        /// <paramref name="view"/> stamped by <paramref name="producer"/>, by key.
        /// Empty when the schema has never been registered in this document —
        /// which is simply "nothing stamped yet", not an error.
        /// </summary>
        public static Dictionary<string, List<Element>> Index(Document doc, View view, Type annotationClass, string producer)
        {
            var map = new Dictionary<string, List<Element>>(StringComparer.Ordinal);
            if (doc == null || view == null || Schema.Lookup(SchemaGuid) == null) return map;
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id).OfClass(annotationClass).WhereElementIsNotElementType())
                {
                    var s = Read(el);
                    if (s == null || !string.Equals(s.Value.Producer, producer, StringComparison.Ordinal)) continue;
                    if (!map.TryGetValue(s.Value.Key, out var list)) map[s.Value.Key] = list = new List<Element>();
                    list.Add(el);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Annotation provenance index ({producer}) in '{view.Name}': {ex.Message}");
            }
            return map;
        }
    }
}
