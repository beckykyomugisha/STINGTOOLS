// StingTools v4 MVP — validation result record.
//
// Shared shape returned by all five v4 validators (connectivity,
// fill, spec, termination, slope). RunAllValidatorsCommand merges
// their outputs into a single WarningsManager-backed report.

using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public enum ValidationSeverity
    {
        Info    = 0,
        Warning = 1,
        Error   = 2
    }

    /// <summary>
    /// One validation observation. Code is a short stable identifier
    /// used to group findings in the warnings report (e.g. "FILL.OVER",
    /// "SPEC.MAT.MISMATCH", "SLOPE.SAN.UNDER").
    /// </summary>
    public class ValidationResult
    {
        public ElementId ElementId { get; set; }
        public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;
        public string Code     { get; set; } = "";
        public string Message  { get; set; } = "";
        public string Validator{ get; set; } = "";

        public ValidationResult() { }
        public ValidationResult(ElementId id, ValidationSeverity sev, string code, string msg, string validator)
        {
            ElementId = id;
            Severity  = sev;
            Code      = code;
            Message   = msg;
            Validator = validator;
        }

        public override string ToString()
            => $"[{Severity}] {Code}  Element {ElementId?.Value} — {Message}  ({Validator})";
    }
}
