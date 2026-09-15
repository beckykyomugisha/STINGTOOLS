// The discipline vocabulary is now data. These cover the two things that make a
// configurable table dangerous: a file that silently does nothing, and a file
// that successfully configures something invalid.

using System;
using System.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    [Collection("SheetDisciplineConfig")]
    public class SheetDisciplineConfigTests : IDisposable
    {
        public SheetDisciplineConfigTests() => SheetDisciplineConfig.Reset();
        public void Dispose() => SheetDisciplineConfig.Reset();

        [Fact]
        public void WithNoFilesTheBuiltInDefaultsAnswer()
        {
            // A missing data file must not stop sheet numbering working. It is a
            // legitimate state — a deploy that dropped data/, a tidied project
            // folder — and defaults that quietly work beat a dead command.
            Assert.False(SheetDisciplineConfig.IsConfigured);
            Assert.Equal("A", SheetDisciplineResolver.FromSheetNumber("A-001"));
            Assert.Equal("ARCH", SheetDisciplineResolver.ToCsvColumn("A"));
        }

        [Fact]
        public void APracticesOwnPrefixIsAdded()
        {
            // The gap this closed: a practice numbering architecture "AR-101" had no
            // way to say so.
            SheetDisciplineConfig.Load(null, @"{ ""numberPrefixes"": { ""ARC"": ""A"", ""FS"": ""FP"" } }");
            Assert.Equal("A", SheetDisciplineResolver.FromSheetNumber("ARC-101"));
            Assert.Equal("FP", SheetDisciplineResolver.FromSheetNumber("FS-01"));
        }

        [Fact]
        public void PrefixesMergeRatherThanReplace()
        {
            // A file naming three prefixes must add three, not reduce the vocabulary
            // to three and break every sheet numbered the shipped way.
            SheetDisciplineConfig.Load(null, @"{ ""numberPrefixes"": { ""ARC"": ""A"" } }");
            Assert.Equal("A", SheetDisciplineResolver.FromSheetNumber("ARC-101"));
            Assert.Equal("M", SheetDisciplineResolver.FromSheetNumber("M-101"));
        }

        [Fact]
        public void TitleKeywordsReplaceBecauseTheirOrderIsTheRule()
        {
            // Appending a project's rules after the shipped ones would make them
            // unreachable for every title the shipped list already matches, which is
            // the opposite of overriding.
            SheetDisciplineConfig.Load(null,
                @"{ ""titleKeywords"": [ { ""discipline"": ""S"", ""words"": [""PLAN""] } ] }");
            Assert.Equal("S", SheetDisciplineResolver.FromTitle("GROUND FLOOR PLAN"));
        }

        [Fact]
        public void AProjectCanNameItsOwnCsvColumn()
        {
            SheetDisciplineConfig.Load(null, @"{ ""csvColumns"": { ""C"": ""CIVIL"" } }");
            var header = new[] { "ARCH", "CIVIL", "GEN" };
            Assert.Equal("CIVIL", SheetDisciplineResolver.ToCsvColumn("C", header));
        }

        [Fact]
        public void AProjectCanMapItsDisciplineCodeToAnIsoRoleLetter()
        {
            SheetDisciplineConfig.Load(null, @"{ ""roleLetters"": { ""FS"": ""Y"", ""ARCH"": ""A"" } }");
            Assert.Equal("Y", Iso19650DocumentCode.NormaliseRole("FS"));
            Assert.Equal("A", Iso19650DocumentCode.NormaliseRole("ARCH"));
        }

        [Theory]
        [InlineData(@"{ ""roleLetters"": { ""FS"": ""FP"" } }")]      // two letters
        [InlineData(@"{ ""roleLetters"": { ""FS"": ""J"" } }")]       // not in the alphabet
        [InlineData(@"{ ""roleLetters"": { ""FS"": ""1"" } }")]       // not a letter
        [InlineData(@"{ ""roleLetters"": { ""FS"": """" } }")]        // blank
        public void ARoleLetterOutsideTheStandardIsRejectedAndNamed(string json)
        {
            var problems = SheetDisciplineConfig.Load(null, json);

            // Rejected...
            Assert.NotEqual("FP", Iso19650DocumentCode.NormaliseRole("FS"));
            Assert.Single(Iso19650DocumentCode.NormaliseRole("FS"));
            Assert.Contains(Iso19650DocumentCode.NormaliseRole("FS"),
                SheetDisciplineConfig.RoleAlphabet.Select(c => c.ToString()));

            // ...and NAMED. Silence here would put an un-interoperable letter on an
            // issued drawing, where it looks perfectly ordinary.
            Assert.NotEmpty(problems);
            Assert.Contains(problems, p => p.Contains("FS"));
        }

        [Fact]
        public void MalformedJsonIsReportedRatherThanSwallowed()
        {
            // An override that silently does nothing is indistinguishable from one
            // being honoured — which is the whole failure mode this codebase produces.
            var problems = SheetDisciplineConfig.Load(null, "{ this is not json");
            Assert.NotEmpty(problems);
            Assert.Equal("A", SheetDisciplineResolver.FromSheetNumber("A-001"));
        }

        [Fact]
        public void OneBadEntryDoesNotDiscardTheGoodOnes()
        {
            var problems = SheetDisciplineConfig.Load(null,
                @"{ ""numberPrefixes"": { ""ARC"": ""A"" }, ""roleLetters"": { ""FS"": ""J"" } }");
            Assert.NotEmpty(problems);
            Assert.Equal("A", SheetDisciplineResolver.FromSheetNumber("ARC-101"));
        }

        [Fact]
        public void TheShippedBaselineParsesAndKeepsTheDefaultsIntact()
        {
            // A data file whose field names do not match the POCO leaves Newtonsoft
            // holding defaults: valid JSON, green build, runtime-dead. This asserts
            // the shipped file actually reaches the fields it is meant to.
            string path = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Data", "STING_SHEET_DISCIPLINES.json");
            if (!System.IO.File.Exists(path))
                throw new System.IO.FileNotFoundException(
                    "STING_SHEET_DISCIPLINES.json was not copied to the test output, so this "
                    + "assertion would pass on nothing -- which is the failure mode it exists "
                    + "to prevent. Check the <None Include> entries in the .csproj.", path);
            string json = System.IO.File.ReadAllText(path);

            var problems = SheetDisciplineConfig.Load(json, null);
            Assert.Empty(problems);
            Assert.True(SheetDisciplineConfig.IsConfigured);

            Assert.Equal("A", SheetDisciplineResolver.FromSheetNumber("AR-101"));
            Assert.Equal("A", SheetDisciplineResolver.FromTitle("GROUND FLOOR PLAN"));
            Assert.Equal("ARCH", SheetDisciplineResolver.ToCsvColumn("A"));
            Assert.True(SheetDisciplineConfig.TitleKeywords.Count >= 10,
                "the shipped titleKeywords did not reach the POCO");
        }
    }
}
