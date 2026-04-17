// FLEX-13 — Custom-field schema client.
//
// End-user callers only need fetchSchema + the CustomFieldValues map. The
// admin-only create/update/delete helpers are exported for the schema-editor
// screen (which only project admins ever see).

import { apiFetch } from "./client";
import type { CustomFieldSchema } from "@/types/customFields";

/** Fetch the active schema for a project (server filters DeletedAt/IsActive). */
export async function fetchSchema(projectId: string): Promise<CustomFieldSchema[]> {
  return apiFetch<CustomFieldSchema[]>(`/api/projects/${projectId}/custom-fields`);
}

// ── Admin-only ──────────────────────────────────────────────────────────

export interface UpsertSchemaRequest {
  key: string;
  label: string;
  fieldType: CustomFieldSchema["fieldType"];
  helpText?: string;
  defaultValueJson?: string | null;
  optionsJson?: string | null;
  required?: boolean;
  sortOrder?: number;
}

export async function createField(projectId: string, req: UpsertSchemaRequest) {
  return apiFetch<CustomFieldSchema>(`/api/projects/${projectId}/custom-fields`, {
    method: "POST",
    body: JSON.stringify(req),
  });
}

export async function updateField(projectId: string, id: string, req: UpsertSchemaRequest) {
  return apiFetch<CustomFieldSchema>(`/api/projects/${projectId}/custom-fields/${id}`, {
    method: "PUT",
    body: JSON.stringify(req),
  });
}

export async function deleteField(projectId: string, id: string) {
  return apiFetch<void>(`/api/projects/${projectId}/custom-fields/${id}`, {
    method: "DELETE",
  });
}

export async function reorderFields(
  projectId: string,
  items: { id: string; sortOrder: number }[]
) {
  return apiFetch<void>(`/api/projects/${projectId}/custom-fields/reorder`, {
    method: "POST",
    body: JSON.stringify({ items }),
  });
}
