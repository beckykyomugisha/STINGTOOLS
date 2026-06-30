// ══════════════════════════════════════════════════════════════════════════
//  WasteTable.cs — per-material / per-category wastage-allowance table. PM-5.
//
//  The audit (§4) found waste was a single global knob (COST_DEFAULT_WASTE_PCT,
//  default 5%), applied identically to rebar, masonry, timber and concrete. NRM2
//  takeoff conventions allow materially different offcut/lapping/breakage
//  allowances — rebar ≈2.5%, masonry/concrete ≈5%, timber ≈10%, tiling ≈10%.
//  This is the single keyword-driven table both the COST path (BOQCostManager)
//  and the CARBON path (SustainElementCarbon / SustainabilityEngine) resolve
//  through, so a quantity is grossed up by the SAME allowance whether you are
//  pricing it or carbon-counting it.
//
//  Resolution: an explicit per-element rate override wins; else the first
//  keyword in MATERIAL then CATEGORY that matches the table; else the project
//  default knob. Keyword order is most-specific-first so "reinforced concrete"
//  resolves rebar before concrete only when "rebar"/"reinforc" appears.
//
//  Zero Autodesk.Revit.* dependencies on purpose — linked into the pure-logic
//  test project (StingTools.Boq.Tests) alongside WasteFactor.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ
{
    public static class WasteTable
    {
        /// <summary>(keyword, waste%) — NRM2 / industry-typical offcut + breakage
        /// allowances. Ordered most-specific-first; first substring hit wins.</summary>
        public static readonly IReadOnlyList<(string Keyword, double Percent)> Defaults =
            new List<(string, double)>
        {
            // Reinforcement — laps + tie wastage, tight on a bar schedule.
            ("rebar",        2.5),
            ("reinforc",     2.5),
            ("mesh",         5.0),
            // Structural / metal — cut to length off stock.
            ("structural steel", 2.5),
            ("steel",        2.5),
            ("aluss",        2.5),
            ("aluminium",    2.5),
            ("metal deck",   5.0),
            // Concrete family — over-pour + spillage.
            ("blinding",     5.0),
            ("screed",       5.0),
            ("concrete",     5.0),
            // Masonry — breakage + cutting at openings.
            ("blockwork",    5.0),
            ("block",        5.0),
            ("brick",        5.0),
            ("masonry",      5.0),
            ("mortar",      10.0),
            // Wet trades — render/plaster/tiling are high-waste.
            ("plasterboard",10.0),
            ("plaster",      8.0),
            ("render",       8.0),
            ("tile",        10.0),
            ("ceramic",     10.0),
            ("paving",       7.5),
            // Joinery / timber — offcuts dominate.
            ("timber",      10.0),
            ("joinery",     10.0),
            ("carpentry",   10.0),
            ("plywood",     10.0),
            ("mdf",         10.0),
            // Finishes / membranes.
            ("insulation",   5.0),
            ("membrane",     7.5),
            ("felt",         7.5),
            ("roofing",      7.5),
            ("waterproof",   7.5),
            ("glaz",         5.0),
            ("glass",        5.0),
            ("paint",        5.0),
            ("decoration",   5.0),
            // Earthworks / fill — bulking + compaction loss.
            ("hardcore",     7.5),
            ("aggregate",    7.5),
            ("sand",         7.5),
            ("gravel",       7.5),
            ("fill",         7.5),
            // MEP linear — cut waste is low.
            ("conduit",      3.0),
            ("trunking",     3.0),
            ("cable tray",   3.0),
            ("ductwork",     5.0),
            ("duct",         5.0),
            ("pipe",         3.0),
            ("cable",        2.0),
        };

        /// <summary>
        /// Resolve the governing waste% for an element. An explicit per-element
        /// rate override (&gt;0) wins; otherwise the first keyword in
        /// <paramref name="material"/> then <paramref name="category"/> that
        /// matches the table; otherwise <paramref name="projectDefaultPercent"/>.
        /// A non-positive / NaN override falls through, so "no override" behaves
        /// exactly as before for unmatched materials (returns the project default).
        /// </summary>
        public static double ResolveWastePercent(
            string material, string category,
            double overrideWastePercent, double projectDefaultPercent)
        {
            if (!double.IsNaN(overrideWastePercent) && overrideWastePercent > 0)
                return overrideWastePercent;

            double? hit = Lookup(material) ?? Lookup(category);
            return hit ?? projectDefaultPercent;
        }

        /// <summary>Table value for a single text token (material or category),
        /// or null when nothing matches.</summary>
        public static double? Lookup(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim().ToLowerInvariant();
            foreach (var (keyword, percent) in Defaults)
                if (s.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                    return percent;
            return null;
        }
    }
}
