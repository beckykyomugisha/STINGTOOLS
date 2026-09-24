// Who may write tier gates, and what a zero-carrier run must say.

using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TierGateScopeTests
    {
        [Fact]
        public void AModelTypeMayBeSwept()
            => Assert.True(TierGateScope.MaySweep(isAnnotationCategory: false));

        [Fact]
        public void AnAnnotationTypeMayNot()
        {
            // Tag types carry these gates as the type-variant catalogue's own
            // state. Three separate commands swept them into one global depth,
            // which is how a "_T2" variant came to read all ten gates on.
            Assert.False(TierGateScope.MaySweep(isAnnotationCategory: true));
        }

        [Fact]
        public void TheZeroCarrierAdviceNamesTheCauseAndTheFix()
        {
            string a = TierGateScope.NoModelCarriersAdvice(14);

            // It must say nothing changed — the old message reported a healthy
            // count while the drawing stood still.
            Assert.Contains("nothing on a drawing will change", a);
            // and name the actual cause, not a symptom
            Assert.Contains("TAG_PARA_STATE", a);
            Assert.Contains("TAG_DEPTH_TIER_INT", a);
            Assert.Contains("bound to no model category", a);
            // and say the skipped tag types were deliberate, with the count
            Assert.Contains("14 tag type(s) were skipped on purpose", a);
        }

        [Fact]
        public void TheAdviceDoesNotClaimTagTypesWhenThereWereNone()
        {
            string a = TierGateScope.NoModelCarriersAdvice(0);
            Assert.DoesNotContain("skipped on purpose", a);
            Assert.Contains("No tag types were found either", a);
        }
    }
}
