// SldAnnotText — Revit-free text building for SLD inline annotations.
//
// Split out of Commands/Symbols/SldAnnotationCommands.cs so the formats can be
// tested without Revit. The caller supplies a getter that resolves a parameter
// name to its text (from the SLD symbol, its source equipment, or that
// equipment's supply circuit — see SldAnnotationEngine).

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.SLD
{
    internal enum SldAnnotKind
    {
        All, Voltage, Current, Fault, Cable, Phase, Load, Reference, Impedance, Diversity
    }

    internal enum SldAnnotFormat { Compact, Full, Reference }

    internal static class SldAnnotText
    {
        public const string P_VOLTAGE   = "ELC_CIR_VOLTAGE_TXT";
        public const string P_CURRENT   = "ELC_CIR_DESIGN_CURRENT_TXT";
        public const string P_FAULT     = "ELC_CIR_FAULT_LEVEL_TXT";
        public const string P_CABLE     = "ELC_CABLE_CSA_TXT";
        public const string P_LOAD      = "ELC_CIR_DESIGN_LOAD_TXT";
        public const string P_BREAKER   = "ELC_CIR_BREAKER_RATING_TXT";
        public const string P_PANEL     = "ELC_PNL_NAME_TXT";
        public const string P_CIRCUIT   = "ELC_CIR_REF_TXT";
        public const string P_IMPEDANCE = "ELC_CIR_ZS_TXT";
        public const string P_DIVERSITY = "ELC_CIR_DIVERSITY_FACTOR_TXT";

        /// <summary>Parameters whose presence makes an element worth annotating.
        /// Resolved through <see cref="WithFallbacks"/>, so a value from a real
        /// source (Revit circuit property, fault study, importer) counts too.</summary>
        public static readonly string[] DataParams = { P_VOLTAGE, P_CURRENT, P_CABLE, P_CIRCUIT };

        // ── Fallback sources ─────────────────────────────────────────────────
        //
        // Nothing in the plugin WRITES the ELC_CIR_* / ELC_CABLE_CSA_TXT text
        // parameters above, so on a real model every annotation came out empty.
        // Each STING key now falls back to where the value actually lives:
        //   * STING parameters that ARE written — the fault study stamps
        //     ELC_PNL_SHORT_CIRCUIT_RATING_KA (alias ELC_PNL_FAULT_KA) on panels;
        //     the Amtech / Trimble importers stamp ELC_CABLE_CSA_MM2_TXT;
        //   * Revit's own circuit / equipment properties, passed in by the caller
        //     under the "@" keys below as SI numbers (invariant culture):
        //     volts, amps, VA — the caller converts from internal units.
        public const string P_FAULT_KA    = "ELC_PNL_SHORT_CIRCUIT_RATING_KA";
        public const string P_FAULT_ALIAS = "ELC_PNL_FAULT_KA";
        public const string P_CABLE_MM2   = "ELC_CABLE_CSA_MM2_TXT";

        public const string N_VOLTAGE_V  = "@RBS_ELEC_VOLTAGE";                 // volts
        public const string N_CURRENT_A  = "@RBS_ELEC_APPARENT_CURRENT_PARAM";  // amps
        public const string N_LOAD_VA    = "@RBS_ELEC_APPARENT_LOAD";           // VA
        public const string N_RATING_A   = "@RBS_ELEC_CIRCUIT_RATING_PARAM";    // amps
        public const string N_WIRE_SIZE  = "@RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM"; // Revit text, e.g. "3x2.5mm²"
        public const string N_PANEL      = "@CIRCUIT_PANEL_NAME";               // supply circuit's panel
        public const string N_CIRCUIT_NO = "@CIRCUIT_NUMBER";                   // supply circuit's number

        /// <summary>Fallback keys per STING key, in priority order.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Fallbacks = new Dictionary<string, string[]>
        {
            [P_VOLTAGE] = new[] { N_VOLTAGE_V },
            [P_CURRENT] = new[] { N_CURRENT_A },
            [P_FAULT]   = new[] { P_FAULT_KA, P_FAULT_ALIAS },
            [P_CABLE]   = new[] { P_CABLE_MM2, N_WIRE_SIZE },
            [P_LOAD]    = new[] { N_LOAD_VA },
            [P_BREAKER] = new[] { N_RATING_A },
            [P_PANEL]   = new[] { N_PANEL },
            [P_CIRCUIT] = new[] { N_CIRCUIT_NO },
        };

        /// <summary>
        /// Wraps a raw getter so each STING key returns its own value when set,
        /// else the first fallback that yields a usable value, formatted for the
        /// annotation builders (units the builder does not add itself are included,
        /// e.g. "230 V", "12.5 kVA", "32 A").
        /// </summary>
        public static Func<string, string> WithFallbacks(Func<string, string> raw)
        {
            if (raw == null) return _ => "";
            return key =>
            {
                string own = (raw(key) ?? "").Trim();
                if (own.Length > 0) return own;
                if (!Fallbacks.TryGetValue(key, out var alts)) return "";
                foreach (var alt in alts)
                {
                    string v = FormatFallback(alt, (raw(alt) ?? "").Trim());
                    if (v.Length > 0) return v;
                }
                return "";
            };
        }

        /// <summary>Formats one fallback source value; "" when absent or not positive.</summary>
        public static string FormatFallback(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            switch (key)
            {
                case N_VOLTAGE_V:
                    return TryNum(value, out double v) && v > 0 ? $"{Fmt(v, "0.#")} V" : "";
                case N_CURRENT_A:
                    return TryNum(value, out double a) && a > 0 ? Fmt(a, "0.#") : "";
                case N_LOAD_VA:
                    // Apparent load — shown in kVA with its unit, never relabelled kW.
                    return TryNum(value, out double va) && va > 0 ? $"{Fmt(va / 1000.0, "0.##")} kVA" : "";
                case N_RATING_A:
                    return TryNum(value, out double r) && r > 0 ? $"{Fmt(r, "0.#")} A" : "";
                case P_FAULT_KA:
                case P_FAULT_ALIAS:
                    return TryNum(value, out double ka) && ka > 0 ? Fmt(ka, "0.##") : "";
                case P_CABLE_MM2:
                case N_WIRE_SIZE:
                {
                    // One parser for every wire-size string: ELC_CABLE_CSA_MM2_TXT is
                    // usually a bare number, Revit's wire size is "3x2.5mm²" / "#12".
                    double mm2 = IsBareNumber(value) && TryNum(value, out double bare)
                        ? bare
                        : StingTools.Core.Electrical.WireSizeParser.ParseCsaMm2(value);
                    return mm2 > 0 ? Fmt(mm2, "0.##") : "";
                }
                default:
                    return value; // text sources (panel name, circuit number)
            }
        }

        private static bool IsBareNumber(string s)
            => System.Text.RegularExpressions.Regex.IsMatch(s.Trim(), @"^[0-9]+(?:[.,][0-9]+)?$");

        private static bool TryNum(string s, out double n)
        {
            n = 0;
            var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"[-+]?[0-9]*[.,]?[0-9]+");
            return m.Success && double.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out n);
        }

        private static string Fmt(double d, string f) => d.ToString(f, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The annotation text for one element. The Reference format means
        /// "circuit reference + panel name only", whatever kind was asked for —
        /// before, it only changed the Cable text and was otherwise ignored.
        /// </summary>
        public static string Build(SldAnnotKind kind, SldAnnotFormat fmt, Func<string, string> get)
        {
            if (get == null) return "";
            if (fmt == SldAnnotFormat.Reference && kind != SldAnnotKind.Phase)
                return BuildReference(get, fmt);
            switch (kind)
            {
                case SldAnnotKind.Voltage:   return BuildVoltage(get, fmt);
                case SldAnnotKind.Current:   return BuildCurrent(get, fmt);
                case SldAnnotKind.Fault:     return BuildFault(get, fmt);
                case SldAnnotKind.Cable:     return BuildCable(get, fmt);
                case SldAnnotKind.Phase:     return BuildPhase(get, fmt);
                case SldAnnotKind.Load:      return BuildLoad(get, fmt);
                case SldAnnotKind.Reference: return BuildReference(get, fmt);
                case SldAnnotKind.Impedance: return BuildImpedance(get, fmt);
                case SldAnnotKind.Diversity: return BuildDiversity(get, fmt);
                case SldAnnotKind.All:       return BuildAll(get, fmt);
                default:                     return "";
            }
        }

        private static string G(Func<string, string> get, string p) => (get(p) ?? "").Trim();

        private static string BuildVoltage(Func<string, string> get, SldAnnotFormat fmt)
        {
            string v = G(get, P_VOLTAGE);
            if (v.Length == 0) return "";
            return fmt == SldAnnotFormat.Full ? $"Voltage: {v}" : v;
        }

        private static string BuildCurrent(Func<string, string> get, SldAnnotFormat fmt)
        {
            string i = G(get, P_CURRENT);
            if (i.Length == 0) return "";
            return fmt == SldAnnotFormat.Full ? $"Ib: {i} A" : $"{i}A";
        }

        private static string BuildFault(Func<string, string> get, SldAnnotFormat fmt)
        {
            string f = G(get, P_FAULT);
            if (f.Length == 0) return "";
            return fmt == SldAnnotFormat.Full ? $"Icc: {f} kA" : $"{f}kA";
        }

        private static string BuildCable(Func<string, string> get, SldAnnotFormat fmt)
        {
            string csa = G(get, P_CABLE);
            string ib  = G(get, P_CURRENT);
            string brk = G(get, P_BREAKER);
            if (csa.Length == 0) return "";
            if (fmt == SldAnnotFormat.Full)
            {
                var parts = new List<string> { $"Cable: {csa} mm²" };
                if (ib.Length > 0)  parts.Add($"Ib: {ib}A");
                if (brk.Length > 0) parts.Add($"Breaker: {brk}");
                return string.Join(" | ", parts);
            }
            return string.Join(" / ", new[] { csa, ib, brk }.Where(x => x.Length > 0));
        }

        private static string BuildPhase(Func<string, string> get, SldAnnotFormat fmt)
        {
            string v = G(get, P_VOLTAGE);
            if (v.Length == 0) return "";
            // 3-phase distribution is line-to-line >= ~300 V; an unparseable
            // value gets no phase label rather than a guessed one.
            if (!TryParseVolts(v, out double volts)) return "";
            bool is3ph = volts >= 300.0;
            return fmt == SldAnnotFormat.Full
                ? (is3ph ? "L1 / L2 / L3 / N / PE" : "L / N / PE")
                : (is3ph ? "3Ph+N+PE" : "1Ph+N");
        }

        /// <summary>Parses "400", "400 V", "0.4kV", "3.3 kV" into volts.</summary>
        public static bool TryParseVolts(string raw, out double volts)
        {
            volts = 0.0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            double multiplier = s.IndexOf("kv", StringComparison.OrdinalIgnoreCase) >= 0 ? 1000.0 : 1.0;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"[-+]?[0-9]*\.?[0-9]+");
            if (!m.Success) return false;
            if (!double.TryParse(m.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double n))
                return false;
            volts = n * multiplier;
            return true;
        }

        private static string BuildLoad(Func<string, string> get, SldAnnotFormat fmt)
        {
            string load = G(get, P_LOAD);
            if (load.Length == 0) return "";
            // A value that already carries its unit (the kVA fallback) keeps it —
            // apparent load must not be relabelled kW.
            if (char.IsLetter(load[load.Length - 1]))
                return fmt == SldAnnotFormat.Full ? $"Load: {load}" : load.Replace(" ", "");
            return fmt == SldAnnotFormat.Full ? $"Load: {load} kW" : $"{load}kW";
        }

        private static string BuildReference(Func<string, string> get, SldAnnotFormat fmt)
        {
            var parts = new[] { G(get, P_PANEL), G(get, P_CIRCUIT) }.Where(x => x.Length > 0);
            string refs = string.Join(" — ", parts);
            return refs.Length == 0 ? "" : (fmt == SldAnnotFormat.Full ? $"Ref: {refs}" : refs);
        }

        private static string BuildImpedance(Func<string, string> get, SldAnnotFormat fmt)
        {
            string zs = G(get, P_IMPEDANCE);
            if (zs.Length == 0) return "";
            return fmt == SldAnnotFormat.Full ? $"Zs: {zs} Ω" : $"Zs={zs}Ω";
        }

        private static string BuildDiversity(Func<string, string> get, SldAnnotFormat fmt)
        {
            string df = G(get, P_DIVERSITY);
            if (df.Length == 0) return "";
            return fmt == SldAnnotFormat.Full ? $"Diversity: {df}" : $"Df={df}";
        }

        private static string BuildAll(Func<string, string> get, SldAnnotFormat fmt)
        {
            // Each field in the chosen format — before, "All" always rendered the
            // compact fields and only changed the separator.
            var segments = new List<string>
            {
                BuildCable(get, fmt),
                BuildVoltage(get, fmt),
                BuildFault(get, fmt),
                BuildLoad(get, fmt),
            }.Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (segments.Count == 0) return "";
            return fmt == SldAnnotFormat.Full ? string.Join(" | ", segments) : string.Join("  ", segments);
        }

        /// <summary>
        /// Parses a stamped kind name back to the enum. Returns false for anything
        /// not produced by <see cref="SldAnnotKind"/>.ToString().
        /// </summary>
        public static bool TryParseKind(string s, out SldAnnotKind kind)
        {
            kind = default;
            return IsName(s) && Enum.TryParse(s, false, out kind) && Enum.IsDefined(typeof(SldAnnotKind), kind);
        }

        public static bool TryParseFormat(string s, out SldAnnotFormat fmt)
        {
            fmt = default;
            return IsName(s) && Enum.TryParse(s, false, out fmt) && Enum.IsDefined(typeof(SldAnnotFormat), fmt);
        }

        private static bool IsName(string s) => !string.IsNullOrEmpty(s) && char.IsLetter(s[0]);
    }
}
