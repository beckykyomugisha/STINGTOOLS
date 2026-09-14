// QR-9 — commissioning sign-off with a witness.
//
// WHY THIS SCREEN EXISTS
// ----------------------
// The scan flow uses Alert, which can ask a yes/no and nothing more. So the one
// step that most needs recording while you are standing at the asset —
// COMMISSIONED, the point it is declared fit for use — was refused on mobile and
// sent back to a desk. Honest, and useless.
//
// The witness requirement is the server's rule, enforced by
// CommissioningStateMachine, which the Revit plugin calls too. This screen does
// not re-implement it: it collects the two names and lets the server decide. If
// the rule changes, this screen keeps working.

import { useState } from 'react';
import {
  View,
  Text,
  TextInput,
  ScrollView,
  StyleSheet,
  TouchableOpacity,
  Alert,
  ActivityIndicator,
} from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { theme } from '@/utils/theme';
import { advanceCommissioning } from '@/api/endpoints';
import { useAuthStore } from '@/stores/authStore';
import { isOnline } from '@/utils/connectivity';
import { enqueue } from '@/utils/offlineQueue';

export default function CommissioningSignoffScreen() {
  const { projectId, uid, tag, name, from, to } = useLocalSearchParams<{
    projectId?: string;
    uid?: string;
    tag?: string;
    name?: string;
    from?: string;
    to?: string;
  }>();

  const auth = useAuthStore.getState();
  // Pre-filled, not hard-coded: the signed-in user is the likely operative, and
  // they can correct it — a supervisor may be recording a fitter's step.
  const [operative, setOperative] = useState(auth.displayName || auth.email || '');
  const [witness, setWitness] = useState('');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);

  const target = (to as string) || 'the next state';

  async function submit() {
    const op = operative.trim();
    const wit = witness.trim();

    // Checked here only so the operative is told BEFORE a round trip. The server
    // enforces it regardless — this is a courtesy, not the rule.
    if (!op) {
      Alert.alert('Who signed this off?', 'A commissioning step has to be attributable to a person.');
      return;
    }
    if (!wit) {
      Alert.alert(
        'Who witnessed it?',
        `${target} declares the asset fit for use, and that is the step a second person has to have seen.`,
      );
      return;
    }
    if (!projectId || !uid) {
      Alert.alert('Missing details', 'Re-scan the asset and try again.');
      return;
    }

    const body = {
      elementUniqueId: uid as string,
      requestedState: (to as string) || undefined,
      operative: op,
      witness: wit,
      notes: notes.trim() || undefined,
      elementTag: (tag as string) || undefined,
      elementName: (name as string) || undefined,
      source: 'mobile-scan',
      occurredAt: new Date().toISOString(),
      // The state we showed on the previous screen. If someone else advanced it
      // meanwhile, the server answers 409 rather than recording a duplicate step
      // under two names.
      expectedCurrentState: (from as string) || undefined,
    };

    setBusy(true);
    try {
      // QR-8 — plant rooms have no signal. Queue rather than lose a sign-off the
      // operative walked down three flights to give.
      if (!(await isOnline())) {
        await enqueue('COMMISSIONING_ADVANCE', { projectId, payload: body });
        Alert.alert(
          'Saved offline',
          `${tag || 'Asset'} → ${target}.\n\n` +
            'It will be sent when you have signal. Until then it is NOT on the server, ' +
            'so nobody else can see it.',
          [{ text: 'OK', onPress: () => router.back() }],
        );
        return;
      }

      const result = await advanceCommissioning(projectId as string, body);
      Alert.alert(
        'Recorded',
        `${tag || 'Asset'}\n\n${result.record.fromState} → ${result.currentState}\n` +
          `By ${op}, witnessed by ${wit}.`,
        [{ text: 'OK', onPress: () => router.back() }],
      );
    } catch (err) {
      const status = (err as { status?: number })?.status;
      const b = (err as { body?: { reason?: string; detail?: string } })?.body;
      if (status === 409) {
        Alert.alert('Someone got there first', b?.detail ?? 'Re-scan to see the current state.');
        return;
      }
      if (status === 422) {
        // Show the SERVER's sentence — it names the actual rule that refused, which
        // a generic "could not save" destroys.
        Alert.alert('Not recorded', b?.reason ?? 'That step is not allowed right now.');
        return;
      }
      Alert.alert('Not recorded', err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ScrollView style={s.screen} contentContainerStyle={s.content} keyboardShouldPersistTaps="handled">
      <Text style={s.title}>{tag || 'Commissioning sign-off'}</Text>
      {!!name && <Text style={s.sub}>{name}</Text>}
      <Text style={s.transition}>
        {(from as string) || 'NOT_STARTED'} → {target}
      </Text>

      <Text style={s.note}>
        This step declares the asset fit for use. It is recorded permanently and cannot be
        reversed — the ladder only goes forward.
      </Text>

      <Text style={s.label}>Signed off by</Text>
      <TextInput
        style={s.input}
        value={operative}
        onChangeText={setOperative}
        placeholder="Your name"
        autoCapitalize="words"
      />

      <Text style={s.label}>Witnessed by</Text>
      <TextInput
        style={s.input}
        value={witness}
        onChangeText={setWitness}
        placeholder="Name of the person who witnessed it"
        autoCapitalize="words"
      />

      <Text style={s.label}>Notes (optional)</Text>
      <TextInput
        style={[s.input, s.multiline]}
        value={notes}
        onChangeText={setNotes}
        placeholder="Anything worth recording about this sign-off"
        multiline
        numberOfLines={3}
      />

      <TouchableOpacity
        style={[s.primary, busy && s.disabled]}
        onPress={() => void submit()}
        disabled={busy}
      >
        {busy ? <ActivityIndicator color="#fff" /> : <Text style={s.primaryText}>Record {target}</Text>}
      </TouchableOpacity>

      <TouchableOpacity style={s.secondary} onPress={() => router.back()} disabled={busy}>
        <Text style={s.secondaryText}>Cancel</Text>
      </TouchableOpacity>
    </ScrollView>
  );
}

const s = StyleSheet.create({
  screen: { flex: 1, backgroundColor: theme.colors.background },
  content: { padding: 16, paddingBottom: 48 },
  title: { fontSize: 20, fontWeight: '700', color: theme.colors.text },
  sub: { fontSize: 14, color: theme.colors.textSecondary, marginTop: 2 },
  transition: { fontSize: 16, fontWeight: '600', color: theme.colors.primary, marginTop: 10 },
  note: {
    fontSize: 13,
    color: theme.colors.textSecondary,
    marginTop: 12,
    marginBottom: 8,
    lineHeight: 18,
  },
  label: { fontSize: 13, fontWeight: '600', color: theme.colors.text, marginTop: 16, marginBottom: 6 },
  input: {
    borderWidth: 1,
    borderColor: theme.colors.border,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontSize: 15,
    color: theme.colors.text,
    backgroundColor: theme.colors.surface,
  },
  multiline: { minHeight: 76, textAlignVertical: 'top' },
  primary: {
    marginTop: 24,
    backgroundColor: theme.colors.primary,
    borderRadius: 8,
    paddingVertical: 14,
    alignItems: 'center',
  },
  primaryText: { color: '#fff', fontSize: 16, fontWeight: '700' },
  disabled: { opacity: 0.6 },
  secondary: { marginTop: 12, paddingVertical: 12, alignItems: 'center' },
  secondaryText: { color: theme.colors.textSecondary, fontSize: 15 },
});
