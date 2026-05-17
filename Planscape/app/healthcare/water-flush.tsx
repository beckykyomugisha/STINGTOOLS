// Healthcare Pack H-21 — HTM 04-01 sentinel flushing log (mobile).
import { useState } from 'react';
import { View, Text, ScrollView, StyleSheet, TextInput, TouchableOpacity } from 'react-native';
import { useProjectStore } from '@/stores/projectStore';

export default function WaterFlushScreen() {
  const activeProject = useProjectStore((s) => s.active);
  const [outletId, setOutletId] = useState('');
  const [tempC, setTempC] = useState('');
  const [duration, setDuration] = useState('');
  const [history, setHistory] = useState<{ id: string; t: string; d: string; ts: string }[]>([]);

  const log = () => {
    if (!outletId || !tempC) { alert('Enter outlet ID and temperature.'); return; }
    setHistory(h => [{ id: outletId, t: tempC, d: duration, ts: new Date().toISOString() }, ...h].slice(0, 50));
    setOutletId(''); setTempC(''); setDuration('');
  };

  return (
    <ScrollView contentContainerStyle={styles.container}>
      <Text style={styles.h1}>Sentinel flushing log</Text>
      <Text style={styles.h2}>{activeProject?.name ?? '—'} — HTM 04-01</Text>
      <Text style={styles.lbl}>Outlet ID</Text>
      <TextInput style={styles.input} value={outletId} onChangeText={setOutletId} placeholder="e.g. PLM-DCW-WD-0042" />
      <Text style={styles.lbl}>Temperature (°C)</Text>
      <TextInput style={styles.input} value={tempC} onChangeText={setTempC} keyboardType="numeric" />
      <Text style={styles.lbl}>Duration (s)</Text>
      <TextInput style={styles.input} value={duration} onChangeText={setDuration} keyboardType="numeric" />
      <TouchableOpacity style={styles.btn} onPress={log}><Text style={styles.btnText}>Log Flush</Text></TouchableOpacity>
      <Text style={styles.section}>Recent (offline queue)</Text>
      {history.map((h, i) => (
        <Text key={i} style={styles.row}>{h.id} — {h.t} °C / {h.d} s — {new Date(h.ts).toLocaleTimeString()}</Text>
      ))}
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
  section: { fontSize: 14, fontWeight: '600', marginTop: 24, marginBottom: 6 },
  row: { fontSize: 12, color: '#444', paddingVertical: 4, borderBottomWidth: 1, borderBottomColor: '#eee' },
});
