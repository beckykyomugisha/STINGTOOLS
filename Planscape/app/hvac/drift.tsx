// Phase 188 (Tier 3) — single-snapshot detail viewer.
// Renders the PayloadJson (verbatim from desktop panel grids) as a
// scrollable list. Keeps the schema-evolution surface tiny.

import { useEffect, useState } from 'react';
import { View, Text, ScrollView, StyleSheet, ActivityIndicator } from 'react-native';
import { useLocalSearchParams } from 'expo-router';
import { useProjectStore } from '@/stores/projectStore';
import { getHvacSnapshot, HvacSnapshotDetail } from '@/api/endpoints';

export default function HvacSnapshotDetailView() {
  const { snapshotId, kind } = useLocalSearchParams<{ snapshotId?: string; kind?: string }>();
  const activeProject = useProjectStore((s) => s.active);
  const [detail,  setDetail]  = useState<HvacSnapshotDetail | null>(null);
  const [rows,    setRows]    = useState<Record<string, unknown>[]>([]);
  const [loading, setLoading] = useState(true);
  const [error,   setError]   = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      if (!activeProject?.id || !snapshotId) { setLoading(false); return; }
      try {
        const d = await getHvacSnapshot(activeProject.id, snapshotId);
        setDetail(d);
        try {
          const parsed = JSON.parse(d.payloadJson || '[]');
          setRows(Array.isArray(parsed) ? parsed : []);
        }
        catch (parseErr: any) {
          setError(`Payload parse: ${parseErr?.message ?? String(parseErr)}`);
        }
      }
      catch (e: any) { setError(e?.message ?? String(e)); }
      finally { setLoading(false); }
    })();
  }, [activeProject?.id, snapshotId]);

  if (loading) return (
    <View style={styles.center}>
      <ActivityIndicator size="large" />
    </View>
  );

  if (!detail) return (
    <View style={styles.center}>
      <Text style={styles.errorText}>Snapshot not found.</Text>
      {error && <Text style={styles.errorText}>{error}</Text>}
    </View>
  );

  return (
    <ScrollView style={styles.container}>
      <Text style={styles.title}>
        {(detail.kind || kind || 'HVAC').toUpperCase()} snapshot
      </Text>
      <Text style={styles.ts}>{new Date(detail.capturedAt).toLocaleString()}</Text>
      <View style={styles.kpiRow}>
        <Kpi label="Inspected" value={String(detail.inspected)} />
        <Kpi label="Pass"      value={String(detail.pass)}      color="#2E7D32" />
        <Kpi label="Warn"      value={String(detail.warn)}      color="#EF6C00" />
        <Kpi label="Fail"      value={String(detail.fail)}      color="#D32F2F" />
      </View>
      {detail.totalKw > 0 && (
        <Text style={styles.totals}>
          Total kW: {detail.totalKw.toFixed(1)} · Worst: {detail.worstValue.toFixed(1)}
        </Text>
      )}

      <Text style={styles.section}>Rows ({rows.length})</Text>
      {rows.length === 0
        ? <Text style={styles.muted}>(empty payload)</Text>
        : rows.slice(0, 200).map((row, i) => (
            <View key={i} style={styles.rowCard}>
              {Object.entries(row).map(([k, v]) => (
                <View key={k} style={styles.kvRow}>
                  <Text style={styles.kvKey}>{k}</Text>
                  <Text style={styles.kvVal}>{String(v)}</Text>
                </View>
              ))}
            </View>
          ))}
      {rows.length > 200 && (
        <Text style={styles.muted}>(+{rows.length - 200} more rows omitted)</Text>
      )}
    </ScrollView>
  );
}

function Kpi({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <View style={styles.kpiBox}>
      <Text style={[styles.kpiValue, color ? { color } : undefined]}>{value}</Text>
      <Text style={styles.kpiLabel}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 12, backgroundColor: '#FAFAFA' },
  center:    { flex: 1, justifyContent: 'center', alignItems: 'center' },
  title:     { fontSize: 16, fontWeight: '700', color: '#263238' },
  ts:        { fontSize: 11, color: '#90A4AE', marginBottom: 12 },
  kpiRow:    { flexDirection: 'row', justifyContent: 'space-between',
               backgroundColor: '#FFF', borderRadius: 6, padding: 8, marginBottom: 8 },
  kpiBox:    { alignItems: 'center', flex: 1 },
  kpiValue:  { fontSize: 18, fontWeight: '700', color: '#263238' },
  kpiLabel:  { fontSize: 10, color: '#607D8B' },
  totals:    { fontSize: 12, color: '#455A64', marginBottom: 12 },
  section:   { fontSize: 12, fontWeight: '600', color: '#37474F',
               textTransform: 'uppercase', marginTop: 8, marginBottom: 4 },
  muted:     { fontSize: 11, color: '#90A4AE', fontStyle: 'italic' },
  rowCard:   { backgroundColor: '#FFF', borderRadius: 4,
               padding: 8, marginBottom: 4 },
  kvRow:     { flexDirection: 'row', paddingVertical: 1 },
  kvKey:     { width: 110, fontSize: 10, color: '#607D8B', fontWeight: '600' },
  kvVal:     { flex: 1, fontSize: 10, color: '#263238', fontFamily: 'monospace' },
  errorText: { fontSize: 12, color: '#D32F2F', textAlign: 'center', padding: 16 },
});
