// Which coordination model set does the fortnightly cycle pull from?
//
// AccPullClashesCommand opened a picker every run, and the answer is the same every
// fortnight. That single dialog is what stands between the KUT cycle and unattended
// operation.
//
// The resolution is a pure function - remembered id + what ACC returned -> a chosen set or
// a named reason - so it is testable without a Revit dialog.
//
// THE ASSERTION THAT MATTERS: a remembered id that ACC no longer returns must NEVER fall
// through to the first available set. A model set can be renamed, archived or replaced, and
// pulling clashes from the wrong one is worse than pulling none - it looks like a clean
// federation, which is precisely the defect PR #927 closed. Silence here would undo two
// passes of work.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccModelSetResolutionTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public AccModelSetResolutionTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accmodelset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, AccOperatingPolicy.FileName);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private AccOperatingPolicy Remembering(string id, string name = "")
        {
            File.WriteAllText(_path,
                "{\"coordModelSetId\":\"" + id + "\",\"coordModelSetName\":\"" + name + "\"}");
            var p = AccOperatingPolicy.Load(_path);
            Assert.Equal(AccPolicySource.Loaded, p.Source);   // the fixture itself must be sound
            return p;
        }

        private static List<AccModelSet> Sets(params string[] ids) =>
            ids.Select(i => new AccModelSet { Id = i, Name = "Set " + i }).ToList();

        // ── The load-bearing case ───────────────────────────────────────────

        [Fact]
        public void RememberedIdMissingFromTheContainer_IsNamed_AndNeverFallsBackToTheFirstSet()
        {
            var available = Sets("ms-alpha", "ms-beta", "ms-gamma");
            var choice = Remembering("ms-retired", "KUT Federated (2026 Q1)").ResolveModelSet(available);

            Assert.Equal(AccModelSetResolution.RememberedMissing, choice.Resolution);
            Assert.False(choice.Resolved);
            Assert.Null(choice.Chosen);                                  // NOT available[0]
            Assert.NotEqual(AccModelSetResolution.Chosen, choice.Resolution);

            // Named by BOTH name and id, so an operator can find what happened in ACC.
            Assert.Contains("ms-retired", choice.Reason, StringComparison.Ordinal);
            Assert.Contains("KUT Federated (2026 Q1)", choice.Reason, StringComparison.Ordinal);
            Assert.Contains("3 set(s) are available", choice.Reason, StringComparison.Ordinal);
            Assert.Contains("none of them is being assumed", choice.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void RememberedIdMissing_EvenWhenExactlyOneSetIsAvailable_IsStillNotAssumed()
        {
            // The most tempting fallback: there is only one, so surely that is the one.
            // It is not - a container with one set that is not the remembered one is a
            // container whose federation was replaced.
            var choice = Remembering("ms-retired").ResolveModelSet(Sets("ms-the-only-one"));

            Assert.Equal(AccModelSetResolution.RememberedMissing, choice.Resolution);
            Assert.Null(choice.Chosen);
        }

        [Fact]
        public void ARenamedSetKeepingItsId_StillResolves()
        {
            // Matching is by id, never by name, so a rename does not break the cycle - and
            // equally, a DIFFERENT set that happens to take the old name cannot capture it.
            var available = new List<AccModelSet> { new AccModelSet { Id = "ms-kut", Name = "KUT Federated v2" } };
            var choice = Remembering("ms-kut", "KUT Federated").ResolveModelSet(available);

            Assert.Equal(AccModelSetResolution.Chosen, choice.Resolution);
            Assert.Equal("ms-kut", choice.Chosen.Id);
            Assert.Equal("KUT Federated v2", choice.Chosen.Name);        // the LIVE name
        }

        [Fact]
        public void ADifferentSetWearingTheRememberedName_DoesNotCaptureTheCycle()
        {
            var available = new List<AccModelSet> { new AccModelSet { Id = "ms-other", Name = "KUT Federated" } };
            var choice = Remembering("ms-kut", "KUT Federated").ResolveModelSet(available);

            Assert.Equal(AccModelSetResolution.RememberedMissing, choice.Resolution);
            Assert.Null(choice.Chosen);
        }

        // ── The other three resolutions ─────────────────────────────────────

        [Fact]
        public void RememberedIdPresent_IsChosen_WithNoPicker()
        {
            var available = Sets("ms-alpha", "ms-kut", "ms-gamma");
            var choice = Remembering("ms-kut").ResolveModelSet(available);

            Assert.Equal(AccModelSetResolution.Chosen, choice.Resolution);
            Assert.True(choice.Resolved);
            Assert.Equal("ms-kut", choice.Chosen.Id);
            Assert.Equal(string.Empty, choice.Reason);
        }

        [Fact]
        public void MatchingIsCaseInsensitive()
        {
            var choice = Remembering("MS-KUT").ResolveModelSet(Sets("ms-kut"));
            Assert.Equal(AccModelSetResolution.Chosen, choice.Resolution);
        }

        [Fact]
        public void NothingRemembered_Prompts()
        {
            var p = AccOperatingPolicy.Load(_path);          // no settings file at all
            var choice = p.ResolveModelSet(Sets("ms-alpha", "ms-beta"));

            Assert.Equal(AccModelSetResolution.NoneRemembered, choice.Resolution);
            Assert.Null(choice.Chosen);
            Assert.Contains("no coordination model set is remembered", choice.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void NoSetsAvailable_IsNotAResolutionFailure()
        {
            // A container with no model sets is a legitimately empty successful read - the
            // caller's existing EmptyOk path owns it. Conflating it with RememberedMissing
            // would turn an empty container into an error and lose the distinction #927
            // was built to preserve.
            foreach (var empty in new List<AccModelSet>[] { new List<AccModelSet>(), null })
            {
                var choice = Remembering("ms-kut").ResolveModelSet(empty);

                Assert.Equal(AccModelSetResolution.NoSetsAvailable, choice.Resolution);
                Assert.NotEqual(AccModelSetResolution.RememberedMissing, choice.Resolution);
                Assert.Null(choice.Chosen);
            }
        }

        [Fact]
        public void NoSetsAvailable_WinsOverNothingRemembered()
        {
            // Order matters: with neither a remembered id nor any sets, the honest report is
            // "the container is empty", not "you have not configured one".
            var choice = AccOperatingPolicy.Load(_path).ResolveModelSet(new List<AccModelSet>());
            Assert.Equal(AccModelSetResolution.NoSetsAvailable, choice.Resolution);
        }

        [Fact]
        public void EveryResolution_IsReachable()
        {
            // Enumerated rather than listed, so a new resolution member is noticed. If one
            // is added and nothing produces it, this fails and names it.
            // Each policy is built BEFORE the shared settings file is rewritten, and the
            // "nothing remembered" case reads a path with no file at all. Reusing _path
            // across all four made that case load the id the previous line had just
            // written, so it produced Chosen and this test failed — which is exactly what
            // it is for, and worth leaving a note about rather than quietly fixing.
            var chosen = Remembering("ms-kut").ResolveModelSet(Sets("ms-kut")).Resolution;
            var missing = Remembering("ms-gone").ResolveModelSet(Sets("ms-kut")).Resolution;
            var noSets = Remembering("ms-kut").ResolveModelSet(new List<AccModelSet>()).Resolution;
            var unconfigured = AccOperatingPolicy.Load(Path.Combine(_dir, "no-such-file.json"))
                                                 .ResolveModelSet(Sets("ms-kut")).Resolution;

            var produced = new HashSet<AccModelSetResolution> { chosen, missing, noSets, unconfigured };
            var all = Enum.GetValues<AccModelSetResolution>();
            Assert.True(all.Length >= 4, $"expected at least 4 resolutions, found {all.Length}");
            var unreachable = all.Where(r => !produced.Contains(r)).ToList();
            Assert.True(unreachable.Count == 0,
                "no case above produces: " + string.Join(", ", unreachable) +
                " — either it is dead, or this test needs the case that reaches it.");
        }
    }
}
