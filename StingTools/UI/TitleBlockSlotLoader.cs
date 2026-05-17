// StingTools — Drawing Template Manager · Phase 137 enhancement
//
// TitleBlockSlotLoader reads viewport-slot definitions from STING-aware
// title-block families. Title blocks loaded into the project that carry
// the shared parameter TB_VIEWPORT_SLOTS_JSON_TXT expose a JSON array
// of {label, normX, normY, normW, normH, viewType, scale} describing
// every slot the title block reserves for a viewport. We harvest the
// labels so the DrawingType editor's slot rows can present a dropdown
// of real slot names instead of leaving the user to type free text.
//
// Fallback: when no title block carries the JSON, we return a list of
// canonical slot names ("Main Plan", "Key Plan", etc.) so the dropdown
// is still useful.

using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
namespace StingTools.UI
{
    public sealed class TitleBlockSlot
    {
        public string TitleBlockFamily { get; set; }
        public string Label { get; set; }
        public double NormX { get; set; }
        public double NormY { get; set; }
        public double NormW { get; set; }
        public double NormH { get; set; }
        public string ViewType { get; set; }
        public int? Scale { get; set; }
    }

    public static class TitleBlockSlotLoader
    {
        private const string SlotJsonParam = "TB_VIEWPORT_SLOTS_JSON_TXT";

        // Per-document cache so editor re-renders don't re-collect FamilySymbols
        // every time GetLabels()/FindByLabel() runs. Keyed weakly on the
        // Document so closing the document drops the entry naturally.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Document, List<TitleBlockSlot>> _cache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<Document, List<TitleBlockSlot>>();

        /// <summary>
        /// Drop the cached slot list for <paramref name="doc"/>. Call after
        /// editing the title-block JSON or reloading the registry so the next
        /// query refreshes from disk.
        /// </summary>
        public static void InvalidateCache(Document doc)
        {
            if (doc == null) return;
            try { _cache.Remove(doc); } catch { /* defensive */ }
        }

        /// <summary>
        /// 22 default slot labels. Used when no STING-aware title
        /// block declares a slot list, so the dropdown is never empty.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultSlotLabels = new List<string>
        {
            "Main Plan", "Key Plan", "Notes", "Schedule", "Title",
            "Section A-A", "Section B-B", "Section C-C",
            "Detail 1", "Detail 2", "Detail 3",
            "Elevation N", "Elevation S", "Elevation E", "Elevation W",
            "3D Axonometric", "3D Perspective", "Render", "Legend",
            "Revision Schedule", "Sheet Index", "Sheet Notes"
        };

        /// <summary>
        /// Read every loaded title-block FamilySymbol's slot JSON and
        /// return a flattened list of TitleBlockSlot rows. Returns an
        /// empty list (not null) when the document is null or no title
        /// block carries the parameter.
        /// </summary>
        public static List<TitleBlockSlot> ReadAll(Document doc)
        {
            if (doc == null) return new List<TitleBlockSlot>();
            if (_cache.TryGetValue(doc, out var cached)) return cached;
            var result = new List<TitleBlockSlot>();
            try
            {
                var symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .ToList();
                foreach (var sym in symbols)
                {
                    var p = sym.LookupParameter(SlotJsonParam);
                    var json = p?.AsString();
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    try
                    {
                        var arr = JArray.Parse(json);
                        foreach (var tok in arr)
                        {
                            result.Add(new TitleBlockSlot
                            {
                                TitleBlockFamily = sym.FamilyName,
                                Label    = tok.Value<string>("label") ?? "",
                                NormX    = tok.Value<double?>("normX") ?? 0,
                                NormY    = tok.Value<double?>("normY") ?? 0,
                                NormW    = tok.Value<double?>("normW") ?? 0,
                                NormH    = tok.Value<double?>("normH") ?? 0,
                                ViewType = tok.Value<string>("viewType") ?? "",
                                Scale    = tok.Value<int?>("scale")
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn(
                            $"TitleBlockSlotLoader: malformed JSON on '{sym.FamilyName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"TitleBlockSlotLoader.ReadAll: {ex.Message}");
            }
            try { _cache.Add(doc, result); } catch { /* concurrent add — fine */ }
            return result;
        }

        /// <summary>
        /// Return distinct slot labels, optionally filtered to a single
        /// title-block family name. Always returns at least the
        /// <see cref="DefaultSlotLabels"/> catalogue so the dropdown is
        /// never empty, with project labels listed first.
        /// </summary>
        public static List<string> GetLabels(Document doc, string titleBlockFamily = null)
        {
            var slots = ReadAll(doc);
            if (!string.IsNullOrEmpty(titleBlockFamily))
                slots = slots
                    .Where(s => string.Equals(s.TitleBlockFamily, titleBlockFamily, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            var fromProject = slots
                .Select(s => s.Label)
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var combined = new List<string>(fromProject);
            foreach (var d in DefaultSlotLabels)
                if (!combined.Contains(d, StringComparer.OrdinalIgnoreCase))
                    combined.Add(d);
            return combined;
        }

        /// <summary>
        /// Find a slot by label across all loaded title blocks. Returns
        /// the first match — useful when the editor wants to auto-fill
        /// normX/Y/W/H once a label is picked.
        /// </summary>
        public static TitleBlockSlot FindByLabel(Document doc, string label, string titleBlockFamily = null)
        {
            if (string.IsNullOrEmpty(label)) return null;
            return ReadAll(doc).FirstOrDefault(s =>
                string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(titleBlockFamily) ||
                 string.Equals(s.TitleBlockFamily, titleBlockFamily, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
