import React, { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/auth'

export default function Login(): React.ReactElement {
  const navigate = useNavigate()
  const { login, restoreSession, isLoading, error, token } = useAuthStore()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [serverUrl, setServerUrl] = useState('http://localhost:5000')
  const [showServer, setShowServer] = useState(false)

  useEffect(() => {
    // Load persisted server URL
    window.electron.store.get<string>('serverUrl').then(url => {
      if (url) setServerUrl(url)
    })
    // Try restoring session
    restoreSession().then(() => {
      if (useAuthStore.getState().token) navigate('/dashboard')
    })
  }, [])

  useEffect(() => {
    if (token) navigate('/dashboard')
  }, [token])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    await window.electron.store.set('serverUrl', serverUrl)
    const ok = await login(email, password)
    if (ok) navigate('/dashboard')
  }

  return (
    <div className="min-h-screen bg-ps-bg flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        {/* Logo */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-2xl bg-ps-accent mb-4">
            <span className="text-white text-3xl font-bold">P</span>
          </div>
          <h1 className="text-ps-text text-2xl font-bold">Planscape Desktop</h1>
          <p className="text-ps-muted text-sm mt-1">BIM Coordination Platform</p>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="ps-label">Email</label>
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              className="ps-input"
              placeholder="you@firm.com"
              required
              autoFocus
            />
          </div>
          <div>
            <label className="ps-label">Password</label>
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              className="ps-input"
              placeholder="••••••••"
              required
            />
          </div>

          {error && (
            <div className="bg-ps-red/10 border border-ps-red/30 rounded-lg px-3 py-2 text-ps-red text-sm">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={isLoading}
            className="ps-btn-primary w-full justify-center py-2.5"
          >
            {isLoading ? 'Signing in…' : 'Sign In'}
          </button>
        </form>

        {/* Server config */}
        <div className="mt-4">
          <button
            type="button"
            onClick={() => setShowServer(s => !s)}
            className="text-ps-muted text-xs hover:text-ps-text transition-colors w-full text-center"
          >
            {showServer ? '▲ Hide' : '▾ Server settings'}
          </button>
          {showServer && (
            <div className="mt-3">
              <label className="ps-label">Server URL</label>
              <input
                type="url"
                value={serverUrl}
                onChange={e => setServerUrl(e.target.value)}
                className="ps-input"
                placeholder="http://localhost:5000"
              />
              <p className="text-ps-muted text-xs mt-1">
                Default: http://localhost:5000 (local dev)
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
