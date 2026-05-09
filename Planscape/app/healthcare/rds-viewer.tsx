// Healthcare Pack H-21 — Read-only Room Data Sheet viewer (mobile).
import { useState } from 'react';
import { View, Text, ScrollView, StyleSheet, TextInput, TouchableOpacity, Alert } from 'react-native';
import { useProjectStore } from '@/stores/projectStore';
import { getRdsSnapshot } from '@/api/endpoints';

export default function RdsViewerScreen() {
  const activeProject = useProjectStore((s) => s.active);
  const [roomId, setRoomId] = useState('');
  const [rds, setRds] = useState<any>(null);

  const load = async () => {
    if (!roomId) return;
    if (!activeProject?.id) { Alert.alert('No project', 'Select a project first.'); return; }
    try {
      const raw: any = await getRdsSnapshot(activeProject.id, roomId);
      const ctx = raw?.contextJson ? JSON.parse(raw.contextJson) : {};
      setRds({
        roomId,
        name: raw?.roomName ?? ctx.doc?.name ?? '—',
        class: raw?.roomClass ?? ctx.doc?.health_class ?? '—',
        area: ctx.doc?.area ?? '—',
        achReq: ctx.doc?.['ach.req'] ?? '—',
        pressRegime: ctx.doc?.['press.regime'] ?? '—',
        deltaPa: ctx.doc?.['press.delta_pa'] ?? '—',
        infectClass: ctx.doc?.['infect.class'] ?? '—',
        tempC: ctx.doc?.['temp.design_c'] ?? '—',
        services: ctx.services ?? [],
        equipment: ctx.equipment ?? [],
        finishes: ctx.finishes ?? [],
      });
    } catch (e: any) {
      Alert.alert('Not found', `No RDS snapshot for ${roomId} on the server. ${e?.message ?? ''}`);
    }
  };

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.h1}>Room Data Sheet</Text>
      <Text style={styles.h2}>{activeProject?.name ?? '—'}</Text>
      <Text style={styles.lbl}>Room ID</Text>
      <TextInput style={styles.input} value={roomId} onChangeText={setRoomId} placeholder="e.g. ICU-04" />
      <TouchableOpacity style={styles.btn} onPress={load}><Text style={styles.btnText}>Load RDS</Text></TouchableOpacity>
      {rds && (
        <View style={styles.card}>
          <Text style={styles.k}>Name: <Text style={styles.v}>{rds.name}</Text></Text>
          <Text style={styles.k}>Class: <Text style={styles.v}>{rds.class}</Text></Text>
          <Text style={styles.k}>Area: <Text style={styles.v}>{rds.area} m²</Text></Text>
          <Text style={styles.k}>ACH (req): <Text style={styles.v}>{rds.achReq}</Text></Text>
          <Text style={styles.k}>Pressure: <Text style={styles.v}>{rds.pressRegime} ({rds.deltaPa} Pa)</Text></Text>
          <Text style={styles.k}>Infection class: <Text style={styles.v}>{rds.infectClass}</Text></Text>
          <Text style={styles.k}>Temp design: <Text style={styles.v}>{rds.tempC} °C</Text></Text>
          <Text style={styles.section}>Services</Text>
          {rds.services.length === 0 ? <Text style={styles.empty}>—</Text> :
            rds.services.map((s: any, i: number) => <Text key={i} style={styles.row}>• {s.type} × {s.count}</Text>)}
        </View>
      )}
    </ScrollView>
  );
}
const styles = StyleSheet.create({
  container: { padding: 16 },
  h1: { fontSize: 22, fontWeight: '700' },
  h2: { fontSize: 13, color: '#666', marginBottom: 16 },
  lbl: { fontSize: 13, color: '#444', marginTop: 10 },
  input: { backgroundColor: '#fff', padding: 10, borderRadius: 4, borderWidth: 1, borderColor: '#ccc' },
  btn: { backgroundColor: '#1976D2', padding: 14, borderRadius: 6, marginTop: 16, alignItems: 'center' },
  btnText: { color: '#fff', fontWeight: '700' },
  card: { backgroundColor: '#fff', padding: 14, borderRadius: 6, marginTop: 18 },
  k: { fontSize: 13, color: '#444', marginBottom: 4 },
  v: { fontWeight: '600', color: '#000' },
  section: { fontSize: 14, fontWeight: '600', marginTop: 12, marginBottom: 4 },
  row: { fontSize: 12, color: '#444' },
  empty: { fontSize: 12, color: '#888', fontStyle: 'italic' },
});
