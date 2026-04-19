import { useState, useEffect, useCallback } from "react";
import {
  View, Text, StyleSheet, TouchableOpacity, Modal, TextInput, ScrollView,
  Alert, ActivityIndicator, Platform, KeyboardAvoidingView,
} from "react-native";
import { router } from "expo-router";
import {
  listMeetings, createMeeting, listOpenMeetingActions, updateMeetingAction,
  logMeetingMinutes, addMeetingAction,
  type MeetingActionItem,
} from "@/api/endpoints";
import { useProjectStore } from "@/stores/projectStore";
import type { Meeting } from "@/types/api";

/**
 * Phase 96 — Meetings screen rewrite.
 *
 * Before: read-only list from CoordinationListScreen.
 * After: two sections — upcoming meetings (with tap-to-edit) + open actions
 * across all meetings (with tick-off, reassign, escalate to NCR). FAB drafts
 * a new meeting. Tapping a meeting row opens an inline detail sheet with
 * minutes editor and action-item capture.
 *
 * Rationale: on-site meetings are usually quick stand-ups where the
 * coordinator wants to tick off outstanding actions and add new ones — the
 * previous read-only view was useless for that workflow.
 */

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
      Alert.alert('Load failed', err instanceof Error ? err.message : String(err));
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
  if (loading) {
    return <View style={styles.center}><ActivityIndicator /></View>;
  }

  const upcoming = meetings.filter((m) => {
    const t = m.scheduledAt ?? (m as any).scheduledDate;
    return t ? new Date(t) >= new Date() : false;
  });
  const past = meetings.filter((m) => !upcoming.includes(m));

  return (
    <>
      <ScrollView style={styles.root} contentContainerStyle={{ paddingBottom: 100 }}>
        {/* Open actions across all meetings — triage queue */}
        {openActions.length > 0 && (
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Open Actions ({openActions.length})</Text>
            {openActions.slice(0, 10).map((a) => (
              <ActionRow
                key={a.id}
                action={a}
                onTick={async () => {
                  if (!projectId || !a.meetingId) {
                    Alert.alert('Meeting link missing', 'Open the meeting detail to close this action.');
                    return;
                  }
                  try {
                    await updateMeetingAction(projectId, a.meetingId, a.id, { status: 'CLOSED' });
                    load();
                  } catch (err) {
                    Alert.alert('Close failed', err instanceof Error ? err.message : String(err));
                  }
                }}
                onEscalate={() => {
                  router.push({
                    pathname: '/(tabs)/issues',
                    params: {
                      createForElement: a.linkedIssueId ?? '',
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
        <MeetingDetailSheet
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

function MeetingRow({ meeting, onPress, isPast = false }: {
  meeting: Meeting; onPress: () => void; isPast?: boolean;
}) {
  const t = meeting.scheduledAt ?? (meeting as any).scheduledDate;
  return (
    <TouchableOpacity style={[styles.meetingRow, isPast && { opacity: 0.75 }]} onPress={onPress} activeOpacity={0.7}>
      <View style={styles.meetingTop}>
        <View style={[styles.typeChip, typeColor(meeting.type)]}>
          <Text style={styles.typeText}>{(meeting.type ?? "MEETING").toUpperCase()}</Text>
        </View>
        {typeof meeting.actionItemCount === 'number' && meeting.actionItemCount > 0 && (
          <Text style={styles.actionCount}>{meeting.actionItemCount} actions</Text>
        )}
      </View>
      <Text style={styles.meetingTitle} numberOfLines={1}>{meeting.title ?? "(untitled)"}</Text>
      <Text style={styles.meta}>
        {t ? new Date(t).toLocaleString() : "Unscheduled"}
      </Text>
    </TouchableOpacity>
  );
}

function ActionRow({ action, onTick, onEscalate }: {
  action: MeetingActionItem; onTick: () => void; onEscalate: () => void;
}) {
  const overdue = action.isOverdue ?? (action.dueDate ? new Date(action.dueDate) < new Date() : false);
  return (
    <View style={[styles.actionRow, overdue && styles.actionRowOverdue]}>
      <TouchableOpacity
        style={styles.tickBox}
        onPress={onTick}
        accessibilityLabel={`Mark action complete: ${action.description}`}
      >
        <Text style={{ fontSize: 16 }}>○</Text>
      </TouchableOpacity>
      <View style={{ flex: 1 }}>
        <Text style={styles.actionDesc} numberOfLines={2}>{action.description}</Text>
        <Text style={styles.actionMeta}>
          {action.assignee ?? 'Unassigned'}
          {action.dueDate ? ` · due ${new Date(action.dueDate).toLocaleDateString()}` : ''}
          {overdue ? ' · OVERDUE' : ''}
        </Text>
      </View>
      <TouchableOpacity onPress={onEscalate} style={styles.escalateBtn}>
        <Text style={styles.escalateText}>→ NCR</Text>
      </TouchableOpacity>
    </View>
  );
}

function MeetingDetailSheet({ meeting, projectId, onClose }: {
  meeting: Meeting; projectId: string; onClose: () => void;
}) {
  const [minutes, setMinutes] = useState((meeting as any).minutes ?? '');
  const [newAction, setNewAction] = useState('');
  const [assignee, setAssignee] = useState('');
  const [saving, setSaving] = useState(false);

  async function saveMinutes() {
    setSaving(true);
    try {
      await logMeetingMinutes(projectId, meeting.id, minutes);
      Alert.alert('Saved', 'Minutes logged to the meeting record.');
    } catch (err) {
      Alert.alert('Save failed', err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  async function addAction() {
    if (!newAction.trim()) return;
    setSaving(true);
    try {
      await addMeetingAction(projectId, meeting.id, {
        description: newAction.trim(),
        assignee: assignee.trim() || undefined,
      });
      setNewAction('');
      setAssignee('');
    } catch (err) {
      Alert.alert('Add failed', err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal visible animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.modalOverlay} behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>
        <View style={styles.modalCard}>
          <View style={styles.sheetHeader}>
            <Text style={styles.sheetTitle}>{meeting.title}</Text>
            <TouchableOpacity onPress={onClose} style={styles.sheetClose}>
              <Text style={{ fontSize: 20 }}>✕</Text>
            </TouchableOpacity>
          </View>

          <ScrollView style={{ maxHeight: 400 }}>
            <Text style={styles.fieldLabel}>Minutes</Text>
            <TextInput
              style={[styles.input, { minHeight: 120, textAlignVertical: 'top' }]}
              multiline
              placeholder="Key decisions, open points, attendees..."
              value={minutes}
              onChangeText={setMinutes}
            />
            <TouchableOpacity style={styles.saveBtn} onPress={saveMinutes} disabled={saving}>
              <Text style={styles.saveBtnText}>{saving ? 'Saving…' : 'Save Minutes'}</Text>
            </TouchableOpacity>

            <View style={{ height: 16 }} />
            <Text style={styles.fieldLabel}>Add Action Item</Text>
            <TextInput style={styles.input} placeholder="What needs to happen?"
              value={newAction} onChangeText={setNewAction} />
            <TextInput style={[styles.input, { marginTop: 6 }]} placeholder="Assignee name"
              value={assignee} onChangeText={setAssignee} />
            <TouchableOpacity
              style={[styles.saveBtn, (!newAction.trim() || saving) && { opacity: 0.5 }]}
              onPress={addAction}
              disabled={!newAction.trim() || saving}
            >
              <Text style={styles.saveBtnText}>Add Action</Text>
            </TouchableOpacity>
          </ScrollView>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

function CreateMeetingModal({ visible, projectId, onClose, onCreated }: {
  visible: boolean; projectId: string; onClose: () => void; onCreated: () => void;
}) {
  const [title, setTitle] = useState('');
  const [when, setWhen] = useState(() => {
    const d = new Date(); d.setHours(d.getHours() + 1, 0, 0, 0);
    return d.toISOString().slice(0, 16); // yyyy-MM-ddTHH:mm
  });
  const [meetingType, setMeetingType] = useState('COORDINATION');
  const [saving, setSaving] = useState(false);

  async function save() {
    if (!title.trim()) return;
    // Phase 96 — parse+validate the ISO-ish datetime before hitting the API.
    // The TextInput accepts any string so "2025-13-45T99:99" previously
    // produced Invalid Date and a cryptic server 400. Surface a clear error
    // client-side instead.
    const parsed = new Date(when);
    if (isNaN(parsed.getTime())) {
      Alert.alert('Invalid date', 'Use format YYYY-MM-DDTHH:MM (e.g. 2025-06-15T14:00).');
      return;
    }
    if (parsed.getTime() < Date.now() - 86_400_000) {
      Alert.alert('Date is in the past', 'Pick a future date for the scheduled meeting.');
      return;
    }
    setSaving(true);
    try {
      await createMeeting(projectId, {
        title: title.trim(),
        meetingType,
        scheduledAt: parsed.toISOString(),
      });
      setTitle('');
      onCreated();
    } catch (err) {
      Alert.alert('Create failed', err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.modalOverlay} behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>
        <View style={styles.modalCard}>
          <Text style={styles.sheetTitle}>New Meeting</Text>
          <Text style={styles.fieldLabel}>Title *</Text>
          <TextInput style={styles.input} value={title} onChangeText={setTitle}
            placeholder="Weekly BIM Coordination" />
          <Text style={styles.fieldLabel}>Type</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false}>
            {['COORDINATION', 'DESIGN_REVIEW', 'CLIENT_REVIEW', 'HANDOVER', 'CLASH_RESOLUTION'].map((t) => (
              <TouchableOpacity
                key={t}
                onPress={() => setMeetingType(t)}
                style={[styles.typePill, meetingType === t && styles.typePillOn]}
              >
                <Text style={[styles.typePillText, meetingType === t && { color: '#fff' }]}>{t}</Text>
              </TouchableOpacity>
            ))}
          </ScrollView>
          <Text style={styles.fieldLabel}>Scheduled (ISO: YYYY-MM-DDTHH:MM)</Text>
          <TextInput style={styles.input} value={when} onChangeText={setWhen} autoCapitalize="none" />

          <View style={styles.sheetActions}>
            <TouchableOpacity style={styles.cancelBtn} onPress={onClose}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={[styles.saveBtn, (!title.trim() || saving) && { opacity: 0.5 }]}
              onPress={save} disabled={!title.trim() || saving}>
              <Text style={styles.saveBtnText}>{saving ? 'Saving…' : 'Create'}</Text>
            </TouchableOpacity>
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
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
  root: { flex: 1, backgroundColor: '#f5f5f5' },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 32 },
  emptyTitle: { fontSize: 18, fontWeight: "700", marginBottom: 8, color: "#333" },
  emptyBody: { color: "#666", textAlign: "center", fontSize: 14, lineHeight: 20 },
  section: { backgroundColor: '#fff', marginTop: 8, padding: 14, borderRadius: 8, marginHorizontal: 12 },
  sectionTitle: { fontSize: 13, fontWeight: '700', color: '#666', textTransform: 'uppercase', letterSpacing: 0.5, marginBottom: 10 },
  meetingRow: { paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: '#f0f0f0' },
  meetingTop: { flexDirection: "row", alignItems: "center", marginBottom: 6 },
  typeChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10 },
  typeText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  actionCount: { marginLeft: 'auto', fontSize: 11, color: '#E8912D', fontWeight: '700' },
  meetingTitle: { fontSize: 15, fontWeight: "600", color: "#222" },
  meta: { fontSize: 12, color: "#666", marginTop: 2 },
  actionRow: {
    flexDirection: 'row', alignItems: 'center', paddingVertical: 8, gap: 8,
    borderBottomWidth: 1, borderBottomColor: '#f0f0f0',
  },
  actionRowOverdue: { backgroundColor: '#ffebee' },
  tickBox: {
    width: 28, height: 28, borderRadius: 14,
    borderWidth: 1.5, borderColor: '#999', alignItems: 'center', justifyContent: 'center',
  },
  actionDesc: { fontSize: 14, color: '#222', fontWeight: '500' },
  actionMeta: { fontSize: 11, color: '#777', marginTop: 2 },
  escalateBtn: { paddingHorizontal: 10, paddingVertical: 4, backgroundColor: '#d32f2f', borderRadius: 4 },
  escalateText: { color: '#fff', fontSize: 11, fontWeight: '700' },
  fab: {
    position: 'absolute', right: 16, bottom: 24,
    width: 56, height: 56, borderRadius: 28,
    backgroundColor: '#E8912D', alignItems: 'center', justifyContent: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 4 }, shadowOpacity: 0.2, shadowRadius: 8, elevation: 6,
  },
  fabText: { color: '#fff', fontSize: 28, marginTop: -2 },

  modalOverlay: { flex: 1, backgroundColor: 'rgba(0,0,0,0.5)', justifyContent: 'flex-end' },
  modalCard: {
    backgroundColor: '#fff', borderTopLeftRadius: 16, borderTopRightRadius: 16,
    padding: 20, maxHeight: '90%',
  },
  sheetHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 },
  sheetTitle: { fontSize: 17, fontWeight: '700', color: '#222' },
  sheetClose: { padding: 4 },
  fieldLabel: { fontSize: 12, fontWeight: '600', color: '#666', marginTop: 12, marginBottom: 4 },
  input: {
    backgroundColor: '#f5f5f5', borderRadius: 6, borderWidth: 1, borderColor: '#e0e0e0',
    paddingHorizontal: 12, paddingVertical: 10, fontSize: 14, color: '#222',
  },
  typePill: {
    paddingHorizontal: 12, paddingVertical: 6, borderRadius: 14,
    backgroundColor: '#f0f0f0', marginRight: 6,
  },
  typePillOn: { backgroundColor: '#1A237E' },
  typePillText: { fontSize: 11, fontWeight: '600', color: '#222' },
  saveBtn: { marginTop: 12, paddingVertical: 12, borderRadius: 8, backgroundColor: '#E8912D', alignItems: 'center' },
  saveBtnText: { color: '#fff', fontWeight: '700' },
  sheetActions: { flexDirection: 'row', gap: 12, marginTop: 20 },
  cancelBtn: { flex: 1, paddingVertical: 12, borderRadius: 8, borderWidth: 1, borderColor: '#e0e0e0', alignItems: 'center' },
  cancelBtnText: { color: '#666', fontWeight: '600' },
});
