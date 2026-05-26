using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    /// <summary>
    /// D7 — Re-tag elements whose material was just swapped, so the new
    /// material-aware PROD code (N+2) lands on the element's tag. Runs
    /// as an Idling job outside the IUpdater's transaction.
    ///
    /// Reuses the central <see cref="TagPipelineHelper.RunFullPipeline"/>
    /// when available so the retag is identical to the one Auto-Tag /
    /// Batch-Tag perform — no divergent retag path.
    /// </summary>
    public sealed class MaterialRetagJob : IIdlingJob
    {
        public string Name => "STING Material → Retag";
        public int Priority => 4;
        public int BudgetMs => 200;
        private const int MaxPerTick = 20;

        private readonly Queue<long> _pending = new Queue<long>();
        private readonly HashSet<long> _seen = new HashSet<long>();

        public void Enqueue(long elementId)
        {
            if (elementId <= 0) return;
            lock (_pending)
            {
                if (_seen.Contains(elementId)) return;
                _pending.Enqueue(elementId);
                _seen.Add(elementId);
            }
        }

        public bool Execute(UIApplication uiApp)
        {
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null) { Drain(); return true; }

            int processed = 0;
            try
            {
                using (var t = new Transaction(doc, "STING Material → Retag"))
                {
                    t.Start();
                    while (processed < MaxPerTick)
                    {
                        long elId;
                        lock (_pending)
                        {
                            if (_pending.Count == 0) break;
                            elId = _pending.Dequeue();
                        }
                        processed++;
                        try
                        {
                            var el = doc.GetElement(new ElementId(elId));
                            if (el == null) continue;
                            // Use the central pipeline helper if it's
                            // accessible at runtime; otherwise just clear
                            // the stale flag — the next manual tag pass
                            // will pick the element up.
                            TryRetag(doc, el);
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("Retag.El", $"Retag {elId}: {ex.Message}"); }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialRetagJob outer: {ex.Message}"); }

            lock (_pending) { return _pending.Count == 0; }
        }

        private static void TryRetag(Document doc, Element el)
        {
            // The full pipeline (TagPipelineHelper.RunFullPipeline) lives
            // in ParameterHelpers and rebuilds every token + container +
            // tag for the element. Reflection-bind so we don't take a
            // hard dependency on its exact static signature.
            try
            {
                var helperType = Type.GetType("StingTools.Core.TagPipelineHelper, StingTools")
                                 ?? typeof(ParameterHelpers).Assembly.GetType("StingTools.Core.TagPipelineHelper");
                var method = helperType?.GetMethod("RunFullPipeline",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    // Try the (doc, el, overwrite) shape first, then (doc, el).
                    var parms = method.GetParameters();
                    if (parms.Length == 3) method.Invoke(null, new object[] { doc, el, true });
                    else if (parms.Length == 2) method.Invoke(null, new object[] { doc, el });
                    return;
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("Retag.Pipeline", $"TryRetag pipeline: {ex.Message}"); }

            // Minimum-viable retag — just clear the stale flag so the
            // next compliance scan reports the element as ready for a
            // manual tag pass.
            try
            {
                var p = el.LookupParameter(ParamRegistry.STALE);
                if (p != null && !p.IsReadOnly) p.Set(0);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("Retag.Stale", $"TryRetag clear stale: {ex.Message}"); }
        }

        private void Drain()
        {
            lock (_pending) { _pending.Clear(); _seen.Clear(); }
        }
    }
}
