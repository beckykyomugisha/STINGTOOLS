using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The shipped drawing-type catalogue, checked against the vocabulary the
    /// runner can actually execute.
    ///
    /// <para><b>The defect these gates exist to prevent:</b> an
    /// <c>AutoAnnotationRule.ruleType</c> nobody implemented was
    /// indistinguishable from one that ran. AnnotationRunner filtered tag rules
    /// through a private HashSet and dim rules through three string.Equals
    /// calls, and anything outside both fell through in silence — no warning,
    /// no count, nothing on the drawing. 56 of the 334 rules in
    /// STING_DRAWING_TYPES.json were in that state (17% of the authored
    /// annotation intent): AutoDimWallLength, AutoDimOpenings,
    /// AutoDimColumnGrid, AutoAnnotateSlope, AutoAnnotateFlowArrow,
    /// AutoTagRoomName, AutoTagRoomNumber, AutoAnnotateSpaceNumber.</para>
    ///
    /// <para>Two directions are asserted, because one alone is circular. The
    /// data must only use names the registry declares, AND every name the
    /// registry declares must belong to a pass — so a future contributor can
    /// neither add a ruleType to the JSON without an implementation, nor
    /// declare one in the registry and leave it unhandled.</para>
    /// </summary>
    public class DrawingAnnotationVocabularyTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static JObject LoadTypes() =>
            JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));

        private static JObject LoadPacks() =>
            JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_VIEW_STYLE_PACKS.json")));

        private static JObject LoadFilters() =>
            JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_AEC_FILTERS.json")));

        // ═════════════════════════════════════════════════════════════════
        //  Direction 1 — the DATA may only use names the registry declares.
        // ═════════════════════════════════════════════════════════════════

        [Fact]
        public void Every_shipped_ruleType_is_in_the_implemented_vocabulary()
        {
            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                var rules = t["annotation"]?["rules"];
                if (rules == null) continue;
                foreach (var rule in rules)
                {
                    var rt = rule["ruleType"]?.ToString();
                    if (!AnnotationRuleKinds.IsKnown(rt))
                        offenders.Add($"{t["id"]}: ruleType '{rt}' (category '{rule["category"]}')");
                }
            }

            Assert.True(offenders.Count == 0,
                "These annotation rules name a ruleType no runner pass handles, so they place nothing:\n  "
                + string.Join("\n  ", offenders.Distinct())
                + "\nValid values: " + string.Join(", ", AnnotationRuleKinds.AllRuleTypes));
        }

        // ═════════════════════════════════════════════════════════════════
        //  Direction 2 — every declared name belongs to a pass. Enumerated
        //  from the registry rather than listed here, so a new member is
        //  covered without anyone remembering to add a case.
        // ═════════════════════════════════════════════════════════════════

        [Fact]
        public void Every_declared_ruleKind_belongs_to_a_pass()
        {
            var unclaimed = AnnotationRuleKinds.All
                .Where(k => k.Pass == AnnotationPass.Unknown)
                .Select(k => k.Name)
                .ToList();
            Assert.True(unclaimed.Count == 0,
                "Declared but assigned to no pass: " + string.Join(", ", unclaimed));
        }

        [Fact]
        public void Resolve_round_trips_every_declared_name_case_insensitively()
        {
            foreach (var name in AnnotationRuleKinds.AllRuleTypes)
            {
                Assert.Equal(name, AnnotationRuleKinds.Resolve(name)?.Name);
                Assert.Equal(name, AnnotationRuleKinds.Resolve(name.ToUpperInvariant())?.Name);
                Assert.Equal(name, AnnotationRuleKinds.Resolve("  " + name.ToLowerInvariant() + "  ")?.Name);
            }
        }

        [Fact]
        public void Unknown_ruleType_resolves_to_null_not_to_a_default()
        {
            // The whole bug was that an unknown name behaved like a no-op
            // rather than an error. Resolve must say "I do not know this".
            Assert.Null(AnnotationRuleKinds.Resolve("AutoDimSomethingInvented"));
            Assert.False(AnnotationRuleKinds.IsKnown("AutoDimSomethingInvented"));
            Assert.Equal(AnnotationPass.Unknown, AnnotationRuleKinds.PassOf("AutoDimSomethingInvented"));
        }

        [Fact]
        public void Null_or_blank_ruleType_defaults_to_AutoTag_matching_the_POCO()
        {
            Assert.Equal(AnnotationRuleKinds.AutoTag, AnnotationRuleKinds.Resolve(null)?.Name);
            Assert.Equal(AnnotationRuleKinds.AutoTag, AnnotationRuleKinds.Resolve("")?.Name);
            Assert.Equal(AnnotationRuleKinds.AutoTag, AnnotationRuleKinds.Resolve("   ")?.Name);
        }

        [Fact]
        public void Room_and_space_aliases_force_their_own_category()
        {
            // AutoTagRoomName on a row whose category says "Walls" must still
            // tag Rooms — the alias only ever meant "tag rooms", and which
            // field the tag prints is the tag family's business.
            Assert.Equal("Rooms",
                AnnotationRuleKinds.EffectiveCategory(AnnotationRuleKinds.AutoTagRoomName, "Walls"));
            Assert.Equal("Rooms",
                AnnotationRuleKinds.EffectiveCategory(AnnotationRuleKinds.AutoTagRoomNumber, null));
            Assert.Equal("Spaces",
                AnnotationRuleKinds.EffectiveCategory(AnnotationRuleKinds.AutoAnnotateSpaceNumber, "Doors"));

            // A kind with no forced category passes the row's own through.
            Assert.Equal("Walls",
                AnnotationRuleKinds.EffectiveCategory(AnnotationRuleKinds.AutoTag, "Walls"));
        }

        // ═════════════════════════════════════════════════════════════════
        //  Style-pack + filter data integrity.
        // ═════════════════════════════════════════════════════════════════

        [Fact]
        public void No_style_pack_declares_the_same_filter_twice()
        {
            // Four such pairs shipped, across three packs, every pair
            // DISAGREEING on colour or weight — the later row silently won and
            // the earlier read as authoritative in the editor.
            var offenders = new List<string>();
            foreach (var p in LoadPacks()["stylePacks"])
            {
                var rules = p["filterRules"] ?? p["filters"];
                if (rules == null) continue;
                var names = rules.Select(r => (r["name"] ?? r["filterName"])?.ToString())
                                 .Where(n => !string.IsNullOrWhiteSpace(n));
                foreach (var g in names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    offenders.Add($"{p["id"]}: '{g.Key}' x{g.Count()}");
            }
            Assert.True(offenders.Count == 0,
                "A pack declaring one filter twice renders only the last row:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_pack_filter_rule_names_a_filter_the_registry_defines()
        {
            var known = new HashSet<string>(
                LoadFilters()["filters"].Select(f => f["name"]?.ToString()),
                StringComparer.OrdinalIgnoreCase);

            var offenders = new List<string>();
            foreach (var p in LoadPacks()["stylePacks"])
            {
                var rules = p["filterRules"] ?? p["filters"];
                if (rules == null) continue;
                foreach (var r in rules)
                {
                    var n = (r["name"] ?? r["filterName"])?.ToString();
                    if (!string.IsNullOrWhiteSpace(n) && !known.Contains(n))
                        offenders.Add($"{p["id"]}: '{n}'");
                }
            }
            Assert.True(offenders.Count == 0,
                "A pack rule naming a filter the registry does not define can never be lazy-created:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_drawing_type_names_a_style_pack_that_exists()
        {
            var packIds = new HashSet<string>(
                LoadPacks()["stylePacks"].Select(p => p["id"]?.ToString()),
                StringComparer.OrdinalIgnoreCase);

            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                var pid = t["viewStylePackId"]?.ToString();
                if (string.IsNullOrWhiteSpace(pid)) continue;
                if (!packIds.Contains(pid)) offenders.Add($"{t["id"]} → '{pid}'");
            }
            Assert.True(offenders.Count == 0, "Dangling viewStylePackId:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_drawing_type_resolves_to_a_style_pack()
        {
            // 8 of 93 profiles named no pack and matched no routing rule, so
            // their views got no category overrides and no filters at all —
            // silently, because every downstream step is null-guarded.
            var packIds = new HashSet<string>(
                LoadPacks()["stylePacks"].Select(p => p["id"]?.ToString()),
                StringComparer.OrdinalIgnoreCase);
            var routing = LoadPacks()["routing"]?
                .Select(r => new ViewStylePackRoutingRule
                {
                    Purpose     = r["purpose"]?.ToString() ?? "*",
                    Discipline  = r["discipline"]?.ToString() ?? "*",
                    Phase       = r["phase"]?.ToString(),
                    StylePackId = r["stylePackId"]?.ToString(),
                })
                .ToList() ?? new List<ViewStylePackRoutingRule>();

            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                var pid = t["viewStylePackId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(pid) && packIds.Contains(pid)) continue;

                var purpose = t["purpose"]?.ToString();
                var disc    = t["discipline"]?.ToString();
                var phase   = t["phase"]?.ToString();
                bool routed = routing.Any(r => r.Matches(purpose, disc, phase)
                                            && !string.IsNullOrWhiteSpace(r.StylePackId)
                                            && packIds.Contains(r.StylePackId));
                if (!routed)
                    offenders.Add($"{t["id"]} (purpose={purpose} disc={disc} phase={phase})");
            }
            Assert.True(offenders.Count == 0,
                "These profiles resolve to NO style pack — no VG overrides, no filters:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_pack_extends_target_exists_and_no_cycle()
        {
            var packs = LoadPacks()["stylePacks"]
                .ToDictionary(p => p["id"].ToString(), p => p["extends"]?.ToString(),
                              StringComparer.OrdinalIgnoreCase);
            foreach (var kv in packs)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { kv.Key };
                var cur = kv.Value;
                while (!string.IsNullOrWhiteSpace(cur))
                {
                    Assert.True(packs.ContainsKey(cur), $"Pack '{kv.Key}' extends missing pack '{cur}'.");
                    Assert.True(seen.Add(cur), $"Extends cycle reached from pack '{kv.Key}' at '{cur}'.");
                    cur = packs[cur];
                }
            }
        }

        [Fact]
        public void Style_pack_routing_uses_real_purpose_and_discipline_vocabulary()
        {
            // The shipped table keyed purpose on "Coord" / "QA" / "ClientReview"
            // / "DesignReview" (none of which are DrawingPurpose values) and
            // discipline on "ARCH" / "MEP" (the types use single letters). It
            // was inert, so nothing broke — now that it is wired it must match.
            var purposes = new HashSet<string>(
                LoadTypes()["drawingTypes"].Select(t => t["purpose"]?.ToString()),
                StringComparer.OrdinalIgnoreCase) { "*" };
            var discs = new HashSet<string>(
                LoadTypes()["drawingTypes"].Select(t => t["discipline"]?.ToString()),
                StringComparer.OrdinalIgnoreCase) { "*" };
            var packIds = new HashSet<string>(
                LoadPacks()["stylePacks"].Select(p => p["id"]?.ToString()),
                StringComparer.OrdinalIgnoreCase);

            var routing = LoadPacks()["routing"];
            Assert.NotNull(routing);
            foreach (var r in routing)
            {
                var purpose = r["purpose"]?.ToString();
                var disc    = r["discipline"]?.ToString();
                var pack    = r["stylePackId"]?.ToString();
                Assert.True(purposes.Contains(purpose),
                    $"Routing purpose '{purpose}' is not a purpose any drawing type declares.");
                Assert.True(discs.Contains(disc),
                    $"Routing discipline '{disc}' is not a discipline any drawing type declares.");
                Assert.True(packIds.Contains(pack),
                    $"Routing rule names missing pack '{pack}'.");
            }
        }

        [Fact]
        public void Style_pack_routing_ends_in_a_catch_all()
        {
            // Without a terminal */* rule the fallback cannot guarantee that a
            // future profile gets any styling at all — which is the condition
            // this whole mechanism exists to remove.
            var routing = LoadPacks()["routing"].ToList();
            var last = routing.Last();
            Assert.Equal("*", last["purpose"]?.ToString());
            Assert.Equal("*", last["discipline"]?.ToString());
            Assert.True(string.IsNullOrWhiteSpace(last["phase"]?.ToString()),
                "The catch-all must not be phase-scoped, or it is not a catch-all.");
        }

        // ═════════════════════════════════════════════════════════════════
        //  Scale / purpose coherence.
        // ═════════════════════════════════════════════════════════════════

        [Fact]
        public void No_profile_claims_NTS_in_its_id_while_carrying_a_real_scale()
        {
            // Four did, with scale 1 — which DT-095 passes (it only fires on
            // <= 0) and DrawingTypePresentation APPLIES, giving a genuine 1:1
            // drafting view and a 5mm tag text height instead of 2.5mm.
            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                var id = t["id"].ToString();
                if (id.IndexOf("NTS", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var scale = t["scale"];
                if (scale != null && scale.Type != JTokenType.String)
                    offenders.Add($"{id}: scale {scale}");
            }
            Assert.True(offenders.Count == 0,
                "NTS in the id but a numeric scale:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Schedules_carry_no_scale()
        {
            // A ScheduleView has no Scale property; the assignment throws and
            // is caught as a warning on every single run.
            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                if (t["purpose"]?.ToString() != "Schedule") continue;
                var scale = t["scale"];
                if (scale != null && scale.Type != JTokenType.String)
                    offenders.Add($"{t["id"]}: scale {scale}");
            }
            Assert.True(offenders.Count == 0,
                "Schedule profiles with a numeric scale:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Print_colour_schemes_come_from_one_closed_vocabulary()
        {
            // Two spellings shipped for one concept — "Monochrome" (38
            // profiles) and "BlackAndWhite" (28) — and nothing validated
            // either, so a third could appear and silently mean nothing.
            var allowed = new HashSet<string>(
                new[] { "Monochrome", "ByDiscipline", "PresentationRich", "PresentationMono", "ClarificationRed" },
                StringComparer.OrdinalIgnoreCase);
            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                var cs = t["print"]?["colourScheme"]?.ToString();
                if (!string.IsNullOrWhiteSpace(cs) && !allowed.Contains(cs))
                    offenders.Add($"{t["id"]}: '{cs}'");
            }
            Assert.True(offenders.Count == 0,
                "Unknown print.colourScheme (nothing branches on it):\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Auto3DTag_is_declared_at_most_once_per_profile()
        {
            // The 3D pass tags the whole view once and ignores the per-row
            // category, so five profiles declaring it eight times each were
            // 40 rows expressing 5 decisions.
            var offenders = new List<string>();
            foreach (var t in LoadTypes()["drawingTypes"])
            {
                var rules = t["annotation"]?["rules"];
                if (rules == null) continue;
                int n = rules.Count(r => string.Equals(r["ruleType"]?.ToString(),
                    AnnotationRuleKinds.Auto3DTag, StringComparison.OrdinalIgnoreCase));
                if (n > 1) offenders.Add($"{t["id"]}: {n}");
            }
            Assert.True(offenders.Count == 0,
                "Redundant Auto3DTag rows:\n  " + string.Join("\n  ", offenders));
        }
    }
}
