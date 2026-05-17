// StingTools — PenetrationProductSelector.
//
// Phase 178f + Phase 179 — picks the right penetration product family
// and type variant for each PenetrationRecord based on:
//   (member-category, duct-shape, host-fire-rating, host-smoke-rating)
//
// Full decision matrix:
//
//   Member          | Fire-rated host      | Smoke-rated host | Acoustic-only | Non-rated
//   ----------------+----------------------+------------------+---------------+----------
//   Pipe / Conduit  | FRP firestop (FR*)   | FRP FR60         | AcousticSeal  | Sleeve
//   Cable Tray      | FRP firestop (FR*)   | FRP FR60         | AcousticSeal  | Sleeve
//   Duct — round    | FD_FR*_ROUND_MOTOR.  | FSD_SMOKE_ONLY   | AcousticSeal  | Sleeve
//   Duct — rect     | FD_FR*_RECT_FUSIBLE  | FSD_SMOKE_ONLY   | AcousticSeal  | Sleeve
//   Duct — restricted depth | FD_CURTAIN_FR60 when FR60 rect + depth flag
//   Combined fire+smoke duct | FD_FR*_COMBINED_SMOKE when host has both ratings
//   Flex pipe/duct  | FRP firestop *       | FRP FR60         | AcousticSeal  | Sleeve
//
// * Flex ducts crossing fire-rated barriers should normally be replaced
//   by rigid duct sections at the penetration; the selector still maps
//   to FRP so the record appears in the register.
//
// Acoustic-only hosts: STING_ACOUSTIC_RW_DB > 0 and STING_FIRE_RATING_TXT empty.
// Smoke-rated hosts:   STING_SMOKE_COMPARTMENT_TXT set (or STING_FIRE_SMOKE_TXT).
// Beam hosts skip fire-stop dispatch entirely — always SLEEVE_GENERIC.
//
// Round vs rectangular duct detection: MemberDiameterMm > 0 from the
// detector = round pipe/conduit/round duct. Value of 0 = rectangular.
// Confirmed via ConnectorManager.Connectors[0].Shape when available.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public enum PenetrationProductKind
    {
        Firestop,        // STING_SEED_SpecialityEquipment (FR* variants)
        FireDamper,      // STING_SEED_FireDamper
        AcousticSeal,    // STING_SEED_AcousticSeal
        SleeveGeneric,   // STING_SEED_SpecialityEquipment (SLEEVE_GENERIC variant)
    }

    public sealed class PenetrationProductChoice
    {
        public PenetrationProductKind Kind { get; set; }
        /// <summary>Family name to look up (matches the seed JSON id).</summary>
        public string FamilyName { get; set; }
        /// <summary>Type-variant name to match against loaded family symbol names.</summary>
        public string TypeVariantHint { get; set; }
        /// <summary>Free-text reason surfaced in the placement result panel.</summary>
        public string Rationale { get; set; }
    }

    public static class PenetrationProductSelector
    {
        // ─── Main dispatch ───────────────────────────────────────────────

        public static PenetrationProductChoice Select(Document doc, PenetrationRecord rec)
        {
            // Beam hosts: structural review only, generic sleeve.
            if (rec.HostKind == PenetrationHostKind.Beam)
            {
                return new PenetrationProductChoice
                {
                    Kind            = PenetrationProductKind.SleeveGeneric,
                    FamilyName      = "STING_SEED_SpecialityEquipment",
                    TypeVariantHint = "SLEEVE_GENERIC",
                    Rationale       = "Beam host — generic sleeve; structural review via PEN_STRUCTURAL_FLAG_TXT.",
                };
            }

            bool isDuct      = IsDuctMember(rec.MemberCategory);
            bool fireRated   = !string.IsNullOrEmpty(rec.FireRating)
                               && !string.Equals(rec.FireRating, "0", StringComparison.Ordinal);
            bool smokeRated  = HasSmokeRating(doc, rec.HostId);
            bool acousticOnly = !fireRated && !smokeRated && HasAcousticRating(doc, rec.HostId);

            // ── Fire-rated host ─────────────────────────────────────────
            if (fireRated)
            {
                if (isDuct)
                {
                    bool isRound  = rec.MemberDiameterMm > 0;
                    bool combined = smokeRated; // host carries both fire + smoke compartment ratings
                    string variant = MapDamperVariant(rec.FireRating, isRound, combined);
                    return new PenetrationProductChoice
                    {
                        Kind            = PenetrationProductKind.FireDamper,
                        FamilyName      = "STING_SEED_FireDamper",
                        TypeVariantHint = variant,
                        Rationale       = $"Duct ({(isRound ? "round" : "rect")}) through {rec.FireRating} barrier" +
                                          (combined ? " + smoke compartment" : "") +
                                          $" — {VariantDescription(variant)}.",
                    };
                }
                // Pipe, conduit, cable tray, flex duct.
                string frVariant = NormaliseFireRating(rec.FireRating);
                return new PenetrationProductChoice
                {
                    Kind            = PenetrationProductKind.Firestop,
                    FamilyName      = "STING_SEED_SpecialityEquipment",
                    TypeVariantHint = frVariant,
                    Rationale       = $"{rec.MemberCategory} through {rec.FireRating} barrier — BS 476-20 / EN 1366-3 intumescent firestop ({frVariant}).",
                };
            }

            // ── Smoke-rated host (no fire rating) ───────────────────────
            if (smokeRated && isDuct)
            {
                return new PenetrationProductChoice
                {
                    Kind            = PenetrationProductKind.FireDamper,
                    FamilyName      = "STING_SEED_FireDamper",
                    TypeVariantHint = "FSD_SMOKE_ONLY",
                    Rationale       = "Smoke compartment barrier, duct — 24 V motorised smoke-only damper (BS EN 15650 ES).",
                };
            }

            // ── Acoustic-only host ──────────────────────────────────────
            if (acousticOnly)
            {
                // Pick acoustic seal variant by Rw rating where readable.
                string acsVariant = MapAcousticVariant(doc, rec.HostId);
                return new PenetrationProductChoice
                {
                    Kind            = PenetrationProductKind.AcousticSeal,
                    FamilyName      = "STING_SEED_AcousticSeal",
                    TypeVariantHint = acsVariant,
                    Rationale       = $"Acoustic-rated host (Rw) — BS 8233 / Approved Doc E acoustic seal ({acsVariant}).",
                };
            }

            // ── Non-rated host ───────────────────────────────────────────
            return new PenetrationProductChoice
            {
                Kind            = PenetrationProductKind.SleeveGeneric,
                FamilyName      = "STING_SEED_SpecialityEquipment",
                TypeVariantHint = "SLEEVE_GENERIC",
                Rationale       = "Non-rated host — generic unlined sleeve.",
            };
        }

        // ─── Type-variant mappers ─────────────────────────────────────────

        /// <summary>
        /// Maps (fireRating, isRound, combinedSmoke) to the exact
        /// TypeVariantDefinition name used in STING_SEED_FireDamper.json.
        /// Round ducts always get motorised (single-blade dampers in circular
        /// housings are factory-motorised); rectangular defaults to fusible-link
        /// at FR60/90, motorised at FR120+, combined when smoke+fire.
        /// </summary>
        private static string MapDamperVariant(string rating, bool isRound, bool combinedSmoke)
        {
            string r = rating?.ToUpperInvariant() ?? "FR60";
            if (isRound)
            {
                switch (r)
                {
                    case "FR30":  return "FD_FR30_ROUND_MOTORISED";
                    case "FR60":  return "FD_FR60_ROUND_MOTORISED";
                    case "FR90":  return "FD_FR90_ROUND_MOTORISED";
                    case "FR120": return "FD_FR120_ROUND_MOTORISED";
                    default:      return "FD_FR60_ROUND_MOTORISED";
                }
            }
            // Rectangular
            if (combinedSmoke)
            {
                switch (r)
                {
                    case "FR120": return "FD_FR120_COMBINED_SMOKE";
                    default:      return "FD_FR60_COMBINED_SMOKE";
                }
            }
            switch (r)
            {
                case "FR60":  return "FD_FR60_RECT_FUSIBLE";
                case "FR90":  return "FD_FR90_RECT_FUSIBLE";
                case "FR120": return "FD_FR120_RECT_MOTORISED"; // high-rating rect → motorised
                default:      return "FD_FR60_RECT_FUSIBLE";
            }
        }

        /// <summary>
        /// Returns a short description of the selected variant for the
        /// placement rationale string shown in the result panel.
        /// </summary>
        private static string VariantDescription(string variant)
        {
            if (variant == null) return "fire damper";
            if (variant.Contains("COMBINED")) return "combined fire+smoke damper (BS EN 15650 EIS)";
            if (variant.Contains("ROUND"))    return "round motorised fire damper (BS EN 15650 EI)";
            if (variant.Contains("MOTORISED"))return "motorised fire damper (BS EN 15650 EI)";
            if (variant.Contains("FUSIBLE"))  return "fusible-link fire damper (BS EN 15650 EI)";
            if (variant.Contains("CURTAIN"))  return "curtain-type fire damper (restricted depth)";
            return "fire damper";
        }

        /// <summary>
        /// Normalises a fire-rating string to one of the SpecialityEquipment
        /// seed variant names (FR30, FR60, FR90, FR120, FR240).
        /// </summary>
        private static string NormaliseFireRating(string rating)
        {
            string r = rating?.ToUpperInvariant() ?? "";
            switch (r)
            {
                case "FR30":  return "FR30";
                case "FR60":  return "FR60";
                case "FR90":  return "FR90";
                case "FR120": return "FR120";
                case "FR240": return "FR240";
                default:      return "FR60";
            }
        }

        /// <summary>
        /// Selects the acoustic seal variant based on the host's Rw rating.
        /// Falls back to ACS_RW45 (the most common UK partition requirement)
        /// when the parameter isn't present.
        /// </summary>
        private static string MapAcousticVariant(Document doc, ElementId hostId)
        {
            double rw = 0;
            try
            {
                var host = doc?.GetElement(hostId);
                if (host != null)
                {
                    string s = ParameterHelpers.GetString(host, "STING_ACOUSTIC_RW_DB");
                    if (!double.TryParse(s, out rw) || rw <= 0)
                    {
                        var t = doc.GetElement(host.GetTypeId());
                        if (t != null) double.TryParse(ParameterHelpers.GetString(t, "STING_ACOUSTIC_RW_DB"), out rw);
                    }
                }
            }
            catch { }
            if (rw >= 55) return "ACS_RW55";
            if (rw >= 50) return "ACS_RW50";
            if (rw >= 45) return "ACS_RW45";
            return "ACS_RW40";
        }

        // ─── Host-rating helpers ──────────────────────────────────────────

        private static bool IsDuctMember(string memberCategory)
        {
            if (string.IsNullOrEmpty(memberCategory)) return false;
            return memberCategory.IndexOf("duct", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasAcousticRating(Document doc, ElementId hostId)
        {
            if (doc == null || hostId == null || hostId == ElementId.InvalidElementId) return false;
            try
            {
                var host = doc.GetElement(hostId);
                if (host == null) return false;
                string s = ParameterHelpers.GetString(host, "STING_ACOUSTIC_RW_DB");
                if (!string.IsNullOrEmpty(s) && double.TryParse(s, out double rw) && rw > 0) return true;
                var t = doc.GetElement(host.GetTypeId());
                if (t == null) return false;
                s = ParameterHelpers.GetString(t, "STING_ACOUSTIC_RW_DB");
                return !string.IsNullOrEmpty(s) && double.TryParse(s, out rw) && rw > 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Returns true when the host (or its type) carries a smoke-compartment
        /// flag. Checks STING_SMOKE_COMPARTMENT_TXT, STING_FIRE_SMOKE_TXT, and
        /// the generic STING_BARRIER_TYPE_TXT for "SMOKE" or "FIRE+SMOKE".
        /// </summary>
        private static bool HasSmokeRating(Document doc, ElementId hostId)
        {
            if (doc == null || hostId == null || hostId == ElementId.InvalidElementId) return false;
            try
            {
                foreach (var elId in new[] { hostId, null })
                {
                    Element el;
                    if (elId == null)
                    {
                        var host2 = doc.GetElement(hostId);
                        if (host2 == null) break;
                        el = doc.GetElement(host2.GetTypeId());
                        if (el == null) break;
                    }
                    else { el = doc.GetElement(elId); if (el == null) continue; }

                    string smoke = ParameterHelpers.GetString(el, "STING_SMOKE_COMPARTMENT_TXT");
                    if (!string.IsNullOrEmpty(smoke) && !string.Equals(smoke, "NONE", StringComparison.OrdinalIgnoreCase))
                        return true;
                    string firesmoke = ParameterHelpers.GetString(el, "STING_FIRE_SMOKE_TXT");
                    if (!string.IsNullOrEmpty(firesmoke)) return true;
                    string barrierType = ParameterHelpers.GetString(el, "STING_BARRIER_TYPE_TXT");
                    if (!string.IsNullOrEmpty(barrierType)
                        && barrierType.IndexOf("SMOKE", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
