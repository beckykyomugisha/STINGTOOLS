// Pack 123 / Gap D — per-element validator-finding suppression.
//
// The eight-validator suite has no per-element opt-out. A coordinator
// who marks an element as "intentionally undefined" (say a wall whose
// acoustic Rw is deliberately not specified) gets hammered with the
// same SPEC.ACOU.RW.MISSING finding on every Run All scan. They either
// disable the whole validator (loses signal) or live with the noise.
//
// This schema lets users suppress specific finding codes per element.
// The result panel filters out suppressed codes and shows
// "12 findings, 3 suppressed" so the suppression remains visible.
//
// Storage: semicolon-joined string of codes (cheap, transparent to
// schedules / IFC export). Audit fields capture who suppressed and when.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;
using StingTools.Core.Validation;

namespace StingTools.Core.Storage
{
    public static class StingValidatorSuppressionSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-123C-8411-F6E5D4C3B2A8");

        private const string SchemaName        = "StingValidatorSuppressionSchema";
        private const string FieldIgnoredCodes = "IgnoredCodes";
        private const string FieldIgnoredBy    = "IgnoredByUser";
        private const string FieldIgnoredAt    = "IgnoredAtUtcTicks";
        private const string FieldReason       = "Reason";

        public class Suppression
        {
            public List<string> IgnoredCodes { get; set; } = new List<string>();
            public string IgnoredByUser { get; set; } = "";
            public long   IgnoredAtUtcTicks;
            public string Reason { get; set; } = "";

            public bool IsCodeIgnored(string code) =>
                !string.IsNullOrEmpty(code) &&
                IgnoredCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
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
                sb.AddSimpleField(FieldIgnoredCodes, typeof(string))
                    .SetDocumentation("Semicolon-joined validator codes to suppress (e.g. SPEC.ACOU.RW.MISSING;CLR.NEIGHBOUR)");
                sb.AddSimpleField(FieldIgnoredBy,    typeof(string))
                    .SetDocumentation("Environment.UserName at the time of suppression");
                sb.AddSimpleField(FieldIgnoredAt,    typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks at the time of suppression");
                sb.AddSimpleField(FieldReason,       typeof(string))
                    .SetDocumentation("Free-text justification surfaced in the validator panel");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingValidatorSuppressionSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Suppression Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = el.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                string codes = entity.Get<string>(FieldIgnoredCodes) ?? "";
                return new Suppression
                {
                    IgnoredCodes = string.IsNullOrEmpty(codes)
                        ? new List<string>()
                        : codes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim()).ToList(),
                    IgnoredByUser     = entity.Get<string>(FieldIgnoredBy) ?? "",
                    IgnoredAtUtcTicks = entity.Get<long>(FieldIgnoredAt),
                    Reason            = entity.Get<string>(FieldReason) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingValidatorSuppressionSchema.Read {el?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(Element el, Suppression data)
        {
            if (el == null || data == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldIgnoredCodes, string.Join(";", data.IgnoredCodes ?? new List<string>()));
                entity.Set(FieldIgnoredBy,    data.IgnoredByUser ?? Environment.UserName ?? "");
                entity.Set(FieldIgnoredAt,    data.IgnoredAtUtcTicks > 0 ? data.IgnoredAtUtcTicks : DateTime.UtcNow.Ticks);
                entity.Set(FieldReason,       data.Reason ?? "");
                el.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingValidatorSuppressionSchema.Write {el?.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Bulk filter helper used by RunAllValidatorsCommand. Returns
        /// (kept, suppressedCount). Cheap — only reads the entity for
        /// elements that actually have a finding.
        /// </summary>
        public static (List<Validation.ValidationResult> kept, int suppressed) Filter(
            Document doc, List<Validation.ValidationResult> findings)
        {
            int suppressed = 0;
            if (findings == null || findings.Count == 0) return (findings, 0);
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return (findings, 0);

            var kept = new List<Validation.ValidationResult>(findings.Count);
            var cache = new Dictionary<long, Suppression>();
            foreach (var r in findings)
            {
                if (r?.ElementId == null || r.ElementId == ElementId.InvalidElementId)
                {
                    kept.Add(r);
                    continue;
                }
                long key = r.ElementId.Value;
                if (!cache.TryGetValue(key, out var supp))
                {
                    var el = doc.GetElement(r.ElementId);
                    supp = Read(el);
                    cache[key] = supp;
                }
                if (supp != null && supp.IsCodeIgnored(r.Code)) { suppressed++; continue; }
                kept.Add(r);
            }
            return (kept, suppressed);
        }
    }
}
