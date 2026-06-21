// ============================================================================
// MepRunBuilder.cs — Phase: MEP-from-DWG V2.
//
// Turns ExtractedLine segments on MEP layers into straight Revit runs —
// Duct / Pipe / Conduit / CableTray — via the documented point-based *.Create
// APIs. Run kind is inferred from the layer name (explicit run keyword), size
// from a layer-name suffix (e.g. M-DUCT-300x200) or a per-kind default, and the
// run elevation from the level + a per-kind offset. Endpoints come straight from
// the ExtractedLine; geometry extraction is the shared CADToModelEngine core.
//
// Duct/Pipe runs are assigned a Mechanical/Piping system TYPE at creation;
// Conduit/CableTray carry no system in the API. Sizes are set on the instance
// where settable (duct W/H, pipe Ø, tray W/H); conduit diameter is type-driven
// and left at the type default in V2.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using StingTools.Model;   // CADToModelEngine, ExtractedLine, Units, ModelWorksetAssigner

namespace StingTools.Core.Cad.Mep
{
    public enum MepRunKind { Duct, Pipe, Conduit, CableTray }

    /// <summary>Parsed run size. Rectangular (duct/tray) carries W×H; round
    /// (pipe/round-duct/conduit) carries a diameter.</summary>
    public class MepSize
    {
        public bool IsRound { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double DiameterMm { get; set; }
        public bool FromLayer { get; set; }   // true when parsed from the layer name (vs a default)
        /// <summary>P6-1.3 — a W×H size was parsed onto a ROUND kind (pipe/conduit) and
        /// coerced to a diameter (larger dimension). Flagged so the user can confirm.</summary>
        public bool RectCoerced { get; set; }

        public override string ToString() => IsRound ? $"Ø{DiameterMm:F0}" : $"{WidthMm:F0}x{HeightMm:F0}";
    }

    /// <summary>One ExtractedLine classified as a run, with its size.</summary>
    public class MepRunCandidate
    {
        public ExtractedLine Line { get; set; }
        public MepRunKind Kind { get; set; }
        public MepSize Size { get; set; }
        /// <summary>V2 wizard — per-run elevation override (mm above level); null = per-kind default.</summary>
        public double? OffsetMm { get; set; }
        /// <summary>V3 — drainage/sanitary pipe that takes a gravity fall.</summary>
        public bool Drainage { get; set; }
        /// <summary>V3 — fall to apply along a drainage run (% , e.g. 1.25 = 1:80). 0 = flat.</summary>
        public double SlopePercent { get; set; }
        /// <summary>P1.1 — service classification parsed from the layer (drives the
        /// MEP system type at Create). Undefined → the builder uses the fallback.</summary>
        public MEPSystemClassification Classification { get; set; } = MEPSystemClassification.UndefinedSystemClassification;
        /// <summary>P6-1.1 — true when no service keyword matched and the system fell back
        /// silently (duct → Supply, pipe → first-available). Reporting only.</summary>
        public bool ServiceDefaulted { get; set; }
        public double LengthFt => Line?.Length ?? 0;
    }

    /// <summary>V3 — a riser block (UP/DN/RISER) that becomes a short vertical run.</summary>
    public class MepRiserCandidate
    {
        public XYZ Point { get; set; }          // plan XY of the riser (model coords)
        public MepRunKind Kind { get; set; }
        public MepSize Size { get; set; }
        public bool Up { get; set; }            // true = rises (UP), false = drops (DN)
        public string BlockName { get; set; }
        /// <summary>P1.1 — service classification (best-effort from the block layer).</summary>
        public MEPSystemClassification Classification { get; set; } = MEPSystemClassification.UndefinedSystemClassification;
        /// <summary>P1.3 — true when this riser is a drainage stack (a low-point sink for fall direction).</summary>
        public bool DrainageStack { get; set; }
    }

    // ── Service classification (pure) ────────────────────────────────────────
    // P1.1 — maps the service token in a layer name to a MEPSystemClassification
    // so each run gets the right MEP system type instead of "first available".
    // Approximate (DWG layer conventions vary); UndefinedSystemClassification ⇒
    // the builder falls back to the first available system type.
    public static class MepServiceClassifier
    {
        public static MEPSystemClassification Classify(string layerName, MepRunKind kind)
            => Classify(layerName, kind, out _);

