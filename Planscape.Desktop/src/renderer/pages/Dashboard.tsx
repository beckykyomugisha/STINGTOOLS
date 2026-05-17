import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/auth'
import { useProjectStore } from '../stores/project'
import { useSyncStore } from '../stores/sync'
import { compliance, type ComplianceSnapshot } from '../api/endpoints'
import ProjectCard from '../components/ProjectCard'
import UploadQueuePanel from '../components/UploadQueue'

function StatCard({ label, value, sub, color = 'text-ps-accent' }: {
  label: string; value: string | number; sub?: string; color?: string
}): React.ReactElement {
  return (
    <div className="ps-card">
      <div className={`text-2xl font-bold ${color}`}>{value}</div>
      <div className="text-ps-text text-sm font-medium mt-0.5">{label}</div>
      {sub && <div className="text-ps-muted text-xs mt-0.5">{sub}</div>}
    </div>
  )
}

export default function Dashboard(): React.ReactElement {
  const { user } = useAuthStore()
  const { projects, fetchProjects, activeProjectId } = useProjectStore()
  const { uploadQueue, recentEvents } = useSyncStore()
  const navigate = useNavigate()

  const [snapshot, setSnapshot] = useState<ComplianceSnapshot | null>(null)

  useEffect(() => { fetchProjects() }, [])

  useEffect(() => {
    if (!activeProjectId) return
    compliance.latest(activeProjectId).then(setSnapshot).catch(() => {})
  }, [activeProjectId])

  const pending   = uploadQueue.filter(j => j.status === 'pending').length
  const uploading = uploadQueue.filter(j => j.status === 'uploading').length
  const errored   = uploadQueue.filter(j => j.status === 'error').length

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-ps-text text-xl font-bold">
          Good {new Date().getHours() < 12 ? 'morning' : new Date().getHours() < 18 ? 'afternoon' : 'evening'},{' '}
          {user?.name.split(' ')[0] ?? 'there'}
        </h1>
        <p className="text-ps-muted text-sm mt-0.5">
          {new Date().toLocaleDateString('en-GB', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}
        </p>
      </div>

      {/* Stats row */}
      <div className="grid grid-cols-4 gap-4">
        <StatCard
          label="Projects"
          value={projects.length}
          sub="active"
        />
        <StatCard
          label="Sync Queue"
          value={pending + uploading}
          sub={errored > 0 ? `${errored} errors` : 'files pending'}
          color={errored > 0 ? 'text-ps-red' : uploading > 0 ? 'text-ps-amber' : 'text-ps-green'}
        />
        <StatCard
          label="Compliance"
          value={snapshot ? `${snapshot.compliancePercent}%` : '—'}
          sub={snapshot ? `${snapshot.taggedElements} / ${snapshot.totalElements} tagged` : 'select project'}
          color={
            snapshot
              ? snapshot.compliancePercent >= 80 ? 'text-ps-green'
              : snapshot.compliancePercent >= 50 ? 'text-ps-amber'
              : 'text-ps-red'
              : 'text-ps-muted'
          }
        />
        <StatCard
          label="Events Today"
          value={recentEvents.filter(e =>
            new Date(e.detectedAt).toDateString() === new Date().toDateString()
          ).length}
          sub="file changes detected"
        />
      </div>

      {/* Main grid */}
      <div className="grid grid-cols-3 gap-6">
        {/* Projects */}
        <div className="col-span-2 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-ps-text font-semibold">Projects</h2>
            <button
              onClick={() => navigate('/projects')}
              className="text-ps-accent text-xs hover:text-ps-accent-dim"
            >
              View all →
            </button>
          </div>
          {projects.length === 0 ? (
            <div className="ps-card text-center py-8 text-ps-muted text-sm">
              No projects yet.{' '}
              <button
                onClick={() => navigate('/projects')}
                className="text-ps-accent hover:underline"
              >
                Add your first project
              </button>
            </div>
          ) : (
            <div className="space-y-3">
              {projects.slice(0, 4).map(p => (
                <ProjectCard key={p.id} project={p} onClick={() => navigate('/dashboard')} />
              ))}
            </div>
          )}
        </div>

        {/* Right column */}
        <div className="space-y-4">
          {/* Upload queue */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <h2 className="text-ps-text font-semibold text-sm">Upload Queue</h2>
              <button
                onClick={() => navigate('/sync')}
                className="text-ps-accent text-xs hover:text-ps-accent-dim"
              >
                View sync →
              </button>
            </div>
            <div className="ps-card">
              <UploadQueuePanel />
            </div>
          </div>

          {/* Recent events */}
          <div>
            <h2 className="text-ps-text font-semibold text-sm mb-2">Recent File Events</h2>
            <div className="ps-card space-y-2 max-h-48 overflow-y-auto">
              {recentEvents.length === 0 ? (
                <div className="text-ps-muted text-xs text-center py-4">
                  No file events yet — link a folder to start watching
                </div>
              ) : (
                recentEvents.slice(0, 10).map((ev, i) => (
                  <div key={i} className="flex items-center gap-2 text-xs">
                    <span className={
                      ev.event === 'add' ? 'text-ps-green' :
                      ev.event === 'change' ? 'text-ps-amber' : 'text-ps-red'
                    }>
                      {ev.event === 'add' ? '+' : ev.event === 'change' ? '~' : '−'}
                    </span>
                    <span className="text-ps-muted truncate flex-1">{ev.filename}</span>
                    <span className="text-ps-muted shrink-0">
                      {new Date(ev.detectedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </span>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
