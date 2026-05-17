import React, { useEffect, useState } from 'react'
import { useProjectStore } from '../stores/project'
import { issues as issuesApi, type IssueDto } from '../api/endpoints'

const PRIORITIES = ['critical', 'high', 'medium', 'low'] as const
const STATUSES   = ['open', 'in_progress', 'resolved', 'closed'] as const

function priorityColor(p: string): string {
  return { critical: 'text-ps-red', high: 'text-ps-amber', medium: 'text-ps-blue', low: 'text-ps-muted' }[p] ?? 'text-ps-muted'
}

function statusBadge(s: string): string {
  return {
    open:        'bg-ps-red/20 text-ps-red',
    in_progress: 'bg-ps-amber/20 text-ps-amber',
    resolved:    'bg-ps-blue/20 text-ps-blue',
    closed:      'bg-ps-muted/20 text-ps-muted'
  }[s] ?? 'bg-ps-muted/20 text-ps-muted'
}

export default function Issues(): React.ReactElement {
  const { activeProjectId } = useProjectStore()
  const [issueList, setIssueList] = useState<IssueDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [filterStatus, setFilterStatus] = useState<string>('ALL')
  const [showCreate, setShowCreate] = useState(false)
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState({
    title: '', description: '', priority: 'medium', assignedTo: '', dueDate: ''
  })

  const load = async () => {
    if (!activeProjectId) return
    setLoading(true)
    try {
      const data = await issuesApi.list(activeProjectId)
      setIssueList(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load issues')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [activeProjectId])

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!activeProjectId) return
    setCreating(true)
    try {
      const created = await issuesApi.create(activeProjectId, form)
      setIssueList(prev => [created, ...prev])
      setShowCreate(false)
      setForm({ title: '', description: '', priority: 'medium', assignedTo: '', dueDate: '' })
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to create issue')
    } finally {
      setCreating(false)
    }
  }

  const handleStatusChange = async (issue: IssueDto, status: string) => {
    if (!activeProjectId) return
    try {
      const updated = await issuesApi.update(activeProjectId, issue.id, { status })
      setIssueList(prev => prev.map(i => i.id === issue.id ? updated : i))
    } catch { /* ignore */ }
  }

  const filtered = issueList.filter(i => {
    if (filterStatus !== 'ALL' && i.status !== filterStatus) return false
    if (search && !i.title.toLowerCase().includes(search.toLowerCase())) return false
    return true
  })

  const counts: Record<string, number> = {}
  for (const s of STATUSES) counts[s] = issueList.filter(i => i.status === s).length

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-ps-text text-xl font-bold">Issues</h1>
          <p className="text-ps-muted text-sm">RFIs, NCRs, and site issues</p>
        </div>
        <div className="flex gap-2">
          <button onClick={load} className="ps-btn-secondary text-xs">⟳ Refresh</button>
          <button
            onClick={() => setShowCreate(s => !s)}
            disabled={!activeProjectId}
            className="ps-btn-primary text-xs"
          >
            + New Issue
          </button>
        </div>
      </div>

      {!activeProjectId && (
        <div className="ps-card text-ps-muted text-sm text-center py-6">
          Select a project to view issues
        </div>
      )}

      {activeProjectId && (
        <>
          {/* Create form */}
          {showCreate && (
            <div className="ps-card border-ps-accent/30">
              <h3 className="text-ps-text font-semibold mb-3">Raise Issue</h3>
              <form onSubmit={handleCreate} className="space-y-3">
                <div>
                  <label className="ps-label">Title *</label>
                  <input
                    value={form.title}
                    onChange={e => setForm(f => ({ ...f, title: e.target.value }))}
                    className="ps-input"
                    required
                    placeholder="Clash at grid B3, Level 2"
                  />
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="ps-label">Priority</label>
                    <select
                      value={form.priority}
                      onChange={e => setForm(f => ({ ...f, priority: e.target.value }))}
                      className="ps-input"
                    >
                      {PRIORITIES.map(p => <option key={p} value={p}>{p}</option>)}
                    </select>
                  </div>
                  <div>
                    <label className="ps-label">Due Date</label>
                    <input
                      type="date"
                      value={form.dueDate}
                      onChange={e => setForm(f => ({ ...f, dueDate: e.target.value }))}
                      className="ps-input"
                    />
                  </div>
                </div>
                <div>
                  <label className="ps-label">Description</label>
                  <textarea
                    value={form.description}
                    onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                    className="ps-input resize-none"
                    rows={2}
                  />
                </div>
                <div className="flex gap-2">
                  <button type="submit" disabled={creating} className="ps-btn-primary text-xs">
                    {creating ? 'Creating…' : 'Raise Issue'}
                  </button>
                  <button type="button" onClick={() => setShowCreate(false)} className="ps-btn-secondary text-xs">
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          )}

          {/* Status filter tabs */}
          <div className="flex items-center gap-2">
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="ps-input max-w-xs text-sm"
              placeholder="Search issues…"
            />
            <div className="flex gap-1">
              {(['ALL', ...STATUSES] as const).map(s => (
                <button
                  key={s}
                  onClick={() => setFilterStatus(s)}
                  className={`text-xs px-3 py-1.5 rounded-lg border transition-colors
                    ${filterStatus === s
                      ? 'bg-ps-accent text-white border-ps-accent'
                      : 'border-ps-border text-ps-muted hover:text-ps-text'
                    }`}
                >
                  {s} {s !== 'ALL' && `(${counts[s] ?? 0})`}
                </button>
              ))}
            </div>
          </div>

          {loading && <div className="text-ps-muted text-sm text-center py-8">Loading…</div>}
          {error && <div className="text-ps-red text-sm text-center py-4">{error}</div>}

          {/* Issue list */}
          {!loading && (
            <div className="space-y-2">
              {filtered.length === 0 ? (
                <div className="ps-card text-ps-muted text-sm text-center py-8">No issues found</div>
              ) : (
                filtered.map(issue => (
                  <div key={issue.id} className="ps-card hover:border-ps-accent/30 transition-colors">
                    <div className="flex items-start gap-3">
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className={`text-xs font-bold uppercase ${priorityColor(issue.priority)}`}>
                            ⚑ {issue.priority}
                          </span>
                          {issue.slaBreached && (
                            <span className="badge bg-ps-red/20 text-ps-red text-xs">SLA Breached</span>
                          )}
                        </div>
                        <div className="text-ps-text font-medium text-sm">{issue.title}</div>
                        {issue.description && (
                          <div className="text-ps-muted text-xs mt-1 line-clamp-2">{issue.description}</div>
                        )}
                        <div className="flex items-center gap-3 mt-1.5 text-xs text-ps-muted">
                          {issue.assignedTo && <span>→ {issue.assignedTo}</span>}
                          {issue.dueDate && (
                            <span className={new Date(issue.dueDate) < new Date() ? 'text-ps-red' : ''}>
                              📅 {new Date(issue.dueDate).toLocaleDateString('en-GB')}
                            </span>
                          )}
                          <span>{new Date(issue.createdAt).toLocaleDateString('en-GB')}</span>
                        </div>
                      </div>
                      <div className="flex flex-col items-end gap-2 shrink-0">
                        <span className={`badge text-xs px-2 py-0.5 rounded ${statusBadge(issue.status)}`}>
                          {issue.status.replace('_', ' ')}
                        </span>
                        <select
                          value={issue.status}
                          onChange={e => handleStatusChange(issue, e.target.value)}
                          className="bg-ps-elevated border border-ps-border rounded text-xs text-ps-muted px-2 py-1"
                        >
                          {STATUSES.map(s => <option key={s} value={s}>{s.replace('_', ' ')}</option>)}
                        </select>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}
