import React from 'react';

/* ------------------------------------------------------------------ */
/* Shared chrome bits                                                  */
/* ------------------------------------------------------------------ */

export function BrowserChrome({
  children,
  url = 'app.planscape.io/dashboard',
  className = '',
}: {
  children: React.ReactNode;
  url?: string;
  className?: string;
}) {
  return (
    <div
      className={`overflow-hidden rounded-xl border border-white/10 bg-navy shadow-2xl ${className}`}
    >
      <div className="flex h-8 items-center gap-2 bg-navy-dark px-3">
        <div className="flex gap-1.5">
          <span className="h-2 w-2 rounded-full bg-red-500" />
          <span className="h-2 w-2 rounded-full bg-yellow-400" />
          <span className="h-2 w-2 rounded-full bg-green-500" />
        </div>
        <div className="mx-auto rounded-full bg-white/10 px-3 py-0.5 text-[10px] text-white/40">
          {url}
        </div>
      </div>
      {children}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Dashboard mockup                                                    */
/* ------------------------------------------------------------------ */

export function DashboardMockup() {
  return (
    <div className="flex aspect-[16/10] w-full bg-navy text-white">
      {/* Sidebar */}
      <div className="w-[140px] shrink-0 bg-navy-dark py-3">
        <div className="px-3 pb-3 text-[9px] font-bold tracking-widest text-white/40">
          PLANSCAPE
        </div>
        <nav className="space-y-0.5 text-[10px]">
          {[
            ['Overview', true],
            ['Issues', false],
            ['Documents', false],
            ['Transmittals', false],
            ['Meetings', false],
            ['Workflows', false],
            ['Warnings', false],
          ].map(([label, active]) => (
            <div
              key={label as string}
              className={`flex items-center px-3 py-1.5 ${
                active
                  ? 'border-l-2 border-orange bg-white/5 text-white'
                  : 'text-white/60'
              }`}
            >
              <span>{label}</span>
            </div>
          ))}
        </nav>
      </div>

      {/* Main */}
      <div className="flex-1 px-4 py-3">
        {/* Top bar */}
        <div className="mb-3 flex items-center justify-between text-[10px]">
          <div className="flex items-center gap-2 rounded-md bg-white/5 px-2 py-1 text-white/80">
            <span className="h-1.5 w-1.5 rounded-full bg-orange" />
            New Hospital Wing (NHW-2026) ▾
          </div>
          <div className="flex items-center gap-2 text-white/50">
            <span>BIM Coordinator</span>
            <span className="rounded bg-white/5 px-2 py-0.5 text-white/70">
              Sign out
            </span>
          </div>
        </div>

        {/* KPI cards */}
        <div className="mb-3 grid grid-cols-4 gap-2">
          <KpiCard value="84%" label="TAG COMPLIANCE" valueClass="text-success" trend />
          <KpiCard value="1,247" label="ELEMENTS" />
          <KpiCard value="12" label="WARNINGS" valueClass="text-warning" />
          <KpiCard value="5" label="OPEN ISSUES" valueClass="text-orange" />
        </div>

        {/* Chart */}
        <div className="mb-3 rounded-lg bg-white/5 p-3">
          <div className="mb-2 text-[9px] font-semibold tracking-wider text-white/60">
            WEEKLY COMPLIANCE TREND
          </div>
          <div className="flex h-16 items-end gap-2">
            {[55, 62, 68, 70, 78, 82, 84].map((h, i) => (
              <div key={i} className="flex flex-1 flex-col items-center gap-1">
                <div
                  className="w-full rounded-sm bg-gradient-to-t from-navy-light to-orange/70"
                  style={{ height: `${h}%` }}
                />
                <span className="text-[8px] text-white/40">
                  {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'][i]}
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* Issue rows */}
        <div className="space-y-1.5">
          <IssueRow
            color="bg-danger"
            title="MEP clash — Level 3 plant room"
            status="IN REVIEW"
            statusClass="bg-purple-500/20 text-purple-300"
            date="2d ago"
          />
          <IssueRow
            color="bg-warning"
            title="Missing fire damper tags — Zone B"
            status="OPEN"
            statusClass="bg-blue-500/20 text-blue-300"
            date="5d ago"
          />
          <IssueRow
            color="bg-success"
            title="Structural beam numbering"
            status="RESOLVED"
            statusClass="bg-green-500/20 text-green-300"
            date="1w ago"
          />
        </div>
      </div>
    </div>
  );
}

function KpiCard({
  value,
  label,
  valueClass = 'text-white',
  trend,
}: {
  value: string;
  label: string;
  valueClass?: string;
  trend?: boolean;
}) {
  return (
    <div className="rounded-lg bg-white/5 p-2.5">
      <div className={`flex items-baseline gap-1 text-lg font-bold ${valueClass}`}>
        {value}
        {trend && <span className="text-[10px] text-success">▲</span>}
      </div>
      <div className="text-[8px] font-semibold tracking-wider text-white/50">
        {label}
      </div>
    </div>
  );
}

function IssueRow({
  color,
  title,
  status,
  statusClass,
  date,
}: {
  color: string;
  title: string;
  status: string;
  statusClass: string;
  date: string;
}) {
  return (
    <div className="flex items-center gap-2 rounded-md bg-white/5 px-2.5 py-1.5 text-[10px]">
      <span className={`h-2 w-2 rounded-full ${color}`} />
      <span className="flex-1 truncate text-white/80">{title}</span>
      <span className={`rounded px-1.5 py-0.5 text-[8px] font-semibold ${statusClass}`}>
        {status}
      </span>
      <span className="w-12 text-right text-[9px] text-white/40">{date}</span>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Issues mockup                                                       */
/* ------------------------------------------------------------------ */

export function IssuesMockup() {
  const rows = [
    {
      id: 'NHW-241',
      title: 'MEP clash with structural beam — Grid C/4',
      pri: 'CRITICAL',
      priClass: 'bg-danger/20 text-red-300',
      assignee: 'JM',
      assigneeBg: 'bg-blue-500',
      status: 'IN REVIEW',
      statusClass: 'bg-purple-500/20 text-purple-300',
      due: '04 May',
    },
    {
      id: 'NHW-238',
      title: 'Fire damper specification missing — Level 4',
      pri: 'HIGH',
      priClass: 'bg-orange/20 text-orange',
      assignee: 'SK',
      assigneeBg: 'bg-emerald-500',
      status: 'OPEN',
      statusClass: 'bg-blue-500/20 text-blue-300',
      due: '07 May',
    },
    {
      id: 'NHW-235',
      title: 'Door swing conflict — Operating theatre 3',
      pri: 'HIGH',
      priClass: 'bg-orange/20 text-orange',
      assignee: 'AT',
      assigneeBg: 'bg-pink-500',
      status: 'OPEN',
      statusClass: 'bg-blue-500/20 text-blue-300',
      due: '08 May',
    },
    {
      id: 'NHW-229',
      title: 'Missing acoustic spec on partition wall',
      pri: 'MEDIUM',
      priClass: 'bg-warning/20 text-amber-300',
      assignee: 'RD',
      assigneeBg: 'bg-purple-500',
      status: 'IN REVIEW',
      statusClass: 'bg-purple-500/20 text-purple-300',
      due: '12 May',
    },
    {
      id: 'NHW-221',
      title: 'Update door schedule — Phase 2 corridor',
      pri: 'LOW',
      priClass: 'bg-slate-500/20 text-slate-300',
      assignee: 'PM',
      assigneeBg: 'bg-cyan-500',
      status: 'RESOLVED',
      statusClass: 'bg-green-500/20 text-green-300',
      due: '02 May',
    },
    {
      id: 'NHW-218',
      title: 'CDE upload checklist — model federation',
      pri: 'MEDIUM',
      priClass: 'bg-warning/20 text-amber-300',
      assignee: 'LO',
      assigneeBg: 'bg-rose-500',
      status: 'CLOSED',
      statusClass: 'bg-slate-500/20 text-slate-400',
      due: '28 Apr',
    },
  ];

  return (
    <div className="aspect-[16/10] w-full bg-navy p-4 text-white">
      {/* Title + filter bar */}
      <div className="mb-3 flex items-center justify-between">
        <div>
          <div className="text-sm font-semibold">Issues — New Hospital Wing</div>
          <div className="text-[10px] text-white/50">
            {rows.length} of 247 issues · Last sync 2 min ago
          </div>
        </div>
        <button className="rounded-md bg-orange px-3 py-1 text-[10px] font-semibold text-white">
          + New Issue
        </button>
      </div>

      <div className="mb-2 flex gap-2 text-[10px]">
        <Filter label="All Issues" />
        <Filter label="Priority" />
        <Filter label="Assignee" />
        <Filter label="Status" />
        <div className="ml-auto flex items-center gap-1 rounded-md border border-white/10 px-2 py-1 text-white/40">
          <span>🔍</span>
          <span>Search…</span>
        </div>
      </div>

      {/* Header */}
      <div className="grid grid-cols-[60px_1fr_70px_50px_80px_60px] items-center gap-2 border-b border-white/10 px-2 py-1.5 text-[8px] font-semibold tracking-wider text-white/40">
        <div>ID</div>
        <div>TITLE</div>
        <div>PRIORITY</div>
        <div>ASSIGNEE</div>
        <div>STATUS</div>
        <div>DUE</div>
      </div>

      {/* Rows */}
      <div className="divide-y divide-white/5">
        {rows.map((r) => (
          <div
            key={r.id}
            className="grid grid-cols-[60px_1fr_70px_50px_80px_60px] items-center gap-2 px-2 py-1.5 text-[10px]"
          >
            <div className="font-mono text-white/50">{r.id}</div>
            <div className="truncate text-white/85">{r.title}</div>
            <div>
              <span className={`rounded px-1.5 py-0.5 text-[8px] font-bold ${r.priClass}`}>
                {r.pri}
              </span>
            </div>
            <div>
              <span
                className={`flex h-5 w-5 items-center justify-center rounded-full text-[8px] font-bold text-white ${r.assigneeBg}`}
              >
                {r.assignee}
              </span>
            </div>
            <div>
              <span className={`rounded px-1.5 py-0.5 text-[8px] font-bold ${r.statusClass}`}>
                {r.status}
              </span>
            </div>
            <div className="text-white/60">{r.due}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Filter({ label }: { label: string }) {
  return (
    <div className="rounded-md border border-white/10 bg-white/5 px-2 py-1 text-white/70">
      {label} ▾
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Plugin mockup (Revit dock panel)                                    */
/* ------------------------------------------------------------------ */

export function PluginMockup() {
  const params = [
    ['DISC', 'M'],
    ['LOC', 'BLD1'],
    ['ZONE', 'Z01'],
    ['LVL', 'L03'],
    ['SYS', 'HVAC'],
    ['FUNC', 'SUP'],
    ['PROD', 'AHU'],
    ['SEQ', '0042'],
  ];

  return (
    <div
      className="mx-auto w-full max-w-[260px] bg-[#1E2124] p-3 text-white"
      style={{ aspectRatio: '9 / 16' }}
    >
      {/* Header */}
      <div className="mb-2 border-b border-white/10 pb-2">
        <div className="text-[11px] font-bold tracking-wide text-white">
          STING Tools
        </div>
        <div className="mt-2 flex gap-3 text-[9px] text-white/50">
          {['SELECT', 'ORGANISE', 'TAGS', 'CREATE'].map((t) => (
            <span
              key={t}
              className={
                t === 'TAGS'
                  ? 'border-b-2 border-orange pb-1 font-bold text-white'
                  : 'pb-1'
              }
            >
              {t}
            </span>
          ))}
        </div>
      </div>

      {/* Form */}
      <div className="space-y-1.5">
        {params.map(([k, v]) => (
          <div
            key={k}
            className="flex items-center justify-between rounded bg-white/5 px-2 py-1.5"
          >
            <span className="text-[9px] font-bold tracking-wider text-white/60">
              {k}
            </span>
            <span className="rounded bg-orange/20 px-1.5 py-0.5 text-[9px] font-bold text-orange">
              {v}
            </span>
          </div>
        ))}
      </div>

      {/* Generated tag */}
      <div className="mt-3">
        <div className="mb-1 text-[8px] font-bold tracking-wider text-white/40">
          GENERATED TAG
        </div>
        <div className="rounded-md border border-orange/60 bg-orange/5 px-2 py-2 text-center font-mono text-[9px] font-bold text-orange">
          M-BLD1-Z01-L03-HVAC-SUP-AHU-0042
        </div>
      </div>

      {/* Buttons */}
      <div className="mt-3 space-y-1.5">
        <button className="w-full rounded bg-orange py-1.5 text-[10px] font-semibold text-white">
          Auto Tag
        </button>
        <button className="w-full rounded border border-white/20 py-1.5 text-[10px] font-semibold text-white/80">
          Validate
        </button>
      </div>

      <div className="mt-3 border-t border-white/10 pt-2 text-[8px] text-white/30">
        ISO 19650 · 8-segment format
      </div>
    </div>
  );
}
