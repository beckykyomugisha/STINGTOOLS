using StingTools.Core;
// StingTools v4 MVP — FabricationServiceLocator (minimal safe surface).
//
// Phase B originally tried to walk FabricationService → palette → button
// and pass button indices to FabricationPart.Create. The Revit API for
// that walk differs across 2024/2025/2026 (property names and method
// signatures were unverified in the sandbox), so Phase B.x defers the
// full integration until the SDK-linked build bench can confirm them.
//
// This locator keeps the two API calls we ARE certain about:
//   FabricationConfiguration.GetFabricationConfiguration(doc)
//   FabricationConfiguration.GetAllLoadedServices()
// and exposes HasFabContent(doc) so routing engines can branch on
// "project has ITM content" without touching the unverified walk.
//
// Full FabricationPart.Create integration — and therefore MAJ
// export — is tracked as a Phase B.2 deferred item.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Fabrication;

namespace StingTools.Core.Fabrication
{
    public static class FabricationServiceLocator
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, FabricationConfiguration> _configByDoc
            = new Dictionary<string, FabricationConfiguration>();

        /// <summary>
        /// Return the active document's FabricationConfiguration or
        /// null when none is loaded. Cached per (PathName, CreationGUID).
        /// </summary>
        public static FabricationConfiguration GetConfig(Document doc)
        {
            if (doc == null) return null;
            string key = MakeCacheKey(doc);
            lock (_lock)
            {
                if (_configByDoc.TryGetValue(key, out var cached) && cached != null)
                    return cached;
            }
            try
            {
                var cfg = FabricationConfiguration.GetFabricationConfiguration(doc);
                lock (_lock) { _configByDoc[key] = cfg; }
                return cfg;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationServiceLocator.GetConfig: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// True when a FabricationConfiguration is loaded AND at least
        /// one service is available. Routing engines branch on this
        /// before attempting ITM-content creation.
        /// </summary>
        public static bool HasFabContent(Document doc)
        {
            var cfg = GetConfig(doc);
            if (cfg == null) return false;
            try
            {
                var services = cfg.GetAllLoadedServices();
                return services != null && services.Count > 0;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationServiceLocator.HasFabContent: {ex.Message}");
                return false;
            }
        }

        public static void InvalidateCache()
        {
            lock (_lock) { _configByDoc.Clear(); }
        }

        private static string MakeCacheKey(Document doc)
        {
            try { return $"{doc.PathName}|{doc.CreationGUID}"; }
            catch { return doc.PathName ?? Guid.NewGuid().ToString(); }
        }
    }
}
