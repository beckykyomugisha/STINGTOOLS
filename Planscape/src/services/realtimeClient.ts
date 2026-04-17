import * as signalR from '@microsoft/signalr';
import { secureStorage } from './secureStorage';

const HUB_URL = process.env.EXPO_PUBLIC_HUB_URL ?? 'https://planscape.example/hubs/project';

export type RealtimeEvent =
  | { type: 'ISSUE_CREATED'; payload: unknown }
  | { type: 'ISSUE_UPDATED'; payload: unknown }
  | { type: 'COMPLIANCE_CHANGED'; payload: unknown }
  | { type: 'NOTIFICATION'; payload: unknown };

type Handler = (ev: RealtimeEvent) => void;

export class RealtimeClient {
  private connection: signalR.HubConnection | null = null;
  private handlers: Handler[] = [];
  private currentProjectId: string | null = null;

  async connect(projectId: string): Promise<void> {
    const token = await secureStorage.getToken();
    if (!token) throw new Error('No auth token for realtime connection');

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${HUB_URL}?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('IssueCreated', p => this.emit({ type: 'ISSUE_CREATED', payload: p }));
    this.connection.on('IssueUpdated', p => this.emit({ type: 'ISSUE_UPDATED', payload: p }));
    this.connection.on('ComplianceChanged', p => this.emit({ type: 'COMPLIANCE_CHANGED', payload: p }));
    this.connection.on('Notification', p => this.emit({ type: 'NOTIFICATION', payload: p }));

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

  private emit(ev: RealtimeEvent): void {
    this.handlers.forEach(h => { try { h(ev); } catch {} });
  }
}

export const realtime = new RealtimeClient();
