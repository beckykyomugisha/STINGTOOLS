// FILE: IEEEStandards.cs - IEEE Power Systems Standards
// Critical for electrical power distribution
// LINES: ~280 (optimized)

using System;

namespace StingBIM.Standards.IEEE
{
    public enum ProtectiveDeviceType { CircuitBreaker, Fuse, Relay }
    public enum FaultType { ThreePhase, LineToGround, LineToLine }
    
    /// <summary>
    /// IEEE Standards for Power Systems
    /// IEEE 141 (Red Book), 242 (Buff Book), 399 (Brown Book), 1584 (Arc Flash)
    /// </summary>
    public static class IEEEStandards
    {
        // IEEE 141 - ELECTRIC POWER DISTRIBUTION (RED BOOK)
        
        // Transformer sizing with diversity and future expansion
        public static double GetTransformerSize(double connectedLoadKVA, double diversityFactor = 0.75, double futureExpansion = 1.25)
        {
            return connectedLoadKVA * diversityFactor * futureExpansion; // kVA
        }
        
        // Transformer impedance (typical values)
        public static double GetTransformerImpedance(double kvaRating)
        {
            if (kvaRating <= 100) return 0.04;    // 4%
            if (kvaRating <= 500) return 0.05;    // 5%
            if (kvaRating <= 1000) return 0.055;  // 5.5%
            if (kvaRating <= 2500) return 0.06;   // 6%
            return 0.065;                          // 6.5%
        }
        
        // Voltage regulation calculation
        public static double GetVoltageRegulation(double loadCurrent, double impedanceOhms, double cableLength)
        {
            return loadCurrent * impedanceOhms * cableLength; // Volts drop
        }
        
        // Power factor correction - capacitor sizing
        public static double GetCapacitorSize(double kW, double existingPF, double desiredPF)
        {
            double existingAngle = Math.Acos(existingPF);
            double desiredAngle = Math.Acos(desiredPF);
            double kVAR = kW * (Math.Tan(existingAngle) - Math.Tan(desiredAngle));
            return kVAR; // Required capacitor size in kVAR
        }
        
        // IEEE 242 - PROTECTION AND COORDINATION (BUFF BOOK)
        
        // Short circuit current calculation (simplified)
        public static double GetShortCircuitCurrent(double systemVoltage, double totalImpedance)
        {
            // Isc = V / (√3 × Z)
            double baseKVA = 1000; // Base MVA for calculation
            double Isc = (baseKVA * 1000) / (Math.Sqrt(3) * systemVoltage * totalImpedance);
            return Isc; // Amperes
        }
        
        // Asymmetrical factor (DC component)
        public static double GetAsymmetricalFactor(double XRratio)
        {
            // Accounts for DC component in first half-cycle
            return 1.0 + Math.Exp(-Math.PI / (2 * XRratio));
        }
        
        // Circuit breaker rating selection
        public static string GetCircuitBreakerRating(double calculatedFaultCurrent)
        {
            if (calculatedFaultCurrent <= 10000) return "10 kA";
            if (calculatedFaultCurrent <= 14000) return "14 kA";
            if (calculatedFaultCurrent <= 22000) return "22 kA";
            if (calculatedFaultCurrent <= 42000) return "42 kA";
            if (calculatedFaultCurrent <= 65000) return "65 kA";
            return "80 kA+";
        }
        
        // Protective device coordination time interval
        public static double GetCoordinationTimeInterval(string upstreamDevice, string downstreamDevice)
        {
            // Recommended time intervals for selectivity
            if (upstreamDevice == "Circuit_Breaker" && downstreamDevice == "Circuit_Breaker")
                return 0.3; // 300 ms
            if (upstreamDevice == "Circuit_Breaker" && downstreamDevice == "Fuse")
                return 0.2; // 200 ms
            if (upstreamDevice == "Fuse" && downstreamDevice == "Fuse")
                return 0.15; // 150 ms
            
            return 0.3; // Default 300 ms
        }
        
        // IEEE 399 - INDUSTRIAL POWER SYSTEMS (BROWN BOOK)
        
        // Motor starting current
        public static double GetMotorStartingCurrent(double motorHP, double voltage, double efficiency = 0.85, double powerFactor = 0.85)
        {
            // Convert HP to kW
            double motorKW = motorHP * 0.746;
            
            // Full load current
            double FLA = (motorKW * 1000) / (Math.Sqrt(3) * voltage * efficiency * powerFactor);
            
            // Starting current = 6 × FLA (typical)
            return FLA * 6.0; // Amperes
        }
        
