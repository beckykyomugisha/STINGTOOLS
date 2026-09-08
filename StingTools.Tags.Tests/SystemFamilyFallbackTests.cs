using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using StingTools.Core.Classification;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-11 — the shipped classification data, driven through the Revit-free
    /// resolvers, at the seam that used to swallow it.
    ///
    /// <para><c>ParameterHelpers.GetFamilyName</c> answered <c>""</c> for every system
    /// element, and <see cref="ProdResolver.Resolve"/> gates its ENTIRE project +
    /// corporate rule lookup on a non-empty family name. So the eleven corporate PROD
    /// rules shipped on Ducts, Floors and Structural Foundations were unreachable — the
    /// data was right, the plumbing dropped it, and nothing said so. These tests assert
    /// the payoff against the shipped CSVs rather than a hand-built fixture, so a row
    /// deleted from the data fails here instead of going quietly dead.</para>
    ///
    /// <para>The Revit half — <c>GetFamilyName</c> itself, and what
    /// <c>ElementType.FamilyName</c> actually returns per category — is NOT exercisable
    /// from a terminal and is not asserted here. What IS asserted is that once a system
    /// family name and a type name arrive, the shipped rules resolve.</para>
    /// </summary>
    public class SystemFamilyFallbackTests
    {
        // Categories whose elements are NOT FamilyInstance, so whose family name could
        // only ever be a SYSTEM family name. Enumerated rather than derived: the Revit
        // API that would answer this is not available here.
        private static readonly HashSet<string> SystemCategories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Walls", "Floors", "Roofs", "Ceilings", "Pipes", "Ducts", "Flex Ducts",
                "Flex Pipes", "Conduits", "Cable Trays", "Stairs", "Railings", "Ramps",
                "Topography", "Toposolid", "Pads", "Curtain Panels", "Curtain Wall Mullions",
            };

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private sealed class ProdRow { public string Prod, Category, Pattern; }

        /// <summary>Mirror of TagConfig.LoadProdCsv — same columns, same upper-casing,
        /// same skip rules. Copied rather than linked because the real one is a private
        /// on a Revit-bound class.</summary>
        private static List<ProdRow> LoadProdRows()
        {
            var rows = new List<ProdRow>();
            bool first = true;
            foreach (string raw in File.ReadLines(Path.Combine(DataDir(), "STING_PROD_CODES.csv")))
            {
                if (first) { first = false; continue; }
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var cols = line.Split(',');
                if (cols.Length < 3) continue;
                string prod = cols[0].Trim(), cat = cols[1].Trim(), pat = cols[2].Trim().ToUpperInvariant();
                if (prod.Length == 0 || cat.Length == 0 || pat.Length == 0) continue;
                rows.Add(new ProdRow { Prod = prod, Category = cat, Pattern = pat });
            }
            Assert.True(rows.Count > 100, "STING_PROD_CODES.csv looks empty: " + rows.Count + " rows");
            return rows;
        }

        private static List<(string Pattern, string ProdCode)> ForCategory(List<ProdRow> rows, string cat)
            => rows.Where(r => string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase))
                   .Select(r => (r.Pattern, r.Prod)).ToList();

        // ── The gap KUT-11 closed ────────────────────────────────────────────────

        /// <summary>Every shipped PROD rule on a SYSTEM category resolves to a corporate
        /// rule once the family and type names arrive — and every one of them falls
        /// through to the category default when the family name is empty, which is what
        /// the plugin did for all of them before KUT-11.</summary>
        [Fact]
        public void SystemCategoryProdRules_WereUnreachable_AndNowResolve()
        {
            var rows = LoadProdRows();
            var systemRows = rows.Where(r => SystemCategories.Contains(r.Category)).ToList();

            // If this ever hits zero the test has stopped proving anything.
            Assert.True(systemRows.Count > 0,
                "No shipped PROD rule sits on a system category — this test no longer guards anything.");

            var prodMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ducts"] = "DCT", ["Floors"] = "FLR", ["Walls"] = "WAL",
            };

            foreach (var row in systemRows)
            {
                // A type name that satisfies the row's own pattern. The shipped patterns
                // are globs like *Concrete Slab* or *Damper*|*Motorised* — take the first
                // alternative and strip the wildcards to get a name it must match.
                string sample = row.Pattern.Split('|')[0].Trim('*');
                Assert.True(sample.Length > 0, "Degenerate pattern on " + row.Category + ": " + row.Pattern);

                var corp = ForCategory(rows, row.Category);

                // AFTER KUT-11: a system family name arrives, so the rule tier is entered.
                string after = ProdResolver.Resolve(
                    familyName: "Floor",              // stand-in system family name
                    typeName: sample,
                    categoryName: row.Category,
                    projRulesForCategory: null,
                    corpRulesForCategory: corp,
                    prodMap: prodMap,
                    source: out string afterSource);
                Assert.True(ProdResolver.IsSpecific(afterSource),
                    $"{row.Category} / '{sample}' resolved as '{afterSource}' ({after}); expected a corporate rule.");

                // BEFORE KUT-11: family name empty, so the whole rule tier is skipped.
                ProdResolver.Resolve(
                    familyName: "",
                    typeName: sample,
                    categoryName: row.Category,
                    projRulesForCategory: null,
                    corpRulesForCategory: corp,
                    prodMap: prodMap,
                    source: out string beforeSource);
                Assert.False(ProdResolver.IsSpecific(beforeSource),
                    $"{row.Category} / '{sample}' resolved specifically with NO family name — the " +
                    "empty-family gate this test describes is gone, so the regression it guards is untestable.");
            }
        }

        /// <summary>The named example: a floor whose type is "Concrete Slab 200" resolves
        /// to SLB rather than to the Floors category default.</summary>
        [Fact]
        public void ConcreteSlabFloor_ResolvesToSlb()
        {
            var rows = LoadProdRows();
            string prod = ProdResolver.Resolve(
                "Floor", "Concrete Slab 200", "Floors",
                null, ForCategory(rows, "Floors"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Floors"] = "FLR" },
                out string source);
            Assert.Equal("SLB", prod);
            Assert.Equal(ProdResolver.Sources.Corporate, source);
        }

        // ── The invariant that keeps the change safe ─────────────────────────────

        /// <summary>
        /// No shipped CSI FamilyRegex row sits on a system category.
        ///
        /// <para>This is the audit the CSV header used to assert in prose, made
        /// executable. It is what makes the KUT-11 change provably inert for CSI: because
        /// GetFamilyName now answers "Basic Wall" where it answered "", a FamilyRegex row
        /// on Walls would START matching — and would match every wall in the model. There
        /// are none, so nothing moves; if someone adds one, this fails rather than the map
        /// silently re-classifying a project.</para>
        /// </summary>
        [Fact]
        public void NoShippedCsiFamilyRegexRowSitsOnASystemCategory()
        {
            var lines = File.ReadAllLines(Path.Combine(DataDir(), "STING_CSI_MASTERFORMAT_MAP.csv"));
            var rules = CsiMasterFormat.ParseCsvLines(lines);
            Assert.True(rules.Count > 100, "CSI map looks empty: " + rules.Count + " rules");

            var offenders = rules
                .Where(r => !string.IsNullOrEmpty(r.FamilyRegex) && SystemCategories.Contains(r.Category))
                .Select(r => $"{r.Category} | {r.FamilyRegex} -> {r.Section}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "FamilyRegex on a system category now matches the SYSTEM FAMILY name (\"Basic Wall\", " +
                "\"Rectangular Duct\"), which is almost never the discriminator intended — put material, " +
                "size and service tests in TypeRegex. Offending rows:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>The guard above is only worth having if it fires. This feeds it exactly
        /// the row it exists to catch.</summary>
        [Fact]
        public void TheSystemCategoryGuard_ActuallyFires()
        {
            var rules = CsiMasterFormat.ParseCsvLines(new[]
            {
                "Category,FamilyRegex,TypeRegex,Sys,Section,Title",
                "Walls,(?i)concrete,,,03 30 00,Cast-in-Place Concrete",
            });
            var offenders = rules
                .Where(r => !string.IsNullOrEmpty(r.FamilyRegex) && SystemCategories.Contains(r.Category))
                .ToList();
            Assert.Single(offenders);
        }
    }
}
