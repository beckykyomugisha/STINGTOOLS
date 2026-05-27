// P2 — Issue detail with comment thread.
//
// Lists metadata + attachments + real-time comments. Accepts new comment
// text and @mentions (future). Subscribes to SignalR CommentAdded events to
// show fresh replies without pull-to-refresh.

import { useEffect, useState, useRef } from "react";
import {
  View,
  Text,
  ScrollView,
  TextInput,
  TouchableOpacity,
  KeyboardAvoidingView,
  Platform,
  StyleSheet,
  ActivityIndicator,
  Alert,
} from "react-native";
import { useLocalSearchParams, Stack } from "expo-router";
import { useProjectStore } from "@/stores/projectStore";
import { apiFetch } from "@/api/client";
import {
  listIssueComments,
  addIssueComment,
  type IssueComment,
} from "@/api/endpoints";
import type { BimIssue } from "@/types/api";
import { theme, getPriorityColor } from "@/utils/theme";
import { realtime } from "@/services/realtimeClient";

export default function IssueDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useProjectStore((s) => s.activeProjectId);

  const [issue, setIssue] = useState<BimIssue | null>(null);
  const [comments, setComments] = useState<IssueComment[]>([]);
  const [loading, setLoading] = useState(true);
  const [draft, setDraft] = useState("");
  const [posting, setPosting] = useState(false);
  const scrollRef = useRef<ScrollView>(null);

  useEffect(() => {
    if (!projectId || !id) return;
    (async () => {
      try {
        const [iss, cmts] = await Promise.all([
          apiFetch<BimIssue>(`/api/projects/${projectId}/issues/${id}`),
          listIssueComments(projectId, id),
        ]);
        setIssue(iss);
        setComments(cmts);
      } catch (err) {
        Alert.alert("Load failed", String(err));
      } finally {
        setLoading(false);
      }
    })();
  }, [projectId, id]);

  // Real-time comment feed — subscribe to the global SignalR handler if present.
  useEffect(() => {
    if (!projectId || !id || !realtime?.on) return;
    const unsub = realtime.on("CommentAdded", (payload: any) => {
      if (payload?.issueId !== id) return;
      setComments((prev) =>
        prev.some((c) => c.id === payload.comment?.id) ? prev : [...prev, payload.comment]
      );
      scrollRef.current?.scrollToEnd({ animated: true });
    });
    return unsub;
  }, [projectId, id]);

  async function handleSend() {
    if (!projectId || !id || !draft.trim() || posting) return;
    const text = draft.trim();
    setPosting(true);
    setDraft("");
    try {
      const newComment = await addIssueComment(projectId, id, text);
      setComments((prev) =>
        prev.some((c) => c.id === newComment.id) ? prev : [...prev, newComment]
      );
      scrollRef.current?.scrollToEnd({ animated: true });
    } catch (err) {
      Alert.alert("Send failed", String(err));
      setDraft(text); // restore
    } finally {
      setPosting(false);
    }
  }

  if (loading) {
    return <View style={styles.center}><ActivityIndicator /></View>;
  }
  if (!issue) {
    return <View style={styles.center}><Text>Issue not found.</Text></View>;
  }

  return (
    <KeyboardAvoidingView
      style={{ flex: 1 }}
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      keyboardVerticalOffset={80}
    >
      <Stack.Screen options={{ title: issue.issueCode || "Issue" }} />
      <ScrollView ref={scrollRef} style={styles.body} contentContainerStyle={{ paddingBottom: 16 }}>
        <View style={styles.header}>
          <View style={styles.tagRow}>
            <View style={[styles.priorityChip, { backgroundColor: getPriorityColor(issue.priority) }]}>
              <Text style={styles.chipText}>{issue.priority}</Text>
            </View>
            <View style={[styles.statusChip, styles["status_" + issue.status.toLowerCase()]]}>
              <Text style={styles.chipText}>{issue.status}</Text>
            </View>
            <Text style={styles.meta}>{issue.type}</Text>
          </View>
          <Text style={styles.title}>{issue.title}</Text>
          {issue.description ? (
            <Text style={styles.description}>{issue.description}</Text>
          ) : null}
          <View style={styles.metaRow}>
            {issue.assignee && <Text style={styles.metaSmall}>To: {issue.assignee}</Text>}
            {issue.discipline && <Text style={styles.metaSmall}>· {issue.discipline}</Text>}
            <Text style={styles.metaSmall}>· {new Date(issue.createdAt).toLocaleDateString()}</Text>
            {issue.isOverdue && <Text style={styles.overdue}>OVERDUE</Text>}
          </View>
        </View>

        <View style={styles.commentsSection}>
          <Text style={styles.commentsHeader}>
            {comments.length === 0 ? "No comments yet" : `${comments.length} comment${comments.length === 1 ? "" : "s"}`}
          </Text>
          {comments.map((c) => <CommentRow key={c.id} comment={c} />)}
        </View>
      </ScrollView>

      <View style={styles.composer}>
        <TextInput
          value={draft}
          onChangeText={setDraft}
          placeholder="Add a comment…"
          multiline
          style={styles.composerInput}
        />
        <TouchableOpacity
          onPress={handleSend}
          disabled={!draft.trim() || posting}
          style={[styles.sendBtn, (!draft.trim() || posting) && { opacity: 0.4 }]}
        >
          {posting
            ? <ActivityIndicator color="#fff" />
            : <Text style={styles.sendBtnText}>Send</Text>}
        </TouchableOpacity>
      </View>
    </KeyboardAvoidingView>
  );
}

