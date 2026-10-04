using System.Collections.Generic;
using StingTools.Core.Validation;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review API-5. LOD_Stamp overwrote ASS_LOD_VERIFIED_TXT on every passing element
    /// and never cleared a failing one: stamping deliverable-b after deliverable-d downgraded every
    /// element, and an element that later failed LOD 500 kept the deliverable-d claim.
    /// </summary>
    public class LodStampRuleTests
    {
        private static readonly LodMilestone B = new LodMilestone { Id = "deliverable-b", Lod = 300 };
        private static readonly LodMilestone D = new LodMilestone { Id = "deliverable-d", Lod = 500 };
        private static readonly List<LodMilestone> All = new List<LodMilestone> { B, D };

        [Fact]
        public void ALowerStampNeverDowngradesAHigherOne() =>
            Assert.Equal(LodStampAction.Keep, LodStampRule.Decide("deliverable-d", B, passed: true, All));

        [Fact]
        public void AHigherStampReplacesALowerOne() =>
            Assert.Equal(LodStampAction.Write, LodStampRule.Decide("deliverable-b", D, passed: true, All));

        [Fact]
        public void AnEmptyElementIsStampedWhenItPasses() =>
            Assert.Equal(LodStampAction.Write, LodStampRule.Decide("", B, passed: true, All));

        [Fact]
        public void FailingAMilestoneWithdrawsAClaimAtOrAboveIt()
        {
            Assert.Equal(LodStampAction.Clear, LodStampRule.Decide("deliverable-d", D, passed: false, All));
            Assert.Equal(LodStampAction.Clear, LodStampRule.Decide("deliverable-d", B, passed: false, All));
        }

        [Fact]
        public void FailingAHigherMilestoneKeepsALowerClaim() =>
            Assert.Equal(LodStampAction.Keep, LodStampRule.Decide("deliverable-b", D, passed: false, All));
    }
}
