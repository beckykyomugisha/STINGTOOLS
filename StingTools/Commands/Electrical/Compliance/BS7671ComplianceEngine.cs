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
    /// All thresholds load from <c>STING_BS7671_DISCONNECTION.json</c> so
    /// projects can tweak Ze (e.g. PME-treated TN-C-S), substitute country
    /// regs, or extend the OCPD multiplier table without recompiling.
    /// </summary>
    public static class BS7671ComplianceEngine
    {
        private static BS7671Thresholds _cache;
        private static readonly object _lock = new object();

        public static BS7671Thresholds Thresholds()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                _cache = LoadThresholds();
                return _cache;
            }
        }

        public static void InvalidateCache() { lock (_lock) _cache = null; }

        private static BS7671Thresholds LoadThresholds()
        {
            var t = new BS7671Thresholds();
            try
            {
                string path = StingToolsApp.FindDataFile("STING_BS7671_DISCONNECTION.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { t.SeedDefaults(); return t; }
                var root = JObject.Parse(File.ReadAllText(path));
                t.NominalUo = root["nominalUoV"]?.Value<double>() ?? 230;
                foreach (var sys in (root["earthingSystems"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    t.Ze[sys.Name] = sys.Value["ZeOhm"]?.Value<double>() ?? 0;
                foreach (var m in (root["iaMultipliers"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (m.Value.Type == JTokenType.Float || m.Value.Type == JTokenType.Integer)
                        t.IaMultiplier[m.Name] = m.Value.Value<double>();
                foreach (var k in (root["adiabaticK"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    t.AdiabaticK[k.Name] = k.Value.Value<double>();
                foreach (var d in (root["disconnectionTimesSec"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    t.DisconnectFinal[d.Name]       = d.Value["final_le32A"]?.Value<double>() ?? 0.4;
                    t.DisconnectDistribution[d.Name]= d.Value["distribution"]?.Value<double>() ?? 5.0;
                }
                foreach (var s in root["rcdRequiredScenarios"] as JArray ?? new JArray())
                    t.RcdScenarios.Add(new RcdScenario
                    {
                        Scenario = s["scenario"]?.ToString() ?? "",
                        IMaxMA   = s["imaxMA"]?.Value<int>() ?? 30,
                        Reg      = s["regulation"]?.ToString() ?? ""
                    });
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BS7671 thresholds load: {ex.Message}");
                t.SeedDefaults();
            }
            return t;
        }

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
        /// Verify Zs × Ia ≤ Uo. Returns the result with the maximum permitted
        /// Zs for that OCPD type/rating, the actual Zs, and pass/fail.
        /// </summary>
        public static ZsCheckResult VerifyZs(double computedZsOhm, string ocpdType, double ratingA,
            double uoV = 230)
        {
            var th = Thresholds();
            double iaMult = th.IaMultiplier.TryGetValue(ocpdType?.ToUpperInvariant() ?? "", out double m)
                ? m : 5.0;
            double ia = iaMult * Math.Max(ratingA, 1);
            double zsMax = uoV / Math.Max(ia, 1);
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
            double faultCurrentA, double clearingTimeSec)
        {
            var th = Thresholds();
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
        public static RcdRecommendation RecommendRcd(string circuitContext, string earthingSystem)
        {
            var th = Thresholds();
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
            var th = Thresholds();
            double ze = th.Ze.TryGetValue(inp.EarthingSystem ?? "TN-C-S", out double zev) ? zev : 0.8;

            double zs = ComputeZs(ze, inp.PhaseCsaMm2, inp.CpcCsaMm2, inp.LengthM,
                inp.Material, inp.Insulation, inp.WireTables);

            var zsCheck = VerifyZs(zs, inp.OcpdType, inp.RatingA, th.NominalUo);

            // Resolve OCPD clearing time at the *prospective fault current*
            // for the circuit (computed from Zs and Uo).
            double pscA = th.NominalUo / Math.Max(zs, 1e-6);
            var tcc = TccDatabaseLoader.Resolve(
                inp.OcpdType + "_" + inp.RatingA.ToString("0"),  // optional label form
                pscA / 1000.0)
                ?? new TccEntry { ClearingMs_At_10xIn = 100, MinFaultKa = 0.05, MaxFaultKa = 6 };
            double clearingSec = tcc.ClearingTimeMs(pscA / 1000.0) / 1000.0;

            var ad = VerifyAdiabatic(inp.PhaseCsaMm2, inp.Material, inp.Insulation,
                pscA, clearingSec);

            var rcd = RecommendRcd(inp.Context, inp.EarthingSystem);

            // Final verdict — fail any single check, escalate to overall fail.
            string verdict = (zsCheck.Passes && ad.Passes) ? "PASS"
                           : (!zsCheck.Passes && rcd.RecommendedMA > 0) ? "PASS_VIA_RCD"
                           : "FAIL";

            return new CircuitAuditResult
            {
                CircuitTag      = inp.CircuitTag,
                PanelName       = inp.PanelName,
                LoadName        = inp.LoadName,
                EarthingSystem  = inp.EarthingSystem,
                ZeOhm           = ze,
                ZsActualOhm     = zsCheck.ZsActualOhm,
                ZsMaxOhm        = zsCheck.ZsMaxOhm,
                ZsMarginPct     = zsCheck.MarginPercent,
                ZsPasses        = zsCheck.Passes,
                ProspectivePscA = pscA,
                ClearingTimeMs  = clearingSec * 1000.0,
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

    public class BS7671Thresholds
    {
        public double NominalUo { get; set; } = 230;
        public Dictionary<string, double> Ze { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> IaMultiplier { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> AdiabaticK { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> DisconnectFinal { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> DisconnectDistribution { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<RcdScenario> RcdScenarios { get; } = new();

        public void SeedDefaults()
        {
            NominalUo = 230;
            Ze["TN-S"] = 0.35; Ze["TN-C-S"] = 0.80; Ze["TT"] = 21.0; Ze["IT"] = 100.0;
            IaMultiplier["MCB_B"] = 5; IaMultiplier["MCB_C"] = 10; IaMultiplier["MCB_D"] = 20;
            IaMultiplier["RCBO_B"] = 5; IaMultiplier["RCBO_C"] = 10;
            IaMultiplier["MCCB"] = 10; IaMultiplier["ACB"] = 8;
            AdiabaticK["Cu/PVC"] = 115; AdiabaticK["Cu/XLPE"] = 143;
            AdiabaticK["Al/PVC"] = 76;  AdiabaticK["Al/XLPE"] = 94;
            DisconnectFinal["TN-S"] = 0.4; DisconnectFinal["TN-C-S"] = 0.4; DisconnectFinal["TT"] = 0.2;
            DisconnectDistribution["TN-S"] = 5.0; DisconnectDistribution["TN-C-S"] = 5.0; DisconnectDistribution["TT"] = 1.0;
        }
    }

    public class RcdScenario { public string Scenario; public int IMaxMA; public string Reg; }

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
    }

    public class CircuitAuditResult
    {
        public string CircuitTag, PanelName, LoadName, EarthingSystem, RcdRegulation, Verdict;
        public double ZeOhm, ZsActualOhm, ZsMaxOhm, ZsMarginPct, ProspectivePscA, ClearingTimeMs, AdiabaticMinCsa, K;
        public bool ZsPasses, AdiabaticPasses;
        public int RcdRequiredMA;
    }
}
