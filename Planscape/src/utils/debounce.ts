/**
 * Phase 96 — tiny debounce utility. Used for search inputs so the filter
 * logic doesn't re-run on every keystroke on a 500+ item list.
 */
export function debounce<Args extends unknown[]>(
  fn: (...args: Args) => void,
  delayMs: number,
): (...args: Args) => void {
  let handle: ReturnType<typeof setTimeout> | null = null;
  return (...args: Args) => {
    if (handle) clearTimeout(handle);
    handle = setTimeout(() => { fn(...args); }, delayMs);
  };
}
