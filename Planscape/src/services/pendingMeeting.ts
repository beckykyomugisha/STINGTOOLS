// pendingMeeting — P1 meeting-invite deep-link handoff.
//
// When an unactivated / signed-out user opens a meeting deep link
// (planscape://meeting/{id}?project={pid}) or taps a meeting-invite push while
// signed out, we stash the target here, send them through login / set-password,
// then route straight into the live meeting once auth completes (the
// "(password)" flow). SecureStore keeps it consistent with the token store.

import * as SecureStore from 'expo-secure-store';

const KEY = 'planscape_pending_meeting';

export interface PendingMeeting {
  projectId: string;
  meetingId: string;
}

export async function setPendingMeeting(target: PendingMeeting): Promise<void> {
  try {
    if (!target.projectId || !target.meetingId) return;
    await SecureStore.setItemAsync(KEY, JSON.stringify(target));
  } catch {
    /* best-effort — a missed handoff just lands the user on the dashboard */
  }
}

/** Returns the pending target (if any) and clears it. One-shot. */
export async function consumePendingMeeting(): Promise<PendingMeeting | null> {
  try {
    const raw = await SecureStore.getItemAsync(KEY);
    if (!raw) return null;
    await SecureStore.deleteItemAsync(KEY);
    const t = JSON.parse(raw) as PendingMeeting;
    return t?.projectId && t?.meetingId ? t : null;
  } catch {
    return null;
  }
}

/** Route into the live meeting screen for a meetingId (resolves to a session there). */
export function meetingLivePath(projectId: string, meetingId: string): string {
  return `/meetings/live?project=${encodeURIComponent(projectId)}&meeting=${encodeURIComponent(meetingId)}`;
}
