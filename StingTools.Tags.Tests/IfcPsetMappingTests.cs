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
            // ── DSCH-38: every row names a property that exists ──────────────────
        //
        // Pset_ / Qto_ are reserved for sets the IFC specification declares (IFC 4.3
        // IfcPropertySet). A row naming a Pset_* / Qto_* property buildingSMART does not
        // define writes into a reserved namespace and is read by nothing; a row naming a
        // Pset_Sting* property the STING contract does not define is the same failure in
        // our own namespace. Both are now build failures, not 'verify' notes.

        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray())))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate " + string.Join("/", parts));
            return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        }

        /// <summary>schema -> set name -> property names, from the generated index
        /// (tools/enums/bsi_pset_index.py, buildingSMART PSD via ifcopenshell).</summary>
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>> BsiIndex()
        {
            var doc = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
                RepoFile("shared", "ifc", "mappings", "BUILDINGSMART_PSET_INDEX.json")));
            var idx = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>>();
            foreach (var s in (Newtonsoft.Json.Linq.JArray)doc["sets"])
            {
                var schema = (string)s["schema"];
                if (!idx.TryGetValue(schema, out var sets)) idx[schema] = sets = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
                sets[(string)s["name"]] = new System.Collections.Generic.HashSet<string>(s["properties"].Select(p => (string)p));
            }
            return idx;
        }

        private static bool IsReservedTarget(string pset) =>
            (pset.StartsWith("Pset_", StringComparison.Ordinal) || pset.StartsWith("Qto_", StringComparison.Ordinal))
            && !pset.StartsWith("Pset_Sting", StringComparison.Ordinal);

        /// <summary>Rows whose Pset_* / Qto_* target is not a buildingSMART property in
        /// both IFC4 and IFC4X3 (the map does not say which schema it writes).</summary>
        private static System.Collections.Generic.List<string> NonBsiTargets(System.Collections.Generic.IEnumerable<IfcPsetEntry> rows)
        {
            var idx = BsiIndex();
            var bad = new System.Collections.Generic.List<string>();
            foreach (var r in rows.Where(r => IsReservedTarget(r.IfcPsetName)))
                foreach (var schema in new[] { "IFC4", "IFC4X3" })
                    if (!idx[schema].TryGetValue(r.IfcPsetName, out var props) || !props.Contains(r.IfcPropertyName))
                        bad.Add($"{r.StingParam} -> {r.IfcPsetName}.{r.IfcPropertyName} is not a buildingSMART {schema} property");
            return bad;
        }

        [Fact]
        public void BsiIndex_IsPopulated_ForBothSchemas()
        {
            // Guards the check below against passing over an empty index.
            var idx = BsiIndex();
            Assert.True(idx["IFC4"].Count > 400, $"IFC4 sets: {idx["IFC4"].Count}");
            Assert.True(idx["IFC4X3"].Count > 600, $"IFC4X3 sets: {idx["IFC4X3"].Count}");
            Assert.Contains("Status", idx["IFC4"]["Pset_WallCommon"]);
            Assert.Contains("NetWeight", idx["IFC4X3"]["Qto_BeamBaseQuantities"]);
        }

        [Fact]
        public void NonBsiTargets_FlagsAnUnknownSetAndAnUnknownProperty()
        {
            var rows = IfcPsetMapping.Parse(@"[
              {""sting_param"":""A"",""pset_name"":""Pset_ConstructionOperation"",""property_name"":""Revision""},
              {""sting_param"":""B"",""pset_name"":""Pset_WindowCommon"",""property_name"":""FrameMaterial""},
              {""sting_param"":""C"",""pset_name"":""Qto_BeamBaseQuantities"",""property_name"":""Weight""},
              {""sting_param"":""D"",""pset_name"":""Pset_WallCommon"",""property_name"":""Status""},
              {""sting_param"":""E"",""pset_name"":""AC_Pset_ElementID"",""property_name"":""ID""},
              {""sting_param"":""F"",""pset_name"":""Pset_StingTags"",""property_name"":""Discipline""}
            ]");
            var bad = NonBsiTargets(rows);
            Assert.Equal(6, bad.Count);   // A, B, C, each in IFC4 and IFC4X3
            Assert.All(new[] { "A ->", "B ->", "C ->" }, p => Assert.Contains(bad, b => b.StartsWith(p)));
            Assert.DoesNotContain(bad, b => b.StartsWith("D ->") || b.StartsWith("E ->") || b.StartsWith("F ->"));
        }

        [Fact]
        public void SharedMap_NoRowTargetsAPsetOrQtoPropertyThatBuildingSmartDoesNotDefine()
        {
            var bad = NonBsiTargets(IfcPsetMapping.Parse(File.ReadAllText(SharedMapPath())));
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        /// <summary>Pset_Sting* name -> property name -> DataType, from shared/ifc/psets.</summary>
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> StingContract()
        {
            System.Xml.Linq.XNamespace ns = "https://stingtools.io/schema/ifc/psets/v1";
            var dir = Path.GetDirectoryName(RepoFile("shared", "ifc", "psets", "_manifest.json"));
            var map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
            foreach (var f in Directory.GetFiles(dir, "Pset_Sting*.xml"))
            {
                var root = System.Xml.Linq.XDocument.Load(f).Root;
                var name = (string)root.Element(ns + "Identity").Element(ns + "Name");
                map[name] = root.Element(ns + "Properties").Elements(ns + "Property")
                    .ToDictionary(p => (string)p.Attribute("name"), p => ((string)p.Element(ns + "DataType") ?? "IfcLabel").Trim());
            }
            return map;
        }

        [Fact]
        public void SharedMap_EveryPset_StingRowNamesAPropertyOfTheStingContract_WithItsDataType()
        {
            var contract = StingContract();
            Assert.True(contract.Count >= 8, $"Pset_Sting* templates found: {contract.Count}");
            var raw = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(SharedMapPath()));
            var bad = new System.Collections.Generic.List<string>();
            int checkedRows = 0;
            foreach (var r in raw)
            {
                var pset = (string)r["pset_name"] ?? "";
                if (!pset.StartsWith("Pset_Sting", StringComparison.Ordinal)) continue;
                checkedRows++;
                var prop = (string)r["property_name"];
                if (!contract.TryGetValue(pset, out var props)) { bad.Add($"{r["sting_param"]}: {pset} has no shared/ifc/psets/{pset}.xml"); continue; }
                if (!props.TryGetValue(prop, out var dt)) { bad.Add($"{r["sting_param"]}: {pset}.{prop} is not in {pset}.xml"); continue; }
                var rowType = (string)r["ifc_data_type"];
                if (rowType != null && rowType != dt) bad.Add($"{r["sting_param"]}: {pset}.{prop} is {rowType} in the map but {dt} in {pset}.xml");
            }
            Assert.True(checkedRows >= 10, $"Pset_Sting* rows checked: {checkedRows}");
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }
    }
}
