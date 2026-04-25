// Shared "active project" store. Dashboard + Issues + Models all need to
// know which project is currently selected. Introduced for the model viewer
// but intentionally generic — existing screens should migrate to this store
// instead of keeping their own `useState<Project | null>`.

import { create } from "zustand";

export interface ProjectSummary {
  id: string;
  name: string;
  code: string;
  tenantId?: string;
}

interface ProjectState {
  activeProjectId: string | null;
  active: ProjectSummary | null;
  recent: ProjectSummary[];

  setActive: (project: ProjectSummary | null) => void;
  pushRecent: (project: ProjectSummary) => void;
  clear: () => void;
}

export const useProjectStore = create<ProjectState>((set, get) => ({
  activeProjectId: null,
  active: null,
  recent: [],

  setActive: (project) => {
    if (!project) { set({ activeProjectId: null, active: null }); return; }
    set({ activeProjectId: project.id, active: project });
    // Remember last 5 distinct projects the user touched.
    const existing = get().recent.filter((p) => p.id !== project.id);
    set({ recent: [project, ...existing].slice(0, 5) });
  },

  pushRecent: (project) => {
    const existing = get().recent.filter((p) => p.id !== project.id);
    set({ recent: [project, ...existing].slice(0, 5) });
  },

  clear: () => set({ activeProjectId: null, active: null, recent: [] }),
}));
