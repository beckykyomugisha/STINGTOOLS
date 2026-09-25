// StingTools — which Plumbing Fixtures are medical-gas terminal units, not sanitaryware
//
// The MGPS stack models terminal units and alarm panels as Plumbing Fixtures
// (MgasNetwork, MgasFlowValidator, the TU / AAP / MAP tags), and since DT-4 so does
// STING's own outlet seed. Everything in Core/Plumbing that scans the Plumbing
// Fixtures category for sinks, WCs and basins must therefore leave them out: an
// oxygen outlet has no trap, no drainage unit and no loading unit.
//
// One predicate, used by every such scan, so the rule cannot be applied in one
// engine and forgotten in the next.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Plumbing
{
    // The rule itself lives in MedicalGasFixtures.Rule.cs (Revit-free, unit-tested).
    public static partial class MedicalGasFixtures
    {
        public static bool IsMedicalGas(Element el)
        {
            if (el == null) return false;
            return IsMedicalGas(Read(el, ParamRegistry.DISC), Read(el, "MGS_GAS_TYPE_TXT"), Read(el, "STING_SEED_FAMILY_TXT"));
        }

        private static string Read(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return null;
                return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            }
            catch (Exception ex) { StingLog.Warn($"MedicalGasFixtures.Read {name}: {ex.Message}"); return null; }
        }
    }
}
