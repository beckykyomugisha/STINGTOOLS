using StingTools.Core.Baseline;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// Layer 3's report. This message is the ONLY account anyone gets of an
    /// operation that opened and rewrote their families, so what it says under
    /// each outcome is the part that has to be pinned.
    /// </summary>
    public class BaselineAugmentReportTests
    {
        [Fact]
        public void Nothing_Attempted_Reports_Nothing()
        {
            // Silence is right only when layer 3 was not asked to act. An
            // invented "0 families augmented" would read as a result.
            Assert.Null(new BaselineAugmentReport().Summary());
        }

        [Fact]
        public void A_Successful_Run_Names_Both_Counts()
        {
            var r = new BaselineAugmentReport
            {
                FamiliesAugmented = 12, ParametersAdded = 36
            };

            string s = r.Summary();

            Assert.Contains("36 shared type parameter(s)", s);
            Assert.Contains("12 famil(ies)", s);
        }

        [Fact]
        public void Total_Failure_Says_So_Rather_Than_Going_Quiet()
        {
            // The dangerous case: every family refused, nothing was added, and
            // a report that only mentioned successes would print nothing at all
            // — indistinguishable from "not asked to do anything".
            var r = new BaselineAugmentReport();
            r.Failed.Add("M_Door-Curtain-Wall: in-place families cannot be edited");

            string s = r.Summary();

            Assert.NotNull(s);
            Assert.Contains("No family was augmented", s);
            Assert.Contains("1 famil(ies) could not be edited", s);
        }

        [Fact]
        public void Partial_Success_Reports_Both_Halves()
        {
            var r = new BaselineAugmentReport { FamiliesAugmented = 8, ParametersAdded = 24 };
            r.Failed.Add("Vendor_Door: the family could not be opened");

            string s = r.Summary();

            Assert.Contains("8 famil(ies)", s);
            Assert.Contains("could not be edited", s);
        }

        [Fact]
        public void Already_Conforming_Families_Are_Reported_Not_Hidden()
        {
            // A re-run that changes nothing must not look like a failed run.
            var r = new BaselineAugmentReport { FamiliesAlreadyConforming = 14 };

            string s = r.Summary();

            Assert.NotNull(s);
            Assert.Contains("14 famil(ies) already carried them", s);
        }

        [Fact]
        public void The_Failure_List_Is_Capped_And_Says_So()
        {
            var r = new BaselineAugmentReport();
            for (int i = 0; i < 25; i++) r.Failed.Add($"Family {i:D2}: refused");

            string s = r.Summary();

            Assert.Contains("…and 15 more", s);
            Assert.Equal(10, System.Text.RegularExpressions.Regex.Matches(s, "refused").Count);
        }

        [Fact]
        public void Failures_Carry_The_Guidance_That_Explains_Them()
        {
            // "could not be edited" without a reason sends someone hunting for
            // a bug that is not there: in-place families genuinely cannot be.
            var r = new BaselineAugmentReport();
            r.Failed.Add("X: refused");

            string s = r.Summary();

            Assert.Contains("In-place families cannot be edited", s);
            Assert.Contains("ownership released", s);
        }
    }
}
