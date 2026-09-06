/** Compliance RAG chip. Renders nothing when there's no signal. */
export function RagBadge({ rag, percent }: { rag?: string; percent?: number }) {
  if (!rag && percent == null) return null;
  const key = (rag || '').toUpperCase();
  const cls =
    key === 'GREEN'
      ? 'bg-success-subtle text-success'
      : key === 'AMBER'
        ? 'bg-warning-subtle text-warning'
        : key === 'RED'
          ? 'bg-danger-subtle text-danger'
          : 'bg-surface-3 text-fg-muted';
  const label = percent != null ? `${Math.round(percent)}%` : key || '—';
  return (
    <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium ${cls}`} title={key ? `Compliance: ${key}` : undefined}>
      {label}
    </span>
  );
}
