// Tests for RevitBackupSweeper.
//
// The first version of this class used Directory.GetFiles with the pattern
// "<stem>.[0-9][0-9][0-9][0-9].rfa". .NET search patterns understand only * and
// ?, so the character class was matched LITERALLY and the call returned nothing
// every single time. It failed silently for exactly the reason this repo keeps
// re-learning: an empty result is not an error. Measured 2026-09-21 - a
// 10-family run left all 10 backups on disk and logged nothing.
//
// These tests exercise the sweep against a REAL temp directory, because the
// defect was in how the filesystem interprets a pattern. A test that mocked the
// directory would have passed against the broken version.

using System;
using System.IO;
using System.Linq;
using StingTools.Commands.TagStudio;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class RevitBackupSweeperTests : IDisposable
    {
        private readonly string _dir;

        public RevitBackupSweeperTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting_sweep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* temp dir, best effort */ }
        }

        private string Touch(string name)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, "x");
            return p;
        }

        [Theory]
        [InlineData("STING - Duct Tag.0001.rfa", true)]
        [InlineData("STING - Duct Tag.0002.rfa", true)]
        [InlineData("STING - Duct Tag.9999.rfa", true)]
        [InlineData("STING - Duct Tag.rfa", false)]
        [InlineData("STING - Panel.TYPE.rfa", false)]   // four chars, not four digits
        [InlineData("STING - Tag.001.rfa", false)]      // three digits
        [InlineData("STING - Tag.00001.rfa", false)]    // five digits
        public void RecognisesOnlyAFourDigitSuffix(string name, bool expected)
        {
            Assert.Equal(expected, RevitBackupSweeper.IsBackupName(Path.Combine(_dir, name)));
        }

        [Fact]
        public void SweepDeletesTheBackupAndKeepsTheFamily()
        {
            string family = Touch("STING - Duct Tag.rfa");
            string backup = Touch("STING - Duct Tag.0001.rfa");

            RevitBackupSweeper.Sweep(family, "test");

            Assert.True(File.Exists(family), "the family itself must survive");
            Assert.False(File.Exists(backup), "the backup must be gone");
        }

        [Fact]
        public void SweepLeavesOtherFamiliesBackupsAlone()
        {
            Touch("STING - Duct Tag.rfa");
            Touch("STING - Duct Tag.0001.rfa");
            string otherBackup = Touch("STING - Pipe Tag.0001.rfa");

            RevitBackupSweeper.Sweep(Path.Combine(_dir, "STING - Duct Tag.rfa"), "test");

            Assert.True(File.Exists(otherBackup), "per-file sweep must not touch another family");
        }

        [Fact]
        public void SweepDoesNotDeleteAFourCharacterNonDigitSuffix()
        {
            string lookalike = Touch("STING - Panel.TYPE.rfa");
            Touch("STING - Panel.rfa");

            RevitBackupSweeper.Sweep(Path.Combine(_dir, "STING - Panel.rfa"), "test");

            Assert.True(File.Exists(lookalike), "? matches any char, so the digit check must hold");
        }

        [Fact]
        public void SweepFolderRemovesEveryBackupAndReportsTheCount()
        {
            for (int i = 1; i <= 3; i++)
            {
                Touch($"Family {i}.rfa");
                Touch($"Family {i}.0001.rfa");
            }
            Touch("Family 1.0002.rfa");

            int removed = RevitBackupSweeper.SweepFolder(_dir, "test");

            Assert.Equal(4, removed);
            Assert.Equal(3, Directory.GetFiles(_dir, "*.rfa").Length);
            Assert.All(Directory.GetFiles(_dir, "*.rfa"),
                       f => Assert.False(RevitBackupSweeper.IsBackupName(f)));
        }

        [Fact]
        public void SweepFolderOnACleanFolderRemovesNothing()
        {
            Touch("Family A.rfa");
            Touch("Family B.rfa");

            Assert.Equal(0, RevitBackupSweeper.SweepFolder(_dir, "test"));
            Assert.Equal(2, Directory.GetFiles(_dir, "*.rfa").Length);
        }

        [Fact]
        public void MissingFolderIsNotAnError()
        {
            Assert.Equal(0, RevitBackupSweeper.SweepFolder(Path.Combine(_dir, "nope"), "test"));
        }

        [Fact]
        public void NullAndEmptyPathsAreNotAnError()
        {
            RevitBackupSweeper.Sweep(null, "test");
            RevitBackupSweeper.Sweep("", "test");
            Assert.Equal(0, RevitBackupSweeper.SweepFolder(null, "test"));
        }
    }
}
