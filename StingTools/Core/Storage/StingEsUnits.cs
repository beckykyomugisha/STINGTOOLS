// StingTools — Extensible Storage units for floating-point fields.
//
// Since Revit 2022 a double / float / XYZ / UV field in an ES schema must be
// given a spec (FieldBuilder.SetSpec), and every Entity.Get / Entity.Set on it
// must pass a unit. Without the spec, SchemaBuilder.Finish() throws
// "Units are required for field <name>", the schema is never created, and every
// write that depends on it silently stores nothing — the plugin's GetOrCreate
// methods catch the exception, log a warning and return null.
//
// STING's floating-point ES fields hold values already expressed in the unit
// their name states (MarginMm, CompliancePct, RateGbp, ...). They are therefore
// declared as unitless Number and read/written with General, which stores the
// value as given: no hidden mm->ft conversion on either side.
//
// Schemas fixed in place (2026-10-01): StingViewCropSchema, StingComplianceBaselineSchema,
// StingCostRateOverrideSchema v1 + v2, StingFohlioSnapshotSchema v2, StingPbrStateSchema.
// Their GUIDs and field names are unchanged. That is safe only because every one
// of them declared a double without a spec, so Finish() always threw and no
// document can hold a registered copy with a conflicting definition. A schema
// that has ever been built successfully must NOT be redefined under its GUID —
// mint a new GUID instead (see StingCostRateOverrideSchema's v1/v2 split).
//
// StingTools.Tags.Tests/ExtensibleStorageUnitsTests scans the plugin source and
// fails on a floating-point field without SetSpec, or a Get/Set on one without
// a unit argument.

using Autodesk.Revit.DB;

namespace StingTools.Core.Storage
{
    public static class StingEsUnits
    {
        /// <summary>Spec for every STING floating-point ES field: unitless Number.</summary>
        public static ForgeTypeId Spec => SpecTypeId.Number;

        /// <summary>Unit for Entity.Get/Set on those fields: General (value stored as given).</summary>
        public static ForgeTypeId Unit => UnitTypeId.General;
    }
}
