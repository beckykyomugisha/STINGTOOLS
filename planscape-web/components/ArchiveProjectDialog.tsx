'use client';

import { useEffect, useState } from 'react';
import { Button, Input, Modal } from '@/components/ui';
import { ApiError } from '@/lib/api';
import { archiveProject } from '@/lib/data';

/**
 * Type-the-code confirmation for archiving a project — the GitHub
 * "type the repository name" pattern.
 *
 * This deliberately mirrors what `DELETE /api/projects/{id}` already enforces
 * server-side (`?confirmCode=<Project.Code>`), rather than shipping a weaker
 * client confirm and letting the server's 400 be the real gate. The server check
 * stays the backstop; this is the part the user actually reads.
 *
 * Archiving is a SOFT delete — say so in the dialog, because "Archive" next to a
 * red button reads as destructive and people hesitate over the wrong thing.
 */
export function ArchiveProjectDialog({
  open,
  onOpenChange,
  projectId,
  projectCode,
  projectName,
  onArchived,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  projectId: string;
  /** The project's own `code` — this is what the user must retype. */
  projectCode: string;
  projectName?: string;
  onArchived?: () => void;
}) {
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Re-opening the dialog must not inherit the previous attempt's typed code or
  // its error banner.
  useEffect(() => {
    if (open) {
      setTyped('');
      setError(null);
    }
  }, [open]);

  // Case-insensitive + trimmed, matching the server's OrdinalIgnoreCase compare.
  // A stricter client check would reject codes the server accepts.
  const matches = typed.trim().toLowerCase() === (projectCode ?? '').trim().toLowerCase();

  async function onConfirm() {
    if (!matches) return;
    setBusy(true);
    setError(null);
    try {
      await archiveProject(projectId, typed.trim());
      onOpenChange(false);
      onArchived?.();
    } catch (e) {
      // 403 is a real, expected answer — the caller is neither the project author
      // nor a tenant admin. Name that instead of dropping a generic toast.
      if (e instanceof ApiError && e.status === 403) {
        setError(
          'You do not have permission to archive this project. Only the person who created it, or a tenant Owner/Admin, can.',
        );
      } else if (e instanceof ApiError && e.status === 400) {
        setError(`${e.message} The code for this project is "${projectCode}".`);
      } else {
        setError(e instanceof Error ? e.message : 'Archive failed.');
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Archive project"
      description="Archiving is reversible — nothing is deleted."
      footer={
        <>
          <Button onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button variant="danger" onClick={() => void onConfirm()} disabled={!matches || busy}>
            {busy ? 'Archiving…' : 'Archive project'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-fg-muted">
          {projectName ? <span className="font-medium text-fg">{projectName}</span> : 'This project'} will be marked
          archived. Its issues, documents and models are kept and it stays visible under the archived filter — it just
          stops counting towards active projects.
        </p>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-fg-muted">
            Type <span className="rounded bg-surface-3 px-1 font-mono text-fg">{projectCode}</span> to confirm
          </span>
          <Input
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            placeholder={projectCode}
            aria-label="Project code confirmation"
            autoFocus
            autoComplete="off"
          />
        </label>
        {error && (
          <p role="alert" className="rounded bg-danger-subtle px-3 py-2 text-sm text-danger">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
}
