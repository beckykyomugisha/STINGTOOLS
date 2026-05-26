// ══════════════════════════════════════════════════════════════════════════
//  Variations list — Phase 184i / P7.
//  Lists all variations for the active project; tap to drill into a
//  detail screen where the user can approve / reject / request more info.
// ══════════════════════════════════════════════════════════════════════════
import React, { useCallback, useEffect, useState } from "react";
import {
  View, Text, FlatList, StyleSheet, TouchableOpacity,
  ActivityIndicator, RefreshControl,
} from "react-native";
import { useRouter } from "expo-router";
import { apiFetch } from "@/api/client";
import { useAuthStore } from "@/stores/authStore";

interface Variation {
  id: string;
  number: string;
  kind: string;           // "Instruction" / "CompensationEvent" / etc.
  status: string;         // "Draft" / "Submitted" / "Approved" / "Rejected"
  title: string;
  totalValue: number;
  currency: string;
  instructionDate: string;
  approvalDate?: string;
}

export default function VariationsScreen() {
  const router = useRouter();
  const projectId = useAuthStore((s) => s.activeProjectId);
  const [items, setItems] = useState<Variation[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (silent = false) => {
    if (!projectId) return;
    if (!silent) setLoading(true);
    setError(null);
    try {
      const data = await apiFetch<Variation[]>(
        `/api/projects/${projectId}/boq/variations`
      );
      setItems(data ?? []);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load variations");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  const statusColor = (s: string) => {
    switch (s) {
      case "Approved":     return "#0a7d2e";
      case "Rejected":     return "#b3261e";
      case "Submitted":    return "#946800";
      case "Incorporated": return "#1c70d8";
      default:             return "#5a5a5a";
    }
  };

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" />
      </View>
    );
  }

  return (
    <FlatList
      data={items}
      keyExtractor={(v) => v.id}
      contentContainerStyle={styles.list}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(true); }} />}
      ListEmptyComponent={
        <View style={styles.empty}>
          <Text style={styles.emptyText}>
            {error ? error : "No variations recorded yet."}
          </Text>
        </View>
      }
      renderItem={({ item }) => (
        <TouchableOpacity
          style={styles.row}
          onPress={() => router.push(`/variations/${item.id}`)}
        >
          <View style={styles.rowHead}>
            <Text style={styles.number}>{item.number}</Text>
            <Text style={[styles.status, { color: statusColor(item.status) }]}>
              {item.status}
            </Text>
          </View>
          <Text style={styles.title} numberOfLines={2}>{item.title}</Text>
          <View style={styles.rowFoot}>
            <Text style={styles.kind}>{item.kind}</Text>
            <Text style={styles.amount}>
              {item.currency} {item.totalValue.toLocaleString()}
            </Text>
          </View>
          <Text style={styles.date}>
            Issued {new Date(item.instructionDate).toLocaleDateString()}
            {item.approvalDate
              ? ` · Approved ${new Date(item.approvalDate).toLocaleDateString()}`
              : ""}
          </Text>
        </TouchableOpacity>
      )}
    />
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
  list: { padding: 12 },
  empty: { padding: 32, alignItems: "center" },
  emptyText: { color: "#5a5a5a", textAlign: "center" },
  row: {
    backgroundColor: "white",
    borderRadius: 10,
    padding: 14,
    marginBottom: 10,
    shadowColor: "#000",
    shadowOpacity: 0.06,
    shadowRadius: 4,
    shadowOffset: { width: 0, height: 1 },
    elevation: 1,
  },
  rowHead: { flexDirection: "row", justifyContent: "space-between", marginBottom: 4 },
  number: { fontSize: 16, fontWeight: "700" },
  status: { fontSize: 12, fontWeight: "600", textTransform: "uppercase" },
  title: { fontSize: 14, marginBottom: 6 },
  rowFoot: { flexDirection: "row", justifyContent: "space-between", marginBottom: 4 },
  kind: { fontSize: 12, color: "#5a5a5a" },
  amount: { fontSize: 13, fontWeight: "600" },
  date: { fontSize: 11, color: "#8a8a8a" },
});
