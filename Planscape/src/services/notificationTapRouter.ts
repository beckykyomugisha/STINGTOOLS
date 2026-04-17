import * as Notifications from 'expo-notifications';
import { crashReporter } from './crashReporter';

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
    const sub = Notifications.addNotificationResponseReceivedListener(response => {
      try {
        const data = response.notification.request.content.data as Record<string, unknown> | undefined;
        if (!data) return;
        crashReporter.info('notification.tap', { data });

        const type = String(data.type ?? '').toUpperCase();
        const projectId = data.projectId as string | undefined;
        const issueId = data.issueId as string | undefined;

        if (type.startsWith('ISSUE') && projectId) {
          // Issues tab — list filters/highlight via querystring (issues.tsx can read params later)
          router.push(`/(tabs)/issues?projectId=${projectId}${issueId ? `&issueId=${issueId}` : ''}`);
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
