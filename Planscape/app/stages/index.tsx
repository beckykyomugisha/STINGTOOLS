// Phase 144 — RIBA stage-gate timeline.
//
// Lists every stage gate on the project ordered by SortOrder. Each row
// shows the gate's status pill, planned/actual dates, and a deliverable
// rollup (pending / submitted / accepted / overdue counts). Empty
// projects get a "Seed RIBA stages" button that POSTs to /seed-riba so
// the manager doesn't have to enter them by hand.

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
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  listStageGates,
  seedRibaStages,
  decideStageGate,
  type StageGateSummary,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function StagesScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);
  const [gates, setGates] = useState<StageGateSummary[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      setGates(await listStageGates(projectId));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load stage gates');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  async function onSeed() {
    if (!projectId) return;
    setWorking(true);
    try {
      const r = await seedRibaStages(projectId);
      Alert.alert('Stages seeded', `Added ${r.added} stage gate${r.added === 1 ? '' : 's'}.`);
      await load();
    } catch (err: unknown) {
      Alert.alert('Seed failed', err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }

  async function onDecide(gate: StageGateSummary, status: 'PASSED' | 'FAILED' | 'WAIVED') {
    if (!projectId) return;
    Alert.alert(
      `Mark ${gate.stageCode} as ${status}?`,
      `${gate.stageName}\n\nThis fires a project-scoped notification and stamps your name as the decision-maker.`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: status,
          style: status === 'FAILED' ? 'destructive' : 'default',
          onPress: async () => {
            setWorking(true);
            try {
              await decideStageGate(projectId, gate.id, status);
              await load();
            } catch (err: unknown) {
              Alert.alert('Decision failed', err instanceof Error ? err.message : String(err));
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
    <ScrollView
      style={styles.root}
      contentContainerStyle={styles.scroll}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />}
    >
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {(gates?.length ?? 0) === 0 ? (
        <View style={styles.emptyCard}>
          <Text style={styles.emptyTitle}>No stage gates yet</Text>
          <Text style={styles.emptyHint}>
            Seed the RIBA Plan of Work 2020 (stages 0–7) or add your own from the office dashboard.
          </Text>
          <TouchableOpacity
            style={[styles.button, working && styles.buttonDisabled]}
            disabled={working}
            onPress={onSeed}
            accessibilityLabel="Seed RIBA stages"
          >
            {working ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonText}>Seed RIBA stages</Text>}
          </TouchableOpacity>
        </View>
      ) : (
        gates!.map((g) => (
          <View key={g.id} style={styles.gateCard}>
            <View style={styles.gateHeader}>
              <View style={{ flex: 1 }}>
                <Text style={styles.gateCode}>{g.stageCode}</Text>
                <Text style={styles.gateName}>{g.stageName}</Text>
              </View>
              <View style={[styles.statusPill, { backgroundColor: statusColor(g.status) }]}>
                <Text style={styles.statusText}>{g.status}</Text>
              </View>
            </View>

            <View style={styles.gateDates}>
              <DateChip label="Planned" value={g.plannedDate} />
              <DateChip label="Actual" value={g.actualDate} />
            </View>

            <View style={styles.deliverableRow}>
              <Counter label="total" value={g.deliverables.total} />
              <Counter label="open" value={g.deliverables.pending + g.deliverables.inProgress} />
              <Counter label="submitted" value={g.deliverables.submitted} accent={theme.colors.accent} />
              <Counter label="accepted" value={g.deliverables.accepted} accent={theme.colors.success} />
              <Counter label="overdue" value={g.deliverables.overdue} accent={theme.colors.danger} />
            </View>

            <View style={styles.actions}>
              <TouchableOpacity
                style={styles.linkBtn}
                onPress={() => router.push({ pathname: '/stages/deliverables', params: { gateId: g.id, gateCode: g.stageCode } } as any)}
                accessibilityLabel={`Open deliverables for ${g.stageCode}`}
              >
                <Text style={styles.linkBtnText}>Deliverables ›</Text>
              </TouchableOpacity>
              {g.status !== 'PASSED' && g.status !== 'FAILED' && g.status !== 'WAIVED' && (
                <View style={styles.decisionRow}>
                  <TouchableOpacity
                    style={[styles.decisionBtn, { backgroundColor: theme.colors.success }, working && styles.buttonDisabled]}
                    disabled={working}
                    onPress={() => onDecide(g, 'PASSED')}
                  >
                    <Text style={styles.decisionText}>Pass</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    style={[styles.decisionBtn, { backgroundColor: theme.colors.danger }, working && styles.buttonDisabled]}
                    disabled={working}
                    onPress={() => onDecide(g, 'FAILED')}
                  >
                    <Text style={styles.decisionText}>Fail</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    style={[styles.decisionBtn, { backgroundColor: theme.colors.disabled }, working && styles.buttonDisabled]}
                    disabled={working}
                    onPress={() => onDecide(g, 'WAIVED')}
                  >
                    <Text style={styles.decisionText}>Waive</Text>
                  </TouchableOpacity>
                </View>
              )}
              {g.status === 'PASSED' || g.status === 'FAILED' || g.status === 'WAIVED' ? (
                <Text style={styles.decidedNote}>
                  Decided by {g.decidedBy ?? 'unknown'}{g.decidedAt ? ` · ${formatDate(g.decidedAt)}` : ''}
                </Text>
              ) : null}
            </View>
          </View>
        ))
      )}
    </ScrollView>
  );
}

function DateChip({ label, value }: { label: string; value?: string | null }) {
  return (
    <View style={styles.dateChip}>
      <Text style={styles.dateChipLabel}>{label}</Text>
      <Text style={styles.dateChipValue}>{value ? formatDate(value) : '—'}</Text>
    </View>
  );
}

function Counter({ label, value, accent }: { label: string; value: number; accent?: string }) {
  return (
    <View style={styles.counter}>
      <Text style={[styles.counterValue, accent ? { color: accent } : null]}>{value}</Text>
      <Text style={styles.counterLabel}>{label}</Text>
    </View>
  );
}

function statusColor(s: StageGateSummary['status']): string {
  switch (s) {
    case 'NOT_STARTED': return theme.colors.disabled;
    case 'IN_PROGRESS': return theme.colors.accent;
    case 'PASSED': return theme.colors.success;
    case 'FAILED': return theme.colors.danger;
    case 'WAIVED': return theme.colors.textSecondary;
    default: return theme.colors.disabled;
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg, alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text, marginBottom: 8 },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, textAlign: 'center', marginBottom: theme.spacing.md },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  gateCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  gateHeader: { flexDirection: 'row', alignItems: 'center', marginBottom: theme.spacing.sm },
  gateCode: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  gateName: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 2 },
  statusPill: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 4 },
  statusText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  gateDates: { flexDirection: 'row', gap: theme.spacing.md, marginBottom: theme.spacing.sm },
  dateChip: { flex: 1 },
  dateChipLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, textTransform: 'uppercase', letterSpacing: 0.5 },
  dateChipValue: { fontSize: theme.fontSize.sm, color: theme.colors.text, marginTop: 2 },
  deliverableRow: {
    flexDirection: 'row',
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    paddingVertical: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  counter: { flex: 1, alignItems: 'center' },
  counterValue: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  counterLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary },
  actions: { gap: theme.spacing.sm },
  linkBtn: { paddingVertical: 4 },
  linkBtnText: { fontSize: theme.fontSize.sm, color: theme.colors.accent, fontWeight: '600' },
  decisionRow: { flexDirection: 'row', gap: theme.spacing.sm },
  decisionBtn: {
    flex: 1, paddingVertical: theme.spacing.sm,
    borderRadius: theme.borderRadius.sm, alignItems: 'center',
  },
  decisionText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.sm },
  decidedNote: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, fontStyle: 'italic' },
  button: {
    backgroundColor: theme.colors.accent,
    paddingVertical: theme.spacing.md,
    paddingHorizontal: theme.spacing.lg,
    borderRadius: theme.borderRadius.md,
    minWidth: 200,
    alignItems: 'center',
  },
  buttonText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.md },
  buttonDisabled: { opacity: 0.5 },
});
