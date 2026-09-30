// StingTools — the Revit-free half of MedicalGasFixtures (unit-tested by StingTools.Tags.Tests)

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Plumbing
{
    public static partial class MedicalGasFixtures
    {
        public const string SeedFamilyId = "STING_SEED_MedGasOutlet";

        /// <summary>
        /// The MGS_GAS_TYPE_TXT vocabulary — the codes MgasNetwork, MgasFlowSolver,
        /// NFPA99Standards, the fab rules and the filters all read.
        /// </summary>
        public static readonly IReadOnlyList<string> GasCodes =
            new[] { "O2", "MA4", "MA7", "N2O", "N2", "CO2", "HE", "VAC", "AGS" };

        // What people type into a room's gas requirement, mapped onto that vocabulary.
        // Writing "AIR" or "AGSS" onto an outlet made it invisible to every consumer above.
        private static readonly Dictionary<string, string> GasAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "OXYGEN", "O2" },
                { "AIR", "MA4" }, { "MEDAIR", "MA4" }, { "MA", "MA4" },
                { "SURGAIR", "MA7" }, { "SA", "MA7" }, { "SA7", "MA7" },
                { "NITROUS", "N2O" }, { "ENTONOX", "N2O" },
                { "NITROGEN", "N2" },
                { "HELIOX", "HE" },
                { "VACUUM", "VAC" }, { "MV", "VAC" },
                { "AGSS", "AGS" },
            };

        /// <summary>The canonical gas code for <paramref name="code"/>, or null when it is not a gas STING knows.</summary>
        public static string CanonicalGasCode(string code)
        {
            string c = (code ?? "").Trim();
            if (c.Length == 0) return null;
            foreach (var g in GasCodes)
                if (string.Equals(g, c, StringComparison.OrdinalIgnoreCase)) return g;
            return GasAliases.TryGetValue(c, out var mapped) ? mapped : null;
        }

        /// <summary>
        /// The family is STING's own medical-gas seed (Revit appends a digit when a
        /// same-named family is loaded twice). STING_SEED_FAMILY_TXT is deliberately NOT
        /// the test: it is an instance value that survives a swap to a manufacturer
        /// family, and a manufacturer outlet's gas must never be overwritten from the seed.
        /// </summary>
        public static bool IsSeedFamilyName(string familyName)
        {
            string n = (familyName ?? "").Trim();
            if (!n.StartsWith(SeedFamilyId, StringComparison.OrdinalIgnoreCase)) return false;
            string rest = n.Substring(SeedFamilyId.Length);
            return rest.All(char.IsDigit);
        }

        /// <summary>
        /// A medical-gas element carries a gas type, the MG discipline code, or the
        /// medical-gas seed's id.
        /// </summary>
        public static bool IsMedicalGas(string disciplineCode, string gasType, string seedFamily)
            => !string.IsNullOrWhiteSpace(gasType)
            || string.Equals((disciplineCode ?? "").Trim(), "MG", StringComparison.OrdinalIgnoreCase)
            || string.Equals((seedFamily ?? "").Trim(), SeedFamilyId, StringComparison.OrdinalIgnoreCase);
    }
}
