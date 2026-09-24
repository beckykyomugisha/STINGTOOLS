// ════════════════════════════════════════════════════════════════════════════
// Bs7671CableSizing — the Revit-free BS 7671 Appendix 4 cable-sizing method.
//
// ELEC-3. The BS 7671 path in CableSizerEngine used to pick the minimum size from
// an uncited threshold ladder (2.5 mm² = 17 A, where Table 4D2A method C gives
// 27 A), multiply by a flat XLPE ×1.18 "insulation factor", apply the PVC
// ambient column to every insulation, ignore grouping, and never read the
// tabulated capacities in STING_WIRE_TABLES.json. This file is the replacement:
//
//   1. It      tabulated current-carrying capacity for the conductor / insulation /
//              cable type / reference method, from a CITED table in the data file.
//              A combination with no table is REFUSED, never approximated.
//   2. Ca Cg Ci Cf   ambient (Table 4B1, per insulation), grouping (Table 4C1),
//              thermal insulation (caller-supplied, Reg 523.9 / Table 52.2) and
//              Cf = 0.725 for a BS 3036 semi-enclosed fuse (Appendix 4 §5.1.1).
//   3. In      the smallest standard device rating with In ≥ Ib (Reg 433.1.1(i)).
//   4. It ≥ In / (Ca·Cg·Ci·Cf)   Appendix 4 §5.1.1 — which, with Iz = It·Ca·Cg·Ci,
//              gives In ≤ Iz (Reg 433.1.1(ii)); for BS EN 60898 / 61009 / 60947-2
//              devices I2 ≤ 1.45·In so 433.1.1(iii) follows, and Cf covers BS 3036.
//   5. VD      mV/A/m × Ib × L / 1000 from the table's own voltage-drop companion
//              (4D2B for 4D2A), single-phase or three-phase column, against the
//              caller's limit. The tabulated mV/A/m is at maximum conductor
//              temperature, so this is conservative (Appendix 4 §6.1 permits a
//              correction for Ib < It; it is deliberately not taken here).
//
// Every factor used is written into Basis so a number on a drawing can be defended.
// NOT covered (and said so in Basis): adiabatic / fault-energy check (Reg 434.5.2),
// earth-fault loop Zs, harmonics on the neutral, and a VD that includes the drop
// upstream of the circuit's origin.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Electrical
{
    /// <summary>One tabulated size row: capacity It and voltage drop mV/A/m.</summary>
    public sealed class Bs7671CapacityRow
    {
        public double CsaMm2 { get; set; }
        public double It1ph { get; set; }
        public double It3ph { get; set; }
        public double MvAm1ph { get; set; }
        public double MvAm3ph { get; set; }
        /// <summary>False when the row has not been checked against the printed table.</summary>
        public bool Verified { get; set; }
    }

    /// <summary>A BS 7671 Appendix 4 capacity table for one reference method.</summary>
    public sealed class Bs7671CapacityTable
    {
        public string Id { get; set; }             // "4D2A"
        public string VoltDropTable { get; set; }  // "4D2B"
        public string Description { get; set; }
        public string Conductor { get; set; }      // "Cu"
        public string Insulation { get; set; }     // "PVC70"
        public double MaxConductorTempC { get; set; }
        public string InstallMethod { get; set; }  // "C"
        public List<Bs7671CapacityRow> Rows { get; } = new List<Bs7671CapacityRow>();
    }

    /// <summary>The Appendix 4 data the sizer runs on — parsed from STING_WIRE_TABLES.json.</summary>
    public sealed class Bs7671Data
    {
        public List<Bs7671CapacityTable> Tables { get; } = new List<Bs7671CapacityTable>();
        /// <summary>Insulation → (ambient °C → Ca), Table 4B1.</summary>
        public Dictionary<string, SortedDictionary<double, double>> Ambient { get; }
            = new Dictionary<string, SortedDictionary<double, double>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Arrangement → (circuit count → Cg), Table 4C1.</summary>
        public Dictionary<string, SortedDictionary<int, double>> Grouping { get; }
            = new Dictionary<string, SortedDictionary<int, double>>(StringComparer.OrdinalIgnoreCase);
        public double SemiEnclosedFuseCf { get; set; } = 0.725;

        /// <summary>The capacity table for a conductor / insulation / reference method, or null.</summary>
        public Bs7671CapacityTable FindTable(string material, string insulation, string method)
            => Tables.FirstOrDefault(t =>
                string.Equals(t.Conductor, string.IsNullOrWhiteSpace(material) ? "Cu" : material.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Insulation, (insulation ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.InstallMethod, (method ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Tabulated It for an exact tabulated CSA (±0.01 mm²); 0 when the size is not in the table.</summary>
        public static double TabulatedIt(Bs7671CapacityTable table, double csaMm2, int phases)
        {
            var row = table?.Rows.FirstOrDefault(r => Math.Abs(r.CsaMm2 - csaMm2) < 0.01);
            if (row == null) return 0;
            return phases == 3 ? row.It3ph : row.It1ph;
        }

        /// <summary>Parse the <c>bs7671Appendix4</c> section. Returns an empty set (which
        /// makes the sizer refuse) when the section is absent — never a fallback table.</summary>
        public static Bs7671Data FromJson(JObject root)
        {
            var d = new Bs7671Data();
            var sec = root?["bs7671Appendix4"] as JObject;
            if (sec == null) return d;

            foreach (var t in (sec["capacityTables"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var table = new Bs7671CapacityTable
                {
                    Id = (string)t["id"],
                    VoltDropTable = (string)t["voltDropTable"],
                    Description = (string)t["description"],
                    Conductor = (string)t["conductor"],
                    Insulation = (string)t["insulation"],
                    MaxConductorTempC = t["maxConductorTempC"]?.Value<double>() ?? 0,
                    InstallMethod = (string)t["installMethod"],
                };
                foreach (var r in (t["sizes"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    table.Rows.Add(new Bs7671CapacityRow
                    {
                        CsaMm2 = r["csaMm2"]?.Value<double>() ?? 0,
                        It1ph = r["It_1ph"]?.Value<double>() ?? 0,
                        It3ph = r["It_3ph"]?.Value<double>() ?? 0,
                        MvAm1ph = r["mVAm_1ph"]?.Value<double>() ?? 0,
                        MvAm3ph = r["mVAm_3ph"]?.Value<double>() ?? 0,
                        Verified = r["verified"]?.Value<bool>() ?? false,
                    });
                }
                table.Rows.Sort((a, b) => a.CsaMm2.CompareTo(b.CsaMm2));
                d.Tables.Add(table);
            }

            if (sec["ambientTemperatureFactors"] is JObject amb)
                foreach (var p in amb.Properties().Where(p => !p.Name.StartsWith("_") && p.Value is JObject))
                {
                    var map = new SortedDictionary<double, double>();
                    foreach (var q in ((JObject)p.Value).Properties())
                        map[double.Parse(q.Name, CultureInfo.InvariantCulture)] = q.Value.Value<double>();
                    d.Ambient[p.Name] = map;
                }

            if (sec["groupingFactors"] is JObject grp)
                foreach (var p in grp.Properties().Where(p => !p.Name.StartsWith("_") && p.Value is JObject))
                {
                    var map = new SortedDictionary<int, double>();
                    foreach (var q in ((JObject)p.Value).Properties())
                        map[int.Parse(q.Name, CultureInfo.InvariantCulture)] = q.Value.Value<double>();
                    d.Grouping[p.Name] = map;
                }

            if (sec["semiEnclosedFuseFactorCf"] != null)
                d.SemiEnclosedFuseCf = sec["semiEnclosedFuseFactorCf"].Value<double>();
            return d;
        }
    }

    public sealed class Bs7671SizingInput
    {
        /// <summary>Design current Ib, A.</summary>
        public double DesignCurrentA { get; set; }
        /// <summary>U0 for single-phase (e.g. 230), line voltage for three-phase (e.g. 400).</summary>
        public double VoltageV { get; set; } = 230.0;
        public int Phases { get; set; } = 1;
        public double LengthM { get; set; }
        public string InstallMethod { get; set; } = "C";
        public string Insulation { get; set; } = "PVC70";
        public string Material { get; set; } = "Cu";
        public double AmbientTempC { get; set; } = 30.0;
        /// <summary>Number of circuits in the group (1 = not grouped).</summary>
        public int GroupedCircuits { get; set; } = 1;
        /// <summary>Table 4C1 arrangement key: "Bunched" or "SingleLayerWall".</summary>
        public string GroupingArrangement { get; set; } = "Bunched";
        /// <summary>Thermal-insulation factor Ci (Reg 523.9 / Table 52.2); 1.0 when none.</summary>
        public double Ci { get; set; } = 1.0;
        /// <summary>An extra caller-supplied derating (e.g. the feeder panel's "derate"),
        /// multiplied in with the tabulated factors and named in Basis. 1.0 = none.</summary>
        public double ExtraDerate { get; set; } = 1.0;
        public bool SemiEnclosedFuse { get; set; }
        public double VdLimitPct { get; set; } = 5.0;
        /// <summary>Standard device ratings to choose In from, ascending.</summary>
        public int[] DeviceRatingsA { get; set; }
        public string DeviceLabel { get; set; } = "BS EN 60898 MCB";
    }

    public sealed class Bs7671SizingResult
    {
        public bool Sized { get; set; }
        public string Refusal { get; set; } = "";
        public double DesignCurrentA { get; set; }
        public int DeviceRatingA { get; set; }
        public double CsaMm2 { get; set; }
        public double TabulatedItA { get; set; }
        public double IzA { get; set; }
        public double RequiredItA { get; set; }
        public double Ca { get; set; } = 1.0;
        public double Cg { get; set; } = 1.0;
        public double Ci { get; set; } = 1.0;
        public double Cf { get; set; } = 1.0;
        public double MvAm { get; set; }
        public double VoltDropV { get; set; }
        public double VoltDropPct { get; set; }
        /// <summary>The smallest size that carries the current, before voltage drop.</summary>
        public double CapacityOnlyCsaMm2 { get; set; }
        /// <summary>True when the chosen row is flagged verified=false in the data.</summary>
        public bool UnverifiedRow { get; set; }
        /// <summary>Tables, factors and assumptions used — for the derivation note.</summary>
        public string Basis { get; set; } = "";
    }

    public static class Bs7671CableSizer
    {
        public static Bs7671SizingResult Size(Bs7671SizingInput input, Bs7671Data data)
        {
            var r = new Bs7671SizingResult();
            if (input == null) return Refuse(r, "No input.");
            r.DesignCurrentA = input.DesignCurrentA;
            if (data == null || data.Tables.Count == 0)
                return Refuse(r, "STING_WIRE_TABLES.json has no bs7671Appendix4 capacity tables; nothing was sized.");
            if (input.DesignCurrentA <= 0) return Refuse(r, "Design current Ib must be > 0.");
            if (input.VoltageV <= 0) return Refuse(r, "Voltage must be > 0.");
            if (input.LengthM < 0) return Refuse(r, "Length must be ≥ 0.");

            string material = string.IsNullOrWhiteSpace(input.Material) ? "Cu" : input.Material.Trim();
            string insulation = (input.Insulation ?? "").Trim();
            string method = (input.InstallMethod ?? "").Trim();

            var table = data.FindTable(material, insulation, method);
            if (table == null)
            {
                string have = string.Join(", ", data.Tables.Select(t =>
                    $"Table {t.Id} {t.Conductor}/{t.Insulation} method {t.InstallMethod}"));
                return Refuse(r,
                    $"No BS 7671 Appendix 4 capacity table is shipped for {material} / {insulation} / " +
                    $"reference method {method}. Available: {have}. Add the table from the printed " +
                    "Appendix 4 to STING_WIRE_TABLES.json — the sizer will not approximate it.");
            }

            // ── Ca — Table 4B1, insulation-specific ─────────────────────────────
            var basis = new StringBuilder();
            if (!data.Ambient.TryGetValue(insulation, out var ambient) || ambient.Count == 0)
                return Refuse(r, $"No Table 4B1 ambient factors for {insulation}.");
            double ta = input.AmbientTempC;
            double ca;
            string caNote;
            double minT = ambient.Keys.First(), maxT = ambient.Keys.Last();
            if (ta <= minT)
            {
                ca = ambient[minT];
                caNote = ta < minT ? $" (ta {ta:0} °C below {minT:0} °C: no uplift taken)" : "";
            }
            else if (ta > maxT)
                return Refuse(r, $"Ambient {ta:0} °C is above the highest Table 4B1 row carried for {insulation} ({maxT:0} °C).");
            else
            {
                double key = ambient.Keys.First(k => k >= ta);   // hotter row = conservative
                ca = ambient[key];
                caNote = key != ta ? $" (ta {ta:0} °C read at the {key:0} °C row)" : "";
            }
            r.Ca = ca;

            // ── Cg — Table 4C1 ──────────────────────────────────────────────────
            int n = Math.Max(1, input.GroupedCircuits);
            double cg = 1.0;
            string cgNote = "1 circuit, not grouped";
            if (n > 1)
            {
                string arr = string.IsNullOrWhiteSpace(input.GroupingArrangement) ? "Bunched" : input.GroupingArrangement;
                if (!data.Grouping.TryGetValue(arr, out var grp) || grp.Count == 0)
                    return Refuse(r, $"No Table 4C1 grouping row '{arr}' in STING_WIRE_TABLES.json.");
                int key;
                if (grp.Keys.Any(k => k >= n)) key = grp.Keys.First(k => k >= n);
                else if (string.Equals(arr, "SingleLayerWall", StringComparison.OrdinalIgnoreCase)) key = grp.Keys.Last();
                else return Refuse(r, $"{n} circuits exceeds the largest Table 4C1 '{arr}' row ({grp.Keys.Last()}).");
                cg = grp[key];
                cgNote = $"{n} circuits {arr}" + (key != n ? $" (read at the {key}-circuit row)" : "");
            }
            r.Cg = cg;

            double ci = input.Ci > 0 && input.Ci <= 1.0 ? input.Ci : 1.0;
            r.Ci = ci;
            double extra = input.ExtraDerate > 0 && input.ExtraDerate <= 1.0 ? input.ExtraDerate : 1.0;
            double cf = input.SemiEnclosedFuse ? data.SemiEnclosedFuseCf : 1.0;
            r.Cf = cf;

            // ── In ≥ Ib ─────────────────────────────────────────────────────────
            int[] ratings = input.DeviceRatingsA ?? new int[0];
            int inA = ratings.Where(x => x >= input.DesignCurrentA).DefaultIfEmpty(0).Min();
            if (inA <= 0)
                return Refuse(r, $"No {input.DeviceLabel} rating ≥ Ib {input.DesignCurrentA:0.0} A.");
            r.DeviceRatingA = inA;

            double factors = ca * cg * ci * extra;
            double requiredIt = inA / (factors * cf);
            r.RequiredItA = requiredIt;
            bool threePh = input.Phases == 3;
            double limit = input.VdLimitPct > 0 ? input.VdLimitPct : 5.0;

            Bs7671CapacityRow winner = null, capacityOnly = null;
            foreach (var row in table.Rows)
            {
                double it = threePh ? row.It3ph : row.It1ph;
                if (it <= 0 || it < requiredIt) continue;
                if (capacityOnly == null) capacityOnly = row;
                double mv = threePh ? row.MvAm3ph : row.MvAm1ph;
                if (mv <= 0) continue;
                double vd = mv * input.DesignCurrentA * input.LengthM / 1000.0;
                if (vd / input.VoltageV * 100.0 <= limit) { winner = row; break; }
            }

            string head =
                $"BS 7671 Appendix 4, Table {table.Id} ({table.Description}, method {table.InstallMethod}, " +
                $"{(threePh ? "3/4-core 3-ph" : "2-core 1-ph")} column) + Table {table.VoltDropTable} mV/A/m. " +
                $"Ib={input.DesignCurrentA:0.0} A; In={inA} A {input.DeviceLabel} (In ≥ Ib, Reg 433.1.1). " +
                $"Ca={ca:0.00} Table 4B1 {insulation}{caNote}; Cg={cg:0.00} Table 4C1 ({cgNote}); Ci={ci:0.00}" +
                (extra < 1.0 ? $"; user derate={extra:0.00}" : "") +
                $"; Cf={cf:0.000}{(input.SemiEnclosedFuse ? " (BS 3036 semi-enclosed fuse)" : "")}. " +
                $"Required It ≥ In/(Ca·Cg·Ci{(extra < 1.0 ? "·derate" : "")}·Cf) = {requiredIt:0.0} A.";

            if (capacityOnly == null)
            {
                r.Basis = head;
                return Refuse(r, $"No size in Table {table.Id} has It ≥ {requiredIt:0.0} A (largest tabulated " +
                                 $"{table.Rows.Last().CsaMm2:0} mm²). Parallel cables are not sized here.");
            }
            r.CapacityOnlyCsaMm2 = capacityOnly.CsaMm2;
            if (winner == null)
            {
                r.Basis = head;
                return Refuse(r, $"{capacityOnly.CsaMm2:0.#} mm² carries the current, but no tabulated size " +
                                 $"keeps voltage drop within {limit:0.0}% over {input.LengthM:0} m.");
            }

            double itW = threePh ? winner.It3ph : winner.It1ph;
            r.CsaMm2 = winner.CsaMm2;
            r.TabulatedItA = itW;
            r.IzA = itW * factors;
            r.MvAm = threePh ? winner.MvAm3ph : winner.MvAm1ph;
            r.VoltDropV = r.MvAm * input.DesignCurrentA * input.LengthM / 1000.0;
            r.VoltDropPct = r.VoltDropV / input.VoltageV * 100.0;
            r.UnverifiedRow = !winner.Verified;
            r.Sized = true;

            var sb = new StringBuilder(head);
            sb.Append($" Selected {winner.CsaMm2:0.#} mm²: It={itW:0.#} A, Iz=It·Ca·Cg·Ci={r.IzA:0.0} A ≥ In {inA} A");
            if (capacityOnly.CsaMm2 < winner.CsaMm2)
                sb.Append($" (current alone needs {capacityOnly.CsaMm2:0.#} mm²; upsized for voltage drop)");
            sb.Append($". VD = {r.MvAm:0.###} mV/A/m × {input.DesignCurrentA:0.0} A × {input.LengthM:0.#} m = " +
                      $"{r.VoltDropV:0.00} V = {r.VoltDropPct:0.00}% of {input.VoltageV:0} V (limit {limit:0.0}%; " +
                      "tabulated mV/A/m at max conductor temperature, not corrected for load).");
            if (r.UnverifiedRow)
                sb.Append($" VERIFY: the Table {table.Id} row for {winner.CsaMm2:0.#} mm² has not been checked " +
                          "against the printed BS 7671 — confirm It and mV/A/m before issue.");
            sb.Append(" Not checked here: adiabatic (Reg 434.5.2), Zs / disconnection time, VD upstream of the circuit origin.");
            r.Basis = sb.ToString();
            return r;
        }

        private static Bs7671SizingResult Refuse(Bs7671SizingResult r, string why)
        {
            r.Sized = false;
            r.Refusal = why;
            return r;
        }
    }

    /// <summary>
    /// Protective-device selection. ELEC-3/4 of the review: the ×1.25 continuous-load
    /// factor is an NEC rule (210.20(A), 215.3) and must not be applied under BS 7671,
    /// where the rule is Ib ≤ In ≤ Iz (Reg 433.1.1).
    /// </summary>
    public static class ProtectiveDeviceSelection
    {
        public sealed class Selection
        {
            public int ProposedA { get; set; }
            public double MinimumA { get; set; }
            /// <summary>True when the proposed device would not be protected by the cable (In &gt; Iz),
            /// or when no standard rating is large enough. A blocked proposal must not be applied.</summary>
            public bool Blocked { get; set; }
            public string Note { get; set; } = "";
        }

        /// <param name="isNec">NEC: next 240.6(A) rating ≥ Ib (×1.25 when continuous).
        /// BS 7671: next rating ≥ Ib, never ×1.25.</param>
        /// <param name="izA">Effective cable capacity Iz when known; null/≤0 when not.</param>
        public static Selection Select(double ibA, bool isNec, bool continuous, int[] ratingsA, double? izA, string izBasis = null)
        {
            var s = new Selection();
            double min = isNec && continuous ? ibA * 1.25 : ibA;
            s.MinimumA = min;
            int proposed = (ratingsA ?? new int[0]).Where(x => x >= min).DefaultIfEmpty(0).Min();
            var notes = new List<string>();
            if (!isNec && continuous) notes.Add("125% continuous factor not applied (NEC rule; BS 7671 uses Ib ≤ In ≤ Iz)");
            if (proposed <= 0)
            {
                s.Blocked = true;
                notes.Add($"no standard rating ≥ {min:0.0} A");
                s.Note = string.Join("; ", notes);
                return s;
            }
            s.ProposedA = proposed;
            if (izA.HasValue && izA.Value > 0)
            {
                if (proposed > izA.Value)
                {
                    s.Blocked = true;
                    notes.Add($"In {proposed} A > Iz {izA.Value:0.#} A — the cable would not be protected against " +
                              "overload; upsize the cable, do not apply" + (string.IsNullOrEmpty(izBasis) ? "" : $" [{izBasis}]"));
                }
                else notes.Add($"In {proposed} A ≤ Iz {izA.Value:0.#} A" + (string.IsNullOrEmpty(izBasis) ? "" : $" [{izBasis}]"));
            }
            else if (!isNec) notes.Add("cable size unknown — In ≤ Iz not checked");
            s.Note = string.Join("; ", notes);
            return s;
        }
    }

    /// <summary>
    /// Conductor cross-section from a free-text wire-size string. ELEC-5: the old parser
    /// took the FIRST number, so "2 x 2.5mm²" became 2 mm² (the conductor count).
    /// Rules, in order: the number immediately before "mm" / "mm²" / "mm2"; else an AWG /
    /// kcmil designation ("#12", "12 AWG", "1/0", "250 kcmil") converted to mm²; else the
    /// LAST number in the string. Returns 0 when nothing parses.
    /// </summary>
    public static class WireSizeParser
    {
        private static readonly Regex Mm = new Regex(@"(\d+(?:[.,]\d+)?)\s*(?:mm²|mm2|mm\^2|sq\.?\s*mm|mm)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Kcmil = new Regex(@"(\d+)\s*(?:kcmil|mcm)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AwgHash = new Regex(@"#\s*(\d{1,2}(?:/0)?)", RegexOptions.CultureInvariant);
        private static readonly Regex AwgWord = new Regex(@"(\d{1,2}(?:/0)?)\s*AWG", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AnyNumber = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

        public static double ParseCsaMm2(string wireSize)
        {
            if (string.IsNullOrWhiteSpace(wireSize)) return 0;
            var m = Mm.Match(wireSize);
            if (m.Success) return ToDouble(m.Groups[1].Value);
            m = Kcmil.Match(wireSize);
            if (m.Success) return Math.Round(ToDouble(m.Groups[1].Value) * 1000.0 * CircularMilMm2, 2);
            m = AwgHash.Match(wireSize);
            if (!m.Success) m = AwgWord.Match(wireSize);
            if (m.Success) return AwgToMm2(m.Groups[1].Value);
            var all = AnyNumber.Matches(wireSize);
            return all.Count > 0 ? ToDouble(all[all.Count - 1].Value) : 0;
        }

        /// <summary>1 circular mil = π/4 × (0.001 in)² = 5.067075e-4 mm².</summary>
        public const double CircularMilMm2 = 5.067074790e-4;

        /// <summary>AWG gauge ("12", "1/0", "4/0") to mm² by the ASTM B258 definition:
        /// d = 0.005 in × 92^((36 − n)/39), n = 0 for 1/0, −1 for 2/0, …</summary>
        public static double AwgToMm2(string awg)
        {
            if (string.IsNullOrWhiteSpace(awg)) return 0;
            awg = awg.Trim();
            int n;
            if (awg.EndsWith("/0"))
            {
                if (!int.TryParse(awg.Substring(0, awg.Length - 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int zeros)) return 0;
                n = 1 - zeros;
            }
            else if (!int.TryParse(awg, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return 0;
            double dIn = 0.005 * Math.Pow(92.0, (36.0 - n) / 39.0);
            double cmil = Math.Pow(dIn * 1000.0, 2);
            return Math.Round(cmil * CircularMilMm2, 2);
        }

        private static double ToDouble(string s)
            => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }
}
