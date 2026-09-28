// Tests for the promotion plan, against a REAL temp directory - the thing being
// judged is file content, and a mocked filesystem would prove nothing about it.
//
// The refusal is the feature. The shared root is searched before the deployed
// library and a deploy cannot touch it, so a promotion of uncommitted work wins
// every lookup until someone promotes again. That is precisely how a library
// from 8 August shadowed six weeks of corrections.

using System;
using System.IO;
using System.Linq;
using StingTools.Core.Content;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagLibraryPromotionTests : IDisposable
    {
        private readonly string _root;

        public TagLibraryPromotionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "sting_promote_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        { try { Directory.Delete(_root, true); } catch { /* temp, best effort */ } }

        private string Dir(string name)
        {
            string d = Path.Combine(_root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        private static void Fam(string dir, string name, string content)
            => File.WriteAllText(Path.Combine(dir, name), content);

        // ── the refusal ──────────────────────────────────────────────────────

        [Fact]
        public void ASourceThatDiffersFromGitIsRefused()
        {
            string src = Dir("src"), tgt = Dir("tgt"), git = Dir("git");
            Fam(src, "A.rfa", "propagated-today");
            Fam(git, "A.rfa", "committed-yesterday");

            var plan = TagLibraryPromotion.Plan(src, tgt, git);

            Assert.False(plan.CanProceed);
            Assert.Contains("differs from the version-controlled copy", plan.Blockers[0]);
            Assert.Contains("A.rfa", plan.Blockers[0]);
        }

        [Fact]
        public void AFamilyMissingFromGitIsAlsoDrift()
        {
            // Generated but never committed - the commonest way to publish
            // something nobody can reproduce.
            string src = Dir("src"), tgt = Dir("tgt"), git = Dir("git");
            Fam(src, "A.rfa", "x"); Fam(src, "B.rfa", "new");
            Fam(git, "A.rfa", "x");

            var plan = TagLibraryPromotion.Plan(src, tgt, git);

            Assert.False(plan.CanProceed);
            Assert.Contains("B.rfa", plan.Blockers[0]);
        }

        [Fact]
        public void AMatchingSourceProceeds()
        {
            string src = Dir("src"), tgt = Dir("tgt"), git = Dir("git");
            Fam(src, "A.rfa", "same"); Fam(git, "A.rfa", "same");

            var plan = TagLibraryPromotion.Plan(src, tgt, git);

            Assert.True(plan.CanProceed);
            Assert.Equal(new[] { "A.rfa" }, plan.ToAdd);
        }

        [Fact]
        public void TheGitCheckIsSkippedWhenThereIsNoCheckout()
        {
            // An end-user machine has no repository. Refusing there would block
            // the people the library exists for.
            string src = Dir("src"), tgt = Dir("tgt");
            Fam(src, "A.rfa", "x");

            Assert.True(TagLibraryPromotion.Plan(src, tgt, null).CanProceed);
            Assert.True(TagLibraryPromotion.Plan(src, tgt, Path.Combine(_root, "nope")).CanProceed);
        }

        [Fact]
        public void AnEmptySourceIsRefused()
        {
            // Publishing nothing over a working library is the worst outcome
            // available, and it would otherwise look like a clean run.
            string src = Dir("src"), tgt = Dir("tgt");
            Fam(tgt, "A.rfa", "the library people are using");

            var plan = TagLibraryPromotion.Plan(src, tgt, null);

            Assert.False(plan.CanProceed);
            Assert.Contains("holds no .rfa", plan.Blockers[0]);
        }

        [Fact]
        public void AMissingSourceIsRefused()
        {
            var plan = TagLibraryPromotion.Plan(Path.Combine(_root, "ghost"), Dir("tgt"), null);
            Assert.False(plan.CanProceed);
            Assert.Contains("does not exist", plan.Blockers[0]);
        }

        // ── the plan ─────────────────────────────────────────────────────────

        [Fact]
        public void AddUpdateAndUnchangedAreSeparated()
        {
            string src = Dir("src"), tgt = Dir("tgt");
            Fam(src, "new.rfa", "n");
            Fam(src, "same.rfa", "s");    Fam(tgt, "same.rfa", "s");
            Fam(src, "changed.rfa", "v2"); Fam(tgt, "changed.rfa", "v1");

            var plan = TagLibraryPromotion.Plan(src, tgt, null);

            Assert.Equal(new[] { "new.rfa" }, plan.ToAdd);
            Assert.Equal(new[] { "changed.rfa" }, plan.ToUpdate);
            Assert.Equal(new[] { "same.rfa" }, plan.Unchanged);
            Assert.Equal(2, plan.WouldWrite);
        }

        [Fact]
        public void AFamilyOnlyInTheTargetIsReportedAndNeverDeleted()
        {
            // It may be stale, or it may be another team's. This command cannot
            // tell, so it keeps it and says so.
            string src = Dir("src"), tgt = Dir("tgt");
            Fam(src, "A.rfa", "a");
            Fam(tgt, "theirs.rfa", "t");

            var plan = TagLibraryPromotion.Plan(src, tgt, null);

            Assert.Equal(new[] { "theirs.rfa" }, plan.ExtraInTarget);
            Assert.True(plan.CanProceed);
            Assert.True(File.Exists(Path.Combine(tgt, "theirs.rfa")));
        }

        [Fact]
        public void RevitBackupsAreNotFamilies()
        {
            string src = Dir("src"), tgt = Dir("tgt");
            Fam(src, "A.rfa", "a"); Fam(src, "A.0001.rfa", "backup");

            var plan = TagLibraryPromotion.Plan(src, tgt, null);

            Assert.Equal(new[] { "A.rfa" }, plan.ToAdd);
        }

        [Theory]
        [InlineData("A.0001.rfa", true)]
        [InlineData("A.9999.rfa", true)]
        [InlineData("A.rfa", false)]
        [InlineData("Panel.TYPE.rfa", false)]
        [InlineData("A.001.rfa", false)]
        public void BackupNamesNeedFourDigits(string name, bool expected)
            => Assert.Equal(expected, TagLibraryPromotion.IsRevitBackupName(name));

        // ── manifest ─────────────────────────────────────────────────────────

        [Fact]
        public void TheManifestRecordsWhatWasPublishedAndFromWhere()
        {
            string src = Dir("src"), tgt = Dir("tgt");
            Fam(src, "new.rfa", "n");
            Fam(src, "changed.rfa", "v2"); Fam(tgt, "changed.rfa", "v1");
            Fam(tgt, "theirs.rfa", "t");

            var plan = TagLibraryPromotion.Plan(src, tgt, null);
            string m = TagLibraryPromotion.BuildManifest(plan, "tester", new DateTime(2026, 9, 21, 20, 30, 0, DateTimeKind.Utc));

            Assert.Contains("promotedBy=tester", m);
            Assert.Contains("2026-09-21T20:30:00Z", m);
            Assert.Contains("source=" + src, m);
            Assert.Contains("ADD\tnew.rfa", m);
            Assert.Contains("UPD\tchanged.rfa", m);
            Assert.Contains("KEPT\ttheirs.rfa", m);
        }

        // ── hashing ──────────────────────────────────────────────────────────

        [Fact]
        public void AnUnreadableFileNeverComparesEqual()
        {
            // A locked or corrupt family passing as "unchanged" would silently
            // leave the old one in the shared library.
            string a = Dir("a"), b = Dir("b");
            Fam(a, "X.rfa", "content"); Fam(b, "X.rfa", "content");

            using (File.Open(Path.Combine(a, "X.rfa"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var diff = TagLibraryPromotion.Compare(
                    TagLibraryPromotion.Families(a), TagLibraryPromotion.Families(b));
                Assert.Contains("X.rfa", diff);
            }
        }

        [Fact]
        public void CompareToleratesNulls()
        {
            Assert.Empty(TagLibraryPromotion.Compare(null, null));
        }
    }
}
