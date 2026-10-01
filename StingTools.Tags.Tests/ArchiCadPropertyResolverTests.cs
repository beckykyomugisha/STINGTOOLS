using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Commands.Interop;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-38 follow-up — the ArchiCAD IFC import reads one STING parameter from an
    /// ordered list of sources: the buildingSMART IFC4 / IFC4X3 property, the IFC2X3
    /// name of the same property, then the vendor name the map used before. The first
    /// source present on the element wins; a later one never overwrites it.
    /// </summary>
    public class ArchiCadPropertyResolverTests
    {
        private static List<AcIfcPropMapping> ShippedMap()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string rel = Path.Combine("StingTools", "Data", "IFC", "ARCHICAD_IFC_MAPPING.json");
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, rel))) dir = dir.Parent;
            Assert.True(dir != null, "Could not locate " + rel);
            return Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, rel)))["property_mappings"]
                .ToObject<List<AcIfcPropMapping>>();
        }

        private static Dictionary<string, string> Props(params (string k, string v)[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private static List<AcIfcPropMapping> Rows(string json) =>
            JsonConvert.DeserializeObject<List<AcIfcPropMapping>>(json);

        [Fact]
        public void FirstSourcePresent_Wins_InFileOrder()
        {
            var rows = Rows(@"[
              {""pset_name"":""A"",""property_name"":""x"",""sting_param"":""P""},
              {""pset_name"":""B"",""property_name"":""x"",""sting_param"":""P""},
              {""pset_name"":""C"",""property_name"":""x"",""sting_param"":""P""}]");
            var both = ArchiCadPropertyResolver.FirstPerTarget(rows, "IFCSPACE", Props(("B.x", "from B"), ("C.x", "from C")));
            var hit = Assert.Single(both);
            Assert.Equal("from B", hit.Value);
            Assert.Equal("B.x", hit.SourceKey);

            // Every candidate is still offered, in file order, so a failed write can fall through.
            var all = ArchiCadPropertyResolver.Candidates(rows, "IFCSPACE", Props(("A.x", "a"), ("C.x", "c"))).ToList();
            Assert.Equal(new[] { "A.x", "C.x" }, all.Select(c => c.SourceKey));
        }

        [Fact]
        public void BlankValuesAndOtherClasses_DoNotCount()
        {
            var rows = Rows(@"[
              {""pset_name"":""A"",""property_name"":""x"",""sting_param"":""P"",""element_types"":[""IFCWALL""]},
              {""pset_name"":""B"",""property_name"":""x"",""sting_param"":""P""},
              {""pset_name"":""C"",""property_name"":""x"",""sting_param"":""P""}]");
            var got = ArchiCadPropertyResolver.FirstPerTarget(rows, "IFCSPACE", Props(("A.x", "wall only"), ("B.x", "  "), ("C.x", "c")));
            Assert.Equal("c", Assert.Single(got).Value);
        }

        [Fact]
        public void ScanAllPsets_MatchesThePropertyInAnySet()
        {
            var rows = Rows(@"[{""pset_name"":"""",""property_name"":""UnitCost"",""sting_param"":""CST_UNIT_COST_NR""}]");
            var got = Assert.Single(ArchiCadPropertyResolver.FirstPerTarget(rows, "IFCWALL", Props(("Custom_Cost.UnitCost", "12.5"))));
            Assert.Equal("Custom_Cost.UnitCost", got.SourceKey);
        }

        public static IEnumerable<object[]> FinishSources() => new[]
        {
            // IFC4 / IFC4X3 buildingSMART
            new object[] { "SPC_FINISH_FLOOR_TXT", "Pset_SpaceCoveringRequirements.FloorCovering" },
            new object[] { "SPC_FINISH_CEILING_TXT", "Pset_SpaceCoveringRequirements.CeilingCovering" },
            new object[] { "SPC_FINISH_WALLS_TXT", "Pset_SpaceCoveringRequirements.WallCovering" },
            new object[] { "SPC_CEILING_HEIGHT_M", "Qto_SpaceBaseQuantities.FinishCeilingHeight" },
            // IFC2X3 buildingSMART
            new object[] { "SPC_FINISH_FLOOR_TXT", "Pset_SpaceCommon.FloorCovering" },
            new object[] { "SPC_FINISH_CEILING_TXT", "Pset_SpaceCommon.CeilingCovering" },
            new object[] { "SPC_FINISH_WALLS_TXT", "Pset_SpaceCommon.WallCovering" },
            new object[] { "SPC_CEILING_HEIGHT_M", "BaseQuantities.ClearHeight" },
            // the vendor names the map used before, last
            new object[] { "SPC_FINISH_FLOOR_TXT", "Pset_SpaceCommon.FinishFloor" },
            new object[] { "SPC_FINISH_CEILING_TXT", "Pset_SpaceCommon.FinishCeiling" },
            new object[] { "SPC_FINISH_WALLS_TXT", "Pset_SpaceCommon.FinishWalls" },
            new object[] { "SPC_CEILING_HEIGHT_M", "Pset_SpaceCommon.CeilingHeight" },
        };

        [Theory]
        [MemberData(nameof(FinishSources))]
        public void ShippedMap_ReadsTheSpaceFinish_FromEachSchemasName(string param, string source)
        {
            var got = ArchiCadPropertyResolver.FirstPerTarget(ShippedMap(), "IFCSPACE", Props((source, "value")))
                .Where(c => c.TargetKey == param).ToList();
            Assert.Equal(source, Assert.Single(got).SourceKey);
        }

        [Fact]
        public void ShippedMap_PrefersTheBuildingSmartName_OverTheIfc2x3AndVendorNames()
        {
            var props = Props(("Pset_SpaceCommon.FinishFloor", "vendor"), ("Pset_SpaceCommon.FloorCovering", "ifc2x3"),
                              ("Pset_SpaceCoveringRequirements.FloorCovering", "ifc4"),
                              ("Pset_SpaceCommon.CeilingHeight", "9"), ("BaseQuantities.ClearHeight", "2.7"));
            var got = ArchiCadPropertyResolver.FirstPerTarget(ShippedMap(), "IFCSPACE", props)
                .ToDictionary(c => c.TargetKey, c => c.Value);
            Assert.Equal("ifc4", got["SPC_FINISH_FLOOR_TXT"]);
            Assert.Equal("2.7", got["SPC_CEILING_HEIGHT_M"]);   // IFC2X3 quantity beats the vendor name
        }

        [Fact]
        public void ShippedMap_SolarGain_ReadsGlazingTypeThenTheVendorName()
        {
            var map = ShippedMap();
            var a = ArchiCadPropertyResolver.FirstPerTarget(map, "IFCWINDOW",
                Props(("Pset_WindowCommon.SolarHeatGainCoefficientGlazing", "0.6"), ("Pset_DoorWindowGlazingType.SolarHeatGainTransmittance", "0.4")));
            Assert.Equal("0.4", a.Single(c => c.TargetKey == "PER_SOLAR_GAIN_NR").Value);
            var b = ArchiCadPropertyResolver.FirstPerTarget(map, "IFCWINDOW",
                Props(("Pset_WindowCommon.SolarHeatGainCoefficientGlazing", "0.6")));
            var vendor = b.Single(c => c.TargetKey == "PER_SOLAR_GAIN_NR");
            Assert.Equal("0.6", vendor.Value);
            Assert.False(string.IsNullOrEmpty(vendor.Mapping.Verify));   // logged as unconfirmed when it matches
        }

        [Fact]
        public void ShippedMap_EveryNonBuildingSmartSourceInTheFallbackGroups_CarriesVerify()
        {
            // The legacy vendor names are kept as last fallbacks only, and say so.
            var legacy = new[] { "FinishFloor", "FinishCeiling", "FinishWalls", "CeilingHeight", "SolarHeatGainCoefficientGlazing" };
            var rows = ShippedMap().Where(m => legacy.Contains(m.ArchiCadProp)).ToList();
            Assert.Equal(5, rows.Count);
            Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Verify), r.ArchiCadProp));
        }

        [Fact]
        public void ShippedMap_KeepsTheWinnerOfEachOverlappingGroup()
        {
            // The import used to let the LAST row with a value win; the groups were
            // reversed when it became first-wins so the same source still wins.
            var map = ShippedMap();
            var space = ArchiCadPropertyResolver.FirstPerTarget(map, "IFCSPACE", Props(
                ("Pset_SpaceCommon.Category", "cat"), ("Pset_SpaceCommon.OccupancyType", "occ"),
                ("AC_Pset_ZoneCategory.ZoneCategoryCode", "zone"),
                ("Pset_SpaceCommon.NetPlannedArea", "10"), ("Qto_SpaceBaseQuantities.NetFloorArea", "11")))
                .ToDictionary(c => c.TargetKey, c => c.Value);
            Assert.Equal("zone", space["ASS_FUNC_TXT"]);
            Assert.Equal("11", space["QTO_NET_FLOOR_AREA_M2"]);
            var wall = ArchiCadPropertyResolver.FirstPerTarget(map, "IFCWALL", Props(
                ("Qto_WallBaseQuantities.GrossSideArea", "5"), ("BaseQuantities.GrossArea", "6")))
                .ToDictionary(c => c.TargetKey, c => c.Value);
            Assert.Equal("6", wall["QTO_GROSS_AREA_M2"]);
        }
    }
}
