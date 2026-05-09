// T3-16 — Cross-project My Actions inbox.
//
// Aggregates every commitment assigned to the current user across *all*
// projects they're a member of: overdue / SLA-breached issues, pending
// document approvals, pending site-photo reviews (T3-4), and meeting
// actions. Per-project getMyActions is fanned out client-side; if the
// server later exposes a unified endpoint we can swap the fan-out for a
// single call without touching the UI.
//
// Refresh:
//  - pull-to-refresh
//  - auto-refresh every 5 min while the screen is mounted

import { useCallback, useEffect, useRef, useState } from 'react';
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
import {
  getMyActions,
  listProjects,
  listSitePhotos,
  type MyActionsPayload,
} from '@/api/endpoints';
import type { Project } from '@/types/api';
import { useProjectStore } from '@/stores/projectStore';

const REFRESH_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes

interface ProjectBucket {
  project: Project;
  actions: MyActionsPayload | null;
  pendingPhotoReviews: number;
  error?: string;
}

export default function AllProjectsInboxScreen() {
  const router = useRouter();
  const setActiveProject = useProjectStore((s) => s.setActive);

  const [buckets, setBuckets] = useState<ProjectBucket[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [generatedAt, setGeneratedAt] = useState<string | null>(null);

  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    try {
      setError(null);
      const projects = await listProjects();
      const results = await Promise.all(
        projects.map(async (p): Promise<ProjectBucket> => {
          try {
            const actions = await getMyActions(p.id, 25);
            // Pending site-photo reviews aren't in MyActions yet — query the
            // photos endpoint with audience=PendingReview as a small sidecar.
            let pendingPhotoReviews = 0;
            try {
              const photos = await listSitePhotos(p.id, { audience: 'PendingReview', pageSize: 1 });
              pendingPhotoReviews = photos.total;
            } catch { /* gracefully ignore — user may lack approver perms */ }
            return { project: p, actions, pendingPhotoReviews };
          } catch (err: unknown) {
            return {
              project: p,
              actions: null,
              pendingPhotoReviews: 0,
              error: err instanceof Error ? err.message : 'Failed to load',
            };
          }
        }),
      );
      // Hide projects with zero pending work to keep the screen tidy.
      const withWork = results.filter((b) =>
        bucketTotal(b) > 0 || (b.error && true),
      );
      setBuckets(withWork);
      setGeneratedAt(new Date().toISOString());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load inbox');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  // Auto-refresh every 5 min while the screen is visible.
  useEffect(() => {
    intervalRef.current = setInterval(() => { void load(); }, REFRESH_INTERVAL_MS);
    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, [load]);

  // Tap a row → set active project + deep-link to its tab.
  const goTo = (bucket: ProjectBucket, path: string) => {
    setActiveProject({
      id: bucket.project.id,
      name: bucket.project.name,
      code: bucket.project.code,
    });
    router.push(path as never);
  };

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
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}
    >
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {buckets.length === 0 ? (
        <View style={styles.emptyCard}>
          <Text style={styles.emptyTitle}>You're all caught up</Text>
          <Text style={styles.emptyHint}>
            Nothing assigned to you across your projects right now.
          </Text>
        </View>
      ) : null}

      {buckets.map((b) => {
        const a = b.actions;
        const isOpen = expanded[b.project.id] ?? true;
        const total = bucketTotal(b);

        return (
          <View key={b.project.id} style={styles.section}>
            <TouchableOpacity
              style={styles.sectionHeader}
              onPress={() => setExpanded((e) => ({ ...e, [b.project.id]: !isOpen }))}
              accessibilityLabel={`${isOpen ? 'Collapse' : 'Expand'} ${b.project.name}`}
            >
              <Text style={styles.sectionTitle}>{b.project.name}</Text>
              <View style={styles.badgeRow}>
                <Badge label="Issues" value={a?.counts.issues ?? 0} color={theme.colors.priorityHigh} />
                <Badge label="Actions" value={a?.counts.actions ?? 0} color={theme.colors.accent} />
                <Badge label="Approvals" value={a?.counts.approvals ?? 0} color={theme.colors.priorityMedium} />
                <Badge label="Photos" value={b.pendingPhotoReviews} color={theme.colors.accent} />
                <Badge label="SLA" value={a?.counts.slaBreached ?? 0} color={theme.colors.danger} />
              </View>
              <Text style={styles.chevron}>{isOpen ? '▾' : '▸'}</Text>
            </TouchableOpacity>

            {b.error ? <Text style={styles.errorInline}>{b.error}</Text> : null}

            {isOpen ? (
              <View style={styles.sectionBody}>
                {/* Issues */}
                {a?.issues.length ? (
                  <SubSection
                    label="Issues assigned to me"
                    onSeeAll={() => goTo(b, '/(tabs)/issues')}
                  >
                    {a.issues.slice(0, 5).map((it) => (
                      <TouchableOpacity
                        key={it.id}
                        style={styles.row}
                        onPress={() => goTo(b, `/issue-detail?id=${it.id}`)}
                        accessibilityLabel={`Open issue ${it.issueCode}`}
                      >
                        <View style={[styles.dot, { backgroundColor: getPriorityColor(it.priority) }]} />
                        <View style={styles.rowBody}>
                          <Text style={styles.rowTitle} numberOfLines={1}>
                            {it.issueCode} — {it.title}
                          </Text>
                          <Text style={styles.rowMeta}>
                            {it.type} · {it.priority} · {it.status}
                            {it.dueDate ? ` · due ${formatDate(it.dueDate)}` : ''}
                          </Text>
                        </View>
                      </TouchableOpacity>
                    ))}
                  </SubSection>
                ) : null}

                {/* Approvals */}
                {a?.approvals.length ? (
                  <SubSection
                    label="Pending document approvals"
                    onSeeAll={() => goTo(b, '/inbox/approvals')}
                  >
                    {a.approvals.slice(0, 5).map((ap) => (
                      <TouchableOpacity
                        key={ap.id}
                        style={styles.row}
                        onPress={() => goTo(b, '/inbox/approvals')}
                        accessibilityLabel={`Approve or reject ${ap.fileName}`}
                      >
                        <View style={[styles.dot, { backgroundColor: theme.colors.priorityMedium }]} />
                        <View style={styles.rowBody}>
                          <Text style={styles.rowTitle} numberOfLines={1}>{ap.fileName}</Text>
                          <Text style={styles.rowMeta}>
                            {ap.transition}
                            {ap.discipline ? ` · ${ap.discipline}` : ''} · {formatDate(ap.requestedAt)}
                          </Text>
                        </View>
                      </TouchableOpacity>
                    ))}
                  </SubSection>
                ) : null}

                {/* Pending photo reviews */}
                {b.pendingPhotoReviews > 0 ? (
                  <SubSection label="Pending photo reviews">
                    <TouchableOpacity
                      style={styles.row}
                      onPress={() => goTo(b, '/site-photos/review')}
                      accessibilityLabel="Open photo review queue"
                    >
                      <View style={[styles.dot, { backgroundColor: theme.colors.accent }]} />
                      <View style={styles.rowBody}>
                        <Text style={styles.rowTitle}>{b.pendingPhotoReviews} photo{b.pendingPhotoReviews === 1 ? '' : 's'} awaiting review</Text>
                        <Text style={styles.rowMeta}>Tap to open the review queue</Text>
                      </View>
                    </TouchableOpacity>
                  </SubSection>
                ) : null}

                {/* Meeting actions */}
                {a?.actions.length ? (
                  <SubSection label="Meeting actions">
                    {a.actions.slice(0, 5).map((act) => (
                      <TouchableOpacity
                        key={act.id}
                        style={styles.row}
                        onPress={() => goTo(b, `/meetings/${act.meetingId}`)}
                        accessibilityLabel={`Open meeting ${act.meetingTitle}`}
                      >
                        <View style={[styles.dot, { backgroundColor: theme.colors.accent }]} />
                        <View style={styles.rowBody}>
                          <Text style={styles.rowTitle} numberOfLines={1}>{act.description}</Text>
                          <Text style={styles.rowMeta}>
                            {act.meetingType} · {act.status}
                            {act.dueDate ? ` · due ${formatDate(act.dueDate)}` : ''}
                          </Text>
                        </View>
                      </TouchableOpacity>
                    ))}
                  </SubSection>
                ) : null}

                {/* SLA breached */}
                {a?.slaBreached.length ? (
                  <SubSection label="SLA breached on the team">
                    {a.slaBreached.slice(0, 5).map((it) => (
                      <TouchableOpacity
                        key={it.id}
                        style={styles.row}
                        onPress={() => goTo(b, `/issue-detail?id=${it.id}`)}
                        accessibilityLabel={`Open SLA-breached issue ${it.issueCode}`}
                      >
                        <View style={[styles.dot, { backgroundColor: theme.colors.danger }]} />
                        <View style={styles.rowBody}>
                          <Text style={styles.rowTitle} numberOfLines={1}>
                            {it.issueCode} — {it.title}
                          </Text>
                          <Text style={styles.rowMeta}>
                            {it.priority} · {it.status} · breached {it.breachHours} h
                          </Text>
                        </View>
                      </TouchableOpacity>
                    ))}
                  </SubSection>
                ) : null}

                {total === 0 && !b.error ? (
                  <Text style={styles.emptySection}>Nothing here.</Text>
                ) : null}
              </View>
            ) : null}
          </View>
        );
      })}

      {generatedAt ? (
        <Text style={styles.footer}>Updated {formatTime(generatedAt)} · auto-refreshes every 5 minutes</Text>
      ) : null}
    </ScrollView>
  );
}

function bucketTotal(b: ProjectBucket): number {
  const a = b.actions;
  if (!a) return b.pendingPhotoReviews;
  return a.counts.total + b.pendingPhotoReviews;
}

function Badge({ label, value, color }: { label: string; value: number; color: string }) {
  if (value <= 0) return null;
  return (
    <View style={[styles.badge, { borderColor: color }]}>
      <Text style={[styles.badgeValue, { color }]}>{value}</Text>
      <Text style={styles.badgeLabel}>{label}</Text>
    </View>
  );
}

function SubSection({
  label, onSeeAll, children,
}: { label: string; onSeeAll?: () => void; children: React.ReactNode }) {
  return (
    <View style={styles.subSection}>
      <View style={styles.subSectionHeader}>
        <Text style={styles.subSectionLabel}>{label}</Text>
        {onSeeAll ? (
          <TouchableOpacity onPress={onSeeAll}>
            <Text style={styles.subSectionLink}>See all ›</Text>
          </TouchableOpacity>
        ) : null}
      </View>
      {children}
    </View>
  );
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}
function formatTime(iso: string): string {
  try { return new Date(iso).toLocaleTimeString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  errorInline: {
    color: theme.colors.danger,
    fontSize: theme.fontSize.xs,
    paddingHorizontal: theme.spacing.md,
    paddingBottom: theme.spacing.sm,
  },

  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg,
    alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text, marginBottom: 4 },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, textAlign: 'center' },
  emptySection: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, fontStyle: 'italic', padding: theme.spacing.sm },

  section: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    marginBottom: theme.spacing.md,
    overflow: 'hidden',
  },
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: theme.spacing.md,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  sectionTitle: { fontSize: theme.fontSize.md, fontWeight: '700', color: theme.colors.text, flexShrink: 1 },
  chevron: { fontSize: 18, color: theme.colors.textSecondary, marginLeft: theme.spacing.sm },
  badgeRow: { flexDirection: 'row', flex: 1, justifyContent: 'flex-end', flexWrap: 'wrap' },
  badge: {
    borderWidth: 1,
    borderRadius: 12,
    paddingHorizontal: 8,
    paddingVertical: 2,
    marginLeft: 4,
    marginVertical: 2,
    flexDirection: 'row',
    alignItems: 'center',
  },
  badgeValue: { fontSize: theme.fontSize.sm, fontWeight: '700', marginRight: 4 },
  badgeLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary },

  sectionBody: { padding: theme.spacing.md },
  subSection: { marginBottom: theme.spacing.sm },
  subSectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    marginBottom: 4,
  },
  subSectionLabel: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  subSectionLink: { fontSize: theme.fontSize.xs, color: theme.colors.accent, fontWeight: '600' },

  row: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  dot: { width: 10, height: 10, borderRadius: 5, marginRight: theme.spacing.sm },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '500' },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },

  footer: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
    textAlign: 'center',
    marginTop: theme.spacing.sm,
  },
});
