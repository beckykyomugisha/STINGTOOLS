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

        public override string ToString() => IsRound ? $"Ø{DiameterMm:F0}" : $"{WidthMm:F0}x{HeightMm:F0}";
    }

    /// <summary>One ExtractedLine classified as a run, with its size.</summary>
    public class MepRunCandidate
    {
        public ExtractedLine Line { get; set; }
        public MepRunKind Kind { get; set; }
        public MepSize Size { get; set; }
        public double LengthFt => Line?.Length ?? 0;
    }

    public class MepRunBuildResult
    {
        public int Created { get; set; }
        public int Failed { get; set; }
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<MepRunKind, int> ByKind { get; } = new Dictionary<MepRunKind, int>();
        public int Tagged { get; set; }
        public void Bump(MepRunKind k) { ByKind.TryGetValue(k, out int n); ByKind[k] = n + 1; }
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
                return new MepSize { IsRound = false, WidthMm = w, HeightMm = h, FromLayer = true };

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
    }

    public class MepRunBuilder
    {
        private readonly Document _doc;

        // Resolved once per build.
        private ElementId _ductType, _mechSys, _pipeType, _pipeSys, _conduitType, _cableTrayType;

        public MepRunBuilder(Document doc) => _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>Create runs for all candidates on the level. One transaction.
        /// <paramref name="offsetByKind"/> overrides the per-kind default elevation when present.</summary>
        public MepRunBuildResult Build(IList<MepRunCandidate> runs, Level level,
            IReadOnlyDictionary<MepRunKind, double> offsetByKind = null)
        {
            var result = new MepRunBuildResult();
            if (runs == null || runs.Count == 0 || level == null) return result;

            ResolveTypes();

            using (var tx = new Transaction(_doc, "STING MODEL: Create MEP Runs from DWG"))
            {
                tx.Start();
                try
                {
                    int i = 0;
                    foreach (var run in runs)
                    {
                        i++;
                        if (run?.Line == null) continue;
                        double offMm = (offsetByKind != null && offsetByKind.TryGetValue(run.Kind, out double o))
                            ? o : MepRunClassifier.DefaultOffsetMm(run.Kind);
                        double z = level.Elevation + StingTools.Model.Units.Mm(offMm);
                        var a = new XYZ(run.Line.Start.X, run.Line.Start.Y, z);
                        var b = new XYZ(run.Line.End.X, run.Line.End.Y, z);
                        if (a.DistanceTo(b) < 0.01) continue;   // degenerate

                        try
                        {
                            Element el = CreateRun(run.Kind, a, b, level.Id, result);
                            if (el == null) continue;
                            ApplySize(el, run.Kind, run.Size, result);
                            ModelWorksetAssigner.Assign(_doc, el);
                            result.CreatedIds.Add(el.Id);
                            result.Created++;
                            result.Bump(run.Kind);
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.Warnings.Count < 30)
                                result.Warnings.Add($"{run.Kind} on '{run.Line.LayerName}': {ex.Message}");
                        }

                        if (i % 50 == 0 && EscapeChecker.IsEscapePressed())
                        {
                            result.Warnings.Add($"Cancelled by user after {i} runs.");
                            break;
                        }
                    }
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

            if (result.CreatedIds.Count > 0)
            {
                try { result.Tagged = ModelEngine.AutoTagCreatedElements(_doc, result.CreatedIds); }
                catch (Exception ex) { StingLog.Warn($"MEP run auto-tag: {ex.Message}"); }
            }
            return result;
        }

        private Element CreateRun(MepRunKind kind, XYZ a, XYZ b, ElementId levelId, MepRunBuildResult result)
        {
            switch (kind)
            {
                case MepRunKind.Duct:
                    if (_ductType == null || _mechSys == null) { Miss(result, "DuctType / MechanicalSystemType"); return null; }
                    return Duct.Create(_doc, _mechSys, _ductType, levelId, a, b);
                case MepRunKind.Pipe:
                    if (_pipeType == null || _pipeSys == null) { Miss(result, "PipeType / PipingSystemType"); return null; }
                    return Pipe.Create(_doc, _pipeSys, _pipeType, levelId, a, b);
                case MepRunKind.Conduit:
                    if (_conduitType == null) { Miss(result, "ConduitType"); return null; }
                    return Conduit.Create(_doc, _conduitType, a, b, levelId);
                case MepRunKind.CableTray:
                    if (_cableTrayType == null) { Miss(result, "CableTrayType"); return null; }
                    return CableTray.Create(_doc, _cableTrayType, a, b, levelId);
                default: return null;
            }
        }

        private void ApplySize(Element el, MepRunKind kind, MepSize size, MepRunBuildResult result)
        {
            if (el == null || size == null) return;
            try
            {
                switch (kind)
                {
                    case MepRunKind.Duct:
                        if (size.IsRound) SetLen(el, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, size.DiameterMm);
                        else { SetLen(el, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, size.WidthMm); SetLen(el, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, size.HeightMm); }
                        break;
                    case MepRunKind.Pipe:
                        SetLen(el, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, size.IsRound ? size.DiameterMm : size.WidthMm);
                        break;
                    case MepRunKind.CableTray:
                        SetLen(el, BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM, size.IsRound ? size.DiameterMm : size.WidthMm);
                        SetLen(el, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, size.IsRound ? size.DiameterMm : size.HeightMm);
                        break;
                    case MepRunKind.Conduit:
                        // Conduit diameter is driven by the conduit TYPE (RBS_CONDUIT_DIAMETER_PARAM
                        // is read-only on the instance) — left at the type default in V2.
                        break;
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Size {kind} {size}: {ex.Message}"); }
        }

        private void SetLen(Element el, BuiltInParameter bip, double mm)
        {
            if (mm <= 0) return;
            var p = el.get_Parameter(bip);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                p.Set(StingTools.Model.Units.Mm(mm));
        }

        private void ResolveTypes()
        {
            _ductType      = FirstId<DuctType>();
            _pipeType      = FirstId<PipeType>();
            _conduitType   = FirstId<ConduitType>();
            _cableTrayType = FirstId<CableTrayType>();

            _mechSys = new FilteredElementCollector(_doc).OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .OrderByDescending(s => s.SystemClassification == MEPSystemClassification.SupplyAir)
                .FirstOrDefault()?.Id;
            _pipeSys = new FilteredElementCollector(_doc).OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>().FirstOrDefault()?.Id;
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
