'use client';

import type { PortalFilterState } from './types';

interface Props {
  value: PortalFilterState;
  onChange: (next: PortalFilterState) => void;
  levels: string[];
  zones: string[];
  onClear: () => void;
}

export default function PortalFilters({
  value,
  onChange,
  levels,
  zones,
  onClear,
}: Props) {
  const hasFilters =
    !!value.from || !!value.to || !!value.levelCode || !!value.zoneCode;

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-slate-700">From</span>
          <input
            type="date"
            value={value.from ?? ''}
            onChange={(e) =>
              onChange({ ...value, from: e.target.value || undefined })
            }
            className="rounded-md border border-slate-300 px-2 py-1.5 focus:border-orange focus:outline-none focus:ring-1 focus:ring-orange"
          />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-slate-700">To</span>
          <input
            type="date"
            value={value.to ?? ''}
            onChange={(e) =>
              onChange({ ...value, to: e.target.value || undefined })
            }
            className="rounded-md border border-slate-300 px-2 py-1.5 focus:border-orange focus:outline-none focus:ring-1 focus:ring-orange"
          />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-slate-700">Level</span>
          <select
            value={value.levelCode ?? ''}
            onChange={(e) =>
              onChange({
                ...value,
                levelCode: e.target.value || undefined,
              })
            }
            className="rounded-md border border-slate-300 bg-white px-2 py-1.5 focus:border-orange focus:outline-none focus:ring-1 focus:ring-orange"
          >
            <option value="">All levels</option>
            {levels.map((l) => (
              <option key={l} value={l}>
                {l}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-slate-700">Zone</span>
          <select
            value={value.zoneCode ?? ''}
            onChange={(e) =>
              onChange({
                ...value,
                zoneCode: e.target.value || undefined,
              })
            }
            className="rounded-md border border-slate-300 bg-white px-2 py-1.5 focus:border-orange focus:outline-none focus:ring-1 focus:ring-orange"
          >
            <option value="">All zones</option>
            {zones.map((z) => (
              <option key={z} value={z}>
                {z}
              </option>
            ))}
          </select>
        </label>

        <div className="flex items-end">
          <button
            type="button"
            onClick={onClear}
            disabled={!hasFilters}
            className="w-full rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 transition hover:border-orange hover:text-orange disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:border-slate-300 disabled:hover:text-slate-700"
          >
            Clear filters
          </button>
        </div>
      </div>
    </div>
  );
}
