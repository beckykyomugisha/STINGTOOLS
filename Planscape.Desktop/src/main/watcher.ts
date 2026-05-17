import chokidar, { FSWatcher } from 'chokidar'
import { EventEmitter } from 'events'
import { extname, basename, relative } from 'path'
import { mainWindow } from './index'

// ── File classification ───────────────────────────────────────────────────────

export type FileCategory =
  | 'ifc'       // IFC models → StingBridge
  | 'rvt'       // Revit models
  | 'dwg'       // AutoCAD drawings
  | 'dgn'       // MicroStation drawings
  | 'pdf'       // PDF documents
  | 'xlsx'      // Excel spreadsheets
  | 'docx'      // Word documents
  | 'image'     // Images (PNG, JPG, etc.)
  | 'other'

const EXTENSION_MAP: Record<string, FileCategory> = {
  '.ifc':  'ifc',
  '.ifczip': 'ifc',
  '.rvt':  'rvt',
  '.rfa':  'rvt',
  '.dwg':  'dwg',
  '.dxf':  'dwg',
  '.dgn':  'dgn',
  '.pdf':  'pdf',
  '.xlsx': 'xlsx',
  '.xls':  'xlsx',
  '.csv':  'xlsx',
  '.docx': 'docx',
  '.doc':  'docx',
  '.png':  'image',
  '.jpg':  'image',
  '.jpeg': 'image',
  '.gif':  'image',
  '.webp': 'image'
}

function classifyFile(filePath: string): FileCategory {
  const ext = extname(filePath).toLowerCase()
  return EXTENSION_MAP[ext] ?? 'other'
}

// ── CDE folder classification ─────────────────────────────────────────────────

export type CDEState = 'WIP' | 'SHARED' | 'PUBLISHED'

function inferCDEState(filePath: string): CDEState {
  const upper = filePath.toUpperCase()
  if (upper.includes('/PUBLISHED/') || upper.includes('\\PUBLISHED\\')) return 'PUBLISHED'
  if (upper.includes('/SHARED/') || upper.includes('\\SHARED\\')) return 'SHARED'
  return 'WIP'
}

// ── FileWatcher ───────────────────────────────────────────────────────────────

export interface WatchedFile {
  absolutePath: string
  relativePath: string
  projectId: string
  localRoot: string
  category: FileCategory
  cdeState: CDEState
  filename: string
  event: 'add' | 'change' | 'unlink'
  detectedAt: string
}

export class FileWatcher extends EventEmitter {
  private watchers = new Map<string, FSWatcher>() // projectId → watcher

  /**
   * Start watching a local folder for a given project.
   * Emits 'file' events with WatchedFile payloads.
   */
  watch(projectId: string, localRoot: string): void {
    if (this.watchers.has(projectId)) {
      this.unwatch(projectId)
    }

    const watcher = chokidar.watch(localRoot, {
      ignored: [
        /(^|[/\\])\../,           // dotfiles
        /node_modules/,
        /\.git/,
        /~\$.*\.(xlsx|docx)/      // Office lock files
      ],
      persistent: true,
      ignoreInitial: false,
      awaitWriteFinish: {
        stabilityThreshold: 2000,
        pollInterval: 100
      },
      depth: 8
    })

    const emit = (event: 'add' | 'change' | 'unlink', filePath: string) => {
      const category = classifyFile(filePath)
      const file: WatchedFile = {
        absolutePath: filePath,
        relativePath: relative(localRoot, filePath),
        projectId,
        localRoot,
        category,
        cdeState: inferCDEState(filePath),
        filename: basename(filePath),
        event,
        detectedAt: new Date().toISOString()
      }

      this.emit('file', file)

      // Forward to renderer
      mainWindow?.webContents.send('watcher:file', file)
    }

    watcher
      .on('add',    p => emit('add', p))
      .on('change', p => emit('change', p))
      .on('unlink', p => emit('unlink', p))
      .on('error',  err => {
        console.error(`[Watcher] Error for project ${projectId}:`, err)
        mainWindow?.webContents.send('watcher:error', { projectId, error: String(err) })
      })
      .on('ready', () => {
        console.log(`[Watcher] Ready for project ${projectId} at ${localRoot}`)
        mainWindow?.webContents.send('watcher:ready', { projectId, localRoot })
      })

    this.watchers.set(projectId, watcher)
  }

  unwatch(projectId: string): void {
    const w = this.watchers.get(projectId)
    if (w) {
      w.close().catch(console.error)
      this.watchers.delete(projectId)
    }
  }

  unwatchAll(): void {
    for (const [id] of this.watchers) {
      this.unwatch(id)
    }
  }

  isWatching(projectId: string): boolean {
    return this.watchers.has(projectId)
  }

  watchedProjects(): string[] {
    return Array.from(this.watchers.keys())
  }
}
