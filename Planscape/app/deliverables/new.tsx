// T3-17 — Create new Information Deliverable.
//
// Minimal form so site teams can record a new deliverable in seconds.
// Offline-aware: when the device is offline, the action is enqueued
// through the offline queue and the user is sent back to the list.

import { useState } from 'react';
import {
  View,
  Text,
  TextInput,
  ScrollView,
  TouchableOpacity,
  StyleSheet,
  Alert,
  ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { createDeliverable, type DeliverableUpsertArgs } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';
import { isOnline } from '@/utils/connectivity';
import { enqueue } from '@/utils/offlineQueue';

export default function NewDeliverableScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);

  const [code, setCode] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [type, setType] = useState('DR');
  const [ownerRole, setOwnerRole] = useState('');
  const [discipline, setDiscipline] = useState('');
  const [suitability, setSuitability] = useState('');
  // Default due date = +14 days, formatted YYYY-MM-DD for the user to edit.
  const [dueDate, setDueDate] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() + 14);
    return d.toISOString().split('T')[0];
  });
  const [saving, setSaving] = useState(false);

  async function save() {
    if (!projectId) return;
    if (!code.trim()) { Alert.alert('Code required'); return; }
    if (!title.trim()) { Alert.alert('Title required'); return; }
    setSaving(true);
    const body: DeliverableUpsertArgs = {
      code: code.trim(),
      title: title.trim(),
      description: description.trim() || undefined,
      type: type.trim() || 'DR',
      ownerRole: ownerRole.trim() || undefined,
      discipline: discipline.trim() || null,
      suitabilityTarget: suitability.trim() || null,
      dueDate: dueDate ? new Date(dueDate).toISOString() : undefined,
    };
    try {
      const online = await isOnline();
      if (online) {
        const created = await createDeliverable(projectId, body);
        router.replace(`/deliverables/${created.id}`);
      } else {
        await enqueue('CREATE_DELIVERABLE', { projectId, body });
        Alert.alert('Queued', 'You are offline — the deliverable will be created when network is back.');
        router.back();
      }
    } catch (err: unknown) {
      Alert.alert('Create failed', err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
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
      <Field label="Code *" value={code} onChange={setCode} autoCapitalize="characters" />
      <Field label="Title *" value={title} onChange={setTitle} />
      <Field label="Description" value={description} onChange={setDescription} multiline />
      <Field label="Type" value={type} onChange={setType} autoCapitalize="characters" />
      <Field label="Owner role" value={ownerRole} onChange={setOwnerRole} />
      <Field label="Discipline" value={discipline} onChange={setDiscipline} autoCapitalize="characters" />
      <Field label="Suitability target" value={suitability} onChange={setSuitability} autoCapitalize="characters" />
      <Field label="Due date (YYYY-MM-DD)" value={dueDate} onChange={setDueDate} />

      <View style={styles.buttons}>
        <TouchableOpacity
          style={[styles.button, styles.buttonGhost]}
          onPress={() => router.back()}
          disabled={saving}
        >
          <Text style={styles.buttonGhostText}>Cancel</Text>
        </TouchableOpacity>
        <TouchableOpacity
          style={[styles.button, styles.buttonPrimary]}
          onPress={save}
          disabled={saving}
        >
          {saving ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonPrimaryText}>Create</Text>}
        </TouchableOpacity>
      </View>
    </ScrollView>
  );
}

function Field({
  label, value, onChange, multiline, autoCapitalize,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  multiline?: boolean;
  autoCapitalize?: 'none' | 'characters' | 'words' | 'sentences';
}) {
  return (
    <View style={{ marginBottom: theme.spacing.sm }}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <TextInput
        style={[styles.input, multiline && { minHeight: 60, textAlignVertical: 'top' }]}
        value={value}
        onChangeText={onChange}
        multiline={multiline}
        autoCapitalize={autoCapitalize}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  fieldLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginBottom: 4 },
  input: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
  },
  buttons: { flexDirection: 'row', justifyContent: 'flex-end', marginTop: theme.spacing.md },
  button: {
    paddingHorizontal: 16, paddingVertical: 10, borderRadius: theme.borderRadius.md,
    alignItems: 'center', justifyContent: 'center', minWidth: 100,
  },
  buttonPrimary: { backgroundColor: theme.colors.accent, marginLeft: theme.spacing.sm },
  buttonPrimaryText: { color: '#fff', fontSize: theme.fontSize.sm, fontWeight: '600' },
  buttonGhost: { backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.border },
  buttonGhostText: { color: theme.colors.text, fontSize: theme.fontSize.sm, fontWeight: '600' },
});
