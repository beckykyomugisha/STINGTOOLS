// ============================================================================
// StingTools Standards — the canonical electrical standard vocabulary.
//
// KUT-7. Before this file there were two vocabularies and nothing translated
// between them:
//
//   the panel emits   "BS7671" | "NEC2023" | "IEC60364" | "ASNZS"
//   the engine tests  standard == "NEC"
//
// So selecting "NEC 2023 (US)" produced a 100% BS 7671 answer, because the
// token never matched. The ROADMAP called NEC2023 "unreachable" without naming
// the reason, and the reason matters: the obvious repair — normalise "NEC2023"
// to "NEC" — is the WORST available change, because the only two places that
// tested for "NEC" were a breaker-size lookup and a routine that renames a
// BS-sized mm2 conductor to the nearest AWG. Matching the token would have
// switched on an AWG label for a conductor sized by BS 7671 Appendix 4.
//
// One vocabulary, declared once, is the precondition for routing by standard
// at all. Revit-free so the routing policy is unit-testable.
// ============================================================================

using System;
using System.Collections.Generic;

namespace StingTools.Standards
{
    /// <summary>The electrical standards STING recognises. The string form is the
    /// canonical id used everywhere: panel tag, snapshot, engine input, parameter.</summary>
    public static class ElectricalStandardId
    {
        public const string Bs7671 = "BS7671";
        public const string Nec2023 = "NEC2023";
        public const string Iec60364 = "IEC60364";
        public const string AsNzs3000 = "ASNZS3000";

        /// <summary>The default when nothing has been selected. BS 7671, because that
        /// is what the panel pre-selects and what every shipped correction-factor table
        /// is drawn from.</summary>
        public const string Default = Bs7671;

        // Every spelling that has ever reached an engine, mapped to one id. "NEC" is
        // the engine's own legacy token; "ASNZS" is the panel's combo tag. Both stay
        // accepted so an older snapshot or a hand-set parameter still resolves.
        private static readonly Dictionary<string, string> _aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BS7671"] = Bs7671,
                ["BS 7671"] = Bs7671,
                ["BS7671_2018"] = Bs7671,
                ["BS7671:2018"] = Bs7671,
                ["NEC"] = Nec2023,
                ["NEC2023"] = Nec2023,
                ["NEC 2023"] = Nec2023,
                ["NEC_2023"] = Nec2023,
                ["IEC60364"] = Iec60364,
                ["IEC 60364"] = Iec60364,
                ["ASNZS"] = AsNzs3000,
                ["ASNZS3000"] = AsNzs3000,
                ["AS/NZS 3000"] = AsNzs3000,
                ["AS3000"] = AsNzs3000,
            };

        /// <summary>Canonical id for any spelling, or <see cref="Default"/> for null,
        /// blank or unrecognised input.</summary>
        public static string Normalise(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Default;
            return _aliases.TryGetValue(raw.Trim(), out string id) ? id : Default;
        }

        /// <summary>True when <paramref name="raw"/> is a spelling this map knows.
        /// Distinguishes "the user chose something we do not recognise" from "the user
        /// chose BS 7671" — <see cref="Normalise"/> alone cannot, because both answer
        /// BS7671.</summary>
        public static bool IsRecognised(string raw)
            => !string.IsNullOrWhiteSpace(raw) && _aliases.ContainsKey(raw.Trim());

        /// <summary>Human label for a report or a dialog.</summary>
        public static string Label(string raw) => Normalise(raw) switch
        {
            Nec2023 => "NEC 2023",
            Iec60364 => "IEC 60364",
            AsNzs3000 => "AS/NZS 3000",
            _ => "BS 7671:2018+A2:2022",
        };

        // ── What STING can actually calculate, per standard ──────────────────

        /// <summary>
        /// Whether conductor SIZING is implemented for a standard, and if not, why.
        ///
        /// <para>This is the honest half of KUT-7. A cable size carries a standard's
        /// name onto a drawing and into a model parameter; producing one from another
        /// standard's tables is worse than producing none, because nothing downstream
        /// can tell the difference. Where the tables are not in the tree, the engine
        /// refuses and says so.</para>
        /// </summary>
        public static bool SupportsConductorSizing(string raw, out string basis, out string refusal)
        {
            switch (Normalise(raw))
            {
                case Nec2023:
                    basis = "NEC 2023 Table 310.16 (75 °C column), 310.15(B)(1) ambient correction, " +
                            "310.15(C)(1) bundling adjustment, 240.6(A) standard ratings, " +
                            "210.19(A)(1) 125% continuous, 240.4(D) small-conductor limit";
                    refusal = null;
                    return true;

                case Iec60364:
                    // BS 7671 IS the UK national implementation of IEC 60364. The shipped
                    // install-method and ambient factors are the Appendix 4 set, which is
                    // harmonised with IEC 60364-5-52 Annex B; sizing on them under an
                    // IEC label is the same calculation, not a substitution.
                    basis = "IEC 60364-5-52 Annex B, via the harmonised BS 7671 Appendix 4 " +
                            "correction factors and standard mm2 series";
                    refusal = null;
                    return true;

                case AsNzs3000:
                    basis = null;
                    refusal =
                        "Conductor sizing is NOT implemented for AS/NZS 3000. The AS/NZS 3008.1.1 " +
                        "current-carrying-capacity and derating tables are not present in this " +
                        "installation, and the shipped tables are BS 7671 Appendix 4. Sizing on them " +
                        "would print an AS/NZS cable size derived from a different standard. " +
                        "Select BS 7671 or IEC 60364 to size, or size externally to AS/NZS 3008.";
                    return false;

                default:
                    basis = "BS 7671:2018+A2:2022 Appendix 4 (install method, insulation and " +
                            "ambient correction factors; Table 4B1) with the standard mm2 series";
                    refusal = null;
                    return true;
            }
        }

        /// <summary>Convenience overload for callers that only need the yes/no.</summary>
        public static bool SupportsConductorSizing(string raw)
            => SupportsConductorSizing(raw, out _, out _);

        /// <summary>True when the standard's conductor series is AWG / kcmil rather than
        /// mm2. Drives how a size is REPORTED — never how it is chosen.</summary>
        public static bool UsesAwgSeries(string raw) => Normalise(raw) == Nec2023;
    }
}
