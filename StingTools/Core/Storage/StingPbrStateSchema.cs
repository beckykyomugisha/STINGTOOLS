// StingTools — Material Hub · Phase 190 follow-up
//
// StingPbrStateSchema persists the per-material PBR pack state
// (active pack metadata + UV / slider / displacement toggle values)
// in Extensible Storage on the Material element so user tuning
// survives Revit restarts AND a Save-As to a fresh project —
// without requiring a separate shared-parameter binding pass.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;
using StingTools.Core.Materials;

namespace StingTools.Core.Storage
{
    public static class StingPbrStateSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("B7C6D5E4-F312-4922-9501-2A3B4C5D6E7F");

        private const string SchemaName  = "StingPbrStateSchema";

        // Pack identity
        private const string FieldPackId       = "PackId";
        private const string FieldDisplayName  = "DisplayName";
        private const string FieldProviderId   = "ProviderId";
        private const string FieldSourceUrl    = "SourceUrl";
        private const string FieldLicense      = "License";
        private const string FieldResolutionPx = "ResolutionPx";

        // Map paths (10 slots)
        private const string FieldMapBaseColor    = "MapBaseColor";
        private const string FieldMapNormal       = "MapNormal";
        private const string FieldMapRoughness    = "MapRoughness";
        private const string FieldMapMetalness    = "MapMetalness";
        private const string FieldMapAo           = "MapAo";
        private const string FieldMapBump         = "MapBump";
        private const string FieldMapDisplacement = "MapDisplacement";
        private const string FieldMapOpacity      = "MapOpacity";
        private const string FieldMapEmission     = "MapEmission";
        private const string FieldMapAnisotropy   = "MapAnisotropy";

        // UV + slider state
        private const string FieldRealWorldScaleXMm = "ScaleXMm";
        private const string FieldRealWorldScaleYMm = "ScaleYMm";
        private const string FieldUvOffsetX         = "UvOffsetX";
        private const string FieldUvOffsetY         = "UvOffsetY";
        private const string FieldUvRotationDeg     = "UvRotationDeg";
        private const string FieldBumpAmount        = "BumpAmount";
        private const string FieldNormalIntensity   = "NormalIntensity";
        private const string FieldDispEnabled       = "DispEnabled";
        private const string FieldDispMinMm         = "DispMinMm";
        private const string FieldDispMaxMm         = "DispMaxMm";
        private const string FieldDispScale         = "DispScale";
        private const string FieldEmissionLuminance = "EmissionLuminance";

        private const string FieldStampedUtcTicks   = "StampedUtcTicks";

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

                foreach (var name in new[]
                {
                    FieldPackId, FieldDisplayName, FieldProviderId, FieldSourceUrl, FieldLicense,
                    FieldMapBaseColor, FieldMapNormal, FieldMapRoughness, FieldMapMetalness, FieldMapAo,
                    FieldMapBump, FieldMapDisplacement, FieldMapOpacity, FieldMapEmission, FieldMapAnisotropy,
                })
                    sb.AddSimpleField(name, typeof(string));

                sb.AddSimpleField(FieldResolutionPx, typeof(int));

                foreach (var name in new[]
                {
                    FieldRealWorldScaleXMm, FieldRealWorldScaleYMm,
                    FieldUvOffsetX, FieldUvOffsetY, FieldUvRotationDeg,
                    FieldBumpAmount, FieldNormalIntensity,
                    FieldDispMinMm, FieldDispMaxMm, FieldDispScale, FieldEmissionLuminance,
                })
                    sb.AddSimpleField(name, typeof(double));

