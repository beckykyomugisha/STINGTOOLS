import { createReadStream, statSync } from 'fs'
import { basename } from 'path'
import { EventEmitter } from 'events'
import type Store from 'electron-store'
import { mainWindow } from './index'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface UploadJob {
  id: string
  filePath: string
  projectId: string
  cdeState: 'WIP' | 'SHARED' | 'PUBLISHED'
  documentType?: string
  retries: number
  maxRetries: number
  queuedAt: string
  status: 'pending' | 'uploading' | 'done' | 'error'
  error?: string
}

export interface UploadProgress {
  jobId: string
  filename: string
  bytesUploaded: number
  totalBytes: number
  percent: number
}

// ── UploadQueue ───────────────────────────────────────────────────────────────

const MAX_CONCURRENT = 2
const BASE_RETRY_DELAY_MS = 5_000
const MAX_FILE_SIZE_BYTES = 500 * 1024 * 1024 // 500 MB

export class UploadQueue extends EventEmitter {
  private queue: UploadJob[] = []
  private active = new Set<string>()
  private running = false
  private timer?: ReturnType<typeof setInterval>
  private jobCounter = 0

  constructor(private store: Store) {
    super()
  }

  // ── Public API ─────────────────────────────────────────────────────────────

  enqueue(
    filePath: string,
    projectId: string,
    cdeState: 'WIP' | 'SHARED' | 'PUBLISHED' = 'WIP',
    documentType?: string
  ): string {
    // Deduplicate: skip if already queued for this path
    const existing = this.queue.find(j => j.filePath === filePath && j.status === 'pending')
    if (existing) return existing.id

    const id = `upload-${Date.now()}-${++this.jobCounter}`
    const job: UploadJob = {
      id,
      filePath,
      projectId,
      cdeState,
      documentType,
      retries: 0,
      maxRetries: 3,
      queuedAt: new Date().toISOString(),
      status: 'pending'
    }
    this.queue.push(job)
    this.notifyRenderer()
    this.pump()
    return id
  }

  /** Immediately drain the queue (called from tray "Sync Now") */
  flushAll(): void {
    this.pump()
  }

  queueSize(): number {
    return this.queue.filter(j => j.status === 'pending').length
  }

  getQueue(): UploadJob[] {
    return [...this.queue]
  }

  start(): void {
    this.running = true
    // Re-try errored jobs every 60 seconds
    this.timer = setInterval(() => {
      const errored = this.queue.filter(j => j.status === 'error' && j.retries < j.maxRetries)
      for (const job of errored) {
        job.status = 'pending'
      }
      if (errored.length > 0) this.pump()
    }, 60_000)
  }

  stop(): void {
    this.running = false
    if (this.timer) clearInterval(this.timer)
  }

  // ── Internal ──────────────────────────────────────────────────────────────

  private pump(): void {
    while (this.active.size < MAX_CONCURRENT) {
      const job = this.queue.find(j => j.status === 'pending' && !this.active.has(j.id))
      if (!job) break
      this.processJob(job)
    }
  }

  private async processJob(job: UploadJob): Promise<void> {
    this.active.add(job.id)
    job.status = 'uploading'
    this.notifyRenderer()

    try {
      await this.upload(job)
      job.status = 'done'
      this.emit('done', job)
    } catch (err: unknown) {
      job.retries++
      job.status = job.retries >= job.maxRetries ? 'error' : 'pending'
      job.error = err instanceof Error ? err.message : String(err)
      console.error(`[UploadQueue] Job ${job.id} failed (attempt ${job.retries}):`, job.error)
    } finally {
      this.active.delete(job.id)
      // Clean up completed jobs older than 60 s
      const cutoff = Date.now() - 60_000
      this.queue = this.queue.filter(
        j => !(j.status === 'done' && new Date(j.queuedAt).getTime() < cutoff)
      )
      this.notifyRenderer()
      this.pump()
    }
  }

  private async upload(job: UploadJob): Promise<void> {
    const serverUrl: string = (this.store as any).get('serverUrl', 'http://localhost:5000')
    const token: string = (this.store as any).get('accessToken', '')

    if (!token) throw new Error('Not authenticated — upload skipped')

    // Check file exists and size
    let fileSize: number
    try {
      const stat = statSync(job.filePath)
      fileSize = stat.size
    } catch {
      throw new Error(`File not found: ${job.filePath}`)
    }
    if (fileSize > MAX_FILE_SIZE_BYTES) {
      throw new Error(`File too large (${Math.round(fileSize / 1024 / 1024)} MB > 500 MB limit)`)
    }

    const filename = basename(job.filePath)
    const url = `${serverUrl}/api/projects/${job.projectId}/documents`

    // Multipart form upload using Node fetch
    const { FormData, Blob } = await import('node:buffer') as any
    const formData = new FormData()
    formData.append('file', new Blob([require('fs').readFileSync(job.filePath)]), filename)
    formData.append('cdeState', job.cdeState)
    if (job.documentType) formData.append('documentType', job.documentType)

    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'X-Client-Type': 'desktop'
      },
      body: formData
    })

    if (!response.ok) {
      const text = await response.text().catch(() => response.statusText)
      throw new Error(`Server returned ${response.status}: ${text}`)
    }
  }

  private notifyRenderer(): void {
    mainWindow?.webContents.send('upload:queue', this.getQueue())
  }
}
