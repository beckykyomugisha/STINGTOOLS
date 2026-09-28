// The MEP system name → SYS code mapping, tested by behaviour. Until 2026-09-28 it
// lived in Revit-bound TagConfig and was checked by reading the order of its return
// statements, which is how the three cases marked below survived.

using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SystemNameClassifierTests
    {
        [Theory]
        // Revit's default piping and duct system types.
        [InlineData("Hydronic Supply", "Pipes", "HWS")]          // was no match → category default DCW
        [InlineData("Hydronic Return", "Pipes", "HWS")]          // was no match → DCW
        [InlineData("Domestic Cold Water", "Pipes", "DCW")]
        [InlineData("Domestic Hot Water", "Pipes", "DHW")]
        [InlineData("Sanitary", "Pipes", "SAN")]
        [InlineData("Vent", "Pipes", "SAN")]
        [InlineData("Fire Protection Wet", "Pipes", "FP")]
        [InlineData("Fire Protection Dry", "Pipes", "FP")]
        [InlineData("Supply Air", "Ducts", "HVAC")]
        [InlineData("Return Air", "Ducts", "HVAC")]
        [InlineData("Exhaust Air", "Ducts", "HVAC")]
        [InlineData("Other", "Pipes", null)]
        // Rainwater before foul drainage.
        [InlineData("Storm Drainage", "Pipes", "RWD")]           // was SAN
        [InlineData("Roof Drain", "Pipes", "RWD")]               // was SAN
        [InlineData("Surface Water Drainage", "Pipes", "RWD")]   // was SAN
        [InlineData("Foul Drainage", "Pipes", "SAN")]
        // Cooling water is HVAC; heating water is HWS.
        [InlineData("Chilled Water Supply", "Pipes", "HVAC")]
        [InlineData("Condenser Water Return", "Pipes", "HVAC")]
        [InlineData("CW", "Pipes", "DCW")]
        [InlineData("CW", "Mechanical Equipment", "HVAC")]
        [InlineData("LTHW Flow", "Pipes", "HWS")]
        [InlineData("Natural Gas", "Pipes", "GAS")]
        [InlineData("LPG", "Pipes", "GAS")]
        // Medical gas is its own system.
        [InlineData("Medical Gas O2", "Pipes", "MGS")]           // was GAS (natural gas)
        [InlineData("Oxygen", "Pipes", "MGS")]                   // was no match → DCW
        [InlineData("Medical Vacuum", "Pipes", "MGS")]           // was no match → DCW
        [InlineData("Medical Air 4 bar", "Pipes", "MGS")]
        [InlineData("Nitrous Oxide", "Pipes", "MGS")]
        [InlineData("AGSS", "Pipes", "MGS")]
        [InlineData("MGPS Zone 2", "Pipes", "MGS")]
        public void System_name(string name, string category, string expected)
            => Assert.Equal(expected, SystemNameClassifier.FromSystemName(name, category));

        [Theory]
        // Ambiguous words that are NOT medical gas on their own.
        [InlineData("Compressed Air", "Pipes")]
        [InlineData("Vacuum Drainage", "Pipes")]
        [InlineData("CO2 Suppression", "Pipes")]
        [InlineData("Anaesthetic Room Extract", "Mechanical Equipment")]
        [InlineData("Surgical Suite Supply Air", "Mechanical Equipment")]
        // Ductwork never carries medical gas.
        [InlineData("Medical Air", "Ducts")]
        public void Not_medical_gas(string name, string category)
            => Assert.NotEqual(SystemNameClassifier.MedicalGasSys, SystemNameClassifier.FromSystemName(name, category));

        [Theory]
        [InlineData("Medical Oxygen", "O2")]
        [InlineData("Medical Air 400 kPa", "MA4")]
        [InlineData("Surgical Air 700", "MA7")]
        [InlineData("Medical Vacuum", "VAC")]
        [InlineData("Nitrous Oxide", "N2O")]
        [InlineData("Entonox", "N2O")]
        [InlineData("Anaesthetic Gas Scavenging", "AGS")]
        [InlineData("AGSS", "AGS")]
        [InlineData("Medical Nitrogen", "N2")]
        [InlineData("Heliox", "HE")]
        [InlineData("MGPS Zone 2", null)]   // medical gas, gas not named
        public void Gas_is_read_into_the_gas_vocabulary(string name, string gas)
        {
            Assert.True(SystemNameClassifier.TryMedicalGas(name, out string g));
            Assert.Equal(gas, g);
            if (g != null) Assert.Contains(g, StingTools.Core.Plumbing.MedicalGasFixtures.GasCodes);
        }

        [Theory]
        [InlineData("HV Switchgear Panel", "HV")]
        [InlineData("11kV Ring Main Unit", "HV")]
        [InlineData("Transformer 1000kVA", "HV")]
        [InlineData("HV/LV Transformer", "HV")]
        [InlineData("Isolating Transformer IPS", null)]    // medical IPS transformer is LV
        [InlineData("Control Transformer", null)]
        [InlineData("CT Chamber", null)]                   // a current transformer, not a CT scanner
        [InlineData("BMS Outstation", "BMS")]
        [InlineData("DDC Controller", "BMS")]
        [InlineData("BACnet Room Sensor", "BMS")]
        [InlineData("Lead Lined Partition", "RAD")]
        [InlineData("X-Ray Room Door", "RAD")]
        [InlineData("MRI Scanner Magnet", "RAD")]
        [InlineData("CT Scanner", "RAD")]
        [InlineData("Linac Bunker Wall", "RAD")]
        [InlineData("LV Distribution Board", null)]
        [InlineData("Basic Wall", null)]
        public void Family_name_system(string family, string sys)
            => Assert.Equal(sys, SystemNameClassifier.FromFamilyName(family));

        [Theory]
        [InlineData("HV", "Transformer 1000kVA", "TRF")]
        [InlineData("HV", "HV Switchgear Panel", "PWR")]
        [InlineData("BMS", "BMS Temperature Sensor", "SNS")]
        [InlineData("BMS", "BMS Damper Actuator", "CTL")]
        [InlineData("BMS", "DDC Controller", "FCT")]
        [InlineData("BMS", "BMS Energy Meter", "MON")]
        [InlineData("BMS", "BMS Panel", null)]
        [InlineData("RAD", "Lead Lined Partition", "SHD")]
        [InlineData("RAD", "MRI 5 Gauss Fence", "ZNE")]
        [InlineData("RAD", "Radiation Monitor", "MON")]
        [InlineData("RAD", "X-Ray Unit", null)]
        public void Family_name_function(string sys, string family, string func)
            => Assert.Equal(func, SystemNameClassifier.FunctionFromName(sys, family));
    }
}
