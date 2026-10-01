// StingTools — Drawing Template Manager · one item of a production run (DTW-195, DTW-204)
//
// Every batch production loop had the same shape — a transaction per item, a commit
// whose status was the only signal — and the same holes: nothing asked whether a
// workshared item could be edited before running it, and a refusal at commit became a
// modal Revit dialog. This is that shape once: pre-flight, run, commit with the failures
// preprocessor attached, and a report line for anything that was not kept.
//
// DTW-204: a large run gave no sign of life inside a step and could not be stopped. With
// a total, the runner shows a progress window (none inside a workflow preset, where the
// engine's own window is up) and the caller asks ShouldStop() between items: Escape, or
// the window's Cancel, stops the run after the item in hand. Committed items are kept —
// the caller still assimilates its group — and StoppedLine says how far it got.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.UI;

namespace StingTools.Core.Drawing
{
    internal sealed class ProductionItemRunner : IDisposable
    {
        internal enum ItemResult { Committed, Skipped, NotKept }

        private readonly Document _doc;
        private readonly string _logTag;
        private readonly int _total;
        private StingProgressDialog _progress;
        private bool _progressTried;

        internal WorksharingPreflight Preflight { get; }
        internal int Committed { get; private set; }
        internal int SkippedCount { get; private set; }
        internal int NotKeptCount { get; private set; }
        /// <summary>Items run (or skipped) so far.</summary>
        internal int Done { get; private set; }
        /// <summary>True once the user has asked the run to stop.</summary>
        internal bool Stopped { get; private set; }

        /// <param name="total">Items the run will attempt; 0 = no progress window.</param>
        internal ProductionItemRunner(Document doc, string logTag, int total = 0)
        {
            _doc = doc;
            _logTag = string.IsNullOrWhiteSpace(logTag) ? "Production" : logTag;
            _total = Math.Max(0, total);
            Preflight = WorksharingPreflight.For(doc);
            // A press latched before the run (e.g. closing the previous dialog) must not stop it.
            EscapeChecker.DrainPendingEscape();
        }

        /// <summary>
        /// DTW-204: true when the run should stop before the next item — Escape, or Cancel
        /// on the progress window. Checked between items, never inside one.
        /// </summary>
        internal bool ShouldStop()
        {
            if (Stopped) return true;
            bool stop;
            try { stop = _progress != null ? _progress.IsCancelled : EscapeChecker.IsEscapePressed(); }
            catch (Exception ex) { StingLog.Warn($"{_logTag} cancel check: {ex.Message}"); stop = false; }
            if (stop)
            {
                Stopped = true;
                StingLog.Info($"{_logTag}: stopped by the user after {Done} of {_total} item(s).");
            }
            return Stopped;
        }

        /// <summary>The report line for a stopped run (null when it was not stopped).</summary>
        internal string StoppedLine(string unit)
            => Stopped ? ProductionRunReport.Stopped(Done, _total, unit) : null;

        /// <summary>
        /// Run one item in its own transaction <paramref name="transactionName"/>.
        /// <paramref name="preflight"/> (optional) returns why the item cannot be edited —
        /// it is then skipped with that reason and nothing is opened. <paramref name="body"/>
        /// does the work; <paramref name="notKept"/> words a commit Revit did not keep.
        /// Every line goes to <paramref name="warnings"/> prefixed with
        /// <paramref name="label"/>. Must be called with no transaction open.
        /// </summary>
        internal ItemResult Run(string transactionName, string label, Func<string> preflight, Action body,
            Func<TransactionStatus, string> notKept, List<string> warnings)
        {
            ShowProgress();
            Status(label);
            try { return RunCore(transactionName, label, preflight, body, notKept, warnings); }
            finally { Done++; Tick(label); }
        }

        private ItemResult RunCore(string transactionName, string label, Func<string> preflight, Action body,
            Func<TransactionStatus, string> notKept, List<string> warnings)
        {
            string block = null;
            try { block = preflight?.Invoke(); }
            catch (Exception ex) { StingLog.Warn($"{_logTag} pre-flight {label}: {ex.Message} — not pre-checked."); }
            if (block != null)
            {
                var line = ProductionRunReport.Skipped(label, block);
                StingLog.Warn($"{_logTag}: {line}");
                warnings?.Add(line);
                SkippedCount++;
                return ItemResult.Skipped;
            }

            using (var t = new Transaction(_doc, transactionName))
            {
                ProductionFailuresPreprocessor pp = null;
                try
                {
                    t.Start();
                    pp = ProductionFailuresPreprocessor.AttachTo(t);
                    body();
                    var status = t.Commit();
                    foreach (var m in pp.Messages) warnings?.Add($"{label}: {m}");
                    if (status == TransactionStatus.Committed) { Committed++; return ItemResult.Committed; }
                    var w = notKept?.Invoke(status) ?? $"{label}: the transaction did not commit ({status}); nothing was kept.";
                    StingLog.Warn($"{_logTag} {w}");
                    warnings?.Add(w);
                    NotKeptCount++;
                    return ItemResult.NotKept;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"{_logTag} {label}: {ex.Message}");
                    if (pp != null) foreach (var m in pp.Messages) warnings?.Add($"{label}: {m}");
                    warnings?.Add($"{label}: {ex.Message} — rolled back.");
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();   // DTW-107
                    NotKeptCount++;
                    return ItemResult.NotKept;
                }
            }
        }

        // ── Progress (DTW-204) ─────────────────────────────────────────────

        private void ShowProgress()
        {
            if (_progressTried) return;
            _progressTried = true;
            // No window inside a preset (the workflow shows its own) or for a run of one.
            if (_total < 2 || PresetDialog.Quiet) return;
            try { _progress = StingProgressDialog.Show(_logTag, _total); }
            catch (Exception ex) { StingLog.Warn($"{_logTag} progress window: {ex.Message} — running without one."); _progress = null; }
        }

        private void Status(string label)
        {
            if (_progress == null) return;
            _progress.SetStatus(label);
            Pump();
        }

        private void Tick(string label)
        {
            if (_progress == null) return;
            _progress.Increment(label);
            Pump();
        }

        /// <summary>
        /// Let the window paint and see its Cancel click. It lives on the Revit thread,
        /// which this loop keeps busy, so queued updates would otherwise wait until the
        /// whole run ended.
        /// </summary>
        private void Pump()
        {
            try
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            }
            catch (Exception ex) { StingLog.Warn($"{_logTag} progress refresh: {ex.Message}"); }
        }

        public void Dispose()
        {
            try { _progress?.Close(); }
            catch (Exception ex) { StingLog.Warn($"{_logTag} progress close: {ex.Message}"); }
            _progress = null;
        }
    }
}
