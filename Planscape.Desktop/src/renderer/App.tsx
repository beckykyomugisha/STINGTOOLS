import React, { useEffect } from 'react'
import { HashRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuthStore } from './stores/auth'
import { useSyncStore } from './stores/sync'
import Sidebar from './components/Sidebar'
import SyncStatusBar from './components/SyncStatusBar'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Projects from './pages/Projects'
import FolderSync from './pages/FolderSync'
import Documents from './pages/Documents'
import Issues from './pages/Issues'
import Settings from './pages/Settings'

function ProtectedLayout(): React.ReactElement {
  const { addUploadJob, setWatcherEvent } = useSyncStore()

  useEffect(() => {
    // Listen for file watcher events from main process
    window.electron.on('watcher:file', (file: unknown) => {
      setWatcherEvent(file as any)
    })
    // Listen for upload queue updates
    window.electron.on('upload:queue', (queue: unknown) => {
      addUploadJob(queue as any)
    })
    return () => {
      window.electron.off('watcher:file', () => {})
      window.electron.off('upload:queue', () => {})
    }
  }, [addUploadJob, setWatcherEvent])

  return (
    <div className="flex h-screen overflow-hidden bg-ps-bg">
      <Sidebar />
      <div className="flex-1 flex flex-col overflow-hidden">
        <main className="flex-1 overflow-auto">
          <Routes>
            <Route path="/" element={<Navigate to="/dashboard" replace />} />
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/projects" element={<Projects />} />
            <Route path="/sync" element={<FolderSync />} />
            <Route path="/documents" element={<Documents />} />
            <Route path="/issues" element={<Issues />} />
            <Route path="/settings" element={<Settings />} />
          </Routes>
        </main>
        <SyncStatusBar />
      </div>
    </div>
  )
}

function AuthGuard({ children }: { children: React.ReactElement }): React.ReactElement {
  const { token } = useAuthStore()
  if (!token) return <Navigate to="/login" replace />
  return children
}

export default function App(): React.ReactElement {
  return (
    <HashRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route
          path="/*"
          element={
            <AuthGuard>
              <ProtectedLayout />
            </AuthGuard>
          }
        />
      </Routes>
    </HashRouter>
  )
}

// Extend Window type for electron bridge
declare global {
  interface Window {
    electron: import('../preload/index').IElectronAPI
  }
}
