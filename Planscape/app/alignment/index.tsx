import React, { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  FlatList,
  TextInput,
  TouchableOpacity,
  ScrollView,
  StyleSheet,
  Alert,
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { useProjectStore } from '@/stores/projectStore';
import { theme } from '@/utils/theme';
import type { Project } from '@/types/api';
import {
  getFederationManifest,
  getModelTransform,
  upsertModelTransform,
  resetModelTransform,
  autoAlignModel,
  runCoherenceScan,
  getCoordinateSystem,
  upsertCoordinateSystem,
  type ModelTransform,
  type CoherenceScanResult,
  type ProjectCoordinateSystem,
  type AutoAlignResult,
} from '@/api/endpoints';

// ── Types ──────────────────────────────────────────────────────────────────

interface ModelRow {
  id: string;
  name: string;
  discipline: string;
  format: string;
  uploadedAt: string;
  transform: ModelTransform | null;
}

interface EditForm {
  translationX: string;
  translationY: string;
  translationZ: string;
  rotationDeg: string;
  scaleFactor: string;
  notes: string;
}

const IDENTITY_FORM: EditForm = {
  translationX: '0',
  translationY: '0',
  translationZ: '0',
  rotationDeg: '0',
  scaleFactor: '1',
  notes: '',
};

function isIdentityTransform(t: ModelTransform): boolean {
  return (
    t.translationX === 0 &&
    t.translationY === 0 &&
    t.translationZ === 0 &&
    t.rotationDeg === 0 &&
    t.scaleFactor === 1
  );
}

// ── Main screen ────────────────────────────────────────────────────────────

export default function AlignmentScreen() {
  const activeProject = useProjectStore((s) => s.active) as Project | null;

  const [models, setModels] = useState<ModelRow[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Coherence scan
  const [coherenceResult, setCoherenceResult] = useState<CoherenceScanResult | null>(null);
  const [isScanning, setIsScanning] = useState(false);

  // Coordinate system
  const [crs, setCrs] = useState<ProjectCoordinateSystem | null>(null);
  const [editingCrs, setEditingCrs] = useState(false);
  const [crsForm, setCrsForm] = useState({
    crsEpsgCode: '',
    crsName: '',
    originEasting: '',
    originNorthing: '',
    originElevation: '',
    trueNorthDeg: '0',
    lengthUnit: 'mm',
    notes: '',
  });
  const [savingCrs, setSavingCrs] = useState(false);

  // Per-model editing state
  const [editingModelId, setEditingModelId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<EditForm>(IDENTITY_FORM);
  const [savingModelId, setSavingModelId] = useState<string | null>(null);
  const [autoAligningModelId, setAutoAligningModelId] = useState<string | null>(null);

  // ── Load data ────────────────────────────────────────────────────────────

  const loadAll = useCallback(async () => {
    if (!activeProject) return;
    setIsLoading(true);
    setLoadError(null);
    try {
      const [manifestRes, crsRes] = await Promise.allSettled([
        getFederationManifest(activeProject.id),
        getCoordinateSystem(activeProject.id),
      ]);

      if (crsRes.status === 'fulfilled') {
        setCrs(crsRes.value);
      }

      if (manifestRes.status === 'rejected') {
        setLoadError('Failed to load models. Check your connection.');
        setModels([]);
        return;
      }

      const manifest = manifestRes.value;

      // Fetch transforms for each model in parallel; a 404 means "identity"
      const transformResults = await Promise.allSettled(
        manifest.models.map((m) => getModelTransform(activeProject.id, m.id)),
      );

      const rows: ModelRow[] = manifest.models.map((m, i) => ({
        id: m.id,
        name: m.name,
        discipline: m.discipline,
        format: m.format,
        uploadedAt: m.uploadedAt,
        transform: transformResults[i].status === 'fulfilled'
          ? transformResults[i].value
          : null,
      }));

      setModels(rows);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to load alignment data';
      setLoadError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [activeProject?.id]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  // ── Coherence scan ───────────────────────────────────────────────────────

  const handleCoherenceScan = useCallback(async () => {
    if (!activeProject) return;
    setIsScanning(true);
    try {
      const result = await runCoherenceScan(activeProject.id);
      setCoherenceResult(result);
    } catch (err) {
      Alert.alert(
        'Scan Failed',
        err instanceof Error ? err.message : 'Coherence scan failed',
      );
    } finally {
      setIsScanning(false);
    }
  }, [activeProject?.id]);

  // ── Auto-align ───────────────────────────────────────────────────────────

  const handleAutoAlign = useCallback(async (modelId: string, modelName: string) => {
    if (!activeProject) return;
    setAutoAligningModelId(modelId);
    try {
      const result: AutoAlignResult = await autoAlignModel(activeProject.id, modelId);
      const msg = result.message
        ?? (result.status === 'ok'
          ? `Auto-alignment applied (confidence ${Math.round((result.confidenceScore ?? 0) * 100)}%).`
          : `Status: ${result.status}`);
      Alert.alert('Auto-Align', msg);
      // Refresh transforms to show the new values
      const updated = await getModelTransform(activeProject.id, modelId).catch(() => null);
      setModels((prev) =>
        prev.map((m) => (m.id === modelId ? { ...m, transform: updated } : m)),
      );
    } catch (err) {
      Alert.alert(
        'Auto-Align Failed',
        err instanceof Error ? err.message : `Could not auto-align ${modelName}`,
      );
    } finally {
      setAutoAligningModelId(null);
    }
  }, [activeProject?.id]);

  // ── Save transform ───────────────────────────────────────────────────────

  const handleSaveTransform = useCallback(async (modelId: string) => {
    if (!activeProject) return;
    const tx = parseFloat(editForm.translationX);
    const ty = parseFloat(editForm.translationY);
    const tz = parseFloat(editForm.translationZ);
    const rot = parseFloat(editForm.rotationDeg);
    const scale = parseFloat(editForm.scaleFactor);

    if ([tx, ty, tz, rot, scale].some((v) => isNaN(v))) {
      Alert.alert('Validation Error', 'All numeric fields must be valid numbers.');
      return;
    }
    if (scale <= 0) {
      Alert.alert('Validation Error', 'Scale factor must be greater than zero.');
      return;
    }

    setSavingModelId(modelId);
    try {
      const saved = await upsertModelTransform(activeProject.id, modelId, {
        translationX: tx,
        translationY: ty,
        translationZ: tz,
        rotationDeg: rot,
        scaleFactor: scale,
        isConfirmed: true,
        notes: editForm.notes.trim() || undefined,
      });
      setModels((prev) =>
        prev.map((m) => (m.id === modelId ? { ...m, transform: saved } : m)),
      );
      setEditingModelId(null);
    } catch (err) {
      Alert.alert(
        'Save Failed',
        err instanceof Error ? err.message : 'Could not save transform',
      );
    } finally {
      setSavingModelId(null);
    }
  }, [activeProject?.id, editForm]);

  // ── Reset transform ──────────────────────────────────────────────────────

  const handleResetTransform = useCallback(async (modelId: string, modelName: string) => {
    if (!activeProject) return;
    Alert.alert(
      'Reset Transform',
      `Reset "${modelName}" to identity (no offset, no rotation, scale=1)?`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Reset',
          style: 'destructive',
          onPress: async () => {
            try {
              await resetModelTransform(activeProject.id, modelId);
              setModels((prev) =>
                prev.map((m) => (m.id === modelId ? { ...m, transform: null } : m)),
              );
              if (editingModelId === modelId) setEditingModelId(null);
            } catch (err) {
              Alert.alert(
                'Reset Failed',
                err instanceof Error ? err.message : 'Could not reset transform',
              );
            }
          },
        },
      ],
    );
  }, [activeProject?.id, editingModelId]);

  // ── CRS save ─────────────────────────────────────────────────────────────

  const handleSaveCrs = useCallback(async () => {
    if (!activeProject) return;
    const trueNorth = parseFloat(crsForm.trueNorthDeg);
    if (isNaN(trueNorth)) {
      Alert.alert('Validation Error', 'True North angle must be a valid number.');
      return;
    }
    setSavingCrs(true);
    try {
      const saved = await upsertCoordinateSystem(activeProject.id, {
        crsEpsgCode: crsForm.crsEpsgCode.trim() || undefined,
        crsName: crsForm.crsName.trim() || undefined,
        originEasting: crsForm.originEasting ? parseFloat(crsForm.originEasting) : undefined,
        originNorthing: crsForm.originNorthing ? parseFloat(crsForm.originNorthing) : undefined,
        originElevation: crsForm.originElevation ? parseFloat(crsForm.originElevation) : undefined,
        trueNorthDeg: trueNorth,
        lengthUnit: crsForm.lengthUnit.trim() || 'mm',
        notes: crsForm.notes.trim() || undefined,
      });
      setCrs(saved);
      setEditingCrs(false);
    } catch (err) {
      Alert.alert(
        'Save Failed',
        err instanceof Error ? err.message : 'Could not save coordinate system',
      );
    } finally {
      setSavingCrs(false);
    }
  }, [activeProject?.id, crsForm]);

  function openCrsEditor() {
    setCrsForm({
      crsEpsgCode: crs?.crsEpsgCode ?? '',
      crsName: crs?.crsName ?? '',
      originEasting: crs?.originEasting != null ? String(crs.originEasting) : '',
      originNorthing: crs?.originNorthing != null ? String(crs.originNorthing) : '',
      originElevation: crs?.originElevation != null ? String(crs.originElevation) : '',
      trueNorthDeg: crs?.trueNorthDeg != null ? String(crs.trueNorthDeg) : '0',
      lengthUnit: crs?.lengthUnit ?? 'mm',
      notes: crs?.notes ?? '',
    });
    setEditingCrs(true);
  }

  function openTransformEditor(row: ModelRow) {
    const t = row.transform;
    setEditForm({
      translationX: t ? String(t.translationX) : '0',
      translationY: t ? String(t.translationY) : '0',
      translationZ: t ? String(t.translationZ) : '0',
      rotationDeg: t ? String(t.rotationDeg) : '0',
      scaleFactor: t ? String(t.scaleFactor) : '1',
      notes: t?.notes ?? '',
    });
    setEditingModelId(row.id);
  }

  // ── Render helpers ────────────────────────────────────────────────────────

  function renderModelCard({ item }: { item: ModelRow }) {
    const isEditing = editingModelId === item.id;
    const isSaving = savingModelId === item.id;
    const isAutoAligning = autoAligningModelId === item.id;
    const t = item.transform;
    const hasTransform = t !== null && !isIdentityTransform(t);

    return (
      <View style={styles.modelCard}>
        {/* Header row */}
        <View style={styles.modelCardHeader}>
          <View style={{ flex: 1 }}>
            <Text style={styles.modelName} numberOfLines={2}>{item.name}</Text>
            <View style={styles.modelMeta}>
              <View style={styles.disciplineChip}>
                <Text style={styles.disciplineChipText}>{item.discipline}</Text>
              </View>
              <Text style={styles.modelFormat}>{item.format.toUpperCase()}</Text>
            </View>
          </View>
        </View>

        {/* Transform summary */}
        <View style={styles.transformRow}>
          {hasTransform ? (
            <Text style={styles.transformValues}>
              TX {t!.translationX.toFixed(1)} · TY {t!.translationY.toFixed(1)} · TZ {t!.translationZ.toFixed(1)}
              {'\n'}Rot {t!.rotationDeg.toFixed(2)}° · Scale {t!.scaleFactor.toFixed(4)}
              {t!.isConfirmed ? '' : ' · unconfirmed'}
            </Text>
          ) : (
            <Text style={styles.identityLabel}>Identity (no offset)</Text>
          )}
        </View>

        {/* Edit form (inline, shown when editing) */}
        {isEditing && (
          <View style={styles.editForm}>
            <Text style={styles.editFormTitle}>Edit Transform</Text>
            <View style={styles.inputRow}>
              <NumericField label="TX (mm)" value={editForm.translationX} onChange={(v) => setEditForm((f) => ({ ...f, translationX: v }))} />
              <NumericField label="TY (mm)" value={editForm.translationY} onChange={(v) => setEditForm((f) => ({ ...f, translationY: v }))} />
              <NumericField label="TZ (mm)" value={editForm.translationZ} onChange={(v) => setEditForm((f) => ({ ...f, translationZ: v }))} />
            </View>
            <View style={styles.inputRow}>
              <NumericField label="Rotation (°)" value={editForm.rotationDeg} onChange={(v) => setEditForm((f) => ({ ...f, rotationDeg: v }))} />
              <NumericField label="Scale" value={editForm.scaleFactor} onChange={(v) => setEditForm((f) => ({ ...f, scaleFactor: v }))} />
            </View>
            <TextInput
              style={[styles.input, styles.notesInput]}
              value={editForm.notes}
              onChangeText={(v) => setEditForm((f) => ({ ...f, notes: v }))}
              placeholder="Notes (optional)"
              placeholderTextColor={theme.colors.textSecondary}
              multiline
              numberOfLines={2}
            />
            <View style={styles.editFormActions}>
              <TouchableOpacity
                style={[styles.btnSm, styles.btnSecondary]}
                onPress={() => setEditingModelId(null)}
                disabled={isSaving}
              >
                <Text style={styles.btnSmText}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={[styles.btnSm, styles.btnPrimary, isSaving && styles.btnDisabled]}
                onPress={() => handleSaveTransform(item.id)}
                disabled={isSaving}
              >
                {isSaving
                  ? <ActivityIndicator size="small" color={theme.colors.surface} />
                  : <Text style={[styles.btnSmText, { color: theme.colors.surface }]}>Save</Text>
                }
              </TouchableOpacity>
            </View>
          </View>
        )}

        {/* Action buttons (shown when not editing) */}
        {!isEditing && (
          <View style={styles.modelActions}>
            <TouchableOpacity
              style={[styles.btnSm, styles.btnOutline, isAutoAligning && styles.btnDisabled]}
              onPress={() => handleAutoAlign(item.id, item.name)}
              disabled={isAutoAligning || !!savingModelId}
              accessibilityLabel={`Auto-align ${item.name}`}
            >
              {isAutoAligning
                ? <ActivityIndicator size="small" color={theme.colors.accent} />
                : <Text style={[styles.btnSmText, { color: theme.colors.accent }]}>Auto-Align</Text>
              }
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.btnSm, styles.btnOutline]}
              onPress={() => openTransformEditor(item)}
              disabled={isAutoAligning}
              accessibilityLabel={`Edit transform for ${item.name}`}
            >
              <Text style={[styles.btnSmText, { color: theme.colors.text }]}>Edit</Text>
            </TouchableOpacity>
            {hasTransform && (
              <TouchableOpacity
                style={[styles.btnSm, styles.btnDanger]}
                onPress={() => handleResetTransform(item.id, item.name)}
                disabled={isAutoAligning}
                accessibilityLabel={`Reset transform for ${item.name}`}
              >
                <Text style={[styles.btnSmText, { color: theme.colors.danger }]}>Reset</Text>
              </TouchableOpacity>
            )}
          </View>
        )}
      </View>
    );
  }

  // ── Guards ────────────────────────────────────────────────────────────────

  if (!activeProject) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyText}>No active project.</Text>
        <Text style={styles.emptySubtext}>Select a project from the dashboard first.</Text>
      </View>
    );
  }

  if (isLoading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading alignment data…</Text>
      </View>
    );
  }

  if (loadError) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorText}>{loadError}</Text>
        <TouchableOpacity style={styles.retryButton} onPress={loadAll}>
          <Text style={styles.retryButtonText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <FlatList
        style={styles.root}
        contentContainerStyle={styles.content}
        data={models}
        keyExtractor={(item) => item.id}
        renderItem={renderModelCard}
        ListHeaderComponent={
          <>
            {/* ── Coordinate System card ── */}
            <View style={styles.sectionCard}>
              <View style={styles.sectionHeader}>
                <Text style={styles.sectionTitle}>Project Coordinate System</Text>
                <TouchableOpacity
                  onPress={editingCrs ? undefined : openCrsEditor}
                  accessibilityLabel="Edit CRS"
                  disabled={editingCrs}
                >
                  {!editingCrs && <Text style={styles.editLink}>Edit CRS</Text>}
                </TouchableOpacity>
              </View>

              {!editingCrs && (
                crs ? (
                  <View>
                    <CrsRow label="CRS" value={crs.crsName || crs.crsEpsgCode || '—'} />
                    <CrsRow label="EPSG" value={crs.crsEpsgCode || '—'} />
                    <CrsRow label="Origin E / N" value={
                      crs.originEasting != null && crs.originNorthing != null
                        ? `${crs.originEasting.toFixed(3)} / ${crs.originNorthing.toFixed(3)}`
                        : '—'
                    } />
                    <CrsRow label="Elevation" value={crs.originElevation != null ? `${crs.originElevation.toFixed(3)} m` : '—'} />
                    <CrsRow label="True North" value={`${crs.trueNorthDeg.toFixed(4)}°`} />
                    <CrsRow label="Length Unit" value={crs.lengthUnit} />
                    {crs.notes ? <CrsRow label="Notes" value={crs.notes} /> : null}
                  </View>
                ) : (
                  <Text style={styles.identityLabel}>No coordinate system configured.</Text>
                )
              )}

              {editingCrs && (
                <ScrollView style={{ maxHeight: 420 }} nestedScrollEnabled>
                  <CrsInputField label="EPSG Code" value={crsForm.crsEpsgCode} onChange={(v) => setCrsForm((f) => ({ ...f, crsEpsgCode: v }))} placeholder="e.g. 27700" />
                  <CrsInputField label="CRS Name" value={crsForm.crsName} onChange={(v) => setCrsForm((f) => ({ ...f, crsName: v }))} placeholder="e.g. OSGB 1936 / British National Grid" />
                  <CrsInputField label="Origin Easting (m)" value={crsForm.originEasting} onChange={(v) => setCrsForm((f) => ({ ...f, originEasting: v }))} placeholder="0" numeric />
                  <CrsInputField label="Origin Northing (m)" value={crsForm.originNorthing} onChange={(v) => setCrsForm((f) => ({ ...f, originNorthing: v }))} placeholder="0" numeric />
                  <CrsInputField label="Origin Elevation (m)" value={crsForm.originElevation} onChange={(v) => setCrsForm((f) => ({ ...f, originElevation: v }))} placeholder="0" numeric />
                  <CrsInputField label="True North (°)" value={crsForm.trueNorthDeg} onChange={(v) => setCrsForm((f) => ({ ...f, trueNorthDeg: v }))} placeholder="0" numeric />
                  <CrsInputField label="Length Unit" value={crsForm.lengthUnit} onChange={(v) => setCrsForm((f) => ({ ...f, lengthUnit: v }))} placeholder="mm" />
                  <CrsInputField label="Notes" value={crsForm.notes} onChange={(v) => setCrsForm((f) => ({ ...f, notes: v }))} placeholder="Optional notes" />
                  <View style={[styles.editFormActions, { marginTop: theme.spacing.md }]}>
                    <TouchableOpacity
                      style={[styles.btnSm, styles.btnSecondary]}
                      onPress={() => setEditingCrs(false)}
                      disabled={savingCrs}
                    >
                      <Text style={styles.btnSmText}>Cancel</Text>
                    </TouchableOpacity>
                    <TouchableOpacity
                      style={[styles.btnSm, styles.btnPrimary, savingCrs && styles.btnDisabled]}
                      onPress={handleSaveCrs}
                      disabled={savingCrs}
                    >
                      {savingCrs
                        ? <ActivityIndicator size="small" color={theme.colors.surface} />
                        : <Text style={[styles.btnSmText, { color: theme.colors.surface }]}>Save CRS</Text>
                      }
                    </TouchableOpacity>
                  </View>
                </ScrollView>
              )}
            </View>

            {/* ── Coherence scan ── */}
            <View style={styles.sectionCard}>
              <View style={styles.sectionHeader}>
                <Text style={styles.sectionTitle}>Federated Coherence</Text>
                <TouchableOpacity
                  style={[styles.btnSm, styles.btnOutline, isScanning && styles.btnDisabled]}
                  onPress={handleCoherenceScan}
                  disabled={isScanning}
                  accessibilityLabel="Run coherence scan"
                >
                  {isScanning
                    ? <ActivityIndicator size="small" color={theme.colors.accent} />
                    : <Text style={[styles.btnSmText, { color: theme.colors.accent }]}>Run Scan</Text>
                  }
                </TouchableOpacity>
              </View>

              {coherenceResult && (
                <View>
                  <View style={styles.coherenceVerdictRow}>
                    <View style={[styles.verdictBadge, verdictStyle(coherenceResult.verdict)]}>
                      <Text style={styles.verdictText}>{coherenceResult.verdict}</Text>
                    </View>
                    <Text style={styles.coherenceMeta}>
                      {coherenceResult.modelCount} model{coherenceResult.modelCount === 1 ? '' : 's'} · {coherenceResult.issuesFound} issue{coherenceResult.issuesFound === 1 ? '' : 's'}
                    </Text>
                  </View>
                  {coherenceResult.issues.map((issue, idx) => (
                    <View key={idx} style={styles.coherenceIssueRow}>
                      <View style={[styles.issueSeverityDot, severityDotStyle(issue.severity)]} />
                      <View style={{ flex: 1 }}>
                        <Text style={styles.coherenceIssueMsg}>{issue.message}</Text>
                        {issue.fixHint ? (
                          <Text style={styles.coherenceIssueHint}>{issue.fixHint}</Text>
                        ) : null}
                      </View>
                    </View>
                  ))}
                </View>
              )}

              {!coherenceResult && !isScanning && (
                <Text style={styles.identityLabel}>
                  Tap "Run Scan" to check cross-model unit, coordinate, and overlap coherence.
                </Text>
              )}
            </View>

            {/* ── Model list header ── */}
            <Text style={styles.listHeader}>
              {models.length} federated model{models.length === 1 ? '' : 's'}
            </Text>
          </>
        }
        ListEmptyComponent={
          <View style={styles.emptyList}>
            <Text style={styles.emptyText}>No models uploaded yet.</Text>
            <Text style={styles.emptySubtext}>Upload models via the Models screen to manage their alignment.</Text>
          </View>
        }
      />
    </KeyboardAvoidingView>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────

function NumericField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <View style={styles.numericField}>
      <Text style={styles.numericLabel}>{label}</Text>
      <TextInput
        style={styles.numericInput}
        value={value}
        onChangeText={onChange}
        keyboardType="numeric"
        placeholderTextColor={theme.colors.textSecondary}
      />
    </View>
  );
}

function CrsRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.crsRow}>
      <Text style={styles.crsLabel}>{label}</Text>
      <Text style={styles.crsValue}>{value}</Text>
    </View>
  );
}

function CrsInputField({
  label, value, onChange, placeholder, numeric,
}: {
  label: string; value: string; onChange: (v: string) => void;
  placeholder?: string; numeric?: boolean;
}) {
  return (
    <View style={styles.crsInputRow}>
      <Text style={styles.crsInputLabel}>{label}</Text>
      <TextInput
        style={styles.crsInput}
        value={value}
        onChangeText={onChange}
        placeholder={placeholder}
        placeholderTextColor={theme.colors.textSecondary}
        keyboardType={numeric ? 'numeric' : 'default'}
      />
    </View>
  );
}

function verdictStyle(verdict: 'PASS' | 'WARN' | 'FAIL') {
  switch (verdict) {
    case 'PASS': return { backgroundColor: theme.colors.success };
    case 'WARN': return { backgroundColor: theme.colors.warning };
    case 'FAIL': return { backgroundColor: theme.colors.danger };
  }
}

function severityDotStyle(severity: 'INFO' | 'WARN' | 'FAIL') {
  switch (severity) {
    case 'INFO': return { backgroundColor: theme.colors.accent };
    case 'WARN': return { backgroundColor: theme.colors.warning };
    case 'FAIL': return { backgroundColor: theme.colors.danger };
  }
}

