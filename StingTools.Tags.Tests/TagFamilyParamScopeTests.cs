// TAGFAM-9: a parameter added to a tag family is TYPE or INSTANCE by one rule.
// Create Tag Families' AddSharedParameters used to add EVERY parameter as INSTANCE,
// so TAG_STYLE_CODE_TXT — now the only record of a new family's style — had no
// per-type value for the variant writer or the Tag Style Engine to set. The rule
// lives in TagFamilyParamScope (Revit-free) and every adding path must call it.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class TagFamilyParamScopeTests
    {
        private static string Src(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repo root");
            return File.ReadAllText(Path.Combine(new[] { dir.FullName, "StingTools" }.Concat(parts).ToArray()));
        }

        // What TagFamilyConfig.TypeScopedParamNames() holds by default: the depth gates,
        // StyleParams (code + appearance; no switches by default), TAG_POS, the depth cache.
        private static readonly string[] Explicit =
        {
            "TAG_PARA_STATE_1_BOOL", "TAG_PARA_STATE_2_BOOL", "TAG_WARN_VISIBLE_BOOL",
            "TAG_STYLE_CODE_TXT", "TAG_SCALE_TIER_AUTO_BOOL", "TAG_DEPTH_TIER_INT", "STING_TAG_POS",
        };

        [Theory]
        [InlineData("TAG_STYLE_CODE_TXT")]
        [InlineData("TAG_PARA_STATE_1_BOOL")]
        [InlineData("TAG_WARN_VISIBLE_BOOL")]
        [InlineData("TAG_DEPTH_TIER_INT")]
        [InlineData("TAG_BOX_COLOR_R_INT")]
        [InlineData("TAG_BOX_STYLE_TXT")]
        [InlineData("TAG_LEADER_COLOR_B_INT")]
        [InlineData("TAG_2.5NOM_BLACK_BOOL")]      // a switch is type whether or not opted in
        [InlineData("TAG_3.5BOLDITALIC_GREY_BOOL")]
        public void Type_level_params_are_type(string name)
            => Assert.True(TagFamilyParamScope.IsType(name, TagFamilyParamScope.NameSet(Explicit)), name);

        [Theory]
        [InlineData("ASS_TAG_1_TXT")]
        [InlineData("ASS_TAG_7A_TXT")]
        [InlineData("ASS_DESCRIPTION_TXT")]
        [InlineData("ASS_DISCIPLINE_COD_TXT")]
        [InlineData("TAG_7_SECTION_VISIBLE_A_BOOL")] // per-element TAG7 toggle, not a style switch
        [InlineData("ASS_TIEIN_TAG_1_TXT")]
        [InlineData("")]
        public void Per_element_params_stay_instance(string name)
            => Assert.False(TagFamilyParamScope.IsType(name, TagFamilyParamScope.NameSet(Explicit)), name);

        [Fact]
        public void The_name_set_is_case_insensitive()
            => Assert.True(TagFamilyParamScope.IsType("tag_style_code_txt", TagFamilyParamScope.NameSet(Explicit)));

        [Fact]
        public void TypeScopedParamNames_is_built_from_visibility_style_pos_and_depth()
        {
            string src = Src("Tags", "TagFamilyCreatorCommand.cs");
            var m = Regex.Match(src, @"TypeScopedParamNames\(\)\s*=>(?<body>[^;]*);");
            Assert.True(m.Success, "TagFamilyConfig.TypeScopedParamNames not found");
            foreach (var part in new[] { "VisibilityParams", "StyleParams", "ParamRegistry.TAG_POS", "ParamRegistry.TAG_DEPTH_TIER" })
                Assert.Contains(part, m.Groups["body"].Value);
        }

        [Fact]
        public void Create_Tag_Families_AddSharedParameters_routes_type_params_to_type()
        {
            string src = Src("Tags", "TagFamilyCreatorCommand.cs");
            var m = Regex.Match(src, @"private bool AddSharedParameters\(Document famDoc,(?<body>.*?)\n        \}\r?\n",
                RegexOptions.Singleline);
            Assert.True(m.Success, "AddSharedParameters not found");
            string body = m.Groups["body"].Value;
            Assert.Contains("TagFamilyConfig.IsTypeScopedParam(", body);
            // The bug: a literal `true` (instance) for every parameter.
            Assert.DoesNotMatch(@"EnsureFamilyParam\([^)]*,\s*true\s*\)", body);
        }

        [Theory]
        [InlineData("Commands/TagStudio/MigrateTagFamiliesCommand.cs")]
        [InlineData("Tags/FamilyParamCreatorCommand.cs")]
        public void Migrate_and_FamilyParamCreator_use_the_same_rule(string file)
        {
            string src = Src(file.Split('/'));
            Assert.Contains("TagFamilyConfig.IsTypeScopedParam(", src);
            Assert.DoesNotContain("StartsWith(\"TAG_BOX_\"", src);
            Assert.DoesNotContain("StartsWith(\"ASS_TAG\"", src);
        }

        [Fact]
        public void Every_AddSharedParameters_caller_goes_through_the_one_method()
        {
            // Create, the tie-in loops and BuildDeclaredFamily all call AddSharedParameters;
            // none adds tag-family shared params by a side route in that file.
            string src = Src("Tags", "TagFamilyCreatorCommand.cs");
            Assert.True(Regex.Matches(src, @"\bAddSharedParameters\(famDoc,").Count >= 7);
            Assert.Empty(Regex.Matches(src, @"EnsureFamilyParam\(").Cast<Match>().Skip(1));
        }
    }
}
