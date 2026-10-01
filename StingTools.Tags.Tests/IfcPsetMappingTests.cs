using System;
using System.IO;
using System.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-24 — the plugin reader of the ONE shared IFC pset map
    /// (shared/ifc/mappings/STING_IFC_PSET_MAPPING.json). Export takes the first
    /// non-import row per parameter in file order; the reader used to keep the LAST
    /// row, so a parameter with several rows exported to whichever came last.
    /// </summary>
    public class IfcPsetMappingTests
    {
        private static string SharedMapPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "shared", "ifc", "mappings", "STING_IFC_PSET_MAPPING.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate shared/ifc/mappings/STING_IFC_PSET_MAPPING.json");
            return Path.Combine(dir.FullName, "shared", "ifc", "mappings", "STING_IFC_PSET_MAPPING.json");
        }

        [Fact]
        public void FirstExportRowWins_InFileOrder()
        {
            const string json = @"[
              {""sting_param"":""P"",""pset_name"":""Imp"",""property_name"":""x"",""direction"":""import""},
              {""sting_param"":""P"",""pset_name"":""First"",""property_name"":""x""},
              {""sting_param"":""P"",""pset_name"":""Second"",""property_name"":""x"",""direction"":""export""}
            ]";
            var rows = IfcPsetMapping.Parse(json);
            Assert.Equal(3, rows.Count);
            Assert.Equal("First", IfcPsetMapping.FirstExport(rows, "P").IfcPsetName);
        }

        [Fact]
        public void EntityRestrictedRows_AreChosenByEntity()
        {
            const string json = @"[
              {""sting_param"":""S"",""pset_name"":""Pset_WallCommon"",""property_name"":""Status"",""element_types"":[""IFCWALL""]},
              {""sting_param"":""S"",""pset_name"":""Pset_DoorCommon"",""property_name"":""Status"",""element_types"":[""IFCDOOR""]}
            ]";
            var rows = IfcPsetMapping.Parse(json);
            Assert.Equal("Pset_DoorCommon", IfcPsetMapping.FirstExport(rows, "S", "IfcDoor").IfcPsetName);
            Assert.Equal("Pset_WallCommon", IfcPsetMapping.FirstExport(rows, "S", "IfcWall").IfcPsetName);
            Assert.Null(IfcPsetMapping.FirstExport(rows, "S", "IfcSlab"));
        }

        [Fact]
        public void RetiredFieldSpelling_IsRefused()
        {
            const string json = @"[{""sting_param"":""P"",""ifc_pset"":""A"",""ifc_property"":""x""}]";
            Assert.Throws<InvalidDataException>(() => IfcPsetMapping.Parse(json));
        }

        [Fact]
        public void SharedMap_ExportsTheTagTokensToPset_StingTags()
        {
            var rows = IfcPsetMapping.Parse(File.ReadAllText(SharedMapPath()));
            foreach (var (param, prop) in new[]
            {
                ("ASS_DISCIPLINE_COD_TXT", "Discipline"), ("ASS_LOC_TXT", "Location"), ("ASS_ZONE_TXT", "Zone"),
                ("ASS_LVL_COD_TXT", "Level"), ("ASS_SYSTEM_TYPE_TXT", "System"), ("ASS_FUNC_TXT", "Function"),
                ("ASS_PRODCT_COD_TXT", "Product"), ("ASS_SEQ_NUM_TXT", "Sequence"), ("ASS_TAG_1_TXT", "FullTag"),
            })
            {
                var e = IfcPsetMapping.FirstExport(rows, param);
                Assert.NotNull(e);
                Assert.Equal("Pset_StingTags", e.IfcPsetName);
                Assert.Equal(prop, e.IfcPropertyName);
            }
            // SEQ is zero-padded text ("0042"); an integer type would drop the padding.
            Assert.Equal("IfcLabel", IfcPsetMapping.FirstExport(rows, "ASS_SEQ_NUM_TXT").IfcDataType);
        }

        [Fact]
        public void SharedMap_EveryRowHasAKnownDirection()
        {
            var rows = IfcPsetMapping.Parse(File.ReadAllText(SharedMapPath()));
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Contains(r.Direction, new[] { "both", "import", "export" }));
        }
        // ── DSCH-38: value translation (value_map) ───────────────────────────

        private const string StatusRow = @"[{""sting_param"":""ASS_STATUS_TXT"",""pset_name"":""Pset_WallCommon"",""property_name"":""Status"",
            ""value_map"":{""NEW"":""NEW"",""EXISTING"":""EXISTING"",""DEMOLISHED"":""DEMOLISH"",""TEMPORARY"":""TEMPORARY""}}]";

        [Theory]
        [InlineData("DEMOLISHED", "DEMOLISH")]
        [InlineData("NEW", "NEW")]
        [InlineData("EXISTING", "EXISTING")]
        [InlineData("TEMPORARY", "TEMPORARY")]
        [InlineData("demolished", "DEMOLISH")]
        public void ValueMap_TranslatesOnExport(string sting, string ifc)
        {
            var e = IfcPsetMapping.Parse(StatusRow)[0];
            Assert.True(e.TryToIfc(sting, out var got, out var reason), reason);
            Assert.Equal(ifc, got);
        }

        [Theory]
        [InlineData("DEMOLISH", "DEMOLISHED")]
        [InlineData("NEW", "NEW")]
        [InlineData("existing", "EXISTING")]
        public void ValueMap_TranslatesOnImport(string ifc, string sting)
        {
            var e = IfcPsetMapping.Parse(StatusRow)[0];
            Assert.True(e.TryFromIfc(ifc, out var got, out var reason), reason);
            Assert.Equal(sting, got);
        }

        [Theory]
        [InlineData("OTHER")]
        [InlineData("NOTKNOWN")]
        [InlineData("UNSET")]
        [InlineData("DEMOLISHED")]   // the STING spelling is not an IFC value
        public void ValueMap_UnknownIfcValue_IsReportedNotGuessed(string ifc)
        {
            var e = IfcPsetMapping.Parse(StatusRow)[0];
            Assert.False(e.TryFromIfc(ifc, out var got, out var reason));
            Assert.Null(got);
            Assert.Contains(ifc, reason);
        }

        [Fact]
        public void ValueMap_UnknownStingValue_IsReportedAndNotFormatted()
        {
            var e = IfcPsetMapping.Parse(StatusRow)[0];
            Assert.False(e.TryToIfc("RETAINED", out var got, out var reason));
            Assert.Null(got);
            Assert.Contains("RETAINED", reason);
            Assert.Null(IfcPsetMapping.FormatStepPropertyValue(e, "RETAINED"));
            Assert.Contains("IFCLABEL('DEMOLISH')", IfcPsetMapping.FormatStepPropertyValue(
                new IfcPsetEntry { StingParam = e.StingParam, IfcPropertyName = "Status", IfcDataType = "IfcLabel", ValueMap = e.ValueMap },
                "DEMOLISHED"));
        }

        [Fact]
        public void NoValueMap_PassesValuesThrough()
        {
            var e = IfcPsetMapping.Parse(@"[{""sting_param"":""P"",""pset_name"":""A"",""property_name"":""x""}]")[0];
            Assert.True(e.TryToIfc("anything", out var a, out _));
            Assert.Equal("anything", a);
            Assert.True(e.TryFromIfc("else", out var b, out _));
            Assert.Equal("else", b);
        }

        [Theory]
        [InlineData(@"{""A"":""X"",""B"":""X""}")]   // two STING values to one IFC value
        [InlineData(@"{""A"":1}")]                    // not a string
        [InlineData(@"{}")]                           // empty
        [InlineData(@"[""A""]")]                      // not an object
        public void BadValueMap_IsRefused(string vm)
        {
            var json = @"[{""sting_param"":""P"",""pset_name"":""A"",""property_name"":""x"",""value_map"":" + vm + "}]";
            Assert.Throws<InvalidDataException>(() => IfcPsetMapping.Parse(json));
        }

        [Fact]
        public void SharedMap_EveryStatusRowTranslatesEveryStingStatus()
        {
            // The values PhaseAutoDetect.DetectStatus writes (TAG_CONFIG_v5_0_VALIDATION VALID_STATUS).
            var rows = IfcPsetMapping.Parse(File.ReadAllText(SharedMapPath()))
                .Where(r => r.StingParam == "ASS_STATUS_TXT" && r.IfcPropertyName == "Status" && r.IsExport)
                .ToList();
            Assert.NotEmpty(rows);
            var ifcEnum = new[] { "NEW", "EXISTING", "DEMOLISH", "TEMPORARY", "OTHER", "NOTKNOWN", "UNSET" };
            foreach (var r in rows)
            {
                Assert.NotNull(r.ValueMap);
                foreach (var s in new[] { "NEW", "EXISTING", "DEMOLISHED", "TEMPORARY" })
                {
                    Assert.True(r.TryToIfc(s, out var ifc, out var why), $"{r.IfcPsetName}: {why}");
                    Assert.Contains(ifc, ifcEnum);
                }
                Assert.True(r.TryToIfc("DEMOLISHED", out var d, out _));
                Assert.Equal("DEMOLISH", d);
            }
        }
    }
}
