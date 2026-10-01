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
}
