import React, { useEffect, useState } from 'react'

interface SettingsState {
  serverUrl: string
  bridgePath: string
  syncIntervalMs: number
  accessToken: string
}

export default function Settings(): React.ReactElement {
  const [settings, setSettings] = useState<SettingsState>({
    serverUrl: 'http://localhost:5000',
    bridgePath: '',
    syncIntervalMs: 300_000,
    accessToken: ''
  })
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [version, setVersion] = useState('')
  const [testResult, setTestResult] = useState<string | null>(null)
  const [testLoading, setTestLoading] = useState(false)

  useEffect(() => {
    const load = async () => {
      const [serverUrl, bridgePath, syncIntervalMs, accessToken, ver] = await Promise.all([
        window.electron.store.get<string>('serverUrl'),
        window.electron.store.get<string>('bridgePath'),
        window.electron.store.get<number>('syncIntervalMs'),
        window.electron.store.get<string>('accessToken'),
        window.electron.app.version()
      ])
      setSettings({
        serverUrl: serverUrl ?? 'http://localhost:5000',
        bridgePath: bridgePath ?? '',
        syncIntervalMs: syncIntervalMs ?? 300_000,
        accessToken: accessToken ? '••••••••' : ''
      })
      setVersion(ver)
    }
    load()
  }, [])

  const handleSave = async () => {
    setSaving(true)
    try {
      await window.electron.store.set('serverUrl', settings.serverUrl)
      await window.electron.store.set('bridgePath', settings.bridgePath)
      await window.electron.store.set('syncIntervalMs', settings.syncIntervalMs)
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
    } finally {
      setSaving(false)
    }
  }

  const handleBrowseBridge = async () => {
    const path = await window.electron.folder.select()
    if (path) setSettings(s => ({ ...s, bridgePath: path }))
  }

  const handleTestConnection = async () => {
    setTestLoading(true)
    setTestResult(null)
    try {
      const res = await fetch(`${settings.serverUrl}/health`, {
        headers: { 'X-Client-Type': 'desktop' }
      })
      setTestResult(res.ok ? '✓ Connected' : `✗ Server returned ${res.status}`)
    } catch (err) {
      setTestResult(`✗ ${err instanceof Error ? err.message : 'Connection failed'}`)
    } finally {
      setTestLoading(false)
    }
  }

  const syncIntervalMinutes = Math.round(settings.syncIntervalMs / 60_000)

  return (
    <div className="p-6 space-y-6 max-w-2xl">
      <div>
        <h1 className="text-ps-text text-xl font-bold">Settings</h1>
        <p className="text-ps-muted text-sm">Configure Planscape Desktop</p>
      </div>

      {/* Server */}
      <div className="ps-card space-y-4">
        <h2 className="text-ps-text font-semibold">Planscape Server</h2>
        <div>
          <label className="ps-label">Server URL</label>
          <div className="flex gap-2">
            <input
              type="url"
              value={settings.serverUrl}
              onChange={e => setSettings(s => ({ ...s, serverUrl: e.target.value }))}
              className="ps-input flex-1"
              placeholder="http://localhost:5000"
            />
            <button
              onClick={handleTestConnection}
              disabled={testLoading}
              className="ps-btn-secondary text-xs shrink-0"
            >
              {testLoading ? 'Testing…' : 'Test'}
            </button>
          </div>
          {testResult && (
            <div className={`text-xs mt-1 ${testResult.startsWith('✓') ? 'text-ps-green' : 'text-ps-red'}`}>
              {testResult}
            </div>
          )}
        </div>
        <div>
          <label className="ps-label">Authentication Status</label>
          <div className={`text-sm ${settings.accessToken ? 'text-ps-green' : 'text-ps-red'}`}>
            {settings.accessToken ? '✓ Logged in' : '✗ Not authenticated — sign in again'}
          </div>
        </div>
      </div>

      {/* StingBridge */}
      <div className="ps-card space-y-4">
        <h2 className="text-ps-text font-semibold">StingBridge</h2>
        <p className="text-ps-muted text-xs">
          StingBridge is a Python process that processes IFC files. Point this to the
          StingBridge package root (the folder containing the <code className="bg-ps-elevated px-1 rounded">StingBridge/</code> module).
        </p>
        <div>
          <label className="ps-label">StingBridge Path</label>
          <div className="flex gap-2">
            <input
              value={settings.bridgePath}
              onChange={e => setSettings(s => ({ ...s, bridgePath: e.target.value }))}
              className="ps-input flex-1 font-mono text-xs"
              placeholder="/home/user/STINGTOOLS/StingBridge"
            />
            <button onClick={handleBrowseBridge} className="ps-btn-secondary text-xs shrink-0">
              Browse
            </button>
          </div>
          {!settings.bridgePath && (
            <div className="text-ps-amber text-xs mt-1">
              ⚠ Not set — IFC files will not trigger StingBridge processing
            </div>
          )}
        </div>
      </div>

      {/* Sync */}
      <div className="ps-card space-y-4">
        <h2 className="text-ps-text font-semibold">Sync Settings</h2>
        <div>
          <label className="ps-label">Auto-sync interval</label>
          <div className="flex items-center gap-3">
            <input
              type="range"
              min={1}
              max={60}
              value={syncIntervalMinutes}
              onChange={e => setSettings(s => ({
                ...s, syncIntervalMs: parseInt(e.target.value) * 60_000
              }))}
              className="flex-1 accent-ps-accent"
            />
            <span className="text-ps-text text-sm w-24 text-right">
              {syncIntervalMinutes} min{syncIntervalMinutes !== 1 ? 's' : ''}
            </span>
          </div>
        </div>
      </div>

      {/* About */}
      <div className="ps-card space-y-2 text-xs text-ps-muted">
        <h2 className="text-ps-text font-semibold text-sm mb-2">About</h2>
        <div className="flex gap-4">
          <span>Version: <span className="text-ps-text font-mono">{version || '—'}</span></span>
        </div>
        <div>
          Planscape Desktop is a companion application for the Planscape BIM coordination platform.
          It monitors local project folders, mirrors ISO 19650 document structures to Planscape Server,
          and integrates with StingBridge for IFC processing.
        </div>
      </div>

      {/* Save */}
      <div className="flex items-center gap-3">
        <button onClick={handleSave} disabled={saving} className="ps-btn-primary">
          {saving ? 'Saving…' : 'Save Settings'}
        </button>
        {saved && <span className="text-ps-green text-sm">✓ Saved</span>}
      </div>
    </div>
  )
}
