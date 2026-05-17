// Hook: useArchiCADLive
//
// Connects to the ArchiCAD live stream for the given projectId and
// returns a live feed of element events + current model status.
//
// Usage (any screen):
//   const { events, status, isLive } = useArchiCADLive(projectId);

import { useEffect, useRef, useState } from 'react';
import { archiCADLive, ArchiCADElement, ModelStatus } from '../services/archiCADLiveClient';

const MAX_EVENTS = 200; // keep last 200 changes in memory

export function useArchiCADLive(projectId: string | null) {
  const [events,  setEvents]  = useState<ArchiCADElement[]>([]);
  const [status,  setStatus]  = useState<ModelStatus | null>(null);
  const [isLive,  setIsLive]  = useState(false);
  const [error,   setError]   = useState<string | null>(null);
  const connected = useRef(false);

  useEffect(() => {
    if (!projectId) return;

    archiCADLive.connect(projectId)
      .then(() => { connected.current = true; setError(null); })
      .catch(e  => setError(e.message));

    const onElement = (ev: ArchiCADElement) =>
      setEvents(prev => [ev, ...prev].slice(0, MAX_EVENTS));

    const onStatus = (s: ModelStatus) => {
      setStatus(s);
      setIsLive(s.isLive ?? true);
    };

    archiCADLive.on('ElementChanged', onElement);
    archiCADLive.on('ElementAdded',   onElement);
    archiCADLive.on('ElementDeleted', onElement);
    archiCADLive.on('ModelStatus',    onStatus);

    return () => {
      archiCADLive.off(onElement);
      archiCADLive.off(onStatus);
      if (connected.current) {
        archiCADLive.disconnect();
        connected.current = false;
      }
    };
  }, [projectId]);

  return { events, status, isLive, error };
}
