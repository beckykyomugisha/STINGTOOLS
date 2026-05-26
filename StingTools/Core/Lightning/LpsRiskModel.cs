// StingTools — LpsRiskModel.cs
//
// Full BS EN 62305-2:2012 risk model. Computes the four risks R1 (loss of
// human life), R2 (loss of service to the public), R3 (loss of cultural
// heritage) and R4 (economic loss) as the sum of the eight standard risk
// components, each of the form R_X = N_X · P_X · L_X:
//
//   R_A  flash to structure, injury (touch/step)         N_D · P_TA·P_B · L_T
//   R_B  flash to structure, physical damage / fire      N_D · P_B      · L_F
//   R_C  flash to structure, failure of internal systems N_D · P_SPD    · L_O
//   R_M  flash near structure, failure of int. systems   N_M · P_M      · L_O
//   R_U  flash to a line, injury (touch)                 N_L · P_TU·P_EB·P_LD · L_T
//   R_V  flash to a line, physical damage / fire         N_L · P_EB·P_LD      · L_F
//   R_W  flash to a line, failure of internal systems    N_L · P_SPD·P_LD     · L_O
//   R_Z  flash near a line, failure of internal systems  N_I · P_SPD·P_LI     · L_O
//
// Number of events (Annex A), probabilities (Annex B) and relative losses
// (Annex C) come from STING_LPS_RISK_TABLES.json. This replaces the old
// single-factor screening weighting in LpsEngine with the real component
// decomposition. It is still a design-aid, not a substitute for a stamped
// risk study: many sub-factors use the standard's typical/default values
// (see the data file) and line parameters fall back to a representative
// power + telecom pair when the caller supplies none.
//
// The assessment computes the UNPROTECTED baseline (no LPS, no SPD) so it
// can answer "is protection needed and to what class"; the recommended
// class is the least-protective class whose residual risk (R × (1 − PE))
// clears every exceeded loss type's tolerable threshold.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public static class LpsRiskModel
    {
        private static readonly object _lock = new object();
        private static JObject _tables;

        // ── Public entry ──────────────────────────────────────────────

        public static LpsRiskResult Compute(LpsRiskInput input)
        {
            var result = new LpsRiskResult();
            try
            {
                if (input == null) { result.Notes = "No input provided"; return result; }
                EnsureTables();

                // ── Geometry → collection area A_D (Annex A.2) ────────
                double L = input.PlanLengthM, W = input.PlanWidthM, H = input.HeightM;
                if ((L <= 0 || W <= 0) && input.PlanAreaM2 > 0)
                {
                    L = Math.Sqrt(input.PlanAreaM2);
                    W = L;
                }
                if (H <= 0) H = 1.0;
                double Ad = input.AeOverrideM2 > 0
                    ? input.AeOverrideM2
                    : (L * W) + (2.0 * (3.0 * H) * (L + W)) + (Math.PI * Math.Pow(3.0 * H, 2));
                if (Ad < 1.0) Ad = 1.0;
                result.CollectionAreaM2 = Ad;

                double Ng = input.GroundFlashDensity > 0 ? input.GroundFlashDensity : 2.0;
                double Cd = input.LocationFactorCd > 0 ? input.LocationFactorCd : 1.0;

                // ── Event counts (Annex A) ────────────────────────────
                double Nd = Ng * Ad * Cd * 1e-6;                  // flashes to the structure
                result.AnnualStrikeFrequency = Nd;

                double nearR = GetD("near", "radiusM", 500.0);
                double aWithin = (L * W) + (2.0 * nearR * (L + W)) + (Math.PI * nearR * nearR);
                double Am = Math.Max(0.0, aWithin - Ad);
                double Nm = Ng * Am * 1e-6;                       // flashes near the structure

                // ── Probabilities (Annex B) — UNPROTECTED baseline ────
                // P_B / P_SPD / P_EB are held at their "no protection" values
                // so the baseline risk reflects an unprotected structure; the
                // recommended class is then derived from residual risk.
                double pB   = GetD("pB_byClass",   "NONE", 1.0);
                double pSPD = GetD("pSPD_byLevel", "NONE", 1.0);
                double pEB  = GetD("pEB_byLevel",  "NONE", 1.0);
                double pTA  = GetD("pTA", "NONE", 1.0);
                double pTU  = GetD("pTU", "NONE", 1.0);

                // P_MS = (K_S1·K_S2·K_S3·K_S4)²  (Annex B.5)
                double ks1 = input.StructureShielded ? GetD("ks", "ks1_structureShielded", 0.5)
                                                      : GetD("ks", "ks1_unshielded", 1.0);
                double ks2 = GetD("ks", "ks2_none", 1.0);
                double ks3 = input.WiringShielded ? GetD("ks", "ks3_shieldedWiring", 0.2)
                                                  : GetD("ks", "ks3_unshieldedWiring", 1.0);
                double uw  = input.UwKv > 0 ? input.UwKv : GetD("ks", "defaultUwKv", 1.5);
                double ks4 = Math.Min(1.0, 1.0 / uw);
                double pMS = Math.Min(1.0, Math.Pow(ks1 * ks2 * ks3 * ks4, 2.0));
                double pM  = Math.Min(1.0, pSPD * pMS);

                // ── Event × probability terms (loss-type independent) ──
                double eA = Nd * pTA * pB;
                double eB = Nd * pB;
                double eC = Nd * pSPD;
                double eM = Nm * pM;
                double eU = 0, eV = 0, eW = 0, eZ = 0;

                double aLper = GetD("line", "aL_perMetre", 40.0);
                double aIper = GetD("line", "aI_perMetre", 4000.0);
                foreach (var ln in ResolveLines(input))
                {
                    double LL = ln.LengthM > 0 ? ln.LengthM : GetD("line", "defaultLengthM", 1000.0);
                    double Ci = GetD("lineCi", Up(ln.Install),     1.0);
                    double Ce = GetD("lineCe", Up(ln.Environment), 0.5);
                    double Ct = GetD("lineCt", Up(ln.Transformer), 1.0);
                    double Nl = Ng * (aLper * LL) * Ci * Ce * Ct * 1e-6;   // flashes to the line
                    double Ni = Ng * (aIper * LL) * Ci * Ce * Ct * 1e-6;   // flashes near the line
                    double pLD = GetD("pLD_byShield", Up(ln.Shield), 1.0);
                    double pLI = GetD("pLI_byShield", Up(ln.Shield), 0.3);
                    eU += Nl * pTU * pEB * pLD;
                    eV += Nl * pEB * pLD;
                    eW += Nl * pSPD * pLD;
                    eZ += Ni * pSPD * pLI;
                }

                // ── Loss model (Annex C) ──────────────────────────────
                var lp = ResolveLossProfile(input);
                double occ = Occupancy(input);

                // Loss values per type (h_z applies to L1 only).
                double LA1 = lp.Rt * lp.Lt1 * occ;
                double LB1 = lp.Rp * lp.Rf * lp.Hz * lp.Lf1 * occ;
                double LO1 = (lp.LifeEndangering ? lp.Lo1 : 0.0) * occ;

                double LB2 = lp.Rp * lp.Rf * lp.Lf2 * occ;
                double LO2 = lp.Lo2 * occ;

                double LB3 = lp.Rp * lp.Rf * lp.Lf3 * occ;

                double LA4 = lp.Rt * lp.Lt4 * occ;
                double LB4 = lp.Rp * lp.Rf * lp.Lf4 * occ;
                double LO4 = lp.Lo4 * occ;

                // ── Components, L1 headline breakdown ─────────────────
                double RA = eA * LA1, RB = eB * LB1, RU = eU * LA1, RV = eV * LB1;
                double RC = eC * LO1, RM = eM * LO1, RW = eW * LO1, RZ = eZ * LO1;
                double R1 = RA + RB + RU + RV + RC + RM + RW + RZ;

                double R2 = eB * LB2 + eV * LB2 + eC * LO2 + eM * LO2 + eW * LO2 + eZ * LO2;
                double R3 = lp.L3Applies ? (eB * LB3 + eV * LB3) : 0.0;
                double R4 = eA * LA4 + eU * LA4 + eB * LB4 + eV * LB4
                          + eC * LO4 + eM * LO4 + eW * LO4 + eZ * LO4;

                result.RiskByLossType["L1"] = R1;
                result.RiskByLossType["L2"] = R2;
                result.RiskByLossType["L3"] = R3;
                result.RiskByLossType["L4"] = R4;
                result.RiskComponents["R1_Direct"] = R1;   // legacy alias
                result.ComponentBreakdown["RA"] = RA;
                result.ComponentBreakdown["RB"] = RB;
                result.ComponentBreakdown["RC"] = RC;
                result.ComponentBreakdown["RM"] = RM;
                result.ComponentBreakdown["RU"] = RU;
                result.ComponentBreakdown["RV"] = RV;
                result.ComponentBreakdown["RW"] = RW;
                result.ComponentBreakdown["RZ"] = RZ;

                // ── Tolerable thresholds (Table 7) ────────────────────
                var lossRt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                { ["L1"] = 1e-5, ["L2"] = 1e-3, ["L3"] = 1e-4, ["L4"] = 1e-3 };
                try
                {
                    var arr = LpsEngine.GetRiskFactorLibrary()?["lossTypes"] as JArray;
                    if (arr != null)
                        foreach (var lt in arr)
                        {
                            string id = lt["id"]?.ToString();
                            double rt = lt["rt"]?.Value<double>() ?? 0.0;
                            if (!string.IsNullOrEmpty(id) && rt > 0) lossRt[id] = rt;
                        }
                }
                catch (Exception ex) { StingLog.Warn($"LossType Rt read: {ex.Message}"); }

                foreach (var kv in lossRt)
                {
                    result.TolerableByLossType[kv.Key] = kv.Value;
                    if (result.RiskByLossType.TryGetValue(kv.Key, out double rval))
                        result.ExceedsByLossType[kv.Key] = rval > kv.Value;
                }
                result.TolerableRisk = input.TolerableRisk > 0 ? input.TolerableRisk : lossRt["L1"];
                result.RequiresLps = result.ExceedsByLossType.Any(kv => kv.Value);

                // ── Residual risk per class + class selection ─────────
                double rWorst = result.RiskByLossType.Values.DefaultIfEmpty(R1).Max();
                foreach (var classDef in LpsEngine.AllClasses())
                {
                    double pe = classDef.ProtectionEfficiency;
                    if (pe <= 0 || pe >= 1) continue;
                    result.ResidualRiskByClass[classDef.Id.ToUpperInvariant()] = rWorst * (1.0 - pe);
                }

                string residualNote = null;
                if (result.RequiresLps)
                {
                    string[] order = { "IV", "III", "II", "I" };
                    string chosen = null;
                    foreach (var cid in order)
                    {
                        var cdef = LpsEngine.LoadClass(cid);
                        if (cdef == null) continue;
                        double pe = cdef.ProtectionEfficiency;
                        if (pe <= 0 || pe >= 1) continue;
                        bool allPass = true;
                        foreach (var kv in result.RiskByLossType)
                        {
                            double rt = result.TolerableByLossType.TryGetValue(kv.Key, out var t)
                                ? t : double.MaxValue;
                            if (kv.Value * (1.0 - pe) > rt) { allPass = false; break; }
                        }
                        if (allPass) { chosen = cid; break; }
                    }
                    if (chosen == null)
                    {
                        chosen = "I";
                        residualNote = " Class I residual still exceeds tolerable risk; coordinated SPDs / SPM required.";
                    }
                    result.RecommendedClass = chosen;
                }
                else result.RecommendedClass = "NONE";

                // Dominant component for the headline note.
                var dom = result.ComponentBreakdown.OrderByDescending(k => k.Value).FirstOrDefault();
                result.Notes = string.Format(
                    "BS EN 62305-2 component model (R = ΣRA..RZ). Nd={0:F4} Nm={1:F4} flashes/yr; " +
                    "R1={2:E2} R2={3:E2} R3={4:E2} R4={5:E2}; dominant L1 component {6}={7:E2}; " +
                    "recommended class {8}.{9} Loss/line sub-factors use BS EN 62305-2 typical " +
                    "values where not supplied — confirm with a full risk study for certification.",
                    Nd, Nm, R1, R2, R3, R4, dom.Key ?? "—", dom.Value,
                    result.RecommendedClass ?? "NONE", residualNote ?? "");
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsRiskModel.Compute failed", ex);
                result.Notes = "Risk assessment failed: " + ex.Message;
            }
            return result;
        }

        // ── Line resolution ───────────────────────────────────────────

        private static IEnumerable<LpsServiceLine> ResolveLines(LpsRiskInput input)
        {
            // An explicit list (even empty) is authoritative: empty ⇒ no
            // connected lines ⇒ no line-related risk components.
            if (input.Lines != null) return input.Lines;

            var derived = new List<LpsServiceLine>();
            if (input.ConnectedServices != null)
            {
                foreach (var s in input.ConnectedServices)
                {
                    string id = (s ?? "").ToUpperInvariant();
                    if (id.StartsWith("POWER"))
                        derived.Add(new LpsServiceLine { Id = "POWER",
                            Install = id.Contains("UG") ? "BURIED" : "AERIAL" });
                    else if (id.StartsWith("TELECOM"))
                        derived.Add(new LpsServiceLine { Id = "TELECOM",
                            Install = id.Contains("UG") ? "BURIED" : "AERIAL" });
                    // GAS / WATER are bonded metallic services, not signal
                    // lines — excluded from the line-surge components.
                }
            }
            if (derived.Count > 0) return derived;

            // Default: a representative incoming power + telecom line.
            return new List<LpsServiceLine>
            {
                new LpsServiceLine { Id = "POWER" },
                new LpsServiceLine { Id = "TELECOM" }
            };
        }

        // ── Loss profile resolution (IDs → numeric → defaults) ─────────

        private class LossProfile
        {
            public double Rt = 0.001, Rp = 1.0, Rf = 0.01, Hz = 1.0;
            public double Lt1 = 0.01, Lt4 = 0.0001;
            public double Lf1 = 0.1, Lf2 = 0.1, Lf3 = 0.1, Lf4 = 0.5;
            public double Lo1 = 0.1, Lo2 = 0.01, Lo4 = 0.01;
            public bool LifeEndangering = false;
            public bool L3Applies = false;
        }

        private static LossProfile ResolveLossProfile(LpsRiskInput input)
        {
            var p = new LossProfile
            {
                Rt  = GetD("loss.rt", Up(input.SoilSurfaceType), 0.001),
                Rp  = GetD("loss.rp", Up(input.FireProtection),  1.0),
                Lt1 = GetD("loss.LT", "L1", 0.01),
                Lt4 = GetD("loss.LT", "L4", 0.0001),
                Lf1 = GetD("loss.LF", "L1", 0.1),
                Lf2 = GetD("loss.LF", "L2", 0.1),
                Lf3 = GetD("loss.LF", "L3", 0.1),
                Lf4 = GetD("loss.LF", "L4", 0.5),
                Lo1 = GetD("loss.LO", "L1_lifeEndangering", 0.1),
                Lo2 = GetD("loss.LO", "L2", 0.01),
                Lo4 = GetD("loss.LO", "L4", 0.01)
            };

            string bId = Up(input.BuildingTypeId);
            string cId = Up(input.InternalContentId);
            string oId = Up(input.OccupantHazardId);

            // r_f — fire/explosion risk (Table C.5)
            string rfKey = Up(input.FireRisk);
            if (string.IsNullOrEmpty(rfKey))
            {
                if (bId == "HAZARDOUS" || cId == "EXPLOSIVE") rfKey = "EXPLOSION";
                else if (bId == "INDUSTRIAL" || cId == "HIGH_FIRE") rfKey = "HIGH";
                else if (input.InternalContentCc >= 5.0) rfKey = "EXPLOSION";
                else if (input.InternalContentCc >= 2.5) rfKey = "HIGH";
                else rfKey = "ORDINARY";
            }
            p.Rf = GetD("loss.rf", rfKey, 0.01);

            // h_z — special hazard / panic (Table C.6), L1 only
            string hzKey = Up(input.SpecialHazard);
            if (string.IsNullOrEmpty(hzKey))
            {
                if (oId == "HIGH" || input.OccupantHazardCd >= 5.0) hzKey = "HIGH_PANIC";
                else if (bId == "HEALTHCARE") hzKey = "MEDIUM_PANIC";
                else if (oId == "MEDIUM" || bId == "EDUCATION" || bId == "CULTURAL"
                         || input.OccupantHazardCd >= 2.0) hzKey = "LOW_PANIC";
                else hzKey = "NONE";
            }
            p.Hz = GetD("loss.hz", hzKey, 1.0);

            // Life-endangering internal systems → R_C/R_M/R_W/R_Z feed R1.
            p.LifeEndangering = input.LifeEndangeringSystems
                ?? (bId == "HEALTHCARE" || bId == "HAZARDOUS" || cId == "EXPLOSIVE"
                    || input.BuildingTypeCb >= 2.5);

            // L3 (cultural heritage) relevance.
            p.L3Applies = bId == "CULTURAL" || cId == "IRREPLACEABLE"
                          || Math.Abs(input.BuildingTypeCb - 2.0) < 1e-6;

            return p;
        }

        private static double Occupancy(LpsRiskInput input)
        {
            double f = 1.0;
            if (input.PersonsInZone > 0 && input.PersonsTotal > 0)
                f *= input.PersonsInZone / input.PersonsTotal;
            if (input.OccupiedHoursPerYear > 0)
                f *= Math.Min(1.0, input.OccupiedHoursPerYear / 8760.0);
            return f;
        }

        // ── Table access ──────────────────────────────────────────────

        /// <summary>Read a numeric table value. <paramref name="section"/> may
        /// be dotted (e.g. "loss.rf") to descend nested objects.</summary>
        private static double GetD(string section, string key, double fallback)
        {
            try
            {
                EnsureTables();
                JToken node = _tables;
                foreach (var part in section.Split('.'))
                {
                    node = node?[part];
                    if (node == null) return fallback;
                }
                var v = node[key];
                if (v == null || v.Type == JTokenType.Null) return fallback;
                return v.Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"LpsRiskModel.GetD {section}.{key}: {ex.Message}"); return fallback; }
        }

        private static string Up(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToUpperInvariant();

        private static void EnsureTables()
        {
            if (_tables != null) return;
            lock (_lock)
            {
                if (_tables != null) return;
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_LPS_RISK_TABLES.json");
                    _tables = (!string.IsNullOrEmpty(path) && File.Exists(path))
                        ? JObject.Parse(File.ReadAllText(path))
                        : new JObject();
                    if (_tables.Count == 0)
                        StingLog.Warn("LpsRiskModel: STING_LPS_RISK_TABLES.json not found — using built-in defaults.");
                }
                catch (Exception ex)
                {
                    StingLog.Error("LpsRiskModel: failed loading STING_LPS_RISK_TABLES.json", ex);
                    _tables = new JObject();
                }
            }
        }
    }
}
