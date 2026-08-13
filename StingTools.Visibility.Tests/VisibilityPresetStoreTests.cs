// Covers runner §2 case 4 — the shipped preset JSON round-trips.
//
// Newtonsoft leaves a mistyped or misspelled field at its default rather than failing, so
// "valid JSON + green build" is not evidence the file works. These tests deserialise the
// ACTUAL shipped Data/STING_VISIBILITY_PRESETS.json (linked into the test output by the
// csproj) and assert the fields the plugin depends on actually arrived.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.Core.Visibility;
using Xunit;

namespace StingTools.Visibility.Tests
{
    public class VisibilityPresetStoreTests
    {
        private static string ShippedBaselinePath() =>
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "STING_VISIBILITY_PRESETS.json");

        [Fact]
        public void ShippedBaseline_IsPresentInTheTestOutput()
        {
            Assert.True(File.Exists(ShippedBaselinePath()),
                $"Expected the shipped baseline at {ShippedBaselinePath()}. " +
                "If this fails the csproj <Content Include> link is broken and every other " +
                "assertion in this file would be vacuously passing on an empty library.");
        }

        [Fact]
        public void ShippedBaseline_HasFourPresets_WithNonNullRules()
        {
            var lib = VisibilityPresetStore.Parse(File.ReadAllText(ShippedBaselinePath()));

            Assert.Equal(4, lib.Presets.Count);
            Assert.All(lib.Presets, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Name));
                Assert.NotNull(p.Rules);
                Assert.NotEmpty(p.Rules);
            });
        }

        [Fact]
        public void ShippedBaseline_ContainsTheFourNamedPresets()
        {
            var lib = VisibilityPresetStore.Parse(File.ReadAllText(ShippedBaselinePath()));
            var names = lib.Presets.Select(p => p.Name).ToList();

            Assert.Contains("Zone isolation", names);
            Assert.Contains("Discipline solo", names);
            Assert.Contains("Hide untagged", names);
            Assert.Contains("MEP only", names);
        }

        [Fact]
        public void ShippedBaseline_EnumFieldsDeserialiseRatherThanSilentlyDefaulting()
        {
            var lib = VisibilityPresetStore.Parse(File.ReadAllText(ShippedBaselinePath()));

            var isolation = lib.Presets.Single(p => p.Name == "Zone isolation");
            var rule = Assert.Single(isolation.Rules);
            Assert.Equal(VisibilityRuleKind.Token, rule.Kind);
            Assert.Equal(VisibilityTokens.Zone, rule.TokenKey);
            Assert.Equal(VisibilityAction.ShowOnly, rule.Action);   // NOT the Hide default
            Assert.NotEmpty(rule.Values);

            var untagged = lib.Presets.Single(p => p.Name == "Hide untagged");
            Assert.Equal(VisibilityAction.Hide, untagged.Rules[0].Action);
            Assert.Contains(VisibilityTokens.Unset, untagged.Rules[0].Values);

            var mep = lib.Presets.Single(p => p.Name == "MEP only");
            Assert.All(mep.Rules, r =>
            {
                Assert.Equal(VisibilityRuleKind.Category, r.Kind);
                // Categories ship as BuiltInCategory names; the plugin resolves ids at load.
                Assert.StartsWith("OST_", r.CategoryName);
            });
        }

        [Fact]
        public void ShippedBaseline_EveryPresetValidates()
        {
            var lib = VisibilityPresetStore.Parse(File.ReadAllText(ShippedBaselinePath()));
            foreach (var p in lib.Presets)
                Assert.Null(VisibilityRuleMatcher.Validate(p));
        }

        // ── Round-trip ──────────────────────────────────────────────────

        [Fact]
        public void SerialiseThenParse_PreservesRules()
        {
            var lib = new VisibilityPresetLibrary
            {
                Presets = new List<VisibilitySet>
                {
                    new VisibilitySet
                    {
                        Name = "Round trip",
                        Mode = VisibilityMode.ViewFilter,
                        Target = VisibilityTarget.AllViewsOnSheet,
                        Rules = new List<VisibilityRule>
                        {
                            new VisibilityRule
                            {
                                Kind = VisibilityRuleKind.Token,
                                TokenKey = VisibilityTokens.Zone,
                                Values = new List<string> { "Z01", VisibilityTokens.Unset },
                                Action = VisibilityAction.ShowOnly
                            }
                        }
                    }
                }
            };

            var back = VisibilityPresetStore.Parse(VisibilityPresetStore.Serialise(lib));
            var p = Assert.Single(back.Presets);

            Assert.Equal("Round trip", p.Name);
            Assert.Equal(VisibilityMode.ViewFilter, p.Mode);
            Assert.Equal(VisibilityTarget.AllViewsOnSheet, p.Target);
            Assert.Equal(VisibilityAction.ShowOnly, p.Rules[0].Action);
            Assert.Equal(new[] { "Z01", VisibilityTokens.Unset }, p.Rules[0].Values);
        }

        [Fact]
        public void Parse_NormalisesNullRuleAndValueLists()
        {
            // Exactly the shape a Newtonsoft silent-default produces from a misspelled field.
            var lib = VisibilityPresetStore.Parse(
                "{ \"presets\": [ { \"name\": \"typo\", \"rulez\": [] } ] }");

            var p = Assert.Single(lib.Presets);
            Assert.NotNull(p.Rules);
            Assert.Empty(p.Rules);
        }

        [Fact]
        public void Parse_BlankOrEmptyInput_YieldsAnEmptyLibrary()
        {
            Assert.Empty(VisibilityPresetStore.Parse(null).Presets);
            Assert.Empty(VisibilityPresetStore.Parse("   ").Presets);
        }

        // ── Corporate + project layering ────────────────────────────────

        [Fact]
        public void ProjectPreset_WinsOverCorporateOfTheSameName()
        {
            string dir = Path.Combine(Path.GetTempPath(), "sting_vis_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                string corporate = Path.Combine(dir, "corporate.json");
                string project = Path.Combine(dir, "project.json");

                File.WriteAllText(corporate, VisibilityPresetStore.Serialise(new VisibilityPresetLibrary
                {
                    Presets = new List<VisibilitySet>
                    {
                        new VisibilitySet { Name = "Shared", Description = "corporate copy" },
                        new VisibilitySet { Name = "CorporateOnly" }
                    }
                }));
                File.WriteAllText(project, VisibilityPresetStore.Serialise(new VisibilityPresetLibrary
                {
                    Presets = new List<VisibilitySet>
                    {
                        new VisibilitySet { Name = "Shared", Description = "project copy" },
                        new VisibilitySet { Name = "ProjectOnly" }
                    }
                }));

                var merged = VisibilityPresetStore.Load(corporate, project);

                Assert.Equal(3, merged.Count);
                var shared = merged.Single(p => p.Name == "Shared");
                Assert.Equal("project copy", shared.Description);
                Assert.Equal("project", shared.Origin);
                Assert.Equal("corporate", merged.Single(p => p.Name == "CorporateOnly").Origin);
                Assert.Contains(merged, p => p.Name == "ProjectOnly");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Load_ToleratesMissingFiles()
        {
            var merged = VisibilityPresetStore.Load("does-not-exist.json", "also-missing.json");
            Assert.Empty(merged);
        }

        [Fact]
        public void Load_ReportsMalformedJsonAsAWarning_RatherThanThrowing()
        {
            string path = Path.Combine(Path.GetTempPath(), "sting_vis_bad_" + Path.GetRandomFileName() + ".json");
            File.WriteAllText(path, "{ this is not json ");
            try
            {
                var warnings = new List<string>();
                var merged = VisibilityPresetStore.Load(path, null, warnings);

                Assert.Empty(merged);
                Assert.NotEmpty(warnings);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Save_WritesOnlyProjectOriginEntries()
        {
            string path = Path.Combine(Path.GetTempPath(), "sting_vis_save_" + Path.GetRandomFileName() + ".json");
            try
            {
                var sets = new List<VisibilitySet>
                {
                    new VisibilitySet { Name = "FromCorporate", Origin = "corporate" },
                    new VisibilitySet { Name = "Mine", Origin = "project" }
                };

                Assert.True(VisibilityPresetStore.Save(path, sets));

                var back = VisibilityPresetStore.Parse(File.ReadAllText(path));
                var only = Assert.Single(back.Presets);
                Assert.Equal("Mine", only.Name);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Save_WithNoPath_ReturnsFalseWithAWarning_NotAnException()
        {
            var warnings = new List<string>();
            Assert.False(VisibilityPresetStore.Save(null, new List<VisibilitySet>(), warnings));
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public void Upsert_ReplacesByNameAndStampsProjectOrigin()
        {
            var existing = new List<VisibilitySet>
            {
                new VisibilitySet { Name = "A", Description = "old", Origin = "corporate" }
            };

            VisibilityPresetStore.Upsert(existing, new VisibilitySet { Name = "a", Description = "new" });

            var only = Assert.Single(existing);
            Assert.Equal("new", only.Description);
            Assert.Equal("project", only.Origin);
        }
    }
}
