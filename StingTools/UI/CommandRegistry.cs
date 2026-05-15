// Phase 165 (INT-02 framework) — dispatch registry for StingCommandHandler.
//
// StingCommandHandler.cs is the single largest file in the codebase
// (≈8 000 lines, ≈1 560 case branches). Migrating every branch in one
// change is too risky to do without compile + Revit verification, so this
// file ships the framework and one fully-converted module — Electrical —
// as a working example of the target pattern. Subsequent modules can be
// migrated one panel at a time, each one removing its case branches from
// the giant switch in StingCommandHandler.Execute and instead registering
// itself at startup.
//
// Pattern:
//   1. A module class (one per panel — Electrical, Drawing, BOQ, Clash …)
//      implements ICommandModule.Register(reg).
//   2. CommandRegistry collects registered handlers in a Dictionary.
//   3. StingCommandHandler.Execute first asks the registry to handle the
//      tag; if no module owns it the existing switch runs as a fallback.
//
// Once every panel has been migrated, the giant switch can be deleted
// outright. Until then, the registry coexists with the switch with
// zero behaviour change for unrouted tags.

using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>Modules bundle one panel's worth of tag → command mappings.</summary>
    public interface ICommandModule
    {
        void Register(CommandRegistry registry);
    }

    /// <summary>Single source of truth for tag → command dispatch.</summary>
    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, Action<UIApplication>> _map =
            new Dictionary<string, Action<UIApplication>>(StringComparer.Ordinal);

        public int Count => _map.Count;

        /// <summary>Register one tag. Throws on duplicate registration so wiring
        /// mistakes surface during the next test run rather than silently
        /// shadowing the original handler.</summary>
        public void Register(string tag, Action<UIApplication> handler)
        {
            if (string.IsNullOrEmpty(tag)) throw new ArgumentException("tag must not be empty");
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_map.ContainsKey(tag))
                throw new InvalidOperationException($"CommandRegistry: duplicate registration for '{tag}'");
            _map[tag] = handler;
        }

        /// <summary>Try to dispatch. Returns true if the tag was handled.</summary>
        public bool TryHandle(string tag, UIApplication app)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            if (!_map.TryGetValue(tag, out var handler)) return false;
            try { handler(app); return true; }
            catch (Exception ex)
            {
                StingLog.Error($"CommandRegistry tag '{tag}' threw", ex);
                throw;
            }
        }

        // ── Bootstrap ─────────────────────────────────────────────────────────
        private static readonly object _instanceLock = new object();
        private static CommandRegistry _instance;

        public static CommandRegistry Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_instanceLock)
                {
                    if (_instance != null) return _instance;
                    var reg = new CommandRegistry();
                    foreach (var module in EnumerateModules())
                    {
                        try { module.Register(reg); }
                        catch (Exception ex) { StingLog.Warn($"CommandRegistry: module '{module.GetType().Name}' threw during register: {ex.Message}"); }
                    }
                    StingLog.Info($"CommandRegistry: {reg.Count} tags registered across module set");
                    _instance = reg;
                    return _instance;
                }
            }
        }

        private static IEnumerable<ICommandModule> EnumerateModules()
        {
            yield return new ElectricalCommandModule();
            // INT-02 Phase 2: remaining panel modules
            yield return new Modules.SelectCommandModule();
            yield return new Modules.OrganiseCommandModule();
            yield return new Modules.DocsCommandModule();
            yield return new Modules.TempCommandModule();
            yield return new Modules.TagsCommandModule();
            yield return new Modules.BimCommandModule();
            yield return new Modules.ModelCommandModule();
            yield return new Modules.ViewCommandModule();
        }
    }

    /// <summary>Phase 165 (INT-02) — pilot module. Owns the four
    /// `Electrical_*` tags that until now lived inline in StingCommandHandler's
    /// 1 560-case switch. Demonstrates the migration pattern for the
    /// remaining ≈ 25 panels.</summary>
    internal sealed class ElectricalCommandModule : ICommandModule
    {
        public void Register(CommandRegistry registry)
        {
            registry.Register("Electrical_AddCable",
                app => StingCommandHandler.RunCommandPublic<Commands.Electrical.AddCableCommand>(app));
            registry.Register("Electrical_ListCables",
                app => StingCommandHandler.RunCommandPublic<Commands.Electrical.ListCablesCommand>(app));
            registry.Register("Electrical_ExportCircuits",
                app => StingCommandHandler.RunCommandPublic<Commands.Electrical.ExportCircuitsCommand>(app));
            registry.Register("Electrical_TrayFill",
                app => StingCommandHandler.RunCommandPublic<Commands.Electrical.ShowTrayFillCommand>(app));
        }
    }
}
