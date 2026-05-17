import React, { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  ListRenderItem,
  RefreshControl,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import { listIfcElements, listProjects } from '@/api/endpoints';
import { theme } from '@/utils/theme';
import type { TaggedElement } from '@/types/api';

// ── Source filter bar ──────────────────────────────────────────────────────

type SourceFilter = 'all' | 'archicad' | 'ifc' | 'revit';

const SOURCE_FILTERS: { key: SourceFilter; label: string }[] = [
  { key: 'all',      label: 'All' },
  { key: 'archicad', label: 'ArchiCAD' },
  { key: 'ifc',      label: 'IFC' },
  { key: 'revit',    label: 'Revit' },
];

const SOURCE_BADGE_COLORS: Record<string, string> = {
  archicad: '#E85D00',   // orange
  ifc:      '#1565C0',   // blue
  revit:    '#2E7D32',   // green
};

// ── IFC type badge colours ─────────────────────────────────────────────────

function ifcTypeBadgeColor(ifcType: string): string {
  const t = ifcType.toLowerCase();
  if (t.includes('wall'))                          return '#1565C0'; // blue
  if (t.includes('slab') || t.includes('floor'))  return '#616161'; // grey
  if (t.includes('column'))                        return '#5D4037'; // brown
  if (t.includes('door') || t.includes('window')) return '#2E7D32'; // green
  if (t.includes('space') || t.includes('zone'))  return '#6A1B9A'; // purple
  return '#455A64';                                                   // slate
}

// Derive a display IFC type from the element's categoryName / typeName.
// The server doesn't send a dedicated ifcType field on TaggedElement, so we
// infer one from categoryName which mirrors the Revit category / IFC export.
function deriveIfcType(el: TaggedElement): string {
  const cat = (el.categoryName ?? '').toLowerCase();
  if (cat.includes('wall'))        return 'IfcWall';
  if (cat.includes('floor') || cat.includes('slab')) return 'IfcSlab';
  if (cat.includes('column'))      return 'IfcColumn';
  if (cat.includes('door'))        return 'IfcDoor';
  if (cat.includes('window'))      return 'IfcWindow';
  if (cat.includes('room') || cat.includes('space')) return 'IfcSpace';
  if (cat.includes('beam'))        return 'IfcBeam';
  if (cat.includes('stair'))       return 'IfcStair';
  if (cat.includes('railing'))     return 'IfcRailing';
  if (cat.includes('roof'))        return 'IfcRoof';
  return 'IfcBuildingElement';
}

// Derive the source from the element's discipline + familyName heuristics.
// In practice the server would stamp a `source` field; we infer it here from
// available fields so the screen works against the existing TaggedElement shape.
function deriveSource(el: TaggedElement): SourceFilter {
  const fam = (el.familyName ?? '').toLowerCase();
  const tag = (el.assTag1 ?? '').toLowerCase();
  if (fam.includes('archicad') || tag.includes('ac-')) return 'archicad';
  if (fam.includes('ifc'))                              return 'ifc';
  // Default assumption for elements synced from the Revit plugin
  return 'revit';
}

// ── Row component ──────────────────────────────────────────────────────────

interface ElementRowProps {
  item: TaggedElement;
}

function ElementRow({ item }: ElementRowProps) {
  const ifcType = deriveIfcType(item);
  const source  = deriveSource(item);

  const ifcColor    = ifcTypeBadgeColor(ifcType);
  const sourceColor = SOURCE_BADGE_COLORS[source] ?? '#455A64';
  const sourceLabel = SOURCE_FILTERS.find((f) => f.key === source)?.label ?? source;

  return (
    <View style={styles.row}>
      <View style={styles.rowTop}>
        <View style={[styles.badge, { backgroundColor: ifcColor }]}>
          <Text style={styles.badgeText} numberOfLines={1}>
            {ifcType}
          </Text>
        </View>
        <View style={[styles.badge, { backgroundColor: sourceColor }]}>
          <Text style={styles.badgeText}>{sourceLabel}</Text>
        </View>
      </View>

      <Text style={styles.elementName} numberOfLines={1}>
        {item.familyName || item.typeName || item.assTag1 || 'Unnamed element'}
      </Text>

      <Text style={styles.globalId} numberOfLines={1}>
        {item.uniqueId}
      </Text>

      {!!item.categoryName && (
        <Text style={styles.category} numberOfLines={1}>
          {item.categoryName}
        </Text>
      )}
    </View>
  );
}

// ── Main screen ────────────────────────────────────────────────────────────

export default function IfcElementsScreen() {
  const [projectId, setProjectId] = useState<string | null>(null);

  useEffect(() => {
    listProjects()
      .then((projects: Array<{ id: string }>) => setProjectId(projects[0]?.id ?? null))
      .catch(() => {/* no-op — fetchElements will handle the null projectId */});
  }, []);

  const [activeFilter, setActiveFilter] = useState<SourceFilter>('all');
  const [elements, setElements]         = useState<TaggedElement[]>([]);
  const [loading, setLoading]           = useState(false);
  const [refreshing, setRefreshing]     = useState(false);
  const [error, setError]               = useState<string | null>(null);

  const fetchElements = useCallback(
    async (isRefresh = false) => {
      if (!projectId) return;
      if (isRefresh) setRefreshing(true);
      else setLoading(true);
      setError(null);
      try {
        const source = activeFilter === 'all' ? undefined : activeFilter;
        const data = await listIfcElements(projectId, source);
        setElements(Array.isArray(data) ? data : []);
      } catch (err) {
        setError((err as Error)?.message ?? 'Failed to load elements');
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [projectId, activeFilter],
  );

  useEffect(() => {
    fetchElements();
  }, [fetchElements]);

  const renderItem: ListRenderItem<TaggedElement> = ({ item }) => (
    <ElementRow item={item} />
  );

  const keyExtractor = (item: TaggedElement) => item.id;

  // ── Source filter bar ──

  const FilterBar = (
    <View style={styles.filterBar}>
      {SOURCE_FILTERS.map(({ key, label }) => {
        const active = key === activeFilter;
        return (
          <TouchableOpacity
            key={key}
            style={[styles.filterChip, active && styles.filterChipActive]}
            onPress={() => setActiveFilter(key)}
            activeOpacity={0.75}
          >
            <Text style={[styles.filterChipText, active && styles.filterChipTextActive]}>
              {label}
            </Text>
          </TouchableOpacity>
        );
      })}
    </View>
  );

  // ── Loading / error / empty states ──

  if (!projectId) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyText}>No project selected.</Text>
      </View>
    );
  }

  if (loading && !refreshing) {
    return (
      <View style={styles.container}>
        {FilterBar}
        <View style={styles.center}>
          <ActivityIndicator size="large" color={theme.colors.accent} />
        </View>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.container}>
        {FilterBar}
        <View style={styles.center}>
          <Text style={styles.errorText}>{error}</Text>
          <TouchableOpacity
            style={styles.retryButton}
            onPress={() => fetchElements()}
          >
            <Text style={styles.retryButtonText}>Retry</Text>
          </TouchableOpacity>
        </View>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      {FilterBar}
      <FlatList
        data={elements}
        keyExtractor={keyExtractor}
        renderItem={renderItem}
        contentContainerStyle={
          elements.length === 0 ? styles.emptyContainer : styles.listContent
        }
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => fetchElements(true)}
            tintColor={theme.colors.accent}
          />
        }
        ListEmptyComponent={
          <View style={styles.emptyInner}>
            <Text style={styles.emptyIcon}>🗄</Text>
            <Text style={styles.emptyTitle}>No elements found</Text>
            <Text style={styles.emptySubtitle}>
              {activeFilter === 'all'
                ? 'No IFC elements have been synced to this project yet.'
                : `No ${SOURCE_FILTERS.find((f) => f.key === activeFilter)?.label} elements match the current filter.`}
            </Text>
          </View>
        }
      />
    </View>
  );
}

