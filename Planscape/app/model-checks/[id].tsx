import React, { useEffect, useState, useCallback } from "react";
import {
  View, Text, StyleSheet, ActivityIndicator,
  SectionList, TouchableOpacity,
} from "react-native";
import { useLocalSearchParams } from "expo-router";
import { apiFetch } from "@/api/client";
import { useProjectStore } from "@/stores/projectStore";

interface ModelCheckFinding {
  id: string;
  severity: string;
  ifcGlobalId?: string;
  ifcType?: string;
  elementName?: string;
  message: string;
  ruleName?: string;
}

interface ModelCheckRunDetail {
  id: string;
  ruleSetName?: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  totalElementsChecked: number;
  findingsCount: number;
  criticalCount: number;
  majorCount: number;
  minorCount: number;
  infoCount: number;
  findings: ModelCheckFinding[];
}

type Section = { title: string; data: ModelCheckFinding[] };

const SEV_ORDER = ["CRITICAL", "MAJOR", "MINOR", "INFO"];

export default function ModelCheckRunScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [run, setRun] = useState<ModelCheckRunDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId || !id) return;
    setLoading(true); setError(null);
    try {
      const data = await apiFetch<ModelCheckRunDetail>(
        `/api/projects/${projectId}/model-checks/runs/${id}`
      );
      setRun(data);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load run details");
    } finally {
      setLoading(false);
    }
  }, [projectId, id]);

  useEffect(() => { load(); }, [load]);

  if (loading) return (
    <View style={styles.center}><ActivityIndicator size="large" color="#1565c0" /></View>
  );
  if (error || !run) return (
    <View style={styles.center}>
      <Text style={styles.errorText}>{error ?? "Not found"}</Text>
    </View>
  );

  const grouped = new Map<string, ModelCheckFinding[]>();
  for (const sev of SEV_ORDER) grouped.set(sev, []);
  for (const f of run.findings) {
    const key = (f.severity ?? "INFO").toUpperCase();
    if (!grouped.has(key)) grouped.set(key, []);
    grouped.get(key)!.push(f);
  }

  const sections: Section[] = SEV_ORDER
    .filter((sev) => (grouped.get(sev)?.length ?? 0) > 0)
    .map((sev) => ({ title: sev, data: grouped.get(sev)! }));

  const duration = run.completedAt
    ? Math.round((new Date(run.completedAt).getTime() - new Date(run.startedAt).getTime()) / 1000)
    : null;

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.headerTitle}>{run.ruleSetName ?? "Model Check Run"}</Text>
        <View style={styles.headerStats}>
          <Stat label="Elements" value={run.totalElementsChecked.toLocaleString()} />
          <Stat label="Findings" value={run.findingsCount.toString()} />
          {duration != null && <Stat label="Duration" value={`${duration}s`} />}
        </View>
        <View style={styles.sevRow}>
          {run.criticalCount > 0 && <SevCount sev="CRITICAL" count={run.criticalCount} />}
          {run.majorCount   > 0 && <SevCount sev="MAJOR"    count={run.majorCount} />}
          {run.minorCount   > 0 && <SevCount sev="MINOR"    count={run.minorCount} />}
          {run.infoCount    > 0 && <SevCount sev="INFO"     count={run.infoCount} />}
          {run.findingsCount === 0 && (
            <Text style={styles.passText}>✓ All checks passed</Text>
          )}
        </View>
      </View>
      {sections.length === 0 ? (
        <View style={styles.center}>
          <Text style={styles.noFindingsText}>No findings — model is clean.</Text>
        </View>
      ) : (
        <SectionList
          sections={sections}
          keyExtractor={(f) => f.id}
          stickySectionHeadersEnabled
          renderSectionHeader={({ section: { title, data } }) => (
            <View style={[styles.sectionHeader, sevHeaderStyle(title)]}>
              <Text style={styles.sectionHeaderText}>
                {title} ({data.length})
              </Text>
            </View>
          )}
          ItemSeparatorComponent={() => <View style={styles.sep} />}
          renderItem={({ item: f }) => (
            <View style={styles.findingRow}>
              <View style={styles.findingTop}>
                {f.ruleName ? (
                  <Text style={styles.ruleName}>{f.ruleName}</Text>
                ) : null}
                {f.ifcType ? (
                  <Text style={styles.ifcType}>{f.ifcType}</Text>
                ) : null}
              </View>
              <Text style={styles.message}>{f.message}</Text>
              {f.elementName ? (
                <Text style={styles.elementName}>{f.elementName}</Text>
              ) : null}
              {f.ifcGlobalId ? (
                <Text style={styles.guid}>{f.ifcGlobalId}</Text>
              ) : null}
            </View>
          )}
        />
      )}
    </View>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.stat}>
      <Text style={styles.statValue}>{value}</Text>
      <Text style={styles.statLabel}>{label}</Text>
    </View>
  );
}

