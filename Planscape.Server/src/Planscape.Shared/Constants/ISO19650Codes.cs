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

    /// <summary>ISO 19650 suitability codes: S0 (WIP), S1–S7 (shared), A1–A5 / B1–B6
    /// (authorised / partial sign-off — published), CR (as-constructed record),
    /// AB (abandoned / superseded), AR (archive). Same set as the plugin's
    /// Iso19650Vocabulary; the A and B codes were missing, so the server rejected
    /// every authorised document.</summary>
    public static readonly string[] SuitabilityCodes =
    {
        "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7",
        "A1", "A2", "A3", "A4", "A5",
        "B1", "B2", "B3", "B4", "B5", "B6",
        "CR", "AB", "AR"
    };

    public static readonly Dictionary<string, int> SLAThresholdsHours = new()
    {
        ["CRITICAL"] = 4,
        ["HIGH"] = 24,
        ["MEDIUM"] = 168,    // 1 week
        ["LOW"] = 336        // 2 weeks
    };
}
