'use client';

import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { cn } from '@/lib/cn';
import { EmptyState, ErrorNote, Input, Select, SkeletonRows } from './primitives';
import { useToast } from './toast';

/**
 * U3 — the DataGrid. Implements the contract in
 * `docs/ACC_UI_SHELL_GRID_CONTRACT.md`: client-side sort + filter, checkbox
 * selection, and OPTIMISTIC per-cell inline edit that rolls back and toasts on
 * failure. Last write wins; there is no merge UI in v1, by decision.
 *
 * Deliberately not a table library. The requirement is a few hundred rows with
 * an editable cell — @tanstack/react-table would add a dependency and an API
 * surface for virtualisation and column pinning nobody asked for. If a grid ever
 * needs to hold 50k rows, that is the moment to reconsider, not now.
 */

export interface Column<T> {
  key: string;
  header: string;
  /** Cell content. Defaults to `String(row[key])`. */
  render?: (row: T) => ReactNode;
  /** Value used for sorting + text filtering. Defaults to `render`-less raw value. */
  value?: (row: T) => string | number | null | undefined;
  sortable?: boolean;
  className?: string;
  /**
   * Makes the cell editable. `options` renders a <select>, otherwise a text
   * input. Only include fields the entity's write endpoint actually accepts —
   * see the grid contract; nothing here widens an API.
   */
  edit?: {
    options?: readonly string[];
    /** Called with the new value. Reject to trigger the rollback + toast. */
    save: (row: T, value: string) => Promise<unknown>;
    /** Current value for the editor. Defaults to `value()`. */
    current?: (row: T) => string;
  };
}

export interface DataGridProps<T> {
  rows: T[] | null;
  columns: Column<T>[];
  rowId: (row: T) => string;
  loading?: boolean;
  error?: string | null;
  /** Row click — opens a detail route. Inline editing never navigates. */
  onRowClick?: (row: T) => void;
  emptyTitle?: string;
  emptyDescription?: string;
  /** Toolbar rendered above the grid; receives the current selection. */
  toolbar?: (ctx: { selected: T[]; clearSelection: () => void }) => ReactNode;
  selectable?: boolean;
  /** Free-text filter box in the header row. */
  filterable?: boolean;
  /** Optimistically-applied local patches so the parent needn't refetch. */
  onRowPatched?: (id: string, key: string, value: string) => void;
}

type SortState = { key: string; dir: 'asc' | 'desc' } | null;

