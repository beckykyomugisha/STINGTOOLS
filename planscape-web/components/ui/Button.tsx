'use client';

import { forwardRef, type ButtonHTMLAttributes } from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/cn';

/**
 * U3 — Button, shadcn/ui shape (cva variants + `asChild`), styled to the U1
 * tokens rather than shadcn's default palette.
 *
 * `asChild` matters more than it looks: it lets a Next.js `<Link>` be styled as
 * a button without nesting an `<a>` inside a `<button>`, which is invalid HTML
 * and breaks keyboard activation.
 */
const button = cva(
  'inline-flex items-center justify-center gap-1.5 rounded font-medium transition disabled:pointer-events-none disabled:opacity-50 whitespace-nowrap',
  {
    variants: {
      variant: {
        primary: 'bg-accent text-fg-on-accent hover:bg-accent-hover',
        secondary: 'border border-border bg-surface text-fg hover:bg-surface-3',
        ghost: 'text-fg-muted hover:bg-surface-3 hover:text-fg',
        danger: 'bg-danger text-fg-on-accent hover:opacity-90',
        link: 'text-accent underline-offset-2 hover:underline',
      },
      size: {
        sm: 'h-7 px-2 text-xs',
        md: 'h-8 px-3 text-sm',
        lg: 'h-9 px-4 text-base',
        icon: 'h-8 w-8 p-0',
      },
    },
    defaultVariants: { variant: 'secondary', size: 'md' },
  },
);

export interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof button> {
  asChild?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { className, variant, size, asChild = false, type, ...props },
  ref,
) {
  const Comp = asChild ? Slot : 'button';
  return (
    <Comp
      ref={ref}
      // Default to type="button". An unspecified <button> inside a <form> is a
      // SUBMIT button — the classic "why did my filter bar reload the page".
      type={asChild ? undefined : (type ?? 'button')}
      className={cn(button({ variant, size }), className)}
      {...props}
    />
  );
});
