// StingTools — orphan healer (Phase 175)
//
// Finds STING symbol tags whose host element has been deleted and
// optionally removes them along with their associated rating-annotation
// TextNotes.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public sealed class OrphanReport
    {
        public int TotalTags { get; set; }
        public int Orphans { get; set; }
        public int Healed { get; set; }
        public List<ElementId> OrphanIds { get; set; } = new List<ElementId>();
    }

    public static class SymbolOrphanHealer
    {
        public static OrphanReport FindOrphans(Document doc)
        {
            var r = new OrphanReport();
            if (doc == null) return r;
            try
            {
                var tags = new FilteredElementCollector(doc)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .Where(HasSymbolId)
                    .ToList();
                r.TotalTags = tags.Count;

                foreach (var tag in tags)
                {
                    try
                    {
                        bool hasLiveHost = false;
                        // TODO-VERIFY-API: GetTaggedLocalElementIds
                        var ids = tag.GetTaggedLocalElementIds();
                        if (ids != null)
                        {
                            foreach (var id in ids)
                            {
                                if (doc.GetElement(id) != null) { hasLiveHost = true; break; }
                            }
                        }
                        if (!hasLiveHost) r.OrphanIds.Add(tag.Id);
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn($"FindOrphans inner: {ex.Message}");
                    }
                }
                r.Orphans = r.OrphanIds.Count;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"FindOrphans: {ex.Message}");
            }
            return r;
        }

        public static int HealOrphans(Document doc, bool deleteOrphans,
            IProgress<string> progress = null)
        {
            var r = FindOrphans(doc);
            if (!deleteOrphans || r.OrphanIds.Count == 0) return 0;
            int healed = 0;
            try
            {
                using (var tx = new Transaction(doc, "STING Heal Symbol Orphans"))
                {
                    tx.Start();
                    foreach (var id in r.OrphanIds)
                    {
                        try
                        {
                            // Remove associated annotation TextNote first.
                            SymbolAnnotationEngine.RemoveAnnotation(doc, id);
                            doc.Delete(id);
                            healed++;
                            progress?.Report($"Healed {healed}/{r.OrphanIds.Count}");
                        }
                        catch (Exception ex)
                        {
                            StingTools.Core.StingLog.Warn($"HealOrphans delete {id}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("HealOrphans", ex);
            }
            return healed;
        }

        private static bool HasSymbolId(IndependentTag tag)
        {
            try
            {
                var p = tag.LookupParameter("STING_SYMBOL_ID");
                return !string.IsNullOrEmpty(p?.AsString());
            }
            catch { return false; }
        }
    }
}
