using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// One readiness indicator (a "light") on the dashboard. Computed from a
    /// live FilteredElementCollector pass so users see the project's actual
    /// state, not a stale snapshot.
    /// </summary>
    public sealed class ReadinessLight
    {
        public string Key { get; set; } = string.Empty;     // "Params" | "Filters" | ...
        public string Label { get; set; } = string.Empty;
        public int Done { get; set; }
        public int Total { get; set; }
        public string Note { get; set; } = string.Empty;

        public double Pct => Total <= 0 ? 0.0 : Done * 100.0 / Total;
        public string Status =>
            Total == 0 ? "Unknown" :
            Done == 0 ? "Red" :
            Done < Total ? "Amber" : "Green";
    }

    /// <summary>
    /// Per-op (done, total) badge used to colour the nav tree. Computed from
    /// the readiness scan so the dashboard's nav can show "(28/28)" next to
    /// each op without re-doing the work.
    /// </summary>
    public sealed class OpBadge
    {
        public string OpTag { get; set; } = string.Empty;
        public int Done { get; set; }
        public int Total { get; set; }
        public string Note { get; set; } = string.Empty;

        public string Status =>
            Total == 0 ? "Unknown" :
            Done == 0 ? "Red" :
            Done < Total ? "Amber" : "Green";
    }

    /// <summary>
    /// Project-wide readiness snapshot consumed by the Template Manager
    /// dashboard. Computed by ProjectReadiness.Compute(doc) on dashboard
    /// open + after every Run. Cheap — single-pass collectors.
    /// </summary>
    public sealed class ReadinessSnapshot
    {
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
        public string DocumentPath { get; set; } = string.Empty;
        public List<ReadinessLight> Lights { get; set; } = new();
        public Dictionary<string, OpBadge> Badges { get; set; } = new();

        public ReadinessLight LightOrDefault(string key) =>
            Lights.FirstOrDefault(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new ReadinessLight { Key = key, Label = key };

        public OpBadge BadgeOrDefault(string opTag) =>
            Badges.TryGetValue(opTag, out var b) ? b : new OpBadge { OpTag = opTag };
    }

    /// <summary>
    /// Computes a readiness snapshot for the active document — what's done,
    /// what's missing, per-op badges for the dashboard nav tree.
    /// Per-document cache with a 5-second staleness window so successive
    /// reads from the dashboard don't re-collect.
    /// </summary>
    public static class ProjectReadiness
    {
        private static readonly ConcurrentDictionary<string, (DateTime t, ReadinessSnapshot s)> _cache = new();
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Compute (or return cached) snapshot. Cache key is the document's
        /// PathName; cache entries older than 5 s are recomputed.
        /// </summary>
        public static ReadinessSnapshot Compute(Document doc, bool forceRefresh = false)
        {
            if (doc == null) return new ReadinessSnapshot();
            string key = doc.PathName ?? doc.Title ?? "untitled";

            if (!forceRefresh
                && _cache.TryGetValue(key, out var hit)
                && DateTime.UtcNow - hit.t < StaleAfter)
                return hit.s;

            var snap = new ReadinessSnapshot { DocumentPath = key };
            try
            {
                CollectLights(doc, snap);
                CollectBadges(doc, snap);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ProjectReadiness.Compute: {ex.Message}");
            }
            _cache[key] = (DateTime.UtcNow, snap);
            return snap;
        }

        /// <summary>Drop the cache for a single document (call on close).</summary>
        public static void Invalidate(Document doc)
        {
            if (doc == null) return;
            _cache.TryRemove(doc.PathName ?? doc.Title ?? "untitled", out _);
        }

        /// <summary>Drop everything (call on plugin shutdown).</summary>
        public static void InvalidateAll() => _cache.Clear();

        // ── Internal: aggregate light counts ─────────────────────────────
        private static void CollectLights(Document doc, ReadinessSnapshot snap)
        {
            // Params: count of parameter GUIDs from ParamRegistry actually bound.
            int paramsBound = 0;
            int paramsTotal = 0;
            try
            {
                var allGuids = StingTools.Core.ParamRegistry.AllParamGuids;
                paramsTotal = allGuids?.Count ?? 0;
                if (paramsTotal > 0)
                {
                    var bindings = doc.ParameterBindings;
                    var it = bindings.ForwardIterator();
                    var bound = new HashSet<Guid>();
                    while (it.MoveNext())
                    {
                        if (it.Key is ExternalDefinition extDef)
                            bound.Add(extDef.GUID);
                    }
                    foreach (var kvp in allGuids)
                        if (bound.Contains(kvp.Value)) paramsBound++;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Readiness params: {ex.Message}"); }
            snap.Lights.Add(new ReadinessLight
            {
                Key = "Params",
                Label = "Shared Parameters",
                Done = paramsBound,
                Total = paramsTotal,
                Note = paramsTotal == 0 ? "No parameter registry loaded" : ""
            });

            // Filters: count STING-prefixed ParameterFilterElements vs target.
            int filterCount = 0;
            try
            {
                filterCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .Count(f => f.Name != null && f.Name.StartsWith("STING", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Readiness filters: {ex.Message}"); }
            snap.Lights.Add(new ReadinessLight
            {
                Key = "Filters",
                Label = "STING Filters",
                Done = filterCount,
                Total = 28
            });

            // Worksets: count STING-prefixed worksets when document is workshared.
            int worksetCount = 0;
            int worksetTotal = doc.IsWorkshared ? 35 : 0;
            try
            {
                if (doc.IsWorkshared)
                {
                    worksetCount = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .ToWorksets()
                        .Count(w => w.Name != null && w.Name.StartsWith("STING", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Readiness worksets: {ex.Message}"); }
            snap.Lights.Add(new ReadinessLight
            {
                Key = "Worksets",
                Label = "Worksets",
                Done = worksetCount,
                Total = worksetTotal,
                Note = doc.IsWorkshared ? "" : "Not workshared"
            });

            // Templates: count STING-prefixed view templates.
            int tmplCount = 0;
            try
            {
                tmplCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Count(v => v.IsTemplate && v.Name != null
                                && v.Name.StartsWith("STING", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Readiness templates: {ex.Message}"); }
            snap.Lights.Add(new ReadinessLight
            {
                Key = "Templates",
                Label = "View Templates",
                Done = tmplCount,
                Total = 23
            });

            // Styles: line patterns + fill patterns (sum).
            int styleCount = 0;
            try
            {
                int linePats = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Count();
                int fillPats = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Count();
                styleCount = Math.Min(linePats, 10) + Math.Min(fillPats, 12);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Readiness styles: {ex.Message}"); }
            snap.Lights.Add(new ReadinessLight
            {
                Key = "Styles",
                Label = "Styles & Patterns",
                Done = styleCount,
                Total = 22
            });
        }

        // ── Internal: per-op badge counts (cheap collectors) ─────────────
        private static void CollectBadges(Document doc, ReadinessSnapshot snap)
        {
            void Add(string tag, int done, int total, string note = "")
            {
                snap.Badges[tag] = new OpBadge { OpTag = tag, Done = done, Total = total, Note = note };
            }

            // Per-op readiness echoes the lights but at a finer granularity.
            // The numbers are intentionally cheap — anything heavier waits for
            // the user to click into the op's preview.
            try
            {
                int filters = snap.LightOrDefault("Filters").Done;
                int templates = snap.LightOrDefault("Templates").Done;
                int worksets = snap.LightOrDefault("Worksets").Done;
                int parms = snap.LightOrDefault("Params").Done;
                int parmsTotal = snap.LightOrDefault("Params").Total;

                Add("CreateParameters", parms, parmsTotal);
                Add("CreateFilters", filters, 28);
                Add("CreateWorksets", worksets, snap.LightOrDefault("Worksets").Total);
                Add("CreateLinePatterns",
                    Math.Min(snap.LightOrDefault("Styles").Done, 10), 10);
                Add("CreatePhases", doc.Phases?.Size ?? 0, 6);

                Add("ViewTemplates", templates, 23);

                int fillPats = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Count();
                int linePats = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement)).Count();
                int textTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Count();
                int dimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType)).Count();
                Add("CreateFillPatterns", Math.Min(fillPats, 12), 12);
                Add("CreateLineStyles", Math.Min(linePats, 16), 16);
                Add("CreateObjectStyles", 0, 40, "Click to inspect");
                Add("CreateTextStyles", Math.Min(textTypes, 12), 12);
                Add("CreateDimensionStyles", Math.Min(dimTypes, 7), 7);
                Add("CreateVGOverrides", 0, 6, "Click to inspect");

                // Views without templates — surfaces as Amber on AutoAssign
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && CanHaveTemplate(v)).ToList();
                int withTmpl = views.Count(v => v.ViewTemplateId != ElementId.InvalidElementId);
                Add("AutoAssignTemplates", withTmpl, views.Count);

                // Audit ops are always "ready to run"
                Add("TemplateAudit", 1, 1, "Ready");
                Add("TemplateDiff", 1, 1, "Ready");
                Add("TemplateComplianceScore", 1, 1, "Ready");
                Add("ValidateTemplate", 1, 1, "Ready");
                Add("TemplateVGAudit", 1, 1, "Ready");
                Add("SchemaValidate", 1, 1, "Ready");
                Add("DynamicBindings", 1, 1, "Ready");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ProjectReadiness.CollectBadges: {ex.Message}");
            }
        }

        private static bool CanHaveTemplate(View v)
        {
            if (v == null || v.IsTemplate) return false;
            try
            {
                return v.ViewType == ViewType.FloorPlan
                    || v.ViewType == ViewType.CeilingPlan
                    || v.ViewType == ViewType.Section
                    || v.ViewType == ViewType.Elevation
                    || v.ViewType == ViewType.ThreeD
                    || v.ViewType == ViewType.AreaPlan
                    || v.ViewType == ViewType.EngineeringPlan
                    || v.ViewType == ViewType.Detail;
            }
            catch { return false; }
        }
    }
}
