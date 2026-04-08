namespace Planscape.Shared.Helpers;

/// <summary>
/// Shared tag format utilities used by both plugin and server.
/// </summary>
public static class TagFormatHelper
{
    /// <summary>
    /// Validate an 8-segment ISO 19650 tag format.
    /// </summary>
    public static bool IsValidTag(string tag, string separator = "-")
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var segments = tag.Split(separator);
        return segments.Length == 8 && segments.All(s => !string.IsNullOrEmpty(s));
    }

    /// <summary>
    /// Check if a tag is fully resolved (no placeholder tokens).
    /// </summary>
    public static bool IsFullyResolved(string tag, string separator = "-")
    {
        if (!IsValidTag(tag, separator)) return false;
        var segments = tag.Split(separator);
        string[] placeholders = { "XX", "ZZ", "GEN", "0000", "UNK" };
        return !segments.Any(s => placeholders.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Parse a tag into its 8 segments.
    /// </summary>
    public static (string disc, string loc, string zone, string lvl, string sys, string func, string prod, string seq)?
        ParseTag(string tag, string separator = "-")
    {
        if (!IsValidTag(tag, separator)) return null;
        var s = tag.Split(separator);
        return (s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7]);
    }

    /// <summary>
    /// Compute RAG status from compliance percentage.
    /// </summary>
    public static string GetRagStatus(double percent) =>
        percent >= 80 ? "GREEN" : percent >= 50 ? "AMBER" : "RED";
}
