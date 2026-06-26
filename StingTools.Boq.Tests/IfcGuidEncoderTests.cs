using System;
using Xunit;
using StingTools.IfcResults;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// INT-0 — pins the IFC GlobalId encoder to the canonical buildingSMART /
    /// RevitIFC algorithm. The reference vector is from the IfcOpenShell docs
    /// (the same compression Revit's exporter uses):
    ///   GUID  f70dd363-bfe3-495d-84a0-2c02dcb7d4d2
    ///   IfcId 3t3TDZl_D9NOIWB0BSjzJI
    /// Without this pin the encoder can silently regress (the Linux sandbox
    /// has no Revit API to verify against).
    /// </summary>
    public class IfcGuidEncoderTests
    {
        private const string RefGuid   = "f70dd363-bfe3-495d-84a0-2c02dcb7d4d2";
        private const string RefIfcGuid = "3t3TDZl_D9NOIWB0BSjzJI";
        private const string Alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        [Fact]
        public void FromGuid_matches_canonical_reference_vector()
        {
            Assert.Equal(RefIfcGuid, IfcGuidEncoder.FromGuid(new Guid(RefGuid)));
        }

        [Fact]
        public void FromGuid_output_is_22_chars_and_alphabet_valid()
        {
            string s = IfcGuidEncoder.FromGuid(new Guid(RefGuid));
            Assert.Equal(22, s.Length);
            foreach (char c in s)
                Assert.Contains(c, Alphabet);
        }

        [Fact]
        public void FromRevitUniqueId_zero_elementid_equals_FromGuid()
        {
            // XOR-folding element id 0 is the identity, so a UniqueId with a
            // 00000000 suffix must equal the raw episode-GUID encoding.
            string fromUid = IfcGuidEncoder.FromRevitUniqueId(RefGuid + "-00000000");
            Assert.Equal(RefIfcGuid, fromUid);
        }

        [Fact]
        public void FromRevitUniqueId_folds_elementid_into_low_bytes()
        {
            // Element id 0x7b XORs the last GUID byte 0xd2 -> 0xa9, changing only
            // the final base64 group "jzJI" -> "jzIf" (hand-derived from the
            // canonical algorithm and cross-checked against the reference vector).
            string folded = IfcGuidEncoder.FromRevitUniqueId(RefGuid + "-0000007b");
            Assert.Equal("3t3TDZl_D9NOIWB0BSjzIf", folded);
        }

        [Fact]
        public void FromRevitUniqueId_is_elementid_sensitive()
        {
            // Same episode GUID, different element-id suffix -> different GlobalId.
            string a = IfcGuidEncoder.FromRevitUniqueId(RefGuid + "-00000001");
            string b = IfcGuidEncoder.FromRevitUniqueId(RefGuid + "-00000002");
            Assert.NotEqual(a, b);
            Assert.NotEqual(IfcGuidEncoder.FromRevitUniqueId(RefGuid + "-00000000"), a);
            Assert.Equal(22, a.Length);
        }

        [Fact]
        public void FromRevitUniqueId_empty_returns_empty()
        {
            Assert.Equal("", IfcGuidEncoder.FromRevitUniqueId(""));
            Assert.Equal("", IfcGuidEncoder.FromRevitUniqueId(null!));
        }

        // Stub exposing only a UniqueId string — stands in for a Revit Element
        // (which cannot be constructed in the no-Revit test sandbox).
        private sealed class FakeElement { public string UniqueId { get; set; } = ""; }

        [Fact]
        public void FromElementGoldStandard_falls_back_to_string_encoder_when_ifc_absent()
        {
            // RevitAPIIFC is not loaded in the test process, so the reflection
            // path is unavailable and the helper must return exactly the
            // canonical string-encoder value for the element's UniqueId.
            string uid = RefGuid + "-0000007b";
            var fake = new FakeElement { UniqueId = uid };
            Assert.Equal(IfcGuidEncoder.FromRevitUniqueId(uid),
                         IfcGuidEncoder.FromElementGoldStandard(fake));
            // And that value is the canonical folded GlobalId pinned above.
            Assert.Equal("3t3TDZl_D9NOIWB0BSjzIf",
                         IfcGuidEncoder.FromElementGoldStandard(fake));
        }

        [Fact]
        public void FromElementGoldStandard_null_returns_empty()
        {
            Assert.Equal("", IfcGuidEncoder.FromElementGoldStandard(null!));
        }
    }
}
