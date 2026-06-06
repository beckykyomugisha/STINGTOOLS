import { useState, useEffect, useCallback, useRef } from "react";
import {
  View, Text, StyleSheet, TouchableOpacity, Modal, TextInput, ScrollView,
  Alert, ActivityIndicator, Platform, KeyboardAvoidingView, FlatList, Share,
} from "react-native";
import { router } from "expo-router";
import {
  listMeetings, getMeeting, createMeeting, updateMeeting,
  logMeetingMinutes, addMeetingAction, updateMeetingAction, listOpenMeetingActions,
  listMeetingAttendees, addMeetingAttendee, updateMeetingAttendee, deleteMeetingAttendee,
  addMeetingAgendaItem, updateMeetingAgendaItem, deleteMeetingAgendaItem,
  exportMeetingMinutesDoc, getMeetingIcsUrl, startLiveSession, getMeetingLiveArtifacts,
  type MeetingActionItem, type MeetingAttendee, type MeetingAgendaItem, type MeetingLiveArtifacts,
} from "@/api/endpoints";
import { MemberPicker } from "@/components/MemberPicker";
import { useProjectStore } from "@/stores/projectStore";
import type { Meeting, ProjectMember } from "@/types/api";

// ── Tab type ──────────────────────────────────────────────────────────────────
type DetailTab = "overview" | "agenda" | "actions" | "attendees";

