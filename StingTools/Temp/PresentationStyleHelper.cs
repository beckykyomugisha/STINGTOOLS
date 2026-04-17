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

        // ── Category-hide matrices ─────────────────────────────────────
        // What belongs on what template: each set represents categories
        // that DO NOT belong on that template type and should be hidden
        // via View.SetCategoryHidden. Not every template needs every hide
        // (e.g. plans need to keep Rooms, sections don't hide Walls).

        /// <summary>
        /// Site / landscape categories — hidden on interior-focused plans
        /// (MEP, working plans, most presentation plans).
        /// </summary>
        public static readonly BuiltInCategory[] SITE_CATEGORIES = new[]
        {
            BuiltInCategory.OST_Topography,
            BuiltInCategory.OST_Site,
            BuiltInCategory.OST_SiteProperty,
            BuiltInCategory.OST_SitePropertyLineSegment,
            BuiltInCategory.OST_Planting,
            BuiltInCategory.OST_Entourage,
            BuiltInCategory.OST_Parking,
            BuiltInCategory.OST_Roads,
            BuiltInCategory.OST_Mass,
        };

        /// <summary>
        /// View-marker / navigation categories — hidden on presentation
        /// templates. Construction lines, reference planes, scope boxes,
        /// section/elevation/callout markers, cameras, matchlines, revision
        /// clouds. Keep on working templates where coordinators need them.
        /// </summary>
        public static readonly BuiltInCategory[] VIEW_MARKER_CATEGORIES = new[]
        {
            BuiltInCategory.OST_CLines,               // Centerlines
            BuiltInCategory.OST_Cameras,
            BuiltInCategory.OST_Sections,             // Section markers
            BuiltInCategory.OST_Elev,                 // Elevation markers
            BuiltInCategory.OST_Callouts,
            BuiltInCategory.OST_ScopeBoxes,
            BuiltInCategory.OST_MatchlineAxis,
            BuiltInCategory.OST_ReferencePoints,
        };

        /// <summary>
        /// MEP / plumbing / electrical categories — hidden on structural
        /// presentation templates and landscape templates.
        /// </summary>
        public static readonly BuiltInCategory[] MEP_CATEGORIES = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
        };

        /// <summary>
        /// Furniture / interior fit-out categories — hidden on structural-only
        /// and landscape-only plans.
        /// </summary>
        public static readonly BuiltInCategory[] INTERIOR_FITOUT_CATEGORIES = new[]
        {
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_SpecialityEquipment,
        };

        /// <summary>
        /// Hide a list of categories on a view, silently skipping any that
        /// don't exist in this Revit version or don't allow visibility control.
        /// </summary>
        public static void HideCategories(View v, IEnumerable<BuiltInCategory> cats)
        {
            if (v == null || cats == null) return;
            foreach (var bic in cats)
            {
                try
                {
                    Category cat = v.Document.Settings.Categories.get_Item(bic);
                    if (cat == null) continue;
                    if (cat.get_AllowsVisibilityControl(v))
                        v.SetCategoryHidden(cat.Id, true);
                }
                catch (Exception ex) { StingLog.Warn($"HideCategory {bic}: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Configure cross-cutting view defaults that apply regardless of
        /// discipline: analytical categories hidden, import categories
        /// hidden on presentation, crop visibility, view discipline tag.
        /// </summary>
        public static void ApplyTemplateDefaults(View v,
            ViewDiscipline? discipline = null,
            bool hideAnalytical = true,
            bool hideImports = false,
            bool? cropBoxVisible = null,
            bool? cropBoxActive = null)
        {
            if (v == null) return;
            try
            {
                if (hideAnalytical)
                {
                    try { v.AreAnalyticalModelCategoriesHidden = true; }
                    catch (Exception ex) { StingLog.Warn($"AreAnalyticalModelCategoriesHidden on '{v.Name}': {ex.Message}"); }
                }
                if (hideImports)
                {
                    try { v.AreImportCategoriesHidden = true; }
                    catch (Exception ex) { StingLog.Warn($"AreImportCategoriesHidden on '{v.Name}': {ex.Message}"); }
                }
                if (discipline.HasValue)
                {
                    try { v.Discipline = discipline.Value; }
                    catch (Exception ex) { StingLog.Warn($"Set Discipline on '{v.Name}': {ex.Message}"); }
                }
                if (cropBoxVisible.HasValue)
                {
                    try { v.CropBoxVisible = cropBoxVisible.Value; }
                    catch (Exception ex) { StingLog.Warn($"CropBoxVisible on '{v.Name}': {ex.Message}"); }
                }
                if (cropBoxActive.HasValue)
                {
                    try { v.CropBoxActive = cropBoxActive.Value; }
                    catch (Exception ex) { StingLog.Warn($"CropBoxActive on '{v.Name}': {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ApplyTemplateDefaults on '{v.Name}': {ex.Message}"); }
        }

        /// <summary>
        /// Canonical hide matrix by template discipline code. Returns the set
        /// of categories that should be hidden for this template type. Called
        /// from ConfigureTemplateVG after the filter stack is laid down so
        /// irrelevant categories are pruned even when a filter would otherwise
        /// colour them.
        /// </summary>
        public static IEnumerable<BuiltInCategory> GetHideCategoriesForTemplate(string discipline)
        {
            switch (discipline)
            {
                // ── MEP / working discipline plans ──
                // Remove site + marker noise; keep interior fit-out (MEP touches it)
                case "M":
                case "E":
                case "P":
                case "FP":
                case "LV":
                case "MEP":
                case "MEP_3D":
                    foreach (var c in SITE_CATEGORIES) yield return c;
                    yield break;

                // ── Architectural working plan ──
                // Keep site (often part of arch drawings); hide only mass
                case "A":
                    yield return BuiltInCategory.OST_Mass;
                    yield break;

                // ── Structural plan ──
                // Hide MEP + interior fit-out — structural drawings are skeleton only
                case "S":
                    foreach (var c in MEP_CATEGORIES) yield return c;
                    foreach (var c in INTERIOR_FITOUT_CATEGORIES) yield return c;
                    foreach (var c in SITE_CATEGORIES) yield return c;
                    yield break;

                // ── Combined services — keep most, remove mass ──
                case "ALL":
                    yield return BuiltInCategory.OST_Mass;
                    yield break;

                // ── Demolition / as-built / area ──
                case "DEMO":
                case "EXIST":
                    yield return BuiltInCategory.OST_Mass;
                    foreach (var c in SITE_CATEGORIES.Where(c => c != BuiltInCategory.OST_Mass))
                        yield return c;
                    yield break;
                case "AREA":
                    // Area plan: hide everything that isn't a room/wall/door/window
                    foreach (var c in MEP_CATEGORIES) yield return c;
                    foreach (var c in INTERIOR_FITOUT_CATEGORIES) yield return c;
                    foreach (var c in SITE_CATEGORIES) yield return c;
                    yield return BuiltInCategory.OST_Stairs;
                    yield return BuiltInCategory.OST_Railings;
                    yield break;

                // ── RCP — reflected ceiling ──
                case "RCP_LTG":
                case "RCP_CLG":
                    foreach (var c in SITE_CATEGORIES) yield return c;
                    yield return BuiltInCategory.OST_Furniture;
                    yield return BuiltInCategory.OST_FurnitureSystems;
                    yield return BuiltInCategory.OST_Casework;
                    yield return BuiltInCategory.OST_PlumbingFixtures;
                    yield return BuiltInCategory.OST_StructuralFoundation;
                    yield return BuiltInCategory.OST_Floors;
                    yield break;

                // ── Presentation plans (both Classic/Enhanced and per-disc) ──
                // Hide markers, site mass, analytical
                case "PRES_C":
                case "PRES_E":
                case "PRES_A":
                case "PRES_S":
                case "PRES_E_DISC":
                case "PRES_P":
                case "PRES_MEP":
                case "PRES_MONO":
                case "PRES_DARK":
                    foreach (var c in VIEW_MARKER_CATEGORIES) yield return c;
                    yield return BuiltInCategory.OST_Mass;
                    yield return BuiltInCategory.OST_RvtLinks;
                    yield break;

                // ── Presentation landscape ──
                // Hide MEP, interior fit-out, structural, markers, mass
                case "PRES_LAND":
                    foreach (var c in MEP_CATEGORIES) yield return c;
                    foreach (var c in INTERIOR_FITOUT_CATEGORIES) yield return c;
                    foreach (var c in VIEW_MARKER_CATEGORIES) yield return c;
                    yield return BuiltInCategory.OST_StructuralColumns;
                    yield return BuiltInCategory.OST_StructuralFraming;
                    yield return BuiltInCategory.OST_StructuralFoundation;
                    yield return BuiltInCategory.OST_Ceilings;
                    yield return BuiltInCategory.OST_Mass;
                    yield break;

                // ── Presentation 3D — hide markers ──
                case "PRES_3D":
                case "3D_A":
                case "3D_S":
                case "3D_E":
                case "3D_P":
                case "3D_MONO":
                case "3D_DARK":
                case "PRES_CANDY_EXT":
                case "PRES_CANDY_INT":
                case "PRES_EARTH_EXT":
                case "PRES_EARTH_INT":
                case "PRES_BLUE_EXT":
                case "PRES_BLUE_INT":
                case "PRES_SKETCH":
                case "PRES_BLACK":
                    foreach (var c in VIEW_MARKER_CATEGORIES) yield return c;
                    yield return BuiltInCategory.OST_Mass;
                    yield break;

                // ── Sections ──
                case "SEC_W":
                case "SEC_P":
                    foreach (var c in SITE_CATEGORIES.Where(c => c != BuiltInCategory.OST_Topography))
                        yield return c;
                    if (discipline == "SEC_P")
                        foreach (var c in VIEW_MARKER_CATEGORIES) yield return c;
                    yield break;
                case "SEC_D":
                    // Detail sections: keep everything that might be in the detail
                    yield return BuiltInCategory.OST_Mass;
                    yield break;

                // ── Elevations ──
                case "ELEV_W":
                    yield return BuiltInCategory.OST_Mass;
                    yield break;
                case "ELEV_P":
                    foreach (var c in VIEW_MARKER_CATEGORIES) yield return c;
                    yield return BuiltInCategory.OST_Mass;
                    yield break;

                default: yield break;
            }
        }

        /// <summary>
        /// Map a STING discipline code to Revit's ViewDiscipline enum so the
        /// Project Browser organizes templates correctly.
        /// </summary>
        public static ViewDiscipline? MapViewDiscipline(string discipline)
        {
            switch (discipline)
            {
                case "M":   return ViewDiscipline.Mechanical;
                case "E":   return ViewDiscipline.Electrical;
                case "P":   return ViewDiscipline.Plumbing;
                case "A":
                case "PRES_A":
                case "PRES_LAND":
                case "PRES_C":
                case "PRES_E":
                case "3D_A":
                case "ELEV_W":
                case "ELEV_P":
                case "AREA":
                case "EXIST":
                case "DEMO":
                case "RCP_LTG":
                case "RCP_CLG":      return ViewDiscipline.Architectural;
                case "S":
                case "PRES_S":
                case "3D_S":         return ViewDiscipline.Structural;
                case "FP":
                case "LV":
                case "MEP":
                case "MEP_3D":
                case "PRES_MEP":
                case "PRES_E_DISC":
                case "PRES_P":
                case "3D_E":
                case "3D_P":
                case "ALL":
                case "PRES_3D":
                case "PRES_MONO":
                case "PRES_DARK":
                case "3D_MONO":
                case "3D_DARK":
                case "PRES_CANDY_EXT":
                case "PRES_CANDY_INT":
                case "PRES_EARTH_EXT":
                case "PRES_EARTH_INT":
                case "PRES_BLUE_EXT":
                case "PRES_BLUE_INT":
                case "PRES_SKETCH":
                case "PRES_BLACK":
                case "SEC_W":
                case "SEC_P":
                case "SEC_D":        return ViewDiscipline.Coordination;
                default:             return null;
            }
        }

        // ── Presentation palettes (named reference-duplicating schemes) ─
        //
        // Each palette captures the colour vocabulary of a specific
        // architectural-presentation render style. The accent fields drive
        // per-category graphic overrides (Accent = focus object, Base = shell,
        // Site = topography slab, Background = 3D view background).
        //
        // All palettes produce clean line-heavy 3D axos with a coloured base
        // slab — the signature look of the reference renders.

        public sealed class PresentationPalette
        {
            public string Name { get; set; }
            public Color Background { get; set; }         // 3D view background
            public Color BackgroundTop { get; set; }      // gradient top (optional)
            public bool UseGradient { get; set; }
            public Color LineColor { get; set; }          // everything-else line colour
            public Color AccentColor { get; set; }        // focus-category accent
            public Color AccentFill { get; set; }         // focus-category surface fill
            public Color SiteLine { get; set; }           // topography edge
            public Color SiteFill { get; set; }           // topography base slab
            public Color BaseLineColor { get; set; }      // walls/shell default
            public bool UseHiddenLine { get; set; }       // DisplayStyle.HLR for line-art
        }

        public static readonly PresentationPalette CANDY_PALETTE = new PresentationPalette
        {
            Name = "Candy",
            Background     = new Color( 56, 145, 150),   // teal bottom
            BackgroundTop  = new Color(245, 210, 220),   // pink top
            UseGradient    = true,
            LineColor      = new Color( 20,  90,  95),   // deep teal outlines
            AccentColor    = new Color(245, 170, 190),   // pink accent
            AccentFill     = new Color(250, 210, 220),   // pale pink surface
            SiteLine       = new Color( 20,  90,  95),   // deep teal on site edge
            SiteFill       = new Color(255, 255, 255),   // white site slab with pattern
            BaseLineColor  = new Color( 30, 100, 110),   // darker teal shell lines
            UseHiddenLine  = true,
        };

        public static readonly PresentationPalette EARTH_PALETTE = new PresentationPalette
        {
            Name = "Earth",
            Background     = new Color(248, 243, 228),   // cream
            BackgroundTop  = new Color(248, 243, 228),
            UseGradient    = false,
            LineColor      = new Color(120,  50,  45),   // maroon outlines
            AccentColor    = new Color(170, 195, 170),   // sage green
            AccentFill     = new Color(195, 215, 195),   // light sage
            SiteLine       = new Color(140,  60,  55),   // deep maroon site edge
            SiteFill       = new Color(150,  70,  65),   // maroon base slab
            BaseLineColor  = new Color(150,  70,  60),   // warm brown shell
            UseHiddenLine  = true,
        };

        public static readonly PresentationPalette BLUE_PALETTE = new PresentationPalette
        {
            Name = "Blue",
            Background     = new Color(240, 240, 242),   // near-white
            BackgroundTop  = new Color(240, 240, 242),
            UseGradient    = false,
            LineColor      = new Color( 30,  90, 170),   // cobalt blue outlines
            AccentColor    = new Color( 30, 110, 200),   // royal blue accent
            AccentFill     = new Color(210, 230, 250),   // pale blue surface
            SiteLine       = new Color( 10,  70, 150),   // deep blue edge
            SiteFill       = new Color( 30, 110, 200),   // blue base slab
            BaseLineColor  = new Color( 60, 120, 200),   // medium blue shell lines
            UseHiddenLine  = true,
        };

        public static readonly PresentationPalette SKETCH_PALETTE = new PresentationPalette
        {
            Name = "Sketch",
            Background     = new Color(215, 215, 215),   // warm grey bottom
            BackgroundTop  = new Color(245, 245, 245),   // near-white top
            UseGradient    = true,
            LineColor      = new Color( 80,  80,  80),   // dark grey outlines
            AccentColor    = new Color(160, 160, 160),   // mid grey accent
            AccentFill     = new Color(225, 225, 225),   // pale grey surface
            SiteLine       = new Color( 60,  60,  60),   // darker grey edge
            SiteFill       = new Color(235, 235, 235),   // light grey slab
            BaseLineColor  = new Color(100, 100, 100),
            UseHiddenLine  = true,
        };

        public static readonly PresentationPalette BLACK_PALETTE = new PresentationPalette
        {
            Name = "Black",
            Background     = new Color( 10,  10,  10),   // near-black
            BackgroundTop  = new Color(  0,   0,   0),
            UseGradient    = false,
            LineColor      = new Color(230, 230, 230),   // near-white strokes
            AccentColor    = new Color(  0,   0,   0),   // black fills on rooms/ceilings
            AccentFill     = new Color(  0,   0,   0),
            SiteLine       = new Color(255, 255, 255),
            SiteFill       = new Color(255, 255, 255),   // white site slab against black bg
            BaseLineColor  = new Color(255, 255, 255),
            UseHiddenLine  = false,
        };

        /// <summary>
        /// Attempt to set a 3D view background (solid or gradient). Uses the
        /// Revit 2022+ View.SetBackground API when available, otherwise silently
        /// skips — per-project Revit versions that predate the API still get the
        /// rest of the palette.
        /// </summary>
        public static void SetView3DBackground(View3D v, PresentationPalette palette)
        {
            if (v == null || palette == null) return;
            try
            {
                // Dynamic access so we degrade gracefully on older Revit APIs
                var bgType = typeof(Autodesk.Revit.DB.View3D).Assembly
                    .GetType("Autodesk.Revit.DB.Background");
                var setBg = typeof(Autodesk.Revit.DB.View3D)
                    .GetMethod("SetBackground", new[] { bgType });
                if (bgType == null || setBg == null) return;

                object bg;
                if (palette.UseGradient)
                {
                    var createGradient = bgType.GetMethod("CreateGradient",
                        new[] { typeof(Color), typeof(Color), typeof(Color) })
                        ?? bgType.GetMethod("CreateGradient",
                            new[] { typeof(Color), typeof(Color) });
                    if (createGradient == null) return;
                    bg = createGradient.GetParameters().Length == 3
                        ? createGradient.Invoke(null, new object[] {
                            palette.BackgroundTop, palette.Background, palette.Background })
                        : createGradient.Invoke(null, new object[] {
                            palette.BackgroundTop, palette.Background });
                }
                else
                {
                    var createSolid = bgType.GetMethod("CreateSolid",
                        new[] { typeof(Color) });
                    if (createSolid == null) return;
                    bg = createSolid.Invoke(null, new object[] { palette.Background });
                }
                setBg.Invoke(v, new[] { bg });
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetView3DBackground on '{v.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Apply a category-level accent override directly — used for "roof only",
        /// "rooms only", "topography only" presentation templates where a SINGLE
        /// Revit category is the focus rather than a whole STING discipline.
        /// </summary>
        public static void ApplyCategoryAccent(View v, BuiltInCategory bic,
            Color lineColor, Color fillColor, int lineWeight,
            FillPatternElement solidFill, bool includeFill = true)
        {
            try
            {
                Category cat = v.Document.Settings.Categories.get_Item(bic);
                if (cat == null) return;
                var ogs = v.GetCategoryOverrides(cat.Id) ?? new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(lineColor);
                if (lineWeight >= 1 && lineWeight <= 16)
                {
                    ogs.SetProjectionLineWeight(lineWeight);
                    ogs.SetCutLineWeight(Math.Min(16, lineWeight + 1));
                }
                ogs.SetCutLineColor(lineColor);
                if (includeFill && solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(fillColor);
                    ogs.SetCutForegroundPatternId(solidFill.Id);
                    ogs.SetCutForegroundPatternColor(fillColor);
                }
                v.SetCategoryOverrides(cat.Id, ogs);
            }
            catch (Exception ex) { StingLog.Warn($"ApplyCategoryAccent {bic}: {ex.Message}"); }
        }

        /// <summary>
        /// Apply a "line-art palette" to a view: every model category gets the
        /// palette base line colour, the accent category (roof / rooms / topography)
        /// gets the AccentColor+Fill, site slab gets SiteLine+SiteFill. Presentation
        /// templates call this to reproduce the reference renders.
        ///
        /// accentCategory = which category carries the primary palette colour
        ///   (Roofs for exterior views, Rooms for cut-away interior views,
        ///    Topography for site-focus views).
        /// </summary>
        public static void ApplyPalette(View v, PresentationPalette palette,
            BuiltInCategory? accentCategory, FillPatternElement solidFill)
        {
            if (v == null || palette == null) return;

            // 3D-specific: background + display style
            if (v is View3D v3d)
            {
                SetView3DBackground(v3d, palette);
                if (palette.UseHiddenLine)
                    ApplyDisplayStyle(v3d, DisplayStyle.HLR);
            }

            // Base-line colour on every visible model category.
            // We apply via a small set of core model categories rather than
            // enumerating every category in the document — keeps the override
            // list lean and predictable.
            BuiltInCategory[] baseCategories = new[]
            {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Railings,
                BuiltInCategory.OST_Ramps, BuiltInCategory.OST_Casework,
                BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems,
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Columns, BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_SpecialityEquipment,
            };
            foreach (var bic in baseCategories)
            {
                try
                {
                    Category cat = v.Document.Settings.Categories.get_Item(bic);
                    if (cat == null) continue;
                    var ogs = v.GetCategoryOverrides(cat.Id) ?? new OverrideGraphicSettings();
                    ogs.SetProjectionLineColor(palette.BaseLineColor);
                    ogs.SetProjectionLineWeight(2);
                    ogs.SetCutLineColor(palette.LineColor);
                    ogs.SetCutLineWeight(3);
                    if (solidFill != null)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                        ogs.SetSurfaceForegroundPatternColor(new Color(255, 255, 255));
                    }
                    v.SetCategoryOverrides(cat.Id, ogs);
                }
                catch (Exception ex) { StingLog.Warn($"Palette base {bic}: {ex.Message}"); }
            }

            // Roofs always get a distinct (base) line weight boost even when
            // not the focus — they read as the "5th facade" in axos.
            try
            {
                Category roofCat = v.Document.Settings.Categories.get_Item(BuiltInCategory.OST_Roofs);
                if (roofCat != null)
                {
                    var ogs = v.GetCategoryOverrides(roofCat.Id) ?? new OverrideGraphicSettings();
                    ogs.SetProjectionLineColor(palette.BaseLineColor);
                    ogs.SetProjectionLineWeight(3);
                    ogs.SetCutLineColor(palette.LineColor);
                    ogs.SetCutLineWeight(4);
                    v.SetCategoryOverrides(roofCat.Id, ogs);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Palette roofs: {ex.Message}"); }

            // Accent category — full colour fill.
            if (accentCategory.HasValue)
            {
                ApplyCategoryAccent(v, accentCategory.Value,
                    palette.AccentColor, palette.AccentFill, 3, solidFill, includeFill: true);
            }

            // Site / topography — base slab colour (matches the reference renders
            // where the ground is a tinted block).
            ApplyCategoryAccent(v, BuiltInCategory.OST_Topography,
                palette.SiteLine, palette.SiteFill, 3, solidFill, includeFill: true);
        }

        /// <summary>
        /// Lookup a palette + accent category by discipline code.
        /// Returns null if the discipline is not a palette-style template.
        /// </summary>
        public static (PresentationPalette Palette, BuiltInCategory? Accent)? PaletteFor(string discipline)
        {
            switch (discipline)
            {
                case "PRES_CANDY_EXT":  return (CANDY_PALETTE,  BuiltInCategory.OST_Roofs);
                case "PRES_CANDY_INT":  return (CANDY_PALETTE,  BuiltInCategory.OST_Rooms);
                case "PRES_EARTH_EXT":  return (EARTH_PALETTE,  BuiltInCategory.OST_Roofs);
                case "PRES_EARTH_INT":  return (EARTH_PALETTE,  BuiltInCategory.OST_Rooms);
                case "PRES_BLUE_EXT":   return (BLUE_PALETTE,   BuiltInCategory.OST_Roofs);
                case "PRES_BLUE_INT":   return (BLUE_PALETTE,   BuiltInCategory.OST_Rooms);
                case "PRES_SKETCH":     return (SKETCH_PALETTE, null);
                case "PRES_BLACK":      return (BLACK_PALETTE,  BuiltInCategory.OST_Rooms);
                default:                return null;
            }
        }
    }
}
