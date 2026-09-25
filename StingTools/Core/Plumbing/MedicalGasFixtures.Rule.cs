// StingTools — the Revit-free half of MedicalGasFixtures (unit-tested by StingTools.Tags.Tests)

using System;

namespace StingTools.Core.Plumbing
{
    public static partial class MedicalGasFixtures
    {
        public const string SeedFamilyId = "STING_SEED_MedGasOutlet";

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
