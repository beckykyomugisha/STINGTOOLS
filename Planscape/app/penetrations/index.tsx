// Phase 178f — Penetrations dashboard.
//
// Lists every penetration sign-off captured against the active
// project, grouped by status. Tap a row → /penetrations/signoff with
// the row's controlNumber pre-filled.
//
// QR / Manual entry buttons jump straight to /penetrations/signoff
// and let the installer scan a tag (encoding controlNumber + pfvUuid)
// or type the FRP-NNNN themselves.

import { useEffect, useState, useCallback } from 'react';
import {
  View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, ActivityIndicator,
} from 'react-native';
import { router } from 'expo-router';
import { useProjectStore } from '@/stores/projectStore';
import { listPenetrationSignoffs, getPenetrationDashboard, PenetrationSignoff } from '@/api/endpoints';

export default function PenetrationsIndexScreen() {
  const activeProject = useProjectStore((s) => s.active);
  const [rows, setRows] = useState<PenetrationSignoff[]>([]);
  const [byStatus, setByStatus] = useState<{ status: string; count: number }[]>([]);
  const [byHost, setByHost] = useState<{ hostType: string; count: number }[]>([]);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    if (!activeProject?.id) return;
    setLoading(true);
    try {
      const [list, dash] = await Promise.all([
        listPenetrationSignoffs(activeProject.id),
        getPenetrationDashboard(activeProject.id),
      ]);
      setRows(list ?? []);
      setByStatus(dash?.byStatus ?? []);
      setByHost(dash?.byHost ?? []);
    } catch {
      // Best-effort — leave previous state on offline / fetch failure.
    } finally { setLoading(false); }
  }, [activeProject?.id]);

  useEffect(() => { load(); }, [load]);

  const onRefresh = useCallback(async () => { setRefreshing(true); await load(); setRefreshing(false); }, [load]);

  if (!activeProject) {
    return (
      <View style={styles.empty}><Text style={styles.emptyText}>Select a project first.</Text></View>
    );
  }

  return (
    <ScrollView
      style={styles.scroll}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
    >
      <View style={styles.actions}>
        <TouchableOpacity
          style={[styles.action, styles.primary]}
          onPress={() => router.push('/penetrations/signoff?scan=1' as any)}
        >
          <Text style={styles.actionText}>📷  Scan QR</Text>
        </TouchableOpacity>
        <TouchableOpacity
          style={styles.action}
          onPress={() => router.push('/penetrations/signoff' as any)}
        >
          <Text style={styles.actionText}>⌨️  Manual entry</Text>
        </TouchableOpacity>
      </View>

      <Text style={styles.sectionTitle}>Status</Text>
      <View style={styles.kpiRow}>
        {byStatus.length === 0 ? (
          <Text style={styles.faded}>No sign-offs yet.</Text>
        ) : byStatus.map((s) => (
          <View key={s.status} style={styles.kpi}>
            <Text style={styles.kpiNum}>{s.count}</Text>
            <Text style={styles.kpiLabel}>{s.status}</Text>
          </View>
        ))}
      </View>

      <Text style={styles.sectionTitle}>By host</Text>
      <View style={styles.kpiRow}>
        {byHost.map((h) => (
          <View key={h.hostType} style={styles.kpi}>
            <Text style={styles.kpiNum}>{h.count}</Text>
            <Text style={styles.kpiLabel}>{h.hostType || '—'}</Text>
          </View>
        ))}
      </View>

      <Text style={styles.sectionTitle}>Recent</Text>
      {loading ? (
        <ActivityIndicator />
      ) : rows.length === 0 ? (
        <Text style={styles.faded}>Nothing logged yet for this project.</Text>
      ) : rows.slice(0, 100).map((r) => (
        <TouchableOpacity
          key={r.penetrationControlNumber}
          style={styles.row}
          onPress={() => router.push(`/penetrations/signoff?cn=${encodeURIComponent(r.penetrationControlNumber)}` as any)}
        >
          <Text style={styles.rowTitle}>{r.penetrationControlNumber}</Text>
          <Text style={styles.rowMeta}>
            {[r.hostType, r.fireRating, r.productKind, r.status].filter(Boolean).join(' · ')}
          </Text>
        </TouchableOpacity>
      ))}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1, padding: 16 },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  emptyText: { fontSize: 16, opacity: 0.7 },
  actions: { flexDirection: 'row', gap: 12, marginBottom: 16 },
  action: { flex: 1, padding: 14, borderRadius: 10, backgroundColor: '#eee', alignItems: 'center' },
  primary: { backgroundColor: '#2D5BFF' },
  actionText: { fontSize: 15, fontWeight: '600', color: '#fff' },
  sectionTitle: { fontSize: 14, fontWeight: '700', marginTop: 14, marginBottom: 6, opacity: 0.7 },
  kpiRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  kpi: { padding: 10, borderRadius: 8, backgroundColor: '#f4f4f4', minWidth: 80, alignItems: 'center' },
  kpiNum: { fontSize: 22, fontWeight: '800' },
  kpiLabel: { fontSize: 11, opacity: 0.6, textTransform: 'uppercase' },
  row: { paddingVertical: 12, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: '#ccc' },
  rowTitle: { fontSize: 15, fontWeight: '600' },
  rowMeta: { fontSize: 12, opacity: 0.6, marginTop: 2 },
  faded: { opacity: 0.6, fontStyle: 'italic' },
});
