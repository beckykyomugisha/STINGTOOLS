// Phase 142 — Site Diary list.
//
// Reverse-chronological feed of every diary entry on the active project,
// grouped by status. Tap to view detail; FAB opens the new-entry form.
//
// Phase 178 — left navigation rail added. A 60px vertical strip on the left
// gives one-tap access to the main coordination sections without going back
// to the dashboard. Active item gets an orange accent background.

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
import { useRouter, usePathname } from 'expo-router';
import { theme } from '@/utils/theme';
import { listSiteDiaries, type SiteDiarySummary } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';
import { SitePhotoFab } from '@/components/SitePhotoFab';

// ── Left navigation rail ───────────────────────────────────────────────
// Coordination sections reachable from the diary without going back to tabs.
const navItems = [
  { label: 'Diary',   icon: '📒', route: '/diary' },
  { label: 'Issues',  icon: '⚠',  route: '/(tabs)/issues' },
  { label: 'Meetings',icon: '📅', route: '/meetings' },
  { label: 'Transmit',icon: '📤', route: '/transmittals' },
  { label: 'Warnings',icon: '⚠️', route: '/warnings' },
  { label: 'Docs',    icon: '📄', route: '/(tabs)/documents' },
  { label: 'Models',  icon: '🧊', route: '/(tabs)/models' },
] as const;

function LeftNavRail({ currentPath }: { currentPath: string }) {
  const router = useRouter();
  return (
    <View style={railStyles.rail}>
      {navItems.map((item) => {
        const isActive = currentPath === item.route || currentPath.startsWith(item.route.replace('/(tabs)/', '/'));
        return (
          <TouchableOpacity
            key={item.route}
            style={[railStyles.item, isActive && railStyles.itemActive]}
            onPress={() => router.push(item.route as any)}
            accessibilityLabel={`Go to ${item.label}`}
          >
            <Text style={railStyles.icon}>{item.icon}</Text>
            <Text style={[railStyles.label, isActive && railStyles.labelActive]}>
              {item.label}
            </Text>
          </TouchableOpacity>
        );
      })}
    </View>
  );
}

const railStyles = StyleSheet.create({
  rail: {
    width: 60,
    backgroundColor: theme.colors.primary,
    paddingTop: theme.spacing.md,
    paddingBottom: theme.spacing.lg,
    alignItems: 'center',
  },
  item: {
    width: 56,
    paddingVertical: theme.spacing.sm,
    alignItems: 'center',
    borderRadius: theme.borderRadius.sm,
    marginBottom: 2,
  },
  itemActive: {
    backgroundColor: theme.colors.accent,
  },
  icon: {
    fontSize: 18,
    lineHeight: 22,
  },
  label: {
    fontSize: 8,
    color: 'rgba(255,255,255,0.7)',
    marginTop: 2,
    textAlign: 'center',
  },
  labelActive: {
    color: '#fff',
    fontWeight: '700',
  },
});

export default function DiaryListScreen() {
  const router = useRouter();
  const pathname = usePathname();
  const projectId = useProjectStore((s) => s.active?.id);
  const [rows, setRows] = useState<SiteDiarySummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const res = await listSiteDiaries(projectId, { pageSize: 50 });
      setRows(res.rows);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load diaries');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  if (!projectId) {
    return (
      <View style={styles.rowLayout}>
        <LeftNavRail currentPath={pathname} />
        <View style={styles.empty}>
          <Text style={styles.emptyText}>Select a project on the dashboard first.</Text>
        </View>
      </View>
    );
  }
  if (loading) {
    return (
      <View style={styles.rowLayout}>
        <LeftNavRail currentPath={pathname} />
        <View style={styles.loading}>
          <ActivityIndicator size="large" color={theme.colors.accent} />
        </View>
      </View>
    );
  }

  return (
    <View style={styles.rowLayout}>
      <LeftNavRail currentPath={pathname} />
      <View style={styles.root}>
      <ScrollView
        contentContainerStyle={styles.scroll}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />
        }
      >
        {error ? <Text style={styles.error}>{error}</Text> : null}

        {rows.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>No diary entries yet</Text>
            <Text style={styles.emptyHint}>Tap the + button to record today's report.</Text>
          </View>
        ) : (
          rows.map((d) => (
            <TouchableOpacity
              key={d.id}
              style={styles.row}
              onPress={() => router.push(`/diary/${d.id}` as any)}
              accessibilityLabel={`Open diary for ${d.diaryDate}`}
            >
              <View style={[styles.statusPill, { backgroundColor: statusColor(d.status) }]}>
                <Text style={styles.statusText}>{d.status}</Text>
              </View>
              <View style={styles.rowBody}>
                <Text style={styles.rowTitle}>
                  {formatDate(d.diaryDate)}
                </Text>
                <Text style={styles.rowMeta}>
                  {d.authorName}
                  {d.authorRole ? ` · ${d.authorRole}` : ''}
                  {' · '}{d.manpowerCount} on site
                  {d.weather ? ` · ${d.weather}` : ''}
                  {d.attachmentCount > 0 ? ` · 📷 ${d.attachmentCount}` : ''}
                </Text>
              </View>
            </TouchableOpacity>
          ))
        )}
      </ScrollView>

      <TouchableOpacity
        style={styles.fab}
        onPress={() => router.push('/diary/new' as any)}
        accessibilityLabel="New diary entry"
      >
        <Text style={styles.fabPlus}>＋</Text>
      </TouchableOpacity>

      {/* Phase 178 — site-photo FAB stacked above the diary FAB so site
          supervisors can capture progress shots straight from the diary list. */}
      <SitePhotoFab bottom={theme.spacing.lg + 72} />
      </View>
    </View>
  );
}

function statusColor(status: SiteDiarySummary['status']): string {
  switch (status) {
    case 'DRAFT': return theme.colors.disabled;
    case 'SUBMITTED': return theme.colors.accent;
    case 'ACKNOWLEDGED': return theme.colors.success;
    case 'ARCHIVED': return theme.colors.textSecondary;
    default: return theme.colors.disabled;
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  rowLayout: { flex: 1, flexDirection: 'row', backgroundColor: theme.colors.primary },
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 96 },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center', backgroundColor: theme.colors.background },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg, backgroundColor: theme.colors.background },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg,
    alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4 },
  error: {
    backgroundColor: '#FFEBEE',
    color: theme.colors.danger,
    padding: theme.spacing.sm,
    borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  statusPill: {
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
    marginRight: theme.spacing.md,
  },
  statusText: {
    color: '#fff',
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
  },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.md, color: theme.colors.text, fontWeight: '500' },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  fab: {
    position: 'absolute',
    right: theme.spacing.lg,
    bottom: theme.spacing.lg,
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: theme.colors.accent,
    justifyContent: 'center',
    alignItems: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.25,
    shadowRadius: 8,
    elevation: 6,
  },
  fabPlus: { color: '#fff', fontSize: 28, fontWeight: '300', lineHeight: 30 },
});
