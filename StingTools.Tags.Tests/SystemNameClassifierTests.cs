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
        // Revit system CLASSIFICATIONS, read when a system type's name says nothing.
        [InlineData("Fire Protection Pre-Action", "Pipes", "FP")]
        [InlineData("Fire Protection Other", "Pipes", "FP")]
        [InlineData("Other Air", "Ducts", null)]
        // Rainwater before foul drainage.
        [InlineData("Storm Drainage", "Pipes", "SWD")]           // was SAN; site storm water is SWD
        [InlineData("Roof Drain", "Pipes", "RWD")]               // was SAN
        [InlineData("Surface Water Drainage", "Pipes", "SWD")]   // was SAN
        [InlineData("Rainwater", "Pipes", "RWD")]
        [InlineData("Foul Drainage", "Pipes", "SAN")]
        // Cooling plant water and refrigerant are their own systems (were all HVAC).
        [InlineData("Chilled Water Supply", "Pipes", "CHW")]
        [InlineData("CHW Return", "Pipes", "CHW")]
        [InlineData("Condenser Water Return", "Pipes", "CDW")]
        [InlineData("Cooling Tower Water", "Pipes", "CDW")]
        [InlineData("Refrigerant Liquid", "Pipes", "REF")]
        [InlineData("VRF Gas Line", "Pipes", "REF")]
        [InlineData("CW", "Pipes", "DCW")]
        [InlineData("CW", "Mechanical Equipment", "CDW")]
        // Steam and condensate (were HWS); an A/C condensate drain is drainage.
        [InlineData("Steam Supply", "Pipes", "STM")]
        [InlineData("Steam Condensate Return", "Pipes", "CON")]
        [InlineData("Condensate Return", "Pipes", "CON")]
        [InlineData("Condensate Drain", "Pipes", "SAN")]
        // Phase 178b plumbing / process systems.
        [InlineData("Rainwater Harvesting", "Pipes", "RWH")]
        [InlineData("Greywater", "Pipes", "GWR")]
        [InlineData("Lab Water RO", "Pipes", "LBW")]
        [InlineData("Pool Circulation", "Pipes", "POL")]
        [InlineData("Irrigation", "Pipes", "IRR")]
        [InlineData("Fuel Oil", "Pipes", "FOL")]
        [InlineData("Fuel Gas", "Pipes", "GAS")]                // not fuel oil
        [InlineData("Compressed Air", "Pipes", "CMP")]
        [InlineData("Chemical Dosing", "Pipes", "CHE")]
        [InlineData("Chemical Waste Drainage", "Pipes", "SAN")] // not dosing
        [InlineData("Siphonic Roof Drainage", "Pipes", "SPH")]
        [InlineData("SuDS Attenuation", "Pipes", "SDS")]
        [InlineData("Septic Tank Outlet", "Pipes", "SEP")]
        [InlineData("Sewage Treatment Works", "Pipes", "STW")]  // not SAN
        [InlineData("Grease Interceptor", "Pipes", "INT")]
        [InlineData("Below Ground Foul", "Pipes", "BGD")]
        [InlineData("Hose Reel", "Pipes", null)]                 // a fire hose reel is not irrigation
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

        [Theory]
        [InlineData("Chiller 500kW", "Mechanical Equipment", "CHW")]
        [InlineData("Cooling Tower", "Mechanical Equipment", "CDW")]
        [InlineData("VRF Outdoor Unit", "Mechanical Equipment", "REF")]
        [InlineData("Grease Trap", "Plumbing Fixtures", "INT")]
        [InlineData("Septic Tank", "Plumbing Equipment", "SEP")]
        [InlineData("Air Compressor", "Mechanical Equipment", "CMP")]
        [InlineData("Medical Air Compressor", "Mechanical Equipment", "MGS")] // medical gas before compressed air
        public void Plant_family_system(string family, string cat, string sys)
        {
            _ = cat;
            Assert.Equal(sys, SystemNameClassifier.FromFamilyName(family));
        }

        [Theory]
        [InlineData("DHW Secondary Return", "RTN")]
        [InlineData("DHW Circulation", "RTN")]
        [InlineData("Chilled Water Flow", "SUP")]
        [InlineData("Hydronic Supply", "SUP")]
        [InlineData("Domestic Hot Water", null)]
        [InlineData("LTHW Return", "RTN")]                   // heating return (HWS)
        public void Flow_direction(string name, string dir)
            => Assert.Equal(dir, SystemNameClassifier.FlowDirection(name));

        [Theory]
        [InlineData("Refrigerant Liquid", "LIQ")]
        [InlineData("Refrigerant Suction", "SUC")]
        [InlineData("VRF Gas Line", "SUC")]
        [InlineData("Hot Gas Discharge", "HGS")]
        [InlineData("Refrigerant", null)]
        public void Refrigerant_line(string name, string func)
            => Assert.Equal(func, SystemNameClassifier.RefrigerantFunction(name));

        [Theory]
        [InlineData("Lighting Fixtures", "Recessed LED Panel", "LTG")]
        [InlineData("Lighting Fixtures", "Emergency Bulkhead 3h", "EMG")]
        [InlineData("Lighting Fixtures", "Exit Sign", "EMG")]
        [InlineData("Lighting Devices", "Switch 1G", "LTG")]
        [InlineData("Electrical Fixtures", "Twin Socket 13A", "SML")]
        [InlineData("Electrical Equipment", "Distribution Board", null)]  // stays PWR
        public void Lv_function(string category, string family, string func)
            => Assert.Equal(func, SystemNameClassifier.LvFunction(category, family));
    }
}
