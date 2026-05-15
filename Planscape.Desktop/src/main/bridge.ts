import { spawn, ChildProcess } from 'child_process'
import { EventEmitter } from 'events'
import { mainWindow } from './index'

export interface BridgeRunOptions {
  ifcPath: string
  projectId: string
  /** Absolute path to the StingBridge package root, e.g. /home/user/STINGTOOLS/StingBridge */
  bridgePath: string
  /** Additional CLI arguments */
  extraArgs?: string[]
}

export interface BridgeProgress {
  jobId: string
  stage: string
  percent: number
  message: string
}

export interface BridgeResult {
  jobId: string
  success: boolean
  outputFiles: string[]
  error?: string
  durationMs: number
}

// ── StingBridge spawner ───────────────────────────────────────────────────────

export class StingBridge extends EventEmitter {
  private activeJobs = new Map<string, ChildProcess>()
  private jobCounter = 0

  /**
   * Spawns: python -m StingBridge.bridge sync --ifc <path> [--project <id>]
   * Streams stdout lines as BridgeProgress events.
   * Returns a BridgeResult when the process exits.
   */
  run(opts: BridgeRunOptions): Promise<BridgeResult> {
    const jobId = `bridge-${Date.now()}-${++this.jobCounter}`
    const startMs = Date.now()

    return new Promise((resolve) => {
      const args = [
        '-m', 'StingBridge.bridge',
        'sync',
        '--ifc', opts.ifcPath,
        '--project', opts.projectId,
        '--json-progress', // ask bridge to emit JSON lines for progress
        ...(opts.extraArgs ?? [])
      ]

      const proc = spawn('python', args, {
        cwd: opts.bridgePath,
        env: { ...process.env },
        stdio: ['ignore', 'pipe', 'pipe']
      })

      this.activeJobs.set(jobId, proc)

      const outputFiles: string[] = []
      let errorOutput = ''

      // ── stdout: JSON progress lines ──────────────────────────────────────
      proc.stdout?.on('data', (chunk: Buffer) => {
        const lines = chunk.toString().split('\n').filter(Boolean)
        for (const line of lines) {
          try {
            const parsed = JSON.parse(line)

            if (parsed.type === 'progress') {
              const progress: BridgeProgress = {
                jobId,
                stage: parsed.stage ?? 'Processing',
                percent: parsed.percent ?? 0,
                message: parsed.message ?? ''
              }
              this.emit('progress', progress)
              mainWindow?.webContents.send('bridge:progress', progress)
            } else if (parsed.type === 'output_file') {
              outputFiles.push(parsed.path)
            }
          } catch {
            // Non-JSON line — treat as raw log
            const progress: BridgeProgress = {
              jobId,
              stage: 'Processing',
              percent: -1,
              message: line
            }
            this.emit('progress', progress)
            mainWindow?.webContents.send('bridge:progress', progress)
          }
        }
      })

      // ── stderr ──────────────────────────────────────────────────────────
      proc.stderr?.on('data', (chunk: Buffer) => {
        errorOutput += chunk.toString()
      })

      // ── exit ─────────────────────────────────────────────────────────────
      proc.on('close', (code) => {
        this.activeJobs.delete(jobId)
        const result: BridgeResult = {
          jobId,
          success: code === 0,
          outputFiles,
          error: code !== 0 ? errorOutput.trim() || `Process exited with code ${code}` : undefined,
          durationMs: Date.now() - startMs
        }
        this.emit('result', result)
        mainWindow?.webContents.send('bridge:result', result)
        resolve(result)
      })

      proc.on('error', (err) => {
        this.activeJobs.delete(jobId)
        const result: BridgeResult = {
          jobId,
          success: false,
          outputFiles: [],
          error: err.message,
          durationMs: Date.now() - startMs
        }
        this.emit('result', result)
        mainWindow?.webContents.send('bridge:result', result)
        resolve(result)
      })
    })
  }

  /** Cancel a running job by jobId */
  cancel(jobId: string): void {
    const proc = this.activeJobs.get(jobId)
    if (proc) {
      proc.kill('SIGTERM')
      this.activeJobs.delete(jobId)
    }
  }

  /** Cancel all running jobs */
  cancelAll(): void {
    for (const [id] of this.activeJobs) {
      this.cancel(id)
    }
  }

  isRunning(jobId: string): boolean {
    return this.activeJobs.has(jobId)
  }

  activeJobCount(): number {
    return this.activeJobs.size
  }
}

// Singleton
export const stingBridge = new StingBridge()
