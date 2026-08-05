using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Mcp
{
    // JSON-RPC 2.0 base types — used for the MCP HTTP transport
    internal class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")] public string Jsonrpc  { get; set; }
        [JsonProperty("id")]      public string Id       { get; set; }
        [JsonProperty("method")]  public string Method   { get; set; }
        [JsonProperty("params")]  public JObject Params  { get; set; }
    }

    internal class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")] public string Jsonrpc = "2.0";
        [JsonProperty("id")]      public string Id { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public McpRpcError Error { get; set; }
    }

    internal class McpRpcError
    {
        [JsonProperty("code")]    public int    Code    { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    internal class McpTool
    {
        [JsonProperty("name")]        public string  Name        { get; set; }
        [JsonProperty("description")] public string  Description { get; set; }
        [JsonProperty("inputSchema")] public JObject InputSchema { get; set; }
    }

    internal class McpContent
    {
        [JsonProperty("type")] public string Type { get; set; } = "text";
        [JsonProperty("text")] public string Text { get; set; }
    }

    internal class McpCallResult
    {
        [JsonProperty("content")] public List<McpContent> Content { get; set; }
        [JsonProperty("isError")] public bool             IsError { get; set; }
    }

    // Connection details surfaced by the one-click MCP server toggle: the live URL,
    // the shared-secret token, and a ready-to-paste Claude Code .mcp.json snippet.
    internal class McpConnectionInfo
    {
        public string Url          { get; set; }
        public string Token        { get; set; }
        public string ClaudeConfig { get; set; }
    }
}
