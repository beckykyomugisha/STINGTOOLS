// Which library files the fixture resolver may load. It searches whole content
// roots by category name, so a tag, a retired family or a Revit backup must never
// be offered as the fixture.

using StingTools.Core.Content;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ContentFileFilterTests
    {
        [Theory]
        [InlineData(@"C:\Lib\Families\Audio Visual Devices - Speaker.rfa", "Audio Visual Devices", true)]
        [InlineData(@"C:\Lib\Tags\STING - Audio Visual Devices Tag.rfa", "Audio Visual Devices", false)]
        [InlineData(@"C:\Plugin\data\TagFamilies\STING - Plumbing Equipment Tag.rfa", "Plumbing Equipment", false)]
        [InlineData(@"C:\Lib\Families\Plumbing Equipment - Pump.0001.rfa", "Plumbing Equipment", false)]
        [InlineData(@"C:\Lib\Tags\_retired\20260929_1200\Old Plumbing Equipment.rfa", "Plumbing Equipment", false)]
        [InlineData(@"C:\Lib\Tags\STING - Door Tag.rfa", "Door Tags", true)]
        [InlineData("/lib/tags_archive/Door.rfa", "Doors", true)]
        [InlineData("", "Doors", false)]
        public void OnlyLoadableFamiliesAreCandidates(string path, string category, bool expected)
            => Assert.Equal(expected, ContentFileFilter.IsLoadCandidate(path, category));

        [Theory]
        [InlineData(@"C:\a\Tags\x.rfa", "Tags", true)]
        [InlineData("/a/tags/x.rfa", "Tags", true)]
        [InlineData(@"C:\a\Tags.rfa", "Tags", false)]   // the file, not a folder
        [InlineData(@"C:\a\MyTags\x.rfa", "Tags", false)]
        public void FolderMatchIsByWholeSegment(string path, string folder, bool expected)
            => Assert.Equal(expected, ContentFileFilter.UnderFolder(path, folder));
    }
}
