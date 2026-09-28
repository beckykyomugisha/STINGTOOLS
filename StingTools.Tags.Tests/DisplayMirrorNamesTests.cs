// Load Tag Families repairs a tag family that carries a STING parameter as TEXT when
// the project holds it under its real type, by swapping in the TEXT display mirror.
// The mirror is found from its description in MR_PARAMETERS.txt; these hold that
// lookup and the file to each other.

using System;
using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class DisplayMirrorNamesTests
    {
        private static string Repo(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()))) dir = dir.Parent;
            Assert.True(dir != null, "Could not locate " + string.Join("/", parts));
            return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        }

        [Theory]
        [InlineData("ASS_ELEVATION_M display mirror [auto-generated]", "ASS_ELEVATION_M")]
        [InlineData("  HVC_SIZE_STALE_BOOL display mirror", "HVC_SIZE_STALE_BOOL")]
        [InlineData("Element elevation in metres from project base", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Reads_the_mirrored_parameter_from_the_description(string description, string expected)
            => Assert.Equal(expected, DisplayMirrorNames.MirroredParameter(description));

        // The 24 parameters a project refused on 2026-09-28 when loading the shipped tag
        // families. Each needs a TEXT mirror, or its label field loads empty.
        [Theory]
        [InlineData("PER_EMBODIED_ENERGY_MJ")] [InlineData("HVC_PIPE_PRESSURE_KPA")]
        [InlineData("HVC_DCT_SHOP_DRAWING_REQ_BOOL")] [InlineData("HVC_PIPE_LENGTH_M")]
        [InlineData("ASS_CST_TOTAL_UGX_NR")] [InlineData("HVC_DCT_FLW_CFM")]
        [InlineData("HVC_DUCT_FLOWRATE_M3H")] [InlineData("HVC_DUCT_AREA_SQ_M")]
        [InlineData("ASS_CST_UNIT_PRICE_UGX_NR")] [InlineData("HVC_AIR_CHANGES_PER_HR")]
        [InlineData("HVC_DCT_SUPPORTS_SPACING_MM")] [InlineData("MNT_HGT_MM")]
        [InlineData("PER_RECYCLABILITY_PCT")] [InlineData("PER_EXPECTED_LIFE_YEARS")]
        [InlineData("CST_CALC_AREA_M2")] [InlineData("CST_CALC_LENGTH_M")]
        [InlineData("PER_REPLACEMENT_COST_UGX")] [InlineData("ASS_CST_STALE_BOOL")]
        [InlineData("CST_LOCAL_MAT_BOOL")] [InlineData("HVC_VEL_MPS")]
        [InlineData("HVC_SIZE_STALE_BOOL")] [InlineData("ASS_CRITICALITY_RATING_NR")]
        [InlineData("ASS_ELEVATION_M")]
        public void Refused_tag_parameters_have_a_text_mirror(string name)
        {
            var mirrors = File.ReadAllLines(Repo("StingTools", "Data", "MR_PARAMETERS.txt"))
                .Select(l => l.Split('\t'))
                .Where(p => p.Length > 7 && p[0] == "PARAM" && p[3] == "TEXT")
                .Select(p => DisplayMirrorNames.MirroredParameter(p[7]))
                .Where(n => n != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains(name, mirrors);
        }
    }
}
