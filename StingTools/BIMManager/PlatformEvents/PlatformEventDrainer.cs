#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BIMManager.PlatformEvents;

/// <summary>
/// K2 (STING side) — drains the platform event spine into the Revit model.
/// One <see cref="IExternalEventHandler"/> so every apply runs on the Revit
/// API thread. SignalR (PlanscapeRealtimeClient) calls <see cref="RequestDrain"/>
/// the instant an event arrives; a manual poll command + any periodic
/// scheduler is the resilience fallback.
///
/// Guarantees per event:
///   • idempotent  — applied ids tracked for the session; server only returns
///                   Pending events, so acked events never re-fetch
///   • conflict-safe — revision-sensitive handlers are rejected (not applied)
///                   when the live model has moved past the event's base
///   • atomic      — each event applies under its own transaction; on
///                   reject/failure the transaction rolls back
/// </summary>
public sealed class PlatformEventDrainer : IExternalEventHandler
{
    private static readonly Lazy<PlatformEventDrainer> _lazy = new(() => new PlatformEventDrainer());
    public static PlatformEventDrainer Instance => _lazy.Value;

    private ExternalEvent? _event;
    private static readonly HashSet<Guid> _appliedThisSession = new();

    private PlatformEventDrainer() { }

    /// <summary>Idempotent — create the ExternalEvent once.</summary>
    public static void EnsureRegistered()
    {
        if (Instance._event == null)
            Instance._event = ExternalEvent.Create(Instance);
    }

    /// <summary>Raise a drain pass on the Revit API thread (safe from any thread).</summary>
    public static void RequestDrain()
    {
        EnsureRegistered();
        Instance._event!.Raise();
    }

    /// <summary>Hook for PlanscapeRealtimeClient — call on a "PlatformEvent" socket push.</summary>
    public static void OnRealtimePlatformEvent() => RequestDrain();

    public string GetName() => "STING Platform Event Drainer";

    public void Execute(UIApplication app)
    {
        try
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var client = PlanscapeServerClient.Instance;
            if (!client.IsConnected) return;

            var projectId = client.CurrentProjectId;
            if (projectId == Guid.Empty) return;

            var (ok, events, _) = client.GetPendingEventsAsync(projectId).GetAwaiter().GetResult();
            if (!ok || events.Count == 0) return;

            var currentRev = PlatformEventRegistry.RevisionProvider.GetCurrentRevision(doc);
            int applied = 0, rejected = 0, failed = 0;

            foreach (var ev in events.OrderBy(e => e.Sequence))
            {
                if (_appliedThisSession.Contains(ev.Id)) continue;

                var handler = PlatformEventRegistry.Resolve(ev.Type);
                if (handler == null)
                {
                    client.RejectEventAsync(projectId, ev.Id, $"no handler for type '{ev.Type}'")
                          .GetAwaiter().GetResult();
                    rejected++;
                    continue;
                }

                // Conflict guard — only for revision-sensitive handlers and only
                // when both sides carry a revision (else treat as agnostic).
                if (handler.RevisionSensitive
                    && !string.IsNullOrEmpty(ev.BaseRevisionId)
                    && !string.IsNullOrEmpty(currentRev)
                    && !string.Equals(ev.BaseRevisionId, currentRev, StringComparison.Ordinal))
                {
                    client.RejectEventAsync(projectId, ev.Id,
                        $"stale base revision (event '{ev.BaseRevisionId}' vs model '{currentRev}')")
                          .GetAwaiter().GetResult();
                    rejected++;
                    continue;
                }

                var result = ApplyOne(doc, handler, ev);
                switch (result.Outcome)
                {
                    case PlatformEventOutcome.Applied:
                        _appliedThisSession.Add(ev.Id);
                        client.AckEventAsync(projectId, ev.Id).GetAwaiter().GetResult();
                        applied++;
                        break;
                    case PlatformEventOutcome.Rejected:
                        client.RejectEventAsync(projectId, ev.Id, result.Detail ?? "rejected", retryable: false)
                              .GetAwaiter().GetResult();
                        rejected++;
                        break;
                    default: // Failed — retryable, stays Pending-eligible after server marks Failed
                        client.RejectEventAsync(projectId, ev.Id, result.Detail ?? "failed", retryable: true)
                              .GetAwaiter().GetResult();
                        failed++;
                        break;
                }
            }

            if (applied + rejected + failed > 0)
                StingLog.Info($"PlatformEvent drain: {applied} applied, {rejected} rejected, {failed} failed");
        }
        catch (Exception ex)
        {
            StingLog.Error("PlatformEventDrainer.Execute failed", ex);
        }
    }

    private static PlatformEventApplyResult ApplyOne(Document doc, IApplyPlatformEvent handler, PlatformEventDto ev)
    {
        using var t = new Transaction(doc, $"STING Apply {ev.Type}");
        try
        {
            t.Start();
            var result = handler.Apply(doc, ev) ?? PlatformEventApplyResult.Failed("handler returned null");
            if (result.Outcome == PlatformEventOutcome.Applied) t.Commit();
            else t.RollBack();
            return result;
        }
        catch (Exception ex)
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            StingLog.Error($"PlatformEvent {ev.Id} ({ev.Type}) threw during apply", ex);
            return PlatformEventApplyResult.Failed(ex.Message);
        }
    }
}

/// <summary>
/// Manual trigger for the K2 drainer (poll fallback / "Sync now" button).
/// Tag: <c>PlatformEvents_Drain</c>.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class PlatformEventDrainCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            PlatformEventDrainer.RequestDrain();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            StingLog.Error("PlatformEventDrainCommand failed", ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}
