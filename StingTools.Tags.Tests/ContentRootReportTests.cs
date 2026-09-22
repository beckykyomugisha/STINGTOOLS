// Tests for the shadow rule.
//
// The case it was written for, exactly as it happened on 2026-09-21:
// %PROGRAMDATA%/STING/ContentLibrary/Tags held 207 tag families dated 8 August
// and is the SHARED tier; the deployed Data/TagFamilies held 206 corrected that
// same day and is the BASELINE, searched last. The shared library answered every
// lookup, so a full day of corrections was unreachable and the same twelve
// shared-parameter conflicts returned after every fix.
//
// The counts were 207 against 206. That is why the rule judges on DATE and not
// on count - a shadowing library of the same size can still be six weeks stale,
// and here it was slightly LARGER.

using System;
using System.Collections.Generic;
using StingTools.Core.Content;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ContentRootReportTests
    {
        private static ContentRootInfo Root(int rank, string tier, int count, string newest, bool exists = true)
            => new ContentRootInfo
            {
                Rank = rank,
                Tier = tier,
                Path = tier + "-path",
                Exists = exists,
                FamilyCount = count,
                Newest = string.IsNullOrEmpty(newest) ? (DateTime?)null : DateTime.Parse(newest),
            };

        [Fact]
        public void TheRealCaseIsReported()
        {
            var warn = ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "shared",   207, "2026-08-08"),
                Root(2, "baseline", 206, "2026-09-21"),
            });

            Assert.NotNull(warn);
            Assert.Contains("207", warn);
            Assert.Contains("44 day(s) OLDER", warn);      // 8 Aug -> 21 Sep
            Assert.Contains("will NOT be used", warn);
        }

        [Fact]
        public void AnEmptyShadowingRootIsNotAProblem()
        {
            // The folder existing is not the issue - it answering lookups is.
            Assert.Null(ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "shared",   0, null),
                Root(2, "baseline", 206, "2026-09-21"),
            }));
        }

        [Fact]
        public void AMissingShadowingRootIsNotAProblem()
        {
            Assert.Null(ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "shared",   0, null, exists: false),
                Root(2, "baseline", 206, "2026-09-21"),
            }));
        }

        [Fact]
        public void ARootBELOWTheBaselineDoesNotShadowIt()
        {
            // Search order is what matters, not presence.
            Assert.Null(ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "baseline", 206, "2026-09-21"),
                Root(2, "shared",   207, "2026-08-08"),
            }));
        }

        [Fact]
        public void AShadowingRootIsFlaggedEvenWhenItIsNEWER()
        {
            // A current firm library legitimately wins - but the person needs to
            // know it is winning, or a fix to the deployed set looks ignored.
            var warn = ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "shared",   207, "2026-09-22"),
                Root(2, "baseline", 206, "2026-09-21"),
            });

            Assert.NotNull(warn);
            Assert.DoesNotContain("OLDER", warn);          // no false age claim
        }

        [Fact]
        public void AnEmptyBaselineRaisesNothing()
        {
            // Nothing to shadow. Silence is correct here.
            Assert.Null(ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "shared",   207, "2026-08-08"),
                Root(2, "baseline", 0,   null),
            }));
        }

        [Fact]
        public void EverySHADOWINGRootIsNamed()
        {
            var warn = ContentRootReport.ShadowWarning(new List<ContentRootInfo>
            {
                Root(1, "project",  3,   "2026-07-01"),
                Root(2, "shared",   207, "2026-08-08"),
                Root(3, "baseline", 206, "2026-09-21"),
            });

            Assert.Contains("project-path", warn);
            Assert.Contains("shared-path", warn);
        }

        [Fact]
        public void NullsAndEmptyListsAreNotAnError()
        {
            Assert.Null(ContentRootReport.ShadowWarning(null));
            Assert.Null(ContentRootReport.ShadowWarning(new List<ContentRootInfo>()));
            ContentRootReport.LogRoots(null);
        }

        // ── Describe ─────────────────────────────────────────────────────────

        [Fact]
        public void DescribeMarksTheBaselineAndAppendsTagsToTheOthers()
        {
            var roots = new[] { @"C:\shared\root", @"C:\deployed\data\TagFamilies" };
            var info = ContentRootReport.Describe(roots, @"C:\deployed\data\TagFamilies");

            Assert.Equal(2, info.Count);
            Assert.Equal("above-baseline", info[0].Tier);   // not guessed as "project"
            Assert.EndsWith("Tags", info[0].Path);                       // tag folder appended
            Assert.Equal("baseline", info[1].Tier);
            Assert.Equal(@"C:\deployed\data\TagFamilies", info[1].Path); // used as-is
        }

        [Fact]
        public void DescribeIgnoresBlankRootsAndKeepsRankContiguous()
        {
            var info = ContentRootReport.Describe(
                new[] { "", @"C:\a", null, @"C:\b" }, @"C:\b");

            Assert.Equal(2, info.Count);
            Assert.Equal(1, info[0].Rank);
            Assert.Equal(2, info[1].Rank);
        }

        [Fact]
        public void DescribeToleratesNull()
        {
            Assert.Empty(ContentRootReport.Describe(null, null));
        }
    
        // ── stale vs promoted ────────────────────────────────────────────────
        //
        // The MESSAGE is the same either way and is never suppressed: once a
        // shared library wins, a deploy alone stops changing what loads. Only
        // the severity differs, and IsStale is what LogRoots asks.

        [Fact]
        public void AnOlderShadowIsStale()
        {
            // The 2026-09-21 bug: 8 August winning over corrections made today.
            Assert.True(ContentRootReport.IsStale(new List<ContentRootInfo>
            {
                Root(1, "shared",   207, "2026-08-08"),
                Root(2, "baseline", 206, "2026-09-21"),
            }));
        }

        [Fact]
        public void ANewerShadowIsNotStale()
        {
            // Promote Tag Library working as intended - informational, not a
            // warning, or the correct configuration nags on every open.
            Assert.False(ContentRootReport.IsStale(new List<ContentRootInfo>
            {
                Root(1, "shared",   206, "2026-09-22"),
                Root(2, "baseline", 206, "2026-09-21"),
            }));
        }

        [Fact]
        public void ASameDayShadowIsNotStale()
        {
            // What today's promotion actually produces: both sides written
            // minutes apart from the same source.
            Assert.False(ContentRootReport.IsStale(new List<ContentRootInfo>
            {
                Root(1, "shared",   206, "2026-09-22"),
                Root(2, "baseline", 206, "2026-09-22"),
            }));
        }

        [Fact]
        public void AnUnknownDateIsTreatedAsStale()
        {
            // Not knowing how old a library is has never been evidence that it
            // is current, and the cost of guessing wrong here is six weeks of
            // fixes that silently never load.
            Assert.True(ContentRootReport.IsStale(new List<ContentRootInfo>
            {
                Root(1, "shared",   3, null),
                Root(2, "baseline", 206, "2026-09-22"),
            }));
        }

        [Fact]
        public void AnEmptyOrAbsentShadowIsNotStale()
        {
            Assert.False(ContentRootReport.IsStale(new List<ContentRootInfo>
            {
                Root(1, "shared",   0, null, exists: false),
                Root(2, "baseline", 206, "2026-09-22"),
            }));
        }
}
}
