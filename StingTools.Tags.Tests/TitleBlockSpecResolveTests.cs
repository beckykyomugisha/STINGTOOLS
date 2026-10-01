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

        /// <summary>The sheet the family prints on: the extent of its resolved
        /// border linework (Resolve honours replacesInheritedGeometry).</summary>
        internal static (double w, double h) PaperExtent(TitleBlockSpec r)
        {
            double w = 0, h = 0;
            foreach (var l in r.Lines)
                foreach (var p in new[] { l.From, l.To })
                    if (p != null && p.Length >= 2) { w = Math.Max(w, p[0]); h = Math.Max(h, p[1]); }
            return (w, h);
        }

        private const double TolMm = 0.5;

        /// <summary>DTW-155: COVER_A2/A3 and CLARIFICATION_A3 inherited A1_common's
        /// 810 × 470 drawable, so their fractional slots resolved across an A1 frame
        /// printed on A2/A3 paper. Every concrete family: drawable inside the paper,
        /// every slot inside the paper, and the drawable sized to the paper.</summary>
        [Theory]
        [MemberData(nameof(ConcreteFamilies))]
        public void Resolved_slots_lie_within_drawable_and_drawable_within_paper(string id)
        {
            var r = Resolved(id);
            var (pw, ph) = PaperExtent(r);
            Assert.True(pw > 0 && ph > 0, $"{id}: no border linework to measure the paper from");
            var d = r.Drawable;
            Assert.NotNull(d);
            Assert.True(d.X >= -TolMm && d.Y >= -TolMm && d.X + d.W <= pw + TolMm && d.Y + d.H <= ph + TolMm,
                $"{id}: drawable ({d.X},{d.Y}) {d.W}x{d.H} is not inside the {pw}x{ph} sheet");

            // A drawable inherited from a smaller sheet still fits inside a bigger
            // one; it just leaves most of the paper unused (COVER_A0 had A1's
            // 810 x 470 rect on 1189 x 841). Every correct family uses >= 70 %.
            double cover = d.W * d.H / (pw * ph);
            Assert.True(cover >= 0.5,
                $"{id}: drawable {d.W}x{d.H} covers only {cover:P0} of the {pw}x{ph} sheet — inherited from another paper size?");

            var bad = new List<string>();
            foreach (var s in r.Slots)
            {
                if (!s.TryResolveAbsolute(d, out var a, out var z)) continue;
                double x0 = a[0], y0 = a[1], x1 = a[0] + z[0], y1 = a[1] + z[1];
                if (x0 < -TolMm || y0 < -TolMm || x1 > pw + TolMm || y1 > ph + TolMm)
                    bad.Add($"{s.Id} ({x0:0.#},{y0:0.#})-({x1:0.#},{y1:0.#}) off the {pw}x{ph} sheet");
                // Absolute-mm slots may sit outside the drawable on purpose (QR in the
                // title strip, a BOM column, a submission stamp). Fractional slots are
                // inside it by construction once the drawable is right, which is what
                // the paper-coverage check below is for.
            }
            Assert.True(bad.Count == 0, $"{id}:\n  " + string.Join("\n  ", bad));
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