        /// <summary><paramref name="defaulted"/> is true when NO explicit service keyword
        /// matched and the result is the silent fallback (duct → Supply, pipe → Undefined →
        /// first-available system). P6-1.1 surfaces these so mis-systemed runs are visible.
        /// Return values are unchanged from the keyword-free overload.</summary>
        public static MEPSystemClassification Classify(string layerName, MepRunKind kind, out bool defaulted)
        {
            defaulted = false;
            string l = (layerName ?? "").ToLowerInvariant();
            if (kind == MepRunKind.Duct)
            {
                if (Regex.IsMatch(l, @"\b(ret|return|ra)\b|return")) return MEPSystemClassification.ReturnAir;
                if (Regex.IsMatch(l, @"\b(exh?|exhaust|ea|toilet.?ext|kitchen.?ext)\b|exhaust")) return MEPSystemClassification.ExhaustAir;
                // Outdoor / fresh air is supply-side — Revit has no distinct OutsideAir
                // classification, so it routes onto SupplyAir. // TODO-VERIFY-API
                if (Regex.IsMatch(l, @"\b(oa|osa|fa|fresh|outdoor|outside)\b|fresh.?air")) return MEPSystemClassification.SupplyAir;
                defaulted = true;                                   // no keyword → silent Supply default
                return MEPSystemClassification.SupplyAir;
            }
            if (kind == MepRunKind.Pipe)
            {
                // Drainage first (most specific).
                if (Regex.IsMatch(l, @"\b(svp|vp|vent)\b|\bvent")) return MEPSystemClassification.Vent;
                if (Regex.IsMatch(l, @"\b(san|soil|foul|waste|wc)\b|sanitary|drainage")) return MEPSystemClassification.Sanitary;
                if (Regex.IsMatch(l, @"\b(rwd|rwp|storm|swd)\b|rain|storm")) return MEPSystemClassification.OtherPipe; // no StormDrain class
                // Domestic.
                if (Regex.IsMatch(l, @"\b(dcw|mcw|cws?|cw)\b")) return MEPSystemClassification.DomesticColdWater;
                if (Regex.IsMatch(l, @"\b(dhwr?|dhw|hws)\b")) return MEPSystemClassification.DomesticHotWater;
                // Hydronic heating/cooling (supply vs return).
                if (Regex.IsMatch(l, @"\b(chwr|hwr|lhwr|hhwr|lphwr|mphwr|cwr)\b")) return MEPSystemClassification.ReturnHydronic;
                if (Regex.IsMatch(l, @"\b(chw|hhw|lhw|lphw|mphw|cwf)\b")) return MEPSystemClassification.SupplyHydronic;
                if (Regex.IsMatch(l, @"\b(cond|condensate|cd)\b")) return MEPSystemClassification.OtherPipe;
                defaulted = true;                                   // no token → Undefined → first-available
            }
            return MEPSystemClassification.UndefinedSystemClassification;
        }
    }

    public class MepRunBuildResult
    {
        public int Created { get; set; }
        public int Failed { get; set; }
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<MepRunKind, int> ByKind { get; } = new Dictionary<MepRunKind, int>();
        /// <summary>P1.1 — resolved system type name → run count, for the report.</summary>
        public Dictionary<string, int> BySystem { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>P1.3 — drainage runs whose fall direction could not be verified against a stack.</summary>
        public int DrainageDirectionUnverified { get; set; }
        /// <summary>P6-1.3 — drainage runs where ≥2 stacks were equally near (fall target ambiguous).</summary>
        public int DrainageDirectionAmbiguous { get; set; }
        /// <summary>P6-1.1 — runs whose system fell back silently (no service keyword).</summary>
        public int ServiceDefaultedDuct { get; set; }
        public int ServiceDefaultedPipe { get; set; }
        /// <summary>P6-1.2 — runs whose applied size differs from the requested size (catalog snap).</summary>
        public int SizeSnapped { get; set; }
        public int Tagged { get; set; }
        public void Bump(MepRunKind k) { ByKind.TryGetValue(k, out int n); ByKind[k] = n + 1; }
        public void BumpSystem(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            BySystem.TryGetValue(name, out int n); BySystem[name] = n + 1;
        }
    }

