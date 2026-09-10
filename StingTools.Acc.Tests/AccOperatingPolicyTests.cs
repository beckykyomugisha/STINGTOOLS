// The prompt/no-prompt decision for the ACC coordination cycle.
//
// These seams are Revit UI dialogs, and no headless test can assert "a TaskDialog did not
// appear". So the DECISION is extracted into AccOperatingPolicy and asserted here; the
// commands only show UI when the decision says to.
//
// The contract that carries the most weight is the dullest one: ABSENT CONFIGURATION MEANS
// PROMPT. Every project that exists today has no settings file, and a default that quietly
// picked a model set, a suitability code or an escalation count would make all of them act
// on a guess. Interactive is safe; unattended is opt-in, per project, in writing.
//
// The second is that MALFORMED must be distinguishable from ABSENT. They answer the same -
// everything prompts - but they are different states, because a corrupt file that reads as
// a deliberate default is the ROADMAP IM-17 shape: two meanings for one value, reconciled
// by a silent fallback.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccOperatingPolicyTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public AccOperatingPolicyTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accpolicy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, AccOperatingPolicy.FileName);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private AccOperatingPolicy Write(string json)
        {
            File.WriteAllText(_path, json);
            return AccOperatingPolicy.Load(_path);
        }

        /// <summary>Every answer is the prompt-preserving one. Written once so each test
        /// below asserts the WHOLE surface rather than the field it happens to care about -
        /// a half-applied configuration is the failure mode, so a partial assertion would
        /// miss it.</summary>
        private static void AssertEverythingPrompts(AccOperatingPolicy p)
        {
            Assert.True(p.MayPrompt);
            Assert.False(p.IsUnattended);
            Assert.Equal(string.Empty, p.CoordModelSetId);
            Assert.Equal(string.Empty, p.CoordModelSetName);
            Assert.Equal(string.Empty, p.PublishSuitability);
            Assert.False(p.Escalation.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(p.Escalation.DisabledReason));
        }

        // ── Absent: the normal state of every project today ─────────────────

        [Fact]
        public void AbsentFile_EverythingPrompts()
        {
            Assert.False(File.Exists(_path));
            var p = AccOperatingPolicy.Load(_path);

            Assert.Equal(AccPolicySource.Absent, p.Source);
            Assert.Equal(string.Empty, p.LoadError);
            AssertEverythingPrompts(p);
            Assert.Contains("will prompt", p.DescribeSource(), StringComparison.Ordinal);
        }

        [Fact]
        public void NullOrEmptyPath_EverythingPrompts()
        {
            // An unsaved document gives StingPaths nothing to resolve, so the path is null.
            foreach (var path in new[] { null, "" })
            {
                var p = AccOperatingPolicy.Load(path);
                Assert.Equal(AccPolicySource.Absent, p.Source);
                AssertEverythingPrompts(p);
            }
        }

        // ── Malformed: same answers, different state ────────────────────────

        [Theory]
        [InlineData("not json at all {{{", "not valid JSON")]
        [InlineData("", "empty")]
        [InlineData("   ", "empty")]
        [InlineData("[1,2,3]", "not valid JSON")]          // JObject.Parse rejects an array
        public void MalformedFile_PromptsAndIsDistinguishableFromAbsent(string body, string expectInReason)
        {
            var p = Write(body);

            AssertEverythingPrompts(p);                                  // prompt-preserving
            Assert.Equal(AccPolicySource.Malformed, p.Source);           // ...and NOT Absent
            Assert.NotEqual(AccPolicySource.Absent, p.Source);
            Assert.False(string.IsNullOrWhiteSpace(p.LoadError));
            Assert.Contains(expectInReason, p.LoadError, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("could NOT be read", p.DescribeSource(), StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnknownKey_MakesTheFileMalformed_RatherThanBeingIgnored()
        {
            // The trap this closes: 'coordModelSet' for 'coordModelSetId'. Ignored, it
            // leaves an Information Manager believing the project is configured when it is
            // not, and the only symptom is a picker that keeps appearing.
            var p = Write("{\"coordModelSet\":\"ms-1\"}");

            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.Contains("coordModelSet", p.LoadError, StringComparison.Ordinal);
            Assert.Contains("does not read", p.LoadError, StringComparison.Ordinal);
            AssertEverythingPrompts(p);
        }

        [Fact]
        public void UnderscorePrefixedKeys_AreAllowed()
        {
            var p = Write("{\"_comment\":\"why this file exists\",\"coordModelSetId\":\"ms-1\"}");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Equal("ms-1", p.CoordModelSetId);
        }

        [Theory]
        [InlineData("{\"unattended\":\"yes\"}", "unattended")]
        [InlineData("{\"coordModelSetId\":42}", "coordModelSetId")]
        [InlineData("{\"escalateMaxCount\":\"ten\"}", "escalateMaxCount")]
        [InlineData("{\"escalateMinScore\":\"high\"}", "escalateMinScore")]
        [InlineData("{\"publishSuitability\":1}", "publishSuitability")]
        public void AKeyOfTheWrongType_MakesTheFileMalformed_AndNamesTheKey(string body, string key)
        {
            // Newtonsoft would leave a mistyped field at its type default. That is how
            // "escalateMinScore is not configured" and "escalateMinScore is 0.0" become the
            // same value - and 0.0 as a threshold escalates everything.
            var p = Write(body);
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.Contains(key, p.LoadError, StringComparison.Ordinal);
            AssertEverythingPrompts(p);
        }

        // ── Each field present: that answer, and the others still prompt ────

        [Fact]
        public void UnattendedAlone_StopsPrompting_AndConfiguresNothingElse()
        {
            var p = Write("{\"unattended\":true}");

            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.False(p.MayPrompt);
            Assert.True(p.IsUnattended);
            // ...and every other answer is still unconfigured.
            Assert.Equal(string.Empty, p.CoordModelSetId);
            Assert.Equal(string.Empty, p.PublishSuitability);
            Assert.False(p.Escalation.Enabled);
        }

        [Fact]
        public void ModelSetAlone_IsRemembered_AndTheRunStillPrompts()
        {
            var p = Write("{\"coordModelSetId\":\"ms-kut\",\"coordModelSetName\":\"KUT Federated\"}");

            Assert.Equal("ms-kut", p.CoordModelSetId);
            Assert.Equal("KUT Federated", p.CoordModelSetName);
            Assert.True(p.MayPrompt);                    // remembering a set is not opting out
            Assert.False(p.Escalation.Enabled);
            Assert.Equal(string.Empty, p.PublishSuitability);
        }

        [Fact]
        public void SuitabilityAlone_IsConfigured_AndNothingElseIs()
        {
            var p = Write("{\"publishSuitability\":\"S1\"}");

            Assert.Equal("S1", p.PublishSuitability);
            Assert.True(p.MayPrompt);
            Assert.Equal(string.Empty, p.CoordModelSetId);
            Assert.False(p.Escalation.Enabled);
        }

        [Fact]
        public void ValuesAreTrimmed()
        {
            var p = Write("{\"coordModelSetId\":\"  ms-kut  \",\"publishSuitability\":\" S1 \"}");
            Assert.Equal("ms-kut", p.CoordModelSetId);
            Assert.Equal("S1", p.PublishSuitability);
        }

        [Fact]
        public void AFullyConfiguredProject_AnswersAllFour()
        {
            var p = Write("{\"unattended\":true,\"coordModelSetId\":\"ms-kut\"," +
                          "\"coordModelSetName\":\"KUT Federated\",\"escalateMaxCount\":5," +
                          "\"escalateMinScore\":0.8,\"publishSuitability\":\"S1\"}");

            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.False(p.MayPrompt);
            Assert.Equal("ms-kut", p.CoordModelSetId);
            Assert.True(p.Escalation.Enabled);
            Assert.Equal(5, p.Escalation.MaxCount);
            Assert.Equal(0.8, p.Escalation.MinScore, 6);
            Assert.Equal("S1", p.PublishSuitability);
            Assert.Contains(_path, p.DescribeSource(), StringComparison.Ordinal);
        }

        // ── Suitability resolution ──────────────────────────────────────────

        private static readonly string[] Valid = { "S0", "S1", "S2", "S3", "S4" };

        [Fact]
        public void Suitability_Unset_ResolvesToEmptyWithAReason()
        {
            var p = AccOperatingPolicy.Load(_path);
            string s = p.ResolveSuitability(Valid, out string reason);

            Assert.Equal(string.Empty, s);               // empty means PROMPT
            Assert.Contains("no publish suitability is configured", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Suitability_Configured_ResolvesToIt()
        {
            var p = Write("{\"publishSuitability\":\"S1\"}");
            string s = p.ResolveSuitability(Valid, out string reason);

            Assert.Equal("S1", s);
            Assert.Contains("S1", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Suitability_UnrecognisedCode_Prompts_RatherThanBeingWrittenThrough()
        {
            // A typo must not reach a bundle name and a transmittal row.
            var p = Write("{\"publishSuitability\":\"S9\"}");
            string s = p.ResolveSuitability(Valid, out string reason);

            Assert.Equal(string.Empty, s);
            Assert.Contains("not a recognised code", reason, StringComparison.Ordinal);
            Assert.Contains("S9", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Suitability_MalformedFile_SaysSo_RatherThanJustSayingUnconfigured()
        {
            var p = Write("not json");
            string s = p.ResolveSuitability(Valid, out string reason);

            Assert.Equal(string.Empty, s);
            Assert.Contains("could not be read", reason, StringComparison.OrdinalIgnoreCase);
        }

        // ── DescribeSource is exhaustive ────────────────────────────────────

        [Fact]
        public void EverySource_HasADescription()
        {
            // Enumerated, not listed, so a new source member is covered without anyone
            // remembering. DescribeSource throws on an unhandled member rather than
            // defaulting, so this goes red the moment one is added.
            var all = Enum.GetValues<AccPolicySource>();
            Assert.True(all.Length >= 3, $"expected at least 3 sources, found {all.Length}");

            foreach (var src in all)
            {
                var p = src switch
                {
                    AccPolicySource.Absent => AccOperatingPolicy.Load(_path),
                    AccPolicySource.Malformed => Write("not json"),
                    _ => Write("{\"unattended\":false}"),
                };
                Assert.Equal(src, p.Source);
                Assert.False(string.IsNullOrWhiteSpace(p.DescribeSource()));
            }
        }
    }
}
