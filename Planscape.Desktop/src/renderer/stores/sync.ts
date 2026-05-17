import { create } from 'zustand'

export interface UploadJob {
  id: string
  filePath: string
  projectId: string
  cdeState: string
  status: 'pending' | 'uploading' | 'done' | 'error'
  retries: number
  maxRetries: number
  queuedAt: string
  error?: string
}

export interface WatcherEvent {
  absolutePath: string
  relativePath: string
  projectId: string
  localRoot: string
  category: string
  cdeState: string
  filename: string
  event: 'add' | 'change' | 'unlink'
  detectedAt: string
}

interface SyncState {
  uploadQueue: UploadJob[]
  recentEvents: WatcherEvent[]
  isSyncing: boolean
  lastSyncAt: string | null
  addUploadJob: (jobs: UploadJob | UploadJob[]) => void
  setWatcherEvent: (event: WatcherEvent) => void
  triggerSync: () => Promise<void>
  refreshQueue: () => Promise<void>
}

export const useSyncStore = create<SyncState>((set) => ({
  uploadQueue: [],
  recentEvents: [],
  isSyncing: false,
  lastSyncAt: null,

  addUploadJob: (jobs) => {
    const arr = Array.isArray(jobs) ? jobs : [jobs]
    set({ uploadQueue: arr })
  },

  setWatcherEvent: (event) => {
    set(state => ({
      recentEvents: [event, ...state.recentEvents].slice(0, 100)
    }))
  },

  triggerSync: async () => {
    set({ isSyncing: true })
    try {
      await window.electron.sync.trigger()
      const { queue } = await window.electron.sync.status()
      set({ uploadQueue: queue, lastSyncAt: new Date().toISOString(), isSyncing: false })
    } catch {
      set({ isSyncing: false })
    }
  },

  refreshQueue: async () => {
    try {
      const { queue } = await window.electron.sync.status()
      set({ uploadQueue: queue })
    } catch { /* ignore */ }
  }
}))
