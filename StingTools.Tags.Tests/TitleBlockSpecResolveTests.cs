using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Resolves every shipped title-block family through the REAL
    /// TitleBlockSpecRegistry.Resolve and checks what the factory would build.
    ///
    /// DTW-149: scalars (templateRft / mode / category) were folded fill-if-blank
    /// root-first, so the ROOT (A1_common) won over the nearer size base and twelve
    /// A0 / A2 / A3 working families were minted from "A1 metric.rft".
    /// </summary>
    public class TitleBlockSpecResolveTests
    {
        internal static TitleBlockLibrary Library()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, "StingTools", "Data", "STING_TITLE_BLOCKS.json");
                if (File.Exists(p)) return JsonConvert.DeserializeObject<TitleBlockLibrary>(File.ReadAllText(p));
                dir = dir.Parent;
            }
            throw new FileNotFoundException("STING_TITLE_BLOCKS.json not found walking up from " + AppContext.BaseDirectory);
        }

        public static IEnumerable<object[]> ConcreteFamilies()
            => Library().Families.Where(f => !f.Abstract).Select(f => new object[] { f.Id });

        internal static TitleBlockSpec Resolved(string id)
        {
            var lib = Library();
            return TitleBlockSpecRegistry.Resolve(lib, lib.Families.First(f => f.Id == id));
        }

        /// <summary>"A3P" → "A3": the portrait marker is not a paper size.</summary>
        internal static string PaperOf(TitleBlockSpec resolved)
        {
            var v = resolved.Parameters.FirstOrDefault(p => p.Name == "PRJ_TB_PAPER_SZ_TXT")?.Default;
            Assert.False(string.IsNullOrEmpty(v), $"{resolved.Id}: no PRJ_TB_PAPER_SZ_TXT default");
            return v.TrimEnd('P', 'p').ToUpperInvariant();
        }

        [Theory]
        [MemberData(nameof(ConcreteFamilies))]
        public void Resolved_template_matches_the_family_paper(string id)
        {
            var r = Resolved(id);
            Assert.False(string.IsNullOrEmpty(r.TemplateRft), $"{id}: no templateRft resolved");
            var m = Regex.Match(Path.GetFileName(r.TemplateRft), @"^(A\d)\b", RegexOptions.IgnoreCase);
            Assert.True(m.Success, $"{id}: cannot read a paper size from template '{r.TemplateRft}'");
            Assert.True(string.Equals(m.Groups[1].Value, PaperOf(r), StringComparison.OrdinalIgnoreCase),
                $"{id}: paper {PaperOf(r)} but minted from '{r.TemplateRft}'");
        }

        [Fact]
        public void Nearer_base_scalar_beats_the_root()
        {
            var lib = new TitleBlockLibrary
            {
                Families =
                {
                    new TitleBlockSpec { Id = "root", Abstract = true, TemplateRft = "A1 metric.rft", Mode = "ROOT" },
                    new TitleBlockSpec { Id = "base", Abstract = true, Extends = "root", TemplateRft = "A3 metric.rft" },
                    new TitleBlockSpec { Id = "leaf", Extends = "base", Mode = "BIM" },
                }
            };
            var r = TitleBlockSpecRegistry.Resolve(lib, lib.Families[2]);
            Assert.Equal("A3 metric.rft", r.TemplateRft);
            Assert.Equal("BIM", r.Mode);
        }
    }
}
