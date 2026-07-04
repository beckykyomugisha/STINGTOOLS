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
                ["tag"]          = c.Tag,
                ["typeName"]     = c.TypeName,
                ["description"]  = c.Description,
                ["category"]     = c.Category,
                ["triggers"]     = c.Triggers,
                ["readOnly"]     = c.ReadOnly,     // may be null when the command class did not resolve
                ["opensUI"]      = c.OpensUI,
                ["dispatchable"] = c.Dispatchable, // true → invoke_capability can actually run it
                ["engineBacked"] = c.EngineBacked, // true → a guarded dialog-free write path exists
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
                ["typeName"]      = cap.TypeName,
                ["typeFullName"]  = cap.TypeFullName,
                ["description"]   = cap.Description,
                ["synthesized"]   = cap.Synthesized,
                ["category"]      = cap.Category,
                ["intent"]        = cap.Intent,
                ["triggers"]      = cap.Triggers,
                ["readOnly"]      = cap.ReadOnly,
                ["opensUI"]       = cap.OpensUI,
                ["engineBacked"]  = cap.EngineBacked,
                ["dispatchable"]  = cap.Dispatchable,
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

            // UI commands are never fired — surface opens_ui without dispatching,
            // regardless of readOnly (a wizard may be a write command).
            if (cap.OpensUI)
                return McpJobResult.Error("opens_ui",
                    $"'{tag}' opens a dialog/wizard. MCP does not fire UI commands; drive it from Revit, " +
                    "or wait for a dialog-free engine path.").ToCallResult();

            // Discover-only: the capability is known (searchable/describable) but has no
            // dispatch tag exposed to MCP, so there is no clean run path. Do NOT attempt to
            // blind-instantiate a reflected command — Execute needs ExternalCommandData and
            // may open modal UI. Report honestly instead.
            if (!cap.Dispatchable)
                return McpJobResult.Error("discoverable_only",
                    $"'{tag}' is a known command but has no dispatch/engine path exposed to MCP yet — " +
                    "it can be described but not run. Drive it from Revit, or it becomes runnable when " +
                    "engine-backed.").ToCallResult();

            // Engine-backed → route through the SINGLE engine registry, which applies the
            // full guardrail set (dry-run plan, confirm gate, TransactionGroup rollback,
            // sync/async by scope). invoke_capability writes ALSO respect mcp_tool_allowlist.
            if (cap.EngineBacked)
            {
                var allow = McpSafety.CheckAllowlist(tag);
                if (allow != null) return allow.ToCallResult();

                JObject inner = args["args"] as JObject;
                var callArgs = inner != null ? (JObject)inner.DeepClone() : new JObject();
                if (args["dryRun"] != null) callArgs["dryRun"] = args["dryRun"];
                if (args["confirm"] != null) callArgs["confirm"] = args["confirm"];
                if (args["scope"] != null && callArgs["scope"] == null) callArgs["scope"] = args["scope"];

                return McpEngineRegistry.DispatchWrite(tag, callArgs).ToCallResult();
            }

            // Not engine-backed. A dry-run still returns a generic, non-executing plan note.
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
                    ["note"]          = "Dry-run: nothing executed. This tag has no engine handler, so a real " +
                                        "invocation is not available (see status no_engine_path / not_allowed).",
                };
                return McpJobResult.Success($"Dry run for '{cap.Tag}' — no execution (not engine-backed).", plan).ToCallResult();
            }

            // Read-only but not engine-backed: do not blind-dispatch (it may open a modal
            // report and deadlock the API thread). Honest status; use the Tier 1 read tools.
            if (cap.ReadOnly == true)
            {
                var data = new Dictionary<string, object>
                {
                    ["status"] = "no_engine_path",
                    ["tag"]    = cap.Tag,
                    ["note"]   = "Read-only command has no dialog-free engine entry yet, so it is not dispatched. " +
                                 "For model reads use the Tier 1 tools (query_elements, get_element, get_compliance, run_validator …).",
                };
                return McpJobResult.Success($"'{cap.Tag}' is read-only but not engine-backed — not dispatched.", data)
                    .ToCallResult();
            }

            // Write tag with no engine handler: refuse honestly.
            return McpJobResult.Error("not_allowed",
                $"'{tag}' is a write command with no dialog-free engine handler yet. Use a Tier-2 verb, " +
                "call with dryRun:true, or wait for it to become engine-backed.").ToCallResult();
        }
    }
}
