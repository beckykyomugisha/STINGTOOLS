import React from 'react'
import type { ProjectDto } from '../api/endpoints'
import { useProjectStore } from '../stores/project'

interface ProjectCardProps {
  project: ProjectDto
  onClick?: () => void
}

function ComplianceGauge({ percent }: { percent: number }): React.ReactElement {
  const color = percent >= 80 ? '#22c55e' : percent >= 50 ? '#f59e0b' : '#ef4444'
  const r = 20
  const circ = 2 * Math.PI * r
  const dash = (percent / 100) * circ

  return (
    <div className="relative w-14 h-14 flex items-center justify-center">
      <svg className="absolute inset-0 -rotate-90" width="56" height="56" viewBox="0 0 56 56">
        <circle cx="28" cy="28" r={r} fill="none" stroke="#2d3748" strokeWidth="4" />
        <circle
          cx="28" cy="28" r={r} fill="none"
          stroke={color} strokeWidth="4"
          strokeDasharray={`${dash} ${circ - dash}`}
          strokeLinecap="round"
        />
      </svg>
      <span className="text-xs font-bold" style={{ color }}>{percent}%</span>
    </div>
  )
}

export default function ProjectCard({ project, onClick }: ProjectCardProps): React.ReactElement {
  const { activeProjectId, setActiveProject } = useProjectStore()
  const isActive = activeProjectId === project.id

  const handleClick = () => {
    setActiveProject(project.id)
    onClick?.()
  }

  const statusColors: Record<string, string> = {
    active:    'bg-ps-green/20 text-ps-green',
    on_hold:   'bg-ps-amber/20 text-ps-amber',
    completed: 'bg-ps-blue/20 text-ps-blue',
    archived:  'bg-ps-muted/20 text-ps-muted'
  }
  const statusCls = statusColors[project.status] ?? statusColors.active

  return (
    <div
      onClick={handleClick}
      className={`ps-card cursor-pointer transition-all duration-150 hover:border-ps-accent/50
        ${isActive ? 'border-ps-accent shadow-lg shadow-ps-accent/10' : ''}
      `}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <span className="text-xs font-mono text-ps-muted">{project.projectCode}</span>
            <span className={`badge text-xs px-2 py-0.5 rounded ${statusCls}`}>
              {project.status.replace('_', ' ')}
            </span>
          </div>
          <h3 className="text-ps-text font-semibold text-sm truncate">{project.name}</h3>
          {project.description && (
            <p className="text-ps-muted text-xs mt-0.5 line-clamp-2">{project.description}</p>
          )}
          <div className="flex items-center gap-3 mt-2 text-xs text-ps-muted">
            {project.memberCount !== undefined && (
              <span>👥 {project.memberCount}</span>
            )}
            <span>{new Date(project.createdAt).toLocaleDateString()}</span>
          </div>
        </div>
        {project.compliancePercent !== undefined && (
          <ComplianceGauge percent={project.compliancePercent} />
        )}
      </div>
    </div>
  )
}
