import { useState } from "react";
import {
  View, Text, StyleSheet, TouchableOpacity, Modal, TextInput, Alert,
  KeyboardAvoidingView, Platform, ActivityIndicator,
} from "react-native";
import { listTransmittals, createTransmittal, sendTransmittal } from "@/api/endpoints";
import { CoordinationListScreen } from "@/components/CoordinationListScreen";
import { useProjectStore } from "@/stores/projectStore";
import type { Transmittal } from "@/types/api";

/**
 * Phase 96 — Transmittal creation flow.
 *
 * Before: read-only list that displayed transmittals sent from Revit/web.
 * After: FAB → create-draft modal (subject, issuedTo, recipients list) →
 * server creates DRAFT → coordinator can send from row long-press.
 *
 * Scope is deliberately narrow: document selection is delegated to the web
 * dashboard because picking 20+ docs from a scrollable modal on a phone is
 * painful UX. Mobile creates the wrapper; web attaches the docs.
 */

export default function TransmittalsScreen() {
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [createVisible, setCreateVisible] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  return (
    <>
      <CoordinationListScreen<Transmittal>
        key={refreshKey}
        title="Transmittals"
        emptyTitle="No transmittals yet"
        emptyBody="Tap the + button to draft a new transmittal, or wait for one from Revit/web."
        fetch={listTransmittals}
        keyExtractor={(t) => t.id}
        onPressRow={(t) => onRowAction(t, projectId, () => setRefreshKey((k) => k + 1))}
        renderRow={(t) => (
          <View style={styles.row}>
            <View style={styles.left}>
              <Text style={styles.code}>{t.transmittalNumber ?? t.id.slice(0, 8)}</Text>
              <View style={[styles.statusChip, statusColor(t.status)]}>
                <Text style={styles.statusText}>{t.status ?? "DRAFT"}</Text>
              </View>
            </View>
            <Text style={styles.title} numberOfLines={1}>{t.subject ?? "(no subject)"}</Text>
            <Text style={styles.meta} numberOfLines={1}>
              To: {t.issuedTo ?? "—"}{typeof t.documentCount === 'number' ? ` · ${t.documentCount} docs` : ''}
            </Text>
            <Text style={styles.meta}>
              {t.sentAt ? `Sent ${new Date(t.sentAt).toLocaleDateString()}` :
               t.createdAt ? `Draft ${new Date(t.createdAt).toLocaleDateString()}` : ""}
            </Text>
          </View>
        )}
      />

      {/* FAB — draft new transmittal. Phase 96. */}
      <TouchableOpacity
        style={styles.fab}
        onPress={() => setCreateVisible(true)}
        accessibilityRole="button"
        accessibilityLabel="Draft a new transmittal"
      >
        <Text style={styles.fabText}>+</Text>
      </TouchableOpacity>

      <CreateTransmittalModal
        visible={createVisible}
        projectId={projectId}
        onClose={() => setCreateVisible(false)}
        onCreated={() => {
          setCreateVisible(false);
          setRefreshKey((k) => k + 1);
        }}
      />
    </>
  );
}

/**
 * Phase 96 — single-flight guard. If the user double-taps the Send button in
 * the confirm dialog, the second tap's request races against the first. We
 * short-circuit while a send is in progress for this transmittal ID.
 */
const _sendingTransmittalIds = new Set<string>();

async function onRowAction(
  t: Transmittal,
  projectId: string | null,
  refresh: () => void,
): Promise<void> {
  if (t.status !== 'DRAFT' || !projectId) {
    if (t.status && t.status !== 'DRAFT') {
      Alert.alert('Already sent', `This transmittal is in status ${t.status}.`);
    }
    return;
  }
  if (_sendingTransmittalIds.has(t.id)) return;
  Alert.alert(
    'Transmittal actions',
    `${t.transmittalNumber} — ${t.subject ?? 'Draft transmittal'}`,
    [
      { text: 'Send now', onPress: async () => {
        if (_sendingTransmittalIds.has(t.id)) return;
        _sendingTransmittalIds.add(t.id);
        try {
          await sendTransmittal(projectId, t.id);
          refresh();
        } catch (err) {
          Alert.alert('Send failed', err instanceof Error ? err.message : String(err));
        } finally {
          _sendingTransmittalIds.delete(t.id);
        }
      } },
      { text: 'Cancel', style: 'cancel' },
    ],
  );
}

