// T3-20 — Project member management + ACL editor.
//
// Lists every active member on the project. PM / Admin / Owner (or anyone
// whose project access bypasses ACLs) sees Edit + Remove controls. The
// edit form lets them change projectRole / iso19650Role and the per-folder
// allow-lists (CDE states / disciplines / suitabilities) using multi-select
// chips. "Remove" is the canonical server DELETE — the brief asks for soft
// delete (isActive: false) but the server lacks a soft-delete update path
// for membership today; we surface this as a flagged risk.

import { useCallback, useEffect, useMemo, useState } from 'react';
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
  listProjectMembersFull,
  updateProjectMember,
  removeProjectMember,
  getMyProjectAccess,
  type ProjectMemberRow,
  type MyProjectAccess,
  type UpdateProjectMemberArgs,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

// Canonical option lists. Allow-list null/empty on the server means "all";
// we surface that visually as "(all)" in the chips row.
const CDE_STATES = ['WIP', 'SHARED', 'PUBLISHED', 'ARCHIVE'];
const DISCIPLINES = ['A', 'S', 'M', 'E', 'P', 'FP', 'LV', 'G'];
const SUITABILITIES = ['S0', 'S1', 'S2', 'S3', 'S4', 'S6', 'S7', 'A1', 'A2', 'A3', 'B1'];
const PROJECT_ROLES = ['PM', 'Admin', 'Owner', 'Coordinator', 'Member', 'Viewer'];
const ISO_ROLES = ['K', 'C', 'TI', 'L', 'AP', 'LAP'];

const ALLOWED_EDITORS = new Set(['PM', 'Admin', 'Owner']);

export default function MembersScreen() {
  const projectId = useProjectStore((s) => s.active?.id);

  const [members, setMembers] = useState<ProjectMemberRow[]>([]);
  const [myAccess, setMyAccess] = useState<MyProjectAccess | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);

  const canEdit = useMemo(() => {
    if (!myAccess) return false;
    if (myAccess.bypassesAcl) return true;
    return ALLOWED_EDITORS.has((myAccess.projectRole ?? '').trim());
  }, [myAccess]);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const [m, a] = await Promise.all([
        listProjectMembersFull(projectId),
        getMyProjectAccess(projectId).catch(() => null),
      ]);
      setMembers(m);
      setMyAccess(a);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load members');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

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
    <ScrollView
      style={styles.root}
      contentContainerStyle={styles.scroll}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}
    >
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {!canEdit ? (
        <View style={styles.banner}>
          <Text style={styles.bannerText}>
            View-only — only PMs, Admins, and Owners can change member roles or access on this project.
          </Text>
        </View>
      ) : null}

      {members.length === 0 ? (
        <View style={styles.emptyCard}>
          <Text style={styles.emptyTitle}>No members yet</Text>
        </View>
      ) : null}

      {members.map((m) => (
        <MemberCard
          key={m.id}
          member={m}
          editing={editingId === m.id}
          canEdit={canEdit}
          onStartEdit={() => setEditingId(m.id)}
          onCancelEdit={() => setEditingId(null)}
          onSaved={async () => { setEditingId(null); await load(); }}
          onRemoved={async () => { setEditingId(null); await load(); }}
          projectId={projectId}
        />
      ))}
    </ScrollView>
  );
}

