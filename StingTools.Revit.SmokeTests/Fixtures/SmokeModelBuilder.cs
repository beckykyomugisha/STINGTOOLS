// StingTools.Revit.SmokeTests — the test model, built from nothing.
//
// No .rvt is checked in. The model is created from the running Revit's own metric
// template, so a Revit upgrade cannot leave a stale fixture behind, and every element
// the tests assert on is created here with KNOWN geometry — the expected values in
// the tests are computed from these numbers, not read back from what the engine did.
//
// Layout (plan, mm, internal origin; Level 1 at 0):
//
//   Grids   verticals x = 0, 6000, 13500   horizontals y = 0, 5000, 12000
//           (uneven on purpose — even spacing hides ordering bugs)
//   Walls   W1 (1000,-3000)→(11000,-3000)   plain
//           W2 (1000,-7000)→(12000,-7000)   hosts 2 doors + 1 window, unevenly spaced
//           W3 (15000,-8000)→(15000,-2000)  plain, runs along Y
//           arc: centre (6000,-15000), r 3000, 0→π
//           stacked (1000,-10000)→(5000,-10000) when the template has a stacked type
//   Columns on-grid (0,0) · off-grid (6700,5450) → 450 to grid y=5000
//                         · off-grid (13100,9000) → 400 to grid x=13500
//   Pipes   drain "Sanitary"  (1000,3000,z800)→(9000,3000,z700): 8 m run, 100 mm fall = 1:80
//           level  cold water (1000,8000,z750)→(9000,8000,z750)
//   Datum   shared elevation of the internal origin set to +45.250 m, so an IL that
//           ignores the survey point is 45 m out rather than invisibly right.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Revit.SmokeTests.Fixtures
{
    internal sealed class SmokeModel
    {
        public Document Doc;
        public string TemplatePath;
        public string SavedPath;
        public Level Level1;
        public Level Level2;
        public List<Grid> Grids = new List<Grid>();

        public List<Wall> StraightWalls = new List<Wall>();
        public Wall OpeningHost;
        public Wall ArcWall;
        public Wall StackedWall;                       // null when the template has no stacked type
        public List<FamilyInstance> Openings = new List<FamilyInstance>();

        public FamilyInstance ColumnOnGrid;
        /// <summary>Off-grid column → expected perpendicular offset (mm) to its nearest grid.</summary>
        public List<(FamilyInstance Column, double ExpectedOffsetMm)> ColumnsOffGrid = new List<(FamilyInstance, double)>();

        public Pipe Drain;                              // 1:80, in a drainage system
        public Pipe LevelPipe;                          // level, NOT drainage
        public const double DrainFallMm = 100, DrainRunMm = 8000;
        public const double SharedElevationM = 45.250;

        /// <summary>Everything the builder had to skip or substitute, with the reason.</summary>
        public List<string> Notes = new List<string>();

        public string Describe() =>
            $"template '{TemplatePath}', saved '{SavedPath}'" +
            (Notes.Count == 0 ? "" : "; notes: " + string.Join(" | ", Notes));
    }

    internal static class SmokeModelBuilder
    {
        internal const double MmPerFt = 304.8;
        internal static double Ft(double mm) => mm / MmPerFt;
        internal static XYZ P(double xMm, double yMm, double zMm = 0) => new XYZ(Ft(xMm), Ft(yMm), Ft(zMm));

        public static SmokeModel Build(Application app)
        {
            var m = new SmokeModel();
            m.TemplatePath = FindTemplate(app, m.Notes);
            m.Doc = m.TemplatePath != null
                ? app.NewProjectDocument(m.TemplatePath)
                : app.NewProjectDocument(UnitSystem.Metric);
            var doc = m.Doc;

            using (var tx = new Transaction(doc, "STING smoke — build fixture"))
            {
                tx.Start();
                SuppressWarnings(tx);

                doc.ProjectInformation.Number = "SMOKE";

                // Levels
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
                m.Level1 = levels.FirstOrDefault() ?? Level.Create(doc, 0);
                m.Level2 = levels.Skip(1).FirstOrDefault() ?? Level.Create(doc, m.Level1.Elevation + Ft(3000));
                if (Math.Abs(m.Level1.Elevation) > 1e-9)
                    m.Notes.Add($"Level 1 is at {m.Level1.Elevation * MmPerFt:F0} mm, not 0 — geometry is placed relative to it.");
                double z0 = m.Level1.Elevation * MmPerFt;

                // Shared datum
                doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero,
                    new ProjectPosition(0, 0, Ft(SmokeModel.SharedElevationM * 1000.0), 0));

                // Grids — uneven spacing on both axes
                foreach (var x in new[] { 0.0, 6000, 13500 })
                    m.Grids.Add(Grid.Create(doc, Line.CreateBound(P(x, -18000, z0), P(x, 14000, z0))));
                foreach (var y in new[] { 0.0, 5000, 12000 })
                    m.Grids.Add(Grid.Create(doc, Line.CreateBound(P(-2000, y, z0), P(16000, y, z0))));

                // Walls
                var basic = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                    .Where(t => t.Kind == WallKind.Basic)
                    .OrderBy(t => t.Name.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException("Template has no basic wall type.");
                double h = Ft(3000);
                Wall MakeWall(Curve c, ElementId typeId)
                {
                    var w = Wall.Create(doc, c, typeId, m.Level1.Id, h, 0, false, false);
                    // Free ends: a join trims or extends the end faces, and the tests
                    // compare the dimension against the LOCATION-line length.
                    try { WallUtils.DisallowWallJoinAtEnd(w, 0); WallUtils.DisallowWallJoinAtEnd(w, 1); }
                    catch (Exception ex) { m.Notes.Add($"could not disallow joins on wall {w.Id}: {ex.Message}"); }
                    return w;
                }

                m.StraightWalls.Add(MakeWall(Line.CreateBound(P(1000, -3000, z0), P(11000, -3000, z0)), basic.Id));
                m.OpeningHost = MakeWall(Line.CreateBound(P(1000, -7000, z0), P(12000, -7000, z0)), basic.Id);
                m.StraightWalls.Add(m.OpeningHost);
                m.StraightWalls.Add(MakeWall(Line.CreateBound(P(15000, -8000, z0), P(15000, -2000, z0)), basic.Id));
                m.ArcWall = MakeWall(Arc.Create(P(6000, -15000, z0), Ft(3000), 0, Math.PI, XYZ.BasisX, XYZ.BasisY), basic.Id);

                var stacked = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                    .FirstOrDefault(t => t.Kind == WallKind.Stacked);
                if (stacked != null)
                    m.StackedWall = MakeWall(Line.CreateBound(P(1000, -10000, z0), P(5000, -10000, z0)), stacked.Id);
                else
                    m.Notes.Add("template has no stacked wall type — stacked-wall case not built.");

                // Openings on W2, unevenly spaced: doors at 1500 / 6000, window at 9500 from the wall start
                var door = SymbolOf(doc, app, BuiltInCategory.OST_Doors, "Doors",
                    new[] { "M_Single-Flush", "M_Door-Single-Flush", "Single-Flush" }, m.Notes);
                var window = SymbolOf(doc, app, BuiltInCategory.OST_Windows, "Windows",
                    new[] { "M_Fixed", "M_Instance-Window-Fixed", "Window-Fixed" }, m.Notes);
                if (door != null)
                {
                    foreach (var along in new[] { 1500.0, 6000 })
                        m.Openings.Add(doc.Create.NewFamilyInstance(P(1000 + along, -7000, z0), door, m.OpeningHost,
                            m.Level1, StructuralType.NonStructural));
                }
                if (window != null)
                {
                    var wi = doc.Create.NewFamilyInstance(P(1000 + 9500, -7000, z0 + 900), window, m.OpeningHost,
                        m.Level1, StructuralType.NonStructural);
                    m.Openings.Add(wi);
                }
                if (door == null || window == null)
                    m.Notes.Add("door and/or window family unavailable — the opening chain covers only what was placed.");

                // Structural columns
                var col = SymbolOf(doc, app, BuiltInCategory.OST_StructuralColumns, "Structural Columns",
                    new[] { "M_Concrete-Rectangular-Column", "M_Concrete-Square-Column", "Concrete-Rectangular-Column" }, m.Notes);
                if (col != null)
                {
                    m.ColumnOnGrid = doc.Create.NewFamilyInstance(P(0, 0, z0), col, m.Level1, StructuralType.Column);
                    m.ColumnsOffGrid.Add((doc.Create.NewFamilyInstance(P(6700, 5450, z0), col, m.Level1, StructuralType.Column), 450));
                    m.ColumnsOffGrid.Add((doc.Create.NewFamilyInstance(P(13100, 9000, z0), col, m.Level1, StructuralType.Column), 400));
                }
                else m.Notes.Add("no structural column family available — column-to-grid case not built.");

                // Pipes
                var pipeType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault();
                if (pipeType == null) m.Notes.Add("template has no pipe type — pipe cases not built.");
                else
                {
                    var drainSys = PipingSystemTypeFor(doc, MEPSystemClassification.Sanitary, new[] { "Sanitary", "Foul" }, "Foul", m.Notes);
                    var coldSys = PipingSystemTypeFor(doc, MEPSystemClassification.DomesticColdWater,
                        new[] { "Domestic Cold Water", "Cold Water" }, "STING Smoke DCW", m.Notes);

                    m.Drain = Pipe.Create(doc, drainSys.Id, pipeType.Id, m.Level1.Id,
                        P(1000, 3000, z0 + 800), P(1000 + SmokeModel.DrainRunMm, 3000, z0 + 800 - SmokeModel.DrainFallMm));
                    m.LevelPipe = Pipe.Create(doc, coldSys.Id, pipeType.Id, m.Level1.Id,
                        P(1000, 8000, z0 + 750), P(9000, 8000, z0 + 750));
                    foreach (var p in new[] { m.Drain, m.LevelPipe })
                    {
                        try { p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(Ft(100)); }
                        catch (Exception ex) { m.Notes.Add($"could not size pipe {p.Id} to DN100: {ex.Message}"); }
                    }
                }

                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException($"fixture transaction did not commit ({status}) — see the [smoke] Revit failure lines in the log.");
            }

            // Saved, because StingPaths / ContentRoots resolve project folders from the
            // document's path, and the flow-arrow family is written into that tree.
            var dir = Path.Combine(Path.GetTempPath(), "StingRevitSmoke", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            m.SavedPath = Path.Combine(dir, "StingSmoke.rvt");
            doc.SaveAs(m.SavedPath, new SaveAsOptions { OverwriteExistingFile = true });
            return m;
        }

        /// <summary>
        /// The running version's metric multi-discipline template, falling back to other
        /// metric defaults. Null ⇒ NewProjectDocument(UnitSystem.Metric), which carries far
        /// fewer types; that is recorded so a skip can be traced to it.
        /// </summary>
        private static string FindTemplate(Application app, List<string> notes)
        {
            var root = $@"C:\ProgramData\Autodesk\RVT {app.VersionNumber}\Templates";
            var preferred = new[] { "Default-Multi-Discipline_Metric.rte", "DefaultMetric.rte", "Default_M_ENU.rte", "Default_M_ENG.rte" };
            try
            {
                if (Directory.Exists(root))
                {
                    var all = Directory.GetFiles(root, "*.rte", SearchOption.AllDirectories);
                    foreach (var name in preferred)
                    {
                        var hit = all.FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
                        if (hit != null) return hit;
                    }
                }
            }
            catch (Exception ex) { notes.Add($"template search under {root} failed: {ex.Message}"); }

            try
            {
                if (!string.IsNullOrEmpty(app.DefaultProjectTemplate) && File.Exists(app.DefaultProjectTemplate))
                {
                    notes.Add($"no metric template under {root}; used Revit's default {app.DefaultProjectTemplate}.");
                    return app.DefaultProjectTemplate;
                }
            }
            catch (Exception ex) { notes.Add($"DefaultProjectTemplate unreadable: {ex.Message}"); }

            notes.Add($"no template found under {root}; built from NewProjectDocument(Metric).");
            return null;
        }

        /// <summary>
        /// An active family symbol in <paramref name="bic"/>: loaded in the template if
        /// present, otherwise loaded from the running version's content library.
        /// </summary>
        private static FamilySymbol SymbolOf(Document doc, Application app, BuiltInCategory bic, string libFolder,
            string[] preferredNames, List<string> notes)
        {
            FamilySymbol Pick(IEnumerable<FamilySymbol> syms)
            {
                var list = syms.ToList();
                foreach (var n in preferredNames)
                {
                    var hit = list.FirstOrDefault(s => (s.Family?.Name ?? "").IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hit != null) return hit;
                }
                return list.FirstOrDefault();
            }

            var sym = Pick(new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(bic).Cast<FamilySymbol>());
            if (sym == null)
            {
                var lib = $@"C:\ProgramData\Autodesk\RVT {app.VersionNumber}\Libraries";
                try
                {
                    var files = Directory.Exists(lib)
                        ? Directory.GetFiles(lib, "*.rfa", SearchOption.AllDirectories)
                            .Where(f => f.IndexOf(@"\" + libFolder + @"\", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToList()
                        : new List<string>();
                    string file = null;
                    foreach (var n in preferredNames)
                    {
                        file = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).StartsWith(n, StringComparison.OrdinalIgnoreCase)
                                                     && f.IndexOf(@"\English", StringComparison.OrdinalIgnoreCase) >= 0)
                            ?? files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).StartsWith(n, StringComparison.OrdinalIgnoreCase));
                        if (file != null) break;
                    }
                    if (file == null)
                    {
                        notes.Add($"no {libFolder} family in the template and none matching [{string.Join(", ", preferredNames)}] under {lib}.");
                        return null;
                    }
                    if (!doc.LoadFamily(file, out var fam) || fam == null)
                    {
                        notes.Add($"LoadFamily refused {file}.");
                        return null;
                    }
                    notes.Add($"loaded {Path.GetFileName(file)} from the content library.");
                    sym = fam.GetFamilySymbolIds().Select(id => doc.GetElement(id)).OfType<FamilySymbol>().FirstOrDefault();
                }
                catch (Exception ex)
                {
                    notes.Add($"loading a {libFolder} family failed: {ex.Message}");
                    return null;
                }
            }
            if (sym != null && !sym.IsActive) { sym.Activate(); doc.Regenerate(); }
            return sym;
        }

        /// <summary>
        /// A piping system type whose NAME is one of <paramref name="names"/> — the drainage
        /// dimensioner matches drainage by system / type name, so the name is the contract,
        /// not the classification. Created when the template has none.
        /// </summary>
        private static PipingSystemType PipingSystemTypeFor(Document doc, MEPSystemClassification cls,
            string[] names, string createName, List<string> notes)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().ToList();
            foreach (var n in names)
            {
                var hit = all.FirstOrDefault(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            notes.Add($"no piping system type named [{string.Join(", ", names)}]; created '{createName}'.");
            return PipingSystemType.Create(doc, cls, createName);
        }

        /// <summary>
        /// No failure may reach a modal dialog — an unattended run would hang on it.
        /// Warnings (joins, overlaps) are deleted and logged. An ERROR rolls the
        /// transaction back and is logged, so the calling test fails with
        /// "did not commit" and the reason is in the run log.
        /// </summary>
        internal static void SuppressWarnings(Transaction tx)
        {
            var opts = tx.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(new FailureRecorder(tx.GetName()));
            opts.SetClearAfterRollback(true);
            opts.SetForcedModalHandling(false);
            tx.SetFailureHandlingOptions(opts);
        }

        private sealed class FailureRecorder : IFailuresPreprocessor
        {
            private readonly string _tx;
            public FailureRecorder(string tx) { _tx = tx; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                bool error = false;
                foreach (var f in fa.GetFailureMessages())
                {
                    var sev = f.GetSeverity();
                    NUnit.Framework.TestContext.Progress.WriteLine(
                        $"[smoke] {_tx}: Revit {sev}: {f.GetDescriptionText()} (elements {string.Join(",", f.GetFailingElementIds())})");
                    if (sev == FailureSeverity.Warning) fa.DeleteWarning(f);
                    else error = true;
                }
                return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
