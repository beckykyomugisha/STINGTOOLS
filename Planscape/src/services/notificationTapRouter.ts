import * as Notifications from 'expo-notifications';
import { crashReporter } from './crashReporter';
import { useNotificationStore } from '@/stores/notificationStore';
import { getToken } from '@/api/client';
import { setPendingMeeting, meetingLivePath } from './pendingMeeting';

interface RouterShape {
  push: (path: string) => void;
}

export const notificationTapRouter = {
  /**
   * Wire up notification-tap deep-linking. Server sends:
   *   data: { issueId, projectId, type: 'ISSUE_*' | 'COMPLIANCE_*' | ... }
   * We parse and route into the relevant tab.
   */
  attach(router: RouterShape): () => void {
    const sub = Notifications.addNotificationResponseReceivedListener(async response => {
      try {
        const data = response.notification.request.content.data as Record<string, unknown> | undefined;
        if (!data) return;
        crashReporter.info('notification.tap', { data });

        const type = String(data.type ?? '').toUpperCase();
        const projectId = data.projectId as string | undefined;
        const issueId = data.issueId as string | undefined;

        // Phase 96 — tapping a notification means the user has seen it, so
        // decrement the unread counter. The foreground receiver
        // (notificationService) incremented it on delivery.
        const feature = type.startsWith('ISSUE') ? 'issues'
          : type.startsWith('COMPLIANCE') ? 'dashboard'
          : type.startsWith('DOCUMENT') ? 'documents'
          : type.startsWith('MEETING') || type.startsWith('MINUTES') ? 'dashboard'
          : 'issues';
        useNotificationStore.getState().decrement(feature);

        // P1 — meeting invite / live-start: deep-link straight into the live
        // meeting and auto-join A/V. Server data carries meetingId (+ optionally
        // meetingSessionId). If signed out, stash the target and route through
        // login → set-password, then back into the meeting.
        if (type === 'MEETING_INVITE' || type === 'MEETING_LIVE') {
          const meetingId = (data.meetingId as string | undefined)
            ?? (data.meetingSessionId as string | undefined);
          if (meetingId && projectId) {
            const token = await getToken().catch(() => null);
            if (token) {
              router.push(meetingLivePath(projectId, meetingId));
            } else {
              await setPendingMeeting({ projectId, meetingId });
              router.push('/login');
            }
            return;
          }
        }

        if (type.startsWith('ISSUE')) {
          // Phase 96 — prefer direct navigation to the detail screen when we
          // know which issue the notification is about. Falls back to the list
          // when only projectId is known (e.g. "N new issues on Project X").
          // issues.tsx also auto-forwards ?issueId=... to the detail screen
          // so this handles notifications from older server builds too.
          if (issueId) {
            // Phase 96 — include projectId so issue-detail can skip the
            // O(n) probe across every project the user has access to.
            router.push(`/issue-detail?id=${issueId}${projectId ? `&projectId=${projectId}` : ''}`);
          } else if (projectId) {
            router.push(`/(tabs)/issues?projectId=${projectId}`);
          } else {
            router.push('/(tabs)/issues');
          }
          return;
        }
        if (type.startsWith('COMPLIANCE') && projectId) {
          router.push(`/(tabs)/?projectId=${projectId}`);
          return;
        }
        if (type.startsWith('DOCUMENT') && projectId) {
          router.push(`/(tabs)/documents?projectId=${projectId}`);
          return;
        }
        // Default: dashboard
        router.push('/(tabs)/');
      } catch (err) {
        crashReporter.warn('notificationTapRouter.attach handler failed', { err: String(err) });
      }
    });
    return () => sub.remove();
  },
};
