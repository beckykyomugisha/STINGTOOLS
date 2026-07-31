import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DataGrid, type Column, type DataGridProps } from './DataGrid';
import { MenuItem } from '@/components/shell/Menu';
import { ToastProvider } from './toast';

/**
 * U3 — the DataGrid is where `docs/ACC_UI_SHELL_GRID_CONTRACT.md` becomes code,
 * so the contract is tested rather than described. The clause that matters most
 * is the failure path: an optimistic edit that the server rejects must ROLL BACK.
 * If it doesn't, the user is looking at a value that does not exist on the
 * server and has no way to know — the worst possible outcome for a coordination
 * tool, and invisible to a typecheck or a build.
 */

interface Row {
  id: string;
  title: string;
  status: string;
}

const rows: Row[] = [
  { id: '1', title: 'Beta duct clash', status: 'Open' },
  { id: '2', title: 'Alpha door swing', status: 'Closed' },
  { id: '3', title: 'Gamma pipe route', status: 'Open' },
];

function grid(cols: Column<Row>[], props: Partial<DataGridProps<Row>> = {}) {
  return render(
    <ToastProvider>
      <DataGrid<Row> rows={rows} columns={cols} rowId={(r) => r.id} {...props} />
    </ToastProvider>,
  );
}

const plain: Column<Row>[] = [
  { key: 'title', header: 'Title' },
  { key: 'status', header: 'Status' },
];

afterEach(cleanup);

describe('DataGrid — display', () => {
  it('renders a row per record', () => {
    grid(plain);
    expect(screen.getByText('Beta duct clash')).toBeDefined();
    expect(screen.getAllByRole('row')).toHaveLength(rows.length + 1); // + header
  });

  it('shows an em dash for an empty cell rather than a blank gap', () => {
    render(
      <ToastProvider>
        <DataGrid<Row> rows={[{ id: '1', title: '', status: 'Open' }]} columns={plain} rowId={(r) => r.id} />
      </ToastProvider>,
    );
    expect(screen.getByText('—')).toBeDefined();
  });

  it('shows an empty state instead of a bare table when there are no rows', () => {
    render(
      <ToastProvider>
        <DataGrid<Row> rows={[]} columns={plain} rowId={(r) => r.id} emptyTitle="No clashes" />
      </ToastProvider>,
    );
    expect(screen.getByText('No clashes')).toBeDefined();
  });

  it('surfaces a load error', () => {
    render(
      <ToastProvider>
        <DataGrid<Row> rows={null} columns={plain} rowId={(r) => r.id} error="Boom" />
      </ToastProvider>,
    );
    expect(screen.getByRole('alert').textContent).toContain('Boom');
  });
});

describe('DataGrid — sort + filter', () => {
  it('sorts ascending, then descending, then back to natural order', async () => {
    const user = userEvent.setup();
    grid(plain);
    const header = screen.getByRole('button', { name: /Title/ });

    const titles = () =>
      screen.getAllByRole('row').slice(1).map((r) => within(r).getAllByRole('cell')[0].textContent);

    expect(titles()).toEqual(['Beta duct clash', 'Alpha door swing', 'Gamma pipe route']);
    await user.click(header);
    expect(titles()).toEqual(['Alpha door swing', 'Beta duct clash', 'Gamma pipe route']);
    await user.click(header);
    expect(titles()).toEqual(['Gamma pipe route', 'Beta duct clash', 'Alpha door swing']);
    await user.click(header);
    // Third click clears the sort — the user can get back to server order.
    expect(titles()).toEqual(['Beta duct clash', 'Alpha door swing', 'Gamma pipe route']);
  });

  it('filters across every column, not just the first', async () => {
    const user = userEvent.setup();
    grid(plain);
    await user.type(screen.getByLabelText('Filter rows'), 'closed');
    expect(screen.getAllByRole('row')).toHaveLength(2); // header + the one match
    expect(screen.getByText('Alpha door swing')).toBeDefined();
  });

  it('explains an empty result as a filter miss, not as "no data"', async () => {
    const user = userEvent.setup();
    grid(plain);
    await user.type(screen.getByLabelText('Filter rows'), 'zzzz');
    expect(screen.getByText(/No rows match that filter/i)).toBeDefined();
  });
});

describe('DataGrid — selection', () => {
  it('select-all covers only the rows currently visible after filtering', async () => {
    const user = userEvent.setup();
    grid(plain, { selectable: true });
    await user.type(screen.getByLabelText('Filter rows'), 'open');
    await user.click(screen.getByLabelText('Select all rows'));
    // Two rows match "Open"; select-all must not silently capture the hidden third.
    expect(screen.getByText('2 selected')).toBeDefined();
  });

  it('does not navigate when a checkbox is clicked', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    grid(plain, { selectable: true, onRowClick });
    await user.click(screen.getByLabelText('Select row 1'));
    expect(onRowClick).not.toHaveBeenCalled();
  });
});

