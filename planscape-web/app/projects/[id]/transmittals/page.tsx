'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import {
  Badge,
  Button,
  DataGrid,
  Input,
  Modal,
  PageHeader,
  toneForStatus,
  useToast,
  type Column,
} from '@/components/ui';
import { createTransmittal, listTransmittals, transmittalAction } from '@/lib/data';
import type { Transmittal } from '@/lib/types';

export const dynamic = 'force-dynamic';

/**
 * U4 — Transmittals.
 *
 * Per the grid contract this is deliberately NOT an editable grid: the write
 * surface is `PUT …/transmittals/{id}/{send|acknowledge|respond}` — a state
 * machine, not fields. An inline status cell would let a user pick a transition
 * the server will reject, so status is read-only and the single legal next
 * action renders as a row button.
 */
const NEXT_ACTION: Record<string, 'send' | 'acknowledge' | 'respond' | undefined> = {
  DRAFT: 'send',
  SENT: 'acknowledge',
  ACKNOWLEDGED: 'respond',
};
const ACTION_LABEL: Record<string, string> = { send: 'Send', acknowledge: 'Acknowledge', respond: 'Respond' };

export default function TransmittalsPage() {
  const { id: projectId } = useParams<{ id: string }>();
  const { toast } = useToast();
  const [items, setItems] = useState<Transmittal[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newOpen, setNewOpen] = useState(false);
  const [recipient, setRecipient] = useState('');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);

  // Respond carries optional notes, so it gets a modal instead of a window.prompt.
  const [respondFor, setRespondFor] = useState<Transmittal | null>(null);
  const [responseNotes, setResponseNotes] = useState('');

  const load = useCallback(() => {
    listTransmittals(projectId)
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load transmittals'));
  }, [projectId]);

  useEffect(load, [load]);

  async function onCreate() {
    if (!recipient.trim()) return;
    setBusy(true);
    try {
      await createTransmittal(projectId, { recipient: recipient.trim(), notes: notes.trim() || undefined });
      toast(`Transmittal to ${recipient.trim()} created`, 'success');
      setRecipient('');
      setNotes('');
      setNewOpen(false);
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed to create transmittal', 'error');
    } finally {
      setBusy(false);
    }
  }

  async function runAction(t: Transmittal, action: 'send' | 'acknowledge' | 'respond', body?: { responseNotes?: string }) {
    try {
      await transmittalAction(projectId, t.id, action, body);
      toast(`${t.transmittalCode} — ${ACTION_LABEL[action].toLowerCase()}ed`, 'success');
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : `${ACTION_LABEL[action]} failed`, 'error');
    }
  }

  const columns: Column<Transmittal>[] = [
    { key: 'transmittalCode', header: 'Code', className: 'w-40 font-mono text-xs' },
    { key: 'recipient', header: 'Recipient', className: 'min-w-[12rem]' },
    {
      key: 'status',
      header: 'Status',
      className: 'w-36',
      render: (t) => <Badge tone={toneForStatus(t.status)}>{t.status}</Badge>,
    },
    { key: 'notes', header: 'Notes', className: 'min-w-[12rem]' },
    {
      key: 'createdAt',
      header: 'Created',
      className: 'w-28',
      render: (t) =>
        t.createdAt ? new Date(t.createdAt).toLocaleDateString() : <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'actions',
      header: '',
      className: 'w-32',
      sortable: false,
      render: (t) => {
        const action = NEXT_ACTION[t.status];
        if (!action) return <span className="text-fg-subtle">—</span>;
        return (
          <Button
            size="sm"
            onClick={() => {
              if (action === 'respond') {
                setResponseNotes('');
                setRespondFor(t);
              } else {
                void runAction(t, action);
              }
            }}
          >
            {ACTION_LABEL[action]}
          </Button>
        );
      },
    },
  ];

  return (
    <AppShell>
      <PageHeader
        title="Transmittals"
        description="Status advances through send → acknowledge → respond actions, not by editing."
        actions={
          <Button variant="primary" onClick={() => setNewOpen(true)}>
            New transmittal
          </Button>
        }
      />
      <DataGrid<Transmittal>
        rows={items}
        columns={columns}
        rowId={(t) => t.id}
        loading={!items && !error}
        error={error}
        emptyTitle="No transmittals"
        emptyDescription="Create one to formally issue documents to a recipient."
      />

      <Modal
        open={newOpen}
        onOpenChange={setNewOpen}
        title="New transmittal"
        footer={
          <>
            <Button onClick={() => setNewOpen(false)}>Cancel</Button>
            <Button variant="primary" onClick={() => void onCreate()} disabled={busy || !recipient.trim()}>
              {busy ? 'Creating…' : 'Create'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">Recipient</span>
            <Input value={recipient} onChange={(e) => setRecipient(e.target.value)} autoFocus />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">Notes (optional)</span>
            <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
      </Modal>

      <Modal
        open={!!respondFor}
        onOpenChange={(o) => !o && setRespondFor(null)}
        title={`Respond to ${respondFor?.transmittalCode ?? ''}`}
        footer={
          <>
            <Button onClick={() => setRespondFor(null)}>Cancel</Button>
            <Button
              variant="primary"
              onClick={() => {
                const t = respondFor;
                setRespondFor(null);
                if (t) void runAction(t, 'respond', { responseNotes: responseNotes.trim() || undefined });
              }}
            >
              Respond
            </Button>
          </>
        }
      >
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-fg-muted">Response notes (optional)</span>
          <Input value={responseNotes} onChange={(e) => setResponseNotes(e.target.value)} autoFocus />
        </label>
      </Modal>
    </AppShell>
  );
}
