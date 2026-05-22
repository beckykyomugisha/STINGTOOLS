// Phase 179 — Photo checklists list (mobile).

import { useCallback, useEffect, useState } from 'react';
import {
  View, Text, StyleSheet, FlatList, RefreshControl, TouchableOpacity, ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import { listPhotoChecklists, type PhotoChecklist } from '@/api/endpoints';

export default function ChecklistsScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);

  const [rows, setRows] = useState<PhotoChecklist[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const list = await listPhotoChecklists(projectId, 'Active');
      setRows(list);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { void load(); }, [load]);

  if (!projectId) {
    return <View style={styles.empty}><Text>Select a project first.</Text></View>;
  }
  if (loading) {
    return <View style={styles.empty}><ActivityIndicator color={theme.colors.accent} size="large" /></View>;
  }

  return (
    <FlatList
      style={styles.root}
      data={rows}
      keyExtractor={(c) => c.id}
      refreshControl={
        <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />
      }
      ListEmptyComponent={
        <View style={styles.empty}>
          <Text style={styles.emptyText}>{error ?? 'No active checklists for this project.'}</Text>
        </View>
      }
      renderItem={({ item }) => {
        const pct = item.total === 0 ? 0 : Math.round(100 * item.done / item.total);
        const ragColor = pct >= 90 ? '#2E7D32' : pct >= 50 ? '#E65C00' : '#C62828';
        return (
          <TouchableOpacity
            style={styles.row}
            onPress={() => router.push({ pathname: '/site-photos/checklist-detail', params: { checklistId: item.id } })}>
            <View style={styles.rowMain}>
              <Text style={styles.rowTitle}>{item.name}</Text>
              <Text style={styles.rowMeta}>
                {item.kind ?? 'Custom'} · {item.levelCode ?? '—'}/{item.zoneCode ?? '—'}
                {item.dueAt ? ` · due ${new Date(item.dueAt).toLocaleDateString()}` : ''}
              </Text>
            </View>
            <View style={[styles.ragPill, { backgroundColor: ragColor }]}>
              <Text style={styles.ragText}>{item.done}/{item.total}</Text>
            </View>
          </TouchableOpacity>
        );
      }}
    />
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { padding: theme.spacing.xl, alignItems: 'center' },
  emptyText: { color: theme.colors.textSecondary, fontStyle: 'italic' },
  row: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: theme.colors.surface,
    padding: theme.spacing.md,
    borderBottomWidth: 1, borderBottomColor: theme.colors.border,
  },
  rowMain: { flex: 1 },
  rowTitle: { fontSize: 15, fontWeight: '600', color: theme.colors.text },
  rowMeta: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 2 },
  ragPill: { paddingVertical: 4, paddingHorizontal: 10, borderRadius: 12 },
  ragText: { color: '#fff', fontSize: 12, fontWeight: '700' },
});
