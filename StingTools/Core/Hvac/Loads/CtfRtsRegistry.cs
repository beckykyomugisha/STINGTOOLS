// StingTools — Conduction Transfer Function (CTF) → Radiant Time Factor.
//
// Phase 187h (Tier-3 RTS). When a zone's envelope segments carry a
// construction-type id present in STING_CTF_COEFFICIENTS.json, the
// per-zone RTF is derived from the area-weighted Y-series convolution
// rather than interpolating between the published Light/Medium/Heavy
// tables.
//
// Pre-tabulated coefficients avoid Laplace-domain inversion at run-
// time. ASHRAE Handbook Fundamentals 2021 Ch.18 §RTS gives the math:
// the Y-series is the wall's hourly impulse response to a unit-step
// temperature input. Sum over all envelope segments weighted by area,
// renormalise to unit sum → per-zone 24-hour RTF.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Hvac.Loads
{
    public class CtfConstruction
    {
        public string Id        { get; set; } = "";
        public string Label     { get; set; } = "";
        public double UValue    { get; set; }
        public double[] Y       { get; set; } = new double[24];
        public string Source    { get; set; } = "";
    }

    public class CtfLibrary
    {
        public Dictionary<string, CtfConstruction> ById { get; }
            = new(StringComparer.OrdinalIgnoreCase);
        public CtfConstruction Get(string id) =>
            !string.IsNullOrWhiteSpace(id) && ById.TryGetValue(id, out var c) ? c : null;
    }

    public static class CtfRtsRegistry
    {
        public const string DataFileName = "STING_CTF_COEFFICIENTS.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/ctf_coefficients.json";

        private static readonly ConcurrentDictionary<string, CtfLibrary> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        public static CtfLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static CtfLibrary Load(Document doc)
        {
            var lib = new CtfLibrary();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), lib);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), lib);
                }
            }
            catch (Exception ex)
            { StingTools.Core.StingLog.Error("CtfRtsRegistry.Load", ex); }
            return lib;
        }

        private static void Apply(JObject j, CtfLibrary lib)
        {
            var arr = j["constructions"] as JArray;
            if (arr == null) return;
            foreach (var c in arr.OfType<JObject>())
            {
                var ctf = new CtfConstruction
                {
                    Id     = (string)c["id"] ?? "",
                    Label  = (string)c["label"] ?? "",
                    UValue = (double?)c["uValue"] ?? 0,
                    Source = (string)c["source"] ?? ""
                };
                var yArr = c["y"] as JArray;
                if (yArr != null)
                {
                    var y = new double[24];
                    for (int i = 0; i < Math.Min(24, yArr.Count); i++) y[i] = (double)yArr[i];
                    ctf.Y = y;
                }
                if (!string.IsNullOrEmpty(ctf.Id)) lib.ById[ctf.Id] = ctf;
            }
        }

        /// <summary>
        /// Build a per-zone Radiant Time Factor array by area-weighting
        /// the construction Y-series across the supplied envelope. Returns
        /// null when none of the envelope segments carry a construction
        /// id present in <paramref name="lib"/>.
        ///
        /// The RTF is renormalised to sum to 1.0 — STING's RTS engine
        /// expects RTFs as percentages of an instantaneous radiant gain.
        /// </summary>
        public static double[] DeriveZoneRtf(IEnumerable<EnvelopeSegment> envelope, CtfLibrary lib)
        {
            if (envelope == null || lib == null) return null;
            var sum = new double[24];
            double sumArea = 0;
            int hits = 0;
            foreach (var seg in envelope)
            {
                var ctf = lib.Get(seg?.ConstructionTypeId);
                if (ctf == null || ctf.Y == null) continue;
                double a = Math.Max(0, seg.AreaM2);
                if (a <= 0) continue;
                for (int i = 0; i < 24 && i < ctf.Y.Length; i++)
                    sum[i] += a * ctf.Y[i];
                sumArea += a;
                hits++;
            }
            if (hits == 0 || sumArea <= 0) return null;
            // Area-weighted impulse response.
            for (int i = 0; i < 24; i++) sum[i] /= sumArea;
            // Renormalise to unit sum so it composes correctly with
            // ApplyRtsToGainWithRtf (which expects RTF as fraction-of-gain).
            double total = sum.Sum();
            if (total <= 1e-12) return null;
            for (int i = 0; i < 24; i++) sum[i] /= total;
            return sum;
        }
    }
}
