// IFC / ArchiCAD live view — desktop.
//
// Shows:
//   • Live indicator (green pulse when ArchiCAD is actively pushing)
//   • Real-time element change feed via SignalR → /hubs/archicad
//   • ifc_drop folder queue (files waiting to be processed by Revit)
//   • Connected authors (who is modeling in ArchiCAD right now)

import React, { useEffect, useRef, useState } from 'react';

// Re-use the same client from the mobile/shared layer.
// In production, resolve via tsconfig path alias @services/archiCADLiveClient.
// For scaffold purposes we inline the type contract here.
interface LiveEvent {
  kind: 'Added' | 'Changed' | 'Deleted';
  elementId:   string;
  elementType: string;
  timestampUtc: string;
}
interface ModelStatus {
  connectedAuthors: string[];
  lastPushUtc:      string;
  isLive:           boolean;
}

declare global {
  interface Window {
    planscape: {
      ifc:      { listPending: (f: string) => Promise<string[]>; openFolder: (f: string) => void };
      settings: { get: (k: string) => Promise<unknown>; set: (k: string, v: unknown) => void };
      notify:   (title: string, body: string) => void;
    };
  }
}

const MAX_EVENTS = 100;

export default function IfcPage() {
  const [projectId,   setProjectId]  = useState('');
  const [dropFolder,  setDropFolder] = useState('');
  const [pending,     setPending]    = useState<string[]>([]);
  const [events,      setEvents]     = useState<LiveEvent[]>([]);
  const [status,      setStatus]     = useState<ModelStatus | null>(null);
  const [connected,   setConnected]  = useState(false);
  const wsRef = useRef<WebSocket | null>(null);

  useEffect(() => {
    Promise.all([
      window.planscape.settings.get('projectId'),
      window.planscape.settings.get('ifcDropFolder'),
    ]).then(([pid, folder]) => {
      setProjectId((pid as string) || '');
      setDropFolder((folder as string) || '');
      if (folder) refresh(folder as string);
    });
  }, []);

  function refresh(folder: string) {
    window.planscape.ifc.listPending(folder).then(setPending);
  }

  // SignalR connection (simplified fetch-EventSource stub for desktop —
  // real build uses @microsoft/signalr via the shared service layer).
  function connect() {
    if (!projectId) return;
    // In the full build this calls archiCADLive.connect(projectId).
    // Here we mark connected to show the UI state.
    setConnected(true);
    window.planscape.notify('Planscape', `Connected to ArchiCAD live feed for project ${projectId}`);
  }

  function disconnect() {
    setConnected(false);
    setStatus(null);
  }

  const kindColor = { Added: '#22c55e', Changed: '#f59e0b', Deleted: '#ef4444' };

  return (
    <div style={{ display: 'flex', gap: 16, height: '100%' }}>

      {/* Left panel — config + status */}
      <div style={{ width: 280, display: 'flex', flexDirection: 'column', gap: 12 }}>

        <section style={card}>
          <h3 style={cardTitle}>Live connection</h3>

          {/* Live indicator */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12 }}>
            <span style={{
              width: 10, height: 10, borderRadius: '50%',
              background: connected && status?.isLive ? '#22c55e' : '#6b7280',
              boxShadow:  connected && status?.isLive ? '0 0 0 3px #bbf7d0' : 'none',
            }}/>
            <span style={{ fontSize: 13, color: '#374151' }}>
              {connected && status?.isLive ? 'LIVE — ArchiCAD streaming'
               : connected                 ? 'Connected — waiting for activity'
               :                             'Not connected'}
            </span>
          </div>

          <label style={label}>Project ID</label>
          <input value={projectId} onChange={e => setProjectId(e.target.value)}
            placeholder="Paste Planscape project GUID"
            style={input} />

          <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            {connected
              ? <button onClick={disconnect} style={{...btn, background:'#fee2e2', color:'#dc2626'}}>Disconnect</button>
              : <button onClick={connect}    style={{...btn, background:'#dcfce7', color:'#16a34a'}}>Connect</button>
            }
          </div>
        </section>

        {status && (
          <section style={card}>
            <h3 style={cardTitle}>Authors online</h3>
            {status.connectedAuthors.length === 0
              ? <p style={{ fontSize: 12, color: '#9ca3af' }}>No authors detected</p>
              : status.connectedAuthors.map(a => (
                  <div key={a} style={{ fontSize: 13, padding: '4px 0', borderBottom: '1px solid #f3f4f6' }}>
                    👤 {a}
                  </div>
                ))
            }
            <p style={{ fontSize: 11, color: '#9ca3af', marginTop: 8 }}>
              Last push: {new Date(status.lastPushUtc).toLocaleTimeString()}
            </p>
          </section>
        )}

        <section style={card}>
          <h3 style={cardTitle}>IFC drop folder</h3>
          <input value={dropFolder} onChange={e => setDropFolder(e.target.value)}
            placeholder="C:\Projects\..\_ifc_drop" style={input} />
          <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <button onClick={() => { window.planscape.settings.set('ifcDropFolder', dropFolder); refresh(dropFolder); }}
              style={btn}>Save</button>
            <button onClick={() => window.planscape.ifc.openFolder(dropFolder)}
              style={btn}>Open ↗</button>
          </div>
          {pending.length > 0 && (
            <p style={{ marginTop: 8, fontSize: 12, color: '#f59e0b' }}>
              ⚠ {pending.length} file(s) pending in queue
            </p>
          )}
        </section>
      </div>

      {/* Right panel — live event feed */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <h2 style={{ margin: 0, fontSize: 18 }}>Live element feed</h2>
          {events.length > 0 && (
            <button onClick={() => setEvents([])} style={{ ...btn, fontSize: 12 }}>Clear</button>
          )}
        </div>

        <div style={{ flex: 1, overflowY: 'auto', background: '#fff', borderRadius: 8, border: '1px solid #e5e7eb' }}>
          {events.length === 0
            ? (
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center',
                height: '100%', color: '#9ca3af', flexDirection: 'column', gap: 8 }}>
                <span style={{ fontSize: 32 }}>⟁</span>
                <span>{connected ? 'Waiting for ArchiCAD changes…' : 'Connect to see live changes'}</span>
              </div>
            )
            : events.map((ev, i) => (
              <div key={`${ev.elementId}-${i}`} style={{
                display: 'flex', alignItems: 'center', gap: 12,
                padding: '8px 14px', borderBottom: '1px solid #f9fafb',
                fontSize: 13,
              }}>
                <span style={{
                  padding: '2px 8px', borderRadius: 4, fontSize: 11, fontWeight: 600,
                  background: kindColor[ev.kind] + '22',
                  color: kindColor[ev.kind],
                  minWidth: 60, textAlign: 'center',
                }}>
                  {ev.kind}
                </span>
                <span style={{ color: '#6b7280', fontFamily: 'monospace', fontSize: 12 }}>
                  {ev.elementType}
                </span>
                <span style={{ flex: 1, color: '#374151', fontFamily: 'monospace', fontSize: 11 }}>
                  {ev.elementId}
                </span>
                <span style={{ color: '#9ca3af', fontSize: 11 }}>
                  {new Date(ev.timestampUtc).toLocaleTimeString()}
                </span>
              </div>
            ))
          }
        </div>
      </div>
    </div>
  );
}

const card: React.CSSProperties = { background:'#fff', borderRadius:8, padding:16, border:'1px solid #e5e7eb' };
const cardTitle: React.CSSProperties = { margin:'0 0 12px', fontSize:14, fontWeight:600, color:'#111827' };
const label: React.CSSProperties = { display:'block', fontSize:12, color:'#6b7280', marginBottom:4 };
const input: React.CSSProperties = { width:'100%', padding:'6px 10px', borderRadius:4, border:'1px solid #d1d5db', fontSize:13 };
const btn: React.CSSProperties = { padding:'5px 12px', borderRadius:4, border:'1px solid #d1d5db', background:'#fff', cursor:'pointer', fontSize:13 };
