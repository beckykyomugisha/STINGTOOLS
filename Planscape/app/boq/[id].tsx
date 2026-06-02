import React, { useEffect, useState, useCallback } from "react";
import {
  View, Text, FlatList, StyleSheet,
  ActivityIndicator, SectionList, TouchableOpacity,
} from "react-native";
import { useLocalSearchParams } from "expo-router";
import { apiFetch } from "@/api/client";
import { useProjectStore } from "@/stores/projectStore";

interface BoqSection {
  id: string;
  sectionCode: string;
  title: string;
  sortOrder: number;
}

interface QuantityLine {
  id: string;
  sectionId?: string;
  itemRef: string;
  description: string;
  unit: string;
  netQuantity: number;
  wastePercent: number;
  grossQuantity: number;
  rate?: number;
  amount?: number;
  nrmCode?: string;
}

interface BoqDetail {
  id: string;
  name: string;
  status: string;
  currency: string;
  totalNet?: number;
  totalGross?: number;
  sections: BoqSection[];
  lines: QuantityLine[];
}

export default function BoqDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [detail, setDetail] = useState<BoqDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId || !id) return;
    setLoading(true); setError(null);
    try {
      const data = await apiFetch<BoqDetail>(`/api/projects/${projectId}/boq/documents/${id}`);
      setDetail(data);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load BOQ");
    } finally {
      setLoading(false);
    }
  }, [projectId, id]);

  useEffect(() => { load(); }, [load]);

  if (loading) return (
    <View style={styles.center}><ActivityIndicator size="large" color="#1565c0" /></View>
  );
  if (error || !detail) return (
    <View style={styles.center}>
      <Text style={styles.errorText}>{error ?? "Not found"}</Text>
    </View>
  );

  const sectionMap = new Map(detail.sections.map((s) => [s.id, s]));
  const grouped: { [key: string]: QuantityLine[] } = {};
  const UNSECTIONED = "__none__";
  for (const line of detail.lines) {
    const key = line.sectionId ?? UNSECTIONED;
    if (!grouped[key]) grouped[key] = [];
    grouped[key].push(line);
  }

  const sections = [
    ...detail.sections.map((s) => ({
      title: `${s.sectionCode} — ${s.title}`,
      data: grouped[s.id] ?? [],
    })),
    ...(grouped[UNSECTIONED]?.length
      ? [{ title: "Unsectioned items", data: grouped[UNSECTIONED] }]
      : []),
  ];

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.headerName}>{detail.name}</Text>
        <View style={{ flexDirection: "row", gap: 8, marginTop: 4 }}>
          <Text style={styles.headerMeta}>{detail.status}</Text>
          <Text style={styles.headerMeta}>·</Text>
          <Text style={styles.headerMeta}>{detail.currency}</Text>
          {detail.totalGross != null && (
            <>
              <Text style={styles.headerMeta}>·</Text>
              <Text style={styles.headerMeta}>
                Gross {detail.totalGross.toLocaleString(undefined, { maximumFractionDigits: 0 })}
              </Text>
            </>
          )}
        </View>
      </View>
      <SectionList
        sections={sections}
        keyExtractor={(line) => line.id}
        stickySectionHeadersEnabled
        renderSectionHeader={({ section: { title } }) => (
          <View style={styles.sectionHeader}>
            <Text style={styles.sectionHeaderText}>{title}</Text>
          </View>
        )}
        ItemSeparatorComponent={() => <View style={styles.sep} />}
        renderItem={({ item: line }) => (
          <View style={styles.lineRow}>
            <View style={styles.lineLeft}>
              <Text style={styles.itemRef}>{line.itemRef}</Text>
              <Text style={styles.description} numberOfLines={2}>{line.description}</Text>
              {line.nrmCode ? (
                <Text style={styles.nrmCode}>{line.nrmCode}</Text>
              ) : null}
            </View>
            <View style={styles.lineRight}>
              <Text style={styles.qty}>
                {line.grossQuantity.toLocaleString(undefined, { maximumFractionDigits: 2 })}
              </Text>
              <Text style={styles.unit}>{line.unit}</Text>
              {line.amount != null ? (
                <Text style={styles.amount}>
                  {line.amount.toLocaleString(undefined, { maximumFractionDigits: 0 })}
                </Text>
              ) : null}
            </View>
          </View>
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f5f6fa" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 24 },
  errorText: { fontSize: 14, color: "#c62828", textAlign: "center" },
  header: {
    backgroundColor: "#1565c0", padding: 16,
  },
  headerName: { fontSize: 17, fontWeight: "700", color: "#fff" },
  headerMeta: { fontSize: 12, color: "#bbdefb" },
  sectionHeader: {
    backgroundColor: "#e3f2fd", paddingHorizontal: 14, paddingVertical: 8,
    borderBottomWidth: 1, borderBottomColor: "#90caf9",
  },
  sectionHeaderText: { fontSize: 12, fontWeight: "700", color: "#0d47a1", textTransform: "uppercase" },
  lineRow: {
    flexDirection: "row", backgroundColor: "#fff",
    paddingHorizontal: 14, paddingVertical: 10, alignItems: "flex-start",
  },
  lineLeft: { flex: 1, paddingRight: 8 },
  lineRight: { alignItems: "flex-end", minWidth: 70 },
  itemRef: { fontSize: 11, color: "#1565c0", fontWeight: "600", marginBottom: 2 },
  description: { fontSize: 13, color: "#222" },
  nrmCode: { fontSize: 10, color: "#999", marginTop: 2 },
  qty: { fontSize: 13, fontWeight: "600", color: "#1a1a2e" },
  unit: { fontSize: 10, color: "#666" },
  amount: { fontSize: 12, color: "#2e7d32", fontWeight: "600", marginTop: 2 },
  sep: { height: 1, backgroundColor: "#f0f0f0" },
});
