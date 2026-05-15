import React, { useEffect } from 'react'
import { useSyncStore } from '../stores/sync'

export default function SyncStatusBar(): React.ReactElement {
  const { uploadQueue, isSyncing, lastSyncAt, triggerSync, refreshQueue } = useSyncStore()

  const pending   = uploadQueue.filter(j => j.status === 'pending').length
  const uploading = uploadQueue.filter(j => j.status === 'uploading').length
  const errored   = uploadQueue.filter(j => j.status === 'error').length
  const done      = uploadQueue.filter(j => j.status === 'done').length

  useEffect(() => {
    refreshQueue()
    const interval = setInterval(refreshQueue, 5000)
    return () => clearInterval(interval)
  }, [refreshQueue])

  const statusText = () => {
    if (isSyncing || uploading > 0) return `Uploading ${uploading} file${uploading !== 1 ? 's' : ''}…`
    if (pending > 0) return `${pending} file${pending !== 1 ? 's' : ''} queued`
    if (errored > 0) return `${errored} upload error${errored !== 1 ? 's' : ''}`
    if (lastSyncAt) return `Last sync ${new Date(lastSyncAt).toLocaleTimeString()}`
    return 'Idle'
  }

  const statusColor = () => {
    if (isSyncing || uploading > 0) return 'text-ps-blue'
    if (errored > 0) return 'text-ps-red'
    if (pending > 0) return 'text-ps-amber'
    return 'text-ps-green'
  }

  const indicator = () => {
    if (isSyncing || uploading > 0) return '●'
    if (errored > 0) return '●'
    if (pending > 0) return '●'
    return '●'
  }

  return (
    <footer className="h-8 bg-ps-surface border-t border-ps-border flex items-center justify-between px-4 shrink-0">
      <div className="flex items-center gap-2">
        <span className={`text-xs ${statusColor()} ${isSyncing || uploading > 0 ? 'animate-pulse' : ''}`}>
          {indicator()}
        </span>
        <span className="text-ps-muted text-xs">{statusText()}</span>
      </div>

      <div className="flex items-center gap-4">
        {done > 0 && (
          <span className="text-ps-green text-xs">{done} synced</span>
        )}
        {(pending > 0 || errored > 0) && (
          <button
            onClick={triggerSync}
            disabled={isSyncing}
            className="text-ps-accent text-xs hover:text-ps-accent-dim disabled:opacity-50 transition-colors"
          >
            Sync now
          </button>
        )}
        <span className="text-ps-border text-xs select-none">Planscape Desktop</span>
      </div>
    </footer>
  )
}