    // ── Run-kind + size detection (pure, no Revit transaction) ───────────────
    public static class MepRunClassifier
    {
        /// <summary>Minimum run length (mm) — filters short fixture-internal lines.</summary>
        public const double MinRunLengthMm = 500.0;

        /// <summary>Infer the run kind from the layer name + extraction category, or null
        /// when the line is not on a recognised run layer. Requires an explicit run keyword
        /// for electrical containment so fixture-symbol lines on power layers are not misread.</summary>
        public static MepRunKind? DetectKind(string layerName, string category)
        {
            string l = (layerName ?? "").ToLowerInvariant();
            if (Regex.IsMatch(l, @"tray|cabletray|ladder|basket")) return MepRunKind.CableTray;
            if (Regex.IsMatch(l, @"conduit|\bcond\b|\bcdt\b"))      return MepRunKind.Conduit;
            if (l.Contains("duct") || string.Equals(category, "Ducts", StringComparison.OrdinalIgnoreCase))
                return MepRunKind.Duct;
            if (l.Contains("pipe") || l.Contains("rohr") ||
                Regex.IsMatch(l, @"\b(chw|hws|lhw|dcw|dhw|cws|san|rwd|hhw|lhw)\b") ||
                string.Equals(category, "Pipes", StringComparison.OrdinalIgnoreCase))
                return MepRunKind.Pipe;
            return null;
        }

        private static readonly Regex RectRx = new Regex(@"(\d{2,4})\s*[x×*]\s*(\d{2,4})", RegexOptions.IgnoreCase);
        private static readonly Regex DiaRx  = new Regex(@"(?:dn|ø|dia|diam|\bd)\s*[-_]?\s*(\d{2,4})", RegexOptions.IgnoreCase);

        /// <summary>Parse a size from the layer name; falls back to a per-kind default
        /// (FromLayer=false) when nothing parses.</summary>
        public static MepSize ParseSize(string layerName, MepRunKind kind)
        {
            string s = layerName ?? "";
            var rect = RectRx.Match(s);
            if (rect.Success &&
                double.TryParse(rect.Groups[1].Value, out double w) &&
                double.TryParse(rect.Groups[2].Value, out double h) &&
                w >= 20 && h >= 20)
            {
                // P6-1.3 — a pipe/conduit is round; a W×H on its layer is not a real pipe
                // size. Coerce to a diameter (larger dimension) and flag, rather than using
                // the first dimension as the bore (the old silent-but-wrong behaviour).
                if (kind == MepRunKind.Pipe || kind == MepRunKind.Conduit)
                    return new MepSize { IsRound = true, DiameterMm = Math.Max(w, h), FromLayer = true, RectCoerced = true };
                return new MepSize { IsRound = false, WidthMm = w, HeightMm = h, FromLayer = true };
            }

            var dia = DiaRx.Match(s);
            if (dia.Success && double.TryParse(dia.Groups[1].Value, out double d) && d >= 10 && d <= 1200)
                return new MepSize { IsRound = true, DiameterMm = d, FromLayer = true };

            return Default(kind);
        }

        public static MepSize Default(MepRunKind kind) => kind switch
        {
            MepRunKind.Duct      => new MepSize { IsRound = false, WidthMm = 300, HeightMm = 200 },
            MepRunKind.Pipe      => new MepSize { IsRound = true,  DiameterMm = 50 },
            MepRunKind.Conduit   => new MepSize { IsRound = true,  DiameterMm = 25 },
            MepRunKind.CableTray => new MepSize { IsRound = false, WidthMm = 150, HeightMm = 50 },
            _ => new MepSize { IsRound = true, DiameterMm = 50 },
        };

        /// <summary>Default run elevation (mm above the level) per kind, used when no
        /// per-layer offset is supplied.</summary>
        public static double DefaultOffsetMm(MepRunKind kind) => kind switch
        {
            MepRunKind.Duct      => 2700,
            MepRunKind.Pipe      => 2500,
            MepRunKind.Conduit   => 2800,
            MepRunKind.CableTray => 2900,
            _ => 2700,
        };

