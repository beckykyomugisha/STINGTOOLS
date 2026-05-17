// Planscape Desktop — root renderer component.
//
// Layout: fixed left sidebar (nav) + main content area.
// Shares the same API client and auth store as the mobile app
// (symlinked or copied from Planscape/src/api/ at build time).
//
// Pages:
//   /dashboard   — compliance gauge + project overview
//   /issues      — issue list + create
//   /documents   — CDE document browser
//   /ifc         — IFC drop-folder status + manual import trigger
//   /settings    — server URL, auth token, ifc_drop path

import React, { useState } from 'react';
import DashboardPage  from './pages/DashboardPage';
import IssuesPage     from './pages/IssuesPage';
import DocumentsPage  from './pages/DocumentsPage';
import IfcPage        from './pages/IfcPage';
import SettingsPage   from './pages/SettingsPage';

type Page = 'dashboard' | 'issues' | 'documents' | 'ifc' | 'settings';

const NAV: { id: Page; label: string; icon: string }[] = [
  { id: 'dashboard',  label: 'Dashboard',  icon: '◈' },
  { id: 'issues',     label: 'Issues',     icon: '⚑' },
  { id: 'documents',  label: 'Documents',  icon: '⎗' },
  { id: 'ifc',        label: 'IFC / ArchiCAD', icon: '⟁' },
  { id: 'settings',   label: 'Settings',   icon: '⚙' },
];

export default function App() {
  const [page, setPage] = useState<Page>('dashboard');

  const pageComponent: Record<Page, React.ReactElement> = {
    dashboard: <DashboardPage />,
    issues:    <IssuesPage />,
    documents: <DocumentsPage />,
    ifc:       <IfcPage />,
    settings:  <SettingsPage />,
  };

  return (
    <div style={{ display: 'flex', height: '100vh', fontFamily: 'system-ui, sans-serif' }}>
      {/* Sidebar */}
      <nav style={{
        width: 200, background: '#1a1a2e', color: '#e0e0e0',
        display: 'flex', flexDirection: 'column', padding: '16px 0',
      }}>
        <div style={{ padding: '0 16px 24px', fontSize: 18, fontWeight: 700, color: '#fff' }}>
          Planscape
        </div>
        {NAV.map(n => (
          <button key={n.id}
            onClick={() => setPage(n.id)}
            style={{
              background: page === n.id ? '#16213e' : 'transparent',
              border: 'none', color: page === n.id ? '#fff' : '#aaa',
              textAlign: 'left', padding: '10px 16px', cursor: 'pointer',
              fontSize: 14, borderLeft: page === n.id ? '3px solid #4f8ef7' : '3px solid transparent',
            }}>
            <span style={{ marginRight: 8 }}>{n.icon}</span>{n.label}
          </button>
        ))}
      </nav>

      {/* Main content */}
      <main style={{ flex: 1, overflow: 'auto', background: '#f5f5f5', padding: 24 }}>
        {pageComponent[page]}
      </main>
    </div>
  );
}
