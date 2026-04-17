using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Centralised graphic-override builder used by view-template configuration.
    /// Resolves STING line patterns and fill patterns from the document, builds
    /// reusable OverrideGraphicSettings for accent/halftone/monochrome/cut/status
    /// overlays, and applies category-level line-weight hierarchy.
    ///
    /// Every template produced by ConfigureTemplateVG should pull its overrides
    /// from this helper so style references (line patterns, fill patterns,
    /// line weights) are wired consistently and uniquely per template type.
    /// </summary>
    internal static class PresentationStyleHelper
    {
        // ── Pattern lookups ────────────────────────────────────────────
        // STING line pattern names — must match CreateLinePatternsCommand.Patterns
        public const string LP_DASHED         = "STING - Dashed";
        public const string LP_DASH_DOT       = "STING - Dash Dot";
        public const string LP_HIDDEN         = "STING - Hidden";
        public const string LP_CENTER         = "STING - Center";
        public const string LP_DEMOLITION     = "STING - Demolition";
        public const string LP_LONG_DASH      = "STING - Long Dash";
        public const string LP_DOT            = "STING - Dot";
        public const string LP_PHASE_BOUNDARY = "STING - Phase Boundary";
        public const string LP_FIRE_COMP      = "STING - Fire Compartment";

        public static ElementId GetLinePatternId(Document doc, string name)
        {
            try
            {
                var lp = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                return lp?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"GetLinePatternId('{name}'): {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        public static FillPatternElement GetSolidFill(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch (Exception ex) { StingLog.Warn($"GetSolidFill: {ex.Message}"); return null; }
        }

        public static FillPatternElement GetFillPatternByName(Document doc, string name, FillPatternTarget target)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(fp =>
                        string.Equals(fp.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        fp.GetFillPattern()?.Target == target);
            }
            catch (Exception ex) { StingLog.Warn($"GetFillPatternByName('{name}'): {ex.Message}"); }
            return null;
        }

        /// <summary>Find a Drafting fill pattern by approximate name match (e.g. "Crosshatch", "Diagonal Up").</summary>
        public static FillPatternElement GetDraftingPattern(Document doc, params string[] nameContains)
        {
            try
            {
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .Where(fp => fp.GetFillPattern()?.Target == FillPatternTarget.Drafting)
                    .ToList();
                foreach (var token in nameContains)
                {
                    var hit = all.FirstOrDefault(fp =>
                        fp.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !fp.GetFillPattern().IsSolidFill);
                    if (hit != null) return hit;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetDraftingPattern: {ex.Message}"); }
            return null;
        }

        // ── Color utilities ────────────────────────────────────────────
        public static Color Lighten(Color c, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            byte r = (byte)Math.Min(255, c.Red + (255 - c.Red) * t);
            byte g = (byte)Math.Min(255, c.Green + (255 - c.Green) * t);
            byte b = (byte)Math.Min(255, c.Blue + (255 - c.Blue) * t);
            return new Color(r, g, b);
        }

        public static Color Darken(Color c, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            byte r = (byte)Math.Max(0, c.Red * (1 - t));
            byte g = (byte)Math.Max(0, c.Green * (1 - t));
            byte b = (byte)Math.Max(0, c.Blue * (1 - t));
            return new Color(r, g, b);
        }

        public static readonly Color BLACK      = new Color(0, 0, 0);
        public static readonly Color WHITE      = new Color(255, 255, 255);
        public static readonly Color DARK_BG    = new Color(40, 40, 40);
        public static readonly Color DIM_GREY   = new Color(80, 80, 80);
        public static readonly Color MID_GREY   = new Color(140, 140, 140);
        public static readonly Color LIGHT_GREY = new Color(200, 200, 200);
        public static readonly Color PAPER      = new Color(245, 245, 240);

        // ── Override builders ──────────────────────────────────────────

        /// <summary>Standard halftone with optional transparency.</summary>
        public static OverrideGraphicSettings Halftone(int transparency = 50)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetHalftone(true);
            ogs.SetSurfaceTransparency(Math.Max(0, Math.Min(100, transparency)));
            return ogs;
        }

        /// <summary>Hide an element entirely (filter visibility false equivalent — used as override).</summary>
        public static OverrideGraphicSettings HiddenLine()
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceTransparency(100);
            ogs.SetHalftone(true);
            return ogs;
        }

        /// <summary>
        /// Working/coordination accent: discipline colour on projection lines,
        /// solid surface fill in the same colour with light transparency.
        /// </summary>
        public static OverrideGraphicSettings DisciplineAccent(Color c, int lineWeight,
            FillPatternElement solidFill, int surfaceTransparency = 0, bool includeFill = true)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(c);
            if (lineWeight >= 1 && lineWeight <= 16) ogs.SetProjectionLineWeight(lineWeight);
            if (includeFill && solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(c);
            }
            if (surfaceTransparency > 0)
                ogs.SetSurfaceTransparency(Math.Min(100, surfaceTransparency));
            return ogs;
        }

        /// <summary>
        /// Cut-plane override: line + fill at cut. Used so plan/section cuts
        /// are clearly differentiated from projected geometry.
        /// </summary>
        public static OverrideGraphicSettings CutAccent(Color c, int cutWeight,
            FillPatternElement solidFill, Color? cutFillColor = null)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetCutLineColor(c);
            if (cutWeight >= 1 && cutWeight <= 16) ogs.SetCutLineWeight(cutWeight);
            if (solidFill != null)
            {
                ogs.SetCutForegroundPatternId(solidFill.Id);
                ogs.SetCutForegroundPatternColor(cutFillColor ?? Lighten(c, 0.5));
            }
            return ogs;
        }

        /// <summary>
        /// Combined projection + cut accent (used on most working/coordination plans).
        /// </summary>
        public static OverrideGraphicSettings DisciplineAccentWithCut(Color c, int projWeight,
            int cutWeight, FillPatternElement solidFill, int surfaceTransparency = 0,
            bool includeProjFill = false)
        {
            var ogs = DisciplineAccent(c, projWeight, solidFill, surfaceTransparency, includeProjFill);
            ogs.SetCutLineColor(Darken(c, 0.2));
            if (cutWeight >= 1 && cutWeight <= 16) ogs.SetCutLineWeight(cutWeight);
            if (solidFill != null)
            {
                ogs.SetCutForegroundPatternId(solidFill.Id);
                ogs.SetCutForegroundPatternColor(Lighten(c, 0.55));
            }
            return ogs;
        }

        /// <summary>
        /// Monochrome line-only override — NO surface fills (true grayscale).
        /// Used by PRES_MONO / 3D_MONO templates.
        /// </summary>
        public static OverrideGraphicSettings MonochromeLine(Color lineColor, int weight, int cutWeight = 0)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(lineColor);
            if (weight >= 1 && weight <= 16) ogs.SetProjectionLineWeight(weight);
            ogs.SetCutLineColor(lineColor);
            int cw = cutWeight > 0 ? cutWeight : Math.Min(16, weight + 1);
            if (cw >= 1 && cw <= 16) ogs.SetCutLineWeight(cw);
            // Explicitly clear any surface pattern by NOT setting it.
            return ogs;
        }

        /// <summary>
        /// Status overlay applied as a TOP filter on every template — it tints
        /// existing/demolished/temporary work so users can spot phase-coded items
        /// at a glance, regardless of discipline.
        /// </summary>
        public static OverrideGraphicSettings StatusExisting(Document doc, FillPatternElement solidFill)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetHalftone(true);
            ogs.SetSurfaceTransparency(50);
            ElementId dashed = GetLinePatternId(doc, LP_DASHED);
            if (dashed != ElementId.InvalidElementId)
            {
                ogs.SetProjectionLinePatternId(dashed);
                ogs.SetCutLinePatternId(dashed);
            }
            ogs.SetProjectionLineColor(MID_GREY);
            ogs.SetCutLineColor(MID_GREY);
            return ogs;
        }

        public static OverrideGraphicSettings StatusDemolished(Document doc, FillPatternElement solidFill)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(220, 0, 0));
            ogs.SetProjectionLineWeight(4);
            ogs.SetCutLineColor(new Color(220, 0, 0));
            ogs.SetCutLineWeight(5);
            ElementId demoPat = GetLinePatternId(doc, LP_DEMOLITION);
            if (demoPat != ElementId.InvalidElementId)
            {
                ogs.SetProjectionLinePatternId(demoPat);
                ogs.SetCutLinePatternId(demoPat);
            }
            // Cross-hatch the cut so demolition is unmistakable
            var xhatch = GetDraftingPattern(doc, "Crosshatch", "Diagonal Crosshatch", "Diagonal");
            if (xhatch != null)
            {
                ogs.SetCutForegroundPatternId(xhatch.Id);
                ogs.SetCutForegroundPatternColor(new Color(220, 0, 0));
            }
            else if (solidFill != null)
            {
                ogs.SetCutForegroundPatternId(solidFill.Id);
                ogs.SetCutForegroundPatternColor(new Color(255, 200, 200));
            }
            return ogs;
        }

        public static OverrideGraphicSettings StatusTemporary(Document doc)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(new Color(220, 180, 0));
            ogs.SetProjectionLineWeight(3);
            ogs.SetSurfaceTransparency(50);
            ElementId dashDot = GetLinePatternId(doc, LP_DASH_DOT);
            if (dashDot != ElementId.InvalidElementId)
            {
                ogs.SetProjectionLinePatternId(dashDot);
                ogs.SetCutLinePatternId(dashDot);
            }
            return ogs;
        }

        public static OverrideGraphicSettings StatusNew(Document doc, Color baseColor)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(baseColor);
            ogs.SetProjectionLineWeight(3);
            return ogs;
        }

        // ── 3D presentation polish ─────────────────────────────────────
        /// <summary>
        /// Apply 3D display style and basic graphics options for 3D templates.
        /// Uses ViewDisplayBackground via View.set_DisplayStyle for shading,
        /// and best-effort sets shadows/silhouettes parameters when available.
        /// </summary>
        public static void ApplyDisplayStyle(View v, DisplayStyle style)
        {
            try { v.DisplayStyle = style; }
            catch (Exception ex) { StingLog.Warn($"DisplayStyle on '{v.Name}': {ex.Message}"); }
        }

        /// <summary>
        /// Apply category line-weight hierarchy for projection + cut.
        /// Used to give templates distinct stroke ranking
        /// (Architectural heaviest on PRES_C, MEP heaviest on coordination, etc.).
        /// </summary>
        public static void SetCategoryWeights(View v, BuiltInCategory bic, int? projWeight, int? cutWeight)
        {
            try
            {
                Category cat = v.Document.Settings.Categories.get_Item(bic);
                if (cat == null) return;
                if (projWeight.HasValue && projWeight.Value >= 1 && projWeight.Value <= 16)
                {
                    var p = projWeight.Value;
                    var ogs = v.GetCategoryOverrides(cat.Id) ?? new OverrideGraphicSettings();
                    ogs.SetProjectionLineWeight(p);
                    v.SetCategoryOverrides(cat.Id, ogs);
                }
                if (cutWeight.HasValue && cutWeight.Value >= 1 && cutWeight.Value <= 16)
                {
                    var cw = cutWeight.Value;
                    var ogs = v.GetCategoryOverrides(cat.Id) ?? new OverrideGraphicSettings();
                    ogs.SetCutLineWeight(cw);
                    v.SetCategoryOverrides(cat.Id, ogs);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetCategoryWeights {bic}: {ex.Message}"); }
        }

        /// <summary>Hide a category in the view template (used to hard-hide MEP in landscape templates).</summary>
        public static void HideCategory(View v, BuiltInCategory bic)
        {
            try
            {
                Category cat = v.Document.Settings.Categories.get_Item(bic);
                if (cat == null) return;
                if (cat.get_AllowsVisibilityControl(v))
                    v.SetCategoryHidden(cat.Id, true);
            }
            catch (Exception ex) { StingLog.Warn($"HideCategory {bic}: {ex.Message}"); }
        }

        // ── Filter assignment helpers ──────────────────────────────────

        /// <summary>
        /// Add a filter to the template if not already present, then apply the override.
        /// Idempotent — safe to call repeatedly.
        /// </summary>
        public static bool AddOrSet(View tmpl, ParameterFilterElement pfe, OverrideGraphicSettings ogs)
        {
            if (tmpl == null || pfe == null) return false;
            try
            {
                var existing = tmpl.GetFilters();
                if (!existing.Contains(pfe.Id)) tmpl.AddFilter(pfe.Id);
                if (ogs != null) tmpl.SetFilterOverrides(pfe.Id, ogs);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AddOrSet filter '{pfe.Name}' on '{tmpl.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>Hide elements matched by a filter (filter visibility false).</summary>
        public static bool HideByFilter(View tmpl, ParameterFilterElement pfe)
        {
            if (tmpl == null || pfe == null) return false;
            try
            {
                var existing = tmpl.GetFilters();
                if (!existing.Contains(pfe.Id)) tmpl.AddFilter(pfe.Id);
                tmpl.SetFilterVisibility(pfe.Id, false);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HideByFilter '{pfe.Name}' on '{tmpl.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wire status overlay filters onto a template. Existing → grey dashed,
        /// Demolished → red+xhatch, Temporary → amber dash-dot, optionally New
        /// → boost line weight.
        /// </summary>
        public static void ApplyStatusOverlays(View tmpl,
            Dictionary<string, ParameterFilterElement> filterLookup,
            Document doc, FillPatternElement solidFill,
            bool includeNewOverlay = false, Color? newColor = null)
        {
            if (filterLookup.TryGetValue("STING - Status: Existing", out var fEx))
                AddOrSet(tmpl, fEx, StatusExisting(doc, solidFill));
            if (filterLookup.TryGetValue("STING - Status: Demolished", out var fDe))
                AddOrSet(tmpl, fDe, StatusDemolished(doc, solidFill));
            if (filterLookup.TryGetValue("STING - Status: Temporary", out var fTe))
                AddOrSet(tmpl, fTe, StatusTemporary(doc));
            if (includeNewOverlay && filterLookup.TryGetValue("STING - Status: New", out var fNe))
                AddOrSet(tmpl, fNe, StatusNew(doc, newColor ?? new Color(0, 130, 50)));
        }

        /// <summary>
        /// <summary>
        /// Wire the full QA filter stack with tiered severity.
        ///   • CRITICAL (red heavy fill):  Untagged, Missing Discipline
        ///   • HIGH    (orange):           Incomplete Tags, Missing Sequence, Missing System
        ///   • MEDIUM  (amber):            Missing Location, No Level
        ///   • LOW     (yellow):           Missing Zone, Missing Function, Missing Product
        ///
        /// Applied LAST so QA alerts visually override discipline and status
        /// overrides — the point of QA filters is that gaps are unmistakable.
        /// </summary>
        public static void ApplyQAOverlays(View tmpl,
            Dictionary<string, ParameterFilterElement> filterLookup,
            FillPatternElement solidFill)
        {
            // ── CRITICAL tier ── red, thick projection line, red surface fill
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - Untagged Elements",
                lineColor: new Color(220, 0, 0), fillColor: new Color(255, 200, 200),
                lineWeight: 4, applyFill: true);
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing Discipline",
                lineColor: new Color(220, 0, 0), fillColor: new Color(255, 210, 210),
                lineWeight: 4, applyFill: true);

            // ── HIGH tier ── orange, medium fill
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - Incomplete Tags",
                lineColor: new Color(255, 130, 0), fillColor: new Color(255, 220, 180),
                lineWeight: 3, applyFill: true);
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing Sequence",
                lineColor: new Color(255, 130, 0), fillColor: new Color(255, 225, 195),
                lineWeight: 3, applyFill: true);
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing System",
                lineColor: new Color(255, 130, 0), fillColor: new Color(255, 225, 195),
                lineWeight: 3, applyFill: true);

            // ── MEDIUM tier ── amber, no fill (less disruptive)
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing Location",
                lineColor: new Color(230, 180, 0), fillColor: new Color(255, 240, 200),
                lineWeight: 3, applyFill: false);
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: No Level",
                lineColor: new Color(230, 180, 0), fillColor: new Color(255, 240, 200),
                lineWeight: 3, applyFill: false);

            // ── LOW tier ── yellow, dot-line pattern (optional token missing)
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing Zone",
                lineColor: new Color(220, 210, 0), fillColor: new Color(255, 255, 200),
                lineWeight: 2, applyFill: false);
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing Function",
                lineColor: new Color(220, 210, 0), fillColor: new Color(255, 255, 200),
                lineWeight: 2, applyFill: false);
            ApplyQaTier(tmpl, filterLookup, solidFill,
                "STING - QA: Missing Product",
                lineColor: new Color(220, 210, 0), fillColor: new Color(255, 255, 200),
                lineWeight: 2, applyFill: false);
        }

        private static void ApplyQaTier(View tmpl,
            Dictionary<string, ParameterFilterElement> filterLookup,
            FillPatternElement solidFill,
            string filterName, Color lineColor, Color fillColor,
            int lineWeight, bool applyFill)
        {
            if (!filterLookup.TryGetValue(filterName, out var pfe)) return;
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(lineColor);
            ogs.SetProjectionLineWeight(Math.Max(1, Math.Min(16, lineWeight)));
            ogs.SetCutLineColor(lineColor);
            ogs.SetCutLineWeight(Math.Max(1, Math.Min(16, lineWeight + 1)));
            if (applyFill && solidFill != null)
            {
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(fillColor);
                ogs.SetCutForegroundPatternId(solidFill.Id);
                ogs.SetCutForegroundPatternColor(fillColor);
            }
            AddOrSet(tmpl, pfe, ogs);
        }

        /// <summary>
        /// Attach system-level filters (Sys: HVAC, Sys: SAN, etc.) to a template
        /// WITHOUT a graphic override. The filters become toggleable via V|G so
        /// users can isolate a subsystem on demand without altering the default
        /// view appearance.
        /// </summary>
        public static void AttachSystemFilters(View tmpl,
            Dictionary<string, ParameterFilterElement> filterLookup,
            IEnumerable<string> sysFilterNames)
        {
            if (sysFilterNames == null) return;
            foreach (string name in sysFilterNames)
            {
                if (!filterLookup.TryGetValue(name, out var pfe)) continue;
                try
                {
                    var existing = tmpl.GetFilters();
                    if (!existing.Contains(pfe.Id))
                    {
                        tmpl.AddFilter(pfe.Id);
                        // Visible toggle stays true; no override applied so the
                        // filter is a pure "isolator" the coordinator can flip.
                        tmpl.SetFilterVisibility(pfe.Id, true);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AttachSystemFilter '{name}' on '{tmpl.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Canonical Sys: filter sets per discipline. Attached as toggleable
        /// isolators on working/coordination templates.
        /// </summary>
        public static IReadOnlyDictionary<string, string[]> DisciplineSystemFilters { get; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["M"]   = new[] { "STING - Sys: HVAC" },
                ["E"]   = new[] { "STING - Sys: LV" },
                ["P"]   = new[] { "STING - Sys: DCW", "STING - Sys: HWS", "STING - Sys: SAN",
                                  "STING - Sys: RWD", "STING - Sys: GAS" },
                ["FP"]  = new[] { "STING - Sys: FP", "STING - Sys: FLS" },
                ["LV"]  = new[] { "STING - Sys: COM", "STING - Sys: SEC",
                                  "STING - Sys: ICT", "STING - Sys: NCL" },
                ["MEP"] = new[] { "STING - Sys: HVAC", "STING - Sys: DCW", "STING - Sys: HWS",
                                  "STING - Sys: SAN", "STING - Sys: RWD", "STING - Sys: GAS",
                                  "STING - Sys: FP",  "STING - Sys: FLS",
                                  "STING - Sys: LV",  "STING - Sys: COM", "STING - Sys: SEC",
                                  "STING - Sys: ICT", "STING - Sys: NCL" },
                ["ALL"] = new[] { "STING - Sys: HVAC", "STING - Sys: DCW", "STING - Sys: HWS",
                                  "STING - Sys: SAN", "STING - Sys: RWD", "STING - Sys: GAS",
                                  "STING - Sys: FP",  "STING - Sys: FLS",
                                  "STING - Sys: LV",  "STING - Sys: COM", "STING - Sys: SEC",
                                  "STING - Sys: ICT", "STING - Sys: NCL" },
            };

        /// <summary>
        /// Attach parameter-based Disc: filters for precision ISO 19650 isolation.
        /// On presentation templates these refine the multi-category discipline
        /// filter by matching on the ASS_DISCIPLINE_COD_TXT parameter — catching
        /// cross-discipline tags (e.g. a mechanical damper placed in the
        /// Architectural family category).
        /// </summary>
        public static void AttachDiscParameterFilters(View tmpl,
            Dictionary<string, ParameterFilterElement> filterLookup,
            string focusDiscCode, Color focusColor, FillPatternElement solidFill)
        {
            string[] discKeys = {
                "STING - Disc: Mechanical", "STING - Disc: Electrical",
                "STING - Disc: Plumbing",   "STING - Disc: Architectural",
                "STING - Disc: Structural", "STING - Disc: Fire Protection",
                "STING - Disc: Low Voltage",
            };
            string focusKey = focusDiscCode switch
            {
                "M"   => "STING - Disc: Mechanical",
                "E"   => "STING - Disc: Electrical",
                "P"   => "STING - Disc: Plumbing",
                "A"   => "STING - Disc: Architectural",
                "S"   => "STING - Disc: Structural",
                "FP"  => "STING - Disc: Fire Protection",
                "LV"  => "STING - Disc: Low Voltage",
                _     => null,
            };

            foreach (string key in discKeys)
            {
                if (!filterLookup.TryGetValue(key, out var pfe)) continue;
                try
                {
                    var existing = tmpl.GetFilters();
                    if (!existing.Contains(pfe.Id))
                    {
                        tmpl.AddFilter(pfe.Id);
                        tmpl.SetFilterVisibility(pfe.Id, true);
                    }
                    if (focusKey != null && string.Equals(key, focusKey, StringComparison.OrdinalIgnoreCase))
                    {
                        // Subtle refinement: brighter edge + slight line weight boost
                        var ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLineColor(focusColor);
                        ogs.SetProjectionLineWeight(3);
                        ogs.SetCutLineColor(Darken(focusColor, 0.2));
                        ogs.SetCutLineWeight(4);
                        tmpl.SetFilterOverrides(pfe.Id, ogs);
                    }
                    // else: attached without override — users can toggle off
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"AttachDiscParamFilter '{key}' on '{tmpl.Name}': {ex.Message}");
                }
            }
        }
    }
}
