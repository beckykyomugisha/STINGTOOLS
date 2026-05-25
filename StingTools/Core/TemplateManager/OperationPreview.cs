using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// One previewable item for a Template Manager op. Rendered in a checkbox
    /// DataGrid so the user can select/deselect, filter by discipline, see
    /// what already exists, and choose a per-row action before committing.
    /// </summary>
    public sealed class PreviewRow
    {
        public bool IsSelected { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Discipline { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public string Action { get; set; } = "Create";   // Create | Skip | Overwrite | Merge | Rename
        public string Source { get; set; } = string.Empty; // CSV | hardcoded | corp-library | project-overlay
        public string Detail { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;    // stable identifier for round-trip
        public int RevitElementId { get; set; }             // backing Revit element id (0 if none)
        public Dictionary<string, string> Extras { get; set; } = new();
    }

    /// <summary>
    /// The full preview envelope for a Template Manager op. Each op exposes a
    /// PreviewProvider that returns one of these so the dashboard can render
    /// a checkbox grid before the user commits.
    /// </summary>
    public sealed class OperationPreview
    {
        public string Operation { get; set; } = string.Empty;
        public string OperationLabel { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<PreviewRow> Rows { get; set; } = new();
        public List<string> AvailableDisciplines { get; set; } = new();
        public List<string> AvailableCategories { get; set; } = new();
        public List<string> AvailableSources { get; set; } = new();
        public bool SupportsScope { get; set; } = false;            // show scope picker
        public bool SupportsConflictResolution { get; set; } = true;
        public bool SupportsDryRun { get; set; } = true;
        public string Notes { get; set; } = string.Empty;

        public int SelectedCount => Rows.Count(r => r.IsSelected);
        public int ExistingCount => Rows.Count(r => r.Exists);
        public int NewCount => Rows.Count(r => !r.Exists);
    }

    /// <summary>
    /// Delegate signature for a per-op preview provider. Implementations live
    /// inside the dashboard registry so the data layer does not depend on Revit.
    /// </summary>
    public delegate OperationPreview PreviewProvider(object docOrContext);
}
