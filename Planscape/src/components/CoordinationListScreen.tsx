// C5 — Shared read-only list screen used by /transmittals, /meetings,
// /workflows, /warnings. Pattern: project-scoped list + refresh control +
// empty state + row renderer. Opinionated on styling; caller supplies the
// fetch + row item.

import { useCallback, useEffect, useState } from "react";
import {
  View,
  Text,
  FlatList,
  RefreshControl,
  ActivityIndicator,
  StyleSheet,
  TouchableOpacity,
} from "react-native";
import { useProjectStore } from "@/stores/projectStore";

export interface CoordinationListProps<T> {
  title: string;
  emptyTitle: string;
  emptyBody: string;
  fetch: (projectId: string) => Promise<T[]>;
  keyExtractor: (item: T) => string;
  renderRow: (item: T) => React.ReactElement;
  filterItem?: (item: T, query: string) => boolean;
  onPressRow?: (item: T) => void;
  headerAction?: React.ReactElement;
}

export function CoordinationListScreen<T>({
  title, emptyTitle, emptyBody,
  fetch, keyExtractor, renderRow, filterItem, onPressRow, headerAction,
}: CoordinationListProps<T>) {
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [items, setItems] = useState<T[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      const rows = await fetch(projectId);
      setItems(rows);
      setError(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, fetch]);

  useEffect(() => { load(); }, [load]);

  if (!projectId) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyTitle}>No project selected</Text>
        <Text style={styles.emptyBody}>Open the Dashboard and pick a project first.</Text>
      </View>
    );
  }
  if (loading) {
    return <View style={styles.center}><ActivityIndicator /></View>;
  }
  if (error) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorTitle}>Couldn't load {title.toLowerCase()}</Text>
        <Text style={styles.errorBody}>{error}</Text>
        <TouchableOpacity onPress={load} style={styles.retryBtn}>
          <Text style={styles.retryText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }
  if (items.length === 0) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyTitle}>{emptyTitle}</Text>
        <Text style={styles.emptyBody}>{emptyBody}</Text>
        {headerAction}
      </View>
    );
  }

  return (
    <FlatList
      data={items}
      keyExtractor={keyExtractor}
      refreshControl={
        <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />
      }
      ItemSeparatorComponent={() => <View style={styles.sep} />}
      ListHeaderComponent={headerAction}
      renderItem={({ item }) => (
        <TouchableOpacity
          onPress={onPressRow ? () => onPressRow(item) : undefined}
          activeOpacity={onPressRow ? 0.6 : 1}
        >
          {renderRow(item)}
        </TouchableOpacity>
      )}
    />
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 32 },
  emptyTitle: { fontSize: 18, fontWeight: "700", marginBottom: 8, color: "#333" },
  emptyBody: { color: "#666", textAlign: "center", fontSize: 14, lineHeight: 20 },
  errorTitle: { fontSize: 16, fontWeight: "700", marginBottom: 8, color: "#d32f2f" },
  errorBody: { color: "#666", textAlign: "center", marginBottom: 16 },
  retryBtn: { paddingHorizontal: 20, paddingVertical: 10, backgroundColor: "#E8912D", borderRadius: 6 },
  retryText: { color: "#fff", fontWeight: "700" },
  sep: { height: 1, backgroundColor: "#eee" },
});
