// P3/P4 — project Recordings archive: EVERY meeting recording in the project (newest
// first), covering both scheduled-meeting recordings AND ad-hoc live-session recordings
// (labelled by date/host). ▶ Play opens the shared in-app player; ⬇ downloads. Members-
// only + presigned URLs are enforced server-side (GET /api/projects/{id}/recordings).
import { useState, useEffect, useCallback } from "react";
import { View, Text, StyleSheet, TouchableOpacity, ScrollView, ActivityIndicator, Linking } from "react-native";
import { getProjectRecordings, type ProjectRecording } from "@/api/endpoints";
import { useProjectStore } from "@/stores/projectStore";
import { RecordingPlayerModal, fmtDur, fmtSize, isAudioKind } from "@/components/RecordingPlayer";

export default function RecordingsScreen() {
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [recordings, setRecordings] = useState<ProjectRecording[]>([]);
  const [loading, setLoading] = useState(true);
  const [play, setPlay] = useState<ProjectRecording | null>(null);

  const load = useCallback(async () => {
    if (!projectId) { setLoading(false); return; }
    try {
      const r = await getProjectRecordings(projectId);
      setRecordings(r.recordings);
    } catch { /* none / not configured */ }
    finally { setLoading(false); }
  }, [projectId]);
  useEffect(() => { load(); }, [load]);

  if (!projectId) return (
    <View style={styles.center}><Text style={styles.emptyTitle}>No project selected</Text>
      <Text style={styles.emptyBody}>Open the Dashboard and pick a project first.</Text></View>
  );
  if (loading) return <View style={styles.center}><ActivityIndicator /></View>;

  return (
    <ScrollView style={styles.root} contentContainerStyle={{ paddingBottom: 100 }}>
      <Text style={styles.h1}>Recordings</Text>
      <Text style={styles.sub}>All meeting & ad-hoc session recordings in this project (newest first).</Text>
      {recordings.length === 0 && (
        <View style={styles.card}><Text style={styles.emptyBody}>No recordings yet. Record a live meeting and it will appear here.</Text></View>
      )}
      {recordings.map((r) => {
        const playable = r.status === "COMPLETE" && !!r.downloadUrl;
        return (
          <View key={r.id} style={styles.card}>
            <View style={{ flexDirection: "row", alignItems: "center" }}>
              <Text style={styles.title} numberOfLines={1}>
                {isAudioKind(r.kind) ? "🎙" : "🎥"} {r.label}{r.adHoc ? "" : ""}
              </Text>
              {r.adHoc && <View style={styles.adhocChip}><Text style={styles.adhocText}>AD-HOC</Text></View>}
            </View>
            <Text style={styles.meta}>
              {new Date(r.startedAt).toLocaleString()} · {fmtDur(r.durationSeconds)} · {fmtSize(r.fileSizeBytes)} · {r.status}
            </Text>
            <View style={{ flexDirection: "row", marginTop: 8 }}>
              {playable ? (
                <>
                  <TouchableOpacity onPress={() => setPlay(r)} style={styles.playBtn}>
                    <Text style={styles.playText}>▶ Play</Text>
                  </TouchableOpacity>
                  <TouchableOpacity onPress={() => Linking.openURL(r.downloadUrl!)} style={styles.dlBtn}>
                    <Text style={styles.dlText}>⬇ Download</Text>
                  </TouchableOpacity>
                </>
              ) : (
                <Text style={styles.pending}>{r.status === "ACTIVE" || r.status === "STARTING" ? "recording…" : r.status}</Text>
              )}
            </View>
          </View>
        );
      })}
      <RecordingPlayerModal rec={play} onClose={() => setPlay(null)} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: "#0f1115", padding: 14 },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 24, backgroundColor: "#0f1115" },
  h1: { color: "#fff", fontSize: 20, fontWeight: "700", marginBottom: 2 },
  sub: { color: "#9aa3b2", fontSize: 12, marginBottom: 12 },
  card: { backgroundColor: "#1a1d24", borderRadius: 8, padding: 12, marginBottom: 10 },
  title: { color: "#e6e6e6", fontSize: 14, fontWeight: "600", flex: 1 },
  meta: { color: "#9aa3b2", fontSize: 12, marginTop: 4 },
  playBtn: { backgroundColor: "#1976d2", paddingVertical: 8, paddingHorizontal: 16, borderRadius: 6, marginRight: 10 },
  playText: { color: "#fff", fontWeight: "600", fontSize: 13 },
  dlBtn: { paddingVertical: 8, paddingHorizontal: 12, borderRadius: 6, borderWidth: 1, borderColor: "rgba(255,255,255,0.18)" },
  dlText: { color: "#1976d2", fontSize: 13 },
  pending: { color: "#9aa3b2", fontSize: 12 },
  adhocChip: { backgroundColor: "rgba(230,81,0,0.18)", borderRadius: 4, paddingHorizontal: 6, paddingVertical: 1, marginLeft: 8 },
  adhocText: { color: "#e65100", fontSize: 10, fontWeight: "700" },
  emptyTitle: { color: "#fff", fontSize: 16, fontWeight: "600", marginBottom: 6 },
  emptyBody: { color: "#9aa3b2", fontSize: 13, textAlign: "center" },
});
