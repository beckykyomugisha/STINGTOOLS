// ════════════════════════════════════════════════════════════════════════════
// McpCommandScan — the single assembly-reflection enumeration of every command
//
// One place that answers "what IExternalCommand classes ship in this assembly?".
// The Revit-2027 MCP descriptor generator (Core/Mcp/McpToolDescriptorGenerator.cs,
// gated behind #if REVIT_2027) and the always-compiled capability catalogue
// (Mcp/McpCapabilityCatalogue.cs) both call this so there is exactly one scan.
//
// Pure reflection over TYPES — no command is ever instantiated here, and no Revit
// API is touched, so it is safe to call off the API thread. Assembly.GetTypes can
// throw ReflectionTypeLoadException when a dependent type fails to load; that is
// caught and the successfully-loaded types are returned so one bad type can never
// sink the whole enumeration.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Mcp
{
    internal static class McpCommandScan
    {
        /// <summary>
        /// Every non-abstract, non-interface class in this assembly that implements
        /// <see cref="IExternalCommand"/>. Never instantiates; resilient to partial
        /// type-load failures.
        /// </summary>
        public static IReadOnlyList<Type> AllCommandTypes()
        {
            Type[] all;
            try
            {
                all = Assembly.GetExecutingAssembly().GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                // Return the types that DID load; log the loader failures once.
                all = rtle.Types.Where(t => t != null).ToArray();
                StingLog.Warn($"McpCommandScan: {rtle.LoaderExceptions?.Length ?? 0} type(s) failed to load; " +
                              $"continuing with {all.Length} loaded type(s).");
            }
            catch (Exception ex)
            {
                StingLog.Error("McpCommandScan.AllCommandTypes", ex);
                return Array.Empty<Type>();
            }

            var list = new List<Type>(all.Length);
            foreach (Type t in all)
            {
                try
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IExternalCommand).IsAssignableFrom(t)) continue;
                    list.Add(t);
                }
                catch (Exception ex)
                {
                    // A single mis-behaving type must not abort the sweep.
                    StingLog.Warn($"McpCommandScan: skipped type '{t?.FullName}': {ex.Message}");
                }
            }
            return list;
        }
    }
}
