// StingTools — PenetrationProductSelector.
//
// Phase 178f — picks the right penetration product family for each
// PenetrationRecord based on (member-category, host-rating-type).
//
// Decision matrix:
//
//   Member          | Fire-rated host  | Acoustic-only host  | Non-rated host
//   ----------------+------------------+---------------------+--------------------
//   Pipe / Conduit  | FRP firestop     | Acoustic seal       | Sleeve (generic)
//   Cable Tray      | FRP firestop     | Acoustic seal       | Sleeve (generic)
//   Duct (any)      | Fire damper      | Acoustic seal       | Sleeve / no action
//   Flex pipe/duct  | FRP firestop *   | Acoustic seal       | Sleeve (generic)
//
// * Flex ducts crossing fire-rated barriers should normally be
//   replaced by rigid duct sections at the penetration; the selector
//   still maps to FRP so the firestop appears in the register, but
//   PenetrationCoverageValidator surfaces a separate flag.
//
// Acoustic-only hosts are walls / floors with STING_ACOUSTIC_RW_DB > 0
// and STING_FIRE_RATING_TXT empty. Beam hosts skip the dispatch
// entirely — their record always maps to the existing
// SLEEVE_GENERIC variant on the SpecialityEquipment seed because
// beam holes are a structural concern, not a fire-stop concern.

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
        /// <summary>Type-variant suffix to match against family symbol names.</summary>
        public string TypeVariantHint { get; set; }
        /// <summary>Free-text reason — surfaced in the placement result panel.</summary>
        public string Rationale { get; set; }
    }

    public static class PenetrationProductSelector
    {
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

            bool isDuct = IsDuctMember(rec.MemberCategory);
            bool fireRated  = !string.IsNullOrEmpty(rec.FireRating) && !string.Equals(rec.FireRating, "0", StringComparison.Ordinal);
            bool acousticOnly = !fireRated && HasAcousticRating(doc, rec.HostId);

            if (fireRated)
            {
                if (isDuct)
                {
                    return new PenetrationProductChoice
                    {
                        Kind            = PenetrationProductKind.FireDamper,
                        FamilyName      = "STING_SEED_FireDamper",
                        TypeVariantHint = MapDamperVariant(rec.FireRating),
                        Rationale       = $"Duct through {rec.FireRating} barrier — BS EN 15650 fire damper.",
                    };
                }
                return new PenetrationProductChoice
                {
                    Kind            = PenetrationProductKind.Firestop,
                    FamilyName      = "STING_SEED_SpecialityEquipment",
                    TypeVariantHint = rec.FireRating,
                    Rationale       = $"Pipe / cable through {rec.FireRating} barrier — BS 476-20 firestop.",
                };
            }

            if (acousticOnly)
            {
                return new PenetrationProductChoice
                {
                    Kind            = PenetrationProductKind.AcousticSeal,
                    FamilyName      = "STING_SEED_AcousticSeal",
                    TypeVariantHint = "ACS_RW45",
                    Rationale       = "Acoustic-rated host — BS 8233 / Approved Doc E acoustic seal.",
                };
            }

            return new PenetrationProductChoice
            {
                Kind            = PenetrationProductKind.SleeveGeneric,
                FamilyName      = "STING_SEED_SpecialityEquipment",
                TypeVariantHint = "SLEEVE_GENERIC",
                Rationale       = "Non-rated host — generic sleeve.",
            };
        }

        private static bool IsDuctMember(string memberCategory)
        {
            if (string.IsNullOrEmpty(memberCategory)) return false;
            string s = memberCategory.ToLowerInvariant();
            return s.Contains("duct");
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

        /// <summary>Map fire rating to the closest fire-damper variant.</summary>
        private static string MapDamperVariant(string rating)
        {
            switch (rating?.ToUpperInvariant())
            {
                case "FR30":
                case "FR60":   return "FD_FR60_RECT_FUSIBLE";
                case "FR90":   return "FD_FR90_RECT_FUSIBLE";
                case "FR120":  return "FD_FR120_RECT_FUSIBLE";
            }
            return "FD_FR60_RECT_FUSIBLE";
        }
    }
}
