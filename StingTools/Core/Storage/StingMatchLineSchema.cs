// StingTools — match-line key storage (DTW-56).
//
// Holds the three keys a match-line detail curve carries — pair key, paired
// sheet ref, direction — in Extensible Storage.
//
// Why not shared parameters: in Revit 2025 the 'Lines' category reports
// AllowsBoundParameters = False, so STING_MATCH_LINE_GUID_TXT / _REF_TXT /
// _DIR_TXT can never be bound to a detail line, and the engine could not find
// its own lines (duplicates on re-run, Validate blind). The parameters stay in
// MR_PARAMETERS.txt and are still read as a fallback — the decision rule is
// MatchLineKeyRules.Resolve in Core/Drawing/MatchLineKeys.cs (Revit-free, tested).
//
// Strings only, so no field needs a unit spec (a double field without SetSpec
// makes SchemaBuilder.Finish() throw — see StingQrAnchorSchema).

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Core.Storage
{
    public static class StingMatchLineSchema
    {
        public static readonly Guid SchemaGuid = new Guid("E1A7B2C4-1011-1260-8411-F6E5D4C3B2E0");

        private const string SchemaName     = "StingMatchLine";
        private const string FieldPairGuid  = "PairGuid";
        private const string FieldRef       = "MatchRef";
        private const string FieldDirection = "Direction";

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
                sb.AddSimpleField(FieldPairGuid, typeof(string))
                    .SetDocumentation("Match-line pair key (was STING_MATCH_LINE_GUID_TXT): scopePair:viewA:viewB[:segN]");
                sb.AddSimpleField(FieldRef, typeof(string))
                    .SetDocumentation("The paired sheet's reference (was STING_MATCH_REF_TXT)");
                sb.AddSimpleField(FieldDirection, typeof(string))
                    .SetDocumentation("vertical / horizontal / dogleg (was STING_MATCH_DIR_TXT)");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingMatchLineSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        /// <summary>True when the schema is registered in this session — i.e. some
        /// document may hold match-line entities. False means none can exist.</summary>
        public static bool IsRegistered()
        {
            try { return Schema.Lookup(SchemaGuid) != null; }
            catch (Exception ex) { StingLog.Warn($"StingMatchLineSchema.IsRegistered: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Store the keys on <paramref name="curve"/>. Needs an open transaction. All three
        /// fields are written together (null → ""); returns false with a reason when Revit
        /// refuses, so the caller can report it rather than leave an unfindable curve.
        /// </summary>
        public static bool Write(Element curve, string pairGuid, string matchRef, string direction, out string error)
        {
            error = null;
            if (curve == null) { error = "no element"; return false; }
            try
            {
                var schema = GetOrCreate();
                if (schema == null) { error = "match-line schema could not be created (see log)"; return false; }
                var e = new Entity(schema);
                e.Set(FieldPairGuid, pairGuid ?? "");
                e.Set(FieldRef, matchRef ?? "");
                e.Set(FieldDirection, direction ?? "");
                curve.SetEntity(e);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                StingLog.Warn($"StingMatchLineSchema.Write on {curve.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>The keys stored on <paramref name="el"/>, or null when it carries no
        /// entity (or the schema has never been registered).</summary>
        public static MatchLineKeys Read(Element el)
        {
            if (el == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var e = el.GetEntity(schema);
                if (e == null || !e.IsValid()) return null;
                return new MatchLineKeys(e.Get<string>(FieldPairGuid), e.Get<string>(FieldRef),
                                         e.Get<string>(FieldDirection), MatchLineKeySource.ExtensibleStorage);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingMatchLineSchema.Read on {el.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>A collector filter passing elements that carry a match-line entity,
        /// or null when the schema is not registered (then nothing can carry one).</summary>
        public static ElementFilter HasEntityFilter()
        {
            try { return IsRegistered() ? new ExtensibleStorageFilter(SchemaGuid) : null; }
            catch (Exception ex) { StingLog.Warn($"StingMatchLineSchema.HasEntityFilter: {ex.Message}"); return null; }
        }
    }
}
