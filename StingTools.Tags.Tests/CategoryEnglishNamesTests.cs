// ══════════════════════════════════════════════════════════════════════════
//  CategoryEnglishNamesTests.cs — TAGACC-6.
//
//  Every tagging table is keyed on the English category display name, and
//  Category.Name is localised, so a non-English Revit tagged nothing. The
//  English name is derived from the BuiltInCategory enum name; these pin the
//  exceptions Revit's naming does not follow.
// ══════════════════════════════════════════════════════════════════════════
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class CategoryEnglishNamesTests
    {
        [Theory]
        [InlineData("OST_MechanicalEquipment", "Mechanical Equipment")]
        [InlineData("OST_ElectricalFixtures", "Electrical Fixtures")]
        [InlineData("OST_LightingFixtures", "Lighting Fixtures")]
        [InlineData("OST_PlumbingFixtures", "Plumbing Fixtures")]
        [InlineData("OST_Doors", "Doors")]
        [InlineData("OST_Walls", "Walls")]
        [InlineData("OST_StructuralFraming", "Structural Framing")]
        [InlineData("OST_DuctCurves", "Ducts")]
        [InlineData("OST_PipeCurves", "Pipes")]
        [InlineData("OST_DuctTerminal", "Air Terminals")]
        [InlineData("OST_DuctAccessory", "Duct Accessories")]
        [InlineData("OST_PipeFitting", "Pipe Fittings")]
        [InlineData("OST_CableTray", "Cable Trays")]
        [InlineData("OST_Conduit", "Conduits")]
        [InlineData("OST_StructColumns", "Structural Columns")]
        [InlineData("OST_StructuralFoundation", "Structural Foundations")]
        [InlineData("OST_SpecialityEquipment", "Specialty Equipment")]
        [InlineData("OST_GenericModel", "Generic Models")]
        public void The_first_candidate_is_the_english_display_name(string enumName, string expected)
        {
            var c = CategoryEnglishNames.Candidates(enumName);
            Assert.NotEmpty(c);
            Assert.Equal(expected, c[0]);
        }

        [Fact]
        public void A_singular_enum_name_also_offers_the_plural()
        {
            Assert.Contains("Generic Models", CategoryEnglishNames.Candidates("OST_GenericModel"));
            Assert.Contains("Communication Devices", CategoryEnglishNames.Candidates("OST_CommunicationDevices"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("INVALID")]
        [InlineData("OST_")]
        public void Non_category_names_give_no_candidates(string enumName)
        {
            Assert.Empty(CategoryEnglishNames.Candidates(enumName));
        }

        [Theory]
        [InlineData("MechanicalEquipment", "Mechanical Equipment")]
        [InlineData("MEPSpaces", "MEP Spaces")]
        [InlineData("Doors", "Doors")]
        public void Camel_case_is_split_on_word_boundaries(string input, string expected)
        {
            Assert.Equal(expected, CategoryEnglishNames.SplitCamel(input));
        }
    }
}
