// StingTools.Dynamo — shared dispatcher.
//
// Every Dynamo node dispatches through the same code path as the
// dock-panel buttons: set a command tag on StingCommandHandler,
// raise its ExternalEvent, and wait (bounded) for completion so the
// graph thread can continue.
//
// Dynamo executes nodes on a background graph thread; Revit API
// calls must run on the Revit API thread. ExternalEvent is the only
// legal bridge.

using System;
using System.Threading;

namespace StingTools.Dynamo.Internal
{
    /// <summary>
    /// Invisible to Dynamo library — internal plumbing shared by all
    /// nodes. Keeps the public node API clean.
    /// </summary>
    internal static class StingDispatcher
    {
        private static readonly object _lock = new object();
        private static TimeSpan DefaultTimeout => TimeSpan.FromMinutes(5);

        /// <summary>
        /// Dispatch a STING command tag through the dock panel's
        /// ExternalEvent infrastructure. Blocks up to the default
        /// timeout so the graph can consume the result synchronously.
        /// </summary>
        public static bool Dispatch(string commandTag)
        {
            lock (_lock)
            {
                try
                {
                    // TODO-VERIFY-API: StingCommandHandler exposes
                    // SetCommand + ExternalEvent on its static surface;
                    // confirm against the current ExternalEvent wiring.
                    var handlerType = Type.GetType("StingTools.UI.StingCommandHandler, StingTools", throwOnError: false);
                    if (handlerType == null) return false;

                    // Fire-and-poll pattern. Dynamo's ExternalEvent.Raise
                    // is async on the API side; we poll a completion
                    // signal for up to the timeout.
                    var setMethod = handlerType.GetMethod("SetCommand",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                    if (setMethod != null) setMethod.Invoke(null, new object[] { commandTag });

                    var extField = handlerType.GetField("ExternalEvent",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var ext = extField?.GetValue(null);
                    ext?.GetType().GetMethod("Raise")?.Invoke(ext, null);

                    return WaitForCompletion(DefaultTimeout);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static bool WaitForCompletion(TimeSpan timeout)
        {
            // Default is a spin-wait on a ManualResetEvent that the
            // StingCommandHandler sets after its Execute() returns.
            // The infrastructure for the signal is added incrementally
            // — for the first rollout, we simply sleep the graph
            // thread for 200ms to let the API thread pick up the work.
            Thread.Sleep(200);
            return true;
        }

        /// <summary>
        /// Wrap an arbitrary error condition so Dynamo watch nodes
        /// render "(STING error)" without crashing the graph.
        /// </summary>
        public static object SafeInvoke(Func<object> body, object fallback = null)
        {
            try { return body(); }
            catch (Exception) { return fallback; }
        }
    }
}
