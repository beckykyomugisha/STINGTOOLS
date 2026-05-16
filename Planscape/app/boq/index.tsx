import React, { useEffect, useState, useCallback } from "react";
import {
  View, Text, FlatList, StyleSheet, TouchableOpacity,
  ActivityIndicator, RefreshControl, TextInput,
} from "react-native";
import { useRouter } from "expo-router";
import { apiFetch } from "@/api/client";
import { useAuthStore } from "@/stores/auth";

interface BoqDocument {
  id: string;
  name: string;
  clientName?: string;
  status: string;
  currency: string;
  totalNet?: number;
  totalGross?: number;
  lockedAt?: string;
  createdAt: string;
  updatedAt: string;
}

export default function BoqScreen() {
  const router = useRouter();
  const projectId = useAuthStore((s) => s.activeProjectId);
  const [docs, setDocs] = useState<BoqDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [query, setQuery] = useState("");
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (silent = false) => {
    if (!projectId) return;
    if (!silent) setLoading(true);
    setError(null);
    try {
      const data = await apiFetch<BoqDocument[]>(`/api/projects/${projectId}/boq`);
      setDocs(data ?? []);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load BOQ documents");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  const onRefresh = () => { setRefreshing(true); load(true); };

  const filtered = query.trim()
    ? docs.filter((d) =>
        d.name.toLowerCase().includes(query.toLowerCase()) ||
        (d.clientName ?? "").toLowerCase().includes(query.toLowerCase())
      )
    : docs;

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
    <View style={styles.container}>
      <TextInput
        style={styles.search}
        placeholder="Search BOQ documents…"
        value={query}
        onChangeText={setQuery}
        clearButtonMode="while-editing"
      />
      <FlatList
        data={filtered}
        keyExtractor={(d) => d.id}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
        ItemSeparatorComponent={() => <View style={styles.sep} />}
        ListEmptyComponent={
          <View style={styles.center}>
            <Text style={styles.emptyTitle}>No BOQ documents</Text>
            <Text style={styles.emptyBody}>
              BOQ documents created by the plugin's NRM2 exporter appear here.
            </Text>
          </View>
        }
        renderItem={({ item: doc }) => (
          <TouchableOpacity
            style={styles.card}
            onPress={() => router.push(`/boq/${doc.id}`)}
            activeOpacity={0.75}
          >
            <View style={styles.cardTop}>
              <Text style={styles.docName} numberOfLines={1}>{doc.name}</Text>
              <View style={[styles.statusChip, statusChipStyle(doc.status)]}>
                <Text style={styles.statusText}>{doc.status}</Text>
              </View>
            </View>
            {doc.clientName ? (
              <Text style={styles.client}>{doc.clientName}</Text>
            ) : null}
            <View style={styles.cardMeta}>
              <Text style={styles.meta}>
                {doc.currency}
                {doc.totalGross != null
                  ? `  Gross: ${formatCurrency(doc.totalGross)}`
                  : doc.totalNet != null
                  ? `  Net: ${formatCurrency(doc.totalNet)}`
                  : ""}
              </Text>
              {doc.lockedAt ? (
                <Text style={[styles.meta, { color: "#c62828" }]}>
                  Locked {new Date(doc.lockedAt).toLocaleDateString()}
                </Text>
              ) : null}
            </View>
            <Text style={styles.updated}>
              Updated {new Date(doc.updatedAt).toLocaleDateString()}
            </Text>
          </TouchableOpacity>
        )}
      />
    </View>
  );
}

function formatCurrency(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function statusChipStyle(status: string) {
  switch ((status ?? "").toUpperCase()) {
    case "DRAFT":    return { backgroundColor: "#90a4ae" };
    case "ISSUED":   return { backgroundColor: "#1976d2" };
    case "APPROVED": return { backgroundColor: "#388e3c" };
    case "LOCKED":   return { backgroundColor: "#6a1b9a" };
    default:         return { backgroundColor: "#757575" };
  }
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f5f6fa" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 24 },
  search: {
    margin: 12, padding: 10, borderRadius: 8,
    backgroundColor: "#fff", fontSize: 14,
    borderWidth: 1, borderColor: "#e0e0e0",
  },
  card: {
    backgroundColor: "#fff", marginHorizontal: 12,
    marginVertical: 4, borderRadius: 8, padding: 14,
    shadowColor: "#000", shadowOpacity: 0.04, shadowRadius: 4,
    elevation: 1,
  },
  cardTop: { flexDirection: "row", alignItems: "center", marginBottom: 4 },
  docName: { flex: 1, fontSize: 15, fontWeight: "600", color: "#1a1a2e" },
  statusChip: {
    paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10, marginLeft: 8,
  },
  statusText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  client: { fontSize: 12, color: "#546e7a", marginBottom: 4 },
  cardMeta: { flexDirection: "row", justifyContent: "space-between", marginTop: 4 },
  meta: { fontSize: 12, color: "#666" },
  updated: { fontSize: 11, color: "#aaa", marginTop: 6 },
  sep: { height: 1, backgroundColor: "#f0f0f0" },
  emptyTitle: { fontSize: 16, fontWeight: "600", color: "#444", marginBottom: 8 },
  emptyBody: { fontSize: 14, color: "#888", textAlign: "center", lineHeight: 20 },
  errorText: { fontSize: 14, color: "#c62828", marginBottom: 16, textAlign: "center" },
  retryBtn: {
    paddingHorizontal: 24, paddingVertical: 10,
    backgroundColor: "#1565c0", borderRadius: 6,
  },
  retryText: { color: "#fff", fontWeight: "600" },
});
