// ════════════════════════════════════════════════════════════════════════════
// McpDiscoveryTools — Tier 3 read-only capability discovery (Phase 2)
//
// These three meta-tools let an agent reach the whole command surface without a
// tool-per-command explosion. search/describe are pure catalogue reads (no Revit
// API — license-gated but no document needed). invoke_capability is PHASE-2-LIMITED:
// it permits only read-only tags or dry-runs, returns opens_ui for dialog/wizard
// tags without dispatching, and never fires a write command (that lands in Phase 3).
// ════════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Mcp
{
    internal static class McpDiscoveryTools
    {
        // ── search_capabilities ──────────────────────────────────────────────────

        public static McpCallResult SearchCapabilities(JObject args)
        {
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic.ToCallResult();

            string query = args["query"]?.Value<string>() ?? "";
            int limit = args["limit"]?.Value<int?>() ?? 15;
            if (limit <= 0) limit = 15;
            if (limit > 50) limit = 50;

            var results = McpCapabilityCatalogue.Search(query, limit);
            var rows = results.Select(c => (object)new Dictionary<string, object>
            {
                ["tag"]         = c.Tag,
                ["description"] = c.Description,
                ["category"]    = c.Category,
                ["triggers"]    = c.Triggers,
                ["readOnly"]    = c.ReadOnly,     // may be null when the command class did not resolve
                ["opensUI"]     = c.OpensUI,
            }).ToList();

            var data = new Dictionary<string, object>
            {
                ["query"]        = query,
                ["count"]        = results.Count,
                ["capabilities"] = rows,
            };
            return McpJobResult.Success(
                $"{results.Count} capability match(es) for \"{query}\".", data).ToCallResult();
        }

        // ── describe_capability ──────────────────────────────────────────────────

        public static McpCallResult DescribeCapability(JObject args)
        {
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic.ToCallResult();

            string tag = args["tag"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(tag))
                return McpJobResult.Error("bad_args", "Missing required argument: tag.").ToCallResult();

            var cap = McpCapabilityCatalogue.Get(tag);
            if (cap == null)
                return McpJobResult.Error("not_found",
                    $"Unknown capability tag '{tag}'. Use search_capabilities to discover valid tags.").ToCallResult();

            var data = new Dictionary<string, object>
            {
                ["tag"]           = cap.Tag,
                ["description"]   = cap.Description,
                ["category"]      = cap.Category,
                ["triggers"]      = cap.Triggers,
                ["readOnly"]      = cap.ReadOnly,
                ["opensUI"]       = cap.OpensUI,
                ["engineBacked"]  = cap.EngineBacked,
                ["inputContract"] = cap.InputContract,
            };
            return McpJobResult.Success($"Capability '{cap.Tag}' — {cap.Description}", data).ToCallResult();
        }

        // ── invoke_capability (Phase-2-limited) ──────────────────────────────────

        public static McpCallResult InvokeCapability(JObject args)
        {
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic.ToCallResult();

            string tag = args["tag"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(tag))
                return McpJobResult.Error("bad_args", "Missing required argument: tag.").ToCallResult();

            var cap = McpCapabilityCatalogue.Get(tag);
            if (cap == null)
                return McpJobResult.Error("not_found",
                    $"Unknown capability tag '{tag}'. Use search_capabilities to discover valid tags.").ToCallResult();

            bool dryRun = McpSafety.IsDryRun(args);

            // UI commands are never fired by Phase 2 — surface opens_ui without dispatching,
            // regardless of readOnly (a wizard may be a write command).
            if (cap.OpensUI)
                return McpJobResult.Error("opens_ui",
                    $"'{tag}' opens a dialog/wizard. Phase 2 does not fire UI commands; drive it from Revit, " +
                    "or wait for a dialog-free engine path (Phase 3).").ToCallResult();

            // Permit only read-only tags, or any tag in dry-run.
            bool permitted = cap.ReadOnly == true || dryRun;
            if (!permitted)
                return McpJobResult.Error("not_allowed",
                    "write invocation lands in Phase 3; call with dryRun:true or use a read tool").ToCallResult();

            if (dryRun)
            {
                var plan = new Dictionary<string, object>
                {
                    ["status"]        = "dry_run",
                    ["tag"]           = cap.Tag,
                    ["readOnly"]      = cap.ReadOnly,
                    ["opensUI"]       = cap.OpensUI,
                    ["engineBacked"]  = cap.EngineBacked,
                    ["inputContract"] = cap.InputContract,
                    ["note"]          = "Phase 2 dry-run: nothing was executed. Real write invocation lands in Phase 3.",
                };
                return McpJobResult.Success($"Dry run for '{cap.Tag}' — no execution.", plan).ToCallResult();
            }

            // Permitted read-only tag, non-UI. Structured read-back only exists once a
            // dialog-free engine path is wired (engineBacked). None are wired in Phase 2,
            // so we do not blind-dispatch an arbitrary command (it may open a modal report
            // and deadlock the waiting HTTP thread). Report honestly.
            if (!cap.EngineBacked)
            {
                var data = new Dictionary<string, object>
                {
                    ["status"] = "no_engine_path",
                    ["tag"]    = cap.Tag,
                    ["note"]   = "Read-only command has no dialog-free engine entry yet, so Phase 2 will not " +
                                 "dispatch it (avoids a possible modal deadlock). It becomes cleanly invokable " +
                                 "with structured read-back when engineBacked in Phase 3. For model reads now, " +
                                 "use the Tier 1 tools (query_elements, get_element, get_compliance, run_validator …).",
                };
                return McpJobResult.Success($"'{cap.Tag}' is read-only but not yet engine-backed — not dispatched.", data)
                    .ToCallResult();
            }

            // (Reserved) engine-backed read execution — no tags qualify in Phase 2.
            return McpJobResult.Error("no_engine_path",
                $"'{cap.Tag}' is flagged engineBacked but no engine handler is wired.").ToCallResult();
        }
    }
}
