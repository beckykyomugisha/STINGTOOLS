import { View, Text, StyleSheet } from "react-native";
import { listMeetings } from "@/api/endpoints";
import { CoordinationListScreen } from "@/components/CoordinationListScreen";
import type { Meeting } from "@/types/api";

export default function MeetingsScreen() {
  return (
    <CoordinationListScreen<Meeting>
      title="Meetings"
      emptyTitle="No meetings scheduled"
      emptyBody="Meetings scheduled by the BIM coordinator appear here, with action items you can tick off on site."
      fetch={listMeetings}
      keyExtractor={(m) => m.id}
      renderRow={(m) => {
        const scheduledAt = m.scheduledAt ?? (m as any).scheduledDate;
        const isPast = scheduledAt ? new Date(scheduledAt) < new Date() : false;
        return (
          <View style={styles.row}>
            <View style={styles.top}>
              <View style={[styles.typeChip, typeColor(m.type)]}>
                <Text style={styles.typeText}>{(m.type ?? "MEETING").toUpperCase()}</Text>
              </View>
              {isPast && <Text style={styles.pastBadge}>PAST</Text>}
            </View>
            <Text style={styles.title}>{m.title ?? "(untitled)"}</Text>
            <Text style={styles.meta}>
              {scheduledAt ? new Date(scheduledAt).toLocaleString() : "Unscheduled"}
              {m.durationMinutes ? ` · ${m.durationMinutes} min` : ""}
            </Text>
            {m.location && <Text style={styles.meta}>📍 {m.location}</Text>}
          </View>
        );
      }}
    />
  );
}

function typeColor(type?: string) {
  switch (type) {
    case "COORDINATION": return { backgroundColor: "#1976d2" };
    case "DESIGN_REVIEW": return { backgroundColor: "#6a1b9a" };
    case "CLIENT_REVIEW": return { backgroundColor: "#c2185b" };
    case "HANDOVER": return { backgroundColor: "#2e7d32" };
    case "CLASH_RESOLUTION": return { backgroundColor: "#ef6c00" };
    default: return { backgroundColor: "#666" };
  }
}

const styles = StyleSheet.create({
  row: { padding: 14, backgroundColor: "#fff" },
  top: { flexDirection: "row", alignItems: "center", marginBottom: 6 },
  typeChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10 },
  typeText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  pastBadge: { marginLeft: 8, fontSize: 10, color: "#999", fontWeight: "700" },
  title: { fontSize: 15, fontWeight: "600", color: "#222" },
  meta: { fontSize: 12, color: "#666", marginTop: 2 },
});
