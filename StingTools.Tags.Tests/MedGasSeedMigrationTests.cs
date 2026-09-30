// MG-1 / MG-2: the medical-gas seed after a type swap, and after a type rename.
//
// MG-2 — THEATRE_GAS_PANEL (TGP) replaced MAP_THEATRE_PANEL (MAP), because
// MgasNetwork reads product code MAP as a Master Alarm Panel. Build Seeds never
// deletes a type a project already holds, so a data-driven "renamedFrom" on the
// variant drives a migration. The decision (rename vs merge vs nothing) is pinned
// here; the Revit half (SeedTypeMigrator) only executes it.
//
// MG-1 — MGS_GAS_TYPE_TXT is an instance parameter, so a type swap keeps the old
// gas. SeedTypeSwapUpdater restamps it (it is a "followsType" parameter) from the gas
// the NEW type declares in the seed spec; the general rule is pinned in
// SeedFollowTypeTests. The type -> gas map placement uses is pinned here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core.Plumbing;
using StingTools.Core.Symbols;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class MedGasSeedMigrationTests
    {
        private static string Data(params string[] parts)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(new[] { d.FullName, "StingTools", "Data" }.Concat(parts).ToArray());
        }

        private static string MedGasSeedPath => Data("Seeds", "STING_SEED_MedGasOutlet.json");

        private static SymbolLibrary LoadSeed(string path) =>
            JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(path));

        private static TypeVariantDefinition V(string name, params string[] renamedFrom) =>
            new TypeVariantDefinition { Name = name, RenamedFrom = renamedFrom.Select(n => (RenamedFromEntry)n).ToList() };

        private static string[] Names(List<RenamedFromEntry> entries) =>
            (entries ?? new List<RenamedFromEntry>()).Select(e => e.Name).ToArray();

        // ── MG-2: the decision ───────────────────────────────────────────

        [Theory]
        [InlineData(false, false, SeedTypeMigrationAction.None)]
        [InlineData(false, true,  SeedTypeMigrationAction.None)]
        [InlineData(true,  false, SeedTypeMigrationAction.Rename)]
        [InlineData(true,  true,  SeedTypeMigrationAction.Merge)]
        public void DecideFollowsWhichTypesTheProjectHolds(bool oldExists, bool newExists, SeedTypeMigrationAction expected)
            => Assert.Equal(expected, SeedTypeRenames.Decide(oldExists, newExists));

        [Fact]
        public void OnlyTheOldTypeRenamesIt()
        {
            var steps = SeedTypeRenames.Plan(new[] { V("THEATRE_GAS_PANEL", "MAP_THEATRE_PANEL") },
                new[] { "TERMINAL_UNIT_O2", "MAP_THEATRE_PANEL" });
            var s = Assert.Single(steps);
            Assert.Equal(SeedTypeMigrationAction.Rename, s.Action);
            Assert.Equal("MAP_THEATRE_PANEL", s.OldName);
            Assert.Equal("THEATRE_GAS_PANEL", s.NewName);
        }

        [Fact]
        public void BothTypesMergeTheOldIntoTheNew()
        {
            var steps = SeedTypeRenames.Plan(new[] { V("THEATRE_GAS_PANEL", "MAP_THEATRE_PANEL") },
                new[] { "MAP_THEATRE_PANEL", "THEATRE_GAS_PANEL" });
            Assert.Equal(SeedTypeMigrationAction.Merge, Assert.Single(steps).Action);
        }

        [Fact]
        public void AMigratedProjectPlansNothing()
        {
            Assert.Empty(SeedTypeRenames.Plan(new[] { V("THEATRE_GAS_PANEL", "MAP_THEATRE_PANEL") },
                new[] { "THEATRE_GAS_PANEL" }));
            Assert.Empty(SeedTypeRenames.Plan(new[] { V("THEATRE_GAS_PANEL") }, new[] { "X" }));
        }

        [Fact]
        public void TwoOldNamesRenameTheFirstAndMergeTheSecond()
        {
            // After the rename the new name exists, so the second old type must merge,
            // not attempt a second rename onto a name that is now taken.
            var steps = SeedTypeRenames.Plan(new[] { V("NEW", "OLD_A", "OLD_B") }, new[] { "OLD_A", "OLD_B" });
            Assert.Equal(2, steps.Count);
            Assert.Equal((SeedTypeMigrationAction.Rename, "OLD_A"), (steps[0].Action, steps[0].OldName));
            Assert.Equal((SeedTypeMigrationAction.Merge, "OLD_B"), (steps[1].Action, steps[1].OldName));
        }

        [Fact]
        public void ADeclaredTypeIsNeverMigratedAway()
        {
            // A renamedFrom naming a type the spec still declares would delete a live type.
            var steps = SeedTypeRenames.Plan(new[] { V("NEW", "KEEP"), V("KEEP") }, new[] { "KEEP", "NEW" });
            Assert.Empty(steps);
        }

        // ── MG-2: the data ───────────────────────────────────────────────

        [Fact]
        public void RenamedFromReadsAStringOrAnArray()
        {
            var one = JsonConvert.DeserializeObject<TypeVariantDefinition>("{\"name\":\"N\",\"renamedFrom\":\"O\"}");
            Assert.Equal(new[] { "O" }, Names(one.RenamedFrom));
            var many = JsonConvert.DeserializeObject<TypeVariantDefinition>("{\"name\":\"N\",\"renamedFrom\":[\"O1\",\"O2\"]}");
            Assert.Equal(new[] { "O1", "O2" }, Names(many.RenamedFrom));
            var none = JsonConvert.DeserializeObject<TypeVariantDefinition>("{\"name\":\"N\"}");
            Assert.True(none.RenamedFrom == null || none.RenamedFrom.Count == 0);
        }

        [Fact]
        public void TheTheatrePanelDeclaresItsOldName()
        {
            var tgp = LoadSeed(MedGasSeedPath).Symbols[0].TypeVariants.Single(v => v.Name == "THEATRE_GAS_PANEL");
            var old = Assert.Single(tgp.RenamedFrom ?? new List<RenamedFromEntry>(), e => e.Name == "MAP_THEATRE_PANEL");
            // The old type's declared code, so a migration only replaces an untouched "MAP".
            Assert.Equal("MAP", old.Parameters?["ASS_PRODCT_COD_TXT"]);
        }

        [Fact]
        public void EveryRenamedFromNamesATypeTheSeedNoLongerDeclares()
        {
            int checkedSeeds = 0;
            var problems = new List<string>();
            foreach (var path in Directory.GetFiles(Data("Seeds"), "*.json"))
            {
                var lib = LoadSeed(path);
                foreach (var sym in lib?.Symbols ?? new List<SymbolDefinition>())
                {
                    problems.AddRange(SeedTypeRenames.Validate(
                        Path.GetFileName(path) + ":" + sym.Id, sym.TypeVariants ?? new List<TypeVariantDefinition>(),
                        sym.Parameters));
                }
                checkedSeeds++;
            }
            Assert.True(checkedSeeds > 10, $"only {checkedSeeds} seed file(s) found");
            Assert.Empty(problems);
        }

        [Fact]
        public void ValidateCatchesTheBadShapes()
        {
            Assert.NotEmpty(SeedTypeRenames.Validate("s", new[] { V("A", "A") }));               // itself
            Assert.NotEmpty(SeedTypeRenames.Validate("s", new[] { V("A", "B"), V("B") }));       // still declared
            Assert.NotEmpty(SeedTypeRenames.Validate("s", new[] { V("A", "X"), V("B", "X") }));  // two owners
            Assert.NotEmpty(SeedTypeRenames.Validate("s", new[] { V("A", " ") }));               // blank
            Assert.Empty(SeedTypeRenames.Validate("s", new[] { V("A", "OLD"), V("B") }));
        }

        // ── MG-1: type -> gas ────────────────────────────────────────────

        private static MedGasSeedTypeMap Map() => MedGasSeedTypeMap.Load(MedGasSeedPath);

        [Theory]
        [InlineData("TERMINAL_UNIT_O2", "O2")]
        [InlineData("TERMINAL_UNIT_N2O", "N2O")]
        [InlineData("TERMINAL_UNIT_MEDAIR", "MA4")]
        [InlineData("TERMINAL_UNIT_SURGAIR", "MA7")]
        [InlineData("TERMINAL_UNIT_VAC", "VAC")]
        [InlineData("TERMINAL_UNIT_CO2", "CO2")]
        [InlineData("TERMINAL_UNIT_N2", "N2")]
        [InlineData("TERMINAL_UNIT_HELIOX", "HE")]
        public void EachTerminalUnitMapsToItsCanonicalGas(string type, string gas)
            => Assert.Equal(gas, Map().GasForType(type));

        [Fact]
        public void EveryDeclaredTerminalUnitMapsToACanonicalGas()
        {
            var map = Map();
            var tus = map.TypeNames.Where(n => n.StartsWith("TERMINAL_UNIT_", StringComparison.Ordinal)).ToList();
            Assert.Equal(8, tus.Count);
            foreach (var t in tus)
            {
                var g = map.GasForType(t);
                Assert.True(g != null && MedicalGasFixtures.CanonicalGasCode(g) == g, $"{t} -> {g ?? "null"}");
            }
        }

        [Theory]
        [InlineData("AVSU_BOX_5GAS")]
        [InlineData("ALARM_PANEL_AREA")]
        [InlineData("THEATRE_GAS_PANEL")]
        [InlineData("BEDHEAD_UNIT_WARD")]
        [InlineData("VIE_MANIFOLD")]
        public void NonOutletTypesDeclareNoGas(string type)
        {
            var map = Map();
            Assert.True(map.IsDeclaredType(type));
            Assert.Null(map.GasForType(type));
        }

        [Fact]
        public void AnUndeclaredTypeIsNotTheSeeds()
        {
            var map = Map();
            Assert.False(map.IsDeclaredType("Some Manufacturer Outlet"));
            Assert.Null(map.GasForType("Some Manufacturer Outlet"));
            Assert.Null(map.GasForType(null));
        }

        [Fact]
        public void TypesForGasFindsTheSeedTypeByDeclaredGas()
        {
            Assert.Equal(new[] { "TERMINAL_UNIT_VAC" }, Map().TypesForGas("VAC"));
            Assert.Equal(new[] { "TERMINAL_UNIT_MEDAIR" }, Map().TypesForGas("AIR"));   // alias
            Assert.Empty(Map().TypesForGas("AGS"));
        }

        [Theory]
        // swapped O2 -> VAC: the gas follows
        [InlineData("TERMINAL_UNIT_VAC", "O2", true, "VAC")]
        // already right: no write
        [InlineData("TERMINAL_UNIT_VAC", "VAC", false, null)]
        // swapped to a type with no gas: clear it
        [InlineData("AVSU_BOX_5GAS", "O2", true, "")]
        [InlineData("AVSU_BOX_5GAS", "", false, null)]
        // a type the seed does not declare is left alone
        [InlineData("MY_DUPLICATED_TYPE", "O2", false, null)]
        // a gas nobody's seed type declares was typed by someone: it stays
        [InlineData("TERMINAL_UNIT_VAC", "O2 (temporary)", false, null)]
        public void TheGasFollowsATypeSwap(string newType, string current, bool change, string expected)
        {
            var cat = SeedFollowTypeCatalog.FromLibraries(new[] { LoadSeed(MedGasSeedPath) });
            var writes = cat.AfterTypeChange("STING_SEED_MedGasOutlet", newType,
                p => p == "MGS_GAS_TYPE_TXT" ? current ?? "" : null);
            Assert.Equal(change, writes.Count == 1);
            if (change) Assert.Equal(("MGS_GAS_TYPE_TXT", expected), (writes[0].Key, writes[0].Value));
        }

        [Theory]
        [InlineData("STING_SEED_MedGasOutlet", true)]
        [InlineData("sting_seed_medgasoutlet", true)]
        [InlineData("STING_SEED_MedGasOutlet1", true)]   // Revit's suffix on a duplicate load
        [InlineData("Beaconmedaes Oxygen Outlet", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyTheSeedFamilyIsTouched(string familyName, bool expected)
            => Assert.Equal(expected, MedicalGasFixtures.IsSeedFamilyName(familyName));

        [Fact]
        public void AMissingSpecIsAnEmptyMapNotACrash()
        {
            var map = MedGasSeedTypeMap.Load(Path.Combine(Path.GetTempPath(), "no-such-seed-" + Guid.NewGuid() + ".json"));
            Assert.True(map.IsEmpty);
            Assert.Empty(map.TypesForGas("VAC"));
        }
    }
}
