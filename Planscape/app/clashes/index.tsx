/**
 * Clashes list screen — BCC Clash Detective equivalent.
 *
 * Shows project clashes with severity/status filters, summary chips,
 * and a "Run detection" CTA. Tapping a clash opens the detail screen
 * (with view-in-model + promote-to-issue actions).
 */
import { useState, useEffect, useCallback } from 'react';
import {
  View, Text, ScrollView, RefreshControl, TouchableOpacity, StyleSheet, ActivityIndicator, Alert,
} from 'react-native';
import { useRouter, Stack } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  listClashes,
  runClashDetection,
  type ClashRecord,
  type ClashSeverity,
  type ClashStatus,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function ClashesScreen() {
  const router = useRouter();
  const project = useProjectStore((s) => s.active);
  const [clashes, setClashes] = useState<ClashRecord[]>([]);
  const [summary, setSummary] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [running, setRunning] = useState(false);
  const [statusFilter, setStatusFilter] = useState<ClashStatus | 'ALL'>('NEW');
  const [severityFilter, setSeverityFilter] = useState<ClashSeverity | 'ALL'>('ALL');

  const load = useCallback(async () => {
    if (!project) { setLoading(false); return; }
    try {
      const res = await listClashes(project.id, {
        status: statusFilter === 'ALL' ? undefined : statusFilter,
        severity: severityFilter === 'ALL' ? undefined : severityFilter,
        pageSize: 100,
      });
      setClashes(res.items);
      setSummary(res.summary);
    } catch (err) {
      console.warn('listClashes failed', err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [project, statusFilter, severityFilter]);

  useEffect(() => { load(); }, [load]);

  async function runDetection() {
    if (!project) return;
    setRunning(true);
    try {
      const res = await runClashDetection(project.id);
      Alert.alert(
        'Clash detection complete',
        `Scanned ${res.scannedPairs} pairs, found ${res.clashesFound} clashes (${res.clashesNew} new, ${res.criticalClashes} critical).`,
      );
      await load();
    } catch (err) {
      Alert.alert('Detection failed', String(err));
    } finally {
      setRunning(false);
    }
  }

  if (!project) {
    return (
      <View style={s.center}>
        <Text style={s.emptyTitle}>No project selected</Text>
        <Text style={s.emptySub}>Pick a project to view clashes.</Text>
      </View>
    );
  }

  const sevColor = (sev: ClashSeverity) => sev === 'CRITICAL' ? theme.colors.danger
                                          : sev === 'MAJOR' ? '#FF9800'
                                          : sev === 'MINOR' ? '#FFC107' : theme.colors.textSecondary;

  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <Stack.Screen options={{ headerShown: true, title: 'Clashes', headerStyle: { backgroundColor: theme.colors.primary }, headerTintColor: '#fff' }} />

      <View style={s.header}>
        <View style={{ flexDirection: 'row', gap: 8 }}>
          {(['ALL','NEW','ACKNOWLEDGED','RESOLVED'] as const).map(st => (
            <TouchableOpacity key={st} style={[s.filterChip, statusFilter === st && s.filterChipActive]}
              onPress={() => setStatusFilter(st)}>
              <Text style={[s.filterChipText, statusFilter === st && s.filterChipTextActive]}>{st}</Text>
            </TouchableOpacity>
          ))}
        </View>
        <View style={{ flexDirection: 'row', gap: 8, marginTop: 8 }}>
          {(['ALL','CRITICAL','MAJOR','MINOR'] as const).map(sv => (
            <TouchableOpacity key={sv} style={[s.sevChip, severityFilter === sv && { backgroundColor: sv === 'ALL' ? theme.colors.primary : sevColor(sv as ClashSeverity) }]}
              onPress={() => setSeverityFilter(sv)}>
              <Text style={[s.sevChipText, severityFilter === sv && { color: '#fff' }]}>{sv}</Text>
            </TouchableOpacity>
          ))}
        </View>
      </View>

      {summary && (
        <View style={s.summaryRow}>
          <Text style={s.summaryText}>{summary.total} total clashes</Text>
          <TouchableOpacity style={[s.runBtn, running && s.runBtnDisabled]} onPress={runDetection} disabled={running}>
            {running ? <ActivityIndicator color="#fff" /> : <Text style={s.runBtnText}>↻ Run Detection</Text>}
          </TouchableOpacity>
        </View>
      )}

      {loading ? (
        <View style={s.center}><ActivityIndicator size="large" color={theme.colors.accent} /></View>
      ) : (
        <ScrollView refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />}>
          {clashes.length === 0 ? (
            <View style={s.center}><Text style={s.emptyTitle}>No clashes</Text>
              <Text style={s.emptySub}>{statusFilter === 'NEW' ? 'No new clashes. Try the Run Detection button.' : 'Try a different filter.'}</Text>
            </View>
          ) : clashes.map(c => (
            <TouchableOpacity key={c.id} style={[s.row, { borderLeftColor: sevColor(c.severity) }]}
              onPress={() => router.push(`/clashes/${c.id}` as any)}>
              <View style={{ flex: 1 }}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                  <Text style={[s.sevBadge, { backgroundColor: sevColor(c.severity) }]}>{c.severity}</Text>
                  <Text style={s.statusBadge}>{c.status}</Text>
                  {c.levelCode && <Text style={s.levelBadge}>{c.levelCode}</Text>}
                </View>
                <Text style={s.title}>{c.disciplineA || '?'} ↔ {c.disciplineB || '?'}</Text>
                <Text style={s.meta}>
                  Depth {c.distanceMm.toFixed(1)} mm · Volume {Math.round(c.overlapVolumeMm3).toLocaleString()} mm³
                </Text>
                {c.assignedTo && <Text style={s.meta}>Assigned: {c.assignedTo}</Text>}
                {c.issueId && <Text style={[s.meta, { color: theme.colors.accent }]}>↗ Linked to issue</Text>}
              </View>
              <Text style={s.arrow}>›</Text>
            </TouchableOpacity>
          ))}
        </ScrollView>
      )}
    </View>
  );
}

const s = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 20 },
  emptyTitle: { fontSize: 18, fontWeight: '700', color: theme.colors.text, marginBottom: 4 },
  emptySub: { fontSize: 14, color: theme.colors.textSecondary, textAlign: 'center' },
  header: { padding: 12, backgroundColor: theme.colors.surface, borderBottomWidth: 1, borderBottomColor: theme.colors.border },
  filterChip: { paddingHorizontal: 10, paddingVertical: 6, borderRadius: 12, borderWidth: 1, borderColor: theme.colors.border },
  filterChipActive: { backgroundColor: theme.colors.primary, borderColor: theme.colors.primary },
  filterChipText: { fontSize: 11, fontWeight: '600', color: theme.colors.text },
  filterChipTextActive: { color: '#fff' },
  sevChip: { paddingHorizontal: 10, paddingVertical: 6, borderRadius: 12, borderWidth: 1, borderColor: theme.colors.border },
  sevChipText: { fontSize: 11, fontWeight: '600', color: theme.colors.text },
  summaryRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', padding: 12, backgroundColor: theme.colors.background },
  summaryText: { fontSize: 14, fontWeight: '600', color: theme.colors.text },
  runBtn: { backgroundColor: theme.colors.accent, paddingHorizontal: 16, paddingVertical: 8, borderRadius: 6 },
  runBtnDisabled: { opacity: 0.5 },
  runBtnText: { color: '#fff', fontWeight: '700', fontSize: 13 },
  row: { flexDirection: 'row', alignItems: 'center', backgroundColor: theme.colors.surface, marginHorizontal: 12, marginTop: 8, padding: 12, borderRadius: 8, borderLeftWidth: 4 },
  sevBadge: { color: '#fff', fontSize: 10, fontWeight: '700', paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4 },
  statusBadge: { color: theme.colors.textSecondary, fontSize: 10, fontWeight: '600', paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4, backgroundColor: theme.colors.background },
  levelBadge: { color: theme.colors.text, fontSize: 10, fontWeight: '600', paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4, backgroundColor: theme.colors.background },
  title: { fontSize: 15, fontWeight: '700', color: theme.colors.text, marginTop: 4 },
  meta: { fontSize: 12, color: theme.colors.textSecondary, marginTop: 2 },
  arrow: { fontSize: 24, color: theme.colors.textSecondary, paddingHorizontal: 8 },
});
