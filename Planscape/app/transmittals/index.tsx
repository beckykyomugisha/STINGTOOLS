import { View, Text, StyleSheet } from "react-native";
import { listTransmittals } from "@/api/endpoints";
import { CoordinationListScreen } from "@/components/CoordinationListScreen";
import type { Transmittal } from "@/types/api";

export default function TransmittalsScreen() {
  return (
    <CoordinationListScreen<Transmittal>
      title="Transmittals"
      emptyTitle="No transmittals yet"
      emptyBody="Transmittals sent from Revit or the web dashboard appear here."
      fetch={listTransmittals}
      keyExtractor={(t) => t.id}
      renderRow={(t) => (
        <View style={styles.row}>
          <View style={styles.left}>
            <Text style={styles.code}>{t.transmittalCode ?? t.code ?? t.id.slice(0, 8)}</Text>
            <View style={[styles.statusChip, statusColor(t.status)]}>
              <Text style={styles.statusText}>{t.status ?? "DRAFT"}</Text>
            </View>
          </View>
          <Text style={styles.title} numberOfLines={1}>{t.title ?? "(no title)"}</Text>
          <Text style={styles.meta} numberOfLines={1}>
            To: {t.recipients ?? "—"}
          </Text>
          <Text style={styles.meta}>
            {t.sentAt ? `Sent ${new Date(t.sentAt).toLocaleDateString()}` :
             t.createdAt ? `Draft ${new Date(t.createdAt).toLocaleDateString()}` : ""}
          </Text>
        </View>
      )}
    />
  );
}

function statusColor(status?: string) {
  switch (status) {
    case "SENT": case "DELIVERED": return { backgroundColor: "#2e7d32" };
    case "DRAFT":                   return { backgroundColor: "#999" };
    case "SUPERSEDED":              return { backgroundColor: "#ef6c00" };
    case "RECEIVED":                return { backgroundColor: "#1976d2" };
    default:                        return { backgroundColor: "#666" };
  }
}

const styles = StyleSheet.create({
  row: { padding: 14, backgroundColor: "#fff" },
  left: { flexDirection: "row", alignItems: "center", marginBottom: 4 },
  code: { fontFamily: "monospace", fontSize: 13, fontWeight: "600", color: "#222", marginRight: 8 },
  statusChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10 },
  statusText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  title: { fontSize: 15, fontWeight: "600", color: "#333" },
  meta: { fontSize: 12, color: "#666", marginTop: 2 },
});