function MemberCard({
  member, editing, canEdit, onStartEdit, onCancelEdit, onSaved, onRemoved, projectId,
}: {
  member: ProjectMemberRow;
  editing: boolean;
  canEdit: boolean;
  onStartEdit: () => void;
  onCancelEdit: () => void;
  onSaved: () => Promise<void>;
  onRemoved: () => Promise<void>;
  projectId: string;
}) {
  const [projectRole, setProjectRole] = useState(member.projectRole);
  const [isoRole, setIsoRole] = useState(member.iso19650Role);
  const [cdeStates, setCdeStates] = useState<string[]>(parseCsv(member.allowedCdeStates));
  const [disciplines, setDisciplines] = useState<string[]>(parseCsv(member.allowedDisciplines));
  const [suitabilities, setSuitabilities] = useState<string[]>(parseCsv(member.allowedSuitabilities));
  const [saving, setSaving] = useState(false);

  // When the parent flips edit mode, refresh local state from the latest member row.
  useEffect(() => {
    if (editing) {
      setProjectRole(member.projectRole);
      setIsoRole(member.iso19650Role);
      setCdeStates(parseCsv(member.allowedCdeStates));
      setDisciplines(parseCsv(member.allowedDisciplines));
      setSuitabilities(parseCsv(member.allowedSuitabilities));
    }
  }, [editing, member]);

  async function save() {
    setSaving(true);
    const body: UpdateProjectMemberArgs = {
      projectRole,
      iso19650Role: isoRole,
      allowedCdeStates: cdeStates,
      allowedDisciplines: disciplines,
      allowedSuitabilities: suitabilities,
    };
    try {
      await updateProjectMember(projectId, member.id, body);
      await onSaved();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      if (msg.includes('HTTP 403')) {
        Alert.alert('Permission denied', 'You do not have permission to edit project members.');
      } else {
        Alert.alert('Save failed', msg);
      }
    } finally {
      setSaving(false);
    }
  }

  function confirmRemove() {
    Alert.alert(
      'Remove member',
      `Remove ${member.displayName} from the project? They will lose access to all project data.`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Remove',
          style: 'destructive',
          onPress: async () => {
            setSaving(true);
            try {
              await removeProjectMember(projectId, member.id);
              await onRemoved();
            } catch (err: unknown) {
              Alert.alert('Remove failed', err instanceof Error ? err.message : String(err));
            } finally {
              setSaving(false);
            }
          },
        },
      ],
    );
  }

  return (
    <View style={styles.card}>
      <View style={styles.cardHeader}>
        <View style={{ flex: 1 }}>
          <Text style={styles.name}>{member.displayName}</Text>
          <Text style={styles.email}>{member.email}</Text>
        </View>
        {canEdit ? (
          editing ? (
            <TouchableOpacity onPress={onCancelEdit} accessibilityLabel="Close edit">
              <Text style={styles.headerLink}>Close</Text>
            </TouchableOpacity>
          ) : (
            <TouchableOpacity onPress={onStartEdit} accessibilityLabel={`Edit ${member.displayName}`}>
              <Text style={styles.headerLink}>Edit</Text>
            </TouchableOpacity>
          )
        ) : null}
      </View>

      {!editing ? (
        <View style={styles.summary}>
          <KV k="Project role" v={member.projectRole || '—'} />
          <KV k="ISO 19650 role" v={member.iso19650Role || '—'} />
          <KV k="CDE states" v={summariseAllowList(member.allowedCdeStates)} />
          <KV k="Disciplines" v={summariseAllowList(member.allowedDisciplines)} />
          <KV k="Suitabilities" v={summariseAllowList(member.allowedSuitabilities)} />
        </View>
      ) : (
        <View style={styles.editor}>
          <Text style={styles.fieldLabel}>Project role</Text>
          <ChipRow options={PROJECT_ROLES} selected={[projectRole]} onToggle={(v) => setProjectRole(v)} singleSelect />

          <Text style={styles.fieldLabel}>ISO 19650 role</Text>
          <ChipRow options={ISO_ROLES} selected={[isoRole]} onToggle={(v) => setIsoRole(v)} singleSelect />

          <Text style={styles.fieldLabel}>
            Allowed CDE states <Text style={styles.fieldHint}>(empty = all)</Text>
          </Text>
          <ChipRow
            options={CDE_STATES}
            selected={cdeStates}
            onToggle={(v) => toggleInList(v, cdeStates, setCdeStates)}
          />

          <Text style={styles.fieldLabel}>
            Allowed disciplines <Text style={styles.fieldHint}>(empty = all)</Text>
          </Text>
          <ChipRow
            options={DISCIPLINES}
            selected={disciplines}
            onToggle={(v) => toggleInList(v, disciplines, setDisciplines)}
          />

          <Text style={styles.fieldLabel}>
            Allowed suitabilities <Text style={styles.fieldHint}>(empty = all)</Text>
          </Text>
          <ChipRow
            options={SUITABILITIES}
            selected={suitabilities}
            onToggle={(v) => toggleInList(v, suitabilities, setSuitabilities)}
          />

          <View style={styles.buttonRow}>
            <TouchableOpacity
              style={[styles.button, styles.buttonDanger]}
              onPress={confirmRemove}
              disabled={saving}
            >
              <Text style={styles.buttonDangerText}>Remove</Text>
            </TouchableOpacity>
            <View style={{ flex: 1 }} />
            <TouchableOpacity
              style={[styles.button, styles.buttonGhost]}
              onPress={onCancelEdit}
              disabled={saving}
            >
              <Text style={styles.buttonGhostText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.button, styles.buttonPrimary]}
              onPress={save}
              disabled={saving}
            >
              {saving ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonPrimaryText}>Save</Text>}
            </TouchableOpacity>
          </View>
        </View>
      )}
    </View>
  );
}

