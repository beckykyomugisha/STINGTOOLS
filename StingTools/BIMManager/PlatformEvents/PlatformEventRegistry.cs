#nullable enable
using System;
using System.Collections.Generic;
using StingTools.Core;

namespace StingTools.BIMManager.PlatformEvents;

/// <summary>
/// K2 (STING side) — maps event Type → handler. Built-in handlers register at
/// first use; feature modules add their own via <see cref="Register"/> (e.g.
/// the meeting layer registers "clash.resolved", the twin layer "twin.alert").
/// </summary>
public static class PlatformEventRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, IApplyPlatformEvent> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _seeded;

    /// <summary>Conflict-guard revision source. Swap for a real provider per project.</summary>
    public static IModelRevisionProvider RevisionProvider { get; set; } = new NullModelRevisionProvider();

    public static void Register(IApplyPlatformEvent handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        lock (_lock)
        {
            _handlers[handler.EventType] = handler;
            StingLog.Info($"PlatformEvent handler registered: {handler.EventType}");
        }
    }

    public static IApplyPlatformEvent? Resolve(string eventType)
    {
        EnsureSeeded();
        lock (_lock)
            return _handlers.TryGetValue(eventType ?? "", out var h) ? h : null;
    }

    public static IReadOnlyCollection<string> KnownTypes
    {
        get { EnsureSeeded(); lock (_lock) return new List<string>(_handlers.Keys); }
    }

    // Built-in handlers. Kept minimal — feature modules register the rest.
    private static void EnsureSeeded()
    {
        if (_seeded) return;
        lock (_lock)
        {
            if (_seeded) return;
            if (!_handlers.ContainsKey(ParamStampEventHandler.Type))
                _handlers[ParamStampEventHandler.Type] = new ParamStampEventHandler();
            _seeded = true;
        }
    }
}
