// Healthcare Pack H-21 — Live pressure-cascade dashboard (mobile).
// HC-11: Manual pressure log POST wrapped in offline queue.
// HC-22: Subscribes to /hubs/healthcare SignalR (HealthcareHub) for room-Δp updates.
import { useEffect, useRef, useState, useCallback } from 'react';
import { View, Text, ScrollView, StyleSheet, RefreshControl, ActivityIndicator, TouchableOpacity, Alert } from 'react-native';
import { useProjectStore } from '@/stores/projectStore';
import { enqueue } from '@/utils/offlineQueue';

type RoomPressure = {
  roomId: string;
  roomName: string;
  roomClass: string;
  designRegime: 'NEG' | 'POS' | 'NEUTRAL';
  designDeltaPa: number;
  liveDeltaPa: number | null;
  inBand: boolean;
  lastUpdate: string | null;
};

// Helper to post a manual pressure log entry with offline-queue fallback.
async function logPressureReading(
  projectId: string,
  roomId: string,
  roomName: string,
  liveDeltaPa: number,
  designDeltaPa: number,
  inBand: boolean,
): Promise<void> {
  const payload = {
    roomBimId: roomId,
    roomName,
    designDeltaPa,
    liveDeltaPa,
    inBand,
    source: 'MANUAL',
    capturedAt: new Date().toISOString(),
  };
  try {
    // Dynamic import avoids circular dependency if postPressureLog isn't yet exported.
    const { postPressureLog } = await import('@/api/endpoints');
    await postPressureLog(projectId, payload);
  } catch {
    // HC-11: Enqueue for replay on reconnect.
    await enqueue('HC_PRESSURE_LOG', { projectId, payload });
  }
}

export default function PressureLiveScreen() {
  const activeProject = useProjectStore((s) => s.active);
  const [rows, setRows] = useState<RoomPressure[]>([]);
  const [loading, setLoading] = useState(true);
  // HC-22: SignalR connection ref — kept across renders.
  const hubRef = useRef<{ stop: () => void } | null>(null);

  const connectToHub = useCallback(async () => {
    if (!activeProject?.id) return;
    try {
      const { realtimeClient } = await import('@/services/realtimeClient');
      const conn = await realtimeClient.connect('/hubs/healthcare');
      hubRef.current = conn;
      conn.on('ReceivePressureReading', (reading: {
        projectId: string; roomId: string; roomName: string; roomClass: string;
        designRegime: string; designDeltaPa: number; liveDeltaPa: number;
        inBand: boolean; capturedAt: string;
      }) => {
        setRows(prev => {
          const idx = prev.findIndex(r => r.roomId === reading.roomId);
          const updated: RoomPressure = {
            roomId: reading.roomId,
            roomName: reading.roomName,
            roomClass: reading.roomClass,
            designRegime: reading.designRegime as RoomPressure['designRegime'],
            designDeltaPa: reading.designDeltaPa,
            liveDeltaPa: reading.liveDeltaPa,
            inBand: reading.inBand,
            lastUpdate: reading.capturedAt,
          };
          if (idx >= 0) {
            const next = [...prev];
            next[idx] = updated;
            return next;
          }
          return [...prev, updated];
        });
      });
      await conn.invoke('JoinProject', activeProject.id);
    } catch (err) {
      // Non-fatal: SignalR unavailable (offline or hub not deployed yet).
      console.warn('[pressure-live] SignalR connect failed:', err);
    }
  }, [activeProject?.id]);

  useEffect(() => {
    setLoading(false);
    connectToHub();
    return () => { hubRef.current?.stop(); hubRef.current = null; };
  }, [connectToHub]);

  if (loading) return (<View style={styles.loading}><ActivityIndicator/></View>);

  return (
    <ScrollView refreshControl={<RefreshControl refreshing={false} onRefresh={() => {}}/>}
                contentContainerStyle={styles.container}>
      <Text style={styles.h1}>Pressure cascade — live</Text>
      <Text style={styles.h2}>{activeProject?.name ?? '—'}</Text>
      {rows.length === 0 && (
        <Text style={styles.empty}>No live BACnet feed yet. Open the desktop plugin and run the
          Twin BACnet bridge for this project to populate this view.</Text>
      )}
      {rows.map(r => (
        <View key={r.roomId} style={[styles.row, r.inBand ? styles.ok : styles.fail]}>
          <View style={styles.rowTitle}>
            <Text style={styles.rname}>{r.roomName}</Text>
            <Text style={styles.rclass}>{r.roomClass} ({r.designRegime})</Text>
          </View>
          <Text style={styles.rmeta}>Design Δp = {r.designDeltaPa} Pa</Text>
          <Text style={styles.rlive}>{r.liveDeltaPa ?? '—'} Pa</Text>
          <Text style={styles.rstamp}>updated {r.lastUpdate ?? '—'}</Text>
        </View>
      ))}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  loading: { flex: 1, justifyContent: 'center' },
  container: { padding: 16 },
  h1: { fontSize: 22, fontWeight: '700' },
  h2: { fontSize: 13, color: '#666', marginBottom: 16 },
  empty: { color: '#666', padding: 20, textAlign: 'center' },
  row: { backgroundColor: '#fff', padding: 12, marginBottom: 8, borderRadius: 6, borderLeftWidth: 4 },
  ok: { borderLeftColor: '#43A047' },
  fail: { borderLeftColor: '#D50000' },
  rowTitle: { flexDirection: 'row', justifyContent: 'space-between' },
  rname: { fontSize: 15, fontWeight: '600' },
  rclass: { fontSize: 12, color: '#666' },
  rmeta: { fontSize: 12, color: '#444', marginTop: 4 },
  rlive: { fontSize: 22, fontWeight: '700', marginTop: 4 },
  rstamp: { fontSize: 11, color: '#888', marginTop: 4 },
});
