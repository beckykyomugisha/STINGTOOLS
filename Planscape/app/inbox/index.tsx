// Phase 142 — My Actions inbox.
//
// One-screen view of every commitment assigned to the current user across
// the active project: issues to action, meeting actions to close out,
// document approvals to decide, and SLA-breached issues on the team to
// triage. Backed by /api/projects/{id}/myactions which is a single
// round-trip aggregator (was three before).

import { useEffect, useState, useCallback } from 'react';
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
import { theme, getPriorityColor } from '@/utils/theme';
import { getMyActions, type MyActionsPayload } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';
import { useInboxStore } from '@/stores/inboxStore';

export default function InboxScreen() {
  const router = useRouter();
  const activeProject = useProjectStore((s) => s.active);
  const projectId = activeProject?.id;

  const [data, setData] = useState<MyActionsPayload | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const payload = await getMyActions(projectId, 25);
      setData(payload);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load inbox');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  // Phase 177-C — re-load when an action elsewhere bumps the inbox version
  // (currently: approve/reject from /inbox/approvals).
  const inboxVersion = useInboxStore((s) => s.version);
  useEffect(() => { load(); }, [load, inboxVersion]);

  const onRefresh = useCallback(() => {
    setRefreshing(true);
    load();
  }, [load]);

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project on the dashboard first.</Text>
      </View>
    );
  }
  if (loading) {
    return (
      <View style={styles.loading}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.root}
      contentContainerStyle={styles.scroll}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
    >
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {/* T3-16 — link to the cross-project aggregator. */}
      <TouchableOpacity
        style={crossProjectStyles.crossProjectLink}
        onPress={() => router.push('/inbox/all-projects')}
        accessibilityLabel="View actions across all projects"
      >
        <Text style={crossProjectStyles.crossProjectText}>View across all projects ›</Text>
      </TouchableOpacity>

      {/* Summary tiles */}
      <View style={styles.tilesRow}>
        <SummaryTile label="Issues" value={data?.counts.issues ?? 0} color={theme.colors.priorityHigh} />
        <SummaryTile label="Actions" value={data?.counts.actions ?? 0} color={theme.colors.accent} />
        <SummaryTile label="Approvals" value={data?.counts.approvals ?? 0} color={theme.colors.priorityMedium} />
        <SummaryTile label="SLA" value={data?.counts.slaBreached ?? 0} color={theme.colors.danger} />
      </View>

      {/* Issues assigned to me */}
      <Section title="Issues assigned to me" empty={!data?.issues.length}>
        {data?.issues.map((it) => (
          <TouchableOpacity
            key={it.id}
            style={styles.row}
            onPress={() => router.push(`/issue-detail?id=${it.id}`)}
            accessibilityLabel={`Open issue ${it.issueCode}`}
          >
            <View style={[styles.priorityDot, { backgroundColor: getPriorityColor(it.priority) }]} />
            <View style={styles.rowBody}>
              <Text style={styles.rowTitle} numberOfLines={1}>
                {it.issueCode} — {it.title}
              </Text>
              <Text style={styles.rowMeta}>
                {it.type} · {it.priority} · {it.status}
                {it.dueDate ? ` · due ${formatDate(it.dueDate)}` : ''}
                {it.attachmentCount > 0 ? ` · 📷 ${it.attachmentCount}` : ''}
              </Text>
            </View>
          </TouchableOpacity>
        ))}
      </Section>

      {/* Meeting actions */}
      <Section title="Meeting actions" empty={!data?.actions.length}>
        {data?.actions.map((a) => (
          <TouchableOpacity
            key={a.id}
            style={styles.row}
            onPress={() => router.push(`/meetings/${a.meetingId}` as any)}
            accessibilityLabel={`Open meeting ${a.meetingTitle}`}
          >
            <View style={[styles.priorityDot, { backgroundColor: theme.colors.accent }]} />
            <View style={styles.rowBody}>
              <Text style={styles.rowTitle} numberOfLines={1}>{a.description}</Text>
              <Text style={styles.rowMeta}>
                {a.meetingType} · {a.status}
                {a.dueDate ? ` · due ${formatDate(a.dueDate)}` : ''}
                {a.linkedIssueId ? ' · linked issue' : ''}
              </Text>
            </View>
          </TouchableOpacity>
        ))}
      </Section>

      {/* Document approvals — Phase 177 routes to the inline approve/reject screen */}
      <Section title="Pending document approvals" empty={!data?.approvals.length}>
        {data?.approvals.map((ap) => (
          <TouchableOpacity
            key={ap.id}
            style={styles.row}
            onPress={() => router.push('/inbox/approvals')}
            accessibilityLabel={`Approve or reject ${ap.fileName}`}
          >
            <View style={[styles.priorityDot, { backgroundColor: theme.colors.priorityMedium }]} />
            <View style={styles.rowBody}>
              <Text style={styles.rowTitle} numberOfLines={1}>{ap.fileName}</Text>
              <Text style={styles.rowMeta}>
                {ap.transition}
                {ap.discipline ? ` · ${ap.discipline}` : ''} · requested {formatDate(ap.requestedAt)} by {ap.requestedBy}
              </Text>
            </View>
          </TouchableOpacity>
        ))}
      </Section>

      {/* SLA breached — manager triage */}
      <Section title="SLA breached on the team" empty={!data?.slaBreached.length}>
        {data?.slaBreached.map((it) => (
          <TouchableOpacity
            key={it.id}
            style={styles.row}
            onPress={() => router.push(`/issue-detail?id=${it.id}`)}
            accessibilityLabel={`Open SLA-breached issue ${it.issueCode}`}
          >
            <View style={[styles.priorityDot, { backgroundColor: theme.colors.danger }]} />
            <View style={styles.rowBody}>
              <Text style={styles.rowTitle} numberOfLines={1}>
                {it.issueCode} — {it.title}
              </Text>
              <Text style={styles.rowMeta}>
                {it.priority} · {it.status} · {it.assignee ?? 'unassigned'} · breached {it.breachHours} h
              </Text>
            </View>
          </TouchableOpacity>
        ))}
      </Section>

      {data?.generatedAt && (
        <Text style={styles.footer}>Updated {formatTime(data.generatedAt)}</Text>
      )}
    </ScrollView>
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

function Section({ title, empty, children }: { title: string; empty: boolean; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionTitle}>{title}</Text>
      {empty ? <Text style={styles.emptySection}>Nothing here.</Text> : children}
    </View>
  );
}

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString();
  } catch { return iso; }
}
function formatTime(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleTimeString();
  } catch { return iso; }
}

// T3-16 — local styles for the cross-project link, kept separate so the
// existing styles object stays untouched.
const crossProjectStyles = StyleSheet.create({
  crossProjectLink: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    alignItems: 'center',
  },
  crossProjectText: {
    color: theme.colors.accent,
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
  },
});

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  error: {
    backgroundColor: '#FFEBEE',
    color: theme.colors.danger,
    padding: theme.spacing.sm,
    borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  tilesRow: { flexDirection: 'row', gap: theme.spacing.sm, marginBottom: theme.spacing.md },
  tile: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.md,
    alignItems: 'center',
    borderTopWidth: 4,
  },
  tileValue: { fontSize: theme.fontSize.xl, fontWeight: '700' },
  tileLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  section: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  sectionTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  emptySection: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    fontStyle: 'italic',
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  priorityDot: { width: 10, height: 10, borderRadius: 5, marginRight: theme.spacing.sm },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '500' },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  footer: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
    textAlign: 'center',
    marginTop: theme.spacing.sm,
    marginBottom: theme.spacing.lg,
  },
});
