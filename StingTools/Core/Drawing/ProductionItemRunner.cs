// StingTools — Drawing Template Manager · one item of a production run (DTW-195)
//
// Every batch production loop had the same shape — a transaction per item, a commit
// whose status was the only signal — and the same holes: nothing asked whether a
// workshared item could be edited before running it, and a refusal at commit became a
// modal Revit dialog. This is that shape once: pre-flight, run, commit with the failures
// preprocessor attached, and a report line for anything that was not kept.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    internal sealed class ProductionItemRunner
    {
        internal enum ItemResult { Committed, Skipped, NotKept }

        private readonly Document _doc;
        private readonly string _logTag;

        internal WorksharingPreflight Preflight { get; }
        internal int Committed { get; private set; }
        internal int SkippedCount { get; private set; }
        internal int NotKeptCount { get; private set; }

        internal ProductionItemRunner(Document doc, string logTag)
        {
            _doc = doc;
            _logTag = string.IsNullOrWhiteSpace(logTag) ? "Production" : logTag;
            Preflight = WorksharingPreflight.For(doc);
        }

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
    }
}
