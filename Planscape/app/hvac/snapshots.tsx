// Phase 188 (Tier 3) — HVAC snapshot list. Drill-in from the dashboard cards.
//
// Lists the most recent N snapshots of a given kind, with timestamp + RAG +
// inspected/pass/warn/fail counters. Tapping a row opens the JSON payload
// detail view (drift) where applicable.

import { useEffect, useState, useCallback } from 'react';
import {
  View, Text, FlatList, RefreshControl, StyleSheet,
  TouchableOpacity, ActivityIndicator,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useProjectStore } from '@/stores/projectStore';
import { listHvacSnapshots, HvacSnapshotSummary } from '@/api/endpoints';

function ragColor(r: string): string {
  if (r === 'R') return '#D32F2F';
  if (r === 'A') return '#EF6C00';
  if (r === 'G') return '#2E7D32';
  return '#9E9E9E';
}

export default function HvacSnapshotsList() {
  const { kind, title } = useLocalSearchParams<{ kind?: string; title?: string }>();
  const router = useRouter();
  const activeProject = useProjectStore((s) => s.active);
  const [rows,      setRows]      = useState<HvacSnapshotSummary[]>([]);
  const [loading,   setLoading]   = useState(true);
  const [refreshing,setRefreshing]= useState(false);
  const [error,     setError]     = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!activeProject?.id) { setLoading(false); return; }
    setError(null);
    try {
      const r = await listHvacSnapshots(activeProject.id, kind, 100);
      setRows(r);
    }
    catch (e: any) {
      setRows([]);
      setError(e?.message ?? String(e));
    }
    finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [activeProject?.id, kind]);

  useEffect(() => { load(); }, [load]);

  if (loading) return (
    <View style={styles.center}>
      <ActivityIndicator size="large" />
      <Text style={styles.muted}>Loading {kind} snapshots…</Text>
    </View>
  );

  return (
    <View style={styles.container}>
      <Text style={styles.header}>{title || kind || 'HVAC'} snapshots</Text>
      {rows.length === 0 ? (
        <View style={styles.empty}>
          <Text style={styles.emptyText}>No snapshots of kind &quot;{kind}&quot;.</Text>
          {error && <Text style={styles.errorText}>{error}</Text>}
        </View>
      ) : (
        <FlatList
          data={rows}
          keyExtractor={(item) => item.id}
          refreshControl={<RefreshControl refreshing={refreshing}
            onRefresh={() => { setRefreshing(true); load(); }} />}
          renderItem={({ item }) => (
            <TouchableOpacity
              style={[styles.row, { borderLeftColor: ragColor(item.rag) }]}
              onPress={() => router.push({
                pathname: '/hvac/drift',
                params: { snapshotId: item.id, kind: item.kind },
              })}
            >
              <View style={styles.rowLeft}>
                <Text style={styles.rowKind}>{item.kind}</Text>
                <Text style={styles.rowTs}>
                  {new Date(item.capturedAt).toLocaleString()}
                </Text>
              </View>
              <View style={styles.rowMid}>
                <Text style={styles.rowCounters}>
                  <Text style={[styles.kpi, { color: '#2E7D32' }]}>{item.pass}</Text>
                  <Text style={styles.kpiSep}> · </Text>
                  <Text style={[styles.kpi, { color: '#EF6C00' }]}>{item.warn}</Text>
                  <Text style={styles.kpiSep}> · </Text>
                  <Text style={[styles.kpi, { color: '#D32F2F' }]}>{item.fail}</Text>
                </Text>
                <Text style={styles.rowInspected}>of {item.inspected}</Text>
              </View>
              <View style={[styles.ragBadge, { backgroundColor: ragColor(item.rag) }]}>
                <Text style={styles.ragText}>{item.rag}</Text>
              </View>
            </TouchableOpacity>
          )}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container:   { flex: 1, backgroundColor: '#FAFAFA', padding: 12 },
  center:      { flex: 1, justifyContent: 'center', alignItems: 'center', padding: 32 },
  muted:       { marginTop: 12, color: '#757575', fontSize: 12 },
  header:      { fontSize: 14, fontWeight: '600', color: '#37474F', marginBottom: 8 },
  empty:       { padding: 24, alignItems: 'center' },
  emptyText:   { fontSize: 13, color: '#90A4AE' },
  errorText:   { marginTop: 8, fontSize: 11, color: '#D32F2F' },
  row:         { flexDirection: 'row', alignItems: 'center',
                 backgroundColor: '#FFF', borderRadius: 6, padding: 12,
                 marginBottom: 4, borderLeftWidth: 3 },
  rowLeft:     { flex: 2 },
  rowKind:     { fontSize: 12, fontWeight: '600', color: '#263238',
                 textTransform: 'capitalize' },
  rowTs:       { fontSize: 10, color: '#90A4AE', marginTop: 2 },
  rowMid:      { flex: 1, alignItems: 'flex-end' },
  rowCounters: { fontSize: 11, fontFamily: 'monospace' },
  kpi:         { fontWeight: '700' },
  kpiSep:      { color: '#CFD8DC' },
  rowInspected:{ fontSize: 10, color: '#90A4AE', marginTop: 2 },
  ragBadge:    { width: 24, height: 24, borderRadius: 12,
                 justifyContent: 'center', alignItems: 'center', marginLeft: 8 },
  ragText:     { color: '#FFF', fontWeight: 'bold', fontSize: 11 },
});
