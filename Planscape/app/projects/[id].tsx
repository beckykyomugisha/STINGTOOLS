// Per-project dashboard. Navigated to from /projects (the project list).
// Shows compliance gauge, KPI row, My Actions, BIM tile, full feature grid,
// and recent issues for a single project.
//
// projectId is extracted from the dynamic route segment via useLocalSearchParams.

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
import { useRouter, useLocalSearchParams } from 'expo-router';
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

export default function ProjectDashboardScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();

  // Read project name/code from the store — set by projects/index.tsx on tap.
  const activeProject = useProjectStore((s) => s.active);

  const [dashboard, setDashboard] = useState<DashboardData | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [myActionsTotal, setMyActionsTotal] = useState<number | null>(null);
  const [slaCount, setSlaCount] = useState<number>(0);
  const [federation, setFederation] = useState<FederationStatus | null>(null);
  const [pendingConflicts, setPendingConflicts] = useState<number>(0);

  const loadData = useCallback(async () => {
    if (!id) return;
    try {
      setError(null);
      const data = await getProjectDashboard(id);
      setDashboard(data);

      try {
        const ma = await getMyActions(id, 1);
        setMyActionsTotal(ma.counts.total);
        setSlaCount(ma.counts.slaBreached);
      } catch {
        setMyActionsTotal(null);
        setSlaCount(0);
      }

      const [fedRes, confRes] = await Promise.allSettled([
        getFederationStatus(id, 14),
        listSyncConflicts(id, { resolution: 'PENDING', pageSize: 1 }),
      ]);
      setFederation(fedRes.status === 'fulfilled' ? fedRes.value : null);
      setPendingConflicts(
        confRes.status === 'fulfilled' ? (confRes.value.summary.pending ?? 0) : 0,
      );
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load project dashboard');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [id]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Refresh My Actions tile when the inbox version bumps.
  const inboxVersion = useInboxStore((s) => s.version);
  useEffect(() => {
    if (!id) return;
    getMyActions(id, 1).then(
      (ma) => { setMyActionsTotal(ma.counts.total); setSlaCount(ma.counts.slaBreached); },
      () => { /* leave previous values */ },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inboxVersion]);

  function onRefresh() {
    setRefreshing(true);
    loadData();
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading project dashboard...</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorBadge}>!</Text>
        <Text style={styles.errorText}>{error}</Text>
        <TouchableOpacity style={styles.retryBtn} onPress={() => { setLoading(true); loadData(); }}>
          <Text style={styles.retryBtnText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  if (!dashboard) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyText}>No data for this project.</Text>
      </View>
    );
  }

  const compliancePct = dashboard.compliance?.compliancePercent ?? 0;
  const ragColor = getRAGColor(compliancePct);

  // Helpers for routing into feature screens. We pass projectId via the
  // shared store (already set in projects/index before navigating here).
  function nav(path: string) {
    router.push(path as any);
  }

  return (
    <View style={{ flex: 1 }}>
      <ScrollView
        style={styles.root}
        contentContainerStyle={styles.scroll}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />
        }
      >
        {/* Breadcrumb back to project list */}
        <TouchableOpacity style={styles.breadcrumb} onPress={() => router.back()}>
          <Text style={styles.breadcrumbChevron}>‹</Text>
          <Text style={styles.breadcrumbText}>Projects</Text>
        </TouchableOpacity>

        {/* Project header */}
        <View style={styles.projectHeader}>
          <Text style={styles.projectName} numberOfLines={2}>
            {activeProject?.name ?? 'Project'}
          </Text>
          <Text style={styles.projectCode}>{activeProject?.code ?? id}</Text>
        </View>

        {/* Compliance gauge */}
        <View style={styles.gaugeCard}>
          <View style={[styles.gaugeCircle, { borderColor: ragColor }]}>
            <Text style={[styles.gaugePct, { color: ragColor }]}>{Math.round(compliancePct)}%</Text>
            <Text style={styles.gaugeLabel}>Compliance</Text>
          </View>
          {dashboard.compliance && (
            <View style={styles.gaugeDetails}>
              <GaugeDetail
                label="Tagged"
                value={`${dashboard.compliance.taggedElements}/${dashboard.compliance.totalElements}`}
              />
              <GaugeDetail
                label="Stale"
                value={String(dashboard.compliance.staleCount)}
                warn={dashboard.compliance.staleCount > 0}
              />
              <GaugeDetail
                label="Warnings"
                value={String(dashboard.compliance.warningCount)}
                warn={dashboard.compliance.warningCount > 10}
              />
            </View>
          )}
        </View>

        {/* KPI row */}
        <View style={styles.kpiRow}>
          <KPICard
            title="Open Issues"
            value={String(dashboard.openIssueCount)}
            color={dashboard.openIssueCount > 5 ? theme.colors.danger : theme.colors.accent}
            onPress={() => nav('/(tabs)/issues')}
          />
          <KPICard
            title="Documents"
            value={String(dashboard.documentCount)}
            color={theme.colors.primary}
            onPress={() => nav('/(tabs)/documents')}
          />
        </View>

        {/* My Actions card */}
        {myActionsTotal !== null && (
          <TouchableOpacity
            style={[
              styles.actionCard,
              { borderLeftColor: slaCount > 0 ? theme.colors.danger : theme.colors.accent },
            ]}
            onPress={() => nav('/inbox')}
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

        {/* BIM Coordination tile */}
        {(federation || pendingConflicts > 0) && (
          <View style={styles.bimCard}>
            <Text style={styles.bimTitle}>BIM Coordination</Text>
            {federation && (
              <TouchableOpacity
                style={styles.bimRow}
                onPress={() => nav('/(tabs)/models')}
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
              onPress={() => nav('/conflicts')}
              accessibilityLabel={`Sync conflicts — ${pendingConflicts} pending`}
            >
              <View style={[styles.ragDot, { backgroundColor: pendingConflicts > 0 ? theme.colors.danger : theme.colors.success }]} />
              <View style={{ flex: 1 }}>
                <Text style={styles.bimRowTitle}>Sync conflicts: {pendingConflicts} pending</Text>
                <Text style={styles.bimRowSub}>
                  {pendingConflicts > 0
                    ? 'Tap to triage stale-update collisions'
                    : 'No outstanding stale-update collisions'}
                </Text>
              </View>
              <Text style={styles.bimArrow}>›</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={styles.bimRow}
              onPress={() => nav('/heatmap')}
              accessibilityLabel="Open tag completeness heatmap"
            >
              <View style={[styles.ragDot, { backgroundColor: theme.colors.accent }]} />
              <View style={{ flex: 1 }}>
                <Text style={styles.bimRowTitle}>Tag completeness heatmap</Text>
                <Text style={styles.bimRowSub}>Per-discipline × per-token completeness grid</Text>
              </View>
              <Text style={styles.bimArrow}>›</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={styles.bimRow}
              onPress={() => nav('/stages')}
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

        {/* Feature navigation grid */}
        <View style={styles.navGridCard}>
          <NavSection title="COORDINATION">
            <NavTile label="Issues" emoji="⚠" onPress={() => nav('/(tabs)/issues')} />
            <NavTile label="Warnings" emoji="🔔" onPress={() => nav('/warnings')} />
            <NavTile label="Clashes" emoji="💥" onPress={() => nav('/clashes')} />
            <NavTile label="Transmittals" emoji="📤" onPress={() => nav('/transmittals')} />
            <NavTile label="Meetings" emoji="📅" onPress={() => nav('/meetings')} />
          </NavSection>

          <NavSection title="SITE">
            <NavTile label="Site Diary" emoji="📒" onPress={() => nav('/diary')} />
            <NavTile label="Site Photos" emoji="📸" onPress={() => nav('/site-photos')} />
            <NavTile label="Punchlist" emoji="🎯" onPress={() => nav('/punchlist')} />
            <NavTile label="Scanner" emoji="📷" onPress={() => nav('/(tabs)/scanner')} />
          </NavSection>

          <NavSection title="DOCUMENTS">
            <NavTile label="Documents" emoji="📄" onPress={() => nav('/(tabs)/documents')} />
            <NavTile label="Deliverables" emoji="📦" onPress={() => nav('/deliverables')} />
            <NavTile label="Workflows" emoji="⚙" onPress={() => nav('/workflows')} />
          </NavSection>

          <NavSection title="MODEL">
            <NavTile label="Models" emoji="🧊" onPress={() => nav('/(tabs)/models')} />
            <NavTile label="Heatmap" emoji="🌡" onPress={() => nav('/heatmap')} />
            <NavTile label="Stages" emoji="🚩" onPress={() => nav('/stages')} />
          </NavSection>

          <NavSection title="ADMIN">
            <NavTile label="Members" emoji="👥" onPress={() => nav('/project-settings')} />
            <NavTile label="Settings" emoji="🔧" onPress={() => nav('/project-settings')} />
          </NavSection>
        </View>

        {/* Discipline breakdown */}
        {dashboard.compliance?.byDiscipline &&
          Object.keys(dashboard.compliance.byDiscipline).length > 0 && (
          <View style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>By Discipline</Text>
            {Object.entries(dashboard.compliance.byDiscipline).map(([disc, data]) => (
              <View key={disc} style={styles.discRow}>
                <Text style={styles.discCode}>{disc}</Text>
                <View style={styles.discBarBg}>
                  <View
                    style={[
                      styles.discBarFill,
                      { width: `${data.compliancePct}%`, backgroundColor: getRAGColor(data.compliancePct) },
                    ]}
                  />
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
              <TouchableOpacity onPress={() => nav('/(tabs)/issues')}>
                <Text style={styles.seeAll}>See all</Text>
              </TouchableOpacity>
            </View>
            {dashboard.recentIssues.slice(0, 5).map((issue) => (
              <IssueRow key={issue.id} issue={issue} />
            ))}
          </View>
        )}
      </ScrollView>

      <SitePhotoFab />
    </View>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

function KPICard({ title, value, color, onPress }: {
  title: string; value: string; color: string; onPress?: () => void;
}) {
  return (
    <TouchableOpacity style={styles.kpiCard} onPress={onPress} activeOpacity={0.7}>
      <Text style={[styles.kpiValue, { color }]}>{value}</Text>
      <Text style={styles.kpiTitle}>{title}</Text>
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

function NavSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={styles.navSection}>
      <Text style={styles.navSectionTitle}>{title}</Text>
      <View style={styles.navTileRow}>{children}</View>
    </View>
  );
}

function NavTile({ label, emoji, onPress }: { label: string; emoji: string; onPress: () => void }) {
  return (
    <TouchableOpacity style={styles.navTile} onPress={onPress} activeOpacity={0.7} accessibilityLabel={label}>
      <Text style={styles.navTileEmoji}>{emoji}</Text>
      <Text style={styles.navTileLabel} numberOfLines={1}>{label}</Text>
    </TouchableOpacity>
  );
}

function ragToColor(rag: 'GREEN' | 'AMBER' | 'RED'): string {
  switch (rag) {
    case 'GREEN': return theme.colors.success;
    case 'AMBER': return theme.colors.warning;
    case 'RED':   return theme.colors.danger;
  }
}

// ── Styles ────────────────────────────────────────────────────────────────────

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
  errorBadge: {
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
  retryBtn: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.sm,
  },
  retryBtnText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },
  emptyText: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
  },

  // Breadcrumb
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
  breadcrumbText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.accent,
    fontWeight: '600',
  },

  // Project header
  projectHeader: {
    marginBottom: theme.spacing.md,
  },
  projectName: {
    fontSize: theme.fontSize.xxl,
    fontWeight: '700',
    color: theme.colors.text,
  },
  projectCode: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginTop: 2,
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

  // KPI row
  kpiRow: {
    flexDirection: 'row',
    gap: theme.spacing.md,
    marginBottom: theme.spacing.md,
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

  // My Actions card
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

  // BIM Coordination tile
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
  bimArrow: {
    fontSize: theme.fontSize.xl,
    color: theme.colors.textSecondary,
    paddingHorizontal: theme.spacing.sm,
  },

  // Feature navigation grid
  navGridCard: {
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
  navSection: {
    marginBottom: theme.spacing.md,
  },
  navSectionTitle: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.8,
    marginBottom: theme.spacing.sm,
  },
  navTileRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
  },
  navTile: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm,
    paddingHorizontal: theme.spacing.md,
    alignItems: 'center',
    minWidth: 72,
  },
  navTileEmoji: {
    fontSize: 22,
  },
  navTileLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.text,
    marginTop: 4,
    textAlign: 'center',
    fontWeight: '500',
  },

  // Section cards
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

  // Recent issues
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
