// StingTools — Design Option Registry.
//
// Single source of truth for STING's view of design options. Merges the
// live Revit state (sets, options, primary flag, active option, element
// counts) with sidecar metadata (purpose, decision date, BOQ delta,
// linked issues, etc.). Cached per-document and invalidated on demand
// by IUpdater hooks or commands that mutate the option graph.
//
// API rules followed:
//   * Enumeration uses OfClass(typeof(DesignOption)) per Rhino.Inside
//     guidance — OST_DesignOptions is not a quick-filter category.
//   * Sets are enumerated via OST_DesignOptionSets and bound to their
//     options via Element.GetDependentElements.
//   * IsPrimary read straight off DesignOption.IsPrimary.
//   * GetActiveDesignOptionId() drives the active-option indicator.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.DesignOptions
{
    /// <summary>Live snapshot of an option set and its options.</summary>
    public class DesignOptionSetSnapshot
    {
        public ElementId SetId;
        public string Name;
        public List<DesignOptionSnapshot> Options = new List<DesignOptionSnapshot>();
        public DesignOptionSetMetadata Metadata;

        public DesignOptionSnapshot Primary
            => Options.FirstOrDefault(o => o.IsPrimary);
    }

    public class DesignOptionSnapshot
    {
        public ElementId OptionId;
        public string Name;
        public bool IsPrimary;
        public bool IsActive;
        public int ElementCount;
        public DesignOptionMetadata Metadata;
    }

    public static class DesignOptionRegistry
    {
        // ── Sidecar location ─────────────────────────────────────────────
        public static string GetSidecarPath(Document doc)
        {
            string projDir = OutputLocationHelper.GetOutputDirectory(doc);
            string bimCoord = Path.Combine(projDir, "_BIM_COORD");
            return Path.Combine(bimCoord, "design_options.json");
        }

        // ── Cache (per-document, by hash code) ───────────────────────────
        private static readonly Dictionary<int, (DateTime when, List<DesignOptionSetSnapshot> data)> _cache
            = new Dictionary<int, (DateTime, List<DesignOptionSetSnapshot>)>();

        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        public static void InvalidateCache(Document doc = null)
        {
            if (doc == null) { _cache.Clear(); return; }
            _cache.Remove(doc.GetHashCode());
        }

        // ── Public surface ───────────────────────────────────────────────

        /// <summary>Returns every option set in the document, hydrated with
        /// sidecar metadata if present. Cached for 30 s.</summary>
        public static IReadOnlyList<DesignOptionSetSnapshot> Snapshot(Document doc)
        {
            if (doc == null) return Array.Empty<DesignOptionSetSnapshot>();
            int key = doc.GetHashCode();
            if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.when < CacheTtl)
                return hit.data;

            var sidecar = LoadSidecar(doc);
            var data = BuildLive(doc, sidecar);
            _cache[key] = (DateTime.UtcNow, data);
            return data;
        }

        /// <summary>Returns the active design option id, or InvalidElementId
        /// if the user is editing the main model.</summary>
        public static ElementId ActiveOptionId(Document doc)
        {
            try { return DesignOption.GetActiveDesignOptionId(doc); }
            catch (Exception ex)
            {
                StingLog.Warn($"DesignOptionRegistry.ActiveOptionId: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>True if the supplied option is its set's primary.
        /// Reads DesignOption.IsPrimary directly — Document.IsDesignOptionPrimary
        /// was removed from the public API in modern Revit versions.</summary>
        public static bool IsPrimary(Document doc, ElementId optionId)
        {
            if (doc == null || optionId == null || optionId == ElementId.InvalidElementId) return true;
            try
            {
                var opt = doc.GetElement(optionId) as DesignOption;
                return opt != null ? opt.IsPrimary : false;
            }
            catch (Exception ex) { StingLog.Warn($"IsPrimary: {ex.Message}"); return false; }
        }

        /// <summary>Total element count assigned to the supplied option.</summary>
        public static int CountElementsInOption(Document doc, ElementId optionId)
        {
            if (doc == null || optionId == null || optionId == ElementId.InvalidElementId) return 0;
            try
            {
                var f = new ElementDesignOptionFilter(optionId);
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(f)
                    .GetElementCount();
            }
            catch (Exception ex) { StingLog.Warn($"CountElementsInOption: {ex.Message}"); return 0; }
        }

        // ── Sidecar I/O ──────────────────────────────────────────────────

        public static DesignOptionSidecar LoadSidecar(Document doc)
        {
            try
            {
                string path = GetSidecarPath(doc);
                if (!File.Exists(path)) return new DesignOptionSidecar();
                var text = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<DesignOptionSidecar>(text)
                       ?? new DesignOptionSidecar();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DesignOptionRegistry.LoadSidecar: {ex.Message}");
                return new DesignOptionSidecar();
            }
        }

        public static void SaveSidecar(Document doc, DesignOptionSidecar sc)
        {
            try
            {
                string path = GetSidecarPath(doc);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                sc.Updated = DateTime.UtcNow;
                File.WriteAllText(path,
                    JsonConvert.SerializeObject(sc, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DesignOptionRegistry.SaveSidecar: {ex.Message}");
            }
        }

        /// <summary>Mutate sidecar safely under a callback then persist.</summary>
        public static void MutateSidecar(Document doc, Action<DesignOptionSidecar> mutate)
        {
            var sc = LoadSidecar(doc);
            try { mutate?.Invoke(sc); } catch (Exception ex) { StingLog.Warn($"MutateSidecar: {ex.Message}"); }
            SaveSidecar(doc, sc);
        }

        // ── Live read ────────────────────────────────────────────────────

        private static List<DesignOptionSetSnapshot> BuildLive(
            Document doc, DesignOptionSidecar sidecar)
        {
            var result = new List<DesignOptionSetSnapshot>();
            ElementId activeId = ActiveOptionId(doc);

            try
            {
                var setElems = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DesignOptionSets)
                    .WhereElementIsNotElementType()
                    .ToElements();

                // Cheap O(opts) options collector — match each option to its set
                // via Element.GroupId / parent dependency.
                var allOptions = new FilteredElementCollector(doc)
                    .OfClass(typeof(DesignOption))
                    .Cast<DesignOption>()
                    .ToList();

                foreach (var setEl in setElems)
                {
                    var snap = new DesignOptionSetSnapshot
                    {
                        SetId = setEl.Id,
                        Name = setEl.Name ?? "(unnamed)"
                    };

                    var depIds = setEl.GetDependentElements(
                        new ElementClassFilter(typeof(DesignOption)));
                    foreach (var optId in depIds)
                    {
                        var opt = doc.GetElement(optId) as DesignOption;
                        if (opt == null) continue;
                        snap.Options.Add(new DesignOptionSnapshot
                        {
                            OptionId = opt.Id,
                            Name = opt.Name ?? "(unnamed)",
                            IsPrimary = opt.IsPrimary,
                            IsActive = activeId != null && activeId == opt.Id,
                            ElementCount = CountElementsInOption(doc, opt.Id)
                        });
                    }

                    // Sidecar hydration
                    snap.Metadata = sidecar?.Sets?
                        .FirstOrDefault(s => string.Equals(s.SetName, snap.Name,
                            StringComparison.OrdinalIgnoreCase));
                    if (snap.Metadata != null)
                    {
                        foreach (var os in snap.Options)
                            os.Metadata = snap.Metadata.Options?
                                .FirstOrDefault(o => string.Equals(o.OptionName, os.Name,
                                    StringComparison.OrdinalIgnoreCase));
                    }

                    result.Add(snap);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DesignOptionRegistry.BuildLive: {ex.Message}");
            }

            return result;
        }

        // ── Param-write helper used from RunFullPipeline ─────────────────

        /// <summary>Writes ASS_DESIGN_OPTION_TXT / ASS_OPTION_SET_TXT /
        /// ASS_OPTION_PRIMARY_BOOL on the supplied element, deriving values
        /// from Element.DesignOption. Idempotent and safe on main-model
        /// elements (writes "Main Model" / empty / 1).</summary>
        public static void WriteOptionParams(Document doc, Element el, bool overwrite = true)
        {
            if (doc == null || el == null) return;
            try
            {
                var dopt = el.DesignOption;
                string optName = dopt?.Name ?? DesignOptionParams.MAIN_MODEL_LABEL;
                string setName = "";
                bool isPrimary = true;

                if (dopt != null)
                {
                    isPrimary = dopt.IsPrimary;
                    var depParents = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_DesignOptionSets)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    foreach (var s in depParents)
                    {
                        var depIds = s.GetDependentElements(
                            new ElementClassFilter(typeof(DesignOption)));
                        if (depIds.Contains(dopt.Id))
                        {
                            setName = s.Name ?? "";
                            break;
                        }
                    }
                }

                ParameterHelpers.SetString(el, DesignOptionParams.OPTION_TXT, optName, overwrite);
                ParameterHelpers.SetString(el, DesignOptionParams.OPTION_SET_TXT, setName, overwrite);
                ParameterHelpers.SetInt(el, DesignOptionParams.OPTION_PRIM_INT, isPrimary ? 1 : 0);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WriteOptionParams: {el?.Id} — {ex.Message}");
            }
        }
    }
}
