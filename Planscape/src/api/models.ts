// MODEL-VIEWER — client for /api/projects/{id}/models endpoints.

import { apiFetch, getBaseUrl, getToken } from "./client";
import type { ModelMeta } from "@/types/models";

export async function listModels(projectId: string): Promise<ModelMeta[]> {
  return apiFetch<ModelMeta[]>(`/api/projects/${projectId}/models`);
}

export async function getModel(projectId: string, modelId: string): Promise<ModelMeta> {
  return apiFetch<ModelMeta>(`/api/projects/${projectId}/models/${modelId}`);
}

/** URL (not a fetch) — pass into the WebView viewer with an Authorization header. */
export async function modelFileUrl(projectId: string, modelId: string): Promise<string> {
  const base = await getBaseUrl();
  return `${base}/api/projects/${projectId}/models/${modelId}/file`;
}

export async function modelThumbnailUrl(projectId: string, modelId: string): Promise<string> {
  const base = await getBaseUrl();
  return `${base}/api/projects/${projectId}/models/${modelId}/thumbnail`;
}

export async function fetchElementMap(
  projectId: string,
  modelId: string
): Promise<Record<string, unknown>> {
  return apiFetch<Record<string, unknown>>(
    `/api/projects/${projectId}/models/${modelId}/element-map`
  );
}

export async function deleteModel(projectId: string, modelId: string): Promise<void> {
  return apiFetch<void>(`/api/projects/${projectId}/models/${modelId}`, { method: "DELETE" });
}

/**
 * Upload a glTF / GLB / IFC file. Element map + thumbnail are optional sidecars.
 * Uses `fetch` directly (not `apiFetch`) because we send multipart, not JSON.
 */
export async function uploadModel(
  projectId: string,
  files: {
    file: { uri: string; name: string; type?: string };
    elementMap?: { uri: string; name: string; type?: string };
    thumbnail?: { uri: string; name: string; type?: string };
  },
  meta: Partial<{
    name: string;
    description: string;
    discipline: string;
    elementCount: number;
    units: string;
    revision: string;
    boundsMinX: number; boundsMinY: number; boundsMinZ: number;
    boundsMaxX: number; boundsMaxY: number; boundsMaxZ: number;
  }>
): Promise<ModelMeta> {
  const base = await getBaseUrl();
  const token = await getToken();
  const form = new FormData();

  form.append("File", {
    uri: files.file.uri,
    name: files.file.name,
    type: files.file.type ?? "application/octet-stream",
  } as unknown as Blob);

  if (files.elementMap) {
    form.append("ElementMap", {
      uri: files.elementMap.uri,
      name: files.elementMap.name,
      type: files.elementMap.type ?? "application/json",
    } as unknown as Blob);
  }
  if (files.thumbnail) {
    form.append("Thumbnail", {
      uri: files.thumbnail.uri,
      name: files.thumbnail.name,
      type: files.thumbnail.type ?? "image/png",
    } as unknown as Blob);
  }
  for (const [k, v] of Object.entries(meta)) {
    if (v != null) form.append(k.charAt(0).toUpperCase() + k.slice(1), String(v));
  }

  const res = await fetch(`${base}/api/projects/${projectId}/models`, {
    method: "POST",
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form as unknown as BodyInit,
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`uploadModel failed: ${res.status} ${body}`);
  }
  return res.json();
}

export async function fetchHeatmap(projectId: string): Promise<{
  elements: Array<{ guid: string; disc: string; isComplete: boolean; missingTokens: string[] }>;
}> {
  const { apiClient } = await import("./client");
  return apiClient.get(`/api/projects/${projectId}/models/heatmap`);
}
