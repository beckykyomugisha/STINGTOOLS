// StingTools — Drawing Template Manager · failures at commit become item warnings (DTW-195)
//
// Without a preprocessor every failure posted while committing a production item opened
// a Revit dialog: one per item, modal, and inside a workflow preset a dialog nobody was
// there to answer. Attached to each item's transaction, this turns them into report
// lines instead:
//
//   * a worksharing refusal (owned by someone else, out of date, central busy) rolls the
//     ITEM back quietly — the rest of the run continues — and says who holds what;
//   * any other error also rolls the item back quietly, with Revit's own text;
//   * a warning is deleted from the commit and kept as an item warning, so it reaches
//     the result report instead of a dialog after the run.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    internal sealed class ProductionFailuresPreprocessor : IFailuresPreprocessor
    {
        /// <summary>Lines for the item's report, in the order Revit posted them.</summary>
        internal List<string> Messages { get; } = new List<string>();

        /// <summary>True when this preprocessor rolled the transaction back.</summary>
        internal bool RolledBack { get; private set; }

        /// <summary>True when the rollback was a worksharing refusal.</summary>
        internal bool Worksharing { get; private set; }

        private static HashSet<Guid> _worksharing;

        private static HashSet<Guid> WorksharingFailures()
        {
            if (_worksharing != null) return _worksharing;
            var set = new HashSet<Guid>();
            void Add(Func<FailureDefinitionId> get)
            {
                try { var id = get(); if (id != null) set.Add(id.Guid); }
                catch (Exception ex) { StingLog.Warn($"ProductionFailuresPreprocessor id: {ex.Message}"); }
            }
            Add(() => BuiltInFailures.EditingFailures.OwnedByOther);
            Add(() => BuiltInFailures.EditingFailures.CannotEditElements);
            Add(() => BuiltInFailures.EditingFailures.OutOfDateElements);
            Add(() => BuiltInFailures.EditingFailures.OwnElementsOutOfDate);
            Add(() => BuiltInFailures.EditingFailures.CannotBorrowBusyContention);
            Add(() => BuiltInFailures.EditingFailures.ElementNotEditable);
            Add(() => BuiltInFailures.EditingFailures.HaveNoPermissionToEdit);
            Add(() => BuiltInFailures.EditingFailures.ReloadBeforeMakeEditable);
            Add(() => BuiltInFailures.EditingFailures.CannotCheckoutWorksets);
            Add(() => BuiltInFailures.EditingFailures.TooManyElementsToCheckout);
            Add(() => BuiltInFailures.EditingFailures.CannotEditDeletedElements);
            Add(() => BuiltInFailures.EditingFailures.CannotEditDeletedWorkset);
            Add(() => BuiltInFailures.EditingFailures.WorksetHasChangedInCentralFile);
            Add(() => BuiltInFailures.EditingFailures.CannotEditEditingElements);
            return _worksharing = set;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            bool rollBack = false;
            try
            {
                var doc = fa.GetDocument();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in fa.GetFailureMessages())
                {
                    string text;
                    try { text = m.GetDescriptionText() ?? "Revit failure"; }
                    catch (Exception ex) { text = "Revit failure (" + ex.Message + ")"; }
                    var id = m.GetFailureDefinitionId();
                    if (id != null && WorksharingFailures().Contains(id.Guid))
                    {
                        rollBack = true; Worksharing = true;
                        Note($"not kept — {text}{Holders(doc, m)} Reload latest, or ask the owner to relinquish, then re-run.", seen);
                    }
                    else if (m.GetSeverity() == FailureSeverity.Warning)
                    {
                        Note("Revit warning: " + text, seen);
                        try { fa.DeleteWarning(m); }
                        catch (Exception ex) { StingLog.Warn($"ProductionFailuresPreprocessor delete warning: {ex.Message}"); }
                    }
                    else
                    {
                        rollBack = true;
                        Note("not kept — Revit error: " + text, seen);
                    }
                }
            }
            catch (Exception ex)
            {
                // Do not guess: leave the failures to Revit's own handling.
                StingLog.Warn($"ProductionFailuresPreprocessor: {ex.Message}");
                return FailureProcessingResult.Continue;
            }
            if (!rollBack) return FailureProcessingResult.Continue;
            RolledBack = true;
            try
            {
                var o = fa.GetFailureHandlingOptions();
                o.SetClearAfterRollback(true);
                fa.SetFailureHandlingOptions(o);
            }
            catch (Exception ex) { StingLog.Warn($"ProductionFailuresPreprocessor options: {ex.Message}"); }
            return FailureProcessingResult.ProceedWithRollBack;
        }

        private void Note(string line, HashSet<string> seen)
        {
            if (seen.Add(line)) Messages.Add(line);
        }

        /// <summary>" ('E-101' held by jsmith)." — who holds the failing elements, when Revit says.</summary>
        private static string Holders(Document doc, FailureMessageAccessor m)
        {
            var parts = new List<string>();
            try
            {
                foreach (var id in m.GetFailingElementIds().Take(4))
                {
                    string name = null, owner = null;
                    try
                    {
                        var e = doc?.GetElement(id);
                        name = e is ViewSheet s ? s.SheetNumber : e?.Name;
                        if (doc != null && doc.IsWorkshared)
                            owner = WorksharingUtils.GetWorksharingTooltipInfo(doc, id)?.Owner;
                    }
                    catch (Exception ex) { StingLog.Warn($"ProductionFailuresPreprocessor holder {id}: {ex.Message}"); }
                    parts.Add($"'{name ?? id.Value.ToString()}'" + (string.IsNullOrWhiteSpace(owner) ? "" : " held by " + owner));
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProductionFailuresPreprocessor holders: {ex.Message}"); }
            return parts.Count == 0 ? "." : " (" + string.Join(", ", parts) + ").";
        }

        /// <summary>Attach a fresh preprocessor to <paramref name="t"/> (started, not ended).</summary>
        internal static ProductionFailuresPreprocessor AttachTo(Transaction t)
        {
            var pp = new ProductionFailuresPreprocessor();
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(pp);
            o.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(o);
            return pp;
        }
    }
}
