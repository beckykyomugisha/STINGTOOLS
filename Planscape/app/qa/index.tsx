/**
 * QA Dashboard — BCC QA tab equivalent.
 *
 * Shows compliance summary, per-discipline bars, warning category
 * breakdown, SLA breaches, and a recent-warnings list.
 *
 * Data:
 *   getProjectDashboard(projectId)  — compliance snapshot + recent issues
 *   listWarnings(projectId)         — warning records
 *   listIssues(projectId)           — filtered for open + overdue
 */

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  RefreshControl,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
} from 'react-native';
import { Stack, useRouter } from 'expo-router';
import { theme, getRAGColor } from '@/utils/theme';
import {
  getProjectDashboard,
  listWarnings,
  listIssues,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';
import type { DashboardData, BimIssue } from '@/types/api';
import type { WarningRecord } from '@/types/api';

// ─── Warning category labels ──────────────────────────────────────────────────

const WARNING_LABELS: Record<string, string> = {
  MISSING_TAG: 'Missing Tag',
  MISSING_LEVEL: 'Missing Level',
  MISSING_SYSTEM: 'Missing System',
  MISSING_LOCATION: 'Missing Location',
  MISSING_DISCIPLINE: 'Missing Discipline',
  DUPLICATE_TAG: 'Duplicate Tag',
  INVALID_FORMAT: 'Invalid Format',
  STALE: 'Stale Element',
};

function warnLabel(category: string): string {
  return WARNING_LABELS[category] ?? category.replace(/_/g, ' ');
}

function warnSeverityColor(severity: string): string {
  const s = String(severity || '').toUpperCase();
  if (s === 'CRITICAL' || s === 'ERROR') return theme.colors.danger;
  if (s === 'WARNING' || s === 'WARN') return theme.colors.warning;
  return theme.colors.textSecondary;
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function formatDate(iso: string): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return iso;
  }
}

function daysOverdue(issue: BimIssue): number {
  const createdMs = new Date(issue.createdAt).getTime();
  const ageH = (Date.now() - createdMs) / (1000 * 60 * 60);
  const slaH =
    issue.priority === 'CRITICAL' ? 4
    : issue.priority === 'HIGH' ? 24
    : issue.priority === 'MEDIUM' ? 168
    : 336;
  return Math.max(0, Math.floor((ageH - slaH) / 24));
}

// ─── Screen ──────────────────────────────────────────────────────────────────

