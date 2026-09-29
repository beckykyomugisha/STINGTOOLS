// StingTools.Revit.SmokeTests — tagging accuracy (ROADMAP TAGACC-12).
//
// The TAGACC-1..11 fixes change how a COMPLETE tag is treated, and every one of them
// lives on the Revit-bound side of the pipeline: element ids, copied instance
// parameters, a host level that changes, a SEQ read back from the model. None of that
// can be unit-tested without Revit, so these tests run the real pipeline
// (TagPipelineHelper.RunFullPipeline, exactly as Batch Tag calls it) on the smoke model:
//
//   1. copy an element                → the copy is re-sequenced, the original keeps its tag
//   2. move an element to a new level → LVL follows it and its SEQ is kept
//   3. run Overwrite twice            → every SEQ survives both runs
//   4. two users tagged before a sync → the newer holder of a shared tag is re-sequenced
//
// (4) is simulated: one Revit session cannot be two users, so the test writes the state a
// sync would leave — two elements holding one tag — and checks the repair. The real
// two-user check is in docs/TAGGING_ACCURACY_TEST_PROTOCOL.md.
//
// The smoke model is created from the metric template and has no STING parameters, so
// the fixture binds the handful the pipeline writes (TaggingBindings). Each test runs in
// a rolled-back TransactionGroup; the bindings are committed once and kept.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using NUnit.Framework;
using StingTools.Core;
using StingTools.Revit.SmokeTests.Fixtures;

namespace StingTools.Revit.SmokeTests
{
    [TestFixture]
    public class TaggingAccuracySmokeTests
    {
        private SmokeModel _m;
        private Document Doc => _m.Doc;

        [OneTimeSetUp]
        public void Setup(Application application)
        {
            _m = SmokeModelCache.Get(application);
            TaggingBindings.PointPluginAtItsDataFolder();
            TaggingBindings.Ensure(Doc);
        }

        // ── 1. copy / paste ─────────────────────────────────────────────────

        [Test]
        public void CopiedElement_IsResequenced_AndTheOriginalKeepsItsTag()
        {
            var source = _m.ColumnsOffGrid.Select(c => c.Column).FirstOrDefault(c => c != null);
            if (source == null) Assert.Ignore("The template has no structural column family.");

            RevitHarness.Isolated(Doc, "tag copy", () =>
            {
                TagPass(new Element[] { source }, TagCollisionMode.AutoIncrement);
                string sourceTag = Tag(source);
                Assert.That(TagConfig.TagIsComplete(sourceTag), Is.True, $"Source was not tagged: '{sourceTag}'");

                Element copy = null;
                RevitHarness.Tx(Doc, "copy column", () =>
                {
                    var ids = ElementTransformUtils.CopyElement(Doc, source.Id, SmokeModelBuilder.P(1500, 0));
                    copy = Doc.GetElement(ids.First());
                });
                Assert.That(Tag(copy), Is.EqualTo(sourceTag),
                    "Precondition: Revit copies instance parameters, so the copy arrives with the source's tag.");

                // A normal (non-overwrite) run — the one that used to skip the copy as complete.
                TagPass(new[] { source, copy }, TagCollisionMode.AutoIncrement);

                Assert.That(Tag(source), Is.EqualTo(sourceTag), "The original must keep its tag.");
                Assert.That(Tag(copy), Is.Not.EqualTo(sourceTag), "The copy still duplicates the original's tag (TAGACC-1).");
                Assert.That(TagConfig.TagIsComplete(Tag(copy)), Is.True, $"The copy's new tag is incomplete: '{Tag(copy)}'");
            });
        }

        // ── 2. moved to another level ───────────────────────────────────────

        [Test]
        public void ElementMovedToAnotherLevel_TakesTheNewLevel_AndKeepsItsSeq()
        {
            Pipe pipe = _m.LevelPipe ?? _m.Drain;
            if (pipe == null) Assert.Ignore("The template has no pipe type.");
            if (_m.Level2 == null) Assert.Ignore("The model has no second level.");

            RevitHarness.Isolated(Doc, "tag move", () =>
            {
                TagPass(new Element[] { pipe }, TagCollisionMode.AutoIncrement);
                string before = Tag(pipe);
                string seqBefore = Token(pipe, ParamRegistry.SEQ);
                Assert.That(TagConfig.TagIsComplete(before), Is.True, $"Pipe was not tagged: '{before}'");
                Assert.That(Token(pipe, ParamRegistry.LVL), Is.EqualTo(ParameterHelpers.GetLevelCodeForLevel(_m.Level1)));

                RevitHarness.Tx(Doc, "move pipe to level 2", () => pipe.ReferenceLevel = _m.Level2);
                Assume.That(pipe.LevelId, Is.EqualTo(_m.Level2.Id), "Revit did not change the pipe's reference level.");

                TagPass(new Element[] { pipe }, TagCollisionMode.AutoIncrement);

                string expectedLvl = ParameterHelpers.GetLevelCodeForLevel(_m.Level2);
                Assert.That(Token(pipe, ParamRegistry.LVL), Is.EqualTo(expectedLvl), "LVL did not follow the move (TAGACC-5).");
                Assert.That(Tag(pipe), Does.Contain(expectedLvl), $"The tag still names the old level: '{Tag(pipe)}'");
                Assert.That(Token(pipe, ParamRegistry.SEQ), Is.EqualTo(seqBefore), "The move renumbered the element.");
            });
        }

