// T3-17 — Information Deliverable detail.
//
// Shows the full record. Edit form opens inline. Transition button shows
// the legal next states from getDeliverableStateMachine() and submits the
// transition with an optional reason. Offline-aware: when the device is
// offline, edits and transitions are enqueued through the offline queue
// so the user can still progress work on a flaky site connection.

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  TextInput,
  StyleSheet,
  Alert,
  ActivityIndicator,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  getDeliverable,
  updateDeliverable,
  transitionDeliverable,
  getDeliverableStateMachine,
  type DeliverableSummary,
  type DeliverableStateMachine,
  type DeliverableUpsertArgs,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';
import { isOnline } from '@/utils/connectivity';
import { enqueue } from '@/utils/offlineQueue';

export default function DeliverableDetailScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useProjectStore((s) => s.active?.id);

  const [d, setD] = useState<DeliverableSummary | null>(null);
  const [sm, setSm] = useState<DeliverableStateMachine | null>(null);
  const [loading, setLoading] = useState(true);
  const [acting, setActing] = useState(false);
  const [editing, setEditing] = useState(false);

  // Edit form state — populated when the user taps Edit.
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [discipline, setDiscipline] = useState('');
  const [suitability, setSuitability] = useState('');
  const [dueDate, setDueDate] = useState('');

  // Transition reason text — surfaced when the user picks a target state.
  const [transitionReason, setTransitionReason] = useState('');

  const load = useCallback(async () => {
    if (!projectId || !id) return;
    try {
      setLoading(true);
      const [det, mach] = await Promise.all([
        getDeliverable(projectId, id),
        getDeliverableStateMachine(projectId).catch(() => null),
      ]);
      setD(det);
      setSm(mach);
      // Seed the edit fields so the user can flip into edit mode without
      // losing the current values.
      setTitle(det.title);
      setDescription('');
      setDiscipline(det.discipline ?? '');
      setSuitability(det.suitabilityTarget ?? '');
      setDueDate(det.dueDate.split('T')[0] ?? det.dueDate);
    } catch (err: unknown) {
      Alert.alert('Failed to load', err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [projectId, id]);

  useEffect(() => { void load(); }, [load]);

  async function saveEdit() {
    if (!projectId || !id || !d) return;
    if (!title.trim()) { Alert.alert('Title required'); return; }
    setActing(true);
    const body: DeliverableUpsertArgs = {
      title: title.trim(),
      description: description.trim() || undefined,
      discipline: discipline.trim() || null,
      suitabilityTarget: suitability.trim() || null,
      dueDate: dueDate ? new Date(dueDate).toISOString() : undefined,
    };
    try {
      const online = await isOnline();
      if (online) {
        await updateDeliverable(projectId, id, body);
        await load();
      } else {
        await enqueue('UPDATE_DELIVERABLE', { projectId, deliverableId: id, body });
        Alert.alert('Queued', 'You are offline — your edit will sync when network is back.');
      }
      setEditing(false);
    } catch (err: unknown) {
      Alert.alert('Save failed', err instanceof Error ? err.message : String(err));
    } finally {
      setActing(false);
    }
  }

  async function doTransition(target: string) {
    if (!projectId || !id) return;
    setActing(true);
    try {
      const online = await isOnline();
      if (online) {
        await transitionDeliverable(projectId, id, target, {
          reason: transitionReason.trim() || undefined,
        });
        setTransitionReason('');
        await load();
      } else {
        await enqueue('TRANSITION_DELIVERABLE', {
          projectId, deliverableId: id, newStatus: target,
          reason: transitionReason.trim() || undefined,
        });
        Alert.alert('Queued', 'You are offline — the transition will sync when network is back.');
        setTransitionReason('');
      }
    } catch (err: unknown) {
      Alert.alert('Transition failed', err instanceof Error ? err.message : String(err));
    } finally {
      setActing(false);
    }
  }

  if (loading) {
    return <View style={styles.loading}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }
  if (!d) return <View style={styles.loading}><Text style={styles.emptyText}>Deliverable not found.</Text></View>;

  const legalTargets = sm
    ? sm.transitions.filter((t) => t.from === d.status).map((t) => t.to)
    : [];

  return (
    <ScrollView style={styles.root} contentContainerStyle={styles.scroll}>
      <View style={styles.header}>
        <View style={[styles.statusPill, { backgroundColor: statusColor(d.status) }]}>
          <Text style={styles.statusText}>{prettyStatus(d.status)}</Text>
        </View>
        {d.isOverdue ? (
          <View style={[styles.statusPill, { backgroundColor: theme.colors.danger, marginLeft: theme.spacing.xs }]}>
            <Text style={styles.statusText}>OVERDUE</Text>
          </View>
        ) : null}
      </View>

      <Text style={styles.code}>{d.code}</Text>
      <Text style={styles.title}>{d.title}</Text>

      <View style={styles.kvBlock}>
        <KV k="Type" v={d.type} />
        <KV k="Owner role" v={d.ownerRole || '—'} />
        <KV k="Discipline" v={d.discipline ?? '—'} />
        <KV k="Suitability target" v={d.suitabilityTarget ?? '—'} />
        <KV k="Due date" v={formatDate(d.dueDate)} />
        {d.submittedAt ? <KV k="Submitted" v={`${formatDate(d.submittedAt)} by ${d.submittedBy ?? '—'}`} /> : null}
        {d.acceptedAt ? <KV k="Accepted" v={`${formatDate(d.acceptedAt)} by ${d.acceptedBy ?? '—'}`} /> : null}
        {d.documentId ? <KV k="Linked document" v={d.documentId} /> : null}
      </View>

      {/* Transition controls */}
      {legalTargets.length > 0 ? (
        <View style={styles.section}>
          <Text style={styles.sectionLabel}>Move to next state</Text>
          <TextInput
            style={styles.input}
            placeholder="Reason (optional)"
            placeholderTextColor={theme.colors.disabled}
            value={transitionReason}
            onChangeText={setTransitionReason}
            multiline
          />
          <View style={styles.transitionRow}>
            {legalTargets.map((target) => (
              <TouchableOpacity
                key={target}
                style={[styles.transitionButton, { backgroundColor: roleColor(sm, target) }]}
                onPress={() => doTransition(target)}
                disabled={acting}
                accessibilityLabel={`Transition to ${target}`}
              >
                <Text style={styles.transitionButtonText}>→ {prettyStatus(target)}</Text>
              </TouchableOpacity>
            ))}
          </View>
        </View>
      ) : (
        <View style={styles.section}>
          <Text style={styles.sectionLabel}>No further transitions</Text>
          <Text style={styles.emptyText}>This deliverable is in a terminal state.</Text>
        </View>
      )}

      {/* Edit form */}
      {editing ? (
        <View style={styles.section}>
          <Text style={styles.sectionLabel}>Edit deliverable</Text>
          <Field label="Title" value={title} onChange={setTitle} />
          <Field label="Description" value={description} onChange={setDescription} multiline />
          <Field label="Discipline" value={discipline} onChange={setDiscipline} />
          <Field label="Suitability target" value={suitability} onChange={setSuitability} />
          <Field label="Due date (YYYY-MM-DD)" value={dueDate} onChange={setDueDate} />
          <View style={styles.formButtons}>
            <TouchableOpacity
              style={[styles.button, styles.buttonGhost]}
              onPress={() => setEditing(false)}
              disabled={acting}
            >
              <Text style={styles.buttonGhostText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.button, styles.buttonPrimary]}
              onPress={saveEdit}
              disabled={acting}
            >
              {acting ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonPrimaryText}>Save</Text>}
            </TouchableOpacity>
          </View>
        </View>
      ) : (
        <TouchableOpacity
          style={[styles.button, styles.buttonGhost, { marginTop: theme.spacing.md }]}
          onPress={() => setEditing(true)}
          disabled={acting}
        >
          <Text style={styles.buttonGhostText}>Edit details</Text>
        </TouchableOpacity>
      )}
    </ScrollView>
  );
}

