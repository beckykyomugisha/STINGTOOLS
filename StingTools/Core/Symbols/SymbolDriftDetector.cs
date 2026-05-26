using StingTools.Core;
// StingTools — drift detector (Phase 175)
//
// Compares each STING symbol tag's stamped standard against the standard
// the resolver currently chooses for the tag's host. Mismatches are
// flagged so SwapTagToStandard can re-mint the tag against the correct
// family.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public sealed class DriftReport
    {
        public int TotalSymbols { get; set; }
        public int DriftedSymbols { get; set; }
        public List<DriftInstance> Drifted { get; set; } = new List<DriftInstance>();
    }

    public sealed class DriftInstance
    {
        public ElementId TagId { get; set; }
        public string ConceptId { get; set; }
        public string ActualStandard { get; set; }
        public string ExpectedStandard { get; set; }
        /// <summary>STANDARD | FAMILY_MISMATCH | COMPOUND_INCONSISTENT</summary>
        public string DriftType { get; set; }
        /// <summary>For COMPOUND_INCONSISTENT: parent compound concept id.</summary>
        public string CompoundParentId { get; set; }
    }

    public static class SymbolDriftDetector
    {
        public static DriftReport DetectDrift(Document doc, View view = null)
        {
            var report = new DriftReport();
            if (doc == null) return report;

            try
            {
                FilteredElementCollector col = view != null
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

                var tags = col.OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .Where(HasSymbolId)
                    .ToList();
                report.TotalSymbols = tags.Count;

                foreach (var tag in tags)
                {
                    try
                    {
                        string conceptId = tag.LookupParameter("STING_SYMBOL_ID")?.AsString();
                        string actualStd = tag.LookupParameter("STING_SYMBOL_STANDARD")?.AsString();
                        if (string.IsNullOrEmpty(conceptId) || string.IsNullOrEmpty(actualStd))
                            continue;

                        Element host = ResolveHost(doc, tag);
                        View tagView = doc.GetElement(tag.OwnerViewId) as View;
                        string expectedStd = SymbolStandardResolver.ResolveStandard(doc, tagView, host);

                        if (!string.Equals(actualStd, expectedStd, StringComparison.OrdinalIgnoreCase))
                        {
                            report.Drifted.Add(new DriftInstance
                            {
                                TagId = tag.Id,
                                ConceptId = conceptId,
                                ActualStandard = actualStd,
                                ExpectedStandard = expectedStd,
                                DriftType = "STANDARD"
                            });
                            continue;
                        }

                        // FAMILY_MISMATCH: stamped standard agrees, but the tag type doesn't.
                        string viewCtx = SymbolViewContextResolver.ToKey(
                            SymbolViewContextResolver.Resolve(tagView));
                        string scaleTier = SymbolScaleEngine.GetScaleTier(tagView);
                        string expectedFamily = SymbolConceptRegistry.GetFamilyName(
                            conceptId, expectedStd, viewCtx, scaleTier, null);
                        string currentFamily = (doc.GetElement(tag.GetTypeId()) as FamilySymbol)?.FamilyName;

                        if (!string.IsNullOrEmpty(expectedFamily) && !string.IsNullOrEmpty(currentFamily)
                            && !string.Equals(currentFamily, expectedFamily, StringComparison.OrdinalIgnoreCase))
                        {
                            report.Drifted.Add(new DriftInstance
                            {
                                TagId = tag.Id,
                                ConceptId = conceptId,
                                ActualStandard = actualStd,
                                ExpectedStandard = expectedStd,
                                DriftType = "FAMILY_MISMATCH"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn($"DetectDrift inner: {ex.Message}");
                    }
                }
                // Compound-parent consistency: every child instance of a
                // compound (stamped via STING_COMPOUND_PARENT_ID) should
                // carry the same standard as its siblings. Mismatches
                // mean a compound was half-swapped.
                AppendCompoundDrift(doc, view, report);

                report.DriftedSymbols = report.Drifted.Count;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"DetectDrift: {ex.Message}");
            }
            return report;
        }

        private static void AppendCompoundDrift(Document doc, View view, DriftReport report)
        {
            try
            {
                FilteredElementCollector col = view != null
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

                // Group every compound child by parent id.
                var byParent = new Dictionary<string, List<FamilyInstance>>(StringComparer.OrdinalIgnoreCase);
                foreach (FamilyInstance fi in col
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>())
                {
                    string parentId = fi.LookupParameter("STING_COMPOUND_PARENT_ID")?.AsString();
                    if (string.IsNullOrEmpty(parentId)) continue;
                    if (!byParent.TryGetValue(parentId, out var bucket))
                        byParent[parentId] = bucket = new List<FamilyInstance>();
                    bucket.Add(fi);
                }

                foreach (var kv in byParent)
                {
                    var standards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var inst in kv.Value)
                    {
                        var s = inst.LookupParameter("STING_SYMBOL_STANDARD")?.AsString();
                        if (!string.IsNullOrEmpty(s)) standards.Add(s);
                    }
                    if (standards.Count <= 1) continue;
                    // Standards diverge across siblings — flag every child.
                    string winner = standards.OrderByDescending(s =>
                        kv.Value.Count(i => string.Equals(
                            i.LookupParameter("STING_SYMBOL_STANDARD")?.AsString(),
                            s, StringComparison.OrdinalIgnoreCase))).First();
                    foreach (var inst in kv.Value)
                    {
                        var s = inst.LookupParameter("STING_SYMBOL_STANDARD")?.AsString() ?? "";
                        if (!string.Equals(s, winner, StringComparison.OrdinalIgnoreCase))
                        {
                            report.Drifted.Add(new DriftInstance
                            {
                                TagId = inst.Id,
                                ConceptId = inst.LookupParameter("STING_SYMBOL_ID")?.AsString(),
                                ActualStandard = s,
                                ExpectedStandard = winner,
                                DriftType = "COMPOUND_INCONSISTENT",
                                CompoundParentId = kv.Key,
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendCompoundDrift: {ex.Message}");
            }
        }

        private static Element ResolveHost(Document doc, IndependentTag tag)
        {
            try
            {
                var p = tag.LookupParameter("STING_HOST_ELEMENT_ID");
                string s = p?.AsString();
                if (!string.IsNullOrEmpty(s) && long.TryParse(s, out var raw))
                    return doc.GetElement(new ElementId(raw));
                var ids = tag.GetTaggedLocalElementIds();
                if (ids != null)
                {
                    foreach (var id in ids)
                    {
                        var el = doc.GetElement(id);
                        if (el != null) return el;
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DriftDetector ResolveHost: {ex.Message}"); }
            return null;
        }

        private static bool HasSymbolId(IndependentTag tag)
        {
            try { return !string.IsNullOrEmpty(tag.LookupParameter("STING_SYMBOL_ID")?.AsString()); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
