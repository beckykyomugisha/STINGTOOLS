import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Menu, MenuItem } from './Menu';

/**
 * U5 — keyboard + ARIA behaviour of the shell's popover menus (account, project
 * and tenant switchers). Every assertion here is something a mouse-only check
 * would pass and a keyboard user would hit immediately.
 */

function harness(onPick = vi.fn()) {
  render(
    <Menu label="Account menu" trigger={<span>AB</span>}>
      {(close) => (
        <>
          <MenuItem
            onClick={() => {
              onPick('one');
              close();
            }}
          >
            One
          </MenuItem>
          <MenuItem onClick={() => onPick('two')}>Two</MenuItem>
          <MenuItem disabled onClick={() => onPick('three')}>
            Three
          </MenuItem>
        </>
      )}
    </Menu>,
  );
  return onPick;
}

afterEach(cleanup);

describe('shell Menu', () => {
  it('exposes menu semantics on the trigger', async () => {
    const user = userEvent.setup();
    harness();
    const trigger = screen.getByRole('button', { name: 'Account menu' });
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    await user.click(trigger);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('menu')).toBeDefined();
  });

  it('moves through items with the arrow keys', async () => {
    const user = userEvent.setup();
    harness();
    await user.click(screen.getByRole('button', { name: 'Account menu' }));

    await user.keyboard('{ArrowDown}');
    expect(document.activeElement?.textContent).toBe('One');
    await user.keyboard('{ArrowDown}');
    expect(document.activeElement?.textContent).toBe('Two');
    // Wraps rather than dead-ending — a disabled item is skipped entirely.
    await user.keyboard('{ArrowDown}');
    expect(document.activeElement?.textContent).toBe('One');
    await user.keyboard('{ArrowUp}');
    expect(document.activeElement?.textContent).toBe('Two');
  });

  it('supports Home and End', async () => {
    const user = userEvent.setup();
    harness();
    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.keyboard('{End}');
    expect(document.activeElement?.textContent).toBe('Two'); // last ENABLED item
    await user.keyboard('{Home}');
    expect(document.activeElement?.textContent).toBe('One');
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    harness();
    const trigger = screen.getByRole('button', { name: 'Account menu' });
    await user.click(trigger);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).toBeNull();
    // Stranding focus at the top of the document is the classic popover bug.
    expect(document.activeElement).toBe(trigger);
  });

  it('closes when clicking outside', async () => {
    const user = userEvent.setup();
    harness();
    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(document.body);
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('activates an item with the keyboard', async () => {
    const user = userEvent.setup();
    const onPick = harness();
    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.keyboard('{ArrowDown}{Enter}');
    expect(onPick).toHaveBeenCalledWith('one');
    expect(screen.queryByRole('menu')).toBeNull(); // the item called close()
  });

  it('never activates a disabled item', async () => {
    const user = userEvent.setup();
    const onPick = harness();
    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('menuitem', { name: 'Three' }));
    expect(onPick).not.toHaveBeenCalled();
  });
});
