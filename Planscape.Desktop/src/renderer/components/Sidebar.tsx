import React from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/auth'
import { useSyncStore } from '../stores/sync'

interface NavItem {
  to: string
  label: string
  icon: string
}

const NAV_ITEMS: NavItem[] = [
  { to: '/dashboard',  label: 'Dashboard',   icon: '⊞' },
  { to: '/projects',   label: 'Projects',    icon: '◫' },
  { to: '/sync',       label: 'Folder Sync', icon: '⟳' },
  { to: '/documents',  label: 'Documents',   icon: '⎘' },
  { to: '/issues',     label: 'Issues',      icon: '⚑' },
  { to: '/settings',   label: 'Settings',    icon: '⚙' }
]

export default function Sidebar(): React.ReactElement {
  const { user, logout } = useAuthStore()
  const { uploadQueue } = useSyncStore()
  const navigate = useNavigate()

  const pendingCount = uploadQueue.filter(j => j.status === 'pending' || j.status === 'uploading').length

  const handleLogout = async () => {
    await logout()
    navigate('/login')
  }

  return (
    <aside className="w-56 flex flex-col bg-ps-surface border-r border-ps-border shrink-0">
      {/* Logo */}
      <div className="flex items-center gap-2 px-4 py-4 border-b border-ps-border">
        <div className="w-8 h-8 rounded-lg bg-ps-accent flex items-center justify-center text-white font-bold text-sm">
          P
        </div>
        <div>
          <div className="text-ps-text font-semibold text-sm leading-tight">Planscape</div>
          <div className="text-ps-muted text-xs">Desktop</div>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 py-2 overflow-y-auto">
        {NAV_ITEMS.map(item => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              `flex items-center gap-3 px-4 py-2.5 text-sm transition-colors duration-100 relative
               ${isActive
                 ? 'bg-ps-accent/10 text-ps-accent font-medium'
                 : 'text-ps-muted hover:text-ps-text hover:bg-ps-elevated'
               }`
            }
          >
            <span className="text-base w-5 text-center">{item.icon}</span>
            <span className="flex-1">{item.label}</span>
            {item.to === '/sync' && pendingCount > 0 && (
              <span className="bg-ps-accent text-white text-xs rounded-full w-5 h-5 flex items-center justify-center font-bold">
                {pendingCount > 9 ? '9+' : pendingCount}
              </span>
            )}
          </NavLink>
        ))}
      </nav>

      {/* User info */}
      <div className="border-t border-ps-border p-3">
        {user && (
          <div className="flex items-center gap-2 mb-2">
            <div className="w-8 h-8 rounded-full bg-ps-elevated flex items-center justify-center text-ps-muted text-sm font-semibold">
              {user.name.charAt(0).toUpperCase()}
            </div>
            <div className="flex-1 min-w-0">
              <div className="text-ps-text text-xs font-medium truncate">{user.name}</div>
              <div className="text-ps-muted text-xs truncate">{user.tenantName}</div>
            </div>
          </div>
        )}
        <button
          onClick={handleLogout}
          className="w-full text-left text-ps-muted hover:text-ps-red text-xs py-1 px-1 transition-colors"
        >
          Sign out
        </button>
      </div>
    </aside>
  )
}
