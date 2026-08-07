'use client';

/**
 * Firm-level team page.
 *
 * Two invite paths exist server-side and only one had a UI:
 *  - `POST /api/projects/{id}/members/invite` — a seat on ONE project. Fully
 *    wired since U4 (`/projects/{id}/members`).
 *  - `POST /api/tenant/invite` — a person in the FIRM, independent of any
 *    project. Had no client code anywhere. This page is its entry point.
 *
 * The whole of `TenantAdminController` is `[Authorize(Roles = "Owner,Admin")]`,
 * so a Coordinator loading this page gets a 403. That is the correct answer to a
 * legitimate request, and it renders as "you need Owner or Admin", not as a
 * failure banner.
 *
 * Removing a user (`DELETE /api/tenant/users/{id}`) exists server-side and is
 * deliberately NOT wired here — see docs/ACC_UI_SHELL_GRID_CONTRACT.md §6.
 */

import { useCallback, useEffect, useState } from 'react';
import { AppShell } from '@/components/AppShell';
import {
  Badge,
  Button,
  Card,
  DataGrid,
  ErrorNote,
  ForbiddenNote,
  ForbiddenPanel,
  Input,
  Modal,
  PageHeader,
  Select,
  useToast,
  type Column,
} from '@/components/ui';
import { ApiError, isForbidden } from '@/lib/api';
import { getTenantDashboard, inviteTenantMember } from '@/lib/data';
import type { QuotaExceeded, TenantDashboard, TenantUser } from '@/lib/types';

// The server maps anything that isn't "Author" to "Coordinator". Offering more
// than the two it actually meters would be a lie about what the plan counts.
const TENANT_ROLES = ['Coordinator', 'Author'];

