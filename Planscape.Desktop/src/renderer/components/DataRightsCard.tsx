import React, { useState } from 'react'
import { dataRights, ERASE_CONFIRMATION_PHRASE } from '../api/endpoints'
import type { ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'

/**
 * GDPR (EU) / POPIA (South Africa) subject-access + erasure controls.
 *
 * Backed by DataRightsController, which is [Authorize(Roles = "Owner,Admin")].
 * We mirror that check here so non-privileged users don't see controls that
 * would only 403 — the server remains the authority.
 *
 * The confirmation phrase is likewise enforced server-side (a mismatch is a
 * 400 `confirmation_required`). The client-side comparison only exists so the
 * button state matches what the server will accept.
 */

const ERASE_ROLES = ['owner', 'admin']

function errorMessage(err: unknown): string {
  if (err && typeof err === 'object' && 'message' in err) {
    return (err as ApiError).message
  }
  return err instanceof Error ? err.message : 'Request failed'
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' })
}

export default function DataRightsCard(): React.ReactElement | null {
  const user = useAuthStore(s => s.user)

  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [exportDone, setExportDone] = useState<string | null>(null)

  const [showErase, setShowErase] = useState(false)
  const [phrase, setPhrase] = useState('')
  const [erasing, setErasing] = useState(false)
  const [eraseError, setEraseError] = useState<string | null>(null)
  const [pendingUntil, setPendingUntil] = useState<string | null>(null)

  const [cancelling, setCancelling] = useState(false)
  const [cancelMessage, setCancelMessage] = useState<string | null>(null)

  // Hide the whole card from roles the server would reject anyway.
  if (!user || !ERASE_ROLES.includes(user.role?.toLowerCase() ?? '')) return null

  const handleExport = async () => {
    setExporting(true)
    setExportError(null)
    setExportDone(null)
    try {
      const { blob, filename } = await dataRights.exportArchive()
      const url = URL.createObjectURL(blob)
      try {
        const a = document.createElement('a')
        a.href = url
        a.download = filename ?? 'planscape-export.zip'
        document.body.appendChild(a)
        a.click()
        a.remove()
      } finally {
        URL.revokeObjectURL(url)
      }
      setExportDone(filename ?? 'planscape-export.zip')
    } catch (err) {
      setExportError(errorMessage(err))
    } finally {
      setExporting(false)
    }
  }

  const handleErase = async () => {
    setErasing(true)
    setEraseError(null)
    try {
      const res = await dataRights.erase(phrase)
      setPendingUntil(res.erasureCompletesAt)
      setShowErase(false)
      setPhrase('')
      setCancelMessage(null)
    } catch (err) {
      setEraseError(errorMessage(err))
    } finally {
      setErasing(false)
    }
  }

  const handleCancelErase = async () => {
    setCancelling(true)
    setEraseError(null)
    try {
      const res = await dataRights.cancelErase()
      setPendingUntil(null)
      setCancelMessage(res.message ?? 'Erasure cancelled; organisation restored.')
    } catch (err) {
      setEraseError(errorMessage(err))
    } finally {
      setCancelling(false)
    }
  }

  return (
    <div className="ps-card space-y-4">
      <div>
        <h2 className="text-ps-text font-semibold">Data Rights</h2>
        <p className="text-ps-muted text-xs mt-1">
          Subject-access and erasure rights under the GDPR and POPIA, for
          <span className="text-ps-text"> {user.tenantName || 'your organisation'}</span>.
          Available to Owners and Admins.
        </p>
      </div>

      {/* ── Export ── */}
      <div className="space-y-2">
        <label className="ps-label">Export all data</label>
        <p className="text-ps-muted text-xs">
          Downloads a ZIP containing every record Planscape holds for your organisation —
          organisation profile, users, projects, memberships, issues, documents, model
          metadata, audit log, subscriptions and invoices. Read-only: exporting changes
          nothing.
        </p>
        <div className="flex items-center gap-3">
          <button onClick={handleExport} disabled={exporting} className="ps-btn-secondary">
            {exporting ? 'Preparing export…' : 'Download export (.zip)'}
          </button>
          {exportDone && <span className="text-ps-green text-xs">✓ Saved {exportDone}</span>}
        </div>
        {exportError && <div className="text-ps-red text-xs">✗ {exportError}</div>}
      </div>

      <div className="border-t border-ps-elevated" />

      {/* ── Erase ── */}
      <div className="space-y-2">
        <label className="ps-label">Erase all data</label>

        {pendingUntil ? (
          <>
            <div className="text-ps-amber text-xs">
              ⚠ Erasure scheduled. This organisation is frozen now, and all of its data will
              be permanently deleted on <span className="font-semibold">{formatDate(pendingUntil)}</span>.
              Until that date the erasure is <span className="font-semibold">fully reversible</span> —
              cancelling restores the organisation and every record in it, exactly as it was.
              Once the deletion runs it cannot be undone.
            </div>
            <button onClick={handleCancelErase} disabled={cancelling} className="ps-btn-secondary">
              {cancelling ? 'Cancelling…' : 'Cancel erasure and restore'}
            </button>
          </>
        ) : (
          <>
            <p className="text-ps-muted text-xs">
              Freezes this organisation immediately and schedules permanent deletion of all
              of its Planscape data in <span className="text-ps-text">30 days</span>. For those
              30 days the erasure is <span className="text-ps-text">fully reversible</span> —
              &ldquo;Cancel erasure&rdquo; here restores the organisation and everything in it.
              After the 30 days the deletion is permanent and cannot be undone.
            </p>

            {cancelMessage && <div className="text-ps-green text-xs">✓ {cancelMessage}</div>}

            {!showErase ? (
              <button onClick={() => setShowErase(true)} className="ps-btn-danger">
                Erase all data…
              </button>
            ) : (
              <div className="space-y-2">
                <p className="text-ps-muted text-xs">
                  Type <code className="bg-ps-elevated px-1 rounded">{ERASE_CONFIRMATION_PHRASE}</code> to confirm.
                </p>
                <input
                  value={phrase}
                  onChange={e => setPhrase(e.target.value)}
                  className="ps-input font-mono"
                  placeholder={ERASE_CONFIRMATION_PHRASE}
                  autoComplete="off"
                  spellCheck={false}
                />
                <div className="flex items-center gap-3">
                  <button
                    onClick={handleErase}
                    disabled={erasing || phrase !== ERASE_CONFIRMATION_PHRASE}
                    className="ps-btn-danger"
                  >
                    {erasing ? 'Scheduling…' : 'Freeze and schedule deletion'}
                  </button>
                  <button
                    onClick={() => { setShowErase(false); setPhrase(''); setEraseError(null) }}
                    disabled={erasing}
                    className="ps-btn-secondary"
                  >
                    Cancel
                  </button>
                </div>
              </div>
            )}
          </>
        )}

        {eraseError && <div className="text-ps-red text-xs">✗ {eraseError}</div>}
      </div>
    </div>
  )
}
