import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * U3 — the shadcn/ui class helper. `clsx` resolves conditionals; `twMerge`
 * resolves Tailwind CONFLICTS, so a caller's `px-4` actually beats a
 * component's default `px-2` instead of both landing in the class list and the
 * winner being decided by stylesheet order (i.e. at random).
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
