import { Tray, Menu, app, nativeImage } from 'electron'
import { join } from 'path'
import type Store from 'electron-store'
import type { BrowserWindow } from 'electron'
import { uploadQueue } from './index'

let tray: Tray | null = null

// Sync status label shown in tray tooltip
let currentStatus = 'Idle'

export function setupTray(store: Store, win: BrowserWindow | null): void {
  const iconPath = join(__dirname, '../../resources/tray-icon.png')
  const icon = nativeImage.createFromPath(iconPath).isEmpty()
    ? nativeImage.createEmpty()
    : nativeImage.createFromPath(iconPath)

  tray = new Tray(icon)
  tray.setToolTip(`Planscape Desktop — ${currentStatus}`)

  updateMenu(store, win)

  // Double-click shows window
  tray.on('double-click', () => {
    if (win) {
      win.show()
      win.focus()
    }
  })
}

export function updateTrayStatus(status: string): void {
  currentStatus = status
  if (tray) {
    tray.setToolTip(`Planscape Desktop — ${status}`)
    // Update the menu label that shows status
    updateMenu(null, null)
  }
}

function updateMenu(store: Store | null, win: BrowserWindow | null): void {
  if (!tray) return

  const queueSize = uploadQueue ? uploadQueue.queueSize() : 0
  const statusLabel = queueSize > 0
    ? `Syncing: ${queueSize} file(s) queued`
    : `Status: ${currentStatus}`

  const contextMenu = Menu.buildFromTemplate([
    {
      label: 'Planscape Desktop',
      enabled: false,
      icon: nativeImage.createEmpty()
    },
    { type: 'separator' },
    {
      label: statusLabel,
      enabled: false
    },
    { type: 'separator' },
    {
      label: 'Open Planscape',
      click: () => {
        if (win) {
          win.show()
          win.focus()
        }
      }
    },
    {
      label: 'Sync Now',
      click: () => {
        uploadQueue?.flushAll()
        win?.webContents.send('sync:triggered')
      }
    },
    { type: 'separator' },
    {
      label: 'Quit Planscape',
      click: () => app.quit()
    }
  ])

  tray.setContextMenu(contextMenu)
}

export function getTray(): Tray | null {
  return tray
}
