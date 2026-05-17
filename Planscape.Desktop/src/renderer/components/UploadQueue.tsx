import React from 'react'
import { useSyncStore } from '../stores/sync'
import type { UploadJob } from '../stores/sync'

function statusBadge(job: UploadJob): React.ReactElement {
  const map: Record<string, { label: string; cls: string }> = {
    pending:   { label: 'Queued',    cls: 'bg-ps-blue/20 text-ps-blue' },
    uploading: { label: 'Uploading', cls: 'bg-ps-amber/20 text-ps-amber animate-pulse' },
    done:      { label: 'Done',      cls: 'bg-ps-green/20 text-ps-green' },
    error:     { label: 'Error',     cls: 'bg-ps-red/20 text-ps-red' }
  }
  const s = map[job.status] ?? map.pending
  return <span className={`badge text-xs px-2 py-0.5 rounded ${s.cls}`}>{s.label}</span>
}

function fileName(path: string): string {
  return path.split(/[/\\]/).pop() ?? path
}

export default function UploadQueuePanel(): React.ReactElement {
  const { uploadQueue } = useSyncStore()

  const active = uploadQueue.filter(j => j.status !== 'done')

  if (active.length === 0) {
    return (
      <div className="text-ps-muted text-xs text-center py-6">
        No uploads in progress
      </div>
    )
  }

  return (
    <div className="space-y-1.5">
      {active.map(job => (
        <div key={job.id} className="flex items-center gap-2 p-2 rounded bg-ps-elevated text-xs">
          <div className="flex-1 min-w-0">
            <div className="truncate text-ps-text font-medium">{fileName(job.filePath)}</div>
            <div className="text-ps-muted truncate">{job.cdeState}</div>
            {job.error && (
              <div className="text-ps-red mt-0.5 truncate">{job.error}</div>
            )}
          </div>
          {statusBadge(job)}
          {job.retries > 0 && (
            <span className="text-ps-muted text-xs">×{job.retries}</span>
          )}
        </div>
      ))}
    </div>
  )
}
