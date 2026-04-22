// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/TransactionHelper.cs — S1.5 (N-G2).
//
// Shared TransactionGroup + Transaction scope utility for v6 engines.
//
// The Revit API best practice is: one Transaction wrapping a batch of
// related changes, grouped under a TransactionGroup that can be rolled
// back atomically if any part of the batch fails. Most STING v1-v5
// commands wrap each element modification in its own Transaction which
// is both slow (per-Transaction overhead, view regeneration hits) and
// non-atomic (a mid-batch failure leaves the model in a half-modified
// state the user cannot easily undo).
//
// All v6 engines (Placement Phase 2, Routing Phase 3, Validation Phase
// 4, Fabrication Phase 5, v6 gap engines Phase 6) call
// TransactionHelper.RunInScope so that (a) the whole batch undoes in
// one Ctrl+Z step, (b) a mid-batch exception triggers
// TransactionGroup.RollBack(), (c) transaction naming is consistent.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Transaction scope helpers for v6 engines.
    /// </summary>
    public static class TransactionHelper
    {
        /// <summary>
        /// Wrap <paramref name="action"/> in a <see cref="Transaction"/>
        /// inside a <see cref="TransactionGroup"/>. Commits both on
        /// success; rolls back both on any thrown exception and
        /// re-throws so the caller can log and surface to the user.
        /// </summary>
        public static void RunInScope(Document doc, string name, Action<Transaction> action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrWhiteSpace(name)) name = "STING v6 operation";

            using (var tg = new TransactionGroup(doc, name))
            {
                tg.Start();
                try
                {
                    using (var t = new Transaction(doc, name))
                    {
                        t.Start();
                        try
                        {
                            action(t);
                            t.Commit();
                        }
                        catch
                        {
                            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                            throw;
                        }
                    }
                    tg.Assimilate();
                }
                catch
                {
                    if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
                    throw;
                }
            }
        }

        /// <summary>
        /// Non-throwing variant. Returns true if the batch committed,
        /// false if it rolled back. Exception written to <see cref="StingLog"/>.
        /// Use when the caller wants to report the failure via
        /// WarningsManager rather than propagate up.
        /// </summary>
        public static bool TryRunInScope(Document doc, string name, Action<Transaction> action)
        {
            try
            {
                RunInScope(doc, name, action);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error($"TransactionHelper.TryRunInScope failed: {name}", ex);
                return false;
            }
        }

        /// <summary>
        /// Run an already-open transaction-free action that needs to be
        /// wrapped in a single Transaction (no group). Prefer
        /// <see cref="RunInScope"/> when the batch modifies more than
        /// one element. Useful for single-element workflows that still
        /// need transaction safety.
        /// </summary>
        public static void RunInSingleTransaction(Document doc, string name, Action<Transaction> action)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (string.IsNullOrWhiteSpace(name)) name = "STING v6 operation";

            using (var t = new Transaction(doc, name))
            {
                t.Start();
                try
                {
                    action(t);
                    t.Commit();
                }
                catch
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    throw;
                }
            }
        }
    }
}
