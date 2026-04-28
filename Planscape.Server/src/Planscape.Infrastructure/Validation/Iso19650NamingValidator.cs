namespace Planscape.Infrastructure.Validation;

/// <summary>
/// Phase 143 — Server-side ISO 19650 file-name validator.
///
/// Mirrors <c>BIMManagerEngine.ValidateDocumentName</c> from the Revit
/// plugin so the office dashboard, mobile uploads and the plugin all
/// enforce the same rules. The naming convention is the standard UK
/// 2021 NA layout:
///
///     Project-Originator-Volume-Level-Type-Role-Class-Number
///
///     Example:  PRJ-ABC-ZZ-01-DR-A-Zz_99-0001
///
/// The validator is intentionally lenient on segments 3 (Volume) and
/// 4 (Level) — projects use bespoke codes there. We only hard-fail on
/// missing fields, malformed Project / Originator codes, and unknown
/// Type / Role codes (the segments where central enforcement matters).
///
/// Callers can declare a document as exempt by passing
/// <see cref="ValidateOptions.AllowAnyExtension"/> when uploading a
/// non-deliverable file (photos, transmittal cover, audit log) — the
/// validator is then a no-op.
/// </summary>
public static class Iso19650NamingValidator
{
    /// <summary>BS EN ISO 19650 (UK 2021 NA) document type codes.</summary>
    public static readonly IReadOnlyDictionary<string, string> DocumentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AF"] = "Animation / Fly-through",
            ["BQ"] = "Bill of Quantities",
            ["CA"] = "Calculations",
            ["CM"] = "Combined Model (Federated)",
            ["CP"] = "Cost Plan",
            ["CR"] = "Correspondence",
            ["DB"] = "Database",
            ["DR"] = "Drawing (2D)",
            ["FN"] = "File Note",
            ["HS"] = "Health and Safety",
            ["IE"] = "Information Exchange (COBie)",
            ["M2"] = "2D Model",
            ["M3"] = "3D Model",
            ["MI"] = "Minutes / Action Notes",
            ["MO"] = "Model (2021 NA)",
            ["MR"] = "Model-derived Report",
            ["MS"] = "Method Statement",
            ["PP"] = "Presentation",
            ["PR"] = "Programme",
            ["RD"] = "Room Data Sheet",
            ["RI"] = "Request for Information",
            ["RP"] = "Report",
            ["SA"] = "Schedule of Accommodation",
            ["SH"] = "Schedule",
            ["SK"] = "Sketch",
            ["SN"] = "Snagging List",
            ["SP"] = "Specification",
            ["SU"] = "Survey",
            ["TN"] = "Technical Note",
            ["VS"] = "Visualisation",
        };

    /// <summary>BS 1192 / ISO 19650 originator role codes.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoleCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "Architect",
            ["B"] = "Building Surveyor",
            ["C"] = "Civil Engineer",
            ["D"] = "Drainage/Hydraulic Engineer",
            ["E"] = "Electrical Engineer",
            ["F"] = "Facilities Manager",
            ["G"] = "Geotechnical Engineer",
            ["H"] = "Heating/HVAC Engineer",
            ["I"] = "Interior Designer",
            ["K"] = "Client/Employer",
            ["L"] = "Landscape Architect",
            ["M"] = "Mechanical Engineer",
            ["P"] = "Public Health Engineer",
            ["Q"] = "Quantity Surveyor/Cost Manager",
            ["S"] = "Structural Engineer",
            ["T"] = "Town Planner",
            ["W"] = "Contractor",
            ["X"] = "Subcontractor",
            ["Z"] = "General/Non-disciplinary",
        };

    public sealed record ValidateOptions(
        bool RequireFullPattern = true,
        bool TolerateExtension = true);

    public sealed record ValidationResult(
        bool IsValid,
        string Pattern,
        IReadOnlyList<string> Errors)
    {
        public string Joined => string.Join("; ", Errors);
    }

    /// <summary>
    /// Validate a file name (with or without extension) against the ISO
    /// 19650 / UK 2021 NA pattern. Returns a structured result with the
    /// detected segment errors so the upload controller can surface them
    /// to the user one-by-one rather than as a single string.
    /// </summary>
    public static ValidationResult Validate(string fileName, ValidateOptions? options = null)
    {
        options ??= new ValidateOptions();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(fileName))
            return new ValidationResult(false, ExpectedPattern,
                new[] { "File name is empty" });

        var name = fileName.Trim();

        // Drop the extension before splitting — designers commonly upload as
        // PRJ-ABC-ZZ-01-DR-A-Zz_99-0001.dwg or .pdf.
        if (options.TolerateExtension)
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot > 0 && lastDot > name.LastIndexOf('-'))
                name = name[..lastDot];
        }

        var parts = name.Split('-');
        if (parts.Length < 6)
        {
            errors.Add($"Expected 6–8 hyphen-separated fields; got {parts.Length}");
            return new ValidationResult(false, ExpectedPattern, errors);
        }

        // Segment 1 — Project. 2–6 alphanumerics is the practical span on
        // UK projects. Codes containing whitespace are always wrong.
        if (parts[0].Length < 2 || parts[0].Length > 6 || ContainsWhitespace(parts[0]))
            errors.Add($"Project code should be 2–6 chars (no spaces): '{parts[0]}'");

        // Segment 2 — Originator. 1–6 alphanumerics; org abbreviations.
        if (parts[1].Length < 1 || parts[1].Length > 6 || ContainsWhitespace(parts[1]))
            errors.Add($"Originator code should be 1–6 chars (no spaces): '{parts[1]}'");

        // Segments 3, 4 — Volume + Level. Project-bespoke; we don't enforce.
        // Validation here would be a constant source of false positives.

        // Segment 5 — Document type. Hard-fail on unknown so the central
        // controlled vocabulary stays controlled. Non-deliverables can be
        // declared with options.RequireFullPattern=false.
        if (options.RequireFullPattern
            && !DocumentTypes.ContainsKey(parts[4]))
        {
            var hint = string.Join(", ", DocumentTypes.Keys.OrderBy(k => k).Take(10));
            errors.Add($"Unknown document type '{parts[4]}'. Common: {hint}, …");
        }

        // Segment 6 — Role. Same logic.
        if (options.RequireFullPattern
            && !RoleCodes.ContainsKey(parts[5]))
        {
            var hint = string.Join(", ", RoleCodes.Keys.OrderBy(k => k));
            errors.Add($"Unknown originator role '{parts[5]}'. Valid: {hint}");
        }

        // Segment 8 (when present) — sequence number. Reject obvious
        // malformations (whitespace, embedded dot/slash).
        if (parts.Length >= 8 && ContainsForbiddenChars(parts[7]))
            errors.Add($"Sequence number contains forbidden characters: '{parts[7]}'");

        return new ValidationResult(errors.Count == 0, ExpectedPattern, errors);
    }

    public const string ExpectedPattern =
        "Project-Originator-Volume-Level-Type-Role-Class-Number  (e.g. PRJ-ABC-ZZ-01-DR-A-Zz_99-0001)";

    private static bool ContainsWhitespace(string s) =>
        s.Any(char.IsWhiteSpace);

    private static bool ContainsForbiddenChars(string s) =>
        s.Any(c => char.IsWhiteSpace(c) || c == '/' || c == '\\' || c == ':' || c == '*');
}
