namespace Planscape.Core.Entities;

/// <summary>
/// FLEX-13 — Per-project custom-field definitions for <see cref="BimIssue"/>.
///
/// Admin table-based editor (decision 4.3 = b) lets project admins add / edit /
/// remove / reorder fields. Values live on <see cref="BimIssue.CustomFields"/>
/// as a JSONB column keyed by <see cref="Key"/>.
///
/// v1 scope = Issues only (decision 4.1). Documents / Projects join later.
/// </summary>
public class IssueCustomFieldSchema : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Stable machine key (snake_case, used as the JSON object key on BimIssue.CustomFields).</summary>
    public string Key { get; set; } = "";

    /// <summary>Human-readable label shown on the form.</summary>
    public string Label { get; set; } = "";

    public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;

    /// <summary>Optional helper text under the input.</summary>
    public string? HelpText { get; set; }

    /// <summary>Default value (JSON-encoded so any type can be represented).</summary>
    public string? DefaultValueJson { get; set; }

    /// <summary>For Dropdown / MultiSelect: JSON array of option strings (or {value,label} objects).</summary>
    public string? OptionsJson { get; set; }

    public bool Required { get; set; }

    /// <summary>Admin ordering. Lower values render first. Sparse integers are fine.</summary>
    public int SortOrder { get; set; }

    /// <summary>When false, the field is hidden from new issues but preserved on existing ones.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Archive-on-delete (decision 4.6 row 1). When an admin deletes the field,
    /// we flip IsActive = false + set DeletedAt; values are preserved on existing
    /// issues until the scheduled purge (default 30 days).
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public Project? Project { get; set; }
}

/// <summary>
/// Supported field types (decision 4.2). Add a new member + UI renderer to
/// extend — storage is string/JSON so no migration is needed for new types.
/// </summary>
public enum CustomFieldType
{
    Text = 0,
    TextArea = 1,
    Number = 2,
    Date = 3,
    Dropdown = 4,
    MultiSelect = 5,
    Boolean = 6,
    UserPicker = 7,
    ElementReference = 8,
    // Skipped for v1 (decision 4.2): URL, FileAttachment.
}
