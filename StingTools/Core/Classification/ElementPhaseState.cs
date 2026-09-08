using System;

namespace StingTools.Core.Classification
{
    /// <summary>
    /// KUT-5 — an element's state within the works, derived from its created and
    /// demolished phases. Revit-free so the rules are testable.
    ///
    /// <para><b>Why this exists.</b> CSI MasterFormat Division 02 is Existing Conditions —
    /// demolition and removals. <c>CsiMasterFormat.Resolve</c> matched on category, family,
    /// type and SYS and was never handed the element's phase, and <b>nobody names a
    /// toposolid "demolition"</b>, so a naming-keyed Division 02 rule could not fire on a
    /// real model. Rules drafted that way were withdrawn rather than shipped: they would
    /// have read as coverage in a review while delivering nothing.</para>
    ///
    /// <para>Revit expresses the state through <c>Phase Created</c> and <c>Phase
    /// Demolished</c>, which is what this classifies.</para>
    /// </summary>
    public static class ElementPhaseState
    {
        /// <summary>Built in this project and still standing at the end.</summary>
        public const string New = "New";

        /// <summary>Present before the works and not removed by them. NOT work — nothing in
        /// the shipped map classifies this, deliberately: a rule here would assert a work
        /// item for something nobody is being paid to touch.</summary>
        public const string Existing = "Existing";

        /// <summary>Removed by the works. This is Division 02.</summary>
        public const string Demolished = "Demolished";

        /// <summary>Built AND removed within the works — temporary works. Distinguished from
        /// <see cref="Demolished"/> because the two are opposite bills: one is a removal, the
        /// other is a hire. Nothing in the shipped map classifies it either (that is
        /// Division 01 / NRM2 preliminaries, and pricing it is a QS judgement); a project can
        /// add a rule now that the state is available to match on.</summary>
        public const string Temporary = "Temporary";

        /// <summary>No phase information — the element is not phase-aware (a grid, a level, a
        /// reference plane) or its phases could not be read.
        ///
        /// <para>Returned as the EMPTY STRING on purpose. <c>CsiRule.Score</c> treats an
        /// empty candidate as "does not match", so an element whose state is unknown can
        /// never satisfy a phase-qualified rule. Classifying a grid line as New — and then as
        /// demolition the day someone writes a broad rule — is the failure this avoids.</para>
        /// </summary>
        public const string Unknown = "";

        /// <summary>
        /// Classify an element from the ORDER of its phases in the document.
        ///
        /// <param name="createdIndex">Index of the created phase in the document's ordered
        /// phase list, or -1 when unset.</param>
        /// <param name="demolishedIndex">Index of the demolished phase, or -1 when the
        /// element is not demolished.</param>
        /// <param name="phaseCount">How many phases the document has.</param>
        /// </summary>
        public static string Classify(int createdIndex, int demolishedIndex, int phaseCount)
        {
            // No phases at all: nothing can be said. A document always has at least one,
            // so this is a read failure rather than a state.
            if (phaseCount <= 0) return Unknown;

            // Not phase-aware. See Unknown: this must NOT fall through to New.
            if (createdIndex < 0 && demolishedIndex < 0) return Unknown;

            if (demolishedIndex >= 0)
            {
                // Built by these works and removed by them: temporary works, not a removal.
                // "createdIndex > 0" is the test because index 0 is the pre-existing phase —
                // see the single-phase caveat below.
                return createdIndex > 0 ? Temporary : Demolished;
            }

            // EXISTING requires a phase to have existed BEFORE this one. In a single-phase
            // project every element sits in phase 0 and none of it is pre-existing: that
            // phase IS the works (Revit names it "New Construction"). Calling all of it
            // Existing would silently classify an entire building as out of scope.
            if (createdIndex == 0 && phaseCount > 1) return Existing;

            return createdIndex >= 0 ? New : Unknown;
        }
    }
}
