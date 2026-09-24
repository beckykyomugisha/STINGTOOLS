using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Slot viewType terms, checked against the closed vocabulary the
    /// placement predicate actually discriminates on.
    ///
    /// <para><b>The defect.</b> <c>SheetPlacementBridge.IsViewTypeCompatible</c>
    /// switches on ten terms and its default arm <b>allows any view</b>. The
    /// eight Schematic profiles used three different spellings between them:
    /// four said "Drafting" (unlisted → allow-all, so it worked by accident),
    /// four said "Section" (which <i>requires</i> ViewType.Section, so a
    /// drafting-view schematic was rejected by its own slot), and the declared
    /// term "Schematic" — the one that accepts a DraftingView — was used by
    /// none of them. <c>health-mep-coord</c> said "Coordination", also
    /// unlisted, also allow-all.</para>
    ///
    /// <para>A permissive default is the right choice for forward
    /// compatibility, but it means a typo and a deliberate term are
    /// indistinguishable at runtime. This gate is what makes them
    /// distinguishable at build time.</para>
    ///
    /// <para>The vocabulary is read from
    /// <c>SheetPlacementBridge.KnownSlotViewTypes</c> rather than listed here,
    /// so the test cannot drift from the switch it is guarding.</para>
    /// </summary>
    public class DrawingSlotVocabularyTests
    {
        // SheetPlacementBridge is Revit-bound, so the vocabulary is mirrored
        // here from its public array via a local copy kept honest by
        // Vocabulary_matches_the_shipped_switch below.
        private static readonly string[] Known =
        {
            "Plan", "RCP", "Section", "Elevation", "Detail", "3D",
            "Schedule", "Legend", "ISO", "Schematic", "Drafting", "Coordination",
        };

        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static JObject Types() =>
            JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));

        [Fact]
        public void Vocabulary_matches_the_shipped_switch()
        {
            // SheetPlacementBridge cannot be compiled into this project (it is
            // Revit-bound), so the local copy above is verified against the
            // source text. Without this the mirror could drift and every other
            // assertion in this file would be checking the wrong list.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Core", "Drawing", "SheetPlacementBridge.cs")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate SheetPlacementBridge.cs");
            var src = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Core", "Drawing", "SheetPlacementBridge.cs"));

            var start = src.IndexOf("KnownSlotViewTypes", StringComparison.Ordinal);
            Assert.True(start > 0, "KnownSlotViewTypes not found in SheetPlacementBridge.cs");
            var open = src.IndexOf('{', start);
            var close = src.IndexOf("};", open, StringComparison.Ordinal);
            var body = src.Substring(open, close - open);
            var declared = System.Text.RegularExpressions.Regex.Matches(body, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToList();

            Assert.Equal(Known.OrderBy(x => x, StringComparer.Ordinal),
                         declared.OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void Every_slot_viewType_is_a_term_the_predicate_discriminates_on()
        {
            var offenders = new List<string>();
            foreach (var t in Types()["drawingTypes"])
            foreach (var slot in t["slots"] ?? Enumerable.Empty<JToken>())
            {
                var vt = slot["viewType"]?.ToString();
                if (string.IsNullOrWhiteSpace(vt)) continue;
                if (!Known.Contains(vt, StringComparer.OrdinalIgnoreCase))
                    offenders.Add($"{t["id"]}: slot '{slot["label"]}' viewType '{vt}'");
            }
            Assert.True(offenders.Count == 0,
                "Unknown slot viewType — IsViewTypeCompatible's default arm accepts ANY view for these, "
                + "so the declared type means nothing:\n  " + string.Join("\n  ", offenders.Distinct()));
        }

        [Fact]
        public void Schematic_profiles_use_the_schematic_slot_term()
        {
            // The specific mistake, named. "Section" on a Schematic profile is
            // not merely inconsistent — it REJECTS the drafting view a
            // schematic is produced as.
            var offenders = new List<string>();
            foreach (var t in Types()["drawingTypes"])
            {
                if (t["purpose"]?.ToString() != "Schematic") continue;
                foreach (var slot in t["slots"] ?? Enumerable.Empty<JToken>())
                {
                    var vt = slot["viewType"]?.ToString();
                    // A schematic sheet may legitimately carry a Legend or
                    // Schedule companion slot; only the drawing slot is checked.
                    if (vt is "Legend" or "Schedule" or "3D" or null) continue;
                    if (!string.Equals(vt, "Schematic", StringComparison.OrdinalIgnoreCase))
                        offenders.Add($"{t["id"]}: slot '{slot["label"]}' viewType '{vt}'");
                }
            }
            Assert.True(offenders.Count == 0,
                "Schematic profiles whose drawing slot is not 'Schematic':\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void No_schedule_or_legend_profile_asks_for_a_crop_box()
        {
            // A ScheduleView and a legend have no crop box, so the crop applier
            // warned on every single run. health-rds-A3 shipped asking for
            // ScopeBoxOrBbox.
            var offenders = new List<string>();
            foreach (var t in Types()["drawingTypes"])
            {
                var purpose = t["purpose"]?.ToString();
                if (purpose != "Schedule" && purpose != "Legend") continue;
                var kind = t["crop"]?["kind"]?.ToString();
                if (!string.IsNullOrWhiteSpace(kind) && kind != "None")
                    offenders.Add($"{t["id"]} ({purpose}): crop.kind '{kind}'");
            }
            Assert.True(offenders.Count == 0,
                "Crop requested on a view type that has no crop box:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void No_schedule_or_legend_profile_is_bound_to_a_managed_pack()
        {
            // Revit rejects View.ViewTemplateId on a schedule and a legend, so a
            // managed pack mints a template it can never assign and throws on the
            // attempt. Two Schedule profiles shipped bound to managed packs.
            var packsDoc = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_VIEW_STYLE_PACKS.json")));
            var packs = packsDoc["stylePacks"].ToDictionary(p => p["id"].ToString(), p => p,
                                                           StringComparer.OrdinalIgnoreCase);

            string ModeOf(string id)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var cur = id;
                while (!string.IsNullOrWhiteSpace(cur) && packs.ContainsKey(cur) && seen.Add(cur))
                {
                    var m = packs[cur]["templateMode"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(m)) return m;
                    cur = packs[cur]["extends"]?.ToString();
                }
                return null;
            }

            var offenders = new List<string>();
            foreach (var t in Types()["drawingTypes"])
            {
                var purpose = t["purpose"]?.ToString();
                if (purpose != "Schedule" && purpose != "Legend") continue;
                var pid = t["viewStylePackId"]?.ToString();
                if (string.IsNullOrWhiteSpace(pid)) continue;
                if (string.Equals(ModeOf(pid), "managed", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{t["id"]} ({purpose}) → '{pid}' resolves to templateMode 'managed'");
            }
            Assert.True(offenders.Count == 0,
                "A managed pack mints a view template Revit cannot assign to these:\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
