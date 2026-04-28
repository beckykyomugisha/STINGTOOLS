// Phase 145 — Stage gate criterion sign-off.
//
// One row per criterion. Toggle the checkbox to sign-off / unsign each
// criterion; the server stamps the actor's display name and timestamp
// for met=true and clears them for met=false. A "Reset & seed defaults"
// button replaces the criteria with a RIBA-stage-aware default set when
// the gate is empty — useful for projects that haven't authored a
// bespoke checklist yet.

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
  TextInput,
} from 'react-native';
import { useLocalSearchParams } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  listStageCriteria,
  signOffStageCriterion,
  replaceStageCriteria,
  type StageCriterion,
  type StageCriteriaResponse,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function CriteriaScreen() {
  const { gateId, gateCode } = useLocalSearchParams<{ gateId: string; gateCode?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);
  const [data, setData] = useState<StageCriteriaResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [working, setWorking] = useState<string | null>(null);
  const [editingComment, setEditingComment] = useState<string | null>(null);
  const [commentDraft, setCommentDraft] = useState('');

  const load = useCallback(async () => {
    if (!projectId || !gateId) return;
    try {
      setData(await listStageCriteria(projectId, gateId));
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, gateId]);

  useEffect(() => { load(); }, [load]);

  async function toggle(c: StageCriterion) {
    if (!projectId || !gateId) return;
    setWorking(c.key);
    try {
      const next = await signOffStageCriterion(
        projectId, gateId, c.key, !c.met,
        { comment: c.comment ?? undefined, evidenceDocId: c.evidenceDocId ?? undefined },
      );
      setData(next);
    } catch (err: unknown) {
      Alert.alert('Sign-off failed', err instanceof Error ? err.message : String(err));
    } finally {
      setWorking(null);
    }
  }

  async function saveComment(c: StageCriterion) {
    if (!projectId || !gateId) return;
    setWorking(c.key);
    try {
      const next = await signOffStageCriterion(
        projectId, gateId, c.key, c.met,
        { comment: commentDraft || undefined, evidenceDocId: c.evidenceDocId ?? undefined },
      );
      setData(next);
      setEditingComment(null);
      setCommentDraft('');
    } catch (err: unknown) {
      Alert.alert('Save comment failed', err instanceof Error ? err.message : String(err));
    } finally {
      setWorking(null);
    }
  }

  async function seedDefaults() {
    if (!projectId || !gateId) return;
    const defaults = ribaDefaults(gateCode ?? '');
    if (defaults.length === 0) {
      Alert.alert('No template available', 'No built-in criteria for this stage code. Author criteria from the office dashboard.');
      return;
    }
    setWorking('__seed__');
    try {
      await replaceStageCriteria(projectId, gateId, defaults);
      await load();
    } catch (err: unknown) {
      Alert.alert('Seed failed', err instanceof Error ? err.message : String(err));
    } finally {
      setWorking(null);
    }
  }

  const sorted = useMemo(
    () => (data?.criteria ?? []).slice().sort((a, b) => a.label.localeCompare(b.label)),
    [data?.criteria],
  );

  if (!projectId || !gateId) {
    return <View style={styles.empty}><Text style={styles.emptyText}>Open a stage gate first.</Text></View>;
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
      {data && data.criteria.length > 0 && (
        <View style={styles.summary}>
          <Text style={styles.summaryTitle}>
            {data.summary.met} of {data.summary.total} criteria met
          </Text>
          <View style={styles.progressBg}>
            <View style={[styles.progressFill, {
              width: data.summary.total === 0 ? '0%' : `${Math.round(100 * data.summary.met / data.summary.total)}%`,
            }]} />
          </View>
          <Text style={styles.summaryHint}>
            {data.summary.outstanding} outstanding · gate {gateCode ?? data.stageCode}
          </Text>
        </View>
      )}

      {sorted.length === 0 ? (
        <View style={styles.emptyCard}>
          <Text style={styles.emptyTitle}>No criteria authored yet</Text>
          <Text style={styles.emptyHint}>
            Seed a default checklist for this RIBA stage, or author bespoke criteria from the office dashboard.
          </Text>
          <TouchableOpacity
            style={[styles.button, working === '__seed__' && styles.buttonDisabled]}
            disabled={working !== null}
            onPress={seedDefaults}
            accessibilityLabel="Seed default RIBA criteria"
          >
            {working === '__seed__'
              ? <ActivityIndicator color="#fff" />
              : <Text style={styles.buttonText}>Seed defaults</Text>}
          </TouchableOpacity>
        </View>
      ) : (
        sorted.map((c) => {
          const busy = working === c.key;
          const editing = editingComment === c.key;
          return (
            <View key={c.key} style={[styles.row, c.met && styles.rowMet]}>
              <TouchableOpacity
                style={styles.checkbox}
                disabled={busy}
                onPress={() => toggle(c)}
                accessibilityLabel={c.met ? `Unsign ${c.label}` : `Sign off ${c.label}`}
              >
                <Text style={[styles.checkboxText, c.met && { color: theme.colors.success }]}>
                  {busy ? '⋯' : c.met ? '☑' : '☐'}
                </Text>
              </TouchableOpacity>
              <View style={styles.rowBody}>
                <Text style={styles.rowTitle}>{c.label}</Text>
                {c.description ? <Text style={styles.rowDesc}>{c.description}</Text> : null}
                {c.met && (c.signedBy || c.signedAt) ? (
                  <Text style={styles.signoff}>
                    Signed by {c.signedBy ?? 'Unknown'}{c.signedAt ? ` · ${formatDate(c.signedAt)}` : ''}
                  </Text>
                ) : null}
                {editing ? (
                  <View style={styles.commentEdit}>
                    <TextInput
                      style={styles.commentInput}
                      value={commentDraft}
                      onChangeText={setCommentDraft}
                      placeholder="Notes / evidence reference"
                      placeholderTextColor={theme.colors.disabled}
                      multiline
                    />
                    <View style={styles.commentActions}>
                      <TouchableOpacity onPress={() => { setEditingComment(null); setCommentDraft(''); }}>
                        <Text style={styles.commentCancel}>Cancel</Text>
                      </TouchableOpacity>
                      <TouchableOpacity disabled={busy} onPress={() => saveComment(c)}>
                        <Text style={styles.commentSave}>Save</Text>
                      </TouchableOpacity>
                    </View>
                  </View>
                ) : (
                  <TouchableOpacity
                    onPress={() => { setEditingComment(c.key); setCommentDraft(c.comment ?? ''); }}
                    accessibilityLabel={c.comment ? 'Edit comment' : 'Add comment'}
                  >
                    <Text style={styles.commentLink}>
                      {c.comment ? `“${c.comment}”` : '+ Add comment / evidence ref'}
                    </Text>
                  </TouchableOpacity>
                )}
              </View>
            </View>
          );
        })
      )}
    </ScrollView>
  );
}

// Phase 145 — minimal RIBA-stage default checklists. These are not
// exhaustive; the BIM Manager is expected to author bespoke criteria
// from the office dashboard. Provided here so a freshly-seeded gate
// has somewhere to start on mobile.
function ribaDefaults(stageCode: string): StageCriterion[] {
  const code = (stageCode || '').toUpperCase();
  switch (code) {
    case 'RIBA-0':
      return [
        { key: 'strategic_def_business_case', label: 'Strategic business case approved by client', met: false },
        { key: 'strategic_def_brief', label: 'Statement of need / strategic brief signed off', met: false },
        { key: 'strategic_def_appointments', label: 'Initial appointments confirmed (lead designer, BIM lead)', met: false },
      ];
    case 'RIBA-1':
      return [
        { key: 'preparation_eir', label: 'Exchange Information Requirements (EIR) issued', met: false },
        { key: 'preparation_pir', label: 'Project Information Requirements (PIR) approved', met: false },
        { key: 'preparation_bep_pre', label: 'Pre-appointment BEP submitted by appointed parties', met: false },
        { key: 'preparation_midp_outline', label: 'Outline MIDP / TIDP drafted', met: false },
        { key: 'preparation_feasibility', label: 'Feasibility studies complete', met: false },
      ];
    case 'RIBA-2':
      return [
        { key: 'concept_design_options', label: 'Design options reviewed and selected', met: false },
        { key: 'concept_design_bep_post', label: 'Post-appointment BEP issued and accepted', met: false },
        { key: 'concept_design_loi', label: 'LOIN matrix agreed for stage 3', met: false },
        { key: 'concept_design_outline_specs', label: 'Outline specifications complete', met: false },
        { key: 'concept_design_costplan', label: 'Stage 2 cost plan approved', met: false },
      ];
    case 'RIBA-3':
      return [
        { key: 'spatial_coord_clash_zero', label: 'Federated model is clash-free at LOD 300', met: false },
        { key: 'spatial_coord_grid', label: 'Grid + level datum agreed across disciplines', met: false },
        { key: 'spatial_coord_zoning', label: 'Building zoning approved by client', met: false },
        { key: 'spatial_coord_drawings', label: 'Stage 3 drawing register published to S2', met: false },
      ];
    case 'RIBA-4':
      return [
        { key: 'tech_design_loi', label: 'Level of information at LOIN target for stage', met: false },
        { key: 'tech_design_specs', label: 'Specifications complete and signed off', met: false },
        { key: 'tech_design_clash', label: 'Coordination clashes resolved or accepted', met: false },
        { key: 'tech_design_costplan', label: 'Cost plan reconciled to model quantities', met: false },
      ];
    case 'RIBA-5':
      return [
        { key: 'mfg_construction_iso', label: 'Construction information issued (S4)', met: false },
        { key: 'mfg_construction_rfis', label: 'RFI backlog within SLA', met: false },
        { key: 'mfg_construction_revisions', label: 'Revision register up to date', met: false },
      ];
    case 'RIBA-6':
      return [
        { key: 'handover_cobie', label: 'COBie spreadsheet validated', met: false },
        { key: 'handover_om', label: 'O&M manuals published', met: false },
        { key: 'handover_asbuilt', label: 'As-built model issued', met: false },
        { key: 'handover_warranties', label: 'Warranty pack delivered', met: false },
      ];
    case 'RIBA-7':
      return [
        { key: 'use_aim', label: 'AIM populated with operational data', met: false },
        { key: 'use_ppm', label: 'Planned preventive maintenance schedule live', met: false },
      ];
    default:
      return [];
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 80 },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg, alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, textAlign: 'center', marginTop: 4, marginBottom: theme.spacing.md },
  summary: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  summaryTitle: { fontSize: theme.fontSize.md, fontWeight: '700', color: theme.colors.text },
  summaryHint: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 4 },
  progressBg: {
    height: 6,
    backgroundColor: theme.colors.background,
    borderRadius: 3,
    overflow: 'hidden',
    marginVertical: theme.spacing.sm,
  },
  progressFill: { height: '100%', backgroundColor: theme.colors.success },
  row: {
    flexDirection: 'row',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  rowMet: { borderLeftWidth: 4, borderLeftColor: theme.colors.success },
  checkbox: { paddingRight: theme.spacing.md, paddingTop: 2 },
  checkboxText: { fontSize: 22, color: theme.colors.text },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  rowDesc: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  signoff: { fontSize: theme.fontSize.xs, color: theme.colors.success, marginTop: 4, fontStyle: 'italic' },
  commentLink: { fontSize: theme.fontSize.xs, color: theme.colors.accent, marginTop: 6 },
  commentEdit: { marginTop: 6 },
  commentInput: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1, borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 6,
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    minHeight: 60,
    textAlignVertical: 'top',
  },
  commentActions: { flexDirection: 'row', justifyContent: 'flex-end', gap: theme.spacing.md, marginTop: 4 },
  commentCancel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary },
  commentSave: { fontSize: theme.fontSize.xs, color: theme.colors.accent, fontWeight: '700' },
  button: {
    backgroundColor: theme.colors.accent,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.md,
    borderRadius: theme.borderRadius.md,
    minWidth: 180,
    alignItems: 'center',
  },
  buttonText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.md },
  buttonDisabled: { opacity: 0.5 },
});
