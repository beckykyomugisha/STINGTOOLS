'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listMembers, inviteMember, updateMemberRole, removeMember } from '@/lib/data';
import type { ProjectMember } from '@/lib/types';

export const dynamic = 'force-dynamic';

const PROJECT_ROLES = ['Viewer', 'Contributor', 'Coordinator', 'Manager', 'Owner', 'Admin'];

export default function MembersPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [members, setMembers] = useState<ProjectMember[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [email, setEmail] = useState('');
  const [inviteRole, setInviteRole] = useState('Contributor');
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    listMembers(projectId)
      .then(setMembers)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load members'));
  }, [projectId]);

  useEffect(load, [load]);

  async function onInvite(e: React.FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const r = await inviteMember(projectId, { email: email.trim(), projectRole: inviteRole });
      setNotice(
        r.emailSent
          ? `Invite emailed to ${email.trim()}.`
          : r.isPending
            ? `Invited ${email.trim()} (pending — email not configured, share the link from the server).`
            : `${email.trim()} added.`,
      );
      setEmail('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Invite failed');
    } finally {
      setBusy(false);
    }
  }

  async function onRoleChange(m: ProjectMember, role: string) {
    setMembers((cur) => cur?.map((x) => (x.id === m.id ? { ...x, projectRole: role } : x)) ?? null);
    try {
      await updateMemberRole(projectId, m.id, { projectRole: role });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to change role');
      load();
    }
  }

  async function onRemove(m: ProjectMember) {
    if (!confirm(`Remove ${m.displayName} from the project?`)) return;
    setMembers((cur) => cur?.filter((x) => x.id !== m.id) ?? null);
    try {
      await removeMember(projectId, m.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove member');
      load();
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
          ← Project
        </Link>
        <h1 className="text-xl font-semibold">Members</h1>
      </div>

      {/* Invite */}
      <form onSubmit={onInvite} className="mb-4 flex flex-wrap items-end gap-2 rounded-lg border border-slate-200 bg-white p-4">
        <label className="block">
          <span className="text-sm text-slate-600">Invite by email</span>
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="person@firm.com"
            className="mt-1 block w-64 rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <select
          value={inviteRole}
          onChange={(e) => setInviteRole(e.target.value)}
          className="rounded border border-slate-300 px-2 py-1.5 text-sm"
        >
          {PROJECT_ROLES.map((r) => (
            <option key={r}>{r}</option>
          ))}
        </select>
        <button
          type="submit"
          disabled={busy || !email.trim()}
          className="rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Inviting…' : 'Invite'}
        </button>
      </form>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {notice && <p className="mb-3 rounded bg-green-50 px-3 py-2 text-sm text-green-700">{notice}</p>}
      {!members && !error && <p className="text-slate-400">Loading…</p>}
      {members && members.length === 0 && <p className="text-slate-500">No members.</p>}

      {members && members.length > 0 && (
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
          {members.map((m) => (
            <li key={m.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <div className="truncate text-sm font-medium">{m.displayName}</div>
                <div className="truncate text-xs text-slate-400">
                  {m.email}
                  {m.iso19650Role ? ` · ${m.iso19650Role}` : ''}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                <select
                  value={PROJECT_ROLES.includes(m.projectRole) ? m.projectRole : ''}
                  onChange={(e) => onRoleChange(m, e.target.value)}
                  className="rounded border border-slate-300 px-2 py-1 text-xs"
                >
                  {!PROJECT_ROLES.includes(m.projectRole) && <option value="">{m.projectRole || '—'}</option>}
                  {PROJECT_ROLES.map((r) => (
                    <option key={r}>{r}</option>
                  ))}
                </select>
                <button
                  onClick={() => onRemove(m)}
                  className="rounded border border-slate-300 px-2 py-1 text-xs text-red-600 hover:bg-red-50"
                >
                  Remove
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <p className="mt-2 text-xs text-slate-400">Managing members requires a Manager+ project role.</p>
    </AppShell>
  );
}
