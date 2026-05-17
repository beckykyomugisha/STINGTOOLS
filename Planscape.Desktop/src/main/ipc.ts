import { ipcMain, dialog } from 'electron'
import type Store from 'electron-store'
import type { FileWatcher } from './watcher'
import type { UploadQueue } from './uploader'
import { stingBridge } from './bridge'
import { folderMirror } from './folderMirror'
import { mainWindow } from './index'

export function registerIpcHandlers(
  store: Store,
  fileWatcher: FileWatcher,
  uploadQueue: UploadQueue
): void {

  // ── Folder operations ────────────────────────────────────────────────────

  ipcMain.handle('folder:select', async () => {
    const result = await dialog.showOpenDialog({
      properties: ['openDirectory'],
      title: 'Select Project Folder'
    })
    return result.canceled ? null : result.filePaths[0]
  })

  ipcMain.handle('folder:watch', (_event, projectId: string, localPath: string) => {
    fileWatcher.watch(projectId, localPath)
    const watched: Array<{ projectId: string; localPath: string }> =
      (store as any).get('watchedFolders', [])
    if (!watched.find(w => w.projectId === projectId)) {
      watched.push({ projectId, localPath })
      ;(store as any).set('watchedFolders', watched)
    }
    return { ok: true }
  })

  ipcMain.handle('folder:unwatch', (_event, projectId: string) => {
    fileWatcher.unwatch(projectId)
    const watched: Array<{ projectId: string; localPath: string }> =
      (store as any).get('watchedFolders', [])
    ;(store as any).set('watchedFolders', watched.filter(w => w.projectId !== projectId))
    return { ok: true }
  })

  ipcMain.handle('folder:watched', () => {
    return (store as any).get('watchedFolders', [])
  })

  ipcMain.handle('folder:tree', (_event, rootPath: string) => {
    return folderMirror.buildTree(rootPath)
  })

  // ── ISO 19650 mirror ─────────────────────────────────────────────────────

  ipcMain.handle(
    'folder:mirror:create',
    (_event, localRoot: string, projectCode: string, projectId: string) => {
      const created = folderMirror.createStructure(localRoot, projectCode)
      folderMirror.register(projectId, created, projectCode)
      return { ok: true, projectRoot: created }
    }
  )

  ipcMain.handle('folder:mirror:mappings', () => folderMirror.allMappings())

  // ── Sync / upload ────────────────────────────────────────────────────────

  ipcMain.handle('sync:status', () => ({
    queueSize: uploadQueue.queueSize(),
    queue: uploadQueue.getQueue()
  }))

  ipcMain.handle('sync:trigger', () => {
    uploadQueue.flushAll()
    return { ok: true }
  })

  ipcMain.handle(
    'sync:enqueue',
    (_event, filePath: string, projectId: string, cdeState: string, documentType?: string) => {
      const id = uploadQueue.enqueue(filePath, projectId, cdeState as any, documentType)
      return { jobId: id }
    }
  )

  // ── StingBridge ──────────────────────────────────────────────────────────

  ipcMain.handle('bridge:run', async (_event, ifcPath: string, projectId: string) => {
    const bridgePath: string = (store as any).get('bridgePath', '')
    if (!bridgePath) {
      return { ok: false, error: 'StingBridge path not configured. Set it in Settings.' }
    }
    const result = await stingBridge.run({ ifcPath, projectId, bridgePath })
    return { ok: result.success, result }
  })

  ipcMain.handle('bridge:cancel', (_event, jobId: string) => {
    stingBridge.cancel(jobId)
    return { ok: true }
  })

  ipcMain.handle('bridge:status', () => ({
    activeJobs: stingBridge.activeJobCount()
  }))

  // ── Store / settings ─────────────────────────────────────────────────────

  ipcMain.handle('store:get', (_event, key: string) => {
    return (store as any).get(key)
  })

  ipcMain.handle('store:set', (_event, key: string, value: unknown) => {
    ;(store as any).set(key, value)
    return { ok: true }
  })

  ipcMain.handle('store:delete', (_event, key: string) => {
    ;(store as any).delete(key)
    return { ok: true }
  })

  // ── App info ─────────────────────────────────────────────────────────────

  ipcMain.handle('app:version', () => {
    return require('../../package.json').version
  })

  // ── Window controls (for custom titlebar on Windows) ─────────────────────

  ipcMain.handle('window:minimize', () => { mainWindow?.minimize() })
  ipcMain.handle('window:maximize', () => {
    if (mainWindow?.isMaximized()) mainWindow.unmaximize()
    else mainWindow?.maximize()
  })
  ipcMain.handle('window:close', () => { mainWindow?.close() })
}
