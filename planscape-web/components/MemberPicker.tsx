'use client';

import { useCallback, useEffect, useState } from 'react';
import { Button, Input, Select } from '@/components/ui';
import { inviteMember, listMembers } from '@/lib/data';
import type { ProjectMember } from '@/lib/types';
import { cn } from '@/lib/cn';

/**
 * The one way to pick a human in this app.
 *
 * Before this existed, every surface that needed a person — issue assignee,
 * transmittal recipient, meeting attendee — was a bare `<input>` you typed a
 * name into. That is not a cosmetic problem: the server resolves an assignee
 * against `ProjectMember` and returns 400 "not an active member of this
 * project" for anything it cannot match, so a typo silently became a failed
 * save. Worse, a *successful* free-text save wrote a display name with no
 * `AssigneeUserId` behind it, which means no notification, no push, no
 * "my issues" filter — the person was assigned in the UI's opinion only.
 *
 * So the roster is the source of truth and this component is the only door to
 * it. `listMembers(projectId)` — the same call the Members page uses — is the
 * canonical read; there is deliberately no second roster to drift from it.
 *
 * The escape hatch matters as much as the dropdown. Real projects need to
 * assign work to someone who is not in the system yet, and a picker that
 * refuses to do so just sends people back to free text somewhere else. Here,
 * "invite by email" calls `inviteMember` and the invitee becomes a real
 * `ProjectMember` (pending until they set a password) before being selected —
 * so the escape hatch *ends* in the canonical roster rather than bypassing it.
 * That is the deliberate difference from the plugin's Attendee Manager, which
 * also carries a genuine "external guest, do not invite" concept for people
 * who attend a meeting but must never get project access. The web app has no
 * such concept today, and inventing one here would be a second roster by
 * another name.
 */

export interface MemberPickerProps {
  projectId: string;
  /**
   * Selected user id(s). Single mode takes a `string | null`; multi mode takes
   * a `string[]`. Kept as ids rather than whole member objects so a parent can
   * hold form state without caring whether the roster has loaded yet.
   */
  value: string | null | string[];
  onChange: (value: string | null | string[]) => void;
  multiple?: boolean;
  /**
   * Fires with the resolved member objects behind `value` whenever the
   * selection or the roster changes. For callers whose stored field is still a
   * string (transmittal recipient) rather than an FK — they need the canonical
   * name/email, and this saves them fetching a second copy of the roster,
   * which is the drift this component exists to prevent.
   */
  onResolve?: (selected: ProjectMember[]) => void;
  /** Shown as the empty option in single mode. */
  placeholder?: string;
  /** Hide the invite row where the caller cannot grant project access. */
  allowInvite?: boolean;
  disabled?: boolean;
  className?: string;
  id?: string;
}

/** `displayName` is optional on the account, so fall back rather than render "". */
export function memberLabel(m: ProjectMember): string {
  return m.displayName?.trim() || m.email;
}

