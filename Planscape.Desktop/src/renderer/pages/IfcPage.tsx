// IFC / ArchiCAD page — desktop-specific, not in the mobile app.
//
// Shows the status of the ifc_drop hot folder and lets the user:
//   • See pending / processing / done / failed counts.
//   • Open the drop folder in Explorer / Finder.
//   • Trigger an ArchiCAD sync (calls the Planscape server API which
//     forwards to the Revit plugin via SignalR).

import React, { useEffect, useState } from 'react';

declare global {
  interface Window {
    planscape: {
      ifc:      { listPending: (f: string) => Promise<string[]>; openFolder: (f: string) => void };
      settings: { get: (k: string) => Promise<unknown>; set: (k: string, v: unknown) => void };
      notify:   (title: string, body: string) => void;
    };
  }
}

export default function IfcPage() {
  const [dropFolder, setDropFolder] = useState<string>('');
  const [pending,    setPending]    = useState<string[]>([]);

  useEffect(() => {
    window.planscape.settings.get('ifcDropFolder').then(f => {
      const folder = (f as string) || '';
      setDropFolder(folder);
      if (folder) refresh(folder);
    });
  }, []);

  function refresh(folder: string) {
    window.planscape.ifc.listPending(folder).then(setPending);
  }

  return (
    <div>
      <h2 style={{ marginTop: 0 }}>IFC / ArchiCAD Sync</h2>

      <section style={{ background: '#fff', borderRadius: 8, padding: 20, marginBottom: 16 }}>
        <label style={{ display: 'block', marginBottom: 8, fontWeight: 600 }}>
          IFC drop folder
        </label>
        <div style={{ display: 'flex', gap: 8 }}>
          <input
            value={dropFolder}
            onChange={e => setDropFolder(e.target.value)}
            placeholder="e.g. C:\Projects\MyProject\_ifc_drop"
            style={{ flex: 1, padding: '6px 10px', borderRadius: 4, border: '1px solid #ccc' }}
          />
          <button onClick={() => window.planscape.settings.set('ifcDropFolder', dropFolder)}
            style={btn}>Save</button>
          <button onClick={() => window.planscape.ifc.openFolder(dropFolder)}
            style={btn}>Open folder</button>
        </div>
      </section>

      <section style={{ background: '#fff', borderRadius: 8, padding: 20 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 style={{ margin: 0 }}>Processing queue</h3>
          <button onClick={() => refresh(dropFolder)} style={btn}>Refresh</button>
        </div>

        {pending.length === 0
          ? <p style={{ color: '#888', marginTop: 12 }}>No IFC files pending — drop a .ifc file into the folder above.</p>
          : (
            <ul style={{ marginTop: 12, paddingLeft: 0, listStyle: 'none' }}>
              {pending.map(f => (
                <li key={f} style={{
                  padding: '8px 12px', background: '#f0f4ff',
                  borderRadius: 4, marginBottom: 6, fontFamily: 'monospace', fontSize: 13,
                }}>
                  ⟁ {f}
                </li>
              ))}
            </ul>
          )
        }
      </section>
    </div>
  );
}

const btn: React.CSSProperties = {
  padding: '6px 14px', borderRadius: 4, border: '1px solid #ccc',
  background: '#fff', cursor: 'pointer', fontSize: 13,
};
