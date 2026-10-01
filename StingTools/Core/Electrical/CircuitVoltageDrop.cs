// ════════════════════════════════════════════════════════════════════════════
// CircuitVoltageDrop — the one way a circuit's voltage drop is worked out (ELC-22).
//
// Before this, five writers stamped ELC_VLT_DROP_PCT (alias ELC_CKT_VD_PCT) by three
// methods: Appendix 4 mV/A/m (cable Apply), BS EN 60228 resistance (Recalculate,
// Auto-Upsize, VD schedule) and imported figures (Amtech / EasyPower / Trimble). The
// last writer won, nothing said which method a value came from, and a circuit with a
// missing length or size was stamped 0.00 % — which the Circuit Check read as a pass.
//
// Now every STING writer resolves through here and stamps ELC_CKT_VD_BASIS_TXT beside
// the number. The first token of the basis is a machine code:
//
//   A4        Appendix 4 mV/A/m from the table recorded on the circuit (its own cable)
//   A4-MAX    no complete cable record: the HIGHEST mV/A/m any loaded table gives for
//             the size — an upper bound, so it can prove a pass but never a fail
//   A4-SIZED  written by a sizer for the cable it chose (feeder sizer, on the board)
//   R60228    BS EN 60228 conductor resistance (NEC path only; not an Appendix 4 figure)
//   IMPORT    a figure from another tool (Amtech, EasyPower, Trimble) — its method unknown
//   NONE      not calculated, and why. Never 0.
//   (blank)   a value written before this basis existed: origin unknown ("legacy")
//
// Revit-free, so the precedence below is unit-tested.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Electrical
{
    public enum VdMethod { None, Appendix4Recorded, Appendix4Envelope, Appendix4Sized, Resistance60228, Imported, Legacy }

    public sealed class CircuitVdInput
    {
        public double CurrentA { get; set; }
        public double LengthM { get; set; }
        public double VoltageV { get; set; }
        public int Phases { get; set; } = 1;
        public double CsaMm2 { get; set; }
        public string Material { get; set; } = "Cu";
        /// <summary>"BS7671" or "NEC…"; NEC circuits use conductor resistance.</summary>
        public string Standard { get; set; } = "BS7671";
        /// <summary>The cable recorded on the circuit when a size was applied (may be blank).</summary>
        public string Insulation { get; set; }
        public string InstallMethod { get; set; }
        public string CableType { get; set; }
        public bool RecordComplete =>
            !string.IsNullOrWhiteSpace(Insulation) && !string.IsNullOrWhiteSpace(InstallMethod)
            && !string.IsNullOrWhiteSpace(CableType);
        /// <summary>Set when the material was not recorded and copper was assumed
        /// (ConductorMaterialText.Resolve); carried into the result's Detail.</summary>
        public string MaterialNote { get; set; }
        /// <summary>Set when the recorded material text was not recognised: no drop.</summary>
        public string MaterialRefusal { get; set; }
    }

    public sealed class CircuitVdResult
    {
        public VdMethod Method { get; set; } = VdMethod.None;
        /// <summary>The drop in %. Only meaningful when <see cref="HasValue"/>.</summary>
        public double Pct { get; set; }
        public bool HasValue => Method != VdMethod.None && Method != VdMethod.Legacy;
        /// <summary>A4-MAX: the real drop is at most this.</summary>
        public bool UpperBound => Method == VdMethod.Appendix4Envelope;
        /// <summary>The mV/A/m used has one source.</summary>
        public bool Unverified { get; set; }
        public double MvAm { get; set; }
        /// <summary>Human-readable basis (without the code).</summary>
        public string Detail { get; set; } = "";

        public string Code => CircuitVoltageDrop.CodeFor(Method);
        /// <summary>What is written to ELC_CKT_VD_BASIS_TXT.</summary>
        public string Stamp => string.IsNullOrEmpty(Detail) ? Code : Code + " " + Detail;
    }

    public static class CircuitVoltageDrop
    {
        public const string CodeA4 = "A4", CodeA4Max = "A4-MAX", CodeA4Sized = "A4-SIZED",
                            CodeR60228 = "R60228", CodeImport = "IMPORT", CodeNone = "NONE";

        public static string CodeFor(VdMethod m) => m switch
        {
            VdMethod.Appendix4Recorded => CodeA4,
            VdMethod.Appendix4Envelope => CodeA4Max,
            VdMethod.Appendix4Sized => CodeA4Sized,
            VdMethod.Resistance60228 => CodeR60228,
            VdMethod.Imported => CodeImport,
            VdMethod.Legacy => "",
            _ => CodeNone,
        };

        /// <summary>The method a stamped basis names. Blank = a value from before the basis existed.</summary>
        public static VdMethod ParseMethod(string stamp)
        {
            string s = (stamp ?? "").Trim();
            if (s.Length == 0) return VdMethod.Legacy;
            string code = s.Split(new[] { ' ' }, 2)[0].ToUpperInvariant();
            switch (code)
            {
                case CodeA4: return VdMethod.Appendix4Recorded;
                case CodeA4Max: return VdMethod.Appendix4Envelope;
                case CodeA4Sized: return VdMethod.Appendix4Sized;
                case CodeR60228: return VdMethod.Resistance60228;
                case CodeImport: return VdMethod.Imported;
                default: return VdMethod.None;
            }
        }

        /// <summary>A figure BS 7671 checks may judge (pass or fail) on.</summary>
        public static bool IsAppendix4(VdMethod m)
            => m == VdMethod.Appendix4Recorded || m == VdMethod.Appendix4Envelope || m == VdMethod.Appendix4Sized;

        /// <summary>The schedule text (ELC_VLT_DROP_TXT) for a result: "3.91", "≤4.13" for an
        /// upper bound, or "—" when nothing was calculated (the basis says why). ELEC-26: unlike
        /// the number, this can always be overwritten, so no stale figure stays on view.</summary>
        public static string DisplayText(CircuitVdResult r)
            => r == null || !r.HasValue ? "—"
             : (r.UpperBound ? "≤" : "") + r.Pct.ToString("0.00", CultureInfo.InvariantCulture);

        public static string ImportBasis(string tool)
            => $"{CodeImport} {(string.IsNullOrWhiteSpace(tool) ? "another tool" : tool.Trim())} (method not known to STING)";

        private static string N(double v, string f = "0.#") => v.ToString(f, CultureInfo.InvariantCulture);

        private static CircuitVdResult None(string why) => new CircuitVdResult { Method = VdMethod.None, Detail = why };

        /// <summary>
        /// Work out the drop, in this order:
        ///   1. a missing input → NONE (never 0)
        ///   2. a standard with no shipped tables → NONE; NEC → conductor resistance, via
        ///      <paramref name="resistancePct"/> (NONE without it)
        ///   3. aluminium → NONE (no aluminium Appendix 4 voltage-drop table is shipped)
        ///   4. no tables (or an invalid project override) → NONE, never a built-in copy
        ///   5. a complete cable record with a loaded table → A4 from that table
        ///      (NONE when the size is not in it, or the project removed the table)
        ///   6. otherwise → A4-MAX, the highest mV/A/m any loaded table gives for the size
        ///   7. no loaded table carries the size → NONE
        /// </summary>
        public static CircuitVdResult Resolve(CircuitVdInput i, Bs7671Data data,
            Func<CircuitVdInput, double> resistancePct = null)
        {
            if (i != null && !string.IsNullOrWhiteSpace(i.MaterialRefusal)) return None(i.MaterialRefusal);
            var r = ResolveCore(i, data, resistancePct);
            // An assumed material is part of the basis, whatever the outcome.
            if (r != null && i != null && !string.IsNullOrWhiteSpace(i.MaterialNote))
                r.Detail = string.IsNullOrEmpty(r.Detail) ? i.MaterialNote : r.Detail + "; " + i.MaterialNote;
            return r;
        }

        private static CircuitVdResult ResolveCore(CircuitVdInput i, Bs7671Data data,
            Func<CircuitVdInput, double> resistancePct)
        {
            if (i == null) return None("no input");
            var missing = new List<string>();
            if (i.CurrentA <= 0) missing.Add("current");
            if (i.LengthM <= 0) missing.Add("length");
            if (i.VoltageV <= 0) missing.Add("voltage");
            if (i.CsaMm2 <= 0) missing.Add("conductor size");
            if (missing.Count > 0) return None("missing " + string.Join(", ", missing));
            int ph = i.Phases >= 3 ? 3 : 1;

            // CCA: no Appendix 4 table and no shipped resistance, under either standard — the
            // resistance method would otherwise price it as copper.
            if (StingTools.Standards.NEC2023.ConductorMaterialText.IsCopperClad(i.Material))
                return None("no copper-clad aluminium (CCA) voltage-drop or resistance data is shipped");

            string std = StingTools.Standards.ElectricalStandardId.Normalise(i.Standard);
            // A standard with no shipped tables gets no figure, never BS 7671's under its name.
            if (!StingTools.Standards.ElectricalStandardId.SupportsConductorSizing(std, out _, out string refusal))
                return None(refusal);
            if (std == StingTools.Standards.ElectricalStandardId.Nec2023)
            {
                if (resistancePct == null) return None("NEC circuit: no resistance method supplied");
                double pct = resistancePct(i);
                return pct > 0
                    ? new CircuitVdResult { Method = VdMethod.Resistance60228, Pct = pct,
                        Detail = $"conductor resistance, {N(i.CsaMm2)} mm² {i.Material}, Ib {N(i.CurrentA)} A, {N(i.LengthM)} m" }
                    : None("conductor resistance not available for this size");
            }

            string material = string.IsNullOrWhiteSpace(i.Material) ? "Cu" : i.Material.Trim();
            if (StingTools.Standards.NEC2023.ConductorMaterialText.IsAluminium(material))
                return None("no BS 7671 Appendix 4 aluminium voltage-drop table is shipped");
            if (data == null || data.Tables.Count == 0)
                return None(!string.IsNullOrEmpty(data?.LoadError) ? data.LoadError : "no BS 7671 Appendix 4 tables loaded");

            string tail = $"{(ph == 3 ? "3-ph" : "1-ph")}; Ib {N(i.CurrentA)} A, {N(i.LengthM)} m, {N(i.CsaMm2)} mm², {N(i.VoltageV, "0")} V";

            if (i.RecordComplete)
            {
                var t = data.FindTable(material, i.Insulation, i.InstallMethod, i.CableType);
                if (t == null && data.RemovedKeys.Contains(Bs7671Data.Key(material, i.Insulation, i.CableType, i.InstallMethod)))
                    return None($"the project override {data.OverrideFile} removes the table for the recorded cable " +
                                $"({i.Insulation} {i.CableType} method {i.InstallMethod})");
                if (t != null)
                {
                    var row = t.Rows.FirstOrDefault(x => Math.Abs(x.CsaMm2 - i.CsaMm2) < 1e-6);
                    double mv = row == null ? 0 : ph == 3 ? row.MvAm3ph : row.MvAm1ph;
                    if (mv <= 0)
                        return None($"{N(i.CsaMm2)} mm² has no mV/A/m in {t.CiteVoltDrop()} (the recorded cable's table)");
                    return new CircuitVdResult
                    {
                        Method = VdMethod.Appendix4Recorded, MvAm = mv, Unverified = !row.MvVerified,
                        Pct = mv * i.CurrentA * i.LengthM / 1000.0 / i.VoltageV * 100.0,
                        Detail = $"{t.CiteVoltDrop()} {N(mv, "0.###")} mV/A/m ({i.Insulation} {i.CableType} method {i.InstallMethod}), {tail}"
                                 + (row.MvVerified ? "" : "; VERIFY: single-source mV/A/m"),
                    };
                }
            }

            double worst = data.MaxTabulatedMvAm(material, i.CsaMm2, ph, out var from);
            if (worst <= 0 || from == null)
                return None($"no loaded Appendix 4 table carries {N(i.CsaMm2)} mm² {material}");
            var fromRow = from.Rows.First(x => Math.Abs(x.CsaMm2 - i.CsaMm2) < 1e-6);
            return new CircuitVdResult
            {
                Method = VdMethod.Appendix4Envelope, MvAm = worst, Unverified = !fromRow.MvVerified,
                Pct = worst * i.CurrentA * i.LengthM / 1000.0 / i.VoltageV * 100.0,
                Detail = $"upper bound: highest mV/A/m for {N(i.CsaMm2)} mm² in any loaded table " +
                         $"({from.CiteVoltDrop()} {N(worst, "0.###")} mV/A/m){(i.RecordComplete ? ", the recorded cable has no loaded table" : ", no cable recorded on the circuit")}, {tail}",
            };
        }

        /// <summary>
        /// The smallest size in <paramref name="sizes"/> whose drop, by <see cref="Resolve"/>,
        /// is within <paramref name="limitPct"/>. An A4-MAX figure counts: at or under the
        /// limit it is under it on any loaded table. Null when none qualifies.
        /// </summary>
        public static double? MinimumCsaForLimit(CircuitVdInput i, Bs7671Data data, double limitPct,
            IEnumerable<double> sizes, out CircuitVdResult at, Func<CircuitVdInput, double> resistancePct = null)
        {
            at = null;
            if (i == null || limitPct <= 0 || sizes == null) return null;
            foreach (double s in sizes.Where(x => x > 0).OrderBy(x => x))
            {
                var trial = new CircuitVdInput
                {
                    CurrentA = i.CurrentA, LengthM = i.LengthM, VoltageV = i.VoltageV, Phases = i.Phases,
                    CsaMm2 = s, Material = i.Material, Standard = i.Standard,
                    Insulation = i.Insulation, InstallMethod = i.InstallMethod, CableType = i.CableType,
                };
                var r = Resolve(trial, data, resistancePct);
                if (r.HasValue && r.Pct <= limitPct + 1e-9) { at = r; return s; }
            }
            return null;
        }
    }
}