        // ── 3. Overwrite twice ──────────────────────────────────────────────

        [Test]
        public void OverwriteTwice_KeepsEverySequenceNumber()
        {
            var elements = Taggable().ToList();
            if (elements.Count < 2) Assert.Ignore("Fewer than two taggable elements in the smoke model.");

            RevitHarness.Isolated(Doc, "overwrite twice", () =>
            {
                TagPass(elements, TagCollisionMode.AutoIncrement);
                var seq = elements.ToDictionary(e => e.Id, e => Token(e, ParamRegistry.SEQ));
                Assert.That(seq.Values.All(s => s.Length > 0), Is.True, "Not every element received a SEQ.");

                TagPass(elements, TagCollisionMode.Overwrite);
                TagPass(elements, TagCollisionMode.Overwrite);

                foreach (var e in elements)
                    Assert.That(Token(e, ParamRegistry.SEQ), Is.EqualTo(seq[e.Id]),
                        $"Overwrite renumbered element {e.Id} ({ParameterHelpers.GetCategoryName(e)}) (TAGACC-4).");
                var tags = elements.Select(Tag).ToList();
                Assert.That(tags.Distinct().Count(), Is.EqualTo(tags.Count), "Overwrite produced duplicate tags.");
            });
        }

        // ── 4. two users tagged before syncing (simulated) ──────────────────

        [Test]
        public void TagHeldByTwoElements_NewerIsResequenced()
        {
            var cols = _m.ColumnsOffGrid.Select(c => c.Column).Where(c => c != null).ToList();
            if (_m.ColumnOnGrid != null) cols.Add(_m.ColumnOnGrid);
            if (cols.Count < 2) Assert.Ignore("Fewer than two structural columns in the smoke model.");
            var older = cols.OrderBy(c => c.Id.Value).First();
            var newer = cols.OrderBy(c => c.Id.Value).Last();

            RevitHarness.Isolated(Doc, "sync duplicate", () =>
            {
                TagPass(new Element[] { older, newer }, TagCollisionMode.AutoIncrement);
                string olderTag = Tag(older);

                // What a sync leaves when two users allocated the same number.
                RevitHarness.Tx(Doc, "simulate sync duplicate", () =>
                {
                    foreach (string p in ParamRegistry.AllTokenParams)
                        ParameterHelpers.SetString(newer, p, ParameterHelpers.GetString(older, p), overwrite: true);
                    ParameterHelpers.SetString(newer, ParamRegistry.TAG1, olderTag, overwrite: true);
                });
                Assert.That(Tag(newer), Is.EqualTo(olderTag), "Precondition: both columns hold one tag.");

                var (index, _) = TagConfig.BuildTagIndexAndCounters(Doc);
                Assert.That(TagConfig.DuplicateHoldersFor(index), Does.Contain(newer.Id.Value),
                    "The index scan did not report the newer holder (TAGACC-2).");

                TagPass(new Element[] { older, newer }, TagCollisionMode.AutoIncrement);
                Assert.That(Tag(older), Is.EqualTo(olderTag), "The older element must keep its tag.");
                Assert.That(Tag(newer), Is.Not.EqualTo(olderTag), "The newer holder was not re-sequenced.");
            });
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private IEnumerable<Element> Taggable()
        {
            foreach (var w in _m.StraightWalls) if (w != null) yield return w;
            foreach (var c in _m.ColumnsOffGrid) if (c.Column != null) yield return c.Column;
            if (_m.ColumnOnGrid != null) yield return _m.ColumnOnGrid;
            if (_m.Drain != null) yield return _m.Drain;
            if (_m.LevelPipe != null) yield return _m.LevelPipe;
        }

        /// <summary>One Batch-Tag-shaped pass: fresh index and context, one committed transaction.</summary>
        private void TagPass(IEnumerable<Element> elements, TagCollisionMode mode)
        {
            var list = elements.ToList();
            var ctx = TokenAutoPopulator.PopulationContext.Build(Doc);
            var (index, counters) = TagConfig.BuildTagIndexAndCounters(Doc);
            bool overwrite = mode == TagCollisionMode.Overwrite;
            RevitHarness.Tx(Doc, "tag pass " + mode, () =>
            {
                foreach (var e in list)
                {
                    var report = new TagConfig.TagWriteReport();
                    TagPipelineHelper.RunFullPipeline(Doc, e, ctx, index, counters, null, null,
                        overwrite: overwrite, skipComplete: !overwrite, collisionMode: mode, report: report);
                    TestContext.Progress.WriteLine($"[tag] {mode} {e.Id} {ParameterHelpers.GetCategoryName(e)} → {report.Outcome} '{Tag(e)}'");
                }
            });
            TokenAutoPopulator.PopulationContext.InvalidateCache();
        }

        private static string Tag(Element e) => ParameterHelpers.GetString(e, ParamRegistry.TAG1) ?? "";
        private static string Token(Element e, string param) => ParameterHelpers.GetString(e, param) ?? "";
    }

