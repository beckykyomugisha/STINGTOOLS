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
    }
}
