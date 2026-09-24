using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.Coordination;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Compliance
{
    /// <summary>
    /// BS 7671:2018 + A2:2022 verification engine. Pure math — no Revit API
    /// dependency. Computes:
    ///
    /// <list type="bullet">
    /// <item><c>Zs = Ze + R1 + R2</c> (earth fault loop impedance)</item>
    /// <item>Disconnection-time check: <c>Zs × Ia ≤ Uo</c> within Table 41.1
    /// time limits. Pass → automatic-disconnection-of-supply (ADS) is met.
    /// Fail → engineer either upsizes the CPC, picks a faster OCPD, or falls
    /// back to RCD protection (Reg 411.4.5).</item>
    /// <item>Adiabatic conductor check: <c>(k·S)² ≥ I²·t</c> per §434.5.2 —
    /// the cable must survive the fault until the OCPD clears it. Catches
    /// the case where Iz looks fine for steady-state but the conductor
    /// melts before the breaker trips.</item>
    /// <item>RCD tier recommendation per Reg 411.3.3 / 411.3.4 / 522.6.202:
    /// 30 mA for sockets ≤32 A in dwellings, cables in walls &lt;50 mm depth,
    /// bathrooms zones 1+2, outdoor sockets, construction sites.</item>
    /// </list>
    ///
    /// All thresholds load from <c>STING_BS7671_DISCONNECTION.json</c>, with a
    /// per-project override at <c>&lt;project&gt;/_BIM_COORD/bs7671_disconnection.json</c>
    /// layered on top (see <see cref="BS7671Thresholds"/>), so a project can
    /// declare its own Ze (e.g. a UMEME supply), Cmin, U0 or OCPD multipliers
    /// without recompiling. Callers with a Document resolve the override path
    /// through <c>StingPaths.MetaFile</c> and pass it in; this class stays
    /// Revit-free.
    /// </summary>
    public static class BS7671ComplianceEngine
    {
        /// <summary>Project override file name under the _BIM_COORD bucket.</summary>
        public const string ProjectOverrideFileName = "bs7671_disconnection.json";

        private static readonly Dictionary<string, BS7671Thresholds> _cache =
            new Dictionary<string, BS7671Thresholds>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        /// <summary>Corporate thresholds only (no project override).</summary>
        public static BS7671Thresholds Thresholds() => Thresholds(null);

        /// <summary>
        /// Corporate thresholds with the project override at
        /// <paramref name="projectOverridePath"/> merged over them (ignored when
        /// null or absent). Cached per override path.
        /// </summary>
        public static BS7671Thresholds Thresholds(string projectOverridePath)
        {
            // Keyed on the override file's last-write time as well as its path, so
            // editing the project Ze takes effect on the next run, not the next
            // Revit session.
            string stamp = "";
            try
            {
                if (!string.IsNullOrEmpty(projectOverridePath) && File.Exists(projectOverridePath))
                    stamp = File.GetLastWriteTimeUtc(projectOverridePath).Ticks.ToString();
            }
            catch (Exception ex) { StingLog.Warn($"BS7671 override stamp: {ex.Message}"); }
            string key = (projectOverridePath ?? "") + "|" + stamp;
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var hit)) return hit;
                string corporate = null;
                try { corporate = StingToolsApp.FindDataFile("STING_BS7671_DISCONNECTION.json"); }
                catch (Exception ex) { StingLog.Warn($"BS7671 thresholds locate: {ex.Message}"); }
                var t = BS7671Thresholds.LoadLayered(corporate, projectOverridePath);
                foreach (var w in t.Warnings) StingLog.Warn($"BS7671 thresholds: {w}");
                _cache[key] = t;
                return t;
            }
        }

        public static void InvalidateCache() { lock (_lock) _cache.Clear(); }

        // ── Earth fault loop impedance Zs ───────────────────────────────

        /// <summary>
        /// Compute Zs = Ze + R1 + R2 in ohms. R1 and R2 are the temperature-
        /// corrected impedances of the phase conductor and CPC over the
        /// circuit length, both at fault temperature (worst case).
        /// </summary>
        public static double ComputeZs(double zeOhm, double phaseCsaMm2, double cpcCsaMm2,
            double lengthM, string material = "Cu", string insulation = "PVC",
            WireTableSet wireTables = null)
        {
            if (lengthM <= 0 || phaseCsaMm2 <= 0) return zeOhm;
            // Use existing FaultCurrentEngine for cable impedance with insulation-aware temp.
            double r1Mohm = FaultCurrentEngine.CableImpedanceMohm(wireTables, phaseCsaMm2, material, lengthM,
                insulation: insulation);
            double r2Mohm = cpcCsaMm2 > 0
                ? FaultCurrentEngine.CableImpedanceMohm(wireTables, cpcCsaMm2, material, lengthM,
                    insulation: insulation)
                : r1Mohm;  // assume CPC = phase if not declared
            return zeOhm + (r1Mohm + r2Mohm) / 1000.0;
        }

        /// <summary>
        /// Verify Zs × Ia ≤ Uo × Cmin (BS 7671 Reg 411.4.4). Returns the result
        /// with the maximum permitted Zs for that OCPD type/rating, the actual
        /// Zs, and pass/fail. Cmin (0.95 shipped, <c>cMin</c> in the JSON) is the
        /// minimum voltage factor that Table 41.3 already includes
        /// (B32: 0.95 × 230 / 160 = 1.37 ohm).
        /// </summary>
        public static ZsCheckResult VerifyZs(double computedZsOhm, string ocpdType, double ratingA,
            double uoV = 230, BS7671Thresholds thresholds = null)
        {
            var th = thresholds ?? Thresholds();
            double iaMult = th.IaMultiplier.TryGetValue(ocpdType?.ToUpperInvariant() ?? "", out double m)
                ? m : 5.0;
            double ia = iaMult * Math.Max(ratingA, 1);
            double zsMax = th.Cmin * uoV / Math.Max(ia, 1);
            return new ZsCheckResult
            {
                OcpdType         = ocpdType,
                RatingA          = ratingA,
                IaA              = ia,
                ZsMaxOhm         = zsMax,
                ZsActualOhm      = computedZsOhm,
                Passes           = computedZsOhm <= zsMax,
                MarginPercent    = zsMax > 0 ? (1 - computedZsOhm / zsMax) * 100.0 : 0
            };
        }

        // ── Adiabatic check (k·S)² ≥ I²·t ────────────────────────────────

        /// <summary>
        /// Adiabatic conductor verification per BS 7671 §434.5.2. The cable
        /// must survive thermally until the OCPD clears the fault. Pass when
        /// (k·S)² ≥ I²·t. Negative margin = conductor undersized.
        /// </summary>
        public static AdiabaticResult VerifyAdiabatic(double csaMm2, string material, string insulation,
            double faultCurrentA, double clearingTimeSec, BS7671Thresholds thresholds = null)
        {
            var th = thresholds ?? Thresholds();
            string key = $"{material ?? "Cu"}/{(insulation ?? "PVC").ToUpperInvariant()}";
            if (!th.AdiabaticK.TryGetValue(key, out double k)) k = 115; // Cu/PVC fallback
            double left  = Math.Pow(k * csaMm2, 2);
            double right = Math.Pow(faultCurrentA, 2) * clearingTimeSec;
            double minCsa = clearingTimeSec > 0
                ? Math.Sqrt(right) / k
                : 0;
            return new AdiabaticResult
            {
                K               = k,
                CsaMm2          = csaMm2,
                FaultCurrentA   = faultCurrentA,
                ClearingTimeSec = clearingTimeSec,
                LeftHandKsq     = left,
                RightHandIsqT   = right,
                MinCsaMm2       = Math.Ceiling(minCsa * 10) / 10,
                Passes          = left >= right
            };
        }

        // ── RCD strategy ─────────────────────────────────────────────────

        /// <summary>
        /// Recommend an RCD/RCBO sensitivity tier for a given circuit
        /// based on regulatory scenarios it matches. Returns the lowest
        /// tier that satisfies all matching scenarios (most onerous wins).
        /// </summary>
        public static RcdRecommendation RecommendRcd(string circuitContext, string earthingSystem,
            BS7671Thresholds thresholds = null)
        {
            var th = thresholds ?? Thresholds();
            string ctx = (circuitContext ?? "").ToLowerInvariant();
            int chosen = 0;
            string regList = "";
            foreach (var s in th.RcdScenarios)
            {
                bool hit = false;
                switch (s.Scenario)
                {
                    case "socket_le32A_dwelling":
                        hit = ctx.Contains("socket") || ctx.Contains("rcbo") || ctx.Contains("ring");
                        break;
                    case "cable_in_wall_lt50mm":
                        hit = ctx.Contains("wall") || ctx.Contains("buried") || ctx.Contains("conceal");
                        break;
                    case "bathroom_zone1_zone2":
                        hit = ctx.Contains("bath") || ctx.Contains("shower") || ctx.Contains("zone1") || ctx.Contains("zone2");
                        break;
                    case "outdoor_socket":
                        hit = ctx.Contains("outdoor") || ctx.Contains("garden") || ctx.Contains("external");
                        break;
                    case "TT_system_all_circuits":
                        hit = string.Equals(earthingSystem, "TT", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "construction_site":
                        hit = ctx.Contains("temporary") || ctx.Contains("site");
                        break;
                }
                if (hit)
                {
                    if (chosen == 0 || s.IMaxMA < chosen) chosen = s.IMaxMA;
                    regList += (regList.Length > 0 ? ", " : "") + s.Reg;
                }
            }
            return new RcdRecommendation
            {
                RecommendedMA   = chosen,
                Mandatory       = chosen > 0,
                Regulations     = regList
            };
        }

        // ── End-to-end audit per circuit ─────────────────────────────────

        /// <summary>
        /// Full per-circuit audit: Zs check + adiabatic + RCD recommendation
        /// in one shot. Caller assembles the results table for the panel grid
        /// and the loop-calculation sheet.
        /// </summary>
        public static CircuitAuditResult AuditCircuit(CircuitAuditInput inp)
        {
            if (inp == null) return null;
            var th = inp.Thresholds ?? Thresholds();
            // An earthing system with no Ze in either layer takes 0.8 ohm (the
            // UK TN-S maximum) and says so in ZeSource rather than silently.
            bool zeKnown = th.Ze.TryGetValue(inp.EarthingSystem ?? "TN-C-S", out double zev);
            double ze = zeKnown ? zev : 0.8;
            string zeSource = zeKnown
                ? (th.ZeSource.TryGetValue(inp.EarthingSystem ?? "TN-C-S", out var src) ? src : "corporate")
                : "ASSUMED 0.8 ohm (earthing system not in the thresholds file)";

            double zs = ComputeZs(ze, inp.PhaseCsaMm2, inp.CpcCsaMm2, inp.LengthM,
                inp.Material, inp.Insulation, inp.WireTables);

            var zsCheck = VerifyZs(zs, inp.OcpdType, inp.RatingA, th.NominalUo, th);

            // Resolve OCPD clearing time at the *prospective fault current*
            // for the circuit (computed from Zs and Uo).
            double pscA = th.NominalUo / Math.Max(zs, 1e-6);

            // Clearing time at the prospective fault current from the IEC 60898-1
            // band of the protective device: the band's MAXIMUM clearing edge, the
            // worst case for §434.5.2. It used to come from a label lookup that never
            // matched ("MCB_C_32") and so fell back to an invented 100-300 ms ramp.
            // MCCB / ACB / unknown curve have no generic band: the adiabatic check is
            // then NOT CHECKED — never a pass on an invented time.
            string letter = (inp.OcpdType ?? "").Split('_').LastOrDefault() ?? "";
            var band = IecMcbBands.Parse($"{letter}{inp.RatingA:0}", inp.OcpdType);
            double clearingSec = band.HasBand ? band.MaxClearTimeS(pscA) : double.NaN;
            bool adiabaticChecked = !double.IsNaN(clearingSec) && !double.IsInfinity(clearingSec);

            var ad = VerifyAdiabatic(inp.PhaseCsaMm2, inp.Material, inp.Insulation,
                pscA, adiabaticChecked ? clearingSec : 0, th);
            if (!adiabaticChecked) ad.Passes = false;

            var rcd = RecommendRcd(inp.Context, inp.EarthingSystem, th);

            // Final verdict — fail any single check, escalate to overall fail. An
            // unchecked adiabatic test cannot produce a PASS: it is UNVERIFIED.
            string verdict = !zsCheck.Passes
                                ? (rcd.RecommendedMA > 0 ? "PASS_VIA_RCD" : "FAIL")
                           : !adiabaticChecked ? "UNVERIFIED"
                           : ad.Passes ? "PASS" : "FAIL";

            return new CircuitAuditResult
            {
                OcpdType        = inp.OcpdType,
                RatingA         = inp.RatingA,
                Assumptions     = new List<string>(inp.Assumptions ?? new List<string>()),
                CircuitTag      = inp.CircuitTag,
                PanelName       = inp.PanelName,
                LoadName        = inp.LoadName,
                EarthingSystem  = inp.EarthingSystem,
                ZeOhm           = ze,
                ZeSource        = zeSource,
                Cmin            = th.Cmin,
                ZsActualOhm     = zsCheck.ZsActualOhm,
                ZsMaxOhm        = zsCheck.ZsMaxOhm,
                ZsMarginPct     = zsCheck.MarginPercent,
                ZsPasses        = zsCheck.Passes,
                ProspectivePscA = pscA,
                ClearingTimeMs  = adiabaticChecked ? clearingSec * 1000.0 : double.NaN,
                AdiabaticPasses = ad.Passes,
                AdiabaticMinCsa = ad.MinCsaMm2,
                K               = ad.K,
                RcdRequiredMA   = rcd.RecommendedMA,
                RcdRegulation   = rcd.Regulations,
                Verdict         = verdict
            };
        }
    }

    // ── DTOs ────────────────────────────────────────────────────────────
    // BS7671Thresholds + RcdScenario live in BS7671Thresholds.cs (Revit-free, tested).

    public class ZsCheckResult
    {
        public string OcpdType; public double RatingA, IaA, ZsMaxOhm, ZsActualOhm, MarginPercent;
        public bool Passes;
    }

    public class AdiabaticResult
    {
        public double K, CsaMm2, FaultCurrentA, ClearingTimeSec, LeftHandKsq, RightHandIsqT, MinCsaMm2;
        public bool Passes;
    }

    public class RcdRecommendation
    {
        public int RecommendedMA;
        public bool Mandatory;
        public string Regulations;
    }

    public class CircuitAuditInput
    {
        public string CircuitTag, PanelName, LoadName, EarthingSystem, OcpdType, Material, Insulation, Context;
        public double RatingA, LengthM, PhaseCsaMm2, CpcCsaMm2;
        public WireTableSet WireTables;
        /// <summary>Inputs the caller defaulted because the model did not hold them.</summary>
        public List<string> Assumptions = new List<string>();
        /// <summary>Corporate + project thresholds; null = corporate only.</summary>
        public BS7671Thresholds Thresholds;
    }

    public class CircuitAuditResult
    {
        public string CircuitTag, PanelName, LoadName, EarthingSystem, RcdRegulation, Verdict, ZeSource;
        public string OcpdType;
        public double RatingA;
        /// <summary>Inputs that were defaulted - the verdict rests on them.</summary>
        public List<string> Assumptions = new List<string>();
        public double Cmin;
        public double ZeOhm, ZsActualOhm, ZsMaxOhm, ZsMarginPct, ProspectivePscA, ClearingTimeMs, AdiabaticMinCsa, K;
        public bool ZsPasses, AdiabaticPasses;
        public int RcdRequiredMA;
    }
}
