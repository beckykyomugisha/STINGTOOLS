'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import {
  Badge,
  Button,
  DataGrid,
  Input,
  MenuItem,
  Modal,
  PageHeader,
  Select,
  useToast,
  type Column,
} from '@/components/ui';
import { inviteMember, listMembers, listProjectRoles, removeMember, updateMemberRole } from '@/lib/data';
import type { Iso19650Role, ProjectMember } from '@/lib/types';

export const dynamic = 'force-dynamic';

const PROJECT_ROLES = ['Viewer', 'Contributor', 'Coordinator', 'Manager', 'Owner', 'Admin'];

/**
 * U4 — Members grid. Editable columns are `projectRole` + `iso19650Role`, which
 * is exactly what `PUT …/members/{id}` accepts. Email and display name come from
 * the user account and have no project-level write endpoint.
 *
 * Invite moves into a Modal: it was a form permanently occupying the top of the
 * page for an action taken once a month.
 */
export default function MembersPage() {
  const { id: projectId } = useParams<{ id: string }>();
  const { toast } = useToast();
  const [members, setMembers] = useState<ProjectMember[] | null>(null);
  const [isoRoles, setIsoRoles] = useState<Iso19650Role[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [email, setEmail] = useState('');
  const [inviteRole, setInviteRole] = useState('Contributor');
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    listMembers(projectId)
      .then(setMembers)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load members'));
  }, [projectId]);

  useEffect(load, [load]);

  useEffect(() => {
    // Optional: an API without the roles endpoint should cost the dropdown its
    // options, not break the page.
    listProjectRoles(projectId)
      .then(setIsoRoles)
      .catch(() => setIsoRoles([]));
  }, [projectId]);

  async function onInvite() {
    if (!email.trim()) return;
    setBusy(true);
    try {
      const r = await inviteMember(projectId, { email: email.trim(), projectRole: inviteRole });
      toast(
        r.emailSent
          ? `Invite emailed to ${email.trim()}.`
          : r.isPending
            ? `Invited ${email.trim()} — pending (email not configured).`
            : `${email.trim()} added.`,
        'success',
      );
      setEmail('');
      setInviteOpen(false);
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Invite failed', 'error');
    } finally {
      setBusy(false);
    }
  }

  async function onRemove(m: ProjectMember) {
    if (!window.confirm(`Remove ${m.displayName || m.email} from this project?`)) return;
    try {
      await removeMember(projectId, m.id);
      toast(`${m.displayName || m.email} removed`, 'success');
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Remove failed', 'error');
    }
  }

  const columns: Column<ProjectMember>[] = [
    {
      key: 'displayName',
      header: 'Name',
      className: 'min-w-[12rem]',
      render: (m) => m.displayName || <span className="text-fg-subtle">—</span>,
    },
    { key: 'email', header: 'Email', className: 'min-w-[14rem]' },
    {
      key: 'projectRole',
      header: 'Project role',
      className: 'w-40',
      render: (m) => <Badge tone="accent">{m.projectRole}</Badge>,
      edit: { options: PROJECT_ROLES, save: (m, v) => updateMemberRole(projectId, m.id, { projectRole: v }) },
    },
    {
      key: 'iso19650Role',
      header: 'ISO 19650 role',
      className: 'w-48',
      // Only editable when the server told us the vocabulary — a free-text ISO
      // role would be rejected, so a dropdown with no options is worse than none.
      ...(isoRoles.length
        ? {
            edit: {
              options: ['', ...isoRoles.map((r) => r.code)],
              save: (m: ProjectMember, v: string) => updateMemberRole(projectId, m.id, { iso19650Role: v }),
            },
          }
        : {}),
    },
    {
      key: 'joinedAt',
      header: 'Joined',
      className: 'w-28',
      render: (m) =>
        m.joinedAt ? new Date(m.joinedAt).toLocaleDateString() : <span className="text-fg-subtle">—</span>,
    },
    // Already in the payload, never shown — and it is the field that answers
    // "who let this person in", which is the question an audit actually asks.
    { key: 'invitedBy', header: 'Invited by', className: 'w-40' },
    {
      key: 'actions',
      header: '',
      className: 'w-20',
      sortable: false,
      render: (m) => (
        <Button size="sm" variant="ghost" onClick={() => void onRemove(m)}>
          Remove
        </Button>
      ),
    },
  ];

  return (
    <AppShell>
      <PageHeader
        title="Members"
        description="Roles are editable inline."
        actions={
          <Button variant="primary" onClick={() => setInviteOpen(true)}>
            Invite member
          </Button>
        }
      />
      <DataGrid<ProjectMember>
        rows={members}
        columns={columns}
        rowId={(m) => m.id}
        loading={!members && !error}
        error={error}
        rowMenu={(m, close) => (
          <MenuItem
            onClick={() => {
              close();
              void onRemove(m);
            }}
          >
            Remove from project
          </MenuItem>
        )}
        emptyTitle="No members yet"
        emptyDescription="Invite someone to give them access to this project."
      />

      <Modal
        open={inviteOpen}
        onOpenChange={setInviteOpen}
        title="Invite member"
        description="They receive an email invite if SMTP is configured on the server."
        footer={
          <>
            <Button onClick={() => setInviteOpen(false)}>Cancel</Button>
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
            <span className="text-fg-muted">Project role</span>
            <Select value={inviteRole} onChange={(e) => setInviteRole(e.target.value)}>
              {PROJECT_ROLES.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </Select>
          </label>
        </div>
      </Modal>
    </AppShell>
  );
}