// ── Styles ─────────────────────────────────────────────────────────────────

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },

  // ── Filter bar ──
  filterBar: {
    flexDirection: 'row',
    paddingHorizontal: 12,
    paddingVertical: 10,
    gap: 8,
    backgroundColor: theme.colors.surface,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  filterChip: {
    paddingHorizontal: 14,
    paddingVertical: 6,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: theme.colors.border,
    backgroundColor: theme.colors.surface,
  },
  filterChipActive: {
    backgroundColor: theme.colors.accent,
    borderColor: theme.colors.accent,
  },
  filterChipText: {
    fontSize: 13,
    fontWeight: '500',
    color: theme.colors.textSecondary,
  },
  filterChipTextActive: {
    color: '#fff',
  },

  // ── List ──
  listContent: {
    padding: 12,
    gap: 8,
  },
  emptyContainer: {
    flexGrow: 1,
  },

  // ── Row ──
  row: {
    backgroundColor: theme.colors.surface,
    borderRadius: 8,
    padding: 12,
    marginBottom: 8,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: theme.colors.border,
  },
  rowTop: {
    flexDirection: 'row',
    gap: 6,
    marginBottom: 6,
    flexWrap: 'wrap',
  },
  badge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 4,
  },
  badgeText: {
    color: '#fff',
    fontSize: 11,
    fontWeight: '600',
  },
  elementName: {
    fontSize: 14,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: 2,
  },
  globalId: {
    fontSize: 11,
    color: theme.colors.textSecondary,
    fontFamily: 'monospace',
    marginBottom: 2,
  },
  category: {
    fontSize: 12,
    color: theme.colors.textSecondary,
  },

  // ── States ──
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
  },
  emptyInner: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 40,
  },
  emptyIcon: {
    fontSize: 48,
    marginBottom: 12,
  },
  emptyTitle: {
    fontSize: 16,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: 6,
    textAlign: 'center',
  },
  emptySubtitle: {
    fontSize: 13,
    color: theme.colors.textSecondary,
    textAlign: 'center',
    lineHeight: 18,
  },
  emptyText: {
    fontSize: 14,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },
  errorText: {
    fontSize: 14,
    color: theme.colors.danger,
    textAlign: 'center',
    marginBottom: 16,
  },
  retryButton: {
    paddingHorizontal: 20,
    paddingVertical: 10,
    backgroundColor: theme.colors.accent,
    borderRadius: 8,
  },
  retryButtonText: {
    color: '#fff',
    fontWeight: '600',
    fontSize: 14,
  },
});