function CreateTransmittalModal({
  visible, projectId, onClose, onCreated,
}: {
  visible: boolean;
  projectId: string | null;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [subject, setSubject] = useState('');
  const [issuedTo, setIssuedTo] = useState('');
  const [saving, setSaving] = useState(false);

  async function save() {
    if (!projectId || !subject.trim()) return;
    setSaving(true);
    try {
      await createTransmittal(projectId, {
        subject: subject.trim(),
        issuedTo: issuedTo.trim(),
        status: 'DRAFT',
      });
      setSubject('');
      setIssuedTo('');
      onCreated();
    } catch (err) {
      Alert.alert('Create failed', err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView
        style={styles.modalOverlay}
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      >
        <View style={styles.modalCard}>
          <Text style={styles.modalTitle}>New Transmittal</Text>
          <Text style={styles.note}>
            Creates a DRAFT transmittal. Attach documents from the web dashboard, then tap the row here to send.
          </Text>

          <Text style={styles.fieldLabel}>Subject *</Text>
          <TextInput
            style={styles.input}
            placeholder="e.g. DD2 Architecture Data Drop"
            value={subject}
            onChangeText={setSubject}
          />

          <Text style={styles.fieldLabel}>Issued To</Text>
          <TextInput
            style={styles.input}
            placeholder="e.g. Client PM, Design Team"
            value={issuedTo}
            onChangeText={setIssuedTo}
          />

          <View style={styles.modalActions}>
            <TouchableOpacity style={styles.cancelBtn} onPress={onClose}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.saveBtn, (!subject.trim() || saving) && { opacity: 0.5 }]}
              onPress={save}
              disabled={!subject.trim() || saving}
            >
              {saving
                ? <ActivityIndicator color="#fff" size="small" />
                : <Text style={styles.saveBtnText}>Save Draft</Text>}
            </TouchableOpacity>
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

function statusColor(status?: string) {
  switch (status) {
    case "SENT": case "DELIVERED": return { backgroundColor: "#2e7d32" };
    case "DRAFT":                   return { backgroundColor: "#999" };
    case "ACKNOWLEDGED":            return { backgroundColor: "#1976d2" };
    case "SUPERSEDED":              return { backgroundColor: "#ef6c00" };
    default:                        return { backgroundColor: "#666" };
  }
}

const styles = StyleSheet.create({
  row: { padding: 14, backgroundColor: "#fff" },
  left: { flexDirection: "row", alignItems: "center", marginBottom: 4 },
  code: { fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace', fontSize: 13, fontWeight: "600", color: "#222", marginRight: 8 },
  statusChip: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 10 },
  statusText: { color: "#fff", fontSize: 10, fontWeight: "700", letterSpacing: 0.5 },
  title: { fontSize: 15, fontWeight: "600", color: "#333" },
  meta: { fontSize: 12, color: "#666", marginTop: 2 },
  fab: {
    position: 'absolute',
    right: 16,
    bottom: 24,
    width: 56, height: 56, borderRadius: 28,
    backgroundColor: '#E8912D',
    alignItems: 'center', justifyContent: 'center',
    shadowColor: '#000', shadowOffset: { width: 0, height: 4 }, shadowOpacity: 0.2, shadowRadius: 8,
    elevation: 6,
  },
  fabText: { color: '#fff', fontSize: 28, fontWeight: '400', marginTop: -2 },
  modalOverlay: { flex: 1, backgroundColor: 'rgba(0,0,0,0.5)', justifyContent: 'flex-end' },
  modalCard: {
    backgroundColor: '#fff', borderTopLeftRadius: 16, borderTopRightRadius: 16,
    padding: 20, maxHeight: '80%',
  },
  modalTitle: { fontSize: 18, fontWeight: '700', marginBottom: 4, color: '#222' },
  note: { fontSize: 12, color: '#666', marginBottom: 16, lineHeight: 17 },
  fieldLabel: { fontSize: 12, fontWeight: '600', color: '#666', marginTop: 12, marginBottom: 4 },
  input: {
    backgroundColor: '#f5f5f5', borderRadius: 6, borderWidth: 1, borderColor: '#e0e0e0',
    paddingHorizontal: 12, paddingVertical: 10, fontSize: 14, color: '#222',
  },
  modalActions: { flexDirection: 'row', gap: 12, marginTop: 20 },
  cancelBtn: {
    flex: 1, paddingVertical: 12, borderRadius: 8, borderWidth: 1, borderColor: '#e0e0e0',
    alignItems: 'center',
  },
  cancelBtnText: { color: '#666', fontWeight: '600' },
  saveBtn: { flex: 1, paddingVertical: 12, borderRadius: 8, backgroundColor: '#E8912D', alignItems: 'center' },
  saveBtnText: { color: '#fff', fontWeight: '700' },
});