                sb.AddSimpleField(FieldDispEnabled, typeof(bool));
                sb.AddSimpleField(FieldStampedUtcTicks, typeof(long));

                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPbrStateSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static TexturePackManifest Read(Material mat)
        {
            if (mat == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var e = mat.GetEntity(schema);
                if (e == null || !e.IsValid()) return null;

                var m = new TexturePackManifest
                {
                    PackId       = e.Get<string>(FieldPackId)       ?? "",
                    DisplayName  = e.Get<string>(FieldDisplayName)  ?? "",
                    ProviderId   = e.Get<string>(FieldProviderId)   ?? "",
                    SourceUrl    = e.Get<string>(FieldSourceUrl),
                    License      = e.Get<string>(FieldLicense),
                    ResolutionPx = e.Get<int>(FieldResolutionPx),
                    Maps = new TexturePackMaps
                    {
                        BaseColor    = e.Get<string>(FieldMapBaseColor),
                        Normal       = e.Get<string>(FieldMapNormal),
                        Roughness    = e.Get<string>(FieldMapRoughness),
                        Metalness    = e.Get<string>(FieldMapMetalness),
                        Ao           = e.Get<string>(FieldMapAo),
                        Bump         = e.Get<string>(FieldMapBump),
                        Displacement = e.Get<string>(FieldMapDisplacement),
                        Opacity      = e.Get<string>(FieldMapOpacity),
                        Emission     = e.Get<string>(FieldMapEmission),
                        Anisotropy   = e.Get<string>(FieldMapAnisotropy),
                    },
                    Defaults = new TexturePackDefaults
                    {
                        RealWorldScaleXMm     = e.Get<double>(FieldRealWorldScaleXMm),
                        RealWorldScaleYMm     = e.Get<double>(FieldRealWorldScaleYMm),
                        UvOffsetX             = e.Get<double>(FieldUvOffsetX),
                        UvOffsetY             = e.Get<double>(FieldUvOffsetY),
                        UvRotationDeg         = e.Get<double>(FieldUvRotationDeg),
                        BumpAmount            = e.Get<double>(FieldBumpAmount),
                        NormalIntensity       = e.Get<double>(FieldNormalIntensity),
                        DisplacementEnabled   = e.Get<bool>(FieldDispEnabled),
                        DisplacementMinMm     = e.Get<double>(FieldDispMinMm),
                        DisplacementMaxMm     = e.Get<double>(FieldDispMaxMm),
                        DisplacementScale     = e.Get<double>(FieldDispScale),
                        EmissionLuminanceCdM2 = e.Get<double>(FieldEmissionLuminance),
                    },
                };
                return string.IsNullOrEmpty(m.PackId) ? null : m;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("PbrStateRead", $"StingPbrStateSchema.Read {mat?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Write the per-material PBR state. Caller MUST hold an open Revit transaction.</summary>
        public static bool Write(Material mat, TexturePackManifest m)
        {
            if (mat == null || m == null) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var e = new Entity(schema);
                e.Set(FieldPackId,       m.PackId       ?? "");
                e.Set(FieldDisplayName,  m.DisplayName  ?? "");
                e.Set(FieldProviderId,   m.ProviderId   ?? "");
                e.Set(FieldSourceUrl,    m.SourceUrl    ?? "");
                e.Set(FieldLicense,      m.License      ?? "");
                e.Set(FieldResolutionPx, m.ResolutionPx);

                var maps = m.Maps ?? new TexturePackMaps();
                e.Set(FieldMapBaseColor,    maps.BaseColor    ?? "");
                e.Set(FieldMapNormal,       maps.Normal       ?? "");
                e.Set(FieldMapRoughness,    maps.Roughness    ?? "");
                e.Set(FieldMapMetalness,    maps.Metalness    ?? "");
                e.Set(FieldMapAo,           maps.Ao           ?? "");
                e.Set(FieldMapBump,         maps.Bump         ?? "");
                e.Set(FieldMapDisplacement, maps.Displacement ?? "");
                e.Set(FieldMapOpacity,      maps.Opacity      ?? "");
                e.Set(FieldMapEmission,     maps.Emission     ?? "");
                e.Set(FieldMapAnisotropy,   maps.Anisotropy   ?? "");

                var d = m.Defaults ?? new TexturePackDefaults();
                e.Set(FieldRealWorldScaleXMm,     d.RealWorldScaleXMm);
                e.Set(FieldRealWorldScaleYMm,     d.RealWorldScaleYMm);
                e.Set(FieldUvOffsetX,             d.UvOffsetX);
                e.Set(FieldUvOffsetY,             d.UvOffsetY);
                e.Set(FieldUvRotationDeg,         d.UvRotationDeg);
                e.Set(FieldBumpAmount,            d.BumpAmount);
                e.Set(FieldNormalIntensity,       d.NormalIntensity);
                e.Set(FieldDispEnabled,           d.DisplacementEnabled);
                e.Set(FieldDispMinMm,             d.DisplacementMinMm);
                e.Set(FieldDispMaxMm,             d.DisplacementMaxMm);
                e.Set(FieldDispScale,             d.DisplacementScale);
                e.Set(FieldEmissionLuminance,     d.EmissionLuminanceCdM2);

                e.Set(FieldStampedUtcTicks, DateTime.UtcNow.Ticks);
                mat.SetEntity(e);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPbrStateSchema.Write {mat?.Id}: {ex.Message}");
                return false;
            }
        }
    }
}