export default function TeamPage() {
  const { toast } = useToast();
  const [data, setData] = useState<TenantDashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [role, setRole] = useState('Coordinator');
  const [busy, setBusy] = useState(false);
  const [inviteError, setInviteError] = useState<string | null>(null);
  // Kept alongside the message rather than derived from it: deciding the
  // treatment by matching the sentence would be exactly the string-sniffing
  // #624 was filed for.
  const [inviteForbidden, setInviteForbidden] = useState(false);

  const load = useCallback(() => {
    getTenantDashboard()
      .then((d) => {
        setData(d);
        setForbidden(false);
        setError(null);
      })
      .catch((e) => {
        if (isForbidden(e)) {
          setForbidden(true);
          return;
        }
        setError(e instanceof Error ? e.message : 'Failed to load the team');
      });
  }, []);

  useEffect(load, [load]);

  async function onInvite() {
    const addr = email.trim();
    if (!addr) return;
    setBusy(true);
    setInviteError(null);
    setInviteForbidden(false);
    try {
      await inviteTenantMember({ email: addr, displayName: displayName.trim() || addr, role });
      toast(`${addr} invited as ${role}.`, 'success');
      setEmail('');
      setDisplayName('');
      setOpen(false);
      load();
    } catch (e) {
      setInviteError(inviteMessage(e));
      setInviteForbidden(isForbidden(e));
    } finally {
      setBusy(false);
    }
  }

  const columns: Column<TenantUser>[] = [
    {
      key: 'displayName',
      header: 'Name',
      className: 'min-w-[12rem] font-medium',
      render: (u) => u.displayName || <span className="text-fg-subtle">—</span>,
    },
    { key: 'email', header: 'Email', className: 'min-w-[14rem]' },
    {
      key: 'role',
      header: 'Role',
      className: 'w-36',
      render: (u) => (u.role ? <Badge tone="accent">{u.role}</Badge> : <span className="text-fg-subtle">—</span>),
    },
    {
      key: 'iso19650Role',
      header: 'ISO 19650',
      className: 'w-24',
    },
    {
      key: 'isActive',
      header: 'State',
      className: 'w-28',
      value: (u) => (u.isActive ? 'Active' : 'Invited'),
      // An invited user is a planted row with no password yet — showing that as
      // "Active" would make an unfinished invite look complete.
      render: (u) =>
        u.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="warning">Invited</Badge>,
    },
    {
      key: 'lastLoginAt',
      header: 'Last sign-in',
      className: 'w-32',
      render: (u) =>
        u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleDateString() : <span className="text-fg-subtle">Never</span>,
    },
  ];

  if (forbidden) {
    // Same two sentences as before, in the shared treatment. This page was the
    // model implementation for #558 — the point of moving it is that the other
    // surfaces stop re-inventing the treatment, not that this one changes.
    return (
      <AppShell>
        <PageHeader title="Team" />
        <ForbiddenPanel
          message={<>You need the Owner or Admin role to manage your firm&rsquo;s team.</>}
          hint="Project-level membership is managed per project, under Members — that does not require Admin."
        />
      </AppShell>
    );
  }

  return (
    <AppShell>
      <PageHeader
        title="Team"
        description={data ? `${data.tenant.name} · ${data.tenant.plan ?? 'plan unknown'}` : 'Everyone in your firm'}
        actions={
          <Button variant="primary" onClick={() => setOpen(true)}>
            Invite to firm
          </Button>
        }
      />

      {error && (
        <div className="mb-4">
          <ErrorNote>{error}</ErrorNote>
        </div>
      )}

      {data && (
        <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Quota label="Authors" axis={data.usage.authors} />
          <Quota label="Coordinators" axis={data.usage.coordinators} />
          <Quota label="Projects" axis={data.usage.projects} />
          <Quota
            label="Storage (MB)"
            axis={{ current: data.usage.storage.currentMb, max: data.usage.storage.maxMb }}
          />
        </div>
      )}

      <DataGrid<TenantUser>
        rows={data?.users ?? null}
        columns={columns}
        rowId={(u) => u.id}
        loading={!data && !error}
        emptyTitle="No one here yet"
        emptyDescription="Invite a colleague to the firm — they can then be added to projects."
      />

      <Modal
        open={open}
        onOpenChange={(o) => {
          setOpen(o);
          if (!o) setInviteError(null);
        }}
        title="Invite to firm"
        description="Firm-wide access. Add them to individual projects afterwards, under a project's Members."
        footer={
          <>
            <Button onClick={() => setOpen(false)} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" onClick={() => void onInvite()} disabled={busy || !email.trim()}>
              {busy ? 'Inviting…' : 'Send invite'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">Email</span>
            <Input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="person@firm.com"
              autoFocus
            />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">Name</span>
            <Input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Optional — defaults to the email"
            />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">Role</span>
            <Select value={role} onChange={(e) => setRole(e.target.value)}>
              {TENANT_ROLES.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </Select>
            <span className="text-xs text-fg-subtle">
              Authors and Coordinators are capped separately by your plan.
            </span>
          </label>
          {inviteError &&
            (inviteForbidden ? (
              // Same sentence as before, in the forbidden treatment. It was
              // rendering in the danger colours, which said "something broke"
              // about the one branch of inviteMessage() that is a permission
              // answer rather than a failure.
              <ForbiddenNote>{inviteError}</ForbiddenNote>
            ) : (
              <ErrorNote>{inviteError}</ErrorNote>
            ))}
        </div>
      </Modal>
    </AppShell>
  );
}

/**
 * Turn the three answers this endpoint actually gives into sentences. Without
 * this, a full plan reads as the literal string "quota_exceeded" — the server's
 * useful sentence lives in `reason`, not in `error`.
 */
function inviteMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return e instanceof Error ? e.message : 'Invite failed.';
  if (e.status === 402) {
    const q = e.body as QuotaExceeded | undefined;
    const detail = q?.reason ? ` ${q.reason}` : '';
    return `Your plan's seat limit is full.${detail} Upgrade your plan to invite more people.`;
  }
  if (e.status === 409) return 'Someone already has an account with that email.';
  // Wording unchanged (#558). What changed is the treatment it renders in —
  // see `inviteForbidden` at the call site.
  if (isForbidden(e)) return 'You need the Owner or Admin role to invite people to the firm.';
  return e.message;
}

function Quota({ label, axis }: { label: string; axis: { current: number; max: number } }) {
  // int.MaxValue is the server's "unlimited" — rendering it as 2147483647 would
  // read as a bug.
  const unlimited = axis.max >= 2147483647;
  const pct = unlimited || !axis.max ? 0 : Math.min(100, Math.round((axis.current / axis.max) * 100));
  const tone = pct >= 100 ? 'bg-danger' : pct >= 80 ? 'bg-warning' : 'bg-accent';
  return (
    <Card>
      <div className="text-xs text-fg-muted">{label}</div>
      <div className="mt-0.5 text-2xl font-semibold text-fg">
        {axis.current}
        <span className="text-sm font-normal text-fg-muted"> / {unlimited ? '∞' : axis.max}</span>
      </div>
      {!unlimited && (
        <div className="mt-2 h-1 w-full overflow-hidden rounded bg-surface-3">
          <div className={`h-full ${tone}`} style={{ width: `${pct}%` }} />
        </div>
      )}
    </Card>
  );
}
