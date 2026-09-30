// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccAutoImportGate.cs — the debounce behind auto-import on the server's
// "acc.issue.changed" signal.
//
// ACC fires one webhook per issue change, and a coordinator closing twenty issues in a row
// produces twenty signals in a minute. Each import is a network read plus a register write,
// so the signals are coalesced: at most one import per MinInterval. A signal that arrives
// inside the interval is NOT dropped - it schedules one trailing run at the end of the
// interval, because the change it announces happened after the run that is suppressing it.
//
// Pure and thread-safe (signals arrive on the SignalR callback thread). Revit-free.

using System;

namespace StingTools.V6
{
    public enum AccAutoImportDecision
    {
        /// <summary>Queue an import now.</summary>
        RunNow,
        /// <summary>Too soon: schedule one run after the returned delay.</summary>
        ScheduleTrailing,
        /// <summary>A trailing run is already scheduled and will cover this signal.</summary>
        AlreadyScheduled,
    }

    public sealed class AccAutoImportGate
    {
        public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

        private readonly object _gate = new object();
        private DateTime? _lastRunUtc;
        private bool _trailingScheduled;

        /// <summary>Decide what a signal at <paramref name="nowUtc"/> should do, and record it.</summary>
        public AccAutoImportDecision OnSignal(DateTime nowUtc, out TimeSpan delay)
        {
            lock (_gate)
            {
                delay = TimeSpan.Zero;
                if (_trailingScheduled) return AccAutoImportDecision.AlreadyScheduled;
                if (!_lastRunUtc.HasValue || nowUtc - _lastRunUtc.Value >= MinInterval)
                {
                    _lastRunUtc = nowUtc;
                    return AccAutoImportDecision.RunNow;
                }
                delay = _lastRunUtc.Value + MinInterval - nowUtc;
                _trailingScheduled = true;
                return AccAutoImportDecision.ScheduleTrailing;
            }
        }

        /// <summary>The trailing run is firing now.</summary>
        public void OnTrailingFired(DateTime nowUtc)
        {
            lock (_gate)
            {
                _trailingScheduled = false;
                _lastRunUtc = nowUtc;
            }
        }
    }
}
