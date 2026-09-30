// "followsType": an instance value that is really a per-type default follows a type
// change — but never over a value somebody chose.
//
// One rule (SeedFollowTypeRule) serves both paths that change a seed element's type:
// a swap (SeedTypeSwapUpdater, old type unknown) and a rename/merge migration on
// Build Seeds (SeedTypeMigrator, old type known from "renamedFrom"). Pinned here:
//   - the rule itself: follow an untouched value, keep an edited one, clear by the
//     same condition, compare numbers by value;
//   - the catalog against the shipped seeds: seed families only, declared types only;
//   - the migration's precision: with the old type's values known, only those count
//     as untouched (MG-2: a user's product code survives the migration);
//   - the data: every followsType parameter is instance and set by a variant, and
//     every instance parameter a variant sets says whether it follows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Symbols;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SeedFollowTypeTests
    {
        private static string SeedsDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(d.FullName, "StingTools", "Data", "Seeds");
        }

        private static List<(string File, SymbolDefinition Sym)> Seeds() =>
            Directory.GetFiles(SeedsDir(), "STING_SEED_*.json").OrderBy(f => f, StringComparer.Ordinal)
                .SelectMany(f => JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(f)).Symbols
                    .Select(s => (Path.GetFileName(f), s)))
                .ToList();

        private static SeedFollowTypeCatalog _shipped;
        private static SeedFollowTypeCatalog Shipped => _shipped ??= SeedFollowTypeCatalog.LoadDirectory(SeedsDir());

        /// <summary>Writes for one instance holding <paramref name="current"/> values.</summary>
        private static Dictionary<string, string> Swap(SeedFollowTypeCatalog cat, string family, string newType,
            Dictionary<string, string> current, string oldType = null)
            => cat.AfterTypeChange(family, newType, p => current.TryGetValue(p, out var v) ? v : null, oldType)
                  .ToDictionary(kv => kv.Key, kv => kv.Value);

        // ── the rule ────────────────────────────────────────────────────

        [Theory]
        // untouched: the value is one the seed put there -> follow
        [InlineData("O2", new[] { "O2", "VAC" }, "VAC", true, "VAC")]
        // blank -> follow
        [InlineData("", new[] { "O2" }, "VAC", true, "VAC")]
        [InlineData(null, new[] { "O2" }, "VAC", true, "VAC")]
        [InlineData("  ", new[] { "O2" }, "VAC", true, "VAC")]
        // a user's value -> keep
        [InlineData("O2 bedhead 3", new[] { "O2", "VAC" }, "VAC", false, null)]
        // already right -> no write
        [InlineData("VAC", new[] { "O2", "VAC" }, "VAC", false, null)]
        // new type declares nothing: clear, by the same condition
        [InlineData("O2", new[] { "O2" }, "", true, "")]
        [InlineData("O2", new[] { "O2" }, null, true, "")]
        [InlineData("my gas", new[] { "O2" }, "", false, null)]
        [InlineData("", new[] { "O2" }, "", false, null)]
        // numbers compare by value: Revit reads a Number back as 63.0
        [InlineData("63.0", new[] { "63", "100" }, "100", true, "100")]
        [InlineData("100.0", new[] { "63" }, "100", false, null)]
        [InlineData("64", new[] { "63" }, "100", false, null)]
        public void TheRule(string current, string[] seedValues, string newValue, bool writes, string expected)
        {
            Assert.Equal(writes, SeedFollowTypeRule.Decide(current, seedValues, newValue, out string write));
            if (writes) Assert.Equal(expected, write);
        }

        // ── the catalog, against the shipped seeds ──────────────────────

        [Fact]
        public void AGasSwapFollowsTheNewType()
        {
            var w = Swap(Shipped, "STING_SEED_MedGasOutlet", "TERMINAL_UNIT_VAC", new Dictionary<string, string>
            {
                ["MGS_GAS_TYPE_TXT"] = "O2", ["ASS_PRODCT_COD_TXT"] = "TU-O2", ["MGS_TU_TYPE_TXT"] = "TERMINAL_UNIT",
                ["MGS_GASES_TXT"] = "O2", ["MGS_HOSPITAL_AREA_TXT"] = "THEATRE",
            });
            Assert.Equal("VAC", w["MGS_GAS_TYPE_TXT"]);
            Assert.Equal("VAC", w["MGS_GASES_TXT"]);
            Assert.True(w.ContainsKey("ASS_PRODCT_COD_TXT"));
            Assert.False(w.ContainsKey("MGS_TU_TYPE_TXT"));        // same on both types: no write
            Assert.False(w.ContainsKey("MGS_HOSPITAL_AREA_TXT"));  // followsType: false — the room's, not the type's
        }

        [Fact]
        public void AUserEditedValueSurvivesASwap()
        {
            var w = Swap(Shipped, "STING_SEED_MedGasOutlet", "TERMINAL_UNIT_VAC", new Dictionary<string, string>
            {
                ["MGS_GAS_TYPE_TXT"] = "O2", ["ASS_PRODCT_COD_TXT"] = "TU-O2-THEATRE-SPECIAL",
            });
            Assert.Equal("VAC", w["MGS_GAS_TYPE_TXT"]);
            Assert.False(w.ContainsKey("ASS_PRODCT_COD_TXT"));
        }

        [Fact]
        public void ASwapToATypeWithNoGasClearsAnUntouchedGas()
        {
            var w = Swap(Shipped, "STING_SEED_MedGasOutlet", "AVSU_BOX_5GAS",
                new Dictionary<string, string> { ["MGS_GAS_TYPE_TXT"] = "O2" });
            Assert.Equal("", w["MGS_GAS_TYPE_TXT"]);
        }

        [Fact]
        public void AFireRatingFollowsADamperSwap()
        {
            string toType = TypeWith("STING_SEED_FireDamper", "PEN_FIRE_RATING_TXT", "FR120");
            var w = Swap(Shipped, "STING_SEED_FireDamper", toType,
                new Dictionary<string, string> { ["PEN_FIRE_RATING_TXT"] = "FR60" });
            Assert.Equal("FR120", w["PEN_FIRE_RATING_TXT"]);
        }

        [Fact]
        public void ANumericPanelRatingFollowsAsANumber()
        {
            // ELC_PNL_MAIN_BRK_A is a registered NUMBER: Revit reads it back as "63.0"-ish.
            string toType = TypeWith("STING_SEED_ElectricalEquipment", "ELC_PNL_MAIN_BRK_A", "630");
            var w = Swap(Shipped, "STING_SEED_ElectricalEquipment", toType,
                new Dictionary<string, string> { ["ELC_PNL_MAIN_BRK_A"] = "63.0", ["ELC_PNL_NUM_OF_WAYS_NR"] = "12" });
            Assert.Equal("630", w["ELC_PNL_MAIN_BRK_A"]);
        }

        [Theory]
        [InlineData("Beaconmedaes Oxygen Outlet")]      // a manufacturer family
        [InlineData("STING_SEED_MedGasOutlet_Custom")]  // not a seed id + digits
        [InlineData("")]
        [InlineData(null)]
        public void ANonSeedFamilyIsNeverTouched(string family)
        {
            Assert.Null(Shipped.SeedIdForFamily(family));
            Assert.Empty(Swap(Shipped, family, "TERMINAL_UNIT_VAC", new Dictionary<string, string> { ["MGS_GAS_TYPE_TXT"] = "O2" }));
        }

        [Fact]
        public void ASecondLoadOfASeedIsStillTheSeed()
            => Assert.Equal("STING_SEED_MedGasOutlet", Shipped.SeedIdForFamily("STING_SEED_MedGasOutlet2"));

        [Fact]
        public void AUserDuplicatedTypeIsLeftAlone()
            => Assert.Empty(Swap(Shipped, "STING_SEED_MedGasOutlet", "TERMINAL_UNIT_VAC copy",
                new Dictionary<string, string> { ["MGS_GAS_TYPE_TXT"] = "O2" }));

        [Fact]
        public void AParameterTheElementDoesNotCarryIsSkipped()
            => Assert.Empty(Swap(Shipped, "STING_SEED_MedGasOutlet", "TERMINAL_UNIT_VAC", new Dictionary<string, string>()));

        [Fact]
        public void AMissingSeedFolderIsAnEmptyCatalogNotACrash()
        {
            var cat = SeedFollowTypeCatalog.LoadDirectory(Path.Combine(Path.GetTempPath(), "no-seeds-" + Guid.NewGuid()));
            Assert.True(cat.IsEmpty);
            Assert.Null(cat.SeedIdForFamily("STING_SEED_MedGasOutlet"));
        }

        // ── the migration (MG-2): the old type's values are known ───────

        [Theory]
        // the old type's code, untouched -> the new code
        [InlineData("MAP", true, "TGP")]
        [InlineData("", true, "TGP")]
        // a code a user typed -> kept
        [InlineData("MAP-THEATRE-2", false, null)]
        // another seed type's code is NOT the old type's: with the old type known, it stays
        [InlineData("AP-AREA", false, null)]
        public void AMigrationReplacesOnlyTheOldTypesValue(string current, bool writes, string expected)
        {
            var w = Swap(Shipped, "STING_SEED_MedGasOutlet", "THEATRE_GAS_PANEL",
                new Dictionary<string, string> { ["ASS_PRODCT_COD_TXT"] = current }, oldType: "MAP_THEATRE_PANEL");
            Assert.Equal(writes, w.ContainsKey("ASS_PRODCT_COD_TXT"));
            if (writes) Assert.Equal(expected, w["ASS_PRODCT_COD_TXT"]);
        }

        [Fact]
        public void AnOldNameWithoutValuesFallsBackToTheSeedsValues()
        {
            var lib = new SymbolLibrary();
            lib.Symbols.Add(new SymbolDefinition
            {
                Id = "STING_SEED_X",
                Parameters = { new ParameterDefinition { Name = "ASS_PRODCT_COD_TXT", IsInstance = true, FollowsType = true } },
                TypeVariants =
                {
                    new TypeVariantDefinition { Name = "A", Parameters = { ["ASS_PRODCT_COD_TXT"] = "AAA" } },
                    new TypeVariantDefinition { Name = "B", Parameters = { ["ASS_PRODCT_COD_TXT"] = "BBB" },
                                                RenamedFrom = new List<RenamedFromEntry> { "OLD_B" } },
                },
            });
            var cat = SeedFollowTypeCatalog.FromLibraries(new[] { lib });
            Assert.Equal("BBB", Swap(cat, "STING_SEED_X", "B", new Dictionary<string, string> { ["ASS_PRODCT_COD_TXT"] = "AAA" }, "OLD_B")["ASS_PRODCT_COD_TXT"]);
            Assert.Empty(Swap(cat, "STING_SEED_X", "B", new Dictionary<string, string> { ["ASS_PRODCT_COD_TXT"] = "mine" }, "OLD_B"));
        }

        [Fact]
        public void AParameterNotMarkedFollowsTypeNeverMoves()
        {
            var lib = new SymbolLibrary();
            lib.Symbols.Add(new SymbolDefinition
            {
                Id = "STING_SEED_Y",
                Parameters = { new ParameterDefinition { Name = "P", IsInstance = true, FollowsType = false } },
                TypeVariants = { new TypeVariantDefinition { Name = "A", Parameters = { ["P"] = "1" } },
                                 new TypeVariantDefinition { Name = "B", Parameters = { ["P"] = "2" } } },
            });
            var cat = SeedFollowTypeCatalog.FromLibraries(new[] { lib });
            Assert.Empty(Swap(cat, "STING_SEED_Y", "B", new Dictionary<string, string> { ["P"] = "1" }));
        }

        [Fact]
        public void RenamedFromReadsEveryShape()
        {
            var obj = JsonConvert.DeserializeObject<TypeVariantDefinition>(
                "{\"name\":\"N\",\"renamedFrom\":[\"O1\",{\"name\":\"O2\",\"params\":{\"ASS_PRODCT_COD_TXT\":\"X\"}}]}");
            Assert.Equal(new[] { "O1", "O2" }, obj.RenamedFrom.Select(e => e.Name));
            Assert.Null(obj.RenamedFrom[0].Parameters);
            Assert.Equal("X", obj.RenamedFrom[1].Parameters["ASS_PRODCT_COD_TXT"]);
            var single = JsonConvert.DeserializeObject<TypeVariantDefinition>("{\"name\":\"N\",\"renamedFrom\":{\"name\":\"O\"}}");
            Assert.Equal("O", Assert.Single(single.RenamedFrom).Name);
            // round-trips
            var back = JsonConvert.DeserializeObject<TypeVariantDefinition>(JsonConvert.SerializeObject(obj));
            Assert.Equal("X", back.RenamedFrom[1].Parameters["ASS_PRODCT_COD_TXT"]);
        }

        [Fact]
        public void AnOldTypesValueForANonFollowingParameterIsRejected()
        {
            var v = new TypeVariantDefinition
            {
                Name = "NEW",
                RenamedFrom = new List<RenamedFromEntry> { new RenamedFromEntry { Name = "OLD", Parameters = new Dictionary<string, string> { ["P"] = "1" } } },
            };
            var ps = new[] { new ParameterDefinition { Name = "P", IsInstance = true, FollowsType = false } };
            Assert.NotEmpty(SeedTypeRenames.Validate("s", new[] { v }, ps));
            ps[0].FollowsType = true;
            Assert.Empty(SeedTypeRenames.Validate("s", new[] { v }, ps));
        }

        // ── the data ────────────────────────────────────────────────────

        [Fact]
        public void EveryFollowsTypeParameterIsInstanceAndSetByAVariant()
        {
            var bad = new List<string>();
            int count = 0;
            foreach (var (file, s) in Seeds())
            {
                var setBy = new HashSet<string>((s.TypeVariants ?? new List<TypeVariantDefinition>())
                    .Where(v => !string.IsNullOrWhiteSpace(v?.Name))
                    .SelectMany(v => (v.Parameters ?? new Dictionary<string, string>()).Keys), StringComparer.Ordinal);
                foreach (var p in s.Parameters ?? new List<ParameterDefinition>())
                {
                    if (p.FollowsType != true) continue;
                    count++;
                    if (!p.IsInstance) bad.Add($"{file} {s.Id}.{p.Name}: followsType on a TYPE parameter — a type parameter already follows");
                    if (!setBy.Contains(p.Name)) bad.Add($"{file} {s.Id}.{p.Name}: followsType but no variant declares a value");
                }
            }
            Assert.True(count > 30, $"only {count} followsType parameters — the flag is not being read");
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Fact]
        public void EveryInstanceParameterAVariantSetsSaysWhetherItFollows()
        {
            // An instance parameter set per type is exactly the stale-after-swap case. A new
            // seed (or a new variant value) must decide: follow, or stay with the instance.
            var bad = new List<string>();
            foreach (var (file, s) in Seeds())
            {
                var setBy = new HashSet<string>((s.TypeVariants ?? new List<TypeVariantDefinition>())
                    .Where(v => !string.IsNullOrWhiteSpace(v?.Name))
                    .SelectMany(v => (v.Parameters ?? new Dictionary<string, string>()).Keys), StringComparer.Ordinal);
                foreach (var p in s.Parameters ?? new List<ParameterDefinition>())
                    if (p.IsInstance && setBy.Contains(p.Name) && p.FollowsType == null)
                        bad.Add($"{file} {s.Id}.{p.Name}: instance, set per type — declare \"followsType\": true or false");
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Theory]
        [InlineData("MGS_GAS_TYPE_TXT")]
        [InlineData("PEN_FIRE_RATING_TXT")]
        [InlineData("ASS_PRODCT_COD_TXT")]
        public void TheNamedParametersFollowWhereverAVariantSetsThem(string name)
        {
            int seen = 0;
            foreach (var (file, s) in Seeds())
            {
                var p = (s.Parameters ?? new List<ParameterDefinition>()).FirstOrDefault(x => x.Name == name);
                bool setByVariant = (s.TypeVariants ?? new List<TypeVariantDefinition>())
                    .Any(v => v?.Parameters != null && v.Parameters.ContainsKey(name));
                if (p == null || !setByVariant) continue;
                seen++;
                Assert.True(p.FollowsType == true, $"{file} {s.Id}.{name} is not followsType");
            }
            Assert.True(seen > 0, $"no seed sets {name} per type");
        }

        private static string TypeWith(string seedId, string param, string value)
        {
            var s = Seeds().Select(x => x.Sym).Single(x => x.Id == seedId);
            var v = s.TypeVariants.FirstOrDefault(t => t?.Parameters != null
                && t.Parameters.TryGetValue(param, out var val) && val == value);
            Assert.True(v != null, $"{seedId} has no type with {param} = {value}");
            return v.Name;
        }
    }
}
