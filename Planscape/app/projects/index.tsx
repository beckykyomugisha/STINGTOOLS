// Projects home screen — corporate list/grid view of all assigned projects.
// Replaces the horizontal chip bar from the old dashboard. Tapping a row or
// card navigates to the per-project dashboard at /projects/[id].

import { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  FlatList,
  TouchableOpacity,
  RefreshControl,
  TextInput,
  ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme, getRAGColor } from '@/utils/theme';
import { listProjects } from '@/api/endpoints';
import type { Project } from '@/types/api';
import { useProjectStore } from '@/stores/projectStore';

type ViewMode = 'list' | 'grid';

// Minimal RAG % from project — real data comes from dashboard endpoint.
// We don't fetch a full dashboard per-row here (expensive); compliance is
// surfaced once you open a project. RAG dot on the row is placeholder-driven
// until the project store has a cached value from a prior visit.
function ragDotColor(projectId: string, recent: Array<{ id: string }>): string {
  // If this is a recently-visited project we have no compliance cached here
  // (compliance lives in DashboardData fetched per-project); default to grey.
  return theme.colors.disabled;
}

export default function ProjectsScreen() {
  const router = useRouter();
  const setActiveInStore = useProjectStore((s) => s.setActive);

  const [projects, setProjects] = useState<Project[]>([]);
  const [filtered, setFiltered] = useState<Project[]>([]);
  const [query, setQuery] = useState('');
  const [mode, setMode] = useState<ViewMode>('list');
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (force = false) => {
    try {
      setError(null);
      const list = await listProjects(force);
      setProjects(list);
      setFiltered(filterList(list, query));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load projects');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => { load(); }, [load]);

  function filterList(list: Project[], q: string): Project[] {
    if (!q.trim()) return list;
    const lower = q.toLowerCase();
    return list.filter(
      (p) =>
        p.name.toLowerCase().includes(lower) ||
        (p.code ?? '').toLowerCase().includes(lower),
    );
  }

  function onSearch(text: string) {
    setQuery(text);
    setFiltered(filterList(projects, text));
  }

  function onRefresh() {
    setRefreshing(true);
    load(true);
  }

  function openProject(project: Project) {
    setActiveInStore({ id: project.id, name: project.name, code: project.code });
    router.push(`/projects/${project.id}` as any);
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading projects...</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorBadge}>!</Text>
        <Text style={styles.errorText}>{error}</Text>
        <TouchableOpacity style={styles.retryBtn} onPress={() => { setLoading(true); load(true); }}>
          <Text style={styles.retryBtnText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  if (projects.length === 0) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyIcon}>🏗</Text>
        <Text style={styles.emptyTitle}>No projects assigned</Text>
        <Text style={styles.emptySub}>Contact your BIM Manager to be added to a project.</Text>
      </View>
    );
  }

  return (
    <View style={styles.root}>
      {/* Search bar */}
      <View style={styles.searchRow}>
        <View style={styles.searchBox}>
          <Text style={styles.searchIcon}>🔍</Text>
          <TextInput
            style={styles.searchInput}
            placeholder="Search by name or code…"
            placeholderTextColor={theme.colors.disabled}
            value={query}
            onChangeText={onSearch}
            autoCapitalize="none"
            clearButtonMode="while-editing"
          />
        </View>
        <TouchableOpacity
          style={styles.toggleBtn}
          onPress={() => setMode((m) => (m === 'list' ? 'grid' : 'list'))}
          accessibilityLabel={mode === 'list' ? 'Switch to grid view' : 'Switch to list view'}
        >
          <Text style={styles.toggleIcon}>{mode === 'list' ? '⊞' : '☰'}</Text>
        </TouchableOpacity>
      </View>

      {/* Count */}
      <Text style={styles.countText}>
        {filtered.length} project{filtered.length === 1 ? '' : 's'}
        {query ? ` matching "${query}"` : ''}
      </Text>

      {mode === 'list' ? (
        <FlatList
          data={filtered}
          keyExtractor={(p) => p.id}
          refreshControl={
            <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />
          }
          contentContainerStyle={styles.listContent}
          ItemSeparatorComponent={() => <View style={styles.separator} />}
          renderItem={({ item }) => (
            <ProjectListRow project={item} onPress={() => openProject(item)} />
          )}
          ListEmptyComponent={
            <View style={styles.center}>
              <Text style={styles.emptySub}>No projects match your search.</Text>
            </View>
          }
        />
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(p) => p.id}
          numColumns={2}
          refreshControl={
            <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />
          }
          contentContainerStyle={styles.gridContent}
          columnWrapperStyle={styles.gridRow}
          renderItem={({ item }) => (
            <ProjectGridCard project={item} onPress={() => openProject(item)} />
          )}
          ListEmptyComponent={
            <View style={styles.center}>
              <Text style={styles.emptySub}>No projects match your search.</Text>
            </View>
          }
        />
      )}
    </View>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────

function ProjectListRow({ project, onPress }: { project: Project; onPress: () => void }) {
  // We don't carry live compliance here; grey dot until project dashboard is loaded.
  const ragColor = theme.colors.disabled;

  return (
    <TouchableOpacity style={styles.listRow} onPress={onPress} activeOpacity={0.7}>
      {/* RAG left border color — grey until we have compliance from a visit */}
      <View style={[styles.listRowBorder, { backgroundColor: ragColor }]} />

      {/* Project code badge */}
      <View style={styles.codeBadge}>
        <Text style={styles.codeBadgeText} numberOfLines={1}>
          {project.code || '—'}
        </Text>
      </View>

      {/* Name + meta */}
      <View style={styles.listRowBody}>
        <Text style={styles.listRowName} numberOfLines={2}>{project.name}</Text>
        <Text style={styles.listRowSub} numberOfLines={1}>
          {project.description
            ? project.description.slice(0, 60)
            : 'Tap to open project dashboard'}
        </Text>
      </View>

      {/* Right side: RAG dot + arrow */}
      <View style={styles.listRowRight}>
        <View style={[styles.ragDot, { backgroundColor: ragColor }]} />
        <Text style={styles.listRowArrow}>›</Text>
      </View>
    </TouchableOpacity>
  );
}

function ProjectGridCard({ project, onPress }: { project: Project; onPress: () => void }) {
  const ragColor = theme.colors.disabled;

  return (
    <TouchableOpacity style={styles.gridCard} onPress={onPress} activeOpacity={0.7}>
      {/* RAG top border */}
      <View style={[styles.gridCardBorder, { backgroundColor: ragColor }]} />

      {/* Compliance circle (placeholder — shows "—" until dashboard fetched) */}
      <View style={[styles.gaugeCircle, { borderColor: ragColor }]}>
        <Text style={[styles.gaugePct, { color: ragColor }]}>—</Text>
      </View>

      {/* Project code (small) */}
      <Text style={styles.gridCardCode} numberOfLines={1}>
        {project.code || '—'}
      </Text>

      {/* Project name (bold, truncated) */}
      <Text style={styles.gridCardName} numberOfLines={2}>
        {project.name}
      </Text>
    </TouchableOpacity>
  );
}

// ── Styles ────────────────────────────────────────────────────────────────

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  center: {
    flex: 1,
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
  emptyIcon: {
    fontSize: 48,
    marginBottom: theme.spacing.md,
  },
  emptyTitle: {
    fontSize: theme.fontSize.xl,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  emptySub: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },

  // Search bar
  searchRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: theme.spacing.md,
    paddingTop: theme.spacing.md,
    paddingBottom: theme.spacing.sm,
    gap: theme.spacing.sm,
  },
  searchBox: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.sm,
    height: 40,
  },
  searchIcon: {
    fontSize: 16,
    marginRight: theme.spacing.xs,
  },
  searchInput: {
    flex: 1,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
    height: 40,
  },
  toggleBtn: {
    width: 40,
    height: 40,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    borderWidth: 1,
    borderColor: theme.colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  toggleIcon: {
    fontSize: 18,
    color: theme.colors.primary,
  },

  // Count label
  countText: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    paddingHorizontal: theme.spacing.md,
    paddingBottom: theme.spacing.xs,
  },

  // List view
  listContent: {
    paddingHorizontal: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
  },
  separator: {
    height: theme.spacing.sm,
  },
  listRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    overflow: 'hidden',
    minHeight: 72,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  listRowBorder: {
    width: 5,
    alignSelf: 'stretch',
  },
  codeBadge: {
    backgroundColor: theme.colors.primary,
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.xs,
    marginHorizontal: theme.spacing.sm,
    minWidth: 52,
    maxWidth: 80,
    alignItems: 'center',
  },
  codeBadgeText: {
    fontFamily: 'monospace' as any,
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.surface,
    textTransform: 'uppercase',
  },
  listRowBody: {
    flex: 1,
    paddingVertical: theme.spacing.sm,
    paddingRight: theme.spacing.sm,
  },
  listRowName: {
    fontSize: theme.fontSize.md,
    fontWeight: '700',
    color: theme.colors.text,
  },
  listRowSub: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },
  listRowRight: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingRight: theme.spacing.sm,
    gap: theme.spacing.xs,
  },
  ragDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
  },
  listRowArrow: {
    fontSize: theme.fontSize.xl,
    color: theme.colors.textSecondary,
  },

  // Grid view
  gridContent: {
    paddingHorizontal: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
  },
  gridRow: {
    gap: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  gridCard: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    overflow: 'hidden',
    padding: theme.spacing.md,
    alignItems: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  gridCardBorder: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    height: 4,
  },
  gaugeCircle: {
    width: 60,
    height: 60,
    borderRadius: 30,
    borderWidth: 4,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  gaugePct: {
    fontSize: theme.fontSize.lg,
    fontWeight: '700',
  },
  gridCardCode: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: theme.spacing.xs,
  },
  gridCardName: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.text,
    textAlign: 'center',
  },
});
