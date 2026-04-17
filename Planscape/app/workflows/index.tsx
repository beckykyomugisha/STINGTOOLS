import { View, Text, StyleSheet } from "react-native";
import { listWorkflowRuns } from "@/api/endpoints";
import { CoordinationListScreen } from "@/components/CoordinationListScreen";
import type { WorkflowRun } from "@/types/api";

export default function WorkflowsScreen() {
  return (
    <CoordinationListScreen<WorkflowRun>
      title="Workflows"
      emptyTitle="No workflow runs yet"
      emptyBody="Workflow executions from the Revit plugin show here — pass / fail counts, duration, compliance delta."
      fetch={listWorkflowRuns}
      keyExtractor={(r) => r.id}
      renderRow={(r) => {
        const delta = (r.complianceAfter ?? 0) - (r.complianceBefore ?? 0);
        const deltaColor = delta > 0 ? "#2e7d32" : delta < 0 ? "#d32f2f" : "#666";
        return (
          <View style={styles.row}>
            <Text style={styles.preset}>{r.preset ?? r.presetName ?? "workflow"}</Text>
            <View style={styles.counts}>
              <Count label="✓" value={r.stepsPassed ?? 0} color="#2e7d32" />
              <Count label="✗" value={r.stepsFailed ?? 0} color="#d32f2f" />
              <Count label="→" value={r.stepsSkipped ?? 0} color="#999" />
              {r.durationMs != null && (
                <Text style={styles.duration}>{(r.durationMs / 1000).toFixed(1)}s</Text>
              )}
            </View>
            {(r.complianceBefore != null || r.complianceAfter != null) && (
              <Text style={[styles.delta, { color: deltaColor }]}>
                {(r.complianceBefore ?? 0).toFixed(0)}% → {(r.complianceAfter ?? 0).toFixed(0)}%
                {" ("}{delta >= 0 ? "+" : ""}{delta.toFixed(1)}{"%)"}
              </Text>
            )}
            <Text style={styles.meta}>
              {r.executedAt ? new Date(r.executedAt).toLocaleString() : ""}
              {r.executedBy ? ` · ${r.executedBy}` : ""}
            </Text>
          </View>
        );
      }}
    />
  );
}

function Count({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <View style={styles.countBadge}>
      <Text style={[styles.countLabel, { color }]}>{label}</Text>
      <Text style={styles.countValue}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { padding: 14, backgroundColor: "#fff" },
  preset: { fontSize: 15, fontWeight: "600", color: "#222" },
  counts: { flexDirection: "row", marginTop: 6, alignItems: "center" },
  countBadge: { flexDirection: "row", marginRight: 12 },
  countLabel: { fontWeight: "700", marginRight: 4 },
  countValue: { color: "#333" },
  duration: { fontSize: 11, color: "#888", marginLeft: "auto" },
  delta: { fontSize: 12, fontWeight: "600", marginTop: 6 },
  meta: { fontSize: 11, color: "#999", marginTop: 4 },
});
