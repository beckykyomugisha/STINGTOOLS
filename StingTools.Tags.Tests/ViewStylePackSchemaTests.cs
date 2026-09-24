// V-12a — STING_VIEW_STYLE_PACKS.json has declared "schemaVersion": "1.3" while
// ViewStylePackLibrary bound only an int "version" the file never carried, so
// packs had no version gate at all while the AEC filters did.

using System;
using System.IO;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class ViewStylePackSchemaTests
    {
        private static ViewStylePackLibrary Shipped()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            var lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(
                File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Data", "STING_VIEW_STYLE_PACKS.json")));
            Assert.True(lib?.Packs != null && lib.Packs.Count > 30, "shipped pack library did not deserialise");
            return lib;
        }

        [Fact]
        public void ShippedSchemaVersionIsBoundAndEqualsTheSupportedVersion()
        {
            // Equal, not merely <=: a field added to the file with a bumped
            // schemaVersion must bump SupportedSchemaVersion in the same change.
            var lib = Shipped();
            Assert.Equal(ViewStylePackLibrary.SupportedSchemaVersion, lib.SchemaVersion);
            Assert.Null(ViewStylePackLibrary.SchemaGateWarning(lib.SchemaVersion, "shipped"));
        }

        [Theory]
        [InlineData("1.4")]
        [InlineData("2.0")]
        [InlineData("1.3.1")]
        public void NewerSchemaWarns(string declared)
            => Assert.Contains("newer than this plugin supports",
                   ViewStylePackLibrary.SchemaGateWarning(declared, "f") ?? "");

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.0")]
        [InlineData("1.3")]
        public void CurrentOlderOrUndeclaredSchemaIsAccepted(string declared)
            => Assert.Null(ViewStylePackLibrary.SchemaGateWarning(declared, "f"));

        [Fact]
        public void UnparseableSchemaWarns()
            => Assert.Contains("not a version number",
                   ViewStylePackLibrary.SchemaGateWarning("one-point-three", "f") ?? "");
    }
}