        /// <summary>V3 — true when a pipe layer is a gravity drainage / sanitary system.</summary>
        public static bool IsDrainage(string layerName)
            => Regex.IsMatch((layerName ?? "").ToLowerInvariant(),
                   @"\b(san|soil|waste|foul|drain|rwd|swd|storm|sewer|wc|svp|vp)\b|drainage|sanitary");

        /// <summary>V3 — default gravity fall for drainage pipe (% of run length). 1:80 ≈ 1.25 %.</summary>
        public const double DefaultDrainageSlopePercent = 1.25;
    }

    // ── Drainage chaining + fall direction (pure, no Revit transaction) ──────
    // P1.2/1.3 — stitch contiguous drainage segments into ordered polylines so a
    // multi-segment drain falls continuously (cumulative invert) toward a stack
    // instead of resetting to flat at every joint.
    public static class MepDrainage
    {
        public struct Seg { public MepRunCandidate Cand; public XYZ S; public XYZ E; }

        /// <summary>P6-1.3 — outcome of orienting a drainage chain's fall.</summary>
        public enum FallResult { Verified, Unverified, Ambiguous }

        private static bool Near(XYZ a, XYZ b, double tolFt)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy) <= tolFt;
        }

        /// <summary>Greedily stitch segments sharing an endpoint (within tol) into ordered chains.</summary>
        public static List<List<Seg>> Chain(IList<MepRunCandidate> drains, double tolFt)
        {
            var chains = new List<List<Seg>>();
            if (drains == null) return chains;
            var remaining = drains.Where(d => d?.Line != null).ToList();

            while (remaining.Count > 0)
            {
                var seed = remaining[0]; remaining.RemoveAt(0);
                var chain = new LinkedList<Seg>();
                chain.AddFirst(new Seg { Cand = seed, S = seed.Line.Start, E = seed.Line.End });

                bool grew = true;
                while (grew)   // extend at the tail
                {
                    grew = false;
                    XYZ tailE = chain.Last.Value.E;
                    for (int k = 0; k < remaining.Count; k++)
                    {
                        var c = remaining[k];
                        if (Near(c.Line.Start, tailE, tolFt)) chain.AddLast(new Seg { Cand = c, S = c.Line.Start, E = c.Line.End });
                        else if (Near(c.Line.End, tailE, tolFt)) chain.AddLast(new Seg { Cand = c, S = c.Line.End, E = c.Line.Start });
                        else continue;
                        remaining.RemoveAt(k); grew = true; break;
                    }
                }
                grew = true;
                while (grew)   // extend at the head
                {
                    grew = false;
                    XYZ headS = chain.First.Value.S;
                    for (int k = 0; k < remaining.Count; k++)
                    {
                        var c = remaining[k];
                        if (Near(c.Line.End, headS, tolFt)) chain.AddFirst(new Seg { Cand = c, S = c.Line.Start, E = c.Line.End });
                        else if (Near(c.Line.Start, headS, tolFt)) chain.AddFirst(new Seg { Cand = c, S = c.Line.End, E = c.Line.Start });
                        else continue;
                        remaining.RemoveAt(k); grew = true; break;
                    }
                }
                chains.Add(chain.ToList());
            }
            return chains;
        }

