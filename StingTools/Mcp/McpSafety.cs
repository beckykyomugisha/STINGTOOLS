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
    }
}
