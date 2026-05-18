// Planscape — ArchiCAD live stream client.
//
// Connects to /hubs/archicad and joins the project group.
// Emits typed events as ArchiCAD authors make model changes.
// Works in React Native (mobile), Electron (desktop), and the web.
//
// Usage:
//   const client = new ArchiCADLiveClient();
//   await client.connect(projectId);
//   client.on('ElementChanged', ev => console.log(ev));
//   // when done:
//   await client.disconnect();

import * as signalR from '@microsoft/signalr';
import { getBaseUrl } from '../api/client';
import { secureStorage } from './secureStorage';

export interface ArchiCADElement {
  kind:         'Added' | 'Changed' | 'Deleted';
  elementId:    string;
  elementType:  string;
  properties?:  Record<string, unknown>;
  boundingBox?: { min: [number,number,number]; max: [number,number,number] };
  timestampUtc: string;
}

export interface ModelStatus {
  projectId:        string;
  connectedAuthors: string[];
  activeLayers:     string[];
  lastPushUtc:      string;
  isLive:           boolean;
  eventCount?:      number;
}

type ElementHandler = (ev: ArchiCADElement) => void;
type StatusHandler  = (s: ModelStatus) => void;

export class ArchiCADLiveClient {
  private connection: signalR.HubConnection | null = null;
  private elementHandlers: Set<ElementHandler> = new Set();
  private statusHandlers:  Set<StatusHandler>  = new Set();
  private _projectId: string | null = null;
  private _isLive = false;

  get isLive()      { return this._isLive; }
  get projectId()   { return this._projectId; }

  async connect(projectId: string): Promise<void> {
    this._projectId = projectId;
    const [token, baseUrl] = await Promise.all([
      secureStorage.getToken(),
      getBaseUrl(),
    ]);

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/archicad`, {
        accessTokenFactory: () => token ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // Register server → client event handlers before starting.
    this.connection.on('ElementAdded',   (ev: ArchiCADElement) => this._emit({ ...ev, kind: 'Added' }));
    this.connection.on('ElementChanged', (ev: ArchiCADElement) => this._emit({ ...ev, kind: 'Changed' }));
    this.connection.on('ElementDeleted', (ev: ArchiCADElement) => this._emit({ ...ev, kind: 'Deleted' }));
    this.connection.on('ModelStatus',    (s: ModelStatus)      => {
      this._isLive = s.isLive ?? true;
      this.statusHandlers.forEach(h => h(s));
    });
    this.connection.on('Joined', () => console.log('[ArchiCADLive] joined project', projectId));

    this.connection.onreconnected(async () => {
      await this.connection?.invoke('JoinProject', projectId);
    });

    await this.connection.start();
    await this.connection.invoke('JoinProject', projectId);
  }

  async disconnect(): Promise<void> {
    if (this._projectId)
      await this.connection?.invoke('LeaveProject', this._projectId);
    await this.connection?.stop();
    this._isLive  = false;
    this._projectId = null;
  }

  on(event: 'ElementChanged' | 'ElementAdded' | 'ElementDeleted', handler: ElementHandler): void;
  on(event: 'ModelStatus', handler: StatusHandler): void;
  on(event: string, handler: ElementHandler | StatusHandler): void {
    if (event === 'ModelStatus') this.statusHandlers.add(handler as StatusHandler);
    else                         this.elementHandlers.add(handler as ElementHandler);
  }

  off(handler: ElementHandler | StatusHandler): void {
    this.elementHandlers.delete(handler as ElementHandler);
    this.statusHandlers.delete(handler as StatusHandler);
  }

  private _emit(ev: ArchiCADElement) {
    this.elementHandlers.forEach(h => h(ev));
  }
}

// Singleton for use across the app.
export const archiCADLive = new ArchiCADLiveClient();
