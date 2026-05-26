using StingTools.Core;
// StingTools — annotation engine (Phase 175)
//
// Places / updates / removes per-standard rating + circuit annotations
// next to placed symbol tags. The TextNote ID is stamped onto the tag
// via STING_SYMBOL_LABEL_ID so the engine can heal or refresh it later.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public static class SymbolAnnotationEngine
    {
        private const double MmPerFoot = 304.8;
        private static double MmToFt(double mm) => mm / MmPerFoot;

        public static ElementId PlaceAnnotation(Document doc, View view, IndependentTag tag,
            string conceptId, string standardId)
        {
            if (doc == null || view == null || tag == null) return ElementId.InvalidElementId;
            try
            {
                var rules = SymbolStandardRegistry.GetAnnotationRules(standardId);
                Element host = ResolveHost(doc, tag);

                string label = BuildLabel(host, rules);
                if (string.IsNullOrWhiteSpace(label)) return ElementId.InvalidElementId;

                XYZ headPos = SafeHeadPosition(tag, view);
                XYZ textPos = OffsetForLabelPosition(headPos, rules.LabelPosition,
                    MmToFt(rules.TextHeightMm * 1.5));

                ElementId tnTypeId = ResolveTextNoteType(doc, rules.TextHeightMm);
                if (tnTypeId == ElementId.InvalidElementId) return ElementId.InvalidElementId;

                var note = TextNote.Create(doc, view.Id, textPos, label, tnTypeId);
                if (note != null)
                {
                    var p = tag.LookupParameter("STING_SYMBOL_LABEL_ID");
                    if (p != null && !p.IsReadOnly) p.Set(note.Id.Value.ToString());
                    return note.Id;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"PlaceAnnotation {conceptId}/{standardId}: {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }

        public static int UpdateAnnotations(Document doc, View view, string newStandardId)
        {
            if (doc == null || view == null) return 0;
            int count = 0;
            try
            {
                var tags = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .Where(t => HasSymbolId(t))
                    .ToList();

                foreach (var tag in tags)
                {
                    RemoveAnnotation(doc, tag.Id);
                    string cid = tag.LookupParameter("STING_SYMBOL_ID")?.AsString();
                    if (string.IsNullOrEmpty(cid)) continue;
                    PlaceAnnotation(doc, view, tag, cid, newStandardId);
                    count++;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"UpdateAnnotations: {ex.Message}");
            }
            return count;
        }

        public static void RemoveAnnotation(Document doc, ElementId tagId)
        {
            if (doc == null || tagId == null || tagId == ElementId.InvalidElementId) return;
            try
            {
                var tag = doc.GetElement(tagId) as IndependentTag;
                if (tag == null) return;
                var p = tag.LookupParameter("STING_SYMBOL_LABEL_ID");
                string idStr = p?.AsString();
                if (string.IsNullOrEmpty(idStr)) return;
                if (!long.TryParse(idStr, out var rawId)) return;
                var noteId = new ElementId(rawId);
                if (doc.GetElement(noteId) is TextNote note)
                    doc.Delete(note.Id);
                if (!p.IsReadOnly) p.Set("");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"RemoveAnnotation: {ex.Message}");
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static Element ResolveHost(Document doc, IndependentTag tag)
        {
            try
            {
                var p = tag.LookupParameter("STING_HOST_ELEMENT_ID");
                string s = p?.AsString();
                if (!string.IsNullOrEmpty(s) && long.TryParse(s, out var raw))
                    return doc.GetElement(new ElementId(raw));

                // TODO-VERIFY-API: GetTaggedLocalElementIds in Revit 2025.
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
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveHost: {ex.Message}"); }
            return null;
        }

        private static string BuildLabel(Element host, AnnotationRules rules)
        {
            if (host == null) return "";
            // STING-prefixed shared parameters take precedence (Phase 175). The bare names
            // CIRCUIT_REF / RATING / POLES / LABEL remain as fallback so imported families
            // built against other library conventions still render labels — but the project
            // template ships ELC_CIRCUIT_REF_TXT / _RATING_TXT / _POLES_NR / _LABEL_TXT.
            string circuit = ReadCircuitParam(host, "ELC_CIRCUIT_REF_TXT",    "CIRCUIT_REF");
            string rating  = ReadCircuitParam(host, "ELC_CIRCUIT_RATING_TXT", "RATING");
            string poles   = ReadCircuitParam(host, "ELC_CIRCUIT_POLES_NR",   "POLES");
            string label   = ReadCircuitParam(host, "ELC_CIRCUIT_LABEL_TXT",  "LABEL");

            string fmt = (rules.RatingFormat ?? "{rating}{unit}")
                .Replace("{poles}", poles)
                .Replace("{rating}", rating)
                .Replace("{curve}", "")
                .Replace("{unit}", "");

            string circuitFmt = string.IsNullOrEmpty(circuit) ? ""
                : $"{rules.CircuitRefPrefix}{circuit}{rules.CircuitRefSuffix}";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(circuitFmt)) parts.Add(circuitFmt);
            if (!string.IsNullOrWhiteSpace(fmt))        parts.Add(fmt);
            if (!string.IsNullOrWhiteSpace(label))      parts.Add(label);
            return string.Join("\n", parts).Trim();
        }

        private static XYZ SafeHeadPosition(IndependentTag tag, View view)
        {
            try { return tag.TagHeadPosition ?? XYZ.Zero; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return XYZ.Zero; }
        }

        private static XYZ OffsetForLabelPosition(XYZ headPos, string labelPosition, double offsetFt)
        {
            switch ((labelPosition ?? "Above").Trim())
            {
                case "Above": return new XYZ(headPos.X, headPos.Y + offsetFt, headPos.Z);
                case "Below": return new XYZ(headPos.X, headPos.Y - offsetFt, headPos.Z);
                case "Right": return new XYZ(headPos.X + offsetFt, headPos.Y, headPos.Z);
                case "Left":  return new XYZ(headPos.X - offsetFt, headPos.Y, headPos.Z);
                default:      return headPos.Add(new XYZ(0, offsetFt, 0));
            }
        }

        // Cache resolved TextNoteType per (doc, height-in-mm) so the
        // height-match scan + duplicate fallback only runs once per
        // standard switch. Cleared by InvalidateAnnotationCache below.
        private static readonly Dictionary<string, ElementId> _textTypeCache
            = new Dictionary<string, ElementId>(StringComparer.Ordinal);

        public static void InvalidateAnnotationCache()
        {
            lock (_textTypeCache) _textTypeCache.Clear();
        }

        /// <summary>
        /// Resolve a <see cref="TextNoteType"/> whose text size matches
        /// <paramref name="textHeightMm"/>. Order:
        ///   1. Cached value for (doc, height).
        ///   2. Any existing TextNoteType whose <c>TEXT_SIZE</c>
        ///      parameter is within 0.05 mm of the request.
        ///   3. Duplicate of the doc's first TextNoteType, named
        ///      <c>STING_Symbol_<height>mm</c>, with TEXT_SIZE set.
        ///   4. Fall back to the first TextNoteType (legacy behaviour)
        ///      so callers always get something usable.
        /// </summary>
        private static ElementId ResolveTextNoteType(Document doc, double textHeightMm)
        {
            string key = (doc?.PathName ?? doc?.Title ?? "") + "::" + textHeightMm.ToString("F2");
            lock (_textTypeCache)
            {
                if (_textTypeCache.TryGetValue(key, out var cached) && cached != ElementId.InvalidElementId
                    && doc.GetElement(cached) is TextNoteType) return cached;
            }
            try
            {
                double targetFt = MmToFt(textHeightMm);
                const double tolMm = 0.05;
                double tolFt = MmToFt(tolMm);

                // 2. Existing match.
                var allTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();
                foreach (var t in allTypes)
                {
                    var p = t.get_Parameter(BuiltInParameter.TEXT_SIZE);
                    if (p == null) continue;
                    if (Math.Abs(p.AsDouble() - targetFt) <= tolFt)
                    {
                        lock (_textTypeCache) _textTypeCache[key] = t.Id;
                        return t.Id;
                    }
                }

                // 3. Duplicate the first available type at the requested size.
                var seed = allTypes.FirstOrDefault();
                if (seed != null)
                {
                    string newName = $"STING_Symbol_{textHeightMm:F1}mm";
                    var existing = allTypes.FirstOrDefault(t => string.Equals(t.Name, newName, StringComparison.Ordinal));
                    if (existing != null)
                    {
                        lock (_textTypeCache) _textTypeCache[key] = existing.Id;
                        return existing.Id;
                    }
                    try
                    {
                        if (seed.Duplicate(newName) is TextNoteType dup)
                        {
                            var sizeParam = dup.get_Parameter(BuiltInParameter.TEXT_SIZE);
                            if (sizeParam != null && !sizeParam.IsReadOnly) sizeParam.Set(targetFt);
                            lock (_textTypeCache) _textTypeCache[key] = dup.Id;
                            return dup.Id;
                        }
                    }
                    catch (Exception dupEx) { StingTools.Core.StingLog.Warn($"ResolveTextNoteType duplicate: {dupEx.Message}"); }
                }

                // 4. Last-resort fallback.
                var first = allTypes.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
                lock (_textTypeCache) _textTypeCache[key] = first;
                return first;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ResolveTextNoteType: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static bool HasSymbolId(IndependentTag tag)
        {
            try
            {
                var p = tag.LookupParameter("STING_SYMBOL_ID");
                return !string.IsNullOrEmpty(p?.AsString());
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Phase 175 — read a circuit parameter, preferring the STING-prefixed
        /// shared parameter (defined in MR_PARAMETERS group ELC_PWR) and falling
        /// back to the bare-name parameter that imported third-party families
        /// commonly carry. Handles String + Integer storage so ELC_CIRCUIT_POLES_NR
        /// rendered as INTEGER still flows into the SLD label correctly.
        /// </summary>
        private static string ReadCircuitParam(Element host, string preferredName, string fallbackName)
        {
            if (host == null) return "";
            try
            {
                var p = host.LookupParameter(preferredName);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.String)  return p.AsString() ?? "";
                    if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                    if (p.StorageType == StorageType.Double)  return p.AsValueString() ?? p.AsDouble().ToString("0.##");
                }
                var f = host.LookupParameter(fallbackName);
                if (f != null && f.HasValue)
                {
                    if (f.StorageType == StorageType.String)  return f.AsString() ?? "";
                    if (f.StorageType == StorageType.Integer) return f.AsInteger().ToString();
                    if (f.StorageType == StorageType.Double)  return f.AsValueString() ?? f.AsDouble().ToString("0.##");
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ReadCircuitParam {preferredName}/{fallbackName}: {ex.Message}"); }
            return "";
        }

        /// <summary>
        /// Phase 175 — read a circuit parameter, preferring the STING-prefixed
        /// shared parameter (defined in MR_PARAMETERS group ELC_PWR) and falling
        /// back to the bare-name parameter that imported third-party families
        /// commonly carry. Handles String + Integer storage so ELC_CIRCUIT_POLES_NR
        /// rendered as INTEGER still flows into the SLD label correctly.
        /// </summary>
        private static string ReadCircuitParam(Element host, string preferredName, string fallbackName)
        {
            if (host == null) return "";
            try
            {
                var p = host.LookupParameter(preferredName);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.String)  return p.AsString() ?? "";
                    if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                    if (p.StorageType == StorageType.Double)  return p.AsValueString() ?? p.AsDouble().ToString("0.##");
                }
                var f = host.LookupParameter(fallbackName);
                if (f != null && f.HasValue)
                {
                    if (f.StorageType == StorageType.String)  return f.AsString() ?? "";
                    if (f.StorageType == StorageType.Integer) return f.AsInteger().ToString();
                    if (f.StorageType == StorageType.Double)  return f.AsValueString() ?? f.AsDouble().ToString("0.##");
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ReadCircuitParam {preferredName}/{fallbackName}: {ex.Message}"); }
            return "";
        }
    }
}
