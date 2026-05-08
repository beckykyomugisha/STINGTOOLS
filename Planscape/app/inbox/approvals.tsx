// Phase 177 — Document approvals inbox.
//
// Lists every PENDING DocumentApproval row on the active project that the
// signed-in user has Manager+ rights to decide on. Approve / Reject are
// inline actions backed by PUT /api/projects/{id}/documents/{docId}/approval/{aid}.
// Approving a SHARED→PUBLISHED record unblocks the publishing transition;
// the originating coordinator can then run the transition from the Revit
// plugin or the documents tab and the gate is satisfied.

import { useCallback, useEffect, useState } from 'react';
import {
  View, Text, ScrollView, RefreshControl, TouchableOpacity,
  StyleSheet, ActivityIndicator, Alert, TextInput,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import {
  getMyActions, decideDocumentApproval, type MyActionsPayload,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

type Approval = MyActionsPayload['approvals'][number];

export default function ApprovalsScreen() {
  const router = useRouter();
  const activeProject = useProjectStore((s) => s.active);
  const projectId = activeProject?.id;

  const [approvals, setApprovals] = useState<Approval[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [decidingId, setDecidingId] = useState<string | null>(null);
  const [comments, setComments] = useState<Record<string, string>>({});

  const load = useCallback(async (isRefresh = false) => {
    if (!projectId) return;
    setError(null);
    if (isRefresh) setRefreshing(true);
    try {
      const data = await getMyActions(projectId, 100);
      setApprovals(data.approvals ?? []);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(msg);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  async function decide(ap: Approval, decision: 'APPROVED' | 'REJECTED') {
    if (!projectId) return;
    setDecidingId(ap.id);
    try {
      await decideDocumentApproval(projectId, ap.documentId, ap.id, decision, comments[ap.id]);
      setApprovals((cur) => cur.filter((a) => a.id !== ap.id));
      setComments((c) => { const next = { ...c }; delete next[ap.id]; return next; });
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      Alert.alert('Approval failed', msg);
    } finally {
      setDecidingId(null);
    }
  }

  function confirmReject(ap: Approval) {
    Alert.alert(
      'Reject approval?',
      `Reject ${ap.transition} for ${ap.fileName}?`,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Reject', style: 'destructive', onPress: () => decide(ap, 'REJECTED') },
      ],
    );
  }

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project to see pending approvals.</Text>
      </View>
    );
  }

  if (loading) {
    return (
      <View style={styles.loading}>
        <ActivityIndicator size="large" color={theme.colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.root}
      contentContainerStyle={styles.scroll}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => load(true)} />}
    >
      {error && <Text style={styles.error}>{error}</Text>}

      {approvals.length === 0 ? (
        <View style={styles.empty}>
          <Text style={styles.emptyText}>No pending approvals.</Text>
          <TouchableOpacity onPress={() => router.back()}>
            <Text style={styles.link}>Back to inbox</Text>
          </TouchableOpacity>
        </View>
      ) : (
        approvals.map((ap) => (
          <View key={ap.id} style={styles.card} accessibilityRole="summary">
            <View style={styles.cardHeader}>
              <Text style={styles.fileName} numberOfLines={1}>{ap.fileName}</Text>
              <Text style={styles.transition}>{ap.transition}</Text>
            </View>

            <Text style={styles.meta}>
              Requested {formatDate(ap.requestedAt)} by {ap.requestedBy}
              {ap.discipline ? ` · ${ap.discipline}` : ''}
            </Text>

            {ap.comments ? <Text style={styles.requesterComment}>“{ap.comments}”</Text> : null}

            <TextInput
              style={styles.commentBox}
              placeholder="Comment (optional)"
              placeholderTextColor={theme.colors.disabled}
              value={comments[ap.id] ?? ''}
              onChangeText={(v) => setComments((c) => ({ ...c, [ap.id]: v }))}
              multiline
              numberOfLines={2}
              accessibilityLabel={`Comment for ${ap.fileName}`}
            />

            <View style={styles.actions}>
              <TouchableOpacity
                style={[styles.btn, styles.btnReject]}
                onPress={() => confirmReject(ap)}
                disabled={decidingId === ap.id}
                accessibilityLabel={`Reject ${ap.fileName}`}
              >
                <Text style={styles.btnRejectText}>Reject</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={[styles.btn, styles.btnApprove]}
                onPress={() => decide(ap, 'APPROVED')}
                disabled={decidingId === ap.id}
                accessibilityLabel={`Approve ${ap.fileName}`}
              >
                {decidingId === ap.id
                  ? <ActivityIndicator color="#fff" />
                  : <Text style={styles.btnApproveText}>Approve</Text>}
              </TouchableOpacity>
            </View>
          </View>
        ))
      )}
    </ScrollView>
  );
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: theme.colors.background },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md, marginBottom: theme.spacing.md },
  link: { color: theme.colors.primary, fontSize: theme.fontSize.sm },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  card: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    borderLeftWidth: 4,
    borderLeftColor: theme.colors.priorityMedium,
  },
  cardHeader: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    marginBottom: theme.spacing.xs,
  },
  fileName: { flex: 1, fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  transition: {
    fontSize: theme.fontSize.xs, fontWeight: '600',
    color: theme.colors.primary, marginLeft: theme.spacing.sm,
  },
  meta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginBottom: theme.spacing.xs },
  requesterComment: {
    fontSize: theme.fontSize.sm, color: theme.colors.text,
    fontStyle: 'italic', marginVertical: theme.spacing.xs,
  },
  commentBox: {
    borderWidth: 1, borderColor: theme.colors.border,
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.sm,
    marginVertical: theme.spacing.sm,
    color: theme.colors.text,
    minHeight: 56,
    textAlignVertical: 'top',
  },
  actions: { flexDirection: 'row', gap: theme.spacing.sm },
  btn: {
    flex: 1, paddingVertical: theme.spacing.sm,
    borderRadius: theme.borderRadius.sm,
    alignItems: 'center', justifyContent: 'center',
  },
  btnApprove:    { backgroundColor: theme.colors.success ?? '#2E7D32' },
  btnReject:     { backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.danger },
  btnApproveText: { color: '#fff', fontWeight: '600' },
  btnRejectText:  { color: theme.colors.danger, fontWeight: '600' },
});
