// ══════════════════════════════════════════════════════════════════════════
//  RateFeedsConfig.cs — Phase 2B. Inline live-rate feed configuration.
//
//  Persists the BCIS / Planscape live-rate feed settings to
//  <project>/_BIM_COORD/rate_feeds.json (same pattern as boq_links.json).
//  The BCIS API key lives ONLY in this project file — it is never committed
//  to the repo. RateProviderRegistry.Build reads this config to construct the
//  BcisHttpRateProvider / PlanscapeRateProvider with real settings instead of
//  the inert defaults.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ.Rates
{
    public class RateFeedsConfig
    {
        public string SchemaVersion = "1.0";

        // ── BCIS Online (or any HTTP price-book behind the same shape) ──────
        public bool BcisEnabled = false;
        public string BcisBaseUrl = "https://service.bcis.co.uk/api";
        public string BcisApiKey = "";          // project-file only — never committed
        public int BcisTtlMinutes = 1440;       // 24 h default

        // ── Planscape server feed (reuses PlanscapeServerClient.Instance auth) ─
        public bool PlanscapeEnabled = false;

        public DateTime LastSaved = DateTime.UtcNow;
        public string LastSavedBy;
    }

    internal static class RateFeedsStore
    {
        private const string FileName = "rate_feeds.json";

        public static RateFeedsConfig Load(Document doc)
        {
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new RateFeedsConfig();
                var cfg = JsonConvert.DeserializeObject<RateFeedsConfig>(File.ReadAllText(path));
                return cfg ?? new RateFeedsConfig();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RateFeedsStore.Load: {ex.Message}");
                return new RateFeedsConfig();
            }
        }

        public static bool Save(Document doc, RateFeedsConfig cfg)
        {
            if (cfg == null) return false;
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path)) { StingLog.Warn("RateFeedsStore.Save: unsaved document, no path."); return false; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                cfg.LastSaved = DateTime.UtcNow;
                cfg.LastSavedBy = Environment.UserName ?? "";
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                StingLog.Info($"RateFeedsStore: saved (BCIS={cfg.BcisEnabled}, Planscape={cfg.PlanscapeEnabled}).");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RateFeedsStore.Save: {ex.Message}");
                return false;
            }
        }

        private static string ResolvePath(Document doc)
        {
            string parent = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(parent)) return null;   // unsaved doc — no persistence
            return Path.Combine(parent, "_BIM_COORD", FileName);
        }
    }
}
