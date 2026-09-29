// Revit saves "<name>.0001.rfa" beside a family; loading one mints a junk family
// named "<name>.0001". Load Tag Families and Repair Tag Library skip them.

using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class RevitBackupFilesTests
    {
        [Theory]
        [InlineData(@"C:\lib\STING - Duct Tag.0001.rfa", true)]
        [InlineData("STING - Air Terminal Tag.0012.rfa", true)]
        [InlineData("STING - Duct Tag.rfa", false)]
        [InlineData("STING - Level 2001 Tag.rfa", false)]
        [InlineData("STING_Tag_Universal.0005.rfa", true)]
        [InlineData("STING - Tag.001.rfa", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Recognises_revit_backups(string path, bool expected)
            => Assert.Equal(expected, RevitBackupFiles.IsBackup(path));
    }
}
