// Shared "active project" store. Dashboard + Issues + Models all need to
// know which project is currently selected. Introduced for the model viewer
// but intentionally generic — existing screens should migrate to this store
// instead of keeping their own `useState<Project | null>`.

import { create } from "zustand";
import AsyncStorage from "@react-native-async-storage/async-storage";

const ACTIVE_PROJECT_KEY = 'planscape_active_project';

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
  /** A7 — restore active project from AsyncStorage on cold start. */
  hydrate: () => Promise<void>;
}

export const useProjectStore = create<ProjectState>((set, get) => ({
  activeProjectId: null,
  active: null,
  recent: [],

  setActive: (project) => {
    if (!project) {
      set({ activeProjectId: null, active: null });
      // A7 — clear persisted active project on explicit null set.
      AsyncStorage.removeItem(ACTIVE_PROJECT_KEY).catch(() => {});
      return;
    }
    set({ activeProjectId: project.id, active: project });
    // A7 — persist so cold starts restore the last selected project.
    AsyncStorage.setItem(ACTIVE_PROJECT_KEY, JSON.stringify(project)).catch(() => {});
    // Remember last 5 distinct projects the user touched.
    const existing = get().recent.filter((p) => p.id !== project.id);
    set({ recent: [project, ...existing].slice(0, 5) });
  },

  pushRecent: (project) => {
    const existing = get().recent.filter((p) => p.id !== project.id);
    set({ recent: [project, ...existing].slice(0, 5) });
  },

  clear: () => {
    set({ activeProjectId: null, active: null, recent: [] });
    // A7 — wipe persisted active project on logout / tenant switch.
    AsyncStorage.removeItem(ACTIVE_PROJECT_KEY).catch(() => {});
  },

  hydrate: async () => {
    try {
      const raw = await AsyncStorage.getItem(ACTIVE_PROJECT_KEY);
      if (!raw) return;
      const project: ProjectSummary = JSON.parse(raw);
      // Only restore if the store doesn't already have an active project
      // (avoid overwriting a project set by the dashboard during the same session).
      if (!get().active) {
        set({ activeProjectId: project.id, active: project });
      }
    } catch {
      // Non-fatal — a fresh session will pick the default project from listProjects.
    }
  },
}));
