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

        /// <summary>Parameters whose presence makes an element worth annotating.</summary>
        public static readonly string[] DataParams = { P_VOLTAGE, P_CURRENT, P_CABLE, P_CIRCUIT };

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
