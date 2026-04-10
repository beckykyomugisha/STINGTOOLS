namespace Planscape.Shared.Constants;

/// <summary>
/// ISO 19650 code lists — shared between plugin and server for consistent validation.
/// </summary>
public static class ISO19650Codes
{
    public static readonly string[] DisciplineCodes = { "M", "E", "P", "A", "S", "FP", "LV", "G" };
    public static readonly string[] LocationCodes = { "BLD1", "BLD2", "BLD3", "EXT", "XX" };
    public static readonly string[] ZoneCodes = { "Z01", "Z02", "Z03", "Z04", "ZZ", "XX" };

    public static readonly string[] SystemCodes =
    {
        "HVAC", "DCW", "DHW", "HWS", "SAN", "RWD", "GAS", "FP", "LV",
        "FLS", "COM", "ICT", "NCL", "SEC", "ARC", "STR", "GEN"
    };

    public static readonly string[] FunctionCodes =
    {
        "SUP", "RET", "EXH", "HTG", "CLG", "VNT", "DCW", "SAN", "HWS",
        "PWR", "LTG", "DIS", "GEN", "ARC", "STR", "FP"
    };

    public static readonly string[] CDEStates =
    {
        "WIP", "SHARED", "PUBLISHED", "ARCHIVE", "SUPERSEDED", "WITHDRAWN", "OBSOLETE"
    };

    public static readonly string[] SuitabilityCodes =
    {
        "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "CR", "AB"
    };

    public static readonly Dictionary<string, int> SLAThresholdsHours = new()
    {
        ["CRITICAL"] = 4,
        ["HIGH"] = 24,
        ["MEDIUM"] = 168,    // 1 week
        ["LOW"] = 336        // 2 weeks
    };
}
