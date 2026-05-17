import React, { useEffect, useState } from 'react'
import { useProjectStore } from '../stores/project'
import ProjectCard from '../components/ProjectCard'
import { projects as projectsApi, type ProjectDto } from '../api/endpoints'

export default function Projects(): React.ReactElement {
  const { projects, fetchProjects, isLoading, error } = useProjectStore()
  const [showCreate, setShowCreate] = useState(false)
  const [search, setSearch] = useState('')
  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState({ name: '', projectCode: '', description: '' })
  const [createError, setCreateError] = useState('')

  useEffect(() => { fetchProjects() }, [])

  const filtered = projects.filter(p =>
    p.name.toLowerCase().includes(search.toLowerCase()) ||
    p.projectCode.toLowerCase().includes(search.toLowerCase())
  )

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    setCreating(true)
    setCreateError('')
    try {
      await projectsApi.create(form)
      await fetchProjects()
      setShowCreate(false)
      setForm({ name: '', projectCode: '', description: '' })
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Failed to create project')
    } finally {
      setCreating(false)
    }
  }

  return (
    <div className="p-6 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-ps-text text-xl font-bold">Projects</h1>
          <p className="text-ps-muted text-sm">{projects.length} project{projects.length !== 1 ? 's' : ''}</p>
        </div>
        <button onClick={() => setShowCreate(s => !s)} className="ps-btn-primary">
          + New Project
        </button>
      </div>

      {/* Create form */}
      {showCreate && (
        <div className="ps-card border-ps-accent/30">
          <h3 className="text-ps-text font-semibold mb-4">Create Project</h3>
          <form onSubmit={handleCreate} className="space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="ps-label">Project Name *</label>
                <input
                  value={form.name}
                  onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                  className="ps-input"
                  placeholder="City Centre Office Block"
                  required
                />
              </div>
              <div>
                <label className="ps-label">Project Code *</label>
                <input
                  value={form.projectCode}
                  onChange={e => setForm(f => ({ ...f, projectCode: e.target.value.toUpperCase() }))}
                  className="ps-input font-mono"
                  placeholder="CCO-2026-001"
                  required
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
                placeholder="Optional project description"
              />
            </div>
            {createError && (
              <div className="text-ps-red text-xs">{createError}</div>
            )}
            <div className="flex gap-2">
              <button type="submit" disabled={creating} className="ps-btn-primary">
                {creating ? 'Creating…' : 'Create'}
              </button>
              <button type="button" onClick={() => setShowCreate(false)} className="ps-btn-secondary">
                Cancel
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Search */}
      <input
        value={search}
        onChange={e => setSearch(e.target.value)}
        className="ps-input max-w-sm"
        placeholder="Search projects…"
      />

      {/* List */}
      {isLoading && (
        <div className="text-ps-muted text-sm text-center py-8">Loading…</div>
      )}
      {error && (
        <div className="text-ps-red text-sm text-center py-4">{error}</div>
      )}
      {!isLoading && filtered.length === 0 && (
        <div className="text-ps-muted text-sm text-center py-8">
          {search ? 'No projects match your search' : 'No projects yet — create one above'}
        </div>
      )}
      <div className="grid grid-cols-2 gap-4">
        {filtered.map(p => <ProjectCard key={p.id} project={p} />)}
      </div>
    </div>
  )
}
