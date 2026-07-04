// ════════════════════════════════════════════════════════════════════════════
// McpResult — typed read-back contract for MCP v2 tools
//
// McpJobResult is the internal result every read/write job returns. It carries a
// success flag, a machine-readable Code, a human Summary line, and an arbitrary
// serialisable Data payload. ToCallResult() renders it into the wire-level
// McpCallResult the MCP client sees:
//
//     <Summary>
//
//     ```json
//     <Data serialized>
//     ```
//
// Rationale: agents parse the fenced JSON block deterministically; humans reading
// the transcript get the Summary line. isError mirrors !Ok.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Mcp
{
    /// <summary>
    /// Result of an MCP job run on the Revit API thread. Convert to the wire-level
    /// <see cref="McpCallResult"/> via <see cref="ToCallResult"/>.
    /// </summary>
    internal class McpJobResult
    {
        /// <summary>True when the job succeeded.</summary>
        public bool Ok { get; set; }

        /// <summary>Machine-readable outcome code ("ok", "not_licensed", "no_document",
        /// "revit_busy", "timeout", "exception", etc.).</summary>
        public string Code { get; set; }

        /// <summary>Short human-readable summary line.</summary>
        public string Summary { get; set; }

        /// <summary>Arbitrary serialisable payload (POCO / dictionary). May be null.</summary>
        public object Data { get; set; }

        // ── Factory helpers ──────────────────────────────────────────────────────

        public static McpJobResult Success(string summary, object data = null) => new McpJobResult
        {
            Ok      = true,
            Code    = "ok",
            Summary = summary ?? string.Empty,
            Data    = data,
        };

        public static McpJobResult Error(string code, string message) => new McpJobResult
        {
            Ok      = false,
            Code    = string.IsNullOrEmpty(code) ? "error" : code,
            Summary = message ?? string.Empty,
            Data    = null,
        };

        // ── Serialisation ────────────────────────────────────────────────────────

        /// <summary>
        /// Render this result into an <see cref="McpCallResult"/> whose single text
        /// content is the summary followed by a fenced JSON block of the data.
        /// When Data is null a minimal envelope ({ ok, code }) is emitted so a JSON
        /// block is always present for deterministic parsing.
        /// </summary>
        public McpCallResult ToCallResult()
        {
            object payload = Data ?? new { ok = Ok, code = Code };

            string json;
            try
            {
                json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            }
            catch (Exception ex)
            {
                // Never let a serialisation failure sink the whole response — degrade
                // to an error envelope rather than throwing on the API thread.
                Core.StingLog.Warn($"McpJobResult.ToCallResult serialise failed: {ex.Message}");
                json = JsonConvert.SerializeObject(new { ok = false, code = "serialise_error", message = ex.Message });
            }

            string text = $"{Summary}\n\n```json\n{json}\n```";

            return new McpCallResult
            {
                Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
                IsError = !Ok,
            };
        }
    }
}
