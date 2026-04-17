import { useState } from 'react';
import { api } from '../services/apiClient';
import { parseQr, QrPayload } from '../services/qrParser';

export function useQrScan() {
  const [lookingUp, setLookingUp] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleScan(raw: string): Promise<{ payload: QrPayload; data: unknown | null }> {
    const payload = parseQr(raw);
    setError(null);
    if (payload.type === 'unknown' || !payload.id) {
      return { payload, data: null };
    }
    setLookingUp(true);
    try {
      let data: unknown = null;
      if (payload.type === 'element') data = await api.lookupElement(payload.id);
      return { payload, data };
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      return { payload, data: null };
    } finally {
      setLookingUp(false);
    }
  }

  return { handleScan, lookingUp, error };
}
