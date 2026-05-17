import { contextBridge, ipcRenderer } from 'electron'

// ── Type definitions for the exposed API ─────────────────────────────────────

export interface IElectronAPI {
  // Folder operations
  folder: {
    select: () => Promise<string | null>
    watch: (projectId: string, localPath: string) => Promise<{ ok: boolean }>
    unwatch: (projectId: string) => Promise<{ ok: boolean }>
    watched: () => Promise<Array<{ projectId: string; localPath: string }>>
    tree: (rootPath: string) => Promise<import('../main/folderMirror').FolderNode[]>
    mirror: {
      create: (localRoot: string, projectCode: string, projectId: string) => Promise<{ ok: boolean; projectRoot: string }>
      mappings: () => Promise<import('../main/folderMirror').MirrorMapping[]>
    }
  }

  // Sync / upload
  sync: {
    status: () => Promise<{ queueSize: number; queue: import('../main/uploader').UploadJob[] }>
    trigger: () => Promise<{ ok: boolean }>
    enqueue: (filePath: string, projectId: string, cdeState: string, documentType?: string) => Promise<{ jobId: string }>
  }

  // StingBridge
  bridge: {
    run: (ifcPath: string, projectId: string) => Promise<{ ok: boolean; result?: import('../main/bridge').BridgeResult; error?: string }>
    cancel: (jobId: string) => Promise<{ ok: boolean }>
    status: () => Promise<{ activeJobs: number }>
  }

  // Electron Store (via IPC for security)
  store: {
    get: <T = unknown>(key: string) => Promise<T>
    set: (key: string, value: unknown) => Promise<{ ok: boolean }>
    delete: (key: string) => Promise<{ ok: boolean }>
  }

  // App info
  app: {
    version: () => Promise<string>
  }

  // Window controls
  window: {
    minimize: () => Promise<void>
    maximize: () => Promise<void>
    close: () => Promise<void>
  }

  // Event listeners
  on: (channel: string, listener: (...args: unknown[]) => void) => void
  off: (channel: string, listener: (...args: unknown[]) => void) => void
}

// ── Safe IPC channels the renderer may receive ────────────────────────────────
const ALLOWED_RECEIVE_CHANNELS = [
  'watcher:file',
  'watcher:error',
  'watcher:ready',
  'upload:queue',
  'bridge:progress',
  'bridge:result',
  'mirror:created',
  'sync:triggered',
  'update:available',
  'update:downloaded'
] as const

// ── Context bridge ────────────────────────────────────────────────────────────

contextBridge.exposeInMainWorld('electron', {
  folder: {
    select:  ()                        => ipcRenderer.invoke('folder:select'),
    watch:   (pid: string, lp: string) => ipcRenderer.invoke('folder:watch', pid, lp),
    unwatch: (pid: string)             => ipcRenderer.invoke('folder:unwatch', pid),
    watched: ()                        => ipcRenderer.invoke('folder:watched'),
    tree:    (root: string)            => ipcRenderer.invoke('folder:tree', root),
    mirror: {
      create:   (lr: string, pc: string, pid: string) =>
                  ipcRenderer.invoke('folder:mirror:create', lr, pc, pid),
      mappings: () => ipcRenderer.invoke('folder:mirror:mappings')
    }
  },

  sync: {
    status:  ()                                                          => ipcRenderer.invoke('sync:status'),
    trigger: ()                                                          => ipcRenderer.invoke('sync:trigger'),
    enqueue: (fp: string, pid: string, state: string, dt?: string)      => ipcRenderer.invoke('sync:enqueue', fp, pid, state, dt)
  },

  bridge: {
    run:    (ifc: string, pid: string) => ipcRenderer.invoke('bridge:run', ifc, pid),
    cancel: (jobId: string)            => ipcRenderer.invoke('bridge:cancel', jobId),
    status: ()                         => ipcRenderer.invoke('bridge:status')
  },

  store: {
    get:    (key: string)               => ipcRenderer.invoke('store:get', key),
    set:    (key: string, val: unknown) => ipcRenderer.invoke('store:set', key, val),
    delete: (key: string)               => ipcRenderer.invoke('store:delete', key)
  },

  app: {
    version: () => ipcRenderer.invoke('app:version')
  },

  window: {
    minimize: () => ipcRenderer.invoke('window:minimize'),
    maximize: () => ipcRenderer.invoke('window:maximize'),
    close:    () => ipcRenderer.invoke('window:close')
  },

  on: (channel: string, listener: (...args: unknown[]) => void) => {
    if (ALLOWED_RECEIVE_CHANNELS.includes(channel as any)) {
      ipcRenderer.on(channel, (_event, ...args) => listener(...args))
    }
  },

  off: (channel: string, listener: (...args: unknown[]) => void) => {
    if (ALLOWED_RECEIVE_CHANNELS.includes(channel as any)) {
      ipcRenderer.removeListener(channel, listener as any)
    }
  }
} satisfies IElectronAPI)