function CommentRow({ comment }: { comment: IssueComment }) {
  return (
    <View style={styles.comment}>
      <View style={styles.commentHeader}>
        <Text style={styles.commentAuthor}>{comment.authorName}</Text>
        <Text style={styles.commentWhen}>
          {new Date(comment.createdAt).toLocaleString()}
          {comment.editedAt ? " · edited" : ""}
        </Text>
      </View>
      <Text style={styles.commentBody}>{comment.body}</Text>
      {comment.source && (
        <Text style={styles.commentSource}>via {comment.source}</Text>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 40 },
  body: { flex: 1, backgroundColor: "#f5f6f8" },

  header: { backgroundColor: "#fff", padding: 16, borderBottomWidth: 1, borderColor: "#eee" },
  tagRow: { flexDirection: "row", alignItems: "center", marginBottom: 8 },
  priorityChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10, marginRight: 6 },
  statusChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10, marginRight: 8 },
  chipText: { color: "#fff", fontSize: 10, fontWeight: "700" },
  status_open: { backgroundColor: "#1976d2" },
  status_in_progress: { backgroundColor: "#6a1b9a" },
  status_resolved: { backgroundColor: "#558b2f" },
  status_closed: { backgroundColor: "#2e7d32" },

  title: { fontSize: 18, fontWeight: "700", color: "#222" },
  description: { fontSize: 14, color: "#444", marginTop: 8, lineHeight: 20 },
  metaRow: { flexDirection: "row", flexWrap: "wrap", marginTop: 12 },
  meta: { fontSize: 12, color: "#666", fontWeight: "600" },
  metaSmall: { fontSize: 11, color: "#888", marginRight: 4 },
  overdue: { fontSize: 11, color: "#d32f2f", fontWeight: "700", marginLeft: 4 },

  commentsSection: { marginTop: 16, paddingHorizontal: 16 },
  commentsHeader: { fontSize: 12, color: "#888", textTransform: "uppercase", letterSpacing: 0.5, marginBottom: 12 },
  comment: { backgroundColor: "#fff", padding: 12, borderRadius: 8, marginBottom: 8 },
  commentHeader: { flexDirection: "row", justifyContent: "space-between", marginBottom: 4 },
  commentAuthor: { fontSize: 13, fontWeight: "600", color: "#222" },
  commentWhen: { fontSize: 11, color: "#999" },
  commentBody: { fontSize: 14, color: "#333", lineHeight: 20 },
  commentSource: { fontSize: 10, color: "#aaa", marginTop: 4, fontStyle: "italic" },

  composer: { flexDirection: "row", padding: 8, backgroundColor: "#fff", borderTopWidth: 1, borderColor: "#eee", alignItems: "flex-end" },
  composerInput: { flex: 1, maxHeight: 120, borderWidth: 1, borderColor: "#ddd", borderRadius: 8, padding: 10, fontSize: 14, backgroundColor: "#fff" },
  sendBtn: { marginLeft: 8, paddingVertical: 12, paddingHorizontal: 16, backgroundColor: "#E8912D", borderRadius: 8 },
  sendBtnText: { color: "#fff", fontWeight: "700" },
});
