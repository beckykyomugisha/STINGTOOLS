using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT-9 — the shipped KUT sheet-number pattern, exercised by the regex engine that
    /// actually runs it.
    ///
    /// <para><b>Why this exists next to the Python generator.</b>
    /// <c>tools/build_kut_owner_standards.py</c> derives the pattern from
    /// <c>tools/kut_naming.py</c> and verifies it with Python's <c>re</c>;
    /// <c>tools/check_kut_documents.py</c> re-checks the same thing. But the pattern is
    /// consumed by <c>OwnerStandardsPack.SheetNumberPattern</c> through
    /// <c>new Regex(rule.Pattern)</c> — .NET, not Python. Two engines agree on this
    /// syntax, and that agreement is an assumption until something asserts it. A pattern
    /// that Python accepts and .NET throws on would fail at runtime inside a
    /// <c>catch</c>, on a real project, months from now.</para>
    ///
    /// <para>These cases mirror the generator's own accept/reject set, deliberately: if
    /// the two ever disagree, one of the two engines is not doing what its author
    /// thought.</para>
    /// </summary>
    public class KutSheetPatternTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(
                       dir.FullName, "project-templates", "KUT", "_BIM_COORD", "owner_standards.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate project-templates/KUT from " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        private static string ShippedPattern()
        {
            string path = Path.Combine(RepoRoot(), "project-templates", "KUT", "_BIM_COORD", "owner_standards.json");
            var doc = JObject.Parse(File.ReadAllText(path));
            var rule = doc["rules"]?.FirstOrDefault(r =>
                (string)r["type"] == "sheetNumberPattern" && (bool?)r["enabled"] == true);
            Assert.True(rule != null, "no enabled sheetNumberPattern rule in owner_standards.json");
            string p = (string)rule["pattern"];
            Assert.False(string.IsNullOrWhiteSpace(p), "the sheetNumberPattern rule carries no pattern");
            return p;
        }

        private static Regex Shipped() => new Regex(ShippedPattern());

        private static string Name(string volume = "01", string level = "GF", string type = "M3",
                                   string role = "A", string originator = "SMB", string number = "0001")
            => $"KUT-{originator}-{volume}-{level}-{type}-{role}-{number}";

        /// <summary>The pattern must COMPILE in .NET. This is the whole reason the file
        /// exists: the generator proves it in Python.</summary>
        [Fact]
        public void TheShippedPatternCompilesInDotNet()
        {
            var ex = Record.Exception(() => new Regex(ShippedPattern()));
            Assert.Null(ex);
        }

        /// <summary>It must be the ENUMERATED form, not the permissive character classes it
        /// replaced. A pattern that still reads <c>[A-Z0-9]{2}</c> for volume has been
        /// reverted, and every accept-case below would still pass.</summary>
        [Fact]
        public void TheShippedPatternEnumeratesRatherThanWavesThrough()
        {
            string p = ShippedPattern();
            Assert.DoesNotContain("[A-Z0-9]{2}", p);
            // The originator is the ONE field that stays a character class, because the
            // register has not been issued (ROADMAP KUT-OPEN-1).
            Assert.Contains("[A-Z0-9]{3}", p);
        }

        [Theory]
        // Volumes: the six buildings, site-wide, all-volumes.
        [InlineData("KUT-SMB-01-GF-M3-A-0001")]
        [InlineData("KUT-SMB-06-GF-M3-A-0001")]
        [InlineData("KUT-SMB-00-GF-M3-A-0001")]
        [InlineData("KUT-SMB-ZZ-ZZ-M3-Z-0001")]
        // Levels, including the trap: "01 first floor, AND UPWARD IN SEQUENCE".
        [InlineData("KUT-SMB-01-B1-M3-A-0001")]
        [InlineData("KUT-SMB-01-RF-M3-A-0001")]
        [InlineData("KUT-SMB-01-XX-M3-A-0001")]
        [InlineData("KUT-SMB-01-01-M3-A-0001")]
        [InlineData("KUT-SMB-01-02-M3-A-0001")]
        [InlineData("KUT-SMB-01-12-M3-A-0001")]
        // Two-character roles, which a single [A-Z] would have rejected.
        [InlineData("KUT-SMB-01-GF-DR-FP-0001")]
        [InlineData("KUT-SMB-01-GF-DR-LV-0001")]
        // Real references published in the issued document set.
        [InlineData("KUT-SMB-ZZ-ZZ-RP-Z-0002")]
        [InlineData("KUT-SMB-ZZ-ZZ-SC-Z-0001")]
        [InlineData("KUT-SMB-ZZ-ZZ-CR-Z-0007")]
        public void Accepts(string name)
            => Assert.True(Shipped().IsMatch(name), $"pattern rejected the authorised name {name}");

        [Theory]
        [InlineData("KUT-SMB-47-GF-M3-A-0001", "volume 47 does not exist")]
        [InlineData("KUT-SMB-01-GF-QQ-A-0001", "type QQ does not exist")]
        [InlineData("KUT-SMB-01-Q1-M3-A-0001", "level Q1 does not exist")]
        [InlineData("KUT-SMB-01-GF-M3-Q-0001", "role Q does not exist")]
        [InlineData("KUT-SMB-01-GF-M3-A-001", "Number is four digits")]
        [InlineData("KUT-SMB-01-GF-M3-A-00001", "Number is four digits")]
        [InlineData("KUT-SM-01-GF-M3-A-0001", "originator is three characters")]
        [InlineData("KUT-SMB-01-GF-M3-A-0001-S2", "suitability is not part of a container name")]
        [InlineData("KUT - SMB - 01 - GF - M3 - A - 0001", "the spaced form is presentation, not a container name")]
        [InlineData("PRJ-SMB-01-GF-M3-A-0001", "the project code is KUT")]
        public void Rejects(string name, string why)
            => Assert.False(Shipped().IsMatch(name), $"pattern accepted {name} — {why}");

        /// <summary>Every discipline code the overlay authorises must be usable as a role in
        /// a container name. These are two rules in one file, and they contradicted each
        /// other once already — the pattern's role field was a single <c>[A-Z]</c> while the
        /// discipline list carried FP and LV, so every fire-protection and low-voltage sheet
        /// would have been reported non-compliant against a standard that permits them.</summary>
        [Fact]
        public void EveryAuthorisedDisciplineCodeIsAValidRole()
        {
            string path = Path.Combine(RepoRoot(), "project-templates", "KUT", "_BIM_COORD", "owner_standards.json");
            var doc = JObject.Parse(File.ReadAllText(path));
            var values = doc["rules"]?.FirstOrDefault(r => (string)r["id"] == "discipline-code-valid")?["values"];
            Assert.True(values != null, "no discipline-code-valid rule");

            var rx = Shipped();
            var rejected = values.Select(v => (string)v)
                                 .Where(code => !rx.IsMatch(Name(role: code)))
                                 .ToList();
            Assert.True(rejected.Count == 0,
                "the sheet pattern rejects discipline code(s) the same file authorises: " +
                string.Join(", ", rejected));
        }

        /// <summary>The overlay must not authorise Z as an ELEMENT discipline. BEP 4.2.2
        /// keeps container fields and asset-identifier fields apart: Z is a container role
        /// (federated / multi-discipline) and never appears on an element.</summary>
        [Fact]
        public void ZIsAContainerRoleAndNotADisciplineCode()
        {
            string path = Path.Combine(RepoRoot(), "project-templates", "KUT", "_BIM_COORD", "owner_standards.json");
            var doc = JObject.Parse(File.ReadAllText(path));
            var values = doc["rules"].First(r => (string)r["id"] == "discipline-code-valid")["values"]
                                     .Select(v => (string)v).ToList();
            Assert.DoesNotContain("Z", values);
            // …but it must still be a legal container role.
            Assert.True(Shipped().IsMatch(Name(role: "Z")));
        }

        /// <summary>Severity stays WARN while the originator register is unissued. A BLOCK
        /// here would fail every container in a project that cannot yet number one — and
        /// "all existing sheets conform" is true only because there are none, which is a
        /// vacuous pass, not evidence.</summary>
        [Fact]
        public void SeverityIsWarnWhileTheOriginatorRegisterIsUnissued()
        {
            string path = Path.Combine(RepoRoot(), "project-templates", "KUT", "_BIM_COORD", "owner_standards.json");
            var doc = JObject.Parse(File.ReadAllText(path));
            var rule = doc["rules"].First(r => (string)r["id"] == "sheet-kut-number-pattern");
            Assert.Equal("WARN", (string)rule["severity"]);
        }
    }
}