function SevCount({ sev, count }: { sev: string; count: number }) {
  return (
    <View style={[styles.sevChip, sevChipStyle(sev)]}>
      <Text style={styles.sevChipText}>{count} {sev}</Text>
    </View>
  );
}

function sevChipStyle(sev: string) {
  switch (sev) {
    case "CRITICAL": return { backgroundColor: "#d32f2f" };
    case "MAJOR":    return { backgroundColor: "#f57c00" };
    case "MINOR":    return { backgroundColor: "#fbc02d" };
    case "INFO":     return { backgroundColor: "#546e7a" };
    default:         return { backgroundColor: "#757575" };
  }
}

function sevHeaderStyle(sev: string) {
  switch (sev) {
    case "CRITICAL": return { backgroundColor: "#ffebee", borderLeftColor: "#d32f2f" };
    case "MAJOR":    return { backgroundColor: "#fff3e0", borderLeftColor: "#f57c00" };
    case "MINOR":    return { backgroundColor: "#fffde7", borderLeftColor: "#fbc02d" };
    default:         return { backgroundColor: "#eceff1", borderLeftColor: "#546e7a" };
  }
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f5f6fa" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 24 },
  header: { backgroundColor: "#1565c0", padding: 16 },
  headerTitle: { fontSize: 17, fontWeight: "700", color: "#fff", marginBottom: 10 },
  headerStats: { flexDirection: "row", gap: 20, marginBottom: 10 },
  stat: { alignItems: "center" },
  statValue: { fontSize: 18, fontWeight: "700", color: "#fff" },
  statLabel: { fontSize: 10, color: "#bbdefb" },
  sevRow: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  sevChip: { paddingHorizontal: 8, paddingVertical: 3, borderRadius: 4 },
  sevChipText: { color: "#fff", fontSize: 10, fontWeight: "700" },
  passText: { color: "#a5d6a7", fontSize: 13, fontWeight: "600" },
  sectionHeader: {
    paddingHorizontal: 14, paddingVertical: 8,
    borderLeftWidth: 4, borderBottomWidth: 1, borderBottomColor: "#e0e0e0",
  },
  sectionHeaderText: { fontSize: 11, fontWeight: "700", textTransform: "uppercase", color: "#37474f" },
  findingRow: { backgroundColor: "#fff", paddingHorizontal: 14, paddingVertical: 10 },
  findingTop: { flexDirection: "row", gap: 8, marginBottom: 4, flexWrap: "wrap" },
  ruleName: { fontSize: 11, color: "#1565c0", fontWeight: "600" },
  ifcType: { fontSize: 11, color: "#546e7a" },
  message: { fontSize: 13, color: "#222" },
  elementName: { fontSize: 12, color: "#555", marginTop: 4 },
  guid: { fontSize: 10, color: "#aaa", fontFamily: "monospace", marginTop: 2 },
  sep: { height: 1, backgroundColor: "#f5f5f5" },
  noFindingsText: { fontSize: 16, color: "#388e3c", fontWeight: "600" },
  errorText: { fontSize: 14, color: "#c62828", textAlign: "center" },
});
