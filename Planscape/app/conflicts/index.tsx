// Phase 143 — Sync Conflicts triage screen.
//
// Lists tag-sync conflicts written by TagSyncController so the BIM
// Manager can see what was rejected when the plugin pushed stale data.
// Filters by resolution status, supports per-row "accept server" /
// "mark merged" and a multi-select bulk-resolve flow.

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  RefreshControl,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { theme } from '@/utils/theme';
import {
  listSyncConflicts,
  resolveSyncConflict,
  bulkResolveSyncConflicts,
  type SyncConflictSummary,
  type SyncConflictsListResponse,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

type ResolutionFilter = 'PENDING' | 'SERVER_WINS' | 'CLIENT_WINS' | 'MERGED' | 'ALL';

export default function ConflictsScreen() {
  const projectId = useProjectStore((s) => s.active?.id);
  const [resp, setResp] = useState<SyncConflictsListResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<ResolutionFilter>('PENDING');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [working, setWorking] = useState(false);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const r = await listSyncConflicts(projectId, {
        resolution: filter === 'ALL' ? undefined : filter,
        pageSize: 100,
      });
      setResp(r);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load conflicts');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, filter]);

  useEffect(() => { load(); }, [load]);

  function toggle(id: string) {
    setSelected((s) => {
      const next = new Set(s);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  async function resolveOne(c: SyncConflictSummary, resolution: 'ACCEPT_SERVER' | 'MERGED') {
    if (!projectId) return;
    setWorking(true);
    try {
      await resolveSyncConflict(projectId, c.id, resolution);
      await load();
    } catch (err: unknown) {
      Alert.alert('Resolve failed', err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }

  async function bulkResolve(resolution: 'ACCEPT_SERVER' | 'MERGED') {
    if (!projectId || selected.size === 0) return;
    Alert.alert(
      `Resolve ${selected.size} conflict${selected.size === 1 ? '' : 's'}?`,
      resolution === 'ACCEPT_SERVER'
        ? 'Server copy will be kept; the rejected client edits stay rejected.'
        : 'Conflicts will be marked merged. No element data changes.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Resolve',
          style: 'destructive',
          onPress: async () => {
            setWorking(true);
            try {
              await bulkResolveSyncConflicts(projectId, Array.from(selected), resolution);
              setSelected(new Set());
              await load();
            } catch (err: unknown) {
              Alert.alert('Bulk resolve failed', err instanceof Error ? err.message : String(err));
            } finally { setWorking(false); }
          },
        },
      ],
    );
  }

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
      {/* Summary banner */}
      <View style={styles.summary}>
        <SummaryTile label="Pending" value={resp?.summary.pending ?? 0} color={theme.colors.danger} />
        <SummaryTile label="Recent (7d)" value={resp?.summary.recentServerWins ?? 0} color={theme.colors.warning} />
        <SummaryTile label="Showing" value={resp?.total ?? 0} color={theme.colors.accent} />
      </View>

      {/* Filter chips */}
      <View style={styles.filters}>
        {(['PENDING', 'SERVER_WINS', 'CLIENT_WINS', 'MERGED', 'ALL'] as ResolutionFilter[]).map((f) => (
          <TouchableOpacity
            key={f}
            style={[styles.filterChip, filter === f && styles.filterChipActive]}
            onPress={() => { setFilter(f); setSelected(new Set()); }}
          >
            <Text style={[styles.filterChipText, filter === f && styles.filterChipTextActive]}>
              {f === 'ALL' ? 'All' : f}
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      {/* Bulk action bar — only when something is selected */}
      {selected.size > 0 && (
        <View style={styles.bulkBar}>
          <Text style={styles.bulkText}>{selected.size} selected</Text>
          <TouchableOpacity style={styles.bulkBtn} disabled={working}
            onPress={() => bulkResolve('ACCEPT_SERVER')}
            accessibilityLabel="Bulk accept server">
            <Text style={styles.bulkBtnText}>Accept Server</Text>
          </TouchableOpacity>
          <TouchableOpacity style={[styles.bulkBtn, styles.bulkBtnGhost]} disabled={working}
            onPress={() => bulkResolve('MERGED')}
            accessibilityLabel="Bulk mark merged">
            <Text style={styles.bulkBtnGhostText}>Mark Merged</Text>
          </TouchableOpacity>
        </View>
      )}

      <ScrollView
        contentContainerStyle={styles.scroll}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />}
      >
        {error ? <Text style={styles.error}>{error}</Text> : null}

        {(resp?.rows.length ?? 0) === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>
              {filter === 'PENDING' ? 'No pending conflicts.' : 'No matching conflicts.'}
            </Text>
            <Text style={styles.emptyHint}>
              Pull to refresh.
            </Text>
          </View>
        ) : (
          resp!.rows.map((c) => {
            const sel = selected.has(c.id);
            return (
              <View key={c.id} style={[styles.row, sel && styles.rowSelected]}>
                <TouchableOpacity
                  style={styles.checkBox}
                  onPress={() => toggle(c.id)}
                  accessibilityLabel={sel ? 'Deselect conflict' : 'Select conflict'}
                >
                  <Text style={styles.checkBoxText}>{sel ? '☑' : '☐'}</Text>
                </TouchableOpacity>
                <View style={styles.rowBody}>
                  <Text style={styles.rowTitle} numberOfLines={1}>
                    {c.conflictType} · element {c.elementId}
                  </Text>
                  <Text style={styles.rowMeta} numberOfLines={2}>
                    {c.clientUserName ?? 'unknown'} pushed {fmt(c.clientTimestamp)} → server held {fmt(c.serverTimestamp)}
                    {' · '}detected {fmt(c.detectedAt)}
                  </Text>
                  <View style={styles.actions}>
                    <View style={[styles.statePill, { backgroundColor: stateColor(c.resolution) }]}>
                      <Text style={styles.statePillText}>{c.resolution}</Text>
                    </View>
                    {c.resolution === 'PENDING' && (
                      <>
                        <TouchableOpacity
                          style={[styles.action, working && { opacity: 0.5 }]}
                          disabled={working}
                          onPress={() => resolveOne(c, 'ACCEPT_SERVER')}
                          accessibilityLabel="Accept server"
                        >
                          <Text style={styles.actionText}>Server</Text>
                        </TouchableOpacity>
                        <TouchableOpacity
                          style={[styles.action, styles.actionGhost, working && { opacity: 0.5 }]}
                          disabled={working}
                          onPress={() => resolveOne(c, 'MERGED')}
                          accessibilityLabel="Mark merged"
                        >
                          <Text style={styles.actionGhostText}>Merged</Text>
                        </TouchableOpacity>
                      </>
                    )}
                  </View>
                </View>
              </View>
            );
          })
        )}
      </ScrollView>
    </View>
  );
}

function SummaryTile({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <View style={[styles.tile, { borderTopColor: color }]}>
      <Text style={[styles.tileValue, { color }]}>{value}</Text>
      <Text style={styles.tileLabel}>{label}</Text>
    </View>
  );
}

function stateColor(s: SyncConflictSummary['resolution']): string {
  switch (s) {
    case 'PENDING': return theme.colors.danger;
    case 'SERVER_WINS': return theme.colors.warning;
    case 'CLIENT_WINS': return theme.colors.accent;
    case 'MERGED': return theme.colors.success;
    default: return theme.colors.disabled;
  }
}

function fmt(iso?: string | null): string {
  if (!iso) return '—';
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg, alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4 },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    margin: theme.spacing.md,
  },
  scroll: { padding: theme.spacing.md, paddingBottom: 80 },
  summary: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    paddingHorizontal: theme.spacing.md,
    paddingTop: theme.spacing.md,
  },
  tile: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm,
    alignItems: 'center',
    borderTopWidth: 4,
  },
  tileValue: { fontSize: theme.fontSize.lg, fontWeight: '700' },
  tileLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  filters: {
    flexDirection: 'row',
    gap: theme.spacing.xs,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    flexWrap: 'wrap',
  },
  filterChip: {
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 4,
    borderRadius: 16,
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  filterChipActive: {
    backgroundColor: theme.colors.accent,
    borderColor: theme.colors.accent,
  },
  filterChipText: { fontSize: theme.fontSize.xs, color: theme.colors.text },
  filterChipTextActive: { color: '#fff', fontWeight: '700' },
  bulkBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
    backgroundColor: theme.colors.surface,
    borderTopWidth: 1,
    borderBottomWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
  },
  bulkText: { flex: 1, fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  bulkBtn: {
    backgroundColor: theme.colors.accent,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 6,
    borderRadius: theme.borderRadius.sm,
  },
  bulkBtnText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.xs },
  bulkBtnGhost: {
    backgroundColor: 'transparent',
    borderWidth: 1.5,
    borderColor: theme.colors.accent,
  },
  bulkBtnGhostText: { color: theme.colors.accent, fontWeight: '600', fontSize: theme.fontSize.xs },
  row: {
    flexDirection: 'row',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  rowSelected: { borderWidth: 2, borderColor: theme.colors.accent },
  checkBox: { paddingTop: 2, paddingRight: theme.spacing.sm },
  checkBoxText: { fontSize: 18, color: theme.colors.text },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 4 },
  actions: { flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs, marginTop: theme.spacing.sm, flexWrap: 'wrap' },
  statePill: {
    paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4,
  },
  statePillText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  action: {
    backgroundColor: theme.colors.accent,
    paddingHorizontal: theme.spacing.sm, paddingVertical: 4,
    borderRadius: theme.borderRadius.sm,
  },
  actionText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.xs },
  actionGhost: {
    backgroundColor: 'transparent',
    borderWidth: 1.5, borderColor: theme.colors.accent,
  },
  actionGhostText: { color: theme.colors.accent, fontWeight: '600', fontSize: theme.fontSize.xs },
});
