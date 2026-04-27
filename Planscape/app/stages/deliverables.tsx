// Phase 144 — MIDP / IE deliverables for one stage gate.
//
// Lists every InformationDeliverable rolled up against the stage. Filter
// chips for status + overdue. Tap a row to drive the next state
// transition through a confirm dialog (the server enforces the
// allowed-from→to map).

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
import { useLocalSearchParams } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  listDeliverables,
  transitionDeliverable,
  getDeliverableStateMachine,
  type DeliverableSummary,
  type DeliverableStateMachine,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

type StatusFilter = DeliverableSummary['status'] | 'ALL';

export default function DeliverablesScreen() {
  const { gateId, gateCode } = useLocalSearchParams<{ gateId?: string; gateCode?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);
  const [rows, setRows] = useState<DeliverableSummary[]>([]);
  const [machine, setMachine] = useState<DeliverableStateMachine | null>(null);
  const [filter, setFilter] = useState<StatusFilter>('ALL');
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [working, setWorking] = useState(false);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      // Phase 145 — fetch the resolved state machine in parallel with the
      // deliverables list so contextual transition buttons match whatever
      // the project actually allows. Failure is non-fatal: we fall back to
      // null and the row hides per-row buttons.
      const [list, sm] = await Promise.allSettled([
        listDeliverables(projectId, {
          stageGateId: gateId,
          status: filter === 'ALL' ? undefined : filter,
          overdueOnly: overdueOnly || undefined,
          pageSize: 200,
        }),
        getDeliverableStateMachine(projectId),
      ]);
      if (list.status === 'fulfilled') setRows(list.value.rows);
      setMachine(sm.status === 'fulfilled' ? sm.value : null);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, gateId, filter, overdueOnly]);

  useEffect(() => { load(); }, [load]);

  // Phase 145 — derive the next-status choices from the resolved state
  // machine rather than a hard-coded switch so custom project flows work
  // out of the box. Falls back to an empty list when the machine endpoint
  // failed (the row then has no per-row transition buttons).
  function nextStatusChoices(d: DeliverableSummary): Array<{ label: string; status: string; danger?: boolean }> {
    const targets = machine?.transitions
      .filter((t) => t.from.toUpperCase() === d.status.toUpperCase())
      .map((t) => t.to.toUpperCase()) ?? [];
    return targets.map((s) => ({
      label: friendlyTransitionLabel(d.status, s),
      status: s,
      danger: s === 'REJECTED' || s === 'FAILED',
    }));
  }

  async function transition(d: DeliverableSummary, target: DeliverableSummary['status']) {
    if (!projectId) return;
    let reason: string | undefined;
    if (target === 'REJECTED') {
      reason = await new Promise<string | undefined>((resolve) => {
        Alert.prompt
          ? Alert.prompt('Rejection reason', undefined, (txt) => resolve(txt || undefined), 'plain-text')
          : resolve('Rejected');
      });
      if (!reason) return;
    }
    setWorking(true);
    try {
      await transitionDeliverable(projectId, d.id, target as Exclude<typeof target, 'ACCEPTED'> | 'ACCEPTED', { reason });
      await load();
    } catch (err: unknown) {
      Alert.alert('Transition failed', err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }

  if (!projectId) {
    return <View style={styles.empty}><Text>Select a project first.</Text></View>;
  }
  if (loading) {
    return <View style={styles.loading}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }

  return (
    <View style={styles.root}>
      <View style={styles.header}>
        <Text style={styles.headerTitle}>{gateCode ? `${gateCode} deliverables` : 'Deliverables'}</Text>
      </View>

      <View style={styles.filters}>
        {(['ALL', 'PENDING', 'IN_PROGRESS', 'SUBMITTED', 'ACCEPTED', 'REJECTED', 'WAIVED'] as StatusFilter[]).map((f) => (
          <TouchableOpacity
            key={f}
            style={[styles.filterChip, filter === f && styles.filterChipActive]}
            onPress={() => setFilter(f)}
          >
            <Text style={[styles.filterChipText, filter === f && styles.filterChipTextActive]}>
              {f === 'ALL' ? 'All' : f}
            </Text>
          </TouchableOpacity>
        ))}
        <TouchableOpacity
          style={[styles.filterChip, overdueOnly && { backgroundColor: theme.colors.danger, borderColor: theme.colors.danger }]}
          onPress={() => setOverdueOnly((v) => !v)}
          accessibilityLabel={overdueOnly ? 'Show all deliverables' : 'Show only overdue'}
        >
          <Text style={[styles.filterChipText, overdueOnly && { color: '#fff', fontWeight: '700' }]}>⚠ Overdue</Text>
        </TouchableOpacity>
      </View>

      <ScrollView
        contentContainerStyle={styles.scroll}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />}
      >
        {rows.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>Nothing matches.</Text>
            <Text style={styles.emptyHint}>Adjust filters or add a deliverable from the office dashboard.</Text>
          </View>
        ) : (
          rows.map((d) => (
            <View key={d.id} style={[styles.row, d.isOverdue && styles.rowOverdue]}>
              <View style={styles.rowHeader}>
                <Text style={styles.rowCode}>{d.code}</Text>
                <View style={[styles.statusPill, { backgroundColor: deliverableColor(d.status) }]}>
                  <Text style={styles.statusText}>{d.status}</Text>
                </View>
              </View>
              <Text style={styles.rowTitle} numberOfLines={2}>{d.title}</Text>
              <Text style={styles.rowMeta}>
                {d.type} · {d.ownerRole}
                {d.discipline ? ` · ${d.discipline}` : ''}
                {d.suitabilityTarget ? ` · ${d.suitabilityTarget}` : ''}
                {' · due '}{formatDate(d.dueDate)}
                {d.isOverdue ? ' ⚠ OVERDUE' : ''}
              </Text>
              <View style={styles.actions}>
                {nextStatusChoices(d).map((c) => (
                  <TouchableOpacity
                    key={c.status}
                    style={[styles.action, c.danger && styles.actionDanger, working && styles.disabled]}
                    disabled={working}
                    onPress={() => transition(d, c.status)}
                    accessibilityLabel={`Transition ${d.code} to ${c.status}`}
                  >
                    <Text style={[styles.actionText, c.danger && { color: '#fff' }]}>{c.label}</Text>
                  </TouchableOpacity>
                ))}
              </View>
            </View>
          ))
        )}
      </ScrollView>
    </View>
  );
}

