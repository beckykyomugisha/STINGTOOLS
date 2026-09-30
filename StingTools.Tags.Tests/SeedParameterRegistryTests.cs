// SEED-1: every seed parameter names a registered shared parameter, by its real name.
//
// Some seed parameters were named with ParamRegistry KEYS (ELC_MAIN_BRK, ELC_WAYS,
// ELC_PHOTO_LUMENS, ...) rather than the parameter names those keys resolve to
// (ELC_PNL_MAIN_BRK_A, ELC_PNL_NUM_OF_WAYS_NR, ELC_PHOTO_LUMENS_NR, ...). A key is
// not a parameter: SymbolLibraryCreator looked the name up in the shared parameter
// file, found nothing, and quietly bound a family-only parameter that no reader,
// writer, schedule or tag ever touches. Nothing failed.
//
// The guards:
//   1. a "shared": true seed parameter is a name in MR_PARAMETERS.txt, or is on the
//      commented allow-list below (unregistered seed vocabulary, a known gap);
//   2. a seed parameter is never named by a registry key that resolves elsewhere;
//   3. the declared "type" agrees with the registered data type (the shared file
//      decides the real spec, but a mismatched declaration is what the family falls
//      back to when the shared file is not set, and it misleads the next reader);
//   4. a family-only ("shared": false) parameter never borrows a registered name —
//      it would collide with the shared parameter a project binds;
//   5. the allow-list cannot go stale: an entry that becomes registered, or that no
//      seed uses any more, fails.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Symbols;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SeedParameterRegistryTests
    {
        private static string DataDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "StingTools", "Data")))
                d = d.Parent;
            Assert.True(d != null, "could not locate StingTools/Data");
            return Path.Combine(d.FullName, "StingTools", "Data");
        }

        /// <summary>Registered shared parameter name -> DATATYPE column of MR_PARAMETERS.txt.</summary>
        private static Dictionary<string, string> Registered()
        {
            var reg = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(DataDir(), "MR_PARAMETERS.txt")))
            {
                var c = line.Split('\t');
                if (c.Length > 3 && c[0] == "PARAM") reg[c[2]] = c[3];
            }
            Assert.True(reg.Count > 1000, $"only {reg.Count} registered parameters read");
            return reg;
        }

        /// <summary>PARAMETER_REGISTRY.json extended_params: key -> param_name.</summary>
        private static Dictionary<string, string> RegistryKeys()
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            var root = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "PARAMETER_REGISTRY.json")));
            foreach (var o in ((JContainer)root["extended_params"]).DescendantsAndSelf().OfType<JObject>())
            {
                var k = (string)o["key"];
                var n = (string)o["param_name"];
                if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(n) && !keys.ContainsKey(k)) keys[k] = n;
            }
            Assert.True(keys.ContainsKey("ELC_MAIN_BRK"), "registry keys not read");
            return keys;
        }

        private static IEnumerable<(string File, SymbolDefinition Sym, ParameterDefinition P)> SeedParams()
        {
            var files = Directory.GetFiles(Path.Combine(DataDir(), "Seeds"), "STING_SEED_*.json")
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.True(files.Count > 10, $"only {files.Count} seed files");
            foreach (var f in files)
            {
                var lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(f));
                foreach (var s in lib.Symbols)
                    foreach (var p in s.Parameters ?? new List<ParameterDefinition>())
                        if (!string.IsNullOrWhiteSpace(p?.Name))
                            yield return (Path.GetFileName(f), s, p);
            }
        }

        /// <summary>The seed "type" vocabulary -> the MR_PARAMETERS.txt DATATYPE it must be.</summary>
        private static readonly Dictionary<string, string> DataTypeOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Text"] = "TEXT",
            ["Number"] = "NUMBER",
            ["Integer"] = "INTEGER",
            ["Length"] = "LENGTH",
            ["YesNo"] = "YESNO",
        };

        /// <summary>
        /// "shared": true seed parameters that are NOT in MR_PARAMETERS.txt. Each builds as
        /// a family-only parameter (SymbolLibraryCreator's fallback when the shared file has
        /// no such definition), so the family carries it but a project schedule, filter or
        /// tag bound to a shared GUID cannot read it. Registering them is a separate
        /// decision — none of them is a key or a misspelling of a registered name (those
        /// were renamed, SEED-1). Remove an entry once it is registered.
        /// </summary>
        private static readonly HashSet<string> UnregisteredSeedVocabulary = new HashSet<string>(StringComparer.Ordinal)
        {
            // AcousticSeal — acoustic seal rating/spec (no ACS_ group registered)
            "ACS_RW_TARGET_DB", "ACS_SEAL_TYPE_TXT", "ACS_DEPTH_MM", "ACS_CERT_TXT",
            // AirTerminal — HVC_TERM_* (the registered HVC_TERMINAL_* are finish/pattern/damper, a different set)
            "HVC_TERM_TYPE_TXT", "HVC_TERM_FLOW_LS", "HVC_TERM_NECK_DN_MM", "HVC_TERM_THROW_M", "HVC_TERM_NOISE_DBA",
            // CommunicationDevice
            "COM_DEV_TYPE_TXT", "COM_DEV_PORTS_NR", "COM_DEV_POE_TXT", "COM_DEV_SPEED_GBPS", "COM_DEV_VLAN_TXT", "COM_DEV_PATCH_REF_TXT",
            // ElectricalEquipment — equipment class and transformer rating
            "ELC_EQP_TYPE_TXT", "ELC_KVA_RATING",
            // ElectricalFixture
            "ELE_FIX_TYPE_TXT", "ELE_FIX_GANG_COUNT_INT", "ELE_FIX_WAY_CONFIG_TXT", "ELE_FIX_RATING_A",
            "ELE_FIX_IP_RATING_TXT", "ELE_FIX_MOUNT_HEIGHT_MM",
            // FireAlarmDevice
            "FLS_DEV_TYPE_TXT", "FLS_DEV_MOUNT_TXT", "FLS_DEV_ZONE_TXT", "FLS_DEV_LOOP_TXT", "FLS_DEV_LOOP_ADDR_TXT", "FLS_DEV_CERT_TXT",
            // FireDamper
            "FD_ACTUATION_TXT", "FD_TRIGGER_TEMP_C", "FD_BSEN15650_CLASS_TXT", "FD_RESET_AFTER_TEST_TXT",
            // JunctionBox — read by CableScheduleBuilder (ELC_JB_IP_RATING_TXT) from the family, by name
            "ELC_JB_IP_RATING_TXT", "ELC_JB_FIRE_RATING_TXT", "ELC_JB_ATEX_ZONE_TXT", "ELC_JB_DOWNSTREAM_REF_TXT",
            // LabFixture
            "LAB_FIX_TYPE_TXT", "LAB_FIX_FACE_VEL_MS", "LAB_FIX_AIR_FLOW_LS", "LAB_FIX_DELUGE_LMIN",
            "LAB_FIX_HAZARD_CLASS_TXT", "LAB_FIX_BACKFLOW_CAT_TXT",
            // LightingFixture — LightingGrid reads the UF/MF factors from the family type, by name
            "LTG_TYPE_TXT", "LTG_IP_RATING_TXT", "ELC_LIGHTING_UF_FACTOR", "ELC_LIGHTING_MF_FACTOR",
            // MechanicalEquipment
            "HVC_EQP_TYPE_TXT", "HVC_EQP_DUTY_KW", "HVC_EQP_FLOW_LS", "HVC_EQP_NOISE_DBA", "HVC_EQP_COP_FACTOR", "HVC_EQP_REFRIG_TXT",
            // MedGasOutlet — the seven ROADMAP DT-4 records as needing registration
            "MGS_TU_TYPE_TXT", "MGS_GASES_TXT", "MGS_SOCKET_STD_TXT", "MGS_OPERATING_KPA", "MGS_CERT_TXT",
            "MGS_HOSPITAL_AREA_TXT", "MGS_AVSU_ZONE_TXT",
            // PlumbingEquipment — PLM_EQP_PRESSURE_BAR is not PLM_EQP_PRESSURE_KPA (different unit, not a rename)
            "PLM_EQP_TYPE_TXT", "PLM_EQP_HEAT_OUTPUT_KW", "PLM_EQP_PRESSURE_BAR", "PLM_EQP_INLET_DN_MM", "PLM_EQP_OUTLET_DN_MM",
            // PlumbingFixture
            "PLM_FIX_ACCESSIBLE_BOOL", "PLM_FIX_FLOWRATE_LMIN", "PLM_FIX_HOT_COLD_TXT", "PLM_FIX_DRAIN_DN_MM",
            // Sprinkler (the only registered FLS_SPR* is FLS_SPRINKLER_COUNT_NR)
            "FLS_SPR_TYPE_TXT", "FLS_SPR_RESPONSE_TXT", "FLS_SPR_K_FACTOR", "FLS_SPR_TEMP_C", "FLS_SPR_HAZARD_TXT", "FLS_SPR_COVER_M2",
        };

        [Fact]
        public void EverySharedSeedParameterIsRegisteredOrAllowListed()
        {
            var reg = Registered();
            var bad = new List<string>();
            foreach (var (file, s, p) in SeedParams())
                if (p.IsShared && !reg.ContainsKey(p.Name) && !UnregisteredSeedVocabulary.Contains(p.Name))
                    bad.Add($"{file} {s.Id}.{p.Name} is \"shared\": true but not in MR_PARAMETERS.txt");
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Fact]
        public void NoSeedParameterIsNamedByARegistryKey()
        {
            var keys = RegistryKeys();
            var bad = new List<string>();
            foreach (var (file, s, p) in SeedParams())
                if (keys.TryGetValue(p.Name, out var real) && real != p.Name)
                    bad.Add($"{file} {s.Id}.{p.Name} is a ParamRegistry key — the parameter is {real}");
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Fact]
        public void EveryRegisteredSeedParameterDeclaresItsRegisteredType()
        {
            var reg = Registered();
            var bad = new List<string>();
            int compared = 0;
            foreach (var (file, s, p) in SeedParams())
            {
                if (!reg.TryGetValue(p.Name, out var dt)) continue;
                compared++;
                if (!DataTypeOf.TryGetValue(p.Type ?? "Text", out var want) || want != dt)
                    bad.Add($"{file} {s.Id}.{p.Name} declares \"{p.Type}\" but is registered {dt}");
            }
            Assert.True(compared > 100, $"only {compared} registered seed parameters compared");
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Fact]
        public void NoFamilyOnlySeedParameterBorrowsARegisteredName()
        {
            var reg = Registered();
            var bad = SeedParams().Where(x => !x.P.IsShared && reg.ContainsKey(x.P.Name))
                .Select(x => $"{x.File} {x.Sym.Id}.{x.P.Name} is \"shared\": false but registered — it would collide with the shared parameter")
                .ToList();
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Fact]
        public void TheAllowListIsNotStale()
        {
            var reg = Registered();
            var used = new HashSet<string>(SeedParams().Where(x => x.P.IsShared).Select(x => x.P.Name), StringComparer.Ordinal);
            var stale = UnregisteredSeedVocabulary
                .Where(n => reg.ContainsKey(n) || !used.Contains(n))
                .Select(n => reg.ContainsKey(n) ? $"{n} is registered now — remove it from the allow-list" : $"{n} is used by no seed — remove it")
                .ToList();
            Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
        }

        [Fact]
        public void EveryVariantParameterIsDeclaredByItsSeed()
        {
            // A variant value for an undeclared parameter is skipped at build with only a warning.
            var bad = new List<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(DataDir(), "Seeds"), "STING_SEED_*.json"))
            {
                var lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(f));
                foreach (var s in lib.Symbols)
                {
                    var declared = new HashSet<string>((s.Parameters ?? new List<ParameterDefinition>()).Select(p => p.Name), StringComparer.Ordinal);
                    foreach (var v in s.TypeVariants ?? new List<TypeVariantDefinition>())
                        foreach (var k in (v.Parameters ?? new Dictionary<string, string>()).Keys)
                            if (!k.StartsWith("_", StringComparison.Ordinal) && !declared.Contains(k))
                                bad.Add($"{Path.GetFileName(f)} {s.Id} variant {v.Name}: {k} is not a declared parameter");
                }
            }
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }
    }
}
