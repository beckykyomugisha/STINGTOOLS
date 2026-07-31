'use client';

// Global error boundary — App Router renders this on an uncaught render/runtime
// error in a route subtree, instead of a blank screen.
export default function GlobalError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <div className="grid min-h-screen place-items-center bg-surface-2 p-6">
      <div className="max-w-md rounded-lg border border-border bg-surface p-6 text-center">
        <h1 className="text-lg font-semibold">Something went wrong</h1>
        <p className="mt-2 text-sm text-fg-muted">{error.message || 'An unexpected error occurred.'}</p>
        <button
          onClick={reset}
          className="mt-4 rounded bg-accent px-4 py-2 text-sm font-medium text-fg-on-accent hover:bg-accent-hover"
        >
          Try again
        </button>
      </div>
    </div>
  );
}
