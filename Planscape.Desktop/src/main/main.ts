// Planscape Desktop — Electron main process.
//
// Responsibilities:
//   • Create and manage the BrowserWindow (renderer host).
//   • Register IPC handlers that the renderer calls for:
//       - File system access (ifc_drop folder, local IFC/Excel files).
//       - Native notifications (system tray, OS toast).
//       - Auto-update checks via electron-updater.
//   • Forward deep-link URLs (planscape://) to the renderer router.

import { app, BrowserWindow, ipcMain, shell, Notification } from 'electron';
import { autoUpdater }  from 'electron-updater';
import Store            from 'electron-store';
import * as path        from 'path';
import * as fs          from 'fs';

const store = new Store();
const isDev = !app.isPackaged;

let mainWindow: BrowserWindow | null = null;

function createWindow() {
  mainWindow = new BrowserWindow({
    width:  1280,
    height: 820,
    minWidth:  900,
    minHeight: 600,
    title: 'Planscape',
    webPreferences: {
      preload:          path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration:  false,
    },
  });

  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
    mainWindow.webContents.openDevTools();
  } else {
    mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'));
  }
}

app.whenReady().then(() => {
  createWindow();
  if (!isDev) autoUpdater.checkForUpdatesAndNotify();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// ── IPC: ifc_drop folder operations ─────────────────────────────────────────

ipcMain.handle('ifc:listPending', async (_e, dropFolder: string) => {
  const processingDir = path.join(dropFolder, 'processing');
  if (!fs.existsSync(processingDir)) return [];
  return fs.readdirSync(processingDir).filter(f => f.endsWith('.ifc'));
});

ipcMain.handle('ifc:openFolder', async (_e, folderPath: string) => {
  shell.openPath(folderPath);
});

// ── IPC: settings ────────────────────────────────────────────────────────────

ipcMain.handle('settings:get', (_e, key: string) => store.get(key));
ipcMain.handle('settings:set', (_e, key: string, value: unknown) => store.set(key, value));

// ── IPC: OS notifications ────────────────────────────────────────────────────

ipcMain.handle('notify', (_e, title: string, body: string) => {
  new Notification({ title, body }).show();
});
