using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Electrical
{
    /// <summary>
    /// The cable a circuit was sized with, as stamped on the circuit when a size is applied
    /// (install method, insulation, cable type). Checks that run later, such as the Circuit
    /// Check and the breaker sizer, read it back so they judge the circuit against its own
    /// Appendix 4 table instead of a panel-wide assumption.
    /// </summary>
    public sealed class CircuitCableRecord
    {
        public string InstallMethod { get; private set; }
        public string Insulation { get; private set; }
        public string CableType { get; private set; }

        /// <summary>All three are recorded. A partial record is not used: guessing the missing
        /// part would pick a table the circuit was never sized on.</summary>
        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(InstallMethod) && !string.IsNullOrWhiteSpace(Insulation)
            && !string.IsNullOrWhiteSpace(CableType);

        public static CircuitCableRecord Read(Element circuit)
        {
            if (circuit == null) return new CircuitCableRecord();
            return new CircuitCableRecord
            {
                // Literal names on the call lines keep tools/check_param_contract.py able to see them.
                InstallMethod = ParameterHelpers.GetString(circuit, "ELC_CBL_INSTALL_METHOD_TXT")?.Trim(),
                Insulation = ParameterHelpers.GetString(circuit, "ELC_CBL_INS_TYPE_TXT")?.Trim(),
                CableType = ParameterHelpers.GetString(circuit, "ELC_CBL_TYPE_TXT")?.Trim(),
            };
        }

        /// <summary>Stamp the cable a size was calculated for. Returns how many of the three
        /// parameters were written (a parameter not bound to the circuit is skipped).</summary>
        public static int Write(Element circuit, string installMethod, string insulation, string cableType)
        {
            int n = 0;
            if (circuit == null) return 0;
            if (!string.IsNullOrWhiteSpace(installMethod)
                && ParameterHelpers.SetString(circuit, "ELC_CBL_INSTALL_METHOD_TXT", installMethod, overwrite: true)) n++;
            if (!string.IsNullOrWhiteSpace(insulation)
                && ParameterHelpers.SetString(circuit, "ELC_CBL_INS_TYPE_TXT", insulation, overwrite: true)) n++;
            if (!string.IsNullOrWhiteSpace(cableType)
                && ParameterHelpers.SetString(circuit, "ELC_CBL_TYPE_TXT", cableType, overwrite: true)) n++;
            return n;
        }

        /// <summary>The Appendix 4 table for this record, or null when the record is incomplete
        /// or names a combination with no shipped table.</summary>
        public Bs7671CapacityTable FindTable(Bs7671Data data, string material = "Cu")
            => IsComplete ? data?.FindTable(material, Insulation, InstallMethod, CableType) : null;

        public override string ToString() => $"{Insulation} {CableType} method {InstallMethod}";
    }
}
