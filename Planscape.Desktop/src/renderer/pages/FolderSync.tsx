import React, { useEffect, useState, useCallback } from 'react'
import { useProjectStore } from '../stores/project'
import { useSyncStore } from '../stores/sync'
import FolderTree from '../components/FolderTree'
import UploadQueuePanel from '../components/UploadQueue'
import type { FolderNode } from '../components/FolderTree'

interface WatchedFolder {
  projectId: string
  localPath: string
  projectName?: string
}

export default function FolderSync(): React.ReactElement {
  const { projects, activeProjectId } = useProjectStore()
  const { recentEvents, triggerSync } = useSyncStore()

  const [watchedFolders, setWatchedFolders] = useState<WatchedFolder[]>([])
  const [selectedFolder, setSelectedFolder] = useState<string | null>(null)
  const [treeNodes, setTreeNodes] = useState<FolderNode[]>([])
  const [treeLoading, setTreeLoading] = useState(false)
  const [bridgeRunning, setBridgeRunning] = useState(false)
  const [bridgeLog, setBridgeLog] = useState<string[]>([])
  const [isDragging, setIsDragging] = useState(false)

  // Load watched folders on mount
  useEffect(() => {
    window.electron.folder.watched().then(folders => {
      const enriched = folders.map(f => ({
        ...f,
        projectName: projects.find(p => p.id === f.projectId)?.name
      }))
      setWatchedFolders(enriched)
    })
  }, [projects])

  // Bridge progress events
  useEffect(() => {
    window.electron.on('bridge:progress', (p: unknown) => {
      const progress = p as { message: string }
      setBridgeLog(prev => [...prev.slice(-99), progress.message])
    })
  }, [])

  const loadTree = useCallback(async (path: string) => {
    setTreeLoading(true)
    try {
      const nodes = await window.electron.folder.tree(path)
      setTreeNodes(nodes)
      setSelectedFolder(path)
    } finally {
      setTreeLoading(false)
    }
  }, [])

  const handleSelectFolder = async () => {
    const path = await window.electron.folder.select()
    if (!path || !activeProjectId) return
    await window.electron.folder.watch(activeProjectId, path)
    setWatchedFolders(prev => [
      ...prev.filter(f => f.projectId !== activeProjectId),
      {
        projectId: activeProjectId,
        localPath: path,
        projectName: projects.find(p => p.id === activeProjectId)?.name
      }
    ])
    loadTree(path)
  }

  const handleCreateStructure = async () => {
    const path = await window.electron.folder.select()
    if (!path || !activeProjectId) return
    const project = projects.find(p => p.id === activeProjectId)
    if (!project) return
    const result = await window.electron.folder.mirror.create(path, project.projectCode, activeProjectId)
    if (result.ok) {
      await window.electron.folder.watch(activeProjectId, result.projectRoot)
      loadTree(result.projectRoot)
    }
  }

  const handleUnwatch = async (projectId: string) => {
    await window.electron.folder.unwatch(projectId)
    setWatchedFolders(prev => prev.filter(f => f.projectId !== projectId))
    if (selectedFolder) setSelectedFolder(null)
    setTreeNodes([])
  }

  const handleFileClick = async (node: FolderNode) => {
    if (node.name.match(/\.ifc$/i) && activeProjectId) {
      setBridgeRunning(true)
      setBridgeLog(['Starting StingBridge…'])
      await window.electron.bridge.run(node.path, activeProjectId)
      setBridgeRunning(false)
    }
  }

  // Drag-and-drop folder onto the drop zone
  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(true)
  }
  const handleDragLeave = () => setIsDragging(false)
  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    if (!activeProjectId) return
    const items = Array.from(e.dataTransfer.files)
    const dir = items.find(f => (f as any).path)
    if (dir) {
      const path = (dir as any).path
      await window.electron.folder.watch(activeProjectId, path)
      loadTree(path)
    }
  }

  const activeProject = projects.find(p => p.id === activeProjectId)

  return (
    <div className="p-6 space-y-4 h-full flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between shrink-0">
        <div>
          <h1 className="text-ps-text text-xl font-bold">Folder Sync</h1>
          <p className="text-ps-muted text-sm">
            Watch local folders and mirror to Planscape Server
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={handleCreateStructure}
            disabled={!activeProjectId}
            className="ps-btn-secondary text-xs"
            title="Create ISO 19650 folder structure"
          >
            📁 Create ISO 19650 Structure
          </button>
          <button
            onClick={handleSelectFolder}
            disabled={!activeProjectId}
            className="ps-btn-primary text-xs"
          >
            + Link Folder
          </button>
        </div>
      </div>

      {!activeProjectId && (
        <div className="ps-card text-ps-muted text-sm text-center py-6">
          Select a project from the Projects page to start syncing
        </div>
      )}

      {/* Drop zone */}
      {activeProjectId && watchedFolders.filter(f => f.projectId === activeProjectId).length === 0 && (
        <div
          className={`border-2 border-dashed rounded-xl p-12 text-center transition-colors
            ${isDragging
              ? 'border-ps-accent bg-ps-accent/10 text-ps-accent'
              : 'border-ps-border text-ps-muted hover:border-ps-accent/50'
            }`}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
        >
          <div className="text-4xl mb-3">📂</div>
          <div className="text-sm font-medium mb-1">
            Drop a project folder here
          </div>
          <div className="text-xs mb-4">or</div>
          <button onClick={handleSelectFolder} className="ps-btn-primary text-xs">
            Browse for Folder
          </button>
        </div>
      )}

      {/* Main layout */}
      {watchedFolders.length > 0 && (
        <div className="flex gap-4 flex-1 min-h-0">
          {/* Watched folders list */}
          <div className="w-64 shrink-0 space-y-2">
            <div className="text-ps-muted text-xs font-semibold uppercase tracking-wider mb-2">
              Watched Folders
            </div>
            {watchedFolders.map(wf => (
              <div
                key={wf.projectId}
                className={`ps-card cursor-pointer p-3 text-xs group hover:border-ps-accent/50
                  ${selectedFolder === wf.localPath ? 'border-ps-accent' : ''}`}
                onClick={() => loadTree(wf.localPath)}
              >
                <div className="font-medium text-ps-text truncate">{wf.projectName ?? wf.projectId}</div>
                <div className="text-ps-muted truncate mt-0.5">{wf.localPath}</div>
                <button
                  onClick={e => { e.stopPropagation(); handleUnwatch(wf.projectId) }}
                  className="text-ps-red text-xs mt-1 opacity-0 group-hover:opacity-100 transition-opacity"
                >
                  Unlink
                </button>
              </div>
            ))}

            {/* Upload queue */}
            <div className="mt-4">
              <div className="text-ps-muted text-xs font-semibold uppercase tracking-wider mb-2">
                Upload Queue
              </div>
              <div className="ps-card p-3">
                <UploadQueuePanel />
              </div>
              <button onClick={triggerSync} className="ps-btn-secondary w-full text-xs mt-2">
                ⟳ Sync Now
              </button>
            </div>
          </div>

          {/* Folder tree */}
          <div className="flex-1 min-h-0 flex flex-col">
            {treeLoading ? (
              <div className="ps-card flex-1 flex items-center justify-center text-ps-muted text-sm">
                Loading…
              </div>
            ) : selectedFolder ? (
              <div className="ps-card flex-1 overflow-auto p-2">
                <div className="text-ps-muted text-xs mb-2 px-2">
                  Click an IFC file to run StingBridge
                </div>
                <FolderTree nodes={treeNodes} onFileClick={handleFileClick} />
              </div>
            ) : (
              <div className="ps-card flex-1 flex items-center justify-center text-ps-muted text-sm">
                Select a watched folder to browse
              </div>
            )}
          </div>

          {/* Bridge log */}
          {(bridgeRunning || bridgeLog.length > 0) && (
            <div className="w-80 shrink-0">
              <div className="text-ps-muted text-xs font-semibold uppercase tracking-wider mb-2">
                StingBridge Log {bridgeRunning && <span className="text-ps-amber animate-pulse ml-1">●</span>}
              </div>
              <div className="ps-card h-full overflow-auto font-mono text-xs text-ps-muted space-y-0.5 max-h-96">
                {bridgeLog.map((line, i) => (
                  <div key={i} className="leading-relaxed">{line}</div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Recent events */}
      {recentEvents.length > 0 && (
        <div className="shrink-0">
          <div className="text-ps-muted text-xs font-semibold uppercase tracking-wider mb-2">
            Recent Events
          </div>
          <div className="ps-card overflow-x-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="text-ps-muted border-b border-ps-border">
                  <th className="text-left py-1 pr-4">Event</th>
                  <th className="text-left py-1 pr-4">File</th>
                  <th className="text-left py-1 pr-4">CDE</th>
                  <th className="text-left py-1">Time</th>
                </tr>
              </thead>
              <tbody>
                {recentEvents.slice(0, 15).map((ev, i) => (
                  <tr key={i} className="border-b border-ps-border/50 hover:bg-ps-elevated">
                    <td className={`py-1 pr-4 font-medium
                      ${ev.event === 'add' ? 'text-ps-green' :
                        ev.event === 'change' ? 'text-ps-amber' : 'text-ps-red'}`}>
                      {ev.event}
                    </td>
                    <td className="py-1 pr-4 text-ps-text truncate max-w-xs">{ev.filename}</td>
                    <td className="py-1 pr-4">
                      <span className={`badge badge-${ev.cdeState.toLowerCase()}`}>{ev.cdeState}</span>
                    </td>
                    <td className="py-1 text-ps-muted">
                      {new Date(ev.detectedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
