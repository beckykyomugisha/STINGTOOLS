import { useState } from "react";
import {
  View, Text, StyleSheet, TouchableOpacity, Modal, Alert, ScrollView,
  ActivityIndicator, Platform, KeyboardAvoidingView,
} from "react-native";
import { listWorkflowRuns, createIssue } from "@/api/endpoints";
import { CoordinationListScreen } from "@/components/CoordinationListScreen";
import { useProjectStore } from "@/stores/projectStore";
import type { WorkflowRun } from "@/types/api";

/**
 * Phase 96 — Workflow runs screen now exposes a "Request Workflow Run" FAB.
 *
 * The Revit plugin runs workflow presets (MorningHealthCheck, DailyQA, etc.)
 * locally. Mobile can't execute them remotely because the Revit API must run
 * on the desktop host, but a BIM coordinator on site often needs a specific
 * preset to be run (e.g. after they've walked the site and found stale
 * elements, they want MorningHealthCheck to re-populate tokens).
 *
 * We implement "remote trigger" as a structured issue of type `WORKFLOW_REQ`
 * assigned to the BIM coordinator with the preset name in the title. This
 * surfaces in the plugin's BIM Coordination Center → Issues tab where the
 * coordinator clicks "Run Preset" to execute. No new server endpoints needed
 * since we reuse the Issues infrastructure + BCC UI already ships with
 * "Run Workflow" handler dispatch.
 */

const MOBILE_PRESETS = [
  { name: 'MorningHealthCheck', desc: 'Re-populate tokens on any stale elements' },
  { name: 'DailyQA', desc: 'Tag new elements, validate, re-run compliance' },
  { name: 'WeeklyDataDrop', desc: 'ISO 19650 information exchange prep' },
  { name: 'EndOfDaySync', desc: 'Save baseline, export registers, create revision' },
  { name: 'PreMeetingPrep', desc: 'Warnings auto-fix + compliance summary' },
  { name: 'COBieReadiness', desc: 'Resolve placeholders, write containers, validate' },
];

export default function WorkflowsScreen() {
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [requestVisible, setRequestVisible] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  return (
    <>
      <CoordinationListScreen<WorkflowRun>
        key={refreshKey}
        title="Workflows"
        emptyTitle="No workflow runs yet"
        emptyBody="Workflow executions from the Revit plugin show here — pass / fail counts, duration, compliance delta. Tap + to request a run."
        fetch={listWorkflowRuns}
        keyExtractor={(r) => r.id}
        renderRow={(r) => {
          const delta = (r.complianceAfter ?? 0) - (r.complianceBefore ?? 0);
          const deltaColor = delta > 0 ? "#2e7d32" : delta < 0 ? "#d32f2f" : "#666";
          return (
            <View style={styles.row}>
              <Text style={styles.preset}>{r.presetName ?? "workflow"}</Text>
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
                {r.userName ? ` · ${r.userName}` : ""}
              </Text>
            </View>
          );
        }}
      />

      <TouchableOpacity
        style={styles.fab}
        onPress={() => setRequestVisible(true)}
        accessibilityRole="button"
        accessibilityLabel="Request a workflow run from the BIM coordinator"
      >
        <Text style={styles.fabText}>+</Text>
      </TouchableOpacity>

      {requestVisible && projectId && (
        <RequestWorkflowModal
          projectId={projectId}
          onClose={() => setRequestVisible(false)}
          onRequested={() => {
            setRequestVisible(false);
            setRefreshKey((k) => k + 1);
          }}
        />
      )}
    </>
  );
}

function RequestWorkflowModal({ projectId, onClose, onRequested }: {
  projectId: string; onClose: () => void; onRequested: () => void;
}) {
  const [busy, setBusy] = useState(false);

  async function request(presetName: string, desc: string) {
    setBusy(true);
    try {
      await createIssue(projectId, {
        title: `Please run workflow: ${presetName}`,
        description: `Mobile request for workflow preset \`${presetName}\`.\n\n${desc}\n\nOpen the BIM Coordination Center → Workflows tab to execute.`,
        type: 'WORKFLOW_REQ',
        priority: 'MEDIUM',
        status: 'OPEN',
      });
      Alert.alert(
        'Request sent',
        'The BIM coordinator has been notified. The workflow will run next time they open the plugin.',
      );
      onRequested();
    } catch (err) {
      Alert.alert('Request failed', err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal visible animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.modalOverlay} behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>
        <View style={styles.modalCard}>
          <Text style={styles.modalTitle}>Request Workflow Run</Text>
          <Text style={styles.modalNote}>
            Tap a preset to ask the BIM coordinator to run it against the active model.
          </Text>
          <ScrollView style={{ maxHeight: 380 }}>
            {MOBILE_PRESETS.map((p) => (
              <TouchableOpacity
                key={p.name}
                style={styles.presetRow}
                onPress={() => request(p.name, p.desc)}
                disabled={busy}
              >
                <View style={{ flex: 1 }}>
                  <Text style={styles.presetName}>{p.name}</Text>
                  <Text style={styles.presetDesc}>{p.desc}</Text>
                </View>
                {busy ? <ActivityIndicator size="small" color="#E8912D" />
                      : <Text style={styles.presetArrow}>→</Text>}
              </TouchableOpacity>
            ))}
          </ScrollView>
          <TouchableOpacity style={styles.cancelBtn} onPress={onClose}>
            <Text style={styles.cancelBtnText}>Cancel</Text>
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>
    </Modal>
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
    padding: 20, maxHeight: '85%',
  },
  modalTitle: { fontSize: 18, fontWeight: '700', marginBottom: 4, color: '#222' },
  modalNote: { fontSize: 12, color: '#666', lineHeight: 17, marginBottom: 12 },
  presetRow: {
    flexDirection: 'row', alignItems: 'center',
    paddingVertical: 12, paddingHorizontal: 10,
    borderBottomWidth: 1, borderBottomColor: '#f0f0f0',
  },
  presetName: { fontSize: 14, fontWeight: '700', color: '#222' },
  presetDesc: { fontSize: 12, color: '#666', marginTop: 2, lineHeight: 17 },
  presetArrow: { fontSize: 20, color: '#E8912D', fontWeight: '700' },
  cancelBtn: {
    marginTop: 14, paddingVertical: 12, borderRadius: 8,
    borderWidth: 1, borderColor: '#e0e0e0', alignItems: 'center',
  },
  cancelBtnText: { color: '#666', fontWeight: '600' },
});