// ── Root screen ───────────────────────────────────────────────────────────────
export default function MeetingsScreen() {
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [meetings, setMeetings] = useState<Meeting[]>([]);
  const [openActions, setOpenActions] = useState<MeetingActionItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<Meeting | null>(null);
  const [createVisible, setCreateVisible] = useState(false);

  const load = useCallback(async () => {
    if (!projectId) { setLoading(false); return; }
    try {
      const [m, a] = await Promise.all([
        listMeetings(projectId),
        listOpenMeetingActions(projectId).catch(() => [] as MeetingActionItem[]),
      ]);
      setMeetings(m);
      setOpenActions(a);
    } catch (err) {
      Alert.alert("Load failed", err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  if (!projectId) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyTitle}>No project selected</Text>
        <Text style={styles.emptyBody}>Open the Dashboard and pick a project first.</Text>
      </View>
    );
  }
  if (loading) return <View style={styles.center}><ActivityIndicator /></View>;

  const upcoming = meetings.filter((m) => {
    const t = m.scheduledAt;
    return t ? new Date(t) >= new Date() : false;
  });
  const past = meetings.filter((m) => !upcoming.includes(m));

  return (
    <>
      <ScrollView style={styles.root} contentContainerStyle={{ paddingBottom: 100 }}>
        {openActions.length > 0 && (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Open Actions ({openActions.length})</Text>
            {openActions.slice(0, 10).map((a) => (
              <ActionRow
                key={a.id}
                action={a}
                onTick={async () => {
                  if (!projectId || !a.meetingId) {
                    Alert.alert("Missing link", "Open the meeting detail to close this action.");
                    return;
                  }
                  try {
                    await updateMeetingAction(projectId, a.meetingId, a.id, { status: "CLOSED" });
                    load();
                  } catch (err) {
                    Alert.alert("Close failed", err instanceof Error ? err.message : String(err));
                  }
                }}
                onEscalate={() => {
                  router.push({
                    pathname: "/(tabs)/issues",
                    params: {
                      createForElement: a.linkedIssueId ?? "",
                      elementTag: `ACTION: ${a.description.slice(0, 30)}`,
                      projectId,
                    },
                  });
                }}
              />
            ))}
          </View>
        )}

        <View style={styles.section}>
          <Text style={styles.sectionTitle}>Upcoming ({upcoming.length})</Text>
          {upcoming.length === 0 ? (
            <Text style={styles.emptyBody}>No meetings scheduled. Tap + to draft one.</Text>
          ) : upcoming.map((m) => (
            <MeetingRow key={m.id} meeting={m} onPress={() => setSelected(m)} />
          ))}
        </View>

        {past.length > 0 && (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Past ({past.length})</Text>
            {past.slice(0, 20).map((m) => (
              <MeetingRow key={m.id} meeting={m} onPress={() => setSelected(m)} isPast />
            ))}
          </View>
        )}
      </ScrollView>

      <TouchableOpacity style={styles.fab} onPress={() => setCreateVisible(true)}
        accessibilityRole="button" accessibilityLabel="Draft a new meeting">
        <Text style={styles.fabText}>+</Text>
      </TouchableOpacity>

      {selected && (
        <MeetingDetailModal
          meeting={selected}
          projectId={projectId}
          onClose={() => { setSelected(null); load(); }}
        />
      )}
      <CreateMeetingModal
        visible={createVisible}
        projectId={projectId}
        onClose={() => setCreateVisible(false)}
        onCreated={() => { setCreateVisible(false); load(); }}
      />
    </>
  );
}

// ── Meeting list row ──────────────────────────────────────────────────────────
function MeetingRow({ meeting, onPress, isPast = false }: {
  meeting: Meeting; onPress: () => void; isPast?: boolean;
}) {
  const type = meeting.meetingType ?? meeting.type ?? "MEETING";
  const t = meeting.scheduledAt;
  const statusColor = meeting.status === "COMPLETED" ? "#2e7d32"
    : meeting.status === "CANCELLED" ? "#999"
    : meeting.status === "IN_PROGRESS" ? "#e65100"
    : "#1976d2";

  return (
    <TouchableOpacity style={[styles.meetingRow, isPast && { opacity: 0.75 }]} onPress={onPress} activeOpacity={0.7}>
      <View style={styles.meetingTop}>
        <View style={[styles.typeChip, typeColor(type)]}>
          <Text style={styles.typeText}>{type.replace(/_/g, " ").toUpperCase()}</Text>
        </View>
        <View style={[styles.statusDot, { backgroundColor: statusColor }]} />
        {meeting.liveSessionId && (
          <View style={styles.liveBadge}><Text style={styles.liveBadgeText}>● LIVE</Text></View>
        )}
        {typeof meeting.actionItemCount === "number" && meeting.actionItemCount > 0 && (
          <Text style={styles.actionCount}>{meeting.actionItemCount} actions</Text>
        )}
      </View>
      <Text style={styles.meetingTitle} numberOfLines={1}>{meeting.title ?? "(untitled)"}</Text>
      <Text style={styles.meta}>
        {t ? new Date(t).toLocaleString() : "Unscheduled"}
        {meeting.location ? ` · ${meeting.location}` : ""}
      </Text>
    </TouchableOpacity>
  );
}

// ── Action row ────────────────────────────────────────────────────────────────
function ActionRow({ action, onTick, onEscalate }: {
  action: MeetingActionItem; onTick: () => void; onEscalate: () => void;
}) {
  const overdue = action.isOverdue ?? (action.dueDate ? new Date(action.dueDate) < new Date() : false);
  const priorityColor = action.priority === "CRITICAL" ? "#d32f2f"
    : action.priority === "HIGH" ? "#e65100"
    : action.priority === "LOW" ? "#388e3c" : "#1976d2";

  return (
    <View style={[styles.actionRow, overdue && styles.actionRowOverdue]}>
      <TouchableOpacity style={styles.tickBox} onPress={onTick}
        accessibilityLabel={`Mark action complete: ${action.description}`}>
        <Text style={{ fontSize: 16 }}>○</Text>
      </TouchableOpacity>
      <View style={{ flex: 1 }}>
        <Text style={styles.actionDesc} numberOfLines={2}>{action.description}</Text>
        <Text style={styles.actionMeta}>
          <Text style={{ color: priorityColor }}>{action.priority ?? "MEDIUM"} · </Text>
          {action.assignee ?? "Unassigned"}
          {action.dueDate ? ` · due ${new Date(action.dueDate).toLocaleDateString()}` : ""}
          {overdue ? " · OVERDUE" : ""}
        </Text>
      </View>
      <TouchableOpacity onPress={onEscalate} style={styles.escalateBtn}>
        <Text style={styles.escalateText}>→ NCR</Text>
      </TouchableOpacity>
    </View>
  );
}

// ── Meeting detail modal (tabbed) ─────────────────────────────────────────────
function MeetingDetailModal({ meeting: initialMeeting, projectId, onClose }: {
  meeting: Meeting; projectId: string; onClose: () => void;
}) {
  const [meeting, setMeeting] = useState<Meeting>(initialMeeting);
  const [tab, setTab] = useState<DetailTab>("overview");
  const [attendees, setAttendees] = useState<MeetingAttendee[]>([]);
  const [agenda, setAgenda] = useState<MeetingAgendaItem[]>([]);
  const [actions, setActions] = useState<MeetingActionItem[]>([]);
  const [loadingDetail, setLoadingDetail] = useState(false);

  const loadDetail = useCallback(async () => {
    setLoadingDetail(true);
    try {
      const [full, att, ag] = await Promise.all([
        getMeeting(projectId, initialMeeting.id),
        listMeetingAttendees(projectId, initialMeeting.id),
        listOpenMeetingActions(projectId).catch(() => [] as MeetingActionItem[]),
      ]);
      setMeeting(full);
      setAttendees(att);
      setActions(ag.filter((a) => a.meetingId === initialMeeting.id));
    } catch {
      // silently fall back to initial data
    } finally {
      setLoadingDetail(false);
    }
  }, [projectId, initialMeeting.id]);

  useEffect(() => { loadDetail(); }, [loadDetail]);

  async function handleExportIcs() {
    const url = getMeetingIcsUrl(projectId, meeting.id);
    try {
      await Share.share({ url, message: `Meeting invite: ${meeting.title}` });
    } catch {
      Alert.alert("Share unavailable", "Copy the ICS URL manually: " + url);
    }
  }

  async function handleExportMinutes() {
    try {
      await exportMeetingMinutesDoc(projectId, meeting.id);
      Alert.alert("Minutes exported", "A Word document has been created in the project CDE.");
    } catch (err) {
      Alert.alert("Export failed", err instanceof Error ? err.message : String(err));
    }
  }

  const tabs: Array<{ key: DetailTab; label: string }> = [
    { key: "overview", label: "Overview" },
    { key: "agenda", label: "Agenda" },
    { key: "actions", label: `Actions${actions.length ? ` (${actions.length})` : ""}` },
    { key: "attendees", label: `Attendees${attendees.length ? ` (${attendees.length})` : ""}` },
  ];

  return (
    <Modal visible animationType="slide" onRequestClose={onClose}>
      <KeyboardAvoidingView style={{ flex: 1, backgroundColor: "#f5f5f5" }}
        behavior={Platform.OS === "ios" ? "padding" : "height"}>
        {/* Header */}
        <View style={styles.detailHeader}>
          <TouchableOpacity onPress={onClose} style={styles.backBtn}>
            <Text style={styles.backText}>← Back</Text>
          </TouchableOpacity>
          <View style={{ flex: 1 }}>
            <Text style={styles.detailTitle} numberOfLines={1}>{meeting.title}</Text>
            <Text style={styles.detailMeta}>
              {meeting.scheduledAt ? new Date(meeting.scheduledAt).toLocaleString() : ""}
              {meeting.location ? ` · ${meeting.location}` : ""}
            </Text>
          </View>
          <View style={[styles.statusBadge, { backgroundColor: statusColor(meeting.status) }]}>
            <Text style={styles.statusText}>{meeting.status}</Text>
          </View>
        </View>

        {/* Tab bar */}
        <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.tabBar}
          contentContainerStyle={{ paddingHorizontal: 12 }}>
          {tabs.map((t) => (
            <TouchableOpacity key={t.key} onPress={() => setTab(t.key)}
              style={[styles.tabPill, tab === t.key && styles.tabPillActive]}>
              <Text style={[styles.tabText, tab === t.key && styles.tabTextActive]}>{t.label}</Text>
            </TouchableOpacity>
          ))}
        </ScrollView>

        {loadingDetail && <ActivityIndicator style={{ marginTop: 16 }} />}

        {/* Tab content */}
        <ScrollView style={{ flex: 1 }} contentContainerStyle={{ padding: 16, paddingBottom: 80 }}>
          {tab === "overview" && (
            <OverviewTab
              meeting={meeting}
              projectId={projectId}
              onRefresh={loadDetail}
              onExportIcs={handleExportIcs}
              onExportMinutes={handleExportMinutes}
            />
          )}
          {tab === "agenda" && (
            <AgendaTab
              agenda={agenda}
              projectId={projectId}
              meetingId={meeting.id}
              onRefresh={loadDetail}
            />
          )}
          {tab === "actions" && (
            <ActionsTab
              actions={actions}
              projectId={projectId}
              meetingId={meeting.id}
              onRefresh={loadDetail}
            />
          )}
          {tab === "attendees" && (
            <AttendeesTab
              attendees={attendees}
              projectId={projectId}
              meetingId={meeting.id}
              onRefresh={loadDetail}
            />
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </Modal>
  );
}

// ── Overview tab ──────────────────────────────────────────────────────────────
function OverviewTab({ meeting, projectId, onRefresh, onExportIcs, onExportMinutes }: {
  meeting: Meeting; projectId: string; onRefresh: () => void;
  onExportIcs: () => void; onExportMinutes: () => void;
}) {
  const [minutes, setMinutes] = useState(meeting.minutes ?? "");
  const [saving, setSaving] = useState(false);
  const [editStatus, setEditStatus] = useState(meeting.status);
  const [joining, setJoining] = useState(false);
  // N5 — live artifacts (snapshots / attendance) captured against this meeting's session(s).
  const [artifacts, setArtifacts] = useState<MeetingLiveArtifacts | null>(null);
  useEffect(() => {
    let alive = true;
    getMeetingLiveArtifacts(projectId, meeting.id)
      .then((a) => { if (alive) setArtifacts(a); })
      .catch(() => { /* none yet */ });
    return () => { alive = false; };
  }, [projectId, meeting.id]);

  // N5 — start/join the LIVE A/V session bound to this scheduled meeting, then open
  // the live screen. Idempotent server-side: the FIRST join creates the session +
  // flips the meeting to IN_PROGRESS; everyone after joins the SAME session, so all
  // live artifacts (snapshots / actions / attendance) flow back to this one meeting.
  async function joinLive() {
    setJoining(true);
    try {
      const session = await startLiveSession(projectId, meeting.id);
      onRefresh();   // pick up IN_PROGRESS + liveSessionId
      router.push({ pathname: "/meetings/live", params: { project: projectId, session: session.sessionId } });
    } catch (err) {
      Alert.alert("Could not join", err instanceof Error ? err.message : String(err));
    } finally {
      setJoining(false);
    }
  }

  async function saveMinutes() {
    setSaving(true);
    try {
      await logMeetingMinutes(projectId, meeting.id, minutes, editStatus);
      Alert.alert("Saved", "Minutes logged.");
      onRefresh();
    } catch (err) {
      Alert.alert("Save failed", err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  const STATUSES = ["SCHEDULED", "IN_PROGRESS", "COMPLETED", "CANCELLED"];

  return (
    <View>
      {/* WS3e — live A/V meeting (camera/mic/screen-share). Replaces the
          "nothing to show yet" disconnect: scheduled meetings can go live. */}
      <View style={styles.card}>
        <Text style={styles.fieldLabel}>Live meeting{meeting.liveSessionId ? "  ● IN PROGRESS" : ""}</Text>
        <TouchableOpacity onPress={joinLive} disabled={joining}
          style={[styles.pillActive, { marginTop: 8, paddingVertical: 12, alignItems: "center", borderRadius: 8, opacity: joining ? 0.6 : 1 }]}>
          <Text style={{ color: "#fff", fontWeight: "600" }}>
            {joining ? "Starting…" : meeting.liveSessionId ? "🎥 Resume live A/V" : "🎥 Join live A/V"}
          </Text>
        </TouchableOpacity>
        <Text style={[styles.fieldValue, { color: "#9aa3b2", fontSize: 12, marginTop: 6 }]}>
          Camera, mic + screen-share over LiveKit. Needs a dev build on mobile (not Expo Go).
        </Text>
      </View>

      {/* N5 — live artifacts that flowed back from the live session(s): viewpoint /
          markup snapshots + attendance from the live roster. (Recording lands here
          once N2 / LiveKit Egress is deployed.) */}
      {artifacts && (artifacts.snapshots.length > 0 || artifacts.attendance.length > 0 || artifacts.sessions.length > 0) && (
        <View style={styles.card}>
          <Text style={styles.fieldLabel}>Live artifacts</Text>
          <Text style={[styles.fieldValue, { fontSize: 12, color: "#666", marginTop: 2 }]}>
            {artifacts.sessions.length} session(s) · {artifacts.snapshots.length} snapshot(s) · {artifacts.attendance.length} attended
          </Text>
          {artifacts.attendance.length > 0 && (
            <Text style={[styles.fieldValue, { fontSize: 12, marginTop: 6 }]} numberOfLines={2}>
              👥 {artifacts.attendance.map((a) => a.displayName).join(", ")}
            </Text>
          )}
          {artifacts.snapshots.slice(0, 5).map((s) => (
            <Text key={s.id} style={[styles.fieldValue, { fontSize: 12, marginTop: 4 }]} numberOfLines={1}>
              📸 {s.label || "Viewpoint"} · {new Date(s.capturedAt).toLocaleString()}
            </Text>
          ))}
        </View>
      )}

      {/* Meeting URL */}
      {meeting.meetingUrl && (
        <View style={styles.card}>
          <Text style={styles.fieldLabel}>Join Link</Text>
          <Text style={[styles.fieldValue, { color: "#1976d2" }]} numberOfLines={1}>{meeting.meetingUrl}</Text>
        </View>
      )}

      {/* Status */}
      <View style={styles.card}>
        <Text style={styles.fieldLabel}>Status</Text>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 6 }}>
          {STATUSES.map((s) => (
            <TouchableOpacity key={s} onPress={() => setEditStatus(s)}
              style={[styles.pill, editStatus === s && styles.pillActive]}>
              <Text style={[styles.pillText, editStatus === s && { color: "#fff" }]}>{s}</Text>
            </TouchableOpacity>
          ))}
        </ScrollView>
      </View>

      {/* Minutes */}
      <View style={styles.card}>
        <Text style={styles.fieldLabel}>Minutes</Text>
        <TextInput
          style={[styles.input, { minHeight: 140, textAlignVertical: "top" }]}
          multiline
          placeholder="Key decisions, open points, attendees..."
          value={minutes}
          onChangeText={setMinutes}
        />
        <TouchableOpacity style={[styles.saveBtn, saving && { opacity: 0.6 }]}
          onPress={saveMinutes} disabled={saving}>
          <Text style={styles.saveBtnText}>{saving ? "Saving…" : "Save Minutes & Status"}</Text>
        </TouchableOpacity>
      </View>

      {/* Export actions */}
      <View style={styles.card}>
        <Text style={styles.fieldLabel}>Export</Text>
        <View style={{ flexDirection: "row", gap: 10, marginTop: 8 }}>
          <TouchableOpacity style={[styles.exportBtn, { backgroundColor: "#1976d2" }]} onPress={onExportIcs}>
            <Text style={styles.exportBtnText}>📅 Add to Calendar (.ics)</Text>
          </TouchableOpacity>
          <TouchableOpacity style={[styles.exportBtn, { backgroundColor: "#6a1b9a" }]} onPress={onExportMinutes}>
            <Text style={styles.exportBtnText}>📄 Export Minutes (Word)</Text>
          </TouchableOpacity>
        </View>
      </View>
    </View>
  );
}

// ── Agenda tab ────────────────────────────────────────────────────────────────
function AgendaTab({ agenda, projectId, meetingId, onRefresh }: {
  agenda: MeetingAgendaItem[]; projectId: string; meetingId: string; onRefresh: () => void;
}) {
  const [title, setTitle] = useState("");
  const [duration, setDuration] = useState("");
  const [presenter, setPresenter] = useState("");
  const [adding, setAdding] = useState(false);
  const [showAdd, setShowAdd] = useState(false);

  async function addItem() {
    if (!title.trim()) return;
    setAdding(true);
    try {
      await addMeetingAgendaItem(projectId, meetingId, {
        title: title.trim(),
        durationMinutes: duration ? parseInt(duration, 10) : undefined,
        presenter: presenter.trim() || undefined,
      });
      setTitle(""); setDuration(""); setPresenter(""); setShowAdd(false);
      onRefresh();
    } catch (err) {
      Alert.alert("Add failed", err instanceof Error ? err.message : String(err));
    } finally {
      setAdding(false);
    }
  }

  async function markStatus(item: MeetingAgendaItem, status: string) {
    try {
      await updateMeetingAgendaItem(projectId, meetingId, item.id, { status });
      onRefresh();
    } catch (err) {
      Alert.alert("Update failed", err instanceof Error ? err.message : String(err));
    }
  }

  const STATUS_LABELS: Record<string, string> = {
    PENDING: "○ Pending", DISCUSSED: "✓ Discussed", DEFERRED: "⟳ Deferred", RESOLVED: "✔ Resolved",
  };
  const STATUS_COLORS: Record<string, string> = {
    PENDING: "#666", DISCUSSED: "#1976d2", DEFERRED: "#e65100", RESOLVED: "#2e7d32",
  };

  return (
    <View>
      {agenda.length === 0 && (
        <Text style={[styles.emptyBody, { marginBottom: 16 }]}>No agenda items yet.</Text>
      )}
      {agenda.map((item, idx) => (
        <View key={item.id} style={styles.agendaCard}>
          <View style={{ flexDirection: "row", alignItems: "center", marginBottom: 4 }}>
            <Text style={styles.agendaIndex}>{idx + 1}.</Text>
            <Text style={styles.agendaTitle}>{item.title}</Text>
            {item.durationMinutes && (
              <Text style={styles.agendaDuration}>{item.durationMinutes}min</Text>
            )}
          </View>
          {item.presenter && <Text style={styles.agendaMeta}>Presenter: {item.presenter}</Text>}
          {item.outcome && <Text style={styles.agendaOutcome}>Outcome: {item.outcome}</Text>}
          {item.decision && <Text style={styles.agendaDecision}>Decision: {item.decision}</Text>}
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 6 }}>
            {Object.entries(STATUS_LABELS).map(([s, label]) => (
              <TouchableOpacity key={s} onPress={() => markStatus(item, s)}
                style={[styles.miniPill, item.status === s && { backgroundColor: STATUS_COLORS[s] }]}>
                <Text style={[styles.miniPillText, item.status === s && { color: "#fff" }]}>{label}</Text>
              </TouchableOpacity>
            ))}
          </ScrollView>
        </View>
      ))}

      {showAdd ? (
        <View style={styles.card}>
          <Text style={styles.fieldLabel}>New Agenda Item</Text>
          <TextInput style={styles.input} placeholder="Item title *" value={title} onChangeText={setTitle} />
          <TextInput style={[styles.input, { marginTop: 6 }]} placeholder="Duration (minutes)"
            value={duration} onChangeText={setDuration} keyboardType="numeric" />
          <TextInput style={[styles.input, { marginTop: 6 }]} placeholder="Presenter"
            value={presenter} onChangeText={setPresenter} />
          <View style={{ flexDirection: "row", gap: 8, marginTop: 10 }}>
            <TouchableOpacity style={[styles.cancelBtn, { flex: 1 }]} onPress={() => setShowAdd(false)}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={[styles.saveBtn, { flex: 1 }, (!title.trim() || adding) && { opacity: 0.5 }]}
              onPress={addItem} disabled={!title.trim() || adding}>
              <Text style={styles.saveBtnText}>{adding ? "Adding…" : "Add"}</Text>
            </TouchableOpacity>
          </View>
        </View>
      ) : (
        <TouchableOpacity style={styles.addRowBtn} onPress={() => setShowAdd(true)}>
          <Text style={styles.addRowBtnText}>+ Add Agenda Item</Text>
        </TouchableOpacity>
      )}
    </View>
  );
}

// ── Actions tab ───────────────────────────────────────────────────────────────
function ActionsTab({ actions, projectId, meetingId, onRefresh }: {
  actions: MeetingActionItem[]; projectId: string; meetingId: string; onRefresh: () => void;
}) {
  const [showAdd, setShowAdd] = useState(false);
  const [desc, setDesc] = useState("");
  const [notes, setNotes] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [priority, setPriority] = useState("MEDIUM");
  const [assigneeMember, setAssigneeMember] = useState<ProjectMember | null>(null);
  const [pickerVisible, setPickerVisible] = useState(false);
  const [saving, setSaving] = useState(false);

  async function addAction() {
    if (!desc.trim()) return;
    setSaving(true);
    try {
      await addMeetingAction(projectId, meetingId, {
        description: desc.trim(),
        notes: notes.trim() || undefined,
        assignee: assigneeMember?.displayName,
        assigneeEmail: assigneeMember?.email,
        assigneeUserId: assigneeMember?.userId,
        dueDate: dueDate || undefined,
        priority,
      });
      setDesc(""); setNotes(""); setDueDate(""); setPriority("MEDIUM"); setAssigneeMember(null);
      setShowAdd(false);
      onRefresh();
    } catch (err) {
      Alert.alert("Add failed", err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  async function closeAction(a: MeetingActionItem) {
    try {
      await updateMeetingAction(projectId, meetingId, a.id, { status: "COMPLETE" });
      onRefresh();
    } catch (err) {
      Alert.alert("Close failed", err instanceof Error ? err.message : String(err));
    }
  }

  const PRIORITIES = ["CRITICAL", "HIGH", "MEDIUM", "LOW"];
  const priorityColor = (p: string) =>
    p === "CRITICAL" ? "#d32f2f" : p === "HIGH" ? "#e65100" : p === "LOW" ? "#388e3c" : "#1976d2";

  return (
    <View>
      {actions.length === 0 && (
        <Text style={[styles.emptyBody, { marginBottom: 16 }]}>No action items for this meeting.</Text>
      )}
      {actions.map((a) => {
        const overdue = a.isOverdue ?? (a.dueDate ? new Date(a.dueDate) < new Date() : false);
        return (
          <View key={a.id} style={[styles.actionCard, overdue && { borderLeftColor: "#d32f2f", borderLeftWidth: 3 }]}>
            <View style={{ flexDirection: "row", alignItems: "center", gap: 8 }}>
              <View style={[styles.priorityBadge, { backgroundColor: priorityColor(a.priority ?? "MEDIUM") }]}>
                <Text style={styles.priorityText}>{a.priority ?? "MEDIUM"}</Text>
              </View>
              <Text style={[styles.actionStatus, { color: a.status === "COMPLETE" ? "#2e7d32" : "#666" }]}>
                {a.status ?? "OPEN"}
              </Text>
            </View>
            <Text style={styles.actionDesc}>{a.description}</Text>
            {a.notes && <Text style={styles.actionNotes}>{a.notes}</Text>}
            <Text style={styles.actionMeta}>
              {a.assignee ?? "Unassigned"}
              {a.dueDate ? ` · due ${new Date(a.dueDate).toLocaleDateString()}` : ""}
              {overdue ? " · OVERDUE" : ""}
            </Text>
            {a.status !== "COMPLETE" && a.status !== "CLOSED" && (
              <TouchableOpacity style={styles.miniActionBtn} onPress={() => closeAction(a)}>
                <Text style={styles.miniActionBtnText}>✓ Mark Complete</Text>
              </TouchableOpacity>
            )}
          </View>
        );
      })}

      {showAdd ? (
        <View style={styles.card}>
          <Text style={styles.fieldLabel}>New Action Item</Text>
          <TextInput style={styles.input} placeholder="What needs to happen? *"
            value={desc} onChangeText={setDesc} />
          <TextInput style={[styles.input, { marginTop: 6 }]} placeholder="Notes (optional)"
            value={notes} onChangeText={setNotes} multiline />

          <Text style={[styles.fieldLabel, { marginTop: 10 }]}>Priority</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 4 }}>
            {PRIORITIES.map((p) => (
              <TouchableOpacity key={p} onPress={() => setPriority(p)}
                style={[styles.pill, priority === p && { backgroundColor: priorityColor(p) }]}>
                <Text style={[styles.pillText, priority === p && { color: "#fff" }]}>{p}</Text>
              </TouchableOpacity>
            ))}
          </ScrollView>

          <Text style={[styles.fieldLabel, { marginTop: 10 }]}>Assignee</Text>
          <TouchableOpacity style={styles.assigneeRow} onPress={() => setPickerVisible(true)}>
            <Text style={assigneeMember ? styles.assigneeText : styles.assigneePlaceholder}>
              {assigneeMember ? `${assigneeMember.displayName} (${assigneeMember.iso19650Role ?? "member"})` : "Tap to assign"}
            </Text>
          </TouchableOpacity>
          {assigneeMember && (
            <TouchableOpacity onPress={() => setAssigneeMember(null)}>
              <Text style={styles.clearText}>Clear assignee</Text>
            </TouchableOpacity>
          )}

          <Text style={[styles.fieldLabel, { marginTop: 10 }]}>Due Date (YYYY-MM-DD)</Text>
          <TextInput style={[styles.input, { marginTop: 4 }]} placeholder="e.g. 2025-07-15"
            value={dueDate} onChangeText={setDueDate} autoCapitalize="none" />

          <View style={{ flexDirection: "row", gap: 8, marginTop: 12 }}>
            <TouchableOpacity style={[styles.cancelBtn, { flex: 1 }]} onPress={() => setShowAdd(false)}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={[styles.saveBtn, { flex: 1 }, (!desc.trim() || saving) && { opacity: 0.5 }]}
              onPress={addAction} disabled={!desc.trim() || saving}>
              <Text style={styles.saveBtnText}>{saving ? "Adding…" : "Add Action"}</Text>
            </TouchableOpacity>
          </View>
        </View>
      ) : (
        <TouchableOpacity style={styles.addRowBtn} onPress={() => setShowAdd(true)}>
          <Text style={styles.addRowBtnText}>+ Add Action Item</Text>
        </TouchableOpacity>
      )}

      <MemberPicker
        visible={pickerVisible}
        projectId={projectId}
        selectedEmail={assigneeMember?.email}
        onSelect={(m) => { setAssigneeMember(m); setPickerVisible(false); }}
        onClose={() => setPickerVisible(false)}
      />
    </View>
  );
}

// ── Attendees tab ─────────────────────────────────────────────────────────────
function AttendeesTab({ attendees, projectId, meetingId, onRefresh }: {
  attendees: MeetingAttendee[]; projectId: string; meetingId: string; onRefresh: () => void;
}) {
  const [showAdd, setShowAdd] = useState(false);
  const [pickerVisible, setPickerVisible] = useState(false);
  const [selectedMember, setSelectedMember] = useState<ProjectMember | null>(null);
  const [externalName, setExternalName] = useState("");
  const [externalEmail, setExternalEmail] = useState("");
  const [role, setRole] = useState("ATTENDEE");
  const [adding, setAdding] = useState(false);

  async function addAttendee() {
    if (!selectedMember && !externalName.trim()) return;
    setAdding(true);
    try {
      await addMeetingAttendee(projectId, meetingId, {
        userId: selectedMember?.userId,
        name: selectedMember?.displayName ?? externalName.trim(),
        email: selectedMember?.email ?? (externalEmail.trim() || undefined),
        role,
      });
      setSelectedMember(null); setExternalName(""); setExternalEmail(""); setRole("ATTENDEE");
      setShowAdd(false);
      onRefresh();
    } catch (err) {
      Alert.alert("Add failed", err instanceof Error ? err.message : String(err));
    } finally {
      setAdding(false);
    }
  }

  async function updateStatus(attendee: MeetingAttendee, attendanceStatus: string) {
    try {
      await updateMeetingAttendee(projectId, meetingId, attendee.id, { attendanceStatus });
      onRefresh();
    } catch (err) {
      Alert.alert("Update failed", err instanceof Error ? err.message : String(err));
    }
  }

  async function removeAttendee(attendee: MeetingAttendee) {
    Alert.alert("Remove attendee", `Remove ${attendee.name}?`, [
      { text: "Cancel", style: "cancel" },
      {
        text: "Remove", style: "destructive", onPress: async () => {
          try {
            await deleteMeetingAttendee(projectId, meetingId, attendee.id);
            onRefresh();
          } catch (err) {
            Alert.alert("Remove failed", err instanceof Error ? err.message : String(err));
          }
        }
      }
    ]);
  }

  const ROLES = ["CHAIR", "SECRETARY", "ATTENDEE", "NOTIFIED"];
  const ATTENDANCE = ["INVITED", "CONFIRMED", "ATTENDED", "ABSENT", "APOLOGY"];
  const roleColor = (r: string) =>
    r === "CHAIR" ? "#6a1b9a" : r === "SECRETARY" ? "#1976d2"
    : r === "NOTIFIED" ? "#888" : "#2e7d32";

  const statusIcon = (s: string) =>
    s === "ATTENDED" ? "✓" : s === "ABSENT" ? "✗" : s === "CONFIRMED" ? "●" : s === "APOLOGY" ? "~" : "○";

  const regularAttendees = attendees.filter((a) => a.role !== "NOTIFIED");
  const bccAttendees = attendees.filter((a) => a.role === "NOTIFIED");

  return (
    <View>
      {attendees.length === 0 && (
        <Text style={[styles.emptyBody, { marginBottom: 16 }]}>No attendees added yet.</Text>
      )}

      {regularAttendees.length > 0 && (
        <>
          <Text style={styles.fieldLabel}>Attendees</Text>
          {regularAttendees.map((a) => (
            <View key={a.id} style={styles.attendeeCard}>
              <View style={{ flex: 1 }}>
                <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
                  <View style={[styles.roleChip, { backgroundColor: roleColor(a.role) }]}>
                    <Text style={styles.roleText}>{a.role}</Text>
                  </View>
                  <Text style={styles.attendeeName}>{a.name}</Text>
                </View>
                {a.email && <Text style={styles.attendeeMeta}>{a.email}</Text>}
                {a.company && <Text style={styles.attendeeMeta}>{a.company}</Text>}
              </View>
              <View style={{ alignItems: "flex-end", gap: 4 }}>
                <Text style={styles.attendanceIcon}>{statusIcon(a.attendanceStatus)}</Text>
                <ScrollView horizontal showsHorizontalScrollIndicator={false}>
                  {ATTENDANCE.map((s) => (
                    <TouchableOpacity key={s} onPress={() => updateStatus(a, s)}
                      style={[styles.miniPill, a.attendanceStatus === s && styles.miniPillActive]}>
                      <Text style={[styles.miniPillText, a.attendanceStatus === s && { color: "#fff" }]}>
                        {s.slice(0, 3)}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </ScrollView>
                <TouchableOpacity onPress={() => removeAttendee(a)}>
                  <Text style={styles.removeText}>Remove</Text>
                </TouchableOpacity>
              </View>
            </View>
          ))}
        </>
      )}

      {bccAttendees.length > 0 && (
        <>
          <Text style={[styles.fieldLabel, { marginTop: 12 }]}>BCC / Notified Only</Text>
          {bccAttendees.map((a) => (
            <View key={a.id} style={[styles.attendeeCard, { opacity: 0.8 }]}>
              <View style={{ flex: 1 }}>
                <Text style={styles.attendeeName}>{a.name}</Text>
                {a.email && <Text style={styles.attendeeMeta}>{a.email}</Text>}
              </View>
              <TouchableOpacity onPress={() => removeAttendee(a)}>
                <Text style={styles.removeText}>Remove</Text>
              </TouchableOpacity>
            </View>
          ))}
        </>
      )}

      {showAdd ? (
        <View style={styles.card}>
          <Text style={styles.fieldLabel}>Add Attendee</Text>
          <TouchableOpacity style={styles.assigneeRow} onPress={() => setPickerVisible(true)}>
            <Text style={selectedMember ? styles.assigneeText : styles.assigneePlaceholder}>
              {selectedMember ? `${selectedMember.displayName} · ${selectedMember.email}` : "Pick from project members"}
            </Text>
          </TouchableOpacity>
          {selectedMember && (
            <TouchableOpacity onPress={() => setSelectedMember(null)}>
              <Text style={styles.clearText}>Clear · add external instead</Text>
            </TouchableOpacity>
          )}
          {!selectedMember && (
            <>
              <Text style={[styles.fieldLabel, { marginTop: 8 }]}>External name *</Text>
              <TextInput style={styles.input} placeholder="Full name"
                value={externalName} onChangeText={setExternalName} />
              <TextInput style={[styles.input, { marginTop: 6 }]} placeholder="Email (optional)"
                value={externalEmail} onChangeText={setExternalEmail} keyboardType="email-address" autoCapitalize="none" />
            </>
          )}
          <Text style={[styles.fieldLabel, { marginTop: 10 }]}>Role</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 4 }}>
            {ROLES.map((r) => (
              <TouchableOpacity key={r} onPress={() => setRole(r)}
                style={[styles.pill, role === r && { backgroundColor: roleColor(r) }]}>
                <Text style={[styles.pillText, role === r && { color: "#fff" }]}>{r}</Text>
              </TouchableOpacity>
            ))}
          </ScrollView>
          <View style={{ flexDirection: "row", gap: 8, marginTop: 12 }}>
            <TouchableOpacity style={[styles.cancelBtn, { flex: 1 }]} onPress={() => setShowAdd(false)}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.saveBtn, { flex: 1 },
                (!selectedMember && !externalName.trim() || adding) && { opacity: 0.5 }]}
              onPress={addAttendee}
              disabled={(!selectedMember && !externalName.trim()) || adding}>
              <Text style={styles.saveBtnText}>{adding ? "Adding…" : "Add"}</Text>
            </TouchableOpacity>
          </View>
        </View>
      ) : (
        <TouchableOpacity style={styles.addRowBtn} onPress={() => setShowAdd(true)}>
          <Text style={styles.addRowBtnText}>+ Add Attendee / BCC</Text>
        </TouchableOpacity>
      )}

      <MemberPicker
        visible={pickerVisible}
        projectId={projectId}
        selectedEmail={selectedMember?.email}
        onSelect={(m) => { setSelectedMember(m); setPickerVisible(false); }}
        onClose={() => setPickerVisible(false)}
      />
    </View>
  );
}

// ── Create meeting modal ──────────────────────────────────────────────────────
function CreateMeetingModal({ visible, projectId, onClose, onCreated }: {
  visible: boolean; projectId: string; onClose: () => void; onCreated: () => void;
}) {
  const [title, setTitle] = useState("");
  const [when, setWhen] = useState(() => {
    const d = new Date(); d.setHours(d.getHours() + 1, 0, 0, 0);
    return d.toISOString().slice(0, 16);
  });
  const [duration, setDuration] = useState("");
  const [meetingType, setMeetingType] = useState("BIM Coordination");
  const [location, setLocation] = useState("");
  const [meetingUrl, setMeetingUrl] = useState("");
  const [bccMembers, setBccMembers] = useState<ProjectMember[]>([]);
  const [pickerVisible, setPickerVisible] = useState(false);
  const [saving, setSaving] = useState(false);

  const MEETING_TYPES = [
    "BIM Coordination", "Design Review", "Client Review",
    "Handover", "Clash Resolution", "Progress Review", "RFI Review", "Safety Briefing",
  ];

  async function save() {
    if (!title.trim()) return;
    const parsed = new Date(when);
    if (isNaN(parsed.getTime())) {
      Alert.alert("Invalid date", "Use format YYYY-MM-DDTHH:MM");
      return;
    }
    setSaving(true);
    try {
      await createMeeting(projectId, {
        title: title.trim(),
        meetingType,
        scheduledAt: parsed.toISOString(),
        durationMinutes: duration ? parseInt(duration, 10) : undefined,
        location: location.trim() || undefined,
        meetingUrl: meetingUrl.trim() || undefined,
        notifiedUserIds: bccMembers.map((m) => m.userId),
      });
      setTitle(""); setDuration(""); setLocation(""); setMeetingUrl(""); setBccMembers([]);
      onCreated();
    } catch (err) {
      Alert.alert("Create failed", err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  function addBcc(member: ProjectMember | null) {
    if (!member) return;
    if (!bccMembers.find((m) => m.userId === member.userId)) {
      setBccMembers((prev) => [...prev, member]);
    }
    setPickerVisible(false);
  }

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.modalOverlay}
        behavior={Platform.OS === "ios" ? "padding" : "height"}>
        <View style={styles.modalCard}>
          <ScrollView showsVerticalScrollIndicator={false}>
            <Text style={styles.sheetTitle}>New Meeting</Text>

            <Text style={styles.fieldLabel}>Title *</Text>
            <TextInput style={styles.input} value={title} onChangeText={setTitle}
              placeholder="Weekly BIM Coordination" />

            <Text style={styles.fieldLabel}>Type</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 4 }}>
              {MEETING_TYPES.map((t) => (
                <TouchableOpacity key={t} onPress={() => setMeetingType(t)}
                  style={[styles.typePill, meetingType === t && styles.typePillOn]}>
                  <Text style={[styles.typePillText, meetingType === t && { color: "#fff" }]}>{t}</Text>
                </TouchableOpacity>
              ))}
            </ScrollView>

            <Text style={styles.fieldLabel}>Date & Time (YYYY-MM-DDTHH:MM)</Text>
            <TextInput style={styles.input} value={when} onChangeText={setWhen}
              autoCapitalize="none" placeholder="2025-07-01T14:00" />

            <Text style={styles.fieldLabel}>Duration (minutes)</Text>
            <TextInput style={styles.input} value={duration} onChangeText={setDuration}
              keyboardType="numeric" placeholder="60" />

            <Text style={styles.fieldLabel}>Location</Text>
            <TextInput style={styles.input} value={location} onChangeText={setLocation}
              placeholder="Conference Room A / TBC" />

            <Text style={styles.fieldLabel}>Video Link (Teams / Zoom)</Text>
            <TextInput style={styles.input} value={meetingUrl} onChangeText={setMeetingUrl}
              autoCapitalize="none" placeholder="https://teams.microsoft.com/…" />

            <Text style={styles.fieldLabel}>BCC / Notify (receives invite & minutes)</Text>
            {bccMembers.map((m) => (
              <View key={m.userId} style={styles.bccChip}>
                <Text style={styles.bccChipText}>{m.displayName}</Text>
                <TouchableOpacity onPress={() => setBccMembers((prev) => prev.filter((x) => x.userId !== m.userId))}>
                  <Text style={{ color: "#fff", marginLeft: 6 }}>✕</Text>
                </TouchableOpacity>
              </View>
            ))}
            <TouchableOpacity style={styles.addRowBtn} onPress={() => setPickerVisible(true)}>
              <Text style={styles.addRowBtnText}>+ Add BCC recipient</Text>
            </TouchableOpacity>
          </ScrollView>

          <View style={styles.sheetActions}>
            <TouchableOpacity style={styles.cancelBtn} onPress={onClose}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={[styles.saveBtn, (!title.trim() || saving) && { opacity: 0.5 }]}
              onPress={save} disabled={!title.trim() || saving}>
              <Text style={styles.saveBtnText}>{saving ? "Saving…" : "Create"}</Text>
            </TouchableOpacity>
          </View>
        </View>
      </KeyboardAvoidingView>

      <MemberPicker
        visible={pickerVisible}
        projectId={projectId}
        onSelect={addBcc}
        onClose={() => setPickerVisible(false)}
      />
    </Modal>
  );
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function typeColor(type?: string) {
  switch (type?.replace(/ /g, "_").toUpperCase()) {
    case "BIM_COORDINATION": return { backgroundColor: "#1976d2" };
    case "DESIGN_REVIEW": return { backgroundColor: "#6a1b9a" };
    case "CLIENT_REVIEW": return { backgroundColor: "#c2185b" };
    case "HANDOVER": return { backgroundColor: "#2e7d32" };
    case "CLASH_RESOLUTION": return { backgroundColor: "#ef6c00" };
    case "PROGRESS_REVIEW": return { backgroundColor: "#00838f" };
    case "RFI_REVIEW": return { backgroundColor: "#558b2f" };
    case "SAFETY_BRIEFING": return { backgroundColor: "#d32f2f" };
    default: return { backgroundColor: "#666" };
  }
}

function statusColor(status: string) {
  switch (status) {
    case "COMPLETED": return "#2e7d32";
    case "IN_PROGRESS": return "#e65100";
    case "CANCELLED": return "#999";
    default: return "#1976d2";
  }
}

// ── Styles ────────────────────────────────────────────────────────────────────
const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: "#f5f5f5" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 32 },
  emptyTitle: { fontSize: 18, fontWeight: "700", marginBottom: 8, color: "#333" },
  emptyBody: { color: "#666", textAlign: "center", fontSize: 14, lineHeight: 20 },
  section: { backgroundColor: "#fff", marginTop: 8, padding: 14, borderRadius: 8, marginHorizontal: 12 },
  sectionTitle: { fontSize: 13, fontWeight: "700", color: "#666", textTransform: "uppercase", letterSpacing: 0.5, marginBottom: 10 },

  meetingRow: { paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: "#f0f0f0" },
  meetingTop: { flexDirection: "row", alignItems: "center", marginBottom: 6, gap: 6 },
  typeChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10 },
  typeText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  statusDot: { width: 8, height: 8, borderRadius: 4 },
  liveBadge: { backgroundColor: "#d32f2f", borderRadius: 8, paddingHorizontal: 6, paddingVertical: 1 },
  liveBadgeText: { color: "#fff", fontSize: 9, fontWeight: "800", letterSpacing: 0.5 },
  actionCount: { marginLeft: "auto", fontSize: 11, color: "#E8912D", fontWeight: "700" },
  meetingTitle: { fontSize: 15, fontWeight: "600", color: "#222" },
  meta: { fontSize: 12, color: "#666", marginTop: 2 },

  actionRow: {
    flexDirection: "row", alignItems: "center", paddingVertical: 8, gap: 8,
    borderBottomWidth: 1, borderBottomColor: "#f0f0f0",
  },
  actionRowOverdue: { backgroundColor: "#ffebee" },
  tickBox: {
    width: 28, height: 28, borderRadius: 14,
    borderWidth: 1.5, borderColor: "#999", alignItems: "center", justifyContent: "center",
  },
  actionDesc: { fontSize: 14, color: "#222", fontWeight: "500" },
  actionNotes: { fontSize: 12, color: "#888", marginTop: 2 },
  actionMeta: { fontSize: 11, color: "#777", marginTop: 2 },
  actionStatus: { fontSize: 11, fontWeight: "600" },
  actionCard: { backgroundColor: "#fff", padding: 12, borderRadius: 8, marginBottom: 8, gap: 4 },
  priorityBadge: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4 },
  priorityText: { color: "#fff", fontSize: 10, fontWeight: "700" },
  miniActionBtn: { marginTop: 6, paddingVertical: 4, paddingHorizontal: 10, backgroundColor: "#2e7d32", borderRadius: 4, alignSelf: "flex-start" },
  miniActionBtnText: { color: "#fff", fontSize: 11, fontWeight: "700" },
  escalateBtn: { paddingHorizontal: 10, paddingVertical: 4, backgroundColor: "#d32f2f", borderRadius: 4 },
  escalateText: { color: "#fff", fontSize: 11, fontWeight: "700" },

  detailHeader: {
    flexDirection: "row", alignItems: "center", padding: 16, paddingTop: Platform.OS === "ios" ? 56 : 16,
    backgroundColor: "#1A237E", gap: 10,
  },
  backBtn: { paddingRight: 8 },
  backText: { color: "#fff", fontSize: 15 },
  detailTitle: { fontSize: 16, fontWeight: "700", color: "#fff" },
  detailMeta: { fontSize: 12, color: "rgba(255,255,255,0.7)", marginTop: 2 },
  statusBadge: { paddingHorizontal: 8, paddingVertical: 3, borderRadius: 10 },
  statusText: { color: "#fff", fontSize: 11, fontWeight: "700" },

  tabBar: { backgroundColor: "#fff", borderBottomWidth: 1, borderBottomColor: "#e0e0e0" },
  tabPill: { paddingHorizontal: 14, paddingVertical: 10, marginVertical: 4 },
  tabPillActive: { borderBottomWidth: 2, borderBottomColor: "#E8912D" },
  tabText: { fontSize: 13, fontWeight: "600", color: "#666" },
  tabTextActive: { color: "#E8912D" },

  card: { backgroundColor: "#fff", borderRadius: 8, padding: 14, marginBottom: 12 },
  fieldLabel: { fontSize: 12, fontWeight: "600", color: "#666", marginBottom: 4 },
  fieldValue: { fontSize: 14, color: "#222" },
  input: {
    backgroundColor: "#f5f5f5", borderRadius: 6, borderWidth: 1, borderColor: "#e0e0e0",
    paddingHorizontal: 12, paddingVertical: 10, fontSize: 14, color: "#222",
  },
  pill: {
    paddingHorizontal: 12, paddingVertical: 6, borderRadius: 14,
    backgroundColor: "#f0f0f0", marginRight: 6,
  },
  pillActive: { backgroundColor: "#1A237E" },
  pillText: { fontSize: 11, fontWeight: "600", color: "#222" },
  miniPill: {
    paddingHorizontal: 8, paddingVertical: 4, borderRadius: 10,
    backgroundColor: "#f0f0f0", marginRight: 4,
  },
  miniPillActive: { backgroundColor: "#1976d2" },
  miniPillText: { fontSize: 10, fontWeight: "600", color: "#444" },
  saveBtn: { marginTop: 12, paddingVertical: 12, borderRadius: 8, backgroundColor: "#E8912D", alignItems: "center" },
  saveBtnText: { color: "#fff", fontWeight: "700" },
  cancelBtn: { paddingVertical: 12, borderRadius: 8, borderWidth: 1, borderColor: "#e0e0e0", alignItems: "center" },
  cancelBtnText: { color: "#666", fontWeight: "600" },
  addRowBtn: { marginTop: 8, paddingVertical: 10, borderRadius: 8, borderWidth: 1, borderColor: "#E8912D", borderStyle: "dashed", alignItems: "center" },
  addRowBtnText: { color: "#E8912D", fontWeight: "600", fontSize: 13 },
  exportBtn: { flex: 1, paddingVertical: 10, borderRadius: 6, alignItems: "center" },
  exportBtnText: { color: "#fff", fontSize: 12, fontWeight: "600" },

  agendaCard: { backgroundColor: "#fff", borderRadius: 8, padding: 12, marginBottom: 8 },
  agendaIndex: { fontSize: 14, fontWeight: "700", color: "#1A237E", marginRight: 6 },
  agendaTitle: { flex: 1, fontSize: 14, fontWeight: "600", color: "#222" },
  agendaDuration: { fontSize: 11, color: "#888", marginLeft: 6 },
  agendaMeta: { fontSize: 12, color: "#888", marginTop: 2 },
  agendaOutcome: { fontSize: 12, color: "#388e3c", marginTop: 4 },
  agendaDecision: { fontSize: 12, fontWeight: "600", color: "#1976d2", marginTop: 2 },

  attendeeCard: { backgroundColor: "#fff", borderRadius: 8, padding: 12, marginBottom: 8, flexDirection: "row" },
  attendeeName: { fontSize: 14, fontWeight: "600", color: "#222" },
  attendeeMeta: { fontSize: 12, color: "#888", marginTop: 2 },
  attendanceIcon: { fontSize: 18, color: "#666" },
  roleChip: { paddingHorizontal: 6, paddingVertical: 2, borderRadius: 6 },
  roleText: { color: "#fff", fontSize: 10, fontWeight: "700" },
  removeText: { fontSize: 11, color: "#d32f2f", marginTop: 4 },

  assigneeRow: {
    backgroundColor: "#f5f5f5", borderRadius: 6, borderWidth: 1, borderColor: "#e0e0e0",
    paddingHorizontal: 12, paddingVertical: 12, marginTop: 4,
  },
  assigneeText: { fontSize: 14, color: "#222" },
  assigneePlaceholder: { fontSize: 14, color: "#aaa" },
  clearText: { fontSize: 12, color: "#1976d2", marginTop: 4 },

  bccChip: {
    flexDirection: "row", alignItems: "center", backgroundColor: "#1A237E",
    borderRadius: 14, paddingHorizontal: 10, paddingVertical: 4, marginTop: 4, alignSelf: "flex-start",
  },
  bccChipText: { color: "#fff", fontSize: 12, fontWeight: "600" },

  fab: {
    position: "absolute", right: 16, bottom: 24,
    width: 56, height: 56, borderRadius: 28,
    backgroundColor: "#E8912D", alignItems: "center", justifyContent: "center",
    shadowColor: "#000", shadowOffset: { width: 0, height: 4 }, shadowOpacity: 0.2, shadowRadius: 8, elevation: 6,
  },
  fabText: { color: "#fff", fontSize: 28, marginTop: -2 },

  modalOverlay: { flex: 1, backgroundColor: "rgba(0,0,0,0.5)", justifyContent: "flex-end" },
  modalCard: {
    backgroundColor: "#fff", borderTopLeftRadius: 16, borderTopRightRadius: 16,
    padding: 20, maxHeight: "92%",
  },
  sheetTitle: { fontSize: 17, fontWeight: "700", color: "#222", marginBottom: 12 },
  sheetActions: { flexDirection: "row", gap: 12, marginTop: 20 },
  typePill: {
    paddingHorizontal: 12, paddingVertical: 6, borderRadius: 14,
    backgroundColor: "#f0f0f0", marginRight: 6,
  },
  typePillOn: { backgroundColor: "#1A237E" },
  typePillText: { fontSize: 11, fontWeight: "600", color: "#222" },
});
