using StingTools.BOQ.Takeoff;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// G-27 — a quantity must say whether it was MEASURED or ASSUMED.
    ///
    /// All 26 lookup() calls read a table shipping a DEFAULT row, so an absent key
    /// yields a number rather than reporting that nothing could be measured. That is
    /// the mechanism behind G-15, and it is universal rather than particular.
    ///
    /// This pins the shared vocabulary. It deliberately preserves RC-1's original
    /// confidence floors (35 unmatched / 55 empty / 100 clean) so promoting the type
    /// out of CompoundTakeoffBuilder cannot change existing take-off behaviour.
    /// </summary>
    public class QuantityResolutionG27Tests
    {
        [Fact]
        public void A_Clean_Run_Is_Measured_At_Full_Confidence()
        {
            var r = new QuantityResolution();
            r.Record("BLOCK", "BLE_BLOCK_SIZE_TXT", "400x200", "MORTAR_VOLUME_FACTOR", LookupState.Measured);

            Assert.Equal(LookupState.Measured, r.Worst);
            Assert.Equal(100, r.ConfidenceFloor);
            Assert.False(r.Any);
            Assert.Equal("measured", r.ExportFlag());
            Assert.Equal("", r.Note());
        }

        [Fact]
        public void An_Empty_Key_Is_Defaulted_Not_Measured()
        {
            // The G-15 shape: the parameter is unset, the DEFAULT row supplies a
            // number, and until now that was indistinguishable from a measurement.
            var r = new QuantityResolution();
            r.Record("BRICK_BOND", "BLE_BRICK_BOND_TYPE_TXT", "", "MORTAR_RATIO", LookupState.Defaulted);

            Assert.Equal(LookupState.Defaulted, r.Worst);
            Assert.Equal(55, r.ConfidenceFloor);          // RC-1's original floor
            Assert.Single(r.Empty);
            Assert.Empty(r.Unmatched);
            Assert.Equal("ASSUMED (default)", r.ExportFlag());
            Assert.Contains("param empty", r.Note());
        }

        [Fact]
        public void A_Set_But_Unmatched_Key_Is_The_More_Dangerous_Default()
        {
            // A typo. RC-1 scored this lower than an omission because the value looks
            // deliberate, and that ordering is preserved.
            var r = new QuantityResolution();
            r.Record("BLOCK", "BLE_BLOCK_SIZE_TXT", "400X200mm", "BLOCKS_PER_M2", LookupState.Defaulted);

            Assert.Equal(35, r.ConfidenceFloor);
            Assert.Single(r.Unmatched);
            Assert.Empty(r.Empty);
            Assert.Contains("UNMATCHED", r.Note());
        }

        [Fact]
        public void No_Row_At_All_Is_Unresolved_And_Outranks_A_Default()
        {
            var r = new QuantityResolution();
            r.Record("BLOCK", "BLE_BLOCK_SIZE_TXT", "", "BLOCKS_PER_M2", LookupState.Defaulted);
            r.Record("SCREED", "BLE_SCREED_TYPE_TXT", "UNKNOWN", "THICKNESS_M", LookupState.Unresolved);

            // A-1 / H-1: a quantity that cannot be measured must report that, and it
            // is a worse state than an assumption, so it wins the row's verdict.
            Assert.Equal(LookupState.Unresolved, r.Worst);
            Assert.Equal(0, r.ConfidenceFloor);
            Assert.Equal("NOT MEASURED", r.ExportFlag());
            Assert.Contains("NOT MEASURED", r.Note());
        }

        [Fact]
        public void Repeated_Sites_Are_Recorded_Once_But_Traced_Every_Time()
        {
            var r = new QuantityResolution();
            for (int i = 0; i < 5; i++)
                r.Record("CONCRETE", "BLE_STRUCT_CONCRETE_GRADE_TXT", "", "CEMENT_BAGS_PER_M3", LookupState.Defaulted);

            // One line in the note — a QS wants the site, not 5 copies of it …
            Assert.Single(r.Empty);
            // … but the trace keeps every occurrence, so a count is still possible.
            Assert.Equal(5, r.Traces.Count);
        }
    }
}
