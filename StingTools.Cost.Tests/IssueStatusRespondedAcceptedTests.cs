using StingTools.Core;
using Xunit;

namespace StingTools.Cost.Tests
{
    /// <summary>
    /// IM-9 — RESPONDED and ACCEPTED are real states in this register and now have their own
    /// kinds.
    ///
    /// <para>Before this, both normalised to <c>Unknown</c>, so <c>Canonical()</c> on either
    /// returned <c>"UNKNOWN"</c>. <c>IsOpen</c> still answered correctly for RESPONDED, but
    /// only because Unknown is treated as open — the right answer for the wrong reason, which
    /// is the kind that stops being right the moment somebody tidies the fallback.</para>
    ///
    /// <para><b>The ROADMAP row proposed folding RESPONDED into <c>Resolved</c>. These tests
    /// encode why that would have been wrong</b>: <c>Resolved</c> makes <c>IsOpen</c> false,
    /// and every other reader in the codebase treats RESPONDED as still open.</para>
    /// </summary>
    public class IssueStatusRespondedAcceptedTests
    {
        // ── They have their own kinds ────────────────────────────────────────

        [Theory]
        [InlineData("RESPONDED")]
        [InlineData("Responded")]
        [InlineData("responded")]
        [InlineData("  responded  ")]
        public void RespondedNormalisesToResponded(string raw)
            => Assert.Equal(IssueStatusKind.Responded, IssueStatusNormalizer.Normalize(raw));

        [Theory]
        [InlineData("ACCEPTED")]
        [InlineData("Accepted")]
        [InlineData("accepted")]
        public void AcceptedNormalisesToAccepted(string raw)
            => Assert.Equal(IssueStatusKind.Accepted, IssueStatusNormalizer.Normalize(raw));

        /// <summary>Neither is Unknown any more — the defect the row names.</summary>
        [Theory]
        [InlineData("RESPONDED")]
        [InlineData("ACCEPTED")]
        public void NeitherIsUnknown(string raw)
            => Assert.NotEqual(IssueStatusKind.Unknown, IssueStatusNormalizer.Normalize(raw));

        // ── Canonical round-trips, which is what the exact-match filters need ─

        /// <summary>
        /// <c>Canonical</c> must give each spelling BACK.
        ///
        /// <para>This is the assertion that rules out folding either into an existing kind.
        /// <c>status == "RESPONDED"</c> drives a "Bulk: Close All RESPONDED" action, and
        /// ACCEPTED appears in three terminal-state skip lists. If canonicalising a stored row
        /// rewrote it to IN_PROGRESS or RESOLVED, those filters would stop matching rows they
        /// used to match — silently, because a filter that matches nothing looks like a
        /// register with nothing in that state.</para>
        /// </summary>
        [Theory]
        [InlineData("RESPONDED", "RESPONDED")]
        [InlineData("responded", "RESPONDED")]
        [InlineData("ACCEPTED", "ACCEPTED")]
        [InlineData("accepted", "ACCEPTED")]
        public void CanonicalRoundTrips(string raw, string expected)
            => Assert.Equal(expected, IssueStatusNormalizer.Canonical(raw));

        [Fact]
        public void CanonicalIsStableUnderRepeatedApplication()
        {
            foreach (string raw in new[] { "RESPONDED", "ACCEPTED", "OPEN", "VOID", "RESOLVED" })
            {
                string once = IssueStatusNormalizer.Canonical(raw);
                Assert.Equal(once, IssueStatusNormalizer.Canonical(once));
            }
        }

        // ── Open vs terminal, matching every other reader in the codebase ────

        /// <summary>RESPONDED is STILL OPEN. `BIMManagerCommands` counts an issue open when
        /// its status is `OPEN || IN_PROGRESS || RESPONDED`, and the platform bridge maps it
        /// to "Active". Folding it into Resolved — as the row suggested — would flip this and
        /// hide every issue awaiting acceptance.</summary>
        [Fact]
        public void RespondedIsStillOpen()
        {
            Assert.True(IssueStatusNormalizer.IsOpen("RESPONDED"));
            Assert.False(IssueStatusNormalizer.IsTerminal("RESPONDED"));
            // …and it must NOT have been folded into Resolved, which is not open.
            Assert.NotEqual(IssueStatusKind.Resolved, IssueStatusNormalizer.Normalize("RESPONDED"));
            Assert.False(IssueStatusNormalizer.IsOpen("RESOLVED"));
        }

        /// <summary>ACCEPTED is terminal. Three skip lists already group it with CLOSED and
        /// VOID, and both the BCF and platform bridges map it to "Resolved".</summary>
        [Fact]
        public void AcceptedIsTerminalAndNotOpen()
        {
            Assert.False(IssueStatusNormalizer.IsOpen("ACCEPTED"));
            Assert.True(IssueStatusNormalizer.IsTerminal("ACCEPTED"));
        }

        // ── IsTerminal, the fact that was written out by hand in four places ─

        [Theory]
        [InlineData("CLOSED", true)]
        [InlineData("VOID", true)]
        [InlineData("ACCEPTED", true)]
        [InlineData("OPEN", false)]
        [InlineData("IN_PROGRESS", false)]
        [InlineData("RESPONDED", false)]
        [InlineData("RESOLVED", false)]   // resolved is not yet closed out
        public void IsTerminalMatchesTheSkipListsInTheCodebase(string raw, bool terminal)
            => Assert.Equal(terminal, IssueStatusNormalizer.IsTerminal(raw));

        /// <summary>Unknown must stay OPEN so `has_open_issues` fails safe on a spelling
        /// nobody has taught this class yet — and must not become terminal, which would make
        /// an unrecognised status disappear from the gate entirely.</summary>
        [Fact]
        public void UnknownStillFailsSafe()
        {
            Assert.Equal(IssueStatusKind.Unknown, IssueStatusNormalizer.Normalize("zzz-not-a-status"));
            Assert.True(IssueStatusNormalizer.IsOpen("zzz-not-a-status"));
            Assert.False(IssueStatusNormalizer.IsTerminal("zzz-not-a-status"));
        }

        // ── Nothing that already worked changed ──────────────────────────────

        [Theory]
        [InlineData("OPEN", IssueStatusKind.Open)]
        [InlineData("New", IssueStatusKind.Open)]
        [InlineData("Reopened", IssueStatusKind.Open)]
        [InlineData("In Progress", IssueStatusKind.InProgress)]
        [InlineData("Resolved", IssueStatusKind.Resolved)]
        [InlineData("answered", IssueStatusKind.Resolved)]
        [InlineData("Closed", IssueStatusKind.Closed)]
        [InlineData("Cancelled", IssueStatusKind.Void)]
        [InlineData("REJECTED", IssueStatusKind.Void)]
        public void ExistingSpellingsAreUnchanged(string raw, IssueStatusKind expected)
            => Assert.Equal(expected, IssueStatusNormalizer.Normalize(raw));

        /// <summary>"answered" and "responded" are DIFFERENT. Answered means the work is done;
        /// responded means a reply is on the table and somebody still has to accept it.
        /// Collapsing them is the mistake this whole change avoids.</summary>
        [Fact]
        public void AnsweredAndRespondedAreNotTheSameThing()
        {
            Assert.NotEqual(IssueStatusNormalizer.Normalize("answered"),
                            IssueStatusNormalizer.Normalize("responded"));
            Assert.False(IssueStatusNormalizer.IsOpen("answered"));
            Assert.True(IssueStatusNormalizer.IsOpen("responded"));
        }
    }
}