        /// <summary>Orient the chain so it FALLS toward the nearest stack (the low end ends
        /// last). Unverified when no stack is found; Ambiguous when ≥2 stacks are nearly
        /// equidistant from the low end (caller flags both for the user to confirm).</summary>
        public static FallResult OrientFall(List<Seg> chain, IList<MepRiserCandidate> stacks, double tolFt)
        {
            if (chain == null || chain.Count == 0) return FallResult.Verified;
            var pts = (stacks ?? new List<MepRiserCandidate>())
                .Where(r => r?.Point != null).Select(r => r.Point).ToList();
            if (pts.Count == 0) return FallResult.Unverified;   // keep deterministic order

            XYZ head = chain[0].S, tail = chain[chain.Count - 1].E;
            double Dist(XYZ p) => pts.Min(s => { double dx = p.X - s.X, dy = p.Y - s.Y; return Math.Sqrt(dx * dx + dy * dy); });
            double dHead = Dist(head), dTail = Dist(tail);
            if (dHead < dTail) Reverse(chain);                  // head is the low end → make it last

            // Ambiguous: the two nearest stacks to the LOW end are within a close band.
            XYZ low = dHead < dTail ? head : tail;
            double bandFt = 500.0 / 304.8;                      // 500 mm
            var sorted = pts.Select(s => { double dx = low.X - s.X, dy = low.Y - s.Y; return Math.Sqrt(dx * dx + dy * dy); })
                            .OrderBy(d => d).ToList();
            if (sorted.Count >= 2 && sorted[1] <= sorted[0] + bandFt) return FallResult.Ambiguous;
            return FallResult.Verified;
        }

        private static void Reverse(List<Seg> chain)
        {
            chain.Reverse();
            for (int i = 0; i < chain.Count; i++)
            {
                var s = chain[i];
                chain[i] = new Seg { Cand = s.Cand, S = s.E, E = s.S };
            }
        }
    }

    public class MepRunBuilder
    {
        private readonly Document _doc;

        // Resolved once per build.
        private ElementId _ductType, _pipeType, _conduitType, _cableTrayType;
        // P1.1 — MEP system types by classification + a first-available fallback.
        private Dictionary<MEPSystemClassification, ElementId> _mechByClass, _pipeByClass;
        private ElementId _mechFallback, _pipeFallback;
        // P6-2.4 — resolve types/systems once; Build + BuildRisers share the result.
        private bool _typesResolved;
        // P6-3 — data-driven run policy (offset / default size / fitting tolerance).
        private MepRunRules _rules;
        private MepRunRules Rules => _rules ?? (_rules = MepRunRulesRegistry.Get(_doc));

        public MepRunBuilder(Document doc) => _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>Coincidence tolerance (ft) for chaining drainage segments — matches
        /// MepFittingBuilder so chained ends still join.</summary>
        internal const double TolFt = 12.0 / 304.8;

