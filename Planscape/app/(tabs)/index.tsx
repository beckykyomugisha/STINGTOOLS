import { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  RefreshControl,
  TouchableOpacity,
  ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme, getRAGColor, getPriorityColor } from '@/utils/theme';
import {
  getProjectDashboard,
  getMyActions,
  getFederationStatus,
  listSyncConflicts,
  type FederationStatus,
} from '@/api/endpoints';
import type { DashboardData, BimIssue } from '@/types/api';
import { useProjectStore } from '@/stores/projectStore';
import { useInboxStore } from '@/stores/inboxStore';
import { SitePhotoFab } from '@/components/SitePhotoFab';

export default function DashboardScreen() {
  const router = useRouter();

  // Dashboard reads the active project from the shared store. The Projects tab
  // (app/projects/index.tsx) sets it when the user taps a row; this screen
  // fetches that project's dashboard data. If no project is active yet we
  // prompt the user to go pick one from the Projects tab.
  const activeProject = useProjectStore((s) => s.active);

  const [dashboard, setDashboard] = useState<DashboardData | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Phase 142 — My Actions count, populated via the aggregator endpoint.
  // Failure is non-fatal; the dashboard still renders without it.
  const [myActionsTotal, setMyActionsTotal] = useState<number | null>(null);
  const [slaCount, setSlaCount] = useState<number>(0);
  // Phase 143 — BIM Coordinator surfaces. Both fetched best-effort.
  const [federation, setFederation] = useState<FederationStatus | null>(null);
  const [pendingConflicts, setPendingConflicts] = useState<number>(0);

  const loadData = useCallback(async () => {
    if (!activeProject) {
      setLoading(false);
      return;
    }
    try {
      setError(null);
      const data = await getProjectDashboard(activeProject.id);
      setDashboard(data);

      // Phase 142 — fetch the My Actions count in parallel with the dashboard.
      // Best-effort: a stale token, missing membership row, or 5xx silently
      // leaves the badge null and the card hidden, never blocking the dashboard.
      try {
        const ma = await getMyActions(activeProject.id, 1);
        setMyActionsTotal(ma.counts.total);
        setSlaCount(ma.counts.slaBreached);
      } catch {
        setMyActionsTotal(null);
        setSlaCount(0);
      }

      // Phase 143 — BIM Coordinator surfaces. Same best-effort pattern.
      // Federation + conflicts run in parallel since they hit independent
      // tables and we want minimum latency on dashboard cold start.
      const [fedRes, confRes] = await Promise.allSettled([
        getFederationStatus(activeProject.id, 14),
        listSyncConflicts(activeProject.id, { resolution: 'PENDING', pageSize: 1 }),
      ]);
      setFederation(fedRes.status === 'fulfilled' ? fedRes.value : null);
      setPendingConflicts(
        confRes.status === 'fulfilled' ? (confRes.value.summary.pending ?? 0) : 0,
      );
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load dashboard';
      setError(msg);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [activeProject]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Phase 177-C — refresh the My Actions tile after the user approves /
  // rejects something elsewhere in the app. The store is bumped by the
  // approvals screen; we re-fetch only the small MyActions slice rather
  // than the full dashboard which is expensive.
  const inboxVersion = useInboxStore((s) => s.version);
  useEffect(() => {
    if (!activeProject) return;
    getMyActions(activeProject.id, 1).then(
      (ma) => { setMyActionsTotal(ma.counts.total); setSlaCount(ma.counts.slaBreached); },
      () => { /* leave previous values */ },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inboxVersion]);

  function onRefresh() {
    setRefreshing(true);
    loadData();
  }

  // No active project yet — the user has not tapped a project from the
  // Projects tab. Show a clear prompt rather than a confusing empty state.
  if (!activeProject) {
    return (
      <View style={styles.center}>
        <Text style={styles.noProjectIcon}>🏗</Text>
        <Text style={styles.noProjectTitle}>No project selected</Text>
        <Text style={styles.noProjectSub}>
          Go to the Projects tab and tap a project to load its dashboard here.
        </Text>
        <TouchableOpacity
          style={styles.goToProjectsBtn}
          onPress={() => router.push('/projects' as any)}
          accessibilityLabel="Go to Projects"
        >
          <Text style={styles.goToProjectsBtnText}>Browse Projects</Text>
        </TouchableOpacity>
      </View>
    );
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading dashboard...</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorIcon}>!</Text>
        <Text style={styles.errorText}>{error}</Text>
        <TouchableOpacity style={styles.retryButton} onPress={() => { setLoading(true); loadData(); }}>
          <Text style={styles.retryButtonText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  if (!dashboard) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyText}>No data for this project.</Text>
        <Text style={styles.emptySubtext}>Pull to refresh or check your connection.</Text>
      </View>
    );
  }

  const compliancePct = dashboard.compliance?.compliancePercent ?? 0;
  const ragColor = getRAGColor(compliancePct);

  return (
    <View style={{ flex: 1 }}>
    <ScrollView
      style={styles.root}
      contentContainerStyle={styles.scroll}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />}
    >
      {/* Breadcrumb back to project list + current project name */}
      <TouchableOpacity
        style={styles.breadcrumb}
        onPress={() => router.push('/projects' as any)}
        accessibilityLabel="Back to project list"
      >
        <Text style={styles.breadcrumbChevron}>‹</Text>
        <Text style={styles.breadcrumbProject} numberOfLines={1}>
          {activeProject.code ? `${activeProject.code} — ${activeProject.name}` : activeProject.name}
        </Text>
      </TouchableOpacity>

      {/* Compliance gauge */}
      <View style={styles.gaugeCard}>
        <View style={[styles.gaugeCircle, { borderColor: ragColor }]}>
          <Text style={[styles.gaugePct, { color: ragColor }]}>{Math.round(compliancePct)}%</Text>
          <Text style={styles.gaugeLabel}>Compliance</Text>
        </View>
        {dashboard.compliance && (
          <View style={styles.gaugeDetails}>
            <GaugeDetail label="Tagged" value={`${dashboard.compliance.taggedElements}/${dashboard.compliance.totalElements}`} />
            <GaugeDetail label="Stale" value={String(dashboard.compliance.staleCount)} warn={dashboard.compliance.staleCount > 0} />
            <GaugeDetail label="Warnings" value={String(dashboard.compliance.warningCount)} warn={dashboard.compliance.warningCount > 10} />
          </View>
        )}
      </View>

      {/* KPI row */}
      <View style={styles.kpiRow}>
        <KPICard
          title="Open Issues"
          value={String(dashboard.openIssueCount)}
          color={dashboard.openIssueCount > 5 ? theme.colors.danger : theme.colors.accent}
          onPress={() => router.push('/(tabs)/issues')}
        />
        <KPICard
          title="Documents"
          value={String(dashboard.documentCount)}
          color={theme.colors.primary}
          onPress={() => router.push('/(tabs)/documents')}
        />
      </View>

      {/* Phase 142 — My Actions card. Single high-visibility CTA so a
          BIM/Construction Manager landing on the dashboard sees what's on
          their plate without scrolling through the issue list. Hidden when
          the aggregator query failed (myActionsTotal === null). */}
      {myActionsTotal !== null && (
        <TouchableOpacity
          style={[
            styles.actionCard,
            { borderLeftColor: slaCount > 0 ? theme.colors.danger : theme.colors.accent },
          ]}
          onPress={() => router.push('/inbox' as any)}
          accessibilityLabel={`Open My Actions inbox — ${myActionsTotal} item${myActionsTotal === 1 ? '' : 's'}`}
        >
          <View style={{ flex: 1 }}>
            <Text style={styles.actionTitle}>My Actions</Text>
            <Text style={styles.actionSub}>
              {myActionsTotal === 0
                ? 'Nothing assigned to you right now.'
                : `${myActionsTotal} item${myActionsTotal === 1 ? '' : 's'} waiting on you`}
              {slaCount > 0 ? ` · ${slaCount} SLA breach${slaCount === 1 ? '' : 'es'}` : ''}
            </Text>
          </View>
          <Text style={styles.actionArrow}>›</Text>
        </TouchableOpacity>
      )}

      {/* Phase 143 — BIM Coordinator tile. Two stacked one-liners covering
          model federation freshness + tag-sync conflict backlog. Hidden when
          neither query succeeded so dashboards on projects without models
          stay clean. RAG color is driven by the federation aggregator. */}
      {(federation || pendingConflicts > 0) && (
        <View style={styles.bimCard}>
          <Text style={styles.bimTitle}>BIM Coordination</Text>
          {federation && (
            <TouchableOpacity
              style={styles.bimRow}
              onPress={() => router.push('/(tabs)/models')}
              accessibilityLabel={`Federation status — ${federation.rag}`}
            >
              <View style={[styles.ragDot, { backgroundColor: ragToColor(federation.rag) }]} />
              <View style={{ flex: 1 }}>
                <Text style={styles.bimRowTitle}>
                  Federation: {federation.totals.models} model{federation.totals.models === 1 ? '' : 's'} across {federation.totals.disciplines} discipline{federation.totals.disciplines === 1 ? '' : 's'}
                </Text>
                <Text style={styles.bimRowSub}>
                  {federation.totals.disciplinesWithStale > 0
                    ? `${federation.totals.disciplinesWithStale} discipline${federation.totals.disciplinesWithStale === 1 ? '' : 's'} stale (>${federation.staleDays} days)`
                    : federation.totals.staleModels > 0
                    ? `${federation.totals.staleModels} stale model${federation.totals.staleModels === 1 ? '' : 's'}`
                    : 'All models current'}
                </Text>
              </View>
              <Text style={styles.bimArrow}>›</Text>
            </TouchableOpacity>
          )}
          <TouchableOpacity
            style={styles.bimRow}
            onPress={() => router.push('/conflicts' as any)}
            accessibilityLabel={`Sync conflicts — ${pendingConflicts} pending`}
          >
            <View style={[styles.ragDot, { backgroundColor: pendingConflicts > 0 ? theme.colors.danger : theme.colors.success }]} />
            <View style={{ flex: 1 }}>
              <Text style={styles.bimRowTitle}>
                Sync conflicts: {pendingConflicts} pending
              </Text>
              <Text style={styles.bimRowSub}>
                {pendingConflicts > 0
                  ? 'Tap to triage stale-update collisions'
                  : 'No outstanding stale-update collisions'}
              </Text>
            </View>
            <Text style={styles.bimArrow}>›</Text>
          </TouchableOpacity>
          {/* Phase 144 — Tag heatmap shortcut. Always visible inside the BIM
              Coordination card so the manager sees the full BIM-side menu. */}
          <TouchableOpacity
            style={styles.bimRow}
            onPress={() => router.push('/heatmap' as any)}
            accessibilityLabel="Open tag completeness heatmap"
          >
            <View style={[styles.ragDot, { backgroundColor: theme.colors.accent }]} />
            <View style={{ flex: 1 }}>
              <Text style={styles.bimRowTitle}>Tag completeness heatmap</Text>
              <Text style={styles.bimRowSub}>Per-discipline × per-token completeness grid</Text>
            </View>
            <Text style={styles.bimArrow}>›</Text>
          </TouchableOpacity>
          {/* Phase 144 — Stage gates / MIDP shortcut. */}
          <TouchableOpacity
            style={styles.bimRow}
            onPress={() => router.push('/stages' as any)}
            accessibilityLabel="Open stage gate timeline"
          >
            <View style={[styles.ragDot, { backgroundColor: theme.colors.primary }]} />
            <View style={{ flex: 1 }}>
              <Text style={styles.bimRowTitle}>Stage gates & MIDP</Text>
              <Text style={styles.bimRowSub}>RIBA timeline + information-exchange deliverables</Text>
            </View>
            <Text style={styles.bimArrow}>›</Text>
          </TouchableOpacity>
        </View>
      )}

      {/* Phase 142 — quick-action row for the manager's most-used routines.
          Placed below My Actions and above the discipline breakdown so it's
          one tap from cold-start. Add new entries by appending to the array. */}
      <View style={styles.quickRow}>
        <QuickAction label="Site Diary" emoji="📒" onPress={() => router.push('/diary' as any)} />
        <QuickAction label="Meetings" emoji="📅" onPress={() => router.push('/meetings' as any)} />
        <QuickAction label="Transmittals" emoji="📤" onPress={() => router.push('/transmittals' as any)} />
        <QuickAction label="Warnings" emoji="⚠️" onPress={() => router.push('/warnings' as any)} />
        <QuickAction label="Healthcare" emoji="🏥" onPress={() => router.push('/healthcare' as any)} />
        {/* T3-6 — Punchlist mode entry point. Lives next to Diary/Meetings
            so on-site supervisors find it on the same row of muscle memory. */}
        <QuickAction label="Punchlist" emoji="🎯" onPress={() => router.push('/punchlist' as any)} />
      </View>

      {/* Discipline breakdown */}
      {dashboard.compliance?.byDiscipline && Object.keys(dashboard.compliance.byDiscipline).length > 0 && (
        <View style={styles.sectionCard}>
          <Text style={styles.sectionTitle}>By Discipline</Text>
          {Object.entries(dashboard.compliance.byDiscipline).map(([disc, data]) => (
            <View key={disc} style={styles.discRow}>
              <Text style={styles.discCode}>{disc}</Text>
              <View style={styles.discBarBg}>
                <View style={[styles.discBarFill, { width: `${data.compliancePct}%`, backgroundColor: getRAGColor(data.compliancePct) }]} />
              </View>
              <Text style={styles.discPct}>{Math.round(data.compliancePct)}%</Text>
            </View>
          ))}
        </View>
      )}

      {/* Recent issues */}
      {dashboard.recentIssues.length > 0 && (
        <View style={styles.sectionCard}>
          <View style={styles.sectionHeader}>
            <Text style={styles.sectionTitle}>Recent Issues</Text>
            <TouchableOpacity onPress={() => router.push('/(tabs)/issues')}>
              <Text style={styles.seeAll}>See all</Text>
            </TouchableOpacity>
          </View>
          {dashboard.recentIssues.slice(0, 5).map((issue) => (
            <IssueRow key={issue.id} issue={issue} />
          ))}
        </View>
      )}
    </ScrollView>
    {/* Phase 178 — site-photo capture FAB anchored to dashboard. */}
    <SitePhotoFab />
    </View>
  );
}

function KPICard({ title, value, color, onPress }: { title: string; value: string; color: string; onPress?: () => void }) {
  return (
    <TouchableOpacity style={styles.kpiCard} onPress={onPress} activeOpacity={0.7}>
      <Text style={[styles.kpiValue, { color }]}>{value}</Text>
      <Text style={styles.kpiTitle}>{title}</Text>
    </TouchableOpacity>
  );
}

function ragToColor(rag: 'GREEN' | 'AMBER' | 'RED'): string {
  switch (rag) {
    case 'GREEN': return theme.colors.success;
    case 'AMBER': return theme.colors.warning;
    case 'RED': return theme.colors.danger;
  }
}

function QuickAction({ label, emoji, onPress }: { label: string; emoji: string; onPress: () => void }) {
  return (
    <TouchableOpacity
      style={styles.quickCard}
      onPress={onPress}
      activeOpacity={0.7}
      accessibilityLabel={label}
    >
      <Text style={styles.quickEmoji}>{emoji}</Text>
      <Text style={styles.quickLabel}>{label}</Text>
    </TouchableOpacity>
  );
}

function GaugeDetail({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <View style={styles.gaugeDetailItem}>
      <Text style={[styles.gaugeDetailValue, warn && { color: theme.colors.danger }]}>{value}</Text>
      <Text style={styles.gaugeDetailLabel}>{label}</Text>
    </View>
  );
}

function IssueRow({ issue }: { issue: BimIssue }) {
  const priorityColor = getPriorityColor(issue.priority);
  return (
    <View style={styles.issueRow}>
      <View style={[styles.issuePriorityDot, { backgroundColor: priorityColor }]} />
      <View style={styles.issueContent}>
        <Text style={styles.issueTitle} numberOfLines={1}>{issue.title}</Text>
        <Text style={styles.issueMeta}>{issue.issueCode} · {issue.type} · {issue.status}</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  scroll: {
    padding: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
  },
  center: {
    flex: 1,
    backgroundColor: theme.colors.background,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  loadingText: {
    marginTop: theme.spacing.md,
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
  },
  errorIcon: {
    fontSize: 40,
    fontWeight: '700',
    color: theme.colors.danger,
    width: 64,
    height: 64,
    lineHeight: 64,
    textAlign: 'center',
    borderRadius: 32,
    backgroundColor: '#FFEBEE',
    marginBottom: theme.spacing.md,
    overflow: 'hidden',
  },
  errorText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.danger,
    textAlign: 'center',
    marginBottom: theme.spacing.md,
  },
  retryButton: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.sm,
  },
  retryButtonText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },
  emptyText: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  emptySubtext: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },

  // No-active-project prompt
  noProjectIcon: {
    fontSize: 48,
    marginBottom: theme.spacing.md,
  },
  noProjectTitle: {
    fontSize: theme.fontSize.xl,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  noProjectSub: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
    marginBottom: theme.spacing.lg,
    maxWidth: 280,
  },
  goToProjectsBtn: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.xl,
    paddingVertical: theme.spacing.sm,
  },
  goToProjectsBtnText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },

  // Breadcrumb — active project name + tap to go back to list
  breadcrumb: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: theme.spacing.md,
  },
  breadcrumbChevron: {
    fontSize: theme.fontSize.xl,
    color: theme.colors.accent,
    marginRight: theme.spacing.xs,
    lineHeight: 22,
  },
  breadcrumbProject: {
    flex: 1,
    fontSize: theme.fontSize.sm,
    color: theme.colors.accent,
    fontWeight: '600',
  },

  // Compliance gauge
  gaugeCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.xl,
    padding: theme.spacing.lg,
    alignItems: 'center',
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.08,
    shadowRadius: 8,
    elevation: 3,
  },
  gaugeCircle: {
    width: 120,
    height: 120,
    borderRadius: 60,
    borderWidth: 6,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: theme.spacing.md,
  },
  gaugePct: {
    fontSize: theme.fontSize.hero,
    fontWeight: '700',
  },
  gaugeLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 1,
  },
  gaugeDetails: {
    flexDirection: 'row',
    justifyContent: 'space-around',
    width: '100%',
  },
  gaugeDetailItem: {
    alignItems: 'center',
  },
  gaugeDetailValue: {
    fontSize: theme.fontSize.lg,
    fontWeight: '700',
    color: theme.colors.text,
  },
  gaugeDetailLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },

  // KPI cards
  kpiRow: {
    flexDirection: 'row',
    gap: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  // Phase 142 — My Actions CTA
  actionCard: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    borderLeftWidth: 4,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 1,
  },
  actionTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
  },
  actionSub: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },
  actionArrow: {
    fontSize: theme.fontSize.xl,
    color: theme.colors.textSecondary,
    paddingHorizontal: theme.spacing.sm,
  },
  // Phase 143 — BIM Coordinator tile
  bimCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  bimTitle: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: theme.spacing.sm,
  },
  bimRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
    borderTopWidth: 1,
    borderTopColor: theme.colors.border,
  },
  ragDot: { width: 10, height: 10, borderRadius: 5, marginRight: theme.spacing.sm },
  bimRowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  bimRowSub: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  bimArrow: { fontSize: theme.fontSize.xl, color: theme.colors.textSecondary, paddingHorizontal: theme.spacing.sm },

  // Phase 142 — quick-action row (Site Diary / Meetings / Transmittals / Warnings)
  quickRow: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    marginBottom: theme.spacing.md,
  },
  quickCard: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.md,
    paddingHorizontal: theme.spacing.xs,
    alignItems: 'center',
  },
  quickEmoji: { fontSize: 22 },
  quickLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.text,
    marginTop: 4,
    textAlign: 'center',
  },
  kpiCard: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    alignItems: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  kpiValue: {
    fontSize: theme.fontSize.xxl,
    fontWeight: '700',
  },
  kpiTitle: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginTop: 4,
    fontWeight: '500',
  },

  // Section card
  sectionCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  sectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: theme.spacing.sm,
  },
  sectionTitle: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  seeAll: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.accent,
    fontWeight: '600',
  },

  // Discipline breakdown
  discRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: theme.spacing.sm,
  },
  discCode: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
    width: 36,
  },
  discBarBg: {
    flex: 1,
    height: 8,
    backgroundColor: theme.colors.border,
    borderRadius: 4,
    marginHorizontal: theme.spacing.sm,
    overflow: 'hidden',
  },
  discBarFill: {
    height: 8,
    borderRadius: 4,
  },
  discPct: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    width: 36,
    textAlign: 'right',
  },

  // Issue rows
  issueRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  issuePriorityDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    marginRight: theme.spacing.sm,
  },
  issueContent: {
    flex: 1,
  },
  issueTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '500',
    color: theme.colors.text,
  },
  issueMeta: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },
});
