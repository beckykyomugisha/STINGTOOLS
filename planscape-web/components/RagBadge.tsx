/** Compliance RAG chip. Renders nothing when there's no signal. */
export function RagBadge({ rag, percent }: { rag?: string; percent?: number }) {
  if (!rag && percent == null) return null;
  const key = (rag || '').toUpperCase();
  const cls =
    key === 'GREEN'
      ? 'bg-green-100 text-green-700'
      : key === 'AMBER'
        ? 'bg-amber-100 text-amber-700'
        : key === 'RED'
          ? 'bg-red-100 text-red-700'
          : 'bg-slate-100 text-slate-600';
  const label = percent != null ? `${Math.round(percent)}%` : key || '—';
  return (
    <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium ${cls}`} title={key ? `Compliance: ${key}` : undefined}>
      {label}
    </span>
  );
}
