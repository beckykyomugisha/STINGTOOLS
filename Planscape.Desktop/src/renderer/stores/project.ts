import { create } from 'zustand'
import { projects as projectsApi, type ProjectDto } from '../api/endpoints'

interface ProjectState {
  projects: ProjectDto[]
  activeProjectId: string | null
  isLoading: boolean
  error: string | null
  fetchProjects: () => Promise<void>
  setActiveProject: (id: string) => void
  getActiveProject: () => ProjectDto | null
}

export const useProjectStore = create<ProjectState>((set, get) => ({
  projects: [],
  activeProjectId: null,
  isLoading: false,
  error: null,

  fetchProjects: async () => {
    set({ isLoading: true, error: null })
    try {
      const data = await projectsApi.list()
      set({ projects: data, isLoading: false })
    } catch (err) {
      set({ error: err instanceof Error ? err.message : 'Failed to load projects', isLoading: false })
    }
  },

  setActiveProject: (id) => set({ activeProjectId: id }),

  getActiveProject: () => {
    const { projects, activeProjectId } = get()
    return projects.find(p => p.id === activeProjectId) ?? null
  }
}))
