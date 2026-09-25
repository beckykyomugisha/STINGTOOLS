using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The sheet-number policy, and the drift it exists to resolve.
    ///
    /// <para>All 90 drawing types carrying an isoNaming block declare the full
    /// ISO 19650-2 field set, yet only 9 of the 93 profiles NUMBER by it; the
    /// other 84 use bespoke short codes. DT-096 only warned on the inverse case
    /// (ISO tokens with no isoNaming), so the mismatch was invisible. Rather
    /// than renumber 84 profiles — which would silently renumber every sheet in
    /// every project in flight — the ISO number is DERIVED from isoNaming under
    /// a project-level policy, so there is no second per-type pattern to
    /// drift.</para>
    ///
    /// <para>The default must remain "profile", because a numbering change is a
    /// document-control event, not a side effect of a plugin update. That is
    /// the first thing asserted below.</para>
    /// </summary>
    public class SheetNumberPolicyTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_DRAWING_TYPES.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        [Theory]
        [InlineData(null,        SheetNumberPolicyKind.Profile)]
        [InlineData("",          SheetNumberPolicyKind.Profile)]
        [InlineData("   ",       SheetNumberPolicyKind.Profile)]
        [InlineData("short",     SheetNumberPolicyKind.Profile)]
        [InlineData("profile",   SheetNumberPolicyKind.Profile)]
        [InlineData("legacy",    SheetNumberPolicyKind.Profile)]
        [InlineData("iso",       SheetNumberPolicyKind.Iso)]
        [InlineData("ISO",       SheetNumberPolicyKind.Iso)]
        [InlineData("iso19650",  SheetNumberPolicyKind.Iso)]
        [InlineData("iso-19650", SheetNumberPolicyKind.Iso)]
        [InlineData("nonsense",  SheetNumberPolicyKind.Profile)]
        public void Parse_defaults_to_profile_for_anything_unrecognised(string input, SheetNumberPolicyKind expected)
            => Assert.Equal(expected, SheetNumberPolicy.Parse(input));

        [Fact]
        public void Profile_policy_never_rewrites_a_pattern()
        {
            var dt = new DrawingType { Id = "x", SheetNumberPattern = "A-RCP-{lvl}-{seq:D3}", IsoNaming = new IsoNaming() };
            Assert.Equal("A-RCP-{lvl}-{seq:D3}",
                SheetNumberPolicy.ResolvePattern(dt, SheetNumberPolicyKind.Profile, out var note));
            Assert.Null(note);
        }

        [Fact]
        public void Iso_policy_rewrites_a_short_pattern_when_isoNaming_exists()
        {
            var dt = new DrawingType
            {
                Id = "arch-rcp",
                SheetNumberPattern = "A-RCP-{lvl}-{seq:D3}",
                IsoNaming = new IsoNaming { Volume = "ZZ", Type = "DR", Role = "A" },
            };
            var got = SheetNumberPolicy.ResolvePattern(dt, SheetNumberPolicyKind.Iso, out var note);
            Assert.Equal(SheetNumberPolicy.IsoPattern, got);
            Assert.Contains("ISO sheet-number policy active", note);
        }

        [Fact]
        public void Iso_policy_refuses_a_profile_with_no_isoNaming_and_says_why()
        {
            // Applying the ISO pattern without the fields to fill it produces
            // "--ZZ--DR--0001--", which is worse than a short code. The refusal
            // must be reported, not silent.
            var dt = new DrawingType { Id = "legend-A3", SheetNumberPattern = "LG-{seq:D2}", IsoNaming = null };
            var got = SheetNumberPolicy.ResolvePattern(dt, SheetNumberPolicyKind.Iso, out var note);
            Assert.Equal("LG-{seq:D2}", got);
            Assert.Contains("no isoNaming block", note);
        }

        [Fact]
        public void Iso_policy_leaves_an_already_iso_pattern_alone()
        {
            var dt = new DrawingType
            {
                Id = "arch-plan",
                SheetNumberPattern = SheetNumberPolicy.IsoPattern,
                IsoNaming = new IsoNaming { Volume = "ZZ" },
            };
            var got = SheetNumberPolicy.ResolvePattern(dt, SheetNumberPolicyKind.Iso, out var note);
            Assert.Equal(SheetNumberPolicy.IsoPattern, got);
            Assert.Null(note);
        }

        [Theory]
        [InlineData("{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}-{suit}-{rev}", true)]
        [InlineData("A-RCP-{lvl}-{seq:D3}", false)]
        [InlineData("{project}-{originator}-{lvl}", false)]   // no {vol}
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsAlreadyIso_requires_all_three_marker_tokens(string pattern, bool expected)
            => Assert.Equal(expected, SheetNumberPolicy.IsAlreadyIso(pattern));

        [Fact]
        public void Every_policy_the_wizard_writes_reads_back_as_itself()
        {
            // Enumerates the enum, so a new policy kind is covered without anyone
            // remembering: if ToParameterValue has no spelling Parse accepts for it,
            // the Project Setup Wizard would write a value the producer reads as Profile.
            foreach (SheetNumberPolicyKind k in Enum.GetValues(typeof(SheetNumberPolicyKind)))
                Assert.Equal(k, SheetNumberPolicy.Parse(SheetNumberPolicy.ToParameterValue(k)));
        }

        [Theory]
        [InlineData(null,       SheetNumberPolicyKind.Profile, "profile")] // record the decision
        [InlineData("",         SheetNumberPolicyKind.Iso,     "iso")]
        [InlineData("short",    SheetNumberPolicyKind.Profile, null)]      // same meaning, keep the user's word
        [InlineData("ISO19650", SheetNumberPolicyKind.Iso,     null)]
        [InlineData("iso",      SheetNumberPolicyKind.Profile, "profile")] // a real change is written
        [InlineData("profile",  SheetNumberPolicyKind.Iso,     "iso")]
        [InlineData("nonsense", SheetNumberPolicyKind.Profile, "profile")] // read as Profile only by default — record the choice
        public void The_wizard_writes_only_a_change_of_policy(string stored, SheetNumberPolicyKind chosen, string expected)
            => Assert.Equal(expected, SheetNumberPolicy.ValueToWrite(stored, chosen));

        [Theory]
        [InlineData("iso", true)]
        [InlineData(" ISO-19650 ", true)]
        [InlineData("short", true)]
        [InlineData("iso 19650", false)]
        [InlineData("nonsense", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsRecognised_tells_a_real_policy_from_a_default(string value, bool expected)
            => Assert.Equal(expected, SheetNumberPolicy.IsRecognised(value));

        [Fact]
        public void Every_spelling_the_wizard_writes_is_recognised()
        {
            foreach (SheetNumberPolicyKind k in Enum.GetValues(typeof(SheetNumberPolicyKind)))
                Assert.True(SheetNumberPolicy.IsRecognised(SheetNumberPolicy.ToParameterValue(k)));
        }

        [Theory]
        [InlineData("profile", "profile", null)]                       // untouched picker: nobody chose
        [InlineData("iso", "iso", null)]
        [InlineData("iso", "profile", SheetNumberPolicyKind.Iso)]      // a real pick
        [InlineData("profile", "iso", SheetNumberPolicyKind.Profile)]
        [InlineData("iso", null, SheetNumberPolicyKind.Iso)]           // nothing was pre-populated
        [InlineData(null, "profile", null)]
        public void The_wizard_records_a_policy_only_when_someone_picked_one(string picked, string prePopulated, SheetNumberPolicyKind? expected)
            => Assert.Equal(expected, SheetNumberPolicy.ChosenOrNull(picked, prePopulated));

        [Fact]
        public void Null_drawing_type_is_tolerated()
            => Assert.Null(SheetNumberPolicy.ResolvePattern(null, SheetNumberPolicyKind.Iso, out _));

        [Fact]
        public void Every_shipped_profile_can_express_its_number_under_both_policies()
        {
            // A profile that can produce neither a usable short number nor a
            // usable ISO one is a hole in the catalogue. Under ISO, "usable"
            // means it either has isoNaming or keeps a non-empty own pattern.
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var offenders = new List<string>();
            foreach (var t in doc["drawingTypes"])
            {
                var pattern = t["sheetNumberPattern"]?.ToString();
                if (string.IsNullOrWhiteSpace(pattern)) { offenders.Add($"{t["id"]}: no sheetNumberPattern"); continue; }

                bool hasIso = t["isoNaming"] != null;
                if (!hasIso && !SheetNumberPolicy.IsAlreadyIso(pattern))
                {
                    // Legitimate — it keeps its own pattern under ISO policy —
                    // but it must at least HAVE one, checked above.
                    continue;
                }
            }
            Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
        }

        [Fact]
        public void Profiles_declaring_isoNaming_declare_the_fields_the_iso_pattern_needs()
        {
            // The ISO pattern references {vol}, {type} and {role}; a profile
            // that opts into ISO numbering with those blank renders empty
            // segments. Reported per profile so the gap is actionable.
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(DataDir(), "STING_DRAWING_TYPES.json")));
            var offenders = new List<string>();
            foreach (var t in doc["drawingTypes"])
            {
                var iso = t["isoNaming"];
                if (iso == null) continue;
                foreach (var f in new[] { "volume", "type", "role" })
                    if (string.IsNullOrWhiteSpace(iso[f]?.ToString()))
                        offenders.Add($"{t["id"]}: isoNaming.{f} is blank");
            }
            Assert.True(offenders.Count == 0,
                "ISO numbering would render empty segments for:\n  " + string.Join("\n  ", offenders));
        }
    }
}
