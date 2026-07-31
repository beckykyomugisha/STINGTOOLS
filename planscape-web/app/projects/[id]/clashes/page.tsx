'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import {
  Badge,
  Button,
  DataGrid,
  PageHeader,
  Select,
  toneForStatus,
  useToast,
  type Column,
} from '@/components/ui';
import { listClashes, runClashDetection, updateClash } from '@/lib/data';
import type { ClashRecord, ClashStatus } from '@/lib/types';

const STATUSES: ClashStatus[] = ['NEW', 'ACKNOWLEDGED', 'RESOLVED', 'CLOSED'];

/** Severity has its own scale, so it doesn't go through toneForStatus. */
function severityTone(s: string): 'danger' | 'warning' | 'neutral' {
  if (s === 'CRITICAL') return 'danger';
  if (s === 'MAJOR') return 'warning';
  return 'neutral';
}

/**
 * U4 — Clashes grid. Editable columns match `ClashUpdateDto` exactly: status,
 * assignedTo, resolutionNote. Everything else (severity, geometry, volumes) is
 * detector output and has no write endpoint, so it stays read-only rather than
 * looking editable and silently failing.
 */
export default function ClashesPage() {
  const { id: projectId } = useParams<{ id: string }>();
  const router = useRouter();
  const { toast } = useToast();
  const [clashes, setClashes] = useState<ClashRecord[] | null>(null);
  const [filter, setFilter] = useState<ClashStatus | 'ALL'>('NEW');
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const load = useCallback(() => {
    setClashes(null);
    setError(null);
    listClashes(projectId, filter === 'ALL' ? {} : { status: filter })
      .then((r) => setClashes(r.items ?? []))
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load clashes'));
  }, [projectId, filter]);

  useEffect(load, [load]);

  async function onRun() {
    setRunning(true);
    try {
      const r = await runClashDetection(projectId);
      toast(`Detection complete — ${r.found ?? 0} found, ${r.created ?? 0} new, ${r.critical ?? 0} critical.`, 'success');
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Clash detection failed', 'error');
    } finally {
      setRunning(false);
    }
  }

  const columns: Column<ClashRecord>[] = [
    {
      key: 'elements',
      header: 'Elements',
      className: 'min-w-[16rem]',
      value: (c) => `${c.elementAType || ''} ${c.elementBType || ''}`,
      render: (c) => (
        <span className="font-medium">
          {c.elementAType || 'Element A'} ↔ {c.elementBType || 'Element B'}
        </span>
      ),
    },
    {
      key: 'severity',
      header: 'Severity',
      className: 'w-28',
      render: (c) => <Badge tone={severityTone(c.severity)}>{c.severity}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      className: 'w-40',
      render: (c) => <Badge tone={toneForStatus(c.status)}>{c.status}</Badge>,
      edit: { options: STATUSES, save: (c, v) => updateClash(projectId, c.id, { status: v as ClashStatus }) },
    },
    {
      key: 'assignedTo',
      header: 'Assigned to',
      className: 'w-40',
      edit: { save: (c, v) => updateClash(projectId, c.id, { assignedTo: v } as Partial<ClashRecord>) },
    },
    {
      key: 'resolutionNote',
      header: 'Resolution note',
      className: 'min-w-[14rem]',
      edit: { save: (c, v) => updateClash(projectId, c.id, { resolutionNote: v } as Partial<ClashRecord>) },
    },
    { key: 'discipline', header: 'Discipline', className: 'w-28' },
    {
      key: 'overlapVolumeMm3',
      header: 'Overlap',
      className: 'w-32 text-right',
      value: (c) => c.overlapVolumeMm3 ?? 0,
      render: (c) =>
        typeof c.overlapVolumeMm3 === 'number' ? (
          `${Math.round(c.overlapVolumeMm3).toLocaleString()} mm³`
        ) : (
          <span className="text-fg-subtle">—</span>
        ),
    },
  ];

  return (
    <AppShell>
      <PageHeader
        title="Clashes"
        description="Status, assignee and resolution notes are editable inline."
        actions={
          <Button variant="primary" onClick={onRun} disabled={running}>
            {running ? 'Running…' : 'Run detection'}
          </Button>
        }
      />
      <DataGrid<ClashRecord>
        rows={clashes}
        columns={columns}
        rowId={(c) => c.id}
        loading={!clashes && !error}
        error={error}
        selectable
        onRowClick={(c) => router.push(`/projects/${projectId}/clashes/${c.id}`)}
        emptyTitle="No clashes"
        emptyDescription="Run detection to populate this list."
        toolbar={() => (
          <Select
            value={filter}
            onChange={(e) => setFilter(e.target.value as ClashStatus | 'ALL')}
            aria-label="Filter by status"
            className="h-7 w-40"
          >
            <option value="ALL">All statuses</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
        )}
      />
    </AppShell>
  );
}