        /// <summary>Create runs for all candidates on the level. One transaction.
        /// <paramref name="offsetByKind"/> overrides the per-kind default elevation;
        /// <paramref name="stacks"/> (riser/stack candidates) drives drainage fall direction.</summary>
        public MepRunBuildResult Build(IList<MepRunCandidate> runs, Level level,
            IReadOnlyDictionary<MepRunKind, double> offsetByKind = null,
            IList<MepRiserCandidate> stacks = null)
        {
            var result = new MepRunBuildResult();
            if (runs == null || runs.Count == 0 || level == null) return result;

            ResolveTypes();

            double OffMm(MepRunCandidate r) => r.OffsetMm
                ?? (offsetByKind != null && offsetByKind.TryGetValue(r.Kind, out double o) ? o
                : Rules.OffsetMm(r.Kind));
            double tolFt = Rules.FittingToleranceFt;

            var normal = runs.Where(r => r?.Line != null && !(r.Drainage && r.SlopePercent > 0)).ToList();
            var drains = runs.Where(r => r?.Line != null && r.Drainage && r.SlopePercent > 0).ToList();

            using (var tx = new Transaction(_doc, "STING MODEL: Create MEP Runs from DWG"))
            {
                tx.Start();
                try
                {
                    int i = 0;

                    // (a) Non-drainage runs — flat at the per-kind elevation.
                    foreach (var run in normal)
                    {
                        i++;
                        double z = level.Elevation + StingTools.Model.Units.Mm(OffMm(run));
                        var a = new XYZ(run.Line.Start.X, run.Line.Start.Y, z);
                        var b = new XYZ(run.Line.End.X, run.Line.End.Y, z);
                        if (a.DistanceTo(b) >= 0.01) CreateOne(run, a, b, level.Id, result);
                        if (MepBatch.ShouldCancel(i, result.Warnings)) goto done;
                    }

                    // (b) Drainage — chain contiguous segments and fall CONTINUOUSLY (each
                    // segment Start Z = previous End Z) toward the nearest stack (P1.2/1.3).
                    // Only DRAINAGE stacks are valid fall targets — a drain must not orient
                    // toward a supply/return riser that happens to be nearer.
                    var drainageStacks = stacks?.Where(s => s != null && s.DrainageStack).ToList();
                    foreach (var chain in MepDrainage.Chain(drains, tolFt))
                    {
                        var fall = MepDrainage.OrientFall(chain, drainageStacks, tolFt);
                        double cur = level.Elevation + StingTools.Model.Units.Mm(OffMm(chain[0].Cand));
                        foreach (var seg in chain)
                        {
                            i++;
                            double segLenFt = Planar(seg.S, seg.E);
                            double endZ = cur - segLenFt * (seg.Cand.SlopePercent / 100.0);
                            var a = new XYZ(seg.S.X, seg.S.Y, cur);
                            var b = new XYZ(seg.E.X, seg.E.Y, endZ);
                            cur = endZ;
                            if (a.DistanceTo(b) >= 0.01)
                            {
                                var el = CreateOne(seg.Cand, a, b, level.Id, result);
                                if (el != null)
                                {
                                    if (fall == MepDrainage.FallResult.Unverified) result.DrainageDirectionUnverified++;
                                    else if (fall == MepDrainage.FallResult.Ambiguous) result.DrainageDirectionAmbiguous++;
                                }
                            }
                            if (MepBatch.ShouldCancel(i, result.Warnings)) goto done;
                        }
                    }
                    done:
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Error("MepRunBuilder.Build", ex);
                    result.Warnings.Add($"Run batch failed (rolled back): {ex.Message}");
                    result.CreatedIds.Clear();
                    return result;
                }
            }

            if (result.DrainageDirectionUnverified > 0)
                result.Warnings.Add($"{result.DrainageDirectionUnverified} drainage run(s): no stack found to set fall direction — confirm fall manually.");
            if (result.DrainageDirectionAmbiguous > 0)
                result.Warnings.Add($"{result.DrainageDirectionAmbiguous} drainage run(s): ≥2 stacks equally near — fall target ambiguous, confirm.");

            MepBatch.AutoTag(_doc, result.CreatedIds, n => result.Tagged = n);
            return result;
        }

        /// <summary>Create one run + size + workset + bookkeeping. Returns the element or null.</summary>
        private Element CreateOne(MepRunCandidate run, XYZ a, XYZ b, ElementId levelId, MepRunBuildResult result)
        {
            try
            {
                Element el = CreateRun(run.Kind, run.Classification, a, b, levelId, result);
                if (el == null) return null;
                ApplySize(el, run.Kind, run.Size, result);
                ModelWorksetAssigner.Assign(_doc, el);
                result.CreatedIds.Add(el.Id);
                result.Created++;
                result.Bump(run.Kind);
                if (run.ServiceDefaulted)
                {
                    if (run.Kind == MepRunKind.Duct) result.ServiceDefaultedDuct++;
                    else if (run.Kind == MepRunKind.Pipe) result.ServiceDefaultedPipe++;
                }
                return el;
            }
            catch (Exception ex)
            {
                result.Failed++;
                if (result.Warnings.Count < 30)
                    result.Warnings.Add($"{run.Kind} on '{run.Line?.LayerName}': {ex.Message}");
                return null;
            }
        }

