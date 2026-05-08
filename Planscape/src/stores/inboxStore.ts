import { create } from 'zustand';

/**
 * Phase 177-C — cross-screen invalidation signal for the My Actions inbox.
 *
 * When a user approves / rejects a document on the approvals screen, the
 * locally-rendered list filters the row out, but the home tab's count
 * tile and the inbox index's "Pending document approvals" section stay
 * stale until they refetch. Bumping `version` here lets any screen that
 * reads MyActions watch this counter and trigger a re-load on change
 * without coupling the screens directly.
 *
 * The store is intentionally tiny — no payload, no per-bucket flags. The
 * one thing screens need is "did anything that affects MyActions just
 * happen", which a monotonic counter gives them.
 */
interface InboxState {
  version: number;
  /** Bump after any action that changes the MyActions response shape. */
  bump: () => void;
}

export const useInboxStore = create<InboxState>((set, get) => ({
  version: 0,
  bump: () => set({ version: get().version + 1 }),
}));
