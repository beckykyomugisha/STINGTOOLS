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
import { useAuthStore } from "@/stores/auth";

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
  // Phase 184o — surfaced from the list endpoint so the user can tell
  // design errors from client requests without drilling into detail.
  reason?: string;
  liability?: string;
  eotDays?: number;
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
          {/* Phase 184o — reason / liability / EOT badges */}
          {(item.reason || item.liability) && (
            <View style={styles.badgeRow}>
              {item.reason && (
                <View style={[styles.badge, { backgroundColor: reasonColor(item.reason) }]}>
                  <Text style={styles.badgeText}>{prettifyReason(item.reason)}</Text>
                </View>
              )}
              {item.liability && (
                <View style={[styles.badge, styles.liabilityBadge]}>
                  <Text style={styles.badgeText}>Pays: {item.liability}</Text>
                </View>
              )}
              {item.eotDays && item.eotDays > 0 ? (
                <View style={[styles.badge, styles.eotBadge]}>
                  <Text style={styles.badgeText}>+{item.eotDays}d EOT</Text>
                </View>
              ) : null}
            </View>
          )}
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

// Phase 184o — reason colour palette helps the user scan the list and
// immediately spot the pattern (e.g. "lots of red — many design errors").
function reasonColor(reason: string): string {
  switch (reason) {
    case "DesignChange":       return "#7c3aed"; // violet — designer
    case "ErrorOmission":      return "#b3261e"; // red — error
    case "ClientRequest":      return "#0a7d2e"; // green — client
    case "SiteCondition":      return "#946800"; // amber — unforeseen
    case "StatutoryChange":    return "#1c70d8"; // blue — statutory
    case "ContractorProposal": return "#0d9488"; // teal — VE
    case "ScopeAddition":      return "#16a34a"; // green-ish — added
    case "ScopeOmission":      return "#dc2626"; // red-ish — omitted
    case "Specification":      return "#4b5563"; // grey — spec
    case "Quality":            return "#0891b2"; // cyan — quality
    case "ProgrammeChange":    return "#a16207"; // dark amber — programme
    default:                   return "#5a5a5a"; // neutral — other
  }
}

function prettifyReason(reason: string): string {
  // Insert spaces before capitals — "DesignChange" → "Design Change".
  return reason.replace(/([A-Z])/g, " $1").trim();
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
  // Phase 184o — reason / liability / EOT badges
  badgeRow: { flexDirection: "row", flexWrap: "wrap", gap: 4, marginVertical: 4 },
  badge: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4, marginRight: 4, marginBottom: 2 },
  badgeText: { color: "white", fontSize: 10, fontWeight: "600" },
  liabilityBadge: { backgroundColor: "#4b5563" },
  eotBadge: { backgroundColor: "#dc2626" },
});
