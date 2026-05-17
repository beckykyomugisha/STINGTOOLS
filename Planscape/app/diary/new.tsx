// Phase 142 — New Site Diary entry form.
//
// Posts a DRAFT diary; user can save and continue editing or submit
// directly. Mirrors the standard CIOB site report fields. Photos are
// linked from already-uploaded documents in a follow-up screen — here we
// just capture the narrative + numeric fields so the entry is created
// fast on a flaky site connection.

import { useState } from 'react';
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  ScrollView,
  StyleSheet,
  Alert,
  ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { createSiteDiary, submitSiteDiary } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function NewDiaryScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);

  const [authorRole, setAuthorRole] = useState('');
  const [weather, setWeather] = useState('');
  const [tempC, setTempC] = useState('');
  const [manpower, setManpower] = useState('');
  const [narrative, setNarrative] = useState('');
  const [safety, setSafety] = useState('');
  const [delays, setDelays] = useState('');
  const [saving, setSaving] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function persistDraft(): Promise<string | null> {
    if (!projectId) return null;
    const manpowerNum = parseInt(manpower, 10);
    const tempNum = tempC ? parseFloat(tempC) : null;
    if (Number.isNaN(manpowerNum) || manpowerNum < 0) {
      Alert.alert('Invalid manpower count', 'Enter a non-negative integer.');
      return null;
    }
    try {
      const res = await createSiteDiary(projectId, {
        diaryDate: new Date().toISOString(),
        authorRole: authorRole || null,
        weather: weather || null,
        temperatureCelsius: tempNum,
        windSpeedKph: null,
        rainfallMm: null,
        manpowerCount: manpowerNum,
        manpowerByTradeJson: null,
        equipmentJson: null,
        deliveriesJson: null,
        narrative: narrative || null,
        checklistJson: null,
        visitorsLog: null,
        safetyIncidents: safety || null,
        delaysAndDisruption: delays || null,
        latitude: null,
        longitude: null,
      });
      return res.id;
    } catch (err: unknown) {
      Alert.alert('Save failed', err instanceof Error ? err.message : String(err));
      return null;
    }
  }

  async function handleSaveDraft() {
    setSaving(true);
    const id = await persistDraft();
    setSaving(false);
    if (id) router.replace(`/diary/${id}` as any);
  }

  async function handleSubmit() {
    setSubmitting(true);
    const id = await persistDraft();
    if (!id || !projectId) { setSubmitting(false); return; }
    try {
      await submitSiteDiary(projectId, id);
    } catch (err: unknown) {
      Alert.alert('Submission failed', err instanceof Error ? err.message : String(err));
      setSubmitting(false);
      return;
    }
    setSubmitting(false);
    router.replace(`/diary/${id}` as any);
  }

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project on the dashboard first.</Text>
      </View>
    );
  }

  return (
    <ScrollView style={styles.root} contentContainerStyle={styles.scroll}>
      <Field label="Your role" value={authorRole} onChange={setAuthorRole} placeholder="e.g. Site Manager, MC, TC" />
      <Field label="Weather" value={weather} onChange={setWeather} placeholder="e.g. Overcast, light rain after lunch" />
      <Field label="Temperature (°C)" value={tempC} onChange={setTempC} keyboard="numeric" placeholder="e.g. 12" />
      <Field label="Manpower on site" value={manpower} onChange={setManpower} keyboard="number-pad" placeholder="e.g. 24" />

      <Field
        label="Narrative"
        value={narrative}
        onChange={setNarrative}
        placeholder="What happened on site today — progress, decisions, observations…"
        multiline
        height={140}
      />
      <Field
        label="Safety incidents / near-misses"
        value={safety}
        onChange={setSafety}
        placeholder="None / describe…"
        multiline
        height={80}
      />
      <Field
        label="Delays & disruption"
        value={delays}
        onChange={setDelays}
        placeholder="Late deliveries, weather stops, design clarifications…"
        multiline
        height={80}
      />

      <View style={styles.buttonRow}>
        <TouchableOpacity
          style={[styles.button, styles.buttonGhost, (saving || submitting) && styles.buttonDisabled]}
          onPress={handleSaveDraft}
          disabled={saving || submitting}
          accessibilityLabel="Save draft"
        >
          {saving ? <ActivityIndicator color={theme.colors.accent} /> : <Text style={styles.buttonGhostText}>Save Draft</Text>}
        </TouchableOpacity>
        <TouchableOpacity
          style={[styles.button, styles.buttonPrimary, (saving || submitting) && styles.buttonDisabled]}
          onPress={handleSubmit}
          disabled={saving || submitting}
          accessibilityLabel="Submit diary"
        >
          {submitting ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonPrimaryText}>Submit</Text>}
        </TouchableOpacity>
      </View>
    </ScrollView>
  );
}

function Field({
  label, value, onChange, placeholder, keyboard, multiline, height,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  keyboard?: 'default' | 'numeric' | 'number-pad' | 'email-address';
  multiline?: boolean;
  height?: number;
}) {
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        style={[styles.input, multiline && { height: height ?? 80, textAlignVertical: 'top' }]}
        value={value}
        onChangeText={onChange}
        placeholder={placeholder}
        placeholderTextColor={theme.colors.disabled}
        keyboardType={keyboard ?? 'default'}
        multiline={!!multiline}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 80 },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  field: { marginBottom: theme.spacing.md },
  label: {
    fontSize: theme.fontSize.sm, fontWeight: '600',
    color: theme.colors.textSecondary, marginBottom: 4,
  },
  input: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm + 4,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  buttonRow: {
    flexDirection: 'row',
    gap: theme.spacing.md,
    marginTop: theme.spacing.lg,
  },
  button: {
    flex: 1,
    paddingVertical: theme.spacing.md,
    borderRadius: theme.borderRadius.md,
    alignItems: 'center',
  },
  buttonPrimary: { backgroundColor: theme.colors.accent },
  buttonPrimaryText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.md },
  buttonGhost: {
    borderWidth: 1.5,
    borderColor: theme.colors.accent,
    backgroundColor: 'transparent',
  },
  buttonGhostText: { color: theme.colors.accent, fontWeight: '600', fontSize: theme.fontSize.md },
  buttonDisabled: { opacity: 0.5 },
});
