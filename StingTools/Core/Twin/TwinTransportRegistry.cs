// Healthcare Pack HC-DEF-05 — pluggable Twin transport registration seam.
//
// A live BACnet/OPC-UA stack must NOT be linked into the plugin assembly (it
// would pull a heavy 3rd-party dependency into every Revit session). Instead
// this registry lets a host/project register a real transport adapter behind
// the existing TwinReadbackBase contract, discovered by protocol name. The
// empty built-ins (BacnetReadback / OpcUaReadback) stay the default so nothing
// breaks when no adapter is registered.
//
// Adapter contract:
//   1. Implement TwinReadbackBase (ProtocolName + Poll(targets)).
//   2. Register a factory at startup (e.g. from a companion add-in's
//      IExternalApplication.OnStartup):
//        TwinTransportRegistry.Register("BACNET", () => new MyBacnetTransport());
//   3. Consumers resolve by protocol: TwinTransportRegistry.Resolve("BACNET").Poll(...).
//      Resolve() returns the registered adapter, else the no-op default, so callers
//      never null-check — an unconfigured protocol simply yields an empty snapshot
//      set and validators/BCC surface "stale".

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;

namespace StingTools.Core.Twin
{
    /// <summary>Ultimate fallback transport — returns no readings for any protocol
    /// with no registered adapter and no matching built-in.</summary>
    public sealed class NullTwinReadback : TwinReadbackBase
    {
        public override string ProtocolName => "NONE";
        public override IEnumerable<TwinSnapshot> Poll(IEnumerable<IoTDeviceRef> targets) => Array.Empty<TwinSnapshot>();
    }

    public static class TwinTransportRegistry
    {
        private static readonly ConcurrentDictionary<string, Func<TwinReadbackBase>> _factories
            = new ConcurrentDictionary<string, Func<TwinReadbackBase>>(StringComparer.OrdinalIgnoreCase);

        static TwinTransportRegistry()
        {
            // Built-in no-op defaults. A host overrides by registering a live
            // adapter under the same protocol key.
            _factories["BACNET"] = () => new BacnetReadback();
            _factories["OPC-UA"] = () => new OpcUaReadback();
        }

        /// <summary>Registers (or replaces) the transport adapter factory for a protocol.</summary>
        public static void Register(string protocol, Func<TwinReadbackBase> factory)
        {
            if (string.IsNullOrWhiteSpace(protocol) || factory == null) return;
            _factories[protocol.Trim()] = factory;
            StingLog.Info($"TwinTransportRegistry: registered transport for '{protocol.Trim()}'.");
        }

        /// <summary>True when a live (host-registered) adapter exists for the protocol,
        /// i.e. beyond the built-in no-op defaults.</summary>
        public static bool HasLiveAdapter(string protocol) =>
            !string.IsNullOrWhiteSpace(protocol) &&
            _factories.TryGetValue(protocol.Trim(), out var f) &&
            !(f() is BacnetReadback) && !(f() is OpcUaReadback) && !(f() is NullTwinReadback);

        public static IEnumerable<string> RegisteredProtocols => _factories.Keys.ToList();

        /// <summary>Resolves a transport for the protocol; never null — falls back to the
        /// built-in default, then a NullTwinReadback.</summary>
        public static TwinReadbackBase Resolve(string protocol)
        {
            if (!string.IsNullOrWhiteSpace(protocol) && _factories.TryGetValue(protocol.Trim(), out var f))
            {
                try { return f() ?? new NullTwinReadback(); }
                catch (Exception ex) { StingLog.Warn($"TwinTransportRegistry resolve '{protocol}': {ex.Message}"); }
            }
            return new NullTwinReadback();
        }
    }
}
