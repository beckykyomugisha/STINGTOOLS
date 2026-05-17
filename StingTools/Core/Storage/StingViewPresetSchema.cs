// Pack 125 / Gap M — per-view preset memory.
//
// Smart Tag Placement learns offsets per category but stores nothing
// per-view. The control / placement centre wants "use the layout we
// set up on the L02 sheet" recall — that's per-View state.
//
// One Entity per View. Stores the active preset name, the last apply
// timestamp, and a JSON blob of the per-view candidate-offset overrides
// that override the project-level StingTagLearnedSchema for this view
// only.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingViewPresetSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("E1A7B2C4-1011-1242-8411-F6E5D4C3B2AE");

        private const string SchemaName        = "StingViewPresetSchema";
        private const string FieldPresetName   = "PresetName";
        private const string FieldAppliedTicks = "AppliedUtcTicks";
        private const string FieldOverridesJson = "OverridesJson";

        public class Preset
        {
            public string PresetName { get; set; } = "";
            public long   AppliedUtcTicks;
            public string OverridesJson { get; set; } = ""; // optional per-view offsets

            public DateTime? AppliedUtc =>
                AppliedUtcTicks > 0 ? (DateTime?)new DateTime(AppliedUtcTicks, DateTimeKind.Utc) : null;
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
                sb.AddSimpleField(FieldPresetName,    typeof(string))
                    .SetDocumentation("Active Smart Tag Placement preset for this view");
                sb.AddSimpleField(FieldAppliedTicks,  typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks of last apply");
                sb.AddSimpleField(FieldOverridesJson, typeof(string))
                    .SetDocumentation("Per-view offset overrides JSON — overrides StingTagLearnedSchema for this view only");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingViewPresetSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Preset Read(View view)
        {
            if (view == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = view.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Preset
                {
                    PresetName      = entity.Get<string>(FieldPresetName) ?? "",
                    AppliedUtcTicks = entity.Get<long>(FieldAppliedTicks),
                    OverridesJson   = entity.Get<string>(FieldOverridesJson) ?? "",
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingViewPresetSchema.Read {view?.Id}: {ex.Message}");
                return null;
            }
        }

        public static bool Write(View view, string presetName, string overridesJson = "")
        {
            if (view == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldPresetName,    presetName ?? "");
                entity.Set(FieldAppliedTicks,  DateTime.UtcNow.Ticks);
                entity.Set(FieldOverridesJson, overridesJson ?? "");
                view.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingViewPresetSchema.Write {view?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