export function MemberPicker({
  projectId,
  value,
  onChange,
  multiple = false,
  onResolve,
  placeholder = 'Unassigned',
  allowInvite = true,
  disabled = false,
  className,
  id,
}: MemberPickerProps) {
  const [members, setMembers] = useState<ProjectMember[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [inviting, setInviting] = useState(false);
  const [inviteEmail, setInviteEmail] = useState('');
  const [showInvite, setShowInvite] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(
    () =>
      listMembers(projectId)
        .then((m) => {
          setMembers(m);
          setError(null);
          return m;
        })
        .catch((e) => {
          // A picker that silently renders zero options looks like a project
          // with no members. Say which of the two it is.
          setError(e instanceof Error ? e.message : 'Failed to load members');
          setMembers([]);
          return [] as ProjectMember[];
        }),
    [projectId],
  );

  useEffect(() => {
    void load();
  }, [load]);

  const selectedIds: string[] = multiple
    ? Array.isArray(value)
      ? value
      : []
    : typeof value === 'string' && value
      ? [value]
      : [];

  // Kept as an effect rather than folded into `toggle` so it also fires once
  // the roster finishes loading — a caller preselecting an id it was handed
  // still gets the resolved member without a second render pass of its own.
  const selectedKey = selectedIds.join(',');
  useEffect(() => {
    if (!onResolve || members === null) return;
    onResolve(members.filter((m) => selectedIds.includes(m.userId)));
    // `selectedKey` stands in for `selectedIds`, which is a fresh array each
    // render; `onResolve` is intentionally excluded so an inline arrow in the
    // caller does not re-fire this on every parent render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [members, selectedKey]);

  function toggle(userId: string) {
    if (!multiple) {
      onChange(userId || null);
      return;
    }
    const next = selectedIds.includes(userId)
      ? selectedIds.filter((v) => v !== userId)
      : [...selectedIds, userId];
    onChange(next);
  }

  async function onInvite() {
    const email = inviteEmail.trim();
    if (!email) return;
    setInviting(true);
    setNotice(null);
    try {
      await inviteMember(projectId, { email });
      // Re-read rather than optimistically splicing: the server decides the
      // member row id and whether this resolved to an existing account, and we
      // need its real userId to select it.
      const fresh = await load();
      const added = fresh.find((m) => m.email.toLowerCase() === email.toLowerCase());
      if (added) {
        if (multiple) {
          if (!selectedIds.includes(added.userId)) onChange([...selectedIds, added.userId]);
        } else {
          onChange(added.userId);
        }
        setNotice(`${memberLabel(added)} invited and selected.`);
      } else {
        // Invite accepted but the row is not readable back yet (pending
        // account). Don't claim a selection that did not happen.
        setNotice(`Invited ${email}. Select them once the invite is accepted.`);
      }
      setInviteEmail('');
      setShowInvite(false);
    } catch (e) {
      setNotice(e instanceof Error ? e.message : 'Invite failed');
    } finally {
      setInviting(false);
    }
  }

  const loading = members === null;

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      {multiple ? (
        <MultiSelectList
          members={members}
          selectedIds={selectedIds}
          onToggle={toggle}
          disabled={disabled}
          loading={loading}
        />
      ) : (
        <Select
          id={id}
          value={selectedIds[0] ?? ''}
          disabled={disabled || loading}
          onChange={(e) => onChange(e.target.value || null)}
        >
          <option value="">{loading ? 'Loading members…' : placeholder}</option>
          {(members ?? []).map((m) => (
            <option key={m.userId} value={m.userId}>
              {memberLabel(m)} — {m.email}
            </option>
          ))}
        </Select>
      )}

      {error && <p className="text-xs text-danger">{error}</p>}
      {!error && !loading && members!.length === 0 && (
        <p className="text-xs text-fg-subtle">No project members yet.</p>
      )}

      {allowInvite && !disabled && (
        <div className="flex flex-col gap-1.5">
          {!showInvite ? (
            <button
              type="button"
              onClick={() => setShowInvite(true)}
              className="self-start text-xs text-fg-subtle underline-offset-2 hover:text-fg hover:underline"
            >
              Not a project member? Invite by email
            </button>
          ) : (
            <div className="flex items-center gap-1.5">
              <Input
                type="email"
                value={inviteEmail}
                onChange={(e) => setInviteEmail(e.target.value)}
                placeholder="person@firm.com"
                autoFocus
                className="max-w-[16rem]"
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    void onInvite();
                  }
                }}
              />
              <Button
                type="button"
                size="sm"
                variant="primary"
                disabled={inviting || !inviteEmail.trim()}
                onClick={() => void onInvite()}
              >
                {inviting ? 'Inviting…' : 'Invite'}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => {
                  setShowInvite(false);
                  setInviteEmail('');
                }}
              >
                Cancel
              </Button>
            </div>
          )}
          {notice && <p className="text-xs text-fg-muted">{notice}</p>}
        </div>
      )}
    </div>
  );
}

/**
 * Multi-select as a checkbox list, not a `<select multiple>`: ctrl-clicking to
 * multi-select in a native listbox is a well-known usability trap, and an
 * attendee list is read far more often than it is edited.
 */
function MultiSelectList({
  members,
  selectedIds,
  onToggle,
  disabled,
  loading,
}: {
  members: ProjectMember[] | null;
  selectedIds: string[];
  onToggle: (userId: string) => void;
  disabled: boolean;
  loading: boolean;
}) {
  if (loading) {
    return (
      <div className="rounded border border-border bg-surface px-2 py-1.5 text-sm text-fg-subtle">
        Loading members…
      </div>
    );
  }
  if (!members || members.length === 0) return null;

  return (
    <ul className="max-h-48 overflow-y-auto rounded border border-border bg-surface">
      {members.map((m) => {
        const checked = selectedIds.includes(m.userId);
        return (
          <li key={m.userId}>
            <label
              className={cn(
                'flex cursor-pointer items-center gap-2 px-2 py-1.5 text-sm hover:bg-surface-2',
                disabled && 'cursor-not-allowed opacity-60',
              )}
            >
              <input
                type="checkbox"
                checked={checked}
                disabled={disabled}
                onChange={() => onToggle(m.userId)}
                className="h-3.5 w-3.5 accent-accent"
              />
              <span className="min-w-0 truncate text-fg">{memberLabel(m)}</span>
              <span className="ml-auto shrink-0 truncate text-xs text-fg-subtle">{m.email}</span>
            </label>
          </li>
        );
      })}
    </ul>
  );
}
