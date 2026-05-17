import React, { useEffect, useState } from 'react'
import { useProjectStore } from '../stores/project'
import { documents, type DocumentDto } from '../api/endpoints'

const CDE_STATES = ['WIP', 'SHARED', 'PUBLISHED', 'ARCHIVED'] as const
type CDEState = typeof CDE_STATES[number]

function CDEBadge({ state }: { state: string }): React.ReactElement {
  const cls: Record<string, string> = {
    WIP:       'badge-wip',
    SHARED:    'badge-shared',
    PUBLISHED: 'badge-published',
    ARCHIVED:  'badge-archived'
  }
  return <span className={`badge ${cls[state] ?? 'badge-wip'}`}>{state}</span>
}

function fileIcon(name: string): string {
  const ext = name.split('.').pop()?.toLowerCase() ?? ''
  const icons: Record<string, string> = {
    ifc: '🏗', rvt: '🏠', dwg: '📐', pdf: '📄', xlsx: '📊', docx: '📝'
  }
  return icons[ext] ?? '📎'
}

function formatSize(bytes?: number): string {
  if (!bytes) return '—'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

export default function Documents(): React.ReactElement {
  const { activeProjectId } = useProjectStore()
  const [docs, setDocs] = useState<DocumentDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [filterState, setFilterState] = useState<CDEState | 'ALL'>('ALL')
  const [transitioning, setTransitioning] = useState<string | null>(null)

  const load = async () => {
    if (!activeProjectId) return
    setLoading(true)
    setError('')
    try {
      const data = await documents.list(activeProjectId)
      setDocs(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load documents')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [activeProjectId])

  const handleTransition = async (doc: DocumentDto, newState: CDEState) => {
    if (!activeProjectId) return
    setTransitioning(doc.id)
    try {
      await documents.transition(activeProjectId, doc.id, newState)
      setDocs(prev => prev.map(d => d.id === doc.id ? { ...d, cdeState: newState } : d))
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Transition failed')
    } finally {
      setTransitioning(null)
    }
  }

  const filtered = docs.filter(d => {
    if (filterState !== 'ALL' && d.cdeState !== filterState) return false
    if (search && !d.name.toLowerCase().includes(search.toLowerCase())) return false
    return true
  })

  const nextState: Record<string, CDEState> = {
    WIP: 'SHARED', SHARED: 'PUBLISHED', PUBLISHED: 'ARCHIVED'
  }

  return (
    <div className="p-6 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-ps-text text-xl font-bold">Documents</h1>
          <p className="text-ps-muted text-sm">ISO 19650 CDE document management</p>
        </div>
        <button onClick={load} className="ps-btn-secondary text-xs">
          ⟳ Refresh
        </button>
      </div>

      {!activeProjectId && (
        <div className="ps-card text-ps-muted text-sm text-center py-6">
          Select a project to view documents
        </div>
      )}

      {activeProjectId && (
        <>
          {/* Filters */}
          <div className="flex items-center gap-3">
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="ps-input max-w-xs text-sm"
              placeholder="Search documents…"
            />
            <div className="flex gap-1">
              {(['ALL', ...CDE_STATES] as const).map(state => (
                <button
                  key={state}
                  onClick={() => setFilterState(state as CDEState | 'ALL')}
                  className={`text-xs px-3 py-1.5 rounded-lg border transition-colors
                    ${filterState === state
                      ? 'bg-ps-accent text-white border-ps-accent'
                      : 'border-ps-border text-ps-muted hover:text-ps-text hover:border-ps-accent/50'
                    }`}
                >
                  {state}
                </button>
              ))}
            </div>
          </div>

          {/* Stats bar */}
          <div className="flex gap-4">
            {CDE_STATES.map(state => {
              const count = docs.filter(d => d.cdeState === state).length
              return (
                <div key={state} className="flex items-center gap-2 text-xs">
                  <CDEBadge state={state} />
                  <span className="text-ps-muted">{count}</span>
                </div>
              )
            })}
          </div>

          {loading && <div className="text-ps-muted text-sm text-center py-8">Loading…</div>}
          {error && <div className="text-ps-red text-sm text-center py-4">{error}</div>}

          {/* Table */}
          {!loading && (
            <div className="ps-card overflow-auto">
              {filtered.length === 0 ? (
                <div className="text-ps-muted text-sm text-center py-8">No documents found</div>
              ) : (
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-ps-muted text-xs border-b border-ps-border">
                      <th className="text-left py-2 pr-4">Name</th>
                      <th className="text-left py-2 pr-4">Discipline</th>
                      <th className="text-left py-2 pr-4">Revision</th>
                      <th className="text-left py-2 pr-4">State</th>
                      <th className="text-left py-2 pr-4">Size</th>
                      <th className="text-left py-2 pr-4">Uploaded</th>
                      <th className="text-left py-2">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map(doc => (
                      <tr key={doc.id} className="border-b border-ps-border/50 hover:bg-ps-elevated">
                        <td className="py-2 pr-4">
                          <div className="flex items-center gap-2">
                            <span className="shrink-0">{fileIcon(doc.name)}</span>
                            <span className="text-ps-text truncate max-w-xs">{doc.name}</span>
                          </div>
                        </td>
                        <td className="py-2 pr-4 text-ps-muted">{doc.discipline || '—'}</td>
                        <td className="py-2 pr-4 font-mono text-ps-muted">{doc.revisionCode || 'P01'}</td>
                        <td className="py-2 pr-4"><CDEBadge state={doc.cdeState} /></td>
                        <td className="py-2 pr-4 text-ps-muted">{formatSize(doc.fileSize)}</td>
                        <td className="py-2 pr-4 text-ps-muted text-xs">
                          {new Date(doc.uploadedAt).toLocaleDateString('en-GB')}
                        </td>
                        <td className="py-2">
                          <div className="flex items-center gap-2">
                            {nextState[doc.cdeState] && (
                              <button
                                onClick={() => handleTransition(doc, nextState[doc.cdeState] as CDEState)}
                                disabled={transitioning === doc.id}
                                className="text-ps-accent text-xs hover:text-ps-accent-dim disabled:opacity-50"
                              >
                                {transitioning === doc.id ? '…' : `→ ${nextState[doc.cdeState]}`}
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}
