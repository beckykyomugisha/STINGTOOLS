import { View, Text, StyleSheet } from "react-native";
import { listWarnings } from "@/api/endpoints";
import { CoordinationListScreen } from "@/components/CoordinationListScreen";
import type { WarningRecord } from "@/types/api";

export default function WarningsScreen() {
  return (
    <CoordinationListScreen<WarningRecord>
      title="Warnings"
      emptyTitle="No warnings reported"
      emptyBody="Revit warnings published by the plugin's Warnings Manager appear here, grouped by category."
      fetch={listWarnings}
      keyExtractor={(w) => w.id}
      renderRow={(w) => {
        const severity = (w.severity ?? "MEDIUM").toUpperCase();
        const sevStyle = severityStyle(severity);
        return (
          <View style={styles.row}>
            <View style={styles.top}>
              <View style={[styles.sevChip, sevStyle.chip]}>
                <Text style={styles.sevText}>{severity}</Text>
              </View>
              <Text style={styles.category}>{w.category ?? "UNCATEGORISED"}</Text>
              {w.elementCount != null && (
                <Text style={styles.count}>· {w.elementCount} elements</Text>
              )}
            </View>
            <Text style={styles.desc} numberOfLines={2}>{w.description ?? "(no description)"}</Text>
            {w.autoFixStrategy && (
              <Text style={styles.fix}>Auto-fix: {w.autoFixStrategy}</Text>
            )}
            <Text style={styles.meta}>
              {w.firstSeen ? `First seen ${new Date(w.firstSeen).toLocaleDateString()}` : ""}
              {w.discipline ? ` · ${w.discipline}` : ""}
            </Text>
          </View>
        );
      }}
    />
  );
}

function severityStyle(sev: string) {
  switch (sev) {
    case "CRITICAL": return { chip: { backgroundColor: "#d32f2f" } };
    case "HIGH":     return { chip: { backgroundColor: "#f57c00" } };
    case "MEDIUM":   return { chip: { backgroundColor: "#fbc02d" } };
    case "LOW":      return { chip: { backgroundColor: "#7cb342" } };
    default:         return { chip: { backgroundColor: "#666" } };
  }
}

const styles = StyleSheet.create({
  row: { padding: 14, backgroundColor: "#fff" },
  top: { flexDirection: "row", alignItems: "center", marginBottom: 6, flexWrap: "wrap" },
  sevChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10, marginRight: 8 },
  sevText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  category: { fontSize: 12, color: "#666", fontWeight: "600", textTransform: "uppercase" },
  count: { fontSize: 12, color: "#888", marginLeft: 4 },
  desc: { fontSize: 14, color: "#222", marginTop: 2 },
  fix: { fontSize: 12, color: "#2e7d32", marginTop: 4 },
  meta: { fontSize: 11, color: "#999", marginTop: 4 },
});
