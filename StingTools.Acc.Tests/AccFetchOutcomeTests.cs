// Pure mapping tests for the ACC fetch-outcome discriminator.
//
// These pin the REQUIREMENT — a failure must never classify as "succeeded and empty" —
// not the implementation. They are deliberately NOT the whole story: a pure-function
// test alone would be an alternative-path escape, because the defect being closed lives
// in the CLIENT, which used to map a 404 to an empty list without consulting any
// classifier at all. AccModelCoordLoopbackTests drives the real transport for that.

using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccFetchOutcomeTests
    {
        // ── The five mappings the coordination gate depends on ──────────────────

        [Fact]
        public void Http401_IsAuthFailed()
            => Assert.Equal(AccFetchStatus.AuthFailed, AccFetchOutcome.Classify(401, -1));

        [Fact]
        public void Http403_IsAuthFailed()
            => Assert.Equal(AccFetchStatus.AuthFailed, AccFetchOutcome.Classify(403, -1));

        [Fact]
        public void Http404_IsNotFound()
            => Assert.Equal(AccFetchStatus.NotFound, AccFetchOutcome.Classify(404, -1));

        [Fact]
        public void Http500_IsTransportFailed()
            => Assert.Equal(AccFetchStatus.TransportFailed, AccFetchOutcome.Classify(500, -1));

        [Fact]
        public void Http200_EmptyModelSetArray_IsEmptyOk()
            => Assert.Equal(AccFetchStatus.EmptyOk,
                AccFetchOutcome.ClassifyArrayBody(200, "{\"modelSets\":[]}", "modelSets", "results"));

        [Fact]
        public void Http200_OneModelSet_IsOk()
            => Assert.Equal(AccFetchStatus.Ok,
                AccFetchOutcome.ClassifyArrayBody(200,
                    "{\"modelSets\":[{\"modelSetId\":\"ms-1\",\"name\":\"KUT Federated\"}]}",
                    "modelSets", "results"));

        // ── No failure may masquerade as EmptyOk ────────────────────────────────

        [Theory]
        [InlineData(400)]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(404)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(502)]
        [InlineData(503)]
        public void NoErrorStatus_EverClassifiesAsEmptyOkOrOk(int httpStatus)
        {
            // Even if a caller reports "I parsed zero items", an error status must not
            // become a success. That conflation is the whole defect.
            Assert.NotEqual(AccFetchStatus.EmptyOk, AccFetchOutcome.Classify(httpStatus, 0));
            Assert.NotEqual(AccFetchStatus.Ok, AccFetchOutcome.Classify(httpStatus, 5));
            Assert.NotEqual(AccFetchStatus.EmptyOk,
                AccFetchOutcome.ClassifyArrayBody(httpStatus, "{\"modelSets\":[]}", "modelSets"));
        }

        [Fact]
        public void Http200_WithUnparseableBody_IsTransportFailed()
            => Assert.Equal(AccFetchStatus.TransportFailed,
                AccFetchOutcome.ClassifyArrayBody(200, "<html>gateway</html>", "modelSets"));

        [Fact]
        public void Http200_MissingTheExpectedArray_IsTransportFailed_NotEmptyOk()
        {
            // A changed APS sub-path answers 200 with a payload of a different shape.
            // Reading that as "no clashes" is how a wrong path looks like a clean model.
            var status = AccFetchOutcome.ClassifyArrayBody(200, "{\"data\":{\"other\":1}}", "modelSets", "results");
            Assert.Equal(AccFetchStatus.TransportFailed, status);
            Assert.NotEqual(AccFetchStatus.EmptyOk, status);
        }

        [Fact]
        public void Http200_BareArray_IsAccepted()
        {
            Assert.Equal(AccFetchStatus.EmptyOk, AccFetchOutcome.ClassifyArrayBody(200, "[]", "modelSets"));
            Assert.Equal(AccFetchStatus.Ok, AccFetchOutcome.ClassifyArrayBody(200, "[{\"id\":\"a\"}]", "modelSets"));
        }

        // ── The result carrier ──────────────────────────────────────────────────

        [Fact]
        public void Succeeded_IsTrueOnlyForOkAndEmptyOk()
        {
            Assert.True(AccFetchResult<int>.Success(1, empty: false).Succeeded);
            Assert.True(AccFetchResult<int>.Success(0, empty: true).Succeeded);
            Assert.False(AccFetchResult<int>.Failure(AccFetchStatus.AuthFailed, 0, 401, "x").Succeeded);
            Assert.False(AccFetchResult<int>.Failure(AccFetchStatus.NotFound, 0, 404, "x").Succeeded);
            Assert.False(AccFetchResult<int>.Failure(AccFetchStatus.TransportFailed, 0, 500, "x").Succeeded);
        }

        [Fact]
        public void FailureDescriptions_NeverSayNothingWasFound()
        {
            // A user reading the dialog must not be able to mistake a failure for a
            // clean result, so no failure description may use the empty-result words.
            foreach (var status in new[] { AccFetchStatus.AuthFailed, AccFetchStatus.NotFound, AccFetchStatus.TransportFailed })
            {
                string text = AccFetchOutcome.Describe(status, 404).ToLowerInvariant();
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("clash-clean", text);
                Assert.DoesNotContain("no clashes", text);
                Assert.DoesNotContain("returned nothing", text);
            }
        }

        [Fact]
        public void CountArray_ReportsRealCounts()
        {
            Assert.Equal(3, AccFetchOutcome.CountArray("{\"tests\":[1,2,3]}", "tests"));
            Assert.Equal(0, AccFetchOutcome.CountArray("{\"tests\":[]}", "tests"));
            Assert.Equal(-1, AccFetchOutcome.CountArray("{\"tests\":{}}", "tests"));
            Assert.Equal(-1, AccFetchOutcome.CountArray("not json", "tests"));
            Assert.Equal(-1, AccFetchOutcome.CountArray(null, "tests"));
        }
    }
}
