using Planscape.API.Services;
using Xunit;

namespace Planscape.Tests;

/// <summary>
/// DSCH-24 — the IFC pset map that IFC ingest reads. The shipped file is the one
/// copy linked from shared/ifc/mappings/ into the API's output, so these tests read
/// the file that actually deploys.
/// </summary>
public class IfcPsetMappingTableTests
{
    private static readonly string ShippedPath = Path.Combine(
        AppContext.BaseDirectory, "Data", "IFC", "STING_IFC_PSET_MAPPING.json");

    private static IReadOnlyList<IfcPsetMappingTable.Entry> Shipped()
    {
        Assert.True(File.Exists(ShippedPath), $"mapping not deployed next to the API: {ShippedPath}");
        return IfcPsetMappingTable.Parse(File.ReadAllText(ShippedPath));
    }

    [Fact]
    public void ShippedMap_ResolvesTheFullTag_FromPset_StingTags()
    {
        var map = Shipped();
        Assert.NotEmpty(map);
        var props = new Dictionary<string, string>
        {
            ["Pset_StingTags.FullTag"] = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003",
        };
        Assert.Equal("M-BLD1-Z01-L02-HVAC-SUP-AHU-0003",
            IfcPsetMappingTable.Resolve(map, props, IfcPsetMappingTable.TagParam, "IfcWall"));
    }

    [Fact]
    public void ShippedMap_DoesNotReadTheTagFromRetiredSources()
    {
        // Pset_Common is not a buildingSMART set, and IfcTag is the IfcElement.Tag
        // attribute (a host element id), not a STING tag. Neither may fill the tag.
        var props = new Dictionary<string, string>
        {
            ["Pset_Common.Tag"] = "X",
            ["IfcTag"] = "Y",
        };
        Assert.Null(IfcPsetMappingTable.Resolve(Shipped(), props, IfcPsetMappingTable.TagParam, "IfcWall"));
    }

    [Fact]
    public void Parse_RefusesTheRetiredFieldSpelling()
    {
        const string json = """[{"sting_param":"ASS_TAG_1_TXT","ifc_pset":"Pset_StingTags","ifc_property":"FullTag"}]""";
        Assert.Throws<InvalidDataException>(() => IfcPsetMappingTable.Parse(json));
    }

    [Fact]
    public void Resolve_FirstRowInFileOrderWins_AndSkipsExportOnlyAndOtherClasses()
    {
        const string json = """
        [
          {"sting_param":"P","pset_name":"A","property_name":"x","direction":"export"},
          {"sting_param":"P","pset_name":"B","property_name":"x","element_types":["IFCDOOR"]},
          {"sting_param":"P","pset_name":"C","property_name":"x"},
          {"sting_param":"P","pset_name":"D","property_name":"x"}
        ]
        """;
        var map = IfcPsetMappingTable.Parse(json);
        var props = new Dictionary<string, string> { ["A.x"] = "a", ["B.x"] = "b", ["C.x"] = "c", ["D.x"] = "d" };

        Assert.Equal("c", IfcPsetMappingTable.Resolve(map, props, "P", "IfcWall"));
        Assert.Equal("b", IfcPsetMappingTable.Resolve(map, props, "P", "IfcDoor"));
    }

    // ── DSCH-38: value translation (value_map), both directions ──────────────

    private const string StatusJson = """
    [
      {"sting_param":"ASS_STATUS_TXT","pset_name":"Pset_WallCommon","property_name":"Status","element_types":["IFCWALL"],
       "value_map":{"NEW":"NEW","EXISTING":"EXISTING","DEMOLISHED":"DEMOLISH","TEMPORARY":"TEMPORARY"}},
      {"sting_param":"ASS_STATUS_TXT","pset_name":"AC_Pset_RenovationInfo","property_name":"RenovationStatus","direction":"import"}
    ]
    """;

    [Theory]
    [InlineData("DEMOLISH", "DEMOLISHED")]
    [InlineData("NEW", "NEW")]
    [InlineData("existing", "EXISTING")]
    [InlineData("TEMPORARY", "TEMPORARY")]
    public void Resolve_TranslatesTheIfcStatus(string ifc, string sting)
    {
        var map = IfcPsetMappingTable.Parse(StatusJson);
        var props = new Dictionary<string, string> { ["Pset_WallCommon.Status"] = ifc };
        var unmapped = new List<string>();
        Assert.Equal(sting, IfcPsetMappingTable.Resolve(map, props, "ASS_STATUS_TXT", "IFCWALL", unmapped));
        Assert.Empty(unmapped);
    }

    [Theory]
    [InlineData("OTHER")]
    [InlineData("NOTKNOWN")]
    [InlineData("UNSET")]
    public void Resolve_UnknownIfcStatus_IsReportedNotStored(string ifc)
    {
        var map = IfcPsetMappingTable.Parse(StatusJson);
        var props = new Dictionary<string, string> { ["Pset_WallCommon.Status"] = ifc };
        var unmapped = new List<string>();
        Assert.Null(IfcPsetMappingTable.Resolve(map, props, "ASS_STATUS_TXT", "IFCWALL", unmapped));
        Assert.Single(unmapped);
        Assert.Contains(ifc, unmapped[0]);
    }

    [Fact]
    public void Resolve_UnknownIfcStatus_FallsThroughToTheNextRow()
    {
        // A later source row still counts; only the untranslatable value is refused.
        var map = IfcPsetMappingTable.Parse(StatusJson);
        var props = new Dictionary<string, string>
        {
            ["Pset_WallCommon.Status"] = "NOTKNOWN",
            ["AC_Pset_RenovationInfo.RenovationStatus"] = "Existing",
        };
        var unmapped = new List<string>();
        Assert.Equal("Existing", IfcPsetMappingTable.Resolve(map, props, "ASS_STATUS_TXT", "IFCWALL", unmapped));
        Assert.Single(unmapped);
    }

    [Fact]
    public void Entry_TranslatesOnExport_AndReportsUnknown()
    {
        var e = IfcPsetMappingTable.Parse(StatusJson)[0];
        Assert.True(e.TryToIfc("DEMOLISHED", out var ifc, out _));
        Assert.Equal("DEMOLISH", ifc);
        Assert.False(e.TryToIfc("RETAINED", out var none, out var why));
        Assert.Null(none);
        Assert.Contains("RETAINED", why);
    }

    [Theory]
    [InlineData("""{"A":"X","B":"X"}""")]
    [InlineData("""{"A":1}""")]
    [InlineData("""{}""")]
    [InlineData("""["A"]""")]
    public void Parse_RefusesABadValueMap(string vm)
    {
        var json = """[{"sting_param":"P","pset_name":"A","property_name":"x","value_map":""" + vm + "}]";
        Assert.Throws<InvalidDataException>(() => IfcPsetMappingTable.Parse(json));
    }

    [Fact]
    public void ShippedMap_EveryStatusRowTranslatesDemolishedBothWays()
    {
        var rows = Shipped().Where(r => r.StingParam == "ASS_STATUS_TXT" && r.PropertyName == "Status").ToList();
        Assert.NotEmpty(rows);
        foreach (var r in rows)
        {
            Assert.NotNull(r.ValueMap);
            Assert.True(r.TryToIfc("DEMOLISHED", out var ifc, out _));
            Assert.Equal("DEMOLISH", ifc);
            Assert.True(r.TryFromIfc("DEMOLISH", out var sting, out _));
            Assert.Equal("DEMOLISHED", sting);
            Assert.False(r.TryFromIfc("NOTKNOWN", out _, out _));
        }
    }
}