// Phase 145 — produce a friendly button label for a state transition.
// Knows the canonical 6-state ISO 19650 vocabulary; falls back to a
// "from → to" label so custom states still render readably.
function friendlyTransitionLabel(from: string, to: string): string {
  const t = to.toUpperCase();
  switch (`${from.toUpperCase()}→${t}`) {
    case 'PENDING→IN_PROGRESS': return 'Start';
    case 'PENDING→WAIVED': return 'Waive';
    case 'IN_PROGRESS→SUBMITTED': return 'Submit';
    case 'IN_PROGRESS→WAIVED': return 'Waive';
    case 'IN_PROGRESS→PENDING': return 'Reset';
    case 'SUBMITTED→ACCEPTED': return 'Accept';
    case 'SUBMITTED→REJECTED': return 'Reject';
    case 'REJECTED→IN_PROGRESS': return 'Resume';
    case 'REJECTED→WAIVED': return 'Waive';
    case 'WAIVED→PENDING': return 'Reopen';
  }
  return `→ ${t}`;
}

function deliverableColor(s: DeliverableSummary['status']): string {
  switch (s) {
    case 'PENDING': return theme.colors.disabled;
    case 'IN_PROGRESS': return theme.colors.accent;
    case 'SUBMITTED': return theme.colors.warning;
    case 'ACCEPTED': return theme.colors.success;
    case 'REJECTED': return theme.colors.danger;
    case 'WAIVED': return theme.colors.textSecondary;
    default: return theme.colors.disabled;
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 80 },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  header: { paddingHorizontal: theme.spacing.md, paddingTop: theme.spacing.md, paddingBottom: theme.spacing.sm },
  headerTitle: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  filters: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.xs,
    paddingHorizontal: theme.spacing.md,
    paddingBottom: theme.spacing.sm,
  },
  filterChip: {
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 4,
    borderRadius: 16,
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  filterChipActive: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  filterChipText: { fontSize: theme.fontSize.xs, color: theme.colors.text },
  filterChipTextActive: { color: '#fff', fontWeight: '700' },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg, alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4 },
  row: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  rowOverdue: { borderLeftWidth: 4, borderLeftColor: theme.colors.danger },
  rowHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  rowCode: { fontSize: theme.fontSize.sm, fontWeight: '700', color: theme.colors.text },
  statusPill: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4 },
  statusText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  rowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, marginTop: 4 },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 4 },
  actions: { flexDirection: 'row', gap: theme.spacing.xs, marginTop: theme.spacing.sm, flexWrap: 'wrap' },
  action: {
    paddingHorizontal: theme.spacing.sm, paddingVertical: 4,
    borderRadius: theme.borderRadius.sm,
    backgroundColor: theme.colors.background,
    borderWidth: 1, borderColor: theme.colors.border,
  },
  actionText: { fontSize: theme.fontSize.xs, color: theme.colors.text, fontWeight: '600' },
  actionDanger: { backgroundColor: theme.colors.danger, borderColor: theme.colors.danger },
  disabled: { opacity: 0.5 },
});
