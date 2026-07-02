// ════════════════════════════════════════════════════════════════════════════
// McpSafety — guardrail helpers shared by every MCP v2 job
//
// Jobs run on the Revit API thread via McpJobBridge. Each job must first re-check
// the license gate (the panel's command handler enforces it, but tools that touch
// the document directly bypass that handler, so they MUST re-check here or become
// a licensing bypass) then confirm a document is open. Model mutation runs inside
// RunInTransactionGroup so any exception rolls the whole operation back.
//
// dryRun / confirm helpers here are lightweight arg readers; they are fully
// exercised by the write suite in Phase 3.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Mcp
{
    internal static class McpSafety
    {
        /// <summary>
        /// Returns a typed not_licensed error when STING is unlicensed on this
        /// machine, else null. Mirrors the hard-lock in StingCommandHandler.Execute.
        /// </summary>
        public static McpJobResult RequireLicense()
        {
            if (!Core.Licensing.LicenseGate.IsLicensed)
                return McpJobResult.Error("not_licensed",
                    "STING Tools is not licensed on this machine. Activate via the " +
                    "STING Tools → Activate ribbon button, then retry.");
            return null;
        }

        /// <summary>
        /// Returns a typed no_document error when there is no active Revit document,
        /// else null.
        /// </summary>
        public static McpJobResult RequireDocument(UIApplication uiApp)
        {
            if (uiApp?.ActiveUIDocument?.Document == null)
                return McpJobResult.Error("no_document",
                    "No active Revit document. Open a project in Revit first, then retry.");
            return null;
        }

        /// <summary>
        /// Runs <paramref name="body"/> inside a named TransactionGroup that is
        /// assimilated on success and rolled back on any exception. The exception
        /// is rethrown so the caller (job bridge) can convert it to a typed error.
        /// Use a "STING " prefix on <paramref name="name"/> per house convention.
        /// </summary>
        public static void RunInTransactionGroup(Document doc, string name, Action body)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (body == null) throw new ArgumentNullException(nameof(body));

            using (var tg = new TransactionGroup(doc, name))
            {
                tg.Start();
                try
                {
                    body();
                    tg.Assimilate();
                }
                catch
                {
                    try { if (tg.GetStatus() == TransactionStatus.Started) tg.RollBack(); }
                    catch (Exception rbEx) { StingLog.Warn($"McpSafety rollback failed: {rbEx.Message}"); }
                    throw;
                }
            }
        }

        // ── dry-run / confirm helpers (fully used in Phase 3) ────────────────────

        /// <summary>Read a boolean argument from the tool call, defaulting when absent.</summary>
        public static bool ReadBool(JObject args, string key, bool defaultValue = false)
        {
            try { return args?[key]?.Value<bool?>() ?? defaultValue; }
            catch { return defaultValue; }
        }

        /// <summary>True when the caller requested a dry run (dryRun:true).</summary>
        public static bool IsDryRun(JObject args) => ReadBool(args, "dryRun", false);

        /// <summary>True when the caller explicitly confirmed a bulk/destructive op (confirm:true).</summary>
        public static bool IsConfirmed(JObject args) => ReadBool(args, "confirm", false);

        // ── Write guardrails (Phase 3) ───────────────────────────────────────────

        /// <summary>Operations touching more than this many elements require confirm:true.</summary>
        public const int BulkThreshold = 25;

        /// <summary>
        /// Confirm gate. Returns a typed needs_confirmation error (carrying the projected
        /// count) when the op affects &gt; <see cref="BulkThreshold"/> elements OR is
        /// project-scope AND confirm was not passed; else null.
        /// </summary>
        public static McpJobResult RequireConfirmation(int affectedCount, bool isProjectScope, bool confirmed)
        {
            bool needs = affectedCount > BulkThreshold || isProjectScope;
            if (needs && !confirmed)
                return McpJobResult.Error("needs_confirmation",
                    $"This operation affects {affectedCount} element(s)" +
                    (isProjectScope ? " (project scope)" : "") +
                    $". Re-call with confirm:true to proceed. Data preview: run again with dryRun:true.");
            return null;
        }

        /// <summary>
        /// Allowlist check for invoke_capability writes. When mcp_tool_allowlist is
        /// non-empty and the tag is absent, returns a typed not_allowed error; else null.
        /// Named Tier-2 verbs bypass this (they are always allowed).
        /// </summary>
        public static McpJobResult CheckAllowlist(string tag)
        {
            if (StingMcpServer.IsToolAllowed(tag)) return null;
            return McpJobResult.Error("not_allowed",
                $"'{tag}' is not in mcp_tool_allowlist. Add it to STING_LLM_CONFIG.json to permit invocation.");
        }

        /// <summary>Standard structured write read-back envelope. Errors + sampleIds capped at 25.</summary>
        public static Dictionary<string, object> WriteResult(
            int changed, int skipped, IEnumerable<string> errors, IEnumerable<long> sampleIds)
        {
            return new Dictionary<string, object>
            {
                ["changed"]   = changed,
                ["skipped"]   = skipped,
                ["errors"]    = errors?.Take(25).ToList() ?? new List<string>(),
                ["sampleIds"] = sampleIds?.Take(25).ToList() ?? new List<long>(),
            };
        }
    }
}