function Field({
  label, value, onChange, multiline,
}: { label: string; value: string; onChange: (v: string) => void; multiline?: boolean }) {
  return (
    <View style={{ marginBottom: theme.spacing.sm }}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <TextInput
        style={[styles.input, multiline && { minHeight: 60, textAlignVertical: 'top' }]}
        value={value}
        onChangeText={onChange}
        multiline={multiline}
      />
    </View>
  );
}

function KV({ k, v }: { k: string; v: string }) {
  return (
    <View style={styles.kvRow}>
      <Text style={styles.kvKey}>{k}</Text>
      <Text style={styles.kvValue} numberOfLines={2}>{v}</Text>
    </View>
  );
}

function prettyStatus(s: string): string { return s.replace(/_/g, ' '); }

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

function roleColor(sm: DeliverableStateMachine | null, target: string): string {
  const role = sm?.roles?.[target];
  switch (role) {
    case 'submitting': return theme.colors.priorityMedium;
    case 'accepting': return theme.colors.success;
    case 'rejecting': return theme.colors.danger;
    case 'terminal': return theme.colors.textSecondary;
    default: return theme.colors.accent;
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.sm },

  header: { flexDirection: 'row', marginBottom: theme.spacing.sm },
  statusPill: { paddingHorizontal: 8, paddingVertical: 4, borderRadius: 10 },
  statusText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  code: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginBottom: 4 },
  title: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text, marginBottom: theme.spacing.md },

  kvBlock: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
  },
  kvRow: { flexDirection: 'row', paddingVertical: 4 },
  kvKey: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, width: 140 },
  kvValue: { fontSize: theme.fontSize.sm, color: theme.colors.text, flex: 1 },

  section: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginTop: theme.spacing.md,
  },
  sectionLabel: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.text,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: theme.spacing.sm,
  },
  fieldLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginBottom: 4 },
  input: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
  },

  transitionRow: { flexDirection: 'row', flexWrap: 'wrap', marginTop: theme.spacing.sm },
  transitionButton: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: theme.borderRadius.sm,
    marginRight: theme.spacing.xs,
    marginBottom: theme.spacing.xs,
  },
  transitionButtonText: { color: '#fff', fontSize: theme.fontSize.sm, fontWeight: '600' },

  formButtons: { flexDirection: 'row', justifyContent: 'flex-end', marginTop: theme.spacing.md },
  button: {
    paddingHorizontal: 16, paddingVertical: 10, borderRadius: theme.borderRadius.md,
    alignItems: 'center', justifyContent: 'center', minWidth: 100,
  },
  buttonPrimary: { backgroundColor: theme.colors.accent, marginLeft: theme.spacing.sm },
  buttonPrimaryText: { color: '#fff', fontSize: theme.fontSize.sm, fontWeight: '600' },
  buttonGhost: { backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.border },
  buttonGhostText: { color: theme.colors.text, fontSize: theme.fontSize.sm, fontWeight: '600' },
});
