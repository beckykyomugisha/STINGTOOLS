namespace Planscape.API;

/// <summary>
/// Single source of truth for IFC element type → discipline mappings.
/// Used by both DocumentsController (display labels) and IfcBoqSeedJob (STING codes).
/// </summary>
public static class IfcDisciplineMapper
{
    // Full mapping: IFC type prefix → (stingCode, displayLabel)
    private static readonly Dictionary<string, (string Code, string Label)> _map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["IfcWall"]                      = ("A",   "Architectural"),
            ["IfcSlab"]                      = ("A",   "Architectural"),
            ["IfcDoor"]                      = ("A",   "Architectural"),
            ["IfcWindow"]                    = ("A",   "Architectural"),
            ["IfcRoof"]                      = ("A",   "Architectural"),
            ["IfcStair"]                     = ("A",   "Architectural"),
            ["IfcRamp"]                      = ("A",   "Architectural"),
            ["IfcRailing"]                   = ("A",   "Architectural"),
            ["IfcCurtainWall"]               = ("A",   "Architectural"),
            ["IfcCovering"]                  = ("A",   "Architectural"),
            ["IfcPlate"]                     = ("A",   "Architectural"),
            ["IfcColumn"]                    = ("S",   "Structural"),
            ["IfcBeam"]                      = ("S",   "Structural"),
            ["IfcFooting"]                   = ("S",   "Structural"),
            ["IfcPile"]                      = ("S",   "Structural"),
            ["IfcMember"]                    = ("S",   "Structural"),
            ["IfcRetainingWall"]             = ("S",   "Structural"),
            ["IfcDuctSegment"]               = ("M",   "Mechanical"),
            ["IfcDuctFitting"]               = ("M",   "Mechanical"),
            ["IfcAirTerminal"]               = ("M",   "Mechanical"),
            ["IfcUnitaryEquipment"]          = ("M",   "Mechanical"),
            ["IfcDamper"]                    = ("M",   "Mechanical"),
            ["IfcCoil"]                      = ("M",   "Mechanical"),
            ["IfcFan"]                       = ("M",   "Mechanical"),
            ["IfcHVACProduct"]               = ("M",   "Mechanical"),
            ["IfcPipeSegment"]               = ("P",   "Plumbing"),
            ["IfcPipeFitting"]               = ("P",   "Plumbing"),
            ["IfcFlowTerminal"]              = ("P",   "Plumbing"),
            ["IfcSanitaryTerminal"]          = ("P",   "Plumbing"),
            ["IfcPump"]                      = ("P",   "Plumbing"),
            ["IfcValve"]                     = ("P",   "Plumbing"),
            ["IfcTank"]                      = ("P",   "Plumbing"),
            ["IfcDrain"]                     = ("P",   "Plumbing"),
            ["IfcCableSegment"]              = ("E",   "Electrical"),
            ["IfcElectricAppliance"]         = ("E",   "Electrical"),
            ["IfcLightFixture"]              = ("E",   "Electrical"),
            ["IfcDistributionBoard"]         = ("E",   "Electrical"),
            ["IfcCableFitting"]              = ("E",   "Electrical"),
            ["IfcJunctionBox"]               = ("E",   "Electrical"),
            ["IfcElectricMotor"]             = ("E",   "Electrical"),
            ["IfcOutlet"]                    = ("E",   "Electrical"),
            ["IfcSwitchingDevice"]           = ("E",   "Electrical"),
            ["IfcFireSuppressionTerminal"]   = ("FP",  "Fire Protection"),
            ["IfcSprinkler"]                 = ("FP",  "Fire Protection"),
            ["IfcFireAlarm"]                 = ("FP",  "Fire Protection"),
            ["IfcSensor"]                    = ("LV",  "Low Voltage"),
            ["IfcAlarm"]                     = ("LV",  "Low Voltage"),
            ["IfcCommunicationsAppliance"]   = ("LV",  "Low Voltage"),
        };

    /// <summary>Returns the STING discipline code (A/M/P/E/S/FP/LV/GEN) for an IFC type name.</summary>
    public static string ToStingCode(string ifcType)
    {
        if (string.IsNullOrEmpty(ifcType)) return "GEN";
        foreach (var kv in _map)
            if (ifcType.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value.Code;
        return "GEN";
    }

    /// <summary>Returns the human-readable discipline label for an IFC type name.</summary>
    public static string ToDisplayLabel(string ifcType)
    {
        if (string.IsNullOrEmpty(ifcType)) return "General";
        foreach (var kv in _map)
            if (ifcType.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value.Label;
        return "General";
    }
}
