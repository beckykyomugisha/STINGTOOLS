// T3-17 — Information Deliverables list.
//
// Reverse-chronological list filtered by status. Tap a row to open the
// detail screen. FAB opens the create form. Status filter chips replicate
// the canonical ISO 19650 deliverable lifecycle.

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  RefreshControl,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  listDeliverables,
  type DeliverableSummary,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

type StatusFilter = 'All' | DeliverableSummary['status'];
const STATUS_OPTIONS: StatusFilter[] = [
  'All', 'PENDING', 'IN_PROGRESS', 'SUBMITTED', 'ACCEPTED', 'REJECTED', 'WAIVED',
];

export default function DeliverablesListScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);

  const [rows, setRows] = useState<DeliverableSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All');
  const [overdueOnly, setOverdueOnly] = useState(false);

  const args = useMemo(() => ({
    status: statusFilter === 'All' ? undefined : statusFilter,
    overdueOnly: overdueOnly || undefined,
    pageSize: 100,
  }), [statusFilter, overdueOnly]);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const res = await listDeliverables(projectId, args);
      setRows(res.rows);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load deliverables');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, args]);

  useEffect(() => { void load(); }, [load]);

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project on the dashboard first.</Text>
      </View>
    );
  }
  if (loading) {
    return <View style={styles.loading}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }

  return (
    <View style={styles.root}>
      {/* Status filter chips */}
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterBar}>
        {STATUS_OPTIONS.map((s) => (
          <TouchableOpacity
            key={s}
            style={[styles.chip, statusFilter === s && styles.chipActive]}
            onPress={() => setStatusFilter(s)}
          >
            <Text style={[styles.chipText, statusFilter === s && styles.chipTextActive]}>
              {prettyStatus(s)}
            </Text>
          </TouchableOpacity>
        ))}
        <TouchableOpacity
          style={[styles.chip, overdueOnly && styles.chipDanger]}
          onPress={() => setOverdueOnly((v) => !v)}
        >
          <Text style={[styles.chipText, overdueOnly && styles.chipTextActive]}>
            Overdue only
          </Text>
        </TouchableOpacity>
      </ScrollView>

      <ScrollView
        contentContainerStyle={styles.scroll}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}
      >
        {error ? <Text style={styles.error}>{error}</Text> : null}

        {rows.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>No deliverables match these filters</Text>
            <Text style={styles.emptyHint}>Tap + to create one.</Text>
          </View>
        ) : (
          rows.map((d) => (
            <TouchableOpacity
              key={d.id}
              style={styles.row}
              onPress={() => router.push({ pathname: '/deliverables/[id]', params: { id: d.id } })}
              accessibilityLabel={`Open deliverable ${d.code}`}
            >
              <View style={[styles.statusPill, { backgroundColor: statusColor(d.status) }]}>
                <Text style={styles.statusText}>{prettyStatus(d.status)}</Text>
              </View>
              <View style={styles.rowBody}>
                <Text style={styles.rowTitle} numberOfLines={1}>
                  {d.code} — {d.title}
                </Text>
                <Text style={styles.rowMeta} numberOfLines={1}>
                  {d.type}
                  {d.discipline ? ` · ${d.discipline}` : ''}
                  {d.suitabilityTarget ? ` · ${d.suitabilityTarget}` : ''}
                  {' · due '}{formatDate(d.dueDate)}
                  {d.isOverdue ? ' · OVERDUE' : ''}
                </Text>
              </View>
            </TouchableOpacity>
          ))
        )}
      </ScrollView>

      <TouchableOpacity
        style={styles.fab}
        onPress={() => router.push('/deliverables/new')}
        accessibilityLabel="Create new deliverable"
      >
        <Text style={styles.fabIcon}>+</Text>
      </TouchableOpacity>
    </View>
  );
}

function prettyStatus(s: string): string {
  return s.replace(/_/g, ' ');
}

function statusColor(status: DeliverableSummary['status']): string {
  switch (status) {
    case 'PENDING': return theme.colors.disabled;
    case 'IN_PROGRESS': return theme.colors.accent;
    case 'SUBMITTED': return theme.colors.priorityMedium;
    case 'ACCEPTED': return theme.colors.success;
    case 'REJECTED': return theme.colors.danger;
    case 'WAIVED': return theme.colors.textSecondary;
    default: return theme.colors.textSecondary;
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg,
    alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text, marginBottom: 4 },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    margin: theme.spacing.md,
  },
  filterBar: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    maxHeight: 56,
  },
  chip: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 16,
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
    marginRight: theme.spacing.xs,
  },
  chipActive: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  chipDanger: { backgroundColor: theme.colors.danger, borderColor: theme.colors.danger },
  chipText: { color: theme.colors.text, fontSize: theme.fontSize.sm, fontWeight: '600' },
  chipTextActive: { color: '#fff' },

  scroll: { padding: theme.spacing.md, paddingBottom: 96 },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  statusPill: {
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 10,
    marginRight: theme.spacing.sm,
  },
  statusText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },

  fab: {
    position: 'absolute',
    right: theme.spacing.md,
    bottom: theme.spacing.md,
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: theme.colors.accent,
    justifyContent: 'center',
    alignItems: 'center',
    elevation: 4,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.25,
    shadowRadius: 4,
  },
  fabIcon: { color: '#fff', fontSize: 28, fontWeight: '600' },
});
