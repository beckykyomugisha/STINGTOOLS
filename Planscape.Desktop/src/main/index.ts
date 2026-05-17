import {
  app,
  BrowserWindow,
  ipcMain,
  dialog,
  shell,
  nativeTheme
} from 'electron'
import { join } from 'path'
import { autoUpdater } from 'electron-updater'
import Store from 'electron-store'
import { setupTray } from './tray'
import { registerIpcHandlers } from './ipc'
import { FileWatcher } from './watcher'
import { UploadQueue } from './uploader'

// ── Global state ──────────────────────────────────────────────────────────────
const store = new Store<{
  serverUrl: string
  accessToken: string
  refreshToken: string
  windowBounds: { width: number; height: number; x?: number; y?: number }
  watchedFolders: Array<{ projectId: string; localPath: string }>
  bridgePath: string
  syncIntervalMs: number
}>({
  defaults: {
    serverUrl: 'http://localhost:5000',
    accessToken: '',
    refreshToken: '',
    windowBounds: { width: 1280, height: 800 },
    watchedFolders: [],
    bridgePath: '',
    syncIntervalMs: 300_000
  }
})

export let mainWindow: BrowserWindow | null = null
export const fileWatcher = new FileWatcher()
export const uploadQueue = new UploadQueue(store)

// ── Window creation ───────────────────────────────────────────────────────────
function createWindow(): void {
  const bounds = store.get('windowBounds')

  mainWindow = new BrowserWindow({
    width: bounds.width,
    height: bounds.height,
    x: bounds.x,
    y: bounds.y,
    minWidth: 900,
    minHeight: 600,
    titleBarStyle: process.platform === 'darwin' ? 'hiddenInset' : 'default',
    backgroundColor: '#1a1a2e',
    show: false,
    icon: join(__dirname, '../../resources/icon.png'),
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  })

  // Load app
  if (process.env.ELECTRON_RENDERER_URL) {
    mainWindow.loadURL(process.env.ELECTRON_RENDERER_URL)
  } else {
    mainWindow.loadFile(join(__dirname, '../../dist/renderer/index.html'))
  }

  // Show when ready to avoid flash
  mainWindow.once('ready-to-show', () => {
    mainWindow!.show()
    if (process.env.NODE_ENV === 'development') {
      mainWindow!.webContents.openDevTools({ mode: 'detach' })
    }
  })

  // Save window bounds on close
  mainWindow.on('close', () => {
    if (mainWindow) {
      const b = mainWindow.getBounds()
      store.set('windowBounds', { width: b.width, height: b.height, x: b.x, y: b.y })
    }
  })

  mainWindow.on('closed', () => { mainWindow = null })

  // Open external links in default browser
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://') || url.startsWith('http://')) {
      shell.openExternal(url)
    }
    return { action: 'deny' }
  })
}

// ── App lifecycle ─────────────────────────────────────────────────────────────
app.whenReady().then(async () => {
  nativeTheme.themeSource = 'dark'

  // Re-create any watched folders from persisted config
  const watchedFolders = store.get('watchedFolders')
  for (const { projectId, localPath } of watchedFolders) {
    fileWatcher.watch(projectId, localPath)
  }

  // Start upload worker
  uploadQueue.start()

  createWindow()
  setupTray(store, mainWindow)
  registerIpcHandlers(store, fileWatcher, uploadQueue)

  // macOS re-activate
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
    else mainWindow?.show()
  })

  // Auto-updater (no-op in dev)
  if (process.env.NODE_ENV !== 'development') {
    autoUpdater.checkForUpdatesAndNotify()
  }
})

app.on('window-all-closed', () => {
  // On macOS keep running in tray
  if (process.platform !== 'darwin') app.quit()
})

app.on('before-quit', () => {
  fileWatcher.unwatchAll()
  uploadQueue.stop()
})

// ── Auto-updater events ───────────────────────────────────────────────────────
autoUpdater.on('update-available', () => {
  mainWindow?.webContents.send('update:available')
})

autoUpdater.on('update-downloaded', () => {
  mainWindow?.webContents.send('update:downloaded')
})

// Allow renderer to install update and restart
ipcMain.handle('update:install', () => {
  autoUpdater.quitAndInstall()
})
