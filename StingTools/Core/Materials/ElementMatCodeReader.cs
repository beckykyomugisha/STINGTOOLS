// ══════════════════════════════════════════════════════════════════════════
//  ElementMatCodeReader.cs — the ONE place that answers "what is this element's
//  MAT_CODE?". Four call sites asked a wall for a parameter bound to materials;
//  they now all come here.
//
//  This file reads Revit and decides nothing. The decision — which material
//  answers, and in what order — is MaterialCodeResolution, which is Revit-free
//  and tested against a plain layer list and a name→code map.
//
//  ── THE PER-DOCUMENT MAP ──────────────────────────────────────────────────
//  MAT_CODE lives on Material elements, so answering per element would mean a
//  parameter read per layer per element. Instead the document's materials are
//  read ONCE into a name→code map.
//
//  It is memoised on (document, material count, 30 seconds) — the same stale
//  window ComplianceScan uses. The count alone is NOT enough: stamping codes
//  onto existing materials changes no count, so a count-only memo would keep
//  serving a map with no codes in it straight after the command that wrote
//  them, and the BOQ would price off category exactly as before with nothing
//  said. A TTL bounds that to half a minute without needing a Revit hook that
//  does not exist, and Invalidate() closes it exactly for callers that write.
//
//  The map also carries HOW MANY materials have a code, which is the number
//  that says whether any of this can work at all. A model where that is 0 is
//  not a model where Pass 3 "found nothing"; it is a model where nobody has run
//  Materials_StampCodes, and the two are worth telling apart.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Materials
{
    public static class ElementMatCodeReader
    {
        private static readonly object _lock = new object();
        private static string _docKey;
        private static int _materialCount = -1;
        private static DateTime _builtUtc = DateTime.MinValue;
        private static Dictionary<string, string> _codeByName;

        /// <summary>How long a built map is trusted. Same window ComplianceScan uses.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

        /// <summary>Materials in the cached map that actually carry a MAT_CODE.</summary>
        public static int CodedMaterialCount { get; private set; }

        /// <summary>Drop the cached map. Call after writing MAT_CODE to materials.</summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _docKey = null; _materialCount = -1; _codeByName = null;
                _builtUtc = DateTime.MinValue; CodedMaterialCount = 0;
            }
        }

        /// <summary>
        /// The element's MAT_CODE, resolved through its material. Never null; an
        /// unresolved element answers <see cref="MatCodeResolution.Nothing"/>, whose
        /// Code is "" — and empty must stay empty rather than become a guess.
        /// </summary>
        public static MatCodeResolution Resolve(Element el)
        {
            if (el == null) return MatCodeResolution.Nothing;
            try
            {
                var doc = el.Document;
                if (doc == null) return MatCodeResolution.Nothing;

                return MaterialCodeResolution.Resolve(
                    ReadLayers(el, doc),
                    CodeByMaterialName(doc),
                    ReadSingleMaterialName(el, doc),
                    ParameterHelpers.GetString(el, "MAT_CODE") ?? "");
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MatCode.Resolve", "Resolve " + el?.Id + ": " + ex.Message);
                return MatCodeResolution.Nothing;
            }
        }

        /// <summary>The code alone, for the call sites that only want the string.</summary>
        public static string ResolveCode(Element el) => Resolve(el).Code;

        // ══════════════════════════════════════════════════════════════════════
        //  Revit reads
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>The element type's compound structure, flattened into the shape
        /// PrimaryMaterialSelector takes. Empty for anything that is not a layered host.
        /// Deliberately the same read MaterialProdOverrideRegistry does — one shape, so
        /// the PROD suffix and the rate key cannot disagree about which layer is the core.</summary>
        private static IReadOnlyList<MaterialLayer> ReadLayers(Element el, Document doc)
        {
            var layers = new List<MaterialLayer>();
            try
            {
                if (!(doc.GetElement(el.GetTypeId()) is HostObjAttributes host)) return layers;
                CompoundStructure cs = host.GetCompoundStructure();
                if (cs == null) return layers;

                var csLayers = cs.GetLayers();
                for (int i = 0; i < csLayers.Count; i++)
                {
                    var cl = csLayers[i];
                    string name = null;
                    if (cl.MaterialId != null && cl.MaterialId.Value > 0)
                        name = doc.GetElement(cl.MaterialId)?.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    layers.Add(new MaterialLayer
                    {
                        Index = i,
                        MaterialName = name,
                        // Width is FEET. Millimetres because every other STING layer
                        // reader uses them and the selector only compares layers to
                        // each other.
                        ThicknessMm = cl.Width * 304.8,
                        IsStructure = cl.Function == MaterialFunctionAssignment.Structure,
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MatCode.Layers", "ReadLayers " + el?.Id + ": " + ex.Message);
            }
            return layers;
        }

        /// <summary>The element's own material, for things with no compound structure —
        /// a FamilyInstance, a pipe. Explicit Material parameter first, then the first
        /// material the element admits to.</summary>
        private static string ReadSingleMaterialName(Element el, Document doc)
        {
            try
            {
#pragma warning disable CS0618
                Parameter p = el.LookupParameter("Material")
                              ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
#pragma warning restore CS0618
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0) return doc.GetElement(mid)?.Name;
                }

                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var mid in mats)
                        if (mid != null && mid.Value > 0) return doc.GetElement(mid)?.Name;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MatCode.Single", "ReadSingleMaterial " + el?.Id + ": " + ex.Message);
            }
            return null;
        }

        /// <summary>MAT_CODE per material name for the whole document, memoised.</summary>
        internal static IReadOnlyDictionary<string, string> CodeByMaterialName(Document doc)
        {
            if (doc == null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string key = doc.PathName ?? doc.Title ?? "";
            int count;
            try { count = new FilteredElementCollector(doc).OfClass(typeof(Material)).GetElementCount(); }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("MatCode.Count", "material count: " + ex.Message);
                count = -1;
            }

            lock (_lock)
            {
                if (_codeByName != null && _docKey == key && _materialCount == count
                    && DateTime.UtcNow - _builtUtc < StaleAfter)
                    return _codeByName;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int coded = 0;
            try
            {
                foreach (Material m in new FilteredElementCollector(doc)
                                       .OfClass(typeof(Material)).Cast<Material>())
                {
                    string name = m?.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string code = (ParameterHelpers.GetString(m, "MAT_CODE") ?? "").Trim();
                    if (code.Length == 0) continue;
                    // First wins, like every other STING key map: two materials sharing a
                    // name is a data question, and picking one silently is how a register
                    // stops being governed.
                    if (map.ContainsKey(name.Trim())) continue;
                    map[name.Trim()] = code;
                    coded++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("ElementMatCodeReader.CodeByMaterialName: " + ex.Message);
            }

            lock (_lock)
            {
                _docKey = key; _materialCount = count; _codeByName = map;
                _builtUtc = DateTime.UtcNow; CodedMaterialCount = coded;
            }

            // One line per rebuild, not per element. A zero here is the difference
            // between "no material matched" and "no material has ever been coded".
            StingLog.Info("MAT_CODE map: " + coded + " of " + (count < 0 ? map.Count : count)
                        + " material(s) carry a code" + (coded == 0
                          ? " — Pass 3 (MATERIAL) cannot fire; run Materials_StampCodes." : "."));
            return map;
        }
    }
}
