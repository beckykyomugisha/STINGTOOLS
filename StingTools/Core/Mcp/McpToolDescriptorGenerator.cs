// Pack 12 — Revit 2027 Model Context Protocol bridge.
//
// Revit 2027 ships a public MCP server — the wire protocol AI agents use to
// call application functions. STING's 763 IExternalCommand classes are
// already arg-less discrete units of work, which is exactly what MCP tools
// look like. This file enumerates every command via reflection and emits
// an MCP tool descriptor per class — the server binds them on startup.
//
// Gated by the REVIT_2027 compile-time symbol (add to csproj
// <DefineConstants>REVIT_2027</DefineConstants> or pass -p:DefineConstants=REVIT_2027
// on the build command line). The main net8.0-windows build against Revit
// 2025/2026 skips the whole file — zero impact.
//
// Net10 multi-targeting: when the main project flips to
// <TargetFrameworks>net8.0-windows;net10.0-windows</TargetFrameworks>, the
// MCP bridge lights up for net10 only. Net8 target remains exactly as
// today.
//
// TODO-VERIFY-API: the Revit 2027 MCP namespace is expected to be
// Autodesk.Revit.Mcp.* per the Roadmap announcement; the exact types
// aren't public yet. This file uses attribute-driven descriptors so the
// registration layer can be stubbed without a hard compile-time dep.

#if REVIT_2027

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;

namespace StingTools.Core.Mcp
{
    /// <summary>
    /// Minimal MCP tool descriptor. Serialised into the server's registry on
    /// startup. Fields mirror the published MCP schema: name, description,
    /// input_schema (JSON Schema object).
    /// </summary>
    public class McpToolDescriptor
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string InputSchema { get; set; } = "{\"type\":\"object\",\"properties\":{}}";
        public string CommandClass { get; set; } = "";
    }

    public static class McpToolDescriptorGenerator
    {
        /// <summary>
        /// Scan this assembly for every IExternalCommand and produce one MCP
        /// tool descriptor per class. Command tag (the dock-panel button tag)
        /// becomes the tool name; the class XML-doc summary becomes the
        /// description.
        /// </summary>
        public static List<McpToolDescriptor> Generate()
        {
            var list = new List<McpToolDescriptor>();
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (Type t in asm.GetTypes())
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IExternalCommand).IsAssignableFrom(t)) continue;

                    var desc = new McpToolDescriptor
                    {
                        Name = DeriveToolName(t),
                        Description = DeriveDescription(t),
                        CommandClass = t.FullName,
                    };
                    list.Add(desc);
                }
                StingLog.Info($"McpToolDescriptorGenerator: emitted {list.Count} tool descriptor(s)");
            }
            catch (Exception ex)
            {
                StingLog.Error("McpToolDescriptorGenerator.Generate", ex);
            }
            return list;
        }

        private static string DeriveToolName(Type t)
        {
            // Strip the "Command" suffix and prefix with the class's namespace
            // leaf — "Tags.AutoTagCommand" → "Tags_AutoTag".
            string name = t.Name;
            if (name.EndsWith("Command", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Command".Length);
            string ns = t.Namespace ?? "";
            string leaf = ns.Split('.').LastOrDefault() ?? "";
            return string.IsNullOrEmpty(leaf) ? name : $"{leaf}_{name}";
        }

        private static string DeriveDescription(Type t)
        {
            // XML docs aren't accessible at runtime; fall back to a terse
            // synthesized description based on the namespace + class name.
            string verb = t.Name.EndsWith("Command", StringComparison.Ordinal)
                ? t.Name.Substring(0, t.Name.Length - "Command".Length) : t.Name;
            string area = (t.Namespace ?? "").Split('.').LastOrDefault() ?? "command";
            return $"Invoke STING {verb} from the {area} area. No arguments required.";
        }
    }

    /// <summary>
    /// Stub server registration — calls the real Revit 2027 MCP registrar at
    /// startup. TODO-VERIFY-API: exact namespace on first net10 build.
    /// </summary>
    public static class McpServerRegistrar
    {
        public static void RegisterAll(UIControlledApplication app)
        {
            if (app == null) return;
            try
            {
                var descriptors = McpToolDescriptorGenerator.Generate();
                // TODO-VERIFY-API: Autodesk.Revit.Mcp.McpHost.RegisterTool(desc)
                // is the expected public API per the 2027 Roadmap. Until the
                // SDK ships, this block is a no-op that logs intent.
                StingLog.Info($"McpServerRegistrar: prepared {descriptors.Count} MCP tool descriptor(s) for registration");
            }
            catch (Exception ex)
            {
                StingLog.Error("McpServerRegistrar.RegisterAll", ex);
            }
        }
    }
}

#endif
