// ══════════════════════════════════════════════════════════════════════════
//  TypeCreationTallyTests.cs — W1. A type whose structure failed was reported
//  as created, and this is the gate that stops it going back.
//
//  Four functions in CompoundTypeCreator caught a SetCompoundStructure failure,
//  logged a warning, and returned TRUE. The comment said it plainly — "type was
//  created, just no layers" — and then the function told its caller the whole
//  operation had succeeded. The type existed, carried the BASE type's build-up,
//  and was counted as created. CreateMEPType, eighty lines below, has always
//  returned false on every failure path.
//
//  These tests drive the TALLY rather than Revit: the decision that matters is
//  "a compound-structure failure means the type was not built", and the counting
//  rule that follows. Both are Revit-free. What is NOT tested here is the Revit
//  call itself or the delete — see the PR's unverified list.
// ══════════════════════════════════════════════════════════════════════════
using System.Linq;
using StingTools.Core.Materials;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TypeCreationTallyTests
    {
        /// <summary>
        /// The old decision, written out so the gate has something to be red against:
        /// build the layers, try the structure, and report success either way.
        /// </summary>
        private static TypeCreationTally OldBehaviour(params bool[] structureSucceeded)
        {
            var t = new TypeCreationTally();
            foreach (bool ok in structureSucceeded)
                // `return true` regardless — the defect, in one line.
                t.Add("Floor", ok ? "GOOD" : "STRUCTURE FAILED", TypeCreationOutcome.CreatedAsDeclared);
            return t;
        }

        private static TypeCreationTally NewBehaviour(params bool[] structureSucceeded)
        {
            var t = new TypeCreationTally();
            foreach (bool ok in structureSucceeded)
                t.Add("Floor", ok ? "GOOD" : "STRUCTURE FAILED",
                      ok ? TypeCreationOutcome.CreatedAsDeclared
                         : TypeCreationOutcome.RefusedStructureFailed,
                      ok ? "" : "Revit rejected the compound structure");
            return t;
        }

        [Fact]
        public void A_Type_Whose_Structure_Failed_Is_Not_Counted_As_Created()
        {
            // Three rows, one of which Revit refuses. The old decision reported three
            // created; the new one reports two, and names the third.
            var oldT = OldBehaviour(true, false, true);
            var newT = NewBehaviour(true, false, true);

            Assert.Equal(3, oldT.CreatedAsDeclared);      // what the defect said
            Assert.Equal(0, oldT.Refused);

            Assert.Equal(2, newT.CreatedAsDeclared);      // what is true
            Assert.Equal(1, newT.Refused);
            Assert.Equal(TypeCreationOutcome.RefusedStructureFailed,
                         newT.Records.Single(r => r.TypeName == "STRUCTURE FAILED").Outcome);
        }

        [Fact]
        public void A_Run_That_Built_Everything_And_A_Run_That_Refused_Half_Do_Not_Print_The_Same_Line()
        {
            // The identity that made this invisible. Asserted on the REPORT, because the
            // report is what a person reads before deciding the run went fine.
            string built = NewBehaviour(true, true, true, true).Report();
            string half = NewBehaviour(true, false, true, false).Report();

            Assert.NotEqual(built, half);
            Assert.Contains("4 created with the build-up the register declares", built);
            Assert.Contains("0 refused", built);
            Assert.Contains("2 created with the build-up the register declares", half);
            Assert.Contains("2 refused because their layers could not be built", half);
            Assert.Contains("STRUCTURE FAILED", half);

            // And the OLD decision printed the same line for both, which is the point.
            Assert.Equal(OldBehaviour(true, true, true, true).Report(),
                         OldBehaviour(true, false, true, false).Report());
        }

        [Fact]
        public void A_Refusal_That_Could_Not_Clean_Up_Is_Louder_Than_One_That_Could()
        {
            // The worst outcome: refused, and the half-made type is still in the model
            // carrying the base type's build-up. It must not read as an ordinary refusal —
            // a type nobody asked for measures, prices and carbon-counts like a real one.
            var t = new TypeCreationTally();
            t.Add("Floor", "CLEAN", TypeCreationOutcome.RefusedStructureFailed, "removed");
            t.Add("Floor", "STUCK", TypeCreationOutcome.RefusedButLeftBehind, "could not be removed");

            Assert.Equal(2, t.Refused);
            Assert.Equal(1, t.LeftBehind);

            string r = t.Report();
            Assert.Contains("WARNING", r);
            Assert.Contains("carrying a build-up nobody asked for", r);
            Assert.Contains("STUCK", r);

            // A run with no left-behind types must not carry that warning.
            var clean = new TypeCreationTally();
            clean.Add("Floor", "CLEAN", TypeCreationOutcome.RefusedStructureFailed, "removed");
            Assert.DoesNotContain("WARNING", clean.Report());
        }

        [Fact]
        public void No_Layers_And_A_Rejected_Structure_Are_Different_Refusals()
        {
            // One is the register row (nothing readable to build from); the other is Revit
            // (it read fine and the structure was still invalid). Different fixes, so
            // different outcomes rather than one "failed".
            var t = new TypeCreationTally();
            t.Add("Wall", "NO ROWS", TypeCreationOutcome.RefusedNoLayers, "no layer could be built");
            t.Add("Wall", "BAD CS", TypeCreationOutcome.RefusedStructureFailed, "Revit said no");

            Assert.Equal(1, t.Count(TypeCreationOutcome.RefusedNoLayers));
            Assert.Equal(1, t.Count(TypeCreationOutcome.RefusedStructureFailed));
            Assert.Equal(2, t.Refused);
            Assert.Contains("no layer could be built", t.Report());
            Assert.Contains("Revit said no", t.Report());
        }

        [Fact]
        public void An_Already_Present_Type_Is_Neither_Created_Nor_Refused()
        {
            var t = new TypeCreationTally();
            t.Add("Roof", "ALREADY THERE", TypeCreationOutcome.AlreadyPresent);
            Assert.Equal(0, t.CreatedAsDeclared);
            Assert.Equal(0, t.Refused);
            Assert.Contains("1 already present", t.Report());
        }

        [Fact]
        public void An_Empty_Run_Says_So_Rather_Than_Reporting_Zero_Of_Everything()
        {
            // "0 created, 0 refused" reads as a clean run. Nothing to create is a different
            // fact and gets different words.
            Assert.Equal("No rows to create from.", new TypeCreationTally().Report());
        }
    }
}