    /// <summary>
    /// Binds the parameters the tagging pipeline writes, from a throwaway shared-parameter
    /// file, as instance parameters on the smoke model's categories.
    /// </summary>
    internal static class TaggingBindings
    {
        private static bool _bound;

        private static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_PipeCurves,
        };

        /// <summary>
        /// Outside Revit's add-in startup StingToolsApp never sets DataPath, so every data
        /// file lookup (token policy, parameter registry) would miss and fall back. Point it
        /// at the data folder built beside StingTools.dll, which is the build under test.
        /// </summary>
        public static void PointPluginAtItsDataFolder()
        {
            string dll = typeof(TagConfig).Assembly.Location;
            string data = Path.Combine(Path.GetDirectoryName(dll) ?? "", "data");
            var type = typeof(StingToolsApp);
            type.GetProperty(nameof(StingToolsApp.AssemblyPath))?.GetSetMethod(true)?.Invoke(null, new object[] { dll });
            type.GetProperty(nameof(StingToolsApp.DataPath))?.GetSetMethod(true)?.Invoke(null, new object[] { data });
            if (!Directory.Exists(data))
                TestContext.Progress.WriteLine($"[tag] no data folder at '{data}' — built-in defaults are used.");
            ParamRegistry.EnsureLoaded();
        }

        public static void Ensure(Document doc)
        {
            if (_bound) return;
            var app = doc.Application;
            string previous = app.SharedParametersFilename;
            string path = Path.Combine(Path.GetTempPath(), $"sting_smoke_tag_params_{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, "");
            try
            {
                app.SharedParametersFilename = path;
                DefinitionFile file = app.OpenSharedParameterFile();
                DefinitionGroup group = file.Groups.Create("STING smoke tagging");

                var cats = new CategorySet();
                foreach (var bic in Categories)
                {
                    Category c = Category.GetCategory(doc, bic);
                    if (c != null && c.AllowsBoundParameters) cats.Insert(c);
                }

                RevitHarness.Tx(doc, "bind tagging parameters", () =>
                {
                    foreach (var (name, spec) in Parameters())
                    {
                        try
                        {
                            var def = group.Definitions.Create(new ExternalDefinitionCreationOptions(name, spec));
                            doc.ParameterBindings.Insert(def, new InstanceBinding(cats), GroupTypeId.IdentityData);
                        }
                        catch (Exception ex) { TestContext.Progress.WriteLine($"[tag] could not bind {name}: {ex.Message}"); }
                    }
                });
                _bound = true;
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous)) app.SharedParametersFilename = previous;
                try { File.Delete(path); } catch (Exception ex) { TestContext.Progress.WriteLine($"[tag] temp file: {ex.Message}"); }
            }
        }

        private static IEnumerable<(string, ForgeTypeId)> Parameters()
        {
            var text = SpecTypeId.String.Text;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<(string, ForgeTypeId)> all = ParamRegistry.AllTokenParams.Select(p => (p, text))
                .Concat(new[]
                {
                    (ParamRegistry.TAG1, text), (ParamRegistry.TAG_PREV, text),
                    (ParamRegistry.TAG_MODIFIED_DT, text), (ParamRegistry.TAG_MODIFIED_BY, text),
                    (ParamRegistry.STATUS, text), (ParamRegistry.REV, text),
                    (ParamRegistry.LOC_SOURCE, text), (ParamRegistry.ZONE_SOURCE, text),
                    (ParamRegistry.DISPLAY_TXT, text), ("ASS_TOKEN_LOCK_TXT", text),
                    ("ASS_LAST_TOKEN_HASH_TXT", text),
                    (ParamRegistry.LVL_ELEM_ID, SpecTypeId.Int.Integer),
                    (ParamRegistry.SYS_DETECT_LAYER, SpecTypeId.Int.Integer),
                    (ParamRegistry.STALE, SpecTypeId.Boolean.YesNo),
                });
            foreach (var p in all)
                if (!string.IsNullOrEmpty(p.Item1) && seen.Add(p.Item1)) yield return p;
        }
    }
}