        // Planar (XY) length in feet — DWG run lines are planar; ignore any Z noise.
        private static double Planar(XYZ a, XYZ b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>V3 — create a vertical run segment per riser block, spanning from the
        /// current level to the adjacent level above (UP) / below (DN), or ±3 m when there
        /// is none. One transaction. Reuses the same type/system resolution as Build.</summary>
        public MepRunBuildResult BuildRisers(IList<MepRiserCandidate> risers, Level level, IList<Level> levels)
        {
            var result = new MepRunBuildResult();
            if (risers == null || risers.Count == 0 || level == null) return result;

            ResolveTypes();
            double curE = level.Elevation;
            double span3m = StingTools.Model.Units.Mm(3000);
            var sorted = (levels ?? new List<Level>()).OrderBy(l => l.Elevation).ToList();

            using (var tx = new Transaction(_doc, "STING MODEL: Create MEP Risers from DWG"))
            {
                tx.Start();
                try
                {
                    int i = 0;
                    foreach (var r in risers)
                    {
                        i++;
                        if (r?.Point == null) continue;
                        // P1.4 — base the riser at the RUN elevation (level + per-kind offset),
                        // not the bare level, so its base end coincides with horizontal runs at
                        // the same XY and the combined fitting pass can join them.
                        double off = StingTools.Model.Units.Mm(Rules.OffsetMm(r.Kind));
                        double baseE = curE + off;
                        var above = sorted.FirstOrDefault(l => l.Elevation > curE + 0.1);
                        var below = sorted.LastOrDefault(l => l.Elevation < curE - 0.1);
                        double topE = r.Up
                            ? (above != null ? above.Elevation : curE + span3m) + off
                            : (below != null ? below.Elevation : curE - span3m) + off;
                        var a = new XYZ(r.Point.X, r.Point.Y, baseE);
                        var b = new XYZ(r.Point.X, r.Point.Y, topE);
                        if (a.DistanceTo(b) < 0.01) continue;

                        try
                        {
                            var el = CreateRun(r.Kind, r.Classification, a, b, level.Id, result);
                            if (el == null) continue;
                            ApplySize(el, r.Kind, r.Size ?? MepRunClassifier.Default(r.Kind), result);
                            ModelWorksetAssigner.Assign(_doc, el);
                            result.CreatedIds.Add(el.Id);
                            result.Created++;
                            result.Bump(r.Kind);
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.Warnings.Count < 30)
                                result.Warnings.Add($"Riser '{r.BlockName}' ({r.Kind}): {ex.Message}");
                        }

                        if (MepBatch.ShouldCancel(i, result.Warnings)) break;   // P6-4.1 — was missing
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Error("MepRunBuilder.BuildRisers", ex);
                    result.Warnings.Add($"Riser batch failed (rolled back): {ex.Message}");
                    result.CreatedIds.Clear();
                    return result;
                }
            }

            MepBatch.AutoTag(_doc, result.CreatedIds, n => result.Tagged = n);
            return result;
        }

        private Element CreateRun(MepRunKind kind, MEPSystemClassification cls, XYZ a, XYZ b, ElementId levelId, MepRunBuildResult result)
        {
            switch (kind)
            {
                case MepRunKind.Duct:
                {
                    var sys = ResolveSystem(_mechByClass, _mechFallback, cls);
                    if (_ductType == null || sys == null) { Miss(result, "DuctType / MechanicalSystemType"); return null; }
                    var el = Duct.Create(_doc, sys, _ductType, levelId, a, b);
                    RecordSystem(result, sys);
                    return el;
                }
                case MepRunKind.Pipe:
                {
                    var sys = ResolveSystem(_pipeByClass, _pipeFallback, cls);
                    if (_pipeType == null || sys == null) { Miss(result, "PipeType / PipingSystemType"); return null; }
                    var el = Pipe.Create(_doc, sys, _pipeType, levelId, a, b);
                    RecordSystem(result, sys);
                    return el;
                }
                case MepRunKind.Conduit:
                    if (_conduitType == null) { Miss(result, "ConduitType"); return null; }
                    return Conduit.Create(_doc, _conduitType, a, b, levelId);
                case MepRunKind.CableTray:
                    if (_cableTrayType == null) { Miss(result, "CableTrayType"); return null; }
                    return CableTray.Create(_doc, _cableTrayType, a, b, levelId);
                default: return null;
            }
        }

        // P1.1 — system type by classification, else the first-available fallback.
        private static ElementId ResolveSystem(Dictionary<MEPSystemClassification, ElementId> byClass,
            ElementId fallback, MEPSystemClassification cls)
        {
            if (byClass != null && cls != MEPSystemClassification.UndefinedSystemClassification &&
                byClass.TryGetValue(cls, out var id) && id != null)
                return id;
            return fallback;
        }

        private void RecordSystem(MepRunBuildResult result, ElementId sysId)
        {
            try { result.BumpSystem(_doc.GetElement(sysId)?.Name); } catch { }
        }

        private void ApplySize(Element el, MepRunKind kind, MepSize size, MepRunBuildResult result)
        {
            if (el == null || size == null) return;
            try
            {
                bool snapped = false;
                switch (kind)
                {
                    case MepRunKind.Duct:
                        if (size.IsRound) snapped |= Differs(SetLen(el, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, size.DiameterMm), size.DiameterMm);
                        else { snapped |= Differs(SetLen(el, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, size.WidthMm), size.WidthMm);
                               snapped |= Differs(SetLen(el, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, size.HeightMm), size.HeightMm); }
                        break;
                    case MepRunKind.Pipe:
                    {
                        double req = size.IsRound ? size.DiameterMm : size.WidthMm;
                        snapped |= Differs(SetLen(el, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, req), req);
                        break;
                    }
                    case MepRunKind.CableTray:
                    {
                        double rw = size.IsRound ? size.DiameterMm : size.WidthMm;
                        double rh = size.IsRound ? size.DiameterMm : size.HeightMm;
                        snapped |= Differs(SetLen(el, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, rw), rw);
                        snapped |= Differs(SetLen(el, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, rh), rh);
                        break;
                    }
                    case MepRunKind.Conduit:
                        // Conduit diameter is driven by the conduit TYPE (RBS_CONDUIT_DIAMETER_PARAM
                        // is read-only on the instance) — left at the type default in V2.
                        break;
                }
                // P6-1.2 — Revit snaps the instance size to the type's size catalog; report
                // when the APPLIED size differs from what was requested (the model truth).
                if (snapped)
                {
                    result.SizeSnapped++;
                    if (result.Warnings.Count < 30)
                        result.Warnings.Add($"Size snapped to catalog: {kind} requested {size} — type adjusted it.");
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Size {kind} {size}: {ex.Message}"); }
        }

        // Sets the parameter (mm) and returns the APPLIED value in mm (read back, so a
        // catalog snap is visible), or NaN when the parameter can't be set.
        private double SetLen(Element el, BuiltInParameter bip, double mm)
        {
            if (mm <= 0) return double.NaN;
            var p = el.get_Parameter(bip);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return double.NaN;
            p.Set(StingTools.Model.Units.Mm(mm));
            return StingTools.Model.Units.ToMm(p.AsDouble());
        }

        private static bool Differs(double appliedMm, double requestedMm)
            => !double.IsNaN(appliedMm) && Math.Abs(appliedMm - requestedMm) > 1.0;

        private void ResolveTypes()
        {
            if (_typesResolved) return;
            _typesResolved = true;
            _ductType      = FirstId<DuctType>();
            _pipeType      = FirstId<PipeType>();
            _conduitType   = FirstId<ConduitType>();
            _cableTrayType = FirstId<CableTrayType>();

            // P1.1 — index every system type by its classification (first per class).
            var mech = new FilteredElementCollector(_doc).OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>().ToList();
            _mechByClass = new Dictionary<MEPSystemClassification, ElementId>();
            foreach (var s in mech)
                if (!_mechByClass.ContainsKey(s.SystemClassification)) _mechByClass[s.SystemClassification] = s.Id;
            _mechFallback = (mech.FirstOrDefault(s => s.SystemClassification == MEPSystemClassification.SupplyAir)
                             ?? mech.FirstOrDefault())?.Id;

            var pipe = new FilteredElementCollector(_doc).OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>().ToList();
            _pipeByClass = new Dictionary<MEPSystemClassification, ElementId>();
            foreach (var s in pipe)
                if (!_pipeByClass.ContainsKey(s.SystemClassification)) _pipeByClass[s.SystemClassification] = s.Id;
            _pipeFallback = pipe.FirstOrDefault()?.Id;
        }

        private ElementId FirstId<T>() where T : Element
            => new FilteredElementCollector(_doc).OfClass(typeof(T)).FirstElementId() is ElementId id &&
               id != ElementId.InvalidElementId ? id : null;

        private static void Miss(MepRunBuildResult result, string what)
        {
            string w = $"No {what} in project — those runs were skipped. Load an MEP template / type first.";
            if (!result.Warnings.Contains(w)) result.Warnings.Add(w);
        }
    }
}
