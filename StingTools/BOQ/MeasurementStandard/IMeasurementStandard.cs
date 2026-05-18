// ══════════════════════════════════════════════════════════════════════════
//  IMeasurementStandard.cs — Strategy interface for BOQ classification.
//
//  Different measurement standards (NRM2, CESMM4, POMI, ICMS3, MMHW)
//  have different section/group codes, description grammars, deduction
//  rules and unit conventions. Rather than hard-coding NRM2 throughout
//  the BOQ engine, callers consult an IMeasurementStandard via the
//  MeasurementStandardRegistry.
//
//  Default standard is NRM2. The standard is carried on
//  BOQDocument.MeasurementStandardId; exports honour it.
//
//  P6 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using Autodesk.Revit.DB;

namespace StingTools.BOQ.MeasurementStandard
{
    public interface IMeasurementStandard
    {
        /// <summary>Stable identifier — "nrm2", "cesmm4", "pomi", "icms3", "mmhw".</summary>
        string Id { get; }

        /// <summary>Spec version (e.g. "NRM2 2nd ed.", "CESMM4 (2012)").</summary>
        string Version { get; }

        /// <summary>Human label for picker UIs.</summary>
        string DisplayName { get; }

        /// <summary>Default unit symbol for a Revit category — varies by standard.</summary>
        string PreferredUnit(string categoryName);

        /// <summary>
        /// Classify a BOQ row into the standard's section/group code.
        /// Falls back to NRM2 mapping when the standard doesn't have an
        /// equivalent (e.g. POMI's broad classes vs CESMM4's detailed
        /// group/sub-group/feature lattice).
        /// </summary>
        string ClassifyRow(BOQLineItem line, Element el);

        /// <summary>
        /// Build the line-item description per the standard's grammar.
        /// NRM2: "Wall, blockwork 100mm, supply and fix."
        /// CESMM4: "Excavation, in firm ground, depth ≤ 1.5m, type A backfill."
        /// </summary>
        string BuildDescription(BOQLineItem line, Element el);

        /// <summary>
        /// Apply deduction rules to a quantity (e.g. CESMM4 deducts
        /// window/door openings from external wall areas at &gt; 0.5 m²).
        /// Returns the net quantity.
        /// </summary>
        double ApplyDeductions(BOQLineItem line, Element el);
    }
}
