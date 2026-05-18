// ══════════════════════════════════════════════════════════════════════════
//  Payment certificates list — Phase 184i / P7.
//  Shows every interim certificate per project. Tap to drill into the
//  detail screen for contractor agree / dispute.
// ══════════════════════════════════════════════════════════════════════════
import React, { useCallback, useEffect, useState } from "react";
import {
  View, Text, FlatList, StyleSheet, TouchableOpacity,
  ActivityIndicator, RefreshControl,
} from "react-native";
import { useRouter } from "expo-router";
import { apiFetch } from "@/api/client";
import { useAuthStore } from "@/stores/auth";

interface PaymentCert {
  id: string;
  certNumber: number;
  contractRef: string;
  form: string;             // "JCT2024" / "NEC4" / "FIDIC2017Red"
  status: string;           // "Draft" / "Issued" / "Agreed" / "Paid" / "Disputed"
  currency: string;
  grossValuation: number;
  retentionAmount: number;
  totalPayable: number;
  valuationDate: string;
}

export default function PaymentCertsScreen() {
  const router = useRouter();
  const projectId = useAuthStore((s) => s.activeProjectId);
  const [items, setItems] = useState<PaymentCert[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (silent = false) => {
    if (!projectId) return;
    if (!silent) setLoading(true);
    setError(null);
    try {
      const data = await apiFetch<PaymentCert[]>(
        `/api/projects/${projectId}/boq/payment-certs`
      );
      setItems(data ?? []);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load payment certificates");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  const statusColor = (s: string) => {
    switch (s) {
      case "Agreed": case "Paid":  return "#0a7d2e";
      case "Disputed":              return "#b3261e";
      case "Issued":                return "#946800";
      default:                      return "#5a5a5a";
    }
  };

  if (loading) {
    return <View style={styles.center}><ActivityIndicator size="large" /></View>;
  }

  return (
    <FlatList
      data={items}
      keyExtractor={(c) => c.id}
      contentContainerStyle={styles.list}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(true); }} />}
      ListEmptyComponent={
        <View style={styles.empty}>
          <Text style={styles.emptyText}>
            {error ? error : "No payment certificates issued yet."}
          </Text>
        </View>
      }
      renderItem={({ item }) => (
        <TouchableOpacity
          style={styles.row}
          onPress={() => router.push(`/payment-certs/${item.id}`)}
        >
          <View style={styles.rowHead}>
            <Text style={styles.number}>Cert #{item.certNumber}</Text>
            <Text style={[styles.status, { color: statusColor(item.status) }]}>
              {item.status}
            </Text>
          </View>
          <Text style={styles.ref}>{item.contractRef} · {item.form}</Text>
          <View style={styles.rowFoot}>
            <Text style={styles.label}>Payable</Text>
            <Text style={styles.amount}>
              {item.currency} {item.totalPayable.toLocaleString(undefined, { maximumFractionDigits: 0 })}
            </Text>
          </View>
          <Text style={styles.meta}>
            Gross {item.currency} {item.grossValuation.toLocaleString(undefined, { maximumFractionDigits: 0 })}
            · Retention {item.currency} {item.retentionAmount.toLocaleString(undefined, { maximumFractionDigits: 0 })}
          </Text>
          <Text style={styles.date}>
            Valuation {new Date(item.valuationDate).toLocaleDateString()}
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
    backgroundColor: "white", borderRadius: 10, padding: 14, marginBottom: 10,
    shadowColor: "#000", shadowOpacity: 0.06, shadowRadius: 4,
    shadowOffset: { width: 0, height: 1 }, elevation: 1,
  },
  rowHead: { flexDirection: "row", justifyContent: "space-between", marginBottom: 4 },
  number: { fontSize: 16, fontWeight: "700" },
  status: { fontSize: 12, fontWeight: "600", textTransform: "uppercase" },
  ref: { fontSize: 13, color: "#5a5a5a", marginBottom: 8 },
  rowFoot: { flexDirection: "row", justifyContent: "space-between", marginBottom: 4 },
  label: { fontSize: 12, color: "#5a5a5a" },
  amount: { fontSize: 15, fontWeight: "700" },
  meta: { fontSize: 12, color: "#5a5a5a" },
  date: { fontSize: 11, color: "#8a8a8a", marginTop: 4 },
});
