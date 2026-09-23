// A return diffuser was tagged SUP because "diffuser" was treated as a
// direction. Both halves of the fix are guarded here: the C# rule, and the
// shipped PROD_CODES rows that decide the product code for the same element.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class HvacDirectionFromNameTests
    {
        [Theory]
        // The measured defect: RETURN and DIFFUSER in one name.
        [InlineData("M_Return Diffuser", "RTN")]
        [InlineData("M_Return Air Diffuser", "RTN")]
        [InlineData("Return Grille", "RTN")]
        [InlineData("M_Exhaust Diffuser", "EXH")]
        [InlineData("Extract Grille", "EXH")]
        [InlineData("Fresh Air Louvre", "FRA")]
        [InlineData("Outside Air Intake", "FRA")]
        // Direction stated plainly still wins.
        [InlineData("M_Supply Diffuser", "SUP")]
        [InlineData("Supply Air Terminal", "SUP")]
        // Shape with no direction: the weak default is allowed, and only here.
        [InlineData("M_Diffuser", "SUP")]
        [InlineData("Square Diffuser 600x600", "SUP")]
        public void DirectionBeatsShape(string family, string expected)
            => Assert.Equal(expected, HvacDirectionFromName.Resolve(family));

        [Theory]
        [InlineData("m_return diffuser", "RTN")]   // lookup is case-insensitive
        [InlineData("M_RETURN DIFFUSER", "RTN")]
        public void CaseDoesNotChangeTheAnswer(string family, string expected)
            => Assert.Equal(expected, HvacDirectionFromName.Resolve(family));

        [Theory]
        [InlineData("M_Pipe Fitting")]
        [InlineData("Basic Wall")]
        [InlineData("")]
        [InlineData(null)]
        public void SaysNothingWhenTheNameSaysNothing(string family)
        {
            // null, not a guess. A name with no direction and no shape must not
            // be forced into one — an invented direction reads as measured fact
            // on a drawing.
            Assert.Null(HvacDirectionFromName.Resolve(family));
        }
    }

    public class AirTerminalProdCodeTests
    {
        private static List<(string Code, string Pattern)> AirTerminalRules()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate StingTools/Data");
            string path = Path.Combine(dir.FullName, "StingTools", "Data", "STING_PROD_CODES.csv");
            Assert.True(File.Exists(path), path);

            var rules = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                var s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                var c = s.Split(',');
                if (c.Length < 3) continue;
                if (!string.Equals(c[1].Trim(), "Air Terminals", StringComparison.OrdinalIgnoreCase)) continue;
                rules.Add((c[0].Trim(), c[2].Trim().ToUpperInvariant()));
            }
            return rules;
        }

        /// <summary>
        /// Mirrors ProdResolver.Strongest: most specific literal wins, measured by
        /// the longest matching alternative. Reimplemented rather than referenced
        /// because ProdPatternMatcher is Revit-coupled; the tie-break is the part
        /// that matters and it is one line.
        /// </summary>
        private static string Resolve(string familyName)
        {
            string n = familyName.ToUpperInvariant();
            string best = null; int bestLen = -1;
            foreach (var (code, pattern) in AirTerminalRules())
                foreach (var alt in pattern.Split('|'))
                {
                    string lit = alt.Trim().Trim('*');
                    if (lit.Length == 0 || !n.Contains(lit)) continue;
                    if (lit.Length > bestLen) { bestLen = lit.Length; best = code; }
                }
            return best;
        }

        [Fact]
        public void AReturnAirTerminalCodeExistsAtAll()
        {
            // Before 2026-09-23 there was none: *Diffuser* under SAT swallowed
            // "Return Diffuser" and GRL only matched *Grille*, so a return
            // diffuser had nowhere to go and was labelled Supply.
            Assert.Contains(AirTerminalRules(), r => r.Code == "RAT");
        }

        [Theory]
        [InlineData("M_Return Diffuser", "RAT")]       // the measured defect
        [InlineData("Return Air Terminal", "RAT")]
        [InlineData("M_Supply Diffuser", "SAT")]
        [InlineData("M_Diffuser", "SAT")]
        [InlineData("Exhaust Grille", "EAT")]
        [InlineData("Return Grille", "GRL")]           // GRL's own description claims it
        [InlineData("Transfer Grille", "GRL")]
        [InlineData("Fresh Air Louvre", "LVR")]
        public void SpecificityDecidesTheProductCode(string family, string expected)
            => Assert.Equal(expected, Resolve(family));

        [Fact]
        public void TheRulesWereActuallyRead()
        {
            // A control: if the locator or the parse produced nothing, every test
            // above would pass vacuously against an empty rule set.
            Assert.True(AirTerminalRules().Count >= 5,
                "expected several Air Terminals rules in the shipped CSV");
        }
    }
}
