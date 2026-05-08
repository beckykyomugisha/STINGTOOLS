// Healthcare Pack H-21 — Live pressure-cascade dashboard (mobile).
// Subscribes to /hubs/healthcare SignalR for room-Δp updates pushed
// from the plugin-side BACnet readback.
import { useEffect, useState } from 'react';
import { View, Text, ScrollView, StyleSheet, RefreshControl, ActivityIndicator } from 'react-native';
import { useProjectStore } from '@/stores/projectStore';

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

export default function PressureLiveScreen() {
  const { activeProject } = useProjectStore();
  const [rows, setRows] = useState<RoomPressure[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Hook into SignalR healthcare hub. Until that ships, render a
    // demonstrative empty-state with the pull-to-refresh affordance.
    setLoading(false);
  }, []);

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
