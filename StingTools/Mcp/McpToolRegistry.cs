using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.Mcp
{
    // Defines the five MCP tools exposed to Claude / Claude Code.
    // Each tool maps to a handler in StingMcpServer.HandleToolCall().
    internal static class McpToolRegistry
    {
        internal static List<McpTool> GetTools() => new List<McpTool>
        {
            new McpTool
            {
                Name = "run_command",
                Description =
                    "Run any STING Tools Revit command by its exact command tag. " +
                    "Use list_commands to discover valid tags. " +
                    "Example tags: AutoTag, BatchTag, COBieExport, MorningHealthCheck, " +
                    "DrawingTypes_SyncStyles, Panel_BatchSchedules, Fabrication_GeneratePackage.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""tag"": {
                            ""type"": ""string"",
                            ""description"": ""The STING command tag to execute""
                        }
                    },
                    ""required"": [""tag""]
                }"),
            },

            new McpTool
            {
                Name = "nlp_query",
                Description =
                    "Execute a STING command using natural language. " +
                    "The built-in NLP engine (435 patterns, offline) finds the best match and runs it. " +
                    "Returns the matched command tag, confidence, and any alternatives. " +
                    "Example: 'tag all mechanical elements', 'run daily QA', " +
                    "'export COBie for handover', 'generate panel schedules'.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""text"": {
                            ""type"": ""string"",
                            ""description"": ""Natural language description of the action you want to perform in Revit""
                        }
                    },
                    ""required"": [""text""]
                }"),
            },

            new McpTool
            {
                Name = "list_commands",
                Description =
                    "List available STING Tools commands with descriptions. " +
                    "Optionally filter by keyword. Returns up to 60 matching commands. " +
                    "Use this to discover the right command tag before calling run_command.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filter"": {
                            ""type"": ""string"",
                            ""description"": ""Optional keyword to filter by command name or description""
                        }
                    }
                }"),
            },

            new McpTool
            {
                Name = "get_status",
                Description =
                    "Get the current STING Tools model status: compliance percentage, " +
                    "tagged/untagged element counts, RAG rating, and top issues. " +
                    "Reads from the cached compliance scan — no Revit API call required.",
                InputSchema = JObject.Parse(@"{""type"": ""object"", ""properties"": {}}"),
            },

            new McpTool
            {
                Name = "ask_bim",
                Description =
                    "Ask a BIM, ISO 19650, or STING Tools knowledge question. " +
                    "Answered from the local 63-entry knowledge base first (instant, offline). " +
                    "Falls back to the configured LLM (Azure OpenAI or Claude) if no local match. " +
                    "Good for: ISO 19650 terms, COBie fields, RIBA stages, suitability codes, " +
                    "STING-specific features (TAG7, DrawingType, ViewStylePack, NLPEngine).",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""question"": {
                            ""type"": ""string"",
                            ""description"": ""Your BIM or standards question""
                        }
                    },
                    ""required"": [""question""]
                }"),
            },
        };
    }
}