// ── Styles ────────────────────────────────────────────────────────────────

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  content: {
    padding: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
  },
  center: {
    flex: 1,
    backgroundColor: theme.colors.background,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  loadingText: {
    marginTop: theme.spacing.md,
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
  },
  errorText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.danger,
    textAlign: 'center',
    marginBottom: theme.spacing.md,
  },
  retryButton: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.sm,
  },
  retryButtonText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },
  emptyText: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
    textAlign: 'center',
  },
  emptySubtext: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },

  // Section card
  sectionCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  sectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: theme.spacing.sm,
  },
  sectionTitle: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.text,
  },
  editLink: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.accent,
    fontWeight: '600',
  },

  // CRS display
  crsRow: {
    flexDirection: 'row',
    paddingVertical: 4,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  crsLabel: {
    width: 110,
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
  },
  crsValue: {
    flex: 1,
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    fontWeight: '500',
  },

  // CRS edit form
  crsInputRow: {
    marginBottom: theme.spacing.sm,
  },
  crsInputLabel: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginBottom: 4,
  },
  crsInput: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    padding: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },

  // Coherence
  coherenceVerdictRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: theme.spacing.sm,
    gap: theme.spacing.sm,
  },
  verdictBadge: {
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 3,
  },
  verdictText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: '#fff',
  },
  coherenceMeta: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
  },
  coherenceIssueRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    paddingVertical: theme.spacing.xs,
    gap: theme.spacing.sm,
  },
  issueSeverityDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginTop: 5,
    flexShrink: 0,
  },
  coherenceIssueMsg: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
  },
  coherenceIssueHint: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },

  // List header
  listHeader: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: theme.spacing.sm,
  },
  emptyList: {
    alignItems: 'center',
    paddingVertical: theme.spacing.xl,
  },

  // Model card
  modelCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  modelCardHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    marginBottom: theme.spacing.sm,
  },
  modelName: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
  },
  modelMeta: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
    marginTop: 4,
  },
  disciplineChip: {
    backgroundColor: theme.colors.accent + '22',
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: 6,
    paddingVertical: 2,
  },
  disciplineChipText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.accent,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  modelFormat: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },

  // Transform summary
  transformRow: {
    marginBottom: theme.spacing.sm,
  },
  transformValues: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    lineHeight: 18,
  },
  identityLabel: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    fontStyle: 'italic',
  },

  // Edit form
  editForm: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  editFormTitle: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  inputRow: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  numericField: {
    flex: 1,
  },
  numericLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginBottom: 2,
  },
  numericInput: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    padding: theme.spacing.sm,
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    textAlign: 'right',
  },
  input: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    padding: theme.spacing.sm,
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
  },
  notesInput: {
    marginBottom: theme.spacing.sm,
    minHeight: 52,
    textAlignVertical: 'top',
  },
  editFormActions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: theme.spacing.sm,
  },

  // Model action buttons
  modelActions: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    flexWrap: 'wrap',
  },

  // Shared button styles
  btnSm: {
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: 7,
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: 72,
    minHeight: 32,
  },
  btnPrimary: {
    backgroundColor: theme.colors.accent,
  },
  btnSecondary: {
    backgroundColor: theme.colors.border,
  },
  btnOutline: {
    borderWidth: 1,
    borderColor: theme.colors.accent,
  },
  btnDanger: {
    borderWidth: 1,
    borderColor: theme.colors.danger,
  },
  btnDisabled: {
    opacity: 0.5,
  },
  btnSmText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
  },
});
