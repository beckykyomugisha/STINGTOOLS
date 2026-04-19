import { create } from 'zustand';
import AsyncStorage from '@react-native-async-storage/async-storage';

const UNREAD_COUNT_KEY = 'planscape_unread_notifications';

/**
 * Phase 96 — notification unread count store. Driven by:
 *   (a) Foreground push handler (notificationService) increments on each
 *       delivery that the user hasn't interacted with yet.
 *   (b) notificationTapRouter calls `decrement()` / `clearForIssue()` when
 *       the user actually taps the notification or opens the target screen.
 *   (c) Tabs tab bar renders `badgeText` on the Issues tab when count > 0.
 *
 * Count is persisted to AsyncStorage so the badge survives app cold-start —
 * users see the pending count immediately on reopen, even before the app
 * reaches the server to sync real state.
 */

interface NotificationState {
  unreadCount: number;
  /** Map of feature → unread count (for per-tab badges). 'issues' only for now. */
  byFeature: Record<string, number>;
  increment: (feature?: string) => Promise<void>;
  decrement: (feature?: string, amount?: number) => Promise<void>;
  clear: (feature?: string) => Promise<void>;
  hydrate: () => Promise<void>;
}

export const useNotificationStore = create<NotificationState>((set, get) => ({
  unreadCount: 0,
  byFeature: {},

  async hydrate() {
    try {
      const raw = await AsyncStorage.getItem(UNREAD_COUNT_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as { total: number; byFeature: Record<string, number> };
        set({
          unreadCount: parsed.total ?? 0,
          byFeature: parsed.byFeature ?? {},
        });
      }
    } catch {
      // Corrupted storage — start at zero rather than crash
    }
  },

  async increment(feature = 'issues') {
    const prevFeat = get().byFeature[feature] ?? 0;
    const nextByFeat = { ...get().byFeature, [feature]: prevFeat + 1 };
    const next = get().unreadCount + 1;
    set({ unreadCount: next, byFeature: nextByFeat });
    await persist(next, nextByFeat);
  },

  async decrement(feature = 'issues', amount = 1) {
    const prevFeat = get().byFeature[feature] ?? 0;
    const nextFeat = Math.max(0, prevFeat - amount);
    const nextByFeat = { ...get().byFeature, [feature]: nextFeat };
    const next = Math.max(0, get().unreadCount - amount);
    set({ unreadCount: next, byFeature: nextByFeat });
    await persist(next, nextByFeat);
  },

  async clear(feature) {
    if (!feature) {
      set({ unreadCount: 0, byFeature: {} });
      await persist(0, {});
      return;
    }
    const byFeature = { ...get().byFeature };
    const removed = byFeature[feature] ?? 0;
    delete byFeature[feature];
    const next = Math.max(0, get().unreadCount - removed);
    set({ unreadCount: next, byFeature });
    await persist(next, byFeature);
  },
}));

async function persist(total: number, byFeature: Record<string, number>): Promise<void> {
  try {
    await AsyncStorage.setItem(UNREAD_COUNT_KEY, JSON.stringify({ total, byFeature }));
  } catch {
    // Storage full / encrypted-file-system edge case — in-memory state still works
  }
}
