import React, { useEffect, useState, useCallback } from "react";
import {
  View, Text, FlatList, StyleSheet, TouchableOpacity,
  ActivityIndicator, RefreshControl, SectionList,
} from "react-native";
import { useRouter } from "expo-router";
import { apiFetch } from "@/api/client";
import { useProjectStore } from "@/stores/projectStore";

interface ModelCheckRun {
  id: string;
  ruleSetId: string;
  ruleSetName?: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  totalElementsChecked: number;
  totalRulesEvaluated: number;
  findingsCount: number;
  criticalCount: number;
  majorCount: number;
  minorCount: number;
  infoCount: number;
  triggeredBy?: string;
}

export default function ModelChecksScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [runs, setRuns] = useState<ModelCheckRun[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (silent = false) => {
    if (!projectId) return;
    if (!silent) setLoading(true);
    setError(null);
    try {
      const data = await apiFetch<ModelCheckRun[]>(`/api/projects/${projectId}/model-checks/runs`);
      setRuns(data ?? []);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load model check runs");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  const onRefresh = () => { setRefreshing(true); load(true); };

  if (loading) return (
    <View style={styles.center}><ActivityIndicator size="large" color="#1565c0" /></View>
  );

  if (error) return (
    <View style={styles.center}>
      <Text style={styles.errorText}>{error}</Text>
      <TouchableOpacity style={styles.retryBtn} onPress={() => load()}>
        <Text style={styles.retryText}>Retry</Text>
      </TouchableOpacity>
    </View>
  );

  return (
    <FlatList
      style={styles.container}
      data={runs}
      keyExtractor={(r) => r.id}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
      ItemSeparatorComponent={() => <View style={styles.sep} />}
      ListEmptyComponent={
        <View style={styles.center}>
          <Text style={styles.emptyTitle}>No model check runs</Text>
          <Text style={styles.emptyBody}>
            Model check runs triggered from the Revit plugin appear here once complete.
          </Text>
        </View>
      }
      renderItem={({ item: run }) => (
        <TouchableOpacity
          style={styles.card}
          onPress={() => router.push(`/model-checks/${run.id}`)}
          activeOpacity={0.75}
        >
          <View style={styles.cardTop}>
            <Text style={styles.ruleSetName} numberOfLines={1}>
              {run.ruleSetName ?? run.ruleSetId}
            </Text>
            <View style={[styles.statusChip, runStatusStyle(run.status)]}>
              <Text style={styles.statusText}>{run.status}</Text>
            </View>
          </View>
          <View style={styles.countRow}>
            {run.criticalCount > 0 && (
              <View style={[styles.countChip, { backgroundColor: "#d32f2f" }]}>
                <Text style={styles.countChipText}>{run.criticalCount} CRITICAL</Text>
              </View>
            )}
            {run.majorCount > 0 && (
              <View style={[styles.countChip, { backgroundColor: "#f57c00" }]}>
                <Text style={styles.countChipText}>{run.majorCount} MAJOR</Text>
              </View>
            )}
            {run.minorCount > 0 && (
              <View style={[styles.countChip, { backgroundColor: "#fbc02d" }]}>
                <Text style={styles.countChipText}>{run.minorCount} MINOR</Text>
              </View>
            )}
            {run.infoCount > 0 && (
              <View style={[styles.countChip, { backgroundColor: "#546e7a" }]}>
                <Text style={styles.countChipText}>{run.infoCount} INFO</Text>
              </View>
            )}
            {run.findingsCount === 0 && (
              <View style={[styles.countChip, { backgroundColor: "#388e3c" }]}>
                <Text style={styles.countChipText}>✓ PASS</Text>
              </View>
            )}
          </View>
          <Text style={styles.meta}>
            {run.totalElementsChecked.toLocaleString()} elements checked
            {" · "}
            {run.totalRulesEvaluated} rules
            {run.triggeredBy ? ` · ${run.triggeredBy}` : ""}
          </Text>
          <Text style={styles.date}>
            {new Date(run.startedAt).toLocaleString()}
            {run.completedAt
              ? ` — ${Math.round((new Date(run.completedAt).getTime() - new Date(run.startedAt).getTime()) / 1000)}s`
              : " (running…)"}
          </Text>
        </TouchableOpacity>
      )}
    />
  );
}

function runStatusStyle(status: string) {
  switch ((status ?? "").toUpperCase()) {
    case "COMPLETED": return { backgroundColor: "#388e3c" };
    case "RUNNING":   return { backgroundColor: "#1976d2" };
    case "FAILED":    return { backgroundColor: "#c62828" };
    default:          return { backgroundColor: "#757575" };
  }
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f5f6fa" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 24 },
  card: {
    backgroundColor: "#fff", marginHorizontal: 12, marginVertical: 4,
    borderRadius: 8, padding: 14, elevation: 1,
    shadowColor: "#000", shadowOpacity: 0.04, shadowRadius: 4,
  },
  cardTop: { flexDirection: "row", alignItems: "center", marginBottom: 8 },
  ruleSetName: { flex: 1, fontSize: 15, fontWeight: "600", color: "#1a1a2e" },
  statusChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10, marginLeft: 8 },
  statusText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  countRow: { flexDirection: "row", flexWrap: "wrap", gap: 4, marginBottom: 8 },
  countChip: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4 },
  countChipText: { color: "#fff", fontSize: 10, fontWeight: "700" },
  meta: { fontSize: 12, color: "#666" },
  date: { fontSize: 11, color: "#aaa", marginTop: 4 },
  sep: { height: 1, backgroundColor: "#f0f0f0" },
  emptyTitle: { fontSize: 16, fontWeight: "600", color: "#444", marginBottom: 8 },
  emptyBody: { fontSize: 14, color: "#888", textAlign: "center", lineHeight: 20 },
  errorText: { fontSize: 14, color: "#c62828", marginBottom: 16, textAlign: "center" },
  retryBtn: { paddingHorizontal: 24, paddingVertical: 10, backgroundColor: "#1565c0", borderRadius: 6 },
  retryText: { color: "#fff", fontWeight: "600" },
});
