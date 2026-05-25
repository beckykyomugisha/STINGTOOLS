using System;
using System.Collections.Generic;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Severity of an OperationResult. Drives RAG colour + icon in the dashboard.
    /// </summary>
    public enum ResultSeverity
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// A single tabular row in an OperationResult.Sections grid.
    /// Generic shape so every command can use the same renderer.
    /// </summary>
    public sealed class ResultRow
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;   // "Created", "Skipped", "Failed", "Drifted", etc.
        public string Discipline { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public long? RevitElementId { get; set; }            // optional — enables "Select in Revit"
        public Dictionary<string, string> Extras { get; set; } = new();
    }

    /// <summary>
    /// A named section of an OperationResult. Sections render as a metric tile +
    /// optional DataGrid in the dashboard's right pane. Each section can carry
    /// its own headline counts and tabular rows.
    /// </summary>
    public sealed class ResultSection
    {
        public string Name { get; set; } = string.Empty;
        public string Headline { get; set; } = string.Empty;   // big number / RAG % / count
        public string SubHeadline { get; set; } = string.Empty;
        public ResultSeverity Severity { get; set; } = ResultSeverity.Info;
        public List<ResultRow> Rows { get; set; } = new();
        public List<(string label, string value)> Metrics { get; set; } = new();
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// The full result envelope for one Template Manager operation. Replaces the
    /// TaskDialog.Show(report.ToString()) pattern that every command used to
    /// terminate with. Commands publish; the dashboard subscribes via
    /// OperationResultBus and renders the sections inline.
    /// </summary>
    public sealed class OperationResult
    {
        public string Operation { get; set; } = string.Empty;     // matches OperationTag from the dashboard
        public string OperationLabel { get; set; } = string.Empty;
        public ResultSeverity Severity { get; set; } = ResultSeverity.Info;
        public string Headline { get; set; } = string.Empty;
        public string SubHeadline { get; set; } = string.Empty;
        public List<ResultSection> Sections { get; set; } = new();
        public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
        public double DurationMs { get; set; }
        public string DocumentPath { get; set; } = string.Empty;
        public Dictionary<string, string> Counters { get; set; } = new();
        public string UserName { get; set; } = string.Empty;       // for audit

        /// <summary>Helper to add a quick text-only section.</summary>
        public ResultSection AddSection(string name, string headline = "", ResultSeverity sev = ResultSeverity.Info)
        {
            var s = new ResultSection { Name = name, Headline = headline, Severity = sev };
            Sections.Add(s);
            return s;
        }
    }
}
