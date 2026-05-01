// S5.1/S5.3 — types for the scene-index streaming pipeline.

export interface SceneChunkRef {
  id: string;
  discipline: string;
  levelCode?: string;
  systemCode?: string;
  url: string;          // signed URL to the chunk GLB
  hash: string;         // SHA-256 — used by the mobile cache for dedup
  sizeBytes: number;
  vertexCount: number;
  compression: 'none' | 'draco' | 'meshopt';
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;
}

export interface SceneManifest {
  projectId: string;
  generatedAt: string;
  chunks: SceneChunkRef[];

  /** Overall scene AABB across all included chunks. */
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;

  /** Disciplines covered by this manifest. */
  disciplines: string[];
}