        // Voltage dip during motor starting
        public static double GetVoltageDip(double motorKVA, double systemShortCircuitMVA)
        {
            return (motorKVA / (systemShortCircuitMVA * 1000)) * 100; // Percentage
        }
        
        // IEEE 1584 - ARC FLASH HAZARD CALCULATION
        
        // Incident energy - Lee method (simplified)
        public static double GetIncidentEnergy(double faultCurrent, double clearingTime, double workingDistance)
        {
            // Simplified Lee method
            double logIbf = Math.Log10(faultCurrent / 1000.0);
            double energy = 1.5 * Math.Pow(10, logIbf - 0.0011) * clearingTime * (610 / Math.Pow(workingDistance, 1.473));
            return energy; // cal/cm²
        }
        
        // PPE category determination
        public static string GetPPECategory(double incidentEnergy)
        {
            if (incidentEnergy < 1.2) return "Category 0 (No Arc-Rated PPE Required)";
            if (incidentEnergy < 4.0) return "Category 1 (Arc rating 4 cal/cm²)";
            if (incidentEnergy < 8.0) return "Category 2 (Arc rating 8 cal/cm²)";
            if (incidentEnergy < 25.0) return "Category 3 (Arc rating 25 cal/cm²)";
            if (incidentEnergy < 40.0) return "Category 4 (Arc rating 40 cal/cm²)";
            return "Category 4+ (>40 cal/cm² - Special Protection Required)";
        }
        
        // Arc flash boundary calculation
        public static double GetArcFlashBoundary(double incidentEnergy, double workingDistance = 18)
        {
            // Distance where incident energy = 1.2 cal/cm²
            double distance = Math.Pow((incidentEnergy / 1.2), (1.0 / 1.473)) * workingDistance;
            return distance; // inches
        }
        
        // Arcing current - IEEE 1584 method
        public static double GetArcingCurrent(double faultCurrent, double voltage)
        {
            // Simplified IEEE 1584 arcing current
            double lgIa;
            
            if (voltage < 1000) // Low voltage
            {
                lgIa = Math.Log10(faultCurrent) + 0.662 * Math.Log10(faultCurrent) 
                       - 0.0966 * voltage / 1000 - 0.000526;
            }
            else // Medium voltage
            {
                lgIa = 0.00402 + 0.983 * Math.Log10(faultCurrent);
            }
            
            return Math.Pow(10, lgIa); // Amperes
        }
        
        // IEEE 519 - HARMONIC LIMITS
        
        // Total harmonic distortion (THD) limits
        public static double GetTHDLimit(string voltageLevel)
        {
            return voltageLevel switch
            {
                "LV_69kV_or_below" => 8.0,     // 8% THD
                "69kV_to_161kV" => 5.0,        // 5% THD
                "161kV_and_above" => 2.5,      // 2.5% THD
                _ => 5.0
            };
        }
        
        // IEEE 446 - EMERGENCY POWER SYSTEMS
        
        // Generator sizing for emergency loads
        public static double GetEmergencyGeneratorSize(double emergencyLoadKW, double safetyFactor = 1.25)
        {
            return emergencyLoadKW * safetyFactor; // kW
        }
        
        // Fuel consumption rate (simplified)
        public static double GetFuelConsumptionRate(double generatorKW, double loadPercentage)
        {
            // Typical diesel generator: 0.3 liters/kWh at full load
            double baseRate = 0.3; // liters per kWh
            double actualLoad = generatorKW * (loadPercentage / 100.0);
            return actualLoad * baseRate; // liters per hour
        }
        
        // IEEE 142 - GROUNDING OF INDUSTRIAL POWER SYSTEMS
        
        // Ground grid resistance (simplified)
        public static double GetGroundGridResistance(double soilResistivity, double gridArea)
        {
            // Simplified Schwarz formula
            return soilResistivity / (4 * Math.Sqrt(gridArea)); // Ohms
        }
        
        // Touch voltage limit
        public static double GetTouchVoltageLimit(double faultDuration)
        {
            // Step and touch potential limits
            return (116 + 0.7 * 1000) / Math.Sqrt(faultDuration); // Volts
        }
    }
}
