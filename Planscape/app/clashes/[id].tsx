/**
 * Clash detail screen — view a single clash with actions:
 *   • View in model — zoom to clash centre in 3D viewer
 *   • Promote to issue — auto-create BimIssue assigned to the right discipline
 *   • Update status / assignee / resolution note
 */
import { useState, useEffect } from 'react';
import {
  View, Text, ScrollView, StyleSheet, TouchableOpacity, TextInput, ActivityIndicator, Alert,
} from 'react-native';
import { useLocalSearchParams, useRouter, Stack } from 'expo-router';
import { theme } from '@/utils/theme';
import { getClash, updateClash, promoteClashToIssue, type ClashRecord, type ClashStatus } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function ClashDetailScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const project = useProjectStore((s) => s.active);
  const [clash, setClash] = useState<ClashRecord | null>(null);
  const [loading, setLoading] = useState(true);
  const [resolutionNote, setResolutionNote] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!project || !id) return;
    getClash(project.id, id).then(c => { setClash(c); setResolutionNote(c.resolutionNote ?? ''); }).finally(() => setLoading(false));
  }, [project, id]);

  async function setStatus(status: ClashStatus) {
    if (!project || !clash) return;
    setBusy(true);
    try {
      const updated = await updateClash(project.id, clash.id, { status, resolutionNote });
      setClash(updated);
    } catch (err) {
      Alert.alert('Update failed', String(err));
    } finally {
      setBusy(false);
    }
  }

  async function promote() {
    if (!project || !clash) return;
    setBusy(true);
    try {
      const issue = await promoteClashToIssue(project.id, clash.id);
      Alert.alert('Promoted to issue', `Issue ${issue.title} created.`);
      const updated = await getClash(project.id, clash.id);
      setClash(updated);
    } catch (err) {
      Alert.alert('Promote failed', String(err));
    } finally { setBusy(false); }
  }

  function viewInModel() {
    if (!clash) return;
    router.push(`/models/${clash.modelAId}?highlightElement=${clash.elementAGuid}` as any);
  }

  if (loading) return <View style={s.center}><ActivityIndicator size="large" /></View>;
  if (!clash) return <View style={s.center}><Text>Clash not found</Text></View>;

  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <Stack.Screen options={{ headerShown: true, title: 'Clash', headerStyle: { backgroundColor: theme.colors.primary }, headerTintColor: '#fff' }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        <View style={s.card}>
          <Text style={s.title}>{clash.disciplineA || '?'} ↔ {clash.disciplineB || '?'}</Text>
          <Text style={s.subtitle}>{clash.severity} · {clash.status}{clash.levelCode ? ` · ${clash.levelCode}` : ''}</Text>
        </View>

        <View style={s.card}>
          <Text style={s.label}>Geometry</Text>
          <Text style={s.kv}>Penetration depth: {clash.distanceMm.toFixed(2)} mm</Text>
          <Text style={s.kv}>Overlap volume: {Math.round(clash.overlapVolumeMm3).toLocaleString()} mm³</Text>
          <Text style={s.kv}>Centre: ({clash.centreX.toFixed(0)}, {clash.centreY.toFixed(0)}, {clash.centreZ.toFixed(0)})</Text>
        </View>

        <View style={s.card}>
          <Text style={s.label}>Elements</Text>
          <Text style={s.kv}>A: {clash.elementAType ?? '?'} · {clash.elementAName ?? clash.elementAGuid.slice(0, 8)}</Text>
          <Text style={s.kv}>B: {clash.elementBType ?? '?'} · {clash.elementBName ?? clash.elementBGuid.slice(0, 8)}</Text>
        </View>

        <View style={s.card}>
          <Text style={s.label}>Resolution note</Text>
          <TextInput
            style={s.textArea}
            value={resolutionNote}
            onChangeText={setResolutionNote}
            multiline
            placeholder="Describe what was changed and why..."
          />
        </View>

        <View style={s.actions}>
          <TouchableOpacity style={[s.btn, s.btnPrimary]} onPress={viewInModel}>
            <Text style={s.btnText}>🧊 View in model</Text>
          </TouchableOpacity>
          {!clash.issueId && (
            <TouchableOpacity style={[s.btn, s.btnAccent]} onPress={promote} disabled={busy}>
              <Text style={s.btnText}>⚠ Promote to issue</Text>
            </TouchableOpacity>
          )}
          {clash.status === 'NEW' && (
            <TouchableOpacity style={[s.btn, s.btnNeutral]} onPress={() => setStatus('ACKNOWLEDGED')} disabled={busy}>
              <Text style={s.btnText}>✓ Acknowledge</Text>
            </TouchableOpacity>
          )}
          {clash.status === 'ACKNOWLEDGED' && (
            <TouchableOpacity style={[s.btn, s.btnAccent]} onPress={() => setStatus('RESOLVED')} disabled={busy}>
              <Text style={s.btnText}>✅ Mark resolved</Text>
            </TouchableOpacity>
          )}
          {clash.status === 'RESOLVED' && (
            <TouchableOpacity style={[s.btn, s.btnNeutral]} onPress={() => setStatus('CLOSED')} disabled={busy}>
              <Text style={s.btnText}>🔒 Close</Text>
            </TouchableOpacity>
          )}
        </View>
      </ScrollView>
    </View>
  );
}

const s = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  card: { backgroundColor: theme.colors.surface, padding: 14, borderRadius: 8, marginBottom: 12 },
  title: { fontSize: 20, fontWeight: '700', color: theme.colors.text },
  subtitle: { fontSize: 13, color: theme.colors.textSecondary, marginTop: 4 },
  label: { fontSize: 11, fontWeight: '700', color: theme.colors.textSecondary, textTransform: 'uppercase', marginBottom: 6 },
  kv: { fontSize: 13, color: theme.colors.text, marginVertical: 2 },
  textArea: { borderWidth: 1, borderColor: theme.colors.border, borderRadius: 6, padding: 10, minHeight: 80, color: theme.colors.text, textAlignVertical: 'top' },
  actions: { gap: 10, marginTop: 12 },
  btn: { padding: 12, borderRadius: 6, alignItems: 'center' },
  btnPrimary: { backgroundColor: theme.colors.primary },
  btnAccent: { backgroundColor: theme.colors.accent },
  btnNeutral: { backgroundColor: theme.colors.textSecondary },
  btnText: { color: '#fff', fontWeight: '700', fontSize: 14 },
});