describe('DataGrid — inline edit (the contract)', () => {
  const editable = (save: (row: Row, v: string) => Promise<unknown>): Column<Row>[] => [
    { key: 'title', header: 'Title' },
    { key: 'status', header: 'Status', edit: { options: ['Open', 'Closed'], save } },
  ];

  it('applies the new value optimistically and calls the endpoint', async () => {
    const user = userEvent.setup();
    // Typed explicitly: an inferred `async () => ({})` gives vi.fn an empty
    // parameter tuple, and reading mock.calls[0][1] then fails to typecheck.
    const save = vi.fn<(row: Row, value: string) => Promise<unknown>>(async () => ({}));
    grid(editable(save));

    await user.click(screen.getAllByTitle('Click to edit')[0]);
    await user.selectOptions(screen.getByRole('combobox'), 'Closed');

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][1]).toBe('Closed');
    await waitFor(() => expect(screen.getByText(/Status updated/i)).toBeDefined());
  });

  it('ROLLS BACK the cell and shows the server message when the save fails', async () => {
    const user = userEvent.setup();
    const save = vi.fn(async () => {
      throw new Error('Conflict: someone else changed this');
    });
    grid(editable(save));

    const cell = screen.getAllByTitle('Click to edit')[0];
    expect(cell.textContent).toBe('Open');

    await user.click(cell);
    await user.selectOptions(screen.getByRole('combobox'), 'Closed');

    // The toast carries the server's own message…
    await waitFor(() => expect(screen.getByText(/someone else changed this/i)).toBeDefined());
    // …and the cell is back to what the server still holds.
    await waitFor(() => expect(screen.getAllByTitle('Click to edit')[0].textContent).toBe('Open'));
  });

  it('does not call the endpoint when the value is unchanged', async () => {
    const user = userEvent.setup();
    const save = vi.fn(async () => ({}));
    grid(editable(save));
    await user.click(screen.getAllByTitle('Click to edit')[0]);
    await user.selectOptions(screen.getByRole('combobox'), 'Open'); // same value
    expect(save).not.toHaveBeenCalled();
  });

  it('editing a cell never triggers the row navigation', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    grid(editable(async () => ({})), { onRowClick });
    await user.click(screen.getAllByTitle('Click to edit')[0]);
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('Escape abandons a text edit instead of committing it', async () => {
    const user = userEvent.setup();
    const save = vi.fn(async () => ({}));
    // filterable:false so the only textbox on screen is the cell editor —
    // otherwise this matches the toolbar's filter box too.
    grid([{ key: 'title', header: 'Title', edit: { save } }], { filterable: false });
    await user.click(screen.getAllByTitle('Click to edit')[0]);
    await user.type(screen.getByRole('textbox'), 'xyz');
    await user.keyboard('{Escape}');
    expect(save).not.toHaveBeenCalled();
  });
});

describe('DataGrid — right-click row menu (U6)', () => {
  const menu = (onOpen = vi.fn()) => ({
    rowMenu: (r: Row, close: () => void) => (
      <MenuItem
        onClick={() => {
          close();
          onOpen(r.id);
        }}
      >
        Open
      </MenuItem>
    ),
  });

  it('does not attach a context menu when no rowMenu is given', async () => {
    const user = userEvent.setup();
    grid(plain);
    await user.pointer({ keys: '[MouseRight]', target: screen.getByText('Beta duct clash') });
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('opens at the right-clicked row and runs that row’s action', async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    grid(plain, menu(onOpen));
    await user.pointer({ keys: '[MouseRight]', target: screen.getByText('Gamma pipe route') });
    const m = screen.getByRole('menu');
    await user.click(within(m).getByRole('menuitem', { name: 'Open' }));
    // The row that was right-clicked, not the first row.
    expect(onOpen).toHaveBeenCalledWith('3');
  });

  it('closes after the action runs', async () => {
    const user = userEvent.setup();
    grid(plain, menu());
    await user.pointer({ keys: '[MouseRight]', target: screen.getByText('Beta duct clash') });
    await user.click(screen.getByRole('menuitem', { name: 'Open' }));
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
  });

  it('Escape closes it', async () => {
    const user = userEvent.setup();
    grid(plain, menu());
    await user.pointer({ keys: '[MouseRight]', target: screen.getByText('Beta duct clash') });
    expect(screen.getByRole('menu')).toBeDefined();
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
  });

  it('right-clicking a row does NOT also fire the row navigation', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    grid(plain, { ...menu(), onRowClick });
    await user.pointer({ keys: '[MouseRight]', target: screen.getByText('Beta duct clash') });
    expect(onRowClick).not.toHaveBeenCalled();
  });
});
