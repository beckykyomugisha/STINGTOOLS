import { mkdirSync, existsSync, readdirSync, statSync } from 'fs'
import { join, relative } from 'path'
import { EventEmitter } from 'events'
import { mainWindow } from './index'

// ── ISO 19650 folder template ─────────────────────────────────────────────────

const ISO19650_STRUCTURE = [
  '00_MANAGEMENT/BEP',
  '00_MANAGEMENT/MIDP',
  '00_MANAGEMENT/MEETINGS',
  '01_ARCHITECTURE/WIP',
  '01_ARCHITECTURE/SHARED',
  '01_ARCHITECTURE/PUBLISHED',
  '02_STRUCTURAL/WIP',
  '02_STRUCTURAL/SHARED',
  '02_STRUCTURAL/PUBLISHED',
  '03_MEP/WIP',
  '03_MEP/SHARED',
  '03_MEP/PUBLISHED',
  '04_COORDINATION/CLASH',
  '04_COORDINATION/FEDERATED',
  '04_COORDINATION/IFC_DROP',
  '05_HANDOVER/COBie',
  '05_HANDOVER/O&M'
]

export interface FolderNode {
  name: string
  path: string
  relativePath: string
  isDirectory: boolean
  children?: FolderNode[]
  fileCount?: number
}

export interface MirrorMapping {
  projectId: string
  localRoot: string
  projectCode: string
  createdAt: string
}

// ── FolderMirror ──────────────────────────────────────────────────────────────

export class FolderMirror extends EventEmitter {
  private mappings = new Map<string, MirrorMapping>()

  /**
   * Create the ISO 19650 folder structure under `localRoot/{projectCode}/`
   */
  createStructure(localRoot: string, projectCode: string): string {
    const projectRoot = join(localRoot, projectCode)
    for (const subPath of ISO19650_STRUCTURE) {
      const full = join(projectRoot, subPath)
      if (!existsSync(full)) {
        mkdirSync(full, { recursive: true })
      }
    }
    console.log(`[FolderMirror] Created ISO 19650 structure at ${projectRoot}`)
    mainWindow?.webContents.send('mirror:created', { projectRoot, projectCode })
    return projectRoot
  }

  /**
   * Register a mapping between a project and a local root folder.
   */
  register(projectId: string, localRoot: string, projectCode: string): void {
    const mapping: MirrorMapping = {
      projectId,
      localRoot,
      projectCode,
      createdAt: new Date().toISOString()
    }
    this.mappings.set(projectId, mapping)
    this.emit('registered', mapping)
  }

  unregister(projectId: string): void {
    this.mappings.delete(projectId)
  }

  getMapping(projectId: string): MirrorMapping | undefined {
    return this.mappings.get(projectId)
  }

  allMappings(): MirrorMapping[] {
    return Array.from(this.mappings.values())
  }

  /**
   * Build a tree representation of a folder for display in the UI.
   */
  buildTree(rootPath: string, maxDepth = 4, currentDepth = 0): FolderNode[] {
    if (!existsSync(rootPath) || currentDepth > maxDepth) return []

    try {
      const entries = readdirSync(rootPath)
      return entries.map(name => {
        const full = join(rootPath, name)
        const rel = relative(rootPath, full)
        try {
          const stat = statSync(full)
          if (stat.isDirectory()) {
            const children = this.buildTree(full, maxDepth, currentDepth + 1)
            const fileCount = children.filter(c => !c.isDirectory).length
            return {
              name,
              path: full,
              relativePath: rel,
              isDirectory: true,
              children,
              fileCount
            }
          } else {
            return {
              name,
              path: full,
              relativePath: rel,
              isDirectory: false
            }
          }
        } catch {
          return { name, path: full, relativePath: rel, isDirectory: false }
        }
      }).sort((a, b) => {
        // Directories first
        if (a.isDirectory && !b.isDirectory) return -1
        if (!a.isDirectory && b.isDirectory) return 1
        return a.name.localeCompare(b.name)
      })
    } catch {
      return []
    }
  }

  /**
   * Determine the correct local folder path for a document based on its
   * CDE state and document type.
   */
  resolveLocalPath(
    projectId: string,
    filename: string,
    cdeState: 'WIP' | 'SHARED' | 'PUBLISHED',
    discipline: 'ARCH' | 'STRUCT' | 'MEP' | 'COORD' | 'MGMT' | 'HANDOVER' = 'ARCH'
  ): string | null {
    const mapping = this.mappings.get(projectId)
    if (!mapping) return null

    const disciplineFolder: Record<string, string> = {
      ARCH:    '01_ARCHITECTURE',
      STRUCT:  '02_STRUCTURAL',
      MEP:     '03_MEP',
      COORD:   '04_COORDINATION',
      MGMT:    '00_MANAGEMENT',
      HANDOVER:'05_HANDOVER'
    }

    const base = disciplineFolder[discipline] ?? '01_ARCHITECTURE'
    return join(mapping.localRoot, mapping.projectCode, base, cdeState)
  }
}

export const folderMirror = new FolderMirror()