export function DataGrid<T>({
  rows,
  columns,
  rowId,
  loading,
  error,
  onRowClick,
  emptyTitle = 'Nothing here yet',
  emptyDescription,
  toolbar,
  selectable = false,
  filterable = true,
  onRowPatched,
}: DataGridProps<T>) {
  const { toast } = useToast();
  const [sort, setSort] = useState<SortState>(null);
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [savingCells, setSavingCells] = useState<Set<string>>(new Set());
  /** Cell overrides applied optimistically, keyed `${rowId}:${colKey}`. */
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const [editing, setEditing] = useState<string | null>(null);

  const valueOf = useCallback(
    (row: T, col: Column<T>): string => {
      const key = `${rowId(row)}:${col.key}`;
      if (key in overrides) return overrides[key];
      if (col.value) return String(col.value(row) ?? '');
      const raw = (row as Record<string, unknown>)[col.key];
      return raw == null ? '' : String(raw);
    },
    [overrides, rowId],
  );

  const filtered = useMemo(() => {
    if (!rows) return null;
    const q = query.trim().toLowerCase();
    if (!q) return rows;
    return rows.filter((r) => columns.some((c) => valueOf(r, c).toLowerCase().includes(q)));
  }, [rows, query, columns, valueOf]);

  const sorted = useMemo(() => {
    if (!filtered || !sort) return filtered;
    const col = columns.find((c) => c.key === sort.key);
    if (!col) return filtered;
    // Copy before sorting: mutating the parent's array in place makes React
    // skip the re-render, and the grid appears not to sort at all.
    return [...filtered].sort((a, b) => {
      const av = valueOf(a, col);
      const bv = valueOf(b, col);
      const an = Number(av);
      const bn = Number(bv);
      const cmp =
        av !== '' && bv !== '' && !Number.isNaN(an) && !Number.isNaN(bn)
          ? an - bn
          : av.localeCompare(bv, undefined, { numeric: true });
      return sort.dir === 'asc' ? cmp : -cmp;
    });
  }, [filtered, sort, columns, valueOf]);

  const clearSelection = useCallback(() => setSelected(new Set()), []);

  function toggleSort(col: Column<T>) {
    if (col.sortable === false) return;
    setSort((s) => (s?.key !== col.key ? { key: col.key, dir: 'asc' } : s.dir === 'asc' ? { key: col.key, dir: 'desc' } : null));
  }

  async function commit(row: T, col: Column<T>, next: string) {
    const id = rowId(row);
    const cellKey = `${id}:${col.key}`;
    const previous = valueOf(row, col);
    setEditing(null);
    if (next === previous || !col.edit) return;

    // Optimistic: paint the new value first, then persist.
    setOverrides((o) => ({ ...o, [cellKey]: next }));
    setSavingCells((s) => new Set(s).add(cellKey));
    try {
      await col.edit.save(row, next);
      onRowPatched?.(id, col.key, next);
      toast(`${col.header} updated`, 'success');
    } catch (e) {
      // Roll THIS cell back — not the row, not the grid. Last write wins is a
      // policy about the server; locally we must not leave a value on screen
      // that the server rejected.
      setOverrides((o) => {
        const copy = { ...o };
        delete copy[cellKey];
        return copy;
      });
      toast(e instanceof Error ? e.message : `Could not update ${col.header}`, 'error');
    } finally {
      setSavingCells((s) => {
        const copy = new Set(s);
        copy.delete(cellKey);
        return copy;
      });
    }
  }

  const selectedRows = useMemo(
    () => (rows || []).filter((r) => selected.has(rowId(r))),
    [rows, selected, rowId],
  );
  const allVisibleSelected = !!sorted?.length && sorted.every((r) => selected.has(rowId(r)));

  return (
    <div className="flex flex-col">
      {(toolbar || filterable) && (
        <div className="flex flex-wrap items-center gap-2 rounded-t-md border border-b-0 border-border bg-surface-2 px-2 py-1.5">
          {filterable && (
            <Input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Filter…"
              aria-label="Filter rows"
              className="h-7 w-40"
            />
          )}
          {selected.size > 0 && (
            <span className="text-xs text-fg-muted">{selected.size} selected</span>
          )}
          {toolbar?.({ selected: selectedRows, clearSelection })}
        </div>
      )}

      <div className="overflow-x-auto rounded-b-md border border-border bg-surface">
        {error && (
          <div className="p-2">
            <ErrorNote>{error}</ErrorNote>
          </div>
        )}

        {loading && !rows && <SkeletonRows rows={6} cols={Math.min(columns.length, 5)} />}

        {!loading && sorted && sorted.length === 0 && (
          <div className="p-2">
            <EmptyState
              title={query ? 'No rows match that filter' : emptyTitle}
              description={query ? 'Clear the filter to see everything.' : emptyDescription}
            />
          </div>
        )}

        {sorted && sorted.length > 0 && (
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-border bg-surface-2 text-left">
                {selectable && (
                  <th className="w-8 px-2 py-1.5">
                    <input
                      type="checkbox"
                      aria-label="Select all rows"
                      checked={allVisibleSelected}
                      onChange={(e) =>
                        setSelected(e.target.checked ? new Set(sorted.map(rowId)) : new Set())
                      }
                    />
                  </th>
                )}
                {columns.map((col) => {
                  const active = sort?.key === col.key;
                  return (
                    <th key={col.key} className={cn('px-2 py-1.5 font-medium text-fg-muted', col.className)}>
                      {col.sortable === false ? (
                        col.header
                      ) : (
                        <button
                          type="button"
                          onClick={() => toggleSort(col)}
                          aria-sort={active ? (sort!.dir === 'asc' ? 'ascending' : 'descending') : 'none'}
                          className="inline-flex items-center gap-1 transition hover:text-fg"
                        >
                          {col.header}
                          <span aria-hidden="true" className={cn('text-2xs', !active && 'opacity-0')}>
                            {active && sort!.dir === 'desc' ? '▼' : '▲'}
                          </span>
                        </button>
                      )}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {sorted.map((row) => {
                const id = rowId(row);
                const isSelected = selected.has(id);
                return (
                  <tr
                    key={id}
                    className={cn(
                      'border-b border-border last:border-0 transition',
                      isSelected ? 'bg-accent-subtle' : 'hover:bg-surface-3',
                      onRowClick && 'cursor-pointer',
                    )}
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                  >
                    {selectable && (
                      <td className="px-2 py-1.5" onClick={(e) => e.stopPropagation()}>
                        <input
                          type="checkbox"
                          aria-label={`Select row ${id}`}
                          checked={isSelected}
                          onChange={(e) =>
                            setSelected((s) => {
                              const copy = new Set(s);
                              if (e.target.checked) copy.add(id);
                              else copy.delete(id);
                              return copy;
                            })
                          }
                        />
                      </td>
                    )}
                    {columns.map((col) => {
                      const cellKey = `${id}:${col.key}`;
                      const saving = savingCells.has(cellKey);
                      const isEditing = editing === cellKey;
                      const current = valueOf(row, col);
                      return (
                        <td
                          key={col.key}
                          className={cn('px-2 py-1.5 align-top text-fg', col.className, saving && 'opacity-60')}
                          // Editing must never also trigger the row's navigation.
                          onClick={col.edit ? (e) => e.stopPropagation() : undefined}
                        >
                          {col.edit && isEditing ? (
                            col.edit.options ? (
                              <Select
                                autoFocus
                                defaultValue={current}
                                onBlur={() => setEditing(null)}
                                onChange={(e) => void commit(row, col, e.target.value)}
                                className="h-7"
                              >
                                {col.edit.options.map((o) => (
                                  <option key={o} value={o}>
                                    {o}
                                  </option>
                                ))}
                              </Select>
                            ) : (
                              <Input
                                autoFocus
                                defaultValue={current}
                                className="h-7"
                                onBlur={(e) => void commit(row, col, e.target.value)}
                                onKeyDown={(e) => {
                                  if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
                                  // Escape must abandon the edit, not commit it.
                                  if (e.key === 'Escape') setEditing(null);
                                }}
                              />
                            )
                          ) : col.edit ? (
                            <button
                              type="button"
                              onClick={() => setEditing(cellKey)}
                              title="Click to edit"
                              className="-mx-1 w-full rounded px-1 text-left transition hover:bg-surface-3"
                            >
                              {col.render ? col.render(row) : current || <span className="text-fg-subtle">—</span>}
                            </button>
                          ) : col.render ? (
                            col.render(row)
                          ) : (
                            current || <span className="text-fg-subtle">—</span>
                          )}
                        </td>
                      );
                    })}
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
