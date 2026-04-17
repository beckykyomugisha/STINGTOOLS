// MODEL-VIEWER — shared types.

export type ModelFormat = "Glb" | "Gltf" | "Ifc" | "Rvt" | "Obj" | "Fbx";

export interface ModelMeta {
  id: string;
  projectId: string;
  name: string;
  description?: string | null;
  discipline?: string | null;
  fileName: string;
  format: ModelFormat;
  fileSizeBytes: number;
  contentHash?: string | null;
  hasElementMap: boolean;
  hasThumbnail: boolean;
  elementCount?: number | null;
  units: string;
  revision?: string | null;
  boundsMinX?: number | null;
  boundsMinY?: number | null;
  boundsMinZ?: number | null;
  boundsMaxX?: number | null;
  boundsMaxY?: number | null;
  boundsMaxZ?: number | null;
  uploadedBy: string;
  uploadedAt: string;
}

/** Optional sidecar exported by the Revit plugin. Keys are element GUIDs. */
export type ElementMap = Record<
  string,
  {
    tag?: string;
    name?: string;
    category?: string;
    discipline?: string;
    level?: string;
  }
>;

export interface ModelPin {
  id: string;
  x: number;
  y: number;
  z: number;
  priority?: "CRITICAL" | "HIGH" | "MEDIUM" | "LOW";
}

export type ViewerTool = "pick" | "measure" | "section" | "pin";
