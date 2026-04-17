// FLEX-13 — Shared types for custom-field schema + values.

export type CustomFieldType =
  | "Text"
  | "TextArea"
  | "Number"
  | "Date"
  | "Dropdown"
  | "MultiSelect"
  | "Boolean"
  | "UserPicker"
  | "ElementReference";

export interface CustomFieldSchema {
  id: string;
  key: string;
  label: string;
  fieldType: CustomFieldType;
  helpText?: string;
  /** JSON-encoded default — parse before use. */
  defaultValueJson?: string | null;
  /** JSON-encoded options (Dropdown / MultiSelect). */
  optionsJson?: string | null;
  required: boolean;
  sortOrder: number;
}

/** Runtime value map — what actually lives on BimIssue.CustomFields. */
export type CustomFieldValues = Record<string, unknown>;

/** Utility: parse a schema's options JSON into a normalised string[] or {value,label}[]. */
export function parseOptions(
  optionsJson: string | null | undefined
): { value: string; label: string }[] {
  if (!optionsJson) return [];
  try {
    const parsed = JSON.parse(optionsJson);
    if (!Array.isArray(parsed)) return [];
    return parsed.map((o) =>
      typeof o === "string"
        ? { value: o, label: o }
        : { value: String(o.value ?? o), label: String(o.label ?? o.value ?? o) }
    );
  } catch {
    return [];
  }
}

/** Returns the first validation error or null if the values satisfy the schema. */
export function validateAgainstSchema(
  schemas: CustomFieldSchema[],
  values: CustomFieldValues
): { field: string; error: string } | null {
  for (const s of schemas) {
    const v = values[s.key];
    if (s.required && (v === undefined || v === null || v === "" ||
        (Array.isArray(v) && v.length === 0))) {
      return { field: s.key, error: `${s.label} is required` };
    }
    if (v == null || v === "") continue;
    if (s.fieldType === "Number" && typeof v !== "number" && isNaN(Number(v))) {
      return { field: s.key, error: `${s.label} must be a number` };
    }
    if (s.fieldType === "Date" && typeof v === "string" && isNaN(Date.parse(v))) {
      return { field: s.key, error: `${s.label} must be a date` };
    }
  }
  return null;
}
