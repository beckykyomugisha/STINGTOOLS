// S5.5 — client wrapper for /api/projects/{id}/scene
import { apiFetch } from './client';
import type { SceneManifest } from '@/types/scenes';

export async function fetchSceneManifest(projectId: string, disciplines?: string[]): Promise<SceneManifest> {
  const qs = disciplines && disciplines.length > 0 ? `?disciplines=${disciplines.join(',')}` : '';
  return apiFetch<SceneManifest>(`/api/v1/projects/${projectId}/scene${qs}`);
}