export default function QADashboardScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);
  const projectName = useProjectStore((s) => s.active?.name);

  const [dashboard, setDashboard] = useState<DashboardData | null>(null);
  const [warnings, setWarnings] = useState<WarningRecord[]>([]);
  const [slaBreaches, setSlaBreaches] = useState<BimIssue[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Which discipline row was tapped (to highlight it)
  const [selectedDisc, setSelectedDisc] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const [dash, warns, issues] = await Promise.all([
        getProjectDashboard(projectId),
        listWarnings(projectId).catch(() => [] as WarningRecord[]),
        listIssues(projectId).catch(() => [] as BimIssue[]),
      ]);
      setDashboard(dash);
      setWarnings(warns);

      const breaches = issues.filter((i) => {
        if (i.status !== 'OPEN' && i.status !== 'IN_PROGRESS') return false;
        return i.isOverdue || daysOverdue(i) > 0;
      });
      // Sort most overdue first
      breaches.sort((a, b) => daysOverdue(b) - daysOverdue(a));
      setSlaBreaches(breaches.slice(0, 20));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load QA data');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { void load(); }, [load]);

  if (!projectId) {
    return (
      <View style={s.center}>
        <Text style={s.emptyText}>Select a project on the dashboard first.</Text>
      </View>
    );
  }

  if (loading) {
    return (
      <View style={s.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }

  const compliance = dashboard?.compliance;
  const compliancePct = compliance?.compliancePercent ?? 0;
  const ragColor = getRAGColor(compliancePct);

  // Warning categories breakdown
  const warnByCategory: Record<string, number> = {};
  for (const w of warnings) {
    warnByCategory[w.category] = (warnByCategory[w.category] ?? 0) + 1;
  }
  const sortedWarnCategories = Object.entries(warnByCategory).sort((a, b) => b[1] - a[1]);
  const maxWarnCount = sortedWarnCategories[0]?.[1] ?? 1;

  // Per-discipline data
  const byDisc = compliance?.byDiscipline ?? {};
  const discEntries = Object.entries(byDisc).sort((a, b) => a[0].localeCompare(b[0]));

  // Per-discipline open issues (from dashboard recent issues + SLA breaches combined)
  const discIssueCount: Record<string, number> = {};
  for (const iss of (dashboard?.recentIssues ?? [])) {
    if (iss.discipline) {
      discIssueCount[iss.discipline] = (discIssueCount[iss.discipline] ?? 0) + 1;
    }
  }

  // Warning count per discipline (approximate from warning descriptions)
  const discWarnCount: Record<string, number> = {};
  // (warnings don't always carry discipline; best-effort via elementId prefix)

  // Recent 10 warnings
  const recentWarnings = [...warnings]
    .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    .slice(0, 10);

  return (
    <>
      <Stack.Screen
        options={{
          headerShown: true,
          title: 'QA Dashboard',
          headerStyle: { backgroundColor: theme.colors.surface },
          headerTitleStyle: { color: theme.colors.text, fontWeight: '700' },
        }}
      />
      <ScrollView
        style={s.root}
        contentContainerStyle={s.scroll}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => { setRefreshing(true); void load(); }}
            tintColor={theme.colors.accent}
          />
        }
      >
        {error ? (
          <View style={s.errorBox}>
            <Text style={s.errorText}>{error}</Text>
          </View>
        ) : null}

        {/* Project name */}
        {projectName ? (
          <Text style={s.projectName}>{projectName}</Text>
        ) : null}

        {/* ── Compliance summary card ── */}
        <View style={s.card}>
          <Text style={s.cardTitle}>Compliance Summary</Text>
          <View style={s.complianceRow}>
            {/* RAG circle */}
            <View style={[s.ragCircle, { borderColor: ragColor }]}>
              <Text style={[s.ragPct, { color: ragColor }]}>{Math.round(compliancePct)}%</Text>
              <Text style={s.ragLabel}>Overall</Text>
            </View>
            {/* Counts */}
            <View style={s.complianceCounts}>
              <CountCell
                label="Total elements"
                value={compliance?.totalElements ?? 0}
              />
              <CountCell
                label="Tagged"
                value={compliance?.taggedElements ?? 0}
                good
              />
              <CountCell
                label="Stale"
                value={compliance?.staleCount ?? 0}
                bad={!!compliance?.staleCount}
              />
              <CountCell
                label="Warnings"
                value={compliance?.warningCount ?? 0}
                bad={!!compliance?.warningCount && (compliance?.warningCount ?? 0) > 10}
              />
            </View>
          </View>
        </View>

        {/* ── Per-discipline table ── */}
        {discEntries.length > 0 ? (
          <View style={s.card}>
            <Text style={s.cardTitle}>By Discipline</Text>
            {discEntries.map(([disc, data]) => {
              const isSelected = selectedDisc === disc;
              return (
                <TouchableOpacity
                  key={disc}
                  style={[s.discRow, isSelected && s.discRowSelected]}
                  onPress={() => setSelectedDisc(isSelected ? null : disc)}
                  accessibilityLabel={`${disc} compliance ${Math.round(data.compliancePct)}%`}
                >
                  <Text style={s.discCode}>{disc}</Text>
                  <View style={s.discBarWrap}>
                    <View style={s.discBarBg}>
                      <View
                        style={[
                          s.discBarFill,
                          {
                            width: `${data.compliancePct}%` as any,
                            backgroundColor: getRAGColor(data.compliancePct),
                          },
                        ]}
                      />
                    </View>
                  </View>
                  <Text style={s.discPct}>{Math.round(data.compliancePct)}%</Text>
                  <Text style={s.discIssue}>
                    {discIssueCount[disc] ? `${discIssueCount[disc]} iss` : '—'}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        ) : null}

        {/* ── Warning categories breakdown ── */}
        {sortedWarnCategories.length > 0 ? (
          <View style={s.card}>
            <Text style={s.cardTitle}>Warning Categories ({warnings.length} total)</Text>
            {sortedWarnCategories.map(([cat, count]) => {
              const pct = (count / maxWarnCount) * 100;
              return (
                <View key={cat} style={s.warnRow}>
                  <Text style={s.warnCat} numberOfLines={1}>{warnLabel(cat)}</Text>
                  <View style={s.warnBarBg}>
                    <View
                      style={[
                        s.warnBarFill,
                        {
                          width: `${pct}%` as any,
                          backgroundColor: theme.colors.warning,
                        },
                      ]}
                    />
                  </View>
                  <Text style={s.warnCount}>{count}</Text>
                </View>
              );
            })}
          </View>
        ) : (
          <View style={s.card}>
            <Text style={s.cardTitle}>Warning Categories</Text>
            <Text style={s.emptyInCard}>No warnings recorded.</Text>
          </View>
        )}

        {/* ── SLA breaches ── */}
        <View style={s.card}>
          <Text style={s.cardTitle}>
            SLA Breaches ({slaBreaches.length})
          </Text>
          {slaBreaches.length === 0 ? (
            <Text style={s.emptyInCard}>No overdue issues.</Text>
          ) : (
            slaBreaches.map((iss) => {
              const days = daysOverdue(iss);
              return (
                <TouchableOpacity
                  key={iss.id}
                  style={s.slaRow}
                  onPress={() =>
                    router.push(`/(tabs)/issue-detail?id=${iss.id}&projectId=${projectId}` as any)
                  }
                  accessibilityLabel={`Open issue ${iss.issueCode}`}
                >
                  <View style={[s.slaDot, { backgroundColor: theme.colors.danger }]} />
                  <View style={{ flex: 1 }}>
                    <Text style={s.slaTitle} numberOfLines={1}>{iss.title}</Text>
                    <Text style={s.slaMeta}>
                      {iss.issueCode} · {iss.assignee || 'Unassigned'} · {days}d overdue
                    </Text>
                  </View>
                  <Text style={s.slaArrow}>›</Text>
                </TouchableOpacity>
              );
            })
          )}
        </View>

        {/* ── Recent warnings list ── */}
        <View style={s.card}>
          <Text style={s.cardTitle}>Recent Warnings</Text>
          {recentWarnings.length === 0 ? (
            <Text style={s.emptyInCard}>No recent warnings.</Text>
          ) : (
            recentWarnings.map((w) => (
              <View key={w.id} style={s.recentWarnRow}>
                <View
                  style={[
                    s.warnSeverityChip,
                    { backgroundColor: warnSeverityColor(w.severity) + '22' },
                  ]}
                >
                  <Text
                    style={[s.warnSeverityText, { color: warnSeverityColor(w.severity) }]}
                  >
                    {w.severity || 'WARN'}
                  </Text>
                </View>
                <View style={{ flex: 1, minWidth: 0 }}>
                  <Text style={s.warnDesc} numberOfLines={2}>{w.description}</Text>
                  <Text style={s.warnDate}>{warnLabel(w.category)} · {formatDate(w.createdAt)}</Text>
                </View>
              </View>
            ))
          )}
        </View>
      </ScrollView>
    </>
  );
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function CountCell({
  label, value, good, bad,
}: {
  label: string;
  value: number;
  good?: boolean;
  bad?: boolean;
}) {
  const color = bad ? theme.colors.danger : good ? theme.colors.success : theme.colors.text;
  return (
    <View style={s.countCell}>
      <Text style={[s.countValue, { color }]}>{value}</Text>
      <Text style={s.countLabel}>{label}</Text>
    </View>
  );
}

// ─── Styles ───────────────────────────────────────────────────────────────────

const s = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
    backgroundColor: theme.colors.background,
  },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  errorBox: {
    backgroundColor: '#FFEBEE',
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.md,
  },
  errorText: { color: theme.colors.danger, fontSize: theme.fontSize.sm },
  projectName: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.sm,
  },

  card: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  cardTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.md,
  },

  // Compliance summary
  complianceRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.md,
  },
  ragCircle: {
    width: 90,
    height: 90,
    borderRadius: 45,
    borderWidth: 5,
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
  },
  ragPct: { fontSize: theme.fontSize.xl, fontWeight: '700' },
  ragLabel: {
    fontSize: 10,
    color: theme.colors.textSecondary,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  complianceCounts: {
    flex: 1,
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
  },
  countCell: { width: '45%' },
  countValue: {
    fontSize: theme.fontSize.lg,
    fontWeight: '700',
  },
  countLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 1,
  },

  // Discipline table
  discRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.xs + 2,
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: 4,
  },
  discRowSelected: {
    backgroundColor: theme.colors.accent + '18',
  },
  discCode: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.text,
    width: 32,
  },
  discBarWrap: { flex: 1, marginHorizontal: theme.spacing.sm },
  discBarBg: {
    height: 8,
    borderRadius: 4,
    backgroundColor: theme.colors.border,
    overflow: 'hidden',
  },
  discBarFill: { height: 8, borderRadius: 4 },
  discPct: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    width: 38,
    textAlign: 'right',
  },
  discIssue: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
    width: 44,
    textAlign: 'right',
  },

  // Warning categories
  warnRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: theme.spacing.sm,
    gap: theme.spacing.sm,
  },
  warnCat: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.text,
    width: 110,
    flexShrink: 0,
  },
  warnBarBg: {
    flex: 1,
    height: 8,
    borderRadius: 4,
    backgroundColor: theme.colors.border,
    overflow: 'hidden',
  },
  warnBarFill: { height: 8, borderRadius: 4 },
  warnCount: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.text,
    width: 28,
    textAlign: 'right',
  },

  // SLA breaches
  slaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
    gap: theme.spacing.sm,
  },
  slaDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    flexShrink: 0,
  },
  slaTitle: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
  },
  slaMeta: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },
  slaArrow: {
    fontSize: theme.fontSize.xl,
    color: theme.colors.textSecondary,
    paddingHorizontal: theme.spacing.xs,
  },

  // Recent warnings
  recentWarnRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
    gap: theme.spacing.sm,
  },
  warnSeverityChip: {
    paddingHorizontal: 7,
    paddingVertical: 2,
    borderRadius: 8,
    flexShrink: 0,
    alignSelf: 'flex-start',
  },
  warnSeverityText: { fontSize: 10, fontWeight: '700' },
  warnDesc: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    marginBottom: 2,
  },
  warnDate: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },
  emptyInCard: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    fontStyle: 'italic',
  },
});