function ChipRow({
  options, selected, onToggle, singleSelect,
}: {
  options: string[];
  selected: string[];
  onToggle: (value: string) => void;
  singleSelect?: boolean;
}) {
  return (
    <View style={styles.chipRow}>
      {options.map((opt) => {
        const isOn = selected.includes(opt);
        return (
          <TouchableOpacity
            key={opt}
            style={[styles.chip, isOn && styles.chipActive]}
            onPress={() => onToggle(opt)}
            accessibilityLabel={`${isOn ? 'Remove' : 'Add'} ${opt}`}
          >
            <Text style={[styles.chipText, isOn && styles.chipTextActive]}>
              {opt}{singleSelect && isOn ? ' ✓' : ''}
            </Text>
          </TouchableOpacity>
        );
      })}
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

function parseCsv(csv: string | null): string[] {
  if (!csv) return [];
  return csv.split(',').map((s) => s.trim()).filter(Boolean);
}

function summariseAllowList(csv: string | null): string {
  const items = parseCsv(csv);
  if (items.length === 0) return '(all)';
  return items.join(', ');
}

function toggleInList(value: string, current: string[], setter: (v: string[]) => void): void {
  if (current.includes(value)) {
    setter(current.filter((v) => v !== value));
  } else {
    setter([...current, value]);
  }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg,
    alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  banner: {
    backgroundColor: theme.colors.surface,
    borderLeftWidth: 4,
    borderLeftColor: theme.colors.priorityMedium,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.md,
    borderRadius: theme.borderRadius.sm,
  },
  bannerText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.xs },

  card: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  cardHeader: { flexDirection: 'row', alignItems: 'center', marginBottom: theme.spacing.sm },
  name: { fontSize: theme.fontSize.md, fontWeight: '700', color: theme.colors.text },
  email: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  headerLink: { color: theme.colors.accent, fontSize: theme.fontSize.sm, fontWeight: '600' },

  summary: { paddingTop: theme.spacing.xs },
  kvRow: { flexDirection: 'row', paddingVertical: 3 },
  kvKey: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, width: 130 },
  kvValue: { fontSize: theme.fontSize.sm, color: theme.colors.text, flex: 1 },

  editor: { paddingTop: theme.spacing.xs },
  fieldLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontWeight: '700',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginTop: theme.spacing.sm,
    marginBottom: 4,
  },
  fieldHint: { color: theme.colors.disabled, fontWeight: '400', textTransform: 'none' },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap' },
  chip: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
    backgroundColor: theme.colors.background,
    borderWidth: 1,
    borderColor: theme.colors.border,
    marginRight: 4,
    marginBottom: 4,
  },
  chipActive: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  chipText: { color: theme.colors.text, fontSize: theme.fontSize.xs, fontWeight: '600' },
  chipTextActive: { color: '#fff' },

  buttonRow: { flexDirection: 'row', alignItems: 'center', marginTop: theme.spacing.md },
  button: {
    paddingHorizontal: 14, paddingVertical: 10, borderRadius: theme.borderRadius.md,
    alignItems: 'center', justifyContent: 'center', minWidth: 80,
  },
  buttonPrimary: { backgroundColor: theme.colors.accent, marginLeft: theme.spacing.sm },
  buttonPrimaryText: { color: '#fff', fontSize: theme.fontSize.sm, fontWeight: '600' },
  buttonGhost: { backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.border },
  buttonGhostText: { color: theme.colors.text, fontSize: theme.fontSize.sm, fontWeight: '600' },
  buttonDanger: { backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.danger },
  buttonDangerText: { color: theme.colors.danger, fontSize: theme.fontSize.sm, fontWeight: '600' },
});
