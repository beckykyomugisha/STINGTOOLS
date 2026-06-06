// Shared recording helpers + in-app player modal, used by the meeting-detail Recordings
// section (P1) and the project Recordings archive (P3). Web renders a real HTML5
// <video> (mp4) / <audio> (audio-only egress) via React.createElement streaming the
// short-lived presigned URL; native falls back to the device player via Linking.
import React from "react";
import { View, Text, Modal, TouchableOpacity, Platform, Linking } from "react-native";

export interface PlayableRecording {
  kind: string;
  downloadUrl?: string | null;
  startedAt: string;
}

export function fmtDur(s?: number | null): string {
  if (!s || s <= 0) return "—";
  const m = Math.floor(s / 60), ss = Math.round(s % 60);
  return `${m}:${String(ss).padStart(2, "0")}`;
}
export function fmtSize(b?: number | null): string {
  if (!b || b <= 0) return "—";
  return b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${(b / 1024).toFixed(0)} KB`;
}
export function isAudioKind(kind: string): boolean {
  return kind === "audio-only" || kind === "audio";
}

export function RecordingPlayerModal({ rec, onClose }: { rec: PlayableRecording | null; onClose: () => void }) {
  if (!rec) return null;
  const audio = isAudioKind(rec.kind);
  const url = rec.downloadUrl || "";
  return (
    <Modal visible transparent animationType="fade" onRequestClose={onClose}>
      <View style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.75)", justifyContent: "center", padding: 16 }}>
        <View style={{ backgroundColor: "#1a1d24", borderRadius: 10, padding: 14, width: "100%", maxWidth: 720, alignSelf: "center" }}>
          <View style={{ flexDirection: "row", alignItems: "center", marginBottom: 10 }}>
            <Text style={{ color: "#fff", fontWeight: "600", flex: 1 }}>
              {audio ? "🎙 Audio recording" : "🎥 Recording"} · {new Date(rec.startedAt).toLocaleString()}
            </Text>
            <TouchableOpacity onPress={onClose}><Text style={{ color: "#9aa3b2", fontSize: 18 }}>✕</Text></TouchableOpacity>
          </View>
          {Platform.OS === "web"
            ? React.createElement(audio ? "audio" : "video", {
                src: url, controls: true, autoPlay: true,
                style: { width: "100%", maxHeight: 440, background: "#000", borderRadius: 8 },
              } as any)
            : (
              <TouchableOpacity onPress={() => url && Linking.openURL(url)}
                style={{ backgroundColor: "#1976d2", paddingVertical: 12, borderRadius: 8, alignItems: "center" }}>
                <Text style={{ color: "#fff", fontWeight: "600" }}>▶ Open in player</Text>
              </TouchableOpacity>
            )}
          <TouchableOpacity onPress={() => url && Linking.openURL(url)} style={{ marginTop: 12 }}>
            <Text style={{ color: "#1976d2" }}>⬇ Download</Text>
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}
