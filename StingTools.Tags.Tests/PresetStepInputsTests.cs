// Commands run unattended inside a workflow preset read their inputs from the
// step's "params". These tests hold the parsing rules those commands share: an
// absent value takes the documented default, a value that cannot be read is an
// error naming the param — never a quiet fallback to something nobody asked for.

using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class PresetStepInputsTests
    {
        [Fact]
        public void Absent_choice_takes_the_default()
        {
            Assert.True(PresetStepInputs.TryChoice("scope", "", PresetStepInputs.ScopeSelection,
                PresetStepInputs.Scopes, out var v, out var err));
            Assert.Equal(PresetStepInputs.ScopeSelection, v);
            Assert.Null(err);
        }

        [Theory]
        [InlineData("project", "project")]
        [InlineData("All Rooms", "project")]
        [InlineData("active_view", "activeview")]
        [InlineData("Active View", "activeview")]
        [InlineData("SELECTED", "selection")]
        public void Choice_aliases_resolve_to_the_canonical_word(string raw, string expected)
        {
            Assert.True(PresetStepInputs.TryChoice("scope", raw, PresetStepInputs.ScopeSelection,
                PresetStepInputs.Scopes, out var v, out _));
            Assert.Equal(expected, v);
        }

        [Fact]
        public void Unreadable_choice_is_an_error_naming_the_param_and_the_accepted_values()
        {
            Assert.False(PresetStepInputs.TryChoice("scope", "everywhere", PresetStepInputs.ScopeSelection,
                PresetStepInputs.Scopes, out var v, out var err));
            Assert.Null(v);
            Assert.Contains("params.scope", err);
            Assert.Contains("everywhere", err);
            Assert.Contains("activeview", err);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("Yes", true)]
        [InlineData("1", true)]
        [InlineData("off", false)]
        [InlineData("no", false)]
        public void Bool_words_are_read(string raw, bool expected)
        {
            Assert.True(PresetStepInputs.TryBool("supports", raw, !expected, out var v, out _));
            Assert.Equal(expected, v);
        }

        [Fact]
        public void Absent_bool_takes_the_default_and_a_typo_is_an_error()
        {
            Assert.True(PresetStepInputs.TryBool("supports", "  ", true, out var v, out _));
            Assert.True(v);
            Assert.False(PresetStepInputs.TryBool("supports", "maybe", true, out _, out var err));
            Assert.Contains("params.supports", err);
        }

        [Fact]
        public void Absent_number_is_null_not_zero()
        {
            Assert.True(PresetStepInputs.TryNumber("utilityFaultKa", "", 0.1, 200, out var v, out var err));
            Assert.Null(v);
            Assert.Null(err);
        }

        [Fact]
        public void Number_is_read_culture_invariant()
        {
            Assert.True(PresetStepInputs.TryNumber("lengthM", "35.5", 0, 10000, out var v, out _));
            Assert.Equal(35.5, v);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("35,5")]
        [InlineData("-1")]
        [InlineData("20001")]
        [InlineData("NaN")]
        public void Unreadable_or_out_of_range_number_is_an_error(string raw)
        {
            Assert.False(PresetStepInputs.TryNumber("lengthM", raw, 0, 20000, out var v, out var err));
            Assert.Null(v);
            Assert.Contains("params.lengthM", err);
        }

        [Fact]
        public void List_splits_trims_and_drops_repeats()
        {
            Assert.Equal(new List<string> { "a", "b", "c" }, PresetStepInputs.ParseList(" a, b;c |A "));
            Assert.Empty(PresetStepInputs.ParseList(null));
        }

        [Fact]
        public void Unknown_lists_the_requested_names_not_in_the_known_set()
        {
            var unknown = PresetStepInputs.Unknown(new[] { "Electrical", "nope", "PLUMBING" },
                new[] { "electrical", "plumbing" });
            Assert.Equal(new List<string> { "nope" }, unknown);
        }

        [Fact]
        public void Disciplines_accept_the_short_forms()
        {
            Assert.True(PresetStepInputs.TryChoiceList("disciplines", "elec, P ,mechanical",
                PresetStepInputs.DropDisciplines, out var set, out _));
            Assert.Equal(new List<string> { "electrical", "plumbing", "hvac" }, set);
            Assert.False(PresetStepInputs.TryChoiceList("disciplines", "elec, gas",
                PresetStepInputs.DropDisciplines, out _, out var err));
            Assert.Contains("gas", err);
        }

        [Fact]
        public void Place_mode_defaults_are_readable()
        {
            Assert.True(PresetStepInputs.TryChoice("mode", "dry-run", PresetStepInputs.ModePlace,
                PresetStepInputs.PlaceModes, out var v, out _));
            Assert.Equal(PresetStepInputs.ModePreview, v);
        }
    }
}
