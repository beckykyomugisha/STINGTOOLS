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
                _thread  = new Thread(Listen) { IsBackground = true, Name = "StingMcpServer" };
                _thread.Start();
                StingLog.Info($"STING MCP server started — http://localhost:{port}/mcp/");
            }
            catch (HttpListenerException ex)
            {
                StingLog.Warn($"MCP server could not bind to port {port}: {ex.Message}. " +
                              "Try a different mcp_port in STING_LLM_CONFIG.json.");
            }
        }

        internal static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            StingLog.Info("STING MCP server stopped");
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

            McpCallResult result;
            switch (name)
            {
                case "run_command":   result = ToolRunCommand(args);   break;
                case "nlp_query":     result = ToolNlpQuery(args);     break;
                case "list_commands": result = ToolListCommands(args); break;
                case "get_status":    result = ToolGetStatus();        break;
                case "ask_bim":       result = ToolAskBim(args);       break;
                default:
                    result = Err($"Unknown tool: {name}. Call tools/list to see available tools.");
                    break;
            }
            return RpcOk(req.Id, result);
        }

        // ── Tool: run_command ────────────────────────────────────────────────────

        private static McpCallResult ToolRunCommand(JObject args)
        {
            string tag = args["tag"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(tag))
                return Err("Missing required argument: tag");

            // Validate against the NLP catalogue so unknown tags are caught before dispatch
            bool known = NLPEngine.IntentPatterns
                .Any(p => string.Equals(p.CommandTag, tag, StringComparison.OrdinalIgnoreCase));
            if (!known)
                return Err($"'{tag}' is not a recognised STING command tag. " +
                           "Call list_commands (with a filter) to find the right tag.");

            bool accepted = StingDockPanel.DispatchCommand(tag);
            return accepted
                ? Ok($"Command '{tag}' dispatched to Revit. " +
                     "The command is now executing — check the Revit window for results and dialogs.")
                : Err("Revit ExternalEvent was not accepted. " +
                      "Revit may be in a modal dialog, a transaction, or mid-sync. Try again shortly.");
        }

        // ── Tool: nlp_query ──────────────────────────────────────────────────────

        private static McpCallResult ToolNlpQuery(JObject args)
        {
            string text = args["text"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(text))
                return Err("Missing required argument: text");

            var results = NLPEngine.ProcessQuery(text);
            if (results.Count == 0)
                return Err($"No STING command matched \"{text}\". " +
                           "Try list_commands to browse, or rephrase using BIM/Revit terminology.");

            string tag  = results[0].CommandTag;
            double conf = results[0].Confidence;

            var alts = results.Skip(1).Take(3)
                .Select(r => $"{r.CommandTag} ({r.Confidence:P0})")
                .ToList();
            string altText = alts.Count > 0
                ? $"\nAlternatives: {string.Join(", ", alts)}"
                : string.Empty;

            bool accepted = StingDockPanel.DispatchCommand(tag);
            return accepted
                ? Ok($"Matched \"{text}\" -> {tag} ({conf:P0} confidence). " +
                     $"Command dispatched to Revit.{altText}")
                : Err($"Matched '{tag}' but Revit ExternalEvent was not accepted. " +
                      "Revit may be busy — try again shortly.");
        }

        // ── Tool: list_commands ──────────────────────────────────────────────────

        private static McpCallResult ToolListCommands(JObject args)
        {
            string filter = args["filter"]?.Value<string>()?.ToLower() ?? string.Empty;

            var commands = NLPEngine.IntentPatterns
                .GroupBy(p => p.CommandTag, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Where(p => string.IsNullOrEmpty(filter) ||
                            p.CommandTag.ToLower().Contains(filter) ||
                            p.Description.ToLower().Contains(filter))
                .OrderBy(p => p.CommandTag)
                .Take(60)
                .Select(p => $"{p.CommandTag}: {p.Description}")
                .ToList();

            return commands.Count > 0
                ? Ok($"{commands.Count} command(s) found:\n\n" + string.Join("\n", commands))
                : Ok($"No commands matched filter \"{filter}\". Try a broader keyword.");
        }

        // ── Tool: get_status ─────────────────────────────────────────────────────

        private static McpCallResult ToolGetStatus()
        {
            var scan = ComplianceScan.GetCached();
            if (scan == null)
                return Ok("No compliance scan cached yet. " +
                          "Open a Revit project and run MorningHealthCheck to populate status.");

            return Ok($"STING Model Status\n{scan.StatusBarText}\n" +
                      $"MCP server: running on http://localhost:{_port}/mcp/");
        }

        // ── Tool: ask_bim ────────────────────────────────────────────────────────

        private static McpCallResult ToolAskBim(JObject args)
        {
            string question = args["question"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(question))
                return Err("Missing required argument: question");

            // Local knowledge base first (instant, offline, no credentials)
            var matches = NLPEngine.SearchKnowledge(question);
            if (matches != null && matches.Count > 0)
            {
                string answer = string.Join("\n\n",
                    matches.Select(m => $"{m.Term}:\n{m.Definition}"));
                return Ok($"[Local BIM Knowledge Base]\n\n{answer}");
            }

            // LLM fallback (requires credentials + mcp_enabled)
            try
            {
                var task = StingLlmService.Instance.AskBimQuestionAsync(question);
                if (task.Wait(8_000) && task.Result != null)
                    return Ok(task.Result);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MCP ask_bim LLM fallback failed: {ex.Message}");
            }

            return Ok("No local match found. Try asking about: ISO 19650, COBie, IFC, BEP, " +
                      "CDE, MIDP, TIDP, RIBA stages, suitability codes, TAG7, DrawingType, " +
                      "ViewStylePack, NLPEngine, or any of the 63 entries in the BIM KB.");
        }

        // ── Response helpers ─────────────────────────────────────────────────────

        private static McpCallResult Ok(string text) => new McpCallResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
            IsError = false,
        };

        private static McpCallResult Err(string text) => new McpCallResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
            IsError = true,
        };

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
