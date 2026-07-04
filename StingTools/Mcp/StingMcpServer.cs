// ════════════════════════════════════════════════════════════════════════════
// StingMcpServer — MCP (Model Context Protocol) server embedded in the addin
//
// Exposes five tools to Claude / Claude Code over HTTP JSON-RPC on localhost:
//   run_command   — execute any STING command by tag
//   nlp_query     — natural language → NLP engine → execute best match
//   list_commands — browse the command catalogue with optional filter
//   get_status    — cached compliance scan results
//   ask_bim       — BIM/ISO 19650 knowledge Q&A (local KB first, LLM fallback)
//
// Transport: HTTP JSON-RPC 2.0  (POST http://localhost:{port}/mcp/)
// Lifecycle: Start() called from StingToolsApp.OnStartup when mcp_enabled=true
//            Stop()  called from StingToolsApp.OnShutdown
//
// Claude Code .mcp.json:
//   { "mcpServers": { "stingtools": { "url": "http://localhost:5199/mcp/" } } }
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Mcp
{
    internal static class StingMcpServer
    {
        private static HttpListener _listener;
        private static Thread       _thread;
        private static int          _port    = 5199;
        private static volatile bool _running = false;

        // Shared-secret auth. When non-empty, every POST must carry a matching
        // X-Sting-Mcp-Token header. Loaded from STING_LLM_CONFIG.json at start.
        private static string        _authToken   = "";
        // Curated allowlist of command tags run_command may execute (empty = all
        // known tags). Loaded from config; consumed by the write suite (Phase 3).
        private static List<string>  _toolAllowlist = new List<string>();

        // Reason the last Start() failed to bind (null when the last start succeeded).
        // Surfaced by StartAndPersist() so the toggle dialog can show why.
        private static string        _lastStartError;

        /// <summary>True while the HTTP listener is bound and serving.</summary>
        internal static bool IsRunning => _running;

        /// <summary>
        /// True when a command tag may be executed via invoke_capability: the allowlist is
        /// empty (all known tags permitted) or explicitly contains the tag. Named Tier-2
        /// write verbs bypass this entirely.
        /// </summary>
        internal static bool IsToolAllowed(string tag)
        {
            if (_toolAllowlist == null || _toolAllowlist.Count == 0) return true;
            return _toolAllowlist.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        internal static void StartIfConfigured()
        {
            try
            {
                string cfgPath = Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                if (!File.Exists(cfgPath)) return;

                var cfg = JObject.Parse(File.ReadAllText(cfgPath));
                bool enabled = cfg["mcp_enabled"]?.Value<bool>() ?? false;
                if (!enabled) return;

                int port = cfg["mcp_port"]?.Value<int>() ?? 5199;

                // Load shared-secret + allowlist before binding so the very first
                // request is already governed by the configured policy.
                _authToken = cfg["mcp_auth_token"]?.Value<string>()?.Trim() ?? "";
                _toolAllowlist = (cfg["mcp_tool_allowlist"] as JArray)?
                    .Select(t => t?.Value<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList() ?? new List<string>();

                Start(port);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MCP config read failed: {ex.Message}");
            }
        }

        internal static void Start(int port = 5199)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/mcp/");
            try
            {
                _listener.Start();
                _running = true;
                _lastStartError = null;
                _thread  = new Thread(Listen) { IsBackground = true, Name = "StingMcpServer" };
                _thread.Start();
                StingLog.Info($"STING MCP server started — http://localhost:{port}/mcp/");
                if (string.IsNullOrEmpty(_authToken))
                    StingLog.Warn("STING MCP server is UNAUTHENTICATED — mcp_auth_token is empty in " +
                                  "STING_LLM_CONFIG.json. The server is localhost-bound, but any local " +
                                  "process can call it. Set mcp_auth_token to require the X-Sting-Mcp-Token header.");
                else
                    StingLog.Info("STING MCP server auth: X-Sting-Mcp-Token required on POST.");
            }
            catch (HttpListenerException ex)
            {
                _lastStartError = $"Could not bind to port {port}: {ex.Message}. " +
                                  "Try a different mcp_port in STING_LLM_CONFIG.json.";
                StingLog.Warn("MCP server " + _lastStartError);
            }
        }

        internal static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            StingLog.Info("STING MCP server stopped");
        }

        // ── One-click toggle surface (used by ToggleMcpServerCommand) ─────────────

        /// <summary>
        /// Start the server live and persist the enable flag + auth token to
        /// STING_LLM_CONFIG.json (all other keys preserved). Generates a token on
        /// first enable when none is configured. Reuses <see cref="Start"/> — does
        /// NOT duplicate listener logic. Returns false (with <paramref name="error"/>
        /// populated from the bind reason) and does NOT persist mcp_enabled=true when
        /// the port cannot be bound.
        /// </summary>
        internal static bool StartAndPersist(out string error)
        {
            error = null;
            if (_running) return true;

            try
            {
                string cfgPath = Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                JObject cfg = File.Exists(cfgPath)
                    ? JObject.Parse(File.ReadAllText(cfgPath))
                    : new JObject();

                // Auto-generate a shared secret on first enable.
                string token = cfg["mcp_auth_token"]?.Value<string>()?.Trim() ?? "";
                if (string.IsNullOrEmpty(token))
                    token = Guid.NewGuid().ToString("N");

                int port = cfg["mcp_port"]?.Value<int>() ?? _port;

                // Load token + allowlist into memory BEFORE Start so the first
                // request is already governed by the configured policy.
                _authToken = token;
                _toolAllowlist = (cfg["mcp_tool_allowlist"] as JArray)?
                    .Select(t => t?.Value<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList() ?? new List<string>();

                Start(port);

                if (!_running)
                {
                    // Bind failed — surface the reason, persist nothing.
                    error = _lastStartError ?? $"MCP server failed to start on port {port}.";
                    return false;
                }

                // Persist only after a confirmed successful start.
                cfg["mcp_enabled"]    = true;
                cfg["mcp_auth_token"] = token;
                if (cfg["mcp_port"] == null) cfg["mcp_port"] = port;
                File.WriteAllText(cfgPath, cfg.ToString(Formatting.Indented));
                StingLog.Info("MCP server enabled + persisted via toggle.");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("MCP StartAndPersist failed", ex);
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Stop the server and persist mcp_enabled=false. The auth token is KEPT so
        /// re-enabling reuses the same secret (no client reconfiguration needed).
        /// </summary>
        internal static void StopAndPersist()
        {
            Stop();
            try
            {
                string cfgPath = Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                if (!File.Exists(cfgPath)) return;
                JObject cfg = JObject.Parse(File.ReadAllText(cfgPath));
                cfg["mcp_enabled"] = false;   // keep mcp_auth_token
                File.WriteAllText(cfgPath, cfg.ToString(Formatting.Indented));
                StingLog.Info("MCP server disabled + persisted via toggle (token retained).");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MCP StopAndPersist config write failed: {ex.Message}");
            }
        }

        /// <summary>Current URL + token + a ready-to-paste Claude Code .mcp.json snippet.</summary>
        internal static McpConnectionInfo GetConnectionInfo()
        {
            string url = $"http://localhost:{_port}/mcp/";
            var snippet = new JObject(
                new JProperty("mcpServers", new JObject(
                    new JProperty("stingtools", new JObject(
                        new JProperty("url", url),
                        new JProperty("headers", new JObject(
                            new JProperty("X-Sting-Mcp-Token", _authToken))))))));
            return new McpConnectionInfo
            {
                Url          = url,
                Token        = _authToken,
                ClaudeConfig = snippet.ToString(Formatting.Indented),
            };
        }

        // ── HTTP listener loop ───────────────────────────────────────────────────

        private static void Listen()
        {
            while (_running && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
                }
                catch (HttpListenerException) { break; }
                catch (Exception ex) { StingLog.Warn($"MCP listener: {ex.Message}"); }
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (ctx.Request.HttpMethod == "OPTIONS")
                { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                // GET → server info
                if (ctx.Request.HttpMethod == "GET")
                {
                    WriteJson(ctx, 200, new
                    {
                        server  = "STING Tools MCP",
                        version = "1.0",
                        port    = _port,
                        tools   = McpToolRegistry.GetTools().Select(t => t.Name).ToList(),
                        usage   = "POST /mcp/ with a JSON-RPC 2.0 body"
                    });
                    return;
                }

                if (ctx.Request.HttpMethod != "POST")
                { WriteJson(ctx, 405, new { error = "Method not allowed" }); return; }

                // Shared-secret gate — POST only (GET server-info stays open, localhost
                // bound). When a token is configured, the X-Sting-Mcp-Token header must
                // match exactly; otherwise reject with JSON-RPC -32001 before doing any
                // work. Id is unknown at this point (body not yet read) → null.
                if (!string.IsNullOrEmpty(_authToken))
                {
                    string presented = ctx.Request.Headers["X-Sting-Mcp-Token"];
                    if (!string.Equals(presented, _authToken, StringComparison.Ordinal))
                    {
                        StingLog.Warn("MCP POST rejected: missing/invalid X-Sting-Mcp-Token.");
                        WriteJson(ctx, 200, RpcError(null, -32001, "unauthorized"));
                        return;
                    }
                }

                string body;
                using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = sr.ReadToEnd();

                JsonRpcRequest req;
                try { req = JsonConvert.DeserializeObject<JsonRpcRequest>(body); }
                catch { WriteJson(ctx, 400, RpcError(null, -32700, "JSON parse error")); return; }

                WriteJson(ctx, 200, Dispatch(req));
            }
            catch (Exception ex)
            {
                StingLog.Error("MCP handle error", ex);
                try { WriteJson(ctx, 500, RpcError(null, -32603, ex.Message)); } catch { }
            }
        }

        // ── JSON-RPC dispatcher ──────────────────────────────────────────────────

        private static JsonRpcResponse Dispatch(JsonRpcRequest req)
        {
            if (req == null) return RpcError(null, -32600, "Invalid request");

            switch (req.Method)
            {
                case "initialize":
                    return RpcOk(req.Id, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities    = new { tools = new { listChanged = false } },
                        serverInfo      = new { name = "stingtools", version = "1.0" }
                    });

                case "tools/list":
                    return RpcOk(req.Id, new { tools = McpToolRegistry.GetTools() });

                case "tools/call":
                    return HandleToolCall(req);

                case "notifications/initialized":
                    return RpcOk(req.Id, new { }); // ack — no response body needed

                default:
                    return RpcError(req.Id, -32601, $"Unknown method: {req.Method}");
            }
        }

        private static JsonRpcResponse HandleToolCall(JsonRpcRequest req)
        {
            string name = req.Params?["name"]?.Value<string>();
            var    args = req.Params?["arguments"] as JObject ?? new JObject();

            // Single tool-execution path — shared with the in-Revit Copilot.
            McpCallResult result = McpToolDispatcher.Dispatch(name, args);
            return RpcOk(req.Id, result);
        }

        // ── Response helpers ─────────────────────────────────────────────────────

        private static JsonRpcResponse RpcOk(string id, object result) =>
            new JsonRpcResponse { Id = id, Result = result };

        private static JsonRpcResponse RpcError(string id, int code, string message) =>
            new JsonRpcResponse { Id = id, Error = new McpRpcError { Code = code, Message = message } };

        private static void WriteJson(HttpListenerContext ctx, int status, object obj)
        {
            byte[] buf = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
            ctx.Response.StatusCode      = status;
            ctx.Response.ContentType     = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
        }
    }
}
