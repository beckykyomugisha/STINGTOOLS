export interface QrPayload {
  type: 'element' | 'issue' | 'document' | 'unknown';
  id?: string;
  raw: string;
}

export function parseQr(raw: string): QrPayload {
  const trimmed = raw.trim();
  const match = trimmed.match(
    /^planscape:\/\/(element|issue|document)\/([a-zA-Z0-9-]+)$/,
  );
  if (match) {
    return { type: match[1] as QrPayload['type'], id: match[2], raw: trimmed };
  }
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(trimmed)) {
    return { type: 'element', id: trimmed, raw: trimmed };
  }
  return { type: 'unknown', raw: trimmed };
}
