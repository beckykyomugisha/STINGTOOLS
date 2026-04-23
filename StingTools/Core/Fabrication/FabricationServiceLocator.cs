// StingTools v4 MVP — FabricationServiceLocator.
//
// Discovers whether the active document has a Revit Fabrication
// Configuration loaded (the ITM-content-driven LOD400 pipeline
// historically delivered via Fabrication CADmep / ESTmep / CAMduct)
// and exposes service-button lookups for the three auto-drop engines
// to opt into FabricationPart.Create when real fab content is
// available. Falls back gracefully when no configuration is present —
// the design-intent Pipe.Create / Duct.Create path remains the
// default so v4 still works on projects without ITM content.
//
// Revit API surface used (Revit 2025):
//   FabricationConfiguration.GetFabricationConfiguration(doc)
//   FabricationConfiguration.GetAllLoadedServices()           → IList<FabricationService>
//   FabricationService.Name
//   FabricationService.ButtonCount
//   FabricationService.GetButtons(groupIndex)                → FabricationServiceButton[]
//   FabricationServiceButton.Name / .Category / .PartCount
//
// The locator caches results per-document so repeated calls during a
// single Execute don't re-run the collector.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Fabrication;

namespace StingTools.Core.Fabrication
{
    /// <summary>
    /// One service-button reference that a drop engine can hand to
    /// FabricationPart.Create.
    /// </summary>
    public class FabricationButtonRef
    {
        public string ServiceName  { get; set; } = "";
        public int    ServiceIndex { get; set; }
        public int    GroupIndex   { get; set; }
        public int    ButtonIndex  { get; set; }
        public string ButtonName   { get; set; } = "";
        public int    PartCount    { get; set; }
        public BuiltInCategory Category { get; set; }

        public override string ToString() =>
            $"{ServiceName} › [{GroupIndex}.{ButtonIndex}] {ButtonName} ({PartCount} parts)";
    }

    public static class FabricationServiceLocator
    {
        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, FabricationConfiguration> _configByDoc
            = new Dictionary<string, FabricationConfiguration>();

        /// <summary>
        /// Returns the active document's FabricationConfiguration or
        /// null when no Fabrication content is loaded. Result is
        /// cached per (doc.PathName, doc.CreationGUID) for the
        /// lifetime of the plugin.
        /// </summary>
        public static FabricationConfiguration GetConfig(Document doc)
        {
            if (doc == null) return null;
            string key = MakeCacheKey(doc);
            lock (_cacheLock)
            {
                if (_configByDoc.TryGetValue(key, out var cached) && cached != null)
                    return cached;
            }
            try
            {
                var cfg = FabricationConfiguration.GetFabricationConfiguration(doc);
                lock (_cacheLock) { _configByDoc[key] = cfg; }
                return cfg;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationServiceLocator: GetFabricationConfiguration failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// True when FabricationConfiguration is present and at least
        /// one service is loaded. Lightweight entry point for drop
        /// engines that want to branch: "if HasFabContent(doc) use
        /// FabricationPart.Create else Pipe.Create".
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
                StingLog.Warn($"FabricationServiceLocator: GetAllLoadedServices failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find the first button matching a case-insensitive substring
        /// of service name + button name that creates parts in the
        /// given category. Nullable result — caller falls back to
        /// design-intent APIs when the button is missing.
        /// </summary>
        public static FabricationButtonRef FindButton(
            Document doc,
            BuiltInCategory desiredCategory,
            string serviceNameHint = null,
            string buttonNameHint  = null)
        {
            var cfg = GetConfig(doc);
            if (cfg == null) return null;

            IList<FabricationService> services;
            try { services = cfg.GetAllLoadedServices(); }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationServiceLocator.FindButton: service enumeration failed: {ex.Message}");
                return null;
            }
            if (services == null || services.Count == 0) return null;

            FabricationButtonRef fallbackAny = null;
            for (int si = 0; si < services.Count; si++)
            {
                var svc = services[si];
                if (svc == null) continue;
                if (!string.IsNullOrEmpty(serviceNameHint) &&
                    !ContainsInsensitive(svc.Name, serviceNameHint))
                    continue;

                int groups;
                try { groups = svc.GroupCount; } catch { groups = 0; }
                for (int gi = 0; gi < groups; gi++)
                {
                    FabricationServiceButton[] buttons;
                    try { buttons = svc.GetButtons(gi); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"FabricationServiceLocator: GetButtons({si},{gi}) failed: {ex.Message}");
                        continue;
                    }
                    if (buttons == null) continue;

                    for (int bi = 0; bi < buttons.Length; bi++)
                    {
                        var b = buttons[bi];
                        if (b == null) continue;

                        bool categoryMatch = false;
                        try { categoryMatch = (b.Category == desiredCategory); } catch { }
                        if (!categoryMatch) continue;

                        bool nameMatch = string.IsNullOrEmpty(buttonNameHint)
                                         || ContainsInsensitive(b.Name, buttonNameHint);

                        var found = new FabricationButtonRef
                        {
                            ServiceName  = svc.Name,
                            ServiceIndex = si,
                            GroupIndex   = gi,
                            ButtonIndex  = bi,
                            ButtonName   = b.Name,
                            PartCount    = SafePartCount(b),
                            Category     = desiredCategory,
                        };

                        if (nameMatch) return found;
                        if (fallbackAny == null) fallbackAny = found;
                    }
                }
            }
            return fallbackAny;
        }

        private static int SafePartCount(FabricationServiceButton b)
        {
            try { return b.PartCount; } catch { return 0; }
        }

        private static bool ContainsInsensitive(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string MakeCacheKey(Document doc)
        {
            try { return $"{doc.PathName}|{doc.CreationGUID}"; }
            catch { return doc.PathName ?? Guid.NewGuid().ToString(); }
        }

        /// <summary>
        /// Clear the per-document cache. Call from
        /// StingToolsApp.OnDocumentClosed so stale configs don't
        /// accumulate across session long usage.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock) { _configByDoc.Clear(); }
        }
    }
}
