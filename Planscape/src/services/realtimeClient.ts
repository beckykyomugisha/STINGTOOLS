import * as signalR from '@microsoft/signalr';
import { secureStorage } from './secureStorage';

const HUB_URL = process.env.EXPO_PUBLIC_HUB_URL ?? 'https://planscape.example/hubs/project';

export type RealtimeEvent =
  | { type: 'ISSUE_CREATED'; payload: unknown }
  | { type: 'ISSUE_UPDATED'; payload: unknown }
  | { type: 'COMPLIANCE_CHANGED'; payload: unknown }
  | { type: 'NOTIFICATION'; payload: unknown };

type Handler = (ev: RealtimeEvent) => void;
type NamedHandler = (payload: any) => void;

export class RealtimeClient {
  private connection: signalR.HubConnection | null = null;
  private handlers: Handler[] = [];
  private namedHandlers: Map<string, Set<NamedHandler>> = new Map();
  private currentProjectId: string | null = null;

  async connect(projectId: string): Promise<void> {
    const token = await secureStorage.getToken();
    if (!token) throw new Error('No auth token for realtime connection');

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${HUB_URL}?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('IssueCreated', p => { this.emit({ type: 'ISSUE_CREATED', payload: p }); this.fire('IssueCreated', p); });
    this.connection.on('IssueUpdated', p => { this.emit({ type: 'ISSUE_UPDATED', payload: p }); this.fire('IssueUpdated', p); });
    this.connection.on('ComplianceChanged', p => { this.emit({ type: 'COMPLIANCE_CHANGED', payload: p }); this.fire('ComplianceChanged', p); });
    this.connection.on('Notification', p => { this.emit({ type: 'NOTIFICATION', payload: p }); this.fire('Notification', p); });
    // Generic events forwarded to named handlers — lets screens subscribe to
    // specific channels (CommentAdded, DocumentUpdated, TransmittalUpdated,
    // MeetingCreated, MeetingUpdated, PresenceChanged) without bloating the
    // RealtimeEvent union.
    //
    // GAP-FIX-SIGNALR — RevisionCreated removed: the server has no
    // RevisionsController, so the channel never fired. Re-add when a
    // server-side revisions feature ships.
    // MeetingCreated / MeetingUpdated added to match new emissions in
    // MeetingsController (Create / BulkCreate / LogMinutes / AddActionItem /
    // UpdateAction).
    for (const evName of [
      'CommentAdded',
      'DocumentUpdated',
      'TransmittalUpdated',
      'MeetingCreated',
      'MeetingUpdated',
      'PresenceChanged',
      // Phase 177 — fired by the server when a member's per-folder ACL
      // changes; the client re-issues JoinProject below to rebuild
      // the per-CDE-state subgroup memberships against the new slice.
      'AclChanged',
      'MemberRevoked',
    ]) {
      this.connection.on(evName, (p: unknown) => this.fire(evName, p));
    }

    // Phase 177 — when the server pushes AclChanged for the active project,
    // drop the project subscription and re-join so SignalR rebuilds the
    // per-CDE-state subgroups against the new allow-list.
    this.connection.on('AclChanged', async (p: { projectId?: string }) => {
      if (!p?.projectId || p.projectId !== this.currentProjectId) return;
      try {
        await this.connection?.invoke('LeaveProject', this.currentProjectId);
        await this.connection?.invoke('JoinProject', this.currentProjectId);
      } catch { /* reconnect path will retry */ }
    });

    await this.connection.start();
    await this.connection.invoke('JoinProject', projectId);
    this.currentProjectId = projectId;
  }

  async disconnect(): Promise<void> {
    if (!this.connection) return;
    if (this.currentProjectId) {
      try { await this.connection.invoke('LeaveProject', this.currentProjectId); } catch {}
    }
    await this.connection.stop();
    this.connection = null;
    this.currentProjectId = null;
  }

  subscribe(handler: Handler): () => void {
    this.handlers.push(handler);
    return () => { this.handlers = this.handlers.filter(h => h !== handler); };
  }

  /** Subscribe to a specific server event by name (e.g. "CommentAdded"). */
  on(eventName: string, handler: NamedHandler): () => void {
    let set = this.namedHandlers.get(eventName);
    if (!set) { set = new Set(); this.namedHandlers.set(eventName, set); }
    set.add(handler);
    return () => { set!.delete(handler); };
  }

  private emit(ev: RealtimeEvent): void {
    this.handlers.forEach(h => { try { h(ev); } catch {} });
  }
  private fire(name: string, payload: any): void {
    const set = this.namedHandlers.get(name);
    if (!set) return;
    set.forEach(h => { try { h(payload); } catch {} });
  }
}

export const realtime = new RealtimeClient();
